module Broker.Core.Tests.ScriptingRosterTests

open System
open Expecto
open Broker.Core

let private now () = DateTimeOffset.UtcNow
let private v = System.Version(1, 0)
let private id name = ScriptingClientId name

[<Tests>]
let scriptingRosterTests =
    testList "ScriptingRoster" [
        test "tryAdd_first connect succeeds with isAdmin = false" {
            // FR-016: every new connection is non-admin until the operator
            // explicitly grants admin from the TUI.
            let r = ScriptingRoster.tryAdd (id "alice") v (now()) ScriptingRoster.empty
            match r with
            | Ok roster ->
                Expect.isFalse (ScriptingRoster.isAdmin (id "alice") roster) "default isAdmin must be false"
            | Error e -> failtestf "expected first add to succeed; got %A" e
        }

        test "tryAdd_collision is rejected with NameInUse" {
            // FR-008: name uniqueness across currently-connected clients.
            let r0 = ScriptingRoster.tryAdd (id "alice") v (now()) ScriptingRoster.empty
            let r1 =
                match r0 with
                | Ok roster -> ScriptingRoster.tryAdd (id "alice") v (now()) roster
                | Error e -> failtestf "first add unexpectedly failed: %A" e
            Expect.equal r1 (Error ScriptingRoster.NameInUse) "second connect with same name → NameInUse"
        }

        test "remove_then_re-add_is allowed (uniqueness is among live clients only)" {
            let r0 =
                ScriptingRoster.tryAdd (id "alice") v (now()) ScriptingRoster.empty
                |> function Ok r -> r | Error e -> failtestf "%A" e
            let r1 = ScriptingRoster.remove (id "alice") r0
            let r2 = ScriptingRoster.tryAdd (id "alice") v (now()) r1
            match r2 with
            | Ok _ -> ()
            | Error e -> failtestf "name should be reusable after remove; got %A" e
        }

        test "grantAdmin_on present client flips isAdmin" {
            // FR-016: operator grants admin per client; in-memory only.
            let r0 =
                ScriptingRoster.tryAdd (id "alice") v (now()) ScriptingRoster.empty
                |> function Ok r -> r | Error e -> failtestf "%A" e
            let r1 =
                ScriptingRoster.grantAdmin (id "alice") r0
                |> function Ok r -> r | Error e -> failtestf "grant should succeed: %A" e
            Expect.isTrue (ScriptingRoster.isAdmin (id "alice") r1) "isAdmin after grant"
        }

        test "grantAdmin_on missing client returns NotFound" {
            Expect.equal
                (ScriptingRoster.grantAdmin (id "nobody") ScriptingRoster.empty)
                (Error (ScriptingRoster.NotFound (id "nobody")))
                "grant against an unknown id rejects"
        }

        test "revokeAdmin_returns isAdmin to false" {
            let roster =
                ScriptingRoster.empty
                |> ScriptingRoster.tryAdd (id "alice") v (now())
                |> function Ok r -> r | Error e -> failtestf "%A" e
                |> ScriptingRoster.grantAdmin (id "alice")
                |> function Ok r -> r | Error e -> failtestf "%A" e
                |> ScriptingRoster.revokeAdmin (id "alice")
                |> function Ok r -> r | Error e -> failtestf "%A" e
            Expect.isFalse (ScriptingRoster.isAdmin (id "alice") roster) "isAdmin after revoke"
        }

        test "fresh empty_roster has no admins (Invariant 3)" {
            // Invariant 3 from data-model: on broker startup every client
            // has isAdmin = false. We use `empty` as the broker-startup
            // proxy.
            Expect.equal (ScriptingRoster.toList ScriptingRoster.empty) [] "empty roster is empty"
        }
    ]
