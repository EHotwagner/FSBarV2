namespace Broker.Mvu.Adapters

open Broker.Mvu

module TimerAdapter =

    /// Production timer interface. `schedule` registers a one-shot or
    /// recurring timer that posts `Msg.AdapterCallback.TimerFired` back
    /// when it fires; `cancel` stops a previously-scheduled timer.
    type TimerAdapter = {
        schedule : Cmd.TimerSchedule<Msg.Msg> -> Async<Cmd.TimerId>
        cancel : Cmd.TimerId -> Async<unit>
    }
