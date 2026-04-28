# Data Model: TUI gRPC Game Broker

**Feature**: 001-tui-grpc-broker
**Date**: 2026-04-27

This document captures the entities, fields, relationships, validation
rules, and state transitions that the broker reasons about. It is the
authoritative reference for `Broker.Core` `.fsi` design and for the
`.proto` contracts in `src/Broker.Contracts/`.

Naming convention: F# entities are records or single-case discriminated
unions in `Broker.Core`; corresponding wire types live under
`Broker.Contracts` and are converted at the protocol boundary.

---

## 1. Entity catalogue

### 1.1 `BrokerInfo`

Identity and version of the running broker process. Static for the lifetime
of the process.

| Field | Type | Notes |
|-------|------|-------|
| `version` | `Version` (major, minor) | Used in `VERSION_MISMATCH` payload (FR-029). |
| `listenAddress` | `string` (host:port) | Default `127.0.0.1:5021` (carried forward from FSBar V1). |
| `startedAt` | `DateTimeOffset` | For "uptime" in dashboard footer. |

### 1.2 `Mode`

Discriminated union — the broker is in exactly one mode at a time.

```fsharp
type Mode =
    | Idle                       // No session attached. Lobby UI available.
    | Hosting of LobbyConfig     // Broker launched and owns the game (admin).
    | Guest                      // External lobby launched the game (no admin).
```

**Validation**:
- Transition to `Hosting` only from `Idle` (FR-001, FR-014).
- Transition to `Guest` only from `Idle` (FR-002, FR-003 — auto-detected
  on `ProxyLink` connect when no host launch is in progress).
- Transition to `Idle` from `Hosting`/`Guest` only via explicit teardown
  or a detected disconnect (FR-026, FR-027).

### 1.3 `LobbyConfig` (host mode only)

The pre-launch description of a host-mode session.

| Field | Type | Notes |
|-------|------|-------|
| `mapName` | `string` | Must be a known map name. |
| `gameMode` | `string` | Game-mode identifier per HighBarV3. |
| `participants` | `ParticipantSlot list` | Ordered. At least one slot. |
| `display` | `Headless \| Graphical` | FR-012. |

**Validation rules** (FR-013):
- `participants.Length` ≤ map capacity for `mapName`.
- At least one slot of kind `ProxyAi` if any scripting client is currently
  connected and expects a slot.
- No duplicate `slotIndex`.
- `mapName` and `gameMode` must be non-empty.

### 1.4 `ParticipantSlot`

A position in a session.

```fsharp
type ParticipantKind =
    | Human                          // External human player
    | BuiltInAi of difficulty:int    // Engine-provided AI
    | ProxyAi                        // Bridged to a scripting client
```

| Field | Type | Notes |
|-------|------|-------|
| `slotIndex` | `int` | Zero-based, unique within a session. |
| `kind` | `ParticipantKind` | |
| `team` | `int` | Team grouping. |
| `boundClient` | `ScriptingClientId option` | Only meaningful when `kind = ProxyAi`. Set/cleared by the single-writer rule (FR-009). |

### 1.5 `ScriptingClientId`

```fsharp
type ScriptingClientId = ScriptingClientId of name:string
```

The canonical identifier asserted by the client on the gRPC handshake
(FR-008, clarification 2026-04-27 Q4). Non-empty. Unique among
currently-connected clients. Not persisted across broker restarts.

### 1.6 `ScriptingClient`

A connected external bot/automation tool.

| Field | Type | Notes |
|-------|------|-------|
| `id` | `ScriptingClientId` | Asserted on handshake. |
| `connectedAt` | `DateTimeOffset` | |
| `protocolVersion` | `Version` | From handshake. Major must match broker (FR-029). |
| `boundSlot` | `int option` | Slot it currently controls (single-writer, FR-009). |
| `isAdmin` | `bool` | Operator-toggled, host mode only (FR-016). Default `false`. |
| `commandQueueDepth` | `int` | Current depth; bounded by `commandQueueCapacity`. |

**Lifecycle** (state machine):

```
[connecting] --handshake_ok--> [active]
[connecting] --name_in_use--> [rejected:NAME_IN_USE]              terminal
[connecting] --major_mismatch--> [rejected:VERSION_MISMATCH]      terminal
[active] --bind_slot--> [active(boundSlot=Some s)]
[active] --unbind_slot--> [active(boundSlot=None)]
[active] --grant_admin (host mode only)--> [active(isAdmin=true)]
[active] --revoke_admin--> [active(isAdmin=false)]
[active] --disconnect--> [gone]                                    terminal
```

