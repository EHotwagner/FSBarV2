module Broker.Tui.Tests.LobbyViewTests

open System
open Expecto
open Broker.Core
open Broker.Tui

let private key (k: ConsoleKey) = ConsoleKeyInfo(' ', k, false, false, false)

let private slot idx kind team : ParticipantSlot.ParticipantSlot =
    { slotIndex = idx
      kind = kind
      team = team
      boundClient = None }

let private draft : Lobby.LobbyConfig =
    { mapName = "Tabula"
      gameMode = "Skirmish"
      participants =
        [ slot 0 ParticipantSlot.Human 0
          slot 1 ParticipantSlot.ProxyAi 1 ]
      display = Lobby.Headless }

[<Tests>]
let lobbyViewTests =
    testList "Broker.Tui.LobbyView (US2 §3)" [
        test "render produces a non-null Spectre Layout" {
            let layout = LobbyView.render draft
            Expect.isNotNull (box layout) "render returns a Layout"
        }

        test "render does not throw on empty participant list" {
            // The lobby may be opened before any slots are added.
            let empty = { draft with participants = [] }
            let layout = LobbyView.render empty
            Expect.isNotNull (box layout) "empty draft renders"
        }

        test "apply_D toggles display Headless -> Graphical" {
            let next = LobbyView.apply (key ConsoleKey.D) draft
            Expect.equal next.display Lobby.Graphical "Headless -> Graphical"
        }

        test "apply_D toggles display Graphical -> Headless" {
            let graphical = { draft with display = Lobby.Graphical }
            let next = LobbyView.apply (key ConsoleKey.D) graphical
            Expect.equal next.display Lobby.Headless "Graphical -> Headless"
        }

        test "apply_unbound key returns the draft unchanged" {
            let next = LobbyView.apply (key ConsoleKey.F1) draft
            Expect.equal next draft "F1 leaves draft alone"
        }

        test "apply_does not mutate other fields" {
            let next = LobbyView.apply (key ConsoleKey.D) draft
            Expect.equal next.mapName     draft.mapName     "mapName unchanged"
            Expect.equal next.gameMode    draft.gameMode    "gameMode unchanged"
            Expect.equal next.participants draft.participants "participants unchanged"
        }
    ]
