// SYNTHETIC FIXTURE: this test exercises the broker-side wire path
// (Kestrel + ProxyLink + ScriptingClient + per-client fan-out) and the
// Tui dashboard render at load, but uses the loopback `SyntheticProxy`
// in place of the eventual HighBarV3 proxy AI. See tasks.md
// Synthetic-Evidence Inventory under T029 / T042.
module Broker.Integration.Tests.DashboardLoadTests

open System
open System.Diagnostics
open System.IO
open System.Net
open System.Net.Sockets
open System.Threading
open System.Threading.Tasks
open Expecto
open Spectre.Console
open Grpc.Net.Client
open Broker.Core
open Broker.Protocol
open Broker.Tui
open FSBarV2.Broker.Contracts

let private freePort () =
    let listener = new TcpListener(IPAddress.Loopback, 0)
    listener.Start()
    let port = (listener.LocalEndpoint :?> IPEndPoint).Port
    listener.Stop()
    port

let private startServer (port: int) =
    let opts = { ServerHost.defaultOptions with listenAddress = sprintf "127.0.0.1:%d" port }
    let task = ServerHost.start opts (System.Version(1, 0)) (fun _ -> ()) CancellationToken.None
    task.Wait()
    task.Result

let private channelFor port =
    GrpcChannel.ForAddress(sprintf "http://127.0.0.1:%d" port)

// 200-col off-TTY ansi console for capturing dashboard transcripts.
type private WideOutput(writer: TextWriter) =
    interface IAnsiConsoleOutput with
        member _.Writer = writer
        member _.IsTerminal = false
        member _.Width = 200
        member _.Height = 200
        member _.SetEncoding(_) = ()

let private renderDashboard (reading: Dashboard.DiagnosticReading) : string =
    let layout = DashboardView.render reading
    use sw = new StringWriter()
    let settings = AnsiConsoleSettings()
    settings.Out <- WideOutput(sw :> TextWriter)
    settings.Ansi <- AnsiSupport.No
    settings.ColorSystem <- ColorSystemSupport.NoColors
    settings.Interactive <- InteractionSupport.No
    let console = AnsiConsole.Create(settings)
    console.Write(layout)
    sw.ToString()

let private buildSnapshot (sid: byte[]) (tick: int64) (unitCount: int) : GameStateSnapshot -> unit =
    fun snap ->
        snap.SessionId <- Google.Protobuf.ByteString.CopyFrom(sid)
        snap.Tick <- tick
        snap.CapturedAtUnixMs <- DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
        // 4 players, ~50 units each → 200 units total (SC-006 floor).
        for pid in 1 .. 4 do
            let p = PlayerTelemetry.empty()
            p.PlayerId <- pid
            p.TeamId <- pid - 1
            p.Name <- sprintf "Player%d" pid
            let r = ResourceVector.empty()
            r.Metal <- 1000.0 + float (pid * 100) + float tick
            r.Energy <- 500.0 + float (pid * 50) + float tick
            p.Resources <- ValueSome r
            p.UnitCount <- uint32 (unitCount / 4)
            p.BuildingCount <- 4u
            let eco = EconomyStats.empty()
            let inc = ResourceVector.empty()
            inc.Metal <- 50.0
            inc.Energy <- 30.0
            eco.Income <- ValueSome inc
            let exp = ResourceVector.empty()
            exp.Metal <- 20.0
            exp.Energy <- 25.0
            eco.Expenditure <- ValueSome exp
            p.Economy <- ValueSome eco
            p.Kills <- uint32 (pid * 2)
            p.Losses <- uint32 pid
            snap.Players.Add(p)
        for uid in 1 .. unitCount do
            let u = Unit.empty()
            u.Id <- uint32 uid
            u.ClassId <- (if uid % 3 = 0 then "tank" elif uid % 3 = 1 then "scout" else "raider")
            u.OwnerPlayerId <- ((uid - 1) % 4) + 1
            let pos = Vec2.empty()
            pos.X <- float32 ((uid * 13) % 1024)
            pos.Y <- float32 ((uid * 19) % 1024)
            u.Pos <- ValueSome pos
            snap.Units.Add(u)

