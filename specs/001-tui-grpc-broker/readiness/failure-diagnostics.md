# Failure Diagnostics — 001-tui-grpc-broker

**Recorded**: 2026-04-27 (T015)

This document records how the broker behaves when it cannot do what the
operator asked, and the wire-level shape of each failure. It is the
reference test surfaces (T016–T021, T030–T032, T043) check against, and
the contract the eventual implementation must keep.

The covered failure modes are:

1. Headless host invokes the 2D visualization (FR-025, SC-008).
2. Host-mode launch with a missing or incompatible game executable (FR-013, FR-014, edge case in spec line 104).
3. Proxy-AI handshake never completes inside the timeout (FR-026, edge case in spec line 96).
4. Major-version mismatch on either gRPC service (FR-029).

For every mode, the rule is **safe failure (Constitution VI)**: surface
the cause to the operator, do not corrupt session state, and do not
silently swallow.

---

## 1. Headless host — viz unavailable

**Trigger.** Operator presses `V` (toggle viz) or starts the broker with
`V` set to default-on, and `Broker.Viz.VizHost.probe ()` returns
`Error reason`.

**Probe inputs.** The Silk.NET / GLFW initialization fails because there
is no graphical environment (no `DISPLAY`, no Wayland socket, no Win32
window class registry, no usable GPU). The implementation may read
`Environment.GetEnvironmentVariable "DISPLAY"`, `WAYLAND_DISPLAY`, etc.,
plus an opportunistic `Silk.NET.Windowing.Window.PrioritizeXxx` probe.

**Operator-visible behaviour.**

- The dashboard footer line shows: `2D visualization unavailable: <reason>`
  where `<reason>` is the string returned by `probe`.
- Pressing `V` again retries the probe (so plugging in a display + reattaching
  works without restart).
- The rest of the broker continues unaffected (FR-025, SC-008): gRPC
  server keeps listening, scripting clients stay connected, dashboard
  keeps refreshing.

**CLI escape hatch.** `--no-viz` skips the probe entirely, so a
deliberately-headless deployment never displays the unavailable banner.

**Audit.** No audit event is written for a viz probe failure; this is an
operator-facing UI concern, not a session-lifecycle event. (The audit log
would otherwise rotate noise on every failed probe.)

---

## 2. Missing or incompatible game executable

**Trigger.** Operator confirms a host-mode launch (`Enter` from `LobbyView`),
but the configured HighBarV3 binary is not present, not executable, or
returns a non-zero exit before the proxy AI attaches.

**Diagnostic chain.**

1. `Broker.Core.Lobby.validate` returns `Ok config`. (Validation is purely
   structural — see FR-013.)
2. `Broker.App` (via `Broker.Core.Session.newHostSession`) transitions
   `SessionState.Configuring → Launching`.
3. `Broker.App.Program` invokes the OS `Process.Start` on the HighBarV3
   binary. Any of the following conditions are surfaced as a single
   `Ended(GameCrashed)` transition:
   - `FileNotFoundException` from `Process.Start`.
   - Process exits within 5 seconds without ever connecting via the
     `ProxyLink` service.
   - `ProxyLink.Attach` opens but the first `Handshake` message is
     malformed.

**Operator-visible behaviour.**

- The lobby launch fails fast — no spinner past the 5-second window.
- Dashboard switches to `Idle` with a banner: `Host-mode launch failed: <detail>`
  where `<detail>` is one of `executable not found at <path>`,
  `executable exited (code <n>) before proxy attached`, or
  `proxy handshake malformed: <message>`.
- Operator may immediately reopen the lobby (`L`) and retry.

**Audit.** A single `SessionEnded(at, sessionId, GameCrashed)` event is
written; the `detail` field carries the full diagnostic chain string.

---

## 3. Proxy-AI handshake timeout

**Trigger.** A peer dials the `ProxyLink` service but does not send a
recognisable `ProxyClientMsg.handshake` within
`Options.keepaliveIntervalMs * 2` (default 4 s).

**Wire behaviour.** The broker closes the bidi stream with a
`ProxyServerMsg.reject` carrying:

```
Reject {
  code = CODE_INVALID_PAYLOAD,
  detail = "handshake_timeout: no Handshake received within 4000 ms"
}
```

…then closes the underlying HTTP/2 stream cleanly.

**Operator-visible behaviour.**

- Dashboard remains `Idle` (no session was ever opened).
- The timestamped event is written to the audit log (FR-028) so
  post-session diagnosis can identify a misconfigured proxy.
- Subsequent reconnect attempts from the same or another peer are
  accepted — the timeout does not blacklist the address.

**Audit.** `ProxyDetached(at, "handshake_timeout: no Handshake received within Nms")`
is written by the `ProxyLink` service host.

---

## 4. Protocol-version mismatch (FR-029)

**Trigger.** A peer sends a `Handshake` (proxy) or a `HelloRequest`
(scripting client) whose `ProtocolVersion.major` differs from the broker's
`BrokerInfo.version.major`.

**Wire behaviour.**

- `ProxyLink.Attach` → broker writes one `ProxyServerMsg.reject` carrying
  `Reject { code = CODE_VERSION_MISMATCH, broker_version = <broker.major.minor> }`
  and closes the stream.
- `ScriptingClient.Hello` → broker returns RPC status `FailedPrecondition`
  with the same `Reject` payload as the response body's reject field.
  No `HelloReply` is sent.

**Operator-visible behaviour.**

- Dashboard is unaffected (no client was admitted).
- Broker version badge in the dashboard footer shows `vMAJOR.MINOR` so the
  operator can compare it against what the rejected peer logged.
- Audit: `VersionMismatchRejected(at, peerKind, peerVersion)` where
  `peerKind` is one of `"proxy"` or `"scripting-client"`.

**Tolerance.** Minor-version skew is allowed in either direction. The
broker SHOULD ignore unknown minor-version-added fields; peers SHOULD do
the same. This is enforced at the codegen edge (proto3 ignores unknown
fields) and not at the application layer.

---

## Test obligations

The implementations of these failure modes are exercised by the following
tests / readiness artifacts:

| Mode | Test / artifact |
|------|-----------------|
| 1 — headless viz       | T043 (`Broker.Viz.VizHost.probe` Expecto), T046 (headless `--no-viz` smoke) |
| 2 — missing game exe   | T035 (`Broker.App` process management Expecto), T037 (US2 walkthrough) |
| 3 — handshake timeout  | T021 (in-memory gRPC Expecto), T029b (recovery measurement) |
| 4 — version mismatch   | T016 (`VersionHandshake.check` matrix), T021 (gRPC end-to-end), T032 (admin-elevation flow) |
