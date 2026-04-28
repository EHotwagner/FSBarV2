namespace Broker.Viz

open System
open System.Threading.Tasks
open Broker.Core

module VizHost =

    /// Probe the runtime environment for a usable graphical display.
    /// Returns `Ok ()` when a window can plausibly be opened; `Error
    /// reason` otherwise (FR-025, SC-008). The reason is the string
    /// surfaced in the dashboard footer when the operator presses `V`
    /// on a headless host.
    val probe : unit -> Result<unit, string>

    type Handle =
        inherit IAsyncDisposable
        abstract IsOpen : bool

    /// Open the SkiaViewer window. Subscribes to the broker's snapshot
    /// stream (modelled as `IObservable<GameStateSnapshot>`) and pushes
    /// scenes built by `SceneBuilder.build`. Returns a handle whose
    /// disposal closes the window cleanly without affecting the broker.
    /// Fails with `InvalidOperationException` when the host has no
    /// graphical display — callers should call `probe` first.
    val open_ :
        snapshots:IObservable<Snapshot.GameStateSnapshot>
        -> Task<Handle>
