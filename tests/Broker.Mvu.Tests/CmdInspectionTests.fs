module Broker.Mvu.Tests.CmdInspectionTests

open System
open Expecto
open Broker.Core
open Broker.Mvu

/// US4 — side effects (gRPC sends, audit writes, viewer ops) inspectable
/// in tests via the Cmd list.
[<Tests>]
let cmdInspectionTests =
    testList "Broker.Mvu.CmdInspection (US4)" [

        test "Synthetic_admin_elevation_emits_audit_AdminGranted" {
            let startedAt = DateTimeOffset(2026, 4, 28, 10, 0, 0, TimeSpan.Zero)
            let id = ScriptingClientId "alice"
            // Build a host-mode model with one client connected.
            let lobby : Lobby.LobbyConfig =
                { mapName = "X"; gameMode = "Y"
                  participants = [ { slotIndex = 0; kind = ParticipantSlot.ProxyAi; team = 0; boundClient = None } ]
                  display = Lobby.Headless }
            let session = Session.newHostSession lobby startedAt
            let info : Session.BrokerInfo = {
                version = System.Version(1, 0); listenAddress = "x"; startedAt = startedAt }
            let mutable r = ScriptingRoster.empty
            match ScriptingRoster.tryAdd id (System.Version(1, 0)) startedAt r with
            | Result.Ok r' -> r <- r'
            | _ -> ()
            let m =
                { Model.init info Model.defaultConfig startedAt with
                    mode = Mode.Hosting lobby
                    session = Some session
                    slots = lobby.participants
                    roster = r }
            let aKey = ConsoleKeyInfo('A', ConsoleKey.A, false, false, false)
            let h = TestRuntime.create m
            TestRuntime.dispatch h (Msg.TuiInput (Msg.Keypress aKey))
            let cmds = TestRuntime.capturedCmds h
            let hasGranted =
                cmds |> List.exists (function
                    | Cmd.AuditCmd (Audit.AdminGranted (_, granted, _)) when granted = id -> true
                    | _ -> false)
            Expect.isTrue hasGranted "AdminGranted Cmd captured for the elevated client"
        }

        test "Synthetic_admin_command_emits_coordinator_outbound_only_when_authorised" {
            let startedAt = DateTimeOffset(2026, 4, 28, 10, 0, 0, TimeSpan.Zero)
            let id = ScriptingClientId "alice"
            let m = Testing.Fixtures.syntheticHostModelElevated id
            let h = TestRuntime.create m
            let pauseCmd : CommandPipeline.Command =
                { commandId = Guid.NewGuid()
                  originatingClient = id
                  targetSlot = None
                  kind = CommandPipeline.Admin (CommandPipeline.AdminPayload.Pause)
                  submittedAt = startedAt }
            TestRuntime.dispatch h (Msg.ScriptingInbound (Msg.Command (id, pauseCmd)))
            let cmds = TestRuntime.capturedCmds h
            let hasOutbound =
                cmds |> List.exists (function
                    | Cmd.CoordinatorOutbound _ -> true
                    | _ -> false)
            Expect.isTrue hasOutbound "authorised admin command → CoordinatorOutbound"
        }

        test "Synthetic_unauthorised_command_emits_reject_and_audit" {
            let startedAt = DateTimeOffset(2026, 4, 28, 10, 0, 0, TimeSpan.Zero)
            let info : Session.BrokerInfo = { version = System.Version(1, 0); listenAddress = "x"; startedAt = startedAt }
            // Idle mode + no admin → admin command rejected.
            let id = ScriptingClientId "bob"
            let mutable r = ScriptingRoster.empty
            match ScriptingRoster.tryAdd id (System.Version(1, 0)) startedAt r with
            | Result.Ok r' -> r <- r'
            | _ -> ()
            let m = { Model.init info Model.defaultConfig startedAt with roster = r }
            let h = TestRuntime.create m
            let cmd : CommandPipeline.Command =
                { commandId = Guid.NewGuid()
                  originatingClient = id
                  targetSlot = None
                  kind = CommandPipeline.Admin (CommandPipeline.AdminPayload.Pause)
                  submittedAt = startedAt }
            TestRuntime.dispatch h (Msg.ScriptingInbound (Msg.Command (id, cmd)))
            let captured = TestRuntime.capturedCmds h
            let hasReject =
                captured |> List.exists (function
                    | Cmd.ScriptingReject _ -> true
                    | _ -> false)
            let hasAuditReject =
                captured |> List.exists (function
                    | Cmd.AuditCmd (Audit.CommandRejected _) -> true
                    | _ -> false)
            Expect.isTrue hasReject "unauthorised command → ScriptingReject Cmd"
            Expect.isTrue hasAuditReject "and audit CommandRejected"
        }
    ]
