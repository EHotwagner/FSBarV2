namespace Broker.Tui

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
        /// Operator pressed `A` from the dashboard — host mode only.
        /// The dashboard is responsible for prompting the operator to
        /// pick a client and emitting `ElevateClient` once chosen.
        | OpenElevatePrompt
        /// Operator pressed `X` to terminate the active session.
        /// Maps to `Session.EndReason.OperatorTerminated` at wire-up.
        | EndSession
        | ToggleViz
        | NoAction

    /// Map a single key (with modifiers) to an Action in the current
    /// dashboard mode. Returns `NoAction` for unbound keys.
    val map :
        key:System.ConsoleKeyInfo
        -> mode:Mode.Mode
        -> Action
