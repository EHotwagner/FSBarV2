# Implementation Plan: Elmish MVU Core for State and I/O

**Branch**: `003-elmish-mvu-core` | **Date**: 2026-04-28 | **Spec**: [spec.md](./spec.md)
**Input**: Feature specification from `/specs/003-elmish-mvu-core/spec.md`

## Summary

Pivot the broker's in-process state / input / output spine onto the
Elmish MVU pattern. All in-process state collapses into a single
immutable `Model` owned by an MVU runtime; every input (TUI keystroke,
gRPC inbound, timer tick, Cmd-completion callback) becomes a typed
`Msg`; every side effect (gRPC send, audit write, viewer op, timer
schedule) becomes a `Cmd<Msg>` value returned from a pure
`update : Msg -> Model -> Model * Cmd<Msg> list`. The Spectre.Console
dashboard is fed by a pure `view : Model -> SpectreLayout`, rendered
live in production via `LiveDisplay` and to a deterministic string in
tests via Spectre's off-screen renderer.

The goal is testability without a TTY or a real game peer. The four
synthetic-evidence carve-outs that 002 left open against feature 001
(T029 broker–proxy transcript, T037 host-mode admin walkthrough,
T042 dashboard-under-load screenshot, T046 viz-window screenshot —
named in 002 spec §SC-005) are closed by MVU-replay tests that drive
scripted `Msg` sequences and assert on the resulting `Model`, the
emitted Cmd list, and the rendered View string. T035 (host-mode
game-process management against a real BAR engine) is **not** closed
by this feature and remains a tracked carve-out (it is an environment-
provisioning gap, not a state-shape problem).

The mutable `Hub` record + `withLock`/`stateLock` discipline shipped
by 001 and carried by 002 is **retired in the same change set** — a
halfway pivot (Hub + MVU side by side) is explicitly rejected
(spec Assumptions). The gRPC services (`HighBarCoordinatorService`,
`ScriptingClientService`), the audit-log envelope, the dashboard
layout, the hotkey set, and the viz behaviour are byte-for-byte
unchanged at the operator-visible surface (FR-018, FR-020, SC-006).

**Change tier**: **Tier 1 (contracted change)** — replaces the public
F# surface of `Broker.Protocol.BrokerState` (the `Hub` type +
`openHostSession` / `openGuestSession` / `closeSession` / `attachCoordinator`
/ `coordinatorCommandChannel` / etc.) with a new MVU runtime surface;
adds the public `Broker.Mvu` project (`Model`, `Msg`, `Cmd`, `Update`,
`View`, `MvuRuntime`, `TestRuntime`, plus adapter-interface modules);
reduces `Broker.Tui.TickLoop` from "owner of dispatch + state mutation"
to a thin keypress-poll-and-render shell that hosts `MvuRuntime.Host`.
Surface-area baselines shift correspondingly. Requires the full
artifact chain.

The wire surface (`highbar.v1.HighBarCoordinator`,
`fsbar.broker.v1.ScriptingClient`) is unchanged (FR-019, FR-020).

## Technical Context

