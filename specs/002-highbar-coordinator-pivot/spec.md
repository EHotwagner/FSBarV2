# Feature Specification: Broker–HighBarCoordinator Wire Pivot

**Feature Branch**: `002-highbar-coordinator-pivot`
**Created**: 2026-04-28
**Status**: Draft
**Input**: User description: "create specs for the reproto/refit work"

## Background *(non-template — context required)*

The shipped broker (feature 001-tui-grpc-broker, merged at `cf0b54b`)
hosts a parallel proxy-side wire contract — `fsbar.broker.v1.ProxyLink`
— that no proxy AI implements. Feature 001's `research.md §7` deferred
proxy-side compatibility to "the proxy AI workstream", but the
HighBarV3 plugin already ships a published proxy-side contract,
`highbar.v1.HighBarCoordinator`, with a working client implementation
in `src/circuit/grpc/CoordinatorClient.cpp`. The plugin dials any
configured endpoint via the `HIGHBAR_COORDINATOR` environment variable
and pushes state via client-streaming `PushState`, pulls commands via
server-streaming `OpenCommandChannel`, and heartbeats via unary
`Heartbeat`.

This feature retires the broker's parallel `ProxyLink` schema and makes
the broker host the matching `HighBarCoordinator` server. Once the
pivot lands, a real BAR + HighBarV3 session can drive the broker over
the wire — clearing four of the five synthetic-evidence carve-outs that
shipped with 001 (T029, T037, T042, T046).

