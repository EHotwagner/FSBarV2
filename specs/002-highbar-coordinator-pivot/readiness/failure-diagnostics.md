# Failure Diagnostics — 002-highbar-coordinator-pivot

**Date**: 2026-04-28

This document records every failure path the coordinator-wire pivot
introduces and how it surfaces to the operator (audit log + dashboard +
gRPC status). Constitution Principle VI requires every operationally
significant failure to fail fast or degrade explicitly — silent failure
is forbidden. Each row below names the failure surface that satisfies
that principle.

## Coordinator-wire failure paths

### 1. Schema-version mismatch (FR-003 / SC-007)

**Trigger**: First `Heartbeat` from the plugin carries a
`schema_version` field whose value does not strictly equal
`HighBarSchemaVersion.expected` (default `"1.0.0"`).

**Surfaces**:

| Surface | Form | Owner |
|---------|------|-------|
| gRPC | `Status.FAILED_PRECONDITION` with `detail = "schema mismatch expected={E} received={R}"`; the unary RPC returns the error before any state acceptance. | `HighBarCoordinatorService.Heartbeat` |
| Audit | `Audit.AuditEvent.CoordinatorSchemaMismatch (at, expected, received, pluginId)` | `BrokerState.noteHeartbeat` rejection |
| Dashboard | Red banner: "Schema mismatch — expected {E}, plugin sent {R}" until the next successful Heartbeat. | `Broker.Tui.DashboardView` (T047) |
| CLI | `--print-schema-version` exits 0 with the broker's expected version on stdout (operator pre-flight). | `Broker.App.Cli` (T046) |

**Recovery**: No automatic recovery. Operator inspects the audit log,
verifies plugin/broker schema strings match, restarts BAR with the
correct plugin or restarts the broker with `--expected-schema-version`
override matching the plugin.

### 2. Non-owner Heartbeat (FR-011)

**Trigger**: A `Heartbeat` arrives whose `plugin_id` differs from the
plugin_id captured by the first successful Heartbeat (the session
owner).

**Surfaces**:

| Surface | Form |
|---------|------|
| gRPC | `Status.PERMISSION_DENIED` with `detail = "not owner attempted={A} owner={O}"`. |
| Audit | `Audit.AuditEvent.CoordinatorNonOwnerRejected (at, attemptedPluginId, ownerPluginId)` |
| Dashboard | (passive) — owner's stream stays attached; the rejected attempt never appears in the dashboard's connection pane. |

**Recovery**: Set `HIGHBAR_COORDINATOR_OWNER_SKIRMISH_AI_ID` correctly
on the plugin side so non-owner instances do not dial the broker. The
rejection is the broker's authoritative check; the env var is the
plugin-side cooperative contract.

### 3. Heartbeat timeout (FR-008)

**Trigger**: No `Heartbeat` and no accepted `StateUpdate` for
`heartbeatTimeoutMs` (default 5000 ms) after the most recent successful
exchange.

**Surfaces**:

| Surface | Form |
|---------|------|
| gRPC | Both `PushState` and `OpenCommandChannel` server-side streams force-close. |
| Audit | `Audit.AuditEvent.CoordinatorDetached (at, pluginId, reason="heartbeat-timeout")`, then `Audit.AuditEvent.SessionEnded (at, sessionId, ProxyDisconnected "heartbeat-timeout")`. |
| Scripting clients | Each subscribed client's `SubscribeState` stream receives a `SessionEnd { Reason = ProxyDisconnected, Detail = "heartbeat-timeout" }` and the stream completes. |
| Dashboard | Mode flips to `Idle`; status pane goes blank within 10 s of the kill (SC-003 budget). |

**Recovery**: Broker stays ready for a new attach. Operator restarts
the plugin (or the engine) and the broker accepts the new session
cleanly with a fresh `sessionId`.

### 4. Admin command with no AICommand mapping (research §3)

**Trigger**: Operator submits `Admin.SetSpeed`, `Admin.OverrideVision`,
or `Admin.OverrideVictory` while the coordinator path is the only
egress (no `HighBarAdmin` bridge).

**Surfaces**:

| Surface | Form |
|---------|------|
| In-process | `WireConvert.tryFromCoreCommandToHighBar` returns `Error CommandPipeline.AdminNotAvailable`. |
| Audit | `Audit.AuditEvent.CommandRejected (at, originatingClient, commandId, AdminNotAvailable)` |
| TUI | Status pane shows "admin not available — no coordinator-side mapping (awaiting future HighBarAdmin bridge)". |
| ScriptingClient wire | `Reject { Code = ADMIN_NOT_AVAILABLE, Detail = "admin not available", CommandId = <id> }` (FR-004 carry-forward). |

**Recovery**: None — by design. Admin parity is a future feature
(research §10). `Pause`/`Resume` and `GrantResources` continue to
work via their `PauseTeamCommand` / `GiveMeCommand` mappings.

### 5. PushState sequence gap (FR-013)

**Trigger**: `StateUpdate.seq` jumps by ≥ 2 on the wire (the plugin's
own 256-deep drop-oldest queue dropped one or more frames before the
broker observed them).

**Surfaces**:

| Surface | Form |
|---------|------|
| In-process | `WireConvert.applyHighBarStateUpdate` returns `ApplyResult.Gap (lastSeq, receivedSeq)`. |
| Audit | `Audit.AuditEvent.CoordinatorStateGap (at, pluginId, lastSeq, receivedSeq)` (one per gap, not per dropped frame). |
| Dashboard | Stale-tick badge surfaces in the telemetry pane until the next gap-free batch lands. The displayed tick advances to `receivedSeq` — the broker does not roll back invented state. |
| Scripting clients | Receive a `Gap` indication on the next state fan-out so downstream clients can decide their own reaction (e.g., re-query). |

**Recovery**: None required — the running view continues from the new
seq. The badge clears when subsequent `StateUpdate`s arrive without
gaps.

### 6. Coordinator listener bind failure (carry-forward from 001 FR-005)

**Trigger**: Kestrel cannot bind the configured `--listen` URI
(port already in use, unix socket path unwritable, etc.).

**Surfaces**:

| Surface | Form |
|---------|------|
| stdout | Serilog ERROR with the underlying `IOException`. |
| Process | Broker exits with non-zero status. |
| Audit | nothing — the audit sink is not yet initialised when bind fails. |

**Recovery**: Operator picks a free port / writable socket path.

### 7. Vendored proto drift (T009 ProtoPin test)

**Trigger**: Files under `src/Broker.Contracts/highbar/` no longer
match the sha256 hashes recorded in `HIGHBAR_PROTO_PIN.md`.

**Surfaces**:

| Surface | Form |
|---------|------|
| CI test | `Broker.Contracts.Tests.ProtoPinTests` fails with the offending file's path + expected vs actual sha256. |
| Build | n/a — drift does not block the build, only the test. |

**Recovery**: Re-run the re-vendoring procedure documented in
`contracts/highbar-proto-pin.md` (refresh both copies, regenerate the
manifest, run the SurfaceArea suite to detect downstream wire-shape
changes), or revert the unintended edit.

## Cross-references

- Spec: `../spec.md` §"Edge Cases", §"Functional Requirements".
- Research: `../research.md` §3 (admin-not-mappable), §4 (heartbeat),
  §5 (schema strict-equality), §6 (owner-AI), §7 (PushState gap).
- Data model: `../data-model.md` §1.7 (`ProxyAiLink` state machine),
  §1.12 (`AuditEvent` arms), §1.14 (`RejectReason` arms).
