// SYNTHETIC FIXTURE: every test that uses this module substitutes a
// loopback `HighBarCoordinatorClient` for the eventual real BAR + HighBarV3
// plugin process (research §9). The broker-side wire path it exercises is
// real production code; the plugin peer is the synthetic part. Tracked in
// tasks.md (002) Synthetic-Evidence Inventory under T023 / T032 and in
// 001's inventory under T029 (closed by T033 in Phase 5).
module Broker.Integration.Tests.SyntheticCoordinator

open System
open System.Threading
open System.Threading.Tasks
open Grpc.Core
open Grpc.Net.Client
open Highbar.V1

/// Loopback HighBar coordinator client. Connects to a hosted broker, sends
/// a Heartbeat, optionally opens PushState and OpenCommandChannel, and
/// exposes helpers for tests to drive snapshots + drops.
type Driver(
    client: HighBarCoordinator.HighBarCoordinatorClient,
    pluginId: string,
    schemaVersion: string,
    pushCall: AsyncClientStreamingCall<StateUpdate, PushAck> option,
    cmdCall: AsyncServerStreamingCall<CommandBatch> option,
    cts: CancellationTokenSource) =

    let mutable seq = 0UL
    let mutable frame = 0u

    member _.PluginId : string = pluginId
    member _.SchemaVersion : string = schemaVersion
    member _.CommandStream : IAsyncStreamReader<CommandBatch> option =
        cmdCall |> Option.map (fun c -> c.ResponseStream)

    /// Send a HeartbeatRequest with the configured pluginId + schemaVersion.
    /// Tests that need to verify reject paths (FR-003 / FR-011) call this
    /// directly with overrides via the lower-level constructor.
    member _.SendHeartbeatAsync (frameOverride: uint32 option) : Task<HeartbeatResponse> =
        let req = HeartbeatRequest.empty()
        req.PluginId <- pluginId
        req.SchemaVersion <- schemaVersion
        req.Frame <- defaultArg frameOverride frame
        client.HeartbeatAsync(req).ResponseAsync

    /// Push one StateSnapshot-flavoured StateUpdate. Caller fills the
    /// `StateSnapshot` payload via `build`; sequencing + framing are
    /// handled by the driver.
    member _.PushSnapshotAsync (build: StateSnapshot -> unit) : Task =
        match pushCall with
        | None -> Task.CompletedTask
        | Some call ->
            task {
                seq <- seq + 1UL
                frame <- frame + 1u
                let ss = StateSnapshot.empty()
                ss.FrameNumber <- frame
                build ss
                let upd = StateUpdate.empty()
                upd.Seq <- seq
                upd.Frame <- frame
                upd.Snapshot <- ss
                do! call.RequestStream.WriteAsync(upd)
            } :> Task

    /// Push a StateUpdate that skips the next sequence number — the broker
    /// should surface a CoordinatorStateGap audit event.
    member _.PushSnapshotWithSeqGapAsync (gap: uint64) (build: StateSnapshot -> unit) : Task =
        match pushCall with
        | None -> Task.CompletedTask
        | Some call ->
            task {
                seq <- seq + gap
                frame <- frame + 1u
                let ss = StateSnapshot.empty()
                ss.FrameNumber <- frame
                build ss
                let upd = StateUpdate.empty()
                upd.Seq <- seq
                upd.Frame <- frame
                upd.Snapshot <- ss
                do! call.RequestStream.WriteAsync(upd)
            } :> Task

    /// Push a KeepAlive payload (FR-008 idle-but-alive beacon).
    member _.PushKeepAliveAsync () : Task =
        match pushCall with
        | None -> Task.CompletedTask
        | Some call ->
            task {
                seq <- seq + 1UL
                let ka = KeepAlive.empty()
                let upd = StateUpdate.empty()
                upd.Seq <- seq
                upd.Frame <- frame
                upd.Keepalive <- ka
                do! call.RequestStream.WriteAsync(upd)
            } :> Task

    /// Graceful close: complete the PushState request stream, dispose
    /// streams. The broker side observes the stream end and runs its
    /// closeSession path.
    member _.CompleteAsync () : Task =
        task {
            match pushCall with
            | Some call ->
                try do! call.RequestStream.CompleteAsync() with _ -> ()
            | None -> ()
        } :> Task

    /// Drop the coordinator without graceful close — emulates a plugin
    /// crash or network drop (FR-008 detection path).
    member _.DropAsync () : Task =
        task {
            cts.Cancel()
            match pushCall with
            | Some call ->
                try do! call.RequestStream.CompleteAsync() with _ -> ()
            | None -> ()
        } :> Task

    interface IDisposable with
        member _.Dispose() =
            try cts.Cancel() with _ -> ()
            pushCall |> Option.iter (fun c -> try c.Dispose() with _ -> ())
            cmdCall |> Option.iter (fun c -> try c.Dispose() with _ -> ())

/// Connect, send the initial Heartbeat, optionally open PushState +
/// OpenCommandChannel, and return a Driver.
///
/// `pluginId` is the plugin's identity; the broker's owner-AI rule
/// (FR-011 / `OwnerRule.FirstAttached` by default) captures it on the
/// first successful Heartbeat.
let connect
    (channel: GrpcChannel)
    (pluginId: string)
    (schemaVersion: string)
    : Task<Driver> =
    task {
        let client = HighBarCoordinator.HighBarCoordinatorClient(channel)
        let cts = new CancellationTokenSource()

        // 1. Heartbeat (handshake).
        let hb = HeartbeatRequest.empty()
        hb.PluginId <- pluginId
        hb.SchemaVersion <- schemaVersion
        hb.Frame <- 0u
        let! _ack = client.HeartbeatAsync(hb).ResponseAsync

        // 2. PushState client-streaming (plugin → broker).
        let pushCall = client.PushStateAsync()

        // 3. OpenCommandChannel server-streaming (broker → plugin).
        let sub = CommandChannelSubscribe.empty()
        sub.PluginId <- pluginId
        let cmdCall = client.OpenCommandChannelAsync(sub, cancellationToken = cts.Token)

        return new Driver(client, pluginId, schemaVersion, Some pushCall, Some cmdCall, cts)
    }

/// Connect for Heartbeat-only flows (schema-mismatch / non-owner rejection
/// tests). Returns a Driver whose PushState / OpenCommandChannel streams
/// are not opened — only the Heartbeat RPC is reachable.
let connectHeartbeatOnly
    (channel: GrpcChannel)
    (pluginId: string)
    (schemaVersion: string)
    : Driver =
    let client = HighBarCoordinator.HighBarCoordinatorClient(channel)
    let cts = new CancellationTokenSource()
    new Driver(client, pluginId, schemaVersion, None, None, cts)
