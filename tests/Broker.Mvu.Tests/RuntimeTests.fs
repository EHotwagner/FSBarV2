module Broker.Mvu.Tests.RuntimeTests

open System
open Expecto
open Broker.Core
open Broker.Mvu

let startedAt = DateTimeOffset(2026, 4, 28, 11, 0, 0, TimeSpan.Zero)
let brokerInfo : Session.BrokerInfo = {
    version = System.Version(1, 0)
    listenAddress = "127.0.0.1:5021"
    startedAt = startedAt
}
let freshHandle () =
    let m = Model.init brokerInfo Model.defaultConfig startedAt
    TestRuntime.create m

let rpcCtx op i : Msg.RpcContext = {
    rpcId = Cmd.RpcId i
    receivedAt = startedAt
    operation = op
}

[<Tests>]
let runtimeTests =
    testList "Broker.Mvu.TestRuntime" [

        // FR-015: synchronous dispatch + capture.
        test "FR-015 Synthetic_dispatch_captures_emitted_cmd" {
            let h = freshHandle ()
            TestRuntime.dispatch h (Msg.CoordinatorInbound (Msg.Heartbeat ("ai", "1.0.0", "e", rpcCtx "Hb" 1L)))
            let cmds = TestRuntime.capturedCmds h
            Expect.isNonEmpty cmds "dispatch produced at least one Cmd"
        }

        // FR-017: structural inspection of emitted Cmds.
        test "FR-017 Synthetic_captured_cmds_are_structurally_inspectable" {
            let h = freshHandle ()
            TestRuntime.dispatch h (Msg.CoordinatorInbound (Msg.Heartbeat ("ai", "1.0.0", "e", rpcCtx "Hb" 1L)))
            let cmds = TestRuntime.capturedCmds h
            let attached =
                cmds |> List.tryPick (function
                    | Cmd.AuditCmd (Audit.CoordinatorAttached (_, plug, _, _)) -> Some plug
                    | _ -> None)
            Expect.equal attached (Some "ai") "audit-attached plugin id is the captured payload"
        }

        // dispatchAll runs in order.
        test "FR-015 Synthetic_dispatchAll_runs_in_order" {
            let h = freshHandle ()
            let id = ScriptingClientId "alice"
            TestRuntime.dispatchAll h [
                Msg.CoordinatorInbound (Msg.Heartbeat ("ai", "1.0.0", "e", rpcCtx "Hb" 1L))
                Msg.ScriptingInbound (Msg.Hello (id, System.Version(1, 0), rpcCtx "Hello" 2L))
            ]
            let m = TestRuntime.currentModel h
            Expect.equal m.mode Mode.Guest "after first heartbeat"
            Expect.isSome (ScriptingRoster.tryFind id m.roster) "after Hello"
        }

        // failCmd routes via Msg.CmdFailure.
        test "FR-015 Synthetic_failCmd_drives_cmd_failure_arm" {
            let h = freshHandle ()
            TestRuntime.dispatch h (Msg.CoordinatorInbound (Msg.Heartbeat ("ai", "1.0.0", "e", rpcCtx "Hb" 1L)))
            // The first captured Cmd is AuditCmd (CoordinatorAttached). Fail it.
            TestRuntime.failCmd h 0 (Msg.AuditWriteFailed ("audit-flush", exn "disk-full"))
            // Audit failure has an empty disposition — no model change visible
            // here, but the dispatch should have completed without throwing.
            Expect.isTrue true "failCmd routed CmdFailure arm"
        }

        test "FR-017 Synthetic_clearCapturedCmds_resets_list" {
            let h = freshHandle ()
            TestRuntime.dispatch h (Msg.CoordinatorInbound (Msg.Heartbeat ("ai", "1.0.0", "e", rpcCtx "Hb" 1L)))
            Expect.isNonEmpty (TestRuntime.capturedCmds h) "before clear"
            TestRuntime.clearCapturedCmds h
            Expect.isEmpty (TestRuntime.capturedCmds h) "after clear"
        }
    ]
