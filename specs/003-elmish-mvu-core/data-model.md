# Data Model: Elmish MVU Core for State and I/O

**Feature**: 003-elmish-mvu-core
**Date**: 2026-04-28

This document records the entities, relationships, and state machines
that change as a result of the MVU pivot. It is a **delta** over
[`specs/001-tui-grpc-broker/data-model.md`](../001-tui-grpc-broker/data-model.md)
and
[`specs/002-highbar-coordinator-pivot/data-model.md`](../002-highbar-coordinator-pivot/data-model.md).
Entities not mentioned here are unchanged at the data level — but
their *ownership* changes: most state that lived in
`BrokerState.Hub`'s mutable fields now lives as fields of a single
immutable `Model` record owned by `MvuRuntime.Host`. The rest of this
document enumerates that shift.

The naming pattern from 001/002 is preserved: pure F# domain entities
live in `Broker.Core` (records / discriminated unions); generated wire
types live under `Highbar.V1.*` and `FSBarV2.Broker.Contracts.*`;
`Broker.Protocol.WireConvert` mediates between the two. **New** in
this feature: a `Broker.Mvu` project hosts the runtime-shape entities
(`Model`, `Msg`, `Cmd`, the adapter interfaces).

---

## 1. New entities

### 1.1 `Model` — single immutable broker state

The `Model` record collapses every field that previously lived in
`BrokerState.Hub` into one immutable value owned by
`MvuRuntime.Host`. Every field's *value* is an entity already
defined in 001/002 data-models; this section enumerates which ones
appear and the few new sub-records needed for runtime bookkeeping.

```fsharp
type Model = {
    // ── identity / config (set at init, never changes) ───────────
    brokerInfo: Session.BrokerInfo                  // 001 §1.1
    config: BrokerConfig                            // ⊕ NEW §1.2
    startedAt: DateTimeOffset

    // ── operating mode + session ─────────────────────────────────
    mode: Mode.Mode                                 // 001 §1.2
    session: Session.Session option                 // 001 §1.3
    coordinator: Session.ProxyAiLink option         // 002 §1.7
                                                    // (renamed in 002 to coordinator-wire fields;
                                                    //  Model just holds the same record)

    // ── peers ────────────────────────────────────────────────────
    roster: ScriptingRoster.Roster                  // 001 §1.5
    slots: ParticipantSlot.ParticipantSlot list     // 001 §1.6
    queues: Map<ScriptingClientId, QueueObservation> // ⊕ NEW §1.3

    // ── lifecycle / dashboard projection ────────────────────────
    snapshot: Snapshot.GameStateSnapshot option     // 001 §1.9 (extended in 002 §1.x)
    pendingLobby: Lobby.LobbyConfig option          // 001 §1.4 — operator's in-flight lobby draft
    elevation: ScriptingClientId option             // 001 §1.x — currently elevated admin client
    viz: VizState                                   // ⊕ NEW §1.4

    // ── runtime self-diagnostics (visible on dashboard) ─────────
    mailboxDepth: int                                // last sampled value
    mailboxHighWater: int                            // running max since startup
    lastMailboxAuditAt: DateTimeOffset option        // rate-limit cooldown for HighWater audit

    // ── workflow scratchpads ────────────────────────────────────
    pendingRpcs: Map<RpcId, RpcWaiter>               // ⊕ NEW §1.5
    timers: Map<TimerId, TimerHandle>                // ⊕ NEW §1.6
}
```

**Construction**: `Model.init brokerInfo config startedAt = { … }`.
The initial `Model` is built by `Broker.App.Program` from CLI args
and passed to `MvuRuntime.Host.start`.

**Invariants**:
1. `mode = Idle ⇒ session = None ∧ coordinator = None`.
2. `coordinator = Some _ ⇒ mode ≠ Idle`.
3. `elevation = Some clientId ⇒ clientId ∈ roster ∧ roster.[clientId].admin = true`.
4. `Map.containsKey clientId queues ⇔ clientId ∈ roster`.
5. `mailboxHighWater ≥ mailboxDepth` always.
6. `pendingRpcs` keys are unique; an `RpcId` is removed when the
   matching `Cmd.completeRpc id response` is executed.
