# US2 — synthetic-coordinator command-egress evidence (T032)

**Date**: 2026-04-28
**Status**: `[S]` — broker-side wire path is real production code; the
plugin peer is the loopback `SyntheticCoordinator` fixture. Real-game
closure for these scenarios is owned by Phase 5 T036a (operator host-mode
walkthrough).

This artifact backs the `[S]` marker on T032 in `tasks.md` and the
matching row in the Synthetic-Evidence Inventory.

## Acceptance scenarios verified

### US2 acceptance #1 — operator Pause reaches the engine wire

`Synthetic_T028` drives `BrokerState.asCoreFacade.OperatorTogglePause()`
against an attached coordinator session. The dispatch path:

1. `OperatorTogglePause` toggles the broker-internal pause display.
2. T031 wiring then enqueues a Core `CommandPipeline.Command` with
   `Admin Pause` (or `Admin Resume` when toggling back) to
   `BrokerState.coordinatorCommandChannel`.
3. The `HighBarCoordinatorService.OpenCommandChannel` drain calls
   `WireConvert.tryFromCoreCommandToHighBar`, producing a
   `CommandBatch` with one `AICommand.PauseTeam { Enable = true }`.
4. The `SyntheticCoordinator.CommandStream` receives the batch.

The test asserts `PauseTeam.Enable = true` on the receiving end, plus
the audit sink emits `CoordinatorCommandChannelOpened` (FR-009).

### US2 acceptance #2 — scripting-client gameplay command reaches the engine

The same `Synthetic_T028` test enqueues a Core `Gameplay (UnitOrder
Move)` directly via `BrokerState.sendToCoordinator` and asserts the
matching `AICommand.MoveUnit { UnitId = 99 }` arrives on the
`OpenCommandChannel` stream. The `SubmitCommands → BackpressureGate
→ sendToProxy/sendToCoordinator` fan-in is already covered by 001's
`ScriptingClientEndToEndTests`; this test confirms the wire-out half
of the new coordinator path delivers the right `AICommand` arm.

### US2 acceptance #3 — backpressure rejects with QUEUE_FULL

`Synthetic_T029` drives `BackpressureGate.process_` directly against a
small-capacity (4) per-client queue with `host` mode + slot bound to
the test client. The gate accepts the first 4 commands and rejects the
remaining 4 with `RejectReason.QueueFull` — confirming FR-010 carries
forward unchanged onto the coordinator path. This pure-domain test
mirrors the per-client backpressure contract that the live wire
(`SubmitCommands → BackpressureGate → sendToCoordinator`) preserves.

## Admin commands without an AICommand mapping

Per research §3, the broker rejects `Admin SetSpeed`,
`Admin OverrideVision`, and `Admin OverrideVictory` at the wire
boundary with `CommandPipeline.AdminNotAvailable` because no AICommand
arm exists on the coordinator side. The unit suite
(`tests/Broker.Protocol.Tests/CoordinatorTests.fs`) covers each
unmappable admin variant:

| Admin command | Result |
|---------------|--------|
| `Pause` / `Resume` | → `AICommand.PauseTeam { Enable = true/false }` |
| `GrantResources (m, e)` | → two `AICommand.GiveMe` (resource_id 0=metal, 1=energy) |
| `SetSpeed` | `Error AdminNotAvailable` |
| `OverrideVision` | `Error AdminNotAvailable` |
| `OverrideVictory` | `Error AdminNotAvailable` |

The TUI surface (T031) audits the rejection so the operator sees a
clear "no coordinator-side mapping (awaiting future HighBarAdmin
bridge)" indication.

## Real-evidence path

| Scenario | Real-wire closure task |
|----------|------------------------|
| Operator Pause via TUI | T036a — host-mode walkthrough closes 001 T037 |
| Scripting-client gameplay command | T036a — same walkthrough |
| Backpressure under load | T036 — dashboard load + admin run |

When the operator captures these against a real BAR + HighBarV3 build,
T038 flips the inventory entries on 001's tasks.md.

## Notes

- The `client_command_id` field carries the lower 64 bits of the broker's
  internal command UUID (data-model §1.10). The unit test verifies the
  byte order against `BitConverter.ToUInt64`.
- `CommandBatch.batch_seq` is monotonic per coordinator attachment; the
  `HighBarCoordinatorService.Service.nextBatchSeq` counter resets on
  each attach.
- Operator hotkey rebinding lives in `BrokerState.asCoreFacade`
  (`OperatorTogglePause`, `OperatorStepSpeed`); the TUI's `TickLoop`
  dispatches to those without further wiring.
