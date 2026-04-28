# Feature Tier — 003-elmish-mvu-core

**Tier**: **1 (contracted change)**

## Affected layer

Cross-cutting state/IO spine. Touches every project except `Lib` and
`Broker.Contracts`:

- `Broker.Mvu` (NEW — entire surface added)
- `Broker.Protocol` (REDUCED — `BrokerState.Hub` retired; new
  `BrokerState.Binding` Msg-translation surface; service `Impl`
  ctors take `Binding` not `Hub`; new `CoordinatorAdapterImpl` +
  `ScriptingAdapterImpl`)
- `Broker.Tui` (REDUCED — `TickLoop` shrunk to keypress-poll-and-render
  shell; `dispatch` table + `UiMode` + `VizController` retired;
  `DashboardView`/`LobbyView` accept Model fragments)
- `Broker.Viz` (EXTENDED — new `VizAdapterImpl`)
- `Broker.App` (UPDATED — composition root rewritten; new
  `AuditAdapterImpl`, `TimerAdapterImpl`, `LifecycleAdapterImpl`;
  `withLock`/`Hub.stateLock` plumbing removed)
- Test projects: `Broker.Mvu.Tests` (NEW), `Broker.Protocol.Tests`
  (rebound through `Binding`), `Broker.Tui.Tests` (rebound for the
  reduced `TickLoop`), `Broker.Integration.Tests` (rebound to live
  `MvuRuntime.Host`)

## Public-API impact

**Wire surface — UNCHANGED** (FR-019, FR-020):
- `highbar.v1.HighBarCoordinator` proto: byte-identical
- `fsbar.broker.v1.ScriptingClient` proto: byte-identical
- `Broker.Contracts.*` F# generated types: surface-area baselines
  must be byte-identical to feature-002-head

**F# public surface — REPLACED**:
- REMOVED: `Broker.Protocol.BrokerState.Hub` and every public
  function on it (`openHostSession`, `openGuestSession`,
  `closeSession`, `attachCoordinator`, `coordinatorCommandChannel`,
  `mode`, `roster`, `slots`, `session`, `attachProxy`,
  `proxyOutbound`, `noteHeartbeat`, `noteStateGap`,
  `refreshLiveness`, `lastHeartbeatAt`, `activePluginId`,
  `telemetryGap`, `clearTelemetryGap`, `applySnapshot`, `snapshots`,
  `togglePause`, `stepSpeed`, `sendToCoordinator`, `registerClient`,
  `unregisterClient`, `tryGetClient`, `liveClients`, `grantAdmin`,
  `revokeAdmin`, `bindSlot`, `unbindSlot`, `asCoreFacade`,
  `setExpectedSchemaVersion`, `expectedSchemaVersion`, `setOwnerRule`,
  `ownerRule`, `auditEmitter`, `brokerVersion`, `create`)
- REMOVED: `Session.CoreFacade` interface — superseded by `update`
- REMOVED: `Broker.Tui.TickLoop.UiMode`, `VizController`, `dispatch`
- REDUCED: `Broker.Protocol.BrokerState.fsi` to `OwnerRule`,
  `Binding`, `bind`, `postMsg`, `awaitResponse`, `init`
- REDUCED: `Broker.Tui.TickLoop.fsi` to a single `run` function
- UPDATED: `HighBarCoordinatorService.create` ctor takes
  `BrokerState.Binding` not `BrokerState.Hub`
- UPDATED: `ScriptingClientService.create` ctor takes
  `BrokerState.Binding` not `BrokerState.Hub`
- ADDED: entire `Broker.Mvu.*` namespace (Cmd, Msg, Model, Update,
  View, MvuRuntime, TestRuntime, six adapter interfaces, Fixtures)
- ADDED: production adapter implementation modules
  (`Broker.App.AuditAdapterImpl`, `Broker.App.TimerAdapterImpl`,
  `Broker.App.LifecycleAdapterImpl`,
  `Broker.Protocol.CoordinatorAdapterImpl`,
  `Broker.Protocol.ScriptingAdapterImpl`,
  `Broker.Viz.VizAdapterImpl`)

