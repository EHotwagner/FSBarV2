module Broker.Core.Tests.CommandPipelineTests

open System
open Expecto
open Broker.Core
open Broker.Core.CommandPipeline

let private now () = DateTimeOffset.UtcNow
let private vec = { Snapshot.x = 0.0f; Snapshot.y = 0.0f }
let private alice = ScriptingClientId "alice"
let private bob   = ScriptingClientId "bob"

let private mkGameplay clientId targetSlot : Command =
    { commandId = Guid.NewGuid()
      originatingClient = clientId
      targetSlot = targetSlot
      kind = Gameplay (UnitOrder ([1u], Move, Some vec, None))
      submittedAt = now() }

let private mkAdmin clientId : Command =
    { commandId = Guid.NewGuid()
      originatingClient = clientId
      targetSlot = None
      kind = Admin Pause
      submittedAt = now() }

let private mkAdminWith clientId payload : Command =
    { commandId = Guid.NewGuid()
      originatingClient = clientId
      targetSlot = None
      kind = Admin payload
      submittedAt = now() }

let private adminPayloads : AdminPayload list =
    [ SetSpeed 2.0m
      Pause
      Resume
      GrantResources (1, { metal = 1000.0; energy = 1000.0 })
      OverrideVision (1, Full)
      OverrideVictory (1, ForceWin) ]

let private slot idx kind boundClient : ParticipantSlot.ParticipantSlot =
    { slotIndex = idx
      kind = kind
      team = 0
      boundClient = boundClient }

let private rosterWith (id: ScriptingClientId) (admin: bool) =
    let r =
        ScriptingRoster.empty
        |> ScriptingRoster.tryAdd id (System.Version(1, 0)) (now())
        |> function Ok r -> r | Error e -> failtestf "%A" e
    if admin then
        ScriptingRoster.grantAdmin id r |> function Ok r -> r | Error e -> failtestf "%A" e
    else r

