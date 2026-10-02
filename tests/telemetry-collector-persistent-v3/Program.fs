open System
open System.Diagnostics
open System.IO
open System.Security.Cryptography
open System.Text
open System.Text.Json
open System.Text.Json.Nodes
open FSGG.Telemetry.PersistentV3

let fail message = raise (InvalidOperationException message)
let check condition message = if not condition then fail message
let shaBytes (bytes: byte array) = Convert.ToHexString(SHA256.HashData bytes).ToLowerInvariant()
let shaFile (path: string) = File.ReadAllBytes path |> shaBytes
let privateMode (path: string) (mode: UnixFileMode) = File.SetUnixFileMode(path, mode)
let write (path: string) (bytes: byte array) (mode: UnixFileMode) = File.WriteAllBytes(path, bytes); privateMode path mode
let json value = JsonSerializer.SerializeToUtf8Bytes value
let parseObject (bytes: byte array) = JsonNode.Parse(bytes).AsObject()
let clone (value: JsonObject) = JsonNode.Parse(value.ToJsonString()).AsObject()
let canonicalModuleSha = Preparation.CanonicalVerifierModuleSha256
let hashes = [| for c in '1' .. '9' -> String(c, 64) |]
let revisions = [| for c in 'a' .. 'f' -> String(c, 40) |]

