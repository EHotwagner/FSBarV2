namespace Broker.Protocol

open System
open System.Threading.Channels
open Broker.Core
open FSBarV2.Broker.Contracts

module BrokerState =

    /// Per-client protocol-edge state. Holds the bounded command queue
    /// (FR-010) and, while the client is subscribed to state, an
    /// outbound channel that the server-streaming `SubscribeState` RPC
    /// drains and writes to the wire.
    type ClientChannel =
        { id: ScriptingClientId
          queue: CommandPipeline.Queue
          mutable subscriber: Channel<StateMsg> option }

    /// In-process broker state. Owned by `Broker.App.Program` and shared
    /// across the two gRPC services + the TUI. All mutation is single-
    /// reader-aware (single TUI tick loop + one gRPC handler thread per
    /// client) and gated by a per-section lock; reads are lock-free.
    type Hub

    val create :
        brokerVersion:Version
        -> commandQueueCapacity:int
        -> auditEmitter:(Audit.AuditEvent -> unit)
        -> Hub

    val brokerVersion : hub:Hub -> Version
    val auditEmitter  : hub:Hub -> (Audit.AuditEvent -> unit)
    val mode          : hub:Hub -> Mode.Mode
    val roster        : hub:Hub -> ScriptingRoster.Roster
    val slots         : hub:Hub -> ParticipantSlot.ParticipantSlot list
    val session       : hub:Hub -> Session.Session option

    /// Mutate the host-mode lobby + initial session. Called from the TUI
    /// when the operator confirms a host-mode launch.
    val openHostSession : config:Lobby.LobbyConfig -> at:DateTimeOffset -> hub:Hub -> Result<unit, string>

    /// Auto-detect to Guest mode on first proxy attach (FR-002, FR-003).
    val openGuestSession : at:DateTimeOffset -> hub:Hub -> Result<unit, string>

    /// Host-mode launch: validate the active host session's lobby against
    /// the current connected-clients roster (FR-013) and, on success,
    /// transition the session from `Configuring` to `Launching`. Returns
    /// the validation error, the state-machine error, or unit on success.
    val launchHostSession :
        at:DateTimeOffset
        -> hub:Hub
        -> Result<unit, string>

    /// Tear down the active session and return to Idle. Notifies all
    /// scripting subscribers via `SessionEnd` and clears the proxy link
    /// (FR-014, FR-026, FR-027).
    val closeSession : reason:Session.EndReason -> at:DateTimeOffset -> hub:Hub -> unit

    // Coordinator-wire seam (feature 002, public-fsi.md).

    /// Owner-AI rule the coordinator service enforces (FR-011).
    type OwnerRule =
        | FirstAttached
        | Pinned of pluginId:string

    val expectedSchemaVersion : hub:Hub -> string
    val setExpectedSchemaVersion : v:string -> hub:Hub -> unit
    val ownerRule : hub:Hub -> OwnerRule
    val setOwnerRule : rule:OwnerRule -> hub:Hub -> unit

    /// Attach the coordinator-side ProxyAiLink. Equivalent to `attachProxy`
    /// today; named differently so the wire-side code reads as
    /// "attachCoordinator" rather than "attachProxy".
    val attachCoordinator : link:Session.ProxyAiLink -> hub:Hub -> Result<unit, string>

    /// Refresh `lastHeartbeatAt` and (if needed) capture the owner pluginId
    /// per the active OwnerRule. Returns `Error NotOwner` when a non-owner
    /// pluginId attempts a Heartbeat against a session whose owner is set.
    val noteHeartbeat :
        pluginId:string
        -> at:DateTimeOffset
        -> hub:Hub
        -> Result<unit, CommandPipeline.RejectReason>

    /// Surface a sequence-gap from PushState (FR-013). Emits a
    /// CoordinatorStateGap audit event and updates the dashboard staleness
    /// flag without rolling back the running view.
    val noteStateGap :
        pluginId:string
        -> lastSeq:uint64
        -> receivedSeq:uint64
        -> at:DateTimeOffset
        -> hub:Hub
        -> unit

    /// Lightweight liveness refresh — bumps `lastHeartbeatAt` without
    /// taking the owner-rule path or emitting an audit event. Called per
    /// inbound StateUpdate so the heartbeat watchdog does not false-trip
    /// during steady streaming. The unary `Heartbeat` RPC keeps using
    /// `noteHeartbeat` for the audit + owner check.
    val refreshLiveness : at:DateTimeOffset -> hub:Hub -> unit

    /// Most recent successful Heartbeat / accepted StateUpdate timestamp
    /// for the live coordinator session; `MinValue` when no session is
    /// attached. Read by the heartbeat watchdog for FR-008 detection.
    val lastHeartbeatAt : hub:Hub -> DateTimeOffset

    /// Plugin id captured by the first successful Heartbeat (`Some`
    /// once a session is attached, `None` while Idle).
    val activePluginId : hub:Hub -> string option

    /// True when a `CoordinatorStateGap` was raised since the last clear
    /// (FR-013 dashboard badge).
    val telemetryGap : hub:Hub -> bool

    /// Reset the gap badge — called by the dashboard renderer after the
    /// stale tick has been shown.
    val clearTelemetryGap : hub:Hub -> unit

    val applySnapshot : snapshot:Snapshot.GameStateSnapshot -> hub:Hub -> unit

    /// Push-based stream of the most recent in-process snapshots. Each
    /// successful `applySnapshot` call broadcasts a value here so the
    /// optional 2D viz can render without polling the Hub.
    val snapshots : hub:Hub -> IObservable<Snapshot.GameStateSnapshot>

    /// Toggle pause on the active session. No-op when no session.
    val togglePause : hub:Hub -> Result<unit, string>

    /// Adjust active-session speed by `delta`. No-op when no session.
    val stepSpeed : delta:decimal -> hub:Hub -> Result<unit, string>

    /// Channel of Core `Command`s the coordinator's `OpenCommandChannel`
    /// handler drains and writes outbound (after converting via
    /// `WireConvert.tryFromCoreCommandToHighBar`). None when no coordinator
    /// is currently attached.
    val coordinatorCommandChannel : hub:Hub -> Channel<CommandPipeline.Command> option

    /// Append a Core `Command` to the coordinator outbound channel. No-op
    /// when no coordinator is attached — commands with nowhere to go are
    /// dropped silently because the per-client queue's `QUEUE_FULL` reject
    /// already produced upstream feedback.
    val sendToCoordinator : command:CommandPipeline.Command -> hub:Hub -> unit

    /// Register a new scripting client (FR-008). Fails with `NameInUse`
    /// when the name collides with another live client.
    val registerClient :
        id:ScriptingClientId
        -> peerVersion:Version
        -> at:DateTimeOffset
        -> hub:Hub
        -> Result<ClientChannel, ScriptingRoster.RosterError>

    val unregisterClient : id:ScriptingClientId -> reason:string -> at:DateTimeOffset -> hub:Hub -> unit

    val tryGetClient : id:ScriptingClientId -> hub:Hub -> ClientChannel option

    /// Snapshot of all currently live `ClientChannel`s.
    val liveClients : hub:Hub -> ClientChannel list

    val grantAdmin : id:ScriptingClientId -> by:string -> at:DateTimeOffset -> hub:Hub -> Result<unit, ScriptingRoster.RosterError>
    val revokeAdmin : id:ScriptingClientId -> by:string -> at:DateTimeOffset -> hub:Hub -> Result<unit, ScriptingRoster.RosterError>

    /// Bind a slot to a client (FR-009, single-writer).
    val bindSlot :
        id:ScriptingClientId
        -> slot:int
        -> hub:Hub
        -> Result<unit, ParticipantSlot.SingleWriterError>

    val unbindSlot : id:ScriptingClientId -> slot:int -> hub:Hub -> unit

    /// Adapter so the TUI / dashboard can read the state through the
    /// `Session.CoreFacade` seam without depending on `Hub` directly.
    val asCoreFacade : hub:Hub -> Session.CoreFacade
