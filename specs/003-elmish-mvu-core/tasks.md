# Tasks: Elmish MVU Core for State and I/O

**Feature branch**: `003-elmish-mvu-core`
**Spec**: `specs/003-elmish-mvu-core/spec.md`
**Plan**: `specs/003-elmish-mvu-core/plan.md`

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

This feature is overall **Tier 1** (replaces the public surface of
`Broker.Protocol.BrokerState`, adds the new `Broker.Mvu` project with a
full curated `.fsi` set, and reduces `Broker.Tui.TickLoop`). Tasks
inherit Tier 1 unless explicitly tagged `[T2]`.

Every task must have a matching entry in `tasks.deps.yml` even if its
dependency list is empty. The `speckit.graph.compute` command refuses to
proceed with dangling references.

---

## Phase 1: Setup

- [X] T001 [P] Pin the `Elmish` package (latest stable 4.x) in `Directory.Packages.props` per the repo's central-package-management discipline (plan §Technical Context, spec Assumptions). **Note**: The repo does not use central package management (no `Directory.Packages.props` exists; every existing `.fsproj` carries its own pinned `<PackageReference Version="…">` — e.g. `Spectre.Console 0.55.2`, `Grpc.AspNetCore.Server 2.76.0`, `Serilog 4.3.1`). Following that convention, `Elmish 4.2.0` is pinned directly in `src/Broker.Mvu/Broker.Mvu.fsproj` (T002). Adopting CPM repo-wide is out of scope for this feature.
- [X] T002 [P] Scaffold `src/Broker.Mvu/Broker.Mvu.fsproj` with `ProjectReference`s to `Broker.Core` and `Broker.Contracts` (the latter supplies the `Highbar.V1.*` and `FSBarV2.Broker.Contracts.*` namespaces opened from `Msg.fsi`) and `PackageReference`s to `Elmish` + `Spectre.Console` (plan §Project Structure)
- [X] T003 [P] Scaffold `tests/Broker.Mvu.Tests/Broker.Mvu.Tests.fsproj` with refs to `Broker.Mvu`, `Broker.Protocol`, and Expecto (plan §Testing)
- [X] T004 Register both new projects in `FSBarV2.sln` and create the readiness scaffolding `specs/003-elmish-mvu-core/readiness/{transcripts,artefacts,baselines}/`
- [X] T005 [P] Record feature Tier 1, affected layer, public-API impact, and required evidence obligations to `specs/003-elmish-mvu-core/readiness/feature-tier.md`

---

## Phase 2: Foundation

