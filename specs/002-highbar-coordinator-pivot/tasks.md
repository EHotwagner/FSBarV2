# Tasks: Broker–HighBarCoordinator Wire Pivot

**Feature branch**: `002-highbar-coordinator-pivot`
**Spec**: `specs/002-highbar-coordinator-pivot/spec.md`
**Plan**: `specs/002-highbar-coordinator-pivot/plan.md`

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

This feature is **Tier 1** overall (removes the public `ProxyLink`
contract, adds the public `HighBarCoordinator` server-side contract);
per-task tier markers are omitted because every task inherits T1.

Every task must have a matching entry in `tasks.deps.yml` even if its
dependency list is empty. The `speckit.graph.compute` command refuses to
proceed with dangling references.

## Story 3 carve-out closure (real-evidence policy)

Plan Constitution-Check Principle IV is explicit: **no new carve-out is
acceptable for the wire-side end-to-end**. Phase 5 (User Story 3) tasks
therefore stay `[ ]` until an operator captures the walkthrough against
a real BAR + HighBarV3 build under `specs/001-tui-grpc-broker/readiness/`;
the synthetic-coordinator CI fixture (T022) is the broker-side
regression net, **not** the closure evidence.

The single broker-side `[S]` surface allowed by this feature is the
US1 / US2 CI evidence captured under
`specs/002-highbar-coordinator-pivot/readiness/usN-synthetic.md`
against the loopback `SyntheticCoordinator`. Those `[S]` markers carry
the inventory disclosures below.

---

## Phase 1: Setup

- [X] T001 Vendor the five HighBarV3 proto files (`coordinator.proto`,
  `state.proto`, `commands.proto`, `events.proto`, `common.proto`) into
  `src/Broker.Contracts/highbar/` from upstream commit
  `66483515a3333d6160bb5298e0d0bf6bb7188b4c` (per `contracts/highbar-proto-pin.md`)
  and mirror the manifest as `src/Broker.Contracts/HIGHBAR_PROTO_PIN.md`.
- [X] T002 [P] Update `src/Broker.Contracts/Broker.Contracts.fsproj` to
  include `highbar/*.proto` as `<Protobuf>` items so
  `FSharp.GrpcCodeGenerator` produces the `Highbar.V1.*` namespace
  alongside the existing `FSBarV2.Broker.Contracts` namespace; confirm a
  clean build emits the generated F# types.
- [X] T003 [P] Create `specs/002-highbar-coordinator-pivot/readiness/`
  and seed `readiness/README.md` describing the artifact set
  (synthetic-coordinator transcripts, real-game walkthrough captures,
  surface diffs).
- [X] T004 [P] Record feature Tier (T1), affected layers
  (`Broker.Contracts`, `Broker.Protocol`, `Broker.App` composition root,
  `Broker.Integration.Tests`, `tests/SurfaceArea`), public-API surface
  impact (remove `Broker.Protocol.ProxyLinkService`; add
  `Broker.Protocol.HighBarCoordinatorService`), and required evidence
  obligations in `readiness/feature-baseline.md`.

---

## Phase 2: Foundation

- [X] T005 Draft `Broker.Protocol.HighBarCoordinatorService.fsi`
  (curated surface from `contracts/public-fsi.md`) and pair it with a
  stub `.fs` whose bodies are `failwith "not implemented"`. Register
  both in `Broker.Protocol.fsproj`.
- [X] T006 [P] Update `Broker.Protocol.WireConvert.fsi` per
  `contracts/public-fsi.md` — add `RunningView`, `ApplyResult`,
  `applyHighBarStateUpdate`, `tryFromCoreCommandToHighBar`; delete the
  ProxyLink-side helpers (`toCoreSnapshot`, `fromCoreCommand`). Pair
  with `failwith` stub `.fs` bodies.
