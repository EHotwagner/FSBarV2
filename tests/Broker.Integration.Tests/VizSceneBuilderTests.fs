module Broker.Integration.Tests.VizSceneBuilderTests

open System
open Expecto
open Broker.Core
open Broker.Core.Snapshot
open Broker.Viz

let private session = Guid.Parse "7f3c0b7e-9a25-4f3a-91a2-12345678abcd"

let private mkPlayer (id: int) (teamId: int) (name: string) : PlayerTelemetry =
    { playerId = id
      teamId = teamId
      name = name
      resources = { metal = 0.0; energy = 0.0 }
      unitCount = 0
      buildingCount = 0
      unitClassBreakdown = Map.empty
      economy = { income = { metal = 0.0; energy = 0.0 }; expenditure = { metal = 0.0; energy = 0.0 } }
      kills = 0
      losses = 0 }

let private mkUnit (id: uint32) (owner: int) (x: float32) (y: float32) : Snapshot.Unit =
    { id = id; classId = "scout"; ownerPlayerId = owner; pos = { x = x; y = y } }

let private mkBuilding (id: uint32) (owner: int) (x: float32) (y: float32) : Snapshot.Building =
    { id = id; classId = "factory"; ownerPlayerId = owner; pos = { x = x; y = y } }

let private mapMeta : MapMeta =
    { mapName = "Tabula"
      size = { x = 1024.0f; y = 768.0f }
      outline = [| 0x00uy; 0x01uy; 0x02uy; 0x03uy |] }

