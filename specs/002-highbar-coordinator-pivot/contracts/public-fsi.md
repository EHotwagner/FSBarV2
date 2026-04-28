# Public F# Surface Sketch (Phase 1)

**Feature**: 002-highbar-coordinator-pivot
**Date**: 2026-04-28

This document captures the curated `.fsi` surface for the modules this
feature adds, removes, or changes. The sketches drive the FSI-first
exercise step (Constitution Principle I) and the surface-area baselines
(Principle II). Modules not listed here keep their 001 `.fsi` verbatim.

---

## ⊕ NEW — `Broker.Protocol.HighBarCoordinatorService.fsi`

```fsharp
namespace Broker.Protocol

open Broker.Core
open Highbar.V1   // F# namespace generated from highbar/coordinator.proto

module HighBarCoordinatorService =

    /// Wrapper carrying the in-process broker state hub and the
    /// coordinator-side configuration (expected schema version,
    /// owner-AI rule, heartbeat timeout). The underlying gRPC
    /// service implementation is registered by `ServerHost.start`
    /// via `app.MapGrpcService<Impl>()`; the `Impl` class derives
    /// from the generated `HighBarCoordinator.HighBarCoordinatorBase`
    /// and is constructed by the ASP.NET Core DI container.
    type Service

    type Config =
        { expectedSchemaVersion: string
          ownerRule: BrokerState.OwnerRule
          heartbeatTimeoutMs: int }

    val defaultConfig : Config

    val create :
        hub:BrokerState.Hub
        -> config:Config
        -> Service

    val isAttached : service:Service -> bool

    /// Force-detach the current coordinator (operator quit, broker
    /// shutdown). Closes any open `PushState` / `OpenCommandChannel`
    /// streams and emits `SessionEnd` to subscribed scripting clients
    /// (FR-008).
    val detach : service:Service -> reason:string -> unit

    /// The concrete gRPC service class registered via `MapGrpcService`.
    /// Public so ASP.NET Core DI can construct it; its single
    /// constructor param is resolved from the singleton `Service`
    /// registered alongside.
    type Impl =
        inherit HighBarCoordinator.HighBarCoordinatorBase
        new : service:Service -> Impl
```

**Why this shape**: Mirrors the retired `ProxyLinkService` shape so
the composition root in `Broker.App.Program` only has to swap one
type. A `Config` record (rather than positional ctor args) keeps
the FSI invocation readable and lets future fields land additively
without changing the surface.

---

## Δ CHANGED — `Broker.Protocol.WireConvert.fsi`

Add the HighBar direction; drop the ProxyLink helpers.

```fsharp
namespace Broker.Protocol

open System
open Broker.Core
open FSBarV2.Broker.Contracts
open Highbar.V1

module WireConvert =

    // === ScriptingClient side (unchanged from 001) ===
    val toCoreCommand    : msg:Command -> CommandPipeline.Command
    val toCoreVersion    : msg:ProtocolVersion -> Version
    val toCoreVersionOpt : msg:ValueOption<ProtocolVersion> -> Version
    val fromCoreSnapshot : snapshot:Snapshot.GameStateSnapshot -> GameStateSnapshot
    val fromCoreVersion  : version:Version -> ProtocolVersion
    val toReject :
        reason:CommandPipeline.RejectReason
        -> commandId:Guid option
        -> brokerVersion:Version option
        -> Reject

    // === Coordinator side (NEW) ===

    /// Apply a HighBar `StateUpdate` (snapshot, delta, or keepalive)
    /// to a running per-session reduction and emit the resulting
    /// broker snapshot, OR a gap indication when `seq` skips. The
    /// reduction state is `RunningView` (opaque to consumers).
    type RunningView

    val emptyRunningView : RunningView

    type ApplyResult =
        | NewSnapshot of Snapshot.GameStateSnapshot
        | Gap of lastSeq:uint64 * receivedSeq:uint64
        | KeepAliveOnly

    val applyHighBarStateUpdate :
        update:StateUpdate
        -> view:RunningView
        -> RunningView * ApplyResult

    /// Build a HighBar `CommandBatch` from a Core `Command`. Returns
    /// `Error AdminNotAvailable` when the admin arm has no AICommand
    /// equivalent (research §3).
    val tryFromCoreCommandToHighBar :
        command:CommandPipeline.Command
        -> batchSeq:uint64
        -> Result<CommandBatch, CommandPipeline.RejectReason>
```