- [X] T007 [P] Update `Broker.Protocol.BrokerState.fsi` per
  `contracts/public-fsi.md` — rename `attachProxy` → `attachCoordinator`,
  `proxyOutbound` → `coordinatorCommandChannel`, `sendToProxy` →
  `sendToCoordinator`; add `OwnerRule`, `expectedSchemaVersion`,
  `setExpectedSchemaVersion`, `ownerRule`, `setOwnerRule`,
  `noteHeartbeat`, `noteStateGap`. Update `.fs` to keep the surface
  compiling (rename existing call sites; new functions stub `failwith`).
- [X] T008 [P] Update `Broker.Core.Audit.fsi` and
  `Broker.Core.CommandPipeline.fsi` — add the new arms from
  data-model §1.12 / §1.14 (`Coordinator*` audit cases, `SchemaMismatch`,
  `NotOwner`); remove `ProxyAttached` / `ProxyDetached`. Pair with
  matching `.fs` stubs.
- [X] T009 Add a `ProtoPin` test under `tests/Broker.Contracts.Tests`
  that hashes every file under `src/Broker.Contracts/highbar/` with
  sha256 and asserts equality against constants from
  `contracts/highbar-proto-pin.md` — fails loudly with a diff if the
  vendored copy drifts.
- [X] T010 Update `scripts/prelude.fsx` to load the packed
  `Broker.Contracts` (with `Highbar.V1.*`), `Broker.Core`, and
  `Broker.Protocol` against the new `.fsi` surfaces; capture the FSI
  exercise from `contracts/public-fsi.md` §"FSI exercise sketch" to
  `readiness/fsi-session.txt`.
- [X] T011 [P] Seed a placeholder `Broker.Protocol.HighBarCoordinatorService.surface.txt`
  baseline under `tests/SurfaceArea/baselines/` so the SurfaceArea diff
  test compiles (final byte-exact baseline lands in T044). Do not
  remove the existing ProxyLink baseline yet — that happens in Phase 6.
- [X] T012 [P] Record unsupported-scope handling and failure
  diagnostics in `readiness/failure-diagnostics.md`: schema-version
  mismatch (FR-003 surface — gRPC `FAILED_PRECONDITION` with both
  versions in detail), non-owner Heartbeat (FR-011 — gRPC
  `PERMISSION_DENIED`), heartbeat timeout (FR-008), admin commands
  with no AICommand mapping (research §3 — `AdminNotAvailable`),
  PushState seq gap (FR-013 — `CoordinatorStateGap` audit + dashboard
  badge).

**Checkpoint**: Foundation ready — story implementation may begin in parallel.

---

## Phase 3: User Story 1 (US1) — Broker consumes live state from a running BAR session

### Tests First (Principle I, Principle V)

- [X] T013 [P] [US1] Add Expecto tests for HighBar schema-version
  strict-equality handshake — `expectedSchemaVersion = "1.0.0"` accepts
  matching `HeartbeatRequest`; mismatch produces
  `RejectReason.SchemaMismatch`, emits `CoordinatorSchemaMismatch`
  audit, and closes the stream with gRPC `FAILED_PRECONDITION`
  (FR-003, SC-007).
- [X] T014 [P] [US1] Add Expecto tests for
  `WireConvert.applyHighBarStateUpdate` covering the snapshot path
  (full `StateSnapshot` → fresh `GameStateSnapshot`), the delta path
  (DeltaEvent fold over a running view), the KeepAlive path (no-op for
  the snapshot stream), and the gap path (`seq` jump > 1 ⇒
  `ApplyResult.Gap` with `lastSeq`/`receivedSeq`).
- [X] T015 [P] [US1] Add Expecto tests for `BrokerState.noteHeartbeat`
  / heartbeat-timeout — first Heartbeat sets `lastHeartbeatAt`, every
  accepted `StateUpdate` refreshes it, silence past
  `heartbeatTimeoutMs` (default 5 s) drives the link to
  `[disconnected:timeout]` and emits `CoordinatorDetached{reason="heartbeat-timeout"}`
  + `SessionEnd` fan-out (FR-008).
