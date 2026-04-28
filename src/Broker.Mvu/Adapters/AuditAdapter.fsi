namespace Broker.Mvu.Adapters

open Broker.Core

module AuditAdapter =

    /// Production audit sink interface. The runtime calls `write` on every
    /// `Cmd.AuditCmd` execution and translates `Result.Error` into
    /// `Msg.CmdFailure.AuditWriteFailed` (FR-008).
    type AuditAdapter = {
        write : Audit.AuditEvent -> Async<Result<unit, exn>>
    }