7. `timers` keys are unique; a `TimerId` is removed by
   `Cmd.timer.cancel id` or by the timer firing (one-shot timers).

### 1.2 `BrokerConfig` — startup-frozen configuration

```fsharp
type BrokerConfig = {
    listenAddress: string
    expectedSchemaVersion: string
    ownerRule: OwnerRule
    heartbeatTimeoutMs: int
    commandQueueCapacity: int
    perClientQueueCapacity: int                   // FR-010 from 001
    mailboxHighWaterMark: int                      // default 1024 — research §2
    mailboxHighWaterCooldownMs: int                // default 5000 — research §2
    queueDepthSampleMs: int                        // default 250 — research §4
    tickIntervalMs: int                            // default 100 — feature 001
    vizEnabled: bool                               // false when `--no-viz`
}

val defaultConfig : BrokerConfig
```

The configuration is set once at `Model.init` and never mutates.
Operator-driven re-configuration (e.g., changing
`tickIntervalMs` mid-run) is out of scope.

### 1.3 `QueueObservation` — Model's view of an adapter-owned queue

Per spec Clarification Q1 (2026-04-28), per-scripting-client outbound
queue *contents* live inside the production `ScriptingAdapter`; only
the observed depth + high-water mark live in `Model`. The adapter
emits these as `Msg.QueueDepth` / `Msg.QueueOverflow` notifications
on a 250 ms cadence (`config.queueDepthSampleMs`) while a client is
subscribed.

```fsharp
type QueueObservation = {
    depth: int
    highWaterMark: int
    overflowCount: int                              // running total for the dashboard
    lastSampledAt: DateTimeOffset
    lastOverflowAt: DateTimeOffset option
}
```

### 1.4 `VizState` — dashboard's view of the viewer-window subsystem

The viewer-window itself is owned by the production `VizAdapter`
(SkiaViewer task). `Model` carries only the dashboard-visible state.

```fsharp
type VizState =
    | Disabled                                       // `--no-viz` was set at startup
    | Closed                                         // viz enabled but window closed
    | Active of openedAt:DateTimeOffset * statusLine:string
    | Failed of failedAt:DateTimeOffset * reason:string
```

The transition from `Closed` → `Active` is driven by the operator's
`V` hotkey (`Msg.TuiKeypress 'V'`). The transition from `Active` →
`Failed` is driven by a `Msg.VizOpFailed` adapter callback.

### 1.5 `RpcWaiter` — outstanding gRPC handler awaiting a response

When a gRPC handler receives an inbound RPC, it dispatches a `Msg`
carrying a fresh `RpcId` and a `TaskCompletionSource<TResponse>`,
then awaits the TCS. The matching `update` clause produces a
`Cmd.completeRpc id response` that fires the TCS. This entry tracks
the in-flight TCS so a session-end can fault-cancel them
gracefully.

```fsharp
type RpcWaiter = {
    issuedAt: DateTimeOffset
    operation: string                               // for diagnostics
    tcs: obj                                        // TaskCompletionSource<TResponse> erased
}

type RpcId = RpcId of int64
```

The `obj` erasure is unavoidable in F# because each handler awaits
a different `TResponse` type. The runtime trusts the matching
`update` clause to produce a Cmd with the correctly-typed response.
This is a single point of truth (the per-Msg `update` clause) and
type-checked by the F# compiler at the dispatch site.

### 1.6 `TimerHandle` — registered timer schedule

```fsharp
type TimerHandle = {
    timerId: TimerId
    scheduledAt: DateTimeOffset
    intervalMs: int                                 // 0 for one-shot
    pendingMsg: Msg                                 // re-emitted on each fire
}

type TimerId = TimerId of int64
```

