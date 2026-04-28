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

    type Session =
        { id: Guid
          mode: Mode.Mode
          state: SessionState
          startedAt: DateTimeOffset
          proxy: ProxyAiLink option
          telemetry: Snapshot.GameStateSnapshot option
          pause: Pause
          speed: decimal }

    let newHostSession (config: Lobby.LobbyConfig) (startedAt: DateTimeOffset) : Session =
        { id = Guid.NewGuid()
          mode = Mode.Mode.Hosting config
          state = Configuring
          startedAt = startedAt
          proxy = None
          telemetry = None
          pause = Running
          speed = 1.0m }

    let newGuestSession (startedAt: DateTimeOffset) : Session =
        { id = Guid.NewGuid()
          mode = Mode.Mode.Guest
          state = Active     // FR-002, FR-003: guest mode is auto-detected when proxy attaches.
          startedAt = startedAt
          proxy = None
          telemetry = None
          pause = Running
          speed = 1.0m }

    let state (s: Session) : SessionState = s.state
    let mode (s: Session) : Mode.Mode = s.mode
    let id (s: Session) : Guid = s.id

    let attachProxy (link: ProxyAiLink) (session: Session) : Result<Session, string> =
        match session.state, session.proxy with
        | Ended _, _ -> Error "session has ended; cannot attach proxy"
        | _, Some _  -> Error "proxy already attached (single-session broker)"
        | _, None ->
            // Configuring (host) → Launching → Active on attach.
            // Guest sessions start in Active per newGuestSession.
            let newState =
                match session.state with
                | Configuring | Launching -> Active
                | s -> s
            Ok { session with proxy = Some link; state = newState }

    let markLaunching (session: Session) : Result<Session, string> =
        match session.mode, session.state with
        | Mode.Mode.Hosting _, Configuring -> Ok { session with state = Launching }
        | Mode.Mode.Hosting _, s ->
            Error (sprintf "markLaunching expected Configuring; got %A" s)
        | other, _ ->
            Error (sprintf "markLaunching only valid in Hosting mode; got %A" other)

    let applySnapshot (snapshot: Snapshot.GameStateSnapshot) (session: Session) : Session =
        let proxy =
            session.proxy
            |> Option.map (fun p -> { p with lastSnapshotAt = Some snapshot.capturedAt })
        { session with telemetry = Some snapshot; proxy = proxy }

    let end_ (reason: EndReason) (at: DateTimeOffset) (session: Session) : Session =
        ignore at  // included in API for future audit-stamping; current Session.state carries the reason only.
        { session with state = Ended reason }

    let togglePause (session: Session) : Session =
        match session.state with
        | Ended _ -> session
        | _ ->
            let next =
                match session.pause with
                | Paused  -> Running
                | Running -> Paused
            { session with pause = next }

    let stepSpeed (delta: decimal) (session: Session) : Session =
        let clamp (v: decimal) =
            if v < 0.25m then 0.25m
            elif v > 8.0m then 8.0m
            else v
        { session with speed = clamp (session.speed + delta) }

    let toReading (now: DateTimeOffset) (session: Session) : SessionReading =
        { id = session.id
          mode = session.mode
          state = session.state
          startedAt = session.startedAt
          elapsed = now - session.startedAt
          proxy = session.proxy
          telemetry = session.telemetry
          pause = session.pause
          speed = session.speed }

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
