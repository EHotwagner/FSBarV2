namespace Broker.Core

open System

module CommandPipeline =

    type VisionMode = Normal | Full | Blind
    type VictoryOverride = ForceWin | ForceLose | Reset

    type AdminPayload =
        | SetSpeed of multiplier:decimal
        | Pause
        | Resume
        | GrantResources of playerId:int * resources:Snapshot.ResourceVector
        | OverrideVision of playerId:int * mode:VisionMode
        | OverrideVictory of playerId:int * outcome:VictoryOverride

    type OrderKind = Move | Attack | Stop | Guard | Patrol

    type GameplayPayload =
        | UnitOrder of unitIds:uint32 list * order:OrderKind * targetPos:Snapshot.Vec2 option * targetUnitId:uint32 option
        | Build of builderId:uint32 * classId:string * pos:Snapshot.Vec2
        | Custom of name:string * blob:byte[]

    type CommandKind =
        | Gameplay of payload:GameplayPayload
        | Admin of payload:AdminPayload

    type Command =
        { commandId: Guid
          originatingClient: ScriptingClientId
          targetSlot: int option
          kind: CommandKind
          submittedAt: DateTimeOffset }

    type RejectReason =
        | QueueFull
        | AdminNotAvailable
        | SlotNotOwned of slot:int * actualOwner:ScriptingClientId option
        | NameInUse
        | VersionMismatch of broker:Version * peer:Version
        | SchemaMismatch of expected:string * received:string
        | NotOwner of attemptedPluginId:string * ownerPluginId:string
        | InvalidPayload of detail:string

    type EnqueueOutcome =
        | Accepted
        | Rejected of RejectReason

    /// Per-client bounded queue (System.Threading.Channels.BoundedChannel
    /// inside). Caller-controlled capacity; default chosen by the protocol
    /// edge (typically 64).
    type Queue

    val createQueue : capacity:int -> Queue

    /// Synchronous enqueue. Never blocks. When the queue is full, the
    /// caller is expected to have already paused reads via the gRPC
    /// flow-control bridge (BackpressureGate); arrivals past the cap
    /// return `Rejected QueueFull` carrying the original commandId.
    val tryEnqueue : queue:Queue -> command:Command -> EnqueueOutcome

    /// Authority check. Pure — does not touch the queue. Returns `Ok ()`
    /// for accepted, `Error reason` for rejected (FR-004, FR-009, FR-016).
    val authorise :
        mode:Mode.Mode
        -> roster:ScriptingRoster.Roster
        -> slots:ParticipantSlot.ParticipantSlot list
        -> command:Command
        -> Result<unit, RejectReason>

    /// Drain up to `max` commands from the queue, in FIFO order.
    val drain : max:int -> queue:Queue -> Command list

    val depth : queue:Queue -> int

    val capacity : queue:Queue -> int
