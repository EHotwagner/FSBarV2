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

    type Roster = { members: Map<ScriptingClientId, ScriptingClient> }

    type RosterError =
        | NameInUse
        | NotFound of ScriptingClientId

    let empty : Roster = { members = Map.empty }

    let tryAdd
        (id: ScriptingClientId)
        (version: Version)
        (connectedAt: DateTimeOffset)
        (roster: Roster)
        : Result<Roster, RosterError> =
        if roster.members.ContainsKey id then Error NameInUse
        else
            let client : ScriptingClient =
                { id = id
                  connectedAt = connectedAt
                  protocolVersion = version
                  boundSlot = None
                  isAdmin = false
                  commandQueueDepth = 0 }
            Ok { members = roster.members |> Map.add id client }

    let remove (id: ScriptingClientId) (roster: Roster) : Roster =
        { members = roster.members |> Map.remove id }

    let private withClient
        (id: ScriptingClientId)
        (roster: Roster)
        (update: ScriptingClient -> ScriptingClient)
        : Result<Roster, RosterError> =
        match roster.members.TryFind id with
        | None -> Error (NotFound id)
        | Some c -> Ok { members = roster.members |> Map.add id (update c) }

    let grantAdmin (id: ScriptingClientId) (roster: Roster) : Result<Roster, RosterError> =
        withClient id roster (fun c -> { c with isAdmin = true })

    let revokeAdmin (id: ScriptingClientId) (roster: Roster) : Result<Roster, RosterError> =
        withClient id roster (fun c -> { c with isAdmin = false })

    let isAdmin (id: ScriptingClientId) (roster: Roster) : bool =
        match roster.members.TryFind id with
        | Some c -> c.isAdmin
        | None -> false

    let toList (roster: Roster) : ScriptingClient list =
        roster.members |> Map.toList |> List.map snd

    let tryFind (id: ScriptingClientId) (roster: Roster) : ScriptingClient option =
        roster.members.TryFind id
