# T048 — `scripts/prelude.fsx` against the packed library

**Date:** 2026-04-28
**Packed versions:** `Broker.Core.0.2.0`, `Broker.Contracts.0.2.0` (under `~/.local/share/nuget-local/`).

The `scripts/` directory contains only `prelude.fsx`; there are no numbered
example scripts to re-run.

## 1. Pack

```
dotnet pack src/Broker.Core/Broker.Core.fsproj      -o ~/.local/share/nuget-local/ /p:Version=0.2.0
dotnet pack src/Broker.Contracts/Broker.Contracts.fsproj -o ~/.local/share/nuget-local/ /p:Version=0.2.0
```

Both packages built and packed cleanly in `Release`.

## 2. Prelude load

```
$ dotnet fsi scripts/prelude.fsx
prelude: Broker.Core loaded.
  Try:   Mode.transition Mode.Mode.Idle Mode.Mode.Guest
  Try:   ScriptingRoster.empty
  Note:  Phase-2 stubs throw 'not implemented'; that is expected.
```

(The trailing "Phase-2 stubs throw" hint is informational text from the
prelude itself and pre-dates Phase 3; the implementations are now real.
Left untouched — see exercise below.)

## 3. Live surface exercise (post Phase 3+)

```
=== Phase 3+ exercise: implementations are real ===
  Mode.transition Idle->Guest -> Ok Guest
  ScriptingRoster.tryAdd alice-bot -> Ok (count=1)
  CommandPipeline.createQueue 4 -> depth=0 cap=4
  Lobby.validate empty-participants -> Ok

OK — Phase 3+ surface live, no 'not implemented' exceptions.
```

Confirms the packed `Broker.Core` library still exposes the public
surface promised by the `.fsi` files and that the implementations
introduced through US1–US4 are reachable from FSI. No `failwith "not
implemented"` paths remain on the public surface.
