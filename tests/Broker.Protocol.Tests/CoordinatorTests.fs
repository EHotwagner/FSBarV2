module Broker.Protocol.Tests.CoordinatorTests

open System
open System.Collections.Concurrent
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

        test "first update sets running seq without raising a gap" {
            let v0 = WireConvert.emptyRunningView
            let upd = mkStateUpdate 5UL 42u
            let v1, result = WireConvert.applyHighBarStateUpdate upd v0
            Expect.equal (WireConvert.lastSeq v1) 5UL "lastSeq advanced"
            match result with
            | WireConvert.Gap _ -> failtest "first update must not raise Gap"
            | _ -> ()
        }

        test "seq jump > 1 returns Gap with both endpoints" {
            let v0 = WireConvert.emptyRunningView
            // First update establishes lastSeq = 1.
            let v1, _ = WireConvert.applyHighBarStateUpdate (mkStateUpdate 1UL 1u) v0
            // Second update jumps from 1 to 5 (3 frames dropped) — gap.
            let _, result = WireConvert.applyHighBarStateUpdate (mkStateUpdate 5UL 5u) v1
            match result with
            | WireConvert.Gap (last, recv) ->
                Expect.equal last 1UL "gap last seq"
                Expect.equal recv 5UL "gap received seq"
            | other ->
                failtestf "expected Gap, got %A" other
        }

        test "consecutive updates do not raise a gap" {
            let v0 = WireConvert.emptyRunningView
            let v1, _ = WireConvert.applyHighBarStateUpdate (mkStateUpdate 1UL 1u) v0
            let _, result = WireConvert.applyHighBarStateUpdate (mkStateUpdate 2UL 2u) v1
            match result with
            | WireConvert.Gap _ -> failtest "consecutive seq must not raise Gap"
            | _ -> ()
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
                        Expect.equal p.Y 20.0f "y"
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
                            ([3u], CommandPipeline.Attack, Some { x = 0.0f; y = 0.0f }, None)))
            match WireConvert.tryFromCoreCommandToHighBar cmd 3UL with
            | Ok batch ->
                let ai = firstAi batch
                match ai.Command with
                | ValueSome (AICommand.Types.Command.AttackArea aa) ->
                    Expect.equal aa.UnitId 3 "unit id"
                    Expect.isGreaterThan aa.Radius 0.0f "non-zero radius"
                | other -> failtestf "expected AttackArea, got %A" other
            | Error r -> failtestf "unexpected reject: %A" r
        }

        test "Stop / Guard maps to corresponding AICommand arms" {
            for kind, _label in [
                CommandPipeline.Stop, "stop"
                CommandPipeline.Guard, "guard" ] do
                let cmd =
                    mkCoreCommand
                        (CommandPipeline.Gameplay
                            (CommandPipeline.UnitOrder ([1u], kind, None, None)))
                match WireConvert.tryFromCoreCommandToHighBar cmd 4UL with
                | Ok batch ->
                    let ai = firstAi batch
                    match ai.Command, kind with
                    | ValueSome (AICommand.Types.Command.Stop _), CommandPipeline.Stop -> ()
                    | ValueSome (AICommand.Types.Command.Guard _), CommandPipeline.Guard -> ()
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
                | other -> failtestf "expected BuildUnit, got %A" other
            | Error r -> failtestf "unexpected reject: %A" r
        }

        test "Custom maps to AICommand.Custom" {
            let cmd =
                mkCoreCommand
                    (CommandPipeline.Gameplay
                        (CommandPipeline.Custom ("ping", [| 0uy; 0uy; 0uy; 0uy |])))
            match WireConvert.tryFromCoreCommandToHighBar cmd 7UL with
            | Ok batch ->
                let ai = firstAi batch
                match ai.Command with
                | ValueSome (AICommand.Types.Command.Custom _) -> ()
                | other -> failtestf "expected Custom, got %A" other
            | Error r -> failtestf "unexpected reject: %A" r
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
