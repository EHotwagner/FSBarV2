namespace Broker.Core

open System

module Audit =

    type AuditEvent =
        | ProxyAttached of at:DateTimeOffset * version:Version
        | ProxyDetached of at:DateTimeOffset * reason:string
        | ClientConnected of at:DateTimeOffset * id:ScriptingClientId * version:Version
        | ClientDisconnected of at:DateTimeOffset * id:ScriptingClientId * reason:string
        | NameInUseRejected of at:DateTimeOffset * attempted:string
        | VersionMismatchRejected of at:DateTimeOffset * peerKind:string * peerVersion:Version
        | AdminGranted of at:DateTimeOffset * id:ScriptingClientId * by:string
        | AdminRevoked of at:DateTimeOffset * id:ScriptingClientId * by:string
        | CommandRejected of at:DateTimeOffset * id:ScriptingClientId * commandId:Guid * reason:CommandPipeline.RejectReason
        | ModeChanged of at:DateTimeOffset * from':Mode.Mode * to':Mode.Mode
        | SessionEnded of at:DateTimeOffset * sessionId:Guid * reason:Session.EndReason

    let private nameOf (ScriptingClientId n) = n

    let toLogTemplate (event: AuditEvent) : struct(string * (string * objnull) array) =
        match event with
        | ProxyAttached (at, v) ->
            struct (
                "audit.proxy_attached at={At} version={Version}",
                [| "At", box at; "Version", box (string v) |])
        | ProxyDetached (at, reason) ->
            struct (
                "audit.proxy_detached at={At} reason={Reason}",
                [| "At", box at; "Reason", box reason |])
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
