namespace Broker.Mvu.Adapters

open Broker.Mvu

module VizAdapter =

    /// Production viewer-window interface. The runtime calls `apply` on
    /// every `Cmd.VizCmd` execution; `status` returns the current footer
    /// status line (or `None` when no viz is loaded).
    type VizAdapter = {
        apply : Cmd.VizOp -> Async<Result<unit, exn>>
        status : unit -> string option
    }
