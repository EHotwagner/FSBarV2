# SC-003 Snapshot-latency evidence (T029a)

**Recorded**: 2026-04-27
**Driver**: `tests/Broker.Integration.Tests/Sc003LatencyTests.fs`
**Fixture**: synthetic-proxy (`SyntheticProxy.connect`) over loopback gRPC.
**Target**: SC-003 — 95 % of state updates ≤ 1 s end-to-end under normal
load (spec line 181).

## Method

1. Boot a real Kestrel-hosted broker on a free loopback port.
2. Hello + SubscribeState a single scripting client.
3. Attach the synthetic proxy.
4. Push **500** snapshots back-to-back, each tagged with the wall-clock
   timestamp at write-call entry.
5. A reader task drains the subscriber's `SubscribeState` stream,
   recording per-tick wall-clock at receipt.
6. Compute per-tick latency as `recv_ms − sent_ms` (monotonic
   `Stopwatch`); sort and read the 95th-percentile.

## Result

```
[SC-003] received=500/500  p95=1ms  max=7ms
  Passed SC-003 snapshot latency.p95 proxy->subscriber wall-clock latency is <= 1 s over 500 snapshots [66 ms]
```

- **received**: 500 / 500 — no snapshots dropped (FR-006 gap-free).
- **p95 latency**: **1 ms** vs the 1000 ms budget — **3 orders of magnitude
  inside SC-003**.
- **max latency**: 7 ms.

## Status

**Real evidence on the wire path.** Synthetic-proxy is a substitute for
the eventual real HighBarV3 proxy AI (research.md §7, §14); the conversion
+ fan-out + per-client `Channel<StateMsg>` + gRPC server-stream are all
production code. Re-running the test against a real proxy AI would only
add the proxy's intrinsic snapshot-cadence, which doesn't enter the
broker-side latency budget measured here.