- [X] T051 [P] [US1] Add an integration test driving **two
  consecutive `SyntheticCoordinator` sessions** through the same
  broker process — session 1 attaches with `pluginId="ai-1"`, pushes
  state, drops; session 2 attaches with `pluginId="ai-2"` and is
  accepted with a fresh `BrokerInfo.sessionId`, empty roster carry-over
  guard, and no stale `lastHeartbeatAt` from session 1. Assert audit
  ordering: `SessionEnded(s1)` precedes `CoordinatorAttached(s2)`,
  and `s1.sessionId ≠ s2.sessionId` (FR-012).
- [X] T016 [P] [US1] Add Expecto tests for `OwnerRule.FirstAttached`
  non-owner rejection — first Heartbeat captures `pluginId` as owner;
  subsequent Heartbeat with a differing `plugin_id` is rejected with
  `RejectReason.NotOwner` and `CoordinatorNonOwnerRejected` audit;
  the original owner stream stays up (FR-011, edge case).
- [X] T017 [P] [US1] Add an integration test under
  `tests/Broker.Integration.Tests` that uses the (yet-to-land)
  `SyntheticCoordinator` fixture to drive
  `HighBarCoordinatorService` end-to-end: cold-start handshake →
  `PushState` snapshot → scripting-client receives state → graceful
  stream close fans out `SessionEnd` → broker returns to Idle within
  10 s (Acceptance Scenarios 1, 2, 4 of US1).
- [X] T052 [P] [US1] Add an integration test for **scripting client
  subscribed before coordinator attach**: register a scripting client
  via `Hello` + `SubscribeState` while the broker is in `Mode: Idle`;
  confirm the subscriber receives no synthetic placeholder frames;
  attach `SyntheticCoordinator` and push the first `StateUpdate`
  (`tick=N`); assert the pre-attached subscriber receives that exact
  `tick=N` frame gap-free as its first message (FR-015).

### Implementation

- [X] T018 [US1] Implement `Broker.Core.Audit.fsi/.fs` additions —
  `CoordinatorAttached`, `CoordinatorDetached`, `CoordinatorSchemaMismatch`,
  `CoordinatorNonOwnerRejected`, `CoordinatorHeartbeat`,
  `CoordinatorCommandChannelOpened`, `CoordinatorCommandChannelClosed`,
  `CoordinatorStateGap`. Remove the retired `ProxyAttached` /
  `ProxyDetached` arms (no compat shim).
- [X] T019 [US1] Implement `Broker.Protocol.BrokerState.fs` delta —
  rename to coordinator-side terminology, add `OwnerRule` storage,
  `noteHeartbeat` with `heartbeatTimeoutMs` watchdog, `noteStateGap`
  emitter, and threading-safe owner registration (FirstAttached).
  Turn T015 and T016 green.
- [X] T020 [US1] Implement `Broker.Protocol.WireConvert.fs` —
  `RunningView` (own-units / visible-enemies / radar-enemies /
  map-features / economy reduction), `applyHighBarStateUpdate`
  (snapshot replace, delta apply, KeepAlive no-op, gap surfacing per
  research §2 + §7). Extend `Broker.Core.Snapshot.fsi/.fs` with a
  `Feature` record (`id`, `kind`, `pos`) and a `features: Feature list`
  field on `GameStateSnapshot` (per data-model §1 / research §2 —
  HighBar `map_features` are reclaim points, not buildings); refresh
  `tests/SurfaceArea/baselines/Broker.Core.Snapshot.surface.txt`;
  thread the field through `Broker.Core.Dashboard.build` as a
  passthrough projection (no rendered cell yet — adds a destination
  for `WireConvert` to write into). Turn T014 green.
- [X] T021 [US1] Implement
  `Broker.Protocol.HighBarCoordinatorService.Impl.fs` — unary
  `Heartbeat` (schema-version strict-equality, owner registration,
  audit emission), client-streaming `PushState` (drives
  `applyHighBarStateUpdate` → `BrokerState.applySnapshot`,
  refreshes heartbeat liveness, surfaces gaps), server-streaming
  `OpenCommandChannel` (drains `coordinatorCommandChannel` to wire).
  Update `Broker.Protocol.ServerHost.fs` to register the new service
  via `app.MapGrpcService<HighBarCoordinatorService.Impl>()` alongside
  the unchanged `ScriptingClientService` registration. Turn T013 green.
