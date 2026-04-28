# US4 — Optional 2D visualization

**Recorded**: 2026-04-28
**Drivers**:
- `tests/Broker.Integration.Tests/VizSceneBuilderTests.fs` (10 tests, T043)
- `tests/Broker.Integration.Tests/VizHostProbeTests.fs` (6 tests, T043)
- `tests/Broker.Integration.Tests/VizSc008Tests.fs` (3 tests, T046)
- `tests/Broker.Tui.Tests/DashboardViewTests.fs` (`renderWithViz` cases, T045)
- Off-TTY render via `dotnet fsi /tmp/render-us4.fsx`

The broker-side wire-up is real. The proxy peer (the source of the
snapshot stream the viewer would draw) is the same loopback substitute
disclosed in T029 / T037 / T042; the actual SkiaViewer window cannot be
captured under `dotnet test` because Spectre's `LiveDisplay` plus
SkiaViewer's GL/Vulkan backend require an interactive TTY and a real
display surface.

## What's real (broker-side wire path)

- `Broker.Viz.SceneBuilder.build` projects every snapshot to a SkiaViewer
  scene with map outline, units (small circles), buildings (small squares)
  and team-derived ownership colours (FR-022 / FR-023).
- `Broker.Viz.VizHost.probe` inspects `DISPLAY` / `WAYLAND_DISPLAY` on
  Linux and returns `Ok ()` when graphics are available, `Error reason`
  on a headless host (FR-025).
- `Broker.Viz.VizHost.open_` subscribes to an `IObservable<GameStateSnapshot>`,
  builds scenes, and pushes them to `SkiaViewer.Viewer.run` on a background
  thread. Returns an `IAsyncDisposable` handle whose disposal closes the
  window cleanly without affecting the broker.
- `Broker.Tui.HotkeyMap.map` maps `V` to `ToggleViz` in every UI mode.
- `Broker.Tui.TickLoop.run` accepts a `VizController option`; `V`
  invokes `VizController.Toggle` when present, no-ops on `--no-viz`.
- `Broker.Tui.DashboardView.renderWithViz` renders the dashboard with an
  optional viz-status line in the footer (SC-008).
- `Broker.App.VizControllerImpl.LiveVizController` opens / closes the
  viewer on `Toggle`, records the probe failure on a headless host, and
  surfaces "2D visualization unavailable: ..." via `Status` so the
  `renderWithViz` footer can show it.
- `Broker.Protocol.BrokerState.snapshots` exposes the in-process snapshot
  stream the viewer subscribes to. Every `applySnapshot` broadcasts to
  this observable in addition to fanning out wire `StateMsg`s, so the
  viz tracks live game state at the same cadence as scripting clients.

## CLI wiring

The broker accepts `--no-viz` to skip the viz subsystem entirely
(`Cli.Args.noViz`). When present, `Program.run` constructs `None` for
`TickLoop.run`'s `viz` parameter, so `V` is a silent no-op and no probe
is performed.

```
$ broker --version
broker v1.0

$ broker --help
broker [options]

Options:
  --listen HOST:PORT   gRPC server listen address (default 127.0.0.1:5021)
  --no-viz             disable the optional 2D visualization subsystem
  --version            print the broker version and exit
  -h, --help           print this help text

$ broker --no-viz --version
broker v1.0
```

## SC-008 footer rendering on a headless host

After pressing `V` on a host with neither `DISPLAY` nor
`WAYLAND_DISPLAY` set, `LiveVizController.Status()` returns
`Some "2D visualization unavailable: no graphical display detected (DISPLAY/WAYLAND_DISPLAY unset)"`.
`DashboardView.renderWithViz` surfaces that line in the footer alongside
the existing hotkey legend. The 200-col off-TTY transcript below is the
exact rendering a TTY would show:

