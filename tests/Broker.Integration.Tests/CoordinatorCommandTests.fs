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
                BrokerState.sendToCoordinator moveCmd handle.Hub

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
