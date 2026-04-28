# Implementation Plan: TUI gRPC Game Broker

**Branch**: `001-tui-grpc-broker` | **Date**: 2026-04-27 | **Spec**: [spec.md](./spec.md)
**Input**: Feature specification from `/specs/001-tui-grpc-broker/spec.md`

## Summary

Build an F# / .NET 10 terminal application that brokers between a real-time
strategy game (HighBarV3) and external scripting clients. The broker hosts
one gRPC server with two services (`ProxyLink` for the in-game proxy AI,
`ScriptingClient` for external bots) on a single listening port, presents a
hotkey-driven Spectre.Console TUI dashboard with live broker / session /
game telemetry, supports two operating modes (host with admin authority and
guest with observe-only), and offers an optional 2D top-down visualization
via the `SkiaViewer` NuGet library (in-process, opens on operator request,
gracefully unavailable on headless hosts). Per-client bounded backpressure
with explicit `QUEUE_FULL` rejection guarantees no silent command drops.
Admin elevation of scripting clients is operator-driven from the TUI, scoped
to the broker process, and audit-logged via Serilog rolling-file sinks.

**Change tier**: **Tier 1 (contracted change)** — introduces public gRPC
contracts (`ProxyLink`, `ScriptingClient`), new project boundaries, and new
external dependencies. Requires the full artifact chain: spec, plan, `.fsi`
contract updates, surface-area baselines, semantic tests, and quickstart.

## Technical Context

