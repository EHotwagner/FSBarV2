namespace Broker.Protocol

open System
open System.Threading.Channels
open Broker.Core
open FSBarV2.Broker.Contracts

module BrokerState =

    type ClientChannel =
        { id: ScriptingClientId
          queue: CommandPipeline.Queue
          mutable subscriber: Channel<StateMsg> option }

    type SnapshotBroadcaster() =
        let observers = ResizeArray<IObserver<Snapshot.GameStateSnapshot>>()
        let lock' = obj ()
        member _.Push(snap: Snapshot.GameStateSnapshot) =
            let snap' =
                lock lock' (fun () -> observers.ToArray())
            for o in snap' do
                try o.OnNext(snap) with _ -> ()
        interface IObservable<Snapshot.GameStateSnapshot> with
            member _.Subscribe(observer: IObserver<Snapshot.GameStateSnapshot>) =
                lock lock' (fun () -> observers.Add observer)
                { new IDisposable with
                    member _.Dispose() =
                        lock lock' (fun () -> observers.Remove observer |> ignore) }

    type Hub =
        { brokerVersion: Version
          commandQueueCapacity: int
          auditEmitter: Audit.AuditEvent -> unit
          mutable session: Session.Session option
          mutable mode: Mode.Mode
          mutable roster: ScriptingRoster.Roster
          mutable slots: ParticipantSlot.ParticipantSlot list
          mutable proxyOutbound: Channel<CommandPipeline.Command> option
          clients: System.Collections.Concurrent.ConcurrentDictionary<ScriptingClientId, ClientChannel>
          stateLock: obj
          snapshotBroadcaster: SnapshotBroadcaster }

    let create
        (brokerVersion: Version)
        (commandQueueCapacity: int)
        (auditEmitter: Audit.AuditEvent -> unit)
        : Hub =
        { brokerVersion = brokerVersion
          commandQueueCapacity = commandQueueCapacity
          auditEmitter = auditEmitter
          session = None
          mode = Mode.Mode.Idle
          roster = ScriptingRoster.empty
          slots = []
          proxyOutbound = None
          clients = System.Collections.Concurrent.ConcurrentDictionary<ScriptingClientId, ClientChannel>()
          stateLock = obj()
          snapshotBroadcaster = SnapshotBroadcaster() }

    let brokerVersion (hub: Hub) = hub.brokerVersion
    let auditEmitter (hub: Hub) = hub.auditEmitter
    let mode (hub: Hub) = hub.mode
    let roster (hub: Hub) = hub.roster
    let slots (hub: Hub) = hub.slots
    let session (hub: Hub) = hub.session

    let private withLock (hub: Hub) (f: unit -> 'a) : 'a =
        lock hub.stateLock f

    let private newProxyOutbound capacity =
        let opts =
            BoundedChannelOptions(capacity,
                FullMode = BoundedChannelFullMode.Wait,
                SingleReader = true,
                SingleWriter = false)
        Channel.CreateBounded<CommandPipeline.Command>(opts)

    let openHostSession
        (config: Lobby.LobbyConfig)
        (at: DateTimeOffset)
        (hub: Hub)
        : Result<unit, string> =
        withLock hub (fun () ->
            match hub.session with
            | Some _ -> Error "session already active"
            | None ->
                let s = Session.newHostSession config at
                let prevMode = hub.mode
                hub.session <- Some s
                hub.mode <- Mode.Mode.Hosting config
                hub.slots <- config.participants
                hub.auditEmitter (Audit.AuditEvent.ModeChanged (at, prevMode, hub.mode))
                Ok ())

    let launchHostSession (at: DateTimeOffset) (hub: Hub) : Result<unit, string> =
        ignore at
        withLock hub (fun () ->
            match hub.session, hub.mode with
            | Some s, Mode.Mode.Hosting cfg ->
                let connected =
                    hub.roster
                    |> ScriptingRoster.toList
                    |> List.map (fun c -> c.id)
                match Lobby.validate cfg connected with
                | Error e -> Error (sprintf "lobby invalid: %A" e)
                | Ok _ ->
                    match Session.markLaunching s with
                    | Ok newSession ->
                        hub.session <- Some newSession
                        Ok ()
                    | Error e -> Error e
            | None, _ -> Error "no active session"
            | Some _, _ -> Error "launchHostSession requires Hosting mode")

    let openGuestSession (at: DateTimeOffset) (hub: Hub) : Result<unit, string> =
        withLock hub (fun () ->
            match hub.session with
            | Some _ -> Error "session already active"
            | None ->
                let s = Session.newGuestSession at
                let prevMode = hub.mode
                hub.session <- Some s
                hub.mode <- Mode.Mode.Guest
                hub.auditEmitter (Audit.AuditEvent.ModeChanged (at, prevMode, hub.mode))
                Ok ())

    let private broadcastSessionEnd (hub: Hub) (sessionId: Guid) (reason: Session.EndReason) =
        let endReasonEnum =
            match reason with
            | Session.Victory             -> SessionEnd.Types.Reason.Victory
            | Session.Defeat              -> SessionEnd.Types.Reason.Defeat
            | Session.OperatorTerminated  -> SessionEnd.Types.Reason.OperatorTerminated
            | Session.GameCrashed         -> SessionEnd.Types.Reason.GameCrashed
            | Session.ProxyDisconnected _ -> SessionEnd.Types.Reason.ProxyDisconnected
        let detail =
            match reason with
            | Session.ProxyDisconnected d -> d
            | _ -> ""
        let bytes = Google.Protobuf.ByteString.CopyFrom(sessionId.ToByteArray())
        let endMsg = SessionEnd.empty()
        endMsg.SessionId <- bytes
        endMsg.Reason <- endReasonEnum
        endMsg.Detail <- detail
        let stateMsg = StateMsg.empty()
        stateMsg.SessionEnd <- endMsg
        for KeyValue(_, c) in hub.clients do
            match c.subscriber with
            | Some ch ->
                ch.Writer.TryWrite(stateMsg) |> ignore
                ch.Writer.TryComplete() |> ignore
            | None -> ()

    let closeSession (reason: Session.EndReason) (at: DateTimeOffset) (hub: Hub) : unit =
        withLock hub (fun () ->
            match hub.session with
            | None -> ()
            | Some s ->
                broadcastSessionEnd hub (Session.id s) reason
                hub.auditEmitter (Audit.AuditEvent.SessionEnded (at, Session.id s, reason))
                let prevMode = hub.mode
                hub.session <- None
                hub.mode <- Mode.Mode.Idle
                hub.slots <- []
                hub.proxyOutbound <- None
                if prevMode <> hub.mode then
                    hub.auditEmitter (Audit.AuditEvent.ModeChanged (at, prevMode, hub.mode)))

    let attachProxy (link: Session.ProxyAiLink) (hub: Hub) : Result<unit, string> =
        withLock hub (fun () ->
            // Auto-detect: if no host session is in flight, this is Guest.
            let s, mode =
                match hub.session with
                | None ->
                    let g = Session.newGuestSession link.attachedAt
                    g, Mode.Mode.Guest
                | Some existing -> existing, hub.mode
            match Session.attachProxy link s with
            | Error e -> Error e
            | Ok newSession ->
                let prevMode = hub.mode
                hub.session <- Some newSession
                hub.mode <- mode
                hub.proxyOutbound <- Some (newProxyOutbound hub.commandQueueCapacity)
                if prevMode <> hub.mode then
                    hub.auditEmitter (Audit.AuditEvent.ModeChanged (link.attachedAt, prevMode, hub.mode))
                hub.auditEmitter (Audit.AuditEvent.ProxyAttached (link.attachedAt, link.protocolVersion))
                Ok ())

    let applySnapshot (snapshot: Snapshot.GameStateSnapshot) (hub: Hub) : unit =
        let wireSnapshot, subscribers =
            withLock hub (fun () ->
                match hub.session with
                | None -> None, []
                | Some s ->
                    let updated = Session.applySnapshot snapshot s
                    hub.session <- Some updated
                    let subs =
                        hub.clients
                        |> Seq.choose (fun kv -> kv.Value.subscriber)
                        |> List.ofSeq
                    Some snapshot, subs)
        // Build the wire-format StateMsg outside the state lock — the
        // conversion is non-trivial and we don't want to hold the lock
        // while N subscriber channels each take their own write.
        match wireSnapshot with
        | None -> ()
        | Some snap ->
            let m = StateMsg.empty()
            m.Snapshot <- WireConvert.fromCoreSnapshot snap
            for ch in subscribers do
                ch.Writer.TryWrite(m) |> ignore
            hub.snapshotBroadcaster.Push snap

    let snapshots (hub: Hub) : IObservable<Snapshot.GameStateSnapshot> =
        hub.snapshotBroadcaster :> IObservable<Snapshot.GameStateSnapshot>

    let togglePause (hub: Hub) : Result<unit, string> =
        withLock hub (fun () ->
            match hub.session with
            | None -> Error "no active session"
            | Some s ->
                hub.session <- Some (Session.togglePause s)
                Ok ())

    let stepSpeed (delta: decimal) (hub: Hub) : Result<unit, string> =
        withLock hub (fun () ->
            match hub.session with
            | None -> Error "no active session"
            | Some s ->
                hub.session <- Some (Session.stepSpeed delta s)
                Ok ())

    let proxyOutbound (hub: Hub) = hub.proxyOutbound

    let sendToProxy (command: CommandPipeline.Command) (hub: Hub) : unit =
        match hub.proxyOutbound with
        | Some ch -> ch.Writer.TryWrite(command) |> ignore
        | None -> ()

    let registerClient
        (id: ScriptingClientId)
        (peerVersion: Version)
        (at: DateTimeOffset)
        (hub: Hub)
        : Result<ClientChannel, ScriptingRoster.RosterError> =
        withLock hub (fun () ->
            match ScriptingRoster.tryAdd id peerVersion at hub.roster with
            | Error e -> Error e
            | Ok newRoster ->
                hub.roster <- newRoster
                let channel : ClientChannel =
                    { id = id
                      queue = CommandPipeline.createQueue hub.commandQueueCapacity
                      subscriber = None }
                hub.clients[id] <- channel
                hub.auditEmitter (Audit.AuditEvent.ClientConnected (at, id, peerVersion))
                Ok channel)

    let unregisterClient
        (id: ScriptingClientId)
        (reason: string)
        (at: DateTimeOffset)
        (hub: Hub)
        : unit =
        withLock hub (fun () ->
            match hub.clients.TryRemove(id) with
            | true, c ->
                c.subscriber
                |> Option.iter (fun ch -> ch.Writer.TryComplete() |> ignore)
                hub.roster <- ScriptingRoster.remove id hub.roster
                // Also free any slot bindings the client held.
                hub.slots <- hub.slots |> List.map (fun s ->
                    if s.boundClient = Some id then { s with boundClient = None } else s)
                hub.auditEmitter (Audit.AuditEvent.ClientDisconnected (at, id, reason))
            | false, _ -> ())

    let tryGetClient (id: ScriptingClientId) (hub: Hub) : ClientChannel option =
        match hub.clients.TryGetValue(id) with
        | true, c -> Some c
        | false, _ -> None

    let liveClients (hub: Hub) : ClientChannel list =
        hub.clients |> Seq.map (fun kv -> kv.Value) |> List.ofSeq

    let grantAdmin
        (id: ScriptingClientId)
        (by: string)
        (at: DateTimeOffset)
        (hub: Hub)
        : Result<unit, ScriptingRoster.RosterError> =
        withLock hub (fun () ->
            match ScriptingRoster.grantAdmin id hub.roster with
            | Error e -> Error e
            | Ok r ->
                hub.roster <- r
                hub.auditEmitter (Audit.AuditEvent.AdminGranted (at, id, by))
                Ok ())

    let revokeAdmin
        (id: ScriptingClientId)
        (by: string)
        (at: DateTimeOffset)
        (hub: Hub)
        : Result<unit, ScriptingRoster.RosterError> =
        withLock hub (fun () ->
            match ScriptingRoster.revokeAdmin id hub.roster with
            | Error e -> Error e
            | Ok r ->
                hub.roster <- r
                hub.auditEmitter (Audit.AuditEvent.AdminRevoked (at, id, by))
                Ok ())

    let bindSlot
        (id: ScriptingClientId)
        (slot: int)
        (hub: Hub)
        : Result<unit, ParticipantSlot.SingleWriterError> =
        withLock hub (fun () ->
            match ParticipantSlot.tryBind slot id hub.slots with
            | Error e -> Error e
            | Ok newSlots ->
                hub.slots <- newSlots
                Ok ())

    let unbindSlot (id: ScriptingClientId) (slot: int) (hub: Hub) : unit =
        withLock hub (fun () ->
            // Only unbind if the slot is currently held by this client.
            let target = hub.slots |> List.tryFind (fun s -> s.slotIndex = slot)
            match target with
            | Some s when s.boundClient = Some id ->
                hub.slots <- ParticipantSlot.unbind slot hub.slots
            | _ -> ())

    let private rosterError (e: ScriptingRoster.RosterError) =
        match e with
        | ScriptingRoster.NameInUse  -> "name in use"
        | ScriptingRoster.NotFound (ScriptingClientId n) -> sprintf "client %s not found" n

    let asCoreFacade (hub: Hub) : Session.CoreFacade =
        { new Session.CoreFacade with
            member _.Mode() = hub.mode
            member _.Roster() = hub.roster
            member _.Slots() = hub.slots
            member _.BrokerVersion() = hub.brokerVersion
            member _.OnProxyAttached(link) = attachProxy link hub |> ignore
            member _.OnProxyDetached(reason) = closeSession (Session.ProxyDisconnected reason) DateTimeOffset.UtcNow hub
            member _.OnSnapshot(snapshot) = applySnapshot snapshot hub
            member _.OnClientConnected(client) =
                // Already handled by registerClient; this hook is for outside callers.
                ignore client
            member _.OnClientDisconnected(id, reason) =
                unregisterClient id reason DateTimeOffset.UtcNow hub
            member _.OperatorOpenHost(config) =
                openHostSession config DateTimeOffset.UtcNow hub
            member _.OperatorLaunchHost() =
                launchHostSession DateTimeOffset.UtcNow hub
            member _.OperatorTogglePause() =
                togglePause hub
            member _.OperatorStepSpeed(delta) =
                stepSpeed delta hub
            member _.OperatorEndSession() =
                closeSession Session.OperatorTerminated DateTimeOffset.UtcNow hub
                Ok ()
            member _.OperatorGrantAdmin(id) =
                grantAdmin id "operator" DateTimeOffset.UtcNow hub
                |> Result.mapError rosterError
            member _.OperatorRevokeAdmin(id) =
                revokeAdmin id "operator" DateTimeOffset.UtcNow hub
                |> Result.mapError rosterError }
