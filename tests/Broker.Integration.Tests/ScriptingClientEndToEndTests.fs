module Broker.Integration.Tests.ScriptingClientEndToEndTests

open System
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

// ---- helpers --------------------------------------------------------------

let private freePort () =
    let listener = new TcpListener(IPAddress.Loopback, 0)
    listener.Start()
    let port = (listener.LocalEndpoint :?> IPEndPoint).Port
    listener.Stop()
    port

let private startServerOn (port: int) (brokerVersion: System.Version) : Task<ServerHost.ServerHandle> =
    let opts =
        { ServerHost.defaultOptions with
            listenAddress = sprintf "127.0.0.1:%d" port }
    ServerHost.start opts brokerVersion (fun _ -> ()) CancellationToken.None

let private channelFor (port: int) =
    GrpcChannel.ForAddress(
        sprintf "http://127.0.0.1:%d" port,
        GrpcChannelOptions(MaxReceiveMessageSize = 4 * 1024 * 1024))

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

let private mkAdminCommand (clientName: string) =
    let cmd = Command.empty()
    cmd.CommandId <-
        Google.Protobuf.ByteString.CopyFrom((Guid.NewGuid()).ToByteArray())
    cmd.OriginatingClient <- clientName
    let admin = AdminPayload.empty()
    admin.Pause <- Pause.empty()
    cmd.Admin <- admin
    cmd.SubmittedAtUnixMs <- DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
    cmd

// ---- tests ----------------------------------------------------------------

[<Tests>]
let scriptingClientE2E =
    // gRPC server start/stop has measurable per-test cost; group everything
    // that can share a server, but keep mismatch tests on their own server
    // (different broker version) for isolation.
    testList "ScriptingClient end-to-end" [

        testAsync "Hello with major version match returns ok and isAdmin=false (US1 acceptance #1, FR-008/FR-029)" {
            let port = freePort()
            let! handle = startServerOn port (System.Version(1, 0)) |> Async.AwaitTask
            try
                use channel = channelFor port
                let client = new ScriptingClient.ScriptingClientClient(channel)
                let! reply =
                    client.HelloAsync(mkHello "alice-bot" (System.Version(1, 0))).ResponseAsync
                    |> Async.AwaitTask
                Expect.isFalse reply.IsAdmin "fresh client must connect non-admin (FR-016)"
                match reply.BrokerVersion with
                | ValueSome v ->
                    Expect.equal (int v.Major) 1 "broker_version.major echoed"
                | ValueNone -> failtest "broker_version absent in HelloReply"
            finally
                (handle :> IAsyncDisposable).DisposeAsync().AsTask().Wait()
        }

        testAsync "Hello with same name twice returns NAME_IN_USE (FR-008)" {
            let port = freePort()
            let! handle = startServerOn port (System.Version(1, 0)) |> Async.AwaitTask
            try
                use channel = channelFor port
                let client = new ScriptingClient.ScriptingClientClient(channel)
                let! _ =
                    client.HelloAsync(mkHello "duplicate" (System.Version(1, 0))).ResponseAsync
                    |> Async.AwaitTask
                let! ex =
                    Async.AwaitTask (
                        task {
                            try
                                let! _ =
                                    client.HelloAsync(mkHello "duplicate" (System.Version(1, 0))).ResponseAsync
                                return None
                            with
                            | :? RpcException as r -> return Some r
                        })
                match ex with
                | Some r ->
                    Expect.equal r.StatusCode StatusCode.AlreadyExists "second Hello must reject"
                    Expect.stringContains r.Status.Detail "NAME_IN_USE" "detail mentions NAME_IN_USE"
                | None -> failtest "expected second Hello to throw RpcException"
            finally
                (handle :> IAsyncDisposable).DisposeAsync().AsTask().Wait()
        }

        testAsync "Hello with major-version mismatch returns FailedPrecondition (FR-029)" {
            let port = freePort()
            let! handle = startServerOn port (System.Version(2, 0)) |> Async.AwaitTask
            try
                use channel = channelFor port
                let client = new ScriptingClient.ScriptingClientClient(channel)
                let! ex =
                    Async.AwaitTask (
                        task {
                            try
                                let! _ =
                                    client.HelloAsync(mkHello "v1-peer" (System.Version(1, 9))).ResponseAsync
                                return None
                            with
                            | :? RpcException as r -> return Some r
                        })
                match ex with
                | Some r ->
                    Expect.equal r.StatusCode StatusCode.FailedPrecondition "version mismatch must reject"
                    Expect.stringContains r.Status.Detail "VERSION_MISMATCH" "detail names the mismatch"
                | None -> failtest "expected version mismatch to throw"
            finally
                (handle :> IAsyncDisposable).DisposeAsync().AsTask().Wait()
        }

        testAsync "Admin command in guest mode is rejected with ADMIN_NOT_AVAILABLE (US1 acceptance #4, FR-004)" {
            let port = freePort()
            let! handle = startServerOn port (System.Version(1, 0)) |> Async.AwaitTask
            try
                use channel = channelFor port
                let client = new ScriptingClient.ScriptingClientClient(channel)
                let! _ =
                    client.HelloAsync(mkHello "admin-attacker" (System.Version(1, 0))).ResponseAsync
                    |> Async.AwaitTask
                use submit = client.SubmitCommandsAsync()
                do! submit.RequestStream.WriteAsync(mkAdminCommand "admin-attacker") |> Async.AwaitTask
                do! submit.RequestStream.CompleteAsync() |> Async.AwaitTask
                let! gotAck = submit.ResponseStream.MoveNext(CancellationToken.None) |> Async.AwaitTask
                Expect.isTrue gotAck "expected at least one CommandAck"
                let ack = submit.ResponseStream.Current
                Expect.isFalse ack.Accepted "admin in guest mode is never accepted"
                match ack.Reject with
                | ValueSome r ->
                    Expect.equal r.Code Reject.Types.Code.AdminNotAvailable "code = ADMIN_NOT_AVAILABLE"
                | ValueNone -> failtest "expected Reject body on rejected admin command"
            finally
                (handle :> IAsyncDisposable).DisposeAsync().AsTask().Wait()
        }
    ]
    |> testSequenced     // keep tests serial — they bind unique ports and own a Kestrel listener.
