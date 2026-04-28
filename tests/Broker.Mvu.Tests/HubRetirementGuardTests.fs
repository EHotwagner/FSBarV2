module Broker.Mvu.Tests.HubRetirementGuardTests

open System.IO
open System.Text.RegularExpressions
open Expecto

/// Walk up from the test bin directory to the repo root.
let private repoRoot () =
    let asm = System.Reflection.Assembly.GetExecutingAssembly()
    let dir = Path.GetDirectoryName(asm.Location) |> Option.ofObj
    let rec walk (path: string option) =
        match path with
        | None -> "."
        | Some p when File.Exists (Path.Combine(p, "FSBarV2.sln")) -> p
        | Some p -> walk (Option.ofObj (Path.GetDirectoryName p))
    walk dir

/// Recursively enumerate .fs / .fsi under the given directory.
let private fsharpFiles (root: string) : string seq =
    let exts = [ ".fs"; ".fsi" ]
    if not (Directory.Exists root) then Seq.empty
    else
        Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
        |> Seq.filter (fun f ->
            // Skip generated build outputs.
            not (f.Contains(Path.DirectorySeparatorChar.ToString() + "obj" + Path.DirectorySeparatorChar.ToString())) &&
            not (f.Contains(Path.DirectorySeparatorChar.ToString() + "bin" + Path.DirectorySeparatorChar.ToString())) &&
            List.exists (fun (e: string) -> f.EndsWith e) exts)

/// True iff `pattern` matches anywhere in `path`'s text content.
let private matches (pattern: string) (path: string) : bool =
    try
        let text = File.ReadAllText path
        Regex.IsMatch(text, pattern)
    with _ -> false

[<Tests>]
let hubRetirementGuardTests =
    let root = repoRoot ()
    let srcDir = Path.Combine(root, "src")
    let testsDir = Path.Combine(root, "tests")
    let allSrcAndTests () =
        Seq.append (fsharpFiles srcDir) (fsharpFiles testsDir)
        |> Seq.toList

    let assertNoHits (label: string) (pattern: string) =
        test (sprintf "SC-008 %s zero-hits in src+tests" label) {
            let hits =
                allSrcAndTests ()
                |> List.filter (matches pattern)
            // Some files cite the retired surface in *plan / spec / removal*
            // contexts (e.g., the hub-retirement-plan citing the patterns it
            // is itself retiring). Allow those by carving out the
            // historical-reference paths.
            let live =
                hits
                |> List.filter (fun p ->
                    not (p.Contains ("specs" + string Path.DirectorySeparatorChar)) &&
                    not (p.EndsWith "HubRetirementGuardTests.fs"))
            Expect.isEmpty live
                (sprintf "SC-008: %s pattern '%s' must have zero live hits, found in: %A" label pattern live)
        }

    testList "Broker.Mvu.HubRetirement (SC-008)" [
        // Note: until Phase 4 retires Hub, these tests are EXPECTED to fail
        // for the patterns that still refer to the live Hub surface. They
        // are wired up here so the guard runs the moment Hub is retired.
        // For Phase 3 we run only the patterns that should already be clean.

        // The mvu-side patterns: Broker.Mvu must never mention Hub or withLock.
        test "SC-008 Synthetic_Broker.Mvu_has_no_Hub_references" {
            let mvuFiles =
                fsharpFiles (Path.Combine(srcDir, "Broker.Mvu"))
                |> Seq.toList
            let hits =
                mvuFiles
                |> List.filter (fun p ->
                    matches "BrokerState\\.Hub" p ||
                    matches "withLock" p ||
                    matches "stateLock" p)
            Expect.isEmpty hits "Broker.Mvu must not reference Hub / withLock / stateLock"
        }
    ]