let fixture root =
    Directory.CreateDirectory root |> ignore
    privateMode root (UnixFileMode.UserRead ||| UnixFileMode.UserWrite ||| UnixFileMode.UserExecute)
    let config = Path.Combine(root, "host.json")
    let secret = Path.Combine(root, "collector.secret")
    let collector = Path.Combine(root, "collector")
    let codexHome = Path.Combine(root, "codex-home")
    let evidence = Path.Combine(root, "evidence")
    let runtimeRoot = Path.Combine(root, "runtime")
    for path in [ codexHome; evidence; runtimeRoot ] do
        Directory.CreateDirectory path |> ignore
        privateMode path (UnixFileMode.UserRead ||| UnixFileMode.UserWrite ||| UnixFileMode.UserExecute)
    write secret (Encoding.UTF8.GetBytes(String('s', 32))) (UnixFileMode.UserRead ||| UnixFileMode.UserWrite)
    write collector (Encoding.UTF8.GetBytes("#!/bin/sh\nexit 0\n")) (UnixFileMode.UserRead ||| UnixFileMode.UserWrite ||| UnixFileMode.UserExecute)
    let sourceModule = Path.GetFullPath(Path.Combine(__SOURCE_DIRECTORY__, "../../tools/learn_01_native_source.py"))
    check (shaFile sourceModule = canonicalModuleSha) "canonical verifier source differs"
    let modulePath = Path.Combine(runtimeRoot, "learn_01_native_source.py")
    File.Copy(sourceModule, modulePath)
    privateMode modulePath (UnixFileMode.UserRead ||| UnixFileMode.UserWrite)
    let runtime = FileInfo("/usr/bin/python3").ResolveLinkTarget(true).FullName
    let runtimeSha = shaFile runtime
    let moduleSha = shaFile modulePath
    let dependency = Path.Combine(runtimeRoot, "declared-runtime-dependency.dat")
    write dependency (Encoding.UTF8.GetBytes("synthetic declared transitive runtime dependency\n")) (UnixFileMode.UserRead ||| UnixFileMode.UserWrite)
    let dependencySha = shaFile dependency
    let rows =
        [| runtime, FileInfo(runtime).Length, runtimeSha
           modulePath, FileInfo(modulePath).Length, moduleSha
           dependency, FileInfo(dependency).Length, dependencySha |]
        |> Array.sortBy (fun (path, _, _) -> path)
        |> Array.map (fun (path, bytes, digest) -> {| path = path; bytes = bytes; sha256 = digest |})
    let manifestPath = Path.Combine(runtimeRoot, "native-verifier-runtime.json")
    let manifestBytes =
        json {| schema = "fsgg.telemetry.native-verifier-runtime/1"; sourceRevision = revisions[2]
                runtimeImageDigest = "sha256:" + hashes[0]; runtimeExecutablePath = runtime
                modulePath = modulePath; files = rows |}
    let manifestSha = shaBytes manifestBytes
    let profile = Encoding.UTF8.GetBytes("{\"schema\":\"synthetic-test-profile/1\"}\n")
    write (Path.Combine(evidence, "fixed-native-capability-profile.json")) profile (UnixFileMode.UserRead ||| UnixFileMode.UserWrite)
    let profileSha = shaBytes profile
    let hostConfig =
        json {| Schema = "fsgg.telemetry.host-config/2"
                Credentials = [| {| Reference = "collector"; SecretFile = secret; WorkspaceId = "fixture-workspace";
                                     ProducerId = "fixture-producer"; StreamId = "fixture-stream"; Role = "native-collector";
                                     GrantId = "fixture-grant"; GrantGeneration = 1L; Revoked = false |} |] |}
    write config hostConfig (UnixFileMode.UserRead ||| UnixFileMode.UserWrite)
    let sourceReference =
        {| schema = "fsgg.telemetry.persistent-source-references/3"; profileSha256 = hashes[1]
           nativeSourceVolume = "fixture-native-source"; developmentTarget = "/fixture/development-source"
           collectorReadOnlyTarget = codexHome; readerProfileSha256 = profileSha; captureQualified = false
           verifierRuntimeManifestSha256 = manifestSha |}
    let input =
        (JsonSerializer.SerializeToNode
            {| schema = "fsgg.telemetry.persistent-v3-preparation-input/1"; identityClass = "synthetic-test"
               manager = {| sourceRevision = "aa05817cd3025ead9d574e772d5302be8cf4e2dc"; sourceTree = "02913d7ac8e36d54f88756ee1fc69e04a5a35cce"
                            artifactUri = "https://example.invalid/manager-fixture.tar"; artifactSha256 = hashes[2]; manifestSha256 = hashes[3] |}
               host = {| sourceRevision = "041cfbf6df882fddee3f32f83882ed8f12d75f06"; sourceTree = "f07a5b16b7163d3d07c2c330776cafb7671ba1ba"
                         artifactUri = "https://example.invalid/host-fixture.tar"; artifactSha256 = hashes[4]
                         manifestSha256 = hashes[5]; journalSha256 = hashes[6] |}
               installation = {| schema = "fsgg.telemetry.native-collector-installation/3"; hostConfigPath = config
                                 credentialReference = "collector"; executablePath = collector; executableSha256 = shaFile collector
                                 codexHome = codexHome; evidenceRoot = evidence; provider = "openai"; model = "gpt-test"; effort = "medium" |}
               sourceGrant = {| credentialReference = "collector"; workspaceId = "fixture-workspace"; producerId = "fixture-producer"
                                streamId = "fixture-stream"; role = "native-collector"; grantId = "fixture-grant"; grantGeneration = 1L |}
               sourceReference = sourceReference
               verifierRuntime = {| schema = "fsgg.telemetry.native-verifier-runtime/1"; sourceRevision = revisions[2]
                                    runtimeImageDigest = "sha256:" + hashes[0]; runtimeExecutablePath = runtime
                                    runtimeExecutableSha256 = runtimeSha; modulePath = modulePath; moduleSha256 = moduleSha
                                    manifestPath = manifestPath; manifestSha256 = manifestSha; files = rows |}
               recovery = {| coverage = [| "configuration"; "credentials"; "evidence"; "manager-receipt"; "manager-sidecar";
                                                 "quiesced-store"; "retained-native-source"; "source-references"; "verifier-runtime" |]
                             requiredPrivateInputs = [| "credential-secret"; "evidence"; "host-configuration"; "persistent-store"; "retained-native-source" |] |} |})
            .AsObject()
    input, config

let expectRefused name (input: JsonObject) =
    match Preparation.prepare (Encoding.UTF8.GetBytes(input.ToJsonString())) with
    | Error _ -> printfn "PASS refusal: %s" name
    | Ok _ -> fail ("accepted " + name)

let runProcess executable arguments =
    let start = ProcessStartInfo(executable)
    start.UseShellExecute <- false; start.RedirectStandardOutput <- true; start.RedirectStandardError <- true
    for argument in arguments do start.ArgumentList.Add argument
    use childProcess = Process.Start start
    let stdout = childProcess.StandardOutput.ReadToEnd()
    let stderr = childProcess.StandardError.ReadToEnd()
    childProcess.WaitForExit()
    childProcess.ExitCode, stdout, stderr

let startProcess executable arguments =
    let start = ProcessStartInfo(executable)
    start.UseShellExecute <- false; start.RedirectStandardOutput <- true; start.RedirectStandardError <- true
    for argument in arguments do start.ArgumentList.Add argument
    Process.Start start

let expectBytesRefused name (bytes: byte array) =
    match Preparation.prepare bytes with
    | Error _ -> printfn "PASS refusal: %s" name
    | Ok _ -> fail ("accepted " + name)

