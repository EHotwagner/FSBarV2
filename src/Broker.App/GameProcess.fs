namespace Broker.App

open System
open System.Diagnostics
open Broker.Core

module GameProcess =

    type Handle =
        inherit IDisposable
        abstract Pid : int
        abstract HasExited : bool
        abstract ExitCode : int option
        abstract OnExited : (int -> unit) -> unit

    let argsFor (baseArgs: string list) (display: Lobby.Display) : string list =
        // Convention: the broker passes a single display flag the game
        // recognises. HighBarV3 uses --headless / --graphical (per
        // quickstart §3 and FSBar V1 carry-over). Concrete flag names will
        // be validated once the real game binary is available; until then
        // the broker hands the operator full control via `baseArgs` and
        // appends only the display directive.
        let displayFlag =
            match display with
            | Lobby.Headless  -> "--headless"
            | Lobby.Graphical -> "--graphical"
        baseArgs @ [ displayFlag ]

    type private RunningHandle(proc: Process) =
        let pid = proc.Id
        let mutable disposed = 0
        // Cached after the process exits, so callers can read state even
        // after `Dispose` has released the underlying Process object.
        let mutable exitCode : int option = None
        let lockObj = obj ()

        // The process's Exited event only fires when EnableRaisingEvents
        // is true. We set it once at construction.
        do proc.EnableRaisingEvents <- true

        let captureExitFromProc () =
            // Called either from the Exited handler or from Dispose. Both
            // paths run after the OS has reaped the process; reading
            // ExitCode is safe.
            try
                if proc.HasExited then
                    exitCode <- Some proc.ExitCode
            with _ -> ()

        do
            proc.Exited.AddHandler(EventHandler(fun _ _ ->
                lock lockObj captureExitFromProc))

        let registerCallback (cb: int -> unit) =
            // Snapshot the exit state under the same lock that captures
            // it, so the cb either fires synchronously (if already gone)
            // or via the Exited event (if still alive).
            let alreadyExited, code =
                lock lockObj (fun () ->
                    captureExitFromProc ()
                    match exitCode with
                    | Some c -> true, c
                    | None   -> false, 0)
            if alreadyExited then
                cb code
            else
                proc.Exited.AddHandler(EventHandler(fun _ _ ->
                    let c = lock lockObj (fun () -> defaultArg exitCode -1)
                    cb c))

        interface Handle with
            member _.Pid = pid
            member _.HasExited =
                lock lockObj (fun () -> exitCode.IsSome)
            member _.ExitCode =
                lock lockObj (fun () -> exitCode)
            member _.OnExited cb = registerCallback cb
            member _.Dispose() =
                if System.Threading.Interlocked.Exchange(&disposed, 1) = 0 then
                    try
                        if not proc.HasExited then proc.Kill(entireProcessTree = true)
                        proc.WaitForExit(2000) |> ignore
                        lock lockObj captureExitFromProc
                    with _ -> ()
                    proc.Dispose()

    let start (exe: string) (args: string list) : Result<Handle, string> =
        if String.IsNullOrWhiteSpace exe then
            Error "game executable path is empty"
        else
            try
                let psi = ProcessStartInfo(exe)
                psi.UseShellExecute <- false
                psi.RedirectStandardOutput <- false
                psi.RedirectStandardError <- false
                for a in args do
                    psi.ArgumentList.Add(a)
                let proc = new Process(StartInfo = psi)
                if proc.Start() then
                    Ok (new RunningHandle(proc) :> Handle)
                else
                    proc.Dispose()
                    Error (sprintf "failed to start %s" exe)
            with ex ->
                Error (sprintf "could not launch %s: %s" exe ex.Message)
