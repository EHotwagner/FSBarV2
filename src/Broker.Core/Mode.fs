namespace Broker.Core

module Mode =

    type Mode =
        | Idle
        | Hosting of Lobby.LobbyConfig
        | Guest

    let isAdminAuthorised (mode: Mode) : bool =
        match mode with
        | Hosting _ -> true
        | Idle | Guest -> false

    let transition (current: Mode) (next: Mode) : Result<Mode, string> =
        match current, next with
        | Idle, Hosting _ -> Ok next
        | Idle, Guest     -> Ok next
        | Hosting _, Idle -> Ok next
        | Guest, Idle     -> Ok next
        | _, _ when current = next -> Ok current
        | _ -> Error (sprintf "transition not allowed: %A -> %A" current next)
