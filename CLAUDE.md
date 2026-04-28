<!-- SPECKIT START -->
Active feature: **002-highbar-coordinator-pivot** — Broker–HighBarCoordinator Wire Pivot.

For technologies, project structure, shell commands, and other context,
read the current implementation plan:

- Plan: `specs/002-highbar-coordinator-pivot/plan.md`
- Spec: `specs/002-highbar-coordinator-pivot/spec.md`
- Research: `specs/002-highbar-coordinator-pivot/research.md`
- Data model: `specs/002-highbar-coordinator-pivot/data-model.md`
- Contracts: `specs/002-highbar-coordinator-pivot/contracts/`
- Quickstart: `specs/002-highbar-coordinator-pivot/quickstart.md`

Prior shipped feature (still in force for everything not contradicted by 002):
`specs/001-tui-grpc-broker/`.

Stack at a glance: F# on .NET 10; Spectre.Console TUI; Grpc.AspNetCore +
`FSharp.GrpcCodeGenerator`; Serilog (rolling-file audit log); SkiaViewer
for the optional 2D viz; Expecto for tests. Visibility lives in `.fsi`
per Constitution Principle II. Feature 002 vendors the upstream
`highbar.v1.HighBarCoordinator` proto set under `src/Broker.Contracts/highbar/`,
pinned via `specs/002-highbar-coordinator-pivot/contracts/highbar-proto-pin.md`.
<!-- SPECKIT END -->
