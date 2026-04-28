module Broker.Integration.Tests.AdminElevationTests

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

let private startCapturing
    (port: int)
    (brokerVersion: System.Version)
    : ServerHost.ServerHandle * System.Collections.Concurrent.ConcurrentQueue<Audit.AuditEvent> =
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

let private mkPause (name: string) =
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

let private hostLobby : Lobby.LobbyConfig =
    { mapName = "Tabula"
      gameMode = "Skirmish"
      participants =
        [ { slotIndex = 0; kind = ParticipantSlot.ProxyAi; team = 0; boundClient = None }
          { slotIndex = 1; kind = ParticipantSlot.BuiltInAi 5; team = 1; boundClient = None } ]
      display = Lobby.Headless }

[<Tests>]
let adminElevationTests =
    testList "Admin elevation lifecycle (US2 acceptance #2-3, FR-016)" [

        testAsync "grant -> Pause accepted; revoke -> Pause rejected; audit covers both" {
            let port = freePort()
            let handle, events = startCapturing port (System.Version(1, 0))
            try
                // Operator confirms a host-mode launch from the TUI; the
                // hub is now in Mode.Hosting and admin authority is the
                // operator's to grant per client (FR-016, quickstart §3).
                BrokerState.openHostSession hostLobby DateTimeOffset.UtcNow handle.Hub
                |> function
                    | Ok () -> ()
                    | Error e -> failtestf "openHostSession failed: %s" e

                use channel = channelFor port
                let client = new ScriptingClient.ScriptingClientClient(channel)
                let! _ =
                    client.HelloAsync(mkHello "alice-bot" (System.Version(1, 0))).ResponseAsync
                    |> Async.AwaitTask

                // Phase 1: non-admin client cannot Pause yet.
                use submit1 = client.SubmitCommandsAsync()
                do! submit1.RequestStream.WriteAsync(mkPause "alice-bot") |> Async.AwaitTask
                do! submit1.RequestStream.CompleteAsync() |> Async.AwaitTask
                let! _ = submit1.ResponseStream.MoveNext(CancellationToken.None) |> Async.AwaitTask
                let ack1 = submit1.ResponseStream.Current
                Expect.isFalse ack1.Accepted "Pause must be rejected before grant"
                match ack1.Reject with
                | ValueSome r ->
                    Expect.equal r.Code Reject.Types.Code.AdminNotAvailable
                        "phase 1: ADMIN_NOT_AVAILABLE before elevation"
                | ValueNone -> failtest "phase 1: expected Reject body"

                // Phase 2: operator elevates alice-bot to admin from the TUI.
                BrokerState.grantAdmin
                    (ScriptingClientId "alice-bot")
                    "operator"
                    DateTimeOffset.UtcNow
                    handle.Hub
                |> function
                    | Ok () -> ()
                    | Error e -> failtestf "grantAdmin failed: %A" e

                use submit2 = client.SubmitCommandsAsync()
                do! submit2.RequestStream.WriteAsync(mkPause "alice-bot") |> Async.AwaitTask
                do! submit2.RequestStream.CompleteAsync() |> Async.AwaitTask
                let! _ = submit2.ResponseStream.MoveNext(CancellationToken.None) |> Async.AwaitTask
                let ack2 = submit2.ResponseStream.Current
                Expect.isTrue ack2.Accepted "Pause must be accepted after grant (Hosting + isAdmin)"
                Expect.equal ack2.Reject ValueNone "phase 2: no reject body when accepted"

                // Phase 3: operator revokes admin; subsequent Pause rejected.
                BrokerState.revokeAdmin
                    (ScriptingClientId "alice-bot")
                    "operator"
                    DateTimeOffset.UtcNow
                    handle.Hub
                |> function
                    | Ok () -> ()
                    | Error e -> failtestf "revokeAdmin failed: %A" e

                use submit3 = client.SubmitCommandsAsync()
                do! submit3.RequestStream.WriteAsync(mkPause "alice-bot") |> Async.AwaitTask
                do! submit3.RequestStream.CompleteAsync() |> Async.AwaitTask
                let! _ = submit3.ResponseStream.MoveNext(CancellationToken.None) |> Async.AwaitTask
                let ack3 = submit3.ResponseStream.Current
                Expect.isFalse ack3.Accepted "Pause must be rejected after revoke"
                match ack3.Reject with
                | ValueSome r ->
                    Expect.equal r.Code Reject.Types.Code.AdminNotAvailable
                        "phase 3: ADMIN_NOT_AVAILABLE after revoke"
                | ValueNone -> failtest "phase 3: expected Reject body"

                // Audit: grant, revoke, and at least two CommandRejected.
                let snap = snapshot events
                let granted =
                    snap |> List.exists (function
                        | Audit.AdminGranted (_, ScriptingClientId "alice-bot", "operator") -> true
                        | _ -> false)
                let revoked =
                    snap |> List.exists (function
                        | Audit.AdminRevoked (_, ScriptingClientId "alice-bot", "operator") -> true
                        | _ -> false)
                let rejectCount =
                    snap |> List.sumBy (function
                        | Audit.CommandRejected
                            (_, ScriptingClientId "alice-bot", _, CommandPipeline.AdminNotAvailable) -> 1
                        | _ -> 0)
                Expect.isTrue  granted "AdminGranted in audit stream"
                Expect.isTrue  revoked "AdminRevoked in audit stream"
                Expect.equal   rejectCount 2 "two CommandRejected(AdminNotAvailable) — phases 1 + 3"
            finally
                (handle :> IAsyncDisposable).DisposeAsync().AsTask().Wait()
        }
    ]
    |> testSequenced
