# US1 Progress Snapshot

**Recorded**: 2026-04-27
**Branch**: `001-tui-grpc-broker`

This is the honest state of US1 after one /speckit-implement session.

## Done

| Task | Status | Evidence |
|------|--------|----------|
| T001–T006 | `[X]` | Phase 1 setup — solution builds, 14 projects, `net10.0`. |
| T007–T015 | `[X]` | Phase 2 foundation — proto codegen, all `.fsi` surfaces, 24 surface-area baselines passing, FSI transcript captured (`readiness/fsi-session.txt`), failure-diagnostics doc. |
| T016 | `[X]` | `VersionHandshake.check` Expecto matrix: 5 tests green. |
| T017 | `[X]` | `ScriptingRoster` Expecto: 7 tests green (FR-008, FR-016, Invariant 4). |
| T018 | `[X]` | `ParticipantSlot.checkBindings`/`tryBind`/`unbind` Expecto: 7 tests green (FR-009, Invariant 1). |
| T019 | `[X]` | `CommandPipeline.tryEnqueue` + `authorise` Expecto: 9 tests green (FR-004, FR-009, FR-010, FR-016). |
| T020 | `[X]` | `Snapshot.isStrictlyAfter` + `mapMetaOnFirstOnly` Expecto: 5 tests green (FR-006, Invariant 5). |
| T022 | `[X]` | `Mode`, `ScriptingRoster`, `ParticipantSlot`, `Audit` `.fs` bodies real; T016–T018 green. |
| T023 | `[X]` | `CommandPipeline` (BoundedChannel), `Snapshot` `.fs` bodies real; T019, T020 green. |
| T024 | `[X]` | `Session` real — guest-mode transitions, host-mode launching/active, `attachProxy`, `applySnapshot`, `end_`, `toReading`. |

Plus the following implementation pieces were landed even though their owning task is still `[ ]` overall:

- `Broker.Core.Lobby.validate` real (helps unblock T030/T033).
- `Broker.Core.Dashboard.build` real (helps unblock T040).
- `Broker.Protocol.BackpressureGate.create`/`process_` real — delegates to `CommandPipeline.authorise` + `tryEnqueue`. (Part of T026.)
- `Broker.App.Cli.parse`/`usage`, `Broker.App.Logging.configure`/`writeAudit` real (Serilog rolling-file sink wired). (Part of T028.)
- `Broker.Tui.HotkeyMap.map` real — covers Q/V/L/Enter/Space/+/- per spec. (Part of T028.)

## Test totals

```
Broker.Core.Tests          30 / 30 passing
Broker.Protocol.Tests       5 /  5 passing
SurfaceArea                24 / 24 passing
Lib.Tests                   2 /  2 passing
                           --
Total                      61 passing, 0 failing
```

## Not yet done — what blocks each

| Task | Blocking factor |
|------|-----------------|
| T021 | Needs T025 (Kestrel + grpc-aspnetcore hosting), T026 (`ScriptingClientService` 4 RPCs incl. bidi-stream), T027 (`ProxyLinkService.Attach` bidi-stream) all real. |
| T021a | Needs T026/T027 to actually emit the audit events through `Logging.writeAudit`. |
| T025 (rest) | `ServerHost.start` — Kestrel listener, `services.AddGrpc()`, `app.MapGrpcService<T>()` for both services from F# composition. Non-trivial because the F# generated bases are `ScriptingClientBase` / `ProxyLinkBase` (Grpc.Core abstract classes) — must subclass + override. |
| T026 (rest) | `ScriptingClientService` derived from `Broker.Contracts.ScriptingClient.ScriptingClientBase`: bidirectional streaming `SubmitCommands` per-client queue + flow control, server-streaming `SubscribeState`, unary `Hello`/`BindSlot`/`UnbindSlot`. |
| T027 | `ProxyLinkService` derived from `Broker.Contracts.ProxyLink.ProxyLinkBase`: bidi `Attach` consuming `ProxyClientMsg`, producing `ProxyServerMsg`; keepalive timer; on detach broadcast `SessionEnd` to all subscribed scripting clients. |
| T028 (rest) | `Layout.rootLayout` and `DashboardView.render` (Spectre.Console widget tree), `TickLoop.run` (single-thread render+input), `Program.main` (wires Logging+Core+Protocol+Tui+optional Viz). |
| T029 / T029a / T029b | All require a running broker — depend on T025–T028. Fixture, latency capture, and recovery measurement all run against the broker process. |
| Phase 4 (T030–T037) | Tests-first tasks (T030–T032) are tractable now that Lobby + Mode are real; impl tasks (T033–T036) extend the same Core/Protocol surface; T037 needs the broker process. |
| Phase 5 (T038–T042) | T040 partially landed (Dashboard.build); T038/T039/T041/T042 depend on TUI + a runnable broker. |
| Phase 6 (T043–T046) | Viz probe + scene builder need both the SkiaViewer integration and a snapshot stream. |
| Phase 7 (T047–T050) | Final audit gates — wait for everything else. |

## Working build environment notes

These are stable across sessions and should not need re-discovery:

- `Directory.Build.props` `TargetFramework = net10.0`.
- `Broker.Contracts.fsproj` sets `<Nullable>disable</Nullable>` because the `Grpc-FSharp.Tools` 0.2.0 generator emits F# code that violates the .NET 10 nullness analyzer; the rest of the projects keep nullable enabled.
- F# proto codegen requires the `grpc-fsharp` global tool (NuGet `grpc-fsharp`, installed binary `protoc-gen-fsharp`). The tool ships with `tfm = net6.0`; we patched `~/.dotnet/tools/.store/grpc-fsharp/0.2.0/grpc-fsharp/0.2.0/tools/net6.0/any/FSharp.GrpcCodeGenerator.runtimeconfig.json` to add `"rollForward": "LatestMajor"`.
- Surface-area baselines: regenerate with `BROKER_REGENERATE_SURFACE_BASELINES=1 dotnet test tests/SurfaceArea/SurfaceArea.fsproj`. Without the env var, the test compares to the checked-in baseline.

## Synthetic-Evidence Inventory

No `[S]` rows yet. All work to date is `[X]` against real evidence; remaining work is honestly `[ ]` (not started).
