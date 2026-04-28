# Quickstart: Elmish MVU Core for State and I/O

**Feature**: 003-elmish-mvu-core
**Date**: 2026-04-28

This quickstart shows two flows:

1. **Maintainer flow** — write a new TUI feature against the MVU
   runtime, drive it from tests, and observe it working in a
   terminal afterwards. Validates SC-001, SC-005.
2. **Operator flow** — launch the post-pivot broker against a real
   BAR + HighBarV3 build and confirm operator-visible behaviour is
   unchanged. Validates SC-004, SC-006, SC-007 and regenerates the
   four carve-out readiness artefacts (T029, T037, T042, T046).

You will need:

- The repo built locally — `dotnet build` at repo root.
- Expecto runner available — `dotnet test tests/Broker.Mvu.Tests`.
- For Operator flow §3 onward: a BAR install + HighBarV3 plugin,
  same prerequisites as the 002 quickstart §1.

---

## §1 Maintainer: drive `update` from a test in under 30 minutes

Validates **SC-001** (a maintainer who has never edited the broker
before can write a 5-message scripted-sequence test in under 30
minutes).

```sh
# From a clean checkout:
dotnet build                                       # builds Broker.Mvu and dependencies
```

Open `tests/Broker.Mvu.Tests/UpdateTests.fs` and add:

```fsharp
let ``operator hits L then E to commit a host-mode lobby`` () =
    let model0 =
        Broker.Mvu.Testing.Fixtures.syntheticIdleModel (DateTimeOffset.UtcNow)
    let handle = TestRuntime.create model0

    TestRuntime.dispatchAll handle [
        Msg.TuiInput (Keypress (ConsoleKeyInfo('L', ConsoleKey.L, false, false, false)))
        Msg.TuiInput (Keypress (ConsoleKeyInfo('1', ConsoleKey.D1, false, false, false)))   // map 1
        Msg.TuiInput (Keypress (ConsoleKeyInfo('2', ConsoleKey.D2, false, false, false)))   // 2 players
        Msg.TuiInput (Keypress (ConsoleKeyInfo(' ', ConsoleKey.Spacebar, false, false, false)))
        Msg.TuiInput (Keypress (ConsoleKeyInfo('\r', ConsoleKey.Enter, false, false, false)))
    ]

    let model = TestRuntime.currentModel handle
    Expect.equal model.mode (Mode.Host (someExpectedConfig)) "lobby committed to host mode"

    let cmds = TestRuntime.capturedCmds handle
    Expect.contains cmds (Cmd.AuditCmd (Audit.HostSessionOpened …)) "audit emitted"
```

Run:

```sh
dotnet test tests/Broker.Mvu.Tests --filter "operator hits L"
```

**Stopwatch the writing time** from clean checkout to passing test —
SC-001 expects ≤30 min. Most of the time is reading the `Msg` and
`Cmd` discriminated unions in `contracts/public-fsi.md`.

---

## §2 Maintainer: prove the four carve-outs close in CI

Validates **SC-002** (T029, T037, T042, T046 close without a real
TTY, real game peer, or real OpenGL surface). Reproduces the
acceptance scenarios from spec User Story 1.

```sh
dotnet test tests/Broker.Mvu.Tests --filter "Carveout"
```

Expected output: four passing tests, each emitting a transcript
under `specs/001-tui-grpc-broker/readiness/T0XX-mvu-replay.txt`
that captures:

