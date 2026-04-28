namespace Broker.Core

open System

module Audit =

    type AuditEvent =
        | ClientConnected of at:DateTimeOffset * id:ScriptingClientId * version:Version
        | ClientDisconnected of at:DateTimeOffset * id:ScriptingClientId * reason:string
        | NameInUseRejected of at:DateTimeOffset * attempted:string
        | VersionMismatchRejected of at:DateTimeOffset * peerKind:string * peerVersion:Version
        | AdminGranted of at:DateTimeOffset * id:ScriptingClientId * by:string
        | AdminRevoked of at:DateTimeOffset * id:ScriptingClientId * by:string
        | CommandRejected of at:DateTimeOffset * id:ScriptingClientId * commandId:Guid * reason:CommandPipeline.RejectReason
        | ModeChanged of at:DateTimeOffset * from':Mode.Mode * to':Mode.Mode
        | SessionEnded of at:DateTimeOffset * sessionId:Guid * reason:Session.EndReason
        | CoordinatorAttached of at:DateTimeOffset * pluginId:string * schemaVersion:string * engineSha256:string
        | CoordinatorDetached of at:DateTimeOffset * pluginId:string * reason:string
        | CoordinatorSchemaMismatch of at:DateTimeOffset * expected:string * received:string * pluginId:string
        | CoordinatorNonOwnerRejected of at:DateTimeOffset * attemptedPluginId:string * ownerPluginId:string
        | CoordinatorHeartbeat of at:DateTimeOffset * pluginId:string * frame:uint32
        | CoordinatorCommandChannelOpened of at:DateTimeOffset * pluginId:string
        | CoordinatorCommandChannelClosed of at:DateTimeOffset * pluginId:string * reason:string
        | CoordinatorStateGap of at:DateTimeOffset * pluginId:string * lastSeq:uint64 * receivedSeq:uint64

    let private nameOf (ScriptingClientId n) = n

    let toLogTemplate (event: AuditEvent) : struct(string * (string * objnull) array) =
        match event with
        | ClientConnected (at, id, v) ->
            struct (
                "audit.client_connected at={At} client_name={ClientName} version={Version}",
                [| "At", box at; "ClientName", box (nameOf id); "Version", box (string v) |])
        | ClientDisconnected (at, id, reason) ->
            struct (
                "audit.client_disconnected at={At} client_name={ClientName} reason={Reason}",
                [| "At", box at; "ClientName", box (nameOf id); "Reason", box reason |])
        | NameInUseRejected (at, attempted) ->
            struct (
                "audit.name_in_use_rejected at={At} attempted={Attempted}",
                [| "At", box at; "Attempted", box attempted |])
        | VersionMismatchRejected (at, peerKind, peerVersion) ->
            struct (
                "audit.version_mismatch_rejected at={At} peer_kind={PeerKind} peer_version={PeerVersion}",
                [| "At", box at; "PeerKind", box peerKind; "PeerVersion", box (string peerVersion) |])
        | AdminGranted (at, id, by) ->
            struct (
                "audit.admin_granted at={At} client_name={ClientName} by={By}",
                [| "At", box at; "ClientName", box (nameOf id); "By", box by |])
        | AdminRevoked (at, id, by) ->
            struct (
                "audit.admin_revoked at={At} client_name={ClientName} by={By}",
                [| "At", box at; "ClientName", box (nameOf id); "By", box by |])
        | CommandRejected (at, id, cmdId, reason) ->
            struct (
                "audit.command_rejected at={At} client_name={ClientName} command_id={CommandId} reason={Reason}",
                [| "At", box at; "ClientName", box (nameOf id); "CommandId", box cmdId; "Reason", box (sprintf "%A" reason) |])
        | ModeChanged (at, from, to_) ->
            struct (
                "audit.mode_changed at={At} from={From} to={To}",
                [| "At", box at; "From", box (sprintf "%A" from); "To", box (sprintf "%A" to_) |])
        | SessionEnded (at, sid, reason) ->
            struct (
                "audit.session_ended at={At} session_id={SessionId} reason={Reason}",
                [| "At", box at; "SessionId", box sid; "Reason", box (sprintf "%A" reason) |])
        | CoordinatorAttached (at, pid, sv, esha) ->
            struct (
                "audit.coordinator_attached at={At} plugin_id={PluginId} schema_version={SchemaVersion} engine_sha256={EngineSha256}",
                [| "At", box at; "PluginId", box pid; "SchemaVersion", box sv; "EngineSha256", box esha |])
        | CoordinatorDetached (at, pid, reason) ->
            struct (
                "audit.coordinator_detached at={At} plugin_id={PluginId} reason={Reason}",
                [| "At", box at; "PluginId", box pid; "Reason", box reason |])
        | CoordinatorSchemaMismatch (at, expected, received, pid) ->
            struct (
                "audit.coordinator_schema_mismatch at={At} expected={Expected} received={Received} plugin_id={PluginId}",
                [| "At", box at; "Expected", box expected; "Received", box received; "PluginId", box pid |])
        | CoordinatorNonOwnerRejected (at, attempted, owner) ->
            struct (
                "audit.coordinator_non_owner_rejected at={At} attempted_plugin_id={AttemptedPluginId} owner_plugin_id={OwnerPluginId}",
                [| "At", box at; "AttemptedPluginId", box attempted; "OwnerPluginId", box owner |])
        | CoordinatorHeartbeat (at, pid, frame) ->
            struct (
                "audit.coordinator_heartbeat at={At} plugin_id={PluginId} frame={Frame}",
                [| "At", box at; "PluginId", box pid; "Frame", box frame |])
        | CoordinatorCommandChannelOpened (at, pid) ->
            struct (
                "audit.coordinator_command_channel_opened at={At} plugin_id={PluginId}",
                [| "At", box at; "PluginId", box pid |])
        | CoordinatorCommandChannelClosed (at, pid, reason) ->
            struct (
                "audit.coordinator_command_channel_closed at={At} plugin_id={PluginId} reason={Reason}",
                [| "At", box at; "PluginId", box pid; "Reason", box reason |])
        | CoordinatorStateGap (at, pid, lastSeq, recvSeq) ->
            struct (
                "audit.coordinator_state_gap at={At} plugin_id={PluginId} last_seq={LastSeq} received_seq={ReceivedSeq}",
                [| "At", box at; "PluginId", box pid; "LastSeq", box lastSeq; "ReceivedSeq", box recvSeq |])
