namespace Broker.Mvu.Adapters

open Broker.Core

module LifecycleAdapter =

    /// Production lifecycle interface. `endSession` broadcasts
    /// `SessionEnd` to every subscribed scripting client and tears down
    /// the active session. `quit` triggers process exit with the given
    /// status code.
    type LifecycleAdapter = {
        endSession : Session.EndReason -> Async<unit>
        quit : int -> Async<unit>
    }
