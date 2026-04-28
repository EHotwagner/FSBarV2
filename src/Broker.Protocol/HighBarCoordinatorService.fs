namespace Broker.Protocol

open System
open System.Threading
open System.Threading.Tasks
open Broker.Core
open Highbar.V1

module HighBarCoordinatorService =

    type Config =
        { expectedSchemaVersion: string
          ownerRule: BrokerState.OwnerRule
          heartbeatTimeoutMs: int }

    let defaultConfig : Config =
        { expectedSchemaVersion = "1.0.0"
          ownerRule = BrokerState.FirstAttached
          heartbeatTimeoutMs = 5000 }

    type Service =
        { hub: BrokerState.Hub
          config: Config
          mutable attached: bool
          // Reset per attach. Set to a fresh CTS when an attach completes;
          // cancellation triggers closeSession + stream tear-down.
          mutable sessionCts: CancellationTokenSource option
          // Sequencing for outbound CommandBatch.batch_seq.
          mutable nextBatchSeq: uint64
          stateLock: obj }

    let create (hub: BrokerState.Hub) (config: Config) : Service =
        BrokerState.setExpectedSchemaVersion config.expectedSchemaVersion hub
        BrokerState.setOwnerRule config.ownerRule hub
        { hub = hub
          config = config
          attached = false
          sessionCts = None
          nextBatchSeq = 1UL
          stateLock = obj() }

    let isAttached (service: Service) : bool = service.attached

    let private withLock (service: Service) (f: unit -> 'a) : 'a =
        lock service.stateLock f

    let private detachInternal (service: Service) (reason: string) : unit =
        let cts =
            withLock service (fun () ->
                let cts = service.sessionCts
                service.sessionCts <- None
                service.attached <- false
                cts)
        match cts with
        | Some cts ->
            try cts.Cancel() with _ -> ()
            cts.Dispose()
        | None -> ()
        let pid =
            BrokerState.activePluginId service.hub
            |> Option.defaultValue ""
        BrokerState.closeSession (Session.ProxyDisconnected reason) DateTimeOffset.UtcNow service.hub
        service.hub
        |> BrokerState.auditEmitter
        |> fun emit -> emit (Audit.AuditEvent.CoordinatorDetached (DateTimeOffset.UtcNow, pid, reason))

    let detach (service: Service) (reason: string) : unit =
        detachInternal service reason

    /// Background heartbeat watchdog. Wakes every 500 ms; if the broker
    /// hasn't seen a Heartbeat or accepted StateUpdate within
    /// `heartbeatTimeoutMs`, force-closes the coordinator session.
    /// Implemented as an async loop bound to the per-attach CTS so the
    /// watchdog dies with the session it was watching (no zombie loops).
    let private startHeartbeatWatchdog (service: Service) (cts: CancellationTokenSource) : unit =
        let token = cts.Token
        Task.Run(fun () ->
            task {
                let interval = TimeSpan.FromMilliseconds(500.0)
                let timeout = TimeSpan.FromMilliseconds(float service.config.heartbeatTimeoutMs)
                while not token.IsCancellationRequested do
                    try
                        do! Task.Delay(interval, token)
                    with :? OperationCanceledException -> ()
                    if not token.IsCancellationRequested then
                        let last = BrokerState.lastHeartbeatAt service.hub
                        if last <> DateTimeOffset.MinValue then
                            let elapsed = DateTimeOffset.UtcNow - last
                            if elapsed > timeout then
                                detachInternal service "heartbeat-timeout"
            } :> Task)
        |> ignore

    let private rpcException (code: Grpc.Core.StatusCode) (detail: string) =
        Grpc.Core.RpcException(Grpc.Core.Status(code, detail))

    type Impl(service: Service) =
        inherit HighBarCoordinator.HighBarCoordinatorBase()

        override _.Heartbeat
            (request: HeartbeatRequest)
            (context: Grpc.Core.ServerCallContext)
            : Task<HeartbeatResponse> =
            ignore context
            task {
                let now = DateTimeOffset.UtcNow
                let received = request.SchemaVersion
                let expected = service.config.expectedSchemaVersion
                // FR-003: strict-equality schema check
                if received <> expected then
                    BrokerState.auditEmitter service.hub
                        (Audit.AuditEvent.CoordinatorSchemaMismatch (now, expected, received, request.PluginId))
                    raise (rpcException
                        Grpc.Core.StatusCode.FailedPrecondition
                        (sprintf "schema mismatch expected=%s received=%s" expected received))
                // First Heartbeat for this session: attach the coordinator link.
                let needsAttach =
                    withLock service (fun () ->
                        if not service.attached then
                            service.attached <- true
                            true
                        else false)
                if needsAttach then
                    let link : Session.ProxyAiLink =
                        { attachedAt = now
                          protocolVersion = Version(1, 0)
                          lastSnapshotAt = None
                          keepAliveIntervalMs = service.config.heartbeatTimeoutMs
                          pluginId = request.PluginId
                          schemaVersion = received
                          engineSha256 = request.EngineSha256
                          lastHeartbeatAt = now
                          lastSeq = 0UL }
                    match BrokerState.attachCoordinator link service.hub with
                    | Error e ->
                        withLock service (fun () -> service.attached <- false)
                        raise (rpcException
                            Grpc.Core.StatusCode.Internal
                            (sprintf "attachCoordinator failed: %s" e))
                    | Ok () ->
                        let cts = new CancellationTokenSource()
                        withLock service (fun () -> service.sessionCts <- Some cts)
                        startHeartbeatWatchdog service cts
                // FR-011 owner check + liveness refresh
                match BrokerState.noteHeartbeat request.PluginId now service.hub with
                | Error (CommandPipeline.NotOwner (attempted, owner)) ->
                    raise (rpcException
                        Grpc.Core.StatusCode.PermissionDenied
                        (sprintf "not owner attempted=%s owner=%s" attempted owner))
                | Error r ->
                    raise (rpcException
                        Grpc.Core.StatusCode.Internal
                        (sprintf "heartbeat rejected: %A" r))
                | Ok () -> ()
                let resp = HeartbeatResponse.empty()
                resp.CoordinatorId <- "fsbar-broker"
                resp.EchoedFrame <- request.Frame
                resp.SchemaVersion <- expected
                return resp
            }

        override _.PushState
            (requestStream: Grpc.Core.IAsyncStreamReader<StateUpdate>)
            (context: Grpc.Core.ServerCallContext)
            : Task<PushAck> =
            task {
                let mutable view = WireConvert.emptyRunningView
                let mutable msgCount = 0UL
                let mutable maxSeq = 0UL
                try
                    let mutable continueLoop = true
                    while continueLoop do
                        let! more = requestStream.MoveNext(context.CancellationToken)
                        if not more then
                            continueLoop <- false
                        else
                            let upd = requestStream.Current
                            let now = DateTimeOffset.UtcNow
                            let view', result = WireConvert.applyHighBarStateUpdate upd view
                            view <- view'
                            msgCount <- msgCount + 1UL
                            if upd.Seq > maxSeq then maxSeq <- upd.Seq
                            // Liveness: every accepted StateUpdate refreshes
                            // the heartbeat clock so the watchdog does not
                            // false-trip during steady streaming.
                            let pid =
                                BrokerState.activePluginId service.hub
                                |> Option.defaultValue ""
                            match result with
                            | WireConvert.NewSnapshot snap ->
                                BrokerState.applySnapshot snap service.hub
                                BrokerState.refreshLiveness now service.hub
                            | WireConvert.Gap (l, r) ->
                                BrokerState.noteStateGap pid l r now service.hub
                                BrokerState.refreshLiveness now service.hub
                            | WireConvert.KeepAliveOnly ->
                                BrokerState.refreshLiveness now service.hub
                with
                | :? OperationCanceledException -> ()
                // Stream completed: plugin signalled it's done streaming
                // state. Treat as graceful disconnect — fan out SessionEnd
                // and return to Idle (FR-008 acceptance scenario 4).
                if not context.CancellationToken.IsCancellationRequested then
                    detachInternal service "graceful-close"
                let ack = PushAck.empty()
                ack.MessagesReceived <- msgCount
                ack.MaxSeqSeen <- maxSeq
                ack.CoordinatorId <- "fsbar-broker"
                return ack
            }

        override _.OpenCommandChannel
            (request: CommandChannelSubscribe)
            (responseStream: Grpc.Core.IServerStreamWriter<CommandBatch>)
            (context: Grpc.Core.ServerCallContext)
            : Task =
            ignore request
            task {
                let pid =
                    BrokerState.activePluginId service.hub
                    |> Option.defaultValue ""
                BrokerState.auditEmitter service.hub
                    (Audit.AuditEvent.CoordinatorCommandChannelOpened (DateTimeOffset.UtcNow, pid))
                let closeReason =
                    try
                        // Drain the broker's coordinator command channel and
                        // marshal each Core Command to a HighBar CommandBatch.
                        // Loops until the gRPC context is cancelled or the
                        // channel is completed.
                        let rec drain () =
                            task {
                                if context.CancellationToken.IsCancellationRequested then
                                    return "cancelled"
                                else
                                    match BrokerState.coordinatorCommandChannel service.hub with
                                    | None ->
                                        // No active session yet — wait briefly.
                                        do! Task.Delay(100, context.CancellationToken)
                                        return! drain ()
                                    | Some ch ->
                                        let! ok = ch.Reader.WaitToReadAsync(context.CancellationToken).AsTask()
                                        if not ok then
                                            return "channel-completed"
                                        else
                                            let mutable cmd = Unchecked.defaultof<_>
                                            while ch.Reader.TryRead(&cmd) do
                                                let seq =
                                                    withLock service (fun () ->
                                                        let s = service.nextBatchSeq
                                                        service.nextBatchSeq <- s + 1UL
                                                        s)
                                                match WireConvert.tryFromCoreCommandToHighBar cmd seq with
                                                | Ok batch ->
                                                    do! responseStream.WriteAsync(batch)
                                                | Error reason ->
                                                    BrokerState.auditEmitter service.hub
                                                        (Audit.AuditEvent.CommandRejected
                                                            (DateTimeOffset.UtcNow,
                                                             cmd.originatingClient,
                                                             cmd.commandId,
                                                             reason))
                                            return! drain ()
                            }
                        let task = drain ()
                        task.Result
                    with
                    | :? OperationCanceledException -> "cancelled"
                    | ex -> sprintf "error: %s" ex.Message
                BrokerState.auditEmitter service.hub
                    (Audit.AuditEvent.CoordinatorCommandChannelClosed (DateTimeOffset.UtcNow, pid, closeReason))
            } :> Task
