namespace Broker.Mvu

open System.Threading
open System.Threading.Tasks
open System.Threading.Channels
open Broker.Mvu.Adapters

module MvuRuntime =

    /// The bag of adapter implementations the runtime calls. Each field
    /// is itself a record-of-functions defined in its own adapter-
    /// interface module under `Broker.Mvu.Adapters.*`.
    type AdapterSet = {
        audit: AuditAdapter.AuditAdapter
        coordinator: CoordinatorAdapter.CoordinatorAdapter
        scripting: ScriptingAdapter.ScriptingAdapter
        viz: VizAdapter.VizAdapter
        timer: TimerAdapter.TimerAdapter
        lifecycle: LifecycleAdapter.LifecycleAdapter
    }

    /// Opaque handle for the running production runtime.
    type Host

    /// Build the runtime in a stopped state. Adapter set is captured
    /// for later use; the dispatcher loop is not yet running.
    val create :
        initialModel:Model.Model
        -> adapters:AdapterSet
        -> Host

    /// Start the dispatcher. Posts `Lifecycle.RuntimeStarted` as the
    /// first `Msg` so `update` can produce its initial `Cmd` batch.
    val start : Host -> CancellationToken -> Task<unit>

    /// Post a `Msg` from any thread. Returns immediately; the dispatcher
    /// processes asynchronously.
    val postMsg : Host -> Msg.Msg -> unit

    /// Issue a fresh `RpcId` for use in a `Msg.RpcContext`. The caller
    /// owns the `TaskCompletionSource<'r>` used to await the response.
    val issueRpcId : Host -> Cmd.RpcId

    /// Post a `Msg` carrying a fresh `RpcContext` and await the typed
    /// response. The matching `update` clause must emit
    /// `Cmd.CompleteRpc rpcId (Success payload)` (or `Fault`).
    val awaitResponse<'r> :
        Host
        -> operation:string
        -> buildMsg:(Msg.RpcContext -> Msg.Msg)
        -> Task<'r>

    /// Read the latest `Model`. Lock-free; returns the most recent
    /// snapshot the dispatcher loop has written.
    val currentModel : Host -> Model.Model

    /// Subscribe to `Model` updates. Returns a `ChannelReader<Model>`
    /// that receives every new `Model` the dispatcher produces. The
    /// render thread reads from this. Multiple subscribers are supported.
    val subscribeModel : Host -> ChannelReader<Model.Model>

    /// Request a graceful shutdown. Posts
    /// `Lifecycle.RuntimeStopRequested`, drains in-flight Cmds, and
    /// completes the `Task` returned by `start`.
    val shutdown : Host -> reason:string -> unit
