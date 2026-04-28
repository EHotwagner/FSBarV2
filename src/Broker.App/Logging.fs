namespace Broker.App

open System.IO
open Broker.Core
open Microsoft.Extensions.Logging
open Serilog
open Serilog.Extensions.Logging

module Logging =

    let configure (logDirectory: string) : ILoggerFactory =
        Directory.CreateDirectory(logDirectory) |> ignore
        let logPath = Path.Combine(logDirectory, "broker-.log")
        let serilog =
            LoggerConfiguration()
                .MinimumLevel.Information()
                .Enrich.FromLogContext()
                .WriteTo.File(
                    path = logPath,
                    rollingInterval = RollingInterval.Day,
                    shared = true,
                    outputTemplate = "{Timestamp:o} [{Level:u3}] {Message:lj} {Properties:j}{NewLine}")
                .CreateLogger()
        // Keep Log.Logger global wired so any Serilog sinks elsewhere flush.
        Log.Logger <- serilog
        let factory = new SerilogLoggerFactory(serilog, dispose = true)
        factory :> ILoggerFactory

    let writeAudit (factory: ILoggerFactory) (event: Audit.AuditEvent) : unit =
        let logger = factory.CreateLogger("audit")
        let struct(template, props) = Audit.toLogTemplate event
        // Pass the property bag through Serilog's structured-log scope so
        // each name lands in the JSON properties bag, then emit a single
        // event with the audit-specific template.
        let dict =
            props
            |> Array.map (fun (k, v) -> System.Collections.Generic.KeyValuePair(k, v))
            :> System.Collections.Generic.IEnumerable<_>
        use _ = logger.BeginScope(dict)
        logger.LogInformation(template)
