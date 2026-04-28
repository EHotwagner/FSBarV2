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

    let build
        (broker: Session.BrokerInfo)
        (serverState: ServerState)
        (roster: ScriptingRoster.Roster)
        (session: Session.Session option)
        (now: DateTimeOffset)
        (staleThreshold: TimeSpan)
        : DiagnosticReading =
        let reading = session |> Option.map (Session.toReading now)
        let mode    = reading |> Option.map (fun r -> r.mode)    |> Option.defaultValue Mode.Mode.Idle
        let state   = reading |> Option.map (fun r -> r.state)
        let elapsed = reading |> Option.map (fun r -> r.elapsed)
        let pause   = reading |> Option.map (fun r -> r.pause)
        let speed   = reading |> Option.map (fun r -> r.speed)
        let telemetry = reading |> Option.bind (fun r -> r.telemetry)
        let stale =
            // FR-021: telemetryStale = true iff (now - lastSnapshotAt) > threshold.
            // No proxy attached → not stale (we don't claim stale until we expect data).
            match reading |> Option.bind (fun r -> r.proxy) with
            | None -> false
            | Some p ->
                match p.lastSnapshotAt with
                | None      -> (now - p.attachedAt) > staleThreshold
                | Some last -> (now - last) > staleThreshold
        { broker = broker
          serverState = serverState
          connectedClients = ScriptingRoster.toList roster
          mode = mode
          session = state
          elapsed = elapsed
          pause = pause
          speed = speed
          telemetry = telemetry
          telemetryStale = stale }
