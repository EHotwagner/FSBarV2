# Public F# Surface Sketch (Phase 1)

**Feature**: 003-elmish-mvu-core
**Date**: 2026-04-28

This document captures the curated `.fsi` surface for the modules this
feature adds, removes, or reduces. The sketches drive the FSI-first
exercise step (Constitution Principle I) and the surface-area
baselines (Principle II). Modules not listed here keep their 001/002
`.fsi` verbatim.

The sketches are *intentionally* terse — they show types and
function signatures, not implementation. Doc-comments name the
constraint each member enforces (FR / SC reference where useful).

---

## ⊕ NEW project — `Broker.Mvu`

`Broker.Mvu.fsproj` `ProjectReference`s `Broker.Core` and
`Broker.Contracts` (the latter supplies the `Highbar.V1.*` and
`FSBarV2.Broker.Contracts.*` namespaces opened from `Msg.fsi`),
and `PackageReference`s `Elmish` + `Spectre.Console`. It does NOT
reference `Broker.Protocol`, `Broker.Tui`, `Broker.Viz`, or
`Broker.App`.

### `Broker.Mvu.Cmd.fsi` — the side-effect envelope

```fsharp
namespace Broker.Mvu

open System
open Broker.Core

module Cmd =

    /// Identifier for an outstanding gRPC handler awaiting a response.
    type RpcId = RpcId of int64

    /// Identifier for a registered timer schedule.
    type TimerId = TimerId of int64

    /// Schedule kind for `ScheduleTick`. One-shot timers fire once and
    /// remove themselves from `Model.timers`; recurring timers stay
    /// registered until `CancelTimer` is dispatched.
    type TimerSchedule =
        | OneShot of delayMs:int * msg:Msg
        | Recurring of intervalMs:int * msg:Msg

    /// The shape an opaque RPC response takes when a Cmd completes
    /// the matching handler's TaskCompletionSource.
    type RpcResult =
        | Success of payload:obj                                  // erased; the matching handler casts.
        | Fault of exn:exn                                        // fault-completes the TCS.

    /// Reasons a scripting-client message can be rejected at the
    /// adapter boundary. Carries forward 001's RejectReason without
    /// renaming.
    type RejectReason = ScriptingRoster.RejectReason

    /// Viewer-window operations. Mirror the existing
    /// `Broker.Viz.VizControllerImpl` surface, restated as data
    /// rather than method calls.
    type VizOp =
        | OpenWindow
        | CloseWindow
        | PushFrame of Snapshot.GameStateSnapshot
        | UpdateStatus of string

    /// Every side effect the broker can perform. Inert data; the
    /// runtime executes it. See research §4 and data-model §1.8.
    and Cmd =
        | NoOp
        | Batch of Cmd list
        | AuditCmd of Audit.AuditEvent
        | CoordinatorOutbound of CommandPipeline.Command
        | ScriptingOutbound of clientId:ScriptingClientId * msg:StateMsg
        | ScriptingReject of clientId:ScriptingClientId * reason:RejectReason
        | VizCmd of VizOp
        | ScheduleTick of schedule:TimerSchedule
        | CancelTimer of TimerId
        | EndSession of reason:Session.EndReason
        | Quit of exitCode:int
        | CompleteRpc of rpcId:RpcId * result:RpcResult

    /// Convenience: combine many Cmds into a single Batch arm,
    /// flattening nested Batches.
    val batch : Cmd list -> Cmd

    /// Convenience: NoOp constant.
    val none : Cmd
```

### `Broker.Mvu.Msg.fsi` — every input that can mutate `Model`

