# Feature Specification: Elmish MVU Core for State and I/O

**Feature Branch**: `003-elmish-mvu-core`
**Created**: 2026-04-28
**Status**: Draft
**Input**: User description: "i want to incorporate elmish as the central mechanism for state and input/output handling. https://github.com/elmish/elmish https://elmish.github.io/elmish/docs/basics.html the goal is to make the whole app testable and be able to work on the backlog of tasks in the older features that need a running game/ui....."

## Background *(non-template — context required)*

The shipped broker (features 001 and 002, both merged to `main`)
centralises live state in a single mutable record (`Hub`) guarded by a
monitor lock, and runs a 100 ms poll loop that reads keystrokes from
the live console, mutates `Hub` through a facade interface, and redraws
the dashboard by projecting `Hub` into a `DiagnosticReading`. The gRPC
services (`HighBarCoordinatorService`, `ScriptingClientService`) also
mutate the same `Hub` under the same lock.

Two consequences fall out of that design:

1. **The TUI is only exercisable on a real interactive terminal.**
   The render path uses Spectre.Console's `LiveDisplay`, which refuses
   redirected stdout. Tests cannot script keystrokes and read back
   what the dashboard *would* have shown.
2. **Behaviour that joins TUI input, gRPC events, and timer ticks is
   only exercisable end-to-end against a running peer.** Today that
   peer is the loopback `SyntheticCoordinator` test fixture. A real
   BAR + HighBarV3 game does not run under `dotnet test`.

The result is the carve-out backlog the user wants to clear: tasks
**T029, T037, T042, T046** from feature 001's `tasks.md` are all
marked `[S]` (synthetic evidence) and listed in feature 002's
`spec.md §Success Criteria SC-005` as items to close. They share a
single root cause — the system has no seam at which "what would the
broker do if it received this sequence of events?" can be answered
without running the real thing.

