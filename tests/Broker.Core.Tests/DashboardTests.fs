module Broker.Core.Tests.DashboardTests

open System
open Expecto
open Broker.Core
open Broker.Core.Dashboard

let private brokerInfo : Session.BrokerInfo =
    { version = System.Version(1, 0)
      listenAddress = "127.0.0.1:5021"
      startedAt = DateTimeOffset(2026, 4, 28, 12, 0, 0, TimeSpan.Zero) }

let private addClient name (roster: ScriptingRoster.Roster) =
    let id = ScriptingClientId name
    match ScriptingRoster.tryAdd id (System.Version(1, 0)) (DateTimeOffset(2026, 4, 28, 12, 0, 0, TimeSpan.Zero)) roster with
    | Ok r -> r
    | Error _ -> failwithf "tryAdd %s unexpectedly failed" name

let private grantAdmin name (roster: ScriptingRoster.Roster) =
    match ScriptingRoster.grantAdmin (ScriptingClientId name) roster with
    | Ok r -> r
    | Error _ -> failwithf "grantAdmin %s unexpectedly failed" name

let private mkSnapshot (sid: Guid) (tick: int64) (capturedAt: DateTimeOffset) : Snapshot.GameStateSnapshot =
    { sessionId = sid
      tick = tick
      capturedAt = capturedAt
      players = []
      units = []
      buildings = []
      features = []
      mapMeta = None }

let private threshold = TimeSpan.FromSeconds 2.0

let private now = DateTimeOffset(2026, 4, 28, 13, 0, 0, TimeSpan.Zero)