The 001 `toCoreSnapshot` (HighBar-shaped via `GameStateSnapshot`) and
`fromCoreCommand` (`Command` builder) are **removed** — those served
the retired ProxyLink wire whose envelope was the broker's own
`fsbar.broker.v1.GameStateSnapshot`/`Command`. Scripting clients
continue to use `fromCoreSnapshot`/`toCoreCommand`.

---

## Δ CHANGED — `Broker.Protocol.BrokerState.fsi`

Three deltas: rename the proxy-side terminology to coordinator;
add `OwnerRule`; track heartbeat-driven liveness.

```fsharp
namespace Broker.Protocol

// ... (unchanged opens and existing types) ...

module BrokerState =

    type ClientChannel = ...   // unchanged

    type Hub
    type CoreFacade = Session.CoreFacade

    /// The owner-AI rule the coordinator service enforces (FR-011).
    type OwnerRule =
        | FirstAttached
        | Pinned of pluginId:string

    val create :
        brokerVersion:Version
        -> commandQueueCapacity:int
        -> auditEmitter:(Audit.AuditEvent -> unit)
        -> Hub

    // ... unchanged readers (brokerVersion, mode, roster, slots, session) ...

    // ⊕ NEW — coordinator handshake helpers
    val expectedSchemaVersion : hub:Hub -> string
    val setExpectedSchemaVersion : v:string -> hub:Hub -> unit
    val ownerRule : hub:Hub -> OwnerRule
    val setOwnerRule : rule:OwnerRule -> hub:Hub -> unit

    // Δ RENAMED  attachProxy → attachCoordinator
    val attachCoordinator :
        link:Session.ProxyAiLink   // F# type unchanged; semantic re-purpose
        -> hub:Hub
        -> Result<unit, string>

    // ⊕ NEW — refresh on every accepted state update / heartbeat
    val noteHeartbeat :
        pluginId:string
        -> at:DateTimeOffset
        -> hub:Hub
        -> Result<unit, CommandPipeline.RejectReason>

    // Δ RENAMED  proxyOutbound → coordinatorCommandChannel
    val coordinatorCommandChannel :
        hub:Hub
        -> System.Threading.Channels.Channel<CommandPipeline.Command> option

    // Δ UNCHANGED-shape  sendToProxy → sendToCoordinator (rename only)
    val sendToCoordinator : command:CommandPipeline.Command -> hub:Hub -> unit

    // ⊕ NEW — gap surfacing for FR-013
    val noteStateGap :
        pluginId:string
        -> lastSeq:uint64
        -> receivedSeq:uint64
        -> at:DateTimeOffset
        -> hub:Hub
        -> unit

    // ... remaining surface (closeSession, applySnapshot, snapshots,
    //     togglePause, stepSpeed, registerClient, unregisterClient,
    //     liveClients, grantAdmin, revokeAdmin, bindSlot, unbindSlot,
    //     asCoreFacade) — unchanged ...
```

**Why these renames are worth the surface diff**: 001 named everything
"proxy" because the wire-side actor was a generic "proxy AI". Now the
contract has a name (`HighBarCoordinator`); the broker's seam name
should follow so the naming reads identically across spec, plan,
data-model, code, and audit log. The internal F# entity name
`ProxyAiLink` (data-model 1.7) is intentionally **not** renamed —
it is a record type with no consumer-facing identity and renaming
would touch every test file for no reader benefit.

---

## Δ CHANGED — `Broker.Core.Audit.fsi`

Additive only — new union arms for the coordinator wire (data-model
§1.12). The retired `ProxyAttached` / `ProxyDetached` arms are
removed.

