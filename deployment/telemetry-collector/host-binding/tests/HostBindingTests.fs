namespace Fs.Gg.Telemetry.HostBinding.Tests

open System
open System.Diagnostics
open System.IO
open System.Security.Cryptography
open System.Text
open System.Text.Json
open Fs.Gg.Telemetry.HostBinding
open Xunit

module private Fixture =
    let repository = Path.GetFullPath(Path.Combine(__SOURCE_DIRECTORY__, "../../../.."))
    let nativeRoot = "deployment/telemetry-collector"

    let sha256 path =
        use stream = File.OpenRead(path)
        Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant()

    let run root args =
        let start = ProcessStartInfo("git")
        start.WorkingDirectory <- root
        start.UseShellExecute <- false
        start.RedirectStandardOutput <- true
        start.RedirectStandardError <- true
        for argument in args do start.ArgumentList.Add(argument)
        use child = Process.Start(start)
        let output = child.StandardOutput.ReadToEnd()
        let error = child.StandardError.ReadToEnd()
        child.WaitForExit()
        if child.ExitCode <> 0 then failwith error
        output.Trim()

    type Repository(?profileTransform: string -> string) =
        let owner = Path.Combine(Path.GetTempPath(), "host-binding-tests-" + Guid.NewGuid().ToString("N"))
        let root = Path.Combine(owner, "repository")
        let pins = Path.Combine(owner, "source-pins.json")
        do
            Directory.CreateDirectory(Path.Combine(root, nativeRoot)) |> ignore
            for name in HostBinding.SourceFiles do
                let source = Path.Combine(repository, nativeRoot, name)
                let target = Path.Combine(root, nativeRoot, name)
                let text = File.ReadAllText(source)
                let value = if name = "native-operation-v1.json" then defaultArg profileTransform id text else text
                File.WriteAllText(target, value, UTF8Encoding(false))
            let entries =
                HostBinding.SourceFiles
                |> List.map (fun name -> name, sha256 (Path.Combine(root, nativeRoot, name)))
                |> Map.ofList
            File.WriteAllText(pins, JsonSerializer.Serialize(entries), UTF8Encoding(false))
            run root [ "init"; "--quiet" ] |> ignore
            run root [ "config"; "user.email"; "test@example.invalid" ] |> ignore
            run root [ "config"; "user.name"; "host-binding-test" ] |> ignore
            run root [ "add"; nativeRoot ] |> ignore
            run root [ "commit"; "--quiet"; "-m"; "fixture" ] |> ignore
        member _.Root = root
        member _.Pins = pins
        member _.Profile = Path.Combine(root, nativeRoot, "native-operation-v1.json")
        member _.Head = run root [ "rev-parse"; "HEAD" ]
        interface IDisposable with member _.Dispose() = Directory.Delete(owner, true)

    let construct (fixture: Repository) =
        HostBinding.construct fixture.Root fixture.Head fixture.Profile fixture.Pins

