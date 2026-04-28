# Public F# Surface Sketches

This document is the Phase 1 design sketch of the curated `.fsi` files for
each new public module. Per Constitution Principle II, *visibility lives in
.fsi*; what is in this document is what the rest of the world sees. Anything
omitted is implicitly private. Surface-area baselines (one per module) will
be generated from these and validated by an Expecto test in
`tests/SurfaceArea/`.

The signatures here are the design contract; the actual `.fsi` files
will be authored under `src/Broker.*` during implementation. They are
exercised in FSI via `scripts/prelude.fsx` *before* their `.fs` bodies
are written (Constitution Principle I).

---

## `Broker.Core` — pure session logic, no I/O

### `module Broker.Core.Mode`
```fsharp
namespace Broker.Core

module Mode =
    type Mode =
        | Idle
        | Hosting of LobbyConfig
        | Guest

    /// True iff admin commands may be issued in this mode.
    val isAdminAuthorised : Mode -> bool

    /// Validate a transition; returns the new mode or a rejection.
    val transition : current:Mode -> next:Mode -> Result<Mode, string>
```

### `module Broker.Core.Lobby`
```fsharp
namespace Broker.Core

module Lobby =
    type Display = Headless | Graphical
    type LobbyConfig =
        { mapName: string
          gameMode: string
          participants: ParticipantSlot list
          display: Display }

    /// Validate per FR-013. Returns the canonical config or the first
    /// validation failure encountered.
    val validate : LobbyConfig -> Result<LobbyConfig, LobbyError>

    type LobbyError =
        | EmptyMapName
        | EmptyGameMode
        | DuplicateSlotIndex of int
        | TooManyParticipants of capacity:int * actual:int
        | MissingProxySlotForBoundClient of clientName:string
```

### `module Broker.Core.ParticipantSlot`
```fsharp
namespace Broker.Core

module ParticipantSlot =
    type Difficulty = int
    type ParticipantKind =
        | Human
        | BuiltInAi of Difficulty
        | ProxyAi

    type ParticipantSlot =
        { slotIndex: int
          kind: ParticipantKind
          team: int
          boundClient: ScriptingClientId option }
```

### `module Broker.Core.ScriptingRoster`
```fsharp
namespace Broker.Core

[<Struct>]
type ScriptingClientId = ScriptingClientId of name:string

module ScriptingRoster =
    type Roster

    val empty : Roster

    /// Try to add a client with the given name. Fails with
    /// `Result.Error NameInUse` if the name is currently held.
    val tryAdd :
        id:ScriptingClientId
        -> version:Version
        -> connectedAt:DateTimeOffset
        -> roster:Roster
        -> Result<Roster, RosterError>

    val remove : id:ScriptingClientId -> roster:Roster -> Roster

    val grantAdmin : id:ScriptingClientId -> roster:Roster -> Result<Roster, RosterError>
    val revokeAdmin : id:ScriptingClientId -> roster:Roster -> Result<Roster, RosterError>

    val isAdmin : id:ScriptingClientId -> roster:Roster -> bool
    val toList : roster:Roster -> ScriptingClient list

    type RosterError =
        | NameInUse
        | NotFound of ScriptingClientId
```

### `module Broker.Core.CommandPipeline`
```fsharp
namespace Broker.Core

module CommandPipeline =
    /// Per-client bounded queue. Implementation uses
    /// `System.Threading.Channels.BoundedChannel`.
    type Queue

    /// Capacity is total queue depth; default 64. Caller chooses.
    val createQueue : capacity:int -> Queue

    type EnqueueOutcome =
        | Accepted
        | Rejected of RejectReason

    /// Synchronous enqueue. Never blocks. Backpressure is managed at the
    /// gRPC edge (BackpressureGate); arrivals past the cap return
    /// `Rejected QueueFull` with the original commandId echoed.
    val tryEnqueue : queue:Queue -> command:Command -> EnqueueOutcome

    /// Authority check. Pure — does not touch the queue.
    /// Returns `Ok` for accepted, `Error reason` for rejected.
    val authorise :
        mode:Mode
        -> roster:ScriptingRoster.Roster
        -> slots:ParticipantSlot list
        -> command:Command
        -> Result<unit, RejectReason>

    /// Drain up to `max` commands from the queue, in FIFO order.
    val drain : max:int -> queue:Queue -> Command list

    val depth : queue:Queue -> int
```

### `module Broker.Core.Session`
```fsharp
namespace Broker.Core

module Session =
    type Session

    val newHostSession : config:LobbyConfig -> startedAt:DateTimeOffset -> Session
    val newGuestSession : startedAt:DateTimeOffset -> Session

    val state : Session -> SessionState
    val mode : Session -> Mode

    val attachProxy : link:ProxyAiLink -> session:Session -> Result<Session, string>
    val applySnapshot : snapshot:GameStateSnapshot -> session:Session -> Session

    val end_ : reason:EndReason -> at:DateTimeOffset -> session:Session -> Session

    /// Pure projection used by the dashboard.
    val toReading : now:DateTimeOffset -> session:Session -> SessionReading
```

### `module Broker.Core.Dashboard`
```fsharp
namespace Broker.Core

module Dashboard =
    /// Pure assembly of the dashboard view-model from broker components.
    /// Has no rendering concerns; the TUI decides how to lay it out.
    val build :
        broker:BrokerInfo
        -> serverState:ServerState
        -> roster:ScriptingRoster.Roster
        -> session:Session option
        -> now:DateTimeOffset
        -> staleThreshold:TimeSpan
        -> DiagnosticReading
```