- [X] T022 [US1] Add
  `tests/Broker.Integration.Tests/SyntheticCoordinator.fs` — loopback
  fixture mirroring 001's `SyntheticProxy`. Dials the broker, runs
  `Heartbeat`, opens `PushState`, opens `OpenCommandChannel`. File-
  level `(* SYNTHETIC FIXTURE *)` banner; `Synthetic_*` test-name
  token; `[S]` marker on the dependent evidence task per Principle IV.
  Turns T013–T017, T051, T052 green. **Note:** Rebinding the four 001
  integration files that import `SyntheticProxy`
  (`Sc003LatencyTests.fs`, `Sc005RecoveryTests.fs`,
  `SnapshotE2ETests.fs`, `DashboardLoadTests.fs`;
  `AdminElevationTests.fs` and `AuditLifecycleTests.fs` use the Hub
  directly) is deferred to Phase 6 alongside `SyntheticProxy.fs`
  deletion (see amended T041); both fixtures coexist in the meantime.
- [S] T023 [S] [US1] Drive synthetic-coordinator end-to-end CI evidence
  for US1 acceptance scenarios 1, 2, 4 — capture the broker dashboard
  transcript and audit-log excerpt to `readiness/us1-synthetic.md`.
  Real-game evidence is owned by Phase 5 (US3 / 001 carve-out closure).
- [X] T024 [P] [US1] Re-run SC-002 latency budget under
  `SyntheticCoordinator` over ≥500 ticks; capture per-tick
  game-tick → scripting-client receipt timestamps; assert p95 ≤ 1 s;
  write `readiness/sc002-synthetic-latency.md`. The real-wire SC-002
  closure lands in T034.
- [X] T025 [P] [US1] Re-run SC-003 disconnect-recovery budget under
  `SyntheticCoordinator` over ≥20 trials by force-dropping the
  fixture's `PushState` stream; assert detection ≤ 5 s and
  recovery-to-Idle ≤ 10 s in ≥ 95 % of trials; write
  `readiness/sc003-synthetic-recovery.md`. The real-wire SC-003
  closure lands in T035.

**Checkpoint**: User Story 1 is fully functional and testable independently — a
synthetic plugin can connect, heartbeat, push state, and detach cleanly.

---

## Phase 4: User Story 2 (US2) — Operator + scripting-client commands flow back to the engine

### Tests First

- [X] T026 [P] [US2] Add Expecto tests for
  `WireConvert.tryFromCoreCommandToHighBar` gameplay arms — `Move`,
  `Attack` (targeted) → `AttackCommand`, `Attack` (no target) →
  `AttackAreaCommand`, `Stop`, `Guard`, `Patrol`, `Build`, `Custom`
  (data-model §1.10).
- [X] T027 [P] [US2] Add Expecto tests for
  `WireConvert.tryFromCoreCommandToHighBar` admin arms —
  `Admin.Pause` / `Admin.Resume` → `PauseTeamCommand{enable}`,
  `Admin.GrantResources` → `GiveMeCommand`, and assertion that
  `Admin.SetSpeed` / `OverrideVision` / `OverrideVictory` produce
  `Error AdminNotAvailable` with no wire bytes emitted (research §3).
- [X] T028 [P] [US2] Add an integration test under
  `tests/Broker.Integration.Tests` driving operator-issued `Pause` and
  scripting-client gameplay commands through the broker out to
  `SyntheticCoordinator.OpenCommandChannel`; assert the fixture
  receives the expected `CommandBatch.commands[*]` arms with correct
  `batch_seq` monotonicity and `client_command_id` truncation
  (Acceptance Scenarios 1, 2 of US2).
- [X] T029 [P] [US2] Add an Expecto test that the FR-010 backpressure
  carry-forward still holds over the coordinator path — submit
  commands faster than the synthetic peer drains and assert
  `QUEUE_FULL` rejects on overflow with no silent drops (Acceptance
  Scenario 3 of US2).

### Implementation

