namespace Broker.Core

open System

module ScriptingRoster =

    type ScriptingClient =
        { id: ScriptingClientId
          connectedAt: DateTimeOffset
          protocolVersion: Version
          boundSlot: int option
          isAdmin: bool
          commandQueueDepth: int }

    type Roster

    type RosterError =
        | NameInUse
        | NotFound of ScriptingClientId

    val empty : Roster

    /// Try to add a client with the given name. Fails with
    /// `Result.Error NameInUse` if the name is currently held (FR-008).
    val tryAdd :
        id:ScriptingClientId
        -> version:Version
        -> connectedAt:DateTimeOffset
        -> roster:Roster
        -> Result<Roster, RosterError>

    val remove : id:ScriptingClientId -> roster:Roster -> Roster

    val grantAdmin : id:ScriptingClientId -> roster:Roster -> Result<Roster, RosterError>
    val revokeAdmin : id:ScriptingClientId -> roster:Roster -> Result<Roster, RosterError>

    val isAdmin : id:ScriptingClientId -> roster:Roster -> bool
    val toList : roster:Roster -> ScriptingClient list

    val tryFind : id:ScriptingClientId -> roster:Roster -> ScriptingClient option