**Language/Version**: F# on .NET 10 (`net10.0`).
**Primary Dependencies**: Spectre.Console (TUI); Grpc.AspNetCore.Server +
Google.Protobuf + `Grpc-FSharp.Tools` NuGet (which ships the
`FSharp.GrpcCodeGenerator` codegen);
Serilog + `Serilog.Extensions.Logging` + `Serilog.Sinks.File` (structured
logging + rolling-file audit log); SkiaViewer (optional 2D viz, transitively
SkiaSharp + Silk.NET); Expecto + `YoloDev.Expecto.TestSdk` (tests).
**Storage**: None. Audit log is rolling-file (Serilog). Admin grants are
in-memory only; not persisted across broker restart (per FR-016).
**Testing**: Expecto run via `dotnet test`. Tests exercise public surface
through the same FSI / packed-library entry points a scripting user would
use (per Constitution Principle I and V).
**Target Platform**: Cross-platform .NET 10 (Linux + Windows). TUI runs in
any standard interactive terminal. The optional 2D viz requires a graphical
display (Vulkan with OpenGL fallback via Silk.NET); on a headless host the
viz toggle reports unavailable and the rest of the broker continues to run.
**Project Type**: TUI application + library (the broker is a CLI binary,
backed by F# libraries that are also driveable from FSI).
**Performance Goals**: Per spec — SC-003 (95% of state updates ≤1 s
end-to-end), SC-005 (disconnect detect ≤5 s, recover ≤10 s), SC-006 (≥1 Hz
dashboard refresh under ≥4 clients + ≥200 units). Plan budget: TUI tick
interval 50–100 ms (≥10 Hz), gRPC keepalive 2 s.
**Constraints**: Single-session per broker (multiple matches require
multiple processes). gRPC server listens on localhost by default; scripting
clients are trusted local processes. Admin grants do not persist. No silent
command drops anywhere in the pipeline.
**Scale/Scope**: One operator, one game session at a time, on the order of
1–8 scripting clients, hundreds of units, dashboard refresh on the order
of seconds-of-game-time. No multi-tenant requirements.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-checked after Phase 1 design.*

| # | Principle | Status | Notes |
|---|-----------|--------|-------|
| I | Spec → FSI → Semantic Tests → Implementation | ✅ Pass (planned) | All public modules will be drafted as `.fsi`, exercised in FSI via `scripts/prelude.fsx`, then implemented. Tests target the same packed-library surface. |
| II | Visibility lives in `.fsi`, not in `.fs` | ✅ Pass | Every public module gets a `.fsi`. `Directory.Build.props` already promotes `FS0078` to error. Surface-area baseline files will be added per public module. |
| III | Idiomatic Simplicity Is the Default | ✅ Pass | No SRTP, no type providers, no custom operators, no exotic CEs (only `async`/`task`/`option`/`result`/`seq`). One justified mutability use: the per-client command channel + the live dashboard view-model (single-writer, hot path). Justification recorded at the use sites. |
| IV | Synthetic Evidence Requires Loud, Repeated Disclosure | ✅ Pass (planned) | `[S]` tasks expected for: (a) game-state snapshots before HighBarV3 wire-up exists (synthetic snapshot fixture), (b) admin-command in-game effects (HighBarV3 not driven yet — assertion limited to broker-side acceptance / forwarding). Each will follow the 5-surface disclosure rule. |
| V | Test Evidence Is Mandatory | ✅ Pass (planned) | Every functional requirement maps to at least one Expecto test exercising the public surface. Synthetic substitutes used only where the proxy-AI side / live game is not yet available. |
| VI | Observability and Safe Failure | ✅ Pass | Serilog structured logging from day 1; FR-026/FR-027 explicitly require fail-fast on proxy / game loss with operator-visible state. No swallowed exceptions in critical paths (gRPC handlers, TUI loop, viz lifecycle). |

**Tier 1 contract surface introduced**: `contracts/proxylink.proto`,
`contracts/scriptingclient.proto`, plus `.fsi` for every new public F#
module (see Project Structure below). Surface-area baselines will be added
in `tests/SurfaceArea/` and validated by an Expecto test per public module.

**No constitutional violations to track in Complexity Tracking.**

### Post-Phase-1 re-evaluation (2026-04-27)

Re-checked the gates after `data-model.md`, `contracts/*`, and
`public-fsi.md` were drafted. No new violations:

- Principle II is now backed by concrete `.fsi` sketches in
  `contracts/public-fsi.md` for every public module the plan introduces;
  surface-area baselines in `tests/SurfaceArea/` are part of the Tier 1
  task set.
- Principle III holds — the `.fsi` sketches use plain records, plain
  discriminated unions, and standard CEs only (`async`/`task`/`option`/
  `result`).
- Principle IV — synthetic dependencies remain limited to the proxy-AI /
  game-side which is owned by a separate workstream (see research.md §7,
  §14). Each `[S]` task will follow the 5-surface disclosure rule.
- The cross-language wire contract (`common.proto`, `proxylink.proto`,
  `scriptingclient.proto`) reinforces the Tier 1 classification but
  introduces no principle conflict — protobuf is the constitution's
  prescribed cross-language seam.

Complexity Tracking remains empty.

## Project Structure

### Documentation (this feature)

```text
specs/001-tui-grpc-broker/
├── spec.md                 # Feature spec (already authored)
├── plan.md                 # This file
├── research.md             # Phase 0 output — tech decisions
├── data-model.md           # Phase 1 output — entities + state machine
├── contracts/
│   ├── proxylink.proto         # gRPC service the proxy AI connects to
│   ├── scriptingclient.proto   # gRPC service external bots connect to
│   ├── common.proto            # Shared messages (snapshot, command, error codes)
│   └── public-fsi.md           # Curated F# public-surface sketches (.fsi)
├── quickstart.md           # Phase 1 output — cold-start walkthroughs
└── tasks.md                # Phase 2 output (NOT created by /speckit-plan)
```

### Source Code (repository root)

```text
src/
├── Broker.Contracts/           # gRPC .proto files + F#-generated message/service types
│   ├── proxylink.proto
│   ├── scriptingclient.proto
│   ├── common.proto
│   └── Broker.Contracts.fsproj
│
├── Broker.Core/                # Pure session/command/state-machine logic. No I/O.
│   ├── Mode.fsi / Mode.fs              # HostMode | GuestMode | Idle
│   ├── Session.fsi / Session.fs        # Session lifecycle + state transitions
│   ├── Lobby.fsi / Lobby.fs            # Host-mode lobby config + validation
│   ├── ParticipantSlot.fsi / .fs       # Slot binding rules + single-writer enforcement
│   ├── CommandPipeline.fsi / .fs       # Per-client queues + backpressure + admin gating
│   ├── ScriptingRoster.fsi / .fs       # Connected clients, name uniqueness, admin elevation
│   ├── Snapshot.fsi / .fs              # Game-state snapshot model + diff helpers
│   ├── Dashboard.fsi / .fs             # View-model the TUI renders
│   ├── Audit.fsi / .fs                 # Audit-event records (lifecycle, admin grants, rejections)
│   └── Broker.Core.fsproj
│
├── Broker.Protocol/            # gRPC server hosting ProxyLink + ScriptingClient.
│   ├── ProxyLinkService.fsi / .fs      # Inbound state ingest + outbound command egress
│   ├── ScriptingClientService.fsi / .fs# State subscriptions + command submissions
│   ├── VersionHandshake.fsi / .fs      # Strict major-version match (FR-029)
│   ├── BackpressureGate.fsi / .fs      # Bridges per-client channels to gRPC flow control
│   ├── ServerHost.fsi / .fs            # Kestrel + ASP.NET Core generic-host wiring
│   └── Broker.Protocol.fsproj
│
├── Broker.Tui/                 # Spectre.Console dashboard + hotkey loop.
│   ├── Layout.fsi / .fs                # Root layout (Header, Status, Telemetry, Footer)
│   ├── DashboardView.fsi / .fs         # Render Dashboard view-model into Spectre widgets
│   ├── HotkeyMap.fsi / .fs             # Key bindings: pause/resume, lobby, elevate, viz, quit
│   ├── LobbyView.fsi / .fs             # Host-mode lobby configuration screens
│   ├── TickLoop.fsi / .fs              # Single-thread render-and-input loop
│   └── Broker.Tui.fsproj
│
├── Broker.Viz/                 # Optional 2D viz adapter. Behind a NuGet/runtime gate.
│   ├── SceneBuilder.fsi / .fs          # GameStateSnapshot → SkiaViewer Scene
│   ├── VizHost.fsi / .fs               # Probe display, start/stop SkiaViewer.run
│   └── Broker.Viz.fsproj
│
├── Broker.App/                 # Composition root + CLI entry point.
│   ├── Cli.fsi / .fs                   # Argument parsing (--port, --no-viz, --listen, --version)
│   ├── Program.fsi / .fs               # Wire Core, Protocol, Tui, optional Viz
│   ├── Logging.fsi / .fs               # Serilog config (TUI status sink + file audit sink)
│   └── Broker.App.fsproj
│
└── Lib/                        # (existing scaffold — keep as-is; may be removed once
                                #  Broker.* projects fully replace the placeholder library)

tests/
├── Broker.Contracts.Tests/     # Wire-format round-trips, version handshake matrix
├── Broker.Core.Tests/          # Session state machine, single-writer rule, backpressure
├── Broker.Protocol.Tests/      # In-memory gRPC end-to-end (Grpc.Net.Client → server)
├── Broker.Tui.Tests/           # Hotkey dispatch + Dashboard view-model rendering
├── Broker.Integration.Tests/   # Multi-component scenarios driven through public surface
└── SurfaceArea/                # Per-module surface-area baselines (Constitution II)

scripts/
├── prelude.fsx                 # FSI bootstrap — load packed Broker.Core for interactive use
└── (existing scaffold scripts)

contracts/                      # (per-feature contracts live under specs/.../contracts/;
                                #  the canonical .proto for codegen lives in src/Broker.Contracts/)
```

**Structure Decision**: Multi-project F# solution. Six new projects under
`src/` matching responsibility seams (contracts, pure core, gRPC protocol
adapter, TUI, optional viz, composition root) plus matching tests under
`tests/`. The split is justified by:

- **Contracts as a separate project**: keeps generated proto types out of
  every consumer's build cache and lets non-F# consumers (proxy AI, future
  Go/Python bots) target the same `.proto` files.
