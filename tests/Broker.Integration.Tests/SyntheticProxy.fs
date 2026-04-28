// SYNTHETIC FIXTURE: every test in this module substitutes a loopback
// `ProxyLinkClient` for the eventual HighBarV3 proxy AI peer (research.md
// §7, §14). The broker-side wire path it exercises is real production
// code; the proxy peer is the synthetic part. Tracked in tasks.md
// Synthetic-Evidence Inventory under T029.
module Broker.Integration.Tests.SyntheticProxy

open System
open System.Threading
open System.Threading.Tasks
open Grpc.Core
open Grpc.Net.Client
open FSBarV2.Broker.Contracts

/// A loopback `ProxyLink` client driver. Connects to a hosted broker,
/// completes the handshake, and exposes `pushSnapshot` / `endSession` for
/// tests. Stands in for the real HighBarV3 proxy AI workstream
/// (research.md §7, §14) — anywhere a US1/US2 evidence task says "exercise
/// against a synthetic-proxy fixture", this is that fixture.
type Driver(call: AsyncDuplexStreamingCall<ProxyClientMsg, ProxyServerMsg>,
            sessionId: byte[],
            cts: CancellationTokenSource) =
    member _.SessionId : byte[] = sessionId

    /// Push one snapshot to the broker. Caller fills in tick / players /
    /// units / etc. via the supplied builder.
    member _.PushSnapshotAsync (build: GameStateSnapshot -> unit) : Task =
        task {
            let snap = GameStateSnapshot.empty()
            snap.SessionId <- Google.Protobuf.ByteString.CopyFrom(sessionId)
            snap.CapturedAtUnixMs <- DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
            build snap
            let m = ProxyClientMsg.empty()
            m.Snapshot <- snap
            do! call.RequestStream.WriteAsync(m)
        } :> Task

    /// Send a SessionEnd message followed by completing the request stream.
    member _.EndSessionAsync (reason: SessionEnd.Types.Reason) : Task =
        task {
            let se = SessionEnd.empty()
            se.SessionId <- Google.Protobuf.ByteString.CopyFrom(sessionId)
            se.Reason <- reason
            let m = ProxyClientMsg.empty()
            m.SessionEnd <- se
            do! call.RequestStream.WriteAsync(m)
            do! call.RequestStream.CompleteAsync()
        } :> Task

    /// Drop the proxy without a graceful SessionEnd — emulates a crash
    /// or network drop (FR-026 detection path).
    member _.DropAsync () : Task =
        task {
            cts.Cancel()
            try
                do! call.RequestStream.CompleteAsync()
            with _ -> ()
        } :> Task

    interface IDisposable with
        member _.Dispose() =
            try cts.Cancel() with _ -> ()
            try call.Dispose() with _ -> ()

/// Open a `ProxyLink.Attach` bidi stream against the broker at `address`,
/// send the initial Handshake at `peerVersion`, await the HandshakeAck,
/// and return a `Driver` ready for snapshot pushes. Throws on handshake
/// failure (the test expectation).
let connect
    (channel: GrpcChannel)
    (peerVersion: System.Version)
    (proxyId: string)
    : Task<Driver> =
    task {
        let client = new ProxyLink.ProxyLinkClient(channel)
        let cts = new CancellationTokenSource()
        let call = client.AttachAsync()

        // 1. Handshake.
        let pv = ProtocolVersion.empty()
        pv.Major <- uint32 peerVersion.Major
        pv.Minor <- uint32 peerVersion.Minor
        let hs = Handshake.empty()
        hs.ProxyVersion <- ValueSome pv
        hs.ProxyId <- proxyId
        let firstMsg = ProxyClientMsg.empty()
        firstMsg.Handshake <- hs
        do! call.RequestStream.WriteAsync(firstMsg)

        // 2. Wait for HandshakeAck (or Reject).
        let! more = call.ResponseStream.MoveNext(cts.Token)
        if not more then
            failwith "proxy stream closed before HandshakeAck"
        let cur = call.ResponseStream.Current
        let sessionBytes =
            match cur.Body with
            | ValueSome (ProxyServerMsg.Types.Body.HandshakeAck ack) ->
                ack.SessionId.ToByteArray()
            | ValueSome (ProxyServerMsg.Types.Body.Reject r) ->
                failwithf "proxy handshake rejected: code=%A detail=%s" r.Code r.Detail
            | other ->
                failwithf "expected HandshakeAck, got %A" other
        return new Driver(call, sessionBytes, cts)
    }
