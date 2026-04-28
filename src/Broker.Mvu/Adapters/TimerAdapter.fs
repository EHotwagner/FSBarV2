namespace Broker.Mvu.Adapters

open Broker.Mvu

module TimerAdapter =

    type TimerAdapter = {
        schedule : Cmd.TimerSchedule<Msg.Msg> -> Async<Cmd.TimerId>
        cancel : Cmd.TimerId -> Async<unit>
    }
