namespace Broker.App

module Cli =

    type Args =
        { listen: string
          noViz: bool
          showVersion: bool }

    let defaults : Args =
        { listen = "127.0.0.1:5021"
          noViz = false
          showVersion = false }

    let usage () : string =
        String.concat "\n" [
            "broker [options]"
            ""
            "Options:"
            "  --listen HOST:PORT   gRPC server listen address (default 127.0.0.1:5021)"
            "  --no-viz             disable the optional 2D visualization subsystem"
            "  --version            print the broker version and exit"
            "  -h, --help           print this help text"
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
            | ("--help" | "-h") :: _      -> Error "help requested"
            | "--listen" :: value :: rest ->
                match parseListen value with
                | Ok v -> loop { acc with listen = v } rest
                | Error e -> Error e
            | "--listen" :: [] -> Error "--listen requires a HOST:PORT argument"
            | unknown :: _ -> Error (sprintf "unknown argument: %s" unknown)
        loop defaults (List.ofArray argv)
