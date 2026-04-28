# Phase 0 Research: TUI gRPC Game Broker

**Feature**: 001-tui-grpc-broker
**Date**: 2026-04-27
**Status**: Complete — all `NEEDS CLARIFICATION` resolved

This document records the technology decisions, their rationale, and the
alternatives that were considered. It is the authoritative reference for the
plan's Technical Context section.

---

## 1. Target framework

- **Decision**: .NET 10 (`net10.0`).
- **Rationale**: The constitution mandates .NET 10 (Engineering Constraints).
  The current `Directory.Build.props` declares `net9.0` (scaffold default);
  this plan bumps it to `net10.0`. The optional viz dependency `SkiaViewer`
  also targets `net10.0`, so this is the only consistent choice.
- **Alternatives considered**:
  - Stay on `net9.0` to minimize scaffold churn — rejected: violates the
    constitution and would force us off SkiaViewer.

## 2. Language and idioms

- **Decision**: F# only, in line with constitution Engineering Constraints
  ("F# on .NET is the exclusive stack"). Every public module ships with a
  curated `.fsi`. Visibility lives in `.fsi` (`FS0078` is already promoted
  to error in `Directory.Build.props`). Idiomatic simplicity is the default.
- **Rationale**: Project constitution.
- **Alternatives considered**: None — non-negotiable.

## 3. TUI library

- **Decision**: **Spectre.Console** (latest from NuGet), used directly from
  F#. Not `fs-spectre`/`FsSpectre` wrappers.
- **Rationale**:
  - Mature, widely used .NET TUI library with `Layout`, `LiveDisplay`,
    panels, tables, rules, status indicators — everything the dashboard
    needs (FR-017 to FR-021).
  - Used directly from F# the API is fluent enough that a thin wrapper
    is unnecessary; one less dependency.
  - F# wrappers (`fs-spectre`, `FsSpectre`) are useful as design references
    for declarative ergonomics but add a dep without adding capability.
- **Alternatives considered**:
  - `Terminal.Gui` — heavier, classic-curses widget model, more layout
    surface than we need; rejected as more complex than the dashboard
    requires.
  - Plain `System.Console` rendering — rejected: would re-implement layout,
    panels, tables, rules from scratch.

## 4. Hotkey-driven input loop

- **Decision**: Single-thread render-and-input loop. One tick:
  1. Drain pending broker events (channel reads, non-blocking) → apply to
     the dashboard view-model.
  2. While `Console.KeyAvailable`, read keys (`Console.ReadKey(true)`),
     dispatch to the controller.
  3. Re-build the `Layout` sections from the view-model.
  4. `ctx.Refresh()` on the `LiveDisplay` context.
  5. Wait the tick interval (~50–100 ms; SC-006 needs ≥1 Hz).
- **Rationale**: Spectre.Console docs are explicit that `LiveDisplay` is
  **not thread-safe** and concurrent input from another thread is not
  supported. The single-loop pattern avoids that hazard while still meeting
  the dashboard refresh budget. Hotkey latency is bounded by the tick (≤100
  ms), well within human reaction expectations.
- **Alternatives considered**:
  - Background `LiveDisplay` thread + separate key-listener thread
    coordinated by a queue — rejected: violates Spectre's documented
    threading contract, and the failure mode (torn frames, races on the
    AnsiConsole renderer) is exactly the kind of "invisible bug" we want
    to avoid.
  - Spectre's `SelectionPrompt`/`MultiSelectionPrompt` — rejected: those
    are blocking modal prompts, incompatible with a continuously-refreshing
    dashboard.

## 5. gRPC server stack

- **Decision**: **Grpc.AspNetCore** (`Grpc.AspNetCore.Server`) hosted via
  the ASP.NET Core generic host, listening only on the gRPC port (no HTTP
  controllers). Single Kestrel listener serving both `ProxyLink` and
  `ScriptingClient` services (FR-005).
- **Rationale**:
  - Microsoft-supported, fully-managed (no native `Grpc.Core` deps —
    `Grpc.Core` is end-of-life).
  - Integrates with `Microsoft.Extensions.Logging` (and therefore Serilog
    via `Serilog.Extensions.Logging`).
  - HTTP/2 server with built-in flow control — directly supports FR-010
    (bounded backpressure via flow control on per-client streams).
- **Alternatives considered**:
  - `Grpc.Core` (native) — rejected: deprecated.
  - `protobuf-net.Grpc` (code-first) — rejected: contracts in `.proto` are
    interoperable with the proxy AI side (which is C++/multi-language) and
    are required for a Tier 1 cross-language contract.

