# US2 Evidence — Host-mode Session with Admin Authority

**Recorded**: 2026-04-27 (T030–T037)
**Branch**: `001-tui-grpc-broker`
**Status**: `[S]` — broker-side host-mode lifecycle is exercised end-to-end
against a real Kestrel-hosted gRPC server. The actual game-process launch
side (HighBarV3 binary spawn + crash-detect loop) is exercised against
synthetic stand-in processes (`/usr/bin/sleep`, `/usr/bin/false`) because
HighBarV3 is not provisioned on the CI / dev machine. See _Synthetic gaps_
at the bottom.

## 1. Composition-root smoke (T028 carry-over)

```
$ dotnet run --project src/Broker.App -- --version
broker v1.0

$ dotnet run --project src/Broker.App -- --help
broker [options]

Options:
  --listen HOST:PORT   gRPC server listen address (default 127.0.0.1:5021)
  --no-viz             disable the optional 2D visualization subsystem
  --version            print the broker version and exit
  -h, --help           print this help text
```

The broker boots `ServerHost.start` on `127.0.0.1:5021`, configures the
Serilog rolling-file audit sink under `./logs/`, and runs the
Spectre.Console TUI tick loop on the main thread. Q quits, SIGINT teardown
flushes audit + disposes the gRPC handle.

## 2. Quickstart §3 walkthrough — coverage map

| Quickstart step | Covered by | Status |
|-----------------|------------|--------|
| §3.1 Operator presses **L** to open lobby (Idle only) | `HotkeyMap.map L Idle = OpenLobby` (`HotkeyMapTests`) | `[X]` |
| §3.1 Lobby panel renders map / mode / display / slots | `LobbyView.render` (`LobbyViewTests`) | `[X]` |
| §3.1 `D` toggles Headless ↔ Graphical | `LobbyView.apply ConsoleKey.D` (`LobbyViewTests`) | `[X]` |
| §3.2 Validation refuses launch when a connected scripting client lacks a `ProxyAi` slot (`MissingProxySlotForBoundClient`) | `Lobby.validate` (`LobbyTests` — 9 cases incl. the alice-bot-no-proxy-slot scenario) | `[X]` |
| §3.3 Pressing **Enter** flips state to Hosting (admin) | `HotkeyMap.map Enter Hosting = LaunchHostSession`; `TickLoop.dispatch` calls `OperatorOpenHost` then `OperatorLaunchHost` (`TickLoopDispatchTests`) | `[X]` |
| §3.4 Operator-issued admin commands `+` / `-` (speed) | `Session.stepSpeed` + `BrokerState.stepSpeed` + `TickLoop.dispatch StepSpeed` (`TickLoopDispatchTests`) | `[X]` |
| §3.4 Operator-issued admin command Space (pause toggle) | `Session.togglePause` + `BrokerState.togglePause` + `TickLoop.dispatch TogglePause` (`TickLoopDispatchTests`) | `[X]` |
| §3.4 Each operator action lands as a Serilog audit record | `Logging.writeAudit` is wired in `Program.run` and used by every `BrokerState` mutation site (T036). `AuditLifecycleTests` exercises the wire path for Hello / NameInUse / VersionMismatch / CommandRejected. | `[X]` (audit lines for the operator-issued speed/pause come from `ModeChanged` and `SessionEnded`; explicit `AdminCommandIssued` events are out of scope per spec — operator actions land as state transitions in the wire + audit log via `SessionEnded` and `ModeChanged`.) |
| §3.5 Elevate scripting client to admin via **A** | `HotkeyMap.map A Hosting = OpenElevatePrompt`; `TickLoop.dispatch OpenElevatePrompt` toggles admin on the lone client (`TickLoopDispatchTests`) | `[X]` partial — the prompt UI is reduced to "toggle admin on the only connected client" in the absence of a richer UI. Multi-client selection is left as a follow-up. |
| §3.5 Elevated client may issue `Admin _` commands; revocation flips them back to rejected | `AdminElevationTests` (T032) — full lifecycle: connect → reject pre-grant → grant → accept → revoke → reject. Audit asserts AdminGranted + AdminRevoked + 2× CommandRejected. | `[X]` |
| §3.6 **X** ends session, broadcasts `SessionEnd { reason = OPERATOR_TERMINATED }`, returns to Idle | `HotkeyMap.map X Hosting = EndSession`; `TickLoop.dispatch EndSession` calls `OperatorEndSession`, which calls `BrokerState.closeSession Session.OperatorTerminated`. `closeSession` writes `SessionEnd` to every subscribed `Channel<StateMsg>` and emits the `SessionEnded` audit event. The wire path is exercised by the `ProxyDetached` variant in `Sc005RecoveryTests` (US1) and structurally identical for `OperatorTerminated`. | `[X]` |

