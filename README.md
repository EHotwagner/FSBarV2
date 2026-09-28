# Speckit F# Library template

A minimal F# library scaffold that aligns with the fsharp-opinionated
Speckit preset: `.fsi`-gated visibility, FSI-first workflow, FAKE-free
verification via `dotnet test`.

## Build prerequisites

The F# protobuf generator is a repository-local .NET tool pinned in
`.config/dotnet-tools.json`. `Broker.Contracts` restores that manifest before
code generation and invokes it through `eng/protoc`; a separate global
`protoc-gen-fsharp` installation is not required.

## HighBar ground coordinates

HighBar/Recoil positions use native `(X,Y,Z)`, where `Y` is elevation and the
ground plane is `(X,Z)`. The legacy broker `Vec2` stores that ground plane as
`(x,y) = (X,Z)`. Commands sent back to HighBar expand a legacy target to
`(X,0,Z)`. The current two-dimensional broker model therefore discards
observed elevation and cannot request nonzero target elevation; callers must
not interpret its second coordinate as native `Y`.

## Use as a `dotnet new` template

```bash
# Install the template pack (one-time per machine).
dotnet new install /path/to/speckit-fsharp-tooling/templates/speckit-fsharp-lib

# Create a new library.
dotnet new speckit-fsharp-lib -n MyLibrary -o MyLibrary
```

## Layout

```
.
├── Directory.Build.props            # common MSBuild settings (FS0078-as-error etc.)
├── FSBarV2.sln
├── src/
│   └── Lib/
│       ├── Lib.fsproj
│       ├── Library.fsi              # the public surface (Principle II)
│       └── Library.fs               # implementation — no access modifiers
├── tests/
│   └── Lib.Tests/
│       ├── Lib.Tests.fsproj         # Expecto
│       ├── Tests.fs
│       └── Program.fs
└── scripts/
    └── prelude.fsx                  # #load this in FSI (Principle I)
```

## After `dotnet new speckit-fsharp-lib`

1. `dotnet test` — confirm the placeholder `add` passes.
2. `dotnet pack -c Release -o ~/.local/share/nuget-local` — make the
   library available to `scripts/prelude.fsx`.
3. `dotnet fsi` then `#load "scripts/prelude.fsx"` — first interactive
   session against the API.
4. `specify init . --ai codex --ai-skills --preset ~/projects/speckit-fsharp-tooling/presets/fsharp-opinionated` — layer the
   preset on top.
5. `specify extension add ~/projects/speckit-fsharp-tooling/extensions/evidence` — enable the DAG + audit.