This feature is **strictly the wire pivot**. Host-mode game-process
launching against a real BAR engine (T035's gap) is out of scope and
left as a future feature.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Broker consumes live state from a running BAR session over real wire (Priority: P1)

An operator launches a BAR + HighBarV3 game with the
`HIGHBAR_COORDINATOR` environment variable pointed at a running broker.
The plugin dials the broker, completes a heartbeat handshake, opens its
state stream, and starts pushing real game frames. The broker's
dashboard shows live data — map, players, units, buildings — fed by
real game ticks rather than the loopback synthetic fixture. Subscribed
scripting clients see the same frames fanned out without code changes.

**Why this priority**: This is the entire reason the feature exists.
Without it, the broker remains an internally-consistent system that
can't talk to its declared target. Every other story depends on it.

**Independent Test**: Boot the broker on a host:port the user has
write access to. Launch BAR with the proxy AI plugin and
`HIGHBAR_COORDINATOR` pointing at that endpoint. Observe live game
state on the broker's dashboard within seconds of the first game tick,
and observe the same state arriving at any connected scripting client.

**Acceptance Scenarios**:

1. **Given** the broker is running and idle, **When** a BAR session
   with the HighBarV3 plugin starts with `HIGHBAR_COORDINATOR` pointed
   at the broker, **Then** the broker's dashboard transitions from
   Idle to an attached session within 10 seconds and the player /
   unit / building counts begin updating.
2. **Given** the broker is running an attached session and a scripting
   client is subscribed, **When** the game advances by one tick,
   **Then** the scripting client receives a corresponding state frame
   within 1 second.
3. **Given** the broker is running an attached session, **When** the
   plugin sends a heartbeat with a `schema_version` that does not
   match the broker's expected constant, **Then** the broker rejects
   the connection with a clear schema-mismatch indication, audit-logs
   the rejection, and remains ready to accept a future connection.
4. **Given** the broker is running an attached session, **When** the
   game ends or the plugin disconnects, **Then** the broker fans out a
   session-end indication to all subscribed scripting clients, returns
   to Idle, and remains ready to accept a new session — within
   10 seconds of the disconnect.

---

### User Story 2 - Operator commands and scripting-client commands flow back to the game (Priority: P1)

The broker pushes operator-issued admin commands (pause, set speed,
grant resources, …) and scripting-client gameplay commands back through
the same coordinator connection to the plugin, and the plugin applies
them in-engine. The dashboard reflects the result in the next snapshot
(speed change, pause indicator, resource delta).

**Why this priority**: Symmetric with Story 1 — without command egress,
the broker is read-only. Both directions must work for the broker to
be useful.

**Independent Test**: With an attached session under host mode (or
guest mode for gameplay-only commands), issue a `Pause` command from
the TUI. Observe the next inbound snapshot showing the paused state,
and observe an audit log line for the dispatched command.

**Acceptance Scenarios**:

1. **Given** an attached host-mode session, **When** the operator
   issues `Pause` from the TUI, **Then** the next inbound snapshot
   shows the paused state and the broker logs the dispatched command
   with its identifier.
2. **Given** an attached session and a scripting client with admin
   elevation, **When** the scripting client submits a gameplay
   command (e.g., move a unit), **Then** the command is dispatched to
   the plugin and the affected unit's position changes in a subsequent
   snapshot.
3. **Given** an attached session, **When** a scripting client submits
   commands faster than the plugin can drain, **Then** the broker
   applies bounded backpressure exactly as it does today (per FR-010
   from feature 001) — no silent drops, `QUEUE_FULL` rejects on
   overflow.

---

### User Story 3 - Synthetic-evidence carve-outs from feature 001 close against real game (Priority: P1)

Tasks T029 / T037 / T042 / T046 from feature 001 — currently `[S]`
because their evidence relied on the loopback `SyntheticProxy` — are
re-run against a real BAR + HighBarV3 session and the readiness
artifacts (`readiness/sc003-latency.md`, `readiness/sc005-recovery.md`,
`readiness/us3-evidence.md`, `readiness/us4-evidence.md`) are
regenerated against real-wire numbers.

**Why this priority**: The synthetic-evidence regime exists so that
deferred real-evidence runs are tracked and eventually closed. Feature
001 explicitly accepted these carve-outs as documented gaps; this is
the feature where they close.

**Independent Test**: Re-run the SC-003 latency capture, the SC-005
recovery test, the US3 dashboard load run, and the US4 viz screenshot
capture against a real BAR session. Confirm each artifact under
`readiness/` is regenerated with real-wire data and the
`Synthetic-Evidence Inventory` entry for those four tasks moves from
"infeasible without proxy AI" to "closed; live evidence captured".

**Acceptance Scenarios**:

1. **Given** the wire pivot is complete, **When** the SC-003 latency
   harness runs against a real plugin-driven session of ≥500 ticks,
   **Then** the p95 game-tick → scripting-client receipt remains
   ≤1 s and the artifact records "real plugin peer".
2. **Given** the wire pivot is complete, **When** SC-005 disconnect
   recovery is exercised over ≥20 trials with the real plugin process
   killed mid-stream, **Then** detection-to-`SessionEnd` ≤5 s and
   detection-to-Idle ≤10 s in ≥95 % of trials.

---

### User Story 4 - The retired ProxyLink surface disappears cleanly (Priority: P2)

The `fsbar.broker.v1.ProxyLink` proto and its F# implementation
(`Broker.Protocol.ProxyLinkService`) are removed from the public
surface. Surface-area baselines under `tests/SurfaceArea/baselines/`
reflect the removal. The `Broker.Contracts` package no longer exports
the `ProxyLink`-side message envelopes (`ProxyClientMsg`,
`ProxyServerMsg`) that exist only for that schema.

**Why this priority**: Tier 1 obligation — public-surface diff control
prevents a parallel-schema mistake from drifting back in. Lower
priority than P1 because a brief overlap is acceptable while the
pivot is bedding in.

**Independent Test**: Run the SurfaceArea diff suite. Confirm no
`ProxyLink`-side baselines remain. Confirm the new
`HighBarCoordinator`-side baselines exist and pass.

**Acceptance Scenarios**:

1. **Given** the pivot is complete, **When** the SurfaceArea suite
   runs, **Then** `Broker.Protocol.ProxyLinkService.surface.txt` is
   removed and a corresponding coordinator-service baseline is added
   and passes.
2. **Given** a downstream caller still imports the old `ProxyLink`
   types, **When** they update to the post-pivot package version,
   **Then** the compilation error names the removed type — no soft
   "still works but deprecated" carry-over.

---

### Edge Cases

- The plugin connects but never sends a `Heartbeat` — the broker times
  out the connection rather than waiting indefinitely.
- The plugin's `schema_version` differs from the broker's expected
  constant — broker rejects with a clear reason and audit-logs both
  the plugin's and broker's versions.
- Two plugin instances try to connect simultaneously to the same
  broker (e.g., multiple AI roles per match) — the broker honors the
  `HIGHBAR_COORDINATOR_OWNER_SKIRMISH_AI_ID` convention and refuses
  any non-owner connection with a clear reason.
- The plugin's PushState queue overflows on its own side (256-deep,
  drop-oldest per `CoordinatorClient.h`) — broker observes a gap in
  the snapshot sequence and surfaces it to the dashboard / audit
  rather than silently advancing the displayed tick.
