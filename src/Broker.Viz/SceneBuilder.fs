namespace Broker.Viz

open System
open SkiaSharp
open SkiaViewer
open Broker.Core

module SceneBuilder =

    type PlayerColor = { r: byte; g: byte; b: byte }

    type SceneEntity =
        { id: uint32
          ownerPlayerId: int
          pos: Snapshot.Vec2
          isBuilding: bool }

    type SceneSummary =
        { snapshotTick: int64
          sessionId: Guid
          mapName: string option
          mapOutlineByteCount: int
          mapSize: Snapshot.Vec2 option
          entities: SceneEntity list
          ownership: Map<int, PlayerColor> }

    type Scene =
        { summary: SceneSummary
          skScene: SkiaViewer.Scene }

    let private palette : PlayerColor[] =
        // 8-entry deterministic palette. Allied teams share their team's
        // colour, so two human players on team 1 are both blue.
        [| { r = 220uy; g =  70uy; b =  70uy }   // 0 red
           { r =  70uy; g = 130uy; b = 240uy }   // 1 blue
           { r =  80uy; g = 200uy; b =  90uy }   // 2 green
           { r = 230uy; g = 220uy; b =  80uy }   // 3 yellow
           { r = 200uy; g =  90uy; b = 200uy }   // 4 purple
           { r = 240uy; g = 160uy; b =  60uy }   // 5 orange
           { r =  80uy; g = 220uy; b = 220uy }   // 6 cyan
           { r = 200uy; g = 200uy; b = 200uy } |] // 7 grey

    let playerColor (playerId: int) (teamId: int) : PlayerColor =
        ignore playerId
        let n = palette.Length
        let idx = ((teamId % n) + n) % n
        palette.[idx]

    let private toSkColor (c: PlayerColor) : SKColor =
        SKColor(c.r, c.g, c.b, 255uy)

    let private bgColor : SKColor = SKColor(15uy, 18uy, 24uy, 255uy)
    let private mapStrokeColor : SKColor = SKColor(140uy, 140uy, 160uy, 255uy)

    let private buildEntities (snap: Snapshot.GameStateSnapshot) : SceneEntity list =
        let unitEntities =
            snap.units
            |> List.map (fun u ->
                { id = u.id
                  ownerPlayerId = u.ownerPlayerId
                  pos = u.pos
                  isBuilding = false })
        let buildingEntities =
            snap.buildings
            |> List.map (fun b ->
                { id = b.id
                  ownerPlayerId = b.ownerPlayerId
                  pos = b.pos
                  isBuilding = true })
        unitEntities @ buildingEntities

    let private buildOwnership (snap: Snapshot.GameStateSnapshot) : Map<int, PlayerColor> =
        snap.players
        |> List.map (fun p -> p.playerId, playerColor p.playerId p.teamId)
        |> Map.ofList

    let private buildSkScene
        (entities: SceneEntity list)
        (ownership: Map<int, PlayerColor>)
        (mapMeta: Snapshot.MapMeta option)
        : SkiaViewer.Scene =
        let mapElements =
            match mapMeta with
            | None -> []
            | Some m ->
                let stroke = SkiaViewer.Scene.stroke mapStrokeColor 2.0f
                [ SkiaViewer.Scene.rect 0.0f 0.0f m.size.x m.size.y stroke ]
        let entityElements =
            entities
            |> List.map (fun e ->
                let color =
                    match Map.tryFind e.ownerPlayerId ownership with
                    | Some c -> toSkColor c
                    | None   -> SKColor(180uy, 180uy, 180uy, 255uy)
                let paint = SkiaViewer.Scene.fill color
                if e.isBuilding then
                    SkiaViewer.Scene.rect (e.pos.x - 4.0f) (e.pos.y - 4.0f) 8.0f 8.0f paint
                else
                    SkiaViewer.Scene.circle e.pos.x e.pos.y 3.0f paint)
        SkiaViewer.Scene.create bgColor (mapElements @ entityElements)

    let build (snapshot: Snapshot.GameStateSnapshot) : Scene =
        let entities = buildEntities snapshot
        let ownership = buildOwnership snapshot
        let summary =
            { snapshotTick = snapshot.tick
              sessionId = snapshot.sessionId
              mapName = snapshot.mapMeta |> Option.map (fun m -> m.mapName)
              mapOutlineByteCount =
                  snapshot.mapMeta
                  |> Option.map (fun m -> m.outline.Length)
                  |> Option.defaultValue 0
              mapSize = snapshot.mapMeta |> Option.map (fun m -> m.size)
              entities = entities
              ownership = ownership }
        let sk = buildSkScene entities ownership snapshot.mapMeta
        { summary = summary; skScene = sk }

    let empty : Scene =
        let summary =
            { snapshotTick = 0L
              sessionId = Guid.Empty
              mapName = None
              mapOutlineByteCount = 0
              mapSize = None
              entities = []
              ownership = Map.empty }
        { summary = summary
          skScene = SkiaViewer.Scene.empty bgColor }

    let summary (scene: Scene) : SceneSummary = scene.summary

    let toSkiaScene (scene: Scene) : SkiaViewer.Scene = scene.skScene
