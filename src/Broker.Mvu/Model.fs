namespace Broker.Mvu

open System
open Broker.Core

module Model =

    type OwnerRule =
        | FixedSkirmishAiId of int
        | EnvVar of name:string
        | AcceptAny

    type VizState =
        | Disabled
        | Closed
        | Active of openedAt:DateTimeOffset * statusLine:string
        | Failed of failedAt:DateTimeOffset * reason:string

    type QueueObservation = {
        depth: int
        highWaterMark: int
        overflowCount: int
        lastSampledAt: DateTimeOffset
        lastOverflowAt: DateTimeOffset option
    }

    type RpcWaiter = {
        issuedAt: DateTimeOffset
        operation: string
        tcs: obj
    }

    type TimerHandle = {
        timerId: Cmd.TimerId
        scheduledAt: DateTimeOffset
        intervalMs: int
        pendingMsg: Msg.Msg
    }

    type BrokerConfig = {
        listenAddress: string
        expectedSchemaVersion: string
        ownerRule: OwnerRule
        heartbeatTimeoutMs: int
        commandQueueCapacity: int
        perClientQueueCapacity: int
        mailboxHighWaterMark: int
        mailboxHighWaterCooldownMs: int
        queueDepthSampleMs: int
        tickIntervalMs: int
        vizEnabled: bool
    }

    let defaultConfig : BrokerConfig =
        { listenAddress = "127.0.0.1:5021"
          expectedSchemaVersion = "1.0.0"
          ownerRule = AcceptAny
          heartbeatTimeoutMs = 5000
          commandQueueCapacity = 64
          perClientQueueCapacity = 256
          mailboxHighWaterMark = 1024
          mailboxHighWaterCooldownMs = 5000
          queueDepthSampleMs = 250
          tickIntervalMs = 100
          vizEnabled = true }

    type Model = {
        brokerInfo: Session.BrokerInfo
        config: BrokerConfig
        startedAt: DateTimeOffset
        mode: Mode.Mode
        session: Session.Session option
        coordinator: Session.ProxyAiLink option
        roster: ScriptingRoster.Roster
        slots: ParticipantSlot.ParticipantSlot list
        queues: Map<ScriptingClientId, QueueObservation>
        snapshot: Snapshot.GameStateSnapshot option
        pendingLobby: Lobby.LobbyConfig option
        elevation: ScriptingClientId option
        viz: VizState
        mailboxDepth: int
        mailboxHighWater: int
        lastMailboxAuditAt: DateTimeOffset option
        pendingRpcs: Map<Cmd.RpcId, RpcWaiter>
        timers: Map<Cmd.TimerId, TimerHandle>
        kickedClients: Set<ScriptingClientId>
    }

    let init
        (brokerInfo: Session.BrokerInfo)
        (config: BrokerConfig)
        (startedAt: DateTimeOffset)
        : Model =
        { brokerInfo = brokerInfo
          config = config
          startedAt = startedAt
          mode = Mode.Idle
          session = None
          coordinator = None
          roster = ScriptingRoster.empty
          slots = []
          queues = Map.empty
          snapshot = None
          pendingLobby = None
          elevation = None
          viz = if config.vizEnabled then VizState.Closed else VizState.Disabled
          mailboxDepth = 0
          mailboxHighWater = 0
          lastMailboxAuditAt = None
          pendingRpcs = Map.empty
          timers = Map.empty
          kickedClients = Set.empty }