This feature introduces the **Elmish Model–View–Update (MVU) loop**
(via the `Elmish` F# library, https://elmish.github.io/elmish/) as the
**central state and I/O dispatch mechanism** of the broker. All state
becomes a single immutable `Model`. All inputs (TUI keystrokes, gRPC
inbound events, timer ticks, command results) become typed `Msg`
values. State transitions are a pure `update : Msg -> Model -> Model *
Cmd<Msg>`. Side effects (gRPC sends, audit-log writes, viewer-window
operations) become `Cmd` values that the runtime executes. The view is
a pure function `Model -> Spectre layout`.

This is **strictly an architectural pivot of the in-process state /
input / output spine.** The gRPC wire surface, the
`HighBarCoordinator` proto, the `ScriptingClient` proto, the audit-log
schema, the dashboard layout, and the operator-visible behaviour are
unchanged. Feature 001 and 002 functional requirements remain in
force — this feature changes how those requirements are implemented
internally so they become testable end-to-end without a TTY or a real
game. T035 (host-mode game-process management against a real BAR
engine) is **not** addressed here; it is an environment-provisioning
gap that an MVU pivot does not solve.

## Clarifications

### Session 2026-04-28

- Q: Where do the per-scripting-client outbound command queues live (Model contents vs. adapter-owned)? → A: Adapter-owned. The per-client `Channel<StateMsg>` stays inside `ScriptingAdapter`; `Model` holds only the observed roster (id, subscription, admin flag, most recent queue depth + high-water observation). `Cmd.ScriptingOutbound` enqueues; the adapter posts `Msg.QueueDepth` / `Msg.QueueOverflow` back so `update` can react to backpressure.
- Q: What is the dispatcher's Msg-queue overflow policy? → A: Unbounded mailbox with a configured high-water-mark audit. The runtime never caps, never rejects, never drops. When mailbox depth crosses the configured threshold the runtime emits a single `MailboxHighWater` audit event (rate-limited so a stuck `update` does not flood the log) so operators see backpressure as a signal, not as a missing message. This supersedes the "queue has a finite bound" phrasing in the edge-case discussion.
- Q: How are Cmd execution failures surfaced back to `update`? → A: Per-effect-family failure `Msg` arms (`AuditWriteFailed`, `CoordinatorSendFailed`, `ScriptingSendFailed`, `VizOpFailed`, `TimerFailed`), each carrying the relevant context (target id where applicable, the operation summary, and the exception). `update` matches them exhaustively so the compiler forces a deliberate decision per family — e.g., dashboard marks the sink as broken, or session is torn down, or the failure is just audited and ignored. No generic `CmdFailed` arm.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Carved-out evidence from feature 001 closes against scripted event streams (Priority: P1)

A maintainer re-runs the four synthetic-evidence tasks from feature 001
that were carved out because they depend on a real proxy AI or a real
interactive terminal — T029 (broker–proxy end-to-end transcript),
T037 (host-mode admin-command walkthrough), T042 (dashboard under load
screenshot), T046 (viz-window screenshot). Each test is rewritten as a
deterministic replay of `Msg` values fed into the MVU runtime, asserts
on the resulting `Model`, and renders the View output to a deterministic
string or off-screen surface. No real terminal is allocated; no real
game peer runs.

**Why this priority**: This is the headline reason the feature exists.
Feature 002's `SC-005` named these four tasks as carve-outs to close,
and Elmish-driven testability is the mechanism by which they close
without waiting for HighBarV3 binaries on CI hosts.

**Independent Test**: From a clean checkout, run
`dotnet test tests/Broker.Tui.Tests` and observe that all four
carve-out scenarios — proxy-attach transcript, lobby-then-admin
walkthrough, 4-client / 200-unit dashboard render, viz-window status
line — pass without spawning Spectre.Console `LiveDisplay`, without a
TTY, and without a `SyntheticCoordinator` loopback peer. Confirm the
`Synthetic-Evidence Inventory` for those four tasks transitions from
"carve-out, infeasible without proxy AI / TTY" to "closed; live
evidence captured by MVU replay".

**Acceptance Scenarios**:

1. **Given** the MVU runtime is wired in, **When** a test feeds the
   message sequence `[CoordinatorAttached; PushStateSnapshot tick=0;
   …; PushStateSnapshot tick=N]` to `update`, **Then** the resulting
   `Model.session` is `Attached` with the expected roster and snapshot,
   and the View output (rendered to string) shows the dashboard the
   operator would have seen — closing T029 without a proxy peer.
2. **Given** the MVU runtime is wired in, **When** a test feeds
   `[TuiKeypress 'L'; … lobby-configuration keys …; TuiKeypress Enter;
   TuiKeypress 'A'; CoordinatorAttached; AdminElevate "client-1";
   AdminCommand Pause]` to `update`, **Then** the resulting `Model`
   reflects the host-mode session, the elevated client, and the paused
   state, and the audit-trail Cmd list contains entries for each
   admin transition — closing T037 without a real TTY.
3. **Given** the MVU runtime is wired in, **When** a test seeds the
   `Model` with 4 subscribed scripting clients and replays 25
   snapshot messages each carrying 200 units, **Then** the View
   renders the dashboard at the expected 200-column width within the
   per-frame budget for at least 25 consecutive frames — closing T042
   without driving four real gRPC peers.
4. **Given** the MVU runtime is wired in, **When** a test feeds
   `[TuiKeypress 'V'; CoordinatorAttached; PushStateSnapshot]` and
   captures the viz-status footer, **Then** the View contains the
   expected viz-active status line; **And** when the test feeds the
   same sequence with the `--no-viz` flag in `Model.config`, the View
   renders the no-viz status line and asserts no viewer Cmd was
   emitted — closing T046 without an OpenGL/Vulkan surface.

---

### User Story 2 - A maintainer adds a new TUI feature with full test coverage before any interactive run (Priority: P1)

A maintainer takes a small TUI-touching task from the backlog (e.g.,
add a "kick scripting client" hotkey, surface coordinator schema
version in the footer, add a per-team kill/loss column) and writes
the entire feature — `Msg` case, `update` clause, `View` change, audit
event — driven by tests. They run `dotnet test`, see it green, and
only afterwards launch the broker in a terminal to spot-check. The
spot-check exposes nothing the tests didn't already exercise.

**Why this priority**: This is what "make the whole app testable"
means in practice — the loop where a human writes code, runs tests,
and trusts the result for TUI-shaped work is currently broken. P1
because every backlog item that touches TUI input or dashboard
rendering is gated on this loop existing.

**Independent Test**: Pick any one open backlog item that adds a
hotkey or a dashboard column. Implement it in three commits — `Msg`
+ `update`, `View` change, tests — and verify the tests fully cover
the behaviour (replay the keystroke, assert the new `Model` field,
render the View, assert the new column or footer line). The first
time the broker runs in a terminal is for confirmation, not
validation.

**Acceptance Scenarios**:

1. **Given** the MVU runtime is the central dispatcher, **When** a
   maintainer adds a new `Msg` case for a hotkey, **Then** the
   compiler refuses to build until `update` handles the case
   (exhaustive match), and a unit test that feeds the new `Msg`
   asserts on the resulting `Model` without instantiating
   Spectre.Console.
2. **Given** the MVU runtime is the central dispatcher, **When** a
   maintainer changes `View` to add a column, **Then** a render test
   that takes a fixture `Model` and converts the resulting Spectre
   layout to a deterministic string contains the new column —
   without launching `LiveDisplay`.
3. **Given** the MVU runtime is the central dispatcher, **When** a
   maintainer's change accidentally drops a Cmd (e.g., forgets to
   audit-log an admin action), **Then** the test asserting on the
   emitted Cmd list fails immediately — the regression is caught
   before any human-driven run.

