module Broker.SurfaceArea.SurfaceWalker

open System
open System.Reflection
open Microsoft.FSharp.Reflection

type SurfaceLine = string

let private isCompilerGenerated (mi: MemberInfo) =
    mi.GetCustomAttributes(typeof<System.Runtime.CompilerServices.CompilerGeneratedAttribute>, false).Length > 0

let private formatType (t: Type) : string =
    if t.IsGenericType then
        let args =
            t.GetGenericArguments()
            |> Array.map (fun a -> a.Name)
            |> String.concat ","
        sprintf "%s<%s>" (t.Name.Split('`').[0]) args
    else t.Name

let private formatParameters (parms: ParameterInfo array) : string =
    parms
    |> Array.map (fun p -> sprintf "%s:%s" p.Name (formatType p.ParameterType))
    |> String.concat ", "

let private fullNameOr (fallback: string) (t: Type) : string =
    match Option.ofObj t.FullName with
    | Some n -> n
    | None -> fallback

let private memberLines (t: Type) : SurfaceLine seq =
    let owner = fullNameOr t.Name t
    seq {
        for m in t.GetMethods(BindingFlags.Public ||| BindingFlags.Static ||| BindingFlags.DeclaredOnly) do
            if not (isCompilerGenerated m) then
                yield sprintf "M %s.%s(%s) : %s"
                    owner m.Name (formatParameters (m.GetParameters())) (formatType m.ReturnType)

        for p in t.GetProperties(BindingFlags.Public ||| BindingFlags.Static ||| BindingFlags.DeclaredOnly) do
            if not (isCompilerGenerated p) then
                yield sprintf "P %s.%s : %s" owner p.Name (formatType p.PropertyType)

        for nt in t.GetNestedTypes(BindingFlags.Public ||| BindingFlags.DeclaredOnly) do
            if not (isCompilerGenerated nt) then
                let kind =
                    if FSharpType.IsRecord(nt, BindingFlags.Public ||| BindingFlags.NonPublic) then "record"
                    elif FSharpType.IsUnion(nt, BindingFlags.Public ||| BindingFlags.NonPublic) then "union"
                    elif nt.IsInterface then "interface"
                    elif nt.IsEnum then "enum"
                    else "class"
                yield sprintf "T %s.%s : %s" owner nt.Name kind

                if FSharpType.IsRecord(nt, BindingFlags.Public ||| BindingFlags.NonPublic) then
                    for f in FSharpType.GetRecordFields(nt, BindingFlags.Public ||| BindingFlags.NonPublic) do
                        yield sprintf "  F %s.%s.%s : %s" owner nt.Name f.Name (formatType f.PropertyType)

                if FSharpType.IsUnion(nt, BindingFlags.Public ||| BindingFlags.NonPublic) then
                    for c in FSharpType.GetUnionCases(nt, BindingFlags.Public ||| BindingFlags.NonPublic) do
                        let fields =
                            c.GetFields()
                            |> Array.map (fun f -> sprintf "%s:%s" f.Name (formatType f.PropertyType))
                            |> String.concat ", "
                        yield sprintf "  C %s.%s.%s(%s)" owner nt.Name c.Name fields

                if nt.IsInterface then
                    for am in nt.GetMethods(BindingFlags.Public ||| BindingFlags.Instance) do
                        yield sprintf "  A %s.%s.%s(%s) : %s"
                            owner nt.Name am.Name (formatParameters (am.GetParameters())) (formatType am.ReturnType)
    }

let moduleSurface (asm: Assembly) (moduleFullName: string) : SurfaceLine list =
    let t =
        asm.GetTypes()
        |> Array.tryFind (fun t -> fullNameOr "" t = moduleFullName)
    match t with
    | None -> []
    | Some t ->
        memberLines t
        |> Seq.distinct
        |> Seq.sort
        |> List.ofSeq

let publicModules (asm: Assembly) : string list =
    asm.GetTypes()
    |> Array.choose (fun t ->
        let n = fullNameOr "" t
        if t.IsPublic
            && t.IsAbstract && t.IsSealed       // F# modules compile to abstract+sealed (static) classes
            && n <> ""
            && not (n.Contains "+")             // exclude nested types
        then Some n
        else None)
    |> Array.sort
    |> List.ofArray
