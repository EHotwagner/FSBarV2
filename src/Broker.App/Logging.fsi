namespace Broker.App

open Broker.Core

module Logging =

    /// Configure Serilog: console sink suppressed (would scramble the
    /// dashboard), rolling-file sink under `logs/broker-YYYYMMDD.log`
    /// for the audit log mandated by FR-028. Returns the `ILogger`
    /// adapter the rest of the broker uses through
    /// `Microsoft.Extensions.Logging`.
    val configure : logDirectory:string -> Microsoft.Extensions.Logging.ILoggerFactory

    /// Emit a structured `Audit.AuditEvent` through Serilog.
    val writeAudit :
        factory:Microsoft.Extensions.Logging.ILoggerFactory
        -> event:Audit.AuditEvent
        -> unit