Used by `Cmd.timer.scheduleRecurring` (heartbeat watchdog,
dashboard-refresh tick, snapshot-staleness probe) and
`Cmd.timer.scheduleOnce`. Cancelled via `Cmd.timer.cancel`.

### 1.7 `Msg` — the discriminated union of all inputs

The full `Msg` union is enumerated in
[`contracts/public-fsi.md`](./contracts/public-fsi.md). It has
roughly 40–50 cases organised into the following groups:

| Group | Examples | Typical handlers |
|-------|----------|-----------------|
| TUI input | `TuiKeypress of ConsoleKeyInfo`, `TuiQuit` | `update` consults `HotkeyMap.translate` and produces the matching Cmd |
| Coordinator inbound (Heartbeat / PushState / OpenCommandChannel) | `CoordinatorAttached`, `Heartbeat`, `PushStateSnapshot`, `PushStateDelta`, `PushStateKeepAlive`, `CoordinatorPushStateClosed`, `CoordinatorCommandChannelOpened`, `CoordinatorDetached` | `update` advances the `coordinator` state machine and emits audit + scripting fan-out Cmds |
| Scripting-client inbound | `ScriptingHello`, `ScriptingSubscribe`, `ScriptingUnsubscribe`, `ScriptingCommand`, `ScriptingDisconnected` | `update` mutates `roster` and emits adapter Cmds |
| Adapter callbacks | `QueueDepth of clientId * depth * highWater`, `QueueOverflow of clientId * rejectedSeq`, `AuditWritten`, `TimerFired of timerId`, `VizWindowClosed`, `MailboxHighWater of depth` | route adapter observations into `update` |
| Cmd-completion / failure | `AuditWriteFailed`, `CoordinatorSendFailed`, `ScriptingSendFailed of clientId`, `VizOpFailed`, `TimerFailed of timerId` (FR-008) | `update` records the failure into `Model` and decides whether to retry, audit, or ignore |
| Lifecycle | `RuntimeStarted`, `RuntimeStopRequested`, `SessionEnded of EndReason` | `update` emits the correct Cmd batch (open audit sink / flush + close) |
| Tick | `DashboardTick of DateTimeOffset` | re-emits the recurring tick + recomputes dashboard projection if needed |

**Exhaustiveness**: every `update` clause matches `Msg` exhaustively.
F# enforces this at compile time (FR-004), which is the central
correctness property the pivot buys.

### 1.8 `Cmd` — the discriminated union of all side effects

```fsharp
type Cmd =
    | NoOp
    | Batch of Cmd list
    | AuditCmd of Audit.AuditEvent
    | CoordinatorOutbound of CommandPipeline.Command
    | ScriptingOutbound of ScriptingClientId * StateMsg
    | ScriptingReject of ScriptingClientId * RejectReason
    | VizCmd of VizOp
    | ScheduleTick of TimerSchedule
    | CancelTimer of TimerId
    | EndSession of Session.EndReason
    | Quit of exitCode:int
    | CompleteRpc of RpcId * RpcResult
```

(Per research §4. The full type with sub-records is in
`contracts/public-fsi.md`.)

Each arm has exactly one production adapter that knows how to
execute it:

| Arm | Production adapter | Test stub |
|-----|--------------------|-----------|
| `AuditCmd` | `AuditAdapterImpl` (Serilog) — `Broker.App` | in-memory `ResizeArray<AuditEvent>` |
| `CoordinatorOutbound` | `CoordinatorAdapterImpl` (gRPC outbound) — `Broker.Protocol` | in-memory `ResizeArray<Command>` |
| `ScriptingOutbound` / `ScriptingReject` | `ScriptingAdapterImpl` (per-client `Channel<StateMsg>`) — `Broker.Protocol` | in-memory map of `ResizeArray` |
| `VizCmd` | `VizAdapterImpl` (SkiaViewer task) — `Broker.Viz` | in-memory `ResizeArray<VizOp>` |
| `ScheduleTick` / `CancelTimer` | `TimerAdapterImpl` (System.Threading.Timer) — `Broker.App` | manual-fire test harness |
| `EndSession` / `Quit` | `LifecycleAdapterImpl` — `Broker.App` | recorded as `ResizeArray<LifecycleOp>` |
| `CompleteRpc` | the gRPC handler's TaskCompletionSource | mocked TCS |
| `Batch` | recursive over arms | recursive over arms |
| `NoOp` | no-op | no-op |

