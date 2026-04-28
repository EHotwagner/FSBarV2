namespace Broker.Viz

open System
open Broker.Core

module SceneBuilder =

    /// Ownership colour assigned to a participant. Derived from the
    /// player's team id by `playerColor`. Team-derived rather than
    /// per-player so that allied players share a colour (FR-023).
    type PlayerColor = { r: byte; g: byte; b: byte }

    /// One unit or building positioned in world space, captured as a
    /// diagnostic projection of the scene the viewer is asked to draw.
    type SceneEntity =
        { id: uint32
          ownerPlayerId: int
          pos: Snapshot.Vec2
          isBuilding: bool }

    /// Pure projection of the scene; what `summary` returns for tests
    /// and dashboard introspection. Carries no SkiaViewer types.
    type SceneSummary =
        { snapshotTick: int64
          sessionId: Guid
          mapName: string option
          mapOutlineByteCount: int
          mapSize: Snapshot.Vec2 option
          entities: SceneEntity list
          ownership: Map<int, PlayerColor> }

    /// Opaque scene record handed to `VizHost`. Wraps SkiaViewer's
    /// scene model — concrete shape lives in the implementation so the
    /// public surface is independent of the SkiaViewer / SkiaSharp types
    /// (the lone exception being the `toSkiaScene` accessor below, which
    /// `VizHost` needs to push frames into `SkiaViewer.Viewer.run`).
    type Scene

    /// Map a single broker-side game-state snapshot to a viewable scene:
    /// map outline (when present in the snapshot's `mapMeta`), all units
    /// and buildings positioned in world space, ownership-coloured per
    /// player team (FR-023).
    val build :
        snapshot:Snapshot.GameStateSnapshot
        -> Scene

    /// Empty scene used between snapshots and on session-end.
    val empty : Scene

    /// Diagnostic projection of a scene — used by tests and the
    /// dashboard footer to introspect a scene without touching its
    /// underlying SkiaViewer representation.
    val summary : scene:Scene -> SceneSummary

    /// Deterministic ownership colour for a given player. Picks from a
    /// fixed 8-entry palette by teamId (modular). The palette is the
    /// same one the renderer uses, so the colour returned here is the
    /// colour the viewer will draw.
    val playerColor : playerId:int -> teamId:int -> PlayerColor

    /// VizHost-facing accessor. Returns the SkiaViewer scene the
    /// viewer's render loop should draw next. Consumers outside
    /// `Broker.Viz` do not need this.
    val toSkiaScene : scene:Scene -> SkiaViewer.Scene