[<Tests>]
let dashboardTests =
    testList "Dashboard.build" [

        // FR-018: server state, broker info, connected-clients are pass-through.
        test "idle_session_None_yields_Idle_mode_and_no_session_fields" {
            let reading =
                build
                    brokerInfo
                    (Listening "127.0.0.1:5021")
                    ScriptingRoster.empty
                    None
                    now
                    threshold
            Expect.equal reading.broker brokerInfo "broker info passes through"
            Expect.equal reading.serverState (Listening "127.0.0.1:5021") "serverState passes through"
            Expect.equal reading.mode Mode.Mode.Idle "no session ⇒ Idle mode"
            Expect.equal reading.session None "no session ⇒ session = None"
            Expect.equal reading.elapsed None "no session ⇒ elapsed = None"
            Expect.equal reading.pause None "no session ⇒ pause = None"
            Expect.equal reading.speed None "no session ⇒ speed = None"
            Expect.equal reading.telemetry None "no session ⇒ telemetry = None"
            Expect.isFalse reading.telemetryStale "no session ⇒ never stale"
        }

        test "server_Down_state_passes_through" {
            let reading =
                build brokerInfo (Down "bind failed: EADDRINUSE") ScriptingRoster.empty None now threshold
            Expect.equal reading.serverState (Down "bind failed: EADDRINUSE") "Down reason preserved"
        }

        // FR-018: connected scripting clients (count + identity + admin flag).
        test "roster_projected_to_connectedClients_with_admin_flag" {
            let roster =
                ScriptingRoster.empty
                |> addClient "alice-bot"
                |> addClient "bob-bot"
                |> grantAdmin "alice-bot"
            let reading = build brokerInfo (Listening "x") roster None now threshold
            Expect.equal reading.connectedClients.Length 2 "two clients enumerated"
            let alice =
                reading.connectedClients
                |> List.find (fun c -> c.id = ScriptingClientId "alice-bot")
            let bob =
                reading.connectedClients
                |> List.find (fun c -> c.id = ScriptingClientId "bob-bot")
            Expect.isTrue alice.isAdmin "alice was elevated"
            Expect.isFalse bob.isAdmin "bob remains non-admin (default per FR-016)"
        }

        // FR-019: mode + session + elapsed + pause + speed projected from session.
        test "guest_active_session_projects_mode_state_and_defaults" {
            let startedAt = now.AddSeconds(-30.0)
            let session = Session.newGuestSession startedAt
            let reading = build brokerInfo (Listening "x") ScriptingRoster.empty (Some session) now threshold
            Expect.equal reading.mode Mode.Mode.Guest "guest session ⇒ Guest mode"
            Expect.equal reading.session (Some Session.Active) "newGuestSession is Active"
            Expect.equal reading.elapsed (Some (TimeSpan.FromSeconds 30.0)) "elapsed = now - startedAt"
            Expect.equal reading.pause (Some Session.Running) "default pause = Running"
            Expect.equal reading.speed (Some 1.0m) "default speed = 1.0"
        }

        test "hosting_mode_propagated_via_Mode_dot_Hosting_LobbyConfig" {
            let cfg : Lobby.LobbyConfig =
                { mapName = "TestMap"
                  gameMode = "skirmish"
                  participants = []
                  display = Lobby.Headless }
            let session = Session.newHostSession cfg (now.AddSeconds(-5.0))
            let reading = build brokerInfo (Listening "x") ScriptingRoster.empty (Some session) now threshold
            Expect.equal reading.mode (Mode.Mode.Hosting cfg) "hosting mode preserves config"
            Expect.equal reading.session (Some Session.Configuring) "host session begins in Configuring"
        }

        test "ended_session_projected_with_EndReason" {
            let session =
                Session.newGuestSession (now.AddSeconds(-10.0))
                |> Session.end_ Session.OperatorTerminated now
            let reading = build brokerInfo (Listening "x") ScriptingRoster.empty (Some session) now threshold
            Expect.equal reading.session (Some (Session.Ended Session.OperatorTerminated)) "ended state preserved"
        }

        test "togglePause_and_stepSpeed_reflected_in_reading" {
            let session =
                Session.newGuestSession (now.AddSeconds(-10.0))
                |> Session.togglePause
                |> Session.stepSpeed 1.0m
            let reading = build brokerInfo (Listening "x") ScriptingRoster.empty (Some session) now threshold
            Expect.equal reading.pause (Some Session.Paused) "togglePause flipped Running→Paused"
            Expect.equal reading.speed (Some 2.0m) "stepSpeed +1.0 from default 1.0 = 2.0"
        }

        // FR-020: per-player telemetry surfaces through the latest snapshot.
        test "applied_snapshot_passes_through_as_telemetry" {
            let startedAt = now.AddSeconds(-10.0)
            let proxy : Session.ProxyAiLink =
                { attachedAt = startedAt
                  protocolVersion = System.Version(1, 0)
                  lastSnapshotAt = None
                  keepAliveIntervalMs = 2000
                  pluginId = ""
                  schemaVersion = ""
                  engineSha256 = ""
                  lastHeartbeatAt = now
                  lastSeq = 0UL }
            let session =
                Session.newGuestSession startedAt
                |> Session.attachProxy proxy
                |> function Ok s -> s | Error e -> failwith e
            let snap = mkSnapshot (Session.id session) 1L (now.AddSeconds(-0.5))
            let session = Session.applySnapshot snap session
            let reading = build brokerInfo (Listening "x") ScriptingRoster.empty (Some session) now threshold
            Expect.equal reading.telemetry (Some snap) "latest snapshot is the dashboard telemetry"
        }

        // FR-021 / Invariant 8: telemetryStale = (now - lastSnapshotAt) > threshold.
        test "no_proxy_attached_means_not_stale" {
            // No proxy ⇒ no claim of staleness yet (we don't claim stale until we expect data).
            let session = Session.newGuestSession (now.AddSeconds(-30.0))
            let reading = build brokerInfo (Listening "x") ScriptingRoster.empty (Some session) now threshold
            Expect.isFalse reading.telemetryStale "no proxy ⇒ telemetryStale = false"
        }

        test "proxy_attached_no_snapshot_within_threshold_is_not_stale" {
            // Proxy attached half a second ago; threshold is 2s ⇒ not stale.
            let proxy : Session.ProxyAiLink =
                { attachedAt = now.AddSeconds(-0.5)
                  protocolVersion = System.Version(1, 0)
                  lastSnapshotAt = None
                  keepAliveIntervalMs = 2000
                  pluginId = ""
                  schemaVersion = ""
                  engineSha256 = ""
                  lastHeartbeatAt = now
                  lastSeq = 0UL }
            let session =
                Session.newGuestSession (now.AddSeconds(-1.0))
                |> Session.attachProxy proxy
                |> function Ok s -> s | Error e -> failwith e
            let reading = build brokerInfo (Listening "x") ScriptingRoster.empty (Some session) now threshold
            Expect.isFalse reading.telemetryStale "attached <threshold ago, no snapshot ⇒ not stale yet"
        }

        test "proxy_attached_no_snapshot_beyond_threshold_is_stale" {
            let proxy : Session.ProxyAiLink =
                { attachedAt = now.AddSeconds(-5.0)
                  protocolVersion = System.Version(1, 0)
                  lastSnapshotAt = None
                  keepAliveIntervalMs = 2000
                  pluginId = ""
                  schemaVersion = ""
                  engineSha256 = ""
                  lastHeartbeatAt = now
                  lastSeq = 0UL }
            let session =
                Session.newGuestSession (now.AddSeconds(-10.0))
                |> Session.attachProxy proxy
                |> function Ok s -> s | Error e -> failwith e
            let reading = build brokerInfo (Listening "x") ScriptingRoster.empty (Some session) now threshold
            Expect.isTrue reading.telemetryStale "attached >threshold ago, no snapshot ⇒ stale (FR-021)"
        }

        test "snapshot_within_threshold_is_not_stale" {
            let attachedAt = now.AddSeconds(-10.0)
            let proxy : Session.ProxyAiLink =
                { attachedAt = attachedAt
                  protocolVersion = System.Version(1, 0)
                  lastSnapshotAt = None
                  keepAliveIntervalMs = 2000
                  pluginId = ""
                  schemaVersion = ""
                  engineSha256 = ""
                  lastHeartbeatAt = now
                  lastSeq = 0UL }
            let session =
                Session.newGuestSession attachedAt
                |> Session.attachProxy proxy
                |> function Ok s -> s | Error e -> failwith e
            let snap = mkSnapshot (Session.id session) 1L (now.AddSeconds(-1.0))
            let session = Session.applySnapshot snap session
            let reading = build brokerInfo (Listening "x") ScriptingRoster.empty (Some session) now threshold
            Expect.isFalse reading.telemetryStale "last snapshot 1s ago, threshold 2s ⇒ not stale"
        }

        test "snapshot_beyond_threshold_is_stale" {
            let attachedAt = now.AddSeconds(-30.0)
            let proxy : Session.ProxyAiLink =
                { attachedAt = attachedAt
                  protocolVersion = System.Version(1, 0)
                  lastSnapshotAt = None
                  keepAliveIntervalMs = 2000
                  pluginId = ""
                  schemaVersion = ""
                  engineSha256 = ""
                  lastHeartbeatAt = now
                  lastSeq = 0UL }
            let session =
                Session.newGuestSession attachedAt
                |> Session.attachProxy proxy
                |> function Ok s -> s | Error e -> failwith e
            let snap = mkSnapshot (Session.id session) 1L (now.AddSeconds(-5.0))
            let session = Session.applySnapshot snap session
            let reading = build brokerInfo (Listening "x") ScriptingRoster.empty (Some session) now threshold
            Expect.isTrue reading.telemetryStale "last snapshot 5s ago, threshold 2s ⇒ stale"
        }

        test "stale_flag_transitions_at_the_threshold_boundary" {
            // Invariant 8 boundary: condition is `(now - lastSnapshotAt) > threshold`.
            // Exactly at threshold ⇒ NOT stale; one tick beyond ⇒ stale.
            let attachedAt = now.AddSeconds(-60.0)
            let proxy : Session.ProxyAiLink =
                { attachedAt = attachedAt
                  protocolVersion = System.Version(1, 0)
                  lastSnapshotAt = None
                  keepAliveIntervalMs = 2000
                  pluginId = ""
                  schemaVersion = ""
                  engineSha256 = ""
                  lastHeartbeatAt = now
                  lastSeq = 0UL }
            let baseSession =
                Session.newGuestSession attachedAt
                |> Session.attachProxy proxy
                |> function Ok s -> s | Error e -> failwith e
            let atThreshold = now - threshold
            let snapAt = mkSnapshot (Session.id baseSession) 1L atThreshold
            let sessionAt = Session.applySnapshot snapAt baseSession
            let readingAt = build brokerInfo (Listening "x") ScriptingRoster.empty (Some sessionAt) now threshold
            Expect.isFalse readingAt.telemetryStale "exactly at threshold ⇒ not stale (strict >)"

            let beyondThreshold = atThreshold.AddTicks(-1L)
            let snapBeyond = mkSnapshot (Session.id baseSession) 1L beyondThreshold
            let sessionBeyond = Session.applySnapshot snapBeyond baseSession
            let readingBeyond = build brokerInfo (Listening "x") ScriptingRoster.empty (Some sessionBeyond) now threshold
            Expect.isTrue readingBeyond.telemetryStale "one tick past threshold ⇒ stale"
        }
    ]
