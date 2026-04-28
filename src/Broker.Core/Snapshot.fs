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

    let isStrictlyAfter (prev: GameStateSnapshot) (next: GameStateSnapshot) : bool =
        prev.sessionId = next.sessionId && next.tick > prev.tick

    let mapMetaOnFirstOnly
        (prev: GameStateSnapshot option)
        (next: GameStateSnapshot)
        : GameStateSnapshot =
        match prev with
        | None -> next
        | Some _ -> { next with mapMeta = None }
