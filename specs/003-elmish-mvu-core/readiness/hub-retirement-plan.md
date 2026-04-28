# Hub Retirement Plan

**Feature**: 003-elmish-mvu-core
**Driver**: FR-001 (Hub retired as state container), FR-003
(`withLock` removed), SC-008 (greppable confirmation),
spec Assumptions ("the pivot ships as a single feature", "halfway
pivot explicitly avoided")

## Removed surface — `Broker.Protocol.BrokerState`

These public members of the `BrokerState.Hub` type and module are
deleted in the same change set that lands `Broker.Mvu`:

### Type
- `BrokerState.Hub` (entire opaque type — and the underlying mutable
  record + `stateLock` `obj`)

### Construction / accessors
- `BrokerState.create`
- `BrokerState.brokerVersion`
- `BrokerState.auditEmitter`
- `BrokerState.mode`
- `BrokerState.roster`
- `BrokerState.slots`
- `BrokerState.session`
- `BrokerState.expectedSchemaVersion`
- `BrokerState.setExpectedSchemaVersion`
- `BrokerState.ownerRule`
- `BrokerState.setOwnerRule`

### Mutations
- `BrokerState.openHostSession`
- `BrokerState.openGuestSession`
- `BrokerState.launchHostSession`
- `BrokerState.closeSession`
- `BrokerState.attachCoordinator`
- `BrokerState.noteHeartbeat`
- `BrokerState.noteStateGap`
- `BrokerState.refreshLiveness`
- `BrokerState.lastHeartbeatAt`
- `BrokerState.activePluginId`
- `BrokerState.telemetryGap`
- `BrokerState.clearTelemetryGap`
- `BrokerState.applySnapshot`
- `BrokerState.snapshots`
- `BrokerState.togglePause`
- `BrokerState.stepSpeed`
- `BrokerState.coordinatorCommandChannel`
- `BrokerState.sendToCoordinator`
- `BrokerState.registerClient`
- `BrokerState.unregisterClient`
- `BrokerState.tryGetClient`
- `BrokerState.liveClients`
- `BrokerState.grantAdmin`
- `BrokerState.revokeAdmin`
- `BrokerState.bindSlot`
- `BrokerState.unbindSlot`
- `BrokerState.asCoreFacade`

### Companion types
- `BrokerState.ClientChannel` (the per-client `subscriber: Channel<StateMsg>` mutable record — superseded by adapter-owned per-client channel)

### `Session.CoreFacade` interface (in `Broker.Core.Session`)

Retired alongside Hub — every method on `CoreFacade` is replaced by
a `Msg` arm dispatched through `MvuRuntime`:

- `Session.CoreFacade.Mode` (read) — superseded by `model.mode`
- `Session.CoreFacade.Roster` (read) — superseded by `model.roster`
- `Session.CoreFacade.Slots` (read) — superseded by `model.slots`
- `Session.CoreFacade.BrokerVersion` (read) — superseded by `model.brokerInfo.version`
- `Session.CoreFacade.OnSnapshot` — `Msg.CoordinatorInbound.PushStateSnapshot`
- `Session.CoreFacade.OnClientConnected` — `Msg.ScriptingInbound.Hello`
- `Session.CoreFacade.OnClientDisconnected` — `Msg.ScriptingInbound.Disconnected`
- `Session.CoreFacade.OperatorOpenHost` — `Msg.TuiInput` for `L`/`Enter`
- `Session.CoreFacade.OperatorLaunchHost` — `Msg.TuiInput` for `Enter`
- `Session.CoreFacade.OperatorTogglePause` — `Msg.TuiInput` for `Space`
- `Session.CoreFacade.OperatorStepSpeed` — `Msg.TuiInput` for `+/-`
- `Session.CoreFacade.OperatorEndSession` — `Msg.TuiInput` for `X`
- `Session.CoreFacade.OperatorGrantAdmin` — `Msg.TuiInput` (clients pane → admin grant)
- `Session.CoreFacade.OperatorRevokeAdmin` — `Msg.TuiInput` (clients pane → admin revoke)

### `Broker.Tui.TickLoop`

Retired surface:
- `TickLoop.UiMode` (now `Model.pendingLobby` carries the in-flight lobby draft)
- `TickLoop.VizController` (now `MvuRuntime` calls `VizAdapter` directly)
- `TickLoop.dispatch` (now `MvuRuntime.postMsg` + `update`)

Reduced surface — only `TickLoop.run` remains, with a new signature
(takes `MvuRuntime.Host` instead of `Session.CoreFacade`).

## SC-008 greppable confirmation

After the change set lands, the following greps must return
**zero hits** (excluding `specs/`, `tasks.md`, this plan, and any
historical-reference comments that explicitly cite "removed in 003"):

```bash
# Hub-mutating assignments
grep -rn 'Hub\.session\s*<-' src/ tests/
grep -rn 'Hub\.mode\s*<-' src/ tests/
grep -rn 'Hub\.roster\s*<-' src/ tests/
grep -rn 'Hub\.slots\s*<-' src/ tests/

# Lock acquisitions on broker state
grep -rn 'withLock' src/ tests/
grep -rn 'stateLock' src/ tests/
grep -rn 'Monitor\.Enter.*hub' src/ tests/

# Hub type itself
grep -rn 'BrokerState\.Hub' src/ tests/
grep -rn 'Hub\.create' src/ tests/

# CoreFacade
grep -rn 'CoreFacade' src/ tests/
grep -rn 'asCoreFacade' src/ tests/

# Hub-attached methods
grep -rn 'attachCoordinator' src/ tests/
grep -rn 'attachProxy' src/ tests/
grep -rn 'openHostSession' src/ tests/
grep -rn 'openGuestSession' src/ tests/
grep -rn 'closeSession\b' src/ tests/
grep -rn 'coordinatorCommandChannel' src/ tests/
```

These greps run in `tests/Broker.Mvu.Tests/HubRetirementGuardTests.fs`
(T033) which fails the test suite if any hit is found outside the
specs directory.

## Test-rebinding plan

All test projects that referenced `Hub` need rebinding through
`MvuRuntime.Host` or its `BrokerState.Binding` translation layer:

| Test project                   | What changes                                                                |
|--------------------------------|----------------------------------------------------------------------------|
| `Broker.Core.Tests`            | No change (Hub never lived here)                                            |
| `Broker.Protocol.Tests`        | All tests rebound: construct `MvuRuntime.Host` with test-stub adapters; build `BrokerState.Binding` over it; pass to service `create` (T049) |
| `Broker.Tui.Tests`             | `TickLoop` tests retired alongside `dispatch` table; new tests against the reduced `TickLoop.run` (T050) |
| `Broker.Integration.Tests`     | `SyntheticCoordinator`, `CoordinatorLoadTests`, `ScriptingClientFanoutTests` rebound to live `MvuRuntime.Host` with production adapters (T051) |
| `Broker.Mvu.Tests`             | New project — exercises `update` and `view` directly (T019..T031) |
| `SurfaceArea`                  | Baselines refreshed: `BrokerState.surface.txt` shrinks; `Broker.Mvu.*` baselines added (T015 done; T058 verifies post-Hub-removal) |

## Composition-root sequencing (T047)

`Broker.App.Program.run` is rewritten in this order:

1. Build `Model` from CLI args (`Model.init brokerInfo config startedAt`).
2. Construct production adapters:
   - `AuditAdapterImpl.create logger`
   - `TimerAdapterImpl.create ()`
   - `LifecycleAdapterImpl.create cts.Cancel`
   - `CoordinatorAdapterImpl.create coordinatorService`
   - `ScriptingAdapterImpl.create perClientCapacity postMsg`
   - `VizAdapterImpl.create controller` (or no-op when `--no-viz`)
3. Pack into `MvuRuntime.AdapterSet`.
4. Construct `host = MvuRuntime.create model adapterSet`.
5. Construct `binding = BrokerState.bind host` and pass to gRPC services.
6. Start gRPC server (`ServerHost.start`).
7. Start runtime (`MvuRuntime.start host cts.Token`).
8. Run `TickLoop.run host cts.Token` on the main thread.
9. On `Q` or `SIGINT`: `MvuRuntime.shutdown host "user-requested"`.

The `withLock`/`Hub.stateLock` plumbing from feature 001 is removed
in the same edit; no transition period.