```fsharp
namespace Broker.Mvu

open System
open Broker.Core
open Highbar.V1
open FSBarV2.Broker.Contracts
open Broker.Mvu.Cmd

module Msg =

    /// The unique identifier the runtime hands out for each in-flight
    /// gRPC handler. The `update` clause for the matching Msg arm
    /// emits `Cmd.CompleteRpc rpcId result` to wake the handler.
    type RpcContext = {
        rpcId: RpcId
        receivedAt: DateTimeOffset
        operation: string
    }

    /// Coordinator-side inbound RPCs (HighBar wire). Each carries the
    /// payload (already wire-converted by `Broker.Protocol.WireConvert`)
    /// plus the RpcContext the runtime will complete on response.
    type CoordinatorInbound =
        | Heartbeat of HeartbeatRequest * RpcContext
        | PushStateOpened of pluginId:string * RpcContext
        | PushStateSnapshot of seq:uint64 * snapshot:Snapshot.GameStateSnapshot
        | PushStateDelta of seq:uint64 * delta:Snapshot.DeltaEvent list
        | PushStateKeepAlive of seq:uint64
        | PushStateClosed of reason:string
        | OpenCommandChannelOpened of pluginId:string * RpcContext

    /// Scripting-client-side inbound RPCs.
    type ScriptingInbound =
        | Hello of HelloRequest * RpcContext
        | Subscribe of clientId:ScriptingClientId * RpcContext
        | Unsubscribe of clientId:ScriptingClientId
        | Command of clientId:ScriptingClientId * cmd:CommandPipeline.Command
        | Disconnected of clientId:ScriptingClientId * reason:string

    /// Adapter callbacks: events that adapters originate.
    type AdapterCallback =
        | QueueDepth of clientId:ScriptingClientId * depth:int * highWaterSinceLastSample:int * sampledAt:DateTimeOffset
        | QueueOverflow of clientId:ScriptingClientId * rejectedSeq:int64 * at:DateTimeOffset
        | TimerFired of timerId:TimerId * firedAt:DateTimeOffset
        | VizWindowClosed of at:DateTimeOffset
        | MailboxHighWater of depth:int * highWater:int * sampledAt:DateTimeOffset

    /// Cmd-execution failure arms. Per spec Clarification Q3
    /// (2026-04-28) and FR-008. Each carries the operation summary
    /// and the underlying exception. Exhaustively matched in `update`.
    type CmdFailure =
        | AuditWriteFailed of summary:string * exn:exn
        | CoordinatorSendFailed of summary:string * exn:exn
        | ScriptingSendFailed of clientId:ScriptingClientId * summary:string * exn:exn
        | VizOpFailed of summary:string * exn:exn
        | TimerFailed of timerId:TimerId * summary:string * exn:exn

    /// Operator input from the TUI thread.
    type TuiInput =
        | Keypress of ConsoleKeyInfo
        | Resize of width:int * height:int
        | QuitRequested

    /// Recurring runtime ticks.
    type Tick =
        | DashboardTick of at:DateTimeOffset
        | HeartbeatProbe of at:DateTimeOffset
        | SnapshotStaleness of at:DateTimeOffset

    /// Lifecycle: bookend events for the runtime's own lifetime.
    type Lifecycle =
        | RuntimeStarted of at:DateTimeOffset
        | RuntimeStopRequested of reason:string * at:DateTimeOffset
        | SessionEnded of reason:Session.EndReason * at:DateTimeOffset

    /// The single Msg union dispatched to `update`. Every input case
    /// the broker can react to is one arm here. Exhaustively matched
    /// in `update`, enforced by the F# compiler (FR-004).
    type Msg =
        | TuiInput of TuiInput
        | CoordinatorInbound of CoordinatorInbound
        | ScriptingInbound of ScriptingInbound
        | AdapterCallback of AdapterCallback
        | CmdFailure of CmdFailure
        | Tick of Tick
        | Lifecycle of Lifecycle
```

### `Broker.Mvu.Model.fsi` — single immutable state value

