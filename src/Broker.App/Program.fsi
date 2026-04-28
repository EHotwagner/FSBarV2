namespace Broker.App

module Program =

    /// Composition-root entry point. Wires Logging, Core, Protocol, Tui,
    /// and (optionally) Viz, then runs until the operator presses `Q` or
    /// the process receives `SIGINT` / Ctrl-C.
    [<EntryPoint>]
    val main : argv:string array -> int