[<Tests>]
let pipelineTests =
    testList "CommandPipeline" [
        test "tryEnqueue_at_capacity_returns_QueueFull" {
            // FR-010: bounded backpressure with reject-on-overflow.
            // Capacity 2 → first two enqueues accept, third rejects.
            let q = createQueue 2
            let r1 = tryEnqueue q (mkGameplay alice (Some 0))
            let r2 = tryEnqueue q (mkGameplay alice (Some 0))
            let r3 = tryEnqueue q (mkGameplay alice (Some 0))
            Expect.equal r1 Accepted "first enqueue accepted"
            Expect.equal r2 Accepted "second enqueue accepted"
            Expect.equal r3 (Rejected QueueFull) "third over-capacity → QueueFull"
        }

        test "drain_returns_FIFO_order_and_resets_depth" {
            // FR-010: never silently dropped, evicted, OR reordered.
            let q = createQueue 4
            let cmds =
                [ for i in 0..2 -> mkGameplay alice (Some 0) ]
            for c in cmds do
                let r = tryEnqueue q c
                Expect.equal r Accepted "enqueue must accept under capacity"
            let drained = drain 10 q
            Expect.equal (List.length drained) 3 "drain returns everything"
            Expect.equal
                (drained |> List.map (fun c -> c.commandId))
                (cmds    |> List.map (fun c -> c.commandId))
                "FIFO order preserved"
            Expect.equal (depth q) 0 "queue empty after drain"
        }

        test "authorise_admin_in_guest_mode_is_AdminNotAvailable" {
            // FR-004 / Invariant 2: every Admin command in non-Hosting
            // mode is rejected, regardless of isAdmin.
            let r = rosterWith alice true
            let cmd = mkAdmin alice
            let result = authorise Mode.Mode.Guest r [] cmd
            Expect.equal result (Error AdminNotAvailable) "admin rejected in guest"
        }

        test "authorise_admin_in_hosting_for_non-admin_is_AdminNotAvailable" {
            // FR-016: scripting clients connect non-admin; an admin command
            // from a non-elevated client is rejected even in host mode.
            let r = rosterWith alice false
            let cmd = mkAdmin alice
            let cfg : Lobby.LobbyConfig =
                { mapName = "MapA"; gameMode = "FFA"
                  participants = []; display = Lobby.Headless }
            let result = authorise (Mode.Mode.Hosting cfg) r [] cmd
            Expect.equal result (Error AdminNotAvailable) "non-admin client cannot run admin"
        }

        test "authorise_admin_in_hosting_for_elevated_client_is_Ok" {
            let r = rosterWith alice true
            let cmd = mkAdmin alice
            let cfg : Lobby.LobbyConfig =
                { mapName = "MapA"; gameMode = "FFA"
                  participants = []; display = Lobby.Headless }
            let result = authorise (Mode.Mode.Hosting cfg) r [] cmd
            Expect.equal result (Ok ()) "elevated admin in host mode is allowed"
        }

        test "authorise_gameplay_to_unowned_slot_is_SlotNotOwned" {
            // FR-009: only the bound client may issue gameplay commands.
            let slots = [ slot 0 ParticipantSlot.ProxyAi (Some bob) ]
            let r = rosterWith alice false
            let cmd = mkGameplay alice (Some 0)
            match authorise Mode.Mode.Guest r slots cmd with
            | Error (SlotNotOwned (0, Some owner)) when owner = bob -> ()
            | other -> failtestf "expected SlotNotOwned; got %A" other
        }

        test "authorise_gameplay_to_owned_slot_is_Ok" {
            let slots = [ slot 0 ParticipantSlot.ProxyAi (Some alice) ]
            let r = rosterWith alice false
            let cmd = mkGameplay alice (Some 0)
            Expect.equal (authorise Mode.Mode.Guest r slots cmd) (Ok ()) "owner may command"
        }

        test "authorise_gameplay_without_targetSlot_is_InvalidPayload" {
            let r = rosterWith alice false
            let cmd = mkGameplay alice None
            match authorise Mode.Mode.Guest r [] cmd with
            | Error (InvalidPayload _) -> ()
            | Error (SlotNotOwned _) -> ()  // implementation may treat as SlotNotOwned None
            | other -> failtestf "expected InvalidPayload or SlotNotOwned; got %A" other
        }

        test "authorise_admin_in_idle_mode_for_admin_client_is_AdminNotAvailable" {
            // FR-004 / Invariant 2: admin commands are rejected in any
            // non-Hosting mode, regardless of the client's isAdmin flag.
            // Idle is the third mode (alongside Hosting/Guest) and must
            // share Guest's behaviour.
            let r = rosterWith alice true
            let cmd = mkAdmin alice
            Expect.equal
                (authorise Mode.Mode.Idle r [] cmd)
                (Error AdminNotAvailable)
                "admin in Idle is never available"
        }

        test "authorise_admin_in_idle_mode_for_non-admin_client_is_AdminNotAvailable" {
            let r = rosterWith alice false
            let cmd = mkAdmin alice
            Expect.equal
                (authorise Mode.Mode.Idle r [] cmd)
                (Error AdminNotAvailable)
                "admin in Idle is never available"
        }

        test "authorise_every_AdminPayload_in_Hosting+admin_is_Ok" {
            // Invariant 2 sweep: every variant of AdminPayload behaves
            // identically — accepted iff Hosting + isAdmin.
            let r = rosterWith alice true
            let cfg : Lobby.LobbyConfig =
                { mapName = "MapA"; gameMode = "FFA"
                  participants = []; display = Lobby.Headless }
            for payload in adminPayloads do
                let cmd = mkAdminWith alice payload
                Expect.equal
                    (authorise (Mode.Mode.Hosting cfg) r [] cmd)
                    (Ok ())
                    (sprintf "admin payload %A must be accepted in Hosting+admin" payload)
        }

        test "authorise_every_AdminPayload_in_Guest_is_AdminNotAvailable" {
            // FR-004: every admin variant is rejected in Guest mode, even
            // when the client carries an isAdmin=true flag (which it
            // shouldn't, but Invariant 3 says we don't trust the flag).
            let r = rosterWith alice true
            for payload in adminPayloads do
                let cmd = mkAdminWith alice payload
                Expect.equal
                    (authorise Mode.Mode.Guest r [] cmd)
                    (Error AdminNotAvailable)
                    (sprintf "admin payload %A must be rejected in Guest" payload)
        }

        test "authorise_every_AdminPayload_in_Idle_is_AdminNotAvailable" {
            let r = rosterWith alice true
            for payload in adminPayloads do
                let cmd = mkAdminWith alice payload
                Expect.equal
                    (authorise Mode.Mode.Idle r [] cmd)
                    (Error AdminNotAvailable)
                    (sprintf "admin payload %A must be rejected in Idle" payload)
        }
    ]
