namespace Broker.Mvu

open System
open System.Collections.Generic
open System.Threading
open System.Threading.Tasks
open System.Threading.Channels
open Broker.Core
open Broker.Mvu.Adapters

module MvuRuntime =

    type AdapterSet = {
        audit: AuditAdapter.AuditAdapter
        coordinator: CoordinatorAdapter.CoordinatorAdapter
        scripting: ScriptingAdapter.ScriptingAdapter
        viz: VizAdapter.VizAdapter
        timer: TimerAdapter.TimerAdapter
        lifecycle: LifecycleAdapter.LifecycleAdapter
    }

    /// Mutable bookkeeping the dispatcher loop reads + writes from a
    /// single thread. Subscribers see immutable snapshots through
    /// `currentModel` and the broadcast channel.
    type private DispatcherState = {
        mutable model: Model.Model
        mailbox: MailboxProcessor<Msg.Msg>
        modelBroadcast: Channel<Model.Model>
        mutable nextRpcId: int64
        rpcWaiters: Dictionary<Cmd.RpcId, TaskCompletionSource<Cmd.RpcResult>>
        cts: CancellationTokenSource
        adapters: AdapterSet
        mutable startedTask: Task<unit> option
        mutable mailboxHighWaterArmed: bool
    }

    type Host = private { state: DispatcherState }

    /// Execute a single Cmd by delegating to the appropriate adapter.
    /// On exception, post the matching `Msg.CmdFailure` arm back to the
    /// dispatcher (FR-008).
    let private runCmd (state: DispatcherState) (cmd: Cmd.Cmd<Msg.Msg>) : unit =
        let post msg = state.mailbox.Post msg
        let summary = sprintf "%A" cmd
        match cmd with
        | Cmd.NoOp -> ()
        | Cmd.Batch _ ->
            // Batches are flattened by the dispatcher loop — runCmd should
            // not see one. If it does, it's a bug; treat as NoOp.
            ()
        | Cmd.AuditCmd ev ->
            Async.StartImmediate (async {
                try
                    let! r = state.adapters.audit.write ev
                    match r with
                    | Result.Ok () -> ()
                    | Result.Error ex -> post (Msg.CmdFailure (Msg.AuditWriteFailed (summary, ex)))
                with ex -> post (Msg.CmdFailure (Msg.AuditWriteFailed (summary, ex)))
            })
        | Cmd.CoordinatorOutbound c ->
            Async.StartImmediate (async {
                try
                    let! r = state.adapters.coordinator.send c
                    match r with
                    | Result.Ok () -> ()
                    | Result.Error ex -> post (Msg.CmdFailure (Msg.CoordinatorSendFailed (summary, ex)))
                with ex -> post (Msg.CmdFailure (Msg.CoordinatorSendFailed (summary, ex)))
            })
        | Cmd.ScriptingOutbound (id, msg) ->
            Async.StartImmediate (async {
                try
                    let! r = state.adapters.scripting.send id msg
                    match r with
                    | Result.Ok () -> ()
                    | Result.Error (ScriptingAdapter.QueueFull rejectedSeq) ->
                        post (Msg.AdapterCallback (Msg.QueueOverflow (id, rejectedSeq, DateTimeOffset.UtcNow)))
                    | Result.Error (ScriptingAdapter.Failed ex) ->
                        post (Msg.CmdFailure (Msg.ScriptingSendFailed (id, summary, ex)))
                with ex -> post (Msg.CmdFailure (Msg.ScriptingSendFailed (id, summary, ex)))
            })
        | Cmd.ScriptingReject (id, reason) ->
            Async.StartImmediate (async {
                try
                    let! r = state.adapters.scripting.reject id reason
                    match r with
                    | Result.Ok () -> ()
                    | Result.Error ex -> post (Msg.CmdFailure (Msg.ScriptingSendFailed (id, summary, ex)))
                with ex -> post (Msg.CmdFailure (Msg.ScriptingSendFailed (id, summary, ex)))
            })
        | Cmd.VizCmd op ->
            Async.StartImmediate (async {
                try
                    let! r = state.adapters.viz.apply op
                    match r with
                    | Result.Ok () -> ()
                    | Result.Error ex -> post (Msg.CmdFailure (Msg.VizOpFailed (summary, ex)))
                with ex -> post (Msg.CmdFailure (Msg.VizOpFailed (summary, ex)))
            })
        | Cmd.ScheduleTick schedule ->
            Async.StartImmediate (async {
                try
                    let! _id = state.adapters.timer.schedule schedule
                    ()
                with ex ->
                    let id =
                        match schedule with
                        | Cmd.OneShot (tid, _, _) | Cmd.Recurring (tid, _, _) -> tid
                    post (Msg.CmdFailure (Msg.TimerFailed (id, summary, ex)))
            })
        | Cmd.CancelTimer id ->
            Async.StartImmediate (async {
                try do! state.adapters.timer.cancel id
                with ex -> post (Msg.CmdFailure (Msg.TimerFailed (id, summary, ex)))
            })
        | Cmd.EndSession reason ->
            Async.StartImmediate (async {
                try do! state.adapters.lifecycle.endSession reason
                with _ -> ()  // endSession is best-effort per diagnostics-plan
            })
        | Cmd.Quit code ->
            Async.StartImmediate (async {
                try do! state.adapters.lifecycle.quit code with _ -> ()
            })
        | Cmd.CompleteRpc (id, result) ->
            match state.rpcWaiters.TryGetValue id with
            | true, tcs ->
                state.rpcWaiters.Remove id |> ignore
                tcs.TrySetResult result |> ignore
            | _ -> ()  // drop — handler already cancelled

    let rec private flatten (cmd: Cmd.Cmd<Msg.Msg>) : Cmd.Cmd<Msg.Msg> seq =
        seq {
            match cmd with
            | Cmd.NoOp -> ()
            | Cmd.Batch xs -> for x in xs do yield! flatten x
            | other -> yield other
        }

    let private mailboxLoop (stateRef: DispatcherState ref) (mb: MailboxProcessor<Msg.Msg>) : Async<unit> =
        async {
            while not (!stateRef).cts.IsCancellationRequested do
                let! msg = mb.Receive()
                let state = !stateRef
                // Sample mailbox depth; emit MailboxHighWater Msg if threshold crossed.
                let depth = mb.CurrentQueueLength
                let hw = max state.model.mailboxHighWater depth
                if depth >= state.model.config.mailboxHighWaterMark && not state.mailboxHighWaterArmed then
                    state.mailbox.Post (Msg.AdapterCallback (Msg.MailboxHighWater (depth, hw, DateTimeOffset.UtcNow)))
                    state.mailboxHighWaterArmed <- true
                elif depth < state.model.config.mailboxHighWaterMark - state.model.config.mailboxHighWaterMark / 8 then
                    state.mailboxHighWaterArmed <- false
                // Apply update.
                let next, cmds = Update.update msg state.model
                state.model <- next
                // Broadcast the new model snapshot.
                state.modelBroadcast.Writer.TryWrite next |> ignore
                // Execute Cmds. Flatten Batches first.
                for c in cmds do
                    for f in flatten c do
                        runCmd state f
        }

    let create (initialModel: Model.Model) (adapters: AdapterSet) : Host =
        let cts = new CancellationTokenSource()
        let bcOpts = BoundedChannelOptions(64)
        bcOpts.SingleReader <- false
        bcOpts.SingleWriter <- true
        bcOpts.FullMode <- BoundedChannelFullMode.DropOldest
        let bc = Channel.CreateBounded<Model.Model>(bcOpts)
        let waiters = Dictionary<Cmd.RpcId, TaskCompletionSource<Cmd.RpcResult>>()
        let mutable nextRpc = 0L
        let mutable state : DispatcherState = Unchecked.defaultof<_>
        let stateRef = ref Unchecked.defaultof<DispatcherState>
        let mb =
            MailboxProcessor.Start (fun mb -> mailboxLoop stateRef mb)
        state <-
            { model = initialModel
              mailbox = mb
              modelBroadcast = bc
              nextRpcId = nextRpc
              rpcWaiters = waiters
              cts = cts
              adapters = adapters
              startedTask = None
              mailboxHighWaterArmed = false }
        stateRef.Value <- state
        // Seed the broadcast with the initial Model so subscribers don't see an empty channel.
        bc.Writer.TryWrite initialModel |> ignore
        { state = state }

    let start (host: Host) (ct: CancellationToken) : Task<unit> =
        // Link the caller's CancellationToken to our internal cts.
        let reg = ct.Register(fun () -> host.state.cts.Cancel())
        let tcs = TaskCompletionSource<unit>()
        host.state.startedTask <- Some tcs.Task
        // Post the initial Lifecycle.RuntimeStarted Msg.
        host.state.mailbox.Post (Msg.Lifecycle (Msg.RuntimeStarted DateTimeOffset.UtcNow))
        ignore reg
        tcs.Task

    let postMsg (host: Host) (msg: Msg.Msg) : unit =
        host.state.mailbox.Post msg

    let issueRpcId (host: Host) : Cmd.RpcId =
        let id = Interlocked.Increment(&host.state.nextRpcId)
        Cmd.RpcId id

    let awaitResponse<'r> (host: Host) (operation: string) (buildMsg: Msg.RpcContext -> Msg.Msg) : Task<'r> =
        let rpcId = issueRpcId host
        let ctx : Msg.RpcContext =
            { rpcId = rpcId; receivedAt = DateTimeOffset.UtcNow; operation = operation }
        let tcs = TaskCompletionSource<Cmd.RpcResult>()
        host.state.rpcWaiters.[rpcId] <- tcs
        host.state.mailbox.Post (buildMsg ctx)
        // Caller awaits the wrapped Task<'r>; the result is `unit` semantically
        // (the caller reads the post-update Model for any payload it needs).
        let resultTask = TaskCompletionSource<'r>()
        tcs.Task.ContinueWith(fun (t: Task<Cmd.RpcResult>) ->
            match t.Result with
            | Cmd.Ok ->
                // Caller's 'r is expected to be `unit`; if not, the handler
                // builds the actual response from the post-update Model.
                resultTask.TrySetResult(Unchecked.defaultof<'r>) |> ignore
            | Cmd.Fault ex ->
                resultTask.TrySetException(ex) |> ignore)
        |> ignore
        resultTask.Task

    let currentModel (host: Host) : Model.Model = host.state.model

    let subscribeModel (host: Host) : ChannelReader<Model.Model> =
        host.state.modelBroadcast.Reader

    let shutdown (host: Host) (reason: string) : unit =
        host.state.mailbox.Post (Msg.Lifecycle (Msg.RuntimeStopRequested (reason, DateTimeOffset.UtcNow)))
        host.state.cts.Cancel()
        match host.state.startedTask with
        | Some _ -> ()  // caller awaits the start Task to drain
        | None -> ()
