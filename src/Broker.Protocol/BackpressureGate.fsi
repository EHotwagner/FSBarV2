namespace Broker.Protocol

open Broker.Core

module BackpressureGate =

    /// Wire-side acknowledgement of a single inbound command. Encodes both
    /// the accept and reject paths so the gRPC service can return a single
    /// `CommandAck` message regardless of outcome.
    type CommandAck =
        { commandId: System.Guid
          accepted: bool
          reject: CommandPipeline.RejectReason option }

    /// Bridges a per-client `CommandPipeline.Queue` to gRPC HTTP/2 flow
    /// control on a server-side bidi stream. Pauses reads when the queue
    /// is at capacity; resumes when a slot frees (FR-010).
    type Gate

    val create :
        queue:CommandPipeline.Queue
        -> Gate

    /// Process one inbound command from the wire. Synchronous; never
    /// blocks the gRPC reader thread. Returns the `CommandAck` to send
    /// back to the peer.
    val process_ :
        gate:Gate
        -> mode:Mode.Mode
        -> roster:ScriptingRoster.Roster
        -> slots:ParticipantSlot.ParticipantSlot list
        -> command:CommandPipeline.Command
        -> CommandAck
