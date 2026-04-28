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

- [ ] T001 [P] Pin the `Elmish` package (latest stable 4.x) in `Directory.Packages.props` per the repo's central-package-management discipline (plan §Technical Context, spec Assumptions)
- [ ] T002 [P] Scaffold `src/Broker.Mvu/Broker.Mvu.fsproj` with `ProjectReference`s to `Broker.Core` and `PackageReference`s to `Elmish` + `Spectre.Console` (plan §Project Structure)
- [ ] T003 [P] Scaffold `tests/Broker.Mvu.Tests/Broker.Mvu.Tests.fsproj` with refs to `Broker.Mvu`, `Broker.Protocol`, and Expecto (plan §Testing)
- [ ] T004 Register both new projects in `FSBarV2.sln` and create the readiness scaffolding `specs/003-elmish-mvu-core/readiness/{transcripts,artefacts,baselines}/`
- [ ] T005 [P] Record feature Tier 1, affected layer, public-API impact, and required evidence obligations to `specs/003-elmish-mvu-core/readiness/feature-tier.md`

---

## Phase 2: Foundation

- [ ] T006 [P] Draft public `.fsi` for `Broker.Mvu.Cmd`, `Broker.Mvu.Msg`, `Broker.Mvu.Model` (the value types — see contracts/public-fsi.md §Cmd/§Msg/§Model)
- [ ] T007 [P] Draft public `.fsi` for `Broker.Mvu.Update` and `Broker.Mvu.View` (the pure transition + projection — contracts/public-fsi.md §Update/§View)
- [ ] T008 [P] Draft public `.fsi` for `Broker.Mvu.MvuRuntime` (production Host + AdapterSet) and `Broker.Mvu.TestRuntime` (synchronous handle) — contracts/public-fsi.md §MvuRuntime/§TestRuntime
- [ ] T009 [P] Draft the six adapter-interface `.fsi` modules under `Broker.Mvu/Adapters/` — `AuditAdapter`, `CoordinatorAdapter`, `ScriptingAdapter`, `VizAdapter`, `TimerAdapter`, `LifecycleAdapter` (contracts/public-fsi.md §Adapter-interface modules)
- [ ] T010 [P] Draft `Broker.Mvu.Testing.Fixtures.fsi` with the synthetic-fixture banner per Principle IV (contracts/public-fsi.md §Fixtures, data-model §6.1)
- [ ] T011 [P] Draft reduced `Broker.Protocol.BrokerState.fsi` (only `Binding`, `bind`, `postMsg`, `awaitResponse`, `OwnerRule`) and updated `HighBarCoordinatorService.fsi` + `ScriptingClientService.fsi` (ctor takes `Binding` not `Hub`)
- [ ] T012 [P] Draft reduced `Broker.Tui.TickLoop.fsi` (keypress-poll-and-render shell only) and updated `DashboardView.fsi` + `LobbyView.fsi` (accept Model fragments)
- [ ] T013 [P] Draft the six production-adapter implementation `.fsi` files (`Broker.App/AuditAdapterImpl`, `TimerAdapterImpl`, `LifecycleAdapterImpl`; `Broker.Protocol/CoordinatorAdapterImpl`, `ScriptingAdapterImpl`; `Broker.Viz/VizAdapterImpl`)
- [ ] T014 Exercise the drafted `.fsi` set from FSI via `scripts/prelude.fsx` against a pack of the new project; capture transcript to `readiness/transcripts/foundation-fsi-session.txt` (Constitution Principle I)
- [ ] T015 Record initial surface-area baselines for the new `Broker.Mvu.*` modules and the updated/reduced `Broker.Protocol.BrokerState`, `Broker.Tui.TickLoop`, `HighBarCoordinatorService`, `ScriptingClientService` modules (Principle II)
- [ ] T016 [P] Document the Cmd-failure routing strategy + per-effect-family failure arms + `MailboxHighWater` rate-limit cooldown to `readiness/diagnostics-plan.md` (FR-008, spec Clarification Q3, Principle VI)
- [ ] T017 Document the `Hub` retirement scope: enumerate every removed surface (`Hub` type, `stateLock`, `openHostSession`, `openGuestSession`, `closeSession`, `attachCoordinator`, `coordinatorCommandChannel`, `mode`, `roster`, `slots`, `session`, `attachProxy`, `proxyOutbound`, `withLock`) with the greppable assertion text for SC-008 to `readiness/hub-retirement-plan.md`

