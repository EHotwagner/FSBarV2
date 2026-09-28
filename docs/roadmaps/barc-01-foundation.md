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

- [x] **BARC-01.1a — ground-coordinate mapping.** Merged by PR #1 and independently read back from protected `main`. Native `(X,Y,Z)` now
  maps to legacy ground `(X,Z)`. Move, Patrol, Build and positional Attack
  targets map back to `(X,0,Z)`. The legacy model does not retain elevation.
- [x] **BARC-01.1b — fail-closed state reduction.** Merged by PR #1 and independently read back from protected `main`. Only a complete
  snapshot establishes a baseline. Sequence gaps, incomplete snapshots and
  nonempty deltas that the broker cannot fully materialize invalidate it.
  Empty deltas and keepalives affect transport progress only; stale or
  duplicate updates cannot regress state; a newer complete snapshot recovers.
- [x] **BARC-01.1c — subscriber validity propagation.** Merged by PR #1 and independently read back from protected `main`. The real
  HighBarCoordinatorService → BrokerState → scripting SubscribeState path
  emits additive invalidity metadata, suppresses cached state for late joins,
  blocks scripting command admission while a gap is current, and clears the
  live gap on full-snapshot recovery while retaining audit history.
- [x] **BARC-01.1d — strict command admission.** Merged by PR #3 as protected `main` `b745e04728ebb6c8bea96667094ff2fa4eba7476`; 41 protocol, 58 core and 5 contract tests passed. Decode scripting wire commands with explicit errors before queue admission. Reject missing or ambiguous fields, non-finite coordinates and grants, unsupported custom commands, multi-unit orders, out-of-range native IDs and nonnumeric build definitions. Preserve the Guard target in the native command. Keep multi-unit expansion for BARC-01.1e; no native or browser qualification is implied by this synthetic broker slice.
- [x] **BARC-01.1e — bounded multi-unit broker delivery.** Merged by PR #5 as protected `main` `41481e35d4f5cf18ace1942e680dcd0a9f2b3f03`. The broker validates 1–64 distinct acting units before admission and expands them in caller order to one native batch per unit. One bounded parent-envelope queue owns scripting and operator admission; a full queue rejects the whole parent. Nonwrapping per-session sequence and correlation ranges bind every child through an audit mapping that retains the full parent UUID, session, client, acting unit and child count. Exactly one command reader may claim a session channel, and guaranteed lease-scoped cleanup accounts for every queued child without letting an old reader close a renewed channel; a live session can create a fresh empty reader channel after normal exit or cancellation. The writer records only `WrittenToTransport`, `Unknown`, or `NotAttempted`, stops without replay after failure/cancellation/session replacement, and never treats a completed gRPC write as native acceptance. Native CoordinatorClient acceptance remains unavailable because it drops batch/client/index provenance and may overflow-drop commands.
- [ ] **BARC-01.1g — scripting feature projection.** Source candidate adds the existing Core snapshot feature collection to the scripting protobuf at the unused `GameStateSnapshot` field tag 8 and preserves exact feature IDs, definition kinds and ground-plane positions through `WireConvert.fromCoreSnapshot`. Focused Contracts and real coordinator-to-scripting loopback fixtures cover protobuf encode/decode, two asymmetric features, a unit and feature that both use ID 7, empty and replacement snapshots, sequence-gap and unsupported-delta fencing, and full-snapshot recovery. The five vendored HighBar protobufs and command delivery are unchanged; this remains synthetic broker evidence rather than native or live-game qualification.

BARC-01.1a–e are merged to protected `main`. BARC-01.1g remains a source candidate until protected merge readback; later native qualification remains separate.

## Evidence boundary

Focused fixtures use asymmetric coordinates and a loopback gRPC
`HighBarCoordinator` client. They cover snapshot → sequence gap → delta →
recovery and snapshot → unapplied nonempty delta → recovery, including a late
subscriber and command refusal during invalidity. The .1g fixture additionally
proves that features survive the real coordinator-to-scripting path without
colliding with unit IDs and that complete snapshots replace the feature set.
This is synthetic protocol
evidence. It does not establish native plugin behavior, a live game session,
BAR content compatibility, browser/WASM behavior or installed operation.

The local protobuf bootstrap pins `grpc-fsharp` 0.2.0 to match
`Grpc-FSharp.Tools` 0.2.0. No dependency or HighBar schema pin is upgraded.

The .1g candidate passes 6 Contracts, 50 Protocol and 58 Core tests. Its
focused real gRPC coordinator-to-scripting test list passes 2 tests.

The repository-wide integration and SurfaceArea projects currently cannot restore the pre-existing unavailable `SkiaViewer` dependency. Focused evidence therefore references only Core, Contracts and Protocol; public surface baselines can be generated with the repository's existing `SurfaceWalker` when that dependency is available. This does not qualify the native plugin or the blocked viewer dependency.
