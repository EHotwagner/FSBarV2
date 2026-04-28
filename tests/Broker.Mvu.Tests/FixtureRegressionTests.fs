module Broker.Mvu.Tests.FixtureRegressionTests

open System
open System.IO
open System.Reflection
open Expecto
open Broker.Mvu

/// US5 — snapshot regression fixtures. Renders three known Models to
/// deterministic strings and asserts equality with checked-in `.txt`
/// fixtures under `Fixtures/`.
let private fixtureDir =
    let asm = Assembly.GetExecutingAssembly()
    let dir = Path.GetDirectoryName(asm.Location) |> Option.ofObj |> Option.defaultValue "."
    Path.Combine(dir, "Fixtures")

let private regenerate =
    let v = System.Environment.GetEnvironmentVariable "BROKER_REGENERATE_VIEW_FIXTURES"
    not (System.String.IsNullOrEmpty v) && v <> "0"

let private oneFixtureTest (label: string) (fixtureFile: string) (model: Model.Model) : Test =
    test (sprintf "Fixture %s matches checked-in render" label) {
        Directory.CreateDirectory fixtureDir |> ignore
        let path = Path.Combine(fixtureDir, fixtureFile)
        let actual = View.renderToString 120 50 model
        if regenerate then
            File.WriteAllText(path, actual)
        else
            Expect.isTrue (File.Exists path) (sprintf "fixture %s exists" fixtureFile)
            let expected = File.ReadAllText path
            Expect.equal actual expected
                (sprintf "View.renderToString drift detected for %s. Re-run with BROKER_REGENERATE_VIEW_FIXTURES=1 after intentional changes." fixtureFile)
    }

[<Tests>]
let fixtureRegressionTests =
    testList "Broker.Mvu.FixtureRegression (US5)" [
        oneFixtureTest
            "dashboard-guest-2clients"
            "dashboard-guest-2clients.txt"
            (Testing.Fixtures.syntheticGuestModel 2 100L)

        oneFixtureTest
            "dashboard-host-elevated"
            "dashboard-host-elevated.txt"
            (Testing.Fixtures.syntheticHostModelElevated (Broker.Core.ScriptingClientId "alice"))

        oneFixtureTest
            "viz-active-footer"
            "viz-active-footer.txt"
            (let m = Testing.Fixtures.syntheticGuestModel 1 50L
             { m with viz = Model.Active (DateTimeOffset(2026, 4, 28, 12, 0, 0, TimeSpan.Zero), "viz active") })
    ]
