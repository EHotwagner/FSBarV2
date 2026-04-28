namespace Broker.Mvu.Adapters

open Broker.Core
open Broker.Mvu

module ScriptingAdapter =

    type ScriptingSendError =
        | QueueFull of rejectedSeq:int64
        | Failed of exn:exn

    type ScriptingAdapter = {
        send : ScriptingClientId -> Cmd.ScriptingOutboundMsg -> Async<Result<unit, ScriptingSendError>>
        reject : ScriptingClientId -> CommandPipeline.RejectReason -> Async<Result<unit, exn>>
        sampleDepth : ScriptingClientId -> Async<int * int>
        onClientDetached : ScriptingClientId -> Async<unit>
    }
