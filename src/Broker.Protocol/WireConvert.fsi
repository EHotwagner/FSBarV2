namespace Broker.Protocol

open System
open Broker.Core
open FSBarV2.Broker.Contracts

/// Conversions between F# Core records / unions and the wire-format
/// records emitted by the proto codegen. This module exists so the gRPC
/// service implementations can deal in Core types and let the wire
/// shape stay an implementation detail (data-model.md §4).
module WireConvert =

    val toCoreSnapshot     : msg:GameStateSnapshot -> Snapshot.GameStateSnapshot
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

    /// Wrap a Core `Command` for the proxy outbound stream's `command`
    /// envelope. Mirrors the inbound conversion.
    val fromCoreCommand : command:CommandPipeline.Command -> Command
