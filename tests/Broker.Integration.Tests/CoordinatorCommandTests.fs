// SYNTHETIC FIXTURE: command-egress + backpressure tests for US2 against
// the loopback `SyntheticCoordinator`. The broker-side wire path is real
// production code; only the plugin peer is synthetic. Real-game closure
// for these scenarios lands in T036a (operator host-mode walkthrough).
module Broker.Integration.Tests.CoordinatorCommandTests

open System
open System.Collections.Concurrent
open System.Net
open System.Net.Sockets
open System.Threading
open System.Threading.Tasks
open Expecto
open Grpc.Net.Client
open Broker.Core
open Broker.Protocol
open FSBarV2.Broker.Contracts
open Highbar.V1

let private freePort () =
    let l = new TcpListener(IPAddress.Loopback, 0)
    l.Start()
    let p = (l.LocalEndpoint :?> IPEndPoint).Port
    l.Stop()
    p

let private startServerWithAudit (port: int) =
    let q = ConcurrentQueue<Audit.AuditEvent>()
    let opts = { ServerHost.defaultOptions with listenAddress = sprintf "127.0.0.1:%d" port }
    let task = ServerHost.start opts (System.Version(1, 0)) (fun e -> q.Enqueue e) CancellationToken.None
    task.Wait()
    task.Result, q

let private channelFor (port: int) =
    GrpcChannel.ForAddress(sprintf "http://127.0.0.1:%d" port)

let private mkHello (name: string) =
    let pv = ProtocolVersion.empty()
    pv.Major <- 1u
    pv.Minor <- 0u
    let req = HelloRequest.empty()
    req.ClientName <- name
    req.ClientVersion <- ValueSome pv
    req

let private waitFor (cond: unit -> bool) (timeoutMs: int) : bool =
    let deadline = DateTime.UtcNow.AddMilliseconds(float timeoutMs)
    let mutable ok = cond()
    while not ok && DateTime.UtcNow < deadline do
        Thread.Sleep 25
        ok <- cond()
    ok

let private heartbeat (client: HighBarCoordinator.HighBarCoordinatorClient) pluginId =
    let request = HeartbeatRequest.empty()
    request.PluginId <- pluginId
    request.SchemaVersion <- "1.0.0"
    request.Frame <- 0u
    client.HeartbeatAsync(request).ResponseAsync

let private openCommandStream (client: HighBarCoordinator.HighBarCoordinatorClient) pluginId token =
    let request = CommandChannelSubscribe.empty()
    request.PluginId <- pluginId
    client.OpenCommandChannelAsync(request, cancellationToken = token)

let private openedCount (audit: ConcurrentQueue<Audit.AuditEvent>) =
    audit.ToArray()
    |> Array.sumBy (function Audit.CoordinatorCommandChannelOpened _ -> 1 | _ -> 0)

let private closedCount (audit: ConcurrentQueue<Audit.AuditEvent>) =
    audit.ToArray()
    |> Array.sumBy (function Audit.CoordinatorCommandChannelClosed _ -> 1 | _ -> 0)

let private mkMoveCommandWire (clientName: string) (unitId: uint32) (x: float32) (y: float32) =
    let cmd = FSBarV2.Broker.Contracts.Command.empty()
    cmd.CommandId <- Google.Protobuf.ByteString.CopyFrom((Guid.NewGuid()).ToByteArray())
    cmd.OriginatingClient <- clientName
    cmd.SubmittedAtUnixMs <- DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
    let pos = FSBarV2.Broker.Contracts.Vec2.empty()
    pos.X <- x
    pos.Y <- y
    let order = FSBarV2.Broker.Contracts.UnitOrder.empty()
    order.Kind <- FSBarV2.Broker.Contracts.UnitOrder.Types.OrderKind.Move
    order.UnitIds.Add(unitId)
    order.TargetPos <- ValueSome pos
    let gameplay = FSBarV2.Broker.Contracts.GameplayPayload.empty()
    gameplay.UnitOrder <- order
    cmd.Gameplay <- gameplay
    cmd

