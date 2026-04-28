namespace Broker.Mvu

open System.Collections.Generic

module TestRuntime =

    type private State = {
        mutable model: Model.Model
        captured: List<Cmd.Cmd<Msg.Msg>>
    }

    type Handle = private { state: State }

    let create (initialModel: Model.Model) : Handle =
        { state = { model = initialModel; captured = List<Cmd.Cmd<Msg.Msg>>() } }

    let rec private flatten (cmd: Cmd.Cmd<Msg.Msg>) : Cmd.Cmd<Msg.Msg> seq =
        seq {
            match cmd with
            | Cmd.NoOp -> ()
            | Cmd.Batch xs ->
                for x in xs do yield! flatten x
            | other -> yield other
        }

    let private record (h: Handle) (cmds: Cmd.Cmd<Msg.Msg> list) : unit =
        for c in cmds do
            for f in flatten c do
                h.state.captured.Add f

    let dispatch (h: Handle) (msg: Msg.Msg) : unit =
        let next, cmds = Update.update msg h.state.model
        h.state.model <- next
        record h cmds

    let dispatchAll (h: Handle) (msgs: Msg.Msg list) : unit =
        for m in msgs do dispatch h m

    let currentModel (h: Handle) : Model.Model = h.state.model

    let capturedCmds (h: Handle) : Cmd.Cmd<Msg.Msg> list = List.ofSeq h.state.captured

    let private isFollowUpTimer (h: Handle) (i: int) : Msg.Msg option =
        match h.state.captured.[i] with
        | Cmd.ScheduleTick (Cmd.OneShot (_, 0, msg))
        | Cmd.ScheduleTick (Cmd.Recurring (_, 0, msg)) -> Some msg
        | _ -> None

    let completeCmd (h: Handle) (i: int) (resultMsg: Msg.Msg) : unit =
        ignore (isFollowUpTimer h i)  // shape sanity — caller picked the index
        if i < 0 || i >= h.state.captured.Count then
            invalidArg "i" "out of range"
        dispatch h resultMsg

    let failCmd (h: Handle) (i: int) (failure: Msg.CmdFailure) : unit =
        if i < 0 || i >= h.state.captured.Count then
            invalidArg "i" "out of range"
        dispatch h (Msg.CmdFailure failure)

    let runUntilQuiescent (h: Handle) : unit =
        // Follow up on any zero-delay one-shot timers captured. Production
        // runtime would fire them through TimerAdapter; here we synthesise
        // the resulting `TimerFired` Msg synchronously.
        let mutable progressed = true
        while progressed do
            progressed <- false
            for i in 0 .. h.state.captured.Count - 1 do
                match h.state.captured.[i] with
                | Cmd.ScheduleTick (Cmd.OneShot (timerId, 0, _msg)) ->
                    let firedAt = h.state.model.startedAt
                    dispatch h (Msg.AdapterCallback (Msg.TimerFired (timerId, firedAt)))
                    progressed <- true
                | _ -> ()

    let clearCapturedCmds (h: Handle) : unit =
        h.state.captured.Clear()