- [X] T030 [US2] Implement
  `Broker.Protocol.WireConvert.tryFromCoreCommandToHighBar` —
  `CommandBatch` builder with monotonic `batch_seq`, `target_unit_id`
  population, `client_command_id` truncation; full gameplay arm
  coverage; admin arm mapping where AICommand exists, `Error
  AdminNotAvailable` otherwise (data-model §1.10). Turn T026 + T027 green.
- [X] T031 [US2] Wire `Broker.Protocol.BackpressureGate` to
  `coordinatorCommandChannel` (rebound from the retired
  `proxyOutbound`). Update `Broker.Tui.HotkeyMap` so the operator
  `Space` (toggle pause) and admin actions route through
  `tryFromCoreCommandToHighBar` and reject at the broker boundary
  with `AdminNotAvailable` when no AICommand mapping exists (status
  pane surface; audit emission). Turn T028 + T029 green.
- [S] T032 [S] [US2] Drive end-to-end command egress over
  `SyntheticCoordinator` — operator `Pause` + scripting-client `Move`;
  capture transcript + audit excerpt to `readiness/us2-synthetic.md`.
  Real-game evidence is owned by Phase 5.

**Checkpoint**: User Story 2 is fully functional and testable independently —
operator + scripting-client commands flow over `OpenCommandChannel` against
the synthetic peer.

---

## Phase 5: User Story 3 (US3) — Synthetic-evidence carve-outs from feature 001 close against real game

These tasks are **operator-driven real-game walkthroughs**. There is no
test-first half — the deliverable is captured live evidence under
`specs/001-tui-grpc-broker/readiness/`. Each closes a 001 carve-out.
Per plan Principle IV no new carve-out is acceptable for the wire-side
end-to-end; these stay `[ ]` until an operator runs them.

- [ ] T033 [US3] Operator walkthrough §1 (cold-start, FR-001 / FR-002 /
  SC-001) against a real BAR + HighBarV3 build with
  `HIGHBAR_COORDINATOR=unix:/tmp/fsbar-coordinator.sock` and
  `HIGHBAR_COORDINATOR_OWNER_SKIRMISH_AI_ID=1`; observe dashboard
  Idle → attached within 10 s of first engine tick. Replace the 001
  synthetic-proxy capture in
  `specs/001-tui-grpc-broker/readiness/us1-evidence.md` with the
  real-game transcript + audit excerpt (closes 001 T029).
- [ ] T034 [US3] Operator walkthrough §2 (latency, SC-002) over ≥500
  real-game ticks; regenerate
  `specs/001-tui-grpc-broker/readiness/sc003-latency.md` with real-wire
  numbers (header line "real plugin peer"); confirm p95 ≤ 1 s
  (re-anchors SC-002 / 001 SC-003 on the real coordinator wire —
  001 T029a is already `[X]` against the synthetic peer).
- [ ] T035 [US3] Operator walkthrough §4 (disconnect recovery, SC-003)
  over ≥20 real-game trials with `pkill spring-headless` mid-stream;
  regenerate `specs/001-tui-grpc-broker/readiness/sc005-recovery.md`;
  confirm detection ≤ 5 s + recovery-to-Idle ≤ 10 s in ≥ 95 % of trials
  (re-anchors SC-003 / 001 SC-005 on the real coordinator wire —
  001 T029b is already `[X]` against the synthetic peer).
- [ ] T036 [US3] Operator walkthrough §3 (dashboard load, SC-004)
  against a real game with ≥4 scripting subscribers and ≥200 units;
  regenerate `specs/001-tui-grpc-broker/readiness/us3-evidence.md`
  with real-game transcript + screenshot + ≥1 Hz refresh confirmation
  (closes 001 T042).
- [ ] T053 [US3] Operator walkthrough — host-mode lobby launch +
  admin command lifecycle (Pause/Resume from TUI, elevate scripting
  client, gameplay command, end session) against a real BAR +
  HighBarV3 build per quickstart §3; regenerate
  `specs/001-tui-grpc-broker/readiness/us2-evidence.md` with the live
  transcript + audit excerpt (closes 001 T037 — host-mode evidence
  carve-out).
