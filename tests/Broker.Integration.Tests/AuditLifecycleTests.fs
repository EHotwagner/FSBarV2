module Broker.Integration.Tests.AuditLifecycleTests

open System
open System.Net
open System.Net.Sockets
open System.Threading
open Expecto
open Grpc.Core
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

/// Start a server that captures every audit event into a thread-safe list.
/// Returns the handle and the list (the test reads it after the wire calls
/// have settled). FR-028: every connection-lifecycle event must be visible
/// in the audit stream with timestamp + identifier.
let private startCapturing
    (port: int)
    (brokerVersion: System.Version) =
    let events = System.Collections.Concurrent.ConcurrentQueue<Audit.AuditEvent>()
    let opts =
        { ServerHost.defaultOptions with
            listenAddress = sprintf "127.0.0.1:%d" port }
    let task =
        ServerHost.start opts brokerVersion (fun ev -> events.Enqueue(ev)) CancellationToken.None
    task.Wait()
    task.Result, events

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

let private mkAdminCmd (name: string) =
    let cmd = Command.empty()
    cmd.CommandId <- Google.Protobuf.ByteString.CopyFrom((Guid.NewGuid()).ToByteArray())
    cmd.OriginatingClient <- name
    let admin = AdminPayload.empty()
    admin.Pause <- Pause.empty()
    cmd.Admin <- admin
    cmd.SubmittedAtUnixMs <- DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
    cmd

let private snapshot (events: System.Collections.Concurrent.ConcurrentQueue<Audit.AuditEvent>) =
    events |> Array.ofSeq |> Array.toList

let private hasClientConnected name list =
    list |> List.exists (function
        | Audit.ClientConnected (_, ScriptingClientId n, _) when n = name -> true
        | _ -> false)

let private hasClientDisconnected name list =
    list |> List.exists (function
        | Audit.ClientDisconnected (_, ScriptingClientId n, _) when n = name -> true
        | _ -> false)

let private hasNameInUse name list =
    list |> List.exists (function
        | Audit.NameInUseRejected (_, attempted) when attempted = name -> true
        | _ -> false)

let private hasVersionMismatch peerKind list =
    list |> List.exists (function
        | Audit.VersionMismatchRejected (_, kind, _) when kind = peerKind -> true
        | _ -> false)

let private hasCommandRejected reason list =
    list |> List.exists (function
        | Audit.CommandRejected (_, _, _, r) when r = reason -> true
        | _ -> false)

[<Tests>]
let auditLifecycleTests =
    testList "Audit lifecycle (FR-028)" [

        testAsync "Hello -> ClientConnected event with timestamp + client_name + version" {
            let port = freePort()
            let handle, events = startCapturing port (System.Version(1, 0))
            try
                use channel = channelFor port
                let client = new ScriptingClient.ScriptingClientClient(channel)
                let! _ =
                    client.HelloAsync(mkHello "alpha" (System.Version(1, 0))).ResponseAsync
                    |> Async.AwaitTask
                Expect.isTrue
                    (snapshot events |> hasClientConnected "alpha")
                    "ClientConnected with id=alpha must be in the audit stream"
            finally
                (handle :> IAsyncDisposable).DisposeAsync().AsTask().Wait()
        }

        testAsync "Hello with same name twice -> NameInUseRejected event" {
            let port = freePort()
            let handle, events = startCapturing port (System.Version(1, 0))
            try
                use channel = channelFor port
                let client = new ScriptingClient.ScriptingClientClient(channel)
                let! _ =
                    client.HelloAsync(mkHello "duplicate" (System.Version(1, 0))).ResponseAsync
                    |> Async.AwaitTask
                let! _ =
                    Async.AwaitTask (
                        task {
                            try
                                let! _ =
                                    client.HelloAsync(mkHello "duplicate" (System.Version(1, 0))).ResponseAsync
                                return ()
                            with :? RpcException -> return ()
                        })
                let snap = snapshot events
                Expect.isTrue (hasClientConnected "duplicate" snap) "first Hello recorded"
                Expect.isTrue (hasNameInUse "duplicate" snap) "second Hello recorded as NameInUseRejected"
            finally
                (handle :> IAsyncDisposable).DisposeAsync().AsTask().Wait()
        }

        testAsync "Hello with major-version mismatch -> VersionMismatchRejected event" {
            let port = freePort()
            let handle, events = startCapturing port (System.Version(2, 0))
            try
                use channel = channelFor port
                let client = new ScriptingClient.ScriptingClientClient(channel)
                do!
                    Async.AwaitTask (
                        task {
                            try
                                let! _ =
                                    client.HelloAsync(mkHello "v1-peer" (System.Version(1, 9))).ResponseAsync
                                return ()
                            with :? RpcException -> return ()
                        })
                Expect.isTrue
                    (snapshot events |> hasVersionMismatch "scripting-client")
                    "VersionMismatchRejected with peer_kind=scripting-client must be in the audit stream"
            finally
                (handle :> IAsyncDisposable).DisposeAsync().AsTask().Wait()
        }

        testAsync "Admin command in guest -> CommandRejected(AdminNotAvailable) event" {
            let port = freePort()
            let handle, events = startCapturing port (System.Version(1, 0))
            try
                use channel = channelFor port
                let client = new ScriptingClient.ScriptingClientClient(channel)
                let! _ =
                    client.HelloAsync(mkHello "admin-attacker" (System.Version(1, 0))).ResponseAsync
                    |> Async.AwaitTask
                use submit = client.SubmitCommandsAsync()
                do! submit.RequestStream.WriteAsync(mkAdminCmd "admin-attacker") |> Async.AwaitTask
                do! submit.RequestStream.CompleteAsync() |> Async.AwaitTask
                let! _ = submit.ResponseStream.MoveNext(CancellationToken.None) |> Async.AwaitTask
                Expect.isTrue
                    (snapshot events |> hasCommandRejected CommandPipeline.AdminNotAvailable)
                    "CommandRejected(AdminNotAvailable) must be in the audit stream"
            finally
                (handle :> IAsyncDisposable).DisposeAsync().AsTask().Wait()
        }
    ]
    |> testSequenced
