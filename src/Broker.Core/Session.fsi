namespace Broker.Core

open System

module Session =

    type BrokerInfo =
        { version: Version
          listenAddress: string
          startedAt: DateTimeOffset }

    type ProxyAiLink =
        { attachedAt: DateTimeOffset
          protocolVersion: Version
          lastSnapshotAt: DateTimeOffset option
          keepAliveIntervalMs: int
          // Coordinator-wire fields (feature 002, data-model §1.7). Empty
          // strings / zero defaults are used by the ProxyLink path until
          // it is removed in Phase 6; the HighBarCoordinatorService path
          // populates them from HeartbeatRequest.
          pluginId: string
          schemaVersion: string
          engineSha256: string
          lastHeartbeatAt: DateTimeOffset
          lastSeq: uint64 }

    type EndReason =
        | Victory
        | Defeat
        | OperatorTerminated
        | GameCrashed
        | ProxyDisconnected of detail:string

    type SessionState =
        | Configuring
        | Launching
        | Active
        | Ended of EndReason

    type Pause =
        | Paused
        | Running

    type SessionReading =
        { id: Guid
          mode: Mode.Mode
          state: SessionState
          startedAt: DateTimeOffset
          elapsed: TimeSpan
          proxy: ProxyAiLink option
          telemetry: Snapshot.GameStateSnapshot option
          pause: Pause
          speed: decimal }

    type Session

    val newHostSession : config:Lobby.LobbyConfig -> startedAt:DateTimeOffset -> Session
    val newGuestSession : startedAt:DateTimeOffset -> Session

    val state : Session -> SessionState
    val mode : Session -> Mode.Mode
    val id : Session -> Guid

    val attachProxy : link:ProxyAiLink -> session:Session -> Result<Session, string>
    val applySnapshot : snapshot:Snapshot.GameStateSnapshot -> session:Session -> Session

    /// Host-mode only. Transition `Configuring -> Launching` once the
    /// operator has confirmed launch and validation has passed (FR-013).
    /// The Launching -> Active transition is driven by proxy attach.
    /// Rejected from Launching / Active / Ended.
    val markLaunching : session:Session -> Result<Session, string>

    val end_ : reason:EndReason -> at:DateTimeOffset -> session:Session -> Session

    /// Toggle the session pause flag (Running <-> Paused). Idempotent on
    /// `Ended` sessions — they stay ended.
    val togglePause : session:Session -> Session

    /// Add `delta` to the session speed multiplier, clamped to [0.25, 8.0].
    /// FR-015 admin command surface.
    val stepSpeed : delta:decimal -> session:Session -> Session

    /// Pure projection used by the dashboard.
    val toReading : now:DateTimeOffset -> session:Session -> SessionReading

    /// Small surface that `Broker.Protocol` consumes from `Broker.Core`
    /// without dragging in the full module tree. Wired by the composition
    /// root in `Broker.App`.
    ///
    /// Read methods (`Mode`, `Roster`, `Slots`, `BrokerVersion`) are used
    /// by the TUI to render the dashboard. `OnXxx` methods are events the
    /// gRPC services push into the hub. `Operator*` methods are operator
    /// actions the TUI dispatches on key presses.
    type CoreFacade =
        abstract Mode : unit -> Mode.Mode
        abstract Roster : unit -> ScriptingRoster.Roster
        abstract Slots : unit -> ParticipantSlot.ParticipantSlot list
        abstract BrokerVersion : unit -> Version
        abstract OnSnapshot : Snapshot.GameStateSnapshot -> unit
        abstract OnClientConnected : ScriptingRoster.ScriptingClient -> unit
        abstract OnClientDisconnected : id:ScriptingClientId * reason:string -> unit
        abstract OperatorOpenHost : config:Lobby.LobbyConfig -> Result<unit, string>
        abstract OperatorLaunchHost : unit -> Result<unit, string>
        abstract OperatorTogglePause : unit -> Result<unit, string>
        abstract OperatorStepSpeed : delta:decimal -> Result<unit, string>
        abstract OperatorEndSession : unit -> Result<unit, string>
        abstract OperatorGrantAdmin : id:ScriptingClientId -> Result<unit, string>
        abstract OperatorRevokeAdmin : id:ScriptingClientId -> Result<unit, string>
