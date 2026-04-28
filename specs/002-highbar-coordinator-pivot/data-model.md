# Data Model: Broker–HighBarCoordinator Wire Pivot

**Feature**: 002-highbar-coordinator-pivot
**Date**: 2026-04-28

This document records the entities, relationships, and state machines
that change as a result of the wire pivot. It is a **delta** over
[`specs/001-tui-grpc-broker/data-model.md`](../001-tui-grpc-broker/data-model.md).
Entities not mentioned here are unchanged.

The naming pattern from 001 is preserved: F# entities live in
`Broker.Core` (records / discriminated unions); generated wire types
live under `Highbar.V1.*` (new) and `FSBarV2.Broker.Contracts.*`
(unchanged on the scripting-client side); `Broker.Protocol.WireConvert`
mediates between the two.

---

## 1. Entity changes

### 1.7 `ProxyAiLink` — updated

The internal name **stays** (data-model 1.7 in 001). It now reflects
the HighBar coordinator wire rather than the retired ProxyLink wire.

| Field | Type | Change vs 001 |
|-------|------|---------------|
| `attachedAt` | `DateTimeOffset` | unchanged |
| `pluginId` | `string` | ⊕ NEW — value of `HeartbeatRequest.plugin_id`; the broker's owner key per spec FR-011. |
| `schemaVersion` | `string` | ⊕ NEW — strict-equality field replacing the 001 `Version` record; defaulted to `""` until the first Heartbeat lands. |
| `engineSha256` | `string` | ⊕ NEW — informational, surfaced on the dashboard for diagnostic alignment (FR-014). |
| `lastHeartbeatAt` | `DateTimeOffset` | ⊕ NEW — set by every accepted Heartbeat RPC and refreshed by every accepted `StateUpdate`. |
| `lastSnapshotAt` | `DateTimeOffset option` | unchanged (still drives FR-021 staleness). |
| `lastSeq` | `uint64` | ⊕ NEW — last `StateUpdate.seq` accepted; gap detection (FR-013). |
| `keepAliveIntervalMs` | `int` | ⊖ REMOVED — superseded by `heartbeatTimeoutMs` (broker-side config). |

**State machine** — replaces 001's:

```
[idle:no proxy] --plugin_dials_first_heartbeat--> [handshaking]
[handshaking] --schema_match + record_owner--> [attached]
[handshaking] --schema_mismatch--> [rejected]                       terminal-for-this-attempt (FR-003)
[attached] --heartbeat_other_plugin_id--> [reject_non_owner]        non-terminal — owner stream stays up (FR-011)
[attached] --state_update--> [attached] (refresh lastHeartbeatAt + lastSeq)
[attached] --keepalive_payload--> [attached] (refresh lastHeartbeatAt only)
[attached] --heartbeat_timeout--> [disconnected:timeout]            after heartbeatTimeoutMs of silence (FR-008)
[attached] --pushstate_remote_close--> [disconnected:graceful]
[attached] --pushstate_stream_error--> [disconnected:error]
[attached] --command_channel_close--> [degraded] (state still flowing; reverse direction lost)
[disconnected:*] --notify_subscribers + close_session--> [idle:no proxy]
```

### 1.10 `Command` — translation table updated

The F# record (data-model 1.10 in 001) is unchanged in shape. What
changes is the **wire translation** in `Broker.Protocol.WireConvert`:

