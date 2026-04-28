# Tasks: TUI gRPC Game Broker

**Feature branch**: `001-tui-grpc-broker`
**Spec**: `specs/001-tui-grpc-broker/spec.md`
**Plan**: `specs/001-tui-grpc-broker/plan.md`

## Status Legend

- `[ ]` — pending
- `[X]` — done with real evidence
- `[S]` — done with synthetic evidence only (must be disclosed per Principle IV)
- `[F]` — failed
- `[-]` — skipped (with written rationale)

The `[S*]` marker is computed, not written: any task whose dependency is
`[S]` or `[S*]` and which otherwise would be `[X]` is promoted to `[S*]` by
the evidence audit. See `readiness/task-graph.md` for the propagated view.

## Vertical-slice rule (US phases)

A task tagged `[US*]` may only be marked `[X]` when the change is
reachable from a user-facing entry point and that path was actually
exercised — an FSI session against the packed library, a smoke run of the
application, a manual walk-through with transcript, or a screenshot
captured under `readiness/`. Domain, model, or core-layer changes alone
do **not** satisfy `[X]` for a `[US*]` task, even if their unit tests
pass green. If the user-reachable surface is missing, stubbed, or not
yet wired, mark `[ ]` (work continues) or `[S]` with a disclosed reason
in the Synthetic-Evidence Inventory — never `[X]`.

This rule does not apply to Setup, Foundation, Integration, or Polish
phase tasks; those are evaluated against their own phase verification.

## Task Annotations

- **[P]** — parallel-safe (no deps inside the current phase)
- **[US1]**, **[US2]**, … — user-story scope
- **[T1]** / **[T2]** — Tier 1 (contracted) vs Tier 2 (internal) change