---

### User Story 3 - Operator-visible runtime behaviour is unchanged (Priority: P1)

An operator runs the post-pivot broker against the same workflows
they ran before — connect a scripting client to a guest-mode session,
host-mode lobby launch, admin elevation, viz toggle, schema-mismatch
rejection — and observes the same dashboard, the same audit lines,
the same gRPC behaviour, the same hotkeys. Nothing visible to a human
operator changes.

**Why this priority**: An architectural pivot that breaks operator
behaviour is a worse outcome than the carve-out backlog it set out to
fix. P1 because every feature 001 and 002 functional requirement
remains in force; this story is the regression gate.

**Independent Test**: Replay the smoke test from `quickstart.md` of
feature 001 and the smoke test from feature 002 against the
post-pivot broker. Confirm: dashboard layout identical, hotkey set
identical, audit-log envelope identical, gRPC handshake / heartbeat
behaviour identical, viz toggle behaviour identical. Compare a recorded
TUI session before and after — diff is empty.

**Acceptance Scenarios**:

1. **Given** the MVU pivot is complete, **When** an operator launches
   the broker and connects a `SyntheticCoordinator` peer, **Then**
   the dashboard shows the same fields in the same layout, with the
   same hotkey footer, that feature 002 shipped with.
2. **Given** the MVU pivot is complete, **When** the broker rejects a
   schema-version mismatch (feature 002 FR-003), **Then** the audit
   log entry has the same shape and the same fields as before — same
   envelope, same severities, same correlation identifiers.
3. **Given** the MVU pivot is complete, **When** the existing
   integration test suite (`tests/Broker.Integration.Tests`,
   including `CoordinatorLoadTests`, `ScriptingClientFanoutTests`)
   runs unchanged, **Then** every test that passed pre-pivot passes
   post-pivot. Feature 001 + 002 functional requirements verifiable
   by the existing suite remain green.

---

