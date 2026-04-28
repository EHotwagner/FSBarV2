module Broker.Tui.Tests.TickLoopDispatchTests

open System
open Expecto
open Broker.Core
open Broker.Tui

/// Minimal CoreFacade stub. Records every operator call so the dispatch
/// table can be asserted without standing up the gRPC server, the
/// roster, or Spectre's LiveDisplay.
type RecorderFacade() =
    let mutable mode : Mode.Mode = Mode.Mode.Idle
    let mutable roster : ScriptingRoster.Roster = ScriptingRoster.empty
    let calls = System.Collections.Generic.List<string>()

    member _.Calls = calls
    member _.SetMode(m) = mode <- m
    member _.SetRoster(r) = roster <- r

    interface Session.CoreFacade with
        member _.Mode() = mode
        member _.Roster() = roster
        member _.Slots() = []
        member _.BrokerVersion() = System.Version(1, 0)
        member _.OnSnapshot(_) = ()
        member _.OnClientConnected(_) = ()
        member _.OnClientDisconnected(_, _) = ()

        member _.OperatorOpenHost(_) =
            calls.Add("OpenHost"); Ok ()
        member _.OperatorLaunchHost() =
            calls.Add("LaunchHost"); Ok ()
        member _.OperatorTogglePause() =
            calls.Add("TogglePause"); Ok ()
        member _.OperatorStepSpeed(d) =
            calls.Add(sprintf "StepSpeed:%g" d); Ok ()
        member _.OperatorEndSession() =
            calls.Add("EndSession"); Ok ()
        member _.OperatorGrantAdmin(ScriptingClientId n) =
            calls.Add(sprintf "Grant:%s" n); Ok ()
        member _.OperatorRevokeAdmin(ScriptingClientId n) =
            calls.Add(sprintf "Revoke:%s" n); Ok ()

let private hostingMode : Mode.Mode =
    Mode.Mode.Hosting
        { mapName = "Tabula"
          gameMode = "Skirmish"
          participants = []
          display = Lobby.Headless }

let private clientWith name isAdmin : ScriptingRoster.ScriptingClient =
    { id = ScriptingClientId name
      connectedAt = DateTimeOffset.UtcNow
      protocolVersion = System.Version(1, 0)
      boundSlot = None
      isAdmin = isAdmin
      commandQueueDepth = 0 }

let private rosterFrom (clients: ScriptingRoster.ScriptingClient list) =
    clients
    |> List.fold
        (fun r c ->
            let r =
                ScriptingRoster.tryAdd c.id c.protocolVersion c.connectedAt r
                |> function Ok r -> r | Error e -> failwithf "%A" e
            if c.isAdmin then
                ScriptingRoster.grantAdmin c.id r
                |> function Ok r -> r | Error e -> failwithf "%A" e
            else r)
        ScriptingRoster.empty

