# Quickstart: TUI gRPC Game Broker

**Feature**: 001-tui-grpc-broker
**Audience**: Operators, scripting-client authors, contributors who want to
exercise the broker without reading the source.

This walkthrough covers the two cold-start scenarios the spec ranks P1
(guest mode) and P2 (host mode), plus the optional 2D visualization.

> Prerequisite: .NET 10 SDK installed. The broker targets `net10.0`.

---

## 1. Build and run from source

```sh
# From the repo root.
dotnet build

# Run the broker on its default port (127.0.0.1:5021).
dotnet run --project src/Broker.App
```

You should see the Spectre.Console dashboard open with:

```
┌ Broker ─ FSBarV2 v0.1.0 ──────────────────────────┐
│ Server: listening 127.0.0.1:5021                  │
│ Mode:   Idle                                      │
│ Clients: 0                                        │
└───────────────────────────────────────────────────┘

[L] Open lobby (host mode)   [V] Toggle 2D viz   [Q] Quit
```

If the broker reports `Server: down`, check no other process is bound to
port 5021 (override with `--listen 127.0.0.1:5025`).

---

## 2. Scenario A — Guest mode (Spec User Story 1, P1)

The broker is already running (Step 1).

**You will need**: a HighBarV3-compatible game session you launch through
your normal lobby client (e.g., Chobby) with a proxy-AI slot, and a
scripting client of your choice.

1. **Launch the game from your lobby client** with one proxy-AI slot.
2. **The proxy AI dials the broker** at `127.0.0.1:5021`. The broker's
   dashboard should flip to:
   ```
   Mode:   Guest (attached)
   Session: <uuid>   Elapsed: 00:00:04   Speed: 1.0×   Running
   ```
3. **Connect a scripting client.** Using the F# client from the broker's
   contracts:
   ```fsharp
   #r "nuget: Grpc.Net.Client"
   open Grpc.Net.Client
   open FSBarV2.Broker.Contracts

   let channel = GrpcChannel.ForAddress("http://127.0.0.1:5021")
   let client = ScriptingClient.ScriptingClient.ScriptingClientClient(channel)

   let hello =
       HelloRequest(
           ClientName = "alice-bot",
           ClientVersion = ProtocolVersion(Major = 1u, Minor = 0u))
   let reply = client.Hello(hello)
   printfn "Connected. is_admin=%b" reply.IsAdmin   // false in guest mode
   ```
4. **Subscribe to game state** (server-streaming):
   ```fsharp
   use call = client.SubscribeState(SubscribeRequest(ClientName = "alice-bot"))
   while call.ResponseStream.MoveNext().Result do
       match call.ResponseStream.Current.BodyCase with
       | StateMsg.BodyOneofCase.Snapshot ->
           let snap = call.ResponseStream.Current.Snapshot
           printfn "tick=%d  units=%d" snap.Tick snap.Units.Count
       | StateMsg.BodyOneofCase.SessionEnd ->
           printfn "session ended"
       | _ -> ()
   ```
5. **Bind a slot, then submit gameplay commands.** Single-writer rule
   (FR-009) — the broker rejects with `SLOT_NOT_OWNED` if another client
   already holds the slot.
   ```fsharp
   let bind = client.BindSlot(BindSlotRequest(ClientName = "alice-bot", SlotIndex = 0))
   if bind.Ok then
       use cmds = client.SubmitCommands()
       let move = Command(... GameplayPayload with UnitOrder ...)
       cmds.RequestStream.WriteAsync(move).Wait()
   ```
6. **Try an admin command.** It should be rejected:
   ```fsharp
   let admin = Command(... AdminPayload.Pause ...)
   cmds.RequestStream.WriteAsync(admin).Wait()
   // Ack on cmds.ResponseStream:  accepted = false, reject.code = ADMIN_NOT_AVAILABLE
   ```

---

## 3. Scenario B — Host mode (Spec User Story 2, P2)

The broker is in `Idle`. Press **`L`** to open the lobby.

