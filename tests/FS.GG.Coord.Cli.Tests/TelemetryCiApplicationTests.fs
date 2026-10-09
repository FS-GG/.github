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

    let private profile rule =
        System.Text.Encoding.UTF8.GetBytes("{\"schema\":\"fsgg.telemetry.ci-attribution/1\",\"rules\":[" + rule + "]}")

    let private exactRule =
        """{"workflow":".github/workflows/ci.yml","job":"build","step":"test","classification":"mixed","rationale":"Validation and policy work execute in one native step."}"""

    [<Fact>]
    let ``UTEL-CI-01 exact tuple matching preserves mixed and refuses aliases or malformed profiles`` () =
        let classify bytes workflow job step = TelemetryCiApplication.classifyProfileForTesting bytes workflow job step
        let bytes = profile exactRule
        Assert.Equal(Ok("mixed", "Validation and policy work execute in one native step."), classify bytes ".github/workflows/ci.yml" "build" "test")
        for workflow, job, step in [ "ci.yml", "build", "test"; ".github/workflows/ci.yml", "renamed", "test"; ".github/workflows/ci.yml", "build", "renamed" ] do
            Assert.Equal(Ok("unclassified", "no exact attribution rule"), classify bytes workflow job step)
        for bad in [ exactRule + "," + exactRule; exactRule + "," + exactRule.Replace("mixed", "admin"); exactRule.Replace("\"job\"", "\"extra\":true,\"job\""); exactRule.Replace("\"job\":\"build\"", "\"job\":\"build\",\"job\":\"build\""); exactRule.Replace(".github/workflows/ci.yml", "ci.yml"); exactRule.Replace("mixed", "unsupported") ] do
            match classify (profile bad) ".github/workflows/ci.yml" "build" "test" with
            | Error _ -> ()
            | Ok _ -> failwith "malformed/duplicate profile must refuse"

    [<Fact>]
    let ``UTEL-CI-03 native shaped mixed population profile recovery and replay use disposable store`` () =
        let head = String.replicate 40 "a"
        let baseSha = String.replicate 40 "d"
        let run id event relations =
            $"""{{"id":{id},"run_attempt":1,"path":".github/workflows/ci.yml","event":"{event}","head_sha":"{head}","repository":{{"id":42,"full_name":"o/r"}},"status":"completed","conclusion":"success","created_at":"2026-01-01T00:00:00Z","run_started_at":"2026-01-01T00:00:01Z","updated_at":"2026-01-01T00:01:01Z","pull_requests":[{relations}]}}"""
        let relation = $"""{{"number":7,"head":{{"sha":"{head}","repo":{{"id":42,"full_name":"o/r"}}}},"base":{{"sha":"{baseSha}","repo":{{"id":42,"full_name":"o/r"}}}}}}"""
        let rows = String.concat "," [ run 1 "pull_request" "{\"number\":7}"; run 2 "pull_request_target" relation; run 3 "pull_request_target" "" ]
        let runs = "{\"total_count\":3,\"workflow_runs\":[" + rows + "]}"
        let jobs id = $"""{{"total_count":1,"jobs":[{{"id":{id},"name":"build","status":"completed","conclusion":"success","started_at":"2026-01-01T00:00:10Z","completed_at":"2026-01-01T00:00:50Z","steps":[{{"number":1,"name":"test","status":"completed","conclusion":"success","started_at":"2026-01-01T00:00:10Z","completed_at":"2026-01-01T00:00:50Z"}},{{"number":2,"name":"skipped","status":"completed","conclusion":"skipped"}}]}}]}}"""
        let checks = "{\"total_count\":0,\"check_runs\":[]}"
        let queue = System.Collections.Generic.Queue<string>([ $"""{{"head":{{"sha":"{head}"}},"base":{{"ref":"main","sha":"{baseSha}"}}}}"""; runs; jobs 201; jobs 202; checks; runs; checks ])
        let transport =
            { new FS.GG.Coord.GitHub.Transport.ISinglePageGitHubTransport with
                member _.SendSingle _ =
                    Ok { Status = 200; Body = queue.Dequeue(); Headers = Map.empty; ETag = None; NextLink = None } }
        let unwrap = function Ok value -> value | Error error -> failwithf "%A" error
        let population = FS.GG.Coord.GitHub.CiReads.discoverPopulation transport "https://api.github.com" "o" "r" 7 head "main" baseSha false |> unwrap
        Assert.Empty queue
        Assert.Equal(3, population.Snapshot.Runs.Length)
        Assert.Equal(2, population.Snapshot.Jobs.Length)
        Assert.Contains("target-association:3:1:Missing", population.Gaps)
        let assignment: FS.GG.Coord.TelemetryCi.Assignment =
            { FeatureId = "UTEL"; ItemId = "UTEL-CI-GAPS"; AttemptId = "fixture"; ParentAttemptId = None; ProducerStream = "routine-delivery" }
        let project bytes = TelemetryCiApplication.projectPopulationForTesting bytes assignment population |> unwrap
        let missing = project None
        let malformed = project (Some(System.Text.Encoding.UTF8.GetBytes "{"))
        let available = project (Some(profile exactRule))
        let events batches =
            batches |> List.collect (fun (bytes: byte array) ->
                use doc = JsonDocument.Parse bytes
                doc.RootElement.GetProperty("events").EnumerateArray() |> Seq.map (fun node -> node.Clone()) |> Seq.toList)
        Assert.DoesNotContain(events missing, fun node -> node.GetProperty("kind").GetString() = "ci-step")
        Assert.DoesNotContain(events malformed, fun node -> node.GetProperty("kind").GetString() = "ci-step")
        Assert.DoesNotContain(events available, fun node -> node.GetProperty("kind").GetString() = "ci-run" && node.GetProperty("runId").GetInt64() = 3L)
        Assert.Contains(events available, fun node -> node.GetProperty("kind").GetString() = "diagnostic" && node.GetProperty("code").GetString().Contains(":sha256:"))
        let root = Path.Combine(Path.GetTempPath(), "fsgg-ci-journey-" + Guid.NewGuid().ToString("N"))
        try
            let approved = FS.GG.Coord.TelemetryStore.ApprovedLocalDurable
            TelemetryStoreApplication.initialize root approved |> unwrap |> ignore
            for batch in missing @ malformed @ available @ available do
                let result = TelemetryStoreApplication.ingest root approved batch |> unwrap
                Assert.DoesNotContain("conflict", result)
            let summary = TelemetryStoreApplication.ciSummary root approved assignment.ItemId |> unwrap
            Assert.Contains("\"mixedSeconds\":40", summary)
            Assert.Contains("\"runnerSeconds\":80", summary)
            Assert.Contains("\"steps\":4", summary)
            Assert.Contains("\"usefulValidationSeconds\":null", summary)
        finally
            if Directory.Exists root then Directory.Delete(root, true)

    [<Fact>]
    let ``UTEL-CI-01 maintained profile identities exist in current workflow source`` () =
        let rec repositoryRoot (path: string) =
            if File.Exists(Path.Combine(path, ".fsgg", "telemetry-ci-attribution.json")) then path
            else
                let parent = Directory.GetParent path
                if isNull parent then failwith "repository profile prerequisite is missing"
                repositoryRoot parent.FullName
        let root = repositoryRoot AppContext.BaseDirectory
        let bytes = File.ReadAllBytes(Path.Combine(root, ".fsgg", "telemetry-ci-attribution.json"))
        use document = JsonDocument.Parse bytes
        for rule in document.RootElement.GetProperty("rules").EnumerateArray() do
            let workflow, job, step = rule.GetProperty("workflow").GetString(), rule.GetProperty("job").GetString(), rule.GetProperty("step").GetString()
            let source = File.ReadAllText(Path.Combine(root, workflow))
            Assert.Contains("  " + job + ":", source)
            Assert.Contains("- name: " + step, source)
            let classified = TelemetryCiApplication.classifyProfileForTesting bytes workflow job step
            match classified with
            | Ok(classification, _) -> Assert.Equal(rule.GetProperty("classification").GetString(), classification)
            | Error errors -> failwithf "%A" errors
        Assert.DoesNotContain("Run tests", System.Text.Encoding.UTF8.GetString bytes)

    [<Fact>]
    let ``UTEL-CI-03 retained native target empty join stays unknown`` () =
        // Sanitized read-only native evidence captured on 2026-10-09; not a causal admission.
        let retained = """
{
  "observedAt": "2026-10-09T21:22:05.299284+00:00",
  "endpoint": "repos/FS-GG/.github/actions/runs/37984850593",
  "projection": {
    "check_suite_id": 102923027629,
    "created_at": "2026-10-09T20:07:27Z",
    "display_title": "chore: adopt main-backed ordinary settlement",
    "event": "pull_request_target",
    "head_branch": "routine/ordinary-main-adoption-preparation-20261009",
    "head_repository": {
      "full_name": "FS-GG/.github",
      "id": 1269292704
    },
    "head_sha": "4b4432095f8d58aa689dd26d617461e1d5d075ef",
    "id": 37984850593,
    "path": ".github/workflows/routine-eligibility.yml",
    "pull_requests": [],
    "repository": {
      "full_name": "FS-GG/.github",
      "id": 1269292704
    },
    "run_attempt": 1,
    "updated_at": "2026-10-09T20:08:23Z"
  },
  "scope": "Read-only evidence; no collection or native acceptance"
}
"""
        use document = JsonDocument.Parse retained
        let row = document.RootElement.GetProperty("projection")
        Assert.Equal(37984850593L, row.GetProperty("id").GetInt64())
        Assert.Empty(row.GetProperty("pull_requests").EnumerateArray())
        Assert.Equal(FS.GG.Coord.GitHub.CiReads.Missing,
            FS.GG.Coord.GitHub.CiReads.targetAssociation "FS-GG/.github" 4342
                (row.GetProperty("head_sha").GetString())
                (Some "31795a80e706fc4203c27e421f9e3648a1dd1329") row)

    // Sanitized native GET evidence: run37997746345/attempt1 and jobs/check114047987738,
    // captured 2026-10-09. This controlled subpopulation does not claim whole-head inventory.
    let private retainedPositiveTarget = """
    {
      "id": 37997746345,
      "run_attempt": 1,
      "path": ".github/workflows/routine-eligibility.yml",
      "event": "pull_request_target",
      "head_sha": "b6be731e8d726fb13d46a034bd0ea60fc01baf2a",
      "status": "completed",
      "conclusion": "success",
      "created_at": "2026-10-09T22:10:29Z",
      "run_started_at": "2026-10-09T22:10:29Z",
      "updated_at": "2026-10-09T22:10:36Z",
      "pull_requests": [
        {
          "number": 4344,
          "head": {
            "sha": "b6be731e8d726fb13d46a034bd0ea60fc01baf2a",
            "repo": {
              "id": 1269292704
            }
          },
          "base": {
            "sha": "a3415321ec33946d66ad1f39469935d01206bb98",
            "repo": {
              "id": 1269292704
            }
          }
        }
      ],
      "repository": {
        "id": 1269292704,
        "full_name": "FS-GG/.github"
      },
      "head_repository": {
        "id": 1269292704,
        "full_name": "FS-GG/.github"
      }
    }
    """
    let private retainedPositiveJobs = """
    {
      "total_count": 1,
      "jobs": [
        {
          "id": 114047987738,
          "name": "routine-eligibility",
          "status": "completed",
          "conclusion": "success",
          "created_at": "2026-10-09T22:10:30Z",
          "started_at": "2026-10-09T22:10:31Z",
          "completed_at": "2026-10-09T22:10:36Z",
          "check_run_url": "https://api.github.com/repos/FS-GG/.github/check-runs/114047987738",
          "steps": [
            {
              "name": "Set up job",
              "status": "completed",
              "conclusion": "success",
              "number": 1,
              "started_at": "2026-10-09T22:10:32Z",
              "completed_at": "2026-10-09T22:10:32Z"
            },
            {
              "name": "Evaluate with the PR base's policy and validator",
              "status": "completed",
              "conclusion": "success",
              "number": 2,
              "started_at": "2026-10-09T22:10:32Z",
              "completed_at": "2026-10-09T22:10:35Z"
            },
            {
              "name": "Complete job",
              "status": "completed",
              "conclusion": "success",
              "number": 3,
              "started_at": "2026-10-09T22:10:35Z",
              "completed_at": "2026-10-09T22:10:35Z"
            }
          ]
        }
      ]
    }
    """
    let private retainedPositiveChecks = """
    {
      "total_count": 1,
      "check_runs": [
        {
          "id": 114047987738,
          "name": "routine-eligibility",
          "status": "completed",
          "conclusion": "success",
          "started_at": "2026-10-09T22:10:31Z",
          "completed_at": "2026-10-09T22:10:36Z",
          "app": {
            "slug": "github-actions"
          }
        }
      ]
    }
    """
    let private retainedHead = "b6be731e8d726fb13d46a034bd0ea60fc01baf2a"
    let private retainedBase = "a3415321ec33946d66ad1f39469935d01206bb98"
    let private retainedPr =
        $"""{{"head":{{"sha":"{retainedHead}"}},"base":{{"ref":"main","sha":"{retainedBase}"}}}}"""
    let private retainedRuns = "{\"total_count\":1,\"workflow_runs\":[" + retainedPositiveTarget + "]}"
    let private retainedPositiveProfile = """{
  "schema": "fsgg.telemetry.ci-attribution/1",
  "rules": [
    {
      "workflow": ".github/workflows/coord-engine.yml",
      "job": "engine",
      "step": "Install the pinned SDD provider validator",
      "classification": "necessary-setup",
      "rationale": "Installs the pinned provider needed by the subsequent source validation."
    },
    {
      "workflow": ".github/workflows/coord-engine.yml",
      "job": "engine",
      "step": "Restore (locked)",
      "classification": "necessary-setup",
      "rationale": "Restores the locked dependencies used by the validation suites."
    },
    {
      "workflow": ".github/workflows/coord-engine.yml",
      "job": "engine",
      "step": "Build the engine",
      "classification": "necessary-setup",
      "rationale": "Builds the candidate engine consumed by subsequent production-route qualification."
    },
    {
      "workflow": ".github/workflows/coord-engine.yml",
      "job": "engine",
      "step": "Test — core telemetry contracts and source binding",
      "classification": "useful-validation",
      "rationale": "Runs the independent Core contract suite against the selected source revision."
    },
    {
      "workflow": ".github/workflows/coord-engine.yml",
      "job": "engine",
      "step": "Test — standalone telemetry HTTPS contracts and host boundaries",
      "classification": "useful-validation",
      "rationale": "Runs the standalone telemetry transport and host boundary contract suite."
    },
    {
      "workflow": ".github/workflows/coord-engine.yml",
      "job": "engine",
      "step": "Test the shared projection and real loopback browser",
      "classification": "mixed",
      "rationale": "Bundles npm/browser installation with dashboard and browser validation; native evidence cannot split the interval."
    },
    {
      "workflow": ".github/workflows/routine-eligibility.yml",
      "job": "routine-eligibility",
      "step": "Evaluate with the PR base's policy and validator",
      "classification": "mixed",
      "rationale": "Bundles trusted base/head source fetching and routine policy assessment in one native step."
    }
  ]
}
"""

    [<Fact>]
    let ``UTEL-CI-03 retained native positive target classifies mixed and replays in disposable store`` () =
        let unwrap = function Ok value -> value | Error error -> failwithf "%A" error
        let queue = System.Collections.Generic.Queue<string>([ retainedPr; retainedRuns; retainedPositiveJobs;
                                                              retainedPositiveChecks; retainedRuns; retainedPositiveChecks ])
        let requests = ResizeArray<FS.GG.Coord.GitHub.Transport.Request>()
        let transport =
            { new FS.GG.Coord.GitHub.Transport.ISinglePageGitHubTransport with
                member _.SendSingle request =
                    requests.Add request
                    Ok { Status = 200; Body = queue.Dequeue(); Headers = Map.empty; ETag = None; NextLink = None } }
        let population = FS.GG.Coord.GitHub.CiReads.discoverPopulation transport "https://api.github.com" "FS-GG" ".github" 4344 retainedHead "main" retainedBase false |> unwrap
        Assert.Empty queue
        Assert.Empty population.Gaps
        let profileBytes = System.Text.Encoding.UTF8.GetBytes retainedPositiveProfile
        let contents = $"""{{"type":"file","path":".fsgg/telemetry-ci-attribution.json","encoding":"base64","content":"{Convert.ToBase64String profileBytes}"}}"""
        queue.Enqueue contents
        let selectedProfile = FS.GG.Coord.GitHub.CiReads.readAttributionProfile transport "FS-GG/.github" retainedHead |> unwrap
        Assert.Equal(Some profileBytes, selectedProfile)
        Assert.Equal("repos/FS-GG/.github/contents/.fsgg/telemetry-ci-attribution.json", requests[6].Path)
        Assert.Equal<(string * string) list>([ "ref", retainedHead ], requests[6].Query)
        let assignment: FS.GG.Coord.TelemetryCi.Assignment =
            { FeatureId = "UTEL"; ItemId = "UTEL-CI-GAPS"; AttemptId = "retained-native-positive"; ParentAttemptId = None; ProducerStream = "routine-delivery" }
        let project bytes = TelemetryCiApplication.projectPopulationForTesting bytes assignment population |> unwrap
        let missing, available = project None, project selectedProfile
        let events batches =
            batches |> List.collect (fun (bytes: byte array) ->
                use document = JsonDocument.Parse bytes
                document.RootElement.GetProperty("events").EnumerateArray() |> Seq.map (fun node -> node.Clone()) |> Seq.toList)
        let steps = events available |> List.filter (fun node -> node.GetProperty("kind").GetString() = "ci-step")
        Assert.Equal(3, steps.Length)
        let mixed = Assert.Single(steps |> List.filter (fun node -> node.GetProperty("classification").GetString() = "mixed"))
        Assert.Equal(37997746345L, mixed.GetProperty("runId").GetInt64())
        Assert.Equal(1, mixed.GetProperty("attempt").GetInt32())
        Assert.Equal(114047987738L, mixed.GetProperty("jobId").GetInt64())
        Assert.Equal("Evaluate with the PR base's policy and validator", mixed.GetProperty("name").GetString())
        Assert.All(steps |> List.filter (fun node -> node.GetProperty("number").GetInt32() <> 2), fun node -> Assert.Equal("unclassified", node.GetProperty("classification").GetString()))
        Assert.DoesNotContain(events missing, fun node -> node.GetProperty("kind").GetString() = "ci-step")
        Assert.Contains(events available, fun node -> node.GetProperty("kind").GetString() = "diagnostic" && node.GetProperty("code").GetString().Contains("3b6ceefa6e1ca1273fdd37bdf1817cd4a79aa771f8e89c5757544f489f0c513c"))
        let root = Path.Combine(Path.GetTempPath(), "fsgg-native-target-" + Guid.NewGuid().ToString("N"))
        try
            let approved = FS.GG.Coord.TelemetryStore.ApprovedLocalDurable
            TelemetryStoreApplication.initialize root approved |> unwrap |> ignore
            for batch in missing @ available @ available do
                TelemetryStoreApplication.ingest root approved batch |> unwrap |> fun result -> Assert.DoesNotContain("conflict", result)
            let summary = TelemetryStoreApplication.ciSummary root approved assignment.ItemId |> unwrap
            Assert.Contains("\"mixedSeconds\":3", summary)
            Assert.Contains("\"runnerSeconds\":5", summary)
            Assert.Contains("\"steps\":3", summary)
            Assert.Contains("\"usefulValidationSeconds\":null", summary)
        finally
            if Directory.Exists root then Directory.Delete(root, true)
