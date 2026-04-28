module Broker.Protocol.Tests.VersionHandshakeTests

open System
open Expecto
open Broker.Protocol

let private v major minor = System.Version(major, minor)

[<Tests>]
let versionHandshakeTests =
    testList "VersionHandshake" [
        test "check_strict major match returns Ok" {
            let r = VersionHandshake.check (v 1 0) (v 1 0)
            Expect.equal r (Ok ()) "1.0 vs 1.0 must accept"
        }

        test "check_minor skew up is tolerated" {
            // FR-029: minor-version skew is allowed. A peer that is one
            // minor ahead must still be accepted (forward-compat).
            let r = VersionHandshake.check (v 1 0) (v 1 5)
            Expect.equal r (Ok ()) "broker 1.0 accepts peer 1.5"
        }

        test "check_minor skew down is tolerated" {
            // Peer one minor behind also accepted (backward-compat).
            let r = VersionHandshake.check (v 2 7) (v 2 1)
            Expect.equal r (Ok ()) "broker 2.7 accepts peer 2.1"
        }

        test "check_major skew up rejected with broker version echoed" {
            let r = VersionHandshake.check (v 1 4) (v 2 0)
            match r with
            | Error broker ->
                Expect.equal broker.Major 1 "Error payload carries broker.major"
                Expect.equal broker.Minor 4 "Error payload carries broker.minor"
            | Ok _ -> failtestf "expected major mismatch to reject"
        }

        test "check_major skew down rejected with broker version echoed" {
            let r = VersionHandshake.check (v 2 0) (v 1 9)
            match r with
            | Error broker ->
                Expect.equal broker.Major 2 "Error carries broker.major (the value the wire echoes)"
            | Ok _ -> failtestf "expected major mismatch to reject"
        }
    ]