### 1.9 Adapter interfaces

For each effect family, `Broker.Mvu` declares an *interface module*
(record-of-functions) that `MvuRuntime.Host` calls. Production
projects construct concrete instances that wire the functions to
real I/O; test runtimes construct concrete instances that wire the
functions to in-memory recorders.

```fsharp
// in Broker.Mvu.AuditAdapter:
type AuditAdapter = {
    write : Audit.AuditEvent -> Async<Result<unit, exn>>
}

// in Broker.Mvu.CoordinatorAdapter:
type CoordinatorAdapter = {
    send : CommandPipeline.Command -> Async<Result<unit, exn>>
    isAttached : unit -> bool
}

// in Broker.Mvu.ScriptingAdapter:
type ScriptingAdapter = {
    send : ScriptingClientId -> StateMsg -> Async<Result<unit, ScriptingSendError>>
    reject : ScriptingClientId -> RejectReason -> Async<Result<unit, exn>>
    sampleDepth : ScriptingClientId -> Async<int * int>     // (depth, highWaterSinceLastSample)
    onClientDetached : ScriptingClientId -> Async<unit>
}

// in Broker.Mvu.VizAdapter:
type VizAdapter = {
    apply : VizOp -> Async<Result<unit, exn>>
    status : unit -> string option
}

// in Broker.Mvu.TimerAdapter:
type TimerAdapter = {
    schedule : TimerSchedule -> Async<TimerId>
    cancel : TimerId -> Async<unit>
}

// in Broker.Mvu.LifecycleAdapter:
type LifecycleAdapter = {
    endSession : Session.EndReason -> Async<unit>
    quit : exitCode:int -> Async<unit>
}
```

Each `Result.Error` is mapped by the runtime into the matching
typed-failure `Msg` arm before being posted back to the dispatcher
(FR-008).

The full `.fsi` shape — including the record fields each function
takes / returns and the function signatures the production
implementations satisfy — is in
[`contracts/public-fsi.md`](./contracts/public-fsi.md).

---

## 2. Retired entities

### 2.1 `BrokerState.Hub` — REMOVED

The mutable record `Hub` and its companion `stateLock` are deleted
from `Broker.Protocol.BrokerState` in this feature's change set.
Every public function that mutated `Hub`'s fields
(`openHostSession`, `openGuestSession`, `closeSession`,
`attachCoordinator`, `coordinatorCommandChannel`, `mode`, `roster`,
`slots`, `session`, `attachProxy`, `proxyOutbound`, …) is removed.

What remains in `BrokerState` is a small Msg-translation surface
used by gRPC handler code:

```fsharp
val postMsg : Msg -> unit
val awaitResponse<'r> : Msg -> Task<'r>
val init : BrokerConfig -> Model
```

(Concrete shape in `contracts/public-fsi.md`.)

This is the **only** state container removed by this feature. All
other 001/002 entities (`Mode`, `Session`, `Lobby`, `Snapshot`,
`ParticipantSlot`, `ScriptingRoster`, `Audit`, `CommandPipeline`,
`ProxyAiLink`) survive verbatim — they are pure data + transitions
and become *fields of `Model`* rather than fields of `Hub`.

### 2.2 `Broker.Tui.TickLoop.dispatch` + `CoreFacade` consumer pattern — REMOVED

