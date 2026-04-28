# Phase 0 Research: Elmish MVU Core for State and I/O

**Feature**: 003-elmish-mvu-core
**Date**: 2026-04-28
**Status**: Complete — all `NEEDS CLARIFICATION` resolved

This document records the technical decisions for pivoting the broker's
state and I/O spine to an Elmish-style Model–View–Update loop, with
rationale and rejected alternatives. It is the authoritative reference
for `plan.md`'s Technical Context. Decisions from features 001 and 002
remain in force for everything not contradicted here (gRPC stack,
`FSharp.GrpcCodeGenerator`, Spectre.Console, Serilog audit sink,
SkiaViewer, Expecto + `dotnet test`).

---

## 1. MVU library choice — `Elmish` (the core abstract package)

- **Decision**: Take a runtime dependency on the `Elmish` NuGet
  package (https://github.com/elmish/elmish, the host-agnostic core
  library — *not* `Fable.Elmish.React`, `Fable.Elmish.Browser`, or any
  Fable-targeted package). Pin to the latest stable 4.x release. The
  package supplies the `Program<'arg, 'model, 'msg, 'view>` abstraction,
  the `Cmd<'msg>` effect type, the `Sub<'msg>` subscription type, and a
  pluggable runner — exactly the surface the broker needs and nothing
  more.
- **Rationale**:
  - The user's spec input cites
    `https://github.com/elmish/elmish` and
    `https://elmish.github.io/elmish/docs/basics.html` directly. The
    `Elmish` package on NuGet is the same library referenced there.
  - It is a tiny library (≈1 kLOC) with no transitive dependencies of
    its own. The .NET-targeted variant has no Fable / JavaScript /
    React surface at all — those live in separate packages we do
    *not* take.
  - It runs on any host: it does not assume a UI thread, a render
    loop, or a particular scheduler. The broker host (Spectre live
    display + gRPC threads + timer Cmds) plugs into it with a single
    custom `setState` hook.
  - It is mature and stable (v4 released years ago, low churn). The
    constitution's "dependencies are minimised" rule (Engineering
    Constraints) is preserved: we add one package, not a stack.
- **Alternatives considered**:
  - **Hand-roll a minimal MVU runtime** (Model, Msg, update, Cmd
    list executed by our own dispatcher). Rejected: the user's spec
    input names Elmish specifically, and rolling our own duplicates
    a well-tested library for no gain. The ergonomics savings (no
    package, no extra import) are negligible against the cost of
    reinventing `Cmd.OfAsync`, `Cmd.batch`, `Cmd.map`, `Sub` etc.
  - **Use `MailboxProcessor<Msg>` directly with no MVU wrapper.**
    Rejected: a MailboxProcessor gives us a queue and a single-
    threaded reader, but no abstraction over side effects, no
    `Cmd.batch` / `Cmd.map` / `Sub` machinery, no testable seam at
    "what Cmds did this Msg produce". We would be re-implementing
    Elmish badly. Mailbox might still appear *inside* the runtime
    as the dispatch queue (see §3); that is an implementation
    choice, not an architecture choice.
  - **F#-port a TEA library from another language** (e.g., Ocaml
    `bonsai`, Haskell `reflex`, Rust `iced`). Rejected: heavier
    abstractions (incremental computation, FRP) than the broker
    needs, and each would be the heaviest dependency in the repo.
  - **Use `IObservable` / Rx as the glue.** Rejected: Rx is a
    different model (streams of values, not state machines) and
    pushes us back toward effects-mixed-with-state. We already use
    `IObservable` in one narrow place (`BrokerState.snapshots`) for
    viz push; that observable becomes a `Sub<Msg>` in the new
    runtime.

## 2. Runner topology — single Elmish program, custom host

- **Decision**: One `Elmish.Program` instance per broker process,
  constructed in `Broker.App.Program.run` and passed to a custom
  runner the broker owns. The custom runner — call it
  `MvuRuntime.Host` — replaces Elmish's default `Program.run`
  (which assumes a console-print loop) with broker-specific
  setState behaviour: feed each new `Model` into a `Channel<Model>`
  the Spectre-render thread drains at the existing tick cadence,
  and feed each emitted `Cmd` into the appropriate side-effect
  adapter pool (see §4).
