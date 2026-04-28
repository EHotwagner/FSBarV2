# Feature Baseline — 001-tui-grpc-broker

**Recorded**: 2026-04-27 (T006)
**Branch**: `001-tui-grpc-broker`

## Tier

**Tier 1 (contracted change).** Introduces public gRPC services
(`ProxyLink`, `ScriptingClient`), three new `.proto` files, six new F#
projects, six new test projects, and corresponding public `.fsi` surface
in every new module.

## Affected layers

| Layer | Project | Notes |
|-------|---------|-------|
| Wire contracts  | `src/Broker.Contracts/`  | `common.proto`, `proxylink.proto`, `scriptingclient.proto` + generated F# types via `Grpc-FSharp.Tools` (`FSharp.GrpcCodeGenerator`). |
| Pure domain     | `src/Broker.Core/`       | Mode, Session, Lobby, ParticipantSlot, ScriptingRoster, CommandPipeline, Snapshot, Dashboard, Audit. No I/O. |
| Protocol edge   | `src/Broker.Protocol/`   | VersionHandshake, BackpressureGate, ServerHost, ProxyLinkService, ScriptingClientService. Kestrel + ASP.NET Core generic host. |
| TUI             | `src/Broker.Tui/`        | Layout, DashboardView, HotkeyMap, LobbyView, TickLoop. Spectre.Console. |
| Optional viz    | `src/Broker.Viz/`        | SceneBuilder, VizHost. SkiaViewer integration; gracefully unavailable on headless. |
| Composition root| `src/Broker.App/`        | Cli, Logging, Program. CLI entry point, Serilog wire-up. |

Existing scaffold (`src/Lib/`, `tests/Lib.Tests/`) remains in place during
build-out; retired or repurposed once `Broker.Core` provides equivalent
real surface.

## Public-API surface impact

- New gRPC services on a single listening port (FR-005) — Tier 1 cross-language seam.
- New public F# modules — every one paired with an `.fsi` (Principle II).
- New surface-area baselines under `tests/SurfaceArea/baselines/` — one per module.
- No change to the existing `FSBarV2.Library` public surface in this feature.

## Evidence obligations

- **Principle I** — `scripts/prelude.fsx` updated to load packed `Broker.Core`; transcript captured to `readiness/fsi-session.txt` (T013).
- **Principle II** — every new public module has both `.fsi` and a checked-in baseline; T014 + T047.
- **Principle IV** — synthetic-proxy substitutes are anticipated for US1/US2 wherever a real HighBarV3 game / proxy AI is not available. Disclosures land in `tasks.md` Synthetic-Evidence Inventory and at code-level `// SYNTHETIC:` comments.
- **Principle V** — every functional requirement maps to at least one Expecto test exercising the public surface; integration test exercises the in-memory gRPC stack end-to-end.
- **Principle VI** — Serilog rolling-file audit sink wired before any acceptance scenario (T028); FR-026/FR-027 fail-fast paths verified by T029b.

## Build-environment notes

- F# proto codegen requires the `grpc-fsharp` global tool (NuGet `grpc-fsharp` 0.2.0 → installs `protoc-gen-fsharp`). Tool DLLs target `net6.0`; we patch `~/.dotnet/tools/.store/grpc-fsharp/0.2.0/grpc-fsharp/0.2.0/tools/net6.0/any/FSharp.GrpcCodeGenerator.runtimeconfig.json` to add `"rollForward": "LatestMajor"` so the tool runs on the .NET 10 SDK.
- `Broker.Contracts.fsproj` sets `<Nullable>disable</Nullable>` because the 0.2.0 generator emits F# code authored before nullable-aware F# (FS3261) was promoted to error in `Directory.Build.props`. All other projects keep nullable enabled.

## Anticipated synthetic-evidence rows

| Task | Reason | Real-evidence path |
|------|--------|--------------------|
| T029 / T029a / T029b | Real HighBarV3 + proxy AI workstream not yet available; substitute is a synthetic-proxy fixture driving `ProxyLink` over loopback gRPC. | Resolved when the HighBarV3 proxy AI lands (separate workstream — research.md §7, §14). |
| T037 | Real HighBarV3 launch path may not be exercisable in CI. Broker-side acceptance + audit assertions remain real; HighBarV3 effects mocked. | Resolved when host-mode launch is wired into a CI-visible HighBarV3 build. |

These rows are predictions; the canonical inventory is the table at the
bottom of `tasks.md`, written as the tasks are actually executed.