## 6. F# code generation from `.proto`

- **Decision**: **`FSharp.GrpcCodeGenerator`** (`Grpc-FSharp.Tools` NuGet
  package + `grpc-fsharp` global tool). Messages → F# records, `oneof` →
  F# unions, optional fields → `ValueOption<'T>`, services → abstract base
  classes for the server side and concrete client classes.
- **Rationale**:
  - Idiomatic F# output beats consuming C#-generated proto types from F#
    (which forces mutable record / nullable patterns through F# code).
  - Tool integrates into the F# build (.fsproj `<Protobuf>` items).
  - Constitution: "Idiomatic Simplicity Is the Default" — generated F# is
    more idiomatic than generated C# bridged to F#.
- **Alternatives considered**:
  - `Grpc.Tools` (C# codegen), F# consumes generated C# — rejected: forces
    mutable records and `null` patterns into F# code; adds friction for
    every message touch.
  - Code-first (`protobuf-net.Grpc`) — rejected (see §5).

## 7. Connection topology and the proxy AI side

- **Decision**: The **broker is the gRPC server**. The in-game proxy AI
  (HighBarV3) connects out to the broker as a gRPC **client** of the
  `ProxyLink` service. The `ProxyLink` contract is owned by this broker
  spec; compatibility on the proxy side is the proxy AI workstream's
  concern (out of scope for this feature).
- **Rationale**:
  - Spec FR-005 is normative: broker hosts a single gRPC server with two
    services on one listening port. Clarification 2026-04-27 Q2 fixes the
    same shape.
  - Public HighBarV3 documentation suggests the in-game plugin can host
    its own gRPC gateway. That is **not** the topology this feature
    targets. Either (a) the proxy AI dials out to the broker in a mode
    distinct from its embedded-gateway mode, or (b) a thin shim on the
    proxy side bridges its embedded gateway to the broker's `ProxyLink`.
    The choice between (a) and (b) is the proxy AI side's problem; the
    broker only commits to "I am a server speaking the `ProxyLink`
    contract on a configurable host:port".
- **Alternatives considered**:
  - Broker dials into the proxy AI's embedded gRPC gateway (broker as
    client) — rejected: contradicts FR-005 and would change every other
    requirement that assumes "broker-hosted services that scripting
    clients also connect to".
  - Two separate listening ports (one per service) — rejected: FR-005
    requires a single port.

## 8. Optional 2D visualization

- **Decision**: **In-process** integration of `SkiaViewer` (NuGet,
  `EHotwagner/SkiaViewer`). The broker exposes an `IObservable<Scene>`
  that the broker assembles from live game state, and calls
  `Viewer.run` on operator request. The Viewer runs on its own background
  thread (per its own design), so the TUI loop is unaffected.
- **Rationale**:
  - Mirrors the FSBar.V1 `FSBar.Viz` design (SkiaSharp + Silk.NET, glyph
    language) without re-implementing it; SkiaViewer is the actively-
    maintained successor.
  - F# all the way through (88% F# upstream).
  - Push-based `IObservable<Scene>` matches our update model: every
    relevant `GameStateSnapshot` produces a new scene.
  - Keeps the broker as a single process — no extra IPC, no second
    binary, simpler deployment.
- **Headless behavior** (FR-025, SC-008): At startup the broker probes
  for a usable graphical environment (GPU/window-system availability via
  Silk.NET). On a headless host, the viz toggle is disabled with a clear
  status message ("2D visualization unavailable: no graphical display").
  All other broker functions continue. CLI flag `--no-viz` forces the
  viz subsystem off entirely (skips even probing).
- **Alternatives considered**:
  - Separate viz process + IPC — rejected: more moving parts, more failure
    modes, no benefit for a single-operator tool.
  - `FSBar.Viz` from V1 directly — rejected: V1 README labels the viz as
    archived prototype work; SkiaViewer is the maintained line.
  - WPF/Avalonia GUI — rejected: heavier dep, narrower platform reach
    (WPF is Windows-only); the spec wants TUI primary, viz optional.

## 9. Structured logging

- **Decision**: **Serilog**, surfaced through `Microsoft.Extensions.Logging`
  via `Serilog.Extensions.Logging`. Console sink for in-TUI diagnostic
  events (rendered into a status pane, not stdout where it would scramble
  the dashboard); rolling-file sink for the audit log mandated by FR-028.
  Capture `TraceId`/`SpanId` automatically (Serilog 4.x default).
- **Rationale**:
  - Constitution states "Structured-logging library: not yet selected; see
    ADR when chosen" — this plan **is** the ADR moment for that pick.
  - Serilog is the de-facto standard for structured logging in .NET; rich
    sinks; the `ILogger<T>` shim lets the rest of the code remain
    framework-agnostic.
  - Audit-quality structured records (FR-028 needs timestamps + connection
    lifecycle + admin grants/revokes + command rejections).
- **Alternatives considered**:
  - `Microsoft.Extensions.Logging` only — rejected: limited structured
    enrichment, anaemic sink ecosystem.
  - `NLog`, `log4net` — rejected: smaller F# story, no clear advantage.

## 10. Test framework

- **Decision**: **Expecto** (already wired in the scaffold), with
  `YoloDev.Expecto.TestSdk` for `dotnet test` integration.
- **Rationale**:
  - Constitution Principle I requires "semantic tests for FSI" — Expecto's
    plain-function test model maps directly to FSI exercise.
  - Already in the scaffold (`tests/Lib.Tests/`), so zero migration cost.
  - F#-native, no attribute gymnastics.
- **Alternatives considered**:
  - xUnit / NUnit — rejected: attribute-driven, less idiomatic in F#.

## 11. Storage / persistence

- **Decision**: No database. Audit log is rolling-file via Serilog
  (`logs/broker-YYYYMMDD.log`). Admin grants are in-memory only — they
  do not survive broker restart (FR-016 explicit).
- **Rationale**: Spec is explicit that admin grants are session-scoped and
  not persisted; clarification 2026-04-27 Q3 confirms.
- **Alternatives considered**: SQLite, JSON snapshot on disk — rejected:
  no requirement asks for cross-restart state.

## 12. Dependency footprint summary

New runtime dependencies introduced by this plan:

| Package                          | Version        | Why                       | Owner |
|----------------------------------|----------------|---------------------------|-------|
| `Spectre.Console`                | latest         | TUI dashboard             | spectreconsole.net |
| `Grpc.AspNetCore.Server`         | latest         | gRPC server host          | dotnet team |
| `Grpc.Net.Client`                | latest (tests) | gRPC client for tests     | dotnet team |
| `Google.Protobuf`                | latest         | wire format               | google |
| `Grpc-FSharp.Tools`              | latest         | F# proto codegen          | Arshia001 |
| `Serilog`                        | 4.x            | structured logging        | serilog |
| `Serilog.Extensions.Logging`     | latest         | `ILogger<T>` bridge       | serilog |
| `Serilog.Sinks.File`             | latest         | rolling-file audit log    | serilog |
| `SkiaViewer`                     | latest         | optional 2D viz           | EHotwagner |
| `Silk.NET.*` (transitive)        | (via SkiaViewer)| GPU windowing            | dotnet/Silk.NET |
| `SkiaSharp` (transitive)         | (via SkiaViewer)| 2D rasterization         | mono |

Each is pinned via NuGet `PackageReference` with explicit version. Per
constitution, future dependency additions require documented need + version
strategy + maintenance owner.

## 13. Performance targets and headroom

| Spec target | Plan budget |
|-------------|-------------|
| SC-003: 95% of state updates ≤1 s end-to-end | Game tick → proxy → broker fan-out: aim ≤200 ms p95. |
| SC-005: disconnect detected ≤5 s, recovery ≤10 s | gRPC keepalive 2 s + 1 missed = ≤4 s detect; idle reset ≤2 s. |
| SC-006: ≥1 Hz dashboard refresh with ≥4 clients, ≥200 units | Tick interval ≥100 ms = 10 Hz; well over budget. |
| FR-010: per-client bounded backpressure, no silent drop | Per-client `Channel<Command>` with `BoundedChannelFullMode.Wait` + flow-control; explicit `QUEUE_FULL` on sync overflow path. |

## 14. Open follow-ups (out of scope for this feature)

- ADR for the proxy AI side connection model (server vs. client vs. shim).
  Tracked separately; this feature commits only to the broker contract.
- Network exposure / authentication beyond localhost. Spec assumes
  trusted local processes; deployment-time hardening is a future feature.
- Multi-session brokers (one broker, many concurrent matches). Spec
  explicitly out of scope for v1.

---

**All `NEEDS CLARIFICATION` placeholders from the Technical Context are
resolved by the decisions above.**
