namespace Broker.Tui

open System
open System.Threading
open System.Threading.Tasks
open Spectre.Console
open Broker.Core

module TickLoop =

    type UiMode =
        | Dashboard
        | Lobby of draft:Lobby.LobbyConfig

    type VizController =
        abstract Toggle : unit -> unit
        abstract Status : unit -> string option

    let private defaultDraft : Lobby.LobbyConfig =
        { mapName = "Tabula"
          gameMode = "Skirmish"
          participants =
            [ { slotIndex = 0; kind = ParticipantSlot.ProxyAi; team = 0; boundClient = None }
              { slotIndex = 1; kind = ParticipantSlot.BuiltInAi 5; team = 1; boundClient = None } ]
          display = Lobby.Headless }

    let dispatch
        (core: Session.CoreFacade)
        (uiMode: UiMode)
        (action: HotkeyMap.Action)
        : UiMode =
        // Pure dispatch table. Returns the next UI mode (most actions are
        // unchanged; OpenLobby switches to Lobby; LaunchHostSession from
        // Lobby switches back to Dashboard). Action results are dropped
        // here — the dashboard footer is the long-term home for Result
        // surfacing, but the broker behaviour does not depend on it.
        let inline ignoreResult (r: Result<unit, string>) = ignore r
        match action, uiMode with
        | HotkeyMap.Quit, _ -> uiMode
        | HotkeyMap.NoAction, _ -> uiMode

        | HotkeyMap.OpenLobby, Dashboard -> Lobby defaultDraft
        | HotkeyMap.OpenLobby, _ -> uiMode

        | HotkeyMap.LaunchHostSession, Lobby draft ->
            // Two-step: first pin the draft as the active host config,
            // then launch (validate + Configuring -> Launching).
            core.OperatorOpenHost draft |> ignoreResult
            core.OperatorLaunchHost ()  |> ignoreResult
            Dashboard
        | HotkeyMap.LaunchHostSession, _ ->
            core.OperatorLaunchHost () |> ignoreResult
            uiMode

        | HotkeyMap.TogglePause, _ ->
            core.OperatorTogglePause () |> ignoreResult
            uiMode
        | HotkeyMap.StepSpeed delta, _ ->
            core.OperatorStepSpeed delta |> ignoreResult
            uiMode
        | HotkeyMap.EndSession, _ ->
            core.OperatorEndSession () |> ignoreResult
            uiMode

        | HotkeyMap.ElevateClient id, _ ->
            core.OperatorGrantAdmin id |> ignoreResult
            uiMode
        | HotkeyMap.RevokeClient id, _ ->
            core.OperatorRevokeAdmin id |> ignoreResult
            uiMode
        | HotkeyMap.OpenElevatePrompt, _ ->
            // Minimal-viable prompt: toggle admin when there is exactly
            // one client. With 0 or >1 we no-op until a richer UI lands —
            // a multi-client prompt is out of scope for the current pass.
            let live =
                core.Roster()
                |> ScriptingRoster.toList
            match live with
            | [ c ] when c.isAdmin -> core.OperatorRevokeAdmin c.id |> ignoreResult
            | [ c ]                -> core.OperatorGrantAdmin  c.id |> ignoreResult
            | _ -> ()
            uiMode

        | HotkeyMap.ToggleViz, _ -> uiMode

    let run
        (core: Session.CoreFacade)
        (viz: VizController option)
        (tickIntervalMs: int)
        (cancellationToken: CancellationToken)
        : Task<unit> =
        // Single-thread render-and-input loop (research.md §4): Spectre's
        // LiveDisplay is not thread-safe, so we own the AnsiConsole here
        // and only this thread touches it.
        task {
            let staleThreshold = TimeSpan.FromSeconds(5.0)
            let buildReading () =
                let now = DateTimeOffset.UtcNow
                let info : Session.BrokerInfo =
                    { version = core.BrokerVersion()
                      listenAddress = "127.0.0.1:5021"
                      startedAt = now }
                Dashboard.build
                    info
                    (Dashboard.Listening "127.0.0.1:5021")
                    (core.Roster())
                    None
                    now
                    staleThreshold

            let vizStatus () =
                match viz with
                | Some v -> v.Status()
                | None   -> None

            let mutable uiMode : UiMode = Dashboard
            let mutable lobbyDraft : Lobby.LobbyConfig = defaultDraft

            let renderCurrent () : Spectre.Console.Layout =
                match uiMode with
                | Dashboard  -> DashboardView.renderWithViz (buildReading ()) (vizStatus ())
                | Lobby draft ->
                    lobbyDraft <- draft
                    LobbyView.render draft

            let initial = renderCurrent ()
            let mutable shouldQuit = false

            let live = AnsiConsole.Live(initial :> Spectre.Console.Rendering.IRenderable)
            do!
                live.StartAsync(fun ctx ->
                    task {
                        while not shouldQuit && not cancellationToken.IsCancellationRequested do
                            // Drain pending key presses; map each through HotkeyMap
                            // and dispatch. In Lobby mode, route the keypress
                            // through LobbyView.apply first so D toggles display
                            // before the global hotkey table sees it.
                            while Console.KeyAvailable do
                                let key = Console.ReadKey(intercept = true)
                                match uiMode with
                                | Lobby draft ->
                                    let edited = LobbyView.apply key draft
                                    if not (System.Object.ReferenceEquals(edited :> obj, draft :> obj))
                                       && edited <> draft then
                                        uiMode <- Lobby edited
                                | Dashboard -> ()
                                match HotkeyMap.map key (core.Mode()) with
                                | HotkeyMap.Quit -> shouldQuit <- true
                                | HotkeyMap.ToggleViz ->
                                    match viz with
                                    | Some v -> try v.Toggle() with _ -> ()
                                    | None   -> ()
                                | other -> uiMode <- dispatch core uiMode other
                            // Re-render the dashboard / lobby from current state.
                            let layout = renderCurrent ()
                            ctx.UpdateTarget(layout :> Spectre.Console.Rendering.IRenderable)
                            ctx.Refresh()
                            try
                                do! Task.Delay(tickIntervalMs, cancellationToken)
                            with :? OperationCanceledException -> shouldQuit <- true
                    } :> Task)
        }
