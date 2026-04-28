module Broker.Integration.Tests.Sc005RecoveryTests

open System
open System.Collections.Concurrent
open System.Net
open System.Net.Sockets
open System.Diagnostics
open System.Threading
open System.Threading.Tasks
open Expecto
open Grpc.Net.Client
open Broker.Core
open Broker.Protocol
open FSBarV2.Broker.Contracts

let private freePort () =
    let l = new TcpListener(IPAddress.Loopback, 0)
    l.Start()
    let p = (l.LocalEndpoint :?> IPEndPoint).Port
    l.Stop()
    p

let private startCapturing port =
    let events = ConcurrentQueue<Audit.AuditEvent>()
    let opts = { ServerHost.defaultOptions with listenAddress = sprintf "127.0.0.1:%d" port }
    let task = ServerHost.start opts (System.Version(1, 0)) (fun ev -> events.Enqueue(ev)) CancellationToken.None
    task.Wait()
    task.Result, events

[<Tests>]
let recoveryTests =
    testList "SC-005 disconnect recovery" [

        testAsync "Detect+notify ≤ 5 s and recover-to-idle ≤ 10 s in ≥ 95% of 20 trials" {
            let trials = 20
            let detectBudgetMs = 5000L
            let recoverBudgetMs = 10000L
            let okThreshold = (trials * 95) / 100   // 19 / 20

            let detects = ResizeArray<int64>()
            let recovers = ResizeArray<int64>()

            let port = freePort()
            // One server, many trials — exercises the close+reattach path.
            let handle, events = startCapturing port
            try
                use channel = GrpcChannel.ForAddress(sprintf "http://127.0.0.1:%d" port)

                for trial in 1 .. trials do
                    events.Clear()
                    // 1. Subscribe a watcher so we can observe SessionEnd fan-out.
                    let scClient = new ScriptingClient.ScriptingClientClient(channel)
                    let helloReq = HelloRequest.empty()
                    helloReq.ClientName <- sprintf "watcher-%d" trial
                    let pv = ProtocolVersion.empty()
                    pv.Major <- 1u
                    pv.Minor <- 0u
                    helloReq.ClientVersion <- ValueSome pv
                    let! _ = scClient.HelloAsync(helloReq).ResponseAsync |> Async.AwaitTask
                    let subReq = SubscribeRequest.empty()
                    subReq.ClientName <- sprintf "watcher-%d" trial
                    use stateCall = scClient.SubscribeStateAsync(subReq)

                    // 2. Attach a synthetic coordinator and push one snapshot
                    //    so a session is fully Active.
                    let! proxy =
                        SyntheticCoordinator.connect channel (sprintf "coord-%d" trial) "1.0.0"
                        |> Async.AwaitTask
                    do! proxy.PushSnapshotAsync (fun _ -> ()) |> Async.AwaitTask

                    // Drain the initial snapshot so MoveNext on the next
                    // round lands exactly on the SessionEnd or stream-close.
                    use primeCts = new CancellationTokenSource(TimeSpan.FromSeconds(2.0))
                    try
                        let! _ = stateCall.ResponseStream.MoveNext(primeCts.Token) |> Async.AwaitTask
                        ()
                    with _ -> ()

                    // 3. Drop the proxy mid-stream and stopwatch.
                    let sw = Stopwatch.StartNew()
                    do! proxy.DropAsync() |> Async.AwaitTask

                    // 4. Detection = subscriber sees stream-end OR SessionEnd.
                    use detCts = new CancellationTokenSource(TimeSpan.FromSeconds(8.0))
                    let mutable detected = false
                    while not detected && not detCts.IsCancellationRequested do
                        try
                            let! more = stateCall.ResponseStream.MoveNext(detCts.Token) |> Async.AwaitTask
                            if not more then detected <- true
                            else
                                let cur = stateCall.ResponseStream.Current
                                match cur.Body with
                                | ValueSome (StateMsg.Types.Body.SessionEnd _) -> detected <- true
                                | _ -> ()
                        with _ -> detected <- true
                    detects.Add(sw.ElapsedMilliseconds)

                    // 5. Recovery = broker back to Idle (proxyOutbound = None).
                    let mutable recovered = BrokerState.coordinatorCommandChannel handle.Hub = None
                    while not recovered && sw.ElapsedMilliseconds < recoverBudgetMs do
                        do! Task.Delay(50) |> Async.AwaitTask
                        recovered <- BrokerState.coordinatorCommandChannel handle.Hub = None
                    recovers.Add(sw.ElapsedMilliseconds)

                    // 6. Cleanup the watcher so the next trial's name is free.
                    (proxy :> IDisposable).Dispose()
                    BrokerState.unregisterClient
                        (ScriptingClientId (sprintf "watcher-%d" trial))
                        "trial complete"
                        DateTimeOffset.UtcNow
                        handle.Hub
            finally
                (handle :> IAsyncDisposable).DisposeAsync().AsTask().Wait()

            let detectOk =
                detects |> Seq.filter (fun ms -> ms <= detectBudgetMs) |> Seq.length
            let recoverOk =
                recovers |> Seq.filter (fun ms -> ms <= recoverBudgetMs) |> Seq.length
            let detectMax = if detects.Count > 0 then Seq.max detects else 0L
            let recoverMax = if recovers.Count > 0 then Seq.max recovers else 0L

            printfn
                "[SC-005] trials=%d detect-ok=%d/%d (max=%dms) recover-ok=%d/%d (max=%dms)"
                trials detectOk trials detectMax recoverOk trials recoverMax

            Expect.isGreaterThanOrEqual detectOk okThreshold
                (sprintf "detection ≤ %dms in ≥ %d / %d trials (saw %d, max %dms)"
                    detectBudgetMs okThreshold trials detectOk detectMax)
            Expect.isGreaterThanOrEqual recoverOk okThreshold
                (sprintf "recovery ≤ %dms in ≥ %d / %d trials (saw %d, max %dms)"
                    recoverBudgetMs okThreshold trials recoverOk recoverMax)
        }
    ]
    |> testSequenced
