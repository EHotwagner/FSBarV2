# US1 — synthetic-coordinator end-to-end evidence (T023)

**Date**: 2026-04-28
**Status**: `[S]` — broker-side wire path is real production code; the
plugin peer is the loopback `SyntheticCoordinator` fixture
(`tests/Broker.Integration.Tests/SyntheticCoordinator.fs`). Real-wire
closure for the same scenarios is owned by Phase 5 (T033 / T034 / T035
/ T036 / T036a / T037).

This artifact backs the `[S]` marker on T023 in `tasks.md` and the
matching row in the Synthetic-Evidence Inventory.

## Acceptance scenarios verified

### US1 acceptance #1 — cold-start within 10 s

The synthetic coordinator dials the broker, sends a Heartbeat, opens
`PushState`, and the broker transitions from Idle to attached. Confirmed
in `Synthetic_T017`:

> `Expect.equal (BrokerState.activePluginId hub) (Some "ai-1") "owner captured"`

The broker's `ProxyAiLink` records `pluginId`, `schemaVersion`, and
`engineSha256`; the audit sink emits `CoordinatorAttached`.

### US1 acceptance #2 — scripting client receives state

A pre-attached `ScriptingClient` subscriber receives the broker's
fan-out frames within the SC-002 budget. `Synthetic_T017` pushes three
snapshots and asserts the subscriber sees `tick=1, 2, 3` in order. The
SC-002 measurement (T024) further confirms p95 ≤ 1 s over 200 ticks at
~30 Hz cadence:

```
p95: 0.618 ms
p99: 2.312 ms
max: 36.835 ms
budget: ≤ 1 s
verdict: PASS
```

(See `sc002-synthetic-latency.md` for the full report.)

### US1 acceptance #3 — schema-version mismatch is rejected

`Synthetic_T013` sends a Heartbeat with `schema_version="0.9.9-test"`
against a broker whose expected version is `1.0.0`. The unary RPC
returns `Status(StatusCode="FailedPrecondition", Detail="schema mismatch
expected=1.0.0 received=0.9.9-test")` within 1 s wall-clock; the audit
sink logs `CoordinatorSchemaMismatch`. Broker stays in `Idle` and is
ready for the next attach.

### US1 acceptance #4 — graceful disconnect → SessionEnd within 10 s

`Synthetic_T017` completes the `PushState` request stream after pushing
three snapshots. The broker's `Impl.PushState` handler observes the
end-of-stream and runs `closeSession ProxyDisconnected`. Subscribed
scripting clients receive a `SessionEnd` indication, the broker returns
to Idle, and `CoordinatorDetached` is audited. SC-003 (T025) measures
this disconnect path under load:

```
trials: 20
detection ≤ 5 s: 20 / 20 (100%)
recovery-to-Idle ≤ 10 s: 20 / 20 (100%)
max detection: 68.2 ms
max recovery: 68.2 ms
budget: detect ≤ 5 s, recover ≤ 10 s in ≥ 95% of trials
verdict: PASS
```

(See `sc003-synthetic-recovery.md` for the full report.)

## Edge cases verified

### Owner-skirmish-AI rule (FR-011)

`Synthetic_T051` runs two consecutive sessions with different
`pluginId`s through the same broker process. The `BrokerState.activePluginId`
field is reset between sessions, the second session is accepted with a
fresh owner, and the audit sink shows ≥ 2 `CoordinatorAttached` events.
Unit-level coverage of the rejection arm lives in
`tests/Broker.Protocol.Tests/CoordinatorTests.fs`:

> `BrokerState.noteHeartbeat` returns `Error (NotOwner ("ai-2", "ai-1"))`
> when a second pluginId attempts a Heartbeat against an already-attached
> session, and `CoordinatorNonOwnerRejected` is audited.

### PushState seq gap (FR-013)

WireConvert unit tests (`Synthetic_T014`) verify that a `seq` jump > 1
produces an `ApplyResult.Gap` with the missed range, and the broker
reports `CoordinatorStateGap` via the audit sink. The dashboard's
`telemetryGap` flag is set by `BrokerState.noteStateGap`.

### Pre-attached scripting client (FR-015)

`Synthetic_T052` subscribes a scripting client BEFORE any coordinator
attaches, then attaches one and pushes the first frame. The pre-attached
subscriber receives the first frame gap-free with the correct tick.

### Multiple consecutive sessions (FR-012)

`Synthetic_T051` covers this directly (see Owner-skirmish-AI rule).

## What "synthetic" specifically means here

The `SyntheticCoordinator` fixture *is* a real `HighBarCoordinatorClient`
talking to the broker over loopback gRPC. Every byte the broker sees on
the coordinator side is a real proto-encoded `HeartbeatRequest` /
`StateUpdate` / `CommandBatch`. The synthetic part is that those bytes
come from a test harness rather than from a BAR engine running the
HighBarV3 plugin. Real-game closure (T033 onwards) replaces the test
harness with the engine but does not change the broker-side code.

## Real-evidence path

| Scenario | Real-wire closure task |
|----------|------------------------|
| Cold-start (acceptance #1) | T033 — quickstart §1 against real BAR |
| Latency budget (SC-002) | T034 — quickstart §2 ≥ 500 real ticks |
| Disconnect recovery (SC-003) | T035 — quickstart §4 ≥ 20 real trials |
| Dashboard load (SC-004) | T036 — quickstart §3 with ≥ 4 clients + ≥ 200 units |
| Schema mismatch (SC-007) | T034 — covered by the real-game cold-start path |
| Host-mode admin lifecycle | T036a — closes 001 T037 |
| Viz capture | T037 — quickstart §1 + viz screenshot |

When the operator captures these against a real BAR + HighBarV3 build,
T038 flips the inventory entries on 001's tasks.md from `[S]` to `[X]`.
