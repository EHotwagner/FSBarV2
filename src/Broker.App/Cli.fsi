namespace Broker.App

module Cli =

    type Args =
        { listen: string
          noViz: bool
          showVersion: bool }

    val defaults : Args

    /// Parse argv into `Args`. Returns `Error msg` on unknown flags or
    /// malformed `--listen host:port`.
    val parse : argv:string array -> Result<Args, string>

    /// Human-readable usage string printed on `--help` or parse failure.
    val usage : unit -> string
