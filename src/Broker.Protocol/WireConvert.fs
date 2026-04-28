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
            | CommandPipeline.SchemaMismatch (expected, received) ->
                // Coordinator-wire concern; if a scripting-client somehow sees
                // it, surface it as InvalidPayload with both versions in the
                // detail (data-model §4: SchemaMismatch is not a ScriptingClient
                // wire code).
                Reject.Types.Code.InvalidPayload, sprintf "schema mismatch expected=%s received=%s" expected received
            | CommandPipeline.NotOwner (attempted, owner) ->
                // Coordinator-wire concern; ScriptingClient never sees this. If
                // it bubbles up here, surface descriptively.
                Reject.Types.Code.InvalidPayload, sprintf "not owner attempted=%s owner=%s" attempted owner
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

    // === Coordinator side (feature 002) =========================================

    type RunningView =
        { sessionId: Guid
          lastSeq: uint64
          units: Map<uint32, Snapshot.Unit>
          features: Map<uint32, Snapshot.Feature>
          mapMeta: Snapshot.MapMeta option
          economy: Snapshot.ResourceVector option
          lastFrame: int64 }

    let emptyRunningView : RunningView =
        { sessionId = Guid.Empty
          lastSeq = 0UL
          units = Map.empty
          features = Map.empty
          mapMeta = None
          economy = None
          lastFrame = 0L }

    let lastSeq (view: RunningView) : uint64 = view.lastSeq

    type ApplyResult =
        | NewSnapshot of Snapshot.GameStateSnapshot
        | Gap of lastSeq:uint64 * receivedSeq:uint64
        | KeepAliveOnly

    // --- HighBar → Core helpers ---

    let private vec3ToVec2 (v: Highbar.V1.Vector3) : Snapshot.Vec2 =
        { x = v.X; y = v.Y }   // research §2: drop z (broker is 2D)

    let private vec3OptToVec2 (v: ValueOption<Highbar.V1.Vector3>) : Snapshot.Vec2 =
        match v with
        | ValueSome v -> vec3ToVec2 v
        | ValueNone -> { x = 0.0f; y = 0.0f }

    let private ownUnitToCoreUnit (u: Highbar.V1.OwnUnit) : Snapshot.Unit =
        { id = u.UnitId
          classId = string u.DefId
          ownerPlayerId = u.TeamId
          pos = vec3OptToVec2 u.Position }

    let private enemyUnitToCoreUnit (u: Highbar.V1.EnemyUnit) : Snapshot.Unit =
        { id = u.UnitId
          classId = string u.DefId
          ownerPlayerId = u.TeamId
          pos = vec3OptToVec2 u.Position }

    let private mapFeatureToCoreFeature (f: Highbar.V1.MapFeature) : Snapshot.Feature =
        { id = f.FeatureId
          kind = string f.DefId
          pos = vec3OptToVec2 f.Position }

    let private staticMapToCoreMapMeta (m: ValueOption<Highbar.V1.StaticMap>) : Snapshot.MapMeta option =
        match m with
        | ValueSome sm ->
            Some
                { mapName = ""
                  size = { x = float32 sm.WidthCells; y = float32 sm.HeightCells }
                  outline = sm.Heightmap.ToByteArray() }
        | ValueNone -> None

    let private teamEconomyToCoreResources (e: ValueOption<Highbar.V1.TeamEconomy>) : Snapshot.ResourceVector option =
        match e with
        | ValueSome te ->
            Some { metal = float te.Metal; energy = float te.Energy }
        | ValueNone -> None

    let private snapshotFromView (view: RunningView) : Snapshot.GameStateSnapshot =
        let unitList = view.units |> Map.toList |> List.map snd
        let featureList = view.features |> Map.toList |> List.map snd
        // Collapse the single-team economy into a synthetic player so the
        // dashboard's per-player telemetry pane has something to show.
        let players : Snapshot.PlayerTelemetry list =
            match view.economy with
            | Some r ->
                [ { playerId = 0
                    teamId = 0
                    name = "host"
                    resources = r
                    unitCount = unitList.Length
                    buildingCount = 0
                    unitClassBreakdown = Map.empty
                    economy = { income = { metal = 0.0; energy = 0.0 }; expenditure = { metal = 0.0; energy = 0.0 } }
                    kills = 0
                    losses = 0 } ]
            | None -> []
        { sessionId = view.sessionId
          tick = view.lastFrame
          capturedAt = DateTimeOffset.UtcNow
          players = players
          units = unitList
          buildings = []
          features = featureList
          mapMeta = view.mapMeta }

    let applyHighBarStateUpdate
        (update: Highbar.V1.StateUpdate)
        (view: RunningView)
        : RunningView * ApplyResult =
        let recvSeq = update.Seq
        // Gap detection: only meaningful once we've seen at least one update.
        // First update sets the running seq; subsequent gaps must skip ≥ 2.
        if view.lastSeq > 0UL && recvSeq > view.lastSeq + 1UL then
            { view with lastSeq = recvSeq }, Gap (view.lastSeq, recvSeq)
        else
            match update.Payload with
            | ValueSome (Highbar.V1.StateUpdate.Types.Payload.Snapshot ss) ->
                let ownUnits =
                    ss.OwnUnits
                    |> Seq.map (fun ou -> ou.UnitId, ownUnitToCoreUnit ou)
                let visibleEnemies =
                    ss.VisibleEnemies
                    |> Seq.map (fun eu -> eu.UnitId, enemyUnitToCoreUnit eu)
                let units =
                    Seq.append ownUnits visibleEnemies
                    |> Map.ofSeq
                let features =
                    ss.MapFeatures
                    |> Seq.map (fun mf -> mf.FeatureId, mapFeatureToCoreFeature mf)
                    |> Map.ofSeq
                let view' =
                    { view with
                        lastSeq = recvSeq
                        units = units
                        features = features
                        mapMeta = staticMapToCoreMapMeta ss.StaticMap
                        economy = teamEconomyToCoreResources ss.Economy
                        lastFrame = int64 update.Frame }
                view', NewSnapshot (snapshotFromView view')
            | ValueSome (Highbar.V1.StateUpdate.Types.Payload.Delta _) ->
                // Phase 3 partial: the running view does not currently fold
                // individual delta events (research §2 lists 27 variants).
                // Surface the latest tick + emit the current snapshot — the
                // underlying broker dashboard does not regress, and the
                // gap-free guarantee is preserved by the seq check above.
                let view' = { view with lastSeq = recvSeq; lastFrame = int64 update.Frame }
                view', NewSnapshot (snapshotFromView view')
            | ValueSome (Highbar.V1.StateUpdate.Types.Payload.Keepalive _) ->
                { view with lastSeq = recvSeq }, KeepAliveOnly
            | ValueNone ->
                { view with lastSeq = recvSeq }, KeepAliveOnly

    // --- Core → HighBar helpers ---

    let private vec2ToVec3 (v: Snapshot.Vec2) : Highbar.V1.Vector3 =
        let w = Highbar.V1.Vector3.empty()
        w.X <- v.x
        w.Y <- v.y
        w.Z <- 0.0f   // broker is 2D; engine treats z as ground height
        w

    let private commandBatch (seq: uint64) (targetUnitId: uint32) (commandId: Guid) (ais: Highbar.V1.AICommand list) : Highbar.V1.CommandBatch =
        let cb = Highbar.V1.CommandBatch.empty()
        cb.BatchSeq <- seq
        cb.TargetUnitId <- targetUnitId
        for ai in ais do cb.Commands.Add(ai)
        // ClientCommandId is a uint64 carrying the lower 64 bits of the UUID.
        let bytes = commandId.ToByteArray()
        let lower = System.BitConverter.ToUInt64(bytes, 0)
        cb.ClientCommandId <- ValueSome lower
        cb

    let tryFromCoreCommandToHighBar
        (command: CommandPipeline.Command)
        (batchSeq: uint64)
        : Result<Highbar.V1.CommandBatch, CommandPipeline.RejectReason> =
        let firstUnit (ids: uint32 list) : int32 =
            match ids with
            | u :: _ -> int u
            | [] -> 0
        let firstUnitU (ids: uint32 list) : uint32 =
            match ids with
            | u :: _ -> u
            | [] -> 0u
        match command.kind with
        | CommandPipeline.Gameplay (CommandPipeline.UnitOrder (uids, kind, targetPos, targetUnitId)) ->
            let target = firstUnitU uids
            match kind, targetUnitId, targetPos with
            | CommandPipeline.Move, _, Some pos ->
                let mu = Highbar.V1.MoveUnitCommand.empty()
                mu.UnitId <- firstUnit uids
                mu.ToPosition <- ValueSome (vec2ToVec3 pos)
                let ai = Highbar.V1.AICommand.empty()
                ai.MoveUnit <- mu
                Ok (commandBatch batchSeq target command.commandId [ai])
            | CommandPipeline.Move, _, None ->
                Error (CommandPipeline.InvalidPayload "Move requires targetPos")
            | CommandPipeline.Attack, Some tid, _ ->
                let ac = Highbar.V1.AttackCommand.empty()
                ac.UnitId <- firstUnit uids
                ac.TargetUnitId <- int tid
                let ai = Highbar.V1.AICommand.empty()
                ai.Attack <- ac
                Ok (commandBatch batchSeq target command.commandId [ai])
            | CommandPipeline.Attack, None, Some pos ->
                let aa = Highbar.V1.AttackAreaCommand.empty()
                aa.UnitId <- firstUnit uids
                aa.AttackPosition <- ValueSome (vec2ToVec3 pos)
                aa.Radius <- 64.0f   // broker default; tunable later
                let ai = Highbar.V1.AICommand.empty()
                ai.AttackArea <- aa
                Ok (commandBatch batchSeq target command.commandId [ai])
            | CommandPipeline.Attack, None, None ->
                Error (CommandPipeline.InvalidPayload "Attack requires either targetUnitId or targetPos")
            | CommandPipeline.Stop, _, _ ->
                let s = Highbar.V1.StopCommand.empty()
                s.UnitId <- firstUnit uids
                let ai = Highbar.V1.AICommand.empty()
                ai.Stop <- s
                Ok (commandBatch batchSeq target command.commandId [ai])
            | CommandPipeline.Guard, _, _ ->
                let g = Highbar.V1.GuardCommand.empty()
                g.UnitId <- firstUnit uids
                let ai = Highbar.V1.AICommand.empty()
                ai.Guard <- g
                Ok (commandBatch batchSeq target command.commandId [ai])
            | CommandPipeline.Patrol, _, Some pos ->
                let p = Highbar.V1.PatrolCommand.empty()
                p.UnitId <- firstUnit uids
                p.ToPosition <- ValueSome (vec2ToVec3 pos)
                let ai = Highbar.V1.AICommand.empty()
                ai.Patrol <- p
                Ok (commandBatch batchSeq target command.commandId [ai])
            | CommandPipeline.Patrol, _, None ->
                Error (CommandPipeline.InvalidPayload "Patrol requires targetPos")
        | CommandPipeline.Gameplay (CommandPipeline.Build (builderId, classId, pos)) ->
            let b = Highbar.V1.BuildUnitCommand.empty()
            b.UnitId <- int builderId
            // classId is a class name (string) on the Core side; HighBar
            // expects an int unit-def id. Try parse; fall back to 0 if the
            // string is not numeric. Real translation needs a class/def map.
            let mutable defId = 0
            System.Int32.TryParse(classId, &defId) |> ignore
            b.ToBuildUnitDefId <- defId
            b.BuildPosition <- ValueSome (vec2ToVec3 pos)
            let ai = Highbar.V1.AICommand.empty()
            ai.BuildUnit <- b
            Ok (commandBatch batchSeq builderId command.commandId [ai])
        | CommandPipeline.Gameplay (CommandPipeline.Custom (_, blob)) ->
            let c = Highbar.V1.CustomCommand.empty()
            // CustomCommand.Params is RepeatedField<float32>; decode the
            // blob as length-prefixed float32 if present (bytes / 4).
            // The mapping is informational; the engine plugin chooses how
            // to interpret CustomCommand.CommandId + Params.
            for i in 0 .. (blob.Length / 4) - 1 do
                let f = System.BitConverter.ToSingle(blob, i * 4)
                c.Params.Add(f)
            let ai = Highbar.V1.AICommand.empty()
            ai.Custom <- c
            Ok (commandBatch batchSeq 0u command.commandId [ai])
        | CommandPipeline.Admin CommandPipeline.Pause ->
            let p = Highbar.V1.PauseTeamCommand.empty()
            p.Enable <- true
            let ai = Highbar.V1.AICommand.empty()
            ai.PauseTeam <- p
            Ok (commandBatch batchSeq 0u command.commandId [ai])
        | CommandPipeline.Admin CommandPipeline.Resume ->
            let p = Highbar.V1.PauseTeamCommand.empty()
            p.Enable <- false
            let ai = Highbar.V1.AICommand.empty()
            ai.PauseTeam <- p
            Ok (commandBatch batchSeq 0u command.commandId [ai])
        | CommandPipeline.Admin (CommandPipeline.GrantResources (_, resources)) ->
            // GiveMeCommand is per-resource; emit two AICommands (metal + energy).
            let metalGm = Highbar.V1.GiveMeCommand.empty()
            metalGm.ResourceId <- 0   // 0 = metal by upstream convention
            metalGm.Amount <- float32 resources.metal
            let energyGm = Highbar.V1.GiveMeCommand.empty()
            energyGm.ResourceId <- 1   // 1 = energy
            energyGm.Amount <- float32 resources.energy
            let aiM = Highbar.V1.AICommand.empty()
            aiM.GiveMe <- metalGm
            let aiE = Highbar.V1.AICommand.empty()
            aiE.GiveMe <- energyGm
            Ok (commandBatch batchSeq 0u command.commandId [aiM; aiE])
        | CommandPipeline.Admin (CommandPipeline.SetSpeed _)
        | CommandPipeline.Admin (CommandPipeline.OverrideVision _)
        | CommandPipeline.Admin (CommandPipeline.OverrideVictory _) ->
            Error CommandPipeline.AdminNotAvailable
