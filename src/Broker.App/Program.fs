namespace Broker.App

open System
open System.IO
open System.Threading
open System.Threading.Tasks
open Broker.Core
open Broker.Protocol
open Broker.Tui
open Broker.Viz

module Program =

    let private brokerVersion = System.Version(1, 0)

    let private run (args: Cli.Args) : Task<int> =
        task {
            // Audit sink: rolling-file Serilog under ./logs/.
            let logDir = Path.Combine(Directory.GetCurrentDirectory(), "logs")
            let factory = Logging.configure logDir
            let auditEmit ev = Logging.writeAudit factory ev

            // Boot the gRPC server (FR-005: single port, both services).
            use cts = new CancellationTokenSource()
            let opts =
                { ServerHost.defaultOptions with listenAddress = args.listen }
            let! handle = ServerHost.start opts brokerVersion auditEmit cts.Token

            // FR-014 / SC-007: optional override of the expected
            // coordinator schema version. Lets the operator force a
            // mismatch for the schema-rejection quickstart flow.
            args.expectedSchemaVersion
            |> Option.iter (fun v -> BrokerState.setExpectedSchemaVersion v handle.Hub)

            // Optional 2D viz: probe at startup to populate the footer
            // status line; the actual SkiaViewer window only opens on `V`.
            let liveController : VizControllerImpl.LiveVizController option =
                if args.noViz then None
                else
                    Some (VizControllerImpl.LiveVizController(BrokerState.snapshots handle.Hub))
            let vizController : TickLoop.VizController option =
                liveController
                |> Option.map (fun c -> c :> TickLoop.VizController)

            try
                // Run the TUI tick loop on the main thread.
                let core = BrokerState.asCoreFacade handle.Hub
                Console.CancelKeyPress.Add(fun e ->
                    e.Cancel <- true
                    cts.Cancel())
                do! TickLoop.run core vizController 100 cts.Token
                return 0
            finally
                cts.Cancel()
                liveController |> Option.iter (fun c -> c.Close())
                (handle :> IAsyncDisposable).DisposeAsync().AsTask().Wait()
                (factory :> IDisposable).Dispose()
        }

    [<EntryPoint>]
    let main (argv: string array) : int =
        match Cli.parse argv with
        | Error "help requested" ->
            Console.Out.WriteLine(Cli.usage())
            0
        | Error msg ->
            Console.Error.WriteLine(sprintf "broker: %s" msg)
            Console.Error.WriteLine()
            Console.Error.WriteLine(Cli.usage())
            2
        | Ok args when args.showVersion ->
            Console.Out.WriteLine(sprintf "broker v%O" brokerVersion)
            0
        | Ok args when args.printSchemaVersion ->
            // FR-014 pre-flight: print the broker's expected coordinator
            // schema version so the operator can confirm alignment with
            // the HighBarV3 plugin before launching BAR.
            let v =
                args.expectedSchemaVersion
                |> Option.defaultValue
                    Broker.Protocol.HighBarCoordinatorService.defaultConfig.expectedSchemaVersion
            Console.Out.WriteLine(sprintf "broker schema version: %s" v)
            0
        | Ok args ->
            try
                (run args).GetAwaiter().GetResult()
            with ex ->
                Console.Error.WriteLine(sprintf "broker: fatal: %s" ex.Message)
                1
