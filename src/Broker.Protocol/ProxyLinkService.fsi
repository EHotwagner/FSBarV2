namespace Broker.Protocol

open Broker.Core
open FSBarV2.Broker.Contracts

module ProxyLinkService =

    /// Wrapper carrying the in-process broker state hub. The underlying
    /// gRPC service implementation is registered by `ServerHost.start` via
    /// `app.MapGrpcService<Impl>()`; the `Impl` class derives from the
    /// generated `ProxyLink.ProxyLinkBase` and is constructed by the
    /// ASP.NET Core DI container.
    type Service

    val create :
        hub:BrokerState.Hub
        -> Service

    val isAttached : service:Service -> bool

    /// Force-detach the current proxy (operator quit, broker shutdown).
    /// The bidi `Attach` stream is closed and subscribed scripting clients
    /// receive a `SessionEnd` notification (FR-026).
    val detach : service:Service -> reason:string -> unit

    /// The concrete gRPC service class registered via `MapGrpcService`.
    /// Public so ASP.NET Core DI can construct it; its single constructor
    /// param is resolved from the singleton `Service` registered alongside.
    type Impl =
        inherit ProxyLink.ProxyLinkBase
        new : service:Service -> Impl
