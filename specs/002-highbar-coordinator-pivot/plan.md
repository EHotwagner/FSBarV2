# Implementation Plan: Broker–HighBarCoordinator Wire Pivot

**Branch**: `002-highbar-coordinator-pivot` | **Date**: 2026-04-28 | **Spec**: [spec.md](./spec.md)
**Input**: Feature specification from `/specs/002-highbar-coordinator-pivot/spec.md`

## Summary

Retire the broker's hand-authored `fsbar.broker.v1.ProxyLink` wire schema
and host the upstream `highbar.v1.HighBarCoordinator` service that the
HighBarV3 plugin's `CoordinatorClient` already dials. After the pivot the
broker speaks the proxy-side wire that real BAR + HighBarV3 sessions
already produce — clearing four of the five synthetic-evidence carve-outs
shipped with feature 001 (T029, T037, T042, T046). The HighBar
proto set is vendored under `src/Broker.Contracts/highbar/`, pinned to a
single upstream commit, and surfaced through the existing
`FSharp.GrpcCodeGenerator` build path. A new `Broker.Protocol`
`HighBarCoordinatorService` replaces `ProxyLinkService`; `WireConvert`
gains a HighBar-side direction; `BrokerState`'s internal `ProxyAiLink`
remains the seam between the wire and the rest of the broker. The
`ScriptingClient` proto and F# surface are byte-for-byte unchanged
(spec FR-007, SC-006).

**Change tier**: **Tier 1 (contracted change)** — removes the public
`fsbar.broker.v1.ProxyLink` proto + its `Broker.Protocol.ProxyLinkService`
F# surface; adds the public `highbar.v1.HighBarCoordinator` service
(server-side) + `Broker.Protocol.HighBarCoordinatorService`; adds and
removes corresponding surface-area baselines. Requires the full artifact
chain.

## Technical Context

**Language/Version**: F# on .NET 10 (`net10.0`). No change vs 001.
**Primary Dependencies**: Unchanged from 001 — Spectre.Console;
`Grpc.AspNetCore.Server` + `Grpc-FSharp.Tools` (`FSharp.GrpcCodeGenerator`);
Serilog; SkiaViewer; Expecto. **No new runtime dependencies.** Compile-time:
the upstream HighBarV3 proto set is vendored into `src/Broker.Contracts/`;
`buf`-style imports are resolved by adding the vendored `highbar/`
subdirectory as a proto include path in `Broker.Contracts.fsproj`.
**Storage**: Unchanged from 001 (rolling-file audit log; in-memory roster).
**Testing**: Unchanged stack (Expecto + `dotnet test`). The 001
`SyntheticProxy` loopback fixture is retired and replaced with a
`SyntheticCoordinator` fixture that drives the new `HighBarCoordinatorService`
end-to-end on CI; the four real-evidence carve-outs (T029/T037/T042/T046)
are closed by an operator-driven walkthrough against a real BAR+HighBarV3
build (Story 3).
**Target Platform**: Cross-platform .NET 10 (Linux + Windows). The wire
pivot does not change platform requirements.
**Project Type**: Same six-project F# layout as 001 (Contracts, Core,
Protocol, Tui, Viz, App). No new projects.
**Performance Goals**: Re-anchored from 001 — SC-002 (game-tick →
scripting-client p95 ≤1 s over 500 real-game ticks), SC-003 (disconnect
detect-to-Idle ≤10 s in ≥95 % of trials), SC-004 (≥1 Hz dashboard at ≥4
clients + ≥200 units), all over the new wire.
**Constraints**: Single-session per broker (carried forward from 001).
The coordinator listener shares the existing `ScriptingClient` Kestrel
endpoint (FR-010 / 001 FR-005). Loopback-only auth (Assumptions
§"Auth on the coordinator wire is loopback-only"). The plugin's PushState
queue (256-deep drop-oldest, plugin side) is observed but not negotiated
— gaps surface as a dashboard indicator + audit event (FR-013).
**Scale/Scope**: Same envelope as 001 — one operator, one game, ≤8
scripting clients, hundreds of units.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-checked after Phase 1 design.*

