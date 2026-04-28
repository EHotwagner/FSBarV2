namespace Broker.Mvu

open System
open Broker.Core

module Update =

    let private now (model: Model.Model) : DateTimeOffset =
        // Timestamps from Tick / AdapterCallback / RpcContext arms feed in
        // explicitly; for arms without one, fall back to the snapshot
        // capturedAt, then to startedAt.
        match model.snapshot with
        | Some s -> s.capturedAt
        | None -> model.startedAt

    // ────────────────────────────────────────────────────────────────────
    // Hotkey translation (inlined to keep Broker.Mvu free of Broker.Tui).
    // ────────────────────────────────────────────────────────────────────

    type private HotkeyAction =
        | NoAction
        | QuitApp
        | OpenLobby
        | LaunchHostSession
        | TogglePause
        | StepSpeed of decimal
        | EndSession
        | ToggleViz
        | OpenElevatePrompt
        | KickElevatedClient

    let private isHosting (mode: Mode.Mode) =
        match mode with Mode.Hosting _ -> true | _ -> false

    let private isActive (mode: Mode.Mode) =
        match mode with Mode.Hosting _ | Mode.Guest -> true | Mode.Idle -> false

    let private translateKey (key: ConsoleKeyInfo) (model: Model.Model) : HotkeyAction =
        let mode = model.mode
        let hasDraft = Option.isSome model.pendingLobby
        match key.Key with
        | ConsoleKey.Q -> QuitApp
        | ConsoleKey.V -> ToggleViz
        | ConsoleKey.L when mode = Mode.Idle -> OpenLobby
        // Enter launches a host session when we're already hosting OR when
        // an operator-staged lobby draft is in flight (`pendingLobby`).
        | ConsoleKey.Enter when isHosting mode || hasDraft -> LaunchHostSession
        | ConsoleKey.Spacebar when isHosting mode -> TogglePause
        | ConsoleKey.OemPlus
        | ConsoleKey.Add when isHosting mode -> StepSpeed 0.25m
        | ConsoleKey.OemMinus
        | ConsoleKey.Subtract when isHosting mode -> StepSpeed -0.25m
        | ConsoleKey.A when isHosting mode -> OpenElevatePrompt
        | ConsoleKey.K when isHosting mode -> KickElevatedClient   // worked-example T053
        | ConsoleKey.X when isActive mode -> EndSession
        | _ -> NoAction

    let private applyHotkey (action: HotkeyAction) (model: Model.Model) : Model.Model * Cmd.Cmd<Msg.Msg> list =
        match action with
        | NoAction -> model, []
        | QuitApp -> model, [ Cmd.Quit 0 ]
        | OpenLobby ->
            let defaultDraft : Lobby.LobbyConfig =
                { mapName = "Tabula"
                  gameMode = "Skirmish"
                  participants =
                    [ { slotIndex = 0; kind = ParticipantSlot.ProxyAi; team = 0; boundClient = None }
                      { slotIndex = 1; kind = ParticipantSlot.BuiltInAi 5; team = 1; boundClient = None } ]
                  display = Lobby.Headless }
            { model with pendingLobby = Some defaultDraft }, []
        | LaunchHostSession ->
            match model.pendingLobby with
            | Some lobby ->
                let connectedClients =
                    model.roster |> ScriptingRoster.toList |> List.map (fun c -> c.id)
                match Lobby.validate lobby connectedClients with
                | Result.Ok validLobby ->
                    let session = Session.newHostSession validLobby (now model)
                    let priorMode = model.mode
                    let model' =
                        { model with
                            session = Some session
                            mode = Mode.Hosting validLobby
                            pendingLobby = None
                            slots = validLobby.participants }
                    model',
                    [ Cmd.AuditCmd (Audit.ModeChanged (now model, priorMode, Mode.Hosting validLobby)) ]
                | Result.Error _ -> model, []
            | None -> model, []
        | TogglePause ->
            { model with session = model.session |> Option.map Session.togglePause }, []
        | StepSpeed delta ->
            { model with session = model.session |> Option.map (Session.stepSpeed delta) }, []
        | EndSession ->
            let priorMode = model.mode
            let model' = { model with mode = Mode.Idle; session = None; coordinator = None }
            model',
            [ Cmd.EndSession Session.OperatorTerminated
              Cmd.AuditCmd (Audit.ModeChanged (now model, priorMode, Mode.Idle)) ]
        | ToggleViz ->
            match model.viz with
            | Model.Disabled -> model, []
            | Model.Closed ->
                { model with viz = Model.Active (now model, "viz active") },
                [ Cmd.VizCmd Cmd.OpenWindow ]
            | Model.Active _ ->
                { model with viz = Model.Closed },
                [ Cmd.VizCmd Cmd.CloseWindow ]
            | Model.Failed _ ->
                { model with viz = Model.Closed }, []
        | OpenElevatePrompt ->
            let live = model.roster |> ScriptingRoster.toList
            match live, model.elevation with
            | [ c ], Some current when c.id = current ->
                match ScriptingRoster.revokeAdmin c.id model.roster with
                | Result.Ok r' ->
                    { model with roster = r'; elevation = None },
                    [ Cmd.AuditCmd (Audit.AdminRevoked (now model, c.id, "operator")) ]
                | Result.Error _ -> model, []
            | [ c ], _ ->
                match ScriptingRoster.grantAdmin c.id model.roster with
                | Result.Ok r' ->
                    { model with roster = r'; elevation = Some c.id },
                    [ Cmd.AuditCmd (Audit.AdminGranted (now model, c.id, "operator")) ]
                | Result.Error _ -> model, []
            | _ -> model, []
        | KickElevatedClient ->
            match model.elevation with
            | Some id ->
                let model' = { model with kickedClients = Set.add id model.kickedClients }
                model',
                [ Cmd.AuditCmd (Audit.AdminRevoked (now model, id, "operator-kick"))
                  Cmd.ScriptingReject (id, CommandPipeline.RejectReason.AdminNotAvailable) ]
            | None -> model, []

    let private updateTuiInput (input: Msg.TuiInput) (model: Model.Model) : Model.Model * Cmd.Cmd<Msg.Msg> list =
        match input with
        | Msg.QuitRequested -> model, [ Cmd.Quit 0 ]
        | Msg.Resize _ -> model, []
        | Msg.Keypress key ->
            let action = translateKey key model
            applyHotkey action model

    // ────────────────────────────────────────────────────────────────────
    // Coordinator inbound
    // ────────────────────────────────────────────────────────────────────

    let private updateCoordinatorInbound (msg: Msg.CoordinatorInbound) (model: Model.Model) : Model.Model * Cmd.Cmd<Msg.Msg> list =
        match msg with
        | Msg.Heartbeat (pluginId, schemaVersion, engineSha256, ctx) ->
            let nowAt = ctx.receivedAt
            let isFirst = Option.isNone model.coordinator
            let link : Session.ProxyAiLink =
                match model.coordinator with
                | Some existing -> { existing with lastHeartbeatAt = nowAt }
                | None ->
                    { attachedAt = nowAt
                      protocolVersion = model.brokerInfo.version
                      lastSnapshotAt = None
                      keepAliveIntervalMs = model.config.heartbeatTimeoutMs / 2
                      pluginId = pluginId
                      schemaVersion = schemaVersion
                      engineSha256 = engineSha256
                      lastHeartbeatAt = nowAt
                      lastSeq = 0UL }
            let model' =
                { model with
                    coordinator = Some link
                    mode = if isFirst then Mode.Guest else model.mode }
            let auditCmd =
                if isFirst then
                    Cmd.AuditCmd (Audit.CoordinatorAttached (nowAt, pluginId, schemaVersion, engineSha256))
                else
                    Cmd.AuditCmd (Audit.CoordinatorHeartbeat (nowAt, pluginId, 0u))
            model', [ auditCmd; Cmd.CompleteRpc (ctx.rpcId, Cmd.Ok) ]

        | Msg.PushStateOpened (_pluginId, ctx) ->
            model, [ Cmd.CompleteRpc (ctx.rpcId, Cmd.Ok) ]

        | Msg.PushStateSnapshot (seqNum, snapshot) ->
            let model' =
                { model with
                    snapshot = Some snapshot
                    coordinator =
                        model.coordinator
                        |> Option.map (fun c ->
                            { c with
                                lastSeq = seqNum
                                lastSnapshotAt = Some snapshot.capturedAt
                                lastHeartbeatAt = snapshot.capturedAt }) }
            let fanout =
                model.roster
                |> ScriptingRoster.toList
                |> List.map (fun c -> Cmd.ScriptingOutbound (c.id, Cmd.Snapshot snapshot))
            let vizCmd =
                match model.viz with
                | Model.Active _ -> [ Cmd.VizCmd (Cmd.PushFrame snapshot) ]
                | _ -> []
            model', fanout @ vizCmd

        | Msg.PushStateDelta seqNum ->
            { model with
                coordinator =
                    model.coordinator |> Option.map (fun c -> { c with lastSeq = seqNum }) }, []

        | Msg.PushStateKeepAlive _ -> model, []

        | Msg.PushStateClosed reason ->
            let pluginId =
                model.coordinator
                |> Option.map (fun c -> c.pluginId)
                |> Option.defaultValue ""
            { model with coordinator = None; mode = Mode.Idle },
            [ Cmd.AuditCmd (Audit.CoordinatorDetached (now model, pluginId, reason)) ]

        | Msg.OpenCommandChannelOpened (pluginId, ctx) ->
            model,
            [ Cmd.AuditCmd (Audit.CoordinatorCommandChannelOpened (now model, pluginId))
              Cmd.CompleteRpc (ctx.rpcId, Cmd.Ok) ]

    // ────────────────────────────────────────────────────────────────────
    // Scripting inbound
    // ────────────────────────────────────────────────────────────────────

    let private updateScriptingInbound (msg: Msg.ScriptingInbound) (model: Model.Model) : Model.Model * Cmd.Cmd<Msg.Msg> list =
        match msg with
        | Msg.Hello (id, version, ctx) ->
            match ScriptingRoster.tryAdd id version ctx.receivedAt model.roster with
            | Result.Ok roster' ->
                { model with roster = roster' },
                [ Cmd.AuditCmd (Audit.ClientConnected (ctx.receivedAt, id, version))
                  Cmd.CompleteRpc (ctx.rpcId, Cmd.Ok) ]
            | Result.Error ScriptingRoster.NameInUse ->
                let (ScriptingClientId name) = id
                model,
                [ Cmd.AuditCmd (Audit.NameInUseRejected (ctx.receivedAt, name))
                  Cmd.CompleteRpc (ctx.rpcId, Cmd.Ok) ]
            | Result.Error (ScriptingRoster.NotFound _) ->
                model,
                [ Cmd.CompleteRpc (ctx.rpcId, Cmd.Fault (exn "unexpected NotFound on add")) ]

        | Msg.Subscribe (id, _ctx) ->
            let obs : Model.QueueObservation =
                { depth = 0
                  highWaterMark = 0
                  overflowCount = 0
                  lastSampledAt = now model
                  lastOverflowAt = None }
            { model with queues = Map.add id obs model.queues }, []

        | Msg.Unsubscribe id ->
            { model with
                queues = Map.remove id model.queues
                elevation = if model.elevation = Some id then None else model.elevation }, []

        | Msg.Command (id, command) ->
            match CommandPipeline.authorise model.mode model.roster model.slots command with
            | Result.Ok () ->
                model, [ Cmd.CoordinatorOutbound command ]
            | Result.Error reason ->
                model,
                [ Cmd.ScriptingReject (id, reason)
                  Cmd.AuditCmd (Audit.CommandRejected (now model, id, command.commandId, reason)) ]

        | Msg.Disconnected (id, reason) ->
            { model with
                roster = ScriptingRoster.remove id model.roster
                queues = Map.remove id model.queues
                elevation = if model.elevation = Some id then None else model.elevation
                kickedClients = Set.remove id model.kickedClients },
            [ Cmd.AuditCmd (Audit.ClientDisconnected (now model, id, reason)) ]

    // ────────────────────────────────────────────────────────────────────
    // Adapter callbacks
    // ────────────────────────────────────────────────────────────────────

    let private updateAdapterCallback (msg: Msg.AdapterCallback) (model: Model.Model) : Model.Model * Cmd.Cmd<Msg.Msg> list =
        match msg with
        | Msg.QueueDepth (id, depth, hwSinceLast, sampledAt) ->
            let updated (obs: Model.QueueObservation) =
                { obs with
                    depth = depth
                    highWaterMark = max obs.highWaterMark hwSinceLast
                    lastSampledAt = sampledAt }
            let next =
                model.queues
                |> Map.tryFind id
                |> Option.map updated
                |> Option.defaultValue
                    { depth = depth
                      highWaterMark = hwSinceLast
                      overflowCount = 0
                      lastSampledAt = sampledAt
                      lastOverflowAt = None }
            { model with queues = Map.add id next model.queues }, []

        | Msg.QueueOverflow (id, _rejectedSeq, at) ->
            let bump (obs: Model.QueueObservation) =
                { obs with
                    overflowCount = obs.overflowCount + 1
                    lastOverflowAt = Some at }
            let next =
                model.queues
                |> Map.tryFind id
                |> Option.map bump
                |> Option.defaultValue
                    { depth = 0
                      highWaterMark = 0
                      overflowCount = 1
                      lastSampledAt = at
                      lastOverflowAt = Some at }
            { model with queues = Map.add id next model.queues },
            [ Cmd.AuditCmd (Audit.CommandRejected (at, id, Guid.Empty, CommandPipeline.RejectReason.QueueFull)) ]

        | Msg.TimerFired (timerId, _firedAt) ->
            match Map.tryFind timerId model.timers with
            | Some handle when handle.intervalMs = 0 ->
                { model with timers = Map.remove timerId model.timers }, []
            | Some _ -> model, []
            | None -> model, []

        | Msg.VizWindowClosed _at ->
            { model with viz = Model.Closed }, []

        | Msg.MailboxHighWater (depth, hw, sampledAt) ->
            let cooldownMs = float model.config.mailboxHighWaterCooldownMs
            let withinCooldown =
                match model.lastMailboxAuditAt with
                | Some last -> (sampledAt - last).TotalMilliseconds < cooldownMs
                | None -> false
            { model with
                mailboxDepth = depth
                mailboxHighWater = max model.mailboxHighWater hw
                lastMailboxAuditAt =
                    if withinCooldown then model.lastMailboxAuditAt
                    else Some sampledAt },
            []
            // Audit.MailboxHighWater arm lands in Phase 8 (data-model §3.4); the
            // dashboard already surfaces mailboxDepth/mailboxHighWater fields.

    // ────────────────────────────────────────────────────────────────────
    // Cmd-failure routing (FR-008, exhaustively matched)
    // ────────────────────────────────────────────────────────────────────

    let private updateCmdFailure (failure: Msg.CmdFailure) (model: Model.Model) : Model.Model * Cmd.Cmd<Msg.Msg> list =
        match failure with
        | Msg.AuditWriteFailed _ -> model, []
        | Msg.CoordinatorSendFailed (summary, _exn) ->
            let pluginId =
                model.coordinator
                |> Option.map (fun c -> c.pluginId)
                |> Option.defaultValue ""
            { model with coordinator = None; mode = Mode.Idle },
            [ Cmd.AuditCmd (Audit.CoordinatorDetached (now model, pluginId, sprintf "send-failed: %s" summary)) ]
        | Msg.ScriptingSendFailed (id, _summary, _exn) ->
            { model with
                roster = ScriptingRoster.remove id model.roster
                queues = Map.remove id model.queues },
            [ Cmd.AuditCmd (Audit.ClientDisconnected (now model, id, "send-failed")) ]
        | Msg.VizOpFailed (summary, _exn) ->
            { model with viz = Model.Failed (now model, summary) }, []
        | Msg.TimerFailed (timerId, _summary, _exn) ->
            { model with timers = Map.remove timerId model.timers }, []

    // ────────────────────────────────────────────────────────────────────
    // Tick + Lifecycle
    // ────────────────────────────────────────────────────────────────────

    let private updateTick (tick: Msg.Tick) (model: Model.Model) : Model.Model * Cmd.Cmd<Msg.Msg> list =
        match tick with
        | Msg.DashboardTick _ -> model, []
        | Msg.HeartbeatProbe at ->
            match model.coordinator with
            | Some link ->
                let elapsed = (at - link.lastHeartbeatAt).TotalMilliseconds
                if elapsed > float model.config.heartbeatTimeoutMs then
                    { model with coordinator = None; mode = Mode.Idle },
                    [ Cmd.AuditCmd (Audit.CoordinatorDetached (at, link.pluginId, "heartbeat-timeout")) ]
                else model, []
            | None -> model, []
        | Msg.SnapshotStaleness _ -> model, []

    let private updateLifecycle (lifecycle: Msg.Lifecycle) (model: Model.Model) : Model.Model * Cmd.Cmd<Msg.Msg> list =
        match lifecycle with
        | Msg.RuntimeStarted _at -> model, []
            // Audit arm RuntimeStarted lands Phase 8 (data-model §3.4).
        | Msg.RuntimeStopRequested (_reason, _at) ->
            model, [ Cmd.EndSession Session.OperatorTerminated; Cmd.Quit 0 ]
        | Msg.SessionEnded (reason, at) ->
            let id =
                model.session
                |> Option.map Session.id
                |> Option.defaultValue Guid.Empty
            { model with mode = Mode.Idle; coordinator = None },
            [ Cmd.AuditCmd (Audit.SessionEnded (at, id, reason)) ]

    // ────────────────────────────────────────────────────────────────────
    // The single update entry point
    // ────────────────────────────────────────────────────────────────────

    let update (msg: Msg.Msg) (model: Model.Model) : Model.Model * Cmd.Cmd<Msg.Msg> list =
        match msg with
        | Msg.TuiInput input -> updateTuiInput input model
        | Msg.CoordinatorInbound c -> updateCoordinatorInbound c model
        | Msg.ScriptingInbound s -> updateScriptingInbound s model
        | Msg.AdapterCallback a -> updateAdapterCallback a model
        | Msg.CmdFailure f -> updateCmdFailure f model
        | Msg.Tick t -> updateTick t model
        | Msg.Lifecycle l -> updateLifecycle l model