- [X] T006 [P] Draft public `.fsi` for `Broker.Mvu.Cmd`, `Broker.Mvu.Msg`, `Broker.Mvu.Model`. **Note**: Resolved a contract/F# tension — the contract called for `TimerSchedule` carrying `Msg` and `Msg.RpcContext` carrying `Cmd.RpcId`, which is mutually recursive across two modules. F# does not support sibling-module mutual recursion (only same-file `and` types). Adopted the canonical Elmish polymorphic pattern `Cmd.Cmd<'msg>` / `TimerSchedule<'msg>` instead — cleanly decouples the two modules and matches the `Cmd<Msg>` notation already used in `plan.md §Technical Context`.
- [X] T007 [P] Draft public `.fsi` for `Broker.Mvu.Update` and `Broker.Mvu.View`
- [X] T008 [P] Draft public `.fsi` for `Broker.Mvu.MvuRuntime` and `Broker.Mvu.TestRuntime`
- [X] T009 [P] Draft the six adapter-interface `.fsi` modules under `Broker.Mvu/Adapters/`
- [X] T010 [P] Draft `Broker.Mvu.Testing.Fixtures.fsi` with the synthetic-fixture banner per Principle IV
- [X] T011 [P] Draft reduced `Broker.Protocol.BrokerState.fsi` and updated `HighBarCoordinatorService.fsi` + `ScriptingClientService.fsi`. **Note**: drafts already authored in `contracts/public-fsi.md §⚠ REDUCED — Broker.Protocol.BrokerState.fsi` and §⚠ UPDATED — Broker.Protocol.HighBarCoordinatorService.fsi`. Wiring into actual project `.fsi` files is part of Phase 4 (T042/T043/T044) when implementations exist.
- [X] T012 [P] Draft reduced `Broker.Tui.TickLoop.fsi` and updated `DashboardView.fsi`/`LobbyView.fsi`. **Note**: drafts in `contracts/public-fsi.md §⚠ REDUCED — Broker.Tui.TickLoop.fsi`; project-file wiring lands in Phase 4 (T045/T046).
- [X] T013 [P] Draft the six production-adapter implementation `.fsi` files. **Note**: drafts in `contracts/public-fsi.md §⊕ NEW — production adapter implementations`; project wiring in Phase 4 (T036–T041).
- [X] T014 FSI exercise — captured to `readiness/transcripts/foundation-fsi-session.txt`. Validates that the .fsi surface loads from a packed Broker.Mvu, that data definitions (Cmd, Msg, Model, BrokerConfig) construct, that `Cmd.batch`/`Cmd.none` work, and that stubs throw the documented `not-yet-implemented` exception.
- [X] T015 Surface-area baselines committed: 14 `Broker.Mvu.*` baselines under `tests/SurfaceArea/baselines/`. Updated/reduced baselines for `Broker.Protocol.BrokerState`, `Broker.Tui.TickLoop`, services lands in Phase 8 T058 (after the actual `.fsi` reductions in Phase 4).
- [X] T016 [P] `readiness/diagnostics-plan.md` — Cmd-failure routing per family, `MailboxHighWater` cooldown + hysteresis, view-error rendering as data, SC-007 budget.
- [X] T017 `readiness/hub-retirement-plan.md` — enumerated removed Hub surface (35+ members across `BrokerState` + `Session.CoreFacade` + `TickLoop` retirees), SC-008 greppable check shell snippets, test-rebinding plan, composition-root sequencing.

**Checkpoint**: Foundation ready — story implementation may begin in parallel.

---

## Phase 3: User Story 1 (US1) — Carve-out closures (Priority: P1)

Closes T029 / T037 / T042 / T046 from feature 001 by replaying scripted
`Msg` sequences through the MVU test runtime; no TTY, no real game peer
(spec §User Story 1, FR-021, SC-002).

### Tests First (Principle I, Principle V)

- [S] T018 [P] [US1] `Broker.Mvu.Testing.Fixtures` implemented in `src/Broker.Mvu/Testing/Fixtures.fs`. Marked `[S]` per Principle IV — synthetic by definition; banner comment in source per data-model §6.1; Synthetic-Evidence Inventory row below.
- [X] T019 [P] [US1] `tests/Broker.Mvu.Tests/UpdateTests.fs` — 7 tests covering FR-001..FR-008 (exhaustive Msg matching, attach/heartbeat semantics, fanout, Cmd-failure routing per family, MailboxHighWater cooldown). All Synthetic-tagged.
- [X] T020 [P] [US1] `tests/Broker.Mvu.Tests/ViewTests.fs` — 4 tests covering FR-009..FR-011 + FR-016 (purity, determinism, content checks for Idle/Guest modes).
- [X] T021 [P] [US1] `tests/Broker.Mvu.Tests/RuntimeTests.fs` — 5 tests covering FR-015/FR-017 (dispatch, dispatchAll, capturedCmds, failCmd, clearCapturedCmds).
- [X] T022 [P] [US1] `CarveoutT029Tests.fs` — MVU-replay of the broker–proxy transcript through the synthetic Msg sequence; asserts attach + detach audits.
- [X] T023 [P] [US1] `CarveoutT037Tests.fs` — host-mode admin walkthrough; asserts Mode.Hosting, session present, client-1 in roster + elevated, audit trail of ModeChanged + ClientConnected + AdminGranted.
- [X] T024 [P] [US1] `CarveoutT042Tests.fs` — 4 clients × 25 snapshots × 200 units; asserts 4 clients connected, tick=25 snapshot applied with 200 units, `View.renderToString` succeeds, 4×25=100 ScriptingOutbound fanouts captured.
- [X] T025 [P] [US1] `CarveoutT046Tests.fs` — two scenarios (vizEnabled=true → V activates, render contains "viz active"; vizEnabled=false → V no-op, no VizCmd, render contains "viz disabled").

### Implementation

- [X] T026 [P] [US1] `Broker.Mvu/Model.fs` — immutable record + `init` + `defaultConfig`. Implemented in Phase 2 as the `.fsi` companion, real bodies operational.
- [X] T027 [P] [US1] `Broker.Mvu/Msg.fs` — `Msg` DU with 7 sub-unions (TuiInput, CoordinatorInbound, ScriptingInbound, AdapterCallback, CmdFailure, Tick, Lifecycle).
- [X] T028 [P] [US1] `Broker.Mvu/Cmd.fs` — `Cmd<'msg>` polymorphic envelope (Elmish-style — see T006 note re circular-dep resolution); `batch` flattens, `none = NoOp`.
- [X] T029 [US1] `Broker.Mvu/Update.fs` — exhaustive Msg match (every top-level arm + nested case), inlined hotkey translation, per-effect-family failure routing, MailboxHighWater cooldown. Real Audit arms (`MailboxHighWater`, `RuntimeStarted`, `RuntimeStopped`) deferred to Phase 8 (data-model §3.4). `Cmd.CompleteRpc` uses simplified `Ok | Fault` shape (T006 note) — handlers read post-update Model for the wire payload.
- [X] T030 [US1] `Broker.Mvu/View.fs` — pure Spectre layout + off-screen `renderToString`. Removed all `DateTimeOffset.UtcNow` calls so `view` is deterministic (uses `model.snapshot.capturedAt` else `model.brokerInfo.startedAt` for "now"). Snapshot-staleness flag deferred to Phase 4 (Msg.Tick.SnapshotStaleness flips a Model flag).
- [X] T031 [US1] `Broker.Mvu/TestRuntime.fs` — synchronous handle, `dispatch`, `dispatchAll`, `capturedCmds` (with Batch/NoOp flattening), `completeCmd`, `failCmd`, `runUntilQuiescent` for zero-delay one-shot timers.
- [ ] T032 [US1] Regenerate `specs/001-tui-grpc-broker/readiness/` artefacts for T029/T037/T042/T046. **Deferred to Phase 8** — the MVU-replay tests themselves are the regeneration evidence; the readiness artefact doc walks through them.