### 1.7 `ProxyAiLink`

The bidirectional gRPC channel between the broker (`ProxyLink` service)
and the in-game proxy AI agent. Singleton — at most one proxy is attached
at a time (single-session broker, per Assumptions in spec).

| Field | Type | Notes |
|-------|------|-------|
| `attachedAt` | `DateTimeOffset` | |
| `protocolVersion` | `Version` | From handshake. |
| `lastSnapshotAt` | `DateTimeOffset option` | Used to mark telemetry stale (FR-021). |
| `keepAliveIntervalMs` | `int` | gRPC keepalive; default 2000. |

**State machine**:

```
[idle:no proxy] --connect--> [handshaking]
[handshaking] --version_ok--> [attached]
[handshaking] --version_mismatch--> [rejected]                     terminal-for-this-attempt
[attached] --snapshot--> [attached]                                stays here per snapshot
[attached] --keepalive_miss>=N--> [disconnected:timeout]
[attached] --remote_close--> [disconnected:graceful]
[disconnected:*] --notify_clients --> [idle:no proxy]              ready for next session
```

### 1.8 `Session`

The live link between the broker and one running game.

| Field | Type | Notes |
|-------|------|-------|
| `id` | `Guid` | Unique per session. |
| `mode` | `Mode` | `Hosting` or `Guest` (never `Idle` — `Idle` means no Session). |
| `state` | `SessionState` | See below. |
| `startedAt` | `DateTimeOffset` | |
| `proxy` | `ProxyAiLink option` | Set after proxy attaches. |
| `telemetry` | `GameStateSnapshot option` | Latest snapshot. |
| `pause` | `Paused \| Running` | Mirrors game pause state. |
| `speed` | `decimal` | Game speed multiplier (1.0 default). |

```fsharp
type SessionState =
    | Configuring     // Host mode only — operator editing LobbyConfig
    | Launching       // Host mode only — game process starting
    | Active          // Proxy attached, snapshots flowing
    | Ended of EndReason

and EndReason =
    | Victory
    | Defeat
    | OperatorTerminated
    | GameCrashed
    | ProxyDisconnected of detail:string
```

**Transitions**:

```
HOST MODE
[Configuring] --validate_ok+launch--> [Launching]
[Configuring] --validate_fail--> [Configuring]                      (FR-013)
[Launching]   --proxy_attached--> [Active]
[Launching]   --launch_timeout/error--> [Ended(GameCrashed)]
[Active]      --proxy_disconnect--> [Ended(ProxyDisconnected _)]    (FR-026)
[Active]      --game_process_gone--> [Ended(GameCrashed)]           (FR-027)
[Active]      --operator_quit--> [Ended(OperatorTerminated)]
[Active]      --game_end_signal--> [Ended(Victory|Defeat)]
[Ended _]     --teardown--> (return broker to Idle)                 (FR-014)

GUEST MODE
(Idle) --proxy_attached--> [Active]                                 (FR-002)
[Active] --proxy_disconnect--> [Ended(ProxyDisconnected _)]
[Ended _] --teardown--> (Idle)
```

### 1.9 `GameStateSnapshot`

Point-in-time view of the active session, streamed to scripting clients
and rendered in the dashboard / viz.

| Field | Type | Notes |
|-------|------|-------|
| `sessionId` | `Guid` | Matches owning session. |
| `tick` | `int64` | Engine tick number (monotonically increasing). |
| `capturedAt` | `DateTimeOffset` | Broker-side receipt time. |
| `players` | `PlayerTelemetry list` | One entry per player/team. |
| `units` | `Unit list` | Position + class + ownership. |
| `buildings` | `Building list` | Position + class + ownership. |
| `mapMeta` | `MapMeta option` | Sent on first snapshot, then omitted. |

```fsharp
type PlayerTelemetry = {
    playerId: int
    teamId: int
    name: string
    resources: ResourceVector            // metal, energy, etc. — generic per spec
    unitCount: int
    buildingCount: int
    unitClassBreakdown: Map<string,int>  // "tank"->12, "scout"->5, ...
    economy: EconomyStats                // income/expenditure per resource
    kills: int
    losses: int
}

type Unit = {
    id: uint32
    classId: string
    ownerPlayerId: int
    pos: Vec2                            // map coordinates
}

type Building = {
    id: uint32
    classId: string
    ownerPlayerId: int
    pos: Vec2
}
```

