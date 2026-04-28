namespace Broker.Protocol

open System
open System.Threading
open System.Threading.Tasks
open Broker.Core

module ServerHost =

    type Options =
        { listenAddress: string
          keepaliveIntervalMs: int
          commandQueueCapacity: int }

    val defaultOptions : Options

    type ServerHandle =
        inherit IAsyncDisposable
        abstract Listening : string
        abstract IsRunning : bool
        /// Reference to the in-process state hub the host wired the
        /// services against (so the TUI / composition root can read it).
        abstract Hub : BrokerState.Hub

    /// Start the gRPC server. Returns a handle that disposes cleanly via
    /// `IAsyncDisposable`; fatal errors propagate via the returned task.
    /// Two services are registered on the single Kestrel listener:
    /// `ProxyLink` (FR-005, in-game proxy AI) and `ScriptingClient`
    /// (external bots / automation tools).
    val start :
        options:Options
        -> brokerVersion:Version
        -> auditEmitter:(Audit.AuditEvent -> unit)
        -> CancellationToken
        -> Task<ServerHandle>