[<EntryPoint>]
let main _ =
    let root = Path.Combine(Path.GetTempPath(), "persistent-v3-preparation-tests-" + Guid.NewGuid().ToString("N"))
    try
        let input, config = fixture root
        let prepared =
            match Preparation.prepare (Encoding.UTF8.GetBytes(input.ToJsonString())) with
            | Error error -> fail error
            | Ok bytes -> bytes
        use preparedDocument = JsonDocument.Parse prepared
        let output = preparedDocument.RootElement
        check (output.GetProperty("status").GetString() = "prepared-inactive") "preparation is not inert"
        check (not (output.GetProperty("activationAuthorized").GetBoolean())) "preparation authorized activation"
        check (not (output.GetProperty("installed").GetBoolean())) "preparation claimed installation"
        check (output.GetProperty("identityClass").GetString() = "synthetic-test") "fixture identity was promoted"
        check (output.GetProperty("managerArguments").EnumerateArray() |> Seq.exists (fun x -> x.GetString() = "3")) "manager /3 argument missing"
        check (output.GetProperty("verifierRuntime").GetProperty("moduleSha256").GetString() = canonicalModuleSha) "canonical module pin missing"
        printfn "PASS positive inert preparation"

        let preparationExe = Environment.GetEnvironmentVariable "FSGG_C2_PREPARATION_EXE"
        if String.IsNullOrWhiteSpace preparationExe then fail "FSGG_C2_PREPARATION_EXE is required for actual CLI coverage"
        let inputPath = Path.Combine(root, "preparation-input.json")
        let outputPath = Path.Combine(root, "preparation-output.json")
        File.WriteAllText(inputPath, input.ToJsonString())
        let cliCode, _, cliError = runProcess preparationExe [| "prepare"; inputPath; outputPath |]
        check (cliCode = 0) ("actual preparation CLI refused: " + cliError)
        check (File.ReadAllBytes outputPath = prepared) "actual CLI output differs from F# preparation result"
        check (File.GetUnixFileMode outputPath = (UnixFileMode.UserRead ||| UnixFileMode.UserWrite)) "actual CLI output mode differs"
        let overwriteCode, _, _ = runProcess preparationExe [| "prepare"; inputPath; outputPath |]
        check (overwriteCode <> 0) "actual preparation CLI overwrote an existing result"
        let targetPath = Path.Combine(root, "preserved-target")
        let targetBytes = Encoding.UTF8.GetBytes("preserve-existing-target")
        File.WriteAllBytes(targetPath, targetBytes)
        let symlinkPath = Path.Combine(root, "symlink-output")
        File.CreateSymbolicLink(symlinkPath, targetPath) |> ignore
        let symlinkCode, _, _ = runProcess preparationExe [| "prepare"; inputPath; symlinkPath |]
        check (symlinkCode <> 0 && File.ReadAllBytes(targetPath) = targetBytes) "actual CLI followed existing symlink"
        let danglingPath = Path.Combine(root, "dangling-output")
        File.CreateSymbolicLink(danglingPath, Path.Combine(root, "absent-target")) |> ignore
        let danglingCode, _, _ = runProcess preparationExe [| "prepare"; inputPath; danglingPath |]
        check (danglingCode <> 0 && not (File.Exists(Path.Combine(root, "absent-target")))) "actual CLI followed dangling symlink"
        let competingPath = Path.Combine(root, "competing-output")
        use first = startProcess preparationExe [| "prepare"; inputPath; competingPath |]
        use second = startProcess preparationExe [| "prepare"; inputPath; competingPath |]
        first.WaitForExit(); second.WaitForExit()
        check ([| first.ExitCode; second.ExitCode |] |> Array.sort = [| 0; 2 |]) "exclusive competing create did not admit exactly one writer"
        check (File.ReadAllBytes competingPath = prepared) "competing create changed prepared bytes"
        let unsafeParent = Path.Combine(root, "unsafe-parent")
        Directory.CreateDirectory unsafeParent |> ignore
        privateMode unsafeParent (UnixFileMode.UserRead ||| UnixFileMode.UserWrite ||| UnixFileMode.UserExecute ||| UnixFileMode.GroupRead ||| UnixFileMode.GroupExecute)
        let unsafeCode, _, _ = runProcess preparationExe [| "prepare"; inputPath; Path.Combine(unsafeParent, "output") |]
        check (unsafeCode <> 0) "actual CLI accepted unsafe parent custody"
        let actualParent = Path.Combine(root, "actual-private-parent")
        Directory.CreateDirectory actualParent |> ignore
        privateMode actualParent (UnixFileMode.UserRead ||| UnixFileMode.UserWrite ||| UnixFileMode.UserExecute)
        let linkedParent = Path.Combine(root, "linked-parent")
        Directory.CreateSymbolicLink(linkedParent, actualParent) |> ignore
        let linkedParentCode, _, _ = runProcess preparationExe [| "prepare"; inputPath; Path.Combine(linkedParent, "output") |]
        check (linkedParentCode <> 0 && not (File.Exists(Path.Combine(actualParent, "output")))) "actual CLI accepted linked parent custody"
        printfn "PASS actual preparation CLI exclusive private creation boundaries"

        let literal = input.ToJsonString()
        let duplicate name oldValue first second =
            let original = $"\"{name}\":{oldValue}"
            let replacement = $"\"{name}\":{first},\"{name}\":{second}"
            let changed = literal.Replace(original, replacement, StringComparison.Ordinal)
            check (changed <> literal) ("duplicate fixture did not mutate " + name)
            Encoding.UTF8.GetBytes changed
        expectBytesRefused "duplicate top-level forward" (duplicate "identityClass" "\"synthetic-test\"" "\"synthetic-test\"" "\"public-artifact\"")
        expectBytesRefused "duplicate top-level reverse" (duplicate "identityClass" "\"synthetic-test\"" "\"public-artifact\"" "\"synthetic-test\"")
        expectBytesRefused "duplicate artifact forward" (duplicate "artifactSha256" ($"\"{hashes[2]}\"") ($"\"{hashes[2]}\"") ($"\"{hashes[3]}\""))
        expectBytesRefused "duplicate artifact reverse" (duplicate "artifactSha256" ($"\"{hashes[2]}\"") ($"\"{hashes[3]}\"") ($"\"{hashes[2]}\""))
        expectBytesRefused "duplicate grant forward" (duplicate "grantGeneration" "1" "1" "2")
        expectBytesRefused "duplicate grant reverse" (duplicate "grantGeneration" "1" "2" "1")
        expectBytesRefused "duplicate nested schema forward" (duplicate "schema" "\"fsgg.telemetry.native-collector-installation/3\"" "\"fsgg.telemetry.native-collector-installation/3\"" "\"fsgg.telemetry.native-collector-installation/2\"")
        expectBytesRefused "duplicate nested schema reverse" (duplicate "schema" "\"fsgg.telemetry.native-collector-installation/3\"" "\"fsgg.telemetry.native-collector-installation/2\"" "\"fsgg.telemetry.native-collector-installation/3\"")

        let missing = clone input
        missing["manager"].AsObject().Remove("artifactSha256") |> ignore
        expectRefused "missing public pin" missing
        let v2 = clone input
        v2["installation"]["schema"] <- JsonValue.Create("fsgg.telemetry.native-collector-installation/2")
        expectRefused "v2 installation" v2
        let v2Source = clone input
        v2Source["sourceReference"]["schema"] <- JsonValue.Create("fsgg.telemetry.persistent-source-references/2")
        expectRefused "v2 source reference" v2Source
        let moduleDigest = clone input
        moduleDigest["verifierRuntime"]["moduleSha256"] <- JsonValue.Create(hashes[7])
        expectRefused "foreign verifier module" moduleDigest
        let manifest = clone input
        manifest["sourceReference"]["verifierRuntimeManifestSha256"] <- JsonValue.Create(hashes[8])
        expectRefused "changed manifest binding" manifest
        let runtimeDigest = clone input
        runtimeDigest["verifierRuntime"]["runtimeExecutableSha256"] <- JsonValue.Create(hashes[8])
        expectRefused "changed runtime binding" runtimeDigest
        let runtime = clone input
        let runtimeFiles = (runtime["verifierRuntime"]["files"]).AsArray()
        let dependencyIndex = runtimeFiles |> Seq.findIndex (fun row -> row["path"].GetValue<string>().EndsWith("declared-runtime-dependency.dat"))
        runtimeFiles.RemoveAt(dependencyIndex)
        expectRefused "omitted declared transitive runtime dependency" runtime
        let sourcePath = clone input
        sourcePath["sourceReference"]["collectorReadOnlyTarget"] <- JsonValue.Create("/foreign/codex-home")
        expectRefused "foreign source reference" sourcePath
        let path = clone input
        path["installation"]["codexHome"] <- JsonValue.Create("relative/path")
        expectRefused "unsafe path" path
        let grant = clone input
        grant["sourceGrant"]["credentialReference"] <- JsonValue.Create("foreign-grant")
        expectRefused "wrong grant reference" grant
        let role = clone input
        role["sourceGrant"]["role"] <- JsonValue.Create("publisher")
        expectRefused "wrong grant role" role
        let recovery = clone input
        (recovery["recovery"]["coverage"]).AsArray().RemoveAt(0)
        expectRefused "incomplete recovery coverage" recovery
        let absent = Encoding.UTF8.GetBytes("{}")
        match Preparation.prepare absent with
        | Error message when message.Contains("unavailable") -> printfn "PASS unavailable: absent records"
        | _ -> fail "absent records did not remain UNAVAILABLE"
        let wrongGeneration = clone input
        wrongGeneration["sourceGrant"]["grantGeneration"] <- JsonValue.Create("one")
        expectRefused "wrong-kind grant generation" wrongGeneration
        let wrongBytes = clone input
        ((wrongBytes["verifierRuntime"]["files"]).AsArray()[0])["bytes"] <- JsonValue.Create("many")
        expectRefused "wrong-kind runtime bytes" wrongBytes

        let materializedSource = Convert.FromBase64String(output.GetProperty("sourceReferenceBytesBase64").GetString())
        let materializedManifest = Convert.FromBase64String(output.GetProperty("verifierRuntimeManifestBytesBase64").GetString())
        let emittedVerifier = output.GetProperty("verifierRuntime")
        check (shaBytes materializedSource = output.GetProperty("sourceReferenceSha256").GetString()) "emitted source-reference digest differs"
        check (shaBytes materializedManifest = emittedVerifier.GetProperty("manifestSha256").GetString()) "emitted runtime manifest digest differs"
        write (Path.Combine(root, "source-reference.json")) materializedSource (UnixFileMode.UserRead ||| UnixFileMode.UserWrite)
        write (emittedVerifier.GetProperty("manifestPath").GetString()) materializedManifest (UnixFileMode.UserRead ||| UnixFileMode.UserWrite)

        let manager = Environment.GetEnvironmentVariable "FSGG_C2_MANAGER_EXE"
        if String.IsNullOrWhiteSpace manager then fail "FSGG_C2_MANAGER_EXE is required for actual manager coverage"
        let args = output.GetProperty("managerArguments").EnumerateArray() |> Seq.map _.GetString() |> Seq.toArray
        let managerCode, _, managerError = runProcess manager args
        check (managerCode = 0) ("actual manager /3 refused prepared inputs: " + managerError)
        use sidecar = JsonDocument.Parse(File.ReadAllBytes(config + ".native-collector.json"))
        use receipt = JsonDocument.Parse(File.ReadAllBytes(config + ".native-collector.receipt.json"))
        check (sidecar.RootElement.GetProperty("Schema").GetString() = "fsgg.telemetry.native-collector-installation/3") "actual manager emitted non-v3 sidecar"
        check (receipt.RootElement.GetProperty("schema").GetString() = "fsgg.telemetry.native-collector-installation-receipt/3") "actual manager emitted non-v3 receipt"
        check (not (receipt.RootElement.GetProperty("activationAuthorized").GetBoolean())) "actual manager activated fixture"
        let preparedGrant = output.GetProperty("sourceGrant")
        check (receipt.RootElement.GetProperty("workspaceId").GetString() = preparedGrant.GetProperty("workspaceId").GetString()) "actual manager workspace grant differs"
        check (receipt.RootElement.GetProperty("grantId").GetString() = preparedGrant.GetProperty("grantId").GetString()) "actual manager grant identity differs"
        check (receipt.RootElement.GetProperty("grantGeneration").GetInt64() = preparedGrant.GetProperty("grantGeneration").GetInt64()) "actual manager grant generation differs"
        let wrongVersion = Array.copy args
        let versionIndex = wrongVersion |> Array.findIndex ((=) "--installation-version")
        wrongVersion[versionIndex + 1] <- "2"
        let negativeCode, _, _ = runProcess manager wrongVersion
        check (negativeCode <> 0) "actual manager accepted v2 retag with v3 inputs"
        printfn "PASS actual manager /3 positive and v2-retag negative"
        printfn "Persistent v3 preparation: PASS"
        if Directory.Exists root then Directory.Delete(root, true)
        0
    with ex ->
        eprintfn "%s" (ex.Message)
        if Directory.Exists root then Directory.Delete(root, true)
        1
