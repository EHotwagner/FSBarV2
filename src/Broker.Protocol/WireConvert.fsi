namespace Broker.Protocol

open System
open Broker.Core
open FSBarV2.Broker.Contracts

/// Conversions between F# Core records / unions and the wire-format
/// records emitted by the proto codegen. This module exists so the gRPC
/// service implementations can deal in Core types and let the wire
/// shape stay an implementation detail (data-model.md §4).
module WireConvert =

    /// Decode only commands whose complete intent has a supported native mapping.
    /// Invalid wire payloads are refused before broker admission.
    val tryToCoreCommand   : msg:Command -> Result<CommandPipeline.Command, CommandPipeline.RejectReason>
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

    /// True only after a complete HighBar snapshot has established a
    /// baseline that has not subsequently been invalidated.
    val hasValidBaseline : view:RunningView -> bool

    type ApplyResult =
        | NewSnapshot of Snapshot.GameStateSnapshot
        | Gap of lastSeq:uint64 * receivedSeq:uint64
        | Invalidated of lastSeq:uint64 * receivedSeq:uint64 * detail:string
        | KeepAliveOnly

    /// Apply a HighBar `StateUpdate` (snapshot, delta, or keepalive) to
    /// the running view. Only a complete snapshot emits broker state.
    /// Sequence gaps and nonempty deltas that cannot be fully materialized
    /// invalidate the baseline until a newer complete snapshot arrives.
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