[<Tests>]
let sceneBuilderTests =
    testList "Viz.SceneBuilder" [

        test "playerColor_palette_is_deterministic_by_team" {
            // Two players on the same team should share a colour, and the
            // colour should be drawn from a fixed palette so the renderer
            // and tests agree (FR-023).
            let p0t1 = SceneBuilder.playerColor 0 1
            let p7t1 = SceneBuilder.playerColor 7 1
            let p0t2 = SceneBuilder.playerColor 0 2
            Expect.equal p0t1 p7t1 "team membership decides colour, not player id"
            Expect.notEqual p0t1 p0t2 "different teams get different colours"
        }

        test "playerColor_handles_negative_and_overflow_team_ids" {
            // The palette wraps modularly so unusual team ids don't crash
            // the renderer. Asserting that two arbitrarily large /
            // negative team ids both resolve to a palette entry.
            let n = SceneBuilder.playerColor 1 -3
            let p = SceneBuilder.playerColor 1 13
            // Compare to a palette entry (team 5) to confirm modular wrap.
            Expect.equal n (SceneBuilder.playerColor 0 5) "team -3 wraps to team 5"
            Expect.equal p (SceneBuilder.playerColor 0 5) "team 13 wraps to team 5"
        }

        test "empty_scene_has_no_entities_and_no_map" {
            let s = SceneBuilder.summary SceneBuilder.empty
            Expect.equal s.snapshotTick 0L "empty has tick 0"
            Expect.equal s.sessionId Guid.Empty "empty has no session"
            Expect.isNone s.mapName "empty has no map"
            Expect.isEmpty s.entities "empty has no entities"
            Expect.isEmpty s.ownership "empty has no ownership"
        }

        test "build_passes_through_tick_and_sessionId" {
            let snap : GameStateSnapshot =
                { sessionId = session
                  tick = 42L
                  capturedAt = DateTimeOffset.UtcNow
                  players = []
                  units = []
                  buildings = []
                  features = []
                  mapMeta = None }
            let s = SceneBuilder.summary (SceneBuilder.build snap)
            Expect.equal s.snapshotTick 42L "tick threaded through"
            Expect.equal s.sessionId session "session id threaded through"
        }

        test "build_unit_positions_appear_as_non-building_entities" {
            let snap : GameStateSnapshot =
                { sessionId = session
                  tick = 1L
                  capturedAt = DateTimeOffset.UtcNow
                  players = [ mkPlayer 0 0 "alice"; mkPlayer 1 1 "bob" ]
                  units =
                    [ mkUnit 1u 0 100.0f 50.0f
                      mkUnit 2u 1 200.0f 80.0f ]
                  buildings = []
                  features = []
                  mapMeta = None }
            let s = SceneBuilder.summary (SceneBuilder.build snap)
            let units =
                s.entities
                |> List.filter (fun e -> not e.isBuilding)
                |> List.sortBy (fun e -> e.id)
            match units with
            | [ u0; u1 ] ->
                Expect.equal u0.id 1u "first unit id"
                Expect.equal u0.ownerPlayerId 0 "first unit owner"
                Expect.equal u0.pos.x 100.0f "first unit x"
                Expect.equal u0.pos.y 50.0f "first unit y"
                Expect.equal u1.id 2u "second unit id"
                Expect.equal u1.ownerPlayerId 1 "second unit owner"
            | other ->
                failtestf "expected exactly two units, got %d" (List.length other)
        }

        test "build_buildings_are_distinguished_from_units" {
            let snap : GameStateSnapshot =
                { sessionId = session
                  tick = 1L
                  capturedAt = DateTimeOffset.UtcNow
                  players = [ mkPlayer 0 0 "alice" ]
                  units = [ mkUnit 1u 0 10.0f 10.0f ]
                  buildings = [ mkBuilding 100u 0 50.0f 75.0f ]
                  features = []
                  mapMeta = None }
            let s = SceneBuilder.summary (SceneBuilder.build snap)
            let bs = s.entities |> List.filter (fun e -> e.isBuilding)
            let us = s.entities |> List.filter (fun e -> not e.isBuilding)
            Expect.equal (List.length us) 1 "one unit"
            match bs with
            | [ b ] ->
                Expect.equal b.id 100u "building id preserved"
                Expect.equal b.pos.x 50.0f "building x preserved"
                Expect.equal b.pos.y 75.0f "building y preserved"
            | other ->
                failtestf "expected exactly one building, got %d" (List.length other)
        }

        test "build_ownership_uses_team_palette_per_player" {
            // Three players on two teams: ownership map should agree with
            // playerColor.
            let snap : GameStateSnapshot =
                { sessionId = session
                  tick = 1L
                  capturedAt = DateTimeOffset.UtcNow
                  players =
                    [ mkPlayer 1 0 "alice"
                      mkPlayer 2 1 "bob"
                      mkPlayer 3 0 "carol" ]
                  units = []
                  buildings = []
                  features = []
                  mapMeta = None }
            let s = SceneBuilder.summary (SceneBuilder.build snap)
            Expect.equal (Map.count s.ownership) 3 "one entry per player"
            Expect.equal s.ownership.[1] (SceneBuilder.playerColor 1 0) "alice colour"
            Expect.equal s.ownership.[2] (SceneBuilder.playerColor 2 1) "bob colour"
            Expect.equal s.ownership.[3] (SceneBuilder.playerColor 3 0) "carol colour"
            Expect.equal s.ownership.[1] s.ownership.[3] "alice and carol share team 0 colour"
            Expect.notEqual s.ownership.[1] s.ownership.[2] "alice and bob differ across teams"
        }

        test "build_with_mapMeta_records_outline_byte_count_and_name" {
            let snap : GameStateSnapshot =
                { sessionId = session
                  tick = 1L
                  capturedAt = DateTimeOffset.UtcNow
                  players = []
                  units = []
                  buildings = []
                  features = []
                  mapMeta = Some mapMeta }
            let s = SceneBuilder.summary (SceneBuilder.build snap)
            Expect.equal s.mapName (Some "Tabula") "map name preserved"
            Expect.equal s.mapOutlineByteCount 4 "outline byte count preserved"
            Expect.equal s.mapSize (Some mapMeta.size) "map size preserved"
        }

        test "build_without_mapMeta_yields_zero_outline_bytes_and_no_name" {
            let snap : GameStateSnapshot =
                { sessionId = session
                  tick = 1L
                  capturedAt = DateTimeOffset.UtcNow
                  players = []
                  units = []
                  buildings = []
                  features = []
                  mapMeta = None }
            let s = SceneBuilder.summary (SceneBuilder.build snap)
            Expect.isNone s.mapName "no map name"
            Expect.equal s.mapOutlineByteCount 0 "no outline bytes"
            Expect.isNone s.mapSize "no map size"
        }

        test "toSkiaScene_produces_one_element_per_entity_plus_map_outline" {
            // White-box check that the SkiaViewer scene actually receives
            // the entities + map outline; if this stops being true, the
            // viewer would silently render nothing on a populated tick.
            let snap : GameStateSnapshot =
                { sessionId = session
                  tick = 1L
                  capturedAt = DateTimeOffset.UtcNow
                  players = [ mkPlayer 0 0 "alice" ]
                  units = [ mkUnit 1u 0 10.0f 10.0f; mkUnit 2u 0 20.0f 20.0f ]
                  buildings = [ mkBuilding 9u 0 30.0f 30.0f ]
                  features = []
                  mapMeta = Some mapMeta }
            let scene = SceneBuilder.build snap
            let sk = SceneBuilder.toSkiaScene scene
            // 3 entities + 1 map outline = 4 elements.
            Expect.equal (List.length sk.Elements) 4 "every entity + outline drawn"
        }

        test "toSkiaScene_empty_has_no_elements" {
            let sk = SceneBuilder.toSkiaScene SceneBuilder.empty
            Expect.equal (List.length sk.Elements) 0 "empty scene has no draw calls"
        }
    ]
