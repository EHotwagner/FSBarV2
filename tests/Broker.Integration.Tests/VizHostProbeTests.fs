module Broker.Integration.Tests.VizHostProbeTests

open System
open Expecto
open Broker.Viz

/// Snapshot/restore the env vars probe reads, so each test runs in a
/// known display posture without leaking onto its peers.
type private DisplayState =
    { display: string | null
      wayland: string | null }

let private snapshot () : DisplayState =
    { display = Environment.GetEnvironmentVariable "DISPLAY"
      wayland = Environment.GetEnvironmentVariable "WAYLAND_DISPLAY" }

let private restore (s: DisplayState) =
    Environment.SetEnvironmentVariable("DISPLAY", s.display)
    Environment.SetEnvironmentVariable("WAYLAND_DISPLAY", s.wayland)

let private withEnv (display: string | null) (wayland: string | null) (f: unit -> 'a) : 'a =
    let saved = snapshot ()
    Environment.SetEnvironmentVariable("DISPLAY", display)
    Environment.SetEnvironmentVariable("WAYLAND_DISPLAY", wayland)
    try f () finally restore saved

// These tests mutate process-global env vars. Mark sequenced so
// Expecto's parallel scheduler doesn't race the snapshot/restore window.
[<Tests>]
let probeTests =
    testSequenced <| testList "Viz.VizHost.probe" [

        // The probe is platform-aware. On Linux/FreeBSD it inspects the
        // standard X11 / Wayland environment variables. On macOS and
        // Windows it returns Ok unconditionally — those platforms always
        // have a graphical session per spec assumptions. We run on Linux
        // in CI / dev, so the env-var paths are the ones we drive.

        test "probe_with_DISPLAY_set_returns_Ok_on_Linux" {
            if OperatingSystem.IsLinux() then
                withEnv ":99" null (fun () ->
                    match VizHost.probe () with
                    | Ok () -> ()
                    | Error e -> failtestf "expected Ok with DISPLAY=:99, got Error %s" e)
        }

        test "probe_with_only_WAYLAND_DISPLAY_returns_Ok_on_Linux" {
            if OperatingSystem.IsLinux() then
                withEnv null "wayland-0" (fun () ->
                    match VizHost.probe () with
                    | Ok () -> ()
                    | Error e -> failtestf "expected Ok with WAYLAND_DISPLAY, got Error %s" e)
        }

        test "probe_with_neither_DISPLAY_nor_WAYLAND_returns_Error_on_Linux" {
            if OperatingSystem.IsLinux() then
                withEnv null null (fun () ->
                    match VizHost.probe () with
                    | Ok () ->
                        failtest "expected Error on headless host (no DISPLAY/WAYLAND_DISPLAY)"
                    | Error msg ->
                        // SC-008: the message has to be informative enough
                        // to surface in the dashboard footer.
                        Expect.isTrue
                            (msg.Contains "DISPLAY" || msg.Contains "display")
                            (sprintf "error message should mention DISPLAY: %s" msg))
        }

        test "probe_treats_empty_DISPLAY_as_headless_on_Linux" {
            if OperatingSystem.IsLinux() then
                withEnv "" "" (fun () ->
                    match VizHost.probe () with
                    | Ok () -> failtest "empty DISPLAY/WAYLAND should be treated as headless"
                    | Error _ -> ())
        }

        test "probe_on_macOS_or_Windows_always_returns_Ok" {
            if OperatingSystem.IsMacOS() || OperatingSystem.IsWindows() then
                match VizHost.probe () with
                | Ok () -> ()
                | Error e -> failtestf "expected Ok on graphical platform, got Error %s" e
        }
    ]
