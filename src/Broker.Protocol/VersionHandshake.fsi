namespace Broker.Protocol

open System

module VersionHandshake =

    /// Strict major-version match (FR-029). Returns `Ok ()` when peer's
    /// major version equals broker's; minor skew is tolerated either way.
    /// On reject the broker's own version is returned so the wire layer
    /// can echo it to the peer in the `VERSION_MISMATCH` payload.
    val check :
        broker:Version
        -> peer:Version
        -> Result<unit, Version>