```fsharp
namespace Broker.Mvu

open System
open Broker.Core
open Broker.Mvu.Cmd
open Broker.Mvu.Msg

module Model =

    /// Operator-visible viz subsystem state. See data-model §1.4.
    type VizState =
        | Disabled
        | Closed
        | Active of openedAt:DateTimeOffset * statusLine:string
        | Failed of failedAt:DateTimeOffset * reason:string

    /// Per-client adapter-queue observation. Owned by `update`; the
    /// adapter populates via `Msg.QueueDepth` callbacks. See
    /// data-model §1.3.
    type QueueObservation = {
        depth: int
        highWaterMark: int
        overflowCount: int
        lastSampledAt: DateTimeOffset
        lastOverflowAt: DateTimeOffset option
    }

    /// In-flight gRPC handler tracking.
    type RpcWaiter = {
        issuedAt: DateTimeOffset
        operation: string
        tcs: obj
    }

    /// Registered timer schedule.
    type TimerHandle = {
        timerId: TimerId
        scheduledAt: DateTimeOffset
        intervalMs: int                       // 0 for one-shot
        pendingMsg: Msg
    }

    /// Startup-frozen broker configuration.
    type BrokerConfig = {
        listenAddress: string
        expectedSchemaVersion: string
        ownerRule: BrokerState.OwnerRule
        heartbeatTimeoutMs: int
        commandQueueCapacity: int
        perClientQueueCapacity: int
        mailboxHighWaterMark: int
        mailboxHighWaterCooldownMs: int
        queueDepthSampleMs: int
        tickIntervalMs: int
        vizEnabled: bool
    }

    val defaultConfig : BrokerConfig

    /// The single immutable broker state value. See data-model §1.1.
    type Model = {
        brokerInfo: Session.BrokerInfo
        config: BrokerConfig
        startedAt: DateTimeOffset
        mode: Mode.Mode
        session: Session.Session option
        coordinator: Session.ProxyAiLink option
        roster: ScriptingRoster.Roster
        slots: ParticipantSlot.ParticipantSlot list
        queues: Map<ScriptingClientId, QueueObservation>
        snapshot: Snapshot.GameStateSnapshot option
        pendingLobby: Lobby.LobbyConfig option
        elevation: ScriptingClientId option
        viz: VizState
        mailboxDepth: int
        mailboxHighWater: int
        lastMailboxAuditAt: DateTimeOffset option
        pendingRpcs: Map<RpcId, RpcWaiter>
        timers: Map<TimerId, TimerHandle>
    }

    /// Construct the initial Model from CLI args + bootstrap context.
    val init :
        brokerInfo:Session.BrokerInfo
        -> config:BrokerConfig
        -> startedAt:DateTimeOffset
        -> Model
```

### `Broker.Mvu.Update.fsi` — the pure transition function

```fsharp
namespace Broker.Mvu

open Broker.Mvu.Cmd
open Broker.Mvu.Msg
open Broker.Mvu.Model

module Update =

    /// The pure broker transition. Single seam at which broker
    /// behaviour is defined. Exhaustively matches Msg (FR-004); the
    /// F# compiler refuses to build a non-exhaustive update.
    /// Returns the next Model and any Cmds to schedule (FR-007).
    val update : Msg -> Model -> Model * Cmd list
```

### `Broker.Mvu.View.fsi` — pure dashboard projection

```fsharp
namespace Broker.Mvu

open Spectre.Console.Rendering
open Broker.Mvu.Model

module View =

    /// Pure projection from Model to a Spectre renderable. Same input
    /// → same output (FR-009). The production runtime feeds the result
    /// to `LiveDisplay.Update`; tests feed the result to Spectre's
    /// off-screen renderer for byte-for-byte string comparison
    /// (FR-011).
    val view : Model -> IRenderable

    /// Render a Model to a deterministic string with a fixed
    /// terminal width. Used by tests (FR-016) and by the snapshot-
    /// regression fixture pattern (User Story 5).
    val renderToString : width:int -> height:int -> Model -> string
```

### `Broker.Mvu.MvuRuntime.fsi` — production runtime

```fsharp
namespace Broker.Mvu

open System
open System.Threading
open System.Threading.Tasks
open Broker.Mvu.Cmd
open Broker.Mvu.Msg
open Broker.Mvu.Model

module MvuRuntime =

    /// The bag of adapter implementations the runtime calls. Each
    /// field is itself a record-of-functions defined in its own
    /// adapter-interface module under `Broker.Mvu.Adapters.*`.
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
        initialModel:Model
        -> adapters:AdapterSet
        -> Host

    /// Start the dispatcher. Posts `Lifecycle.RuntimeStarted` as the
    /// first Msg so `update` can produce its initial Cmd batch
    /// (open audit sink, register heartbeat watchdog, etc.).
    val start : Host -> CancellationToken -> Task<unit>

    /// Post a Msg from any thread. Returns immediately; the dispatcher
    /// processes asynchronously.
    val postMsg : Host -> Msg -> unit

    /// Post a Msg and await a typed response. The matching `update`
    /// clause must emit `Cmd.CompleteRpc rpcId (Success payload)`.
    /// Blocks until the Cmd executes or the runtime is shut down.
    val awaitResponse<'r> : Host -> Msg -> Task<'r>

    /// Read the latest Model. Lock-free; returns the most recent
    /// snapshot the dispatcher loop has written.
    val currentModel : Host -> Model

    /// Subscribe to Model updates. Returns a Channel<Model> that
    /// receives every new Model the dispatcher produces. The render
    /// thread reads from this. Multiple subscribers are supported.
    val subscribeModel : Host -> System.Threading.Channels.ChannelReader<Model>

    /// Request a graceful shutdown. Posts
    /// `Lifecycle.RuntimeStopRequested`, drains in-flight Cmds, and
    /// completes the Task returned by `start`.
    val shutdown : Host -> reason:string -> unit
```