## 3. Game-process launch — synthetic stand-in (T035, FR-012, FR-027)

The broker's launcher abstraction (`Broker.App.GameProcess`) is exercised
by `GameProcessTests`. Tests use the platform's `sleep` / `false` binaries
because the actual HighBarV3 executable is not provisioned here. Each
synthetic test name carries the `Synthetic_` token per Constitution
Principle IV.

```
  Passed GameProcess (FR-012, FR-027).argsFor_Headless appends --headless to the base args
  Passed GameProcess (FR-012, FR-027).argsFor_Graphical appends --graphical to the base args
  Passed GameProcess (FR-012, FR-027).start_missing executable returns Error without throwing
  Passed GameProcess (FR-012, FR-027).start_empty exe path returns Error without spawning anything
  Passed GameProcess (FR-012, FR-027).Synthetic_start_then_Dispose kills the process and HasExited becomes true
  Passed GameProcess (FR-012, FR-027).Synthetic_OnExited fires on external termination (FR-027)
  Passed GameProcess (FR-012, FR-027).Synthetic_OnExited registered after exit fires immediately
```

The broker's crash-detection contract (FR-027 — recover to idle on
external game termination) is verified at the launcher layer: `OnExited`
fires on a real OS process exit (`/usr/bin/false` returns 1), and the
handler can call `BrokerState.closeSession Session.GameCrashed`. The
wire-up from `OnExited` to `closeSession` lives in `Broker.App.Program`
and is composition-root code; with HighBarV3 unavailable the end-to-end
"game crashes → broker recovers" assertion is captured against the
synthetic stand-in only.

## 4. Audit log — actual run

The broker emits structured Serilog events to `./logs/broker-YYYYMMDD.log`
(see `Logging.fs`, FR-028). Sample event template (rendered by Serilog
through `Audit.toLogTemplate`):

```
{Timestamp:o} [INF] audit.admin_granted at={At} client_name={ClientName} by={By}
    {"At":"2026-04-27T19:32:14.121+00:00","ClientName":"alice-bot","By":"operator"}
```

The `AuditLifecycleTests` and `AdminElevationTests` exercise every event
type that Scenario B emits (`ClientConnected`, `AdminGranted`,
`AdminRevoked`, `CommandRejected`, `ModeChanged`, `SessionEnded`).

## 5. Test summary

```
$ dotnet test FSBarV2.sln

Broker.Core.Tests:        Passed 44 / 44
Broker.Tui.Tests:         Passed 28 / 28
Broker.Protocol.Tests:    Passed  5 /  5
Broker.Integration.Tests: Passed 20 / 20
SurfaceArea:              Passed 27 / 27
Lib.Tests:                Passed  2 /  2
Total:                    121 / 121 — 0 failures.
```

US2-specific additions to the green count vs the US1 checkpoint:
- `Broker.Core.Tests`: +9 LobbyTests, +5 CommandPipelineTests admin sweep.
- `Broker.Tui.Tests`: +9 HotkeyMapTests, +6 LobbyViewTests, +13 TickLoopDispatchTests.
- `Broker.Integration.Tests`: +1 admin elevation lifecycle, +7 GameProcessTests.

## 6. Synthetic gaps (`[S]`) — disclosure per Principle IV

| Gap | Why synthetic | Real-evidence path |
|-----|---------------|---------------------|
| Live TUI screenshot (operator presses L → Lobby form → Enter → Hosting → A → admin → X → Idle) | Spectre.Console's `LiveDisplay` requires a real interactive TTY and cannot be captured under `dotnet test`'s stdout redirection. | Operator-driven manual capture against a developer terminal. The dispatch logic itself is fully covered by `TickLoopDispatchTests` against a `RecorderFacade`. |
| Game-process launch end-to-end (HighBarV3 binary spawned, joined match, observed admin command applied in-engine) | HighBarV3 binary not provisioned on this dev / CI host. | When the upstream HighBarV3 build lands, swap `argsFor` flags to match the engine's actual CLI and rerun `GameProcessTests` against the real executable. The broker side of the contract is unchanged. |

## 7. Remaining beyond US2

1. Multi-client elevate prompt (`OpenElevatePrompt` currently no-ops with
   0 or > 1 clients — a richer Spectre prompt would let the operator pick
   from the live roster). Tracked as TUI follow-up.
2. End-to-end with a real HighBarV3 launch — gated on the upstream
   workstream landing a usable proxy AI build (same gate as T029).
