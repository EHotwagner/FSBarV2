# Feature Specification: TUI gRPC Game Broker

**Feature Branch**: `001-tui-grpc-broker`
**Created**: 2026-04-27
**Status**: Draft
**Input**: User description: "i want to create a new version of https://github.com/EHotwagner/FSBarV1_Archived for https://github.com/EHotwagner/HighBarV3 it should be a tui application hosting a grpc server. it should have an optional 2d visualization of the game. it should support connections of scripting clients that consume game state and produce commands. it needs to be able to handle 2 different scenarios, 1 where it initiates the whole topology - starts the game/headless or graphical with the proxy ai which connects to the app. it should have full admin rights in that case, speed, pause, cheating.... In the other case a game is opened by chobby lobby with a proxy ai placed that connects to the app. in that case no admin rights are available. in case 1 full lobby possibilities should be available, map/mode/number and kind of participants. there should be live diagnostic dashboard information available showing game/status. it should also show game information like resources/unit number/units/ buildings and relevant game statistics."

## Overview

A terminal-based broker application that sits between an external real-time strategy game (HighBarV3, the successor to FSBarV1) and one or more external scripting clients (typically AI bots, automation tools, or training agents). The broker exposes a gRPC interface that streams live game state to subscribed clients and accepts commands from them, while presenting a live operator dashboard in a text user interface (TUI). It supports two operating modes — one in which the broker orchestrates the whole game session itself (with full admin powers and lobby control), and one in which the broker simply attaches to a session that an external lobby client has already started (with read/observe rights and limited command privileges).

## Clarifications

### Session 2026-04-27

- Q: How should the broker handle scripting-client command rate exceeding game consumption? → A: Bounded backpressure with reject-on-overflow — per-client command queue with a finite maximum depth; gRPC flow control pauses reads from that client's stream when the queue is full; commands that arrive past the limit are rejected with a `QUEUE_FULL` status. Commands are never silently dropped.
- Q: How does the in-game proxy AI connect to the broker? → A: One gRPC server, two distinct gRPC services on the same listening port — `ProxyLink` for the in-game proxy AI (state ingest + command egress), `ScriptingClient` for external bots/tools. Roles are distinguished by service, not by token or tag.
- Q: How does a scripting client become privileged to issue admin commands (FR-016)? → A: Operator grants admin per scripting client from the TUI, scoped to the current broker process. New connections are non-admin by default; elevation and revocation are explicit operator actions, both audit-logged. No static tokens or slot-derived privilege.
- Q: How is a scripting client's stable identifier (FR-008) established? → A: Client-asserted on the gRPC handshake (non-empty name string); broker rejects new connections whose name collides with another currently-connected client (`NAME_IN_USE`). The asserted name is the canonical identifier for dashboard, logs, audit, and conflict resolution. Uniqueness is enforced only among currently-connected clients; names are not persisted across broker restarts.
- Q: How are protocol-version mismatches between the broker and its peers (proxy AI, scripting clients) handled? → A: Strict major-version match on handshake. Each gRPC service exchanges `MAJOR.MINOR` at connect time; the broker rejects peers whose major version differs from its own with a `VERSION_MISMATCH` error carrying the broker's advertised version. Minor-version skew is allowed.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Scripting client consumes live game state and issues commands in an externally hosted match (Priority: P1)

The most common path: a player launches a multiplayer match through their normal lobby client (Chobby), which places a designated proxy AI slot in the game. That proxy AI connects to the running broker. A scripting client (e.g., an AI bot the operator is developing) connects to the broker over gRPC, subscribes to the game-state stream, and submits commands that flow back through the proxy into the game. The operator watches the broker's TUI to confirm the link is healthy.

**Why this priority**: This is the minimum viable use case — it delivers the core value (a working bridge between a game session and a scripting client) without requiring the broker to also orchestrate game launch or hold admin rights. It is also the scenario most operators will encounter most often.

**Independent Test**: Start the broker, manually launch a HighBarV3 match through the lobby client with the proxy AI slot, connect a sample scripting client to the broker's gRPC endpoint, verify the client receives a state stream and that a submitted command (e.g., move a unit) takes effect in the game.

**Acceptance Scenarios**:

