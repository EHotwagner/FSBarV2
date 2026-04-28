namespace Broker.Protocol

open System
open System.Threading.Channels
open System.Threading.Tasks
open Grpc.Core
open Broker.Core
open FSBarV2.Broker.Contracts

module ScriptingClientService =

    type Service = { hub: BrokerState.Hub }

    let create (hub: BrokerState.Hub) : Service = { hub = hub }

    let connectedCount (service: Service) : int =
        BrokerState.liveClients service.hub |> List.length

    let broadcastSessionEnd (service: Service) (reason: Session.EndReason) : unit =
        BrokerState.closeSession reason DateTimeOffset.UtcNow service.hub

    // ---- internal RPC handlers ------------------------------------------

    let internal handleHello
        (hub: BrokerState.Hub)
        (request: HelloRequest)
        (_context: ServerCallContext)
        : Task<HelloReply> =
        task {
            let brokerVersion = BrokerState.brokerVersion hub
            let auditEmit = BrokerState.auditEmitter hub
            let now = DateTimeOffset.UtcNow
            let peerVersion = WireConvert.toCoreVersionOpt request.ClientVersion
            match VersionHandshake.check brokerVersion peerVersion with
            | Error _ ->
                auditEmit (Audit.AuditEvent.VersionMismatchRejected (now, "scripting-client", peerVersion))
                raise (
                    RpcException(
                        Status(StatusCode.FailedPrecondition,
                            sprintf "VERSION_MISMATCH broker=%O peer=%O" brokerVersion peerVersion)))
                return Unchecked.defaultof<HelloReply>     // unreachable, satisfies type checker
            | Ok () ->
                let id = ScriptingClientId request.ClientName
                if String.IsNullOrWhiteSpace request.ClientName then
                    raise (RpcException(Status(StatusCode.InvalidArgument, "client_name must be non-empty")))
                    return Unchecked.defaultof<HelloReply>
                else
                    match BrokerState.registerClient id peerVersion now hub with
                    | Error ScriptingRoster.NameInUse ->
                        auditEmit (Audit.AuditEvent.NameInUseRejected (now, request.ClientName))
                        raise (RpcException(Status(StatusCode.AlreadyExists, sprintf "NAME_IN_USE: %s" request.ClientName)))
                        return Unchecked.defaultof<HelloReply>
                    | Error other ->
                        raise (RpcException(Status(StatusCode.Internal, sprintf "%A" other)))
                        return Unchecked.defaultof<HelloReply>
                    | Ok _ ->
                        let reply = HelloReply.empty()
                        reply.BrokerVersion <- ValueSome (WireConvert.fromCoreVersion brokerVersion)
                        let sid =
                            BrokerState.session hub
                            |> Option.map Session.id
                            |> Option.defaultValue Guid.Empty
                        reply.SessionId <- Google.Protobuf.ByteString.CopyFrom(sid.ToByteArray())
                        reply.IsAdmin <- false
                        return reply
        }

    let internal handleBindSlot
        (hub: BrokerState.Hub)
        (request: BindSlotRequest)
        (_context: ServerCallContext)
        : Task<BindSlotReply> =
        task {
            let id = ScriptingClientId request.ClientName
            let reply = BindSlotReply.empty()
            match BrokerState.bindSlot id request.SlotIndex hub with
            | Ok () ->
                reply.Ok <- true
                return reply
            | Error e ->
                reply.Ok <- false
                let reason =
                    match e with
                    | ParticipantSlot.SlotAlreadyBound (s, owner) ->
                        CommandPipeline.SlotNotOwned (s, Some owner)
                    | ParticipantSlot.SlotNotProxyAi s ->
                        CommandPipeline.SlotNotOwned (s, None)
                reply.Reject <- ValueSome (WireConvert.toReject reason None None)
                return reply
        }

    let internal handleUnbindSlot
        (hub: BrokerState.Hub)
        (request: UnbindSlotRequest)
        (_context: ServerCallContext)
        : Task<UnbindSlotReply> =
        task {
            let id = ScriptingClientId request.ClientName
            BrokerState.unbindSlot id request.SlotIndex hub
            let reply = UnbindSlotReply.empty()
            reply.Ok <- true
            return reply
        }

    let internal handleSubscribeState
        (hub: BrokerState.Hub)
        (request: SubscribeRequest)
        (response: IServerStreamWriter<StateMsg>)
        (context: ServerCallContext)
        : Task =
        task {
            let id = ScriptingClientId request.ClientName
            match BrokerState.tryGetClient id hub with
            | None ->
                raise (RpcException(Status(StatusCode.NotFound, "client not registered; call Hello first")))
            | Some client ->
                let opts =
                    BoundedChannelOptions(64,
                        FullMode = BoundedChannelFullMode.Wait,
                        SingleReader = true,
                        SingleWriter = false)
                let ch = Channel.CreateBounded<StateMsg>(opts)
                client.subscriber <- Some ch

                // First message: current snapshot if any. Subsequent
                // messages flow through the broadcast hook (see below).
                match BrokerState.session hub |> Option.bind (fun s -> (Session.toReading DateTimeOffset.UtcNow s).telemetry) with
                | Some snap ->
                    let m = StateMsg.empty()
                    m.Snapshot <- WireConvert.fromCoreSnapshot snap
                    do! response.WriteAsync(m)
                | None -> ()

                try
                    try
                        while not context.CancellationToken.IsCancellationRequested do
                            let! more = ch.Reader.WaitToReadAsync(context.CancellationToken).AsTask()
                            if not more then return ()
                            let mutable msg = Unchecked.defaultof<StateMsg>
                            while ch.Reader.TryRead(&msg) do
                                do! response.WriteAsync(msg)
                    with
                    | :? OperationCanceledException -> ()
                finally
                    client.subscriber <- None
        } :> Task

    let internal handleSubmitCommands
        (hub: BrokerState.Hub)
        (request: IAsyncStreamReader<Command>)
        (response: IServerStreamWriter<CommandAck>)
        (context: ServerCallContext)
        : Task =
        task {
            try
                while! request.MoveNext(context.CancellationToken) do
                    let wire = request.Current
                    let cmd = WireConvert.toCoreCommand wire
                    let id = cmd.originatingClient
                    match BrokerState.tryGetClient id hub with
                    | None ->
                        // Out-of-band: writer didn't call Hello.
                        let ack = CommandAck.empty()
                        ack.CommandId <- wire.CommandId
                        ack.Accepted <- false
                        ack.Reject <-
                            ValueSome (
                                WireConvert.toReject
                                    (CommandPipeline.InvalidPayload "client not registered; call Hello first")
                                    (Some cmd.commandId)
                                    None)
                        do! response.WriteAsync(ack)
                    | Some client ->
                        let gate = BackpressureGate.create client.queue
                        let result =
                            BackpressureGate.process_
                                gate
                                (BrokerState.mode hub)
                                (BrokerState.roster hub)
                                (BrokerState.slots hub)
                                cmd
                        let ack = CommandAck.empty()
                        ack.CommandId <- wire.CommandId
                        ack.Accepted <- result.accepted
                        match result.reject with
                        | Some r ->
                            (BrokerState.auditEmitter hub) (
                                Audit.AuditEvent.CommandRejected (
                                    DateTimeOffset.UtcNow, id, cmd.commandId, r))
                            ack.Reject <- ValueSome (WireConvert.toReject r (Some cmd.commandId) None)
                        | None ->
                            // Forward to proxy if attached; otherwise it stays
                            // in the per-client queue and the next drain picks it up.
                            BrokerState.sendToCoordinator cmd hub
                        do! response.WriteAsync(ack)
            with
            | :? OperationCanceledException -> ()
        } :> Task

    type Impl(service: Service) =
        inherit ScriptingClient.ScriptingClientBase()
        override _.Hello request context =
            handleHello service.hub request context
        override _.BindSlot request context =
            handleBindSlot service.hub request context
        override _.UnbindSlot request context =
            handleUnbindSlot service.hub request context
        override _.SubscribeState request response context =
            handleSubscribeState service.hub request response context
        override _.SubmitCommands request response context =
            handleSubmitCommands service.hub request response context
