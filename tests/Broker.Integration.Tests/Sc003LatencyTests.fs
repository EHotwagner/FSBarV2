module Broker.Integration.Tests.Sc003LatencyTests

open System
open System.Net
open System.Net.Sockets
open System.Diagnostics
open System.Threading
open System.Threading.Tasks
open Expecto
open Grpc.Net.Client
open Broker.Protocol
open FSBarV2.Broker.Contracts

let private freePort () =
    let l = new TcpListener(IPAddress.Loopback, 0)
    l.Start()
    let p = (l.LocalEndpoint :?> IPEndPoint).Port
    l.Stop()
    p

let private startServer port =
    let opts = { ServerHost.defaultOptions with listenAddress = sprintf "127.0.0.1:%d" port }
    let task = ServerHost.start opts (System.Version(1, 0)) (fun _ -> ()) CancellationToken.None
    task.Wait()
    task.Result

let private p95 (sorted: int64 array) : int64 =
    if sorted.Length = 0 then 0L
    else
        let idx = max 0 (int (float sorted.Length * 0.95) - 1)
        sorted.[min idx (sorted.Length - 1)]

[<Tests>]
let latencyTests =
    testList "SC-003 snapshot latency" [

        testAsync "p95 proxy->subscriber wall-clock latency is <= 1 s over 500 snapshots" {
            let totalSnapshots = 500
            let port = freePort()
            let handle = startServer port
            try
                use channel = GrpcChannel.ForAddress(sprintf "http://127.0.0.1:%d" port)

                let scClient = new ScriptingClient.ScriptingClientClient(channel)
                let helloReq = HelloRequest.empty()
                helloReq.ClientName <- "latency-bot"
                let pv = ProtocolVersion.empty()
                pv.Major <- 1u
                pv.Minor <- 0u
                helloReq.ClientVersion <- ValueSome pv
                let! _ = scClient.HelloAsync(helloReq).ResponseAsync |> Async.AwaitTask

                let subReq = SubscribeRequest.empty()
                subReq.ClientName <- "latency-bot"
                use stateCall = scClient.SubscribeStateAsync(subReq)

                let! proxy = SyntheticCoordinator.connect channel "lat-coord" "1.0.0" |> Async.AwaitTask
                use _ = proxy

                // Per-tick send timestamps (us we can do diff against the
                // wall-clock when the subscriber sees them). Using a simple
                // dictionary keyed on `tick`.
                let sentAt = System.Collections.Generic.Dictionary<int64, int64>()
                let stopwatch = Stopwatch.StartNew()

                // Reader task: drains every StateMsg from the subscriber
                // stream, records arrival time per tick, exits when we have
                // all `totalSnapshots`.
                let recvLatencies = ResizeArray<int64>()
                let readerCts = new CancellationTokenSource()
                let readerTask =
                    task {
                        try
                            while not readerCts.IsCancellationRequested
                                  && recvLatencies.Count < totalSnapshots do
                                let! more = stateCall.ResponseStream.MoveNext(readerCts.Token)
                                if not more then return ()
                                let cur = stateCall.ResponseStream.Current
                                match cur.Body with
                                | ValueSome (StateMsg.Types.Body.Snapshot s) ->
                                    let recvMs = stopwatch.ElapsedMilliseconds
                                    let ok, sent = sentAt.TryGetValue(s.Tick)
                                    if ok then recvLatencies.Add(recvMs - sent)
                                | _ -> ()
                        with :? OperationCanceledException -> ()
                    } :> Task

                // Writer: push `totalSnapshots` snapshots at ~30 Hz cadence
                // (~33 ms apart) to mirror real-game tick rate. Hammering
                // the gRPC client-stream back-to-back saturates HTTP/2 flow
                // control under loopback and inflates the per-tick latency
                // the budget measures. SyntheticCoordinator auto-increments
                // the StateUpdate.Frame which becomes the broker's Tick;
                // for a tight `for tick in 1..N` loop the auto-frame matches
                // `tick` exactly.
                for tick in 1L .. int64 totalSnapshots do
                    sentAt.[tick] <- stopwatch.ElapsedMilliseconds
                    do! proxy.PushSnapshotAsync (fun _ -> ()) |> Async.AwaitTask
                    do! Task.Delay(33) |> Async.AwaitTask

                // Wait for the reader to drain (5s safety budget).
                // 30 Hz pacing of 500 pushes = ~16.5 s push window; allow
                // headroom for the reader to drain.
                use waitCts = new CancellationTokenSource(TimeSpan.FromSeconds(25.0))
                while recvLatencies.Count < totalSnapshots
                      && not waitCts.IsCancellationRequested do
                    do! Task.Delay(10) |> Async.AwaitTask
                readerCts.Cancel()
                try do! readerTask |> Async.AwaitTask with _ -> ()

                Expect.isGreaterThanOrEqual
                    recvLatencies.Count
                    (totalSnapshots * 95 / 100)
                    "must receive at least 95% of snapshots within the wait budget"

                let sorted = recvLatencies.ToArray() |> Array.sort
                let p95Latency = p95 sorted
                let maxLatency = if sorted.Length > 0 then sorted.[sorted.Length - 1] else 0L
                printfn "[SC-003] received=%d/%d  p95=%dms  max=%dms"
                    recvLatencies.Count totalSnapshots p95Latency maxLatency
                Expect.isLessThanOrEqual p95Latency 1000L
                    (sprintf "SC-003: p95 latency must be <= 1000 ms (saw %dms over %d samples)"
                        p95Latency recvLatencies.Count)
            finally
                (handle :> IAsyncDisposable).DisposeAsync().AsTask().Wait()
        }
    ]
    |> testSequenced
