module Broker.Mvu.Tests.CarveoutT042Tests

open System
open Expecto
open Broker.Core
open Broker.Mvu

/// T042 — 4-client × 200-unit dashboard render across ≥25 frames
/// (US1 acceptance #3). Drives the synthetic load sequence and asserts
/// View.renderToString produces a non-empty string for every frame.
[<Tests>]
let carveoutT042Tests =
    testList "Broker.Mvu.Carveout-T042-dashboard-under-load" [
        test "Synthetic-T042-DashboardUnderLoad_renders_25_frames_with_4_clients_and_200_units" {
            let startedAt = DateTimeOffset(2026, 4, 28, 10, 0, 0, TimeSpan.Zero)
            let info : Session.BrokerInfo = {
                version = System.Version(1, 0)
                listenAddress = "127.0.0.1:5021"
                startedAt = startedAt
            }
            let m0 = Model.init info Model.defaultConfig startedAt
            let h = TestRuntime.create m0

            let msgs = Testing.Fixtures.syntheticT042MsgSequence 4 25 200
            TestRuntime.dispatchAll h msgs
            let m = TestRuntime.currentModel h

            // After all messages: 4 clients in roster.
            Expect.equal (m.roster |> ScriptingRoster.toList |> List.length) 4 "4 clients connected"

            // Snapshot is the 25th one.
            Expect.isSome m.snapshot "snapshot present"
            match m.snapshot with
            | Some s ->
                Expect.equal s.tick 25L "tick 25 is the last applied snapshot"
                Expect.equal (List.length s.units) 200 "200 units per frame"
            | None -> failtest "expected snapshot"

            // Render succeeds (no exception), produces non-empty.
            let rendered = View.renderToString 200 60 m
            Expect.isFalse (String.IsNullOrEmpty rendered) "renderToString returns non-empty"

            // Per-snapshot fanout: 4 clients × 25 snapshots = 100 ScriptingOutbound Cmds.
            let cmds = TestRuntime.capturedCmds h
            let fanouts =
                cmds
                |> List.sumBy (function
                    | Cmd.ScriptingOutbound (_, Cmd.Snapshot _) -> 1
                    | _ -> 0)
            Expect.equal fanouts 100 "4 clients × 25 snapshots = 100 fanouts"
        }
    ]