1. **Given** the broker is running with no game attached, **When** an externally launched game's proxy AI connects to it, **Then** the broker recognizes the connection and the TUI shows the session as "attached (guest mode)".
2. **Given** a guest-mode game session is attached, **When** a scripting client opens a gRPC subscription to game state, **Then** the client receives an initial snapshot followed by continuous state updates without missing intermediate frames.
3. **Given** a scripting client is subscribed to a guest-mode session, **When** the client issues a legal in-game command (e.g., issue an order to a unit it controls), **Then** the command is forwarded to the proxy AI and reflected in the game within one game tick under normal load.
4. **Given** a guest-mode session is active, **When** a scripting client issues an admin command (e.g., set game speed, pause, cheat), **Then** the broker rejects the command with a clear "admin not available in guest mode" error and the game state is unchanged.
5. **Given** the proxy AI disconnects mid-match (game ended, crash, network failure), **When** scripting clients are still subscribed, **Then** they receive a clear session-end notification and the TUI marks the session as "disconnected" with the reason.

---

### User Story 2 - Operator self-hosts a full match with admin controls (Priority: P2)

The operator wants to run controlled experiments or training sessions: pick a map, set a game mode, choose how many and what kind of participants (human players, built-in AIs, proxy-AI slots for scripting clients), then launch. Once the match is running, the operator has full admin authority — they can change game speed, pause/resume, and apply cheat-style overrides (resources, vision, victory conditions) from the TUI or via privileged scripting-client commands.

**Why this priority**: This unlocks the experiment/training workflow that the previous-generation tool (FSBarV1) supported, and is the second-most-common path. It is meaningfully larger in scope than P1 (lobby UI, game-process management, admin command surface), so it ships behind P1.

**Independent Test**: From the broker's TUI, configure a host-mode session (map, mode, two AI slots, one proxy-AI slot), launch it, watch the game start either headless or graphical per setting, attach a scripting client to the proxy slot, and exercise admin commands (set speed 2×, pause, grant resources) — verify each takes effect in the game.

**Acceptance Scenarios**:

