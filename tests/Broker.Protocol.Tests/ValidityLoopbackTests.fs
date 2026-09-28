module Broker.Protocol.Tests.ValidityLoopbackTests

open System
open System.Collections.Concurrent
open System.Net
open System.Net.Sockets
open System.Threading
open Expecto
open Grpc.Core
open Grpc.Net.Client
open Broker.Core
open Broker.Protocol
open FSBarV2.Broker.Contracts
open Highbar.V1

let private freePort () =
    let listener = new TcpListener(IPAddress.Loopback, 0)
    listener.Start()
    let port = (listener.LocalEndpoint :?> IPEndPoint).Port
    listener.Stop()
    port

let private mkHello name =
    let version = ProtocolVersion.empty()
    version.Major <- 1u
    version.Minor <- 0u
    let request = HelloRequest.empty()
    request.ClientName <- name
    request.ClientVersion <- ValueSome version
    request

let private mkSnapshot seq frame includeUnit features =
    let snapshot = StateSnapshot.empty()
    snapshot.FrameNumber <- frame
    if includeUnit then
        let unit = OwnUnit.empty()
        unit.UnitId <- 7u
        unit.DefId <- 303u
        unit.TeamId <- 2
        let pos = Vector3.empty()
        pos.X <- 3.0f
        pos.Y <- 400.0f
        pos.Z <- -5.0f
        unit.Position <- ValueSome pos
        snapshot.OwnUnits.Add(unit)
    for id, kind, x, elevation, z in features do
        let feature = MapFeature.empty()
        feature.FeatureId <- id
        feature.DefId <- kind
        let pos = Vector3.empty()
        pos.X <- x
        pos.Y <- elevation
        pos.Z <- z
        feature.Position <- ValueSome pos
        snapshot.MapFeatures.Add(feature)
    let update = StateUpdate.empty()
    update.Seq <- seq
    update.Frame <- frame
    update.Snapshot <- snapshot
    update

let private mkNonemptyDelta seq frame =
    let event = DeltaEvent.empty()
    event.EconomyTick <- EconomyTickEvent.empty()
    let delta = StateDelta.empty()
    delta.Events.Add(event)
    let update = StateUpdate.empty()
    update.Seq <- seq
    update.Frame <- frame
    update.Delta <- delta
    update

let private readNext (call: AsyncServerStreamingCall<StateMsg>) (token: CancellationToken) =
    async {
        let! more = call.ResponseStream.MoveNext(token) |> Async.AwaitTask
        if not more then failtest "state stream closed unexpectedly"
        return call.ResponseStream.Current
    }