### `Broker.Mvu.TestRuntime.fsi` — synchronous test runtime

```fsharp
namespace Broker.Mvu

open Broker.Mvu.Cmd
open Broker.Mvu.Msg
open Broker.Mvu.Model

module TestRuntime =

    /// Opaque handle. Captures Cmds in an ordered list rather than
    /// executing them. See FR-006 / FR-015 / FR-017 and research §6.
    type Handle

    /// Build the test runtime against an initial Model. No adapters
    /// are registered; Cmds are recorded.
    val create : initialModel:Model -> Handle

    /// Synchronously dispatch a Msg. Updates the Model in place
    /// (the Handle owns one mutable cell for the running Model)
    /// and appends emitted Cmds to the captured list.
    val dispatch : Handle -> Msg -> unit

    /// Synchronously dispatch many Msgs in order.
    val dispatchAll : Handle -> Msg list -> unit

    /// Read the current Model.
    val currentModel : Handle -> Model

    /// Read the ordered, full list of every Cmd emitted by every
    /// dispatch since `create`. The same Cmd appears once per emission.
    val capturedCmds : Handle -> Cmd list

    /// For the `i`-th captured Cmd, model its successful completion
    /// by feeding `resultMsg` into the dispatcher. Used by tests that
    /// need to script a Cmd's effect on subsequent state. Throws if
    /// `i` is out of range.
    val completeCmd : Handle -> i:int -> resultMsg:Msg -> unit

    /// For the `i`-th captured Cmd, model its failure by feeding the
    /// matching `Msg.CmdFailure ...` arm into the dispatcher. The
    /// caller chooses the failure arm (the `i`-th Cmd's effect family
    /// determines which arms are valid).
    val failCmd : Handle -> i:int -> failure:CmdFailure -> unit

    /// Drain any timer-fired or follow-up Msgs the previous dispatch
    /// may have queued (e.g., when an `update` clause emits a
    /// `Cmd.ScheduleTick OneShot 0 …` that should fire immediately
    /// in test). Returns when no Msg is pending.
    val runUntilQuiescent : Handle -> unit

    /// Reset the captured-Cmd list to empty. Useful for asserting on
    /// "what Cmds did *this* dispatch produce" rather than the
    /// cumulative total.
    val clearCapturedCmds : Handle -> unit
```

### Adapter-interface modules

Each is a record-of-functions that the production runtime calls.
Production projects construct concrete instances; test runtimes
construct concrete instances backed by `ResizeArray` recorders.

```fsharp
namespace Broker.Mvu.Adapters

open Broker.Core
open Broker.Mvu.Cmd

module AuditAdapter =
    type AuditAdapter = {
        write : Audit.AuditEvent -> Async<Result<unit, exn>>
    }

module CoordinatorAdapter =
    type CoordinatorAdapter = {
        send : CommandPipeline.Command -> Async<Result<unit, exn>>
        isAttached : unit -> bool
    }

module ScriptingAdapter =
    /// Failure shape for scripting-client outbound sends. Distinguishes
    /// "queue is full per FR-010" (which the runtime translates into
    /// `Msg.QueueOverflow`) from a generic exception (which becomes
    /// `Msg.ScriptingSendFailed`).
    type ScriptingSendError =
        | QueueFull of rejectedSeq:int64
        | Failed of exn:exn

    type ScriptingAdapter = {
        send : ScriptingClientId -> StateMsg -> Async<Result<unit, ScriptingSendError>>
        reject : ScriptingClientId -> RejectReason -> Async<Result<unit, exn>>
        sampleDepth : ScriptingClientId -> Async<int * int>
        onClientDetached : ScriptingClientId -> Async<unit>
    }

module VizAdapter =
    type VizAdapter = {
        apply : VizOp -> Async<Result<unit, exn>>
        status : unit -> string option
    }

module TimerAdapter =
    type TimerAdapter = {
        schedule : TimerSchedule -> Async<TimerId>
        cancel : TimerId -> Async<unit>
    }

module LifecycleAdapter =
    type LifecycleAdapter = {
        endSession : Session.EndReason -> Async<unit>
        quit : exitCode:int -> Async<unit>
    }
```

### `Broker.Mvu.Testing.Fixtures.fsi` — `[S]` synthetic fixtures