- **Rationale**:
  - Elmish's `Program` is parameterised on `init`, `update`,
    `view`, and `setState`. Supplying our own `setState` is the
    library-blessed extension point for hosts that aren't
    React/HTML.
  - Keeps Spectre.Console as the rendering target without forcing
    it into the Elmish runtime: the dispatcher computes the next
    `Model`, the render thread reads the latest `Model` and asks
    `view` to project it. The two threads share only an immutable
    `Model` reference passed through a bounded channel — no
    locking on a mutable state record.
  - One program per process matches one `Hub` per process from
    feature 001 — same lifecycle, same single-session invariant.
- **Alternatives considered**:
  - **Run multiple Elmish programs (one per session, one per
    client).** Rejected: cross-program state synchronisation
    becomes the new mutable shared state. The broker's invariants
    (single session, global roster, shared dashboard) sit naturally
    inside one Model.
  - **Use `Program.runWith` directly with no custom host.**
    Rejected: the default runner ties the dispatch loop to the
    thread that called `runWith`, which collides with the existing
    Spectre LiveDisplay's single-thread requirement. A custom host
    decouples them cleanly.

## 3. Dispatcher implementation — `MailboxProcessor<Msg>` inside `MvuRuntime.Host`

- **Decision**: The `MvuRuntime.Host` runs a single
  `MailboxProcessor<Msg>` reader loop. Every `Msg` enters this
  mailbox; the loop applies `update`, broadcasts the new `Model`,
  and dispatches each emitted `Cmd` to its adapter (which, on
  completion, may post a follow-up `Msg` back to the same
  mailbox).
- **Rationale**:
  - `MailboxProcessor` is the F# standard-library answer to
    "single-reader, in-order, bounded queue", which is exactly the
    shape FR-002 + FR-003 require. No new dependency, no
    reinvented primitive.
  - The broker already uses `Channel<T>` for gRPC outbound; using
    `MailboxProcessor` for the dispatcher inbound keeps the two
    seams visually distinct and matches their distinct usage
    patterns (channel = stream, mailbox = command queue).
  - Single-reader semantics replace the `withLock` discipline from
    feature 001 / 002 directly. The lock disappears because the
    only writer is the mailbox-loop thread.
  - Bounded: `MailboxProcessor` does not bound by default, but the
    broker's host exposes a queue-depth Msg under FR-006 / edge case
    "messages faster than `update`", and the runtime audit-emits a
    warning when depth crosses a configured high-water mark. This
    is an audit Cmd, not a hard reject — backpressure on Msgs
    arriving from gRPC handlers would deadlock the gRPC thread.
- **Alternatives considered**:
  - **`System.Threading.Channels.Channel<Msg>` with an explicit
    reader Task.** Workable, but produces almost the same shape as
    `MailboxProcessor` with a bit more ceremony. `MailboxProcessor`
    is the idiomatic F# choice (Constitution III) and keeps the
    code legible.
  - **`BlockingCollection<Msg>`.** Rejected: lower-level, fewer F#
    idioms, no async-friendly receive.
  - **Direct method-call dispatch (no queue) with a lock.**
    Rejected: that is what we have today (`Hub.stateLock`) and
    exactly what the spec is removing.

## 4. Cmd execution — one adapter per side-effect family

- **Decision**: The Cmd type is a discriminated union with one arm
  per side-effect family the broker performs:

  | `Cmd` arm | Adapter |
  |-----------|---------|
  | `AuditCmd of Audit.AuditEvent` | `AuditAdapter` → existing Serilog pipeline |
  | `CoordinatorOutbound of CommandPipeline.Command` | `CoordinatorAdapter` → existing `Channel<Command>` drained by `OpenCommandChannel` RPC |
  | `ScriptingOutbound of clientId:ScriptingClientId * StateMsg` | `ScriptingAdapter` → existing per-client `Channel<StateMsg>` |
  | `ScriptingReject of clientId:ScriptingClientId * Reject` | same adapter |
  | `VizCmd of VizOp` | `VizAdapter` → existing `VizControllerImpl` |
  | `ScheduleTick of delayMs:int * Msg` | `TimerAdapter` → `System.Threading.Timer` |
  | `EndSession of reason:Session.EndReason` | `LifecycleAdapter` → emits to all subscribed adapters and posts `SessionEnded` Msg |
  | `Quit of exitCode:int` | `LifecycleAdapter` → triggers app-level `cts.Cancel()` |
  | `Batch of Cmd list` | recursive |
  | `NoOp` | -- |

  Each adapter has two implementations: production (does the I/O)
  and test (records the call into an in-memory list). The
  production adapter set is registered by `Broker.App.Program`;
  the test adapter set is registered by integration / TUI tests.

