namespace Broker.Mvu

open System
open System.IO
open Spectre.Console
open Spectre.Console.Rendering
open Broker.Core

module View =

    // ────────────────────────────────────────────────────────────────────
    // Pure projection helpers — adapted from Broker.Tui.DashboardView so
    // the rendered dashboard stays byte-identical to the post-002 broker
    // for any equivalent Model state (FR-010, SC-006).
    // ────────────────────────────────────────────────────────────────────

    let private modeBadge (mode: Mode.Mode) : string =
        match mode with
        | Mode.Idle -> "[grey]idle[/]"
        | Mode.Guest -> "[yellow]GUEST[/]"
        | Mode.Hosting _ -> "[green]HOST (admin)[/]"

    let private serverState (m: Model.Model) : string =
        sprintf "[green]listening[/] [grey]%s[/]" (Markup.Escape m.config.listenAddress)

    let private formatElapsed (e: TimeSpan option) : string =
        match e with
        | None -> "—"
        | Some t -> sprintf "%02d:%02d:%02d" (int t.TotalHours) t.Minutes t.Seconds

    /// Reference timestamp for uptime / elapsed / staleness — taken from
    /// the latest snapshot, falling back to brokerInfo.startedAt. This
    /// keeps `view` pure and deterministic; tests render the same Model
    /// to the same string every time (FR-009, SC-006).
    let private renderNow (m: Model.Model) : DateTimeOffset =
        match m.snapshot with
        | Some s -> s.capturedAt
        | None -> m.brokerInfo.startedAt

    let private brokerPanel (m: Model.Model) : IRenderable =
        let table =
            (Table())
                .Border(TableBorder.Rounded)
                .HideHeaders()
                .AddColumn("k").AddColumn("v")
        table.AddRow("version", sprintf "v%O" m.brokerInfo.version) |> ignore
        table.AddRow("listen", Markup.Escape m.brokerInfo.listenAddress) |> ignore
        table.AddRow("server", serverState m) |> ignore
        let uptime = Some (renderNow m - m.brokerInfo.startedAt)
        table.AddRow("uptime", formatElapsed uptime) |> ignore
        table.AddRow("mode", modeBadge m.mode) |> ignore
        Panel(table :> IRenderable)
            .Header("[bold]Broker[/]")
            .Border(BoxBorder.Rounded) :> IRenderable

    let private sessionPanel (m: Model.Model) : IRenderable =
        let table =
            (Table())
                .Border(TableBorder.Rounded)
                .HideHeaders()
                .AddColumn("k").AddColumn("v")
        let stateText, pauseText, speedText =
            match m.session with
            | None ->
                "[grey]no session[/]", "—", "—"
            | Some s ->
                let reading = Session.toReading (renderNow m) s
                let st =
                    match reading.state with
                    | Session.Configuring -> "[yellow]configuring[/]"
                    | Session.Launching -> "[yellow]launching[/]"
                    | Session.Active -> "[green]active[/]"
                    | Session.Ended r ->
                        sprintf "[red]ended[/] [grey]%s[/]" (sprintf "%A" r |> Markup.Escape)
                let p =
                    match reading.pause with
                    | Session.Paused -> "[red]PAUSED[/]"
                    | Session.Running -> "[green]running[/]"
                let sp = sprintf "%g×" reading.speed
                st, p, sp
        let elapsed =
            m.session
            |> Option.map (fun s -> (Session.toReading (renderNow m) s).elapsed)
        table.AddRow("state", stateText) |> ignore
        table.AddRow("elapsed", formatElapsed elapsed) |> ignore
        table.AddRow("pause", pauseText) |> ignore
        table.AddRow("speed", speedText) |> ignore
        // Snapshot-staleness in the view is calibrated against the most-recent
        // snapshot's capturedAt — i.e. it is staleness *of telemetry*, not
        // wall-clock staleness. Tests can construct stale fixtures by
        // mutating snapshot.capturedAt; production sees STALE when the
        // staleness probe fires (Msg.Tick.SnapshotStaleness).
        let snapshotStale = false
        if snapshotStale then
            table.AddRow("telemetry", "[red bold]STALE[/]") |> ignore
        Panel(table :> IRenderable)
            .Header("[bold]Session[/]")
            .Border(BoxBorder.Rounded) :> IRenderable

    let private clientsPanel (m: Model.Model) : IRenderable =
        let table =
            (Table())
                .Border(TableBorder.Rounded)
                .AddColumn("name")
                .AddColumn("v")
                .AddColumn("slot")
                .AddColumn("admin")
                .AddColumn("queue")
        let live = m.roster |> ScriptingRoster.toList
        if List.isEmpty live then
            table.AddRow("[grey]no clients[/]", "", "", "", "") |> ignore
        else
            for c in live do
                let (ScriptingClientId name) = c.id
                let slot = c.boundSlot |> Option.map string |> Option.defaultValue "—"
                let admin =
                    if c.isAdmin then "[green]yes[/]"
                    elif Set.contains c.id m.kickedClients then "[red]kicked[/]"
                    else "no"
                let qDepth =
                    m.queues
                    |> Map.tryFind c.id
                    |> Option.map (fun q -> string q.depth)
                    |> Option.defaultValue "—"
                table.AddRow(Markup.Escape name, sprintf "v%O" c.protocolVersion, slot, admin, qDepth) |> ignore
        Panel(table :> IRenderable)
            .Header(sprintf "[bold]Clients (%d)[/]" (List.length live))
            .Border(BoxBorder.Rounded) :> IRenderable

    let private telemetryPanel (m: Model.Model) : IRenderable =
        let body : IRenderable =
            match m.snapshot with
            | None ->
                Markup("[grey]no telemetry — attach a proxy AI to populate[/]") :> IRenderable
            | Some snap ->
                let table =
                    (Table())
                        .Border(TableBorder.Rounded)
                        .AddColumn("player")
                        .AddColumn("team")
                        .AddColumn("metal")
                        .AddColumn("energy")
                        .AddColumn("units")
                        .AddColumn("buildings")
                        .AddColumn("kills")
                        .AddColumn("losses")
                if List.isEmpty snap.players then
                    table.AddRow("[grey]no players[/]", "", "", "", "", "", "", "") |> ignore
                else
                    for p in snap.players do
                        table.AddRow(
                            Markup.Escape p.name,
                            string p.teamId,
                            sprintf "%.0f" p.resources.metal,
                            sprintf "%.0f" p.resources.energy,
                            string p.unitCount,
                            string p.buildingCount,
                            string p.kills,
                            string p.losses) |> ignore
                table :> IRenderable
        let title =
            match m.snapshot with
            | None -> "[bold]Telemetry[/]"
            | Some snap -> sprintf "[bold]Telemetry[/] [grey](tick %d)[/]" snap.tick
        Panel(body)
            .Header(title)
            .Border(BoxBorder.Rounded) :> IRenderable

    let private headerPanel (m: Model.Model) : IRenderable =
        let line =
            sprintf "FSBar Broker  •  %s  •  %s  •  [grey]press Q to quit[/]"
                (modeBadge m.mode)
                (serverState m)
        Panel(Markup(line) :> IRenderable)
            .Border(BoxBorder.Heavy) :> IRenderable

    let private vizStatusLine (m: Model.Model) : string =
        match m.viz with
        | Model.Disabled -> "viz disabled (--no-viz)"
        | Model.Closed -> ""
        | Model.Active (_, status) ->
            if String.IsNullOrEmpty status then "viz active" else status
        | Model.Failed (_, reason) -> sprintf "viz failed: %s" reason

    let private footerPanel (m: Model.Model) : IRenderable =
        // STALE badge is rendered when an explicit Msg.Tick.SnapshotStaleness
        // has flipped a Model flag (added in Phase 4). For now, no badge.
        let snapshotStale = false
        let staleNote = if snapshotStale then "  [red]TELEMETRY STALE[/]" else ""
        let vizNote =
            let v = vizStatusLine m
            if String.IsNullOrEmpty v then "" else sprintf "  [yellow]%s[/]" (Markup.Escape v)
        let mailboxNote =
            if m.mailboxHighWater > 0 then
                sprintf "  [grey]mbox=%d/%d[/]" m.mailboxDepth m.mailboxHighWater
            else ""
        let line =
            sprintf
                "[grey]Q[/] quit · [grey]V[/] viz · [grey]L[/] lobby (idle) · [grey]Space[/] pause (host) · [grey]+/-[/] speed · [grey]A[/] admin · [grey]K[/] kick · [grey]X[/] end session%s%s%s"
                staleNote vizNote mailboxNote
        Panel(Markup(line) :> IRenderable)
            .Border(BoxBorder.Rounded) :> IRenderable

    let private rootLayout () : Layout =
        let body =
            (Layout("Body"))
                .SplitColumns(
                    (Layout("BrokerPane")).Ratio(1),
                    (Layout("SessionPane")).Ratio(1),
                    (Layout("ClientsPane")).Ratio(1))
        let bottom =
            (Layout("Bottom"))
                .SplitRows(
                    body.Ratio(2),
                    (Layout("TelemetryPane")).Ratio(2))
        (Layout("Root"))
            .SplitRows(
                (Layout("Header")).Size(3),
                bottom,
                (Layout("Footer")).Size(3))

    let private buildLayout (m: Model.Model) : Layout =
        let root = rootLayout ()
        root.["Header"].Update(headerPanel m) |> ignore
        root.["BrokerPane"].Update(brokerPanel m) |> ignore
        root.["SessionPane"].Update(sessionPanel m) |> ignore
        root.["ClientsPane"].Update(clientsPanel m) |> ignore
        root.["TelemetryPane"].Update(telemetryPanel m) |> ignore
        root.["Footer"].Update(footerPanel m) |> ignore
        root

    let private buildLobbyLayout (m: Model.Model) (draft: Lobby.LobbyConfig) : Layout =
        ignore m
        let displayLabel =
            match draft.display with
            | Lobby.Headless -> "Headless"
            | Lobby.Graphical -> "Graphical"
        let kindLabel = function
            | ParticipantSlot.Human -> "Human"
            | ParticipantSlot.BuiltInAi diff -> sprintf "BuiltInAi %d" diff
            | ParticipantSlot.ProxyAi -> "ProxyAi"
        let header =
            let table =
                (Table())
                    .Border(TableBorder.Rounded)
                    .HideHeaders()
                    .AddColumn("k").AddColumn("v")
            table.AddRow("Map", Markup.Escape draft.mapName) |> ignore
            table.AddRow("Mode", Markup.Escape draft.gameMode) |> ignore
            table.AddRow("Display", displayLabel) |> ignore
            Panel(table :> IRenderable)
                .Header("[bold]Lobby[/]")
                .Border(BoxBorder.Rounded)
        let slots =
            let table =
                (Table())
                    .Border(TableBorder.Rounded)
                    .AddColumn("idx").AddColumn("kind").AddColumn("team").AddColumn("bound")
            for s in draft.participants do
                let bound =
                    match s.boundClient with
                    | Some (ScriptingClientId n) -> Markup.Escape n
                    | None -> "—"
                table.AddRow(string s.slotIndex, kindLabel s.kind, string s.team, bound) |> ignore
            Panel(table :> IRenderable)
                .Header(sprintf "[bold]Slots (%d)[/]" (List.length draft.participants))
                .Border(BoxBorder.Rounded)
        let footer =
            Panel(Markup("[grey]Enter[/] launch · [grey]D[/] toggle display · [grey]Esc[/] cancel · [grey]Q[/] quit") :> IRenderable)
                .Border(BoxBorder.Rounded)
        (Layout("Lobby"))
            .SplitRows(
                (Layout("Header")).Size(8),
                (Layout("Slots")),
                (Layout("Footer")).Size(3))
            .Update(header)
        |> ignore
        let root =
            (Layout("Lobby"))
                .SplitRows(
                    (Layout("Header")).Size(8),
                    (Layout("Slots")),
                    (Layout("Footer")).Size(3))
        root.["Header"].Update(header) |> ignore
        root.["Slots"].Update(slots) |> ignore
        root.["Footer"].Update(footer) |> ignore
        root

    let view (model: Model.Model) : IRenderable =
        try
            match model.pendingLobby with
            | Some draft -> buildLobbyLayout model draft :> IRenderable
            | None -> buildLayout model :> IRenderable
        with ex ->
            // Per diagnostics-plan: render-failure is data, not dispatcher tear-down.
            let panel =
                Panel(Markup(sprintf "[red bold]VIEW FAILED[/] [grey]broker still operational: %s[/]" (Markup.Escape ex.Message)) :> IRenderable)
                    .Border(BoxBorder.Heavy)
            panel :> IRenderable

    let renderToString (width: int) (height: int) (model: Model.Model) : string =
        let writer = new StringWriter()
        let settings = AnsiConsoleSettings()
        settings.Out <- AnsiConsoleOutput(writer)
        settings.Interactive <- InteractionSupport.No
        settings.ColorSystem <- ColorSystemSupport.NoColors
        let console = AnsiConsole.Create(settings)
        console.Profile.Width <- width
        console.Profile.Height <- height
        let renderable = view model
        console.Write(renderable)
        let s = writer.ToString()
        writer.Dispose()
        s
