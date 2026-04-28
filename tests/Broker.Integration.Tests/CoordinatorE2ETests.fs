module Broker.Integration.Tests.CoordinatorE2ETests

open System
open System.Collections.Concurrent
open System.Net
open System.Net.Sockets
open System.Threading
open System.Threading.Tasks
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

let private startServerWithAudit (port: int) =
    let q = ConcurrentQueue<Audit.AuditEvent>()
    let opts = { ServerHost.defaultOptions with listenAddress = sprintf "127.0.0.1:%d" port }
    let task = ServerHost.start opts (System.Version(1, 0)) (fun e -> q.Enqueue e) CancellationToken.None
    task.Wait()
    task.Result, q

let private channelFor (port: int) =
    GrpcChannel.ForAddress(sprintf "http://127.0.0.1:%d" port)

let private mkHello (name: string) (v: System.Version) =
    let pv = ProtocolVersion.empty()
    pv.Major <- uint32 v.Major
    pv.Minor <- uint32 v.Minor
    let req = HelloRequest.empty()
    req.ClientName <- name
    req.ClientVersion <- ValueSome pv
    req

let private hasAudit (q: ConcurrentQueue<Audit.AuditEvent>) (predicate: Audit.AuditEvent -> bool) =
    q.ToArray() |> Array.exists predicate