[<Tests>]
let validityLoopbackTests =
    testList "BARC-01 coordinator to scripting validity loopback" [
        test "wire admission accepts bounded distinct units and rejects malformed identities" {
            let mkCommand () =
                let command = Command.empty()
                command.CommandId <- Google.Protobuf.ByteString.CopyFrom(Guid.NewGuid().ToByteArray())
                command.OriginatingClient <- "watcher"
                command.SubmittedAtUnixMs <- DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
                let pause = AdminPayload.empty()
                pause.Pause <- Pause.empty()
                command.Admin <- pause
                command
            let malformedId = mkCommand ()
            malformedId.CommandId <- Google.Protobuf.ByteString.Empty
            match WireConvert.tryToCoreCommand malformedId with
            | Error (CommandPipeline.InvalidPayload _) -> ()
            | other -> failtestf "malformed UUID should reject: %A" other

            let command = mkCommand ()
            let order = UnitOrder.empty()
            order.UnitIds.Add(1u)
            order.UnitIds.Add(2u)
            order.Kind <- UnitOrder.Types.OrderKind.Move
            let target = Vec2.empty()
            target.X <- 1.0f
            target.Y <- 2.0f
            order.TargetPos <- ValueSome target
            let gameplay = GameplayPayload.empty()
            gameplay.UnitOrder <- order
            command.Gameplay <- gameplay
            match WireConvert.tryToCoreCommand command with
            | Ok { kind = CommandPipeline.Gameplay (CommandPipeline.UnitOrder ([1u; 2u], _, _, _)) } -> ()
            | other -> failtestf "bounded distinct multi-unit command should decode atomically: %A" other

            order.UnitIds.Add(2u)
            match WireConvert.tryToCoreCommand command with
            | Error (CommandPipeline.InvalidPayload _) -> ()
            | other -> failtestf "duplicate acting units should reject: %A" other

            let valid = mkCommand ()
            match WireConvert.tryToCoreCommand valid with
            | Ok { kind = CommandPipeline.Admin CommandPipeline.Pause } -> ()
            | other -> failtestf "valid pause should decode: %A" other
        }
        testAsync "Synthetic_BARC01 snapshot gap and unapplied-delta recovery stay fail closed" {
            let port = freePort()
            let audit = ConcurrentQueue<Audit.AuditEvent>()
            let options =
                { ServerHost.defaultOptions with
                    listenAddress = sprintf "127.0.0.1:%d" port }
            let! handle =
                ServerHost.start options (System.Version(1, 0)) audit.Enqueue CancellationToken.None
                |> Async.AwaitTask
            try
                use channel = GrpcChannel.ForAddress(sprintf "http://127.0.0.1:%d" port)
                let scripting = ScriptingClient.ScriptingClientClient(channel)
                let coordinator = HighBarCoordinator.HighBarCoordinatorClient(channel)
                let! _ = scripting.HelloAsync(mkHello "watcher").ResponseAsync |> Async.AwaitTask
                let subscribe = SubscribeRequest.empty()
                subscribe.ClientName <- "watcher"
                use stateCall = scripting.SubscribeStateAsync(subscribe)

                let heartbeat = HeartbeatRequest.empty()
                heartbeat.PluginId <- "synthetic-barc-01"
                heartbeat.SchemaVersion <- "1.0.0"
                let! _ = coordinator.HeartbeatAsync(heartbeat).ResponseAsync |> Async.AwaitTask
                use push = coordinator.PushStateAsync()
                use timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5.0))

                do!
                    push.RequestStream.WriteAsync(
                        mkSnapshot
                            1UL
                            1u
                            true
                            [ 7u, 101u, 11.0f, 999.0f, -13.0f
                              19u, 202u, -23.0f, 888.0f, 29.0f ])
                    |> Async.AwaitTask
                let! first = readNext stateCall timeout.Token
                let! initial =
                    match first.Body with
                    | ValueSome (StateMsg.Types.Body.Validity validity) ->
                        Expect.equal validity.Status StateValidity.Types.Status.Invalid "pre-baseline state is explicit"
                        readNext stateCall timeout.Token
                    | _ -> async.Return first
                match initial.Body with
                | ValueSome (StateMsg.Types.Body.Snapshot snapshot) ->
                    Expect.equal snapshot.Tick 1L "complete snapshot is current"
                    Expect.equal snapshot.Units.Count 1 "unit is retained"
                    Expect.equal snapshot.Units.[0].Id 7u "unit id is independent of feature ids"
                    Expect.equal snapshot.Features.Count 2 "both features reach scripting"
                    let firstFeature = snapshot.Features.[0]
                    Expect.equal firstFeature.Id 7u "feature may share id 7 with a unit"
                    Expect.equal firstFeature.Kind "101" "first feature kind is exact"
                    Expect.equal firstFeature.Pos.Value.X 11.0f "first feature X is exact"
                    Expect.equal firstFeature.Pos.Value.Y -13.0f "native Z maps to scripting Y"
                    let secondFeature = snapshot.Features.[1]
                    Expect.equal secondFeature.Id 19u "second feature id is exact"
                    Expect.equal secondFeature.Kind "202" "second feature kind is exact"
                    Expect.equal secondFeature.Pos.Value.X -23.0f "second feature X is asymmetric"
                    Expect.equal secondFeature.Pos.Value.Y 29.0f "second feature Z is asymmetric"
                | other -> failtestf "expected initial snapshot, got %A" other

                do! push.RequestStream.WriteAsync(mkNonemptyDelta 3UL 3u) |> Async.AwaitTask
                let! invalid = readNext stateCall timeout.Token
                match invalid.Body with
                | ValueSome (StateMsg.Types.Body.Validity validity) ->
                    Expect.equal validity.Status StateValidity.Types.Status.Invalid "gap invalidates"
                    Expect.equal validity.LastSeq 1UL "last contiguous sequence"
                    Expect.equal validity.ReceivedSeq 3UL "received sequence"
                | other -> failtestf "expected invalidation, got %A" other
                Expect.isTrue (BrokerState.telemetryGap handle.Hub) "current gap is set"
                BrokerState.clearTelemetryGap handle.Hub
                Expect.isTrue (BrokerState.telemetryGap handle.Hub) "invalidity cannot be acknowledged away"

                // Joining during invalidity must produce metadata rather than
                // replaying the cached tick-1 snapshot as current.
                let! _ = scripting.HelloAsync(mkHello "late-watcher").ResponseAsync |> Async.AwaitTask
                let lateSubscribe = SubscribeRequest.empty()
                lateSubscribe.ClientName <- "late-watcher"
                use lateCall = scripting.SubscribeStateAsync(lateSubscribe)
                let! lateInitial = readNext lateCall timeout.Token
                match lateInitial.Body with
                | ValueSome (StateMsg.Types.Body.Validity validity) ->
                    Expect.equal validity.Status StateValidity.Types.Status.Invalid "late join sees invalidity"
                | other -> failtestf "late join received cached state: %A" other

                // The server-side control gate protects old readers that do
                // not understand the additive validity arm.
                use submit = scripting.SubmitCommandsAsync()
                let command = Command.empty()
                command.CommandId <- Google.Protobuf.ByteString.CopyFrom(Guid.NewGuid().ToByteArray())
                command.OriginatingClient <- "watcher"
                command.SubmittedAtUnixMs <- DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
                let pause = AdminPayload.empty()
                pause.Pause <- Pause.empty()
                command.Admin <- pause
                do! submit.RequestStream.WriteAsync(command) |> Async.AwaitTask
                let! hasAck = submit.ResponseStream.MoveNext(timeout.Token) |> Async.AwaitTask
                Expect.isTrue hasAck "invalid-state command gets an acknowledgement"
                let ack = submit.ResponseStream.Current
                Expect.isFalse ack.Accepted "invalid state blocks command admission"
                match ack.Reject with
                | ValueSome reject ->
                    Expect.equal reject.Code Reject.Types.Code.InvalidPayload "control refusal code"
                    Expect.stringContains reject.Detail "telemetry baseline is invalid" "actionable refusal"
                | ValueNone -> failtest "expected command rejection detail"

                do! push.RequestStream.WriteAsync(mkSnapshot 4UL 4u false []) |> Async.AwaitTask
                let! recovered = readNext stateCall timeout.Token
                let! lateRecovered = readNext lateCall timeout.Token
                for message in [ recovered; lateRecovered ] do
                    match message.Body with
                    | ValueSome (StateMsg.Types.Body.Snapshot snapshot) ->
                        Expect.equal snapshot.Tick 4L "new full baseline recovers"
                        Expect.equal snapshot.Features.Count 0 "empty snapshot replaces old features"
                        Expect.equal snapshot.Units.Count 0 "empty snapshot replaces old units independently"
                    | other -> failtestf "expected recovered snapshot, got %A" other
                Expect.isFalse (BrokerState.telemetryGap handle.Hub) "recovery clears current gap"
                Expect.isTrue (BrokerState.telemetryValid handle.Hub) "recovery marks state current"
                Expect.isTrue
                    (audit.ToArray()
                     |> Array.exists (function
                         | Audit.AuditEvent.CoordinatorStateGap (_, "synthetic-barc-01", 1UL, 3UL) -> true
                         | _ -> false))
                    "gap remains in audit history"

                do! push.RequestStream.WriteAsync(mkNonemptyDelta 5UL 5u) |> Async.AwaitTask
                let! unapplied = readNext stateCall timeout.Token
                match unapplied.Body with
                | ValueSome (StateMsg.Types.Body.Validity validity) ->
                    Expect.stringContains validity.Detail "does not materialize" "unapplied delta reason"
                | other -> failtestf "expected unapplied-delta invalidation, got %A" other
                do!
                    push.RequestStream.WriteAsync(
                        mkSnapshot 6UL 6u false [ 31u, 404u, 37.0f, 777.0f, -41.0f ])
                    |> Async.AwaitTask
                let! recoveredAgain = readNext stateCall timeout.Token
                match recoveredAgain.Body with
                | ValueSome (StateMsg.Types.Body.Snapshot snapshot) ->
                    Expect.equal snapshot.Tick 6L "second full baseline recovers"
                    Expect.equal snapshot.Features.Count 1 "replacement feature set is current"
                    Expect.equal snapshot.Features.[0].Id 31u "old feature ids do not survive replacement"
                    Expect.equal snapshot.Features.[0].Kind "404" "replacement kind is exact"
                    Expect.equal snapshot.Features.[0].Pos.Value.X 37.0f "replacement X is exact"
                    Expect.equal snapshot.Features.[0].Pos.Value.Y -41.0f "replacement Z maps to scripting Y"
                | other -> failtestf "expected second recovery, got %A" other
                Expect.isTrue
                    (audit.ToArray()
                     |> Array.exists (function
                         | Audit.AuditEvent.CoordinatorStateInvalidated (_, "synthetic-barc-01", 4UL, 5UL, _) -> true
                         | _ -> false))
                    "unapplied delta remains in audit history"
            finally
                (handle :> IAsyncDisposable).DisposeAsync().AsTask().Wait()
        }
    ]
    |> testSequenced
