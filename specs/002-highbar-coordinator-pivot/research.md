# Phase 0 Research: Broker–HighBarCoordinator Wire Pivot

**Feature**: 002-highbar-coordinator-pivot
**Date**: 2026-04-28
**Status**: Complete — all `NEEDS CLARIFICATION` resolved

This document records the technical decisions for the wire pivot, with
rationale and considered alternatives. It is the authoritative reference
for the plan's Technical Context section. The 001 research stays in
force for everything not contradicted here (gRPC stack, F# codegen,
TUI library, structured logging, test framework, viz, storage).

---

## 1. Vendoring strategy for the upstream HighBarV3 proto set

- **Decision**: Copy five proto files verbatim from
  `EHotwagner/HighBarV3@66483515` into
  `src/Broker.Contracts/highbar/`:
  `coordinator.proto`, `state.proto`, `commands.proto`, `events.proto`,
  `common.proto`. Track the upstream commit SHA and per-file blob SHAs
  in `contracts/highbar-proto-pin.md` and a mirrored
  `src/Broker.Contracts/HIGHBAR_PROTO_PIN.md`. Add the
  `highbar/` subdirectory as a `<Protobuf>` include path in
  `Broker.Contracts.fsproj` so `FSharp.GrpcCodeGenerator` produces
  the `Highbar.V1.*` namespace alongside the existing
  `FSBarV2.Broker.Contracts` namespace.
- **Rationale**:
  - HighBarV3 does not currently publish a buf module or NuGet package
    for its proto set; vendoring is the simplest path that still gives
    a SHA-auditable record of the contract version we're against.
  - Per spec Assumptions, the HighBarV3 proto is treated as a stable
    upstream contract for this feature's lifetime; drift is a separate
    pin-and-update workflow. A vendored directory + pin manifest
    enforces exactly that boundary.
  - F# codegen via `FSharp.GrpcCodeGenerator` already handles multi-
    package proto sets (the existing `Broker.Contracts` project mixes
    three `.proto` files under `fsbar.broker.v1`). Adding a second
    package (`highbar.v1`) is mechanical.
- **Files NOT vendored**: `service.proto` (HighBarProxy/HighBarAdmin —
  the plugin-hosted embedded gateway, explicitly out of scope per
  spec Out of Scope) and `callbacks.proto` (only used from
  `service.proto`).
- **Alternatives considered**:
  - Reference upstream as a git submodule under `vendor/HighBarV3` —
    rejected: heavier than necessary, and submodules tend to get
    out-of-sync with the main checkout without anyone noticing.
  - Wait for upstream to publish a buf module — rejected: blocks the
    feature on a separate workstream we don't own.
  - Hand-author a parallel "broker.proxy.v2" proto that mirrors what
    we need from HighBar and translate at the wire — rejected: this
    is exactly the parallel-schema mistake feature 001 made; the
    point of this feature is to retire that pattern.

## 2. Wire-shape mapping: HighBar `StateUpdate` → broker `Snapshot`

The upstream `highbar.v1.StateUpdate` is a snapshot/delta envelope; the
broker's internal `Snapshot.GameStateSnapshot` (from 001 data-model
§1.9) is a flat, snapshot-only model. Translation rules:

| HighBar wire field | Broker `GameStateSnapshot` field | Notes |
|--------------------|----------------------------------|-------|
| `StateUpdate.frame` | `tick: int64` | Cast `uint32 → int64`. Strictly increasing per session per data-model invariant 5. |
| `StateUpdate.send_monotonic_ns` (informational only) | not stored | Optional plugin-side latency probe; broker logs at debug level. |
| `StateUpdate.payload = StateSnapshot` (full snapshot path) | overwrites `players`, `units`, `buildings`, `mapMeta` | Snapshot path produces a complete frame. |
| `StateUpdate.payload = StateDelta` (incremental events path) | folded into the running broker-side snapshot | The broker maintains a per-session reduction of `OwnUnit` / `EnemyUnit` from the most recent `StateSnapshot`, applies each `DeltaEvent` (UnitCreated, UnitDestroyed, UnitDamaged, etc.) into that running view, and emits a fresh broker snapshot to subscribers on each accepted delta — same gap-free guarantee 001 already enforces. |
| `StateUpdate.payload = KeepAlive` | no-op for the snapshot stream; refreshes `ProxyAiLink.lastHeartbeatAt` | KeepAlive is the plugin's "I'm idle but alive" beacon — distinct from the unary `Heartbeat` RPC. |
| `StateSnapshot.own_units` + `visible_enemies` (+ `radar_enemies`) | broker `units: Unit list` | Each maps to a broker `Unit` with `id`/`classId` (from `def_id`)/`ownerPlayerId` (from `team_id`)/`pos` (from `Vector3.x,y` — z-axis dropped, broker is 2D). |
| `StateSnapshot.map_features` | broker `buildings: Building list` (best-fit) OR a new `Snapshot.Feature` list | **Decision**: extend `Snapshot.GameStateSnapshot` with a `features` list rather than overload `buildings`. Features are reclaim points, not buildings. |
| `StateSnapshot.economy` (`TeamEconomy`) | broker `players[i].resources` + `players[i].economy` | Single team's economy maps to the per-player telemetry of the owner team; other players' telemetry is reconstructed from delta events when present, or left empty. |
| `StateSnapshot.static_map` (`StaticMap`) | broker `mapMeta` | `width_cells`/`height_cells` → `MapMeta.size`. `metal_spots`/`start_positions`/`heightmap` not surfaced today; available via the new `features` list when needed. |

