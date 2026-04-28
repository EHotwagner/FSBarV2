namespace Broker.App

module Cli =

    type Args =
        { listen: string
          noViz: bool
          showVersion: bool
          /// Print the broker's expected coordinator schema version and
          /// exit (FR-014). Used as a pre-flight before launching the
          /// HighBarV3 plugin to confirm version alignment.
          printSchemaVersion: bool
          /// Override the broker's expected coordinator schema version.
          /// `None` keeps the compile-time default (`"1.0.0"`); `Some v`
          /// makes every Heartbeat strict-compare against `v`. Used by
          /// the schema-mismatch quickstart flow (FR-003 / SC-007).
          expectedSchemaVersion: string option }

    val defaults : Args

    /// Parse argv into `Args`. Returns `Error msg` on unknown flags or
    /// malformed `--listen host:port`.
    val parse : argv:string array -> Result<Args, string>

    /// Human-readable usage string printed on `--help` or parse failure.
    val usage : unit -> string