module HostBindingTests =
    [<Fact>]
    let ``current profile and source pins form a validated binding`` () =
        use fixture = new Fixture.Repository()
        let binding = Fixture.construct fixture
        Assert.Equal("1ef6d54eb3f9572580407efe9f266f643645af3af17c33723c0aa8368e5f4f34", binding.ProfileSha256)
        Assert.Equal(HostBinding.OperationId, binding.OperationId)
        Assert.Matches("^[0-9a-f]{40}$", binding.SourceTree)
        Assert.Matches("^[0-9a-f]{64}$", binding.BindingSha256)
        Assert.Matches("^[0-9a-f]{64}$", binding.ProducerSha256)

    [<Fact>]
    let ``admission formula matches independent fixed vector`` () =
        let binding =
            { Schema = "fsgg.telemetry.validated-host-binding/1"
              SourceSha = String('a', 40)
              SourceTree = String('b', 40)
              ProfileSha256 = "1ef6d54eb3f9572580407efe9f266f643645af3af17c33723c0aa8368e5f4f34"
              OperationId = HostBinding.OperationId
              SourcePinsSha256 = String('c', 64)
              ProducerSha256 = String('d', 64)
              BindingSha256 = String('e', 64) }
        Assert.Equal("1c12f59d8d04c7310f168eacb9b8403181febf479ee927cc7bd29d9dce5f7fe6", HostBinding.deriveAdmission binding "run-0001")
        let permuted = { binding with SourceSha = binding.ProfileSha256; ProfileSha256 = String('a', 40) }
        Assert.False(HostBinding.deriveAdmission binding "run-0001" = HostBinding.deriveAdmission permuted "run-0001")

    [<Fact>]
    let ``duplicate profile key is refused`` () =
        use fixture = new Fixture.Repository(fun text -> text.Replace("\"schema\":", "\"schema\": \"duplicate\", \"schema\":"))
        Assert.Throws<BindingRefusal>(fun () -> Fixture.construct fixture |> ignore) |> ignore

    [<Fact>]
    let ``wrong profile scalar kind is refused`` () =
        use fixture = new Fixture.Repository(fun text -> text.Replace("\"operationId\": \"v2-host-01.8a-native-collaboration-v1\"", "\"operationId\": 7"))
        Assert.Throws<BindingRefusal>(fun () -> Fixture.construct fixture |> ignore) |> ignore

    [<Fact>]
    let ``operation drift is refused`` () =
        use fixture = new Fixture.Repository(fun text -> text.Replace(HostBinding.OperationId, "v2-host-01.8a-readonly-source-compatibility-v1"))
        Assert.Throws<BindingRefusal>(fun () -> Fixture.construct fixture |> ignore) |> ignore

    [<Fact>]
    let ``source mutation after construction is refused`` () =
        use fixture = new Fixture.Repository()
        let binding = Fixture.construct fixture
        File.AppendAllText(fixture.Profile, "\n")
        Assert.Throws<BindingRefusal>(fun () -> Fixture.construct fixture |> ignore) |> ignore
        Assert.Throws<BindingRefusal>(fun () -> HostBinding.verifyAdmission binding "run-0001" binding.BindingSha256 (String('0', 64))) |> ignore

    [<Fact>]
    let ``wrong revision and mixed pin content are refused`` () =
        use fixture = new Fixture.Repository()
        Assert.Throws<BindingRefusal>(fun () -> HostBinding.construct fixture.Root (String('a', 40)) fixture.Profile fixture.Pins |> ignore) |> ignore
        let fields = JsonSerializer.Deserialize<Map<string, string>>(File.ReadAllText(fixture.Pins))
        let mixed = fields.Add("native-operation-v1.json", "5a30fc507f023d542521aac66c8f49c5ae6ee8d9e34dc90c1a3bf3ab30f6b08f")
        File.WriteAllText(fixture.Pins, JsonSerializer.Serialize(mixed))
        Assert.Throws<BindingRefusal>(fun () -> Fixture.construct fixture |> ignore) |> ignore

    [<Fact>]
    let ``duplicate and wrong-kind source pins are refused`` () =
        use duplicate = new Fixture.Repository()
        let raw = File.ReadAllText(duplicate.Pins)
        File.WriteAllText(duplicate.Pins, raw.Replace("{", "{\"qualify_native.py\":\"" + String('0', 64) + "\","))
        Assert.Throws<BindingRefusal>(fun () -> Fixture.construct duplicate |> ignore) |> ignore
        use wrongKind = new Fixture.Repository()
        let wrong = File.ReadAllText(wrongKind.Pins).Replace("\"native-producer-config.toml\":\"", "\"native-producer-config.toml\":7,\"ignored\":\"")
        File.WriteAllText(wrongKind.Pins, wrong)
        Assert.Throws<BindingRefusal>(fun () -> Fixture.construct wrongKind |> ignore) |> ignore

    [<Fact>]
    let ``binding and admission comparisons are exact`` () =
        use fixture = new Fixture.Repository()
        let binding = Fixture.construct fixture
        let admission = HostBinding.deriveAdmission binding "run-0001"
        HostBinding.verifyAdmission binding "run-0001" binding.BindingSha256 admission
        Assert.Throws<BindingRefusal>(fun () -> HostBinding.verifyAdmission binding "run-0002" binding.BindingSha256 admission) |> ignore
        Assert.Throws<BindingRefusal>(fun () -> HostBinding.verifyAdmission binding "run-0001" (String('0', 64)) admission) |> ignore