- **Rationale**:
  - The broker only needs the subset that drives its dashboard and
    the scripting-client snapshot fan-out — full HighBar fidelity is
    not required.
  - The delta-aware reduction is the right place to put the gap-
    detection logic for FR-013: the broker observes
    `StateUpdate.seq`, fails the running view if a gap appears, and
    surfaces the gap through both the audit sink and the dashboard
    instead of advancing tick.
- **Alternatives considered**:
  - Force the plugin to send `StateSnapshot` every frame (no deltas) —
    rejected: contradicts upstream's "snapshot once, deltas thereafter"
    flow; would also produce far more wire bytes per frame.
  - Pass HighBar `StateUpdate` through the broker unchanged —
    rejected: the scripting client wire is unchanged (FR-007); a
    translation step is mandatory.

## 3. Wire-shape mapping: broker `Command` → HighBar `CommandBatch`

The upstream `highbar.v1.CommandBatch` carries a list of `AICommand`
oneof variants targeting one unit. The broker's internal `Command`
record (data-model 1.10) carries either `Gameplay` or `Admin` payloads.

**Gameplay translation** — all gameplay variants land:

| Broker `GameplayPayload` | HighBar `AICommand` arm |
|--------------------------|-------------------------|
| `UnitOrder { kind = MOVE; ... }` | `MoveUnitCommand` |
| `UnitOrder { kind = ATTACK; targetUnitId = Some _ }` | `AttackCommand` |
| `UnitOrder { kind = ATTACK; targetUnitId = None; targetPos }` | `AttackAreaCommand` (radius derived from broker config) |
| `UnitOrder { kind = STOP }` | `StopCommand` |
| `UnitOrder { kind = GUARD }` | `GuardCommand` |
| `UnitOrder { kind = PATROL }` | `PatrolCommand` |
| `Build { builderId; classId; pos }` | `BuildUnitCommand` |
| `Custom { name; blob }` | `AICommand.custom = CustomCommand` (params decoded from blob) |

