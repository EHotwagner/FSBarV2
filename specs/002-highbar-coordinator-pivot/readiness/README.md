# Readiness — 002-highbar-coordinator-pivot

This directory holds the **evidence artifacts** captured while implementing
the Broker–HighBarCoordinator wire pivot. Each artifact backs a task in
`tasks.md` and is referenced from the synthetic-evidence inventory or from
a User-Story closure scenario.

## Artifact set

| File | Owner task | Purpose |
|------|------------|---------|
| `feature-baseline.md` | T004 | Tier classification, affected layers, public-API impact, evidence obligations. |
| `fsi-session.txt` | T010 / T048 | FSI exercise transcript per `contracts/public-fsi.md` §"FSI exercise sketch" — proves the public surface is reachable from the packed library. |
| `failure-diagnostics.md` | T012 | Schema mismatch / non-owner / heartbeat timeout / admin-not-mappable / state-gap surfaces. |
| `us1-synthetic.md` | T023 | CI-fixture end-to-end transcript (cold-start + state ingest + graceful disconnect via `SyntheticCoordinator`). Closes the broker-side wire path under loopback; real-game closure lives under `specs/001-tui-grpc-broker/readiness/`. |
| `sc002-synthetic-latency.md` | T024 | Latency budget under `SyntheticCoordinator` over ≥500 ticks — re-anchors SC-002 on CI. |
| `sc003-synthetic-recovery.md` | T025 | Disconnect-recovery budget under `SyntheticCoordinator` over ≥20 trials — re-anchors SC-003 on CI. |
| `us2-synthetic.md` | T032 | CI-fixture transcript for command egress (operator Pause + scripting-client Move). |
| `us4-build-clean.txt` | T043 | `dotnet build` transcript confirming no `ProxyClientMsg` / `ProxyServerMsg` / `ProxyLinkService` references remain. |
| `us4-evidence.md` | T045 | SurfaceArea diff transcript: ProxyLink baselines removed, HighBarCoordinatorService baseline added, ScriptingClient byte-identical. |
| `task-graph.md` / `task-graph.json` | T049 | Computed by `run-audit.sh --graph-only` — phase ordering, `[S*]` propagation, dangling-ref check. |
| `synthetic-evidence.json` | T050 | Audit verdict + per-`[S]` justification. |

## Real-game evidence

The real-game walkthroughs (T033–T037, T053) regenerate artifacts under
**`specs/001-tui-grpc-broker/readiness/`** rather than this directory.
That is by design: those captures *close* 001's open carve-outs (T029,
T037, T042, T046), so the artifact paths must match what 001's
inventory points at.

## Inventory cross-reference

The `[S]` markers in `tasks.md` map directly to the
Synthetic-Evidence Inventory at the bottom of `tasks.md`. Each entry in
that table cites a real-evidence path under `readiness/` here OR under
`specs/001-tui-grpc-broker/readiness/`.