**Checkpoint**: User Story 1 — carve-out closure path is fully testable end-to-end via `dotnet test tests/Broker.Mvu.Tests`.

---

## Phase 4: User Story 3 (US3) — Operator-visible behaviour unchanged (Priority: P1)

Wires the production runtime, six production adapters, and reduced
gRPC / TUI surfaces; retires `Hub` and `withLock` (spec §User Story 3,
FR-018..FR-020, SC-003, SC-004, SC-008).

### Tests First (Principle I, Principle V)

- [ ] T033 [P] [US3] Add `tests/Broker.Mvu.Tests/HubRetirementGuardTests.fs` — ripgrep-based assertion that `Hub.session <-`, `Hub.mode <-`, `withLock`, and equivalent direct mutations have zero hits outside historical specs/comments (SC-008). **Partial**: file authored with `assertNoHits` helper for the full SC-008 pattern set, but only the `Broker.Mvu`-scoped guard is currently in the active `testList` (the multi-pattern grep would fail today against the live Hub surface). Activates fully alongside T048 once `BrokerState.Hub` is deleted.
- [ ] T034 [P] [US3] Add `tests/Broker.Protocol.Tests` cases driving `HighBarCoordinatorService.Impl` and `ScriptingClientService.Impl` through `MvuRuntime.Host` to assert that inbound RPCs translate into the expected `Msg` dispatch and the response is read back from the resulting `Model` (FR-013)

### Implementation

