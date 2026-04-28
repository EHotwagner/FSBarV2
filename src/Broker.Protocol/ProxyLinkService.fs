namespace Broker.Protocol

open System
open System.Threading.Tasks
open Grpc.Core
open Broker.Core
open FSBarV2.Broker.Contracts

module ProxyLinkService =

    type Service =
        { hub: BrokerState.Hub
          mutable attached: int }   // 0 = no proxy, 1 = proxy attached

    let create (hub: BrokerState.Hub) : Service =
        { hub = hub; attached = 0 }

    let isAttached (service: Service) : bool =
        System.Threading.Volatile.Read(&service.attached) <> 0

    let detach (service: Service) (reason: string) : unit =
        // closeSession also broadcasts SessionEnd to all subscribers and
        // emits the audit event chain.
        BrokerState.closeSession (Session.ProxyDisconnected reason) DateTimeOffset.UtcNow service.hub
        System.Threading.Interlocked.Exchange(&service.attached, 0) |> ignore

    let private writeAck (response: IServerStreamWriter<ProxyServerMsg>) (msg: ProxyServerMsg) : Task =
        response.WriteAsync(msg)

    let private writeRejectAndClose
        (response: IServerStreamWriter<ProxyServerMsg>)
        (reason: CommandPipeline.RejectReason)
        (brokerVersion: Version) =
        let r = WireConvert.toReject reason None (Some brokerVersion)
        let m = ProxyServerMsg.empty()
        m.Reject <- r
        writeAck response m

    let internal handleAttach
        (service: Service)
        (request: IAsyncStreamReader<ProxyClientMsg>)
        (response: IServerStreamWriter<ProxyServerMsg>)
        (context: ServerCallContext)
        : Task =
        task {
            let hub = service.hub
            let brokerVersion = BrokerState.brokerVersion hub
            let auditEmit = BrokerState.auditEmitter hub

            // Step 1 — read the first message; it MUST be a Handshake.
            let! gotFirst = request.MoveNext(context.CancellationToken)
            if not gotFirst then
                return ()
            else
                let first = request.Current
                match first.Body with
                | ValueSome (ProxyClientMsg.Types.Body.Handshake hs) ->
                    let peerVersion = WireConvert.toCoreVersionOpt hs.ProxyVersion
                    match VersionHandshake.check brokerVersion peerVersion with
                    | Error _ ->
                        auditEmit (Audit.AuditEvent.VersionMismatchRejected (DateTimeOffset.UtcNow, "proxy", peerVersion))
                        do! writeRejectAndClose response (CommandPipeline.VersionMismatch (brokerVersion, peerVersion)) brokerVersion
                        return ()
                    | Ok () ->
                        let now = DateTimeOffset.UtcNow
                        let link : Session.ProxyAiLink =
                            { attachedAt = now
                              protocolVersion = peerVersion
                              lastSnapshotAt = None
                              keepAliveIntervalMs = 2000 }
                        match BrokerState.attachProxy link hub with
                        | Error reason ->
                            do! writeRejectAndClose response (CommandPipeline.InvalidPayload reason) brokerVersion
                            return ()
                        | Ok () ->
                            System.Threading.Interlocked.Exchange(&service.attached, 1) |> ignore
                            // Send HandshakeAck.
                            let ack = HandshakeAck.empty()
                            ack.BrokerVersion <- ValueSome (WireConvert.fromCoreVersion brokerVersion)
                            let sid =
                                BrokerState.session hub
                                |> Option.map Session.id
                                |> Option.defaultValue Guid.Empty
                            ack.SessionId <- Google.Protobuf.ByteString.CopyFrom(sid.ToByteArray())
                            ack.KeepaliveIntervalMs <- uint32 link.keepAliveIntervalMs
                            let ackMsg = ProxyServerMsg.empty()
                            ackMsg.HandshakeAck <- ack
                            do! writeAck response ackMsg

                            // Step 2 — start a background drain of the proxy outbound channel.
                            let outbound = BrokerState.proxyOutbound hub
                            let drainTask =
                                match outbound with
                                | None -> Task.CompletedTask
                                | Some ch ->
                                    task {
                                        let reader = ch.Reader
                                        try
                                            let mutable keepGoing = true
                                            while keepGoing && not context.CancellationToken.IsCancellationRequested do
                                                let! more = reader.WaitToReadAsync(context.CancellationToken).AsTask()
                                                if not more then
                                                    keepGoing <- false
                                                else
                                                    let mutable cmd = Unchecked.defaultof<CommandPipeline.Command>
                                                    while reader.TryRead(&cmd) do
                                                        let wireCmd = WireConvert.fromCoreCommand cmd
                                                        let m = ProxyServerMsg.empty()
                                                        m.Command <- wireCmd
                                                        do! response.WriteAsync(m)
                                        with
                                        | :? OperationCanceledException -> ()
                                    } :> Task

                            // Step 3 — read inbound proxy messages until EOF/cancel.
                            let mutable continueReading = true
                            try
                                while continueReading do
                                    let! more = request.MoveNext(context.CancellationToken)
                                    if not more then
                                        continueReading <- false
                                    else
                                        let cur = request.Current
                                        match cur.Body with
                                        | ValueSome (ProxyClientMsg.Types.Body.Snapshot s) ->
                                            let snap = WireConvert.toCoreSnapshot s
                                            BrokerState.applySnapshot snap hub
                                        | ValueSome (ProxyClientMsg.Types.Body.Ping p) ->
                                            let pong = KeepAlivePong.empty()
                                            pong.EchoAtUnixMs <- p.SentAtUnixMs
                                            let m = ProxyServerMsg.empty()
                                            m.Pong <- pong
                                            do! response.WriteAsync(m)
                                        | ValueSome (ProxyClientMsg.Types.Body.SessionEnd se) ->
                                            let reason =
                                                match se.Reason with
                                                | SessionEnd.Types.Reason.Victory            -> Session.Victory
                                                | SessionEnd.Types.Reason.Defeat             -> Session.Defeat
                                                | SessionEnd.Types.Reason.OperatorTerminated -> Session.OperatorTerminated
                                                | SessionEnd.Types.Reason.GameCrashed        -> Session.GameCrashed
                                                | SessionEnd.Types.Reason.ProxyDisconnected  -> Session.ProxyDisconnected se.Detail
                                                | _                                          -> Session.OperatorTerminated
                                            BrokerState.closeSession reason DateTimeOffset.UtcNow hub
                                            continueReading <- false
                                        | ValueSome (ProxyClientMsg.Types.Body.Handshake _) ->
                                            // Second handshake on an already-attached stream — ignore.
                                            ()
                                        | ValueNone -> ()
                            with
                            | :? OperationCanceledException -> ()
                            | _ -> ()      // any wire-level read failure is treated as proxy drop

                            // Signal the outbound drain task to stop so its
                            // WaitToReadAsync returns false. Without this the
                            // drainTask hangs and closeSession never fires
                            // (FR-026 detection budget would be infinite).
                            match outbound with
                            | Some ch -> ch.Writer.TryComplete() |> ignore
                            | None -> ()

                            try do! drainTask with _ -> ()
                            // Stream closed — emit ProxyDetached + reset.
                            BrokerState.closeSession
                                (Session.ProxyDisconnected "stream closed")
                                DateTimeOffset.UtcNow
                                hub
                            auditEmit (Audit.AuditEvent.ProxyDetached (DateTimeOffset.UtcNow, "stream closed"))
                            System.Threading.Interlocked.Exchange(&service.attached, 0) |> ignore
                            return ()
                | _ ->
                    // First message wasn't a handshake — reject.
                    auditEmit (Audit.AuditEvent.ProxyDetached (DateTimeOffset.UtcNow, "handshake_missing"))
                    do! writeRejectAndClose response (CommandPipeline.InvalidPayload "first message must be Handshake") brokerVersion
                    return ()
        } :> Task

    type Impl(service: Service) =
        inherit ProxyLink.ProxyLinkBase()
        override _.Attach request response context =
            handleAttach service request response context
