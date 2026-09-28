module Broker.Contracts.Tests.CommonProtoTests

open Expecto
open Google.Protobuf
open FSBarV2.Broker.Contracts

let private pos x y =
    let value = Vec2.empty()
    value.X <- x
    value.Y <- y
    value

[<Tests>]
let commonProtoTests =
    testList "common.proto feature compatibility" [
        test "feature tag 8 survives protobuf encode and decode beside unit id 7" {
            Expect.equal GameStateSnapshot.FeaturesFieldNumber 8 "features use the next unused snapshot tag"

            let snapshot = GameStateSnapshot.empty()
            let unit = Unit.empty()
            unit.Id <- 7u
            unit.ClassId <- "303"
            unit.Pos <- ValueSome (pos 3.0f -5.0f)
            snapshot.Units.Add(unit)

            let first = Feature.empty()
            first.Id <- 7u
            first.Kind <- "101"
            first.Pos <- ValueSome (pos 11.0f -13.0f)
            snapshot.Features.Add(first)

            let second = Feature.empty()
            second.Id <- 19u
            second.Kind <- "202"
            second.Pos <- ValueSome (pos -23.0f 29.0f)
            snapshot.Features.Add(second)

            let decoded = GameStateSnapshot.Parser.ParseFrom(snapshot.ToByteArray())
            Expect.equal decoded.Units.Count 1 "unit collection round-trips"
            Expect.equal decoded.Units.[0].Id 7u "unit id 7 remains a unit"
            Expect.equal decoded.Features.Count 2 "feature collection round-trips"
            Expect.equal decoded.Features.[0].Id 7u "feature id 7 remains independent"
            Expect.equal decoded.Features.[0].Kind "101" "first kind round-trips"
            Expect.equal decoded.Features.[0].Pos.Value.X 11.0f "first X round-trips"
            Expect.equal decoded.Features.[0].Pos.Value.Y -13.0f "first Y round-trips"
            Expect.equal decoded.Features.[1].Id 19u "second id round-trips"
            Expect.equal decoded.Features.[1].Kind "202" "second kind round-trips"
            Expect.equal decoded.Features.[1].Pos.Value.X -23.0f "second X round-trips"
            Expect.equal decoded.Features.[1].Pos.Value.Y 29.0f "second Y round-trips"
        }
    ]
