namespace Broker.Mvu.Adapters

open Broker.Core

module CoordinatorAdapter =

    type CoordinatorAdapter = {
        send : CommandPipeline.Command -> Async<Result<unit, exn>>
        isAttached : unit -> bool
    }
