namespace FS.GG.Coord.Cli.Tests

open System
open System.IO
open System.Text.Json
open Xunit
open FS.GG.Coord.Cli

module TelemetryCiApplicationTests =
    let private fixture outcome delivery observedHead =
        let root =
            Path.Combine(Path.GetTempPath(), "fsgg-ci-workspace-" + Guid.NewGuid().ToString("N"))

        Directory.CreateDirectory root |> ignore
        let assignment = Path.Combine(root, "assignment.json")
        let result = Path.Combine(root, "delivery.json")

        File.WriteAllText(
            assignment,
            """{"schema":"fsgg.telemetry.ci-assignment/1","featureId":"L1","itemId":"L1-CI","attemptId":"attempt-1","parentAttemptId":null,"producerStream":"routine-delivery"}"""
        )

        if not (OperatingSystem.IsWindows()) then
            File.SetUnixFileMode(assignment, UnixFileMode.UserRead ||| UnixFileMode.UserWrite)

        let observed =
            observedHead |> Option.map (sprintf "\"%s\"") |> Option.defaultValue "null"

        File.WriteAllText(
            result,
            $"""{{"schema":"fsgg.routine-delivery/v1","repo":"o/r","pr":7,"baseRef":"main","baseSha":"dddddddddddddddddddddddddddddddddddddddd","expectedHead":"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa","observedHead":{observed},"outcome":"{outcome}","codeDelivery":"{delivery}","mergeCommit":null,"outcomeAt":null,"observedAt":"2026-09-09T10:04:01Z"}}"""
        )

        { new IDisposable with
            member _.Dispose() = Directory.Delete(root, true)
        },
        assignment,
        result

    let private invoke publish drain localRoot args =
        let priorOut, priorError = Console.Out, Console.Error
        use stdout = new StringWriter()
        use stderr = new StringWriter()

        try
            Console.SetOut stdout
            Console.SetError stderr

            let code =
                TelemetryCiApplication.runWithWorkspaceForTesting
                    (fun _ _ -> Ok "binding")
                    publish
                    drain
                    localRoot
                    "reconcile"
                    args

            code, stdout.ToString(), stderr.ToString()
        finally
            Console.SetOut priorOut
            Console.SetError priorError

    [<Fact>]
    let ``delivered remote reconciliation publishes native outcome and remains admission pending`` () =
        let cleanup, assignment, delivery =
            fixture "delivered" "delivered" (Some(String.replicate 40 "a"))

        use cleanup = cleanup
        let published = ResizeArray<byte array>()

        let publish binding bytes =
            Assert.Equal("binding", binding)
            published.Add bytes
            Ok "durably-received"

        let code, stdout, error =
            invoke
                publish
                (fun _ -> Ok "drained")
                (fun _ -> Ok None)
                [
                    "--assignment"
                    assignment
                    "--delivery"
                    delivery
                    "--config"
                    "/private/config.json"
                    "--repository"
                    "o/r"
                ]

        Assert.True((code = 0), error)
        Assert.Single published |> ignore
        use batch = JsonDocument.Parse(published[0])
        let event = batch.RootElement.GetProperty("events")[0]
        Assert.Equal("native-item-outcome", event.GetProperty("kind").GetString())
        Assert.Equal("delivered", event.GetProperty("outcome").GetString())
        Assert.Contains("\"status\":\"pending\"", stdout)
        Assert.Contains("remote-admission-query-unavailable", stdout)

    [<Fact>]
    let ``remote admission still refuses a non-ready non-exact initial witness`` () =
        let cleanup, assignment, delivery =
            fixture "refused" "not-delivered" (Some(String.replicate 40 "c"))

        use cleanup = cleanup
        let mutable publications = 0

        let code, _, error =
            invoke
                (fun _ _ ->
                    publications <- publications + 1
                    Ok "queued")
                (fun _ -> Ok "drained")
                (fun _ -> Ok None)
                [
                    "--assignment"
                    assignment
                    "--delivery"
                    delivery
                    "--config"
                    "/private/config.json"
                    "--repository"
                    "o/r"
                ]

        Assert.Equal(1, code)
        Assert.Equal(1, publications)
        Assert.Contains("first CI population admission requires ready", error)

    [<Fact>]
    let ``explicit missing environment config cannot fall back to legacy store`` () =
        let cleanup, assignment, delivery =
            fixture "delivered" "delivered" (Some(String.replicate 40 "a"))

        use cleanup = cleanup

        let priorConfig, priorStore =
            Environment.GetEnvironmentVariable("FSGG_TELEMETRY_CONFIG"),
            Environment.GetEnvironmentVariable("FSGG_TELEMETRY_STORE")

        Environment.SetEnvironmentVariable("FSGG_TELEMETRY_CONFIG", "/missing/selected-workspace.json")
        Environment.SetEnvironmentVariable("FSGG_TELEMETRY_STORE", "/otherwise-valid-legacy")
        let mutable resolved = 0

        try
            let priorOut, priorError = Console.Out, Console.Error
            use stdout = new StringWriter()
            use stderr = new StringWriter()

            try
                Console.SetOut stdout
                Console.SetError stderr

                let code =
                    TelemetryCiApplication.runWithWorkspaceForTesting
                        (fun _ _ ->
                            resolved <- resolved + 1
                            Error [ "unconfigured" ])
                        (fun _ _ -> failwith "publish")
                        (fun _ -> failwith "drain")
                        (fun _ -> failwith "local")
                        "reconcile"
                        [ "--assignment"; assignment; "--delivery"; delivery ]

                Assert.Equal(1, code)
                Assert.Equal(1, resolved)
            finally
                Console.SetOut priorOut
                Console.SetError priorError
        finally
            Environment.SetEnvironmentVariable("FSGG_TELEMETRY_CONFIG", priorConfig)
            Environment.SetEnvironmentVariable("FSGG_TELEMETRY_STORE", priorStore)

    let private withDefaultConfig (contents: string) test =
        let root = Path.Combine(Path.GetTempPath(), "fsgg-ci-config-" + Guid.NewGuid().ToString("N"))
        let directory = Path.Combine(root, "fs-gg")
        Directory.CreateDirectory directory |> ignore
        let path = Path.Combine(directory, "telemetry.json")
        File.WriteAllText(path, contents)
        if not (OperatingSystem.IsWindows()) then
            File.SetUnixFileMode(path, UnixFileMode.UserRead ||| UnixFileMode.UserWrite)
        let names = [ "XDG_CONFIG_HOME"; "FSGG_TELEMETRY_CONFIG"; "FSGG_TELEMETRY_STORE" ]
        let prior = names |> List.map (fun name -> name, Environment.GetEnvironmentVariable name)
        try
            Environment.SetEnvironmentVariable("XDG_CONFIG_HOME", root)
            Environment.SetEnvironmentVariable("FSGG_TELEMETRY_CONFIG", null)
            Environment.SetEnvironmentVariable("FSGG_TELEMETRY_STORE", null)
            test path
        finally
            prior |> List.iter (fun (name, value) -> Environment.SetEnvironmentVariable(name, value))
            Directory.Delete(root, true)

    let private targetDecision action args =
        let priorOut, priorError = Console.Out, Console.Error
        use stdout = new StringWriter()
        use stderr = new StringWriter()
        try
            Console.SetOut stdout
            Console.SetError stderr
            // Inject durability and omit required collection inputs: selection is exercised,
            // but no store, API, producer or Host operation can run.
            let code =
                TelemetryCiApplication.runWithAssessment
                    FS.GG.Coord.TelemetryStore.ApprovedLocalDurable
                    action
                    args
            code, stderr.ToString()
        finally
            Console.SetOut priorOut
            Console.SetError priorError

    let private legacyConfig =
        JsonSerializer.Serialize
            {| schema = "fsgg.telemetry.host-config/1"
               storeRoot = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "legacy-ci-store"))
               engine = "fsgg-coord-engine" |}

    [<Theory>]
    [<InlineData("collect")>]
    [<InlineData("reconcile")>]
    let ``implicit legacy host default allows explicit legacy store selection`` action =
        withDefaultConfig legacyConfig (fun _ ->
            let code, error = targetDecision action [ "--store-root"; "/private/selected-store" ]
            Assert.Equal(1, code)
            Assert.Contains(action + " requires", error)
            Assert.DoesNotContain("cannot both be selected", error))

    [<Theory>]
    [<InlineData("collect")>]
    [<InlineData("reconcile")>]
    let ``implicit workspace default still refuses an explicit legacy store`` action =
        withDefaultConfig
            """{"schema":"fsgg.telemetry.workspace-config/1","engine":"fsgg-coord-engine","associations":[],"retiredAssociations":[]}"""
            (fun _ ->
                let code, error = targetDecision action [ "--store-root"; "/private/selected-store" ]
                Assert.Equal(1, code)
                Assert.Contains("workspace config and legacy store root cannot both be selected", error))

    [<Theory>]
    [<InlineData("collect", false)>]
    [<InlineData("collect", true)>]
    [<InlineData("reconcile", false)>]
    [<InlineData("reconcile", true)>]
    let ``explicit and environment configs cannot fall back to legacy store`` action fromEnvironment =
        withDefaultConfig legacyConfig (fun path ->
            let missing = path + ".missing"
            let selection =
                if fromEnvironment then
                    Environment.SetEnvironmentVariable("FSGG_TELEMETRY_CONFIG", missing)
                    []
                else
                    [ "--config"; missing ]
            let code, error = targetDecision action (selection @ [ "--store-root"; "/private/selected-store" ])
            Assert.Equal(1, code)
            Assert.Contains("workspace config and legacy store root cannot both be selected", error))

    [<Theory>]
    [<InlineData("collect", "{")>]
    [<InlineData("reconcile", "{")>]
    [<InlineData("collect", "{\"schema\":\"other/1\"}")>]
    [<InlineData("reconcile", "{\"schema\":\"other/1\"}")>]
    [<InlineData("collect", "{\"schema\":\"fsgg.telemetry.host-config/1\",\"schema\":\"fsgg.telemetry.host-config/1\",\"storeRoot\":\"/private/legacy\",\"engine\":\"fsgg-coord-engine\"}")>]
    [<InlineData("reconcile", "{\"schema\":\"fsgg.telemetry.host-config/1\",\"schema\":\"fsgg.telemetry.host-config/1\",\"storeRoot\":\"/private/legacy\",\"engine\":\"fsgg-coord-engine\"}")>]
    [<InlineData("collect", "{\"schema\":\"fsgg.telemetry.host-config/1\",\"storeRoot\":\"relative\",\"engine\":\"fsgg-coord-engine\"}")>]
    [<InlineData("reconcile", "{\"schema\":\"fsgg.telemetry.host-config/1\",\"storeRoot\":\"relative\",\"engine\":\"fsgg-coord-engine\"}")>]
    let ``invalid implicit defaults refuse rather than falling back to legacy`` action contents =
        withDefaultConfig contents (fun _ ->
            let code, error = targetDecision action [ "--store-root"; "/private/selected-store" ]
            Assert.Equal(1, code)
            Assert.Contains("configuration-", error)
            Assert.DoesNotContain(action + " requires", error))

    [<Theory>]
    [<InlineData("collect", false, false)>]
    [<InlineData("collect", false, true)>]
    [<InlineData("collect", true, false)>]
    [<InlineData("collect", true, true)>]
    [<InlineData("reconcile", false, false)>]
    [<InlineData("reconcile", false, true)>]
    [<InlineData("reconcile", true, false)>]
    [<InlineData("reconcile", true, true)>]
    let ``invalid selected config reaches resolver without environment store fallback`` action fromEnvironment malformed =
        withDefaultConfig "{" (fun path ->
            let selected = if malformed then path else path + ".missing"
            Environment.SetEnvironmentVariable("FSGG_TELEMETRY_STORE", "/private/legacy")
            let args =
                if fromEnvironment then
                    Environment.SetEnvironmentVariable("FSGG_TELEMETRY_CONFIG", selected)
                    []
                else
                    [ "--config"; selected ]
            let mutable resolutions = 0
            let priorOut, priorError = Console.Out, Console.Error
            use stdout = new StringWriter()
            use stderr = new StringWriter()
            try
                Console.SetOut stdout
                Console.SetError stderr
                let code =
                    TelemetryCiApplication.runWithWorkspaceForTesting
                        (fun config _ ->
                            Assert.Equal((if fromEnvironment then None else Some selected), config)
                            resolutions <- resolutions + 1
                            Error [ "selected-config-invalid" ])
                        (fun (_: string) _ -> failwith "publication must not run")
                        (fun _ -> failwith "drain must not run")
                        (fun _ -> failwith "local binding must not run")
                        action
                        args
                Assert.Equal(1, code)
                Assert.Equal(1, resolutions)
                Assert.Contains("selected-config-invalid", stderr.ToString())
            finally
                Console.SetOut priorOut
                Console.SetError priorError)

    [<Theory>]
    [<InlineData("collect")>]
    [<InlineData("reconcile")>]
    let ``missing explicit config argument refuses before any default resolution`` action =
        withDefaultConfig legacyConfig (fun _ ->
            let code, error = targetDecision action [ "--config" ]
            Assert.Equal(1, code)
            Assert.Contains("configuration-path-required", error))

    [<Theory>]
    [<InlineData("collect")>]
    [<InlineData("reconcile")>]
    let ``whitespace environment config cannot silently select legacy store`` action =
        withDefaultConfig legacyConfig (fun _ ->
            Environment.SetEnvironmentVariable("FSGG_TELEMETRY_CONFIG", " ")
            let code, error = targetDecision action [ "--store-root"; "/private/selected-store" ]
            Assert.Equal(1, code)
            Assert.Contains("workspace config and legacy store root cannot both be selected", error))

    [<Fact>]
    let ``UTEL-06.8 explicit remote correction refuses with zero local fallback publication or drain`` () =
        let priorOut, priorError = Console.Out, Console.Error
        use stdout = new StringWriter()
        use stderr = new StringWriter()
        let mutable effects = 0
        try
            Console.SetOut stdout
            Console.SetError stderr
            let code =
                TelemetryCiApplication.runWithWorkspaceForTesting
                    (fun _ _ -> Ok "remote")
                    (fun _ _ -> effects <- effects + 1; Ok "published")
                    (fun _ -> effects <- effects + 1; Ok "drained")
                    (fun _ -> Ok None)
                    "correct"
                    [ "--config"; "/private/selected-remote.json"; "--repository"; "o/r"; "--plan"; "/private/never-read-plan.json" ]
            Assert.Equal(1, code)
            Assert.Contains("ci-attribution-correction-remote-unsupported", stderr.ToString())
            Assert.Equal("", stdout.ToString())
            Assert.Equal(0, effects)
        finally
            Console.SetOut priorOut
            Console.SetError priorError
