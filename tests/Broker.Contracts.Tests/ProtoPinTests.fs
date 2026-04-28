module Broker.Contracts.Tests.ProtoPinTests

open System.IO
open System.Security.Cryptography
open Expecto

/// Vendored proto pin (mirror of
/// `specs/002-highbar-coordinator-pivot/contracts/highbar-proto-pin.md` /
/// `src/Broker.Contracts/HIGHBAR_PROTO_PIN.md`). If the on-disk file's
/// sha256 drifts from this constant, the test fails loudly with a diff —
/// re-run the re-vendoring procedure in the pin manifest.
let private expectedHashes : (string * string) list =
    [ "coordinator.proto", "d8a0e651ed6a8186a7eea0beb6a05ede7c4a9f0a132b581e8af2e23fe30cf5f6"
      "state.proto",       "ca223f63ba081e23b6baf201a053337282332b018f69114681924016b06d9810"
      "commands.proto",    "19cbca8c0b84de976e5e99c20fd9a7e3b63909248d45caed3f237315cc780de0"
      "events.proto",      "796d79ed2eb3c20505565d7b93d2d14ce8e1c1c75caa8f4e10bf87ab2a4f0175"
      "common.proto",      "15072451c53f4428bc3c1c1c35e21be5f79a2461c451fa1be523ce26667826b1" ]

/// Vendored protos live next to the Broker.Contracts project. Test runner
/// sits at `tests/Broker.Contracts.Tests/bin/Debug/net10.0/`; walk up to
/// the repo root and into `src/Broker.Contracts/highbar/`.
let private highbarDir =
    let asmDir =
        let asmLoc = System.Reflection.Assembly.GetExecutingAssembly().Location
        Path.GetDirectoryName(asmLoc) |> Option.ofObj |> Option.defaultValue "."
    Path.GetFullPath(Path.Combine(asmDir, "..", "..", "..", "..", "..", "src", "Broker.Contracts", "highbar"))

let private sha256Hex (path: string) : string =
    use sha = SHA256.Create()
    use stream = File.OpenRead(path)
    let bytes = sha.ComputeHash(stream)
    bytes |> Array.map (fun b -> b.ToString("x2")) |> String.concat ""

[<Tests>]
let protoPinTests =
    testList "ProtoPin" [
        for fname, expected in expectedHashes do
            test (sprintf "vendored highbar/%s matches pin manifest" fname) {
                let p = Path.Combine(highbarDir, fname)
                Expect.isTrue (File.Exists p)
                    (sprintf "vendored proto missing: %s" p)
                let actual = sha256Hex p
                Expect.equal actual expected
                    (sprintf "sha256 drift in %s: re-vendor per HIGHBAR_PROTO_PIN.md or update the pin." fname)
            }
    ]
