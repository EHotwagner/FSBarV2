namespace Broker.Protocol

open System
open Broker.Core
open FSBarV2.Broker.Contracts

module WireConvert =

    let private bytesToGuid (bs: Google.Protobuf.ByteString) : Guid =
        if bs.Length <> 16 then Guid.Empty
        else Guid(bs.ToByteArray())

    let private guidToBytes (g: Guid) : Google.Protobuf.ByteString =
        Google.Protobuf.ByteString.CopyFrom(g.ToByteArray())

    let private toCoreVecOpt (v: ValueOption<Vec2>) : Snapshot.Vec2 =
        match v with
        | ValueSome v -> { x = v.X; y = v.Y }
        | ValueNone -> { x = 0.0f; y = 0.0f }

    let private fromCoreVec (v: Snapshot.Vec2) : Vec2 =
        let w = Vec2.empty()
        w.X <- v.x
        w.Y <- v.y
        w

    let private toCoreResOpt (r: ValueOption<ResourceVector>) : Snapshot.ResourceVector =
        match r with
        | ValueSome r -> { metal = r.Metal; energy = r.Energy }
        | ValueNone -> { metal = 0.0; energy = 0.0 }

    let private fromCoreResources (r: Snapshot.ResourceVector) : ResourceVector =
        let w = ResourceVector.empty()
        w.Metal <- r.metal
        w.Energy <- r.energy
        w

    let private toCoreEconOpt (e: ValueOption<EconomyStats>) : Snapshot.EconomyStats =
        match e with
        | ValueSome e -> { income = toCoreResOpt e.Income; expenditure = toCoreResOpt e.Expenditure }
        | ValueNone -> { income = { metal = 0.0; energy = 0.0 }; expenditure = { metal = 0.0; energy = 0.0 } }

    let private fromCoreEconomy (e: Snapshot.EconomyStats) : EconomyStats =
        let w = EconomyStats.empty()
        w.Income <- ValueSome (fromCoreResources e.income)
        w.Expenditure <- ValueSome (fromCoreResources e.expenditure)
        w

    let private toCorePlayer (p: PlayerTelemetry) : Snapshot.PlayerTelemetry =
        { playerId = p.PlayerId
          teamId = p.TeamId
          name = p.Name
          resources = toCoreResOpt p.Resources
          unitCount = int p.UnitCount
          buildingCount = int p.BuildingCount
          unitClassBreakdown =
              p.UnitClassBreakdown
              |> Seq.map (fun kv -> kv.Key, int kv.Value)
              |> Map.ofSeq
          economy = toCoreEconOpt p.Economy
          kills = int p.Kills
          losses = int p.Losses }

    let private fromCorePlayer (p: Snapshot.PlayerTelemetry) : PlayerTelemetry =
        let w = PlayerTelemetry.empty()
        w.PlayerId <- p.playerId
        w.TeamId <- p.teamId
        w.Name <- p.name
        w.Resources <- ValueSome (fromCoreResources p.resources)
        w.UnitCount <- uint32 p.unitCount
        w.BuildingCount <- uint32 p.buildingCount
        for KeyValue(k, v) in p.unitClassBreakdown do
            w.UnitClassBreakdown[k] <- uint32 v
        w.Economy <- ValueSome (fromCoreEconomy p.economy)
        w.Kills <- uint32 p.kills
        w.Losses <- uint32 p.losses
        w

    let private toCoreUnit (u: Unit) : Snapshot.Unit =
        { id = u.Id
          classId = u.ClassId
          ownerPlayerId = u.OwnerPlayerId
          pos = toCoreVecOpt u.Pos }

    let private toCoreBuilding (b: Building) : Snapshot.Building =
        { id = b.Id
          classId = b.ClassId
          ownerPlayerId = b.OwnerPlayerId
          pos = toCoreVecOpt b.Pos }

    let private fromCoreUnit (u: Snapshot.Unit) : Unit =
        let w = Unit.empty()
        w.Id <- u.id
        w.ClassId <- u.classId
        w.OwnerPlayerId <- u.ownerPlayerId
        w.Pos <- ValueSome (fromCoreVec u.pos)
        w

    let private fromCoreBuilding (b: Snapshot.Building) : Building =
        let w = Building.empty()
        w.Id <- b.id
        w.ClassId <- b.classId
        w.OwnerPlayerId <- b.ownerPlayerId
        w.Pos <- ValueSome (fromCoreVec b.pos)
        w

    let private toCoreMapMetaOpt (m: ValueOption<MapMeta>) : Snapshot.MapMeta option =
        match m with
        | ValueSome m ->
            Some
                { mapName = m.MapName
                  size = toCoreVecOpt m.Size
                  outline = m.Outline.ToByteArray() }
        | ValueNone -> None

    let toCoreSnapshot (msg: GameStateSnapshot) : Snapshot.GameStateSnapshot =
        { sessionId = bytesToGuid msg.SessionId
          tick = msg.Tick
          capturedAt = DateTimeOffset.FromUnixTimeMilliseconds(msg.CapturedAtUnixMs)
          players = msg.Players |> Seq.map toCorePlayer |> List.ofSeq
          units = msg.Units |> Seq.map toCoreUnit |> List.ofSeq
          buildings = msg.Buildings |> Seq.map toCoreBuilding |> List.ofSeq
          mapMeta = toCoreMapMetaOpt msg.MapMeta }

    let fromCoreSnapshot (snapshot: Snapshot.GameStateSnapshot) : GameStateSnapshot =
        let w = GameStateSnapshot.empty()
        w.SessionId <- guidToBytes snapshot.sessionId
        w.Tick <- snapshot.tick
        w.CapturedAtUnixMs <- snapshot.capturedAt.ToUnixTimeMilliseconds()
        for p in snapshot.players do
            w.Players.Add(fromCorePlayer p)
        for u in snapshot.units do
            w.Units.Add(fromCoreUnit u)
        for b in snapshot.buildings do
            w.Buildings.Add(fromCoreBuilding b)
        match snapshot.mapMeta with
        | None -> ()
        | Some m ->
            let mw = MapMeta.empty()
            mw.MapName <- m.mapName
            mw.Size <- ValueSome (fromCoreVec m.size)
            mw.Outline <- Google.Protobuf.ByteString.CopyFrom(m.outline)
            w.MapMeta <- ValueSome mw
        w

    let toCoreVersion (msg: ProtocolVersion) : Version =
        Version(int msg.Major, int msg.Minor)

    let toCoreVersionOpt (msg: ValueOption<ProtocolVersion>) : Version =
        match msg with
        | ValueSome v -> toCoreVersion v
        | ValueNone -> Version(0, 0)

    let fromCoreVersion (version: Version) : ProtocolVersion =
        let w = ProtocolVersion.empty()
        w.Major <- uint32 (max 0 version.Major)
        w.Minor <- uint32 (max 0 version.Minor)
        w

    let private toCoreOrderKind (k: UnitOrder.Types.OrderKind) : CommandPipeline.OrderKind =
        match k with
        | UnitOrder.Types.OrderKind.Move    -> CommandPipeline.Move
        | UnitOrder.Types.OrderKind.Attack  -> CommandPipeline.Attack
        | UnitOrder.Types.OrderKind.Stop    -> CommandPipeline.Stop
        | UnitOrder.Types.OrderKind.Guard   -> CommandPipeline.Guard
        | UnitOrder.Types.OrderKind.Patrol  -> CommandPipeline.Patrol
        | _                                 -> CommandPipeline.Stop

    let private fromCoreOrderKind (k: CommandPipeline.OrderKind) : UnitOrder.Types.OrderKind =
        match k with
        | CommandPipeline.Move    -> UnitOrder.Types.OrderKind.Move
        | CommandPipeline.Attack  -> UnitOrder.Types.OrderKind.Attack
        | CommandPipeline.Stop    -> UnitOrder.Types.OrderKind.Stop
        | CommandPipeline.Guard   -> UnitOrder.Types.OrderKind.Guard
        | CommandPipeline.Patrol  -> UnitOrder.Types.OrderKind.Patrol

    let private toCoreVision (m: VisionMode) : CommandPipeline.VisionMode =
        match m with
        | VisionMode.Full   -> CommandPipeline.Full
        | VisionMode.Blind  -> CommandPipeline.Blind
        | _                 -> CommandPipeline.Normal

    let private toCoreVictory (m: VictoryOverride) : CommandPipeline.VictoryOverride =
        match m with
        | VictoryOverride.ForceWin   -> CommandPipeline.ForceWin
        | VictoryOverride.ForceLose  -> CommandPipeline.ForceLose
        | _                          -> CommandPipeline.Reset

    let private fromCoreVision (m: CommandPipeline.VisionMode) : VisionMode =
        match m with
        | CommandPipeline.Full   -> VisionMode.Full
        | CommandPipeline.Blind  -> VisionMode.Blind
        | CommandPipeline.Normal -> VisionMode.Normal

    let private fromCoreVictory (m: CommandPipeline.VictoryOverride) : VictoryOverride =
        match m with
        | CommandPipeline.ForceWin  -> VictoryOverride.ForceWin
        | CommandPipeline.ForceLose -> VictoryOverride.ForceLose
        | CommandPipeline.Reset     -> VictoryOverride.Reset

    let private toCoreGameplay (gp: GameplayPayload) : CommandPipeline.GameplayPayload =
        match gp.Body with
        | ValueSome (GameplayPayload.Types.Body.UnitOrder uo) ->
            let target =
                match uo.TargetPos with
                | ValueSome p -> Some { Snapshot.x = p.X; Snapshot.y = p.Y }
                | ValueNone -> None
            let targetUnit =
                if uo.TargetUnitId = 0u then None else Some uo.TargetUnitId
            CommandPipeline.UnitOrder (
                uo.UnitIds |> List.ofSeq,
                toCoreOrderKind uo.Kind,
                target,
                targetUnit)
        | ValueSome (GameplayPayload.Types.Body.Build bo) ->
            CommandPipeline.Build (bo.BuilderId, bo.ClassId, toCoreVecOpt bo.Pos)
        | ValueSome (GameplayPayload.Types.Body.Custom c) ->
            CommandPipeline.Custom (c.Name, c.Blob.ToByteArray())
        | ValueNone ->
            CommandPipeline.Custom ("", [||])

    let private toCoreAdmin (ap: AdminPayload) : CommandPipeline.AdminPayload =
        match ap.Body with
        | ValueSome (AdminPayload.Types.Body.SetSpeed s) -> CommandPipeline.SetSpeed (decimal s.Multiplier)
        | ValueSome (AdminPayload.Types.Body.Pause _)    -> CommandPipeline.Pause
        | ValueSome (AdminPayload.Types.Body.Resume _)   -> CommandPipeline.Resume
        | ValueSome (AdminPayload.Types.Body.GrantResources g) ->
            CommandPipeline.GrantResources (g.PlayerId, toCoreResOpt g.Resources)
        | ValueSome (AdminPayload.Types.Body.OverrideVision v) ->
            CommandPipeline.OverrideVision (v.PlayerId, toCoreVision v.Mode)
        | ValueSome (AdminPayload.Types.Body.OverrideVictory v) ->
            CommandPipeline.OverrideVictory (v.PlayerId, toCoreVictory v.Outcome)
        | ValueNone -> CommandPipeline.Pause

    let toCoreCommand (msg: Command) : CommandPipeline.Command =
        let kind =
            match msg.Kind with
            | ValueSome (Command.Types.Kind.Gameplay gp) -> CommandPipeline.Gameplay (toCoreGameplay gp)
            | ValueSome (Command.Types.Kind.Admin ap)    -> CommandPipeline.Admin (toCoreAdmin ap)
            | ValueNone -> CommandPipeline.Gameplay (CommandPipeline.Custom ("", [||]))
        let cid = bytesToGuid msg.CommandId
        let target = if msg.TargetSlot = 0 then None else Some msg.TargetSlot
        { commandId = (if cid = Guid.Empty then Guid.NewGuid() else cid)
          originatingClient = ScriptingClientId msg.OriginatingClient
          targetSlot = target
          kind = kind
          submittedAt = DateTimeOffset.FromUnixTimeMilliseconds(msg.SubmittedAtUnixMs) }

    let fromCoreCommand (command: CommandPipeline.Command) : Command =
        let w = Command.empty()
        w.CommandId <- guidToBytes command.commandId
        let (ScriptingClientId name) = command.originatingClient
        w.OriginatingClient <- name
        w.TargetSlot <- defaultArg command.targetSlot 0
        w.SubmittedAtUnixMs <- command.submittedAt.ToUnixTimeMilliseconds()
        match command.kind with
        | CommandPipeline.Gameplay gp ->
            let gw = GameplayPayload.empty()
            match gp with
            | CommandPipeline.UnitOrder (ids, kind, pos, target) ->
                let uo = UnitOrder.empty()
                for id in ids do uo.UnitIds.Add(id)
                uo.Kind <- fromCoreOrderKind kind
                pos |> Option.iter (fun p -> uo.TargetPos <- ValueSome (fromCoreVec p))
                target |> Option.iter (fun t -> uo.TargetUnitId <- t)
                gw.UnitOrder <- uo
            | CommandPipeline.Build (bid, cid, p) ->
                let bo = BuildOrder.empty()
                bo.BuilderId <- bid
                bo.ClassId <- cid
                bo.Pos <- ValueSome (fromCoreVec p)
                gw.Build <- bo
            | CommandPipeline.Custom (n, blob) ->
                let cw = CustomCommand.empty()
                cw.Name <- n
                cw.Blob <- Google.Protobuf.ByteString.CopyFrom(blob)
                gw.Custom <- cw
            w.Gameplay <- gw
        | CommandPipeline.Admin ap ->
            let aw = AdminPayload.empty()
            match ap with
            | CommandPipeline.SetSpeed m ->
                let s = SetSpeed.empty()
                s.Multiplier <- float m
                aw.SetSpeed <- s
            | CommandPipeline.Pause -> aw.Pause <- Pause.empty()
            | CommandPipeline.Resume -> aw.Resume <- Resume.empty()
            | CommandPipeline.GrantResources (pid, r) ->
                let g = GrantResources.empty()
                g.PlayerId <- pid
                g.Resources <- ValueSome (fromCoreResources r)
                aw.GrantResources <- g
            | CommandPipeline.OverrideVision (pid, m) ->
                let ov = OverrideVision.empty()
                ov.PlayerId <- pid
                ov.Mode <- fromCoreVision m
                aw.OverrideVision <- ov
            | CommandPipeline.OverrideVictory (pid, outcome) ->
                let ov = OverrideVictoryMessage.empty()
                ov.PlayerId <- pid
                ov.Outcome <- fromCoreVictory outcome
                aw.OverrideVictory <- ov
            w.Admin <- aw
        w

    let toReject
        (reason: CommandPipeline.RejectReason)
        (commandId: Guid option)
        (brokerVersion: Version option)
        : Reject =
        let w = Reject.empty()
        let code, detail =
            match reason with
            | CommandPipeline.QueueFull -> Reject.Types.Code.QueueFull, "queue full"
            | CommandPipeline.AdminNotAvailable -> Reject.Types.Code.AdminNotAvailable, "admin not available"
            | CommandPipeline.SlotNotOwned (s, owner) ->
                let detail =
                    match owner with
                    | Some (ScriptingClientId n) -> sprintf "slot %d owned by %s" s n
                    | None -> sprintf "slot %d unowned" s
                Reject.Types.Code.SlotNotOwned, detail
            | CommandPipeline.NameInUse -> Reject.Types.Code.NameInUse, "name in use"
            | CommandPipeline.VersionMismatch (b, p) ->
                Reject.Types.Code.VersionMismatch, sprintf "broker %O peer %O" b p
            | CommandPipeline.InvalidPayload d ->
                Reject.Types.Code.InvalidPayload, d
        w.Code <- code
        w.Detail <- detail
        match commandId with
        | Some id -> w.CommandId <- guidToBytes id
        | None -> ()
        match brokerVersion with
        | Some v -> w.BrokerVersion <- ValueSome (fromCoreVersion v)
        | None -> ()
        w