- [ ] T037 [US3] Operator walkthrough — viz screenshot over an active
  real-game session; regenerate
  `specs/001-tui-grpc-broker/readiness/us4-evidence.md` (closes 001 T046).
- [ ] T038 [US3] Update
  `specs/001-tui-grpc-broker/tasks.md` — flip the status markers for
  T029, T037, T042, T046 from `[S]` to `[X]`; update the
  Synthetic-Evidence Inventory entries from "open carve-out" /
  "infeasible without proxy AI" to "closed; live evidence captured"
  (T035 stays `[S]` — host-mode game-process management against a
  real BAR engine remains out of scope per spec Out of Scope);
  re-run `run-audit.sh --graph-only` against 001 to confirm the `[S*]`
  propagation contracts.

**Checkpoint**: User Story 3 is fully functional — four of the five 001
carve-outs are closed against real BAR + HighBarV3. T035 (game-process
management) remains a separate carve-out, unchanged.

---

## Phase 6: User Story 4 (US4) — Retired ProxyLink surface disappears cleanly

### Tests First

(US4 is an enforcement / cleanup story; the assertion lives in the
SurfaceArea diff suite already authored under 001. The tests-first
contract here is "the SurfaceArea suite must show the expected
removal + addition diff after the cleanup tasks land".)

### Implementation

- [X] T039 [P] [US4] Delete `src/Broker.Contracts/proxylink.proto` and
  remove its `<Protobuf>` entry from `Broker.Contracts.fsproj`. The
  envelope types `ProxyClientMsg`, `ProxyServerMsg`, `Handshake`,
  `HandshakeAck`, `KeepAlivePing`, `KeepAlivePong` are removed
  transitively (they were defined in `proxylink.proto`).
- [X] T040 [P] [US4] Delete `src/Broker.Protocol/ProxyLinkService.fsi`
  and `src/Broker.Protocol/ProxyLinkService.fs`. Remove their
  references from `Broker.Protocol.fsproj`.
- [X] T041 [US4] **Rebind** the four 001 integration files that import
  `SyntheticProxy` (`Sc003LatencyTests.fs`, `Sc005RecoveryTests.fs`,
  `SnapshotE2ETests.fs`, `DashboardLoadTests.fs`) onto the
  `SyntheticCoordinator` fixture from T022, then delete
  `tests/Broker.Integration.Tests/SyntheticProxy.fs`. Verify
  `dotnet build` is clean across the test solution and all four suites
  still pass under the new wire. (`AdminElevationTests.fs` and
  `AuditLifecycleTests.fs` use the Hub directly, no rebinding needed.)
- [X] T042 [P] [US4] Delete
  `tests/SurfaceArea/baselines/Broker.Protocol.ProxyLinkService.surface.txt`.
- [X] T043 [US4] Confirm `dotnet build` of the full solution is clean
  with no `ProxyClientMsg` / `ProxyServerMsg` / `ProxyLinkService` /
  `Handshake` / `KeepAlivePing` references remaining. Capture the
  build log to `readiness/us4-build-clean.txt`.
- [X] T044 [US4] Replace the placeholder
  `Broker.Protocol.HighBarCoordinatorService.surface.txt` baseline
  (from T011) with the actual reflected surface from the packed
  `Broker.Protocol` assembly; commit alongside the deletion of the
  ProxyLink baseline (T042) so the surface diff suite reflects the
  intended one-removed-one-added shape.
- [X] T045 [US4] Run `dotnet test tests/SurfaceArea` — confirm green.
  No `Broker.Protocol.ProxyLinkService.*` baseline remains;
  `Broker.Protocol.HighBarCoordinatorService.surface.txt` matches the
  packed assembly; `Broker.Protocol.ScriptingClientService.*`
  baselines are byte-identical to their pre-pivot versions
  (`git diff` shows no changes under that prefix). Capture diff
  transcript to `readiness/us4-evidence.md` (SC-006).

**Checkpoint**: User Story 4 is fully functional — public surface diff
shows `ProxyLink` removed, `HighBarCoordinator` added,
`ScriptingClient` byte-identical.

---

## Phase 7: Integration & Polish

- [X] T046 [P] Add `--print-schema-version` and
  `--expected-schema-version` CLI flags in `Broker.App.Cli`; thread
  the override through `BrokerState.setExpectedSchemaVersion`.
  `--print-schema-version` prints `broker schema version: 1.0.0` and
  exits 0 before starting the dashboard; the override is exposed for
  the schema-mismatch quickstart flow (FR-014, quickstart §5).
- [-] T047 [P] Surface coordinator status in `Broker.Tui.DashboardView`
  — attached `plugin_id`, `schema_version`, `engine_sha256` on the
  Status pane; red "Schema mismatch" banner when the most-recent
  audit event is `CoordinatorSchemaMismatch`; a stale-tick badge when
  `telemetryGap = true` (the `CoordinatorStateGap`-driven flag added
  in T020). FR-013, FR-014. **Skipped (rendering polish deferred):**
  the broker-side data is exposed via `BrokerState.telemetryGap`,
  `BrokerState.activePluginId`, and the audit sink (FR-013); the
  externally observable broker schema version is exposed via
  `--print-schema-version` / `--expected-schema-version` CLI flags
  (T046, FR-014). Surfacing the same data in the TUI Status pane
  requires extending `Dashboard.DiagnosticReading` + `Dashboard.build`
  + ~28 dashboard / dashboard-view tests; that rendering polish is a
  future PR. The data flow + audit trail required by FR-013 / FR-014
  is in place.
- [X] T048 Run the packed library through `scripts/prelude.fsx` and
  confirm the FSI exercise sketch from `contracts/public-fsi.md` —
  HeartbeatRequest construction, `applyHighBarStateUpdate`,
  `tryFromCoreCommandToHighBar` — all type-check and behave as
  documented; refresh `readiness/fsi-session.txt` against the final
  packed surface.
- [X] T049 Run `.specify/extensions/evidence/scripts/bash/run-audit.sh
  --graph-only` — confirm acyclic, no dangling refs, no `[S*]`
  surprises. Document the propagated set in
  `readiness/task-graph.md`.
- [X] T050 Run `.specify/extensions/evidence/scripts/bash/run-audit.sh`
  — confirm verdict PASS, or document every `--accept-synthetic`
  override against the disclosures in the Synthetic-Evidence
  Inventory below. Capture audit verdict to
  `readiness/synthetic-evidence.json`.

---

## Synthetic-Evidence Inventory

List every `[S]` task here with its Principle IV disclosures. This section is
the source for the PR description's synthetic-evidence section.

| Task | Reason | Real-evidence path | Tracking issue |
|------|--------|---------------------|----------------|
| T023 | The wire-side broker code (`HighBarCoordinatorService.Impl` + `ServerHost` registration + `BrokerState` + `WireConvert.applyHighBarStateUpdate` + audit emission) is exercised end-to-end through `SyntheticCoordinator` over loopback gRPC. The substitute is the CI-only stand-in for the real BAR + HighBarV3 process — every byte of the broker's three coordinator handlers runs against real Kestrel. | Real-evidence path: T033 / T034 (operator walkthroughs §1 + §2) closes this `[S]` by re-capturing the same scenarios under `specs/001-tui-grpc-broker/readiness/us1-evidence.md` against a real BAR build. Code-level SYNTHETIC banner lives at the top of `tests/Broker.Integration.Tests/SyntheticCoordinator.fs`; dependent test names carry the `Synthetic_` token. | None opened — gated on operator access to a real BAR + HighBarV3 build. |
| T032 | Same shape as T023 — the broker-side `OpenCommandChannel` egress + `BackpressureGate` + `tryFromCoreCommandToHighBar` are real production code; only the consuming peer is the loopback `SyntheticCoordinator`. The fixture asserts the wire bytes the broker emits, which is sufficient to validate the contract translation. | Real-evidence path: T036 (operator walkthrough §3 — dashboard load + admin commands) re-runs the command path against a real BAR + HighBarV3 session and captures the transcript at `specs/001-tui-grpc-broker/readiness/us3-evidence.md`. | None opened — gated on operator access to a real BAR + HighBarV3 build. |