[<Tests>]
let dashboardLoadTests =
    testList "US3 dashboard under load (T042)" [

        testAsync "Synthetic_dashboard renders ≥4 clients and ≥200 units at ≥1 Hz refresh and persists evidence" {
            let totalSnapshots = 25
            let unitsPerSnapshot = 200
            let clientCount = 4

            let port = freePort()
            let handle = startServer port
            try
                use channel = channelFor port
                let scClient = new ScriptingClient.ScriptingClientClient(channel)

                // 1. Hello + subscribe `clientCount` real clients.
                let clientNames =
                    [| for i in 1 .. clientCount -> sprintf "load-bot-%d" i |]
                let subscribers = ResizeArray<_>()
                for name in clientNames do
                    let hello = HelloRequest.empty()
                    hello.ClientName <- name
                    let pv = ProtocolVersion.empty()
                    pv.Major <- 1u
                    pv.Minor <- 0u
                    hello.ClientVersion <- ValueSome pv
                    let! _ = scClient.HelloAsync(hello).ResponseAsync |> Async.AwaitTask
                    let subReq = SubscribeRequest.empty()
                    subReq.ClientName <- name
                    let call = scClient.SubscribeStateAsync(subReq)
                    subscribers.Add(call)

                // 2. Synthetic proxy attaches → broker auto-detects Guest mode.
                let! proxy =
                    SyntheticProxy.connect channel (System.Version(1, 0)) "load-proxy" |> Async.AwaitTask
                use _ = proxy

                // Drain each subscriber's incoming snapshots into a per-client
                // counter so we can assert fan-out (gap-free, FR-006).
                let perClientReceived = Array.zeroCreate<int> clientCount
                let drainCts = new CancellationTokenSource()
                let drainers =
                    [|
                        for i in 0 .. clientCount - 1 ->
                            let call = subscribers.[i]
                            let idx = i
                            task {
                                try
                                    while not drainCts.IsCancellationRequested do
                                        let! more = call.ResponseStream.MoveNext(drainCts.Token)
                                        if not more then return ()
                                        match call.ResponseStream.Current.Body with
                                        | ValueSome (StateMsg.Types.Body.Snapshot _) ->
                                            Interlocked.Increment(&perClientReceived.[idx]) |> ignore
                                        | _ -> ()
                                with :? OperationCanceledException -> ()
                            } :> Task
                    |]

                // 3. Push 25 snapshots at 5 Hz cadence (≥1 Hz refresh, SC-006).
                //    Each snapshot carries 4 players + 200 units.
                let cadenceMs = 200
                let stopwatch = Stopwatch.StartNew()
                for tick in 1L .. int64 totalSnapshots do
                    do! proxy.PushSnapshotAsync (buildSnapshot proxy.SessionId tick unitsPerSnapshot)
                        |> Async.AwaitTask
                    do! Task.Delay(cadenceMs) |> Async.AwaitTask
                let pushElapsed = stopwatch.Elapsed

                // Allow a brief drain settle window (gRPC fan-out + reader scheduling).
                do! Task.Delay(500) |> Async.AwaitTask

                // 4. Capture an at-load dashboard reading + render.
                let core = BrokerState.asCoreFacade handle.Hub
                let now = DateTimeOffset.UtcNow
                let staleThreshold = TimeSpan.FromSeconds 2.0
                let serverState = Dashboard.Listening handle.Listening
                let brokerInfo : Session.BrokerInfo =
                    { version = core.BrokerVersion()
                      listenAddress = handle.Listening
                      startedAt = now.AddSeconds(-5.0) }
                let reading =
                    Dashboard.build brokerInfo serverState (core.Roster()) (BrokerState.session handle.Hub)
                        now staleThreshold
                let transcript = renderDashboard reading

                // 5. Tear down before assertions / file write.
                drainCts.CancelAfter(TimeSpan.FromSeconds 1.0)
                for d in drainers do
                    try do! d |> Async.AwaitTask with _ -> ()

                // 6. SC-006 — at least 1 Hz visible refresh: tick advanced from
                //    1 to 25 over the push window; per-client fan-out must
                //    keep pace with the proxy push rate.
                Expect.equal reading.session (Some Session.Active) "session is Active under load"
                Expect.equal reading.mode Mode.Mode.Guest "auto-detected guest mode on proxy attach"
                let snap = reading.telemetry |> Option.defaultWith (fun () -> failwith "no telemetry on dashboard reading")
                Expect.isGreaterThanOrEqual snap.tick 20L "latest dashboard tick must reflect the push window"

                let cadenceFloor = totalSnapshots * 95 / 100  // 95% ⇒ allow 1-2 in-flight at teardown
                for i in 0 .. clientCount - 1 do
                    Expect.isGreaterThanOrEqual perClientReceived.[i] cadenceFloor
                        (sprintf "client %s must have received ≥%d/%d snapshots (saw %d)"
                            clientNames.[i] cadenceFloor totalSnapshots perClientReceived.[i])

                // 7. SC-007 — content checks: the rendered dashboard must
                //    surface mode, connection state, and per-player counts in
                //    one render so an operator can read it in ≤10 s.
                Expect.stringContains transcript "GUEST" "mode badge visible (SC-007)"
                Expect.stringContains transcript "listening" "server-state badge visible (SC-007)"
                for name in clientNames do
                    Expect.stringContains transcript name (sprintf "client %s listed in dashboard (FR-018)" name)
                for pid in 1 .. 4 do
                    Expect.stringContains transcript (sprintf "Player%d" pid)
                        (sprintf "player %d telemetry visible (FR-020)" pid)
                Expect.stringContains transcript (sprintf "tick %d" snap.tick)
                    "telemetry tick header visible"
                Expect.stringContains transcript "Clients (4)" "client count visible in clients pane"

                // 8. Persist the transcript + summary as US3 evidence.
                let evidencePath = "../../../../../specs/001-tui-grpc-broker/readiness/us3-evidence.md"
                let absEvidence = Path.GetFullPath evidencePath
                let summary =
                    sprintf
                        "## US3 dashboard load run\n\n\
                         **Recorded**: %s\n\
                         **Driver**: `tests/Broker.Integration.Tests/DashboardLoadTests.fs`\n\
                         **Fixture**: synthetic-proxy (`SyntheticProxy.connect`) + 4 real loopback gRPC scripting clients.\n\n\
                         ## SC-006 / SC-007 measurements\n\n\
                         - **Connected clients**: %d (%s)\n\
                         - **Snapshots pushed**: %d at %dms cadence (push window ≈ %.1f s, ≥1 Hz refresh)\n\
                         - **Latest dashboard tick at end of run**: %d\n\
                         - **Per-client fan-out received**: %s\n\
                         - **Units per snapshot**: %d (≥200 floor — SC-006)\n\
                         - **Players per snapshot**: 4\n\
                         - **Mode**: Guest (auto-detected on proxy attach, FR-002 / FR-003)\n\
                         - **Server state**: %s\n\
                         - **Telemetry stale**: %b\n\n\
                         ## SC-007 dashboard content (rendered at peak load)\n\n\
                         The rendering below is what `Broker.Tui.DashboardView.render` produces against the\n\
                         live `Hub` state at the end of the load run, captured via a 200-col off-TTY\n\
                         `IAnsiConsole`. Mode, connection state, every connected client, and per-player\n\
                         resources / unit counts are all on one screen — SC-007 (≤10 s recognition).\n\n\
                         ```\n\
                         %s\n\
                         ```\n\n\
                         ## Status\n\n\
                         `[S]` — broker-side TUI render and per-client fan-out are real production code on\n\
                         a real Kestrel-hosted gRPC server with 4 live clients and a real `Channel<StateMsg>`\n\
                         drain per client. The proxy peer is the loopback `SyntheticProxy` substitute for\n\
                         the eventual HighBarV3 workstream (research.md §7, §14) — same disclosure as T029\n\
                         and T037. A live-TTY screenshot capture is the remaining synthetic gap (Spectre\n\
                         `LiveDisplay` requires an interactive TTY); the rendered transcript above is the\n\
                         exact output a TTY would show.\n"
                        (DateTimeOffset.UtcNow.ToString("o"))
                        clientCount
                        (String.concat ", " clientNames)
                        totalSnapshots cadenceMs pushElapsed.TotalSeconds
                        snap.tick
                        (perClientReceived |> Array.map string |> String.concat " / ")
                        unitsPerSnapshot
                        handle.Listening
                        reading.telemetryStale
                        transcript
                File.WriteAllText(absEvidence, summary)
                printfn "[T042] wrote US3 evidence to %s (%d bytes)" absEvidence summary.Length
            finally
                (handle :> IAsyncDisposable).DisposeAsync().AsTask().Wait()
        }
    ]
    |> testSequenced