- the `Msg` sequence fed,
- the resulting `Model` (rendered as F# `sprintf "%A"`),
- the captured Cmd list (rendered the same way),
- the rendered View string (from `View.renderToString 200 50 model`)
  for fixture-comparison tests.

The Synthetic-Evidence Inventory entries for those four tasks are
updated from "open carve-out" → "closed; live evidence captured by
MVU replay" (FR-021).

---

## §3 Maintainer: add a new hotkey end-to-end (≤100 LOC)

Validates **SC-005** (adding a new hotkey from `Msg` case to passing
test takes <100 lines of F#). Worked example: add a `K` hotkey that
kicks the most recently subscribed scripting client.

**1. Msg arm** (`Broker.Mvu.Msg.fsi`, +1 line):

```fsharp
| KickMostRecentScriptingClient
```

**2. `update` clause** (`Broker.Mvu.Update.fs`, ~15 lines):

```fsharp
| Msg.TuiInput (Keypress { Key = ConsoleKey.K }) ->
    match model.roster |> ScriptingRoster.mostRecentSubscribed with
    | Some clientId ->
        { model with roster = ScriptingRoster.markKicked clientId model.roster },
        Cmd.batch [
            Cmd.ScriptingReject (clientId, RejectReason.Kicked)
            Cmd.AuditCmd (Audit.ScriptingClientKicked (clientId, "operator hotkey"))
        ]
    | None -> model, Cmd.none
```

**3. Hotkey footer** (`Broker.Tui.HotkeyMap.fs`, +1 line in the table).

**4. View change** (`Broker.Mvu.View.fs` or `Broker.Tui.DashboardView.fs`,
~5 lines to surface "K = kick" in the footer).

**5. Test** (`tests/Broker.Mvu.Tests/UpdateTests.fs`, ~25 lines):

```fsharp
let ``K hotkey kicks the most recently subscribed scripting client`` () =
    let model0 =
        Broker.Mvu.Testing.Fixtures.syntheticGuestModel 2 100L
    let handle = TestRuntime.create model0

    TestRuntime.dispatch handle (
        Msg.TuiInput (Keypress (ConsoleKeyInfo('K', ConsoleKey.K, false, false, false)))
    )

    let model = TestRuntime.currentModel handle
    let cmds = TestRuntime.capturedCmds handle
    Expect.isTrue
        (cmds |> List.exists (function Cmd.ScriptingReject (_, RejectReason.Kicked) -> true | _ -> false))
        "ScriptingReject Kicked emitted"
    Expect.isTrue
        (cmds |> List.exists (function Cmd.AuditCmd (Audit.ScriptingClientKicked _) -> true | _ -> false))
        "audit emitted"
```

Total LOC: ~50 + the test ~25 = ~75. Comfortably under SC-005's
100-line bar.

---

## §4 Operator: cold-start vs feature 002 — dashboard byte-for-byte equal

Validates **SC-006** (post-pivot dashboard renders identically to
pre-pivot for the same conceptual state) and **FR-018, FR-020**
(operator-visible behaviour unchanged).

```sh
# Build the pre-pivot broker once for comparison (checkout the
# parent commit of the 003-merge):
git worktree add ../FSBarV2-pre003 main
( cd ../FSBarV2-pre003 && dotnet build )

# Build the post-pivot broker:
dotnet build
```

In two terminals:

```sh
# Terminal A — pre-pivot:
../FSBarV2-pre003/src/Broker.App/bin/Debug/net10.0/Broker.App \
    --listen unix:/tmp/fsbar-pre.sock \
    --record-dashboard /tmp/dashboard-pre.txt

# Terminal B — post-pivot:
src/Broker.App/bin/Debug/net10.0/Broker.App \
    --listen unix:/tmp/fsbar-post.sock \
    --record-dashboard /tmp/dashboard-post.txt
```

(`--record-dashboard` is the existing diagnostic hook from feature
001's quickstart §3; it captures the rendered Spectre output to a
file at each tick.)

Drive both with the same `SyntheticCoordinator`-backed integration
peer:

```sh
dotnet test tests/Broker.Integration.Tests \
    --filter "DashboardLoadTests" \
    -- --target unix:/tmp/fsbar-pre.sock
dotnet test tests/Broker.Integration.Tests \
    --filter "DashboardLoadTests" \
    -- --target unix:/tmp/fsbar-post.sock
```

Compare:

```sh
diff /tmp/dashboard-pre.txt /tmp/dashboard-post.txt
# expected: empty
```

Empty diff confirms SC-006 byte-for-byte. A one-time layout
normalisation pass may be required if Spectre version changed in
the same window — the spec carves out "after layout normalisation".

---

## §5 Operator: real-game walkthrough regenerates carve-out readiness

Validates **FR-021** by capturing live evidence for the four
carve-out tasks from the post-pivot broker against a real
BAR + HighBarV3 build. This step is identical to feature 002
quickstart §1–4 (those flows are unchanged by the MVU pivot — the
wire surface is identical), so refer to
[`specs/002-highbar-coordinator-pivot/quickstart.md`](../002-highbar-coordinator-pivot/quickstart.md)
for the engine-launch incantations.

The pivot-specific assertion to add at each step: **the captured
audit-log envelope and the captured dashboard pane should match
the pre-pivot capture stored under
`specs/001-tui-grpc-broker/readiness/`** (the 002 capture). Diff
the captures; the diff should be empty in the operator-visible
fields and may differ only in timestamps and PIDs.

The freshly-captured artefacts are written to:

```
specs/001-tui-grpc-broker/readiness/
├── T029-coordinator-attached.txt        # ⚠ regenerated post-pivot
├── T037-host-admin-walkthrough.txt      # ⚠ regenerated post-pivot
├── T042-dashboard-under-load.txt        # ⚠ regenerated post-pivot
└── T046-viz-window-status.txt           # ⚠ regenerated post-pivot
```

For each, the corresponding `Synthetic-Evidence Inventory` row
flips from "open carve-out, infeasible without proxy AI / TTY" to
"closed; live evidence captured by MVU replay (CI) + real-game
walkthrough (operator)".

---

## §6 Operator: confirm `Hub` and `withLock` are gone (SC-008)

Validates **SC-008** — greppable confirmation that the mutable
`Hub` record and its `stateLock` monitor are removed.

```sh
# Should produce zero hits in src/ and tests/ outside of historical
# spec documents under specs/001-tui-grpc-broker/ and specs/002-…/:
rg --type fsharp 'Hub\.session\s*<-' src tests
rg --type fsharp 'Hub\.mode\s*<-' src tests
rg --type fsharp 'withLock' src tests
rg --type fsharp '\bstateLock\b' src tests
```

Each command is expected to return zero matches. The same set of
greps runs as a CI guard test in
`tests/Broker.Mvu.Tests/HubRetirementGuardTests.fs` so the
property is enforced on every PR.

---

## §7 Operator: confirm performance unchanged (SC-007)

Validates **SC-007** (no observable additional latency on TUI
keystroke responsiveness or gRPC RPC turnaround beyond the same
per-tick budget as the post-002 broker).

```sh
# Game-tick → scripting-client p95 latency (same as 002 §2):
dotnet test tests/Broker.Integration.Tests \
    --filter "Sc003LatencyTests" \
    -- --window 500

# Dashboard-load throughput (same as 002 §3):
dotnet test tests/Broker.Integration.Tests \
    --filter "DashboardLoadTests" \
    -- --clients 4 --units 200 --frames 25

# TUI keystroke responsiveness — manual or scripted; the test
# `Broker.Mvu.Tests/RuntimeTests.fs::keystroke-to-Cmd-roundtrip`
# captures a histogram and asserts on p95 ≤2 ms.
dotnet test tests/Broker.Mvu.Tests --filter "keystroke-to-Cmd-roundtrip"
```

All three should pass at the same thresholds 002 shipped with. Any
regression is a blocker per spec FR-018 / SC-007.

---

## §8 Maintainer: snapshot-regression fixture pattern (User Story 5)

Validates **User Story 5** acceptance scenarios.

```sh
# 1. Pick a Model fixture and check in its rendered string:
dotnet test tests/Broker.Mvu.Tests --filter "ViewSnapshotTests" -- --update-fixtures
git status
# expected: tests/Broker.Mvu.Tests/Fixtures/dashboard-guest-2clients.txt is new/modified

# 2. Re-run without --update-fixtures: comparison test passes:
dotnet test tests/Broker.Mvu.Tests --filter "ViewSnapshotTests"

# 3. Edit a label in `Broker.Mvu.View.fs` (e.g., "Mode:" → "MODE:")
# and re-run: comparison test fails with a unified diff.
dotnet test tests/Broker.Mvu.Tests --filter "ViewSnapshotTests"
# expected: failure with a clear "expected vs actual" diff
```

---

## Troubleshooting

- **`dotnet test` complains about missing `Elmish` package**:
  the package may not yet be in `Directory.Packages.props`. Confirm:
  `grep Elmish Directory.Packages.props`. If missing, add it pinned
  to the latest stable 4.x.
- **Off-screen renderer throws on `view`**:
  set `AnsiConsoleSettings.ColorSystem = NoColors` and
  `Interactive = No` per research §5. The
  `View.renderToString` helper does this for you; if a test bypasses
  it, replicate the settings.
- **`TestRuntime.dispatch` deadlocks**:
  it should not — `TestRuntime` is fully synchronous. If a test
  appears to hang, you are probably calling `Runtime.dispatch` (the
  production runtime) instead of `TestRuntime.dispatch`. Check the
  `open` declarations.
- **`View` throws on a fixture Model**:
  per spec Edge Case "View function throws on a malformed Model",
  this is a defect in the View, not the Model. Fix the View to
  render an error panel or reject the malformed field at the type
  level.
