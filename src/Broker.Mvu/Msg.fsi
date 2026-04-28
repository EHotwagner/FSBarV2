namespace Broker.Mvu

open System
open Broker.Core
open Highbar.V1
open FSBarV2.Broker.Contracts

module Msg =

    /// The unique identifier the runtime hands out for each in-flight
    /// gRPC handler. The `update` clause for the matching `Msg` arm
    /// emits `Cmd.CompleteRpc rpcId result` to wake the handler.
    type RpcContext = {
        rpcId: Cmd.RpcId
        receivedAt: DateTimeOffset
        operation: string
    }

    /// Coordinator-side inbound RPCs (HighBar wire). Each carries the
    /// payload **already decoded** by `Broker.Protocol.WireConvert` —
    /// no proto types in `Broker.Mvu`. The `RpcContext` is the rendezvous
    /// the runtime completes when `update` emits `Cmd.CompleteRpc`.
    type CoordinatorInbound =
        | Heartbeat of pluginId:string * schemaVersion:string * engineSha256:string * RpcContext
        | PushStateOpened of pluginId:string * RpcContext
        | PushStateSnapshot of seq:uint64 * snapshot:Snapshot.GameStateSnapshot
        | PushStateDelta of seq:uint64
        | PushStateKeepAlive of seq:uint64
        | PushStateClosed of reason:string
        | OpenCommandChannelOpened of pluginId:string * RpcContext

    /// Scripting-client-side inbound RPCs.
    type ScriptingInbound =
        | Hello of clientId:ScriptingClientId * protocolVersion:System.Version * RpcContext
        | Subscribe of clientId:ScriptingClientId * RpcContext
        | Unsubscribe of clientId:ScriptingClientId
        | Command of clientId:ScriptingClientId * cmd:CommandPipeline.Command
        | Disconnected of clientId:ScriptingClientId * reason:string

    /// Adapter callbacks: events that adapters originate.
    type AdapterCallback =
        | QueueDepth of clientId:ScriptingClientId * depth:int * highWaterSinceLastSample:int * sampledAt:DateTimeOffset
        | QueueOverflow of clientId:ScriptingClientId * rejectedSeq:int64 * at:DateTimeOffset
        | TimerFired of timerId:Cmd.TimerId * firedAt:DateTimeOffset
        | VizWindowClosed of at:DateTimeOffset
        | MailboxHighWater of depth:int * highWater:int * sampledAt:DateTimeOffset

    /// Cmd-execution failure arms. Per spec Clarification Q3 (2026-04-28)
    /// and FR-008. Each carries the operation summary and the underlying
    /// exception. Exhaustively matched in `update`.
    type CmdFailure =
        | AuditWriteFailed of summary:string * exn:exn
        | CoordinatorSendFailed of summary:string * exn:exn
        | ScriptingSendFailed of clientId:ScriptingClientId * summary:string * exn:exn
        | VizOpFailed of summary:string * exn:exn
        | TimerFailed of timerId:Cmd.TimerId * summary:string * exn:exn

    /// Operator input from the TUI thread.
    type TuiInput =
        | Keypress of ConsoleKeyInfo
        | Resize of width:int * height:int
        | QuitRequested

    /// Recurring runtime ticks.
    type Tick =
        | DashboardTick of at:DateTimeOffset
        | HeartbeatProbe of at:DateTimeOffset
        | SnapshotStaleness of at:DateTimeOffset

    /// Lifecycle: bookend events for the runtime's own lifetime.
    type Lifecycle =
        | RuntimeStarted of at:DateTimeOffset
        | RuntimeStopRequested of reason:string * at:DateTimeOffset
        | SessionEnded of reason:Session.EndReason * at:DateTimeOffset

    /// The single Msg union dispatched to `update`. Every input case the
    /// broker can react to is one arm here. Exhaustively matched in
    /// `update`, enforced by the F# compiler (FR-004).
    type Msg =
        | TuiInput of TuiInput
        | CoordinatorInbound of CoordinatorInbound
        | ScriptingInbound of ScriptingInbound
        | AdapterCallback of AdapterCallback
        | CmdFailure of CmdFailure
        | Tick of Tick
        | Lifecycle of Lifecycle
