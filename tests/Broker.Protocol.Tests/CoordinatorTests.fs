module Broker.Protocol.Tests.CoordinatorTests

open System
open System.Collections.Concurrent
open System.Threading
open System.Threading.Tasks
open Expecto
open Broker.Core
open Broker.Protocol
open Highbar.V1

let private mkCoreCommand (kind: CommandPipeline.CommandKind) : CommandPipeline.Command =
    { commandId = Guid.NewGuid()
      originatingClient = ScriptingClientId "test"
      targetSlot = None
      kind = kind
      submittedAt = DateTimeOffset.UtcNow }

let private firstAi (batch: CommandBatch) : AICommand =
    if batch.Commands.Count = 0 then
        failtest "expected at least one AICommand"
    batch.Commands.[0]

let private mkSnapshotPayload (frame: uint32) =
    let ss = StateSnapshot.empty()
    ss.FrameNumber <- frame
    ss

let private mkStateUpdate (seqNo: uint64) (frame: uint32) =
    let upd = StateUpdate.empty()
    upd.Seq <- seqNo
    upd.Frame <- frame
    upd.Snapshot <- mkSnapshotPayload frame
    upd

let private mkKeepalive (seqNo: uint64) (frame: uint32) =
    let upd = StateUpdate.empty()
    upd.Seq <- seqNo
    upd.Frame <- frame
    upd.Keepalive <- KeepAlive.empty()
    upd

let private mkDelta (seqNo: uint64) (frame: uint32) (nonempty: bool) =
    let delta = StateDelta.empty()
    if nonempty then
        let event = DeltaEvent.empty()
        event.EconomyTick <- EconomyTickEvent.empty()
        delta.Events.Add(event)
    let upd = StateUpdate.empty()
    upd.Seq <- seqNo
    upd.Frame <- frame
    upd.Delta <- delta
    upd

let private position x y z =
    let p = Vector3.empty()
    p.X <- x
    p.Y <- y
    p.Z <- z
    p

let private mkHubWithAudit () =
    let q = ConcurrentQueue<Audit.AuditEvent>()
    let hub = BrokerState.create (System.Version(1, 0)) 64 (fun e -> q.Enqueue e)
    hub, q

