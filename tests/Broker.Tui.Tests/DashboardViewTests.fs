module Broker.Tui.Tests.DashboardViewTests

open System
open System.IO
open Expecto
open Spectre.Console
open Broker.Core
open Broker.Tui

let private brokerInfo : Session.BrokerInfo =
    { version = System.Version(1, 0)
      listenAddress = "127.0.0.1:5021"
      startedAt = DateTimeOffset(2026, 4, 28, 12, 0, 0, TimeSpan.Zero) }

let private mkClient name slot admin queue : ScriptingRoster.ScriptingClient =
    { id = ScriptingClientId name
      connectedAt = DateTimeOffset(2026, 4, 28, 12, 0, 0, TimeSpan.Zero)
      protocolVersion = System.Version(1, 0)
      boundSlot = slot
      isAdmin = admin
      commandQueueDepth = queue }

let private mkPlayer (pid: int) (team: int) (name: string) (metal: float) (energy: float)
                     (units: int) (buildings: int) (kills: int) (losses: int) : Snapshot.PlayerTelemetry =
    { playerId = pid
      teamId = team
      name = name
      resources = { metal = metal; energy = energy }
      unitCount = units
      buildingCount = buildings
      unitClassBreakdown = Map.empty
      economy = { income = { metal = 0.0; energy = 0.0 }; expenditure = { metal = 0.0; energy = 0.0 } }
      kills = kills
      losses = losses }

let private idleReading : Dashboard.DiagnosticReading =
    { broker = brokerInfo
      serverState = Dashboard.Listening "127.0.0.1:5021"
      connectedClients = []
      mode = Mode.Mode.Idle
      session = None
      elapsed = None
      pause = None
      speed = None
      telemetry = None
      telemetryStale = false }

// 200 chars is wide enough that names like "alice-bot" don't wrap inside
// the 5-column Clients table when the layout's columns get evenly split.
type private WideOutput(writer: TextWriter) =
    interface IAnsiConsoleOutput with
        member _.Writer = writer
        member _.IsTerminal = false
        member _.Width = 200
        member _.Height = 200
        member _.SetEncoding(_) = ()

/// Render a Layout into plain text by writing to a Spectre console
/// pointed at a `StringWriter` with ANSI / colours disabled and a fixed
/// 200-char width. Off-TTY rendering path the snapshot tests assert against.
let private renderToText (layout: Layout) : string =
    use sw = new StringWriter()
    let settings = AnsiConsoleSettings()
    settings.Out <- WideOutput(sw :> TextWriter)
    settings.Ansi <- AnsiSupport.No
    settings.ColorSystem <- ColorSystemSupport.NoColors
    settings.Interactive <- InteractionSupport.No
    let console = AnsiConsole.Create(settings)
    console.Write(layout)
    sw.ToString()

