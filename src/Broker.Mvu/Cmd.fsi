namespace Broker.Mvu

open Broker.Core
open FSBarV2.Broker.Contracts

module Cmd =

    /// Identifier for an outstanding gRPC handler awaiting a response.
    type RpcId = RpcId of int64

    /// Identifier for a registered timer schedule.
    type TimerId = TimerId of int64

    /// Result an `update` clause hands back to the gRPC handler that
    /// originally dispatched the matching `Msg`. The handler reads the
    /// post-update `Model` to assemble the wire response — `Cmd.CompleteRpc`
    /// is just the "you may proceed" signal, plus a fault path. This is
    /// simpler than the data-model §1.5 `obj` erasure: no boxing, no cast,
    /// no risk of a payload-shape mismatch.
    type RpcResult =
        | Ok
        | Fault of exn:exn

    /// Reasons a scripting-client message can be rejected at the adapter
    /// boundary. Carries forward 001's `CommandPipeline.RejectReason`.
    type RejectReason = CommandPipeline.RejectReason

    /// Viewer-window operations. Mirror the `Broker.Viz.VizControllerImpl`
    /// surface, restated as data rather than method calls.
    type VizOp =
        | OpenWindow
        | CloseWindow
        | PushFrame of Snapshot.GameStateSnapshot
        | UpdateStatus of string

    /// Domain payload for an outbound message to a scripting client. The
    /// production `ScriptingAdapter` translates this into a proto
    /// `FSBarV2.Broker.Contracts.StateMsg` on the wire — `Broker.Mvu`
    /// itself stays free of proto-generated types.
    type ScriptingOutboundMsg =
        | Snapshot of Snapshot.GameStateSnapshot
        | SessionEnd of Session.EndReason

    /// Schedule kind for `ScheduleTick`. Polymorphic over `'msg` to
    /// avoid a circular dependency between `Cmd` and `Msg`. Production
    /// code instantiates `TimerSchedule<Msg.Msg>`. One-shot timers fire
    /// once and remove themselves from `Model.timers`; recurring timers
    /// stay registered until `CancelTimer` is dispatched.
    type TimerSchedule<'msg> =
        | OneShot of timerId:TimerId * delayMs:int * msg:'msg
        | Recurring of timerId:TimerId * intervalMs:int * msg:'msg

    /// Every side effect the broker can perform. Inert data; the runtime
    /// executes it. Polymorphic over `'msg` to keep `Cmd` and `Msg`
    /// independently compilable; `update` returns
    /// `Cmd<Msg> list` (see `Update.fsi`). See research §4 and
    /// data-model §1.8.
    type Cmd<'msg> =
        | NoOp
        | Batch of Cmd<'msg> list
        | AuditCmd of Audit.AuditEvent
        | CoordinatorOutbound of CommandPipeline.Command
        | ScriptingOutbound of clientId:ScriptingClientId * msg:ScriptingOutboundMsg
        | ScriptingReject of clientId:ScriptingClientId * reason:RejectReason
        | VizCmd of VizOp
        | ScheduleTick of schedule:TimerSchedule<'msg>
        | CancelTimer of TimerId
        | EndSession of reason:Session.EndReason
        | Quit of exitCode:int
        | CompleteRpc of rpcId:RpcId * result:RpcResult

    /// Convenience: combine many Cmds into a single `Batch` arm,
    /// flattening nested `Batch`es and dropping `NoOp`s.
    val batch : Cmd<'msg> list -> Cmd<'msg>

    /// Convenience: NoOp constant.
    val none<'msg> : Cmd<'msg>
