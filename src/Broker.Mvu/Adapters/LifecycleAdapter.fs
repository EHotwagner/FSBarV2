namespace Broker.Mvu.Adapters

open Broker.Core

module LifecycleAdapter =

    type LifecycleAdapter = {
        endSession : Session.EndReason -> Async<unit>
        quit : int -> Async<unit>
    }
