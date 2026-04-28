namespace Broker.Protocol

open Broker.Core

module BackpressureGate =

    type CommandAck =
        { commandId: System.Guid
          accepted: bool
          reject: CommandPipeline.RejectReason option }

    type Gate = { queue: CommandPipeline.Queue }

    let create (queue: CommandPipeline.Queue) : Gate =
        { queue = queue }

    let process_
        (gate: Gate)
        (mode: Mode.Mode)
        (roster: ScriptingRoster.Roster)
        (slots: ParticipantSlot.ParticipantSlot list)
        (command: CommandPipeline.Command)
        : CommandAck =
        // Two gates per FR-010 + FR-009/FR-016 + FR-004:
        //  1. Authority check (pure) — admin/host, slot ownership.
        //  2. Bounded enqueue — overflow returns QueueFull. The actual
        //     HTTP/2 flow-control read-pause that backs this up lives in
        //     the gRPC service handler and is informed by `depth >= capacity`.
        match CommandPipeline.authorise mode roster slots command with
        | Error reason ->
            { commandId = command.commandId
              accepted = false
              reject = Some reason }
        | Ok () ->
            match CommandPipeline.tryEnqueue gate.queue command with
            | CommandPipeline.Accepted ->
                { commandId = command.commandId; accepted = true; reject = None }
            | CommandPipeline.Rejected reason ->
                { commandId = command.commandId; accepted = false; reject = Some reason }