- The broker disconnects the plugin mid-game (broker shutdown,
  operator-initiated end-of-session) — plugin-side `CoordinatorClient`
  observes RPC error and the BAR engine continues without the
  coordinator (degraded mode, plugin-side concern).
- A scripting client subscribes before any plugin has connected —
  broker reports the session as Idle and starts feeding frames the
  moment the plugin attaches; no synthetic placeholder data emitted.
- A second BAR game starts in the same broker process after the first
  ends — broker accepts the new coordinator session cleanly, with a
  fresh session identifier and a clean dashboard.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: System MUST host a gRPC service the HighBarV3 plugin can
  dial via the `HIGHBAR_COORDINATOR` environment variable, accepting
  gRPC URIs in both `unix:/path` and `host:port` forms (the URI shapes
  the plugin's `CoordinatorClient.cpp` already supports).
- **FR-002**: System MUST accept the three RPCs the plugin drives —
  unary `Heartbeat`, client-streaming `PushState`, server-streaming
  `OpenCommandChannel` — over a single gRPC channel per attached
  session.
- **FR-003**: System MUST validate the plugin's `schema_version` field
  against a broker-side expected constant (strict equality, per the
  contract's published rule), reject mismatches with a clear reason,
  and audit-log both versions on rejection.
- **FR-004**: System MUST translate inbound HighBar-shaped state
  updates into the broker's existing snapshot model used by the
  dashboard, viz, and ScriptingClient fan-out.
- **FR-005**: System MUST translate broker-internal commands (gameplay
  + admin) into HighBar-shaped command batches on the
  `OpenCommandChannel` reverse stream.
- **FR-006**: System MUST drop the previous `fsbar.broker.v1.ProxyLink`
  service from its public surface — the proto file, the F# service
  implementation, the surface-area baseline, and the
  `ProxyClientMsg` / `ProxyServerMsg` envelope types that exist only
  for that schema.
- **FR-007**: System's `ScriptingClient` surface (proto + F#) MUST
  remain unchanged in this feature. Existing scripting clients MUST
  continue to work without code changes.
- **FR-008**: System MUST detect plugin disconnection via heartbeat
  timeout, stream close (graceful), or stream error (unexpected), fan
  out a session-end indication to all subscribed scripting clients,
  return to Idle, and accept a future connection — all within 10
  seconds of the disconnect being detectable.
- **FR-009**: System MUST log lifecycle events on the coordinator
  wire — plugin attached, plugin detached (with reason), schema
  mismatch, heartbeat received, command channel opened, command
  channel closed — to the existing audit sink with the existing event
  envelope.
- **FR-010**: System MUST host the coordinator listener on the same
  gRPC server / single listening port as the existing `ScriptingClient`
  service. Distinct services on a single port — preserves feature 001
  FR-005.
- **FR-011**: System MUST honor the
  `HIGHBAR_COORDINATOR_OWNER_SKIRMISH_AI_ID` convention from the
  HighBarV3 contract — if multiple plugin instances are present in a
  single match, only the owner instance is expected to dial out, and
  the broker MUST reject any non-owner attempt with a clear reason.
- **FR-012**: System MUST handle multiple consecutive coordinator
  sessions across a single broker-process lifetime — each session has
  a distinct identifier, fresh dashboard state, and clean audit trail.
- **FR-013**: System MUST surface plugin-side PushState backpressure
  drops as a gap indication on the dashboard and an audit event,
  rather than silently advancing the displayed tick.
- **FR-014**: System MUST advertise its own broker-side schema version
  — readable from the dashboard or a CLI flag — for diagnostic
  alignment when the plugin's version drifts.
- **FR-015**: Scripting clients connected before the coordinator
  attaches MUST be subscribed correctly when state begins flowing —
  no missed initial snapshot, no synthetic placeholder frames.

### Key Entities

- **Coordinator session**: The single live attachment between a BAR /
  HighBarV3 plugin and the broker, scoped to one game. Begins with
  the plugin's first successful Heartbeat, ends with disconnect.
- **State update**: An inbound game-frame message from the plugin,
  translated into the broker's internal snapshot used by the rest of
  the system.
- **Command batch**: An outbound message envelope from the broker to
  the plugin, carrying gameplay or admin commands the plugin will
  apply on the next engine tick.
- **Heartbeat exchange**: A unary RPC the plugin issues every N frames
  to prove the coordinator wire is alive; carries `schema_version`,
  the plugin identity, and the current engine frame number.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: An operator can launch a BAR + HighBarV3 game with the
  broker as coordinator and observe live game state on the dashboard
  within 10 seconds of the first engine tick (cold-start budget).
- **SC-002**: Game-tick → scripting-client-receipt p95 latency is
  ≤1 second over a 500-tick window of real game data (re-anchors
  feature 001 SC-003 on real wire).
- **SC-003**: Disconnect-recovery — when the plugin process is killed
  mid-stream, the broker detects the loss, fans out session-end to
  scripting clients, and returns to Idle in ≤10 seconds wall-clock in
  ≥95 % of trials over ≥20 trials (re-anchors feature 001 SC-005).
- **SC-004**: With ≥4 scripting clients subscribed and a real game
  producing ≥200 units, the dashboard refreshes at ≥1 Hz (re-anchors
  feature 001 SC-006).
- **SC-005**: Synthetic-Evidence Inventory entries for tasks T029,
  T037, T042, T046 of feature 001 transition from "open carve-out" to
  "closed; live evidence captured" with corresponding artifacts under
  `readiness/`.
- **SC-006**: Public-surface diff after the pivot adds the coordinator
  service, removes the `ProxyLink` service, and otherwise leaves the
  `ScriptingClient` surface unchanged byte-for-byte. Existing
  scripting-client packages built against the previous broker surface
  continue to work.
- **SC-007**: A schema-version mismatch is detected at the first
  Heartbeat — operator sees a clear error in the dashboard and audit
  log within 1 second of the rejected RPC.

## Assumptions

- The HighBarV3 plugin's `highbar.v1.HighBarCoordinator` proto is
  treated as a stable upstream contract for this feature's lifetime.
  Drift in HighBarV3's contract is handled by a separate broker-side
  pin-and-update workflow, not by this feature.
- The BAR engine binary the operator uses to run the game is whatever
  the operator's BAR install ships. The broker does not spawn the
  engine in this feature — the operator (or the lobby client, or a
  manual `HIGHBAR_COORDINATOR=… spring-headless …` invocation) starts
  BAR. Host-mode game-process management against a real engine
  (feature 001 task T035) remains a separate future feature.
- The `ScriptingClient` proto, its F# surface, and its existing tests
  are unchanged by this feature. Feature 001 invariants and FRs
  governing the `ScriptingClient` side remain in force.
- The legacy `ProxyLink` proto and its F# implementation are removed
  outright in this feature — no deprecation period, no compatibility
  shim. The 001 broker shipped with `ProxyLink` as a published surface
  but no live consumers (the `SyntheticProxy` was a test fixture, not
  a deployed peer), so a clean break is acceptable.
- The synthetic-evidence carve-outs from feature 001 that this feature
  closes are T029, T037, T042, T046. Task T035 (game-process
  management) is **not** closed by this feature; it remains a tracked
  carve-out to be addressed separately.
- The `HIGHBAR_COORDINATOR` env var is the operator's contract for
  pointing the plugin at the broker. The broker does not expect any
  alternate discovery mechanism (mDNS, well-known port, config file
  in BAR's data dir) in this feature.
- Auth on the coordinator wire is loopback-only — same scope as
  feature 001's `research.md §11`. The broker does not require or
  validate the plugin's `ai_token`. If non-loopback coordinator
  endpoints are ever needed, that is a separate future feature.
- The broker continues to host both services on a single gRPC
  listening port, satisfying feature 001 FR-005. The pivot is a
  service-set change on the existing listener, not a new listener.

## Out of Scope

- Spawning the BAR engine binary from the broker (host mode's
  game-process gap; feature 001 task T035 remains carved out).
- Authentication / encryption on the coordinator wire beyond loopback
  containment.
- Bridging the plugin's separate `HighBarProxy` / `HighBarAdmin`
  embedded gateway. The broker only consumes the coordinator-side
  contract — the embedded gateway is for direct-to-plugin clients and
  is not the broker's concern.
- Backwards compatibility with the retired `ProxyLink` proto. Old
  consumers (none in production) must update.
- Changes to the `ScriptingClient` proto or F# surface.
- Lifting FSBarV1's engine-launch / script-generation code into
  feature 002. That work belongs to whichever future feature picks up
  T035.