```fsharp
namespace Broker.Mvu.Testing

(* SYNTHETIC FIXTURE: representative Model values, not derived from a
   live BAR session. Real-game evidence regenerated by Story 3 walkthrough —
   see specs/001-tui-grpc-broker/readiness/. *)

open System
open Broker.Mvu.Model
open Broker.Mvu.Msg

module Fixtures =

    /// Idle-mode Model with no coordinator attached.
    val syntheticIdleModel : startedAt:DateTimeOffset -> Model

    /// Guest-mode Model with N subscribed scripting clients, mid-game.
    val syntheticGuestModel : clientCount:int -> tick:int64 -> Model

    /// Host-mode Model with an elevated client.
    val syntheticHostModelElevated : elevatedClient:ScriptingClientId -> Model

    /// Sequence builders for the carve-out scenarios.
    val syntheticT029MsgSequence : unit -> Msg list
    val syntheticT037MsgSequence : unit -> Msg list
    val syntheticT042MsgSequence : clientCount:int -> snapshotCount:int -> unitsPerFrame:int -> Msg list
    val syntheticT046MsgSequence : vizEnabled:bool -> Msg list
```

---

## ⚠ REDUCED — `Broker.Protocol.BrokerState.fsi`

Drops the `Hub` type and every public mutation function. What
remains is a small Msg-translation surface for gRPC handlers.

```fsharp
namespace Broker.Protocol

open System.Threading.Tasks
open Broker.Mvu.Cmd
open Broker.Mvu.Msg
open Broker.Mvu.Model

module BrokerState =

    /// Owner-AI rule (carried forward verbatim from 002 §1.7).
    type OwnerRule =
        | FixedSkirmishAiId of int
        | EnvVar of name:string
        | AcceptAny

    /// Bind the gRPC services to a running `MvuRuntime.Host`.
    /// Called once by `Broker.App.Program` after starting the runtime;
    /// the gRPC services capture the resulting handle for use in their
    /// inbound RPC handlers.
    type Binding

    val bind : host:MvuRuntime.Host -> Binding

    val postMsg : Binding -> Msg -> unit
    val awaitResponse<'r> : Binding -> Msg -> Task<'r>
```

The 001/002 surface (`create`, `mode`, `roster`, `slots`, `session`,
`openHostSession`, `openGuestSession`, `closeSession`,
`attachCoordinator`, `coordinatorCommandChannel`, `attachProxy`,
`proxyOutbound`) is **all removed**.

---

## ⚠ REDUCED — `Broker.Tui.TickLoop.fsi`

Drops the dispatch table and the `CoreFacade` consumer pattern.
What remains is a thin keypress-poll-and-render shell.

```fsharp
namespace Broker.Tui

open System.Threading
open System.Threading.Tasks
open Broker.Mvu

module TickLoop =

    /// Single-thread render-and-input loop. Owns the AnsiConsole.Live
    /// context — Spectre.Console's `LiveDisplay` is not thread-safe,
    /// so rendering and input handling share one thread (research §4
    /// of feature 001, still in force).
    ///
    /// On each iteration:
    /// (1) Poll Console.KeyAvailable. If a key is available, read it
    ///     and post `Msg.TuiInput.Keypress` to the host.
    /// (2) Take the latest Model from `MvuRuntime.subscribeModel` /
    ///     `MvuRuntime.currentModel` and feed
    ///     `Broker.Mvu.View.view model` to LiveDisplay.Update.
    ///
    /// Returns when the user issues `Quit` or the cancellation token
    /// fires.
    val run :
        host:MvuRuntime.Host
        -> tickIntervalMs:int
        -> CancellationToken
        -> Task<unit>
```

The previous `UiMode`, `VizController`, and `dispatch` exports are
**removed**. UI-mode draft state (e.g., a partially-entered lobby
config) lives in `Model.pendingLobby` per data-model §1.1; viz
status is read from `Model.viz`; dispatch is `MvuRuntime.postMsg`.

---

## ⚠ UPDATED — `Broker.Protocol.HighBarCoordinatorService.fsi`

Same public type names. The `Service` and `Impl` types' constructors
now take a `BrokerState.Binding` instead of a `BrokerState.Hub`.
Handler bodies translate inbound RPCs into `Msg.CoordinatorInbound`
arms and await the matching `Cmd.CompleteRpc` via the
`BrokerState.awaitResponse` helper.

