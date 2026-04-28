# Feature Baseline — 002-highbar-coordinator-pivot

**Date**: 2026-04-28
**Spec**: [`../spec.md`](../spec.md)
**Plan**: [`../plan.md`](../plan.md)

## Tier classification

**Tier 1 (contracted change).** The feature removes the public
`fsbar.broker.v1.ProxyLink` proto + its `Broker.Protocol.ProxyLinkService`
F# surface; adds the public `highbar.v1.HighBarCoordinator` server-side
service + `Broker.Protocol.HighBarCoordinatorService`; adds and removes
the corresponding surface-area baselines. Per Constitution §"Change
Classification" this requires the full artifact chain: spec, plan,
`.fsi` updates, surface-area baseline updates, test evidence, and
documentation updates.

## Affected layers

| Layer | Project | Change |
|-------|---------|--------|
| Wire contracts | `src/Broker.Contracts/` | Vendor `highbar/*.proto` (5 files); remove `proxylink.proto`; add `HIGHBAR_PROTO_PIN.md` mirror; ProtoPin drift test under `Broker.Contracts.Tests`. |
| Domain | `src/Broker.Core/` | Additive only — `Audit.AuditEvent` gains `Coordinator*` arms; `CommandPipeline.RejectReason` gains `SchemaMismatch` + `NotOwner` arms; `Snapshot.GameStateSnapshot` gains a `features` list (per data-model §1). |
| Protocol | `src/Broker.Protocol/` | Add `HighBarCoordinatorService.fsi/.fs`; rewrite `WireConvert` to add HighBar direction + drop ProxyLink helpers; rename `BrokerState.attachProxy` → `attachCoordinator`, `proxyOutbound` → `coordinatorCommandChannel`, `sendToProxy` → `sendToCoordinator`; add `OwnerRule`, `noteHeartbeat`, `noteStateGap`; remove `ProxyLinkService.fsi/.fs`; rebind `ServerHost` registration. |
| TUI | `src/Broker.Tui/` | Surface coordinator status (plugin_id, schema version, engine SHA) + state-gap badge in `DashboardView`. |
| Viz | `src/Broker.Viz/` | No changes. |
| App | `src/Broker.App/` | Add `--print-schema-version` and `--expected-schema-version` CLI flags (FR-014); composition root rewires automatically once Protocol wiring updates. |
| Integration tests | `tests/Broker.Integration.Tests/` | Replace `SyntheticProxy.fs` with `SyntheticCoordinator.fs`; rebind six 001 test files (`Sc003LatencyTests`, `Sc005RecoveryTests`, `SnapshotE2ETests`, `AdminElevationTests`, `AuditLifecycleTests`, `DashboardLoadTests`). |
| Surface-area | `tests/SurfaceArea/baselines/` | Add `Broker.Protocol.HighBarCoordinatorService.surface.txt`; remove `Broker.Protocol.ProxyLinkService.surface.txt`; refresh `Broker.Core.Snapshot.surface.txt` for the new `features` field. |
| Audit log | rolling-file sink (unchanged location) | New event variants: `CoordinatorAttached/Detached/SchemaMismatch/NonOwnerRejected/Heartbeat/CommandChannelOpened/Closed/StateGap`. Retire `ProxyAttached/Detached`. |

## Public-API surface impact

**Removed (Tier 1 — published types disappearing):**

- F# module `Broker.Protocol.ProxyLinkService` (entire `.fsi`).
- F# module `FSBarV2.Broker.Contracts` envelope types `ProxyClientMsg`,
  `ProxyServerMsg`, `Handshake`, `HandshakeAck`, `KeepAlivePing`,
  `KeepAlivePong` (defined in `proxylink.proto`).
- proto file `fsbar/broker/v1/proxylink.proto`.
- Audit-event arms `ProxyAttached`, `ProxyDetached`.
- Surface baseline `tests/SurfaceArea/baselines/Broker.Protocol.ProxyLinkService.surface.txt`.

**Added:**

- F# module `Broker.Protocol.HighBarCoordinatorService` (server-side
  service wrapper + `Service` / `Config` / `Impl` types).
- F# namespace `Highbar.V1.*` (generated from vendored upstream protos).
- Public functions added to `WireConvert` (`RunningView`, `ApplyResult`,
  `applyHighBarStateUpdate`, `tryFromCoreCommandToHighBar`).
- Public functions added to `BrokerState` (`OwnerRule`,
  `expectedSchemaVersion`, `setExpectedSchemaVersion`, `ownerRule`,
  `setOwnerRule`, `attachCoordinator`, `noteHeartbeat`, `noteStateGap`,
  `coordinatorCommandChannel`, `sendToCoordinator`).
- Audit-event arms `CoordinatorAttached/Detached/SchemaMismatch/NonOwnerRejected/Heartbeat/CommandChannelOpened/Closed/StateGap`.
- `RejectReason` arms `SchemaMismatch`, `NotOwner`.
- `Snapshot.Feature` record + `Snapshot.GameStateSnapshot.features` field.
- CLI flags `--print-schema-version`, `--expected-schema-version`.
- Surface baseline `tests/SurfaceArea/baselines/Broker.Protocol.HighBarCoordinatorService.surface.txt`.

**Unchanged (load-bearing for spec FR-007 / SC-006):**

- Entire `ScriptingClient` proto + F# surface — byte-identical baseline
  required.

## Required evidence obligations

Per Constitution Principle V + §"Quality Gates":

| Gate | Owner task(s) | Evidence type |
|------|---------------|---------------|
| Specification | already passed (`spec.md` complete; checklist `[X]`-stamped) | n/a |
| Planning | already passed (`plan.md` Constitution Check ✅) | n/a |
| Task | T049 (graph compute), already validated (53 tasks, acyclic, 0 dangling refs) | `readiness/task-graph.{json,md}` |
| Implementation | per-task `[X]` / `[S]` markers in `tasks.md` with synthetic disclosures | source code + tests + `readiness/` artifacts |
| Evidence | T050 (audit) — verdict PASS, or every `[S]` covered by an explicit `--accept-synthetic` override | `readiness/synthetic-evidence.json` |

## Synthetic-evidence policy

Plan §IV is non-negotiable: **no new wire-side end-to-end carve-out is
acceptable**. The two `[S]`-eligible tasks in this feature
(T023, T032) cover only the **CI-side `SyntheticCoordinator` substrate**
because real BAR + HighBarV3 cannot run on CI (binary not provisioned,
GL context unavailable in headless containers). Real-game closure is
owned by Phase 5 (T033–T037, T053).

001 carve-outs T029 / T037 / T042 / T046 close in this feature via
T033, T036a, T034 (re-anchor), T035 (re-anchor), T036, T037, and
T038's status-flip step. T035 (game-process management against a
real BAR engine) remains an open 001 carve-out — explicitly out of
scope per spec Out of Scope.

## Out-of-scope reminders

- Spawning the BAR engine binary (001 T035 — separate future feature).
- Authentication / encryption beyond loopback (separate future feature).
- Bridging `HighBarProxy` / `HighBarAdmin` embedded gateway (separate
  future feature; see research §10).
- Backwards compatibility with the retired `ProxyLink` proto (clean
  break per spec Assumptions).
- Multi-AI broker (one broker, multiple coordinator sessions) — carried
  forward from 001 as out of scope.