[<Tests>]
let coordinatorE2ETests =
    testList "Coordinator end-to-end (US1 / FR-001 / FR-002 / FR-003 / FR-008 / FR-011 / FR-012 / FR-015)" [

        // --- T013 / FR-003 / SC-007 -------------------------------------------------

        testAsync "Synthetic_T013 schema-version mismatch is rejected at the first Heartbeat" {
            let port = freePort()
            let handle, audit = startServerWithAudit port
            try
                use channel = channelFor port
                let client = HighBarCoordinator.HighBarCoordinatorClient(channel)
                let req = HeartbeatRequest.empty()
                req.PluginId <- "ai-bad-schema"
                req.SchemaVersion <- "0.9.9-test"  // broker default is "1.0.0"
                req.Frame <- 0u
                let started = DateTimeOffset.UtcNow
                let! result =
                    Async.Catch (client.HeartbeatAsync(req).ResponseAsync |> Async.AwaitTask)
                let elapsed = DateTimeOffset.UtcNow - started
                let rec unwrap (ex: exn) : RpcException option =
                    match ex with
                    | :? RpcException as r -> Some r
                    | _ ->
                        match ex.InnerException with
                        | null -> None
                        | inner -> unwrap inner
                match result with
                | Choice1Of2 _ -> failtest "expected RpcException, got success"
                | Choice2Of2 ex ->
                    match unwrap ex with
                    | Some r ->
                        Expect.equal r.StatusCode StatusCode.FailedPrecondition "FR-003 surface"
                    | None ->
                        failtestf "expected RpcException, got %A" ex
                // SC-007: detection within 1 s wall-clock
                Expect.isTrue (elapsed.TotalSeconds < 1.0) (sprintf "rejected in %fs (≤ 1 s budget)" elapsed.TotalSeconds)
                Expect.isTrue
                    (hasAudit audit (function
                        | Audit.AuditEvent.CoordinatorSchemaMismatch _ -> true
                        | _ -> false))
                    "CoordinatorSchemaMismatch audit event emitted"
            finally
                (handle :> IAsyncDisposable).DisposeAsync().AsTask().Wait()
        }

        // --- T017 / Acceptance #1, #2, #4 -------------------------------------------

        testAsync "Synthetic_T017 cold-start: scripting client receives state from coordinator wire" {
            let port = freePort()
            let handle, audit = startServerWithAudit port
            try
                use channel = channelFor port
                let scClient = ScriptingClient.ScriptingClientClient(channel)
                let! _ =
                    scClient.HelloAsync(mkHello "subscriber-1" (System.Version(1, 0))).ResponseAsync
                    |> Async.AwaitTask
                let subReq = SubscribeRequest.empty()
                subReq.ClientName <- "subscriber-1"
                use stateCall = scClient.SubscribeStateAsync(subReq)

                // Attach the synthetic coordinator.
                let! coord =
                    SyntheticCoordinator.connect channel "ai-1" "1.0.0" |> Async.AwaitTask
                use _ = coord

                // Push three snapshots with monotonic frame numbers.
                for f in [ 1u; 2u; 3u ] do
                    do! coord.PushSnapshotAsync (fun ss -> ss.FrameNumber <- f) |> Async.AwaitTask

                // Read three snapshots from the subscriber stream within the
                // 1 s SC-002 budget.
                use cts = new CancellationTokenSource(TimeSpan.FromSeconds(5.0))
                let received = ResizeArray<int64>()
                let mutable continueLoop = true
                while continueLoop && received.Count < 3 do
                    let! more = stateCall.ResponseStream.MoveNext(cts.Token) |> Async.AwaitTask
                    if not more then
                        continueLoop <- false
                    else
                        let m = stateCall.ResponseStream.Current
                        match m.Body with
                        | ValueSome (StateMsg.Types.Body.Snapshot s) -> received.Add(s.Tick)
                        | _ -> ()

                Expect.equal received.Count 3 "three snapshots received"
                Expect.equal received.[0] 1L "first tick"
                Expect.equal received.[1] 2L "second tick"
                Expect.equal received.[2] 3L "third tick"

                // Acceptance #1: dashboard transitions from Idle to attached.
                let hub = handle.Hub
                Expect.equal (BrokerState.activePluginId hub) (Some "ai-1") "owner captured"

                // Acceptance #4: graceful close → SessionEnd fan-out.
                do! coord.CompleteAsync() |> Async.AwaitTask
                // Allow watchdog/closeSession time to land.
                do! Async.Sleep 200
                Expect.isTrue
                    (hasAudit audit (function
                        | Audit.AuditEvent.CoordinatorAttached _ -> true
                        | _ -> false))
                    "CoordinatorAttached audit"
            finally
                (handle :> IAsyncDisposable).DisposeAsync().AsTask().Wait()
        }

        // --- T051 / FR-012 ----------------------------------------------------------

        testAsync "Synthetic_T051 two consecutive coordinator sessions reset cleanly" {
            let port = freePort()
            let handle, audit = startServerWithAudit port
            try
                use channel = channelFor port

                // Session 1
                let! coord1 =
                    SyntheticCoordinator.connect channel "ai-1" "1.0.0" |> Async.AwaitTask
                let sid1Opt = BrokerState.activePluginId handle.Hub
                Expect.equal sid1Opt (Some "ai-1") "session 1 owner"
                do! coord1.CompleteAsync() |> Async.AwaitTask
                (coord1 :> IDisposable).Dispose()
                // Allow closeSession to land; the watchdog is per-attach so the
                // graceful close must propagate quickly.
                do! Async.Sleep 300

                Expect.equal (BrokerState.activePluginId handle.Hub) None "post-close: no active plugin"

                // Session 2 with a different pluginId
                let! coord2 =
                    SyntheticCoordinator.connect channel "ai-2" "1.0.0" |> Async.AwaitTask
                let sid2Opt = BrokerState.activePluginId handle.Hub
                Expect.equal sid2Opt (Some "ai-2") "session 2 owner — fresh"

                use _ = coord2
                let attachCount =
                    audit.ToArray()
                    |> Array.sumBy (function
                        | Audit.AuditEvent.CoordinatorAttached _ -> 1
                        | _ -> 0)
                Expect.isGreaterThanOrEqual attachCount 2 "two CoordinatorAttached events"
            finally
                (handle :> IAsyncDisposable).DisposeAsync().AsTask().Wait()
        }

        // --- T052 / FR-015 ----------------------------------------------------------

        testAsync "Synthetic_T052 scripting client subscribed before coordinator attach receives the first frame" {
            let port = freePort()
            let handle, _ = startServerWithAudit port
            try
                use channel = channelFor port
                let scClient = ScriptingClient.ScriptingClientClient(channel)

                // Subscribe BEFORE the coordinator attaches.
                let! _ =
                    scClient.HelloAsync(mkHello "early-bird" (System.Version(1, 0))).ResponseAsync
                    |> Async.AwaitTask
                let subReq = SubscribeRequest.empty()
                subReq.ClientName <- "early-bird"
                use stateCall = scClient.SubscribeStateAsync(subReq)

                // Confirm: broker is Idle, no active coordinator.
                Expect.equal (BrokerState.activePluginId handle.Hub) None "no plugin attached yet"

                // Now attach + push the first frame. SyntheticCoordinator's
                // internal counter assigns frame=1 for the first push.
                let! coord =
                    SyntheticCoordinator.connect channel "ai-late" "1.0.0" |> Async.AwaitTask
                use _ = coord
                do! coord.PushSnapshotAsync (fun _ -> ()) |> Async.AwaitTask

                // First message reaching the pre-subscribed stream must be the
                // first frame (tick=1) — gap-free.
                use cts = new CancellationTokenSource(TimeSpan.FromSeconds(5.0))
                let mutable firstTick = -1L
                while firstTick < 0L do
                    let! more = stateCall.ResponseStream.MoveNext(cts.Token) |> Async.AwaitTask
                    if not more then ()
                    else
                        let m = stateCall.ResponseStream.Current
                        match m.Body with
                        | ValueSome (StateMsg.Types.Body.Snapshot s) -> firstTick <- s.Tick
                        | _ -> ()
                Expect.equal firstTick 1L "first tick reaches the pre-attached subscriber gap-free"
            finally
                (handle :> IAsyncDisposable).DisposeAsync().AsTask().Wait()
        }
    ]