1. **Given** the broker is idle, **When** the operator opens the host-mode lobby in the TUI, configures a map/mode/participant set, and confirms launch, **Then** the broker starts the game process (headless or graphical per the operator's choice), places the configured participants and proxy AI slots, and the TUI shows the session as "hosting (admin)".
2. **Given** a host-mode session is running, **When** the operator issues an admin command from the TUI (e.g., set game speed to 2×), **Then** the change is applied to the game and reflected in the dashboard within one second.
3. **Given** a host-mode session is running, **When** a scripting client connected to one of the proxy-AI slots issues an admin command (e.g., pause, grant resources), **Then** the command is accepted, applied, and confirmed back to the client.
4. **Given** a host-mode session is being configured, **When** the operator selects an invalid combination (e.g., more participants than the map supports, missing required slot), **Then** the lobby UI surfaces the specific problem and refuses launch until corrected.
5. **Given** a host-mode session ends (victory/defeat/operator-terminated), **When** cleanup runs, **Then** the game process is stopped, all scripting clients are notified of session end, and the broker returns to idle ready for a new session.

---

### User Story 3 - Live diagnostic dashboard with game telemetry (Priority: P2)

While any session is active (host or guest), the operator can read the TUI dashboard to see at a glance: broker health (gRPC server up, connected clients, version), session health (mode, attached or hosted, time elapsed, current speed, pause state), and live game telemetry per player or team (resources, unit count, building count, key unit-class breakdowns, headline statistics such as economy income, kills/losses, map control). The dashboard updates in real time without operator interaction.

**Why this priority**: Operators need this to know whether the broker is doing its job and to make in-session decisions, but the bridge in P1 is technically usable without it. Bundled at P2 because P2 (host mode) without a dashboard is impractical.

**Independent Test**: With any active session (P1 or P2), verify that the dashboard shows current resources, unit count, building count, and at least one statistical view per active player/team, that the values change as the game progresses, and that broker/server status indicators reflect actual connection state when a client connects or disconnects.

**Acceptance Scenarios**:

1. **Given** any active session, **When** the operator views the TUI dashboard, **Then** they see the current broker status (server state, connected scripting clients, mode), session status (host or guest, elapsed time, speed, pause), and per-player game telemetry (resources, unit count, building count, statistics).
2. **Given** the dashboard is visible during an active match, **When** a player's resources or unit count changes in-game, **Then** the dashboard reflects the new values within one second of the underlying game tick.
3. **Given** the dashboard is visible, **When** a scripting client connects or disconnects from the gRPC server, **Then** the connected-clients indicator updates immediately to reflect the new count and shows per-client identity.
4. **Given** the broker loses contact with the game (proxy AI disconnect, game crash), **When** the operator looks at the dashboard, **Then** the session status shows the disconnected state, the reason if known, and game telemetry is shown as stale rather than fabricated.

---

### User Story 4 - Optional 2D visualization of the game (Priority: P3)

The operator can optionally open a 2D top-down visualization of the live match — terrain/map outline, unit positions, building positions, ownership colors — alongside the TUI. The visualization is read-only (it does not accept input) and is provided so the operator can spot-check what scripting clients are doing without alt-tabbing into the game itself, especially when the game is running headless.

**Why this priority**: Useful but not required for the broker to function or for scripting clients to operate. Most useful in combination with headless host-mode runs. Ships last.

**Independent Test**: With an active session (any mode), enable the 2D visualization. Verify it shows the map and units in their correct positions, that ownership colors match the participant configuration, and that positions update as the game progresses.

**Acceptance Scenarios**:

1. **Given** an active session, **When** the operator enables the 2D visualization, **Then** a separate window opens showing the map outline, all units, and all buildings with correct positions and ownership colors.
2. **Given** the 2D visualization is open, **When** units move or are created/destroyed in-game, **Then** the visualization updates within one second to match.
3. **Given** the visualization is open, **When** the session ends or the operator closes the visualization, **Then** the window closes cleanly without affecting the broker, scripting clients, or game.
4. **Given** the broker is running on a system without a graphical environment (e.g., headless Linux server), **When** the operator attempts to open the 2D visualization, **Then** the broker reports the visualization is unavailable on this system rather than crashing.

---

### Edge Cases

- The proxy AI connects but never sends a recognizable handshake — the broker must time out and reject the connection rather than wait indefinitely.
- A scripting client sends commands faster than the game can consume them — the broker applies bounded backpressure (per FR-010): the per-client queue fills, gRPC flow control pauses reads, and any command that still arrives is rejected with `QUEUE_FULL`. Commands are never silently dropped.
- A scripting client disconnects mid-command — any partially submitted command is discarded; the game is not left in an inconsistent state.
- Two scripting clients attach to the same player slot in a host-mode session and both issue conflicting commands — the broker must define and enforce a deterministic ownership rule (one client per slot, or last-writer-wins, or first-writer-locks).
- The game crashes or is forcibly killed externally during a host-mode session — the broker must detect the process loss, notify clients, mark the session disconnected, and recover to idle without manual intervention.
- The operator attempts admin commands in guest mode (e.g., via a misconfigured client) — every such attempt is rejected with a clear error and logged, never partially applied.
- The TUI is resized to a very small terminal — the dashboard must degrade gracefully (hide or compress sections) rather than corrupt the display.
- A scripting client subscribes mid-match — it must receive a current full snapshot before incremental updates, so it does not act on partial state.
- The configured game executable for host mode is missing or incompatible — host mode launch must fail fast with a clear pointer to what is wrong, not hang.

## Requirements *(mandatory)*

### Functional Requirements

#### Operating Modes

- **FR-001**: System MUST support a "host mode" in which the broker launches and owns the game process, holds full admin authority over the running session, and can configure the lobby (map, mode, number and kind of participants).
- **FR-002**: System MUST support a "guest mode" in which the broker attaches to a game session that an external lobby client (Chobby) has already started, with no admin authority over the session.
- **FR-003**: System MUST detect its operating mode automatically based on whether it launched the game itself (host) or the proxy AI connected from an externally launched game (guest), and MUST display the active mode prominently in the TUI.
- **FR-004**: System MUST refuse any admin-class command in guest mode (game speed, pause/resume, resource grants, victory overrides, vision overrides, and any other state-mutating administrative override), with an explicit error identifying the command as admin-only.

#### gRPC Server and Scripting Clients

- **FR-005**: System MUST host a single gRPC server endpoint for the lifetime of the broker process, exposing two distinct gRPC services on the same listening port: a `ProxyLink` service for the in-game proxy AI (game-state ingest and command egress) and a `ScriptingClient` service for external scripting clients (state subscription and command submission). Role authority is determined by which service a peer connects to; a peer connecting to one service MUST NOT be able to use the other's RPCs.
- **FR-006**: System MUST allow one or more scripting clients to subscribe to a live game-state stream and MUST deliver to each subscriber a current full snapshot followed by continuous incremental updates without gaps.
- **FR-007**: System MUST accept in-session commands from connected scripting clients and forward them to the game via the proxy AI within one game tick under normal load.
- **FR-008**: System MUST identify each scripting client by a non-empty name string asserted by the client on the gRPC handshake; the asserted name is the canonical identifier shown in the dashboard, written to logs, attached to every command for audit, and used for conflict resolution. The broker MUST reject any new connection whose asserted name matches another currently-connected scripting client with a `NAME_IN_USE` error. Uniqueness is enforced only among currently-connected clients; names are not persisted across broker restarts.
- **FR-009**: System MUST define and enforce a single-writer rule per controllable participant slot — at most one scripting client may issue gameplay commands to a given proxy-AI slot at a time; subsequent attempts by other clients are rejected with a clear conflict error.
- **FR-010**: System MUST apply per-client bounded backpressure when scripting clients submit commands faster than the game can consume them: each client has a command queue with a finite maximum depth; when the queue is full the broker pauses reads from that client's stream via gRPC flow control; any command that nevertheless arrives past the limit MUST be rejected synchronously with a `QUEUE_FULL` status carrying the offending command's identifier. Commands MUST NEVER be silently dropped, evicted, or reordered.

#### Lobby and Session Control (Host Mode)

- **FR-011**: System MUST allow the operator, in host mode, to configure the map, game mode, and the number and kind of participants (human, built-in AI, proxy-AI slot) before launching the session.
- **FR-012**: System MUST allow the operator, in host mode, to choose between launching the game in headless (no graphical window) and graphical modes.
- **FR-013**: System MUST validate the lobby configuration before launch and refuse to launch with a clear, specific error when the configuration is invalid (e.g., participant count exceeds map capacity, missing proxy-AI slot referenced by a connected scripting client).
- **FR-014**: System MUST cleanly tear down host-mode sessions when the match ends (victory, defeat, operator termination, or game crash), notifying all scripting clients and returning to idle.

#### Admin Authority (Host Mode)

- **FR-015**: System MUST provide, in host mode, admin commands for at minimum: setting game speed, pausing and resuming, and applying cheat-class overrides (resource grants, vision/fog overrides, victory state).
- **FR-016**: System MUST allow admin commands to be issued from the TUI by the operator and from scripting clients that the operator has explicitly elevated to admin in the current broker process, with both paths producing identical effects and audit records. Scripting clients connect non-admin by default; the operator grants and revokes admin authority per client from the TUI as explicit, audit-logged actions. Admin grants do not survive broker restart and are not persisted to disk. An admin command from a non-elevated client MUST be rejected with the same `admin not available` error used for guest-mode rejections (FR-004) and MUST NOT be partially applied.

#### Diagnostic Dashboard

- **FR-017**: System MUST present a live operator dashboard in the TUI that updates without operator interaction.
- **FR-018**: Dashboard MUST show broker status: gRPC server state (up/down/listening address visibility), connected scripting clients (count, per-client identity, and per-client admin-elevation state), and broker version.
- **FR-019**: Dashboard MUST show session status: operating mode (host/guest/idle), attached/hosting state, elapsed match time, current game speed, and pause state.
- **FR-020**: Dashboard MUST show game telemetry per player or team: current resources, total unit count, total building count, breakdown by major unit class, and headline statistics including economy income/expenditure, kills, and losses.
- **FR-021**: Dashboard MUST visibly mark game telemetry as stale (rather than displaying last-known values as if current) when contact with the proxy AI is lost.

#### Optional 2D Visualization

- **FR-022**: System MUST optionally provide a 2D top-down visualization of the active session, opened on operator request.
- **FR-023**: 2D visualization MUST render the map outline, unit positions, building positions, and ownership colors matching the participant configuration, and MUST update to track in-game state within one second.
- **FR-024**: 2D visualization MUST be read-only — it MUST NOT accept gameplay input and MUST NOT influence the game.
- **FR-025**: System MUST gracefully report unavailability of the 2D visualization on environments without a graphical display, rather than crashing or blocking other broker functions.

#### Robustness

- **FR-026**: System MUST detect proxy-AI disconnection (graceful or unexpected), notify all subscribed scripting clients with a session-end indication and reason, and return to a state ready to accept a new session.
- **FR-027**: System MUST detect external termination of a host-mode game process and recover to idle without operator intervention.
- **FR-028**: System MUST log connection lifecycle events (proxy AI attach/detach, scripting client connect/disconnect, command rejections, mode transitions) with timestamps, sufficient for post-session diagnosis.

#### Protocol Versioning

- **FR-029**: Both gRPC services (`ProxyLink` and `ScriptingClient`) MUST exchange a `MAJOR.MINOR` protocol version on handshake. The broker MUST reject any peer whose major version differs from the broker's with a `VERSION_MISMATCH` error carrying the broker's advertised major and minor version, and MUST log the rejection. Minor-version skew between broker and peer MUST be allowed (peers and broker MUST be tolerant of unknown minor-version-added fields).

### Key Entities

- **Broker**: The TUI application instance itself — owns the gRPC server, the operator dashboard, and at most one active session at a time.
- **Session**: The live link between the broker and one running game. Has a mode (host or guest), a start time, a state (idle/configuring/launching/active/ended), and an associated proxy-AI link.
- **Lobby Configuration**: The pre-launch description of a host-mode session — map, game mode, ordered list of participants (human / built-in AI / proxy-AI slot), graphical-vs-headless choice. Only meaningful in host mode.
- **Participant Slot**: A position in a session that may be occupied by a human player, a built-in AI, or a proxy-AI slot bridged to a scripting client.
- **Proxy-AI Link**: The bidirectional channel between the broker and the in-game proxy AI agent — implemented as the broker's `ProxyLink` gRPC service. Carries game-state updates inbound and forwarded commands outbound. Distinct from the `ScriptingClient` service (which external bots use), but hosted on the same gRPC server and listening port.
- **Scripting Client**: An external process connected to the broker's `ScriptingClient` gRPC service that consumes game state and may produce gameplay or (in host mode, when the operator has elevated it) admin commands. Each asserts a non-empty name on the gRPC handshake which serves as its canonical identifier (unique among currently-connected clients), optionally a bound participant slot, and an admin-elevation flag toggled only by the operator from the TUI for the current broker process.
- **Game State Snapshot**: A point-in-time view of the active session — per-player resources, units (count, list, class breakdown), buildings (count, list), and key statistics (economy, kills, losses, map control). Streamed to scripting clients and rendered in the dashboard and visualization.
- **Command**: A request from a scripting client (or the operator, for admin commands) to mutate the game — gameplay (unit orders, build, etc.) or admin (speed, pause, cheat). Carries an originating-client identifier and a target slot.
- **Diagnostic Reading**: A point-in-time observation of broker, session, or telemetry health for the dashboard — distinct from Game State because it includes broker-internal data (connection states, server health) that the game itself does not produce.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: An operator can connect a scripting client to a guest-mode session and observe the client receiving live game state in under five minutes from a cold start, with no manual configuration of the gRPC endpoint beyond the documented default.
- **SC-002**: An operator can configure and launch a host-mode session (map, mode, at least three participants including one proxy-AI slot) entirely from the TUI in under three minutes from a cold start.
- **SC-003**: From the moment a relevant in-game change occurs (resource change, unit created/destroyed, command applied) to the moment it is visible in the dashboard and delivered to subscribed scripting clients, ninety-five percent of updates arrive within one second under normal load.
- **SC-004**: Admin commands issued in guest mode are rejected one hundred percent of the time with a clear admin-not-available error, with zero cases of partial application.
- **SC-005**: When the proxy AI or game process disconnects unexpectedly, the broker detects the loss and notifies all subscribed scripting clients within five seconds, and recovers to idle ready for a new session within ten seconds, in at least ninety-five percent of disconnect events.
- **SC-006**: The dashboard remains responsive (visible refresh of live values at least once per second) with at least four scripting clients connected and at least two hundred units in play.
- **SC-007**: A new operator can identify, from the dashboard alone, whether the broker is in host or guest mode, whether the session is connected, and current resources and unit count for each player, in under ten seconds of looking at it.
- **SC-008**: When the optional 2D visualization is unavailable on a headless host, the rest of the broker (gRPC server, dashboard, scripting-client bridge) continues to operate normally with no degradation.

## Assumptions

- Targets the HighBarV3 game engine and its associated lobby client (Chobby). The broker treats the game as an external process and does not ship the game itself.
- A "proxy AI" component compatible with HighBarV3 exists or will be developed alongside the broker; this spec defines the broker side of that link, not the proxy AI itself.
- The broker is single-session at a time. Running multiple concurrent matches from one broker is out of scope for the initial release; an operator who needs that runs multiple broker instances on different ports.
- The gRPC server listens on localhost by default. Network exposure (and any associated authentication of scripting clients) is a deployment-time configuration concern; for this initial spec, scripting clients are assumed to be trusted local processes.
- The TUI runs in a standard interactive terminal. Server/automation use of the broker without a TUI is out of scope for the initial release.
- The 2D visualization is offered for informational use only and is not on the critical path for any user story; running the broker with `--no-visualization` (or equivalent) on a headless host is fully supported.
- Game-mechanics-specific terminology (resources, units, buildings, statistics) is intentionally generic in this spec and will be mapped to HighBarV3-specific concepts during planning.
- "Cheat-class" admin overrides (resources, vision, victory) are a subset of the admin command surface and are only available in host mode; the exact list is constrained by what HighBarV3 itself exposes to a privileged AI.
- Scripting clients are responsible for their own authoring/correctness; the broker validates command structure and authority but does not validate game-strategic correctness.
- The operator is technical (familiar with gRPC tooling, terminal UIs, and the target game) — onboarding documentation assumes this audience.
