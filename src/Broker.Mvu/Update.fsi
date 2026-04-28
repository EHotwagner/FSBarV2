namespace Broker.Mvu

module Update =

    /// The pure broker transition. Single seam at which broker behaviour
    /// is defined. Exhaustively matches `Msg` (FR-004); the F# compiler
    /// refuses to build a non-exhaustive update. Returns the next `Model`
    /// and any `Cmd`s to schedule (FR-007).
    val update : Msg.Msg -> Model.Model -> Model.Model * Cmd.Cmd<Msg.Msg> list
