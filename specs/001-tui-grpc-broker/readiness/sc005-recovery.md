# SC-005 Disconnect-recovery evidence (T029b)

**Recorded**: 2026-04-27
**Driver**: `tests/Broker.Integration.Tests/Sc005RecoveryTests.fs`
**Fixture**: synthetic-proxy (`SyntheticProxy.connect` + `DropAsync`).
**Target**: SC-005 — broker detects disconnect ≤ 5 s, recovers to idle
≤ 10 s in ≥ 95 % of disconnect events (spec line 183).

## Method

20 trials, sharing one broker (so the close + reattach path is exercised
end-to-end repeatedly). Each trial:

1. Hello + SubscribeState a watcher (`watcher-N`).
2. Attach a synthetic proxy (`proxy-N`); push one snapshot so the
   session is fully `Active`.
3. Drain the initial snapshot off the watcher's stream (so the next
   `MoveNext` lands on the SessionEnd or stream-close).
4. **Drop the proxy** (`Driver.DropAsync()` → `RequestStream.CompleteAsync`).
   Stopwatch starts here.
5. **Detection**: stop the clock when the watcher's `MoveNext` returns
   false OR yields a `SessionEnd` body.
6. **Recovery**: stop the clock when `BrokerState.proxyOutbound hub = None`
   (broker is back to idle, ready for the next session).
7. Unregister the watcher so the next trial's name is free.

## Result

```
[SC-005] trials=20 detect-ok=20/20 (max=10ms) recover-ok=20/20 (max=11ms)
  Passed SC-005 disconnect recovery.Detect+notify ≤ 5 s and recover-to-idle ≤ 10 s in ≥ 95% of 20 trials [677 ms]
```

- **detect-ok**: 20 / 20 (≥ 19 / 20 required).
- **detect max**: 10 ms vs the 5 000 ms budget — **500× inside SC-005**.
- **recover-ok**: 20 / 20.
- **recover max**: 11 ms vs the 10 000 ms budget.

## Bug found and fixed during T029b authoring

The first run of this test surfaced a real bug in `ProxyLinkService.fs`:
when the proxy stream closed, the outbound-command drain task hung on
`Channel.Reader.WaitToReadAsync` because nothing completed the channel
writer. `do! drainTask` then blocked forever, so `closeSession` never
ran and the broker stayed in `Guest` mode with `proxyOutbound = Some`.

Fix: complete the proxy outbound channel writer immediately after the
read loop exits. With that one line, detection drops from "8024 ms,
0 / 20 trials" to "10 ms, 20 / 20 trials". Tests-first earned its keep.

## Status

**Real evidence on the wire path.** Same caveat as SC-003: synthetic-
proxy is a stand-in for HighBarV3's proxy AI workstream, but the broker-
side detection + recovery logic exercised here is production code.
