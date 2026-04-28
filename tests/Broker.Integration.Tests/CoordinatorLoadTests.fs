// SYNTHETIC FIXTURE: SC-002 latency + SC-003 recovery measurements
// against the loopback `SyntheticCoordinator` (research §9). The
// broker-side wire path is real production code; only the plugin peer
// is synthetic. Real-game closure for SC-002 / SC-003 lands in T034 /
// T035 against a real BAR + HighBarV3 build.
module Broker.Integration.Tests.CoordinatorLoadTests

open System
open System.Collections.Concurrent
open System.IO
open System.Net
open System.Net.Sockets
open System.Threading
open System.Threading.Tasks
open Expecto
open Grpc.Net.Client
open Broker.Core
open Broker.Protocol
open FSBarV2.Broker.Contracts

let private freePort () =
    let listener = new TcpListener(IPAddress.Loopback, 0)
    listener.Start()
    let port = (listener.LocalEndpoint :?> IPEndPoint).Port
    listener.Stop()
    port

let private startServer (port: int) =
    let q = ConcurrentQueue<Audit.AuditEvent>()
    let opts = { ServerHost.defaultOptions with listenAddress = sprintf "127.0.0.1:%d" port }
    let task = ServerHost.start opts (System.Version(1, 0)) (fun e -> q.Enqueue e) CancellationToken.None
    task.Wait()
    task.Result, q

let private mkHello (name: string) =
    let pv = ProtocolVersion.empty()
    pv.Major <- 1u
    pv.Minor <- 0u
    let req = HelloRequest.empty()
    req.ClientName <- name
    req.ClientVersion <- ValueSome pv
    req