The 001 dispatch table that translated hotkeys into
`CoreFacade.openHostSession` / `CoreFacade.closeSession` / etc. is
deleted. Hotkey actions become `Msg.Hotkey` values dispatched via
`MvuRuntime.Host.postMsg`; the `update` function consults the
existing pure `HotkeyMap.translate` and produces the matching Cmd.
The reduced `TickLoop.run` polls `Console.KeyAvailable` on its
single thread, posts each translated keypress as a `Msg`, and
calls `view (latestModel())` on each tick to feed
`LiveDisplay.Update`.

---

## 3. Updated entities

### 3.1 `Session.ProxyAiLink` — unchanged shape, lives in `Model`

The 002-data-model record (002 §1.7 with the `pluginId`,
`schemaVersion`, `engineSha256`, `lastHeartbeatAt`, `lastSeq`
fields) is unchanged. What changes is its *home*: previously it
was reached via `Hub.session |> Option.map _.proxy`. Now it is
`Model.coordinator` directly.

### 3.2 `ScriptingRoster.Roster` — unchanged shape, lives in `Model`

Same shape as 001 §1.5. Lives in `Model.roster`. Mutations that
previously locked `Hub.stateLock` and assigned to a mutable map
become `update` returning `{ model with roster = … }`.

### 3.3 `Snapshot.GameStateSnapshot` — unchanged shape, lives in `Model`

Same shape as 001 §1.9 plus the 002 extensions (§1.x in 002
data-model). Lives in `Model.snapshot`.

### 3.4 `Audit.AuditEvent` — unchanged shape, but extended with new arms

Three new audit arms are added by this feature:

```fsharp
| MailboxHighWater of depth:int * highWater:int * sampledAt:DateTimeOffset
| RuntimeStarted of brokerVersion:Version * startedAt:DateTimeOffset
| RuntimeStopped of reason:string * stoppedAt:DateTimeOffset
```

`MailboxHighWater` is rate-limited (research §2). `RuntimeStarted` /
`RuntimeStopped` are bookend audits that bracket every broker
session.

The 001/002 audit envelope (severity, timestamp, correlation ID,
payload schema) is unchanged (FR-018, SC-006).

---

## 4. Relationships and ownership

```
            ┌───────────────────────────────┐
            │   MvuRuntime.Host  (singleton) │
            │                                │
            │  - MailboxProcessor<Msg>       │
            │  - currentModel : Model        │
            │  - adapters : AdapterSet       │
            │  - modelBroadcast : Channel<Model> ──→ render thread
            └────────────┬───────────────────┘
                         │ post Msg
                         ▼
                   ┌──────────┐
   gRPC handlers ──┤  postMsg │──┐  (fan-in)
   TUI keypress ───┤          │  │
   Timer fires ────┤          │  │
   Adapter callbacks ─────────┘  │
                                 │
                                 │ (mailbox single reader)
                                 ▼
                       ┌────────────────────┐
                       │   update : Msg ->  │
                       │   Model -> Model * │
                       │   Cmd list         │
                       └─────────┬──────────┘
                                 │ Cmd list
                                 ▼
                       ┌─────────────────────┐
                       │ adapter dispatcher  │
                       │ (per-arm fan-out)   │
                       └─────────┬───────────┘
                                 │ I/O complete / fail
                                 │ (post follow-up Msg)
                                 └────► back to mailbox
```

- **Single writer to `currentModel`**: the mailbox loop body. No
  lock is needed (FR-003).
- **Many readers of `currentModel`**: the render thread (via the
  broadcast channel), gRPC handlers awaiting an `RpcWaiter`'s TCS
  (the TCS payload is the projection of `Model` they need), and
  the diagnostics dashboard. Reads are immutable record reads; the
  broadcast channel hands out the latest reference.
- **No cross-thread synchronisation primitives** beyond the
  `MailboxProcessor` and the broadcast `Channel<Model>` — both
  standard .NET / F# library types.

---

## 5. State transitions affected by the pivot

### 5.1 Coordinator-attach state machine (002 §1.7) — same shape, new home

The state machine from 002 data-model §1.7 is unchanged. The
transitions are now expressed as `update` clauses:

