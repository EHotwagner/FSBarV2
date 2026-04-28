## US3 dashboard load run

**Recorded**: 2026-04-28T06:54:33.4363161+00:00
**Driver**: `tests/Broker.Integration.Tests/DashboardLoadTests.fs`
**Fixture**: synthetic-proxy (`SyntheticProxy.connect`) + 4 real loopback gRPC scripting clients.

## SC-006 / SC-007 measurements

- **Connected clients**: 4 (load-bot-1, load-bot-2, load-bot-3, load-bot-4)
- **Snapshots pushed**: 25 at 200ms cadence (push window ≈ 5.0 s, ≥1 Hz refresh)
- **Latest dashboard tick at end of run**: 25
- **Per-client fan-out received**: 25 / 25 / 25 / 25
- **Units per snapshot**: 200 (≥200 floor — SC-006)
- **Players per snapshot**: 4
- **Mode**: Guest (auto-detected on proxy attach, FR-002 / FR-003)
- **Server state**: 127.0.0.1:34363
- **Telemetry stale**: false

## SC-007 dashboard content (rendered at peak load)

The rendering below is what `Broker.Tui.DashboardView.render` produces against the
live `Hub` state at the end of the load run, captured via a 200-col off-TTY
`IAnsiConsole`. Mode, connection state, every connected client, and per-player
resources / unit counts are all on one screen — SC-007 (≤10 s recognition).

