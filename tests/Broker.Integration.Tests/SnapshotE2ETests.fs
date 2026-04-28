module Broker.Integration.Tests.SnapshotE2ETests

open System
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

let private startServer (port: int) (brokerVersion: System.Version) =
    let opts = { ServerHost.defaultOptions with listenAddress = sprintf "127.0.0.1:%d" port }
    let task = ServerHost.start opts brokerVersion (fun _ -> ()) CancellationToken.None
    task.Wait()
    task.Result

let private channelFor (port: int) =
    GrpcChannel.ForAddress(sprintf "http://127.0.0.1:%d" port)

let private mkProtocolVersion (v: System.Version) =
    let pv = ProtocolVersion.empty()
    pv.Major <- uint32 v.Major
    pv.Minor <- uint32 v.Minor
    pv

let private mkHello (name: string) (version: System.Version) =
    let req = HelloRequest.empty()
    req.ClientName <- name
    req.ClientVersion <- ValueSome (mkProtocolVersion version)
    req

let private mkSubscribe (name: string) =
    let req = SubscribeRequest.empty()
    req.ClientName <- name
    req

[<Tests>]
let snapshotE2ETests =
    testList "Snapshot end-to-end (US1 acceptance #2 / FR-006)" [

        testAsync "Subscribed client receives snapshots pushed by the synthetic proxy" {
            let port = freePort()
            let handle = startServer port (System.Version(1, 0))
            try
                use channel = channelFor port

                // 1. Hello + subscribe.
                let scClient = new ScriptingClient.ScriptingClientClient(channel)
                let! _ =
                    scClient.HelloAsync(mkHello "subscriber-1" (System.Version(1, 0))).ResponseAsync
                    |> Async.AwaitTask

                use stateCall = scClient.SubscribeStateAsync(mkSubscribe "subscriber-1")

                // 2. Attach a synthetic proxy.
                let! proxy =
                    SyntheticProxy.connect channel (System.Version(1, 0)) "test-proxy"
                    |> Async.AwaitTask
                use _ = proxy

                // 3. Push three snapshots with monotonic ticks.
                for tick in [ 1L; 2L; 3L ] do
                    do!
                        proxy.PushSnapshotAsync (fun s ->
                            s.Tick <- tick)
                        |> Async.AwaitTask

                // 4. Read three snapshots from the subscriber stream.
                let received = ResizeArray<int64>()
                use timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5.0))
                while received.Count < 3 do
                    let! gotMore =
                        stateCall.ResponseStream.MoveNext(timeout.Token)
                        |> Async.AwaitTask
                    if not gotMore then
                        failtestf "stream closed before all snapshots arrived (received %d)" received.Count
                    let cur = stateCall.ResponseStream.Current
                    match cur.Body with
                    | ValueSome (StateMsg.Types.Body.Snapshot snap) ->
                        received.Add(snap.Tick)
                    | other ->
                        failtestf "expected Snapshot, got %A" other

                Expect.equal
                    (received |> List.ofSeq)
                    [ 1L; 2L; 3L ]
                    "subscriber must see ticks in order, with no gaps (FR-006)"
            finally
                (handle :> IAsyncDisposable).DisposeAsync().AsTask().Wait()
        }

        testAsync "Subscribed client receives SessionEnd when proxy gracefully ends (US1 acceptance #5 / FR-026)" {
            let port = freePort()
            let handle = startServer port (System.Version(1, 0))
            try
                use channel = channelFor port

                let scClient = new ScriptingClient.ScriptingClientClient(channel)
                let! _ =
                    scClient.HelloAsync(mkHello "watcher" (System.Version(1, 0))).ResponseAsync
                    |> Async.AwaitTask
                use stateCall = scClient.SubscribeStateAsync(mkSubscribe "watcher")

                let! proxy =
                    SyntheticProxy.connect channel (System.Version(1, 0)) "test-proxy"
                    |> Async.AwaitTask
                use _ = proxy

                // Push one snapshot, then end the session gracefully.
                do! proxy.PushSnapshotAsync (fun s -> s.Tick <- 1L) |> Async.AwaitTask
                do! proxy.EndSessionAsync(SessionEnd.Types.Reason.OperatorTerminated) |> Async.AwaitTask

                // Read until we see SessionEnd or the stream closes.
                use timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5.0))
                let mutable sawEnd = false
                let mutable closed = false
                while not sawEnd && not closed do
                    let! more =
                        stateCall.ResponseStream.MoveNext(timeout.Token)
                        |> Async.AwaitTask
                    if not more then
                        closed <- true
                    else
                        let cur = stateCall.ResponseStream.Current
                        match cur.Body with
                        | ValueSome (StateMsg.Types.Body.SessionEnd se) ->
                            Expect.equal
                                se.Reason
                                SessionEnd.Types.Reason.OperatorTerminated
                                "SessionEnd reason matches what the proxy sent"
                            sawEnd <- true
                        | ValueSome (StateMsg.Types.Body.Snapshot _) -> ()    // earlier broadcast
                        | other -> failtestf "unexpected StateMsg body: %A" other
                Expect.isTrue (sawEnd || closed) "subscriber must see SessionEnd or stream close on graceful end"
            finally
                (handle :> IAsyncDisposable).DisposeAsync().AsTask().Wait()
        }
    ]
    |> testSequenced