- [S] T035 [P] [US3] Implement `Broker.Mvu/MvuRuntime.fs` — `Host`, MailboxProcessor<Msg> dispatcher, custom Elmish `setState` hook, `AdapterSet`, `Channel<Model>` broadcast for the render thread, mailbox high-water sampling + rate-limited audit (research §2/§3). **Implementation complete** (see `src/Broker.Mvu/MvuRuntime.fs`); the runtime compiles and is reachable from FSI (T014 transcript), but no production composition currently constructs a `Host`. Per the vertical-slice rule it is `[S]` until T047 wires it into `Broker.App.Program`. Synthetic-Evidence Inventory row added below.
- [ ] T036 [P] [US3] Implement `Broker.App/AuditAdapterImpl.fs` (Serilog) — production audit sink emitting the existing envelope plus the three new arms (`MailboxHighWater`, `RuntimeStarted`, `RuntimeStopped` — data-model §3.4)
- [ ] T037 [P] [US3] Implement `Broker.App/TimerAdapterImpl.fs` — `System.Threading.Timer` per registered tick, posting `Msg.AdapterCallback.TimerFired` back through the runtime
- [ ] T038 [P] [US3] Implement `Broker.App/LifecycleAdapterImpl.fs` — process exit + `SessionEnd` broadcast (graceful-shutdown path, research §8)
- [ ] T039 [P] [US3] Implement `Broker.Protocol/CoordinatorAdapterImpl.fs` — drains the runtime-emitted outbound `Channel<Command>` and writes to the active `OpenCommandChannel` server-stream
- [ ] T040 [P] [US3] Implement `Broker.Protocol/ScriptingAdapterImpl.fs` — owns per-client `Channel<StateMsg>`; enforces FR-010 bounded backpressure; samples depth + high-water on `queueDepthSampleMs` cadence and posts `Msg.AdapterCallback.QueueDepth`/`QueueOverflow` back (spec Clarification Q1, FR-005)
- [ ] T041 [P] [US3] Implement `Broker.Viz/VizAdapterImpl.fs` — drains a per-adapter `VizOp` channel into the dedicated SkiaViewer task; updates `VizControllerImpl` to match the new interface
- [ ] T042 [US3] Implement reduced `Broker.Protocol/BrokerState.fs` — `Binding`, `bind`, `postMsg`, `awaitResponse<'r>`, `init`; the new Msg-translation surface used by gRPC handlers
- [ ] T043 [US3] Refactor `Broker.Protocol/HighBarCoordinatorService.fs` `Impl` handlers to dispatch `Msg.CoordinatorInbound` arms via `Binding.awaitResponse` and read responses from the resulting `Model` (FR-013); zero direct state mutation
- [ ] T044 [US3] Refactor `Broker.Protocol/ScriptingClientService.fs` `Impl` handlers to dispatch `Msg.ScriptingInbound` arms via `Binding.awaitResponse` (FR-013)
- [ ] T045 [US3] Update `Broker.Tui/DashboardView.fs` and `Broker.Tui/LobbyView.fs` to accept Model fragments (replacing the previous `DiagnosticReading`/`Hub` projections); composed by `Broker.Mvu.View`
- [ ] T046 [US3] Reduce `Broker.Tui/TickLoop.fs` to the keypress-poll-and-render shell: poll `Console.KeyAvailable`, post `Msg.TuiInput.Keypress`, drain `MvuRuntime.subscribeModel` on each tick, feed `Broker.Mvu.View.view` into `LiveDisplay.Update`. Remove the previous `dispatch` table, `UiMode`, and `CoreFacade` consumer pattern
- [ ] T047 [US3] Update `Broker.App/Program.fs` composition root: build initial `Model` from CLI args, register the six production adapters into `AdapterSet`, start `MvuRuntime.Host`, bind the gRPC services through `BrokerState.bind`, run `Broker.Tui.TickLoop`. Remove the `withLock` / `Hub.stateLock` plumbing in the same change
- [ ] T048 [US3] Delete `BrokerState.Hub` + `stateLock` and every removed mutation function listed in `readiness/hub-retirement-plan.md` (T017). Confirm SC-008 greppable check is green
- [ ] T049 [US3] Update `Broker.Protocol.Tests` to bind through `MvuRuntime.Host` instead of `Hub`; the existing wire-shape coverage is preserved against the new surface
- [ ] T050 [US3] Update `Broker.Tui.Tests` for the reduced `TickLoop` and the off-screen render path against `Broker.Tui.View` composition
- [ ] T051 [US3] Verify `Broker.Integration.Tests` (`SyntheticCoordinator`, `CoordinatorLoadTests`, `ScriptingClientFanoutTests`) green against the production runtime — real adapters, real gRPC, real audit sink, real Spectre live render (US3 acceptance scenario 3, FR-018)

**Checkpoint**: User Story 3 — operator-visible behaviour byte-for-byte unchanged; `Hub` retired; `withLock` zero hits.

---

## Phase 5: User Story 2 (US2) — Maintainer adds a TUI feature with full test coverage (Priority: P1)

Worked example for SC-005 — the maintainer experience targeted by the
pivot. A small TUI-touching backlog item is implemented end-to-end
through `Msg` + `update` + `View` + tests (spec §User Story 2).

- [S] T052 [US2] Add a worked-example test that drives a new hotkey or column from `Msg` case → `update` clause → `View` render assert in fewer than 100 lines (SC-005 measurement). Implemented in `tests/Broker.Mvu.Tests/WorkedExampleTests.fs` (2 tests, both green). Marked `[S]` per the vertical-slice rule: the K hotkey is exercised through `TestRuntime.dispatch`, not yet via a real key press, because `TickLoop` keypress wiring is deferred to T046. Synthetic-Evidence Inventory row below.
- [S] T053 [US2] Implement the worked-example feature (chose: `K` hotkey to kick the elevated scripting client) as `HotkeyAction.KickElevatedClient` + Update clause + `Model.kickedClients` field + audit Cmd. **Implementation complete** in `src/Broker.Mvu/Update.fs` and `src/Broker.Mvu/Model.fs/.fsi`. Marked `[S]` per the vertical-slice rule: same reason as T052 — the user-reachable wire-up (TickLoop posting `Msg.TuiInput.Keypress` for K) lands with T046. Synthetic-Evidence Inventory row below.
- [X] T054 [US2] Update `quickstart.md` Story 2 with the maintainer workflow walkthrough citing the worked example as canonical reference (lines 100–151 walk through the K-hotkey 5-step workflow under the SC-005 100-line bar).