### User Story 4 - Side effects (gRPC sends, audit writes, viewer ops) are inspectable in tests (Priority: P2)

A maintainer writing a test for a flow that should "send a Pause
command to the plugin and write a `command-dispatched` audit entry"
asserts on the **list of Cmds** the `update` returned, not on real
gRPC traffic or real audit-log files. Effects are described as data;
the runtime executes them in production but a test runtime captures
them.

**Why this priority**: P2 because the carve-outs in Story 1 mostly
require this capability, but a maintainer could in principle assert
on `Model` only and leave Cmd-list inspection to integration tests.
P2 because it is a strict generalisation of Story 2 and lifts a
noticeable amount of friction off any maintainer writing a flow test.

**Independent Test**: Take any flow that mutates state **and**
produces a side effect (admin elevation → audit entry; admin command
→ outbound gRPC + audit; schema mismatch → audit + connection
rejection). Write a test that asserts on both the resulting `Model`
and the resulting Cmd list, with no live audit log file written and
no live gRPC frame on the wire.

**Acceptance Scenarios**:

1. **Given** an `update` clause emits a `Cmd.audit` and a
   `Cmd.sendCoordinatorCommand`, **When** a test feeds the input
   `Msg`, **Then** the test reads back exactly two Cmd values whose
   payloads match what the audit log and the wire would have
   received — no real audit file is opened, no real channel is
   written.
2. **Given** a flow that emits Cmds depending on `Model` state,
   **When** the same `Msg` is fed against two different starting
   `Model` values, **Then** the Cmd lists differ exactly in the way
   the production code would have differed — verified by direct
   inspection of the returned Cmd values.

---

### User Story 5 - Snapshot-based regression tests for dashboard layouts (Priority: P3)

A maintainer can take a `Model` fixture, render the View to a
deterministic string, and check it into the repo as a regression
fixture (akin to a textual screenshot). Subsequent dashboard changes
that alter the rendered string require an explicit fixture update,
making accidental layout regressions visible in code review.

**Why this priority**: This is a quality-of-life addition layered on
top of the testable View function from Stories 1–4. P3 because the
core testability win lands without it; snapshot fixtures are an
optimisation for spotting drift.

