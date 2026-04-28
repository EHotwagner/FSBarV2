// prelude.fsx — load the packed Broker libraries from your local NuGet cache.
//
// Principle I: every non-trivial change starts in FSI against the public
// surface. This prelude gives you that surface in one #load.
//
// Usage from FSI (dotnet fsi or VS Code Ionide):
//   dotnet fsi
//   > #load "scripts/prelude.fsx" ;;
//   > open Broker.Core ;;
//   > Mode.transition Mode.Mode.Idle Mode.Mode.Guest ;;
//
// To pack the libraries first:
//   dotnet pack src/Broker.Core/Broker.Core.fsproj      -o ~/.local/share/nuget-local/
//   dotnet pack src/Broker.Contracts/Broker.Contracts.fsproj -o ~/.local/share/nuget-local/
//
// Once the libraries' .fs bodies are real (Phase 3+), the values below
// return real results instead of throwing `failwith "not implemented"`.

#i "nuget: file:///home/developer/.local/share/nuget-local/"
#r "nuget: Broker.Core"
#r "nuget: Broker.Contracts"

open Broker.Core
open FSBarV2.Broker.Contracts
open Highbar.V1   // feature 002: vendored HighBarV3 proto types

printfn "prelude: Broker.Core + Broker.Contracts (incl. Highbar.V1) loaded."
printfn "  Try:   Mode.transition Mode.Mode.Idle Mode.Mode.Guest"
printfn "  Try:   ScriptingRoster.empty"
printfn "  Try:   HeartbeatRequest(PluginId = \"ai-7\", SchemaVersion = \"1.0.0\")"
printfn "  Note:  Phase-2 stubs throw 'not implemented'; that is expected."
