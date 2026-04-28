namespace Broker.App

open Broker.Core

module GameProcess =

    /// Handle to a running game process. Disposing it kills the process
    /// (synchronously) and releases the underlying OS handle. The
    /// `OnExited` callback fires exactly once when the process leaves
    /// (either via `Dispose` or external termination).
    type Handle =
        inherit System.IDisposable
        abstract Pid : int
        abstract HasExited : bool
        abstract ExitCode : int option
        /// Register a callback for the `Exited` event. Multiple callbacks
        /// may be registered. Each fires at most once. If the process has
        /// already exited when the callback is registered, it fires
        /// immediately on the caller's thread.
        abstract OnExited : (int -> unit) -> unit

    /// Build the argument vector the broker passes to the game executable
    /// for the given lobby display preference (FR-012).
    /// Pure — separated from `start` so the wire-up tests can assert the
    /// args sent for headless / graphical without spawning a process.
    val argsFor :
        baseArgs:string list
        -> display:Lobby.Display
        -> string list

    /// Spawn the configured game process. Returns an `Error` immediately
    /// (without throwing) on missing executables, missing permissions, or
    /// invalid arguments — the broker uses this to fail fast with a clear
    /// pointer to what is wrong (spec edge case "configured game executable
    /// is missing or incompatible").
    val start :
        exe:string
        -> args:string list
        -> Result<Handle, string>
