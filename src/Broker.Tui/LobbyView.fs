namespace Broker.Tui

open System
open Spectre.Console
open Spectre.Console.Rendering
open Broker.Core

module LobbyView =

    let private displayLabel (d: Lobby.Display) =
        match d with
        | Lobby.Headless  -> "Headless"
        | Lobby.Graphical -> "Graphical"

    let private kindLabel (k: ParticipantSlot.ParticipantKind) =
        match k with
        | ParticipantSlot.Human            -> "Human"
        | ParticipantSlot.BuiltInAi diff   -> sprintf "BuiltInAi %d" diff
        | ParticipantSlot.ProxyAi          -> "ProxyAi"

    let private headerPanel (draft: Lobby.LobbyConfig) : IRenderable =
        let table =
            (Table())
                .Border(TableBorder.Rounded)
                .HideHeaders()
                .AddColumn("k").AddColumn("v")
        table.AddRow("Map",     Markup.Escape draft.mapName) |> ignore
        table.AddRow("Mode",    Markup.Escape draft.gameMode) |> ignore
        table.AddRow("Display", displayLabel draft.display) |> ignore
        Panel(table :> IRenderable)
            .Header("[bold]Lobby[/]")
            .Border(BoxBorder.Rounded) :> IRenderable

    let private slotsPanel (draft: Lobby.LobbyConfig) : IRenderable =
        let table =
            (Table())
                .Border(TableBorder.Rounded)
                .AddColumn("idx")
                .AddColumn("kind")
                .AddColumn("team")
                .AddColumn("bound")
        if List.isEmpty draft.participants then
            table.AddRow("[grey]no participants[/]", "", "", "") |> ignore
        else
            for s in draft.participants do
                let bound =
                    match s.boundClient with
                    | Some (ScriptingClientId n) -> Markup.Escape n
                    | None -> "—"
                table.AddRow(
                    string s.slotIndex,
                    kindLabel s.kind,
                    string s.team,
                    bound) |> ignore
        Panel(table :> IRenderable)
            .Header(sprintf "[bold]Slots (%d)[/]" (List.length draft.participants))
            .Border(BoxBorder.Rounded) :> IRenderable

    let private footerPanel () : IRenderable =
        let line =
            "[grey]Enter[/] launch · [grey]D[/] toggle display · [grey]Esc[/] cancel · [grey]Q[/] quit"
        Panel(Markup(line) :> IRenderable)
            .Border(BoxBorder.Rounded) :> IRenderable

    let render (draft: Lobby.LobbyConfig) : Layout =
        let root =
            (Layout("Lobby"))
                .SplitRows(
                    (Layout("Header")).Size(8),
                    (Layout("Slots")),
                    (Layout("Footer")).Size(3))
        root.["Header"].Update(headerPanel draft)  |> ignore
        root.["Slots"].Update(slotsPanel draft)    |> ignore
        root.["Footer"].Update(footerPanel ())     |> ignore
        root

    let apply
        (key: ConsoleKeyInfo)
        (draft: Lobby.LobbyConfig)
        : Lobby.LobbyConfig =
        // Minimum-viable editor: only Display has a finite enum, so it is
        // the only field with a single-keypress edit path. Map and game
        // mode are typed strings (text-input UI is out of scope for this
        // pass — quickstart §3 uses defaults). Slot editing is delegated
        // to the dedicated participant-pane handler in TickLoop.
        match key.Key with
        | ConsoleKey.D ->
            let next =
                match draft.display with
                | Lobby.Headless  -> Lobby.Graphical
                | Lobby.Graphical -> Lobby.Headless
            { draft with display = next }
        | _ -> draft
