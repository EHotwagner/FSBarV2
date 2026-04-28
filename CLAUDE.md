<!-- SPECKIT START -->
Active feature: **001-tui-grpc-broker** — TUI gRPC Game Broker.

For technologies, project structure, shell commands, and other context,
read the current implementation plan:

- Plan: `specs/001-tui-grpc-broker/plan.md`
- Spec: `specs/001-tui-grpc-broker/spec.md`
- Research: `specs/001-tui-grpc-broker/research.md`
- Data model: `specs/001-tui-grpc-broker/data-model.md`
- Contracts: `specs/001-tui-grpc-broker/contracts/`
- Quickstart: `specs/001-tui-grpc-broker/quickstart.md`

Stack at a glance: F# on .NET 10; Spectre.Console TUI; Grpc.AspNetCore +
`FSharp.GrpcCodeGenerator`; Serilog (rolling-file audit log); SkiaViewer
for the optional 2D viz; Expecto for tests. Visibility lives in `.fsi`
per Constitution Principle II.
<!-- SPECKIT END -->
