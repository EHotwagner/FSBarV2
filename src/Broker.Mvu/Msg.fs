namespace Broker.Mvu

open System
open Broker.Core
open Highbar.V1
open FSBarV2.Broker.Contracts

module Msg =

    type RpcContext = {
        rpcId: Cmd.RpcId
        receivedAt: DateTimeOffset
        operation: string
    }

    type CoordinatorInbound =
        | Heartbeat of pluginId:string * schemaVersion:string * engineSha256:string * RpcContext
        | PushStateOpened of pluginId:string * RpcContext
        | PushStateSnapshot of seq:uint64 * snapshot:Snapshot.GameStateSnapshot
        | PushStateDelta of seq:uint64
        | PushStateKeepAlive of seq:uint64
        | PushStateClosed of reason:string
        | OpenCommandChannelOpened of pluginId:string * RpcContext

    type ScriptingInbound =
        | Hello of clientId:ScriptingClientId * protocolVersion:System.Version * RpcContext
        | Subscribe of clientId:ScriptingClientId * RpcContext
        | Unsubscribe of clientId:ScriptingClientId
        | Command of clientId:ScriptingClientId * cmd:CommandPipeline.Command
        | Disconnected of clientId:ScriptingClientId * reason:string

    type AdapterCallback =
        | QueueDepth of clientId:ScriptingClientId * depth:int * highWaterSinceLastSample:int * sampledAt:DateTimeOffset
        | QueueOverflow of clientId:ScriptingClientId * rejectedSeq:int64 * at:DateTimeOffset
        | TimerFired of timerId:Cmd.TimerId * firedAt:DateTimeOffset
        | VizWindowClosed of at:DateTimeOffset
        | MailboxHighWater of depth:int * highWater:int * sampledAt:DateTimeOffset

    type CmdFailure =
        | AuditWriteFailed of summary:string * exn:exn
        | CoordinatorSendFailed of summary:string * exn:exn
        | ScriptingSendFailed of clientId:ScriptingClientId * summary:string * exn:exn
        | VizOpFailed of summary:string * exn:exn
        | TimerFailed of timerId:Cmd.TimerId * summary:string * exn:exn

    type TuiInput =
        | Keypress of ConsoleKeyInfo
        | Resize of width:int * height:int
        | QuitRequested

    type Tick =
        | DashboardTick of at:DateTimeOffset
        | HeartbeatProbe of at:DateTimeOffset
        | SnapshotStaleness of at:DateTimeOffset

    type Lifecycle =
        | RuntimeStarted of at:DateTimeOffset
        | RuntimeStopRequested of reason:string * at:DateTimeOffset
        | SessionEnded of reason:Session.EndReason * at:DateTimeOffset

    type Msg =
        | TuiInput of TuiInput
        | CoordinatorInbound of CoordinatorInbound
        | ScriptingInbound of ScriptingInbound
        | AdapterCallback of AdapterCallback
        | CmdFailure of CmdFailure
        | Tick of Tick
        | Lifecycle of Lifecycle
