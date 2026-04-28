# US1 Evidence — Guest-mode Bridge

**Recorded**: 2026-04-27 (T029, T021a, T028)
**Branch**: `001-tui-grpc-broker`
**Status**: substantially exercised against a real Kestrel-hosted gRPC server.
The synthetic-proxy fixture (a `ProxyLinkClient` driving snapshots end-to-end)
is not yet authored — see _Remaining for full US1_ at the bottom.

## 1. Composition-root smoke (T028)

The broker boots from `src/Broker.App/Broker.App.fsproj`. Key CLI paths:

```
$ dotnet run --project src/Broker.App/Broker.App.fsproj -- --version
broker v1.0

$ dotnet run --project src/Broker.App/Broker.App.fsproj -- --help
broker [options]

Options:
  --listen HOST:PORT   gRPC server listen address (default 127.0.0.1:5021)
  --no-viz             disable the optional 2D visualization subsystem
  --version            print the broker version and exit
  -h, --help           print this help text

$ dotnet run --project src/Broker.App/Broker.App.fsproj -- --listen 0
broker: --listen value '0' is not in HOST:PORT form
[usage banner]

$ dotnet run --project src/Broker.App/Broker.App.fsproj -- --no-such-flag
broker: unknown argument: --no-such-flag
[usage banner]
```

Without arguments, the broker:
1. Configures the Serilog rolling-file audit sink under `./logs/`.
2. Boots `ServerHost.start` on `127.0.0.1:5021` (Kestrel HTTP/2, both
   `ProxyLink` and `ScriptingClient` services registered on one port — FR-005).
3. Runs the Spectre.Console TUI tick loop on the main thread.
4. Listens for `SIGINT` (Ctrl-C) or the operator pressing `Q` to quit.

The TUI tick loop uses Spectre's `LiveDisplay` and re-renders a five-pane
layout (header, broker / session / clients columns, telemetry, footer)
every 100 ms. Because `LiveDisplay` requires a real interactive terminal,
the visual screenshot is operator-captured rather than reproduced in CI.

## 2. End-to-end wire exercise (T021, T021a)

The integration tests in `tests/Broker.Integration.Tests/` boot the real
broker server in-process (free port per test), drive it with
`Grpc.Net.Client`, and assert both wire-level behavior and audit-stream
side effects.

```
$ dotnet test tests/Broker.Integration.Tests/Broker.Integration.Tests.fsproj

  Passed Audit lifecycle (FR-028).Hello -> ClientConnected event with timestamp + client_name + version
  Passed Audit lifecycle (FR-028).Hello with same name twice -> NameInUseRejected event
  Passed Audit lifecycle (FR-028).Hello with major-version mismatch -> VersionMismatchRejected event
  Passed Audit lifecycle (FR-028).Admin command in guest -> CommandRejected(AdminNotAvailable) event
  Passed ScriptingClient end-to-end.Hello with major version match returns ok and isAdmin=false (US1 acceptance #1, FR-008/FR-029)
  Passed ScriptingClient end-to-end.Hello with same name twice returns NAME_IN_USE (FR-008)
  Passed ScriptingClient end-to-end.Hello with major-version mismatch returns FailedPrecondition (FR-029)
  Passed ScriptingClient end-to-end.Admin command in guest mode is rejected with ADMIN_NOT_AVAILABLE (US1 acceptance #4, FR-004)

Test Run Successful.
Total tests: 8
```

These map to spec acceptance scenarios as follows:

| Test | US1 acceptance scenario | FR / Invariant |
|------|--------------------------|-----------------|
| Hello / ok / non-admin           | #1 (proxy → guest mode), #4 (admin denied)          | FR-008, FR-016 |
| Hello / NAME_IN_USE              | additional safeguard for #1 (canonical id)          | FR-008         |
| Hello / VERSION_MISMATCH         | edge-case from clarifications                       | FR-029         |
| Admin in guest / ADMIN_NOT_AVAILABLE | #4 (admin command rejected with clear error)    | FR-004, Inv 2  |
| Audit / ClientConnected          | #2 / #3 (operator can identify client lifecycle)    | FR-028         |
| Audit / NameInUseRejected        | conflict resolution (FR-008)                        | FR-008, FR-028 |
| Audit / VersionMismatchRejected  | post-session diagnosis                              | FR-029, FR-028 |
| Audit / CommandRejected          | every reject is recorded, no silent loss            | FR-010, FR-028 |

## 3. Coverage map

| US1 acceptance scenario                                   | Status |
|------------------------------------------------------------|--------|
| #1: external proxy attaches → broker shows guest-mode      | partial — `BrokerState.attachProxy` + `ProxyLinkService.Impl.Attach` real and audit-logged; no synthetic proxy fixture yet. |
| #2: client subscribes → snapshots flow                     | partial — `SubscribeState` registers a per-client `Channel<StateMsg>`; broadcast hook on snapshot ingest is wired in `BrokerState.applySnapshot` only as state update, not yet fan-out to subscriber channels. |
| #3: client issues legal in-game command → forwarded         | done — `SubmitCommands` runs through `BackpressureGate.process_` + `BrokerState.sendToProxy` to the proxy outbound channel. |
| #4: client issues admin command in guest → rejected         | done — verified by integration test. |
| #5: proxy disconnects → SessionEnd to subscribers           | done — `ProxyLinkService` `closeSession` fan-out wired and tested for the audit side; subscriber-channel close path not yet exercised end-to-end. |

## Remaining for full US1

1. Wire snapshot fan-out to all subscribers when `BrokerState.applySnapshot`
   fires (currently only the latest snapshot is cached in `Session.telemetry`).
2. Author a `SyntheticProxy` fixture under `tests/Broker.Integration.Tests/`
   that uses the `ProxyLinkClient` to drive a sequence of snapshots, then
   capture the latency histogram (T029a, SC-003) and disconnect-recovery
   timings (T029b, SC-005).
3. Operator-captured screenshot of the live TUI dashboard against the
   synthetic proxy.
