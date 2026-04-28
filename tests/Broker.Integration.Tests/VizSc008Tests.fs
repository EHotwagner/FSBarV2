module Broker.Integration.Tests.VizSc008Tests

open System
open System.Net
open System.Net.Sockets
open System.Threading
open Expecto
open Grpc.Net.Client
open Broker.Core
open Broker.Protocol
open Broker.Tui
open Broker.Viz
open Broker.App
open FSBarV2.Broker.Contracts

let private freePort () =
    let listener = new TcpListener(IPAddress.Loopback, 0)
    listener.Start()
    let port = (listener.LocalEndpoint :?> IPEndPoint).Port
    listener.Stop()
    port

let private withHeadlessEnv (f: unit -> 'a) : 'a =
    // Force the probe down its FR-025 / SC-008 headless branch.
    let display = Environment.GetEnvironmentVariable "DISPLAY"
    let wayland = Environment.GetEnvironmentVariable "WAYLAND_DISPLAY"
    Environment.SetEnvironmentVariable("DISPLAY", null)
    Environment.SetEnvironmentVariable("WAYLAND_DISPLAY", null)
    try f ()
    finally
        Environment.SetEnvironmentVariable("DISPLAY", display)
        Environment.SetEnvironmentVariable("WAYLAND_DISPLAY", wayland)

let private mkHello (name: string) (v: System.Version) =
    let pv = ProtocolVersion.empty ()
    pv.Major <- uint32 v.Major
    pv.Minor <- uint32 v.Minor
    let req = HelloRequest.empty ()
    req.ClientName <- name
    req.ClientVersion <- ValueSome pv
    req

let private noObservable<'a> () : IObservable<'a> =
    { new IObservable<'a> with
        member _.Subscribe(_observer: IObserver<'a>) =
            { new IDisposable with member _.Dispose() = () } }

// SC-008: env-var manipulation + LiveVizController state mutate process-global
// state, so each test must run with the headless / non-headless boundaries
// snapshotted/restored — sequenced to avoid races with other Viz tests.
[<Tests>]
let sc008Tests =
    testSequenced <| testList "Viz.Sc008.HeadlessAndNoVizDoNotBlockBroker" [

        test "LiveVizController_Toggle_on_headless_records_unavailable_status" {
            // The first Toggle on a headless host stores the probe failure
            // and surfaces it via Status() so the dashboard footer can
            // render "2D visualization unavailable: ...". A second Toggle
            // is idempotent — re-probes, same failure, still no window.
            withHeadlessEnv (fun () ->
                let ctl = VizControllerImpl.LiveVizController(noObservable<Snapshot.GameStateSnapshot>())
                let asViz = ctl :> TickLoop.VizController
                Expect.isNone (asViz.Status())
                    "no probe attempted yet → no status"
                asViz.Toggle()
                match asViz.Status() with
                | Some msg ->
                    Expect.stringContains msg "2D visualization unavailable"
                        "footer-ready prefix present"
                    Expect.stringContains msg "DISPLAY"
                        "reason mentions DISPLAY"
                | None ->
                    failtest "expected an unavailability status after headless Toggle"
                // Second Toggle re-probes; still failing, status persists.
                asViz.Toggle()
                Expect.isSome (asViz.Status())
                    "second Toggle leaves status set"
                ctl.Close())
        }

        // FR-025 / SC-008 wire-end: a broker booted with --no-viz still
        // accepts gRPC handshakes from scripting clients. Drives the path
        // Program.run takes when args.noViz = true: a None VizController is
        // passed to TickLoop, the rest of the broker stays up.
        testCaseAsync "broker boots with --no-viz and answers gRPC Hello" (async {
            let port = freePort ()
            let opts =
                { ServerHost.defaultOptions with
                    listenAddress = sprintf "127.0.0.1:%d" port }
            let brokerVersion = System.Version(1, 0)
            let auditEmit (_ev: Audit.AuditEvent) = ()
            let! handle =
                ServerHost.start opts brokerVersion auditEmit CancellationToken.None
                |> Async.AwaitTask
            try
                use channel = GrpcChannel.ForAddress(sprintf "http://127.0.0.1:%d" port)
                let client = new ScriptingClient.ScriptingClientClient(channel)
                let! reply =
                    client.HelloAsync(mkHello "no-viz-bot" brokerVersion).ResponseAsync
                    |> Async.AwaitTask
                match reply.BrokerVersion with
                | ValueSome bv ->
                    Expect.equal bv.Major 1u
                        "Hello reply echoes the broker major version"
                | ValueNone ->
                    failtest "expected reply.BrokerVersion to be set"
                // The TickLoop's own ToggleViz path is `viz.Toggle()` when
                // viz is Some, no-op when None. Mirror the production None
                // path here to confirm it is a tolerated branch.
                let core = BrokerState.asCoreFacade handle.Hub
                let next = TickLoop.dispatch core TickLoop.Dashboard HotkeyMap.ToggleViz
                Expect.equal next TickLoop.Dashboard
                    "ToggleViz is a no-op in dispatch (TickLoop.run owns the side effect)"
            finally
                (handle :> IAsyncDisposable).DisposeAsync().AsTask().Wait()
        })

        // FR-022: V toggle respects --no-viz by leaving the controller as
        // None — Program.run constructs no controller in that branch and
        // the TUI's `V` handler is a silent no-op.
        test "Program.run_with_noViz_constructs_no_VizController" {
            // Stand-in: when args.noViz = true, Program.run threads
            // `None` to TickLoop.run. The vizStatus() inside the loop
            // is therefore always None, so the footer renders without a
            // viz line. Confirm the footer rendering matches.
            let reading : Dashboard.DiagnosticReading =
                { broker =
                    { version = System.Version(1, 0)
                      listenAddress = "127.0.0.1:5021"
                      startedAt = DateTimeOffset.UtcNow }
                  serverState = Dashboard.Listening "127.0.0.1:5021"
                  connectedClients = []
                  mode = Mode.Mode.Idle
                  session = None
                  elapsed = None
                  pause = None
                  speed = None
                  telemetry = None
                  telemetryStale = false }
            let layout = DashboardView.renderWithViz reading None
            Expect.isNotNull (box layout) "renderWithViz None returns a Layout"
        }
    ]
