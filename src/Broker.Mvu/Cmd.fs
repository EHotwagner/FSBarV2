namespace Broker.Mvu

open Broker.Core
open FSBarV2.Broker.Contracts

module Cmd =

    type RpcId = RpcId of int64

    type TimerId = TimerId of int64

    type RpcResult =
        | Ok
        | Fault of exn:exn

    type RejectReason = CommandPipeline.RejectReason


    type VizOp =
        | OpenWindow
        | CloseWindow
        | PushFrame of Snapshot.GameStateSnapshot
        | UpdateStatus of string

    type ScriptingOutboundMsg =
        | Snapshot of Snapshot.GameStateSnapshot
        | SessionEnd of Session.EndReason

    type TimerSchedule<'msg> =
        | OneShot of timerId:TimerId * delayMs:int * msg:'msg
        | Recurring of timerId:TimerId * intervalMs:int * msg:'msg

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

    let none<'msg> : Cmd<'msg> = NoOp

    let batch (cmds: Cmd<'msg> list) : Cmd<'msg> =
        let rec flatten acc = function
            | [] -> List.rev acc
            | NoOp :: rest -> flatten acc rest
            | Batch xs :: rest -> flatten acc (xs @ rest)
            | c :: rest -> flatten (c :: acc) rest
        match flatten [] cmds with
        | [] -> NoOp
        | [ single ] -> single
        | many -> Batch many
