module Broker.Mvu.Tests.WorkedExampleTests

open System
open Expecto
open Broker.Core
open Broker.Mvu

/// US2 SC-005 worked example — adding a TUI hotkey from `Msg` case to passing
/// test in fewer than 100 lines. The hotkey under test is `K` (kick the
/// currently-elevated scripting client). The whole feature lives in:
///   - HotkeyAction.KickElevatedClient (Update.fs)
///   - K-mapping in translateKey (Update.fs)
///   - applyHotkey clause emitting AdminRevoked + ScriptingReject (Update.fs)
///   - Model.kickedClients field (Model.fs / Model.fsi)
///   - this single-test file
///
/// SC-005 budget: count the lines in Update.fs added for this hotkey
/// (≤30) plus the Model.kickedClients field (1) plus this test (≤40)
/// — comfortably under 100 LOC.
[<Tests>]
let workedExampleTests =
    testList "Broker.Mvu.WorkedExample (US2 SC-005)" [
        test "WorkedExample_K_hotkey_in_host_mode_kicks_elevated_client" {
            let id = ScriptingClientId "alice"
            let m = Testing.Fixtures.syntheticHostModelElevated id
            Expect.equal m.elevation (Some id) "fixture starts with alice elevated"

            let h = TestRuntime.create m
            let kKey = ConsoleKeyInfo('K', ConsoleKey.K, false, false, false)
            TestRuntime.dispatch h (Msg.TuiInput (Msg.Keypress kKey))
            let m' = TestRuntime.currentModel h

            Expect.isTrue (Set.contains id m'.kickedClients) "client is marked kicked in Model"
            let cmds = TestRuntime.capturedCmds h
            let hasReject =
                cmds |> List.exists (function
                    | Cmd.ScriptingReject (target, CommandPipeline.RejectReason.AdminNotAvailable) when target = id -> true
                    | _ -> false)
            let hasAuditRevoke =
                cmds |> List.exists (function
                    | Cmd.AuditCmd (Audit.AdminRevoked _) -> true
                    | _ -> false)
            Expect.isTrue hasReject "ScriptingReject Cmd emitted for the kicked client"
            Expect.isTrue hasAuditRevoke "AdminRevoked audit Cmd emitted"
        }

        test "WorkedExample_K_hotkey_is_no_op_when_no_one_elevated" {
            let startedAt = DateTimeOffset(2026, 4, 28, 10, 0, 0, TimeSpan.Zero)
            let info : Session.BrokerInfo = { version = System.Version(1, 0); listenAddress = "x"; startedAt = startedAt }
            let lobby : Lobby.LobbyConfig =
                { mapName = "X"; gameMode = "Y"
                  participants = [ { slotIndex = 0; kind = ParticipantSlot.ProxyAi; team = 0; boundClient = None } ]
                  display = Lobby.Headless }
            let m =
                { Model.init info Model.defaultConfig startedAt with
                    mode = Mode.Hosting lobby }
            let h = TestRuntime.create m
            let kKey = ConsoleKeyInfo('K', ConsoleKey.K, false, false, false)
            TestRuntime.dispatch h (Msg.TuiInput (Msg.Keypress kKey))
            let m' = TestRuntime.currentModel h
            Expect.isEmpty m'.kickedClients "no client kicked when none elevated"
        }
    ]
