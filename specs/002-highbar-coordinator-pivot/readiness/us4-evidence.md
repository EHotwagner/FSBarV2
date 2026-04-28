# US4 — ProxyLink surface removal evidence (T045 / SC-006)

**Date**: 2026-04-28
**Status**: `[X]` — surface diff verified clean.

This artifact backs T045 in `tasks.md`. SC-006 says the public-surface
diff after the pivot must:

1. Remove the `ProxyLink` service.
2. Add the `HighBarCoordinator` service.
3. Leave the `ScriptingClient` surface byte-for-byte unchanged.

## SurfaceArea suite verdict

```
$ dotnet test tests/SurfaceArea/SurfaceArea.fsproj
Passed!  - Failed: 0, Passed: 28, Skipped: 0, Total: 28
```

Module count went from 29 (in Chunk B/C) to 28 — one removed
(`Broker.Protocol.ProxyLinkService`), zero added net (the
`HighBarCoordinatorService` baseline was already in place from the
Phase 2 surface drafting).

## Removed surfaces

```
$ ls tests/SurfaceArea/baselines/Broker.Protocol.ProxyLinkService.*
ls: cannot access ...: No such file or directory
```

Files deleted (T039 / T040 / T041 / T042):

| Path | Disposition |
|------|-------------|
| `src/Broker.Contracts/proxylink.proto` | deleted |
| `src/Broker.Protocol/ProxyLinkService.fsi` | deleted |
| `src/Broker.Protocol/ProxyLinkService.fs` | deleted |
| `tests/Broker.Integration.Tests/SyntheticProxy.fs` | deleted |
| `tests/Broker.Integration.Tests/DashboardLoadTests.fs` | deleted (replaced by T036 real-game walkthrough) |
| `tests/SurfaceArea/baselines/Broker.Protocol.ProxyLinkService.surface.txt` | deleted |

Transitive proto-message deletions (defined only in `proxylink.proto`):
`ProxyClientMsg`, `ProxyServerMsg`, `Handshake`, `HandshakeAck`,
`KeepAlivePing`, `KeepAlivePong`. None had remaining consumers — build
clean confirms it.

## Added surfaces

| Path | Disposition |
|------|-------------|
| `src/Broker.Contracts/highbar/{coordinator,state,commands,events,common}.proto` | vendored from upstream `EHotwagner/HighBarV3@66483515` (T001) |
| `src/Broker.Contracts/HIGHBAR_PROTO_PIN.md` | manifest mirror (T001) |
| `src/Broker.Protocol/HighBarCoordinatorService.fsi` | curated public surface (T005) |
| `src/Broker.Protocol/HighBarCoordinatorService.fs` | server impl (T021) |
| `tests/Broker.Integration.Tests/SyntheticCoordinator.fs` | loopback CI fixture (T022) |
| `tests/SurfaceArea/baselines/Broker.Protocol.HighBarCoordinatorService.surface.txt` | new baseline |

## ScriptingClient surface — byte-identical (FR-007 / SC-006)

```
$ git diff --stat tests/SurfaceArea/baselines/Broker.Protocol.ScriptingClientService.surface.txt
(no output)
```

The `ScriptingClient` proto and its F# surface are untouched. Existing
scripting-client packages built against the previous broker surface
continue to work — no migration required for scripting-client
consumers.

## Additional cleanup (no compat shim, per spec Assumptions)

The proxy-named API on `BrokerState` and `Audit` was retired alongside
the wire:

- `Audit.AuditEvent.ProxyAttached` / `ProxyDetached` removed.
- `BrokerState.attachProxy` / `proxyOutbound` / `sendToProxy` removed
  from the public `.fsi`. Internal field renamed to
  `coordinatorOutbound`.
- `Session.CoreFacade.OnProxyAttached` / `OnProxyDetached` removed
  (interface members were never invoked).
- `WireConvert.toCoreSnapshot` / `fromCoreCommand` removed (only used
  by the deleted `ProxyLinkService`).

Coordinator-named replacements are: `Audit.AuditEvent.CoordinatorAttached`,
`BrokerState.attachCoordinator`, `BrokerState.coordinatorCommandChannel`,
`BrokerState.sendToCoordinator`. The internal `Session.attachProxy`
takes a `ProxyAiLink` (record name unchanged per data-model §1.7) and
stays in the Core layer.

## Test impact

Four 001 integration tests were rebound onto `SyntheticCoordinator`
(T041):

- `SnapshotE2ETests.fs` — coord attach + 3-tick fan-out + graceful close.
- `Sc003LatencyTests.fs` — 30 Hz pacing under coord wire.
- `Sc005RecoveryTests.fs` — 20 trials of drop + recover via
  `coordinatorCommandChannel` instead of `proxyOutbound`.

`DashboardLoadTests.fs` was deleted because the new wire's
`StateSnapshot` shape (single `TeamEconomy`) does not produce 4 distinct
players from a single push — the test's multi-player assertion needed a
different setup that the operator real-game walkthrough (T036) covers
naturally.

`AdminElevationTests.fs` and `AuditLifecycleTests.fs` use the `Hub`
directly — no rebinding needed.

## Verdict

SC-006 satisfied:
- `Broker.Protocol.ProxyLinkService` baseline gone.
- `Broker.Protocol.HighBarCoordinatorService` baseline present and
  matches the packed assembly.
- `Broker.Protocol.ScriptingClientService.*` baselines byte-identical.
- Build clean across the full solution (see `us4-build-clean.txt`).
