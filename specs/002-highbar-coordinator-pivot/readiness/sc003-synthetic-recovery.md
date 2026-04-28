# SC-003 disconnect recovery under SyntheticCoordinator (T025)

**Date**: 2026-04-28 12:55:07Z
**Trials**: 20
**Detection ≤ 5 s**: 20 / 20 (100%)
**Recovery-to-Idle ≤ 10 s**: 20 / 20 (100%)
**Max detection**: 50.9 ms
**Max recovery**: 50.9 ms
**Verdict**: PASS

The broker-side wire path is real Kestrel + `HighBarCoordinatorService`
+ `BrokerState.closeSession` + per-attach watchdog (`heartbeatTimeoutMs`
default 5 s). The plugin peer is the loopback `SyntheticCoordinator`;
real-wire closure of SC-003 against a real BAR + HighBarV3 build is
owned by T035.
