namespace Broker.App

module Cli =

    type Args =
        { listen: string
          noViz: bool
          showVersion: bool
          printSchemaVersion: bool
          expectedSchemaVersion: string option }

    let defaults : Args =
        { listen = "127.0.0.1:5021"
          noViz = false
          showVersion = false
          printSchemaVersion = false
          expectedSchemaVersion = None }

    let usage () : string =
        String.concat "\n" [
            "broker [options]"
            ""
            "Options:"
            "  --listen HOST:PORT             gRPC server listen address (default 127.0.0.1:5021)"
            "  --no-viz                       disable the optional 2D visualization subsystem"
            "  --version                      print the broker version and exit"
            "  --print-schema-version         print the expected coordinator schema version and exit"
            "  --expected-schema-version V    override the expected coordinator schema version (default 1.0.0)"
            "  -h, --help                     print this help text"
            ""
        ]

    let private parseListen (value: string) : Result<string, string> =
        if System.String.IsNullOrWhiteSpace value then
            Error "--listen requires a HOST:PORT argument"
        elif not (value.Contains ":") then
            Error (sprintf "--listen value '%s' is not in HOST:PORT form" value)
        else Ok value

    let parse (argv: string array) : Result<Args, string> =
        let rec loop (acc: Args) (argv: string list) : Result<Args, string> =
            match argv with
            | [] -> Ok acc
            | "--no-viz" :: rest          -> loop { acc with noViz = true } rest
            | "--version" :: rest         -> loop { acc with showVersion = true } rest
            | "--print-schema-version" :: rest ->
                loop { acc with printSchemaVersion = true } rest
            | "--expected-schema-version" :: value :: rest ->
                if System.String.IsNullOrWhiteSpace value then
                    Error "--expected-schema-version requires a value"
                else
                    loop { acc with expectedSchemaVersion = Some value } rest
            | "--expected-schema-version" :: [] ->
                Error "--expected-schema-version requires a value"
            | ("--help" | "-h") :: _      -> Error "help requested"
            | "--listen" :: value :: rest ->
                match parseListen value with
                | Ok v -> loop { acc with listen = v } rest
                | Error e -> Error e
            | "--listen" :: [] -> Error "--listen requires a HOST:PORT argument"
            | unknown :: _ -> Error (sprintf "unknown argument: %s" unknown)
        loop defaults (List.ofArray argv)
