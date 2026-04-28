namespace Broker.Tui

open System
open Broker.Core

module HotkeyMap =

    type Action =
        | Quit
        | OpenLobby
        | LaunchHostSession
        | TogglePause
        | StepSpeed of delta:decimal
        | ElevateClient of ScriptingClientId
        | RevokeClient of ScriptingClientId
        | OpenElevatePrompt
        | EndSession
        | ToggleViz
        | NoAction

    let private isHosting (mode: Mode.Mode) =
        match mode with
        | Mode.Mode.Hosting _ -> true
        | _ -> false

    let private isActive (mode: Mode.Mode) =
        match mode with
        | Mode.Mode.Hosting _ | Mode.Mode.Guest -> true
        | Mode.Mode.Idle -> false

    let map (key: ConsoleKeyInfo) (mode: Mode.Mode) : Action =
        // Bindings per quickstart.md / spec hotkey list:
        //   Q       : quit (always available)
        //   V       : toggle 2D viz (always available, may report unavailable)
        //   L       : open lobby (Idle only)
        //   Enter   : confirm host launch (when in Hosting + Configuring)
        //   Space   : pause/resume (host mode only)
        //   + / -   : step game speed (host mode only)
        //   A       : open elevate prompt (host mode only)
        //   X       : end session (any active mode)
        // ElevateClient / RevokeClient require an explicit client target
        // (the operator is interacting with the clients pane), so the
        // single-keypress mapping returns NoAction for them — those
        // actions are emitted by the dedicated clients-pane handler.
        match key.Key with
        | ConsoleKey.Q     -> Quit
        | ConsoleKey.V     -> ToggleViz
        | ConsoleKey.L     when mode = Mode.Mode.Idle -> OpenLobby
        | ConsoleKey.Enter when isHosting mode -> LaunchHostSession
        | ConsoleKey.Spacebar when isHosting mode -> TogglePause
        | ConsoleKey.OemPlus
        | ConsoleKey.Add   when isHosting mode -> StepSpeed 0.25m
        | ConsoleKey.OemMinus
        | ConsoleKey.Subtract when isHosting mode -> StepSpeed -0.25m
        | ConsoleKey.A     when isHosting mode -> OpenElevatePrompt
        | ConsoleKey.X     when isActive mode  -> EndSession
        | _ -> NoAction
