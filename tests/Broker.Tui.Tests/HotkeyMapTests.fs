module Broker.Tui.Tests.HotkeyMapTests

open System
open Expecto
open Broker.Core
open Broker.Tui
open Broker.Tui.HotkeyMap

let private key (k: ConsoleKey) = ConsoleKeyInfo(' ', k, false, false, false)

let private hostingMode : Mode.Mode =
    Mode.Mode.Hosting
        { mapName = "Tabula"
          gameMode = "Skirmish"
          participants = []
          display = Lobby.Headless }

[<Tests>]
let hotkeyMapTests =
    testList "HotkeyMap.map (US2 quickstart §3)" [
        test "Q maps to Quit in any mode" {
            for mode in [ Mode.Mode.Idle; Mode.Mode.Guest; hostingMode ] do
                Expect.equal (map (key ConsoleKey.Q) mode) Quit (sprintf "Q in %A" mode)
        }

        test "L maps to OpenLobby only in Idle" {
            Expect.equal (map (key ConsoleKey.L) Mode.Mode.Idle) OpenLobby "L in Idle"
            Expect.equal (map (key ConsoleKey.L) Mode.Mode.Guest) NoAction "L in Guest"
            Expect.equal (map (key ConsoleKey.L) hostingMode) NoAction "L in Hosting"
        }

        test "Enter maps to LaunchHostSession only in Hosting" {
            Expect.equal (map (key ConsoleKey.Enter) hostingMode) LaunchHostSession "Enter in Hosting"
            Expect.equal (map (key ConsoleKey.Enter) Mode.Mode.Idle) NoAction "Enter in Idle"
            Expect.equal (map (key ConsoleKey.Enter) Mode.Mode.Guest) NoAction "Enter in Guest"
        }

        test "Space maps to TogglePause only in Hosting" {
            Expect.equal (map (key ConsoleKey.Spacebar) hostingMode) TogglePause "Space in Hosting"
            Expect.equal (map (key ConsoleKey.Spacebar) Mode.Mode.Guest) NoAction "Space in Guest"
        }

        test "+/- maps to StepSpeed only in Hosting" {
            match map (key ConsoleKey.OemPlus) hostingMode with
            | StepSpeed d when d > 0m -> ()
            | other -> failtestf "expected StepSpeed +; got %A" other
            match map (key ConsoleKey.OemMinus) hostingMode with
            | StepSpeed d when d < 0m -> ()
            | other -> failtestf "expected StepSpeed -; got %A" other
            Expect.equal (map (key ConsoleKey.OemPlus) Mode.Mode.Idle) NoAction "+ in Idle"
            Expect.equal (map (key ConsoleKey.OemMinus) Mode.Mode.Guest) NoAction "- in Guest"
        }

        test "A maps to OpenElevatePrompt only in Hosting" {
            // FR-016: admin elevation is host-mode only.
            Expect.equal (map (key ConsoleKey.A) hostingMode) OpenElevatePrompt "A in Hosting"
            Expect.equal (map (key ConsoleKey.A) Mode.Mode.Guest) NoAction "A in Guest"
            Expect.equal (map (key ConsoleKey.A) Mode.Mode.Idle) NoAction "A in Idle"
        }

        test "X maps to EndSession in any active mode but not Idle" {
            Expect.equal (map (key ConsoleKey.X) hostingMode) EndSession "X in Hosting"
            Expect.equal (map (key ConsoleKey.X) Mode.Mode.Guest) EndSession "X in Guest"
            Expect.equal (map (key ConsoleKey.X) Mode.Mode.Idle) NoAction "X in Idle (no session)"
        }

        test "V maps to ToggleViz in any mode" {
            for mode in [ Mode.Mode.Idle; Mode.Mode.Guest; hostingMode ] do
                Expect.equal (map (key ConsoleKey.V) mode) ToggleViz (sprintf "V in %A" mode)
        }

        test "unbound key returns NoAction" {
            Expect.equal (map (key ConsoleKey.F1) Mode.Mode.Idle) NoAction "F1 unbound"
        }
    ]
