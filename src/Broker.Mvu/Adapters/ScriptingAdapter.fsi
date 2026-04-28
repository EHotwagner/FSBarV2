namespace Broker.Mvu.Adapters

open Broker.Core
open Broker.Mvu

module ScriptingAdapter =

    /// Failure shape for scripting-client outbound sends. Distinguishes
    /// "queue is full per FR-010" (which the runtime translates into
    /// `Msg.AdapterCallback.QueueOverflow`) from a generic exception
    /// (which becomes `Msg.CmdFailure.ScriptingSendFailed`).
    type ScriptingSendError =
        | QueueFull of rejectedSeq:int64
        | Failed of exn:exn

    type ScriptingAdapter = {
        send : ScriptingClientId -> Cmd.ScriptingOutboundMsg -> Async<Result<unit, ScriptingSendError>>
        reject : ScriptingClientId -> CommandPipeline.RejectReason -> Async<Result<unit, exn>>
        sampleDepth : ScriptingClientId -> Async<int * int>
        onClientDetached : ScriptingClientId -> Async<unit>
    }