[<Tests>]
let wireConvertTests =
    testList "WireConvert.applyHighBarStateUpdate (T014 / FR-013)" [

        test "snapshot path returns NewSnapshot with frame as tick" {
            let v0 = WireConvert.emptyRunningView
            let upd = mkStateUpdate 1UL 100u
            let _, result = WireConvert.applyHighBarStateUpdate upd v0
            match result with
            | WireConvert.NewSnapshot s ->
                Expect.equal s.tick 100L "frame is threaded into tick"
            | other ->
                failtestf "expected NewSnapshot, got %A" other
        }

        test "keepalive path returns KeepAliveOnly" {
            let v0 = WireConvert.emptyRunningView
            let upd = mkKeepalive 1UL 50u
            let _, result = WireConvert.applyHighBarStateUpdate upd v0
            match result with
            | WireConvert.KeepAliveOnly -> ()
            | other -> failtestf "expected KeepAliveOnly, got %A" other
        }

        test "first complete snapshot establishes the baseline without raising a gap" {
            let v0 = WireConvert.emptyRunningView
            let upd = mkStateUpdate 5UL 42u
            let v1, result = WireConvert.applyHighBarStateUpdate upd v0
            Expect.equal (WireConvert.lastSeq v1) 5UL "lastSeq advanced"
            Expect.isTrue (WireConvert.hasValidBaseline v1) "snapshot establishes baseline"
            match result with
            | WireConvert.Gap _ -> failtest "first update must not raise Gap"
            | _ -> ()
        }

        test "newer full snapshot recovers directly across a sequence jump" {
            let v0 = WireConvert.emptyRunningView
            let v1, _ = WireConvert.applyHighBarStateUpdate (mkStateUpdate 1UL 1u) v0
            let v2, result = WireConvert.applyHighBarStateUpdate (mkStateUpdate 5UL 5u) v1
            Expect.isTrue (WireConvert.hasValidBaseline v2) "replacement snapshot is valid"
            match result with
            | WireConvert.NewSnapshot snapshot -> Expect.equal snapshot.tick 5L "new baseline tick"
            | other -> failtestf "expected NewSnapshot, got %A" other
        }

        test "consecutive updates do not raise a gap" {
            let v0 = WireConvert.emptyRunningView
            let v1, _ = WireConvert.applyHighBarStateUpdate (mkStateUpdate 1UL 1u) v0
            let _, result = WireConvert.applyHighBarStateUpdate (mkStateUpdate 2UL 2u) v1
            match result with
            | WireConvert.Gap _ -> failtest "consecutive seq must not raise Gap"
            | _ -> ()
        }

        test "native X/Y/Z projects to legacy ground X/Z" {
            let own = OwnUnit.empty()
            own.UnitId <- 7u
            own.DefId <- 42u
            own.Position <- ValueSome (position 11.0f 7.0f 23.0f)
            let upd = mkStateUpdate 1UL 1u
            upd.Snapshot.OwnUnits.Add(own)
            let _, result = WireConvert.applyHighBarStateUpdate upd WireConvert.emptyRunningView
            match result with
            | WireConvert.NewSnapshot snapshot ->
                Expect.equal snapshot.units.Head.pos.x 11.0f "ground x comes from native X"
                Expect.equal snapshot.units.Head.pos.y 23.0f "ground y comes from native Z"
            | other -> failtestf "expected NewSnapshot, got %A" other
        }

        test "nonempty delta before a baseline cannot fabricate a snapshot" {
            let view, result =
                WireConvert.applyHighBarStateUpdate
                    (mkDelta 1UL 1u true)
                    WireConvert.emptyRunningView
            Expect.isFalse (WireConvert.hasValidBaseline view) "no baseline was invented"
            match result with
            | WireConvert.Invalidated (_, 1UL, _) -> ()
            | other -> failtestf "expected Invalidated, got %A" other
        }

        test "snapshot gap delta invalidates until a newer snapshot recovers" {
            let v1, _ = WireConvert.applyHighBarStateUpdate (mkStateUpdate 1UL 1u) WireConvert.emptyRunningView
            let v2, gap = WireConvert.applyHighBarStateUpdate (mkDelta 3UL 3u true) v1
            Expect.isFalse (WireConvert.hasValidBaseline v2) "gap invalidates baseline"
            match gap with
            | WireConvert.Gap (1UL, 3UL) -> ()
            | other -> failtestf "expected gap, got %A" other
            let v3, recovery = WireConvert.applyHighBarStateUpdate (mkStateUpdate 4UL 4u) v2
            Expect.isTrue (WireConvert.hasValidBaseline v3) "full snapshot recovers"
            match recovery with
            | WireConvert.NewSnapshot snapshot -> Expect.equal snapshot.tick 4L "recovery tick"
            | other -> failtestf "expected recovered snapshot, got %A" other
        }

        test "unapplied delta invalidates until a newer snapshot recovers" {
            let v1, _ = WireConvert.applyHighBarStateUpdate (mkStateUpdate 1UL 1u) WireConvert.emptyRunningView
            let v2, invalid = WireConvert.applyHighBarStateUpdate (mkDelta 2UL 2u true) v1
            Expect.isFalse (WireConvert.hasValidBaseline v2) "unapplied delta invalidates"
            match invalid with
            | WireConvert.Invalidated (1UL, 2UL, _) -> ()
            | other -> failtestf "expected invalidation, got %A" other
            let v3, recovery = WireConvert.applyHighBarStateUpdate (mkStateUpdate 3UL 3u) v2
            Expect.isTrue (WireConvert.hasValidBaseline v3) "full snapshot recovers"
            match recovery with
            | WireConvert.NewSnapshot _ -> ()
            | other -> failtestf "expected recovered snapshot, got %A" other
        }

        test "empty delta and keepalive advance transport only" {
            let v1, _ = WireConvert.applyHighBarStateUpdate (mkStateUpdate 1UL 1u) WireConvert.emptyRunningView
            let v2, emptyResult = WireConvert.applyHighBarStateUpdate (mkDelta 2UL 2u false) v1
            let v3, keepaliveResult = WireConvert.applyHighBarStateUpdate (mkKeepalive 3UL 2u) v2
            Expect.equal emptyResult WireConvert.KeepAliveOnly "empty delta emits no snapshot"
            Expect.equal keepaliveResult WireConvert.KeepAliveOnly "keepalive emits no snapshot"
            Expect.isTrue (WireConvert.hasValidBaseline v3) "transport-only updates preserve baseline"
            Expect.equal (WireConvert.lastSeq v3) 3UL "transport sequence advances"
        }

        test "older and duplicate updates never regress the accepted sequence" {
            let v1, _ = WireConvert.applyHighBarStateUpdate (mkStateUpdate 5UL 5u) WireConvert.emptyRunningView
            let v2, _ = WireConvert.applyHighBarStateUpdate (mkStateUpdate 5UL 50u) v1
            let v3, _ = WireConvert.applyHighBarStateUpdate (mkStateUpdate 4UL 40u) v2
            Expect.equal (WireConvert.lastSeq v3) 5UL "sequence did not regress"
            Expect.isTrue (WireConvert.hasValidBaseline v3) "valid baseline is retained"
        }

        test "maximum sequence does not overflow gap detection" {
            let v1, _ =
                WireConvert.applyHighBarStateUpdate
                    (mkStateUpdate UInt64.MaxValue UInt32.MaxValue)
                    WireConvert.emptyRunningView
            let v2, result = WireConvert.applyHighBarStateUpdate (mkKeepalive 0UL 0u) v1
            Expect.equal (WireConvert.lastSeq v2) UInt64.MaxValue "wrapped sequence is ignored"
            Expect.equal result WireConvert.KeepAliveOnly "older update is transport-only"
        }

        test "snapshot with a missing entity position is invalid instead of fabricating zeroes" {
            let own = OwnUnit.empty()
            own.UnitId <- 9u
            let upd = mkStateUpdate 1UL 1u
            upd.Snapshot.OwnUnits.Add(own)
            let view, result = WireConvert.applyHighBarStateUpdate upd WireConvert.emptyRunningView
            Expect.isFalse (WireConvert.hasValidBaseline view) "incomplete snapshot rejected"
            match result with
            | WireConvert.Invalidated (_, _, detail) ->
                Expect.stringContains detail "missing position" "reason identifies absent field"
            | other -> failtestf "expected invalidation, got %A" other
        }
    ]

