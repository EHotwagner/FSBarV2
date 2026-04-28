namespace Broker.Mvu.Adapters

open Broker.Mvu

module VizAdapter =

    type VizAdapter = {
        apply : Cmd.VizOp -> Async<Result<unit, exn>>
        status : unit -> string option
    }
