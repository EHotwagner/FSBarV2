namespace Broker.Protocol

open System

module VersionHandshake =

    let check (broker: Version) (peer: Version) : Result<unit, Version> =
        // FR-029: strict major-version match. Minor skew is tolerated in
        // either direction. On reject we echo the broker's own version so
        // the wire layer can populate `Reject.broker_version`.
        if broker.Major = peer.Major then Ok ()
        else Error broker
