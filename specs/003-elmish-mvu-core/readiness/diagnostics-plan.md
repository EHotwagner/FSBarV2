# Cmd-failure & MailboxHighWater Diagnostics Plan

**Feature**: 003-elmish-mvu-core
**Driver**: FR-008 (typed per-effect-family failure arms),
spec Clarification Q3 (no generic `CmdFailed`),
Constitution Principle VI (observability and safe failure)

## Cmd-failure routing strategy

Every `Cmd` family has exactly one matching failure arm. The runtime
wraps each adapter call in a `try-with` at the boundary; on
exception, it converts the failure into a typed `Msg.CmdFailure ...`
and posts it back to the dispatcher. `update` matches the failures
exhaustively — the F# compiler will refuse to build if any arm is
missed.

| Cmd arm                              | Adapter call                          | Failure Msg arm                              |
|--------------------------------------|---------------------------------------|----------------------------------------------|
| `AuditCmd ev`                        | `AuditAdapter.write ev`               | `Msg.AuditWriteFailed (summary, exn)`        |
| `CoordinatorOutbound cmd`            | `CoordinatorAdapter.send cmd`         | `Msg.CoordinatorSendFailed (summary, exn)`   |
| `ScriptingOutbound (id, msg)` *      | `ScriptingAdapter.send id msg`        | `Msg.ScriptingSendFailed (id, summary, exn)` |
| `ScriptingReject (id, reason)`       | `ScriptingAdapter.reject id reason`   | `Msg.ScriptingSendFailed (id, summary, exn)` |
| `VizCmd op`                          | `VizAdapter.apply op`                 | `Msg.VizOpFailed (summary, exn)`             |
| `ScheduleTick schedule`              | `TimerAdapter.schedule schedule`      | `Msg.TimerFailed (timerId, summary, exn)`    |
| `CancelTimer id`                     | `TimerAdapter.cancel id`              | `Msg.TimerFailed (id, summary, exn)`         |
| `EndSession reason`                  | `LifecycleAdapter.endSession reason`  | (logged via `AuditCmd`; no failure arm — endSession itself is best-effort) |
| `Quit code`                          | `LifecycleAdapter.quit code`          | (process-exit cannot fail observably)        |
| `CompleteRpc (id, result)`           | local `TaskCompletionSource<'r>`      | (TCS completion does not raise; payload type-cast errors fault the TCS via `RpcResult.Fault`) |

\* Special case: `ScriptingAdapter.send` returns
`Result<unit, ScriptingSendError>` — `ScriptingSendError.QueueFull rejectedSeq`
becomes `Msg.AdapterCallback.QueueOverflow` (a *normal* event, not a
failure — FR-005, spec Clarification Q1), while `Failed exn` becomes
`Msg.CmdFailure.ScriptingSendFailed` (a failure to deliver).

### Per-effect-family `update` clauses

`update` must handle every `Msg.CmdFailure` arm explicitly. The
expected dispositions:

| Failure arm                         | Default `update` disposition                                                                      |
|-------------------------------------|---------------------------------------------------------------------------------------------------|
| `AuditWriteFailed`                  | Mark audit sink as broken in dashboard; emit one stderr line; do **not** retry (avoid log loops). |
| `CoordinatorSendFailed`             | Emit `Cmd.AuditCmd (CoordinatorDetached "send-failed")`; tear down the coordinator session.       |
| `ScriptingSendFailed (id, ...)`     | Mark client unsubscribed in `Model.roster`; emit `Cmd.AuditCmd (ClientDisconnected id "send-failed")`. |
| `VizOpFailed`                       | Transition `Model.viz` to `Failed (now, reason)`; emit `Cmd.AuditCmd (VizFailed reason)`; runtime continues. |
| `TimerFailed (id, ...)`             | Drop the timer from `Model.timers`; emit `Cmd.AuditCmd` describing the failure; if heartbeat watchdog, re-arm via fresh `Cmd.ScheduleTick`. |

The dispatcher MUST remain alive across every Cmd failure — none of
the dispositions tear down the runtime. Runtime tear-down is reserved
for `Lifecycle.RuntimeStopRequested` / `Lifecycle.SessionEnded`.

