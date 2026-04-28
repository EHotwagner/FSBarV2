namespace Broker.Mvu.Adapters

open Broker.Core

module AuditAdapter =

    type AuditAdapter = {
        write : Audit.AuditEvent -> Async<Result<unit, exn>>
    }