```
┏━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━┓
┃ FSBar Broker  •  idle  •  listening 127.0.0.1:5021  •  press Q to quit ┃
┗━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━┛
╭─Broker─────────────────────────────────╮                        ╭─Session──────────────────╮                                       ╭─Clients (0)───────────────────────────────╮
│ ╭─────────┬──────────────────────────╮ │                        │ ╭─────────┬────────────╮ │                                       │ ╭────────────┬───┬──────┬───────┬───────╮ │
│ │ version │ v1.0                     │ │                        │ │ state   │ no session │ │                                       │ │ name       │ v │ slot │ admin │ queue │ │
│ │ listen  │ 127.0.0.1:5021           │ │                        │ │ elapsed │ —          │ │                                       │ ├────────────┼───┼──────┼───────┼───────┤ │
│ │ server  │ listening 127.0.0.1:5021 │ │                        │ │ pause   │ —          │ │                                       │ │ no clients │   │      │       │       │ │
│ │ uptime  │ 00:00:00                 │ │                        │ │ speed   │ —          │ │                                       │ ╰────────────┴───┴──────┴───────┴───────╯ │
│ │ mode    │ idle                     │ │                        │ ╰─────────┴────────────╯ │                                       ╰───────────────────────────────────────────╯
│ ╰─────────┴──────────────────────────╯ │                        ╰──────────────────────────╯
╰────────────────────────────────────────╯
…
╭───────────────────────────────────────────────────────────────────────────────────────────────────────────────────────────────────────────────────────────────────────────────╮
│ Q quit · V viz · L lobby (idle) · Space pause (host) · +/- speed · X end session  2D visualization unavailable: no graphical display detected (DISPLAY/WAYLAND_DISPLAY unset) │
╰───────────────────────────────────────────────────────────────────────────────────────────────────────────────────────────────────────────────────────────────────────────────╯
```

## SC-008 broker stays up under `--no-viz`

`VizSc008Tests.broker_boots_with_--no-viz_and_answers_gRPC_Hello`
boots a real Kestrel-hosted gRPC server with no viz controller, then
performs a `Hello` handshake from a real `Grpc.Net.Client`. The reply
echoes `BrokerVersion.Major = 1`. The same test invokes
`TickLoop.dispatch` with `HotkeyMap.ToggleViz` in `--no-viz` posture
and confirms the dispatch is a tolerated no-op (the viz side effect
lives in `TickLoop.run`, only fired when a `VizController` is wired).

`VizSc008Tests.LiveVizController_Toggle_on_headless_records_unavailable_status`
runs the production `LiveVizController` against a synthesised headless
environment (DISPLAY/WAYLAND unset) and a no-op snapshot observable.
Toggle once → `Status()` reports
`Some "2D visualization unavailable: no graphical display detected (DISPLAY/WAYLAND_DISPLAY unset)"`.
Toggle again → status persists; second probe still fails. The broker's
gRPC server, scripting-client roster, snapshot fan-out, and audit
emission are all unaffected.

## What's synthetic (and why)

The remaining gap is the live screenshot of the `SkiaViewer` window over
an active session. Two separate constraints rule it out under
`dotnet test`:

1. **Live TTY** — the dashboard's `Spectre.Console.AnsiConsole.Live`
   context refuses to drive a redirected stdout (same gap as T037 /
   T042). The transcript above captures the off-TTY render path the
   live TUI also produces; the only thing missing is the redraw cadence.
2. **Live display surface** — `SkiaViewer.Viewer.run` opens a Silk.NET
   window backed by GL or Vulkan. Even with `DISPLAY=:1` set inside the
   dev container, opening it from a non-interactive `dotnet test`
   process is not a thing CI can capture.

A manual operator-driven walkthrough of quickstart §4 against a
graphical workstation with a real HighBarV3 game emitting snapshots is
the canonical real-evidence path. That walkthrough is gated on the same
HighBarV3 workstream as T029 / T037 / T042.

## Status

`[S]` — broker-side wire path is real production code (probe, snapshot
broadcaster, controller, footer, dispatch all green across 19 dedicated
tests + the existing TUI / integration suites). The remaining gap is the
operator-driven live screenshot, which requires a real interactive TTY
plus a real display surface plus an upstream HighBarV3 build emitting
snapshots — same gating as T029 / T037 / T042. See the
Synthetic-Evidence Inventory in `tasks.md`.
