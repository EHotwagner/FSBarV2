namespace Broker.Core

open System

module Audit =

    type AuditEvent =
        | ClientConnected of at:DateTimeOffset * id:ScriptingClientId * version:Version
        | ClientDisconnected of at:DateTimeOffset * id:ScriptingClientId * reason:string
        | NameInUseRejected of at:DateTimeOffset * attempted:string
        | VersionMismatchRejected of at:DateTimeOffset * peerKind:string * peerVersion:Version
        | AdminGranted of at:DateTimeOffset * id:ScriptingClientId * by:string
        | AdminRevoked of at:DateTimeOffset * id:ScriptingClientId * by:string
        | CommandRejected of at:DateTimeOffset * id:ScriptingClientId * commandId:Guid * reason:CommandPipeline.RejectReason
        | ModeChanged of at:DateTimeOffset * from':Mode.Mode * to':Mode.Mode
        | SessionEnded of at:DateTimeOffset * sessionId:Guid * reason:Session.EndReason
        // Coordinator-wire arms (feature 002, data-model §1.12).
        | CoordinatorAttached of at:DateTimeOffset * pluginId:string * schemaVersion:string * engineSha256:string
        | CoordinatorDetached of at:DateTimeOffset * pluginId:string * reason:string
        | CoordinatorSchemaMismatch of at:DateTimeOffset * expected:string * received:string * pluginId:string
        | CoordinatorNonOwnerRejected of at:DateTimeOffset * attemptedPluginId:string * ownerPluginId:string
        | CoordinatorHeartbeat of at:DateTimeOffset * pluginId:string * frame:uint32
        | CoordinatorCommandChannelOpened of at:DateTimeOffset * pluginId:string
        | CoordinatorCommandChannelClosed of at:DateTimeOffset * pluginId:string * reason:string
        | CoordinatorStateGap of at:DateTimeOffset * pluginId:string * lastSeq:uint64 * receivedSeq:uint64

    /// Render an event for Serilog. Returns the message template + its
    /// structured property bag. The keys in the bag are stable identifiers
    /// that downstream sinks can index. Values are `objnull` because
    /// Serilog property bags may legitimately carry null values for
    /// optional fields.
    val toLogTemplate : AuditEvent -> struct(string * (string * objnull) array)
