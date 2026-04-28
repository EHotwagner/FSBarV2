module Broker.Mvu.Tests.UpdateTests

open System
open Expecto
open Broker.Core
open Broker.Mvu

let startedAt = DateTimeOffset(2026, 4, 28, 11, 0, 0, TimeSpan.Zero)
let brokerInfo : Session.BrokerInfo = {
    version = System.Version(1, 0)
    listenAddress = "127.0.0.1:5021"
    startedAt = startedAt
}
let freshModel () =
    Model.init brokerInfo Model.defaultConfig startedAt

let mkRpcCtx op i : Msg.RpcContext = {
    rpcId = Cmd.RpcId i
    receivedAt = startedAt.AddSeconds 5.0
    operation = op
}

[<Tests>]
let updateTests =
    testList "Broker.Mvu.Update" [

        // ── FR-001/FR-002/FR-003: Model is the single state container.
        test "FR-001 Synthetic_init_idle_model_is_idle" {
            let m = freshModel ()
            Expect.equal m.mode Mode.Idle "fresh model is in Idle"
            Expect.isNone m.coordinator "no coordinator on init"
            Expect.isNone m.session "no session on init"
        }

        // ── FR-002/FR-007: Coordinator attach via Heartbeat — Synthetic
        test "FR-002 Synthetic_first_heartbeat_attaches_coordinator" {
            let m = freshModel ()
            let msg = Msg.CoordinatorInbound (Msg.Heartbeat ("ai-7", "1.0.0", "engine", mkRpcCtx "Heartbeat" 1L))
            let m', cmds = Update.update msg m
            Expect.equal m'.mode Mode.Guest "first heartbeat → Guest mode"
            Expect.isSome m'.coordinator "coordinator attached"
            // Audit + CompleteRpc
            let hasAuditAttached =
                cmds |> List.exists (function
                    | Cmd.AuditCmd (Audit.CoordinatorAttached _) -> true
                    | _ -> false)
            Expect.isTrue hasAuditAttached "first heartbeat emits CoordinatorAttached audit"
            let hasComplete =
                cmds |> List.exists (function
                    | Cmd.CompleteRpc (Cmd.RpcId 1L, Cmd.Ok) -> true
                    | _ -> false)
            Expect.isTrue hasComplete "Cmd.CompleteRpc closes the heartbeat RPC"
        }

        test "FR-002 Synthetic_subsequent_heartbeat_keeps_mode" {
            let m = freshModel ()
            let m', _ = Update.update (Msg.CoordinatorInbound (Msg.Heartbeat ("ai", "1.0.0", "e", mkRpcCtx "h" 1L))) m
            let m'', cmds = Update.update (Msg.CoordinatorInbound (Msg.Heartbeat ("ai", "1.0.0", "e", mkRpcCtx "h" 2L))) m'
            Expect.equal m''.mode Mode.Guest "stays Guest"
            let hasHbAudit =
                cmds |> List.exists (function
                    | Cmd.AuditCmd (Audit.CoordinatorHeartbeat _) -> true
                    | _ -> false)
            Expect.isTrue hasHbAudit "subsequent heartbeats emit CoordinatorHeartbeat audit"
        }

        // ── FR-005/FR-007: PushStateSnapshot fans out to subscribers.
        test "FR-005 Synthetic_snapshot_fanout_to_subscribed_clients" {
            let m = Broker.Mvu.Testing.Fixtures.syntheticGuestModel 3 100L
            let snap : Snapshot.GameStateSnapshot =
                { sessionId = Guid.NewGuid()
                  tick = 101L
                  capturedAt = startedAt.AddSeconds 60.0
                  players = []
                  units = []
                  buildings = []
                  features = []
                  mapMeta = None }
            let _, cmds = Update.update (Msg.CoordinatorInbound (Msg.PushStateSnapshot (101UL, snap))) m
            let fanoutCount =
                cmds
                |> List.sumBy (function
                    | Cmd.ScriptingOutbound (_, Cmd.Snapshot _) -> 1
                    | _ -> 0)
            Expect.equal fanoutCount 3 "one ScriptingOutbound per subscribed client"
        }

        // ── FR-004 / Constitution III: every Msg arm hits update without exception.
        test "FR-004 Synthetic_exhaustive_match_no_throw" {
            let m = freshModel ()
            let messages : Msg.Msg list = [
                Msg.TuiInput (Msg.QuitRequested)
                Msg.TuiInput (Msg.Resize (80, 24))
                Msg.AdapterCallback (Msg.MailboxHighWater (1024, 1500, startedAt))
                Msg.AdapterCallback (Msg.VizWindowClosed startedAt)
                Msg.CmdFailure (Msg.AuditWriteFailed ("flush", exn "x"))
                Msg.CmdFailure (Msg.VizOpFailed ("open", exn "x"))
                Msg.CmdFailure (Msg.TimerFailed (Cmd.TimerId 1L, "tick", exn "x"))
                Msg.Tick (Msg.DashboardTick startedAt)
                Msg.Tick (Msg.HeartbeatProbe startedAt)
                Msg.Lifecycle (Msg.RuntimeStarted startedAt)
            ]
            for msg in messages do
                Update.update msg m |> ignore
            // If we got here without exception, the exhaustive match holds.
            Expect.isTrue true "all top-level Msg arms dispatch"
        }

        // ── FR-008: Cmd-failure routing is per-effect-family.
        test "FR-008 Synthetic_coordinator_send_failed_tears_down_session" {
            let m = Broker.Mvu.Testing.Fixtures.syntheticGuestModel 2 50L
            let m', cmds = Update.update (Msg.CmdFailure (Msg.CoordinatorSendFailed ("send", exn "wire"))) m
            Expect.isNone m'.coordinator "coordinator detached on send-failed"
            Expect.equal m'.mode Mode.Idle "mode → Idle on send-failed"
            let hasDetach =
                cmds |> List.exists (function
                    | Cmd.AuditCmd (Audit.CoordinatorDetached _) -> true
                    | _ -> false)
            Expect.isTrue hasDetach "audit CoordinatorDetached emitted"
        }

        test "FR-008 Synthetic_scripting_send_failed_drops_client" {
            let id = ScriptingClientId "client-1"
            let m =
                Broker.Mvu.Testing.Fixtures.syntheticGuestModel 1 50L
                // Replace the synthetic client with a known id.
                |> fun base' ->
                    let mutable r = ScriptingRoster.empty
                    match ScriptingRoster.tryAdd id (System.Version(1, 0)) startedAt r with
                    | Result.Ok r' -> r <- r'
                    | _ -> ()
                    { base' with roster = r; queues = Map.add id { depth = 0; highWaterMark = 0; overflowCount = 0; lastSampledAt = startedAt; lastOverflowAt = None } Map.empty }
            let m', _ = Update.update (Msg.CmdFailure (Msg.ScriptingSendFailed (id, "wire", exn "broken"))) m
            Expect.isFalse (Map.containsKey id m'.queues) "queue observation removed"
            Expect.isNone (ScriptingRoster.tryFind id m'.roster) "client removed from roster"
        }

        // ── Mailbox high-water cooldown.
        test "FR-008 Synthetic_mailbox_highwater_cooldown_suppresses_audit" {
            let m = freshModel ()
            // First crossing — captures lastMailboxAuditAt.
            let m', _ = Update.update (Msg.AdapterCallback (Msg.MailboxHighWater (1500, 1500, startedAt))) m
            Expect.isSome m'.lastMailboxAuditAt "first crossing arms cooldown"
            // Second crossing inside cooldown — lastMailboxAuditAt unchanged.
            let m'', _ = Update.update (Msg.AdapterCallback (Msg.MailboxHighWater (1600, 1600, startedAt.AddMilliseconds 500.0))) m'
            Expect.equal m''.lastMailboxAuditAt m'.lastMailboxAuditAt "within cooldown — last-audit timestamp unchanged"
        }
    ]