[<Tests>]
let dashboardViewTests =
    testList "Broker.Tui.DashboardView (US3 §5)" [

        // Layout structure: every named slot DashboardView populates must
        // be discoverable on the rendered Layout.
        test "render produces a Layout with all six named slots" {
            let layout = DashboardView.render idleReading
            Expect.isNotNull (box layout) "render returns a Layout"
            for slot in [ Layout.Header; Layout.BrokerPane; Layout.SessionPane
                          Layout.ClientsPane; Layout.TelemetryPane; Layout.Footer ] do
                Expect.isSome (Layout.tryGetSlot layout slot) (sprintf "slot %A present" slot)
        }

        test "render does not throw on an idle reading with no clients and no session" {
            let layout = DashboardView.render idleReading
            let text = renderToText layout
            Expect.isGreaterThan text.Length 0 "non-empty render output"
        }

        // FR-018: broker pane shows version and listen address.
        test "broker pane contains version and listen address" {
            let text = DashboardView.render idleReading |> renderToText
            Expect.stringContains text "1.0" "broker version visible"
            Expect.stringContains text "127.0.0.1:5021" "listen address visible"
        }

        // FR-019: mode + session badges visible.
        test "idle mode banner renders the idle badge" {
            let text = DashboardView.render idleReading |> renderToText
            Expect.stringContains text "idle" "idle badge rendered (case-insensitive ok)"
        }

        test "guest mode shows GUEST badge" {
            let reading = { idleReading with mode = Mode.Mode.Guest; session = Some Session.Active
                                             elapsed = Some (TimeSpan.FromSeconds 30.0)
                                             pause = Some Session.Running
                                             speed = Some 1.0m }
            let text = DashboardView.render reading |> renderToText
            Expect.stringContains text "GUEST" "GUEST badge rendered"
        }

        test "host mode shows HOST badge" {
            let cfg : Lobby.LobbyConfig =
                { mapName = "TestMap"; gameMode = "skirmish"; participants = []; display = Lobby.Headless }
            let reading = { idleReading with mode = Mode.Mode.Hosting cfg; session = Some Session.Configuring
                                             elapsed = Some (TimeSpan.FromSeconds 5.0)
                                             pause = Some Session.Running
                                             speed = Some 1.0m }
            let text = DashboardView.render reading |> renderToText
            Expect.stringContains text "HOST" "HOST badge rendered"
        }

        // FR-018: per-client identity, admin flag, queue depth visible in clients pane.
        test "clients pane lists each connected client by name" {
            let reading =
                { idleReading with
                    connectedClients =
                        [ mkClient "alice-bot" (Some 0) true 3
                          mkClient "bob-bot"   None     false 0 ] }
            let text = DashboardView.render reading |> renderToText
            Expect.stringContains text "alice-bot" "alice client name visible"
            Expect.stringContains text "bob-bot"   "bob client name visible"
        }

        test "clients pane header reflects the count" {
            let reading =
                { idleReading with
                    connectedClients =
                        [ mkClient "a" None false 0
                          mkClient "b" None false 0
                          mkClient "c" None false 0 ] }
            let text = DashboardView.render reading |> renderToText
            Expect.stringContains text "Clients (3)" "header shows live client count"
        }

        test "clients pane shows empty placeholder when no clients" {
            let text = DashboardView.render idleReading |> renderToText
            Expect.stringContains text "Clients (0)" "header shows zero count"
            Expect.stringContains text "no clients"  "empty placeholder rendered"
        }

        // FR-020: per-player telemetry visible — resources, units, buildings, kills/losses.
        test "telemetry pane lists per-player resources and counts" {
            let snap : Snapshot.GameStateSnapshot =
                { sessionId = Guid.NewGuid()
                  tick = 42L
                  capturedAt = DateTimeOffset(2026, 4, 28, 13, 0, 0, TimeSpan.Zero)
                  players =
                    [ mkPlayer 1 0 "Red"  1500.0 800.0 12 4 7 2
                      mkPlayer 2 1 "Blue"  900.0 600.0  8 3 2 5 ]
                  units = []
                  buildings = []
                  mapMeta = None }
            let reading =
                { idleReading with
                    mode = Mode.Mode.Guest
                    session = Some Session.Active
                    elapsed = Some (TimeSpan.FromSeconds 60.0)
                    pause = Some Session.Running
                    speed = Some 1.0m
                    telemetry = Some snap }
            let text = DashboardView.render reading |> renderToText
            Expect.stringContains text "Red"    "player Red rendered"
            Expect.stringContains text "Blue"   "player Blue rendered"
            Expect.stringContains text "1500"   "Red metal value visible"
            Expect.stringContains text "800"    "Red energy value visible"
            Expect.stringContains text "tick 42" "telemetry header notes the tick"
        }

        test "telemetry pane shows empty placeholder when no snapshot" {
            let text = DashboardView.render idleReading |> renderToText
            Expect.stringContains text "no telemetry" "empty telemetry placeholder rendered"
        }

        // FR-021 / Invariant 8: stale marker visible when telemetryStale = true.
        test "stale flag surfaces a STALE marker in the rendered output" {
            let reading =
                { idleReading with
                    mode = Mode.Mode.Guest
                    session = Some Session.Active
                    elapsed = Some (TimeSpan.FromSeconds 60.0)
                    pause = Some Session.Running
                    speed = Some 1.0m
                    telemetryStale = true }
            let text = DashboardView.render reading |> renderToText
            Expect.stringContains text "STALE" "stale marker visible (FR-021)"
        }

        test "non-stale reading does not surface the STALE marker" {
            let text = DashboardView.render idleReading |> renderToText
            Expect.isFalse (text.Contains "STALE") "STALE marker absent when telemetry not stale"
        }

        // Footer panel surfaces the canonical hotkey legend so SC-007 can be
        // satisfied in ≤10s of looking — confirm the legend renders.
        test "footer renders the hotkey legend" {
            let text = DashboardView.render idleReading |> renderToText
            Expect.stringContains text "quit"  "footer mentions quit"
            Expect.stringContains text "lobby" "footer mentions lobby hotkey"
        }

        // SC-008 / FR-025: when the viz is unavailable the footer surfaces a
        // human-readable status line. Drives the renderWithViz overload that
        // App-side composition uses for the live tick loop.
        test "renderWithViz_with viz status surfaces the line in the footer" {
            let layout =
                DashboardView.renderWithViz
                    idleReading
                    (Some "2D visualization unavailable: no graphical display")
            let text = renderToText layout
            Expect.stringContains text "2D visualization unavailable"
                "footer surfaces unavailability message"
        }

        test "renderWithViz_with None viz status renders a plain footer" {
            let text =
                DashboardView.renderWithViz idleReading None
                |> renderToText
            Expect.isFalse (text.Contains "unavailable")
                "no viz status means no extra footer line"
            Expect.stringContains text "quit"
                "footer hotkey legend still present"
        }

        test "render is equivalent to renderWithViz with None" {
            // Backwards-compat sanity check so existing callers (and the
            // 14 other tests here) keep passing through `render` while
            // App opts into `renderWithViz`.
            let plain = DashboardView.render idleReading |> renderToText
            let wrapped =
                DashboardView.renderWithViz idleReading None
                |> renderToText
            Expect.equal plain wrapped "render and renderWithViz None agree"
        }
    ]