```fsharp
// pseudo:
let update msg model =
    match msg with
    | CoordinatorAttached attach ->
        match model.coordinator with
        | None ->
            { model with coordinator = Some attach;
                         mode = transitionToGuestOrHost attach },
            Cmd.Batch [
                Cmd.AuditCmd (Audit.CoordinatorAttached attach);
                Cmd.ScheduleTick (heartbeatWatchdog attach.pluginId);
            ]
        | Some _ -> model, Cmd.AuditCmd (Audit.CoordinatorAttachRejected "already attached")
    | Heartbeat hb -> ...
    | ...
```

### 5.2 Mailbox high-water audit cooldown

The new `lastMailboxAuditAt` field on `Model` enforces the rate
limit:

```
[depth ≤ HighWater - hysteresis]    → quiescent (no audit)
[depth > HighWater] && (now - lastMailboxAuditAt > cooldownMs)
                                     → emit Cmd.AuditCmd (MailboxHighWater …)
                                     → set lastMailboxAuditAt = now
[depth > HighWater] && (now - lastMailboxAuditAt ≤ cooldownMs)
                                     → suppress (cooldown)
```

The hysteresis prevents thrashing around the threshold; default is
`HighWater - HighWater/8` (i.e., 87.5 % of threshold).

### 5.3 Cmd-failure routing (FR-008)

For each effect-family adapter, the runtime wraps its async call
in a try-with at the boundary:

```fsharp
async {
    try
        do! adapter.write event
        // success-path Msg, e.g., AuditWritten — only emitted if `update`
        // explicitly requested follow-up via Cmd shape
    with ex ->
        post (Msg.AuditWriteFailed (summary, ex))
}
```

The `update` clause for `AuditWriteFailed` decides whether to
retry, mark the audit sink as broken in the dashboard, or simply
log to stderr — but the *decision* lives in `update`, not in the
adapter. The same pattern applies to every effect family per
spec Clarification Q3.

---

## 6. Synthetic-evidence disclosure plan

Per Constitution Principle IV, every synthetic surface added by
this feature is disclosed at all five surfaces.

### 6.1 New `[S]` surface: `Broker.Mvu.Testing.Fixtures`

A small fixture builder that constructs representative `Model`
values (e.g., "guest mode, 2 subscribed scripting clients,
mid-game with 200 units", "host mode with elevated client",
"coordinator attached but no snapshot yet"). These fixtures are
synthetic by definition because the values they encode would
otherwise come from a live game.

| Surface | Disclosure |
|---------|------------|
| Task | `tasks.md` marks each fixture-touching task `[S]`; downstream tasks that consume the fixtures auto-mark `[S*]` per the evidence audit. |
| Code | `Broker.Mvu.Testing.Fixtures` opens with a banner `(* SYNTHETIC FIXTURE: representative Model values, not derived from a live BAR session. Real-game evidence regenerated by Story 3 walkthrough — see specs/001-tui-grpc-broker/readiness/. *)` |
| Test | Tests that use these fixtures contain the token `Synthetic` in their name (e.g., `let ``Synthetic-T029-CoordinatorAttachedTranscript`` = …`). |
| Spec | Spec User Story 1 names the synthetic-fixture path and points at the real-game walkthrough in `quickstart.md` Story 3. |
| PR | The PR description enumerates each `[S]` task and links to this section. |

### 6.2 No other new synthetic surfaces

The `SyntheticCoordinator` integration fixture from 002 is **not**
new — it survives unchanged and continues to be disclosed under
002's plan.

The carve-out closures for T029 / T037 / T042 / T046 are *replacing*
synthetic evidence with MVU-replay evidence. The MVU-replay tests
themselves use the `Fixtures` builder for their starting `Model`,
which is why `Fixtures` carries the `[S]` tag. The real-game
evidence regeneration under `readiness/` (FR-021) is where the
non-synthetic closure ultimately lands; the MVU-replay tests
provide the deterministic CI evidence.
