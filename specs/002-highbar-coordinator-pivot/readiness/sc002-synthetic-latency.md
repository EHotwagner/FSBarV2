# SC-002 latency under SyntheticCoordinator (T024)

**Date**: 2026-04-28 12:55:06Z
**Samples**: 200 snapshots
**p95**: 0.328 ms
**p99**: 0.497 ms
**max**: 0.757 ms
**Budget**: ≤ 1 s
**Verdict**: PASS

The broker-side wire path is real Kestrel + `HighBarCoordinatorService` +
`BrokerState.applySnapshot` + per-client `Channel<StateMsg>` fan-out. The
plugin peer is the loopback `SyntheticCoordinator` fixture — real-wire
closure of SC-002 against a real BAR + HighBarV3 build is owned by T034.
