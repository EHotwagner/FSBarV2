module Broker.SurfaceArea.SurfaceWalker

open System.Reflection

/// A single sorted line for the public-surface baseline. Each line is
/// stable across rebuilds (no stack-frame addresses, no random ordering)
/// so a `string` diff is the meaningful comparison.
type SurfaceLine = string

/// Walk an assembly's public surface filtered to types whose CLR full name
/// equals `moduleFullName` (an F# module is a static nested class). For a
/// module like `Broker.Core.Mode` declared in the namespace `Broker.Core`,
/// the CLR full name is `Broker.Core.Mode`.
val moduleSurface :
    asm:Assembly
    -> moduleFullName:string
    -> SurfaceLine list

/// All public (non-compiler-generated) module-style types defined in this
/// assembly, returned as their CLR full names. Used by tests to discover
/// which modules require a baseline.
val publicModules : asm:Assembly -> string list