**Language/Version**: F# on .NET 10 (`net10.0`). No change vs 001/002.
**Primary Dependencies**:
- **NEW**: `Elmish` (https://github.com/elmish/elmish) — the F# MVU
  core, host-agnostic NuGet package targeting netstandard. Treated as
  a load-bearing external dependency on the same footing as
  Spectre.Console or Serilog (spec Assumptions). Pinned to the latest
  stable 4.x release in `Directory.Packages.props` per the repo's
  existing central-package-management discipline. We use the
  `Program<'arg, 'model, 'msg, 'view>` abstraction, the `Cmd<'msg>`
  effect type, and the `Sub<'msg>` subscription type, with a custom
  `setState` hook so Spectre's `LiveDisplay` thread reads the latest
  `Model` from the dispatcher (research §2). No `Fable.*`, no
  `Elmish.Bridge`, no `Feliz` (Fable-only packages, out of scope per
  spec §Out of Scope).
- Spectre.Console (unchanged — still the production render target
  per FR-010; off-screen renderer additionally used for tests per
  FR-011).
- `Grpc.AspNetCore.Server` + `FSharp.GrpcCodeGenerator` (unchanged —
  the gRPC server keeps its current host; RPC handlers translate
  inbound calls into `Msg` dispatches).
- Serilog (unchanged — audit-log sink; entries are now described as
  `Cmd.Audit` values produced by `update` and executed by the
  production audit adapter).
- SkiaViewer (unchanged — viewer-window operations become
  `Cmd.Viz` values).
- Expecto (unchanged — test framework; no new test deps).

**Storage**: Unchanged from 002 (rolling-file audit log; in-memory
roster). The `Model` is in-process, same lifetime as today's `Hub`
(spec §Out of Scope: persisting `Model` across restarts is explicitly
out of scope).

**Testing**:
- New `tests/Broker.Mvu.Tests` project — pure-F# Expecto tests that
  build a `Model`, drive `update` synchronously via the test runtime,
  and assert on the resulting `Model` + Cmd list (FR-015, FR-017).
- `tests/Broker.Tui.Tests` extended — render the View to a
  deterministic string via Spectre's off-screen renderer; check in
  textual fixtures for the snapshot-regression pattern (FR-016, US5).
- `tests/Broker.Integration.Tests` — existing `SyntheticCoordinator`
  + scripting-client fan-out tests rebound onto the production
  runtime (real adapters, real gRPC, real audit sink) so end-to-end
  wire behaviour stays verified (FR-018, US3 acceptance).
- `tests/SurfaceArea` — baselines updated for the retired `Hub`
  surface and the new `Broker.Mvu.*` modules.

**Target Platform**: Cross-platform .NET 10 (Linux + Windows). No
change.

**Project Type**: Same six-project F# layout as 002, **plus** one new
project `src/Broker.Mvu` containing the `Model` / `Msg` / `update` /
`view` / `Cmd` / runtime surface. Adding a project is the simplest
path that respects Constitution Principle II (curated `.fsi` per
public module) and keeps the MVU core independently buildable and
testable. The alternative — folding MVU into `Broker.Core` —
would cross-contaminate the pure state-machine project with
runtime/dispatcher concerns that don't belong there.

**Performance Goals**: Re-anchored from 002 — SC-002 (game-tick →
scripting-client p95 ≤1 s), SC-003 (disconnect detect-to-Idle ≤10 s
in ≥95 % of trials), SC-004 (≥1 Hz dashboard at ≥4 clients + ≥200
units). Spec SC-007 explicitly forbids any observable additional
latency on TUI keystroke responsiveness or gRPC RPC turnaround
beyond the same per-tick budget. Performance is expected unchanged
or improved (one fewer lock acquisition per RPC vs the `withLock`
discipline — spec Assumptions).

**Constraints**:
- Single-session per broker (carried forward from 001/002).
- Single-threaded `update` execution against the `Model` (FR-003).
  The dispatcher is an `F# MailboxProcessor<Msg>` reader loop owned
  by `MvuRuntime.Host` (research §3); the mailbox-loop thread is the
  only writer of the running `Model` reference, removing the need
  for `withLock` / `Hub.stateLock`.
- Unbounded mailbox with `MailboxHighWater` audit (spec
  Clarification 2026-04-28 Q2). No drop / no reject / no eviction.
  Bounding the mailbox would deadlock gRPC handler threads that are
  awaiting a TaskCompletionSource for the response.
- Cmd execution failures route back as typed per-effect-family
  failure `Msg` arms — `AuditWriteFailed`, `CoordinatorSendFailed`,
  `ScriptingSendFailed`, `VizOpFailed`, `TimerFailed` — exhaustively
  matched in `update` (spec Clarification Q3, FR-008).
- Per-scripting-client outbound queues remain inside the scripting
  adapter (the `Channel<StateMsg>`). `Model` only observes their
  depth + high-water mark; bounded backpressure (FR-010 from 001)
  is enforced by the adapter and surfaces as `Msg.QueueOverflow`
  (spec Clarification Q1, FR-005).

**Scale/Scope**: Same envelope as 001/002 — one operator, one game,
≤8 scripting clients, hundreds of units.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-checked after Phase 1 design.*

| # | Principle | Status | Notes |
|---|-----------|--------|-------|
| I | Spec → FSI → Semantic Tests → Implementation | ✅ Pass | The new `Broker.Mvu` modules (`Model`, `Msg`, `Cmd`, `Update`, `View`, `MvuRuntime`, `TestRuntime`, plus adapter-interface modules) are drafted as `.fsi` first under `contracts/public-fsi.md`; exercised via an extended `scripts/prelude.fsx` that loads `Elmish` and a packed `Broker.Mvu`; semantic tests under `tests/Broker.Mvu.Tests` exercise the public surface, then `.fs` bodies are written. The four carve-out scenarios (T029/T037/T042/T046) are scripted as Expecto cases that drive the same `.fsi`-declared test runtime entry point. |
| II | Visibility lives in `.fsi`, not in `.fs` | ✅ Pass | One new project (`Broker.Mvu`) with curated `.fsi` for every public module: `Model.fsi`, `Msg.fsi`, `Cmd.fsi`, `Update.fsi`, `View.fsi`, `MvuRuntime.fsi`, `TestRuntime.fsi`, and the per-effect-family adapter-interface modules (`AuditAdapter`, `CoordinatorAdapter`, `ScriptingAdapter`, `VizAdapter`, `TimerAdapter`, `LifecycleAdapter`). New surface-area baselines under `tests/SurfaceArea/baselines/`. The retired `BrokerState.Hub` surface has its baseline deleted in the same change set; `Broker.Tui.TickLoop`'s reduced surface gets a refreshed baseline. `.fs` files have no `private`/`internal`/`public` keywords on top-level bindings. |
| III | Idiomatic Simplicity Is the Default | ✅ Pass | No SRTP, no type providers, no exotic CEs (the design uses plain records, plain DUs, and standard CEs only — `async`/`task`/`option`/`result`). The dispatcher is an `F# MailboxProcessor<Msg>` (Constitution III's standard-library bias — research §3); its loop body owns the running `Model` value as a regular `let` binding rebound on each `update` (no `let mutable` required because `MailboxProcessor.Receive` returns control to the loop body). Other mutability previously needed (`Hub.session <-`, `Hub.mode <-`, `withLock`) is **removed**, not added. The Elmish library is small, well-understood, and stable; its `Cmd` and `Program` primitives are themselves plain F# (records and functions). |
| IV | Synthetic Evidence Requires Loud, Repeated Disclosure | ✅ Pass (planned) | This feature **closes** four 001 carve-outs (T029/T037/T042/T046) by replacing synthetic-fixture evidence with MVU-replay evidence captured against the production code path. New `[S]` work expected: a small fixture builder (`Broker.Mvu.Testing.Fixtures`) for assembling representative `Model` values — synthetic by definition since the values would otherwise come from a live game. Disclosure plan mirrors 002: tagged at the 5 disclosure surfaces (task `[S]`, code-site comment, test-name token, fixture banner, PR enumeration). T035 (host-mode game-process management) is explicitly **not** closed; it remains a tracked carve-out. |
| V | Test Evidence Is Mandatory | ✅ Pass (planned) | Every functional requirement maps to at least one Expecto test (see `tasks.md` once generated). FR-001 through FR-008 (state, dispatch, effects) each have a dedicated unit test against `update`; FR-009 through FR-011 (view) each have an off-screen render test; FR-012 through FR-014 (inputs) have integration tests that drive the production runtime end-to-end through the existing `SyntheticCoordinator`. SC-008 ("`Hub.session <-` etc. greppable to zero hits") is verified by a repository-level guard test. |
| VI | Observability and Safe Failure | ✅ Pass | Cmd-execution failures are surfaced via typed per-effect-family `Msg` arms (FR-008) — no silent failure, no swallowed exception. The `MailboxHighWater` audit event (spec Clarification Q2) is a new structured-diagnostics signal for backpressure. The View function's failure mode is a rendered error panel (spec Edge Case "View function throws on a malformed Model"), not a dispatcher tear-down. The runtime emits `RuntimeStarted` / `RuntimeStopped` lifecycle audits. |

**Tier 1 contract surface change** —
- **Removed**: `Broker.Protocol.BrokerState.Hub` (and every public
  function on the `Hub` surface — `openHostSession`,
  `openGuestSession`, `closeSession`, `attachCoordinator`,
  `coordinatorCommandChannel`, `mode`, `roster`, `slots`, `session`,
  …); the corresponding surface-area baseline.
- **Reduced**: `Broker.Protocol.BrokerState` shrinks to a small
  Msg-translation surface (`postMsg`, `awaitResponse`, `init`) used
  by gRPC handlers; `Broker.Tui.TickLoop` shrinks to a thin
  keypress-poll-and-render shell that hosts `MvuRuntime.Host` and
  feeds `Msg.Hotkey` values into the mailbox.
- **Added**: New project `Broker.Mvu` exporting the public modules
  enumerated under Constitution Check II, with new surface-area
  baselines. New public Cmd / Msg cases; the `Elmish` package as a
  declared dependency in `Directory.Packages.props`. Production
  adapter implementations are added to the projects that own each
  I/O subsystem (Audit / Timer / Lifecycle in `Broker.App`,
  Coordinator / Scripting in `Broker.Protocol`, Viz in `Broker.Viz`)
  per research §4 — the adapter *interfaces* live in `Broker.Mvu`,
  the *implementations* live close to the I/O they own.

**No constitutional violations to track in Complexity Tracking.**

### Post-Phase-1 re-evaluation (2026-04-28)

Re-checked the gates after `data-model.md`, `contracts/public-fsi.md`,
and `quickstart.md` were drafted. No new violations:

- Principle II is backed by concrete `.fsi` sketches in
  `contracts/public-fsi.md` for every new public module in
  `Broker.Mvu`, and for the reduced `Broker.Protocol.BrokerState`
  and `Broker.Tui.TickLoop` surfaces.
- Principle III holds — the sketches use plain records, plain
  unions, and standard CEs only. The dispatcher uses
  `MailboxProcessor<Msg>` (an F# standard-library primitive); the
  loop body re-binds `Model` on each iteration without `let mutable`.
- Principle IV — the fixture builder under
  `Broker.Mvu.Testing.Fixtures` is the only new synthetic surface,
  and its disclosure plan is captured in `data-model.md` §6.
- Principle VI — the `Cmd` failure routing surface in
  `contracts/public-fsi.md` makes per-effect-family failure arms
  the only failure path; no generic `CmdFailed` arm is permitted.

Complexity Tracking remains empty.

## Project Structure

### Documentation (this feature)

```text
specs/003-elmish-mvu-core/
├── spec.md                         # Feature spec (already authored)
├── plan.md                         # This file
├── research.md                     # Phase 0 — Elmish-on-.NET decisions, runtime architecture
├── data-model.md                   # Phase 1 — Model/Msg/Cmd entity catalogue
├── contracts/
│   └── public-fsi.md               # F# .fsi sketches for the new Broker.Mvu surface
├── quickstart.md                   # Phase 1 — operator + maintainer walkthrough
└── tasks.md                        # Phase 2 output (NOT created by /speckit-plan)
```

### Source Code (delta from 002)

```text
src/
├── Broker.Contracts/                       # Unchanged — gRPC .proto + F# generated types.
│
├── Broker.Core/                            # Unchanged at the public surface.
│   │   The pure state-machine modules (Mode, Session, Lobby,
│   │   ScriptingRoster, Snapshot, Audit, …) survive the pivot
│   │   verbatim. They are pure data + transitions; the MVU core
│   │   composes them. Internal callers shift from `Hub.foo` to
│   │   the corresponding `Model.foo` field, but this happens
│   │   inside `Broker.Mvu.Update` which is not part of `Broker.Core`.
│
├── Broker.Mvu/                             # ⊕ NEW — the MVU spine (Broker.Core + Elmish + Spectre.Console deps only)
│   ├── Model.fsi/.fs                           # the immutable Model record + builder
│   ├── Msg.fsi/.fs                             # the Msg discriminated union (every input)
│   ├── Cmd.fsi/.fs                             # the Cmd DU — AuditCmd / CoordinatorOutbound /
│   │                                           # ScriptingOutbound / ScriptingReject / VizCmd /
│   │                                           # ScheduleTick / EndSession / Quit / Batch / NoOp
│   ├── Update.fsi/.fs                          # update : Msg -> Model -> Model * Cmd list
│   ├── View.fsi/.fs                            # view : Model -> Spectre.Console.IRenderable
│   ├── MvuRuntime.fsi/.fs                      # `MvuRuntime.Host` — production runtime;
│   │                                           # owns the MailboxProcessor<Msg> dispatcher,
│   │                                           # custom Elmish Program setState, and the
│   │                                           # adapter-registration entry points.
│   ├── TestRuntime.fsi/.fs                     # synchronous test runtime — captures Cmds
│   ├── Adapters/                               # adapter *interfaces* (function-shapes)
│   │   ├── AuditAdapter.fsi/.fs                # interface: AuditEvent -> Async<unit>
│   │   ├── CoordinatorAdapter.fsi/.fs          # interface: outbound command callbacks
│   │   ├── ScriptingAdapter.fsi/.fs            # interface: per-client send / reject /
│   │   │                                       # depth-observation hooks. Production impl
│   │   │                                       # owns the Channel<StateMsg> per spec Q1.
│   │   ├── VizAdapter.fsi/.fs                  # interface: VizOp -> unit
│   │   ├── TimerAdapter.fsi/.fs                # interface: schedule / cancel ticks
│   │   └── LifecycleAdapter.fsi/.fs            # interface: SessionEnd broadcast + process exit
│   ├── Testing/
│   │   └── Fixtures.fsi/.fs                    # `[S]` Model fixtures + Msg-stream builders
│   └── Broker.Mvu.fsproj
│
├── Broker.Protocol/                        # Reduced surface
│   ├── BackpressureGate.fsi/.fs                # unchanged in shape; rebound to the production
│   │                                           # ScriptingAdapter implementation that lives here.
│   ├── BrokerState.fsi/.fs                     # ⚠ REDUCED — the `Hub` type and every mutation
│   │                                           # method are removed (FR-001/SC-008). What
│   │                                           # remains is a small Msg-translation surface
│   │                                           # (`postMsg`, `awaitResponse`, `init`) the
│   │                                           # gRPC services use to talk to MvuRuntime.Host.
│   ├── CoordinatorAdapterImpl.fsi/.fs          # ⊕ NEW — production CoordinatorAdapter
│   │                                           # implementation; drains the runtime-emitted
│   │                                           # outbound `Channel<Command>` and writes to
│   │                                           # the active `OpenCommandChannel` server-stream.
│   ├── ScriptingAdapterImpl.fsi/.fs            # ⊕ NEW — production ScriptingAdapter
│   │                                           # implementation; owns per-client
│   │                                           # `Channel<StateMsg>`, enforces FR-010
│   │                                           # bounded backpressure, posts QueueDepth /
│   │                                           # QueueOverflow Msgs back to the runtime.
│   ├── HighBarCoordinatorService.fsi/.fs       # ⚠ UPDATED — RPC handlers translate inbound
│   │                                           # calls into Msg dispatches via BrokerState
│   │                                           # postMsg + TaskCompletionSource awaits;
│   │                                           # no direct state mutation (FR-013).
│   ├── ScriptingClientService.fsi/.fs          # ⚠ UPDATED — same change as above
│   ├── ServerHost.fsi/.fs                      # unchanged shape; constructed against the
│   │                                           # MvuRuntime.Host handle injected by App.
│   ├── VersionHandshake.fsi/.fs                # unchanged
│   ├── WireConvert.fsi/.fs                     # unchanged
│   └── Broker.Protocol.fsproj                  # add ProjectReference to Broker.Mvu
│
├── Broker.Tui/                             # Reduced surface
│   ├── DashboardView.fsi/.fs                   # ⚠ UPDATED — accepts `Model` (or a projection
│   │                                           # of it) instead of a `DiagnosticReading` from
│   │                                           # `Hub`. Returns `IRenderable` as today.
│   │                                           # Composed by `Broker.Mvu.View`.
│   ├── HotkeyMap.fsi/.fs                       # unchanged
│   ├── Layout.fsi/.fs                          # unchanged
│   ├── LobbyView.fsi/.fs                       # ⚠ UPDATED — accepts the lobby fragment of
│   │                                           # `Model`; composed by `Broker.Mvu.View`.
│   ├── TickLoop.fsi/.fs                        # ⚠ REDUCED — research §7. Becomes a thin
│   │                                           # keypress-poll-and-render shell:
│   │                                           # (1) poll Console.KeyAvailable, translate
│   │                                           # to `Msg.Hotkey`, post to MvuRuntime.Host;
│   │                                           # (2) on each tick, read latest `Model`
│   │                                           # broadcast, call `Broker.Mvu.View.view`,
│   │                                           # feed `IRenderable` to LiveDisplay.Update.
│   │                                           # The `dispatch` and `CoreFacade` surface
│   │                                           # from feature 001 is REMOVED.
│   └── Broker.Tui.fsproj                       # add ProjectReference to Broker.Mvu
│
├── Broker.Viz/                             # Lightly extended
│   └── VizAdapterImpl.fsi/.fs                  # ⊕ NEW — production VizAdapter implementation;
│                                               # owns the dedicated SkiaViewer task that
│                                               # drains a per-adapter VizOp channel.
│
└── Broker.App/                             # ⚠ UPDATED — composition root
    ├── AuditAdapterImpl.fsi/.fs                # ⊕ NEW — production AuditAdapter (Serilog).
    ├── TimerAdapterImpl.fsi/.fs                # ⊕ NEW — production TimerAdapter
    │                                           # (System.Threading.Timer per registered tick).
    ├── LifecycleAdapterImpl.fsi/.fs            # ⊕ NEW — process exit + SessionEnd broadcast.
    ├── Program.fs(.fsi)                        # ⚠ UPDATED — constructs Model, registers the
    │                                           # six production adapters, starts MvuRuntime.Host,
    │                                           # binds the gRPC server (via ServerHost), runs
    │                                           # `Broker.Tui.TickLoop` for keystrokes + render.
    │                                           # The `withLock`/`Hub.stateLock` plumbing is
    │                                           # removed.
    ├── Cli.fs/.fsi                             # unchanged
    ├── GameProcess.fs/.fsi                     # unchanged
    ├── Logging.fs/.fsi                         # unchanged
    └── VizControllerImpl.fs/.fsi               # ⚠ UPDATED — adapts to the new VizAdapter
                                                # interface signature.

tests/
├── Broker.Contracts.Tests/                 # Unchanged
├── Broker.Core.Tests/                      # Unchanged
├── Broker.Mvu.Tests/                       # ⊕ NEW
│   ├── UpdateTests.fs                          # FR-001..FR-008 — pure update behaviour
│   ├── ViewTests.fs                            # FR-009..FR-011 — off-screen render + fixtures
│   ├── RuntimeTests.fs                         # mailbox high-water audit, single-thread,
│   │                                           # per-effect-family failure routing
│   ├── HubRetirementGuardTests.fs              # SC-008 — ripgrep-based guard test asserting
│   │                                           # zero hits for `Hub.session <-`, `Hub.mode <-`,
│   │                                           # `withLock` outside historical specs/comments.
│   ├── CarveoutT029Tests.fs                    # ⊕ closes carve-out (broker–proxy transcript)
│   ├── CarveoutT037Tests.fs                    # ⊕ closes carve-out (host-mode admin walkthrough)
│   ├── CarveoutT042Tests.fs                    # ⊕ closes carve-out (dashboard under load)
│   ├── CarveoutT046Tests.fs                    # ⊕ closes carve-out (viz status line)
│   ├── Fixtures/                               # `[S]` checked-in Model + render fixtures
│   │   ├── dashboard-guest-2clients.txt
│   │   ├── dashboard-host-elevated.txt
│   │   └── viz-active-footer.txt
│   └── Broker.Mvu.Tests.fsproj
├── Broker.Protocol.Tests/                  # ⚠ UPDATED — drop tests against Hub; add tests
│   │                                       # that drive the gRPC service Impl through
│   │                                       # MvuRuntime.Host to assert that inbound RPCs
│   │                                       # translate to Msg dispatches and the response
│   │                                       # is read back from the resulting Model.
├── Broker.Tui.Tests/                       # ⚠ UPDATED — old `dispatch` tests retired
│   │                                       # alongside the reduced TickLoop. Replaced with
│   │                                       # off-screen render tests against `Broker.Tui.View`
│   │                                       # composition (Spectre layout primitives).
├── Broker.Integration.Tests/               # Unchanged shape; tests rebound to production
│   │                                       # MvuRuntime.Host (`SyntheticCoordinator` +
│   │                                       # Scripting fan-out still exercise real gRPC,
│   │                                       # real audit, real Spectre live render). All US3
│   │                                       # acceptance scenarios live here.
├── Lib.Tests/                              # Unchanged
└── SurfaceArea/baselines/
    ├── Broker.Protocol.BrokerState.surface.txt        # ⚠ REGENERATED (Hub removed; small
    │                                                  # Msg-translation surface remains)
    ├── Broker.Tui.TickLoop.surface.txt                # ⚠ REGENERATED (reduced surface)
    ├── Broker.Mvu.Model.surface.txt                   # ⊕ NEW
    ├── Broker.Mvu.Msg.surface.txt                     # ⊕ NEW
    ├── Broker.Mvu.Cmd.surface.txt                     # ⊕ NEW
    ├── Broker.Mvu.Update.surface.txt                  # ⊕ NEW
    ├── Broker.Mvu.View.surface.txt                    # ⊕ NEW
    ├── Broker.Mvu.MvuRuntime.surface.txt              # ⊕ NEW
    ├── Broker.Mvu.TestRuntime.surface.txt             # ⊕ NEW
    ├── Broker.Mvu.Adapters.AuditAdapter.surface.txt        # ⊕ NEW
    ├── Broker.Mvu.Adapters.CoordinatorAdapter.surface.txt  # ⊕ NEW
    ├── Broker.Mvu.Adapters.ScriptingAdapter.surface.txt    # ⊕ NEW
    ├── Broker.Mvu.Adapters.VizAdapter.surface.txt          # ⊕ NEW
    ├── Broker.Mvu.Adapters.TimerAdapter.surface.txt        # ⊕ NEW
    └── Broker.Mvu.Adapters.LifecycleAdapter.surface.txt    # ⊕ NEW

readiness/                                  # T029/T037/T042/T046 closure artefacts
└── (regenerated under specs/001-tui-grpc-broker/readiness/ — see spec FR-021)
```

**Structure Decision**: Add one new project `Broker.Mvu` between
`Broker.Core` (pure state) and `Broker.Protocol`/`Broker.Tui`/
`Broker.App` (the integration projects). The new project owns the
single `Model` value, the `Msg` union, the `Cmd` envelope, the
`update` function, the `view` function, both the production and
test runtimes, and **adapter-interface modules** — the six adapter
*interfaces* (Audit / Coordinator / Scripting / Viz / Timer /
Lifecycle) sit here so `update` can name them, but adapter
*implementations* live with the I/O subsystems they own (research
§4):

- `Broker.App` hosts production Audit, Timer, Lifecycle adapters
  (Serilog, `System.Threading.Timer`, process-exit/SessionEnd).
- `Broker.Protocol` hosts production Coordinator and Scripting
  adapters (they sit alongside the gRPC services that drive their
  outbound channels).
- `Broker.Viz` hosts the production Viz adapter (it owns the
  long-lived SkiaViewer task).

Test adapter implementations live in `tests/Broker.Mvu.Tests` (and,
where the unit under test is a service, `tests/Broker.Protocol.Tests`)
as in-memory recorders.

`Broker.Core` stays a pure-state library; nothing in `Broker.Core`
gains a dependency on Elmish or on the runtime. `Broker.Protocol`
and `Broker.Tui` shed their state-management code: `Hub` is removed
from `Broker.Protocol`, and `Broker.Tui.TickLoop` is reduced from
"owner of dispatch + state mutation via `CoreFacade`" to a thin
keypress-poll-and-render shell that hosts `MvuRuntime.Host`. Both
projects become thinner integration layers that only translate
between the wire / TTY and `Msg` values.

The composition root in `Broker.App.Program` constructs
`MvuRuntime.Host` with the six production adapters, binds the gRPC
server via `ServerHost`, and runs `Broker.Tui.TickLoop` for
keystrokes and render. The `withLock`/`Hub.stateLock` plumbing is
removed in the same change set (research §8). The gRPC services
keep their public class names and DI lifetimes — only their
handler bodies change (from "mutate Hub" to "dispatch Msg, await
the TaskCompletionSource the matching Cmd will complete, write the
response").

## Complexity Tracking

> **Empty by design — Constitution Check passed without violations.**

Two judgement calls worth recording, neither a deviation from any
principle:

1. **One new project (`Broker.Mvu`) rather than folding MVU into
   `Broker.Core`.** Folding would force `Broker.Core` to depend on
   Elmish and on Spectre.Console (because `view` returns
   `IRenderable`), which would contaminate the pure state-machine
   project. The boundary cost (one more `.fsproj`, one more set of
   `.fsi` baselines) is far smaller than the cost of mixing data
   types with the dispatcher. This is the same separation feature
   001 used between `Broker.Core` (pure) and `Broker.Protocol`
   (gRPC integration).
2. **Custom `MvuRuntime.Host` instead of the stock `Elmish.Program.run`
   default.** Elmish's stock `Program.run` ties the dispatch loop to
   the thread that calls it, which collides with Spectre's
   `LiveDisplay` single-thread requirement (research §2). The
   custom host swaps in a `setState` hook that pushes the new `Model`
   into a broadcast channel the render thread drains at the
   existing tick cadence. This is the library-blessed extension
   point for hosts that aren't React/HTML, not a workaround. The
   `Model` / `Msg` / `Cmd` / `update` / `view` definitions sit
   directly on top of Elmish's stock `Program` builder — a future
   swap to a different runner shape is mechanical.

If Elmish on .NET ever surfaces a `Program` primitive that fits the
broker's input fan-in (TUI keystrokes + N gRPC services + timers +
Cmd callbacks) directly, the future swap is mechanical (replace
the `MvuRuntime.Host` body with the canned runner; the `Model` /
`Msg` / `Cmd` / `update` / `view` definitions are unaffected).
