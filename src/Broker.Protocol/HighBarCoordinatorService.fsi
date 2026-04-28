namespace Broker.Protocol

open Broker.Core
open Highbar.V1

/// Server-side wrapper around the upstream HighBarV3 `HighBarCoordinator`
/// service (vendored under `src/Broker.Contracts/highbar/`). Hosts the
/// three RPCs the plugin drives — unary `Heartbeat`, client-streaming
/// `PushState`, server-streaming `OpenCommandChannel` — over the same
/// Kestrel listener as `ScriptingClientService` (FR-010).
module HighBarCoordinatorService =

    /// Opaque service handle; carries the `BrokerState.Hub` plus the
    /// coordinator-side configuration. Constructed once at composition
    /// root and registered via `app.MapGrpcService<Impl>()`.
    type Service

    type Config =
        { expectedSchemaVersion: string
          ownerRule: BrokerState.OwnerRule
          heartbeatTimeoutMs: int }

    val defaultConfig : Config

    val create :
        hub:BrokerState.Hub
        -> config:Config
        -> Service

    /// True iff a coordinator session is currently attached.
    val isAttached : service:Service -> bool

    /// Force-detach the current coordinator (operator quit, broker
    /// shutdown). Closes any open `PushState` / `OpenCommandChannel`
    /// streams and emits `SessionEnd` to subscribed scripting clients
    /// (FR-008).
    val detach : service:Service -> reason:string -> unit

    /// The concrete gRPC service class registered via `MapGrpcService`.
    /// Public so ASP.NET Core DI can construct it; its single constructor
    /// param is the `Service` registered alongside.
    type Impl =
        inherit HighBarCoordinator.HighBarCoordinatorBase
        new : service:Service -> Impl
