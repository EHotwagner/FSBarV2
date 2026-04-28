namespace Broker.Core

open System

module Dashboard =

    type ServerState =
        | Listening of address:string
        | Down of reason:string

    type DiagnosticReading =
        { broker: Session.BrokerInfo
          serverState: ServerState
          connectedClients: ScriptingRoster.ScriptingClient list
          mode: Mode.Mode
          session: Session.SessionState option
          elapsed: TimeSpan option
          pause: Session.Pause option
          speed: decimal option
          telemetry: Snapshot.GameStateSnapshot option
          telemetryStale: bool }

    /// Pure assembly of the dashboard view-model from broker components.
    /// Rendering is the TUI's concern (see `Broker.Tui.DashboardView`).
    val build :
        broker:Session.BrokerInfo
        -> serverState:ServerState
        -> roster:ScriptingRoster.Roster
        -> session:Session.Session option
        -> now:DateTimeOffset
        -> staleThreshold:TimeSpan
        -> DiagnosticReading
