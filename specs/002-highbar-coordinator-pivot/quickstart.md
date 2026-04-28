# Quickstart: Broker–HighBarCoordinator Wire Pivot

**Feature**: 002-highbar-coordinator-pivot
**Date**: 2026-04-28

This quickstart shows how an operator validates the wire pivot end-to-end
against a real BAR + HighBarV3 build. The Story-3 carve-out closures
(T029/T037/T042/T046 from feature 001) all reduce to running these flows
and capturing the resulting transcripts under `readiness/`.

You will need:

- A BAR install (`spring-headless` or `spring`) on the operator's machine
  with the HighBarV3 plugin built and installed in the engine's AI lookup
  path. Both the engine and the plugin are owned upstream
  (`EHotwagner/HighBarV3`); installation is out of scope for this
  feature.
- The broker built locally (`dotnet build` at repo root) and on `PATH`
  (or runnable from `src/Broker.App/bin/Debug/net10.0/Broker.App`).
- A scripting client to subscribe; the existing `tests/Broker.Integration.Tests`
  end-to-end harness binary works for headless verification.

---

## §1 Cold-start: dashboard goes live within 10 s

Validates **SC-001** (live dashboard within 10 s of first engine tick) and
**FR-001 / FR-002** (broker accepts the plugin's three RPCs over the
single coordinator URI).

```sh
# Terminal 1 — broker, listening on a Unix socket the plugin can dial
dotnet run --project src/Broker.App -- \
    --listen unix:/tmp/fsbar-coordinator.sock \
    --print-schema-version
# Expect: "broker schema version: 1.0.0" then the Spectre dashboard
# in Idle state.

# Terminal 2 — launch BAR with the plugin pointed at the broker
HIGHBAR_COORDINATOR=unix:/tmp/fsbar-coordinator.sock \
HIGHBAR_COORDINATOR_OWNER_SKIRMISH_AI_ID=1 \
spring-headless --skirmish ...   # operator's usual launch flags
```

**What to observe in the broker dashboard**:

1. Within 10 s of the first engine tick, the dashboard transitions from
   `Mode: Idle` to `Mode: Guest` (or `Hosting` if the operator is
   driving a host-mode lobby) and shows the attached plugin's
   `plugin_id` + schema version + engine SHA in the Status pane.
2. Tick counter starts climbing; player / unit / building counts begin
   updating each frame.
3. Audit log under `logs/broker-YYYYMMDD.log` includes a
   `CoordinatorAttached` event, then `CoordinatorHeartbeat` events
   sampled every N seconds.

**For Story-3 closure**: capture the dashboard transcript and the audit
log excerpt to `specs/001-tui-grpc-broker/readiness/us1-evidence.md`,
replacing the `[S]` synthetic-proxy capture with a real-game one. Update
the synthetic-evidence inventory entry for T029 from "infeasible without
proxy AI" to "closed; live evidence captured".

---

## §2 Subscribed scripting client sees frames within 1 s

Validates **SC-002** (game-tick → scripting-client p95 ≤1 s over 500
real-game ticks). Re-anchors **001 SC-003**.

With the broker + game running from §1:

```sh
# Terminal 3 — start a scripting subscriber (the integration harness
# has a one-shot subscribe-and-print mode; substitute your own client)
dotnet test tests/Broker.Integration.Tests \
    --filter "Sc003LatencyTests" \
    --no-build \
    -- \
    --coordinator unix:/tmp/fsbar-coordinator.sock \
    --window 500 \
    --capture readiness/sc003-latency.md
```

**What to observe**:

1. The harness records 500 ticks of game-tick → scripting-client receipt
   timestamps, computes the p95, and writes
   `specs/001-tui-grpc-broker/readiness/sc003-latency.md` with the
   real-wire numbers.
2. p95 ≤1 s; if it isn't, the artifact records the violation and Story
   3 isn't complete until either the cause is fixed or the SC is
   formally re-anchored with rationale.

**For Story-3 closure**: replaces 001's synthetic-loopback `sc003-latency.md`
header line ("synthetic proxy peer") with "real plugin peer".

---

## §3 Operator commands reach the engine

Validates **FR-005** (gameplay commands flow back over OpenCommandChannel)
and the User Story 2 acceptance scenarios.

With the broker + game + subscriber running:

