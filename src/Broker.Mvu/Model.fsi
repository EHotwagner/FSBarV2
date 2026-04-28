namespace Broker.Mvu

open System
open Broker.Core

module Model =

    /// Owner-AI rule (carried forward verbatim from 002 §1.7). Lives in
    /// `Broker.Mvu.Model` so the configuration record can reference it
    /// without `Broker.Mvu` taking a project reference back into
    /// `Broker.Protocol`.
    type OwnerRule =
        | FixedSkirmishAiId of int
        | EnvVar of name:string
        | AcceptAny

    /// Operator-visible viz subsystem state. See data-model §1.4.
    type VizState =
        | Disabled
        | Closed
        | Active of openedAt:DateTimeOffset * statusLine:string
        | Failed of failedAt:DateTimeOffset * reason:string

    /// Per-client adapter-queue observation. Owned by `update`; the
    /// adapter populates via `Msg.AdapterCallback.QueueDepth` callbacks.
    /// See data-model §1.3.
    type QueueObservation = {
        depth: int
        highWaterMark: int
        overflowCount: int
        lastSampledAt: DateTimeOffset
        lastOverflowAt: DateTimeOffset option
    }

    /// In-flight gRPC handler tracking.
    type RpcWaiter = {
        issuedAt: DateTimeOffset
        operation: string
        tcs: obj
    }

    /// Registered timer schedule.
    type TimerHandle = {
        timerId: Cmd.TimerId
        scheduledAt: DateTimeOffset
        intervalMs: int
        pendingMsg: Msg.Msg
    }

    /// Startup-frozen broker configuration.
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

    val defaultConfig : BrokerConfig

    /// The single immutable broker state value. See data-model §1.1.
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

    /// Construct the initial Model from CLI args + bootstrap context.
    val init :
        brokerInfo:Session.BrokerInfo
        -> config:BrokerConfig
        -> startedAt:DateTimeOffset
        -> Model
