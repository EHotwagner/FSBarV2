namespace Broker.Tui

open Broker.Core

module DashboardView =

    /// Pure render: turn a `DiagnosticReading` into a Spectre.Console
    /// `Layout` ready for a Live display context. Unit-testable without a
    /// TTY by reading the layout's serialised form.
    val render : reading:Dashboard.DiagnosticReading -> Spectre.Console.Layout

    /// As `render`, but with an optional viz-status line surfaced in the
    /// dashboard footer. `None` means "viz available, nothing to say";
    /// `Some msg` is the line to render (e.g.
    /// "2D visualization unavailable: no graphical display"; SC-008).
    val renderWithViz :
        reading:Dashboard.DiagnosticReading
        -> vizStatus:string option
        -> Spectre.Console.Layout