[<Tests>]
let coordinatorCommandTests =
    testSequenced <| testList "Coordinator command egress + backpressure (US2 / FR-005 / FR-010)" [

        // --- T028 / Acceptance #1, #2 of US2 -----------------------------------------

        testAsync "Synthetic_T028 operator Pause + scripting-client Move both reach the coordinator wire" {
            let port = freePort()
            let handle, audit = startServerWithAudit port
            try
                use channel = channelFor port

                // Coordinator attaches first; opens a Guest session.
                let! coord =
                    SyntheticCoordinator.connect channel "ai-cmd" "1.0.0" |> Async.AwaitTask
                use _ = coord
                Expect.isTrue
                    (waitFor (fun () -> BrokerState.activePluginId handle.Hub = Some "ai-cmd") 2000)
                    "coordinator attached"

                // --- Path 1: operator Pause via the CoreFacade dispatch (T031).
                let facade = BrokerState.asCoreFacade handle.Hub
                let pauseResult = facade.OperatorTogglePause()
                Expect.equal pauseResult (Ok ()) "OperatorTogglePause Ok"

                // --- Path 2: scripting-client gameplay command, simulated by
                // calling BrokerState.sendToCoordinator directly. (The
                // SubmitCommands → BackpressureGate → sendToProxy fan-in is
                // already covered by 001 ScriptingClientEndToEndTests; this
                // test is about the coordinator wire-out path.)
                let moveCmd : CommandPipeline.Command =
                    { commandId = Guid.NewGuid()
                      originatingClient = ScriptingClientId "alice-bot"
                      targetSlot = Some 0
                      kind =
                        CommandPipeline.Gameplay
                            (CommandPipeline.UnitOrder
                                ([99u], CommandPipeline.Move, Some { x = 50.0f; y = 75.0f }, None))
                      submittedAt = DateTimeOffset.UtcNow }
                BrokerState.sendToCoordinator moveCmd handle.Hub |> ignore

                // --- Drain OpenCommandChannel; verify both arrived.
                let stream =
                    match coord.CommandStream with
                    | Some s -> s
                    | None -> failtest "coordinator command stream missing"

                let mutable sawPause = false
                let mutable sawMove = false
                use cts = new CancellationTokenSource(TimeSpan.FromSeconds(5.0))
                while not (sawPause && sawMove) do
                    let! more = stream.MoveNext(cts.Token) |> Async.AwaitTask
                    if not more then
                        failtest "command stream closed before both batches arrived"
                    let batch = stream.Current
                    for ai in batch.Commands do
                        match ai.Command with
                        | ValueSome (AICommand.Types.Command.PauseTeam p) ->
                            Expect.isTrue p.Enable "Pause -> enable=true"
                            sawPause <- true
                        | ValueSome (AICommand.Types.Command.MoveUnit mu) ->
                            Expect.equal mu.UnitId 99 "moved unit id"
                            sawMove <- true
                        | _ -> ()

                Expect.isTrue sawPause "operator Pause arrived as PauseTeamCommand"
                Expect.isTrue sawMove "scripting Move arrived as MoveUnitCommand"

                // FR-009 audit lifecycle.
                Expect.isTrue
                    (audit.ToArray()
                     |> Array.exists (function
                         | Audit.AuditEvent.CoordinatorCommandChannelOpened _ -> true
                         | _ -> false))
                    "CoordinatorCommandChannelOpened audit"
            finally
                (handle :> IAsyncDisposable).DisposeAsync().AsTask().Wait()
        }

        testAsync "BARC-01.1e scripting ACK admits one atomic parent and emits ordered child batches" {
            let port = freePort()
            let handle, _ = startServerWithAudit port
            try
                use channel = channelFor port
                let scripting = ScriptingClient.ScriptingClientClient(channel)
                let clientId = ScriptingClientId "multi-bot"
                let lobby : Lobby.LobbyConfig =
                    { mapName = "Tabula"
                      gameMode = "Skirmish"
                      participants =
                        [ { slotIndex = 1; kind = ParticipantSlot.ProxyAi; team = 0; boundClient = Some clientId } ]
                      display = Lobby.Headless }
                BrokerState.openHostSession lobby DateTimeOffset.UtcNow handle.Hub
                |> function Ok () -> () | Error error -> failtest error
                let! _ = scripting.HelloAsync(mkHello "multi-bot").ResponseAsync |> Async.AwaitTask
                BrokerState.launchHostSession DateTimeOffset.UtcNow handle.Hub
                |> function Ok () -> () | Error error -> failtest error
                let! coordinator = SyntheticCoordinator.connect channel "multi-coordinator" "1.0.0" |> Async.AwaitTask
                use _ = coordinator

                use submit = scripting.SubmitCommandsAsync()
                let command = mkMoveCommandWire "multi-bot" 8u 17.0f 19.0f
                command.TargetSlot <- 1
                let order = command.Gameplay.UnitOrder
                order.UnitIds.Add(2u)
                order.UnitIds.Add(5u)
                do! submit.RequestStream.WriteAsync(command) |> Async.AwaitTask
                use timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5.0))
                let! hasAck = submit.ResponseStream.MoveNext(timeout.Token) |> Async.AwaitTask
                Expect.isTrue hasAck "command acknowledgement returned"
                Expect.isFalse submit.ResponseStream.Current.Accepted "pre-baseline parent is refused atomically"
                match submit.ResponseStream.Current.Reject with
                | ValueSome reject -> Expect.stringContains reject.Detail "baseline is invalid or missing" "initial validity fence"
                | ValueNone -> failtest "pre-baseline refusal detail missing"

                let sessionId = BrokerState.session handle.Hub |> Option.map Session.id |> Option.get
                BrokerState.applySnapshot
                    { sessionId = sessionId; tick = 1L; capturedAt = DateTimeOffset.UtcNow
                      players = []; units = []; buildings = []; features = []; mapMeta = None }
                    handle.Hub
                do! submit.RequestStream.WriteAsync(command) |> Async.AwaitTask
                let! hasAcceptedAck = submit.ResponseStream.MoveNext(timeout.Token) |> Async.AwaitTask
                Expect.isTrue hasAcceptedAck "post-baseline acknowledgement returned"
                Expect.isTrue submit.ResponseStream.Current.Accepted "whole parent was admitted after baseline"

                let stream = coordinator.CommandStream |> Option.defaultWith (fun () -> failtest "command stream missing")
                let received = ResizeArray<CommandBatch>()
                while received.Count < 3 do
                    let! more = stream.MoveNext(timeout.Token) |> Async.AwaitTask
                    if not more then failtest "command stream closed before every child"
                    received.Add stream.Current
                Expect.sequenceEqual (received |> Seq.map _.TargetUnitId) [8u; 2u; 5u] "one ordered batch per acting unit"
                Expect.equal (received |> Seq.map _.BatchSeq |> Set.ofSeq |> Set.count) 3 "child sequences are distinct"
                Expect.equal
                    (received |> Seq.map (fun batch -> batch.ClientCommandId |> ValueOption.defaultValue 0UL) |> Set.ofSeq |> Set.count)
                    3
                    "child correlations are distinct"
            finally
                (handle :> IAsyncDisposable).DisposeAsync().AsTask().Wait()
        }

        testAsync "idle command RPC cancellation releases its lease and a replacement carries commands" {
            let port = freePort()
            let handle, audit = startServerWithAudit port
            try
                use channel = channelFor port
                let client = HighBarCoordinator.HighBarCoordinatorClient(channel)
                let! _ = heartbeat client "cancel-reconnect" |> Async.AwaitTask
                use firstCts = new CancellationTokenSource()
                use first = openCommandStream client "cancel-reconnect" firstCts.Token
                Expect.isTrue (waitFor (fun () -> openedCount audit = 1) 2000) "first reader acquired its lease"
                firstCts.Cancel()
                Expect.isTrue
                    (waitFor (fun () -> closedCount audit = 1 && not (BrokerState.hasCoordinatorCommandChannel handle.Hub)) 2000)
                    "idle cancellation closed and drained the claimed channel"

                use secondCts = new CancellationTokenSource(TimeSpan.FromSeconds(5.0))
                use second = openCommandStream client "cancel-reconnect" secondCts.Token
                Expect.isTrue (waitFor (fun () -> openedCount audit = 2) 2000) "replacement reader acquired a fresh channel"
                let command : CommandPipeline.Command =
                    { commandId = Guid.NewGuid(); originatingClient = ScriptingClientId "operator"
                      targetSlot = None; kind = CommandPipeline.Admin CommandPipeline.Pause
                      submittedAt = DateTimeOffset.UtcNow }
                Expect.equal (BrokerState.sendToCoordinator command handle.Hub) (Ok ()) "replacement command admitted"
                let! more = second.ResponseStream.MoveNext(secondCts.Token) |> Async.AwaitTask
                Expect.isTrue more "replacement stream received a batch"
                Expect.equal second.ResponseStream.Current.BatchSeq 1UL "first session sequence was retained across reader renewal"
            finally
                (handle :> IAsyncDisposable).DisposeAsync().AsTask().Wait()
        }

        testAsync "normal command channel completion permits a fresh reader in the live session" {
            let port = freePort()
            let handle, audit = startServerWithAudit port
            try
                use channel = channelFor port
                let client = HighBarCoordinator.HighBarCoordinatorClient(channel)
                let! _ = heartbeat client "normal-reconnect" |> Async.AwaitTask
                use firstCts = new CancellationTokenSource(TimeSpan.FromSeconds(5.0))
                use first = openCommandStream client "normal-reconnect" firstCts.Token
                Expect.isTrue (waitFor (fun () -> openedCount audit = 1) 2000) "first reader acquired its lease"
                let sessionId = BrokerState.session handle.Hub |> Option.map Session.id |> Option.get
                BrokerState.completeCoordinatorCommandChannel sessionId "fixture normal completion" handle.Hub
                Expect.isTrue (waitFor (fun () -> closedCount audit = 1) 2000) "server observed normal channel completion"

                use secondCts = new CancellationTokenSource(TimeSpan.FromSeconds(5.0))
                use second = openCommandStream client "normal-reconnect" secondCts.Token
                Expect.isTrue (waitFor (fun () -> openedCount audit = 2) 2000) "fresh reader renewed the live session channel"
                let command : CommandPipeline.Command =
                    { commandId = Guid.NewGuid(); originatingClient = ScriptingClientId "operator"
                      targetSlot = None; kind = CommandPipeline.Admin CommandPipeline.Resume
                      submittedAt = DateTimeOffset.UtcNow }
                Expect.equal (BrokerState.sendToCoordinator command handle.Hub) (Ok ()) "command admitted after normal renewal"
                let! more = second.ResponseStream.MoveNext(secondCts.Token) |> Async.AwaitTask
                Expect.isTrue more "renewed stream received a batch"
                Expect.equal second.ResponseStream.Current.BatchSeq 1UL "session cursor did not skip on empty channel renewal"
            finally
                (handle :> IAsyncDisposable).DisposeAsync().AsTask().Wait()
        }

        // --- T029 / FR-010 backpressure carry-forward --------------------------------
        // Drives the BackpressureGate directly to verify the per-client
        // queue's QUEUE_FULL semantics still hold under the new wire. The
        // SubmitCommands → BackpressureGate fan-in is already covered by
        // 001 ScriptingClientEndToEndTests / AdminElevationTests; this test
        // focuses on the queue + reject contract that FR-010 carries forward.

        testAsync "Synthetic_T029 BackpressureGate rejects with QueueFull when the per-client queue overflows" {
            // No gRPC server needed: BackpressureGate + per-client Queue is
            // a pure-domain seam. Set up a host session with a slot bound to
            // the spammer so authorise() permits the gameplay command, then
            // overrun the small-capacity queue and assert the FR-010
            // carry-forward shape.
            let id = ScriptingClientId "spammer"
            let lobby : Lobby.LobbyConfig =
                { mapName = "Tabula"
                  gameMode = "Skirmish"
                  participants =
                    [ { slotIndex = 0; kind = ParticipantSlot.Human; team = 0; boundClient = Some id } ]
                  display = Lobby.Headless }
            let hub =
                BrokerState.create (System.Version(1, 0)) 64 (fun _ -> ())
            BrokerState.openHostSession lobby DateTimeOffset.UtcNow hub
            |> function Ok _ -> () | Error e -> failtestf "openHostSession: %s" e
            BrokerState.registerClient id (System.Version(1, 0)) DateTimeOffset.UtcNow hub
            |> function Ok _ -> () | Error e -> failtestf "registerClient: %A" e

            let queue = CommandPipeline.createQueue 4
            let gate = BackpressureGate.create queue

            let mkCmd () : CommandPipeline.Command =
                { commandId = Guid.NewGuid()
                  originatingClient = id
                  targetSlot = Some 0
                  kind =
                    CommandPipeline.Gameplay
                        (CommandPipeline.UnitOrder
                            ([1u], CommandPipeline.Move, Some { x = 0.0f; y = 0.0f }, None))
                  submittedAt = DateTimeOffset.UtcNow }

            let mutable accepted = 0
            let mutable queueFullSeen = false
            for _ in 1 .. 8 do
                let result =
                    BackpressureGate.process_
                        gate
                        (BrokerState.mode hub)
                        (BrokerState.roster hub)
                        (BrokerState.slots hub)
                        (mkCmd())
                if result.accepted then accepted <- accepted + 1
                match result.reject with
                | Some CommandPipeline.QueueFull -> queueFullSeen <- true
                | _ -> ()

            Expect.isTrue queueFullSeen "FR-010 carry-forward: at least one QueueFull on the coordinator path"
            Expect.isLessThanOrEqual accepted 4 "no more than queue capacity accepted"
        }
    ]