**Checkpoint**: User Story 2 — adding a new TUI feature is exercisable from tests alone before any interactive run.

---

## Phase 6: User Story 4 (US4) — Side effects inspectable in tests (Priority: P2)

- [X] T055 [US4] Added `tests/Broker.Mvu.Tests/CmdInspectionTests.fs` with three Synthetic-tagged tests asserting `Cmd` shape: `AdminGranted` audit, authorised admin command → `CoordinatorOutbound`, unauthorised command → `ScriptingReject` + `CommandRejected` audit. Drives the cases through `TestRuntime` + `Testing.Fixtures` with no live audit file and no live gRPC frame on the wire — exactly the surface US4 specifies. Auto-`[S*]` propagation via T018 is expected and acceptable.

---

## Phase 7: User Story 5 (US5) — Snapshot regression fixtures (Priority: P3)

- [X] T056 [P] [US5] Checked in render fixtures `tests/Broker.Mvu.Tests/Fixtures/{dashboard-guest-2clients,dashboard-host-elevated,viz-active-footer}.txt` (generated from `View.renderToString` against the synthetic Models in `Testing.Fixtures`).
- [X] T057 [US5] Added `tests/Broker.Mvu.Tests/FixtureRegressionTests.fs` reading the three checked-in `.txt` files and asserting `View.renderToString` equality. Includes a `BROKER_REGENERATE_VIEW_FIXTURES=1` regenerate path. Fixture-update workflow note still owed in `quickstart.md` Story 5 (will land in T062 PR-description sweep).

---

## Phase 8: Integration & Polish

- [ ] T058 Surface-area baselines refresh — regenerate baselines for new + updated public modules; delete the retired `Broker.Protocol.BrokerState.surface.txt` Hub-era baseline; commit refreshed `.txt` files. **FR-019 / FR-020 guard**: confirm `Broker.Contracts.*` baselines are byte-identical to the feature-002-head versions, and that `HighBarCoordinatorService.surface.txt` / `ScriptingClientService.surface.txt` differ from feature-002-head only in the constructor parameter type (`Hub` → `BrokerState.Binding`); fail the task if any other gRPC service surface drift is found (Tier 1 obligation)
- [ ] T059 Run the packed `Broker.Mvu` library through `scripts/prelude.fsx` and any numbered example scripts under `scripts/examples/`; capture session to `readiness/transcripts/integration-fsi-session.txt` (Constitution Principle I, US1 independent test confirmation)
- [ ] T060 Run `speckit.graph.compute` (or `.specify/extensions/evidence/scripts/bash/run-audit.sh --graph-only`) — confirm no cycles, no dangling refs, no `[S*]` surprises
- [ ] T061 Run `speckit.evidence.audit` — confirm verdict PASS or document every `--accept-synthetic` override against `readiness/feature-tier.md`
- [ ] T062 Finalise PR description: enumerate `[S]` tasks, link the disclosure plan in `data-model.md §6`, reference the SC-001..SC-008 evidence locations, refresh the Synthetic-Evidence Inventory below

---

## Synthetic-Evidence Inventory

List every `[S]` task here with its Principle IV disclosures. This section is
the source for the PR description's synthetic-evidence section.

| Task | Reason | Real-evidence path | Tracking issue |
|------|--------|---------------------|----------------|
| T018 | Synthetic `Model` + `Msg`-stream fixtures used to drive carve-out tests; values would otherwise come from a live BAR session | `specs/001-tui-grpc-broker/readiness/` (regenerated by Story 3 walkthrough); production smoke run captured by T059 | _(none yet)_ |
| T035 | `MvuRuntime.Host` is implemented but not yet wired into any production composition; vertical-slice rule fails until `Broker.App.Program` constructs a `Host` and the gRPC services dispatch through `BrokerState.Binding` over it | T047 composition-root rewrite + T051 integration-tests-against-live-runtime | _(none yet — covered by T047/T051)_ |
| T052 | Worked-example test exercises the K hotkey through `TestRuntime.dispatch`; a real key press cannot reach `update` until `TickLoop` is rewritten | T046 reduces `TickLoop.run` to post `Msg.TuiInput.Keypress` into the `Host` mailbox; T050 rebinds `Broker.Tui.Tests` against the new path | _(none yet — covered by T046/T050)_ |
| T053 | Worked-example feature is fully implemented in `Update.fs` / `Model.fs`; user-reachable wire-up depends on the same `TickLoop` reduction | Same as T052 (T046 + T050) | _(none yet — covered by T046/T050)_ |
