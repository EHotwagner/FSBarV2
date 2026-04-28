namespace Broker.Core

open System

module Snapshot =

    type Vec2 = { x: float32; y: float32 }

    type ResourceVector = { metal: float; energy: float }

    type EconomyStats =
        { income: ResourceVector
          expenditure: ResourceVector }

    type MapMeta =
        { mapName: string
          size: Vec2
          outline: byte[] }

    type PlayerTelemetry =
        { playerId: int
          teamId: int
          name: string
          resources: ResourceVector
          unitCount: int
          buildingCount: int
          unitClassBreakdown: Map<string, int>
          economy: EconomyStats
          kills: int
          losses: int }

    type Unit =
        { id: uint32
          classId: string
          ownerPlayerId: int
          pos: Vec2 }

    type Building =
        { id: uint32
          classId: string
          ownerPlayerId: int
          pos: Vec2 }

    /// HighBar `MapFeature` projected into the broker's snapshot
    /// (data-model §1, research §2). Features are reclaim points, not
    /// buildings; live alongside the broker's existing entity lists.
    type Feature =
        { id: uint32
          kind: string
          pos: Vec2 }

    type GameStateSnapshot =
        { sessionId: Guid
          tick: int64
          capturedAt: DateTimeOffset
          players: PlayerTelemetry list
          units: Unit list
          buildings: Building list
          features: Feature list
          mapMeta: MapMeta option }

    /// True iff `next.tick > prev.tick` and they share `sessionId`.
    val isStrictlyAfter : prev:GameStateSnapshot -> next:GameStateSnapshot -> bool

    /// Apply a transformation to `mapMeta` only when this is the first
    /// snapshot for the session (i.e. `prev` is None). Used to enforce
    /// the "mapMeta on first only" invariant (FR-006, Invariant 5).
    val mapMetaOnFirstOnly :
        prev:GameStateSnapshot option
        -> next:GameStateSnapshot
        -> GameStateSnapshot