## Required evidence obligations

Per Constitution Principles I, II, IV, V, VI:

1. **Principle I (Spec → FSI → Tests → Impl)**: every new public
   `.fsi` exists before its `.fs` body; FSI exercise transcript
   captured to `readiness/transcripts/foundation-fsi-session.txt`
   (T014) and `readiness/transcripts/integration-fsi-session.txt`
   (T059).
2. **Principle II (Visibility lives in `.fsi`)**: surface-area
   baselines under `tests/SurfaceArea/baselines/`:
   - REGENERATED: `Broker.Protocol.BrokerState.surface.txt`,
     `Broker.Tui.TickLoop.surface.txt`,
     `Broker.Protocol.HighBarCoordinatorService.surface.txt`,
     `Broker.Protocol.ScriptingClientService.surface.txt`
   - NEW: 13+ baselines for `Broker.Mvu.*` modules and adapter impls
   - DELETED: any Hub-era baseline rows for the retired surface
   - INVARIANT: `Broker.Contracts.*` baselines byte-identical to
     feature-002-head (FR-019)
3. **Principle IV (Synthetic-evidence disclosure)**:
   - `Broker.Mvu.Testing.Fixtures` is `[S]` (T018) — synthetic
     `Model` + `Msg`-stream builders for the four carve-outs.
   - Banner comment in `Fixtures.fs` per data-model §6.1
   - Test-name token `Synthetic` on every fixture-driven test
   - Synthetic-Evidence Inventory row in `tasks.md`
   - PR description enumerates `[S]` tasks
4. **Principle V (Test evidence is mandatory)**: every FR maps to
   ≥1 Expecto test:
   - FR-001..FR-008 → `tests/Broker.Mvu.Tests/UpdateTests.fs`
   - FR-009..FR-011 + FR-016 → `ViewTests.fs`
   - FR-015, FR-017 → `RuntimeTests.fs`
   - FR-012..FR-014, FR-018 → `Broker.Integration.Tests` rebound
     to live `MvuRuntime.Host`
   - SC-008 → `HubRetirementGuardTests.fs` (greppable assertion)
   - FR-021 → carve-out closures regenerate
     `specs/001-tui-grpc-broker/readiness/` artefacts
5. **Principle VI (Observability)**: per-effect-family failure
   `Msg` arms (FR-008); `MailboxHighWater` rate-limited audit;
   `RuntimeStarted`/`RuntimeStopped` lifecycle audits; view-error
   panel rendered as data, not as an exception.

## Carve-out closures from feature 001

This feature closes:
- T029 — broker–proxy end-to-end transcript (MVU-replay)
- T037 — host-mode admin walkthrough (MVU-replay)
- T042 — dashboard under load (MVU-replay)
- T046 — viz status line (MVU-replay)

Does NOT close:
- T035 — host-mode game-process management against a real BAR
  engine. Environment-provisioning gap, not a state-shape gap.
  Tracked as remaining carve-out.

## Tier 1 obligation summary

- `[ ]` Spec authored (`spec.md`)
- `[ ]` Plan authored (`plan.md`)
- `[ ]` Research authored (`research.md`)
- `[ ]` Data model authored (`data-model.md`)
- `[ ]` Contracts authored (`contracts/public-fsi.md`)
- `[ ]` Quickstart authored (`quickstart.md`)
- `[ ]` All `.fsi` drafted before any `.fs` body
- `[ ]` FSI transcript captured (foundation + integration)
- `[ ]` Surface-area baselines refreshed
- `[ ]` Synthetic-Evidence Inventory complete
- `[ ]` `speckit.graph.compute` clean (no cycles, no `[S*]` surprises)
- `[ ]` `speckit.evidence.audit` PASS
- `[ ]` PR description enumerates `[S]` tasks + SC evidence locations