**Checkpoint**: Foundation ready — story implementation may begin in parallel.

---

## Phase 3: User Story 1 (US1) — Carve-out closures (Priority: P1)

Closes T029 / T037 / T042 / T046 from feature 001 by replaying scripted
`Msg` sequences through the MVU test runtime; no TTY, no real game peer
(spec §User Story 1, FR-021, SC-002).

### Tests First (Principle I, Principle V)

- [ ] T018 [P] [US1] Implement `Broker.Mvu.Testing.Fixtures` (synthetic Model + Msg-stream builders for the four carve-out scenarios). Marked `[S]` per Principle IV — synthetic by definition; banner comment in source per data-model §6.1
- [ ] T019 [P] [US1] Add `tests/Broker.Mvu.Tests/UpdateTests.fs` covering FR-001..FR-008 — pure update behaviour, exhaustive Msg matching, single-thread invariant, per-effect-family failure routing
- [ ] T020 [P] [US1] Add `tests/Broker.Mvu.Tests/ViewTests.fs` covering FR-009..FR-011 + FR-016 — `view` purity, `renderToString` determinism, byte-for-byte parity with post-002 dashboard for a fixed `Model` (SC-006)
- [ ] T021 [P] [US1] Add `tests/Broker.Mvu.Tests/RuntimeTests.fs` covering the test-runtime contract (FR-015, FR-017): synchronous dispatch, captured Cmd list shape, `completeCmd`/`failCmd`/`runUntilQuiescent` semantics
- [ ] T022 [P] [US1] Add `tests/Broker.Mvu.Tests/CarveoutT029Tests.fs` — broker–proxy transcript MVU-replay (acceptance scenario 1, spec §US1)
- [ ] T023 [P] [US1] Add `tests/Broker.Mvu.Tests/CarveoutT037Tests.fs` — host-mode admin walkthrough MVU-replay (acceptance scenario 2)
- [ ] T024 [P] [US1] Add `tests/Broker.Mvu.Tests/CarveoutT042Tests.fs` — 4-client × 200-unit dashboard render across ≥25 frames (acceptance scenario 3)
- [ ] T025 [P] [US1] Add `tests/Broker.Mvu.Tests/CarveoutT046Tests.fs` — viz status line in both `vizEnabled=true` and `--no-viz` modes (acceptance scenario 4)

### Implementation

- [ ] T026 [P] [US1] Implement `Broker.Mvu/Model.fs` — the immutable record + `init` builder (data-model §1.1, §1.2–§1.6)
- [ ] T027 [P] [US1] Implement `Broker.Mvu/Msg.fs` — the discriminated union covering every input (data-model §1.7)
- [ ] T028 [P] [US1] Implement `Broker.Mvu/Cmd.fs` — DU + `batch`/`none` helpers (data-model §1.8)
- [ ] T029 [US1] Implement `Broker.Mvu/Update.fs` — exhaustive Msg match producing `Model * Cmd list`; FR-001..FR-008 + spec edge cases (cmd-failure routing, mailbox high-water cooldown, view-error rendering as data)
- [ ] T030 [US1] Implement `Broker.Mvu/View.fs` — `view : Model -> IRenderable` + `renderToString`; preserves post-002 dashboard byte-for-byte (FR-009, FR-010, FR-011, SC-006)
- [ ] T031 [US1] Implement `Broker.Mvu/TestRuntime.fs` — synchronous `dispatch`/`dispatchAll`/`completeCmd`/`failCmd`/`runUntilQuiescent` (FR-015, FR-017)
- [ ] T032 [US1] Regenerate readiness artefacts under `specs/001-tui-grpc-broker/readiness/` for T029/T037/T042/T046 — MVU-replay evidence captured, `Synthetic-Evidence Inventory` entries flipped to "closed; live evidence captured by MVU replay" (FR-021)

