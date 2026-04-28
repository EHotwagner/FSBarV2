module Broker.Mvu.Tests.CarveoutT046Tests

open System
open Expecto
open Broker.Core
open Broker.Mvu

/// T046 — viz status line in both vizEnabled=true and --no-viz modes
/// (US1 acceptance #4). Drives the V hotkey + CoordinatorAttached +
/// PushStateSnapshot sequence and asserts the View captures the expected
/// viz status line; in --no-viz mode, asserts no VizCmd was emitted.
[<Tests>]
let carveoutT046Tests =
    testList "Broker.Mvu.Carveout-T046-viz-status-line" [

        test "Synthetic-T046-VizActive_status_appears_in_render_when_viz_enabled" {
            let startedAt = DateTimeOffset(2026, 4, 28, 10, 0, 0, TimeSpan.Zero)
            let info : Session.BrokerInfo = {
                version = System.Version(1, 0)
                listenAddress = "127.0.0.1:5021"
                startedAt = startedAt
            }
            let cfg = { Model.defaultConfig with vizEnabled = true }
            let m0 = Model.init info cfg startedAt
            let h = TestRuntime.create m0
            let msgs = Testing.Fixtures.syntheticT046MsgSequence true
            TestRuntime.dispatchAll h msgs
            let m = TestRuntime.currentModel h

            // V hotkey: viz transitions Closed → Active.
            Expect.isTrue
                (match m.viz with Model.Active _ -> true | _ -> false)
                "viz active after V keypress"

            // VizCmd OpenWindow was emitted by the V keypress, then PushFrame
            // by the snapshot fanout (because viz is Active).
            let cmds = TestRuntime.capturedCmds h
            let hasOpen =
                cmds |> List.exists (function
                    | Cmd.VizCmd Cmd.OpenWindow -> true
                    | _ -> false)
            let hasPushFrame =
                cmds |> List.exists (function
                    | Cmd.VizCmd (Cmd.PushFrame _) -> true
                    | _ -> false)
            Expect.isTrue hasOpen "Cmd.VizCmd OpenWindow emitted on V"
            Expect.isTrue hasPushFrame "Cmd.VizCmd PushFrame emitted on snapshot while viz Active"

            let rendered = View.renderToString 200 60 m
            Expect.stringContains rendered "viz active" "viz status line appears in footer"
        }

        test "Synthetic-T046-NoViz_silent_no_op_when_viz_disabled" {
            let startedAt = DateTimeOffset(2026, 4, 28, 10, 0, 0, TimeSpan.Zero)
            let info : Session.BrokerInfo = {
                version = System.Version(1, 0)
                listenAddress = "127.0.0.1:5021"
                startedAt = startedAt
            }
            let cfg = { Model.defaultConfig with vizEnabled = false }
            let m0 = Model.init info cfg startedAt
            let h = TestRuntime.create m0
            let msgs = Testing.Fixtures.syntheticT046MsgSequence false
            TestRuntime.dispatchAll h msgs
            let m = TestRuntime.currentModel h

            // Viz stays Disabled — V hotkey was a no-op.
            Expect.equal m.viz Model.Disabled "viz remains Disabled with --no-viz"

            // No VizCmd emitted at all.
            let cmds = TestRuntime.capturedCmds h
            let hasViz =
                cmds |> List.exists (function
                    | Cmd.VizCmd _ -> true
                    | _ -> false)
            Expect.isFalse hasViz "no VizCmd emitted when viz disabled"

            // The footer renders the disabled status.
            let rendered = View.renderToString 200 60 m
            Expect.stringContains rendered "viz disabled" "viz disabled status line in footer"
        }
    ]
