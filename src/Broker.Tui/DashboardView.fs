namespace Broker.Tui

open System
open Spectre.Console
open Spectre.Console.Rendering
open Broker.Core

module DashboardView =

    let private modeBadge (mode: Mode.Mode) : string =
        match mode with
        | Mode.Mode.Idle      -> "[grey]idle[/]"
        | Mode.Mode.Guest     -> "[yellow]GUEST[/]"
        | Mode.Mode.Hosting _ -> "[green]HOST (admin)[/]"

    let private serverState (s: Dashboard.ServerState) : string =
        match s with
        | Dashboard.Listening addr -> sprintf "[green]listening[/] [grey]%s[/]" (Markup.Escape addr)
        | Dashboard.Down reason    -> sprintf "[red]down[/] %s" (Markup.Escape reason)

    let private formatElapsed (e: TimeSpan option) : string =
        match e with
        | None -> "—"
        | Some t -> sprintf "%02d:%02d:%02d" (int t.TotalHours) t.Minutes t.Seconds

    let private brokerPanel (r: Dashboard.DiagnosticReading) : IRenderable =
        let table =
            (Table())
                .Border(TableBorder.Rounded)
                .HideHeaders()
                .AddColumn("k").AddColumn("v")
        table.AddRow("version",  sprintf "v%O" r.broker.version) |> ignore
        table.AddRow("listen",   Markup.Escape r.broker.listenAddress) |> ignore
        table.AddRow("server",   serverState r.serverState) |> ignore
        table.AddRow("uptime",   formatElapsed (Some (DateTimeOffset.UtcNow - r.broker.startedAt))) |> ignore
        table.AddRow("mode",     modeBadge r.mode) |> ignore
        Panel(table :> IRenderable)
            .Header("[bold]Broker[/]")
            .Border(BoxBorder.Rounded) :> IRenderable

    let private sessionPanel (r: Dashboard.DiagnosticReading) : IRenderable =
        let table =
            (Table())
                .Border(TableBorder.Rounded)
                .HideHeaders()
                .AddColumn("k").AddColumn("v")
        let stateText =
            match r.session with
            | None -> "[grey]no session[/]"
            | Some Session.Configuring        -> "[yellow]configuring[/]"
            | Some Session.Launching          -> "[yellow]launching[/]"
            | Some Session.Active             -> "[green]active[/]"
            | Some (Session.Ended reason)     -> sprintf "[red]ended[/] [grey]%s[/]" (sprintf "%A" reason |> Markup.Escape)
        let pauseText =
            match r.pause with
            | Some Session.Paused -> "[red]PAUSED[/]"
            | Some Session.Running -> "[green]running[/]"
            | None -> "—"
        let speedText =
            match r.speed with
            | Some s -> sprintf "%g×" s
            | None -> "—"
        table.AddRow("state",    stateText) |> ignore
        table.AddRow("elapsed",  formatElapsed r.elapsed) |> ignore
        table.AddRow("pause",    pauseText) |> ignore
        table.AddRow("speed",    speedText) |> ignore
        if r.telemetryStale then
            table.AddRow("telemetry", "[red bold]STALE[/]") |> ignore
        Panel(table :> IRenderable)
            .Header("[bold]Session[/]")
            .Border(BoxBorder.Rounded) :> IRenderable

    let private clientsPanel (r: Dashboard.DiagnosticReading) : IRenderable =
        let table =
            (Table())
                .Border(TableBorder.Rounded)
                .AddColumn("name")
                .AddColumn("v")
                .AddColumn("slot")
                .AddColumn("admin")
                .AddColumn("queue")
        if List.isEmpty r.connectedClients then
            table.AddRow("[grey]no clients[/]", "", "", "", "") |> ignore
        else
            for c in r.connectedClients do
                let (ScriptingClientId name) = c.id
                let slot = c.boundSlot |> Option.map string |> Option.defaultValue "—"
                let admin = if c.isAdmin then "[green]yes[/]" else "no"
                table.AddRow(
                    Markup.Escape name,
                    sprintf "v%O" c.protocolVersion,
                    slot,
                    admin,
                    string c.commandQueueDepth) |> ignore
        Panel(table :> IRenderable)
            .Header(sprintf "[bold]Clients (%d)[/]" (List.length r.connectedClients))
            .Border(BoxBorder.Rounded) :> IRenderable

    let private telemetryPanel (r: Dashboard.DiagnosticReading) : IRenderable =
        let body : IRenderable =
            match r.telemetry with
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
            match r.telemetry with
            | None      -> "[bold]Telemetry[/]"
            | Some snap -> sprintf "[bold]Telemetry[/] [grey](tick %d)[/]" snap.tick
        Panel(body)
            .Header(title)
            .Border(BoxBorder.Rounded) :> IRenderable

    let private headerPanel (r: Dashboard.DiagnosticReading) : IRenderable =
        let line =
            sprintf
                "FSBar Broker  •  %s  •  %s  •  [grey]press Q to quit[/]"
                (modeBadge r.mode)
                (serverState r.serverState)
        Panel(Markup(line) :> IRenderable)
            .Border(BoxBorder.Heavy) :> IRenderable

    let private footerPanel (r: Dashboard.DiagnosticReading) (vizStatus: string option) : IRenderable =
        let staleNote =
            if r.telemetryStale then "  [red]TELEMETRY STALE[/]" else ""
        let vizNote =
            match vizStatus with
            | Some s when not (System.String.IsNullOrEmpty s) ->
                sprintf "  [yellow]%s[/]" (Markup.Escape s)
            | _ -> ""
        let line =
            sprintf
                "[grey]Q[/] quit · [grey]V[/] viz · [grey]L[/] lobby (idle) · [grey]Space[/] pause (host) · [grey]+/-[/] speed · [grey]X[/] end session%s%s"
                staleNote
                vizNote
        Panel(Markup(line) :> IRenderable)
            .Border(BoxBorder.Rounded) :> IRenderable

    let renderWithViz (reading: Dashboard.DiagnosticReading) (vizStatus: string option) : Layout =
        let root = Layout.rootLayout ()
        let put slot panel =
            match Layout.tryGetSlot root slot with
            | Some l -> l.Update(panel) |> ignore
            | None -> ()
        put Layout.Header        (headerPanel reading)
        put Layout.BrokerPane    (brokerPanel reading)
        put Layout.SessionPane   (sessionPanel reading)
        put Layout.ClientsPane   (clientsPanel reading)
        put Layout.TelemetryPane (telemetryPanel reading)
        put Layout.Footer        (footerPanel reading vizStatus)
        root

    let render (reading: Dashboard.DiagnosticReading) : Layout =
        renderWithViz reading None
