module Broker.Mvu.Tests.CarveoutT037Tests

open System
open Expecto
open Broker.Core
open Broker.Mvu

/// T037 — host-mode admin-command walkthrough MVU-replay (US1 acceptance #2).
/// Drives the OpenLobby + LaunchHostSession + Hello + admin-elevate
/// sequence through the test runtime and asserts the resulting Model
/// reflects the host-mode session, the elevated client, and the audit
/// Cmd list contains entries for each admin transition.
[<Tests>]
let carveoutT037Tests =
    testList "Broker.Mvu.Carveout-T037-host-admin-walkthrough" [
        test "Synthetic-T037-HostAdminWalkthrough_captures_lobby_launch_and_audit" {
            let startedAt = DateTimeOffset(2026, 4, 28, 10, 0, 0, TimeSpan.Zero)
            let info : Session.BrokerInfo = {
                version = System.Version(1, 0)
                listenAddress = "127.0.0.1:5021"
                startedAt = startedAt
            }
            let m0 = Model.init info Model.defaultConfig startedAt
            let h = TestRuntime.create m0
            let msgs = Testing.Fixtures.syntheticT037MsgSequence ()
            TestRuntime.dispatchAll h msgs
            let m = TestRuntime.currentModel h

            // After OpenLobby: pendingLobby set.
            // After LaunchHostSession: Mode.Hosting and a session.
            Expect.isTrue
                (match m.mode with Mode.Hosting _ -> true | _ -> false)
                "after Enter, mode → Hosting"
            Expect.isSome m.session "session built after launch"

            // After Hello: client-1 in roster.
            let id = ScriptingClientId "client-1"
            Expect.isSome (ScriptingRoster.tryFind id m.roster) "client-1 in roster"

            // After A: client-1 elevated.
            Expect.equal m.elevation (Some id) "client-1 elevated"

            // The audit trail contains ModeChanged (host launch), ClientConnected,
            // and AdminGranted.
            let cmds = TestRuntime.capturedCmds h
            let hasModeChanged =
                cmds |> List.exists (function
                    | Cmd.AuditCmd (Audit.ModeChanged _) -> true
                    | _ -> false)
            let hasClientConnected =
                cmds |> List.exists (function
                    | Cmd.AuditCmd (Audit.ClientConnected _) -> true
                    | _ -> false)
            let hasAdminGranted =
                cmds |> List.exists (function
                    | Cmd.AuditCmd (Audit.AdminGranted _) -> true
                    | _ -> false)
            Expect.isTrue hasModeChanged "audit ModeChanged emitted on host-launch"
            Expect.isTrue hasClientConnected "audit ClientConnected emitted on Hello"
            Expect.isTrue hasAdminGranted "audit AdminGranted emitted on elevation"
        }
    ]
