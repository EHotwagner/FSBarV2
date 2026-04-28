# Readiness Artifacts — 001-tui-grpc-broker

This directory holds the evidence artifacts produced while implementing the
TUI gRPC Game Broker feature. Per Constitution Principles I, IV, and V,
every non-trivial change must leave behind a reproducible artifact that
shows the public surface was actually exercised.

## Artifact set

| File / pattern                        | Origin task | Purpose |
|---------------------------------------|-------------|---------|
| `feature-baseline.md`                 | T006        | Tier, affected layers, public-surface impact, evidence obligations. |
| `fsi-session.txt`                     | T013        | Captured FSI transcript loading the packed `Broker.Core` library. |
| `failure-diagnostics.md`              | T015        | Headless viz, missing game exe, proxy timeout, version mismatch wire format. |
| `us1-evidence.md`                     | T029        | Quickstart §2 Scenario A — guest-mode bridge end-to-end (synthetic-proxy). |
| `sc003-latency.md`                    | T029a       | p95 snapshot latency under synthetic-proxy fixture (≤ 1 s budget). |
| `sc005-recovery.md`                   | T029b       | Disconnect detect ≤ 5 s, recover ≤ 10 s in ≥ 95 % trials. |
| `us2-evidence.md`                     | T037        | Quickstart §3 Scenario B — host-mode + admin lifecycle. |
| `us3-evidence.md`                     | T042        | Live dashboard under load (≥ 4 clients, ≥ 200 units, ≥ 1 Hz). |
| `us4-evidence.md`                     | T046        | Viz screenshot + headless `--no-viz` graceful degradation. |
| `task-graph.json`, `task-graph.md`    | T049        | `speckit.graph.compute` outputs — propagation + checkpoints. |

## Conventions

- Artifacts are plain UTF-8 markdown / text. Screenshots may be PNG. No
  binary captures other than screenshots.
- Each artifact begins with a one-line provenance header: the task id, the
  date the artifact was produced, and any synthetic-evidence flag.
- When a task is marked `[S]`, its corresponding artifact (or the
  Synthetic-Evidence Inventory entry in `tasks.md`) records the reason
  and the real-evidence path — never silently `[X]`.
