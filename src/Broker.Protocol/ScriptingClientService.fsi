namespace Broker.Protocol

open Broker.Core
open FSBarV2.Broker.Contracts

module ScriptingClientService =

    /// Wrapper carrying the in-process broker state hub. The underlying
    /// gRPC service implementation is registered by `ServerHost.start` via
    /// `app.MapGrpcService<Impl>()`; the `Impl` class derives from the
    /// generated `ScriptingClient.ScriptingClientBase` and is constructed
    /// by the ASP.NET Core DI container.
    type Service

    val create :
        hub:BrokerState.Hub
        -> Service

    /// Current count of live scripting clients (for dashboard / metrics).
    val connectedCount : service:Service -> int

    /// Notify all subscribers that the active session has ended (FR-026).
    /// Closes their state streams cleanly.
    val broadcastSessionEnd :
        service:Service
        -> reason:Session.EndReason
        -> unit

    /// Concrete gRPC service class registered via `MapGrpcService`.
    type Impl =
        inherit ScriptingClient.ScriptingClientBase
        new : service:Service -> Impl
