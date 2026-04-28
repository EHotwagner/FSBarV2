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

    val attachProxy : link:Session.ProxyAiLink -> hub:Hub -> Result<unit, string>
    val applySnapshot : snapshot:Snapshot.GameStateSnapshot -> hub:Hub -> unit

    /// Push-based stream of the most recent in-process snapshots. Each
    /// successful `applySnapshot` call broadcasts a value here so the
    /// optional 2D viz can render without polling the Hub.
    val snapshots : hub:Hub -> IObservable<Snapshot.GameStateSnapshot>

    /// Toggle pause on the active session. No-op when no session.
    val togglePause : hub:Hub -> Result<unit, string>

    /// Adjust active-session speed by `delta`. No-op when no session.
    val stepSpeed : delta:decimal -> hub:Hub -> Result<unit, string>

    /// Channel of Core `Command`s the proxy bidi handler drains and
    /// writes outbound (after converting via `WireConvert.fromCoreCommand`).
    /// None when no proxy is currently attached.
    val proxyOutbound : hub:Hub -> Channel<CommandPipeline.Command> option

    /// Append a Core `Command` to the proxy outbound channel. No-op when
    /// no proxy is attached (commands that have nowhere to go because the
    /// proxy detached are dropped on the proxy side; the per-client queue
    /// overflow already produced its `QUEUE_FULL` reject upstream).
    val sendToProxy : command:CommandPipeline.Command -> hub:Hub -> unit

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