[<Tests>]
let heartbeatTests =
    testList "BrokerState.noteHeartbeat (T015 / FR-008 + FR-011)" [

        test "first heartbeat captures plugin id and refreshes lastHeartbeatAt" {
            let hub, _ = mkHubWithAudit()
            let now = DateTimeOffset.UtcNow
            let r = BrokerState.noteHeartbeat "ai-1" now hub
            Expect.equal r (Ok ()) "first heartbeat accepted"
            Expect.equal (BrokerState.activePluginId hub) (Some "ai-1") "plugin id captured"
            Expect.equal (BrokerState.lastHeartbeatAt hub) now "lastHeartbeatAt set"
        }

        test "subsequent heartbeats with same pluginId are accepted" {
            let hub, _ = mkHubWithAudit()
            let t0 = DateTimeOffset.UtcNow
            let t1 = t0.AddSeconds(1.0)
            BrokerState.noteHeartbeat "ai-1" t0 hub |> ignore
            let r = BrokerState.noteHeartbeat "ai-1" t1 hub
            Expect.equal r (Ok ()) "second heartbeat accepted"
            Expect.equal (BrokerState.lastHeartbeatAt hub) t1 "lastHeartbeatAt advanced"
        }

        test "heartbeat from a different plugin id is rejected as NotOwner (T016 / FR-011)" {
            let hub, audit = mkHubWithAudit()
            let now = DateTimeOffset.UtcNow
            BrokerState.noteHeartbeat "ai-1" now hub |> ignore
            let r = BrokerState.noteHeartbeat "ai-2" (now.AddSeconds(1.0)) hub
            match r with
            | Error (CommandPipeline.NotOwner (attempted, owner)) ->
                Expect.equal attempted "ai-2" "attempted recorded"
                Expect.equal owner "ai-1" "owner unchanged"
            | other ->
                failtestf "expected Error NotOwner, got %A" other
            // Audit must show the rejection (T016 / FR-011 surface).
            let arr = audit.ToArray()
            let nonOwner =
                arr
                |> Array.tryFind (function
                    | Audit.AuditEvent.CoordinatorNonOwnerRejected _ -> true
                    | _ -> false)
            Expect.isSome nonOwner "CoordinatorNonOwnerRejected emitted"
        }

        test "Pinned ownerRule rejects every other plugin id immediately" {
            let hub, _ = mkHubWithAudit()
            BrokerState.setOwnerRule (BrokerState.Pinned "ai-pinned") hub
            let r = BrokerState.noteHeartbeat "ai-other" DateTimeOffset.UtcNow hub
            match r with
            | Error (CommandPipeline.NotOwner (attempted, owner)) ->
                Expect.equal attempted "ai-other" "attempted recorded"
                Expect.equal owner "ai-pinned" "pinned owner echoed"
            | other ->
                failtestf "expected NotOwner, got %A" other
        }
    ]