**Admin translation gap** — admin commands have no AICommand
equivalent. Admin overrides land on the upstream `HighBarAdmin`
service, which the plugin hosts and which is **out of scope** per
spec Out of Scope ("Bridging the plugin's separate `HighBarProxy` /
`HighBarAdmin` embedded gateway. The broker only consumes the
coordinator-side contract").

- **Decision**: Map admin commands to AICommand arms only where a
  plausible mapping exists (`Pause`/`Resume` → `PauseTeamCommand` for
  the host's team; `GrantResources` → `GiveMeCommand` when cheats are
  enabled). For admin commands with no AICommand mapping (`SetSpeed`
  global, `OverrideVision`, `OverrideVictory`), the broker rejects
  the command at the boundary with `RejectReason.AdminNotAvailable`
  (already present in the union), audit-logs the rejection with the
  reason "no coordinator-side mapping; awaiting future HighBarAdmin
  bridge", and the dashboard shows the operator-issued command was
  refused with that explanation.
- **Rationale**:
  - The wire pivot's purpose is gameplay-and-state end-to-end, not
    admin parity. Spec User Story 1 is the load-bearing P1; User
    Story 2 mentions admin-and-gameplay symmetrically but its
    acceptance scenario #1 (`Pause` from TUI) lands on a coordinator-
    routable command (`PauseTeamCommand` is per-team).
  - This keeps the broker honest: admin operations that genuinely
    cannot reach the engine over this wire are rejected at the
    broker boundary with a clear reason rather than queued and
    silently dropped at the wire edge. No silent failure
    (Constitution Principle VI).
  - The "future HighBarAdmin bridge" gap is documented in §10 as
    an explicit follow-up.
- **Alternatives considered**:
  - Block the entire admin command surface in this feature —
    rejected: regresses the operator UX from 001 with no upside;
    `Pause`/`Resume`/`GrantResources` work today via a synthetic
    proxy, and the team-scoped AICommand variants make a real
    coordinator path possible.
  - Add a HighBarAdmin client to the broker as part of this feature —
    rejected: out of scope per spec; doubles the contract surface;
    the spec explicitly carves it out.

## 4. Disconnect detection: heartbeat-driven, not gRPC keepalive

001 used a 2-second gRPC keepalive + 1-missed = ≤4 s detection
window (data-model 1.7 `keepAliveIntervalMs`). The HighBar coordinator
contract uses an explicit unary `Heartbeat` RPC the plugin issues
every N frames (per `coordinator.proto` comments).

- **Decision**: The broker tracks `ProxyAiLink.lastHeartbeatAt` (set
  by every successful `Heartbeat` RPC and refreshed by every accepted
  `StateUpdate`) and times out the link after a configurable
  `heartbeatTimeoutMs` (default 5000 ms) of silence. On timeout, the
  service force-closes the open `PushState` and `OpenCommandChannel`
  streams, calls `BrokerState.closeSession ProxyDisconnected`, and
  the existing fan-out path fires `SessionEnd` to subscribers.
- **Rationale**:
  - SC-003 budgets ≤5 s detection of plugin loss, ≤10 s recovery.
    A 5 s heartbeat timeout matches that with the existing fan-out
    overhead within budget.
  - The heartbeat is a **plugin-application-level** signal, which
    survives gRPC transport quirks (e.g., HTTP/2 PINGs not arriving
    when the engine thread is blocked) better than transport
    keepalive.
- **Alternatives considered**:
  - Keep the 001 gRPC-keepalive timer — rejected: ignores the
    upstream contract's heartbeat semantics; unnecessary divergence.
  - Stack both (heartbeat + keepalive, OR-of-timers) — rejected:
    extra complexity for no measurable benefit; the heartbeat is
    sufficient on its own.

## 5. Schema-version handshake: string strict-equality

001 used `ProtocolVersion {major, minor}` with major-strict matching
(FR-029). The HighBar contract uses a `string schema_version` field
(currently `"1.0.0"`) compared by **strict equality** per the upstream
`SchemaVersion.h` rule.

- **Decision**: The broker holds a compile-time string constant
  `Broker.Protocol.HighBarSchemaVersion.expected = "1.0.0"` (mirrors
  upstream `SCHEMA_VERSION`). Every `Heartbeat` (and every
  `CommandChannelSubscribe` from the plugin) is checked for strict
  equality on the field; mismatch is a `RejectReason.SchemaMismatch`
  audit-logged with both the broker's and the plugin's version
  strings. The broker's version string is also surfaced on the
  dashboard footer (FR-014) and via `--print-schema-version` CLI.
- **Rationale**:
  - Matching upstream's existing comparison rule is the only way to
    guarantee byte-compatibility; semver-aware comparison would
    accept versions upstream considers incompatible.
  - The string is set at the build edge (compile-time `const`),
    so no runtime input affects it. Diagnostics are exhaustive
    (audit + dashboard + CLI) when drift surfaces.
- **Alternatives considered**:
  - Map the string into a major-minor tuple and reuse the 001
    `ProtocolVersion` machinery — rejected: invents a transformation
    whose only purpose is to reuse code; risk of introducing skew
    between broker and plugin interpretations.

## 6. Owner-skirmish-AI ID enforcement

The plugin documentation specifies a `HIGHBAR_COORDINATOR_OWNER_SKIRMISH_AI_ID`
environment variable convention: in a multi-AI match, only the AI whose
skirmish-AI ID matches the env var dials out. The broker must reject
non-owner attempts (FR-011 + Edge Cases).

- **Decision**: The broker accepts the **first** Heartbeat that
  passes schema-version check as the session owner; subsequent
  Heartbeat RPCs from a *different* `plugin_id` are rejected with
  `RejectReason.NotOwner` and audit-logged. The `plugin_id` field
  serves as the broker's owner key (the env-var convention is the
  plugin side's contract; the broker just sees the ID the plugin
  sent).
- **Rationale**:
  - The broker can't read the plugin-side env var. The honest
    enforcement primitive on the broker side is "first attached
    plugin_id is the owner; no second plugin can attach until the
    session ends".
  - This composes correctly with the single-session-per-broker
    invariant from 001 — there's only ever one owner per session.
