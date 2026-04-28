namespace Broker.Mvu.Adapters

open Broker.Core

module CoordinatorAdapter =

    /// Production coordinator outbound interface. The runtime calls `send`
    /// on every `Cmd.CoordinatorOutbound` execution; `isAttached` lets
    /// the runtime decide whether to drop or queue the command.
    type CoordinatorAdapter = {
        send : CommandPipeline.Command -> Async<Result<unit, exn>>
        isAttached : unit -> bool
    }