[<Tests>]
let commandTranslationTests =
    testList "WireConvert.tryFromCoreCommandToHighBar (T026 / T027 / FR-005)" [

        // --- T026 / gameplay arms ---------------------------------------------------

        test "Move with targetPos maps to AICommand.MoveUnit" {
            let cmd =
                mkCoreCommand
                    (CommandPipeline.Gameplay
                        (CommandPipeline.UnitOrder
                            ([5u], CommandPipeline.Move, Some { x = 10.0f; y = 20.0f }, None)))
            match WireConvert.tryFromCoreCommandToHighBar cmd 1UL with
            | Ok batch ->
                Expect.equal batch.BatchSeq 1UL "batch_seq"
                Expect.equal batch.TargetUnitId 5u "target unit id"
                let ai = firstAi batch
                match ai.Command with
                | ValueSome (AICommand.Types.Command.MoveUnit mu) ->
                    Expect.equal mu.UnitId 5 "unit id"
                    match mu.ToPosition with
                    | ValueSome p ->
                        Expect.equal p.X 10.0f "x"
                        Expect.equal p.Y 0.0f "native elevation is deliberately zero"
                        Expect.equal p.Z 20.0f "legacy ground y maps to native Z"
                    | ValueNone -> failtest "expected ToPosition"
                | other -> failtestf "expected MoveUnit, got %A" other
            | Error r -> failtestf "unexpected reject: %A" r
        }

        test "Attack with targetUnitId maps to AICommand.Attack" {
            let cmd =
                mkCoreCommand
                    (CommandPipeline.Gameplay
                        (CommandPipeline.UnitOrder
                            ([7u], CommandPipeline.Attack, None, Some 99u)))
            match WireConvert.tryFromCoreCommandToHighBar cmd 2UL with
            | Ok batch ->
                let ai = firstAi batch
                match ai.Command with
                | ValueSome (AICommand.Types.Command.Attack ac) ->
                    Expect.equal ac.UnitId 7 "attacker"
                    Expect.equal ac.TargetUnitId 99 "target"
                | other -> failtestf "expected Attack, got %A" other
            | Error r -> failtestf "unexpected reject: %A" r
        }

        test "Attack with no targetUnitId but targetPos maps to AttackArea" {
            let cmd =
                mkCoreCommand
                    (CommandPipeline.Gameplay
                        (CommandPipeline.UnitOrder
                            ([3u], CommandPipeline.Attack, Some { x = 11.0f; y = 23.0f }, None)))
            match WireConvert.tryFromCoreCommandToHighBar cmd 3UL with
            | Ok batch ->
                let ai = firstAi batch
                match ai.Command with
                | ValueSome (AICommand.Types.Command.AttackArea aa) ->
                    Expect.equal aa.UnitId 3 "unit id"
                    Expect.isGreaterThan aa.Radius 0.0f "non-zero radius"
                    match aa.AttackPosition with
                    | ValueSome p ->
                        Expect.equal p.X 11.0f "attack ground x"
                        Expect.equal p.Y 0.0f "attack elevation"
                        Expect.equal p.Z 23.0f "attack ground z"
                    | ValueNone -> failtest "expected AttackPosition"
                | other -> failtestf "expected AttackArea, got %A" other
            | Error r -> failtestf "unexpected reject: %A" r
        }

        test "Stop / Guard maps to corresponding AICommand arms" {
            for kind, targetId in [
                CommandPipeline.Stop, None
                CommandPipeline.Guard, Some 9u ] do
                let cmd =
                    mkCoreCommand
                        (CommandPipeline.Gameplay
                            (CommandPipeline.UnitOrder ([1u], kind, None, targetId)))
                match WireConvert.tryFromCoreCommandToHighBar cmd 4UL with
                | Ok batch ->
                    let ai = firstAi batch
                    match ai.Command, kind with
                    | ValueSome (AICommand.Types.Command.Stop _), CommandPipeline.Stop -> ()
                    | ValueSome (AICommand.Types.Command.Guard guard), CommandPipeline.Guard ->
                        Expect.equal guard.GuardUnitId 9 "guard target survives conversion"
                    | got, _ -> failtestf "wrong AICommand arm for %A: %A" kind got
                | Error r -> failtestf "unexpected reject for %A: %A" kind r
        }

        test "Patrol with targetPos maps to AICommand.Patrol" {
            let cmd =
                mkCoreCommand
                    (CommandPipeline.Gameplay
                        (CommandPipeline.UnitOrder
                            ([4u], CommandPipeline.Patrol, Some { x = 50.0f; y = 60.0f }, None)))
            match WireConvert.tryFromCoreCommandToHighBar cmd 5UL with
            | Ok batch ->
                let ai = firstAi batch
                match ai.Command with
                | ValueSome (AICommand.Types.Command.Patrol p) ->
                    Expect.equal p.UnitId 4 "unit id"
                    match p.ToPosition with
                    | ValueSome target ->
                        Expect.equal target.X 50.0f "patrol ground x"
                        Expect.equal target.Y 0.0f "patrol elevation"
                        Expect.equal target.Z 60.0f "patrol ground z"
                    | ValueNone -> failtest "expected ToPosition"
                | other -> failtestf "expected Patrol, got %A" other
            | Error r -> failtestf "unexpected reject: %A" r
        }

        test "Build maps to AICommand.BuildUnit with class id parsed as def id" {
            let cmd =
                mkCoreCommand
                    (CommandPipeline.Gameplay
                        (CommandPipeline.Build
                            (10u, "42", { x = 100.0f; y = 200.0f })))
            match WireConvert.tryFromCoreCommandToHighBar cmd 6UL with
            | Ok batch ->
                Expect.equal batch.TargetUnitId 10u "builder threaded as target"
                let ai = firstAi batch
                match ai.Command with
                | ValueSome (AICommand.Types.Command.BuildUnit b) ->
                    Expect.equal b.UnitId 10 "builder"
                    Expect.equal b.ToBuildUnitDefId 42 "class -> def id"
                    match b.BuildPosition with
                    | ValueSome p ->
                        Expect.equal p.X 100.0f "build ground x"
                        Expect.equal p.Y 0.0f "build elevation"
                        Expect.equal p.Z 200.0f "build ground z"
                    | ValueNone -> failtest "expected BuildPosition"
                | other -> failtestf "expected BuildUnit, got %A" other
            | Error r -> failtestf "unexpected reject: %A" r
        }

        test "Custom rejects because native semantics are not defined" {
            let cmd =
                mkCoreCommand
                    (CommandPipeline.Gameplay
                        (CommandPipeline.Custom ("ping", [| 0uy; 0uy; 0uy; 0uy |])))
            match WireConvert.tryFromCoreCommandToHighBar cmd 7UL with
            | Error (CommandPipeline.InvalidPayload _) -> ()
            | other -> failtestf "expected InvalidPayload, got %A" other
        }

        test "empty order and invalid native identifiers reject before mapping" {
            for ids in [ []; [UInt32.MaxValue] ] do
                let cmd =
                    mkCoreCommand
                        (CommandPipeline.Gameplay
                            (CommandPipeline.UnitOrder (ids, CommandPipeline.Move, Some { x = 1.0f; y = 2.0f }, None)))
                match WireConvert.tryFromCoreCommandToHighBar cmd 8UL with
                | Error (CommandPipeline.InvalidPayload _) -> ()
                | other -> failtestf "expected InvalidPayload for %A, got %A" ids other
        }

        test "multi-unit expansion is ordered, distinct and all-or-nothing" {
            let cmd =
                mkCoreCommand
                    (CommandPipeline.Gameplay
                        (CommandPipeline.UnitOrder
                            ([9u; 3u; 7u], CommandPipeline.Guard, None, Some 42u)))
            match WireConvert.tryExpandCoreCommandToHighBar cmd [101UL, 201UL; 102UL, 202UL; 103UL, 203UL] with
            | Error reason -> failtestf "unexpected reject: %A" reason
            | Ok batches ->
                Expect.sequenceEqual (batches |> List.map _.TargetUnitId) [9u; 3u; 7u] "acting-unit order is preserved"
                Expect.sequenceEqual (batches |> List.map _.BatchSeq) [101UL; 102UL; 103UL] "reserved sequences are preserved"
                Expect.sequenceEqual
                    (batches |> List.map (fun batch -> batch.ClientCommandId |> ValueOption.defaultValue 0UL))
                    [201UL; 202UL; 203UL]
                    "per-child correlations are preserved"
                for batch in batches do
                    match (firstAi batch).Command with
                    | ValueSome (AICommand.Types.Command.Guard guard) ->
                        Expect.equal guard.GuardUnitId 42 "Guard target survives every expansion"
                    | other -> failtestf "expected Guard, got %A" other

            for ids in [ [1u; 1u]; [1u .. 65u] ] do
                let invalid =
                    { cmd with
                        kind = CommandPipeline.Gameplay (CommandPipeline.UnitOrder (ids, CommandPipeline.Move, Some { x = 1.0f; y = 2.0f }, None)) }
                let allocations = ids |> List.mapi (fun i _ -> uint64 (i + 1), uint64 (i + 100))
                match WireConvert.tryExpandCoreCommandToHighBar invalid allocations with
                | Error (CommandPipeline.InvalidPayload _) -> ()
                | other -> failtestf "expected atomic validation refusal for %A, got %A" ids other
        }

        test "nonnumeric build definition and non-finite position reject" {
            let invalidKinds = [
                CommandPipeline.Build (10u, "armmex", { x = 1.0f; y = 2.0f })
                CommandPipeline.Build (10u, "42", { x = Single.NaN; y = 2.0f }) ]
            for kind in invalidKinds do
                let cmd = mkCoreCommand (CommandPipeline.Gameplay kind)
                match WireConvert.tryFromCoreCommandToHighBar cmd 8UL with
                | Error (CommandPipeline.InvalidPayload _) -> ()
                | other -> failtestf "expected InvalidPayload, got %A" other
        }

        test "Move without targetPos rejects with InvalidPayload" {
            let cmd =
                mkCoreCommand
                    (CommandPipeline.Gameplay
                        (CommandPipeline.UnitOrder ([1u], CommandPipeline.Move, None, None)))
            match WireConvert.tryFromCoreCommandToHighBar cmd 8UL with
            | Error (CommandPipeline.InvalidPayload _) -> ()
            | other -> failtestf "expected InvalidPayload, got %A" other
        }

        // --- T027 / admin arms ------------------------------------------------------

        test "Admin Pause maps to AICommand.PauseTeam(enable=true)" {
            let cmd = mkCoreCommand (CommandPipeline.Admin CommandPipeline.Pause)
            match WireConvert.tryFromCoreCommandToHighBar cmd 9UL with
            | Ok batch ->
                let ai = firstAi batch
                match ai.Command with
                | ValueSome (AICommand.Types.Command.PauseTeam p) ->
                    Expect.isTrue p.Enable "Pause -> enable=true"
                | other -> failtestf "expected PauseTeam, got %A" other
            | Error r -> failtestf "unexpected reject: %A" r
        }

        test "Admin Resume maps to AICommand.PauseTeam(enable=false)" {
            let cmd = mkCoreCommand (CommandPipeline.Admin CommandPipeline.Resume)
            match WireConvert.tryFromCoreCommandToHighBar cmd 10UL with
            | Ok batch ->
                let ai = firstAi batch
                match ai.Command with
                | ValueSome (AICommand.Types.Command.PauseTeam p) ->
                    Expect.isFalse p.Enable "Resume -> enable=false"
                | other -> failtestf "expected PauseTeam, got %A" other
            | Error r -> failtestf "unexpected reject: %A" r
        }

        test "Admin GrantResources emits two GiveMe commands (metal + energy)" {
            let cmd =
                mkCoreCommand
                    (CommandPipeline.Admin
                        (CommandPipeline.GrantResources
                            (0, { metal = 100.0; energy = 200.0 })))
            match WireConvert.tryFromCoreCommandToHighBar cmd 11UL with
            | Ok batch ->
                Expect.equal batch.Commands.Count 2 "two AICommands per GrantResources"
                let kinds =
                    batch.Commands
                    |> Seq.map (fun ai ->
                        match ai.Command with
                        | ValueSome (AICommand.Types.Command.GiveMe g) -> g.ResourceId, g.Amount
                        | _ -> -1, 0.0f)
                    |> Seq.toList
                Expect.contains kinds (0, 100.0f) "metal grant"
                Expect.contains kinds (1, 200.0f) "energy grant"
            | Error r -> failtestf "unexpected reject: %A" r
        }

        test "Admin SetSpeed rejects with AdminNotAvailable (research §3)" {
            let cmd = mkCoreCommand (CommandPipeline.Admin (CommandPipeline.SetSpeed 2.0m))
            match WireConvert.tryFromCoreCommandToHighBar cmd 12UL with
            | Error CommandPipeline.AdminNotAvailable -> ()
            | other -> failtestf "expected AdminNotAvailable, got %A" other
        }

        test "Admin OverrideVision rejects with AdminNotAvailable" {
            let cmd =
                mkCoreCommand
                    (CommandPipeline.Admin
                        (CommandPipeline.OverrideVision (0, CommandPipeline.Full)))
            match WireConvert.tryFromCoreCommandToHighBar cmd 13UL with
            | Error CommandPipeline.AdminNotAvailable -> ()
            | other -> failtestf "expected AdminNotAvailable, got %A" other
        }

        test "Admin OverrideVictory rejects with AdminNotAvailable" {
            let cmd =
                mkCoreCommand
                    (CommandPipeline.Admin
                        (CommandPipeline.OverrideVictory (0, CommandPipeline.ForceWin)))
            match WireConvert.tryFromCoreCommandToHighBar cmd 14UL with
            | Error CommandPipeline.AdminNotAvailable -> ()
            | other -> failtestf "expected AdminNotAvailable, got %A" other
        }

        test "client_command_id carries the lower 64 bits of the UUID" {
            let cmd = mkCoreCommand (CommandPipeline.Admin CommandPipeline.Pause)
            match WireConvert.tryFromCoreCommandToHighBar cmd 99UL with
            | Ok batch ->
                let bytes = cmd.commandId.ToByteArray()
                let expected = System.BitConverter.ToUInt64(bytes, 0)
                match batch.ClientCommandId with
                | ValueSome got -> Expect.equal got expected "client_command_id matches lower-64"
                | ValueNone -> failtest "expected ClientCommandId set"
            | Error r -> failtestf "unexpected reject: %A" r
        }
    ]