| # | Principle | Status | Notes |
|---|-----------|--------|-------|
| I | Spec → FSI → Semantic Tests → Implementation | ✅ Pass | The new `HighBarCoordinatorService` and `WireConvert.fromHighBar*` / `toHighBar*` functions are drafted as `.fsi` first (`contracts/public-fsi.md`), exercised in FSI via the existing `scripts/prelude.fsx` (extended to load the vendored HighBar proto types), then implemented. |
| II | Visibility lives in `.fsi`, not in `.fs` | ✅ Pass | One new public module (`HighBarCoordinatorService`) gets a curated `.fsi`. One existing public module (`ProxyLinkService`) is removed in full — `.fsi` and `.fs` deleted together; baseline removed. `WireConvert.fsi` and `BrokerState.fsi` get additive surface deltas, never `private`/`internal` keywords in `.fs`. |
| III | Idiomatic Simplicity Is the Default | ✅ Pass | No SRTP, no type providers, no exotic CEs. Two justified mutability uses carry forward from 001 (per-client command channel; live dashboard view-model) — the new coordinator service uses the same `Channel<T>`-based outbound queue and per-attach mutable `ProxyAiLink` field that 001 uses; no new mutability. |
| IV | Synthetic Evidence Requires Loud, Repeated Disclosure | ✅ Pass (planned) | This feature **closes** four of the five existing 001 carve-outs (T029/T037/T042/T046). New `[S]` work expected in the broker-side test suite while a real BAR build is unavailable to CI: a `SyntheticCoordinator` fixture that drives the new `HighBarCoordinatorService` over loopback gRPC, tagged at the 5 disclosure surfaces. **No new carve-out is acceptable for the wire-side end-to-end** — Story 3 mandates real-game evidence regenerated under `readiness/`. The remaining 001 carve-out (T035, host-mode game-process management) is explicitly **not** closed by this feature; it survives unchanged. |
| V | Test Evidence Is Mandatory | ✅ Pass (planned) | Every new FR maps to at least one Expecto test against the public surface (see `tasks.md` once generated). The schema-version mismatch path (FR-003), heartbeat timeout path (FR-008), owner-skirmish-AI rejection (FR-011), and plugin-side gap detection (FR-013) each get a dedicated test against the `HighBarCoordinatorService` Impl. |
| VI | Observability and Safe Failure | ✅ Pass | All coordinator-wire lifecycle events (FR-009) emit through the existing Serilog audit sink. New audit cases extend the `Audit.AuditEvent` union additively. Schema-mismatch and non-owner attempts fail fast at the first Heartbeat — no silent acceptance, no swallowed exception. |

**Tier 1 contract surface change** —
- **Removed**: `src/Broker.Contracts/proxylink.proto`; F# module `Broker.Protocol.ProxyLinkService`; surface-area baseline `tests/SurfaceArea/baselines/Broker.Protocol.ProxyLinkService.surface.txt`; the proto envelope types `ProxyClientMsg`, `ProxyServerMsg`, `Handshake`, `HandshakeAck`, `KeepAlivePing`, `KeepAlivePong` (all defined in `proxylink.proto`).
- **Added**: vendored upstream proto set under `src/Broker.Contracts/highbar/` (5 files, see Project Structure); F# module `Broker.Protocol.HighBarCoordinatorService` (`.fsi` + `.fs` + baseline). `WireConvert.fsi` gains `toCoreSnapshotFromHighBar`, `fromCoreCommandToHighBar`, and helpers; `BrokerState.fsi` gains a small `attachCoordinator` / `coordinatorCommandChannel` surface delta replacing the retired `attachProxy` / `proxyOutbound` (rename, not addition).

**No constitutional violations to track in Complexity Tracking.**

### Post-Phase-1 re-evaluation (2026-04-28)

Re-checked the gates after `data-model.md`, `contracts/`, and
`public-fsi.md` were drafted. No new violations:

- Principle II is backed by concrete `.fsi` sketches in
  `contracts/public-fsi.md` for the new and modified public modules.
- Principle III holds — the new `.fsi` sketches use plain records,
  plain unions, and standard CEs only (`async`/`task`/`option`/
  `result`).
- Principle IV — the only synthetic surface introduced by this feature
  is the CI-side `SyntheticCoordinator` fixture (replacing the retired
  001 `SyntheticProxy`). Its disclosure plan mirrors 001 §4. Real-game
  evidence is owned by Story 3 and lands under `readiness/`.
- The cross-language wire contract is **vendored verbatim from
  upstream HighBarV3** at a pinned SHA (see
  `contracts/highbar-proto-pin.md`). Drift handling is owned by a
  separate pin-and-update workflow per spec Assumptions, not by this
  feature.

Complexity Tracking remains empty.

## Project Structure

### Documentation (this feature)

```text
specs/002-highbar-coordinator-pivot/
├── spec.md                      # Feature spec (already authored)
├── plan.md                      # This file
├── research.md                  # Phase 0 output — wire mapping decisions
├── data-model.md                # Phase 1 output — entity delta vs 001
├── contracts/
│   ├── highbar/                     # Vendored proto set (pinned to upstream SHA)
│   │   ├── coordinator.proto
│   │   ├── state.proto
│   │   ├── commands.proto
│   │   ├── events.proto
│   │   └── common.proto
│   ├── highbar-proto-pin.md         # Upstream SHA + retrieval workflow
│   └── public-fsi.md                # F# .fsi sketches for new/changed modules
├── quickstart.md                # Phase 1 output — operator real-game walkthrough
└── tasks.md                     # Phase 2 output (NOT created by /speckit-plan)
```

### Source Code (delta from 001)

