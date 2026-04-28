# HighBarV3 Proto Pin Manifest (mirror)

This file is a byte-mirror of
[`specs/002-highbar-coordinator-pivot/contracts/highbar-proto-pin.md`](../../specs/002-highbar-coordinator-pivot/contracts/highbar-proto-pin.md).
The spec-side copy is canonical; this copy lives next to the vendored
`.proto` files so the audit anchor travels with the source tree.

## Upstream coordinates

- **Repo**: `EHotwagner/HighBarV3`
- **Branch**: `master`
- **Commit SHA (full)**: `66483515a3333d6160bb5298e0d0bf6bb7188b4c`
- **Captured at**: 2026-04-28

## Vendored files

The five files under `src/Broker.Contracts/highbar/` MUST hash to the
sha256 values below. The `Broker.Contracts.Tests` `ProtoPin` test (T009)
verifies this on every CI run.

| File | GitHub blob SHA | sha256 of file content |
|------|-----------------|------------------------|
| `coordinator.proto` | `2955ac7d29898da08bc776e9ab2010d7aa05accd` | `d8a0e651ed6a8186a7eea0beb6a05ede7c4a9f0a132b581e8af2e23fe30cf5f6` |
| `state.proto` | `530ed5e5f9ac8187a63053b213de0d48d110ee13` | `ca223f63ba081e23b6baf201a053337282332b018f69114681924016b06d9810` |
| `commands.proto` | `ff8a676496501c3e47aa7fb69fd226550f6b320e` | `19cbca8c0b84de976e5e99c20fd9a7e3b63909248d45caed3f237315cc780de0` |
| `events.proto` | `0b050ceb5820f4b1f34a825abc399e8bc446200c` | `796d79ed2eb3c20505565d7b93d2d14ce8e1c1c75caa8f4e10bf87ab2a4f0175` |
| `common.proto` | `7b8b1d915b22e874d672593650917a3f0fc8a97e` | `15072451c53f4428bc3c1c1c35e21be5f79a2461c451fa1be523ce26667826b1` |

## Files NOT vendored (deliberately)

`service.proto` (HighBarProxy + HighBarAdmin) and `callbacks.proto` are
out of scope per spec Out of Scope. The broker only consumes the
coordinator-side contract.

## Re-vendoring procedure

See the canonical
[`contracts/highbar-proto-pin.md`](../../specs/002-highbar-coordinator-pivot/contracts/highbar-proto-pin.md)
for the full procedure. In short: pick a new upstream SHA, refresh both
copies (this directory and `specs/002-highbar-coordinator-pivot/contracts/highbar/`),
update both manifests, run the SurfaceArea suite, update `WireConvert`
+ tests for any wire-shape change.
