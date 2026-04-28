module Broker.Core.Tests.SnapshotTests

open System
open Expecto
open Broker.Core
open Broker.Core.Snapshot

let private now () = DateTimeOffset.UtcNow
let private session = Guid.NewGuid()

let private mk (sid: Guid) (tick: int64) (mapMeta: MapMeta option) : GameStateSnapshot =
    { sessionId = sid
      tick = tick
      capturedAt = now()
      players = []
      units = []
      buildings = []
      mapMeta = mapMeta }

let private mapMeta : MapMeta =
    { mapName = "TestMap"
      size = { x = 1024.0f; y = 1024.0f }
      outline = [||] }

[<Tests>]
let snapshotTests =
    testList "Snapshot" [
        test "isStrictlyAfter_higher_tick_same_session_is_true" {
            let prev = mk session 1L None
            let next = mk session 2L None
            Expect.isTrue (isStrictlyAfter prev next) "tick 2 > tick 1"
        }

        test "isStrictlyAfter_equal_tick_is_false" {
            let s = mk session 5L None
            Expect.isFalse (isStrictlyAfter s s) "tick equality breaks monotonicity"
        }

        test "isStrictlyAfter_lower_tick_is_false" {
            let prev = mk session 5L None
            let next = mk session 4L None
            Expect.isFalse (isStrictlyAfter prev next) "tick 4 < tick 5 violates monotonicity"
        }

        test "isStrictlyAfter_different_session_is_false" {
            // Cross-session comparisons are meaningless; the helper must
            // refuse them rather than silently accepting (FR-006).
            let prev = mk (Guid.NewGuid()) 1L None
            let next = mk (Guid.NewGuid()) 99L None
            Expect.isFalse (isStrictlyAfter prev next) "different session ids never compare strictly-after"
        }

        test "mapMetaOnFirstOnly_first_snapshot_keeps_mapMeta" {
            // Invariant 5: mapMeta is present on the first snapshot of a
            // session. The first snapshot has no `prev`.
            let next = mk session 1L (Some mapMeta)
            let result = mapMetaOnFirstOnly None next
            Expect.equal result.mapMeta (Some mapMeta) "first snapshot keeps its mapMeta"
        }

        test "mapMetaOnFirstOnly_subsequent_snapshot_strips_mapMeta" {
            // Subsequent snapshots that incorrectly carry mapMeta have
            // it stripped by this helper before delivery.
            let prev = mk session 1L (Some mapMeta)
            let next = mk session 2L (Some mapMeta)
            let result = mapMetaOnFirstOnly (Some prev) next
            Expect.equal result.mapMeta None "later snapshot has mapMeta stripped"
        }

        test "mapMetaOnFirstOnly_subsequent_snapshot_without_mapMeta_unchanged" {
            let prev = mk session 1L (Some mapMeta)
            let next = mk session 2L None
            let result = mapMetaOnFirstOnly (Some prev) next
            Expect.equal result.mapMeta None "no mapMeta to begin with → no mapMeta after"
        }
    ]