1. In the broker TUI, press **P** (pause hotkey).
2. In the next inbound `StateUpdate`, observe the dashboard's
   pause indicator flip and a `CoordinatorCommandChannelOpened` plus
   per-command audit line for the dispatched `PauseTeamCommand`.
3. From a scripting client with admin elevation
   (`G alice-bot` from the TUI to grant; see 001 quickstart §3.2),
   submit a `MoveUnit` command targeting a known unit ID.
4. In a subsequent snapshot, observe the unit's position has changed
   and the dispatch is audit-logged with the original command UUID.

**Admin commands without an AICommand mapping** — `SetSpeed`,
`OverrideVision`, `OverrideVictory` — are rejected at the broker
boundary today with a clear "no coordinator-side mapping" message in
the TUI status pane and a `CommandRejected{AdminNotAvailable}`
audit event. See research §3 for the rationale; full admin parity is
a future feature.

---

## §4 Disconnect recovery

Validates **SC-003** (detection-to-Idle ≤10 s in ≥95 % of ≥20 trials).
Re-anchors **001 SC-005**.

```sh
# With everything running from §3:
# Terminal 4 — kill the engine process mid-stream
pkill spring-headless
```

**What to observe**:

1. Within ≤5 s of the kill, the broker emits a `CoordinatorDetached`
   audit event with reason `heartbeat-timeout` (or `stream-error` if
   the gRPC channel tore before the heartbeat lapsed).
2. All subscribed scripting clients receive a `SessionEnd` indication
   within 1 s of the broker's detection.
3. Within ≤10 s total, the dashboard returns to `Mode: Idle` and is
   ready to accept a new plugin connection.

Repeat ≥20 times with `Sc005RecoveryTests`-style automation; record
detection-to-`SessionEnd` and detection-to-Idle distributions to
`specs/001-tui-grpc-broker/readiness/sc005-recovery.md`. ≥95 % of
trials must clear the budget.

---

## §5 Schema-version mismatch handshake

Validates **FR-003 / SC-007**.

```sh
# Build the broker with a deliberately-wrong expected schema version
# (a CLI override exists for this purpose):
dotnet run --project src/Broker.App -- \
    --listen unix:/tmp/fsbar-coordinator.sock \
    --expected-schema-version 0.9.9-test
```

Launch the plugin pointed at this broker. **What to observe**:

1. Within 1 s of the plugin's first `Heartbeat`, the broker rejects
   with gRPC `FAILED_PRECONDITION` carrying both versions in the
   status detail.
2. The broker's audit sink contains a `CoordinatorSchemaMismatch`
   event with `expected="0.9.9-test"` and `received="1.0.0"`.
3. The dashboard's Status pane shows a red "Schema mismatch" banner
   with both version strings.
4. The broker remains in `Mode: Idle` and is ready to accept a future
   correctly-versioned connection (no permanent failure state).

Restart the broker without the override to confirm normal operation
resumes once the version expectations align.

---

## §6 ProxyLink surface is gone

Validates **FR-006** and **SC-006** (ProxyLink removed; ScriptingClient
unchanged).

```sh
dotnet test tests/SurfaceArea
```

**What to observe**:

1. Test suite green.
2. No `Broker.Protocol.ProxyLinkService.surface.txt` baseline exists.
3. A `Broker.Protocol.HighBarCoordinatorService.surface.txt` baseline
   exists and matches the F# `.fsi` produced by the build.
4. All `Broker.Protocol.ScriptingClientService.*` baselines are
   byte-identical to their pre-pivot versions (`git diff` shows no
   changes under that prefix).

A downstream package built against pre-pivot
`Broker.Contracts` that imports `ProxyClientMsg` or
`Broker.Protocol.ProxyLinkService` should fail to build with a clear
"name not found" error — confirming there's no soft compatibility
shim left behind.

---

## §7 Pin manifest verification

Validates the audit anchor on the vendored proto set.

```sh
dotnet test tests/Broker.Contracts.Tests --filter "ProtoPin"
```

**What to observe**:

1. The pin test computes sha256 of every file under
   `src/Broker.Contracts/highbar/` and asserts equality with the
   constants generated from
   `specs/002-highbar-coordinator-pivot/contracts/highbar-proto-pin.md`.
2. If anyone hand-edits a vendored proto, this test fails loudly with
   a diff between the manifest and the actual file. Re-running the
   re-vendoring procedure in the pin manifest fixes both copies.
