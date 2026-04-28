namespace Broker.Protocol

open System
open System.Net
open System.Threading
open System.Threading.Tasks
open Microsoft.AspNetCore.Builder
open Microsoft.AspNetCore.Hosting
open Microsoft.AspNetCore.Server.Kestrel.Core
open Microsoft.Extensions.DependencyInjection
open Microsoft.Extensions.Hosting
open Microsoft.Extensions.Logging
open Broker.Core

module ServerHost =

    type Options =
        { listenAddress: string
          keepaliveIntervalMs: int
          commandQueueCapacity: int }

    let defaultOptions : Options =
        { listenAddress = "127.0.0.1:5021"
          keepaliveIntervalMs = 2000
          commandQueueCapacity = 64 }

    type ServerHandle =
        inherit IAsyncDisposable
        abstract Listening : string
        abstract IsRunning : bool
        abstract Hub : BrokerState.Hub

    let private parseEndpoint (s: string) : IPEndPoint =
        // Accepts "host:port"; resolves "localhost" and "127.0.0.1" only
        // (we do not want to do DNS at startup for the loopback default).
        let i = s.LastIndexOf(':')
        if i <= 0 || i = s.Length - 1 then
            failwithf "listenAddress must be HOST:PORT, got %s" s
        let host = s.Substring(0, i)
        let port = Int32.Parse(s.Substring(i + 1))
        let ip =
            match host with
            | "localhost" | "127.0.0.1" -> IPAddress.Loopback
            | "0.0.0.0" -> IPAddress.Any
            | other ->
                let mutable parsed : IPAddress | null = null
                if IPAddress.TryParse(other, &parsed) then
                    match parsed with
                    | null -> failwithf "could not parse listen host %s" other
                    | p    -> p
                else failwithf "could not parse listen host %s" other
        IPEndPoint(ip, port)

    type private RunningHandle(app: WebApplication, hub: BrokerState.Hub, listening: string) =
        let mutable disposed = 0
        interface ServerHandle with
            member _.Listening = listening
            member _.IsRunning = System.Threading.Volatile.Read(&disposed) = 0
            member _.Hub = hub
            member _.DisposeAsync() =
                if System.Threading.Interlocked.Exchange(&disposed, 1) = 0 then
                    ValueTask((app :> IAsyncDisposable).DisposeAsync().AsTask())
                else
                    ValueTask()

    let start
        (options: Options)
        (brokerVersion: Version)
        (auditEmitter: Audit.AuditEvent -> unit)
        (cancellationToken: CancellationToken)
        : Task<ServerHandle> =
        task {
            let endpoint = parseEndpoint options.listenAddress
            let hub = BrokerState.create brokerVersion options.commandQueueCapacity auditEmitter
            let scriptingService = ScriptingClientService.create hub
            let coordinatorService =
                HighBarCoordinatorService.create hub HighBarCoordinatorService.defaultConfig

            let builder = WebApplication.CreateBuilder()
            // Disable the default startup banner / Kestrel chatter; the
            // dashboard owns the screen.
            LoggingBuilderExtensions.ClearProviders(builder.Logging) |> ignore
            builder.Services.AddGrpc() |> ignore
            // Singleton Service wrappers — DI hands them to the Impl ctors.
            builder.Services.AddSingleton<ScriptingClientService.Service>(scriptingService) |> ignore
            builder.Services.AddSingleton<HighBarCoordinatorService.Service>(coordinatorService) |> ignore
            builder.Services.AddSingleton<BrokerState.Hub>(hub) |> ignore
            builder.WebHost.ConfigureKestrel(fun (opts: KestrelServerOptions) ->
                opts.Listen(endpoint, fun listen ->
                    listen.Protocols <- HttpProtocols.Http2)) |> ignore

            let app = builder.Build()
            app.MapGrpcService<ScriptingClientService.Impl>() |> ignore
            app.MapGrpcService<HighBarCoordinatorService.Impl>() |> ignore

            do! app.StartAsync(cancellationToken)
            let handle = RunningHandle(app, hub, options.listenAddress)
            return (handle :> ServerHandle)
        }
