module Broker.Core.Tests.LobbyTests

open Expecto
open Broker.Core
open Broker.Core.Lobby

let private slot idx kind team : ParticipantSlot.ParticipantSlot =
    { slotIndex = idx
      kind = kind
      team = team
      boundClient = None }

let private cfg participants : LobbyConfig =
    { mapName = "Tabula"
      gameMode = "Skirmish"
      participants = participants
      display = Headless }

[<Tests>]
let lobbyTests =
    testList "Lobby.validate (FR-013)" [
        test "validate_valid config without clients is Ok" {
            let c =
                cfg [ slot 0 ParticipantSlot.Human 0
                      slot 1 ParticipantSlot.ProxyAi 1 ]
            Expect.equal (validate c []) (Ok c) "well-formed config validates"
        }

        test "validate_empty mapName is EmptyMapName" {
            let c = { (cfg []) with mapName = "" }
            Expect.equal (validate c []) (Error EmptyMapName) "empty map → EmptyMapName"
        }

        test "validate_whitespace mapName is EmptyMapName" {
            let c = { (cfg []) with mapName = "   " }
            Expect.equal (validate c []) (Error EmptyMapName) "whitespace map → EmptyMapName"
        }

        test "validate_empty gameMode is EmptyGameMode" {
            let c = { (cfg []) with gameMode = "" }
            Expect.equal (validate c []) (Error EmptyGameMode) "empty mode → EmptyGameMode"
        }

        test "validate_duplicate slotIndex is DuplicateSlotIndex" {
            // Slot 0 used by both Human and ProxyAi → conflict.
            let c =
                cfg [ slot 0 ParticipantSlot.Human 0
                      slot 0 ParticipantSlot.ProxyAi 1 ]
            Expect.equal
                (validate c [])
                (Error (DuplicateSlotIndex 0))
                "duplicate slotIndex 0 → DuplicateSlotIndex 0"
        }

        test "validate_too many participants is TooManyParticipants" {
            // hardCapacity is 16 (HighBarV3 lobby max).
            let participants =
                [ for i in 0 .. 16 -> slot i ParticipantSlot.Human 0 ]
            match validate (cfg participants) [] with
            | Error (TooManyParticipants (cap, actual)) ->
                Expect.equal cap 16 "capacity reported correctly"
                Expect.equal actual 17 "actual count reported correctly"
            | other -> failtestf "expected TooManyParticipants; got %A" other
        }

        test "validate_connected client with no ProxyAi slot is MissingProxySlotForBoundClient" {
            // Quickstart §3 step 2: alice-bot is connected; lobby has no
            // ProxyAi slot → must refuse launch.
            let c =
                cfg [ slot 0 ParticipantSlot.Human 0
                      slot 1 (ParticipantSlot.BuiltInAi 5) 1 ]
            let clients = [ ScriptingClientId "alice-bot" ]
            match validate c clients with
            | Error (MissingProxySlotForBoundClient "alice-bot") -> ()
            | other ->
                failtestf "expected MissingProxySlotForBoundClient \"alice-bot\"; got %A" other
        }

        test "validate_connected client with a ProxyAi slot is Ok" {
            let c =
                cfg [ slot 0 ParticipantSlot.Human 0
                      slot 1 ParticipantSlot.ProxyAi 1 ]
            let clients = [ ScriptingClientId "alice-bot" ]
            Expect.equal (validate c clients) (Ok c) "ProxyAi slot present → Ok"
        }

        test "validate_no clients without ProxyAi slot is Ok" {
            // No connected clients → nothing to satisfy. Operator may run
            // a Human / BuiltInAi-only lobby for spec / replay purposes.
            let c =
                cfg [ slot 0 ParticipantSlot.Human 0
                      slot 1 (ParticipantSlot.BuiltInAi 3) 1 ]
            Expect.equal (validate c []) (Ok c) "no clients → no ProxyAi requirement"
        }
    ]