[<Tests>]
let outboundDeliveryTests =
    testList "bounded atomic coordinator delivery (BARC-01.1e)" [
        test "one bounded admission expands a parent and rejects overflow without partial enqueue" {
            let hub = BrokerState.create (System.Version(1, 0)) 1 ignore
            let link : Session.ProxyAiLink =
                { attachedAt = DateTimeOffset.UtcNow
                  protocolVersion = System.Version(1, 0)
                  lastSnapshotAt = None
                  keepAliveIntervalMs = 5000
                  pluginId = "delivery-test"
                  schemaVersion = "1.0.0"
                  engineSha256 = "test"
                  lastHeartbeatAt = DateTimeOffset.UtcNow
                  lastSeq = 0UL }
            Expect.equal (BrokerState.attachCoordinator link hub) (Ok ()) "coordinator attached"
            let parent =
                mkCoreCommand
                    (CommandPipeline.Gameplay
                        (CommandPipeline.UnitOrder
                            ([8u; 2u; 5u], CommandPipeline.Move, Some { x = 11.0f; y = 13.0f }, None)))
            Expect.equal (BrokerState.sendToCoordinator parent hub) (Ok ()) "whole parent admitted"
            let overflow = { parent with commandId = Guid.NewGuid() }
            Expect.equal (BrokerState.sendToCoordinator overflow hub) (Error CommandPipeline.QueueFull) "full outbound queue rejects the whole second parent"
            let reader =
                match BrokerState.tryClaimCoordinatorCommandChannel hub with
                | BrokerState.Claimed lease -> lease.reader
                | other -> failtestf "expected reader lease, got %A" other
            let mutable delivery = Unchecked.defaultof<BrokerState.OutboundDelivery>
            Expect.isTrue (reader.TryRead(&delivery)) "one parent envelope was queued"
            Expect.equal delivery.parentCommandId parent.commandId "full parent UUID is retained"
            Expect.equal delivery.batches.Length 3 "one batch per acting unit"
            Expect.sequenceEqual (delivery.batches |> List.map _.TargetUnitId) [8u; 2u; 5u] "batch order"
            Expect.equal
                (delivery.batches |> List.map (fun batch -> batch.ClientCommandId |> ValueOption.defaultValue 0UL) |> Set.ofList |> Set.count)
                3
                "child correlations are collision-free"
        }

        test "repeated parent UUID still receives collision-free child identities" {
            let hub = BrokerState.create (System.Version(1, 0)) 4 ignore
            let link : Session.ProxyAiLink =
                { attachedAt = DateTimeOffset.UtcNow; protocolVersion = System.Version(1, 0); lastSnapshotAt = None
                  keepAliveIntervalMs = 5000; pluginId = "collision-test"; schemaVersion = "1.0.0"
                  engineSha256 = "test"; lastHeartbeatAt = DateTimeOffset.UtcNow; lastSeq = 0UL }
            Expect.equal (BrokerState.attachCoordinator link hub) (Ok ()) "coordinator attached"
            let parent =
                mkCoreCommand
                    (CommandPipeline.Gameplay
                        (CommandPipeline.UnitOrder ([3u; 4u], CommandPipeline.Stop, None, None)))
            Expect.equal (BrokerState.sendToCoordinator parent hub) (Ok ()) "first parent admitted"
            Expect.equal (BrokerState.sendToCoordinator parent hub) (Ok ()) "duplicate parent identity admitted with fresh child identities"
            let reader =
                match BrokerState.tryClaimCoordinatorCommandChannel hub with
                | BrokerState.Claimed lease -> lease.reader
                | other -> failtestf "expected reader lease, got %A" other
            let read () =
                let mutable delivery = Unchecked.defaultof<BrokerState.OutboundDelivery>
                Expect.isTrue (reader.TryRead(&delivery)) "delivery available"
                delivery
            let first = read ()
            let second = read ()
            let correlations (delivery: BrokerState.OutboundDelivery) =
                delivery.batches
                |> List.map (fun batch -> batch.ClientCommandId |> ValueOption.defaultValue 0UL)
                |> Set.ofList
            Expect.isEmpty (Set.intersect (correlations first) (correlations second)) "duplicate parents cannot collide"
            Expect.sequenceEqual (first.batches |> List.map _.BatchSeq) [1UL; 2UL] "first range"
            Expect.sequenceEqual (second.batches |> List.map _.BatchSeq) [3UL; 4UL] "second range"
        }

        test "a coordinator session grants exactly one reader lease" {
            let hub = BrokerState.create (System.Version(1, 0)) 4 ignore
            let link : Session.ProxyAiLink =
                { attachedAt = DateTimeOffset.UtcNow; protocolVersion = System.Version(1, 0); lastSnapshotAt = None
                  keepAliveIntervalMs = 5000; pluginId = "lease-test"; schemaVersion = "1.0.0"
                  engineSha256 = "test"; lastHeartbeatAt = DateTimeOffset.UtcNow; lastSeq = 0UL }
            Expect.equal (BrokerState.attachCoordinator link hub) (Ok ()) "coordinator attached"
            let oldReader =
                match BrokerState.tryClaimCoordinatorCommandChannel hub with
                | BrokerState.Claimed lease -> lease.reader
                | other -> failtestf "first reader should claim: %A" other
            Expect.equal (BrokerState.tryClaimCoordinatorCommandChannel hub) BrokerState.AlreadyClaimed "second reader is refused"
            BrokerState.closeSession Session.OperatorTerminated DateTimeOffset.UtcNow hub
            Expect.equal (BrokerState.tryClaimCoordinatorCommandChannel hub) BrokerState.NoCoordinator "closed session cannot be claimed"
            let replacement = { link with attachedAt = DateTimeOffset.UtcNow; pluginId = "lease-test-2" }
            Expect.equal (BrokerState.attachCoordinator replacement hub) (Ok ()) "replacement coordinator attached"
            let newReader =
                match BrokerState.tryClaimCoordinatorCommandChannel hub with
                | BrokerState.Claimed lease -> lease.reader
                | other -> failtestf "replacement reader should claim its own channel: %A" other
            let command = mkCoreCommand (CommandPipeline.Admin CommandPipeline.Pause)
            Expect.equal (BrokerState.sendToCoordinator command hub) (Ok ()) "replacement delivery admitted"
            let mutable stale = Unchecked.defaultof<BrokerState.OutboundDelivery>
            Expect.isFalse (oldReader.TryRead(&stale)) "old lease cannot consume replacement delivery"
            let mutable current = Unchecked.defaultof<BrokerState.OutboundDelivery>
            Expect.isTrue (newReader.TryRead(&current)) "replacement lease receives replacement delivery"
            Expect.equal current.parentCommandId command.commandId "replacement channel identity"
        }

        test "stale reader cleanup cannot close a renewed channel in the same session" {
            let hub = BrokerState.create (System.Version(1, 0)) 4 ignore
            let link : Session.ProxyAiLink =
                { attachedAt = DateTimeOffset.UtcNow; protocolVersion = System.Version(1, 0); lastSnapshotAt = None
                  keepAliveIntervalMs = 5000; pluginId = "renewal-test"; schemaVersion = "1.0.0"
                  engineSha256 = "test"; lastHeartbeatAt = DateTimeOffset.UtcNow; lastSeq = 0UL }
            Expect.equal (BrokerState.attachCoordinator link hub) (Ok ()) "coordinator attached"
            let oldLease =
                match BrokerState.tryClaimCoordinatorCommandChannel hub with
                | BrokerState.Claimed lease -> lease
                | other -> failtestf "old lease missing: %A" other
            BrokerState.completeCoordinatorCommandChannel oldLease.sessionId "normal completion" hub
            Expect.isTrue (BrokerState.ensureCoordinatorCommandChannel hub) "live session renewed its empty channel"
            let newLease =
                match BrokerState.tryClaimCoordinatorCommandChannel hub with
                | BrokerState.Claimed lease -> lease
                | other -> failtestf "new lease missing: %A" other
            BrokerState.closeCoordinatorCommandChannel oldLease.leaseId "late old-reader cleanup" hub
            Expect.isTrue (BrokerState.hasCoordinatorCommandChannel hub) "old lease cannot close renewed channel"
            let command = mkCoreCommand (CommandPipeline.Admin CommandPipeline.Pause)
            Expect.equal (BrokerState.sendToCoordinator command hub) (Ok ()) "renewed channel remains writable"
            let mutable delivery = Unchecked.defaultof<BrokerState.OutboundDelivery>
            Expect.isTrue (newLease.reader.TryRead(&delivery)) "renewed reader receives command"
        }

        test "operator-path outbound refusal is audited" {
            let hub, audit = mkHubWithAudit ()
            let command = mkCoreCommand (CommandPipeline.Admin CommandPipeline.Pause)
            match BrokerState.sendToCoordinator command hub with
            | Error (CommandPipeline.InvalidPayload detail) ->
                Expect.stringContains detail "no active coordinator" "actionable refusal"
            | other -> failtestf "expected no-coordinator refusal, got %A" other
            Expect.isTrue
                (audit.ToArray()
                 |> Array.exists (function
                    | Audit.CommandRejected (_, client, commandId, CommandPipeline.InvalidPayload _)
                        when client = command.originatingClient && commandId = command.commandId -> true
                    | _ -> false))
                "operator refusal is retained in audit"
        }

        testAsync "pre-write cancellation records every child NotAttempted" {
            let hub, audit = mkHubWithAudit ()
            let service = HighBarCoordinatorService.create hub HighBarCoordinatorService.defaultConfig
            let link : Session.ProxyAiLink =
                { attachedAt = DateTimeOffset.UtcNow; protocolVersion = System.Version(1, 0); lastSnapshotAt = None
                  keepAliveIntervalMs = 5000; pluginId = "cancel-test"; schemaVersion = "1.0.0"
                  engineSha256 = "test"; lastHeartbeatAt = DateTimeOffset.UtcNow; lastSeq = 0UL }
            Expect.equal (BrokerState.attachCoordinator link hub) (Ok ()) "coordinator attached"
            let parent =
                mkCoreCommand
                    (CommandPipeline.Gameplay
                        (CommandPipeline.UnitOrder ([10u; 11u], CommandPipeline.Stop, None, None)))
            Expect.equal (BrokerState.sendToCoordinator parent hub) (Ok ()) "parent admitted"
            let cancelledLater = { parent with commandId = Guid.NewGuid() }
            Expect.equal (BrokerState.sendToCoordinator cancelledLater hub) (Ok ()) "later parent admitted"
            let reader =
                match BrokerState.tryClaimCoordinatorCommandChannel hub with
                | BrokerState.Claimed lease -> lease.reader
                | other -> failtestf "expected reader lease, got %A" other
            let mutable delivery = Unchecked.defaultof<BrokerState.OutboundDelivery>
            Expect.isTrue (reader.TryRead(&delivery)) "delivery available"
            use cancelled = new CancellationTokenSource()
            cancelled.Cancel()
            let mutable writes = 0
            let! result =
                HighBarCoordinatorService.writeDelivery service (fun _ -> writes <- writes + 1; Task.CompletedTask) cancelled.Token delivery
                |> Async.AwaitTask
            Expect.equal result (Some "cancelled") "cancelled writer stops"
            Expect.equal writes 0 "no child write was attempted"
            BrokerState.completeCoordinatorCommandChannel delivery.sessionId "cancelled" hub
            let outcomes =
                audit.ToArray()
                |> Array.choose (function
                    | Audit.CoordinatorCommandDelivery (_, _, _, parentId, _, _, _, _, _, outcome, _)
                        when parentId = parent.commandId -> Some outcome
                    | _ -> None)
            Expect.sequenceEqual outcomes [Audit.NotAttempted; Audit.NotAttempted] "every child remains explicit"
            let laterOutcomes =
                audit.ToArray()
                |> Array.choose (function
                    | Audit.CoordinatorCommandDelivery (_, _, _, parentId, _, _, _, _, _, outcome, _)
                        when parentId = cancelledLater.commandId -> Some outcome
                    | _ -> None)
            Expect.sequenceEqual laterOutcomes [Audit.NotAttempted; Audit.NotAttempted] "cancellation drains later accepted parents"
        }

        testAsync "writer failure records WrittenToTransport, Unknown and NotAttempted without retry" {
            let hub, audit = mkHubWithAudit ()
            let service = HighBarCoordinatorService.create hub HighBarCoordinatorService.defaultConfig
            let link : Session.ProxyAiLink =
                { attachedAt = DateTimeOffset.UtcNow
                  protocolVersion = System.Version(1, 0)
                  lastSnapshotAt = None
                  keepAliveIntervalMs = 5000
                  pluginId = "failure-test"
                  schemaVersion = "1.0.0"
                  engineSha256 = "test"
                  lastHeartbeatAt = DateTimeOffset.UtcNow
                  lastSeq = 0UL }
            Expect.equal (BrokerState.attachCoordinator link hub) (Ok ()) "coordinator attached"
            let parent =
                mkCoreCommand
                    (CommandPipeline.Gameplay
                        (CommandPipeline.UnitOrder
                            ([1u; 2u; 3u], CommandPipeline.Stop, None, None)))
            Expect.equal (BrokerState.sendToCoordinator parent hub) (Ok ()) "parent admitted"
            let laterParent =
                { parent with
                    commandId = Guid.NewGuid()
                    kind = CommandPipeline.Gameplay (CommandPipeline.UnitOrder ([7u; 8u], CommandPipeline.Stop, None, None)) }
            Expect.equal (BrokerState.sendToCoordinator laterParent hub) (Ok ()) "later parent admitted"
            let reader =
                match BrokerState.tryClaimCoordinatorCommandChannel hub with
                | BrokerState.Claimed lease -> lease.reader
                | other -> failtestf "expected reader lease, got %A" other
            let mutable delivery = Unchecked.defaultof<BrokerState.OutboundDelivery>
            Expect.isTrue (reader.TryRead(&delivery)) "delivery available"
            let mutable writes = 0
            let writer (_: CommandBatch) =
                writes <- writes + 1
                if writes = 2 then Task.FromException(Exception("injected writer failure"))
                else Task.CompletedTask
            let! result = HighBarCoordinatorService.writeDelivery service writer CancellationToken.None delivery |> Async.AwaitTask
            Expect.isSome result "writer failure terminates this delivery"
            Expect.equal writes 2 "failed child is not replayed and later child is not attempted"
            BrokerState.completeCoordinatorCommandChannel delivery.sessionId "injected writer failure" hub
            let outcomes =
                audit.ToArray()
                |> Array.choose (function
                    | Audit.CoordinatorCommandDelivery (_, _, client, parentId, index, childCount, actingUnit, _, _, outcome, _)
                        when parentId = parent.commandId -> Some (client, index, childCount, actingUnit, outcome)
                    | _ -> None)
                |> Array.toList
            Expect.equal outcomes
                [ parent.originatingClient, 0, 3, 1u, Audit.WrittenToTransport
                  parent.originatingClient, 1, 3, 2u, Audit.Unknown
                  parent.originatingClient, 2, 3, 3u, Audit.NotAttempted ]
                "transport outcomes retain client, child cardinality and acting-unit mapping"
            let laterOutcomes =
                audit.ToArray()
                |> Array.choose (function
                    | Audit.CoordinatorCommandDelivery (_, _, _, parentId, _, _, _, _, _, outcome, _)
                        when parentId = laterParent.commandId -> Some outcome
                    | _ -> None)
            Expect.sequenceEqual laterOutcomes [Audit.NotAttempted; Audit.NotAttempted] "writer failure drains later accepted parents"
        }

        testAsync "session replacement marks every unwritten child NotAttempted" {
            let hub, audit = mkHubWithAudit ()
            let service = HighBarCoordinatorService.create hub HighBarCoordinatorService.defaultConfig
            let link : Session.ProxyAiLink =
                { attachedAt = DateTimeOffset.UtcNow; protocolVersion = System.Version(1, 0); lastSnapshotAt = None
                  keepAliveIntervalMs = 5000; pluginId = "replacement-test"; schemaVersion = "1.0.0"
                  engineSha256 = "test"; lastHeartbeatAt = DateTimeOffset.UtcNow; lastSeq = 0UL }
            Expect.equal (BrokerState.attachCoordinator link hub) (Ok ()) "coordinator attached"
            let parent =
                mkCoreCommand
                    (CommandPipeline.Gameplay
                        (CommandPipeline.UnitOrder ([4u; 6u], CommandPipeline.Stop, None, None)))
            Expect.equal (BrokerState.sendToCoordinator parent hub) (Ok ()) "parent admitted"
            let queued = { parent with commandId = Guid.NewGuid() }
            Expect.equal (BrokerState.sendToCoordinator queued hub) (Ok ()) "second parent admitted"
            let reader =
                match BrokerState.tryClaimCoordinatorCommandChannel hub with
                | BrokerState.Claimed lease -> lease.reader
                | other -> failtestf "expected reader lease, got %A" other
            let mutable delivery = Unchecked.defaultof<BrokerState.OutboundDelivery>
            Expect.isTrue (reader.TryRead(&delivery)) "delivery available"
            BrokerState.closeSession Session.OperatorTerminated DateTimeOffset.UtcNow hub
            let mutable writes = 0
            let! result =
                HighBarCoordinatorService.writeDelivery service (fun _ -> writes <- writes + 1; Task.CompletedTask) CancellationToken.None delivery
                |> Async.AwaitTask
            Expect.equal result (Some "session-replaced") "stale session is refused"
            Expect.equal writes 0 "no stale child is written"
            let outcomes =
                audit.ToArray()
                |> Array.choose (function
                    | Audit.CoordinatorCommandDelivery (_, _, _, parentId, _, _, _, _, _, outcome, _)
                        when parentId = parent.commandId -> Some outcome
                    | _ -> None)
            Expect.sequenceEqual outcomes [Audit.NotAttempted; Audit.NotAttempted] "all stale children are explicit"
            let queuedOutcomes =
                audit.ToArray()
                |> Array.choose (function
                    | Audit.CoordinatorCommandDelivery (_, _, _, parentId, _, _, _, _, _, outcome, _)
                        when parentId = queued.commandId -> Some outcome
                    | _ -> None)
            Expect.sequenceEqual queuedOutcomes [Audit.NotAttempted; Audit.NotAttempted] "closure drains later accepted parents"
        }
    ]