```text
src/
├── Broker.Contracts/                # gRPC .proto + F#-generated types
│   ├── proxylink.proto              # ⊖ REMOVED
│   ├── scriptingclient.proto        # unchanged (spec FR-007)
│   ├── common.proto                 # unchanged on the broker side
│   ├── highbar/                     # ⊕ NEW — vendored from HighBarV3@<sha>
│   │   ├── coordinator.proto
│   │   ├── state.proto
│   │   ├── commands.proto
│   │   ├── events.proto
│   │   └── common.proto
│   ├── HIGHBAR_PROTO_PIN.md         # ⊕ NEW — pin manifest (mirror of specs/.../contracts/highbar-proto-pin.md)
│   └── Broker.Contracts.fsproj      # updated <Protobuf> includes
│
├── Broker.Core/                     # No changes — pure state machine survives the pivot
│
├── Broker.Protocol/
│   ├── ProxyLinkService.fsi/.fs     # ⊖ REMOVED
│   ├── HighBarCoordinatorService.fsi/.fs    # ⊕ NEW
│   ├── VersionHandshake.fsi/.fs     # repurposed: schema-version (string) strict equality + owner-AI rule
│   ├── BackpressureGate.fsi/.fs     # unchanged in shape; rebound to the new service's outbound channel
│   ├── BrokerState.fsi/.fs          # rename `attachProxy`/`proxyOutbound` → `attachCoordinator`/`coordinatorCommandChannel`; add heartbeat-tracking field
│   ├── WireConvert.fsi/.fs          # add `toCoreSnapshotFromHighBar`, `fromCoreCommandToHighBar`, drop ProxyLink-side helpers
│   ├── ServerHost.fsi/.fs           # `MapGrpcService<HighBarCoordinatorService.Impl>` replaces ProxyLink registration
│   └── Broker.Protocol.fsproj       # updated references
│
├── Broker.Tui/                      # No changes — view-model unchanged
├── Broker.Viz/                      # No changes
└── Broker.App/                      # No changes (composition root rewires automatically once Protocol wiring updates)

tests/
├── Broker.Contracts.Tests/          # add HighBar-shaped wire-format round-trip tests; drop ProxyLink ones
├── Broker.Core.Tests/               # No changes
├── Broker.Protocol.Tests/           # add HighBarCoordinatorService unary/streaming tests; add owner-AI rejection test; drop ProxyLink tests
├── Broker.Tui.Tests/                # No changes
├── Broker.Integration.Tests/
│   ├── SyntheticProxy.fs            # ⊖ REMOVED
│   ├── SyntheticCoordinator.fs      # ⊕ NEW — replacement loopback fixture for CI
│   └── (Sc003LatencyTests, Sc005RecoveryTests, SnapshotE2ETests, AdminElevationTests, AuditLifecycleTests, DashboardLoadTests) — rebound onto SyntheticCoordinator
└── SurfaceArea/baselines/
    ├── Broker.Protocol.ProxyLinkService.surface.txt          # ⊖ REMOVED
    └── Broker.Protocol.HighBarCoordinatorService.surface.txt # ⊕ NEW

readiness/                            # Story 3 — real-game evidence
└── (refreshed under specs/001-tui-grpc-broker/readiness/ — see spec User Story 3)
```

**Structure Decision**: Same six-project F# solution as 001. The pivot
is contained inside `Broker.Contracts` (proto set swap) and
`Broker.Protocol` (service swap + wire conversion delta). `Broker.Core`,
`Broker.Tui`, `Broker.Viz`, and `Broker.App` are untouched at the
public-surface level — the seam between the wire and the rest of the
broker is `BrokerState.Hub`'s `ProxyAiLink` record, which remains the
internal name for the live attachment regardless of which wire delivers
it (data-model 1.7 retains its name on purpose; renaming would touch
every consumer for no semantic gain).

The vendored `highbar/` proto subdirectory is added to
`Broker.Contracts.fsproj`'s `<Protobuf>` includes; codegen produces
the `Highbar.V1.*` namespace alongside the existing
`FSBarV2.Broker.Contracts` namespace. No build-system change is
required — `FSharp.GrpcCodeGenerator` already supports multiple
package roots in one project.

## Complexity Tracking

> **Empty by design — Constitution Check passed without violations.**

The single non-trivial choice is vendoring five upstream proto files
into `src/Broker.Contracts/highbar/` rather than referencing them as a
NuGet/buf module. This is not a deviation from any principle; it is
the simplest and most auditable path given that:

- HighBarV3 does not publish a versioned proto NuGet/buf module today.
- A vendored copy with a SHA-pinned manifest gives us the same change-
  control discipline as a binary dependency, with one less moving
  part and a clean diff for any drift event.
- The pin workflow is documented in `contracts/highbar-proto-pin.md`
  so update mechanics are explicit and audit-loggable.

If upstream ever publishes a buf module, the future swap is mechanical
(replace the vendored directory with a `buf.lock`-pinned dep); the F#
codegen surface and the F# `WireConvert` translation layer are unaffected.