- **Alternatives considered**:
  - Operator-driven owner-AI selection from the TUI — rejected:
    adds a UX wart for a constraint that's already settled by
    "first connection wins" given the upstream env-var convention.
  - Whitelist of allowed plugin_ids via CLI flag — rejected: no
    operational requirement asks for it; can be added later if
    the threat model grows.

## 7. Plugin-side PushState backpressure (256-deep, drop-oldest)

Per `CoordinatorClient.h` the plugin's outgoing PushState queue is
256-deep with drop-oldest semantics — an overloaded plugin loses old
state frames before it loses connection.

- **Decision**: The broker observes `StateUpdate.seq` (the plugin's
  monotonic counter) on every accepted state message. A gap (`seq`
  jump > 1) is emitted as a `Snapshot.Gap` audit event and surfaced
  on the dashboard with a stale-tick badge (FR-013), but the running
  reduction continues from the new seq without rolling back. The
  scripting-client fan-out also receives a corresponding gap
  indication so downstream clients can decide their own reaction.
- **Rationale**:
  - Silently advancing the displayed tick is the failure mode FR-013
    forbids; explicit gap surfacing is the correct response.
  - Rolling back the running view on a gap would invent state
    (the plugin has dropped frames the broker can never recover);
    advancing with a labelled gap is honest.
- **Alternatives considered**:
  - Force the plugin to slow down via flow control — rejected: the
    upstream plugin's queue is drop-oldest by design; HTTP/2 flow
    control on the broker side wouldn't reach it before the queue
    head was already discarded plugin-side.

## 8. Service hosting: same Kestrel listener as 001

- **Decision**: The new `HighBarCoordinatorService.Impl` is registered
  via `app.MapGrpcService<HighBarCoordinatorService.Impl>()` on the
  existing `Broker.Protocol.ServerHost` Kestrel listener. The
  `ScriptingClient` service registration is unchanged. One port,
  two services — same shape as 001.
- **Rationale**:
  - 001 FR-005 / SC-001 are normative; the wire pivot must preserve
    them (spec FR-010, SC-006).
  - `Grpc.AspNetCore.Server` accepts multiple `MapGrpcService` calls
    on one host without issue.
- **Alternatives considered**: None — non-negotiable per 001 and
  spec FR-010.

## 9. Test substitution strategy

001 shipped a `SyntheticProxy` loopback fixture under
`tests/Broker.Integration.Tests/SyntheticProxy.fs` that drove the
`ProxyLink.Attach` bidi stream on CI. That fixture retires with the
`ProxyLink` service.

- **Decision**: Replace it with a `SyntheticCoordinator` fixture in
  the same directory (`tests/Broker.Integration.Tests/SyntheticCoordinator.fs`)
  that drives the new `HighBarCoordinatorService` end-to-end on
  loopback gRPC. Carries identical disclosure scaffolding (file-
  level `(* SYNTHETIC FIXTURE *)` banner; `Synthetic_` test names;
  `[S]` markers in the migrated test tasks). The four 001 carve-out
  tasks (T029/T037/T042/T046) are not "fixed by replacing the
  fixture" — they are closed by **operator-driven real-game
  walkthroughs** (Story 3) under `readiness/`.
- **Rationale**:
  - CI can't run a real BAR + HighBarV3 build (binary not
    provisioned, GL context unavailable in headless containers).
    A synthetic fixture is still required for the broker-side wire
    code to have automated coverage.
  - Story 3 explicitly demands the readiness artifacts be regenerated
    against real-wire numbers; the synthetic fixture is the CI
    safety net, not the closure evidence.
- **Alternatives considered**:
  - Drop CI integration tests entirely — rejected: regresses
    coverage; a synthetic fixture exists precisely to avoid that.
  - Pin a recorded real-game session as a fixture replay —
    rejected: large binary blobs in the repo; no value over a
    deterministic synthetic emitter.

## 10. Open follow-ups (out of scope for this feature)

- **Broker-side `HighBarAdmin` client**. Bridges true admin
  commands (`SetSpeed`, `OverrideVision`, `OverrideVictory`) to the
  plugin's embedded gateway. Documented as a separate follow-up
  feature; until then, those commands reject at the broker boundary
  with `AdminNotAvailable`.
- **Game-process management against a real BAR engine** (001's
  T035 carve-out). Not closed by this feature; remains tracked.
- **Non-loopback coordinator endpoints** (auth, encryption). Per
  spec Assumptions, loopback-only stays for now.
- **Multi-AI broker** (one broker, multiple coordinator sessions).
  Carried forward from 001's §14 as out of scope.

---

**All `NEEDS CLARIFICATION` placeholders from the Technical Context are
resolved by the decisions above.**
