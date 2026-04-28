namespace Broker.Core

open System
open System.Threading.Channels

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

    type Queue =
        { capacity: int
          channel: Channel<Command> }

    let createQueue (capacity: int) : Queue =
        // FullMode = DropWrite would silently discard — explicitly forbidden
        // by FR-010 ("never silently dropped"). Using BoundedChannelFullMode
        // .Wait so the writer can be told via TryWrite=false that the queue
        // is full; the synchronous reject path turns that into QueueFull.
        let opts =
            BoundedChannelOptions(capacity,
                FullMode = BoundedChannelFullMode.Wait,
                SingleReader = false,
                SingleWriter = false)
        { capacity = capacity
          channel = Channel.CreateBounded<Command>(opts) }

    let tryEnqueue (queue: Queue) (command: Command) : EnqueueOutcome =
        if queue.channel.Writer.TryWrite command
        then Accepted
        else Rejected QueueFull

    let private isOwner (slots: ParticipantSlot.ParticipantSlot list) (slot: int) (client: ScriptingClientId) =
        slots
        |> List.tryFind (fun s -> s.slotIndex = slot)
        |> Option.bind (fun s -> s.boundClient)
        |> function
            | Some owner -> Ok (owner = client), Some owner
            | None       -> Ok false, None

    let authorise
        (mode: Mode.Mode)
        (roster: ScriptingRoster.Roster)
        (slots: ParticipantSlot.ParticipantSlot list)
        (command: Command)
        : Result<unit, RejectReason> =
        match command.kind with
        | Admin _ ->
            // FR-004 / FR-016: only Hosting + isAdmin permits admin.
            match mode with
            | Mode.Mode.Hosting _ when ScriptingRoster.isAdmin command.originatingClient roster -> Ok ()
            | _ -> Error AdminNotAvailable
        | Gameplay _ ->
            match command.targetSlot with
            | None -> Error (InvalidPayload "gameplay command requires targetSlot")
            | Some slot ->
                let owns, actual = isOwner slots slot command.originatingClient
                match owns with
                | Ok true -> Ok ()
                | _       -> Error (SlotNotOwned (slot, actual))

    let drain (max: int) (queue: Queue) : Command list =
        let acc = ResizeArray<Command>()
        let mutable n = 0
        let mutable item = Unchecked.defaultof<Command>
        while n < max && queue.channel.Reader.TryRead(&item) do
            acc.Add item
            n <- n + 1
        List.ofSeq acc

    let depth (queue: Queue) : int =
        queue.channel.Reader.Count

    let capacity (queue: Queue) : int =
        queue.capacity