1. **Lobby UI** (Spectre.Console panel):
   ```
   ┌ Lobby ──────────────────────────────────────────┐
   │ Map:        [Tabula]                            │
   │ Mode:       [Skirmish]                          │
   │ Display:    [Headless]                          │
   │                                                 │
   │ Slots:                                          │
   │   0  Human       Team 0                         │
   │   1  ProxyAi     Team 1                         │
   │   2  BuiltInAi 5 Team 1                         │
   │                                                 │
   │ [Enter] Launch     [Esc] Cancel                 │
   └─────────────────────────────────────────────────┘
   ```
2. **Validation.** The broker validates per FR-013 before launching. If a
   scripting client `alice-bot` is already connected and there is no
   `ProxyAi` slot, the lobby shows:
   ```
   Cannot launch: scripting client "alice-bot" expects a ProxyAi slot.
   ```
3. **Press Enter to launch.** Dashboard becomes:
   ```
   Mode:   Hosting (admin)
   Session: <uuid>   Elapsed: 00:00:01   Speed: 1.0×   Running
   ```
4. **Operator-issued admin commands** (from the TUI):
   - **`+`** — speed up by 0.5×
   - **`-`** — slow down by 0.5×
   - **Space** — toggle pause/resume
   Each is logged as an `AuditEvent.AdminGranted`-style record in
   `logs/broker-YYYYMMDD.log` (Serilog rolling-file sink).
5. **Elevate a scripting client to admin.** With the dashboard showing
   `alice-bot` in the Clients pane, press **`A`** then select `alice-bot`
   from the prompt → the client may now issue `Admin _` commands. The
   grant is in-memory only and does not survive broker restart (FR-016).
6. **End session.** Press **`X`** to terminate. The broker:
   - sends `SessionEnd { reason = OPERATOR_TERMINATED }` on every
     subscribed `SubscribeState` stream (FR-014),
   - tears down the game process,
   - returns the dashboard to `Mode: Idle`.

---

## 4. Scenario C — Optional 2D visualization (Spec User Story 4, P3)

With any active session (host or guest), press **`V`** to open the 2D
top-down view.

- A separate window opens via `SkiaViewer.run`, showing the map outline,
  units, and buildings with ownership colors.
- The window updates within one second of each game tick.
- The window is read-only — input does not flow back to the game.
- Press **`V`** again to close it.

**Headless hosts**: on a server with no graphical display (e.g.,
SSH'd Linux box), pressing **`V`** shows in the dashboard footer:

```
2D visualization unavailable: no graphical display
```

The broker, gRPC server, dashboard, and scripting clients are all
unaffected (SC-008). Pass `--no-viz` at startup to skip the probe entirely.

---

## 5. Backpressure observation

In a host or guest session, point a load-test client at `SubmitCommands`
and saturate the per-client queue (default capacity 64). The expected
behavior (FR-010):

- Once 64 commands are queued, the broker pauses reads on the client's
  HTTP/2 stream (visible as a stall on `WriteAsync`).
- If the client side keeps writing past the flow-control window, those
  commands arrive at the broker and are rejected with
  `Reject { code = QUEUE_FULL, command_id = <the id you sent> }`.
- The dashboard shows the client's queue depth in the Clients pane.
- Commands are NEVER silently dropped, evicted, or reordered — assert this
  with the test fixture in `tests/Broker.Integration.Tests/`.

---

## 6. Audit log

Every connection lifecycle event, mode change, admin grant/revoke, and
command rejection is appended to `logs/broker-YYYYMMDD.log` as a Serilog
JSON object. Sample line:

```json
{"@t":"2026-04-27T19:32:14.121Z","@mt":"AdminGranted to {ClientId} by {Actor}",
 "ClientId":"alice-bot","Actor":"operator","SourceContext":"Broker.Core.Audit"}
```

This file is the post-session diagnosis surface required by FR-028.

---

## 7. FSI-driven exploration

For interactive design / debugging, load the broker's pure core into
F# Interactive without spinning up Kestrel:

```sh
dotnet fsi scripts/prelude.fsx
```

`prelude.fsx` references the packed `Broker.Core` and exposes its public
surface — the same surface the tests exercise. This is the canonical
entry point for the Spec → FSI → Tests → Implementation workflow
mandated by Constitution Principle I.
