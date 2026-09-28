# BARC-01 foundation source window

Part: **Fable BAR client and custom WASM control — BARC-01**

Unified index: [FS-GG/.github §9.8](https://github.com/FS-GG/.github/blob/main/docs/2026-09-07-154210-fs-gg-unified-development-roadmap.md#98-feature-parts-and-subroadmap-index)

Feature roadmap: [BARC-01](https://github.com/FS-GG/.github/blob/main/docs/2026-09-08-134900-fable-bar-wasm-client-design-roadmap.md#12-feature-roadmap--barc-01)

This is the first bounded source window of BARC-01.1, based on FSBarV2
`bbd3c4beb6009b32d456a921913f96042251dac0`. It repairs the existing broker
boundary only. HighBarV3 native source, the five vendored HighBar protobufs,
live BAR/Recoil qualification, browser/WASM composition, installed adoption,
publication and deployment remain outside this window.

## Window status

- [ ] **BARC-01.1a — ground-coordinate mapping.** Implemented and tested on
  the local routine branch; pending admission and merge. Native `(X,Y,Z)` now
  maps to legacy ground `(X,Z)`. Move, Patrol, Build and positional Attack
  targets map back to `(X,0,Z)`. The legacy model does not retain elevation.
- [ ] **BARC-01.1b — fail-closed state reduction.** Implemented and tested on
  the local routine branch; pending admission and merge. Only a complete
  snapshot establishes a baseline. Sequence gaps, incomplete snapshots and
  nonempty deltas that the broker cannot fully materialize invalidate it.
  Empty deltas and keepalives affect transport progress only; stale or
  duplicate updates cannot regress state; a newer complete snapshot recovers.
- [ ] **BARC-01.1c — subscriber validity propagation.** Implemented and tested
  on the local routine branch; pending admission and merge. The real
  HighBarCoordinatorService → BrokerState → scripting SubscribeState path
  emits additive invalidity metadata, suppresses cached state for late joins,
  blocks scripting command admission while a gap is current, and clears the
  live gap on full-snapshot recovery while retaining audit history.

The boxes stay open until the implementation is merged and read back from the
default branch.

## Evidence boundary

Focused fixtures use asymmetric coordinates and a loopback gRPC
`HighBarCoordinator` client. They cover snapshot → sequence gap → delta →
recovery and snapshot → unapplied nonempty delta → recovery, including a late
subscriber and command refusal during invalidity. This is synthetic protocol
evidence. It does not establish native plugin behavior, a live game session,
BAR content compatibility, browser/WASM behavior or installed operation.

The local protobuf bootstrap pins `grpc-fsharp` 0.2.0 to match
`Grpc-FSharp.Tools` 0.2.0. No dependency or HighBar schema pin is upgraded.
