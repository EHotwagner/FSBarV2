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

    /// FR-013: validate the configuration. Returns the canonical config
    /// or the first validation failure encountered.
    /// `connectedClients` carries the names of the currently-connected
    /// scripting clients so the broker can refuse to launch a host-mode
    /// session whose lobby has no `ProxyAi` slot for them
    /// (`MissingProxySlotForBoundClient`).
    val validate :
        config:LobbyConfig
        -> connectedClients:ScriptingClientId list
        -> Result<LobbyConfig, LobbyError>
