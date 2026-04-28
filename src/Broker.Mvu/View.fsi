namespace Broker.Mvu

open Spectre.Console.Rendering

module View =

    /// Pure projection from `Model` to a Spectre renderable. Same input
    /// → same output (FR-009). The production runtime feeds the result
    /// to `LiveDisplay.Update`; tests feed it to Spectre's off-screen
    /// renderer for byte-for-byte string comparison (FR-011).
    val view : Model.Model -> IRenderable

    /// Render a `Model` to a deterministic string with a fixed terminal
    /// width. Used by tests (FR-016) and by the snapshot-regression
    /// fixture pattern (User Story 5).
    val renderToString : width:int -> height:int -> Model.Model -> string