- **Rationale**:
  - One adapter per family keeps the production runtime parsable —
    a reviewer can answer "what does this Cmd do?" by looking at
    one .fs file. Cross-family coupling stays in `update`, where
    the type system catches it.
  - Test adapters record by appending to a `ResizeArray<Cmd>` — the
    simplest possible recording surface, exposed as `IReadOnlyList`
    to the test. FR-017 ("structural inspection of emitted Cmds")
    is satisfied by direct equality on Cmd values, no test-only
    DSL required.
  - The Cmd type is **the** central data type of this feature.
    Putting it in `Broker.Core` (the lowest project) lets every
    other layer build adapters without circular references.
- **Alternatives considered**:
  - **Free-monad / interpreter-style Cmds with phantom-typed
    return values.** Rejected: the constitution discourages SRTP /
    exotic CEs / clever abstractions (Principle III). A simple DU
    is enough.
  - **Use Elmish's stock `Cmd` with `OfAsync` / `OfTask` only.**
    Rejected: stock Cmd is "fire-and-forget Async producing a
    Msg", which works for one-shot effects but does not give us a
    typed inventory of effects to assert in tests. The DU form is
    a strict generalisation: `Cmd.OfAsync` machinery is still
    available where it fits, but the spine is our typed union.
  - **Side-effect adapters as a single big `IBrokerEffects`
    interface.** Rejected: monolithic interfaces grow without
    bound and fight the type-checker's exhaustive-match win.

## 5. View — pure `Model -> Spectre.Layout`