**Validation**:
- `tick` strictly greater than the previous accepted snapshot for the same
  session (gap-free streams to subscribers, FR-006).
- `mapMeta` MUST be present in the first snapshot of a session and SHOULD
  be omitted in subsequent snapshots.

### 1.10 `Command`

A request from a scripting client (or the operator, for admin commands)
to mutate the game.

```fsharp
type CommandKind =
    | Gameplay of payload:GameplayPayload    // Unit orders, build, etc.
    | Admin of payload:AdminPayload          // Speed, pause, cheat-class

and AdminPayload =
    | SetSpeed of multiplier:decimal
    | Pause
    | Resume
    | GrantResources of playerId:int * resources:ResourceVector
    | OverrideVision of playerId:int * mode:VisionMode
    | OverrideVictory of playerId:int * outcome:VictoryOverride

and GameplayPayload =
    | UnitOrder of unitIds:uint32 list * order:OrderKind
    | Build of builderId:uint32 * classId:string * pos:Vec2
    | Custom of name:string * blob:byte[]    // forward-compat escape hatch
```

| Field | Type | Notes |
|-------|------|-------|
| `commandId` | `Guid` | Client-generated; echoed in any rejection (`QUEUE_FULL` payload). |
| `originatingClient` | `ScriptingClientId` | Always present; for audit. |
| `targetSlot` | `int option` | Required for `Gameplay`; meaningless for `Admin`. |
| `kind` | `CommandKind` | |
| `submittedAt` | `DateTimeOffset` | |

**Authority rules**:
- `Admin _` — accepted iff `mode = Hosting` AND originating client
  `isAdmin = true` (or operator-issued from TUI). Otherwise rejected with
  `ADMIN_NOT_AVAILABLE` (FR-004, FR-016).
- `Gameplay _` — accepted iff `targetSlot.IsSome` AND that slot's
  `boundClient = Some originatingClient` (single-writer rule, FR-009).
  Otherwise rejected with `SLOT_NOT_OWNED`.

### 1.11 Per-client command queue

Per-client bounded channel sitting between `ScriptingClient.SubmitCommand`
RPC handler and the `ProxyLink` egress (FR-010).

| Field | Type | Notes |
|-------|------|-------|
| `clientId` | `ScriptingClientId` | |
| `capacity` | `int` | Default 64; configurable. |
| `depth` | `int` | Current count. Exposed in dashboard. |

**Behavior**:
- When `depth < capacity`: enqueue, return `OK`.
- When `depth = capacity`: pause reads on the client's gRPC stream
  (HTTP/2 flow control via the BackpressureGate).
- Any command that nevertheless arrives once paused is rejected
  synchronously with `QUEUE_FULL` carrying the offending `commandId`.
- Commands MUST NEVER be silently dropped, evicted, or reordered.

### 1.12 `AuditEvent`

Structured records written to the rolling-file audit sink (Serilog,
FR-028). One union, one record per case for a stable wire shape.

```fsharp
type AuditEvent =
    | ProxyAttached of at:DateTimeOffset * version:Version
    | ProxyDetached of at:DateTimeOffset * reason:string
    | ClientConnected of at:DateTimeOffset * id:ScriptingClientId * version:Version
    | ClientDisconnected of at:DateTimeOffset * id:ScriptingClientId * reason:string
    | NameInUseRejected of at:DateTimeOffset * attempted:string
    | VersionMismatchRejected of at:DateTimeOffset * peerKind:string * peerVersion:Version
    | AdminGranted of at:DateTimeOffset * id:ScriptingClientId * by:string
    | AdminRevoked of at:DateTimeOffset * id:ScriptingClientId * by:string
    | CommandRejected of at:DateTimeOffset * id:ScriptingClientId * commandId:Guid * reason:RejectReason
    | ModeChanged of at:DateTimeOffset * from:Mode * to:Mode
    | SessionEnded of at:DateTimeOffset * sessionId:Guid * reason:EndReason
```

### 1.13 `DiagnosticReading`

The dashboard view-model (FR-017 to FR-021). Distinct from `GameStateSnapshot`
because it includes broker-internal state.

