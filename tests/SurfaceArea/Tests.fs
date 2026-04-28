module Broker.SurfaceArea.Tests

open System
open System.IO
open System.Reflection
open Expecto
open Broker.SurfaceArea.SurfaceWalker

/// Path to the checked-in baseline files. Each module gets its own:
///   tests/SurfaceArea/baselines/<CLR-full-name>.surface.txt
let baselineDir =
    let asmDir =
        match Option.ofObj (Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)) with
        | Some d -> d
        | None -> "."
    // bin/Debug/net10.0 -> ../../../baselines
    Path.GetFullPath(Path.Combine(asmDir, "..", "..", "..", "baselines"))

let private regenerate =
    let v = Environment.GetEnvironmentVariable "BROKER_REGENERATE_SURFACE_BASELINES"
    not (String.IsNullOrEmpty v) && v <> "0"

let private brokerAssemblies : Assembly list =
    [ typeof<Broker.Core.ScriptingClientId>.Assembly                  // Broker.Core
      typeof<Broker.Protocol.BackpressureGate.CommandAck>.Assembly    // Broker.Protocol
      typeof<Broker.Tui.Layout.Slot>.Assembly                         // Broker.Tui
      typeof<Broker.Viz.SceneBuilder.Scene>.Assembly                  // Broker.Viz
      typeof<Broker.App.Cli.Args>.Assembly                            // Broker.App
      typeof<Broker.Mvu.Cmd.RpcId>.Assembly ]                         // Broker.Mvu
    |> List.distinct

let private oneModuleTest (asm: Assembly) (moduleName: string) : Test =
    test (sprintf "Surface %s matches baseline" moduleName) {
        let actual = moduleSurface asm moduleName |> String.concat "\n"
        let baselinePath = Path.Combine(baselineDir, sprintf "%s.surface.txt" moduleName)
        if regenerate then
            Directory.CreateDirectory baselineDir |> ignore
            File.WriteAllText(baselinePath, actual + "\n")
            // Always pass in regenerate mode.
            ()
        else
            Expect.isTrue
                (File.Exists baselinePath)
                (sprintf "Baseline missing for %s. Re-run with BROKER_REGENERATE_SURFACE_BASELINES=1." moduleName)
            let expected = (File.ReadAllText baselinePath).TrimEnd()
            Expect.equal actual expected
                (sprintf "Public surface drift for %s. Inspect the diff and update the baseline together with the .fsi change." moduleName)
    }

[<Tests>]
let surfaceAreaTests =
    testList "SurfaceArea" [
        for asm in brokerAssemblies do
            for m in publicModules asm do
                oneModuleTest asm m
    ]