- **Decision**: The `view` function takes a `Model` and returns a
  `Spectre.Console.Rendering.IRenderable` (the protocol Spectre's
  `LiveDisplay` consumes). Production renders by calling
  `ctx.UpdateTarget(view model)` on the next tick; tests render by
  calling Spectre's `AnsiConsole.Create(AnsiConsoleSettings(Out =
  AnsiConsoleOutput(stringWriter), Interactive = No, ColorSystem =
  No))` — the off-screen renderer Spectre ships explicitly for non-
  interactive consumers.
- **Rationale**:
  - Spectre's off-screen renderer is the supported way to convert
    a renderable to a string. It is the same code path Spectre's
    own snapshot tests use; it is not a hack.
  - Returning `IRenderable` (not a string) keeps `view` zero-
    allocation in the hot path and lets the production runtime
    pass it to LiveDisplay's diff machinery.
  - View depending on `Spectre.Console` already happens in
    `Broker.Tui.DashboardView` today — the pivot does not enlarge
    Tui's dependency footprint.
- **Alternatives considered**:
  - **View returns a string.** Rejected: forfeits Spectre's diff /
    re-render optimisation; allocates a fresh string every tick.
  - **View returns a custom typed AST that we then translate to
    Spectre.** Rejected: adds a layer for no win; Spectre's
    renderable protocol *is* an AST.

## 6. Inputs — three sources, one mailbox

- **Decision**: Three input adapters, each a thin translator that
  feeds the dispatcher's mailbox:
  1. **Keypress adapter** — replaces today's `Console.KeyAvailable`
     polling in `TickLoop`. Polls on the same render thread (or a
     dedicated reader Task), translates `ConsoleKeyInfo` →
     `HotkeyMap.Action` → `Msg.Hotkey`, posts to mailbox.
  2. **gRPC RPC adapter** — each handler in
     `HighBarCoordinatorService` and `ScriptingClientService` posts
     a Msg with a TaskCompletionSource for the response, awaits the
     TCS, returns the response. The `update` clause for that Msg
     produces the response value via a Cmd that completes the TCS.
     RPC handlers do not mutate state; they wait for Cmds to wake
     them.
  3. **Timer adapter** — heartbeat watchdog (FR-008), dashboard
     staleness probe (FR-021), refresh tick (the existing 100 ms
     cadence) all become `Cmd.ScheduleTick` registrations whose
     firing posts a `Msg.Tick` to the mailbox.
- **Rationale**:
  - Keeps the gRPC service classes thin: they translate proto
    types to/from Msgs and never touch broker state directly.
  - The TCS roundtrip looks heavier than direct mutation but is
    bounded by `update`'s execution time, which is microseconds
    today and does not change. RPC turnaround is dominated by
    gRPC framing + network, not by the dispatcher.
  - Putting timers behind Cmds makes tests deterministic — there
    is no real `System.Threading.Timer` in tests, only a recorded
    `ScheduleTick` Cmd that the test fires synthetically.
- **Alternatives considered**:
  - **gRPC handlers post-and-don't-wait, computing the response
    from a side-effect adapter.** Rejected: response derivation
    needs the post-update `Model`, which is not visible outside
    `update` without re-reading shared state.
  - **One Msg per RPC type with a callback-shaped continuation
    (instead of TCS).** Equivalent in practice; TCS is the
    standard .NET idiom and integrates with `Task<T>`-returning
    gRPC handlers without additional plumbing.

## 7. Project layout — new `Broker.Mvu` library, light edits in others

- **Decision**: Add a new library project `Broker.Mvu`, depending
  only on `Broker.Core` (no Tui, no Protocol, no App reference).
  It contains the shared `Model`, `Msg`, `Cmd` types, the `update`
  function, the `view` function, and the `MvuRuntime.Host`. The
  existing `Broker.Tui` project shrinks: `TickLoop.fs` becomes a
  thin keypress-poll-and-render shell that hosts `MvuRuntime.Host`
  and feeds keypress Msgs in. `Broker.Protocol`'s gRPC services
  shrink to "translate proto → Msg → translate Cmd-set/Model →
  proto". `Broker.Core` does *not* gain MVU types — it stays a
  pure domain library. `Broker.App.Program` constructs Model,
  registers production adapters, and starts `MvuRuntime.Host`.
- **Rationale**:
  - Putting Model + Msg + Cmd + update + view in one library makes
    the type-checker the architectural enforcer: every reference
    to `Model` flows through the same `Broker.Mvu` namespace.
  - The library has no runtime dependencies beyond `Broker.Core`
    and the `Elmish` package, so its tests can be the fastest
    project in the suite — no Spectre instantiation, no gRPC
    server, no audit file.
  - Visibility lives in `.fsi` per Constitution II: each public
    module in `Broker.Mvu` (`Model`, `Msg`, `Cmd`, `Update`,
    `View`, `MvuRuntime`) gets a curated `.fsi`. `update` and
    `view` are exposed as top-level values; the runtime's
    internals are hidden.
  - Constitution III is honoured — pipelines + records + DU
    pattern matching only. No SRTP, no type providers, no exotic
    CEs.
- **Alternatives considered**:
  - **Put MVU types directly in `Broker.Core`.** Rejected: pulls
    `Elmish` into the lowest layer, which adds a transitive runtime
    dep to every project (including the test runners). The
    domain library should not know it is being driven by MVU.
  - **Put MVU in `Broker.Tui`.** Rejected: `Broker.Protocol` would
    have to reference `Broker.Tui` to dispatch RPC Msgs, which
    inverts today's project DAG (`Broker.Tui` depends on
    `Broker.Core` only; gRPC depends on `Broker.Protocol` →
    `Broker.Core`). New project preserves the DAG.
  - **One MVU project per domain (TuiMvu, NetMvu, …).** Rejected:
    the broker has one `Model`, not many. Splitting it would
    reintroduce cross-program synchronisation.

## 8. Replacing `Hub` — single-shot deletion, no parallel runtime

- **Decision**: `BrokerState.Hub` and its `stateLock` are removed
  in the same change set that lands `Broker.Mvu`. There is no
  Hub-and-MVU-coexisting interim — `BrokerState.fsi` is rewritten
  to expose the gRPC services' Msg-translation surface (`postMsg`,
  `awaitResponse`) and a small bootstrap `init` that builds the
  initial `Model` from CLI args. Adapters that need to read live
  state for an effect (e.g., the dashboard renderer needing the
  latest snapshot) get the `Model` from the dispatcher's broadcast
  channel, not from a shared mutable.
- **Rationale**:
  - A halfway state — `Hub` for some mutations, `Model` for
    others — would carry both correctness models simultaneously and
    is strictly worse than either endpoint. Spec assumption:
    "The pivot ships as a single feature, not incrementally". This
    research formalises that.
  - Greppable confirmation per SC-008: zero hits for `withLock`,
    `Hub.session <-`, `Hub.mode <-` after the change set lands.
  - Ripping out the lock removes a class of latent deadlocks (RPC
    thread acquires lock, audit emit takes its own lock, and
    today both have to be careful never to call into each other
    under the broker lock). The single-mailbox-reader model has no
    such pitfall.
- **Alternatives considered**:
  - **Strangler-fig migration** (Hub stays, Mvu added; routes
    move over one at a time). Rejected on user instruction in the
    spec; also rejected on cost/benefit — every route already
    funnels through `Hub`'s small public surface, so a one-shot
    rewrite touches the same files a strangler would have touched
    by the end, plus no double maintenance during transition.

## 9. `SyntheticCoordinator` — kept, repurposed as a wire-level integration peer

- **Decision**: The `tests/Broker.Integration.Tests/SyntheticCoordinator.fs`
  fixture from feature 002 stays. Integration tests that exercise
  the gRPC wire end-to-end (Kestrel binds, real gRPC handshake,
  real protobuf framing) still use it as the loopback peer. The
  new MVU-replay tests sit one layer below the wire — they feed
  `Msg`s directly to `update`, bypassing gRPC entirely.
- **Rationale**:
  - The two test layers exercise different invariants. MVU-replay
    tests prove "the broker would do the right thing if it
    received this Msg". `SyntheticCoordinator`-driven integration
    tests prove "the gRPC wire correctly translates plugin frames
    into those Msgs". Both are needed; deleting either weakens
    coverage.
  - Carving out T029, T037, T042, T046 (spec FR-021) is closed by
    the MVU-replay layer: those tasks' carve-out reason is
    "running game / TTY required", which the MVU-replay layer
    removes. Wire-translation correctness was never the carve-out
    reason and stays covered by the integration layer.
- **Alternatives considered**:
  - **Drop `SyntheticCoordinator` and rely only on MVU-replay
    tests.** Rejected: would silently lose wire-translation
    coverage. A bug in `WireConvert.applyHighBarStateUpdate` would
    be invisible to MVU-replay tests because they bypass it.

## 10. Performance model — no regression budget required

- **Decision**: The pivot does not introduce a performance
  regression budget. The expected wall-clock cost per Msg is
  comparable to, and likely lower than, the current `withLock`-
  wrapped mutation cost.
- **Rationale**:
  - Current path: gRPC handler → `Monitor.Enter(stateLock)` →
    mutate Hub fields → `Monitor.Exit` → return.
  - New path: gRPC handler → `mailbox.Post(Msg)` → wait on TCS →
    return. Internally, the mailbox loop runs `update` on the
    queued Msg with no lock acquisition.
  - On a single-core hot path the two are within microseconds of
    each other; on a multi-core machine the new path is faster
    because it removes the lock-contention window between RPC
    threads.
  - Existing performance SCs (001 SC-003, 002 SC-002 for game-
    tick → scripting-client p95 ≤1 s) are about gRPC framing +
    fan-out, not the dispatcher. The MVU rewrite cannot regress
    them noticeably.
- **Alternatives considered**:
  - **Adopt a regression budget anyway** (e.g., "≤5 % per-Msg
    overhead"). Considered but rejected: would require a baseline
    capture run + tooling that does not exist today, which is
    work for SC-007 but not a planning prerequisite.
