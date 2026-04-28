namespace Broker.App

open System
open Broker.Core
open Broker.Tui
open Broker.Viz

module VizControllerImpl =

    /// Live wiring for the optional 2D viz used by `Program.main`. The
    /// first `Toggle` opens a `VizHost.Handle` (or records a probe failure
    /// for the dashboard footer per SC-008); a second `Toggle` closes it.
    /// The constructor parameter is the stream of broker-side snapshots
    /// (`BrokerState.snapshots`) the viewer subscribes to.
    type LiveVizController =
        new : snapshots:IObservable<Snapshot.GameStateSnapshot> -> LiveVizController

        /// Implements `TickLoop.VizController.Toggle` and `Status`. The
        /// interface is what the TUI tick loop talks to; this concrete
        /// type is exposed so the integration suite can drive
        /// `Toggle` / `Status` against a known display posture.
        interface TickLoop.VizController

        /// Closes the underlying `VizHost.Handle`, if open. Idempotent.
        /// Called by `Program.main` during shutdown.
        member Close : unit -> unit