- **Pure `Broker.Core` with no I/O**: required for FSI-driven design
  (Constitution Principle I). The state machine, single-writer rule, and
  backpressure logic must be exercisable from FSI without spinning up
  Kestrel.
- **`Broker.Protocol` separated from Core**: gRPC integration is the I/O
  edge; isolating it keeps Core testable and lets the surface-area baseline
  for Core remain stable across protocol changes.
- **`Broker.Tui` separated from `Broker.App`**: the dashboard view-model
  and hotkey map are unit-testable independently of the live ANSI renderer.
- **`Broker.Viz` as its own project**: SkiaSharp/Silk.NET deps are heavy
  and platform-touching; isolating them makes `--no-viz` builds (and CI on
  headless boxes) straightforward.

Existing scaffold `src/Lib/` and `tests/Lib.Tests/` stay in place during
the early phases; they will be retired or repurposed once `Broker.Core`
provides equivalent (real) public surface.

## Complexity Tracking

> **Empty by design — Constitution Check passed without violations.**

No principle requires justification beyond what the spec already records.
The single non-trivial complexity in this plan — six F# projects instead of
one — is the direct consequence of Constitution Principle II (visibility
lives in `.fsi`, baselines per public module) combined with the spec's
multi-edge architecture (gRPC, TUI, optional viz). It is not an exception
to a principle; it is the principle applied. No row needed.