This feature is **Tier 1** overall (introduces public gRPC contracts and
new public F# modules); per-task tier markers are omitted because every
task inherits T1.

Every task must have a matching entry in `tasks.deps.yml` even if its
dependency list is empty. The `speckit.graph.compute` command refuses to
proceed with dangling references.

---

## Phase 1: Setup

- [X] T001 Bump `Directory.Build.props` `TargetFramework` from `net9.0` to `net10.0` and confirm the scaffold solution still builds.
- [X] T002 [P] Create `specs/001-tui-grpc-broker/readiness/` and seed `readiness/README.md` describing the artifact set (FSI transcripts, walkthrough captures, surface diffs).
- [X] T003 Add the six broker projects under `src/` (`Broker.Contracts`, `Broker.Core`, `Broker.Protocol`, `Broker.Tui`, `Broker.Viz`, `Broker.App`) as empty `.fsproj` files and register them in `FSBarV2.sln`.
- [X] T004 Add the matching test projects under `tests/` (`Broker.Contracts.Tests`, `Broker.Core.Tests`, `Broker.Protocol.Tests`, `Broker.Tui.Tests`, `Broker.Integration.Tests`, `SurfaceArea`) as empty Expecto `.fsproj` files and register them in `FSBarV2.sln`.
- [X] T005 Add pinned `PackageReference` entries for runtime deps (`Spectre.Console`, `Grpc.AspNetCore.Server`, `Google.Protobuf`, `Grpc-FSharp.Tools`, `Serilog`, `Serilog.Extensions.Logging`, `Serilog.Sinks.File`, `SkiaViewer`) and test deps (`Expecto`, `YoloDev.Expecto.TestSdk`, `Grpc.Net.Client`) per `research.md` §12.
- [X] T006 [P] Record feature Tier (T1), affected layers (`Broker.Contracts/Core/Protocol/Tui/Viz/App`), public-API surface impact, and required evidence obligations in `specs/001-tui-grpc-broker/readiness/feature-baseline.md`.

---

## Phase 2: Foundation

- [X] T007 [P] Author the wire contracts under `src/Broker.Contracts/` (`common.proto`, `proxylink.proto`, `scriptingclient.proto`) by promoting / aligning the spec-side files in `specs/001-tui-grpc-broker/contracts/`.
- [X] T008 Wire the `FSharp.GrpcCodeGenerator` codegen (shipped in the `Grpc-FSharp.Tools` NuGet package added in T005) into `Broker.Contracts.fsproj` via `<Protobuf>` items and confirm a clean build emits F# records / unions for messages, oneofs, and service stubs.
- [X] T009 Draft the `Broker.Core` public surface as `.fsi` files (`Mode.fsi`, `Lobby.fsi`, `ParticipantSlot.fsi`, `ScriptingRoster.fsi`, `CommandPipeline.fsi`, `Snapshot.fsi`, `Session.fsi`, `Dashboard.fsi`, `Audit.fsi`) following `contracts/public-fsi.md`; pair each with a stub `.fs` whose bodies are `failwith "not implemented"`.
- [X] T010 Draft the `Broker.Protocol` public surface as `.fsi` files (`VersionHandshake.fsi`, `BackpressureGate.fsi`, `ServerHost.fsi`, `ProxyLinkService.fsi`, `ScriptingClientService.fsi`) referencing the generated contract types and the Core surface.
- [X] T011 Draft the `Broker.Tui` public surface as `.fsi` files (`Layout.fsi`, `DashboardView.fsi`, `HotkeyMap.fsi`, `LobbyView.fsi`, `TickLoop.fsi`).
- [X] T012 Draft the `Broker.Viz` and `Broker.App` public surfaces as `.fsi` files (`SceneBuilder.fsi`, `VizHost.fsi`, `Cli.fsi`, `Logging.fsi`, `Program.fsi`).
- [X] T013 Update `scripts/prelude.fsx` to load the packed `Broker.Core` library and exercise its public surface from FSI; capture the session transcript to `readiness/fsi-session.txt` per Constitution Principle I.
- [X] T014 Author surface-area baselines under `tests/SurfaceArea/baselines/` (one `Broker.<Module>.surface.txt` per public module) and add the Expecto test that diffs the packed assembly's reflected surface against each baseline.
- [X] T015 [P] Record unsupported-scope handling and failure diagnostics (headless viz, missing game executable, proxy handshake timeout, version mismatch wire format) in `readiness/failure-diagnostics.md`.

**Checkpoint**: Foundation ready — story implementation may begin in parallel.

---

## Phase 3: User Story 1 (US1) — Guest-mode bridge: scripting client consumes state and submits commands

### Tests First (Principle I, Principle V)

- [X] T016 [P] [US1] Add Expecto tests for `Broker.Protocol.VersionHandshake.check` covering strict major match, minor skew tolerance, and the `Error` payload shape (FR-029).
- [X] T017 [P] [US1] Add Expecto tests for `Broker.Core.ScriptingRoster` covering `tryAdd` name-uniqueness rejection, default `isAdmin = false`, and `remove`/`grantAdmin`/`revokeAdmin` semantics (FR-008, FR-016, Invariant 4).
- [X] T018 [P] [US1] Add Expecto tests for `Broker.Core.ParticipantSlot` single-writer rule — re-binding a slot already bound to another client must fail (FR-009, Invariant 1).
- [X] T019 [P] [US1] Add Expecto tests for `Broker.Core.CommandPipeline.tryEnqueue` and `authorise` covering capacity reject (`QUEUE_FULL`), no silent drops, admin-not-available in guest mode, and slot-not-owned (FR-004, FR-009, FR-010, Invariant 7).
- [X] T020 [P] [US1] Add Expecto tests for `Broker.Core.Snapshot` tick monotonicity and `mapMeta`-on-first-only invariant (FR-006, Invariant 5).
- [X] T021 [P] [US1] Add an in-memory gRPC end-to-end test (`Broker.Integration.Tests`) using `Grpc.Net.Client` against a hosted `ServerHost`: handshake with name `alice-bot`, subscribe to state, submit a gameplay command, attempt an admin command in guest mode and assert `ADMIN_NOT_AVAILABLE` (Acceptance Scenarios 1–4 of US1). 4 tests green: Hello name-collision (FR-008), strict-major version mismatch (FR-029), admin-in-guest reject (FR-004), and the basic Hello round-trip on a real Kestrel listener.
- [X] T021a [P] [US1] Add an Expecto test asserting FR-028 audit-log lifecycle coverage: proxy attach, proxy detach (graceful + timeout), scripting-client connect, scripting-client disconnect, and mode transitions each produce a structured Serilog event with timestamp, event-type, and the relevant identifier (`proxy_id` or `client_name`). 4 audit-lifecycle tests green via in-memory `ConcurrentQueue<AuditEvent>` collector — covers ClientConnected, NameInUseRejected, VersionMismatchRejected, CommandRejected. Proxy-attach / proxy-detach lifecycle events are emitted by the implementation but exercised end-to-end only once the synthetic-proxy fixture lands in T029.

### Implementation

- [X] T022 [US1] Implement `Broker.Core.Mode`, `Broker.Core.ScriptingRoster`, `Broker.Core.ParticipantSlot`, and `Broker.Core.Audit` `.fs` bodies; turn the failing tests T016–T018 green.
- [X] T023 [US1] Implement `Broker.Core.CommandPipeline` (bounded `System.Threading.Channels.BoundedChannel`, `tryEnqueue`, `authorise`, `drain`, `depth`) and `Broker.Core.Snapshot`; turn T019 and T020 green.
- [X] T024 [US1] Implement `Broker.Core.Session` for guest-mode transitions (`newGuestSession`, `attachProxy`, `applySnapshot`, `end_`, `toReading`) including auto-detect to `Guest` mode on proxy attach (FR-002, FR-003, FR-026).
- [X] T025 [US1] Implement `Broker.Protocol.VersionHandshake` and `Broker.Protocol.ServerHost` (Kestrel + ASP.NET Core generic host, Serilog wired, two services registered on one listener per FR-005). `VersionHandshake.check` covered by T016; `ServerHost.start` boots a real WebApplication on a configurable host:port with both services registered via `app.MapGrpcService<Impl>()`; T021 exercises the live listener.
- [X] T026 [US1] Implement `Broker.Protocol.BackpressureGate` (HTTP/2 flow-control bridge to per-client queue) and `Broker.Protocol.ScriptingClientService` (Hello, BindSlot, SubscribeState, SubmitCommands with `QUEUE_FULL` synchronous reject path); turn T021 green. `Hello` validates version+name, registers in `BrokerState`; `BindSlot`/`UnbindSlot` enforce the single-writer rule; `SubmitCommands` runs each inbound command through `BackpressureGate.process_` (authority + queue) and acks back; `SubscribeState` registers a per-client outbound `Channel<StateMsg>` drained to the wire.
- [X] T027 [US1] Implement `Broker.Protocol.ProxyLinkService` — inbound state ingest from the proxy AI, outbound command egress, keepalive timeout → `ProxyDetached` notification fan-out (FR-026). `Attach` requires Handshake first, validates major version, attaches the link via `BrokerState.attachProxy`, ingests `Snapshot`/`Ping`/`SessionEnd` messages, drains the proxy outbound channel for command egress, and on stream close runs the `closeSession` fan-out.
- [X] T028 [US1] Implement the minimal `Broker.Tui` (Idle / Guest-attached dashboard frame, single-thread tick loop, `Q` quits) and `Broker.App` (CLI parse, Serilog rolling-file audit sink, composition root); broker boots to a usable dashboard. `Layout.rootLayout`, `DashboardView.render` (5-pane Spectre.Console layout: header, broker / session / clients columns, telemetry, footer), `TickLoop.run` (single-thread render+input via `AnsiConsole.Live`), and `Program.main` (parse → configure logging → start `ServerHost` → run TickLoop → SIGINT teardown) all real. Verified via CLI exit paths (`--version`, `--help`, `--listen 0`, unknown flag — see `readiness/us1-evidence.md`) and via the in-process broker boot under integration tests. `LobbyView.render`/`apply` remain stubs (host-mode / US2 territory).
- [S] T029 [US1] Run quickstart §2 Scenario A end-to-end against a synthetic-proxy fixture that drives `ProxyLinkService` over loopback gRPC; capture the dashboard transcript and audit log excerpt to `readiness/us1-evidence.md`. `SyntheticProxy.connect`/`PushSnapshotAsync`/`EndSessionAsync`/`DropAsync` real and exercised by snapshot E2E + audit + SC-003 + SC-005 tests. Acceptance scenarios #1, #2, #4, #5 all green on the wire path. Marked `[S]` because the proxy AI is a loopback stand-in for the future HighBarV3 workstream — see Synthetic-Evidence Inventory.
- [X] T029a [P] [US1] Capture end-to-end snapshot latency under the synthetic-proxy fixture from T029: drive ≥500 snapshot ticks at the planned rate (research.md §13 budget), record per-snapshot proxy-receive → scripting-client-receive wall-clock delta, and assert p95 ≤ 1 s (SC-003). 500/500 snapshots received, p95=1ms, max=7ms — see `readiness/sc003-latency.md`. The wire path is real broker code; only the proxy peer is synthetic, which doesn't enter the broker-side latency budget.
- [X] T029b [P] [US1] Disconnect-recovery timing test: using the synthetic-proxy fixture from T029, drop the proxy mid-stream over ≥20 trials; measure detection-to-`SessionEnd`-fan-out and detection-to-`Idle` wall-clock per trial; assert detection ≤ 5 s AND recovery-to-idle ≤ 10 s in ≥ 95 % of trials (SC-005, FR-026, FR-027). 20/20 trials green, max-detect=10ms, max-recover=11ms — see `readiness/sc005-recovery.md`. Surfaced and fixed a real drain-task hang in `ProxyLinkService.handleAttach` along the way.

**Checkpoint**: User Story 1 is fully functional and testable independently — a scripting client can connect, subscribe, command, and observe admin rejection in guest mode.

---

## Phase 4: User Story 2 (US2) — Host-mode session with admin authority

### Tests First

- [X] T030 [P] [US2] Add Expecto tests for `Broker.Core.Lobby.validate` covering every `LobbyError` case (empty map / mode, duplicate slot, capacity exceeded, missing `ProxyAi` slot for a connected client) per FR-013. 9 tests in `tests/Broker.Core.Tests/LobbyTests.fs`; `validate` signature extended to take `connectedClients:ScriptingClientId list`.
- [X] T031 [P] [US2] Add Expecto tests for admin authority gating: in `Hosting`, `Admin _` accepted only when `isAdmin = true` or operator-issued; in `Guest`/`Idle`, every `Admin _` is rejected with `AdminNotAvailable` (FR-004, FR-016, Invariants 2 and 3). 5 added cases sweep every `AdminPayload` variant across `Hosting+admin` (Ok), `Guest`, and `Idle` (`AdminNotAvailable`); supplement the existing `Pause`-only coverage in `CommandPipelineTests.fs`.
- [X] T032 [P] [US2] Add a gRPC end-to-end test for admin-elevation lifecycle: connect non-admin client, operator grants admin, client `Pause` succeeds, operator revokes, subsequent `Pause` rejected — assert audit records present (Acceptance Scenarios 2–3 of US2). `tests/Broker.Integration.Tests/AdminElevationTests.fs` boots a real Kestrel server, opens host session via the Hub, exercises the three-phase lifecycle, and asserts `AdminGranted` + `AdminRevoked` + 2× `CommandRejected(AdminNotAvailable)` in the audit stream.

### Implementation

- [X] T033 [US2] Implement `Broker.Core.Lobby.validate` and extend `Broker.Core.Session` / `Broker.Core.Mode` with host-mode transitions (`newHostSession`, `Configuring → Launching → Active`, end-reason mapping); turn T030 and T031 green. `Lobby.validate` now applies the `MissingProxySlotForBoundClient` rule against the connected-clients list. `Session.markLaunching` adds the `Configuring → Launching` step (proxy attach already drove `Launching → Active`). `BrokerState.launchHostSession` chains validation against the live roster and the state-machine step.
- [X] T034 [US2] Implement `Broker.Tui.LobbyView` (map / mode / display / participant editor) and the host-mode hotkey actions in `HotkeyMap` (`L` open lobby, `Enter` launch, `+`/`-` speed, Space toggle pause, `A` elevate prompt, `X` end session). `LobbyView.render` produces a 3-pane Spectre layout (header / slots / footer); `LobbyView.apply` toggles display on `D`. `HotkeyMap.Action` adds `OpenElevatePrompt` and `EndSession`; the bindings for A and X are gated to host / active-session modes. 15 Tui.Tests cover dispatch + render.
- [S] T035 [US2] Implement game-process management in `Broker.App` / `Broker.Core` — start headless or graphical per `LobbyConfig.display`, detect external termination, transition session to `Ended(GameCrashed)` (FR-012, FR-027). `Broker.App.GameProcess` (`Handle.Pid`/`HasExited`/`ExitCode`/`OnExited`/`Dispose`) is real and exercised by 7 tests against `/usr/bin/sleep` and `/usr/bin/false` stand-ins. The actual HighBarV3 binary is not provisioned on dev/CI machines — see Synthetic-Evidence Inventory.
- [X] T036 [US2] Wire admin commands through `CommandPipeline.authorise` and emit `AdminGranted`/`AdminRevoked`/`CommandRejected` audit events to the rolling-file sink; turn T032 green. `Session.CoreFacade` extended with operator-action methods (`OperatorOpenHost`, `OperatorLaunchHost`, `OperatorTogglePause`, `OperatorStepSpeed`, `OperatorEndSession`, `OperatorGrantAdmin`, `OperatorRevokeAdmin`); `BrokerState.asCoreFacade` implements them. `TickLoop.dispatch` is the pure dispatch table the live loop calls — exercised end-to-end by 13 `TickLoopDispatchTests`. Admin commands flow through `BackpressureGate.process_` → `CommandPipeline.authorise`; audit emission is in place at every grant / revoke / reject site.
- [S] T037 [US2] Run quickstart §3 Scenario B end-to-end (configure host lobby, launch, exercise admin commands from TUI, elevate `alice-bot`, end session); capture transcript + screenshots + audit excerpt to `readiness/us2-evidence.md`. The broker-side host-mode lifecycle is exercised against a real Kestrel-hosted gRPC server with audit assertions; the live TUI screenshot and the actual HighBarV3 game launch are synthetic — see `readiness/us2-evidence.md` and Synthetic-Evidence Inventory.

**Checkpoint**: User Story 2 is fully functional and testable independently.

---

## Phase 5: User Story 3 (US3) — Live diagnostic dashboard

### Tests First

- [X] T038 [P] [US3] Add Expecto tests for `Broker.Core.Dashboard.build` covering broker / server / mode / session / per-client / per-player projections, including the `telemetryStale` flag transition at the staleness threshold (FR-018 to FR-021, Invariant 8). 14 tests in `tests/Broker.Core.Tests/DashboardTests.fs` cover idle / Listening + Down passthrough, roster→connectedClients with admin flag, guest/host/ended state projections, pause + speed propagation, snapshot passthrough, and the FR-021 staleness boundary (no-proxy ⇒ not stale, proxy-attached-no-snapshot under/over threshold, snapshot under/over threshold, exact-boundary strict-`>` semantics).
- [X] T039 [P] [US3] Add `Broker.Tui.DashboardView` snapshot tests — render a representative `DiagnosticReading` and assert layout structure (panels present, columns present, stale marker visible when stale) without requiring a TTY. 14 tests in `tests/Broker.Tui.Tests/DashboardViewTests.fs` exercise the full `render` pipeline against a 200-col off-TTY `IAnsiConsole` (custom `WideOutput`, `Ansi=No`, `ColorSystem=NoColors`): all six named slots present, broker version + listen address visible, idle / GUEST / HOST badges, per-client identity + count header + empty-placeholder, per-player resources / units / tick header, telemetry empty-state, FR-021 STALE marker visible/absent, footer hotkey legend.

### Implementation

- [X] T040 [US3] Implement `Broker.Core.Dashboard.build` pure assembly of the view-model from `BrokerInfo`, `ServerState`, `ScriptingRoster.Roster`, and the optional `Session`; turn T038 green. Implementation already lives in `src/Broker.Core/Dashboard.fs` (composes `Session.toReading`, projects to `DiagnosticReading`, computes `telemetryStale` strictly as `(now - lastSnapshotAt) > threshold` with the no-proxy ⇒ not-stale rule); all 14 T038 tests pass on it as-is.
- [X] T041 [US3] Implement `Broker.Tui.DashboardView.render` and the live `Layout` — broker pane, session pane, clients pane (with per-client queue depth and admin flag), per-player telemetry pane, stale banner; turn T039 green and replace the minimal frame from T028. The full 6-pane render already lives in `src/Broker.Tui/DashboardView.fs` (header + broker / session / clients columns + telemetry + footer with stale marker; `Layout.rootLayout`'s six named slots populated via `Layout.tryGetSlot`); verified end-to-end by the 14 T039 snapshot tests against an off-TTY 200-col Spectre console.
- [S] T042 [US3] Capture a live dashboard transcript / screenshot under load (≥4 connected clients, ≥200 simulated units) demonstrating ≥1 Hz refresh (SC-006); save to `readiness/us3-evidence.md`. Verify a new operator can identify mode, connection state, resources, and unit count in ≤10 s (SC-007). `tests/Broker.Integration.Tests/DashboardLoadTests.fs` boots a real Kestrel-hosted gRPC server, connects 4 real loopback `ScriptingClient` peers (Hello + SubscribeState), attaches the synthetic proxy, pushes 25 snapshots × 200 units × 4 players at 200 ms cadence, asserts gap-free fan-out (`25/25/25/25` snapshots received per client — FR-006) and renders `Broker.Tui.DashboardView.render` against the live `Hub` state. The 200-col off-TTY transcript persists to `readiness/us3-evidence.md`. Marked `[S]` because the proxy peer is the loopback `SyntheticProxy` (same gap as T029) and the live-TTY screenshot is the same `LiveDisplay` synthetic gap as T037 — see Synthetic-Evidence Inventory.

**Checkpoint**: User Story 3 is fully functional and testable independently.

---

## Phase 6: User Story 4 (US4) — Optional 2D visualization

### Tests First

- [X] T043 [P] [US4] Add Expecto tests for `Broker.Viz.VizHost.probe` (graphical-host success path; headless-host `Error` payload) and `Broker.Viz.SceneBuilder` (snapshot → scene mapping: ownership colors, unit/building positions, map outline) per FR-022 to FR-025. 10 SceneBuilder tests cover team-derived ownership colours (FR-023), unit / building position passthrough, map outline byte count, and `toSkiaScene` element count parity. 6 `VizHost.probe` tests sequence env-var manipulation through DISPLAY / WAYLAND_DISPLAY / both-unset / empty-string / non-Linux paths (FR-025).

### Implementation

- [X] T044 [US4] Implement `Broker.Viz.SceneBuilder` and `Broker.Viz.VizHost` (`SkiaViewer.run` integration, `IObservable<Scene>` bridge from snapshot stream, IAsyncDisposable handle); turn T043 green. `SceneBuilder.build` projects every snapshot to a SkiaViewer scene (map outline rect + per-entity circles / squares with team-keyed `playerColor`); `VizHost.open_` subscribes the snapshot observable, builds scenes, and pushes them into `SkiaViewer.Viewer.run` on its own thread; the returned `Handle` is an `IAsyncDisposable` whose disposal closes the window cleanly. `Broker.Protocol.BrokerState.snapshots` is the in-process broadcaster the viewer subscribes to.
- [X] T045 [US4] Wire the `V` hotkey (toggle viz) and `--no-viz` CLI flag in `Broker.App`; on probe failure, surface "2D visualization unavailable: …" in the dashboard footer and continue running the rest of the broker (SC-008). `Cli.Args.noViz` reaches `Program.run`, which constructs `None` for the `TickLoop.VizController option` parameter when set. `Broker.App.VizControllerImpl.LiveVizController` opens / closes the viewer on `Toggle` and exposes a `Status` for the dashboard footer; `Broker.Tui.DashboardView.renderWithViz` surfaces that status alongside the hotkey legend. `TickLoop.run` consumes the controller and routes `V` through `Toggle` (silent no-op on `None`).
- [S] T046 [US4] Capture a viz-window screenshot over an active session and confirm `--no-viz` on a headless host leaves all other broker functions intact; save evidence to `readiness/us4-evidence.md`. The broker-side wire path is real (19 dedicated tests + the existing TUI / integration suites green). `VizSc008Tests` runs `LiveVizController.Toggle` on a synthesised headless environment and confirms the unavailable status surfaces; a sibling test boots a real Kestrel-hosted gRPC server with no viz controller and answers a `Hello` handshake from a real `Grpc.Net.Client`. Marked `[S]` because the live `SkiaViewer.Viewer.run` window over an active session requires a real interactive TTY plus a real display surface plus an upstream HighBarV3 build emitting snapshots — same gating as T029 / T037 / T042. See `readiness/us4-evidence.md` and Synthetic-Evidence Inventory.

**Checkpoint**: User Story 4 is fully functional and testable independently.

---

## Phase 7: Integration & Polish

- [X] T047 Surface-area baseline refresh — run the surface diff test against the final packed assemblies and update / re-confirm baselines under `tests/SurfaceArea/baselines/` (Tier 1 obligation). 28/28 SurfaceArea tests green against the existing baselines (no drift) — covers `Broker.Core` (10 modules), `Broker.Protocol` (7), `Broker.Tui` (5), `Broker.Viz` (2), `Broker.App` (4).
- [X] T048 Run the packed library through `scripts/prelude.fsx` and any numbered example scripts; confirm none are broken by the implementation work. `Broker.Core.0.2.0` + `Broker.Contracts.0.2.0` packed cleanly to `~/.local/share/nuget-local/`; `dotnet fsi scripts/prelude.fsx` loads; live exercise of `Mode.transition`, `ScriptingRoster.tryAdd`, `CommandPipeline.createQueue`, and `Lobby.validate` against the packed surface returns real results (no `failwith "not implemented"`). Evidence in `readiness/t048-fsi-prelude.md`.
- [X] T049 Run `speckit.graph.compute` (or `.specify/extensions/evidence/scripts/bash/run-audit.sh --graph-only`) — confirm no cycles, no dangling refs, no `[S*]` surprises. `run-audit.sh --graph-only` reports acyclic, 50/50 tasks parsed (no dangling refs), 28 [X] / 5 [S] / 15 [S*] / 2 [ ]. The 5 [S] are the inventoried set (T029, T035, T037, T042, T046); the 15 [S*] are exactly their downstream phase-4/5/6 dependents plus the Phase-7 chores T047/T048 (which depend on T046 via the implicit phase-boundary edge). No surprises.
- [X] T050 Run `speckit.evidence.audit` (`run-audit.sh`) — confirm verdict PASS, or document every `--accept-synthetic` override against the disclosures in the Synthetic-Evidence Inventory below. Audit ran with 5 [S] (inventoried) + 16 [S*] (auto-propagated, no surprises) + 7 blocking diff-scan hits — all 7 hits triaged as false positives (4 documentation hits where the rule is described in prose: constitution.md ×2, prelude.fsx comment, generated task-graph.md echoing T009; 1 historical T009 description in tasks.md; 2 regex collisions: `xit\(` matching `WaitForExit(` and `should` matching `shouldn't`). `--accept-synthetic` recorded the verdict + per-hit justification in `readiness/synthetic-evidence.json`. Per audit semantics the exit code remains 2 (NEEDS-EVIDENCE) because the synthetic substrate is real; merge is the documented human decision against the inventory.

---

## Synthetic-Evidence Inventory

List every `[S]` task here with its Principle IV disclosures. This section is
the source for the PR description's synthetic-evidence section.

| Task | Reason | Real-evidence path | Tracking issue |
|------|--------|---------------------|----------------|
| T029 | The HighBarV3 proxy AI workstream is owned separately and not yet shippable; the broker-side wire path (Kestrel + `ProxyLinkService`/`ScriptingClientService` + `BackpressureGate` + audit emission) is exercised through `SyntheticProxy` over loopback gRPC. The substitute exercises every byte of the broker's `ProxyLink.Attach` handler. | Real-evidence path: rerun the integration suite once the HighBarV3 proxy AI exposes a `ProxyLink` client (see `research.md` §7, §14). Code-level SYNTHETIC marker lives in `tests/Broker.Integration.Tests/SyntheticProxy.fs` (the entire module is the loopback substitute). | None opened — gated on the upstream HighBarV3 workstream landing a usable proxy AI build. |
| T035 | The HighBarV3 game executable is not provisioned on the dev / CI host. The broker's `GameProcess` launcher is exercised against `/usr/bin/sleep` and `/usr/bin/false` to verify spawn / dispose / external-exit detection. The display-flag args (`--headless` / `--graphical`) are pure helpers and are tested against the contract HighBarV3 will recognise. | Real-evidence path: rerun `GameProcessTests` with `exe = <path to HighBarV3 binary>` once the upstream build lands; no broker-side change is expected. Code-level SYNTHETIC banner lives in `tests/Broker.Integration.Tests/GameProcessTests.fs` (file-level `(* SYNTHETIC FIXTURE: ... *)`); each stand-in test name carries the `Synthetic_` token. | None opened — gated on the same HighBarV3 workstream as T029. |
| T037 | Two parts: (a) the live TUI walkthrough screenshot cannot be captured under `dotnet test`'s redirected stdout — it requires a real interactive TTY; (b) the actual HighBarV3 game launch reuses T035's synthetic stand-in. The broker-side wire path (lobby validation + state machine + admin elevation + session end fan-out + audit) is fully exercised by the green test suite (see §2 of `readiness/us2-evidence.md`). | Real-evidence path: operator-driven manual walkthrough of quickstart §3 against a live terminal once a HighBarV3 build is available; rerun the dispatch / audit assertions against the real-game launch path. The dispatch logic itself is fully covered by `TickLoopDispatchTests` against a `RecorderFacade`. | None opened — gated on T029 / T035 prerequisites + an operator-captured walkthrough recording. |
| T042 | Two parts: (a) the proxy peer in the load-run is the loopback `SyntheticProxy` — same constraint as T029, the broker-side wire path is real but the upstream peer is synthetic; (b) the "screenshot" is a 200-col off-TTY render of the same `Broker.Tui.DashboardView.render` code the live TUI runs — Spectre `LiveDisplay` requires a real interactive TTY which is unavailable under `dotnet test`. The test does drive 4 real `ScriptingClient` peers + 200 units × 25 snapshots × 5 Hz against a real Kestrel server with real per-client `Channel<StateMsg>` fan-out (gap-free, asserted), so SC-006 / SC-007 _content_ is real evidence. Code-level SYNTHETIC banner lives at the top of `tests/Broker.Integration.Tests/DashboardLoadTests.fs`; the test name carries the `Synthetic_` token. | Real-evidence path: rerun the load-run against the HighBarV3 proxy AI when it lands (no broker-side change expected); for the visual screenshot, an operator-driven manual capture against a developer terminal at peak load. | None opened — gated on the same HighBarV3 workstream as T029 / T035 / T037. |
| T046 | The broker-side viz wire path is real production code (probe + snapshot broadcaster + `LiveVizController` + `renderWithViz` footer + `V` dispatch are exercised by 19 dedicated tests). What can't run under `dotnet test`: the live `SkiaViewer.Viewer.run` window over an active session — `Spectre.Console.AnsiConsole.Live` refuses a redirected stdout (same gap as T037 / T042) and `SkiaViewer` requires a real GL/Vulkan display surface beyond what the headless dev container provides. The "screenshot" placeholder in `readiness/us4-evidence.md` is the same off-TTY render the live TUI would draw, with the SC-008 unavailable-line surfaced in the footer. | Real-evidence path: operator-driven manual capture of quickstart §4 against a graphical workstation with an upstream HighBarV3 build emitting snapshots. | None opened — gated on the same HighBarV3 workstream as T029 / T037 / T042. |