**Checkpoint**: User Story 1 — carve-out closure path is fully testable end-to-end via `dotnet test tests/Broker.Mvu.Tests`.

---

## Phase 4: User Story 3 (US3) — Operator-visible behaviour unchanged (Priority: P1)

Wires the production runtime, six production adapters, and reduced
gRPC / TUI surfaces; retires `Hub` and `withLock` (spec §User Story 3,
FR-018..FR-020, SC-003, SC-004, SC-008).

### Tests First (Principle I, Principle V)

- [ ] T033 [P] [US3] Add `tests/Broker.Mvu.Tests/HubRetirementGuardTests.fs` — ripgrep-based assertion that `Hub.session <-`, `Hub.mode <-`, `withLock`, and equivalent direct mutations have zero hits outside historical specs/comments (SC-008)
- [ ] T034 [P] [US3] Add `tests/Broker.Protocol.Tests` cases driving `HighBarCoordinatorService.Impl` and `ScriptingClientService.Impl` through `MvuRuntime.Host` to assert that inbound RPCs translate into the expected `Msg` dispatch and the response is read back from the resulting `Model` (FR-013)

### Implementation

- [ ] T035 [P] [US3] Implement `Broker.Mvu/MvuRuntime.fs` — `Host`, MailboxProcessor<Msg> dispatcher, custom Elmish `setState` hook, `AdapterSet`, `Channel<Model>` broadcast for the render thread, mailbox high-water sampling + rate-limited audit (research §2/§3)
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

- [ ] T052 [US2] Add a worked-example test that drives a new hotkey or column from `Msg` case → `update` clause → `View` render assert in fewer than 100 lines (SC-005 measurement)
- [ ] T053 [US2] Implement the worked-example feature (e.g., "kick scripting client" hotkey or per-team kill/loss column — pick one open backlog item) as the actual `Msg` case + `update` clause + `View` change + audit Cmd
- [ ] T054 [US2] Update `quickstart.md` Story 2 with the maintainer workflow walkthrough citing the worked example as canonical reference

**Checkpoint**: User Story 2 — adding a new TUI feature is exercisable from tests alone before any interactive run.

---

## Phase 6: User Story 4 (US4) — Side effects inspectable in tests (Priority: P2)

- [ ] T055 [US4] Add tests in `tests/Broker.Mvu.Tests/CmdInspectionTests.fs` asserting `Cmd` list shape for representative flows: admin elevation → audit; admin command → coordinator outbound + audit; schema mismatch → audit + scripting reject — using `TestRuntime` + `Fixtures` with no live audit file and no live gRPC frame on the wire (US4 acceptance scenarios)

---

## Phase 7: User Story 5 (US5) — Snapshot regression fixtures (Priority: P3)

- [ ] T056 [P] [US5] Check in render fixtures `tests/Broker.Mvu.Tests/Fixtures/dashboard-guest-2clients.txt`, `dashboard-host-elevated.txt`, `viz-active-footer.txt` (data-model §6.1, plan §Testing)
- [ ] T057 [US5] Add `FixtureRegressionTests.fs` reading the checked-in `.txt` files and asserting `View.renderToString` equality; document the fixture-update workflow in `quickstart.md` Story 5

---

## Phase 8: Integration & Polish

- [ ] T058 Surface-area baselines refresh — regenerate baselines for new + updated public modules; delete the retired `Broker.Protocol.BrokerState.surface.txt` Hub-era baseline; commit refreshed `.txt` files (Tier 1 obligation)
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