```fsharp
namespace Broker.Protocol

open Broker.Mvu
open Highbar.V1

module HighBarCoordinatorService =

    type Service

    type Config =
        { expectedSchemaVersion: string
          ownerRule: BrokerState.OwnerRule
          heartbeatTimeoutMs: int }

    val defaultConfig : Config

    val create :
        binding:BrokerState.Binding
        -> config:Config
        -> Service

    val isAttached : service:Service -> bool

    val detach : service:Service -> reason:string -> unit

    type Impl =
        inherit HighBarCoordinator.HighBarCoordinatorBase
        new : service:Service -> Impl
```

`Broker.Protocol.ScriptingClientService.fsi` follows the same shape:
`create` takes a `BrokerState.Binding`, the `Impl` ctor unchanged,
inbound RPC handlers now post `Msg.ScriptingInbound` arms and await
the matching `Cmd.CompleteRpc`.

---

## ⊕ NEW — production adapter implementations (live close to the I/O)

Each implementation file declares a single public `create` function
that returns the matching adapter record. Each `.fsi` is small —
typically 5–10 lines.

```fsharp
// src/Broker.App/AuditAdapterImpl.fsi
namespace Broker.App

open Serilog
open Broker.Mvu.Adapters.AuditAdapter

module AuditAdapterImpl =
    val create : logger:ILogger -> AuditAdapter

// src/Broker.App/TimerAdapterImpl.fsi
namespace Broker.App

open Broker.Mvu.Adapters.TimerAdapter

module TimerAdapterImpl =
    val create : unit -> TimerAdapter

// src/Broker.App/LifecycleAdapterImpl.fsi
namespace Broker.App

open Broker.Mvu.Adapters.LifecycleAdapter

module LifecycleAdapterImpl =
    val create : onQuit:(int -> unit) -> LifecycleAdapter

// src/Broker.Protocol/CoordinatorAdapterImpl.fsi
namespace Broker.Protocol

open Broker.Mvu.Adapters.CoordinatorAdapter

module CoordinatorAdapterImpl =
    val create : service:HighBarCoordinatorService.Service -> CoordinatorAdapter

// src/Broker.Protocol/ScriptingAdapterImpl.fsi
namespace Broker.Protocol

open Broker.Mvu.Adapters.ScriptingAdapter

module ScriptingAdapterImpl =
    val create :
        perClientCapacity:int
        -> postMsg:(Msg -> unit)
        -> ScriptingAdapter

// src/Broker.Viz/VizAdapterImpl.fsi
namespace Broker.Viz

open Broker.Mvu.Adapters.VizAdapter

module VizAdapterImpl =
    val create : controller:VizControllerImpl.Controller -> VizAdapter
```

---

## Surface-area baselines impacted

| Baseline | Action |
|----------|--------|
| `Broker.Protocol.BrokerState.surface.txt` | ⚠ regenerated (Hub removed; new minimal surface) |
| `Broker.Tui.TickLoop.surface.txt` | ⚠ regenerated (reduced surface) |
| `Broker.Mvu.Model.surface.txt` | ⊕ new |
| `Broker.Mvu.Msg.surface.txt` | ⊕ new |
| `Broker.Mvu.Cmd.surface.txt` | ⊕ new |
| `Broker.Mvu.Update.surface.txt` | ⊕ new |
| `Broker.Mvu.View.surface.txt` | ⊕ new |
| `Broker.Mvu.MvuRuntime.surface.txt` | ⊕ new |
| `Broker.Mvu.TestRuntime.surface.txt` | ⊕ new |
| `Broker.Mvu.Adapters.*.surface.txt` (six files) | ⊕ new |
| `Broker.Mvu.Testing.Fixtures.surface.txt` | ⊕ new (`[S]` — banner comment in source per Principle IV) |
| `Broker.App.AuditAdapterImpl.surface.txt` | ⊕ new |
| `Broker.App.TimerAdapterImpl.surface.txt` | ⊕ new |
| `Broker.App.LifecycleAdapterImpl.surface.txt` | ⊕ new |
| `Broker.Protocol.CoordinatorAdapterImpl.surface.txt` | ⊕ new |
| `Broker.Protocol.ScriptingAdapterImpl.surface.txt` | ⊕ new |
| `Broker.Viz.VizAdapterImpl.surface.txt` | ⊕ new |
| `Broker.Protocol.HighBarCoordinatorService.surface.txt` | ⚠ regenerated (ctor takes `Binding` not `Hub`) |
| `Broker.Protocol.ScriptingClientService.surface.txt` | ⚠ regenerated (ctor takes `Binding` not `Hub`) |