```fsharp
type AuditEvent =
    | ClientConnected of at:DateTimeOffset * id:ScriptingClientId * version:Version
    | ClientDisconnected of at:DateTimeOffset * id:ScriptingClientId * reason:string
    | NameInUseRejected of at:DateTimeOffset * attempted:string
    | VersionMismatchRejected of at:DateTimeOffset * peerKind:string * peerVersion:Version
    | AdminGranted of at:DateTimeOffset * id:ScriptingClientId * by:string
    | AdminRevoked of at:DateTimeOffset * id:ScriptingClientId * by:string
    | CommandRejected of at:DateTimeOffset * id:ScriptingClientId * commandId:Guid * reason:RejectReason
    | ModeChanged of at:DateTimeOffset * from:Mode * to:Mode
    | SessionEnded of at:DateTimeOffset * sessionId:Guid * reason:EndReason
    | CoordinatorAttached of at:DateTimeOffset * pluginId:string * schemaVersion:string * engineSha256:string
    | CoordinatorDetached of at:DateTimeOffset * pluginId:string * reason:string
    | CoordinatorSchemaMismatch of at:DateTimeOffset * expected:string * received:string * pluginId:string
    | CoordinatorNonOwnerRejected of at:DateTimeOffset * attemptedPluginId:string * ownerPluginId:string
    | CoordinatorHeartbeat of at:DateTimeOffset * pluginId:string * frame:uint32
    | CoordinatorCommandChannelOpened of at:DateTimeOffset * pluginId:string
    | CoordinatorCommandChannelClosed of at:DateTimeOffset * pluginId:string * reason:string
    | CoordinatorStateGap of at:DateTimeOffset * pluginId:string * lastSeq:uint64 * receivedSeq:uint64
```

---

## Δ CHANGED — `Broker.Core.CommandPipeline.fsi`

Two new `RejectReason` arms (data-model §1.14):

```fsharp
type RejectReason =
    | QueueFull
    | AdminNotAvailable
    | SlotNotOwned of slot:int * actualOwner:ScriptingClientId option
    | NameInUse
    | VersionMismatch of broker:Version * peer:Version
    | SchemaMismatch of expected:string * received:string         // ⊕ NEW
    | NotOwner of attemptedPluginId:string * ownerPluginId:string // ⊕ NEW
    | InvalidPayload of detail:string
```

---

## ⊖ REMOVED — `Broker.Protocol.ProxyLinkService.fsi`

Deleted in full. Surface-area baseline at
`tests/SurfaceArea/baselines/Broker.Protocol.ProxyLinkService.surface.txt`
removed alongside.

---

## FSI exercise sketch

When `scripts/prelude.fsx` is updated to load the new wire types, the
sequence below is the FSI-first validation per Constitution Principle I:

```fsharp
// Bootstrap (already in prelude.fsx; extended for HighBar)
#r "nuget: Grpc-FSharp.Tools, *"
#r "../src/Broker.Contracts/bin/Debug/net10.0/Broker.Contracts.dll"
#r "../src/Broker.Core/bin/Debug/net10.0/Broker.Core.dll"
#r "../src/Broker.Protocol/bin/Debug/net10.0/Broker.Protocol.dll"
open Broker.Core
open Broker.Protocol
open Highbar.V1

// 1. Heartbeat round trip (no wire — purely the F# type shapes)
let hb = HeartbeatRequest(PluginId = "ai-7", Frame = 1234u,
                          EngineSha256 = "deadbeef",
                          SchemaVersion = "1.0.0")
let cfg = { HighBarCoordinatorService.defaultConfig with
              expectedSchemaVersion = "1.0.0" }

// 2. State update reduction
let view0 = WireConvert.emptyRunningView
let snapshot = StateUpdate(Seq = 1UL, Frame = 100u,
                           Payload = StateUpdate.PayloadOneofCase.Snapshot,
                           Snapshot = StateSnapshot(FrameNumber = 100u))
let view1, result = WireConvert.applyHighBarStateUpdate snapshot view0
match result with
| WireConvert.NewSnapshot s -> printfn "tick=%d units=%d" s.tick s.units.Length
| WireConvert.Gap (l, r) -> printfn "gap from %d to %d" l r
| WireConvert.KeepAliveOnly -> printfn "keepalive"

// 3. Command translation
let cmd = ... // build a Core Command via existing helpers
match WireConvert.tryFromCoreCommandToHighBar cmd 1UL with
| Ok batch -> printfn "batch with %d commands" batch.Commands.Count
| Error r  -> printfn "rejected: %A" r
```

A `dotnet fsi scripts/prelude.fsx` smoke run is part of the Phase 2
task plan; if any of the three sequences above fails to typecheck
or behave as documented, the `.fsi` is wrong and we revise the
sketch before any `.fs` body is written.
