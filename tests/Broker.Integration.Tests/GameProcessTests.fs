(* SYNTHETIC FIXTURE: this whole file exercises the broker's GameProcess
 * launcher against stand-in OS processes (sleep, /bin/false, /usr/bin/env)
 * because the real HighBarV3 game executable is not provisioned on the
 * dev / CI machines. The wire-up to a real game is part of the HighBarV3
 * upstream workstream — see Synthetic-Evidence Inventory entry T035. *)
module Broker.Integration.Tests.GameProcessTests

open System
open System.Threading
open Expecto
open Broker.Core
open Broker.App

let private sleepExe = "/usr/bin/sleep"
let private falseExe = "/usr/bin/false"

[<Tests>]
let gameProcessTests =
    testList "GameProcess (FR-012, FR-027)" [

        test "argsFor_Headless appends --headless to the base args" {
            // FR-012: launch flag matches LobbyConfig.display.
            let args = GameProcess.argsFor [ "--map"; "Tabula" ] Lobby.Headless
            Expect.equal args [ "--map"; "Tabula"; "--headless" ] "--headless appended"
        }

        test "argsFor_Graphical appends --graphical to the base args" {
            let args = GameProcess.argsFor [ "--map"; "Tabula" ] Lobby.Graphical
            Expect.equal args [ "--map"; "Tabula"; "--graphical" ] "--graphical appended"
        }

        test "start_missing executable returns Error without throwing" {
            // Edge case: configured game executable is missing → fail fast
            // with a clear message; do not throw.
            match GameProcess.start "/nonexistent/path/to/game" [] with
            | Error msg ->
                Expect.stringContains msg "/nonexistent/path/to/game" "error mentions the missing path"
            | Ok _ -> failtest "expected Error for nonexistent exe"
        }

        test "start_empty exe path returns Error without spawning anything" {
            match GameProcess.start "" [] with
            | Error msg ->
                Expect.stringContains msg "empty" "error names the empty path"
            | Ok _ -> failtest "expected Error for empty exe"
        }

        test "Synthetic_start_then_Dispose kills the process and HasExited becomes true" {
            // SYNTHETIC: stand-in sleep(60s) — kept long enough that we
            // observe pre-dispose HasExited = false.
            match GameProcess.start sleepExe [ "60" ] with
            | Error e -> failtestf "start sleep 60 should succeed; got %s" e
            | Ok handle ->
                Expect.isFalse handle.HasExited "sleep is still running"
                Expect.isGreaterThan handle.Pid 0 "pid is set"
                handle.Dispose()
                Thread.Sleep(200)   // let the OS reap the process
                Expect.isTrue handle.HasExited "process dead after Dispose"
        }

        test "Synthetic_OnExited fires on external termination (FR-027)" {
            // The proxy AI / game crashing externally must be visible to
            // the broker so it can emit Ended(GameCrashed).
            match GameProcess.start falseExe [] with
            | Error e -> failtestf "start /usr/bin/false should succeed; got %s" e
            | Ok handle ->
                let observed = new ManualResetEventSlim(false)
                let mutable code = -1
                handle.OnExited (fun ec ->
                    code <- ec
                    observed.Set())
                Expect.isTrue (observed.Wait(2000)) "OnExited fired within 2 s"
                Expect.equal code 1 "/usr/bin/false exits with code 1"
                handle.Dispose()
        }

        test "Synthetic_OnExited registered after exit fires immediately" {
            match GameProcess.start falseExe [] with
            | Error e -> failtestf "start /usr/bin/false should succeed; got %s" e
            | Ok handle ->
                // Wait for the process to leave before registering.
                let mutable spins = 0
                while not handle.HasExited && spins < 50 do
                    Thread.Sleep(40); spins <- spins + 1
                Expect.isTrue handle.HasExited "process exited"
                let mutable code = -99
                handle.OnExited (fun ec -> code <- ec)
                Expect.equal code 1 "callback fired synchronously with the exit code"
                handle.Dispose()
        }
    ]
    |> testSequenced
