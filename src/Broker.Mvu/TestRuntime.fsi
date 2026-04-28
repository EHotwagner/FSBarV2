namespace Broker.Mvu

module TestRuntime =

    /// Opaque handle. Captures Cmds in an ordered list rather than
    /// executing them. See FR-006 / FR-015 / FR-017 and research §6.
    type Handle

    /// Build the test runtime against an initial `Model`. No adapters
    /// are registered; Cmds are recorded.
    val create : initialModel:Model.Model -> Handle

    /// Synchronously dispatch a `Msg`. Updates the `Model` in place (the
    /// `Handle` owns one mutable cell for the running `Model`) and
    /// appends emitted Cmds to the captured list.
    val dispatch : Handle -> Msg.Msg -> unit

    /// Synchronously dispatch many Msgs in order.
    val dispatchAll : Handle -> Msg.Msg list -> unit

    /// Read the current `Model`.
    val currentModel : Handle -> Model.Model

    /// Read the ordered, full list of every `Cmd` emitted by every
    /// dispatch since `create`. The same Cmd appears once per emission.
    val capturedCmds : Handle -> Cmd.Cmd<Msg.Msg> list

    /// For the `i`-th captured Cmd, model its successful completion by
    /// feeding `resultMsg` into the dispatcher.
    val completeCmd : Handle -> i:int -> resultMsg:Msg.Msg -> unit

    /// For the `i`-th captured Cmd, model its failure by feeding the
    /// matching `Msg.CmdFailure ...` arm into the dispatcher.
    val failCmd : Handle -> i:int -> failure:Msg.CmdFailure -> unit

    /// Drain any timer-fired or follow-up Msgs the previous dispatch may
    /// have queued. Returns when no `Msg` is pending.
    val runUntilQuiescent : Handle -> unit

    /// Reset the captured-Cmd list to empty.
    val clearCapturedCmds : Handle -> unit