[<Tests>]
let dispatchTests =
    testList "TickLoop.dispatch (US2 §3 / FR-016)" [

        test "Quit_NoAction_keep UI mode unchanged and call nothing" {
            let recorder = RecorderFacade()
            let next = TickLoop.dispatch (recorder :> Session.CoreFacade) TickLoop.Dashboard HotkeyMap.Quit
            Expect.equal next TickLoop.Dashboard "Quit leaves uiMode alone"
            Expect.equal recorder.Calls.Count 0 "Quit does not invoke operator methods"
        }

        test "OpenLobby_from_Dashboard switches to Lobby with default draft" {
            let recorder = RecorderFacade()
            let next = TickLoop.dispatch (recorder :> Session.CoreFacade) TickLoop.Dashboard HotkeyMap.OpenLobby
            match next with
            | TickLoop.Lobby draft ->
                Expect.isFalse (System.String.IsNullOrEmpty draft.mapName) "draft seeded"
            | other -> failtestf "expected Lobby; got %A" other
            Expect.equal recorder.Calls.Count 0 "no operator method called yet"
        }

        test "LaunchHostSession_from_Lobby calls OpenHost+LaunchHost and returns to Dashboard" {
            // FR-013: validation happens inside OperatorLaunchHost; the
            // dispatch only owns ordering: open then launch then flip UI.
            let recorder = RecorderFacade()
            let draft : Lobby.LobbyConfig =
                { mapName = "Tabula"
                  gameMode = "Skirmish"
                  participants = [ { slotIndex = 0; kind = ParticipantSlot.ProxyAi; team = 0; boundClient = None } ]
                  display = Lobby.Headless }
            let next =
                TickLoop.dispatch
                    (recorder :> Session.CoreFacade)
                    (TickLoop.Lobby draft)
                    HotkeyMap.LaunchHostSession
            Expect.equal next TickLoop.Dashboard "Launch returns to Dashboard"
            Expect.equal (List.ofSeq recorder.Calls) [ "OpenHost"; "LaunchHost" ] "open then launch"
        }

        test "TogglePause_calls TogglePause operator action" {
            let recorder = RecorderFacade()
            recorder.SetMode(hostingMode)
            let _ = TickLoop.dispatch (recorder :> Session.CoreFacade) TickLoop.Dashboard HotkeyMap.TogglePause
            Expect.equal (List.ofSeq recorder.Calls) [ "TogglePause" ] "toggle dispatched"
        }

        test "StepSpeed_carries the delta into the operator call" {
            let recorder = RecorderFacade()
            recorder.SetMode(hostingMode)
            let _ = TickLoop.dispatch (recorder :> Session.CoreFacade) TickLoop.Dashboard (HotkeyMap.StepSpeed 0.25m)
            Expect.equal (List.ofSeq recorder.Calls) [ "StepSpeed:0.25" ] "delta forwarded"
        }

        test "EndSession_calls EndSession operator action" {
            let recorder = RecorderFacade()
            let _ = TickLoop.dispatch (recorder :> Session.CoreFacade) TickLoop.Dashboard HotkeyMap.EndSession
            Expect.equal (List.ofSeq recorder.Calls) [ "EndSession" ] "end dispatched"
        }

        test "ElevateClient_calls GrantAdmin with the client id" {
            let recorder = RecorderFacade()
            let _ =
                TickLoop.dispatch
                    (recorder :> Session.CoreFacade)
                    TickLoop.Dashboard
                    (HotkeyMap.ElevateClient (ScriptingClientId "alice"))
            Expect.equal (List.ofSeq recorder.Calls) [ "Grant:alice" ] "grant on alice"
        }

        test "RevokeClient_calls RevokeAdmin with the client id" {
            let recorder = RecorderFacade()
            let _ =
                TickLoop.dispatch
                    (recorder :> Session.CoreFacade)
                    TickLoop.Dashboard
                    (HotkeyMap.RevokeClient (ScriptingClientId "alice"))
            Expect.equal (List.ofSeq recorder.Calls) [ "Revoke:alice" ] "revoke on alice"
        }

        test "OpenElevatePrompt_with_one_non-admin client grants that client" {
            let recorder = RecorderFacade()
            recorder.SetRoster(rosterFrom [ clientWith "alice" false ])
            let _ = TickLoop.dispatch (recorder :> Session.CoreFacade) TickLoop.Dashboard HotkeyMap.OpenElevatePrompt
            Expect.equal (List.ofSeq recorder.Calls) [ "Grant:alice" ] "lone client gets granted"
        }

        test "OpenElevatePrompt_with_one_admin client revokes" {
            let recorder = RecorderFacade()
            recorder.SetRoster(rosterFrom [ clientWith "alice" true ])
            let _ = TickLoop.dispatch (recorder :> Session.CoreFacade) TickLoop.Dashboard HotkeyMap.OpenElevatePrompt
            Expect.equal (List.ofSeq recorder.Calls) [ "Revoke:alice" ] "admin client gets revoked"
        }

        test "OpenElevatePrompt_with_zero_clients is a no-op" {
            let recorder = RecorderFacade()
            let _ = TickLoop.dispatch (recorder :> Session.CoreFacade) TickLoop.Dashboard HotkeyMap.OpenElevatePrompt
            Expect.equal recorder.Calls.Count 0 "no clients = no action"
        }

        test "OpenElevatePrompt_with_multiple_clients is a no-op until prompt UI lands" {
            let recorder = RecorderFacade()
            recorder.SetRoster(rosterFrom [ clientWith "alice" false; clientWith "bob" false ])
            let _ = TickLoop.dispatch (recorder :> Session.CoreFacade) TickLoop.Dashboard HotkeyMap.OpenElevatePrompt
            Expect.equal recorder.Calls.Count 0 "multiple clients require explicit selection — pending UI"
        }

        test "ToggleViz_does not call any operator action" {
            let recorder = RecorderFacade()
            let _ = TickLoop.dispatch (recorder :> Session.CoreFacade) TickLoop.Dashboard HotkeyMap.ToggleViz
            Expect.equal recorder.Calls.Count 0 "viz toggle is local to the TUI"
        }
    ]