### `module Broker.Core.Audit`
```fsharp
namespace Broker.Core

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
        | CommandRejected of at:DateTimeOffset * id:ScriptingClientId * commandId:Guid * reason:RejectReason
        | ModeChanged of at:DateTimeOffset * from':Mode * to':Mode
        | SessionEnded of at:DateTimeOffset * sessionId:Guid * reason:EndReason

    /// Render an event for Serilog. Returns the message template + its
    /// structured property bag.
    val toLogTemplate : AuditEvent -> struct(string * (string * obj) array)
```

---

## `Broker.Protocol` — gRPC server adapter

### `module Broker.Protocol.VersionHandshake`
```fsharp
namespace Broker.Protocol

module VersionHandshake =
    /// Strict major match (FR-029). Returns Ok unit on accept,
    /// `Error broker'sVersion` on reject (the value the wire layer must
    /// echo to the peer).
    val check :
        broker:Version
        -> peer:Version
        -> Result<unit, Version>
```

### `module Broker.Protocol.BackpressureGate`
```fsharp
namespace Broker.Protocol

module BackpressureGate =
    /// Bridges per-client `CommandPipeline.Queue` to gRPC HTTP/2 flow
    /// control on a server-side bidi stream. Pauses reads when the
    /// queue is at capacity; resumes when a slot frees.
    type Gate

    val create :
        queue:CommandPipeline.Queue
        -> Gate

    /// Process one inbound command from the wire. Returns the wire-side
    /// `CommandAck` to send back. Synchronous; never blocks the
    /// gRPC reader thread.
    val process_ :
        gate:Gate
        -> mode:Mode
        -> roster:ScriptingRoster.Roster
        -> slots:ParticipantSlot list
        -> command:Command
        -> CommandAck
```

### `module Broker.Protocol.ServerHost`
```fsharp
namespace Broker.Protocol

module ServerHost =
    type Options =
        { listenAddress: string       // "127.0.0.1:5021" default
          keepaliveIntervalMs: int    // 2000 default
          commandQueueCapacity: int } // 64 default

    val defaultOptions : Options

    /// Start the gRPC server. Returns a handle that disposes cleanly on
    /// `dispose` and propagates fatal errors via the returned task.
    val start :
        options:Options
        -> core:CoreFacade
        -> System.Threading.CancellationToken
        -> System.Threading.Tasks.Task<ServerHandle>

    type ServerHandle =
        inherit System.IAsyncDisposable
        abstract Listening : string
        abstract IsRunning : bool
```

`CoreFacade` is the small surface `Broker.Protocol` needs from `Broker.Core`
without depending on the full module tree — defined in `Broker.Core` and
re-exposed here.

---

## `Broker.Tui`

### `module Broker.Tui.HotkeyMap`
```fsharp
namespace Broker.Tui

module HotkeyMap =
    type Action =
        | Quit
        | OpenLobby                  // Idle → host configuration
        | LaunchHostSession
        | TogglePause
        | StepSpeed of delta:decimal
        | ElevateClient of ScriptingClientId
        | RevokeClient of ScriptingClientId
        | ToggleViz
        | NoAction

    /// Map a single key (with modifiers) to an Action in the current
    /// dashboard mode. Returns NoAction for unbound keys.
    val map :
        key:System.ConsoleKeyInfo
        -> mode:Mode
        -> Action
```

### `module Broker.Tui.DashboardView`
```fsharp
namespace Broker.Tui

module DashboardView =
    /// Pure render: turn a DiagnosticReading into a Spectre.Console
    /// `Layout` ready for a Live context. Unit-testable without a TTY.
    val render : reading:DiagnosticReading -> Spectre.Console.Layout
```

### `module Broker.Tui.TickLoop`
```fsharp
namespace Broker.Tui

module TickLoop =
    /// Single-thread render-and-input loop. Owns the AnsiConsole.Live
    /// context. Returns when the user issues `Quit` or the cancellation
    /// token fires.
    val run :
        core:CoreFacade
        -> tickIntervalMs:int       // default 100
        -> System.Threading.CancellationToken
        -> System.Threading.Tasks.Task<unit>
```

---

## `Broker.Viz`

### `module Broker.Viz.VizHost`
```fsharp
namespace Broker.Viz

module VizHost =
    /// Probe the runtime environment for a usable graphical display.
    /// Returns the reason on unavailable hosts (FR-025, SC-008).
    val probe : unit -> Result<unit, string>

    type Handle =
        inherit System.IAsyncDisposable
        abstract IsOpen : bool

    /// Open the SkiaViewer window. Subscribes to the broker's snapshot
    /// stream and pushes scenes via IObservable<Scene>. Returns a handle
    /// that closes the window on disposal.
    val open_ :
        snapshots:System.IObservable<GameStateSnapshot>
        -> System.Threading.Tasks.Task<Handle>
```

---

## `Broker.App`

### `module Broker.App.Cli`
```fsharp
namespace Broker.App

module Cli =
    type Args =
        { listen: string             // host:port
          noViz: bool                // disable viz subsystem entirely
          showVersion: bool }

    val parse : argv:string array -> Result<Args, string>
```

### `module Broker.App.Program`
```fsharp
namespace Broker.App

module Program =
    /// Entry point. Wires Logging, Core, Protocol, Tui, optional Viz.
    [<EntryPoint>]
    val main : argv:string array -> int
```

---

## Surface-area baselines

For each module above, an Expecto test in `tests/SurfaceArea/` will:

1. Use `System.Reflection` against the packed assembly to enumerate the
   public surface.
2. Compare to a checked-in baseline file (one per module, e.g.
   `Broker.Core.Mode.surface.txt`).
3. Fail loudly when the surface drifts without an accompanying baseline
   update — preventing accidental public-surface additions.

Baselines are part of the Tier 1 contract (constitution Engineering
Constraints). Updating them is allowed, but the change must travel
together with a `.fsi` change in the same commit.
