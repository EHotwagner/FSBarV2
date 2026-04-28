namespace Broker.Protocol

open System
open Broker.Core
open FSBarV2.Broker.Contracts

/// Conversions between F# Core records / unions and the wire-format
/// records emitted by the proto codegen. This module exists so the gRPC
/// service implementations can deal in Core types and let the wire
/// shape stay an implementation detail (data-model.md §4).
module WireConvert =

    val toCoreCommand      : msg:Command -> CommandPipeline.Command
    val toCoreVersion      : msg:ProtocolVersion -> Version

    /// Same as `toCoreVersion` but defaults to 0.0 when the optional
    /// wire field is absent.
    val toCoreVersionOpt   : msg:ValueOption<ProtocolVersion> -> Version

    val fromCoreSnapshot   : snapshot:Snapshot.GameStateSnapshot -> GameStateSnapshot
    val fromCoreVersion    : version:Version -> ProtocolVersion

    /// Render a `RejectReason` to a wire-format `Reject`. The optional
    /// `commandId` is echoed when present (used by `QUEUE_FULL` etc.).
    val toReject :
        reason:CommandPipeline.RejectReason
        -> commandId:Guid option
        -> brokerVersion:Version option
        -> Reject

    // === Coordinator side (feature 002, public-fsi.md) ===========================

    /// Per-session reduction of HighBar state-update payloads. Opaque to
    /// consumers; produced/consumed by `applyHighBarStateUpdate`.
    type RunningView

    val emptyRunningView : RunningView

    /// Most recent `StateUpdate.seq` accepted into the running view. `0`
    /// before any update has been applied.
    val lastSeq : view:RunningView -> uint64

    type ApplyResult =
        | NewSnapshot of Snapshot.GameStateSnapshot
        | Gap of lastSeq:uint64 * receivedSeq:uint64
        | KeepAliveOnly

    /// Apply a HighBar `StateUpdate` (snapshot, delta, or keepalive) to
    /// the running view and emit the resulting broker snapshot OR a gap
    /// indication when `seq` skips. Sequence-gap surfacing is the
    /// FR-013 enforcement point.
    val applyHighBarStateUpdate :
        update:Highbar.V1.StateUpdate
        -> view:RunningView
        -> RunningView * ApplyResult

    /// Build a HighBar `CommandBatch` from a Core `Command`. Returns
    /// `Error AdminNotAvailable` when the admin arm has no AICommand
    /// equivalent (research §3).
    val tryFromCoreCommandToHighBar :
        command:CommandPipeline.Command
        -> batchSeq:uint64
        -> Result<Highbar.V1.CommandBatch, CommandPipeline.RejectReason>