**Independent Test**: For one chosen dashboard state (e.g., "guest
mode, 2 scripting clients, mid-game"), check in a `.txt` fixture of
the rendered View output. Modify a label in `View`. Run tests.
Confirm the fixture-comparison test fails with a readable diff and
that updating the fixture closes the failure.

**Acceptance Scenarios**:

1. **Given** a dashboard `Model` fixture and a checked-in rendered
   string, **When** the View is unchanged, **Then** the comparison
   test passes.
2. **Given** the same fixture, **When** a maintainer changes the
   View, **Then** the comparison test fails with a unified diff
   pointing at the changed lines.

---

### Edge Cases

- **A `Msg` arrives that no `update` clause handles** — the F#
  compiler refuses to build (exhaustive match on a discriminated
  union). This is the central correctness property the pivot buys;
  it must remain enforceable, not deferred to runtime.
- **A `Cmd` raises an exception while executing** — the runtime
  must catch it, route it as a follow-up `Msg` to `update` (so the
  failure is observable in `Model` and in the audit Cmd list), and
  not tear down the dispatcher.
- **The dispatcher is fed messages faster than `update` can
  process them** — incoming messages are queued in order in an
  unbounded mailbox. The runtime emits a single rate-limited
  `MailboxHighWater` audit event when the queue depth crosses a
  configured threshold so operators see the backpressure signal,
  but never drops, rejects, or evicts a Msg (a deadlock surface
  for handlers awaiting a TCS, and a silent-failure surface for
  audit). The bounded-backpressure rule from feature 001 FR-010
  applies only to per-scripting-client outbound queues, which are
  enforced by `ScriptingAdapter` against its own
  `Channel<StateMsg>` and reported back through
  `Msg.QueueOverflow`.
- **Two threads dispatch simultaneously** — the runtime guarantees
  `update` runs single-threaded against the `Model`, so concurrent
  dispatchers do not need to coordinate. This replaces the current
  `withLock`/`Hub.stateLock` discipline.
- **A test feeds a `Msg` sequence that ends in a state the
  production runtime would have continued running from** — the
  test runtime supports stopping at a quiescent state, capturing
  the `Model` and the unexecuted Cmds, without timing out.
- **The View function throws on a malformed `Model`** — render
  failure must be visible to tests and to operators (rendered
  panel showing the error) rather than tearing down the
  dispatcher.
- **A long-running Cmd (e.g., a heartbeat watchdog) is in flight
  when the dispatcher receives a session-end `Msg`** — the runtime
  must expose a cancellation handle so the Cmd's effect can be
  stopped without leaking the watchdog Task.
- **Spectre.Console rendering still requires a real TTY at
  runtime** — the View function returns a Spectre layout that the
  production host hands to `LiveDisplay`; tests render the same
  layout to a string via Spectre's off-screen renderer. The pivot
  does not replace Spectre.Console; it replaces what feeds it.

## Requirements *(mandatory)*

### Functional Requirements

#### State and message dispatch

- **FR-001**: System MUST represent all in-process broker state — the
  current session, the roster of attached peers (id, subscription
  status, admin-elevation flag, most recent observed outbound queue
  depth and high-water mark), the operator's configuration, the
  dashboard projection, and any in-flight workflow state — as a
  single immutable `Model` value owned by the MVU runtime. Per-
  scripting-client outbound queues themselves (the `Channel<StateMsg>`
  contents) live inside `ScriptingAdapter` and are NOT part of
  `Model`; their depth is observed by `Model` via adapter-emitted
  `Msg.QueueDepth` / `Msg.QueueOverflow` notifications. The mutable
  `Hub` record from feature 001 / 002 MUST be retired as a state
  container.
- **FR-002**: System MUST funnel every input that mutates broker
  state — TUI keystrokes, gRPC inbound RPC events, timer ticks,
  command results, internal lifecycle signals — through a single
  typed `Msg` discriminated union dispatched to a single
  `update : Msg -> Model -> Model * Cmd<Msg> list` function.
- **FR-003**: System MUST run `update` single-threaded against the
  `Model` — concurrent dispatchers MUST be serialised by the runtime,
  not by call-site locking. The `withLock` / `Hub.stateLock`
  discipline of feature 001 MUST be removed from gRPC service code,
  TUI dispatch code, and any other call site that mutates state.
- **FR-004**: System MUST require `update`'s match on `Msg` to be
  exhaustive at compile time. Adding a new `Msg` case without
  handling it MUST fail the build.

#### Effects

- **FR-005**: System MUST represent every side effect the broker
  performs — outbound gRPC sends to the coordinator, outbound gRPC
  sends to scripting clients (enqueued into per-client adapter-owned
  channels), audit-log writes, viewer-window operations, timer
  schedules, process exits — as `Cmd<Msg>` values returned from
  `update`. The bounded backpressure rule from feature 001 (FR-010,
  `QUEUE_FULL` reject on per-client overflow) is enforced inside
  `ScriptingAdapter` against its `Channel<StateMsg>`, and the
  resulting reject is reported back to `update` as `Msg.QueueOverflow`
  so the audit Cmd is emitted from `update`. Direct side-effecting
  calls inside `update` MUST be forbidden.
- **FR-006**: System MUST execute Cmds via a runtime that, in
  production, performs the real I/O, and in tests, captures the Cmd
  list for inspection without performing the I/O. Both runtimes
  MUST share the same Cmd value definitions.
- **FR-007**: System MUST route the result of any executed Cmd —
  including failures, replies, and timer firings — back to `update`
  as a follow-up `Msg`. Cmd execution MUST NOT mutate `Model`
  directly.
- **FR-008**: System MUST surface a Cmd-execution failure to
  `update` as a typed per-effect-family failure `Msg` —
  `AuditWriteFailed`, `CoordinatorSendFailed`, `ScriptingSendFailed`
  (carrying the affected `ScriptingClientId`), `VizOpFailed`,
  `TimerFailed` — each carrying the operation summary and the
  underlying exception. `update` MUST match them exhaustively at
  compile time. No generic `CmdFailed` arm is permitted; failures
  MUST NOT be swallowed by the runtime. The dispatcher MUST remain
  alive across Cmd failures.

#### View and rendering

- **FR-009**: System MUST render the dashboard via a pure
  `view : Model -> SpectreLayout` function that has no I/O, allocates
  no resources, and produces the same output for the same input
  every time.
- **FR-010**: System MUST host the production render path on
  Spectre.Console's `LiveDisplay`, fed by `view` outputs at the
  existing tick cadence. The Spectre.Console rendering target,
  hotkey footer, panel layout, and dashboard fields MUST remain
  byte-for-byte the same as the post-002 broker for any given
  `Model`.
- **FR-011**: System MUST also support rendering `view` output to a
  deterministic string via Spectre.Console's off-screen renderer,
  for use by tests, regression fixtures, and any future non-TTY
  diagnostic export.

#### Inputs

- **FR-012**: System MUST translate TUI keystrokes — captured by the
  existing keypress polling — into `Msg` values dispatched to
  `update`. The hotkey set, key bindings, and per-mode availability
  defined in feature 001 MUST remain unchanged.
- **FR-013**: System MUST translate every gRPC inbound RPC handler
  in `HighBarCoordinatorService` and `ScriptingClientService` into
  a `Msg` dispatch that returns the response from the resulting
  `Model` / `Cmd` pair. RPC handlers MUST NOT mutate state directly.
- **FR-014**: System MUST translate periodic timer ticks (heartbeat
  watchdog, dashboard refresh, snapshot staleness probe) into `Msg`
  values dispatched to `update`. Timer schedules MUST be Cmds, not
  background Tasks owned by feature code.

#### Test runtime

- **FR-015**: System MUST provide a test entry point that lets a
  test build an initial `Model`, feed an ordered sequence of `Msg`
  values, drive `update` synchronously, and read back the final
  `Model` plus the full ordered list of emitted Cmds, without
  starting a Spectre.Console `LiveDisplay`, without opening a real
  audit-log file, and without binding a real gRPC listener.
- **FR-016**: System MUST allow tests to render the View of any
  `Model` to a deterministic string for equality comparison,
  including the regression-fixture pattern described in Story 5.
- **FR-017**: System MUST allow tests to assert on the structural
  contents of emitted Cmds — type, target, payload — by direct
  inspection of the returned Cmd list, without an indirection
  through a real I/O subsystem.

#### Migration boundaries (preserved invariants)

- **FR-018**: System MUST preserve every functional requirement
  shipped in features 001 and 002 — operating modes, gRPC service
  shape, schema-version handling, command authority rules,
  bounded-backpressure rules, audit-log envelope, dashboard fields,
  viz behaviour. The pivot is implementation-internal.
- **FR-019**: System MUST keep the public surface of `Broker.Contracts`
  (the proto-derived F# types) unchanged. SurfaceArea baselines for
  contract types MUST NOT shift as a side effect of this feature.
- **FR-020**: System MUST keep the public surface of the gRPC services
  themselves — `HighBarCoordinator`, `ScriptingClient` — unchanged.
  Wire behaviour observed by external peers MUST be identical.

#### Carve-out closure

- **FR-021**: The `Synthetic-Evidence Inventory` entries for tasks
  T029, T037, T042, T046 of feature 001 MUST transition from "open
  carve-out" to "closed; live evidence captured by MVU replay" with
  corresponding artefacts under `readiness/`. Task T035 (host-mode
  game-process management) is explicitly **not** closed by this
  feature and remains a tracked carve-out.

### Key Entities

- **Model**: The single immutable value that holds all in-process
  broker state — replaces `Hub`. Contains the current session, the
  attached peers' identities, elevation flags, subscription
  state, and most recently observed outbound-queue depth + high-
  water mark per client (queue *contents* live in
  `ScriptingAdapter`, not in `Model`); the operator's configuration
  (host/guest, viz on/off, hotkey state); the dashboard's
  projection-ready fields; and any pending workflow state (e.g.,
  partially-entered lobby configuration, in-flight admin elevation
  prompt).
- **Msg**: A typed discriminated union of every input that can mutate
  the `Model`. Has cases for TUI keystrokes, every gRPC inbound RPC
  event, every timer tick, every Cmd-completion callback, and every
  internal lifecycle signal. Exhaustively matched in `update`.
- **Update function**: A pure function `Msg -> Model -> Model *
  Cmd<Msg> list` that returns the next `Model` and any side effects
  to schedule. Single seam at which broker behaviour is defined.
- **Cmd**: A description of a side effect — an outbound gRPC send,
  an audit-log entry, a viewer-window operation, a timer schedule,
  a process exit. Inert data; the runtime executes it.
- **MVU runtime (production)**: The host that owns the dispatcher
  thread, runs `update` against incoming `Msg`s, executes Cmds
  against real I/O subsystems, drives the Spectre.Console live
  render at the tick cadence, and routes Cmd results back as `Msg`s.
- **MVU runtime (test)**: A drop-in alternative that runs `update`
  synchronously, captures Cmds in a list rather than executing
  them, and exposes the resulting `Model` and Cmd list to the test
  for assertion.
- **View function**: A pure function `Model -> SpectreLayout` that
  produces the dashboard's Spectre layout. Used by both runtimes.
- **Side-effect adapter**: The production-runtime component that
  takes a Cmd value and performs the corresponding I/O — one
  adapter per effect family (audit, gRPC outbound, viewer, timer).
  Each adapter has a test-runtime stub that records its inputs.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: A maintainer who has never edited the broker before
  can write a passing test that feeds a 5-message scripted sequence
  through `update` and asserts on the resulting `Model` and Cmd
  list, in under 30 minutes from a clean checkout, with no real
  TTY, no real audit-log file, and no gRPC peer.
- **SC-002**: All four carve-out tasks T029, T037, T042, T046 from
  feature 001 are closed by tests that drive scripted message
  sequences through the MVU runtime — no synthetic loopback proxy,
  no live `LiveDisplay`, no real game peer. Their `readiness/`
  artefacts are regenerated against MVU-replayed evidence.
- **SC-003**: 100 % of TUI keypress handlers, gRPC inbound RPC
  handlers, and timer-tick handlers route through the MVU runtime.
  No production code path mutates broker state outside `update`.
  No production code path emits a side effect outside a Cmd.
- **SC-004**: The post-pivot broker passes every test from
  features 001 and 002 — `Broker.Core.Tests`, `Broker.Tui.Tests`,
  `Broker.Protocol.Tests`, `Broker.Integration.Tests`,
  `SurfaceArea` — that passed pre-pivot, with zero behavioural
  diff observable by an external peer or by an operator at the
  TUI.
- **SC-005**: Adding a new TUI hotkey from `Msg` case to passing
  test takes fewer than 100 lines of F# (one case in the union,
  one clause in `update`, one View change, one unit test, one
  optional regression fixture) — measured by checking in such a
  hotkey as a worked example during the pivot.
- **SC-006**: A render of the post-pivot dashboard against a fixed
  `Model` fixture matches the pre-pivot dashboard render of the
  same conceptual state byte-for-byte (after layout normalisation).
- **SC-007**: The MVU dispatcher processes inputs at the cadence
  the post-002 broker did — no observable additional latency on
  TUI keystroke responsiveness or gRPC RPC turnaround beyond the
  same per-tick budget.
- **SC-008**: The mutable `Hub` record and its `stateLock` monitor
  are removed from the codebase. Greppable confirmation: zero
  hits for `Hub.session <-`, `Hub.mode <-`, `withLock`, or
  equivalent direct mutations of broker state outside the MVU
  runtime's internals.

## Assumptions

- The `Elmish` F# library (https://github.com/elmish/elmish) is
  the chosen MVU implementation. It is small, well-understood,
  and has a stable API; this feature treats it as a load-bearing
  external dependency on the same footing as Spectre.Console or
  Serilog.
- Spectre.Console remains the rendering target for the production
  TUI. The MVU pivot replaces what feeds Spectre, not Spectre
  itself. Dashboard panels, hotkey footer, and layout primitives
  are unchanged.
- Serilog remains the audit-log sink. The MVU pivot describes
  audit entries as Cmd values; the production Cmd executor writes
  them through the existing Serilog pipeline using the existing
  envelope.
- The gRPC server (Kestrel + `HighBarCoordinator` /
  `ScriptingClient`) remains hosted by `Microsoft.AspNetCore` as
  it is today. RPC handlers translate inbound calls into `Msg`s
  and translate the responses they get back from the MVU runtime
  into outbound RPC payloads. The gRPC server itself is not
  inside the MVU runtime.
- `SkiaViewer` remains the viz back-end. The MVU pivot represents
  viewer-window operations (open / close / push frame) as Cmds;
  the production executor calls into SkiaViewer; the test
  executor records the calls.
- The `SyntheticCoordinator` test fixture is retained as an
  integration-level peer for tests that exercise the gRPC wire
  end-to-end. The new MVU-replay tests sit one layer below the
  wire — they feed `Msg`s directly, bypassing gRPC. Both styles
  coexist.
- The pivot ships as a single feature, not incrementally — all
  state mutations move into `update` together, all side effects
  move into Cmds together, the mutable `Hub` is removed in the
  same change set. A halfway pivot (Hub + MVU side by side) is
  worse than either endpoint and is explicitly avoided.
- Feature 001 task T035 (host-mode game-process management
  against a real BAR engine) is **not** closed by this feature.
  It is an environment-provisioning gap (no game binary on CI),
  which an MVU pivot does not address. T035 remains a tracked
  carve-out.
- Performance is expected to be unchanged or improved. The
  current `withLock` discipline already serialises mutations;
  the MVU dispatcher does the same with one fewer lock acquisition
  per RPC. No performance regression budget is reserved.
- Constitution Principle II ("visibility lives in `.fsi`") applies
  to the new MVU code. `Model`, `Msg`, `update`, `view`, and the
  Cmd type each get an `.fsi` declaring exactly the surface that
  callers and tests need.

## Out of Scope

- Replacing Spectre.Console as the rendering library.
- Replacing Serilog as the audit-log sink.
- Closing T035 (host-mode game-process management against a real
  BAR engine). T035 is environment-provisioning, not state-shape.
- Adding new operator-visible features. The pivot is purely
  internal; new features are written *on top of* the MVU runtime
  in subsequent changes.
- Changing the gRPC wire surface, the proto contracts, the audit
  envelope, the dashboard layout, the hotkey set, or the viz
  behaviour.
- Migrating to `Fable` for any web/UI target. This feature uses
  the F# `Elmish` core (the architectural pattern) on .NET; no
  Fable.React, no Fable.Browser, no JavaScript output.
- Persisting `Model` across broker restarts. The `Model` is in-
  process state, same lifetime as today's `Hub`.
- Introducing a separate event-sourced log of `Msg` values for
  replay against production traffic. Test-time replay is enough
  for the carve-outs this feature targets; production-traffic
  replay is a future feature if ever needed.