| Field | Type | Notes |
|-------|------|-------|
| `broker` | `BrokerInfo` | |
| `serverState` | `Listening of string \| Down of reason:string` | FR-018. |
| `connectedClients` | `ScriptingClient list` | For per-client identity + admin flag (FR-018). |
| `mode` | `Mode` | FR-019. |
| `session` | `SessionState option` | None when `Idle`. |
| `elapsed` | `TimeSpan option` | Since session start. |
| `pause` | `Paused \| Running option` | None when no session. |
| `speed` | `decimal option` | None when no session. |
| `telemetry` | `GameStateSnapshot option` | Latest. |
| `telemetryStale` | `bool` | True when no snapshot received within staleness threshold (FR-021). |

### 1.14 `RejectReason`

```fsharp
type RejectReason =
    | QueueFull
    | AdminNotAvailable
    | SlotNotOwned of slot:int * actualOwner:ScriptingClientId option
    | NameInUse
    | VersionMismatch of broker:Version * peer:Version
    | InvalidPayload of detail:string
```

Each maps 1:1 to a gRPC status code at the protocol edge.

---

## 2. Relationships

```
Broker (1) ──────owns────── (1) gRPC Server (1) ─── hosts ─── (2) Services
   │                                                          │ ProxyLink
   │                                                          │ ScriptingClient
   │
   ├── (0..1) Session ──── (0..1) ProxyAiLink
   │              │
   │              ├── Mode (Hosting → owns LobbyConfig → owns ParticipantSlots)
   │              │                                              │
   │              │                                              └── (0..1) boundClient → ScriptingClient
   │              │
   │              └── (0..*) GameStateSnapshot (latest cached on Session.telemetry)
   │
   ├── (0..*) ScriptingClient ──── (1) per-client CommandQueue
   │
   └── (0..*) AuditEvent (rolling file)
```

Cardinality notes:
- One broker, at most one session at a time (Assumptions, spec line 192).
- One session, at most one proxy-AI link (singleton).
- Many scripting clients, but only one bound to any given participant slot
  at any time (FR-009).

---

## 3. Cross-entity invariants

These are the rules an Expecto property test must enforce against the
public surface of `Broker.Core`:

1. **Single-writer per slot** (FR-009): No two `ScriptingClient`s can have
   the same `boundSlot` value at the same time. Re-bind requires unbind.
2. **Admin only in host mode** (FR-004, FR-016): For all sessions where
   `mode ≠ Hosting`, every `Admin _` command is rejected with
   `AdminNotAvailable`, regardless of `isAdmin`.
3. **Admin grants do not survive restart** (FR-016): On broker startup,
   every `ScriptingClient.isAdmin = false`.
4. **Name uniqueness across live clients** (FR-008): No two
   `ScriptingClient`s in the active roster share an `id`.
5. **Snapshot tick monotonicity** (FR-006 gap-free): For any subscribed
   client, the sequence of delivered snapshots has strictly increasing
   `tick`.
6. **Major-version match** (FR-029): Any peer with `protocolVersion.major
   ≠ BrokerInfo.version.major` is rejected at handshake.
7. **No silent command drops** (FR-010): For every accepted command, there
   is exactly one of {forwarded to proxy, audit-logged rejection}. Test
   counts must reconcile.
8. **Telemetry staleness flag truthful** (FR-021): `telemetryStale = true`
   iff `(now - lastSnapshotAt) > staleThreshold`.

---

## 4. Mapping to wire (`.proto`)

The wire types in `src/Broker.Contracts/` are NOT identical to the F#
records above — they are wire-format optimised. The conversion layer lives
in `Broker.Protocol` and is small enough to test exhaustively:

| F# entity | Wire message (`common.proto`) |
|-----------|-------------------------------|
| `GameStateSnapshot` | `GameStateSnapshot` |
| `Command` | `Command` (oneof on `CommandKind`) |
| `RejectReason` | `Reject` (oneof on reason) |
| `Version` | `ProtocolVersion` |
| `ScriptingClientId` | `string` (in `Handshake.client_name`) |
| `EndReason` | `SessionEnd.Reason` (enum) |

The `.proto` definitions are authored in `specs/001-tui-grpc-broker/contracts/`
(reviewed alongside this plan) and copied / referenced from
`src/Broker.Contracts/` for the build.
