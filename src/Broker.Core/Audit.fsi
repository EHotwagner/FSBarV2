namespace Broker.Core

open System

module Audit =

    type AuditEvent =
        | ProxyAttached of at:DateTimeOffset * version:Version
        | ProxyDetached of at:DateTimeOffset * reason:string
        | ClientConnected of at:DateTimeOffset * id:ScriptingClientId * version:Version
        | ClientDisconnected of at:DateTimeOffset * id:ScriptingClientId * reason:string
        | NameInUseRejected of at:DateTimeOffset * attempted:string
        | VersionMismatchRejected of at:DateTimeOffset * peerKind:string * peerVersion:Version
        | AdminGranted of at:DateTimeOffset * id:ScriptingClientId * by:string
        | AdminRevoked of at:DateTimeOffset * id:ScriptingClientId * by:string
        | CommandRejected of at:DateTimeOffset * id:ScriptingClientId * commandId:Guid * reason:CommandPipeline.RejectReason
        | ModeChanged of at:DateTimeOffset * from':Mode.Mode * to':Mode.Mode
        | SessionEnded of at:DateTimeOffset * sessionId:Guid * reason:Session.EndReason

    /// Render an event for Serilog. Returns the message template + its
    /// structured property bag. The keys in the bag are stable identifiers
    /// that downstream sinks can index. Values are `objnull` because
    /// Serilog property bags may legitimately carry null values for
    /// optional fields.
    val toLogTemplate : AuditEvent -> struct(string * (string * objnull) array)