| Broker `CommandKind` arm | HighBar wire | Notes |
|--------------------------|--------------|-------|
| `Gameplay (UnitOrder { kind = MOVE; ... })` | `AICommand.move_unit = MoveUnitCommand` | direct map |
| `Gameplay (UnitOrder { kind = ATTACK; targetUnitId = Some _ })` | `AICommand.attack = AttackCommand` | direct map |
| `Gameplay (UnitOrder { kind = ATTACK; targetUnitId = None })` | `AICommand.attack_area = AttackAreaCommand` | radius from broker config |
| `Gameplay (UnitOrder { kind = STOP })` | `AICommand.stop = StopCommand` | direct map |
| `Gameplay (UnitOrder { kind = GUARD })` | `AICommand.guard = GuardCommand` | direct map |
| `Gameplay (UnitOrder { kind = PATROL })` | `AICommand.patrol = PatrolCommand` | direct map |
| `Gameplay (Build { ... })` | `AICommand.build_unit = BuildUnitCommand` | direct map |
| `Gameplay (Custom { name; blob })` | `AICommand.custom = CustomCommand` | params decoded from blob |
| `Admin Pause` | `AICommand.pause_team = PauseTeamCommand { enable = true }` | team-scoped (host's team). |
| `Admin Resume` | `AICommand.pause_team = PauseTeamCommand { enable = false }` | team-scoped. |
| `Admin (GrantResources { resources })` | `AICommand.give_me = GiveMeCommand { ... }` | requires cheats enabled. |
| `Admin (SetSpeed _)` | — no AICommand mapping | rejected with `AdminNotAvailable` (research §3). |
| `Admin (OverrideVision _)` | — no AICommand mapping | rejected. |
| `Admin (OverrideVictory _)` | — no AICommand mapping | rejected. |

Each accepted command becomes a `CommandBatch`:
- `CommandBatch.batch_seq` = monotonic broker-assigned per-link counter.
- `CommandBatch.target_unit_id` = `Command.targetSlot` for gameplay; ignored arm for admin (set to 0).
- `CommandBatch.commands = [ AICommand wrapping the translated arm ]`.
- `CommandBatch.client_command_id` = lower 64 bits of `Command.commandId`
  (UUID truncated; the upper bits are stored alongside in audit for
  full uniqueness).

### 1.14 `RejectReason` — new arms

```fsharp
type RejectReason =
    | QueueFull                                                       // unchanged
    | AdminNotAvailable                                               // unchanged — now also covers admin-with-no-AICommand-mapping
    | SlotNotOwned of slot:int * actualOwner:ScriptingClientId option // unchanged
    | NameInUse                                                       // unchanged
    | SchemaMismatch of expected:string * received:string              // ⊕ NEW — replaces `VersionMismatch` for the coordinator wire (the scripting-client wire keeps its existing `VersionMismatch` arm)
    | NotOwner of attemptedPluginId:string * ownerPluginId:string     // ⊕ NEW — non-owner Heartbeat attempt (FR-011)
    | InvalidPayload of detail:string                                 // unchanged
```

The 001 `VersionMismatch of broker:Version * peer:Version` arm stays
(used by `ScriptingClient` handshake, which keeps the
`ProtocolVersion` shape per FR-007). The new `SchemaMismatch` arm is
specifically for the HighBar coordinator handshake. `WireConvert.toReject`
gains a code path for the new arms.

### 1.12 `AuditEvent` — additive cases

```fsharp
type AuditEvent =
    | ... (existing 001 cases unchanged)
    | CoordinatorAttached of at:DateTimeOffset * pluginId:string * schemaVersion:string * engineSha256:string
    | CoordinatorDetached of at:DateTimeOffset * pluginId:string * reason:string
    | CoordinatorSchemaMismatch of at:DateTimeOffset * expected:string * received:string * pluginId:string
    | CoordinatorNonOwnerRejected of at:DateTimeOffset * attemptedPluginId:string * ownerPluginId:string
    | CoordinatorHeartbeat of at:DateTimeOffset * pluginId:string * frame:uint32   // sampled; not every HB
    | CoordinatorCommandChannelOpened of at:DateTimeOffset * pluginId:string
    | CoordinatorCommandChannelClosed of at:DateTimeOffset * pluginId:string * reason:string
    | CoordinatorStateGap of at:DateTimeOffset * pluginId:string * lastSeq:uint64 * receivedSeq:uint64
```

The 001 `ProxyAttached` / `ProxyDetached` cases are **retired** in
favour of the more specific Coordinator-prefixed cases above. Code
sites that emitted them from the (removed) `ProxyLinkService` are
deleted with that service; no compat shim.

### 1.15 `HeartbeatExchange` *(new)*

```fsharp
type HeartbeatExchange = {
    pluginId: string
    frame: uint32
    schemaVersion: string
    engineSha256: string
    receivedAt: DateTimeOffset
}
```

Recorded on every successful Heartbeat for the dashboard "last beat"
indicator. Not persisted; replaced by the next exchange.

### 1.16 `OwnerRule` *(new)*

```fsharp
type OwnerRule =
    | FirstAttached                            // default: first plugin_id wins for the session
    | Pinned of pluginId:string                // operator-set pin (CLI flag) — defensive option, not enabled by default
```

Held by `BrokerState.Hub`. Drives the non-owner rejection in §1.7's
state machine.

---

## 2. Removed entities

The following data-model entries from 001 are **deleted** along with
the ProxyLink wire:

| 001 entity | Disposition |
|------------|-------------|
| ProxyLink wire `Handshake` / `HandshakeAck` | Removed — superseded by HighBar `HeartbeatRequest` / `HeartbeatResponse`. |
| ProxyLink wire `KeepAlivePing` / `KeepAlivePong` | Removed — superseded by HighBar Heartbeat (the unary RPC) and `KeepAlive` payload arm of `StateUpdate`. |
| Wire envelopes `ProxyClientMsg` / `ProxyServerMsg` | Removed — HighBar coordinator uses three distinct RPCs instead of a single bidi multiplex. |

The internal F# `Mode` / `Session` / `LobbyConfig` / `ParticipantSlot`
/ `ScriptingClientId` / `ScriptingClient` / `GameStateSnapshot` /
`DiagnosticReading` / `BrokerInfo` entities from 001 are **unchanged
in shape**.

---

## 3. Cross-entity invariants — additions

The 001 invariants 1–8 carry over unchanged. Three new invariants are
introduced by this feature; they must be enforceable through the
`Broker.Core` / `Broker.Protocol` public surface:

9. **Schema-version strict equality** (FR-003): For every accepted
   `Heartbeat`, `HeartbeatRequest.schema_version =
   HighBarSchemaVersion.expected`. Mismatches produce zero state
   acceptance — the connection is closed before any
   `StateUpdate` is processed.
10. **Single coordinator owner** (FR-011): While
    `ProxyAiLink.pluginId = Some p`, no Heartbeat with `plugin_id ≠ p`
    is accepted. Tested by submitting a second `Heartbeat` with a
    differing `plugin_id` and asserting `RejectReason.NotOwner`.
11. **Sequence-gap surfacing** (FR-013): For any accepted state
    update with `seq` ≥ `lastSeq + 2`, exactly one
    `CoordinatorStateGap` audit event fires and the dashboard
    `telemetryGap = true` flag is set for that frame's render.

---

## 4. Mapping to wire (`.proto`)

The wire mapping table from 001 §4 is replaced by:

| Broker F# entity | Coordinator-side wire (`highbar/*.proto`) | Scripting-client-side wire (`scriptingclient.proto`, `common.proto`) |
|------------------|-------------------------------------------|----------------------------------------------------------------------|
| `Snapshot.GameStateSnapshot` | derived from `highbar.v1.StateUpdate` (snapshot+delta reduction) | unchanged: `fsbar.broker.v1.GameStateSnapshot` |
| `CommandPipeline.Command` (gameplay arm) | `highbar.v1.CommandBatch.commands[*] = AICommand.{...}` | unchanged: `fsbar.broker.v1.Command` |
| `CommandPipeline.Command` (admin arm with AICommand mapping) | `highbar.v1.CommandBatch.commands[*] = AICommand.pause_team / give_me` | unchanged |
| `CommandPipeline.Command` (admin arm without mapping) | rejected at broker boundary | rejected at broker boundary |
| Heartbeat | `highbar.v1.HeartbeatRequest` / `HeartbeatResponse` | n/a |
| Schema version | `string schema_version` (strict equality) | unchanged: `fsbar.broker.v1.ProtocolVersion {major, minor}` |
| `RejectReason.SchemaMismatch` | not emitted on the wire — broker closes the stream with a gRPC `FAILED_PRECONDITION` status detailing both versions | n/a |
| `RejectReason.NotOwner` | gRPC `PERMISSION_DENIED` on the rejected Heartbeat | n/a |
| `EndReason` | broker emits gRPC stream close on PushState / OpenCommandChannel | unchanged: `fsbar.broker.v1.SessionEnd.Reason` to scripting clients |

The full HighBar `StateUpdate` and `CommandBatch` message families
(including all 97 AICommand variants) are **vendored** but only a
gameplay subset and three admin variants are actually constructed by
`WireConvert`. Unmapped variants are still recognized on the receive
side (the broker just doesn't construct them); a future feature can
extend the construction subset without re-vendoring.

The vendored `.proto` files live under `src/Broker.Contracts/highbar/`
and are pinned to upstream commit `66483515a3...` per
[`contracts/highbar-proto-pin.md`](./contracts/highbar-proto-pin.md).