```
┏━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━┓                                                                                                                            
┃ FSBar Broker  •  GUEST  •  listening 127.0.0.1:34363  •  press Q to quit ┃                                                                                                                            
┗━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━┛                                                                                                                            
╭─Broker──────────────────────────────────╮                       ╭─Session────────────────╮                                         ╭─Clients (4)──────────────────────────────────╮                   
│ ╭─────────┬───────────────────────────╮ │                       │ ╭─────────┬──────────╮ │                                         │ ╭────────────┬──────┬──────┬───────┬───────╮ │                   
│ │ version │ v1.0                      │ │                       │ │ state   │ active   │ │                                         │ │ name       │ v    │ slot │ admin │ queue │ │                   
│ │ listen  │ 127.0.0.1:34363           │ │                       │ │ elapsed │ 00:00:05 │ │                                         │ ├────────────┼──────┼──────┼───────┼───────┤ │                   
│ │ server  │ listening 127.0.0.1:34363 │ │                       │ │ pause   │ running  │ │                                         │ │ load-bot-1 │ v1.0 │ —    │ no    │ 0     │ │                   
│ │ uptime  │ 00:00:05                  │ │                       │ │ speed   │ 1×       │ │                                         │ │ load-bot-2 │ v1.0 │ —    │ no    │ 0     │ │                   
│ │ mode    │ GUEST                     │ │                       │ ╰─────────┴──────────╯ │                                         │ │ load-bot-3 │ v1.0 │ —    │ no    │ 0     │ │                   
│ ╰─────────┴───────────────────────────╯ │                       ╰────────────────────────╯                                         │ │ load-bot-4 │ v1.0 │ —    │ no    │ 0     │ │                   
╰─────────────────────────────────────────╯                                                                                          │ ╰────────────┴──────┴──────┴───────┴───────╯ │                   
                                                                                                                                     ╰──────────────────────────────────────────────╯                   
                                                                                                                                                                                                        
                                                                                                                                                                                                        
                                                                                                                                                                                                        
                                                                                                                                                                                                        
                                                                                                                                                                                                        
                                                                                                                                                                                                        
                                                                                                                                                                                                        
                                                                                                                                                                                                        
                                                                                                                                                                                                        
                                                                                                                                                                                                        
                                                                                                                                                                                                        
                                                                                                                                                                                                        
                                                                                                                                                                                                        
                                                                                                                                                                                                        
                                                                                                                                                                                                        
                                                                                                                                                                                                        
                                                                                                                                                                                                        
                                                                                                                                                                                                        
                                                                                                                                                                                                        
                                                                                                                                                                                                        
                                                                                                                                                                                                        
                                                                                                                                                                                                        
                                                                                                                                                                                                        
                                                                                                                                                                                                        
                                                                                                                                                                                                        
                                                                                                                                                                                                        
                                                                                                                                                                                                        
                                                                                                                                                                                                        
                                                                                                                                                                                                        
                                                                                                                                                                                                        
                                                                                                                                                                                                        
                                                                                                                                                                                                        
                                                                                                                                                                                                        
                                                                                                                                                                                                        
                                                                                                                                                                                                        
                                                                                                                                                                                                        
                                                                                                                                                                                                        
                                                                                                                                                                                                        
                                                                                                                                                                                                        
                                                                                                                                                                                                        
                                                                                                                                                                                                        
                                                                                                                                                                                                        
                                                                                                                                                                                                        
                                                                                                                                                                                                        
                                                                                                                                                                                                        
                                                                                                                                                                                                        
                                                                                                                                                                                                        
                                                                                                                                                                                                        
                                                                                                                                                                                                        
                                                                                                                                                                                                        
                                                                                                                                                                                                        
                                                                                                                                                                                                        
                                                                                                                                                                                                        
                                                                                                                                                                                                        
                                                                                                                                                                                                        
                                                                                                                                                                                                        
                                                                                                                                                                                                        
                                                                                                                                                                                                        
                                                                                                                                                                                                        
                                                                                                                                                                                                        
                                                                                                                                                                                                        
                                                                                                                                                                                                        
                                                                                                                                                                                                        
                                                                                                                                                                                                        
                                                                                                                                                                                                        
                                                                                                                                                                                                        
                                                                                                                                                                                                        
                                                                                                                                                                                                        
                                                                                                                                                                                                        
                                                                                                                                                                                                        
                                                                                                                                                                                                        
                                                                                                                                                                                                        
                                                                                                                                                                                                        
                                                                                                                                                                                                        
                                                                                                                                                                                                        
                                                                                                                                                                                                        
                                                                                                                                                                                                        
                                                                                                                                                                                                        
                                                                                                                                                                                                        
                                                                                                                                                                                                        
                                                                                                                                                                                                        
                                                                                                                                                                                                        
                                                                                                                                                                                                        
                                                                                                                                                                                                        
                                                                                                                                                                                                        
                                                                                                                                                                                                        
                                                                                                                                                                                                        
╭─Telemetry (tick 25)──────────────────────────────────────────────────────╮                                                                                                                            
│ ╭─────────┬──────┬───────┬────────┬───────┬───────────┬───────┬────────╮ │                                                                                                                            
│ │ player  │ team │ metal │ energy │ units │ buildings │ kills │ losses │ │                                                                                                                            
│ ├─────────┼──────┼───────┼────────┼───────┼───────────┼───────┼────────┤ │                                                                                                                            
│ │ Player1 │ 0    │ 1125  │ 575    │ 50    │ 4         │ 2     │ 1      │ │                                                                                                                            
│ │ Player2 │ 1    │ 1225  │ 625    │ 50    │ 4         │ 4     │ 2      │ │                                                                                                                            
│ │ Player3 │ 2    │ 1325  │ 675    │ 50    │ 4         │ 6     │ 3      │ │                                                                                                                            
│ │ Player4 │ 3    │ 1425  │ 725    │ 50    │ 4         │ 8     │ 4      │ │                                                                                                                            
│ ╰─────────┴──────┴───────┴────────┴───────┴───────────┴───────┴────────╯ │                                                                                                                            
╰──────────────────────────────────────────────────────────────────────────╯                                                                                                                            
                                                                                                                                                                                                        
                                                                                                                                                                                                        
                                                                                                                                                                                                        
                                                                                                                                                                                                        
                                                                                                                                                                                                        
                                                                                                                                                                                                        
                                                                                                                                                                                                        
                                                                                                                                                                                                        
                                                                                                                                                                                                        
                                                                                                                                                                                                        
                                                                                                                                                                                                        
                                                                                                                                                                                                        
                                                                                                                                                                                                        
                                                                                                                                                                                                        
                                                                                                                                                                                                        
                                                                                                                                                                                                        
                                                                                                                                                                                                        
                                                                                                                                                                                                        
                                                                                                                                                                                                        
                                                                                                                                                                                                        
                                                                                                                                                                                                        
                                                                                                                                                                                                        
                                                                                                                                                                                                        
                                                                                                                                                                                                        
                                                                                                                                                                                                        
                                                                                                                                                                                                        
                                                                                                                                                                                                        
                                                                                                                                                                                                        
                                                                                                                                                                                                        
                                                                                                                                                                                                        
                                                                                                                                                                                                        
                                                                                                                                                                                                        
                                                                                                                                                                                                        
                                                                                                                                                                                                        
                                                                                                                                                                                                        
                                                                                                                                                                                                        
                                                                                                                                                                                                        
                                                                                                                                                                                                        
                                                                                                                                                                                                        
                                                                                                                                                                                                        
                                                                                                                                                                                                        
                                                                                                                                                                                                        
                                                                                                                                                                                                        
                                                                                                                                                                                                        
                                                                                                                                                                                                        
                                                                                                                                                                                                        
                                                                                                                                                                                                        
                                                                                                                                                                                                        
                                                                                                                                                                                                        
                                                                                                                                                                                                        
                                                                                                                                                                                                        
                                                                                                                                                                                                        
                                                                                                                                                                                                        
                                                                                                                                                                                                        
                                                                                                                                                                                                        
                                                                                                                                                                                                        
                                                                                                                                                                                                        
                                                                                                                                                                                                        
                                                                                                                                                                                                        
                                                                                                                                                                                                        
                                                                                                                                                                                                        
                                                                                                                                                                                                        
                                                                                                                                                                                                        
                                                                                                                                                                                                        
                                                                                                                                                                                                        
                                                                                                                                                                                                        
                                                                                                                                                                                                        
                                                                                                                                                                                                        
                                                                                                                                                                                                        
                                                                                                                                                                                                        
                                                                                                                                                                                                        
                                                                                                                                                                                                        
                                                                                                                                                                                                        
                                                                                                                                                                                                        
                                                                                                                                                                                                        
                                                                                                                                                                                                        
                                                                                                                                                                                                        
                                                                                                                                                                                                        
                                                                                                                                                                                                        
                                                                                                                                                                                                        
                                                                                                                                                                                                        
                                                                                                                                                                                                        
                                                                                                                                                                                                        
                                                                                                                                                                                                        
                                                                                                                                                                                                        
                                                                                                                                                                                                        
                                                                                                                                                                                                        
╭──────────────────────────────────────────────────────────────────────────────────╮                                                                                                                    
│ Q quit · V viz · L lobby (idle) · Space pause (host) · +/- speed · X end session │                                                                                                                    
╰──────────────────────────────────────────────────────────────────────────────────╯                                                                                                                    
```

## Status

`[S]` — broker-side TUI render and per-client fan-out are real production code on
a real Kestrel-hosted gRPC server with 4 live clients and a real `Channel<StateMsg>`
drain per client. The proxy peer is the loopback `SyntheticProxy` substitute for
the eventual HighBarV3 workstream (research.md §7, §14) — same disclosure as T029
and T037. A live-TTY screenshot capture is the remaining synthetic gap (Spectre
`LiveDisplay` requires an interactive TTY); the rendered transcript above is the
exact output a TTY would show.