## Mailbox high-water audit

The runtime owns an unbounded `MailboxProcessor<Msg>`. Bounding it
would deadlock gRPC handler threads waiting on a TCS for the
response. Instead, the runtime samples the mailbox depth on every
mailbox-loop iteration; when it crosses the configured threshold,
the runtime posts `Msg.AdapterCallback.MailboxHighWater (depth, hw,
sampledAt)` so `update` can audit and react.

### Configuration

| Field                                  | Default | Source                                   |
|----------------------------------------|--------:|------------------------------------------|
| `BrokerConfig.mailboxHighWaterMark`    | 1024    | research §2; `Model.defaultConfig`       |
| `BrokerConfig.mailboxHighWaterCooldownMs` | 5000 | research §2; rate-limits the audit Cmd   |

### Update-side rate-limit cooldown

`Model` carries `lastMailboxAuditAt: DateTimeOffset option`. The
clause for `MailboxHighWater (depth, hw, sampledAt)`:

```
match model.lastMailboxAuditAt with
| Some last when (sampledAt - last).TotalMilliseconds < float model.config.mailboxHighWaterCooldownMs ->
    // Within cooldown — update Model state but suppress the audit Cmd.
    { model with mailboxDepth = depth; mailboxHighWater = max model.mailboxHighWater hw },
    [ Cmd.NoOp ]
| _ ->
    // First crossing or cooldown elapsed — emit the audit and reset the timer.
    { model with mailboxDepth = depth
                 mailboxHighWater = max model.mailboxHighWater hw
                 lastMailboxAuditAt = Some sampledAt },
    [ Cmd.AuditCmd (Audit.MailboxHighWater (depth, hw, sampledAt)) ]
```

Hysteresis (depth must drop below `HighWaterMark - HighWaterMark/8`
before re-arming) is enforced in the **runtime sampler** rather
than `update`, because `update` only sees crossings the runtime
chose to surface. Runtime sampler logic:

```
on each mailbox iteration:
    let depth = mailbox.CurrentQueueLength
    let hw = max sampler.runningHighWater depth
    sampler.runningHighWater <- hw
    if depth >= config.mailboxHighWaterMark
       && not sampler.alreadySignaled then
        post (Msg.AdapterCallback (MailboxHighWater (depth, hw, now)))
        sampler.alreadySignaled <- true
    elif depth < config.mailboxHighWaterMark - config.mailboxHighWaterMark/8 then
        // Below hysteresis floor — re-arm.
        sampler.alreadySignaled <- false
```

The dashboard footer shows `mailboxDepth / mailboxHighWater` whenever
either is non-zero.

## RuntimeStarted / RuntimeStopped lifecycle audits

`Audit.AuditEvent` gains three new arms (data-model §3.4):

```
| MailboxHighWater of depth:int * highWater:int * sampledAt:DateTimeOffset
| RuntimeStarted of brokerVersion:Version * startedAt:DateTimeOffset
| RuntimeStopped of reason:string * stoppedAt:DateTimeOffset
```

The runtime emits `RuntimeStarted` as the first `Cmd.AuditCmd` after
processing `Msg.Lifecycle.RuntimeStarted`; it emits `RuntimeStopped`
as the final `Cmd.AuditCmd` before completing the `start` Task.
Operators see them as bookends in the audit log.

## View-error rendering as data

`view: Model -> IRenderable` is pure — but `Spectre.Console.Layout`
construction can throw on invalid markup or impossible split ratios.
The runtime wraps each `view model` call in a try-with **inside the
render thread**, not the dispatcher; on exception it returns a
`Markup` panel with the exception text and a banner ("VIEW FAILED —
broker still operational"). The dispatcher remains untouched —
`Model` is unchanged and `update` does not learn about render
failures (they are not state transitions).

## SC-007 budget

These diagnostics MUST NOT add observable latency to TUI keypress
turnaround or gRPC RPC turnaround beyond the existing per-tick
budget. The mailbox sampler runs on the same loop iteration as
`update`, so its overhead is the cost of one `int` comparison plus
one rate-limit check per Msg — well under microseconds.
