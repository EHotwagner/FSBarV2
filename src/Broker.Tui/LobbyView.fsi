namespace Broker.Tui

open Broker.Core

module LobbyView =

    /// Editable lobby form (host mode only). Render returns the current
    /// configuration view; `apply` mutates a mutable working copy via the
    /// supplied `LobbyConfig` editor function.
    val render : draft:Lobby.LobbyConfig -> Spectre.Console.Layout

    /// Single-keypress edit step. Returns the updated draft (or the input
    /// draft unchanged for unbound keys).
    val apply :
        key:System.ConsoleKeyInfo
        -> draft:Lobby.LobbyConfig
        -> Lobby.LobbyConfig
