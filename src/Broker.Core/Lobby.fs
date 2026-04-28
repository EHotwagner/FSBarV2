namespace Broker.Core

module Lobby =

    type Display = Headless | Graphical

    type LobbyConfig =
        { mapName: string
          gameMode: string
          participants: ParticipantSlot.ParticipantSlot list
          display: Display }

    type LobbyError =
        | EmptyMapName
        | EmptyGameMode
        | DuplicateSlotIndex of int
        | TooManyParticipants of capacity:int * actual:int
        | MissingProxySlotForBoundClient of clientName:string

    /// Conservative cap for now; real engine map metadata would carry the
    /// authoritative capacity. 16 mirrors HighBarV3's lobby max.
    let private hardCapacity = 16

    let private hasProxySlot (slots: ParticipantSlot.ParticipantSlot list) : bool =
        slots |> List.exists (fun s -> s.kind = ParticipantSlot.ProxyAi)

    let validate
        (config: LobbyConfig)
        (connectedClients: ScriptingClientId list)
        : Result<LobbyConfig, LobbyError> =
        if System.String.IsNullOrWhiteSpace config.mapName then Error EmptyMapName
        elif System.String.IsNullOrWhiteSpace config.gameMode then Error EmptyGameMode
        elif config.participants.Length > hardCapacity then
            Error (TooManyParticipants (hardCapacity, config.participants.Length))
        else
            // Duplicate slotIndex.
            let duplicate =
                config.participants
                |> List.groupBy (fun s -> s.slotIndex)
                |> List.tryPick (fun (idx, bucket) -> if List.length bucket > 1 then Some idx else None)
            match duplicate with
            | Some idx -> Error (DuplicateSlotIndex idx)
            | None ->
                // FR-013: refuse launch when a scripting client is
                // currently connected and the lobby has no ProxyAi slot
                // for it to land in. Quickstart §3 step 2.
                match connectedClients with
                | client :: _ when not (hasProxySlot config.participants) ->
                    let (ScriptingClientId name) = client
                    Error (MissingProxySlotForBoundClient name)
                | _ -> Ok config