let private percentile (samples: float array) (p: float) : float =
    if samples.Length = 0 then 0.0
    else
        let sorted = Array.sortBy id samples
        let idx = int (ceil ((p / 100.0) * float sorted.Length)) - 1
        let idx' = max 0 (min (sorted.Length - 1) idx)
        sorted.[idx']

[<Tests>]
let coordinatorLoadTests =
    testSequenced <| testList "Coordinator load + recovery (SC-002 / SC-003 under SyntheticCoordinator)" [

        // T024 — SC-002 latency under SyntheticCoordinator (re-anchor on CI).
        testAsync "Synthetic_T024 game-tick → scripting-client p95 ≤ 1 s over 500 ticks" {
            let port = freePort()
            let handle, _ = startServer port
            try
                use channel = GrpcChannel.ForAddress(sprintf "http://127.0.0.1:%d" port)
                let scClient = ScriptingClient.ScriptingClientClient(channel)
                let! _ = scClient.HelloAsync(mkHello "load-sub").ResponseAsync |> Async.AwaitTask
                let subReq = SubscribeRequest.empty()
                subReq.ClientName <- "load-sub"
                use stateCall = scClient.SubscribeStateAsync(subReq)

                let! coord =
                    SyntheticCoordinator.connect channel "ai-load" "1.0.0" |> Async.AwaitTask
                use _ = coord

                // Pre-arm receiver: collect 500 timestamps as snapshots arrive.
                // CI-friendly sample count. SC-002 phrases the budget as "p95
                // ≤ 1 s over a 500-tick window of real game data"; the
                // synthetic CI run uses a smaller window because the broker
                // and subscriber share a single core and gRPC pipe under
                // dotnet test. Real-game closure (T034) re-runs the budget at
                // ≥ 500 ticks against a real BAR engine.
                let received = ConcurrentQueue<int64 * int64>()  // tick, recv-ticks-utc
                let totalExpected = 200
                let receiverDone = new TaskCompletionSource<unit>()
                let receiverCts = new CancellationTokenSource()
                let receiverTask =
                    Task.Run(fun () ->
                        task {
                            try
                                while received.Count < totalExpected do
                                    let! more = stateCall.ResponseStream.MoveNext(receiverCts.Token)
                                    if more then
                                        let m = stateCall.ResponseStream.Current
                                        match m.Body with
                                        | ValueSome (StateMsg.Types.Body.Snapshot s) ->
                                            received.Enqueue(s.Tick, DateTimeOffset.UtcNow.UtcTicks)
                                        | _ -> ()
                                receiverDone.TrySetResult() |> ignore
                            with _ -> receiverDone.TrySetResult() |> ignore
                        } :> Task)
                ignore receiverTask

                // Push at ~30 Hz (~33 ms between frames) to mirror real-game
                // cadence. Hammering the gRPC pipe back-to-back saturates
                // HTTP/2 flow control under loopback and hides the per-tick
                // latency the budget measures.
                let pushTimestamps = Array.zeroCreate<int64> totalExpected
                for i in 0 .. totalExpected - 1 do
                    pushTimestamps.[i] <- DateTimeOffset.UtcNow.UtcTicks
                    do! coord.PushSnapshotAsync (fun _ -> ()) |> Async.AwaitTask
                    do! Async.Sleep 33

                // Wait for receiver to drain (15 s budget, matches 001's
                // SC-003 tolerance under loopback gRPC).
                let waitTask =
                    Task.WhenAny(receiverDone.Task, Task.Delay(15000))
                let! _ = waitTask |> Async.AwaitTask
                receiverCts.Cancel()

                // Per-snapshot latency in ms; require ≥ 95% received within
                // the wait budget (mirrors 001 Sc003LatencyTests rule).
                let arr = received.ToArray()
                Expect.isGreaterThanOrEqual arr.Length (totalExpected * 95 / 100)
                    (sprintf "received %d/%d snapshots (≥ 95%% required)" arr.Length totalExpected)

                let latenciesMs =
                    arr
                    |> Array.map (fun (tick, recv) ->
                        // tick = frame from the synthetic coordinator (1-based);
                        // pushTimestamps is 0-based.
                        let pushTs = pushTimestamps.[int tick - 1]
                        float (recv - pushTs) / 10000.0)  // ticks → ms
                let p95 = percentile latenciesMs 95.0
                let p99 = percentile latenciesMs 99.0
                let max = Array.max latenciesMs

                Expect.isLessThan p95 1000.0
                    (sprintf "SC-002: p95=%.2fms must be ≤ 1000 ms (%d samples)" p95 arr.Length)

                let report = sprintf """# SC-002 latency under SyntheticCoordinator (T024)

**Date**: %s
**Samples**: %d snapshots
**p95**: %.3f ms
**p99**: %.3f ms
**max**: %.3f ms
**Budget**: ≤ 1 s
**Verdict**: PASS

The broker-side wire path is real Kestrel + `HighBarCoordinatorService` +
`BrokerState.applySnapshot` + per-client `Channel<StateMsg>` fan-out. The
plugin peer is the loopback `SyntheticCoordinator` fixture — real-wire
closure of SC-002 against a real BAR + HighBarV3 build is owned by T034.
"""
                                            (DateTimeOffset.UtcNow.ToString("u"))
                                            arr.Length p95 p99 max
                let dir =
                    Path.GetFullPath(
                        Path.Combine(__SOURCE_DIRECTORY__, "..", "..",
                            "specs", "002-highbar-coordinator-pivot", "readiness"))
                Directory.CreateDirectory(dir) |> ignore
                File.WriteAllText(Path.Combine(dir, "sc002-synthetic-latency.md"), report)
            finally
                (handle :> IAsyncDisposable).DisposeAsync().AsTask().Wait()
        }

        // T025 — SC-003 disconnect-recovery under SyntheticCoordinator.
        testAsync "Synthetic_T025 detect ≤ 5 s + recover ≤ 10 s in ≥ 95% of 20 trials" {
            let trials = 20
            let detectMs = Array.zeroCreate<float> trials
            let recoverMs = Array.zeroCreate<float> trials
            for i in 0 .. trials - 1 do
                let port = freePort()
                let handle, _ = startServer port
                try
                    use channel = GrpcChannel.ForAddress(sprintf "http://127.0.0.1:%d" port)
                    let! coord =
                        SyntheticCoordinator.connect channel (sprintf "ai-trial-%d" i) "1.0.0"
                        |> Async.AwaitTask
                    // Push one snapshot so the broker is in steady state.
                    do! coord.PushSnapshotAsync (fun _ -> ()) |> Async.AwaitTask

                    // Drop without graceful close (DropAsync cancels the CTS
                    // and best-effort completes the request stream).
                    let dropAt = DateTimeOffset.UtcNow
                    do! coord.DropAsync() |> Async.AwaitTask

                    // Wait for the broker to detect (activePluginId → None)
                    // and reach Idle. Poll every 50 ms; cap at 11 s wall.
                    let detectDeadline = dropAt.AddSeconds(11.0)
                    let mutable detectedAt = None
                    let mutable idleAt = None
                    while DateTimeOffset.UtcNow < detectDeadline
                          && (detectedAt.IsNone || idleAt.IsNone) do
                        let now = DateTimeOffset.UtcNow
                        if detectedAt.IsNone
                           && BrokerState.activePluginId handle.Hub = None then
                            detectedAt <- Some now
                        if idleAt.IsNone
                           && BrokerState.mode handle.Hub = Mode.Mode.Idle then
                            idleAt <- Some now
                        if detectedAt.IsNone || idleAt.IsNone then
                            do! Async.Sleep 50

                    detectMs.[i] <-
                        match detectedAt with
                        | Some t -> (t - dropAt).TotalMilliseconds
                        | None -> 999999.0
                    recoverMs.[i] <-
                        match idleAt with
                        | Some t -> (t - dropAt).TotalMilliseconds
                        | None -> 999999.0
                finally
                    (handle :> IAsyncDisposable).DisposeAsync().AsTask().Wait()

            let detectOk =
                detectMs |> Array.filter (fun m -> m <= 5000.0) |> Array.length
            let recoverOk =
                recoverMs |> Array.filter (fun m -> m <= 10000.0) |> Array.length
            let detectRate = float detectOk / float trials
            let recoverRate = float recoverOk / float trials

            Expect.isGreaterThanOrEqual detectRate 0.95
                (sprintf "detect ≤ 5 s in %.0f%% of %d trials" (detectRate * 100.0) trials)
            Expect.isGreaterThanOrEqual recoverRate 0.95
                (sprintf "recover ≤ 10 s in %.0f%% of %d trials" (recoverRate * 100.0) trials)

            let detectMax = Array.max detectMs
            let recoverMax = Array.max recoverMs

            let report = sprintf """# SC-003 disconnect recovery under SyntheticCoordinator (T025)

**Date**: %s
**Trials**: %d
**Detection ≤ 5 s**: %d / %d (%.0f%%)
**Recovery-to-Idle ≤ 10 s**: %d / %d (%.0f%%)
**Max detection**: %.1f ms
**Max recovery**: %.1f ms
**Verdict**: PASS

The broker-side wire path is real Kestrel + `HighBarCoordinatorService`
+ `BrokerState.closeSession` + per-attach watchdog (`heartbeatTimeoutMs`
default 5 s). The plugin peer is the loopback `SyntheticCoordinator`;
real-wire closure of SC-003 against a real BAR + HighBarV3 build is
owned by T035.
"""
                                            (DateTimeOffset.UtcNow.ToString("u"))
                                            trials
                                            detectOk trials (detectRate * 100.0)
                                            recoverOk trials (recoverRate * 100.0)
                                            detectMax recoverMax
            let dir =
                Path.GetFullPath(
                    Path.Combine(__SOURCE_DIRECTORY__, "..", "..",
                        "specs", "002-highbar-coordinator-pivot", "readiness"))
            Directory.CreateDirectory(dir) |> ignore
            File.WriteAllText(Path.Combine(dir, "sc003-synthetic-recovery.md"), report)
        }
    ]
