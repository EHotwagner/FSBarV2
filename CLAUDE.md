<!-- SPECKIT START -->
Active feature: **003-elmish-mvu-core** — Elmish MVU Core for State and I/O.

For technologies, project structure, shell commands, and other context,
read the current implementation plan:

- Plan: `specs/003-elmish-mvu-core/plan.md`
- Spec: `specs/003-elmish-mvu-core/spec.md`
- Research: `specs/003-elmish-mvu-core/research.md`
- Data model: `specs/003-elmish-mvu-core/data-model.md`
- Contracts: `specs/003-elmish-mvu-core/contracts/`
- Quickstart: `specs/003-elmish-mvu-core/quickstart.md`

Prior shipped features (still in force for everything not contradicted by 003):
`specs/001-tui-grpc-broker/`, `specs/002-highbar-coordinator-pivot/`.

Stack at a glance: F# on .NET 10; Spectre.Console TUI; Grpc.AspNetCore +
`FSharp.GrpcCodeGenerator`; Serilog (rolling-file audit log); SkiaViewer
for the optional 2D viz; Expecto for tests. Visibility lives in `.fsi`
per Constitution Principle II. Feature 003 adds the `Elmish` NuGet
package as a load-bearing dependency and introduces a new `Broker.Mvu`
project housing the `Model` / `Msg` / `Cmd` / `update` / `view`
spine plus production and test runtimes; the mutable `BrokerState.Hub`
and `withLock` discipline from 001/002 are retired in the same change set.
<!-- SPECKIT END -->
