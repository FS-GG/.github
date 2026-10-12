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

    let private continuationFixture storeId =
        let head = String.replicate 40 "a"
        let baseSha = String.replicate 40 "d"
        let now = DateTimeOffset.Parse "2026-10-12T00:00:00Z"
        let bytes = profile exactRule
        let digest bytes = FS.GG.Coord.TelemetryCi.ProviderCheckpoint.digest bytes
        let assignment: FS.GG.Coord.TelemetryCi.Assignment =
            { FeatureId = "UTEL"; ItemId = "UTEL-C3-PHASE"; AttemptId = "fixture"; ParentAttemptId = None; ProducerStream = "routine-delivery" }
        let identity: FS.GG.Coord.TelemetryCi.ProviderCheckpoint.Identity =
            { Assignment = assignment; AssignmentDigest = digest (System.Text.Encoding.UTF8.GetBytes "assignment")
              StoreId = storeId; Collection = digest (System.Text.Encoding.UTF8.GetBytes($"o/r\u001f{head}\u001f7\u001f{assignment.ItemId}\u001fall-workflows"))
              ApiOrigin = "https://api.github.com"; Repository = "o/r"; RepositoryId = 42L; PullRequest = 7L
              BaseRef = "main"; BaseSha = baseSha; Head = head; ProfileDigest = digest bytes; ParserContract = "provider/1" }
        let transport =
            { new FS.GG.Coord.GitHub.Transport.IDeadlineSinglePageGitHubTransport with
                member _.SendSingleWithin(request, _) =
                    let body =
                        if request.Path.Contains("/contents/") then
                            $"""{{"type":"file","path":".fsgg/telemetry-ci-attribution.json","encoding":"base64","content":"{Convert.ToBase64String bytes}"}}"""
                        elif request.Path.Contains("/pulls/") then
                            $"""{{"number":7,"head":{{"sha":"{head}"}},"base":{{"ref":"main","sha":"{baseSha}","repo":{{"id":42,"full_name":"o/r"}}}}}}"""
                        elif request.Path.EndsWith("/check-suites") then "{\"total_count\":0,\"check_suites\":[]}"
                        else "{\"total_count\":0,\"workflow_runs\":[]}"
                    Ok { Status = 200; Body = body; Headers = Map.empty; ETag = None; NextLink = None } }
        let unwrap = function Ok value -> value | Error errors -> failwithf "%A" errors
        let mutable state = None
        let mutable terminal = None
        for _ in 1 .. 10 do
            if terminal.IsNone then
                let result = FS.GG.Coord.GitHub.CiReads.resumePopulation transport identity (fun () -> now) false 0L state |> unwrap
                state <- Some result.Checkpoint
                if result.Population.IsSome then terminal <- Some result
        let result = terminal |> Option.defaultWith (fun () -> failwith "fixture did not stabilize")
        now, bytes, identity, result

    [<Fact>]
    let ``C3 typed continuation prepares exact fence graph seal phases without effects`` () =
        let now, bytes, identity, result = continuationFixture "disposable-store"
        let digest bytes = FS.GG.Coord.TelemetryCi.ProviderCheckpoint.digest bytes
        let unwrap = function Ok value -> value | Error errors -> failwithf "%A" errors
        let project expected cp = TelemetryCiApplication.projectContinuationForTesting expected now false 0L (Some bytes) [] cp result.Population.Value
        let fence, graph, seal = project identity result.Checkpoint |> unwrap
        let replay = project identity result.Checkpoint |> unwrap
        Assert.Equal((fence, graph, seal), replay)
        let pending = TelemetryCiApplication.prepareContinuationPendingForTesting identity now [] result.Checkpoint (fence, graph, seal) |> unwrap
        let exact, phase, batch = TelemetryCiApplication.recoverContinuationPendingForTesting identity now [] pending |> unwrap
        Assert.Equal(0, phase)
        Assert.Equal(0, batch)
        Assert.Equal<byte array>(List.head fence, exact.Value)
        let preparedFixtureFacts =
            (fence @ graph @ seal) |> List.collect (fun payload ->
                (FS.GG.Coord.TelemetryStore.parseBatch payload |> unwrap).Facts)
        let scope: FS.GG.Coord.TelemetryReceipt.Scope = { Workspace = "fixture"; Producer = "collector"; Stream = "routine-delivery" }
        let receipt payload status changedDigest changedProducer changedStream =
            let node = System.Text.Json.Nodes.JsonObject()
            node["schema"] <- FS.GG.Coord.TelemetryReceipt.Schema
            node["workspaceId"] <- scope.Workspace
            node["producerId"] <- scope.Producer
            node["streamId"] <- scope.Stream
            node["batchId"] <- (FS.GG.Coord.TelemetryStore.parseBatch payload |> unwrap).IngestId
            node["payload"] <- System.Text.Json.Nodes.JsonNode.Parse(payload)
            let envelope = FS.GG.Coord.TelemetryReceipt.parse (System.Text.Encoding.UTF8.GetBytes(node.ToJsonString())) |> unwrap
            JsonSerializer.Serialize
                {| schema = "fsgg.telemetry.receipt/1"; workspaceId = scope.Workspace
                   producerId = changedProducer |> Option.defaultValue scope.Producer
                   streamId = changedStream |> Option.defaultValue scope.Stream; batchId = envelope.BatchId
                   digest = changedDigest |> Option.defaultValue envelope.Digest; status = status; code = (None: string option) |}
        let first = List.head fence
        for wrong in [ receipt first "durably-received" None None None
                       receipt first "applied" (Some(String.replicate 64 "0")) None None
                       receipt first "applied" None (Some "foreign-producer") None
                       receipt first "applied" None None (Some "foreign-principal-stream") ] do
            let unchanged, complete = TelemetryCiApplication.advanceContinuationPendingForTesting identity now scope preparedFixtureFacts pending wrong |> unwrap
            Assert.False complete
            Assert.Equal<byte array>(pending, unchanged)
        let noFacts, noFactsComplete = TelemetryCiApplication.advanceContinuationPendingForTesting identity now scope [] pending (receipt first "applied" None None None) |> unwrap
        Assert.False noFactsComplete
        Assert.Equal<byte array>(pending, noFacts)
        let mutable replayState = pending
        let mutable completed = false
        for payload in fence @ graph @ seal do
            let exactPayload, _, _ = TelemetryCiApplication.recoverContinuationPendingForTesting identity now preparedFixtureFacts replayState |> unwrap
            Assert.Equal<byte array>(payload, exactPayload.Value)
            let next, complete = TelemetryCiApplication.advanceContinuationPendingForTesting identity now scope preparedFixtureFacts replayState (receipt payload "applied" None None None) |> unwrap
            replayState <- next
            completed <- complete
        Assert.True completed
        let promoted = FS.GG.Coord.TelemetryCi.ProviderCheckpoint.parse identity now replayState |> unwrap
        Assert.Equal(FS.GG.Coord.TelemetryCi.ProviderCheckpoint.Stable, promoted.Phase)
        Assert.Equal("", promoted.PendingBatchDigest)
        Assert.Empty promoted.FactReferences
        let saved = FS.GG.Coord.TelemetryCi.ProviderCheckpoint.parse identity now pending |> unwrap
        let claim = System.Text.Json.Nodes.JsonNode.Parse(Convert.FromBase64String saved.PendingBatchBase64).AsObject()
        claim["phase"] <- 3
        claim["batch"] <- 0
        let claimedBytes = System.Text.Encoding.UTF8.GetBytes(claim.ToJsonString()) |> TelemetryCiApplication.normalizePendingBytesForTesting
        let forgedDone =
            { saved with PendingBatchDigest = digest claimedBytes; PendingBatchBase64 = Convert.ToBase64String claimedBytes }
            |> FS.GG.Coord.TelemetryCi.ProviderCheckpoint.encode identity now |> unwrap
        match TelemetryCiApplication.recoverContinuationPendingForTesting identity now [] forgedDone with
        | Error _ -> ()
        | Ok _ -> failwith "private pending phase cannot declare receiver completion"
        for changed in [ { identity with StoreId = "other-store" }; { identity with Head = String.replicate 40 "f" } ] do
            match TelemetryCiApplication.recoverContinuationPendingForTesting changed now [] pending with
            | Error _ -> ()
            | Ok _ -> failwith "pending private state cannot rebind custody/candidate"
        match TelemetryCiApplication.prepareContinuationPendingForTesting identity now [] result.Checkpoint (graph, fence, seal) with
        | Error _ -> ()
        | Ok _ -> failwith "phase order must refuse before persistence"
        let events batches =
            batches |> List.collect (fun (payload: byte array) ->
                Assert.InRange(payload.Length, 1, FS.GG.Coord.TelemetryStore.MaxBatchBytes)
                Assert.Equal(byte '\n', payload[payload.Length - 1])
                use doc = JsonDocument.Parse payload
                doc.RootElement.GetProperty("events").EnumerateArray() |> Seq.map _.Clone() |> Seq.toList)
        let fenceEvents, graphEvents, sealEvents = events fence, events graph, events seal
        Assert.Contains(fenceEvents, fun node -> node.GetProperty("kind").GetString() = "ci-population-coverage" && node.GetProperty("checks").GetString() = "partial")
        Assert.Contains(graphEvents, fun node -> node.GetProperty("kind").GetString() = "ci-suite-page/1" && node.GetProperty("nativeCount").GetInt32() = 0)
        Assert.DoesNotContain(graphEvents, fun node -> node.GetProperty("kind").GetString() = "ci-suite-seal/1")
        Assert.Equal("ci-suite-seal/1", (List.exactlyOne sealEvents).GetProperty("kind").GetString())
        for invalid in [ { identity with Head = String.replicate 40 "f" }; { identity with StoreId = "other-store" } ] do
            match project invalid result.Checkpoint with Error _ -> () | Ok _ -> failwith "changed custody/scope must refuse"
        match project identity { result.Checkpoint with Phase = FS.GG.Coord.TelemetryCi.ProviderCheckpoint.FirstSweep } with
        | Error _ -> ()
        | Ok _ -> failwith "unfinished provider stability must refuse"

    [<Fact>]
    let ``C3 LF normalized intent bytes are immutable and idempotent`` () =
        let input = System.Text.Encoding.UTF8.GetBytes "{}"
        let normalized = TelemetryCiApplication.normalizePendingBytesForTesting input
        Assert.Equal<byte array>([| byte '{'; byte '}'; byte '\n' |], normalized)
        Assert.Equal<byte array>(normalized, TelemetryCiApplication.normalizePendingBytesForTesting normalized)
        normalized[0] <- byte 'x'
        Assert.Equal<byte array>(System.Text.Encoding.UTF8.GetBytes "{}", input)

    let private withContinuationPrivateDirectory action =
        let directory = Path.Combine(Path.GetTempPath(), "fsgg-ci-custody-" + Guid.NewGuid().ToString("N"))
        Directory.CreateDirectory(directory, enum<UnixFileMode> 0o700) |> ignore
        try action directory finally Directory.Delete(directory, true)

    let private continuationManySuiteTransport (profileBytes: byte array) (expected: FS.GG.Coord.TelemetryCi.ProviderCheckpoint.Identity) observe repositoryId =
            { new FS.GG.Coord.GitHub.Transport.IDeadlineSinglePageGitHubTransport with
                member _.SendSingleWithin(request, remaining) =
                    observe ()
                    Assert.Equal("GET", request.Method)
                    Assert.True(remaining > TimeSpan.Zero && remaining <= TimeSpan.FromSeconds 30.0)
                    let body =
                        if request.Path = "repos/o/r" then
                            $"""{{"id":{repositoryId ()},"full_name":"o/r"}}"""
                        elif request.Path.Contains("/contents/") then
                            $"""{{"type":"file","path":".fsgg/telemetry-ci-attribution.json","encoding":"base64","content":"{Convert.ToBase64String profileBytes}"}}"""
                        elif request.Path.Contains("/pulls/") then
                            $"""{{"number":7,"head":{{"sha":"{expected.Head}"}},"base":{{"ref":"main","sha":"{expected.BaseSha}","repo":{{"id":42,"full_name":"o/r"}}}}}}"""
                        elif request.Path.EndsWith("/check-suites") then
                            let suites =
                                [ 1 .. 41 ] |> List.map (fun id ->
                                    $"""{{"id":{id},"head_sha":"{expected.Head}","repository":{{"id":42,"full_name":"o/r"}},"app":{{"id":1,"slug":"github-actions"}},"status":"completed","conclusion":"success","check_runs_url":"https://api.github.com/repos/o/r/check-suites/{id}/check-runs"}}""") |> String.concat ","
                            $"""{{"total_count":41,"check_suites":[{suites}]}}"""
                        elif request.Path.Contains("/check-runs") then "{\"total_count\":0,\"check_runs\":[]}"
                        else "{\"total_count\":0,\"workflow_runs\":[]}"
                    Ok { Status = 200; Body = body; Headers = Map.empty; ETag = None; NextLink = None } }

    [<Fact>]
    let ``C3 acquisition meters identity profile and multi invocation native graph together`` () =
        let unwrap = function Ok value -> value | Error errors -> failwithf "%A" errors
        let now, profileBytes, expected, _ = continuationFixture "disposable-store"
        let mutable calls = 0
        let mutable repositoryId = 42L
        let transport = continuationManySuiteTransport profileBytes expected (fun () -> calls <- calls + 1) (fun () -> repositoryId)
        let mutable state = None
        let mutable actual = { expected with RepositoryId = 0L; ProfileDigest = "" }
        let mutable terminal = None
        let mutable invocations = 0
        let mutable total = 0
        while terminal.IsNone && invocations < 10 do
            calls <- 0
            let identity, bytes, result, requests =
                TelemetryCiApplication.acquireContinuationForTesting transport actual (fun () -> now) false 0L state |> unwrap
            Assert.Equal(calls, requests)
            Assert.InRange(requests, 1, 40)
            Assert.Equal<byte array>(profileBytes, bytes)
            if invocations = 0 then
                Assert.Equal(40, requests)
                Assert.True(result.Population.IsNone)
            actual <- identity
            state <- Some result.Checkpoint
            total <- total + requests
            invocations <- invocations + 1
            if result.Population.IsSome then terminal <- Some result
        Assert.True(invocations > 1 && total > 40)
        let complete = terminal |> Option.defaultWith (fun () -> failwith "bounded native graph did not stabilize")
        Assert.Equal(41, complete.Checkpoint.Resources |> List.filter (fun resource -> resource.Key.Kind = FS.GG.Coord.TelemetryCi.ProviderCheckpoint.SuiteChecks) |> List.length)
        repositoryId <- 43L
        calls <- 0
        match TelemetryCiApplication.acquireContinuationForTesting transport actual (fun () -> now) false 0L state with
        | Error _ -> Assert.Equal(1, calls)
        | Ok _ -> failwith "native repository substitution must refuse before dependent profile or graph requests"

    [<Fact>]
    let ``C3 retained collection joins bounded acquisition to authenticated graph publication`` () =
        withContinuationPrivateDirectory (fun directory ->
            let unwrap = function Ok value -> value | Error errors -> failwithf "%A" errors
            let approved = FS.GG.Coord.TelemetryStore.ApprovedLocalDurable
            let path = Path.Combine(directory, "receiver")
            TelemetryStoreApplication.initialize path approved |> unwrap |> ignore
            let scope: FS.GG.Coord.TelemetryReceipt.Scope =
                { Workspace = "fixture"; Producer = "collector"; Stream = "routine-delivery" }
            TelemetryStoreApplication.provisionReceiptWorkspace path approved scope.Workspace |> unwrap |> ignore
            TelemetryStoreApplication.enrollReceiptProducer path approved scope |> unwrap |> ignore
            let storeId, _, _, _ = TelemetryCiApplication.receiverContinuationReadbackForTesting path approved "UTEL-C3-PHASE" scope |> unwrap
            let now, profileBytes, expected, _ = continuationFixture storeId
            let expected = { expected with RepositoryId = 0L; ProfileDigest = "" }
            let mutable calls = 0
            let transport = continuationManySuiteTransport profileBytes expected (fun () -> calls <- calls + 1) (fun () -> 42L)
            let submitted = ResizeArray<string>()
            let publish (payload: byte array) =
                let batch = FS.GG.Coord.TelemetryStore.parseBatch payload |> unwrap
                submitted.Add batch.IngestId
                let node = System.Text.Json.Nodes.JsonObject()
                node["schema"] <- FS.GG.Coord.TelemetryReceipt.Schema
                node["workspaceId"] <- scope.Workspace
                node["producerId"] <- scope.Producer
                node["streamId"] <- scope.Stream
                node["batchId"] <- batch.IngestId
                node["payload"] <- System.Text.Json.Nodes.JsonNode.Parse(payload)
                TelemetryStoreApplication.submitReceipt path approved scope (System.Text.Encoding.UTF8.GetBytes(node.ToJsonString()))
            let drain () = TelemetryStoreApplication.drainReceipts path approved scope.Workspace |> Result.map ignore
            let name = "continuation-" + String.replicate 64 "c"
            let mutable terminal = None
            for _ in 1 .. 10 do
                if terminal.IsNone then
                    calls <- 0
                    let status, requests, checkpoint =
                        TelemetryCiApplication.continueCollectionForTesting directory name path approved expected (fun () -> now) scope transport publish drain ignore |> unwrap
                    Assert.Equal(calls, requests)
                    Assert.InRange(requests, 1, 40)
                    let retained = FS.GG.Coord.TelemetryCi.ProviderCheckpoint.parse checkpoint.Identity now (File.ReadAllBytes(Path.Combine(directory, name + ".json"))) |> unwrap
                    Assert.Equal(checkpoint, retained)
                    if status = "complete" then terminal <- Some checkpoint
            Assert.True(terminal.IsSome)
            Assert.Equal(submitted.Count, submitted |> Seq.distinct |> Seq.length)
            let _, _, _, applied = TelemetryCiApplication.receiverContinuationReadbackForTesting path approved expected.Assignment.ItemId scope |> unwrap
            let seal = applied |> List.filter (fun fact -> fact.Kind = "ci-suite-seal/1") |> List.exactlyOne
            Assert.True(seal.Revision > 0L)
            let summary = TelemetryStoreApplication.ciSummary path approved expected.Assignment.ItemId |> unwrap
            use summaryDocument = JsonDocument.Parse summary
            Assert.Equal("complete", summaryDocument.RootElement.GetProperty("checkCoverage").GetString())
            calls <- 0
            let status, requests, _ =
                TelemetryCiApplication.continueCollectionForTesting directory name path approved expected (fun () -> now) scope transport publish drain ignore |> unwrap
            Assert.True(calls > 0)
            Assert.Equal(calls, requests)
            Assert.Equal("pending", status))

    [<Fact>]
    let ``C3 configured CLI refuses current binding drift and completes retained receiver journey`` () =
        withContinuationPrivateDirectory (fun directory ->
            let unwrap = function Ok value -> value | Error errors -> failwithf "%A" errors
            let approved = FS.GG.Coord.TelemetryStore.ApprovedLocalDurable
            let path = Path.Combine(directory, "receiver")
            TelemetryStoreApplication.initialize path approved |> unwrap |> ignore
            let scope: FS.GG.Coord.TelemetryReceipt.Scope =
                { Workspace = "fixture"; Producer = "collector"; Stream = "routine-delivery" }
            TelemetryStoreApplication.provisionReceiptWorkspace path approved scope.Workspace |> unwrap |> ignore
            TelemetryStoreApplication.enrollReceiptProducer path approved scope |> unwrap |> ignore
            let storeId, _, _, _ = TelemetryCiApplication.receiverContinuationReadbackForTesting path approved "UTEL-C3-PHASE" scope |> unwrap
            let now, profileBytes, expected, _ = continuationFixture storeId
            let config = Path.Combine(directory, "workspace.json")
            let assignment = Path.Combine(directory, "assignment.json")
            let delivery = Path.Combine(directory, "delivery.json")
            let assignmentBytes =
                System.Text.Encoding.UTF8.GetBytes(
                    """{"schema":"fsgg.telemetry.ci-assignment/1","featureId":"UTEL","itemId":"UTEL-C3-PHASE","attemptId":"fixture","parentAttemptId":null,"producerStream":"routine-delivery"}""")
            let privateWrite (target: string) (bytes: byte array) =
                File.WriteAllBytes(target, bytes)
                File.SetUnixFileMode(target, enum<UnixFileMode> 0o600)
            privateWrite assignment assignmentBytes
            File.WriteAllText(delivery,
                $"""{{"schema":"fsgg.routine-delivery/v1","repo":"o/r","pr":7,"baseRef":"main","baseSha":"{expected.BaseSha}","expectedHead":"{expected.Head}","observedHead":"{expected.Head}","outcome":"ready","codeDelivery":"not-delivered","mergeCommit":null,"outcomeAt":null,"observedAt":"2026-10-12T00:00:00Z"}}""")
            File.SetUnixFileMode(delivery, enum<UnixFileMode> 0o644)
            privateWrite config (System.Text.Encoding.UTF8.GetBytes
                $"""{{"schema":"fsgg.telemetry.workspace-config/1","engine":"fsgg-coord-engine","associations":[{{"workspaceId":"{scope.Workspace}","producerId":"{scope.Producer}","streamId":"{scope.Stream}","repositories":["o/r"],"destination":{{"kind":"local","storeRoot":"{path}"}}}}],"retiredAssociations":[]}}""")
            let mutable calls = 0
            let mutable changeAssignment = false
            let transport =
                continuationManySuiteTransport profileBytes expected
                    (fun () ->
                        calls <- calls + 1
                        if changeAssignment then
                            changeAssignment <- false
                            privateWrite assignment (System.Text.Encoding.UTF8.GetBytes
                                """{"schema":"fsgg.telemetry.ci-assignment/1","featureId":"UTEL","itemId":"UTEL-C3-PHASE","attemptId":"changed","parentAttemptId":null,"producerStream":"routine-delivery"}"""))
                    (fun () -> 42L)
            let args = [ "--config"; config; "--repository"; "o/r"; "--assignment"; assignment; "--delivery"; delivery ]
            let invoke () =
                let oldOut, oldError = Console.Out, Console.Error
                use stdout = new StringWriter()
                use stderr = new StringWriter()
                try
                    Console.SetOut stdout
                    Console.SetError stderr
                    let code = TelemetryCiApplication.runConfiguredContinuationForTesting approved (fun () -> now) transport args
                    code, stdout.ToString(), stderr.ToString()
                finally
                    Console.SetOut oldOut
                    Console.SetError oldError
            privateWrite assignment (System.Text.Encoding.UTF8.GetBytes(System.Text.Encoding.UTF8.GetString(assignmentBytes).Replace("routine-delivery", "foreign-principal")))
            let wrongCode, _, wrongError = invoke ()
            Assert.Equal(1, wrongCode)
            Assert.Contains("ci-continuation-principal-stream-binding", wrongError)
            Assert.Equal(0, calls)
            privateWrite assignment assignmentBytes
            changeAssignment <- true
            let driftCode, _, driftError = invoke ()
            Assert.Equal(1, driftCode)
            Assert.Contains("ci-continuation-current-input-drift", driftError)
            Assert.Equal(1, calls)
            Assert.Empty(Directory.GetFiles(path, "continuation-*.json"))
            privateWrite assignment assignmentBytes
            let mutable complete = false
            for _ in 1 .. 10 do
                if not complete then
                    calls <- 0
                    let code, json, error = invoke ()
                    Assert.True((code = 0), error)
                    use output = JsonDocument.Parse json
                    Assert.Equal(calls, output.RootElement.GetProperty("nativeRequests").GetInt32())
                    Assert.InRange(calls, 1, 40)
                    complete <- output.RootElement.GetProperty("status").GetString() = "complete"
            Assert.True complete
            let _, _, _, facts = TelemetryCiApplication.receiverContinuationReadbackForTesting path approved expected.Assignment.ItemId scope |> unwrap
            Assert.Contains(facts, fun fact -> fact.Kind = "ci-suite-seal/1")
            use summary = JsonDocument.Parse(TelemetryStoreApplication.ciSummary path approved expected.Assignment.ItemId |> unwrap)
            Assert.Equal("complete", summary.RootElement.GetProperty("checkCoverage").GetString()))

    [<Fact>]
    let ``C3 durable actor resumes authenticated receiver custody without resubmission`` () =
        withContinuationPrivateDirectory (fun directory ->
            let unwrap = function Ok value -> value | Error errors -> failwithf "%A" errors
            let approved = FS.GG.Coord.TelemetryStore.ApprovedLocalDurable
            let path = Path.Combine(directory, "receiver")
            TelemetryStoreApplication.initialize path approved |> unwrap |> ignore
            let scope: FS.GG.Coord.TelemetryReceipt.Scope =
                { Workspace = "fixture"; Producer = "collector"; Stream = "routine-delivery" }
            TelemetryStoreApplication.provisionReceiptWorkspace path approved scope.Workspace |> unwrap |> ignore
            TelemetryStoreApplication.enrollReceiptProducer path approved scope |> unwrap |> ignore
            let storeId, contract, _, facts =
                TelemetryCiApplication.receiverContinuationReadbackForTesting path approved "UTEL-C3-PHASE" scope |> unwrap
            Assert.Equal("fsgg.telemetry.ci-suite-receiver/1", contract)
            Assert.Empty facts
            let now, profileBytes, identity, result = continuationFixture storeId
            let phases =
                TelemetryCiApplication.projectContinuationForTesting identity now false 0L (Some profileBytes) [] result.Checkpoint result.Population.Value |> unwrap
            let pending = TelemetryCiApplication.prepareContinuationPendingForTesting identity now [] result.Checkpoint phases |> unwrap
            let name = "continuation-" + String.replicate 64 "b"
            let retainedPath = Path.Combine(directory, name + ".json")
            let submitted = ResizeArray<string>()
            let publish (payload: byte array) =
                let batch = FS.GG.Coord.TelemetryStore.parseBatch payload |> unwrap
                submitted.Add batch.IngestId
                let node = System.Text.Json.Nodes.JsonObject()
                node["schema"] <- FS.GG.Coord.TelemetryReceipt.Schema
                node["workspaceId"] <- scope.Workspace
                node["producerId"] <- scope.Producer
                node["streamId"] <- scope.Stream
                node["batchId"] <- batch.IngestId
                node["payload"] <- System.Text.Json.Nodes.JsonNode.Parse(payload)
                TelemetryStoreApplication.submitReceipt path approved scope (System.Text.Encoding.UTF8.GetBytes(node.ToJsonString()))
            let mutable drains = 0
            let drain () =
                drains <- drains + 1
                TelemetryStoreApplication.drainReceipts path approved scope.Workspace |> Result.map ignore
            let run hook expected =
                TelemetryCiApplication.publishContinuationPendingForTesting directory name path approved expected (fun () -> now) scope pending publish drain hook
            match run ignore { identity with StoreId = "foreign-store" } with
            | Error _ -> Assert.False(File.Exists retainedPath); Assert.Empty submitted
            | Ok _ -> failwith "wrong receiver must refuse before private persistence or publication"
            let crashAt boundary stage = if stage = boundary then failwith ("fixture-crash-" + boundary)
            match run (crashAt "after-publication") identity with
            | Error errors -> Assert.Contains("fixture-crash-after-publication", errors)
            | Ok _ -> failwith "publication crash control must interrupt"
            Assert.Equal(1, submitted.Count)
            Assert.Equal(0, drains)
            Assert.Equal<byte array>(pending, File.ReadAllBytes retainedPath)
            match run (crashAt "after-drain") identity with
            | Error errors -> Assert.Contains("fixture-crash-after-drain", errors)
            | Ok _ -> failwith "drain crash control must interrupt"
            Assert.Equal(1, submitted.Count)
            Assert.Equal(1, drains)
            Assert.Equal<byte array>(pending, File.ReadAllBytes retainedPath)
            match run (crashAt "before-checkpoint-write") identity with
            | Error errors -> Assert.Contains("fixture-crash-before-checkpoint-write", errors)
            | Ok _ -> failwith "applied readback crash control must interrupt"
            Assert.Equal(1, submitted.Count)
            Assert.Equal(1, drains)
            Assert.Equal<byte array>(pending, File.ReadAllBytes retainedPath)
            let stableBytes, status = run ignore identity |> unwrap
            Assert.Equal("complete", status)
            let fence, graph, seal = phases
            Assert.Equal(fence.Length + graph.Length + seal.Length, submitted.Count)
            Assert.Equal(submitted.Count, submitted |> Seq.distinct |> Seq.length)
            Assert.Equal<byte array>(stableBytes, File.ReadAllBytes retainedPath)
            let checkpoint = FS.GG.Coord.TelemetryCi.ProviderCheckpoint.parse identity now stableBytes |> unwrap
            Assert.Equal(FS.GG.Coord.TelemetryCi.ProviderCheckpoint.Stable, checkpoint.Phase)
            let _, _, _, applied =
                TelemetryCiApplication.receiverContinuationReadbackForTesting path approved identity.Assignment.ItemId scope |> unwrap
            Assert.Contains(applied, fun fact -> fact.Kind = "ci-suite-seal/1")
            let publishesAtCompletion = submitted.Count
            match run ignore identity with
            | Error _ -> Assert.Equal(publishesAtCompletion, submitted.Count)
            | Ok _ -> failwith "stable private checkpoint must not infer fresh provider completion")

    [<Fact>]
    let ``C3 private publication lock is exclusive and retains the original inode`` () =
        withContinuationPrivateDirectory (fun directory ->
            let result =
                TelemetryCiApplication.continuationPrivateCustodyForTesting directory (fun guard ->
                    guard ()
                    match TelemetryCiApplication.continuationPrivateCustodyForTesting directory (fun _ -> Ok()) with
                    | Error errors -> Assert.Contains("ci-continuation-publication-busy", errors)
                    | Ok _ -> failwith "concurrent publication lock must refuse"
                    guard ()
                    Ok "guarded")
            Assert.Equal<Result<string, string list>>(Ok "guarded", result)
            Assert.True(File.Exists(Path.Combine(directory, "publication.lock"))))

    [<Fact>]
    let ``C3 private publication refuses replaced named lock despite retained descriptor`` () =
        withContinuationPrivateDirectory (fun directory ->
            let result =
                TelemetryCiApplication.continuationPrivateCustodyForTesting directory (fun guard ->
                    let path = Path.Combine(directory, "publication.lock")
                    File.Move(path, path + ".original")
                    File.WriteAllBytes(path, Array.empty)
                    File.SetUnixFileMode(path, enum<UnixFileMode> 0o600)
                    guard ()
                    Ok())
            match result with
            | Error errors -> Assert.Contains("ci-continuation-private-lock-custody-lost", errors)
            | Ok _ -> failwith "replaced named inode must refuse")

    [<Fact>]
    let ``C3 private publication refuses symlink and writable ancestry before callback`` () =
        withContinuationPrivateDirectory (fun directory ->
            let mutable entered = false
            let run path = TelemetryCiApplication.continuationPrivateCustodyForTesting path (fun _ -> entered <- true; Ok())
            let child = Path.Combine(directory, "child")
            Directory.CreateDirectory(child, enum<UnixFileMode> 0o700) |> ignore
            File.SetUnixFileMode(directory, enum<UnixFileMode> 0o702)
            match run child with Error _ -> () | Ok _ -> failwith "writable ancestor must refuse"
            File.SetUnixFileMode(directory, enum<UnixFileMode> 0o700)
            let alias = Path.Combine(directory, "alias")
            Directory.CreateSymbolicLink(alias, child) |> ignore
            match run alias with Error _ -> () | Ok _ -> failwith "symlink directory must refuse"
            Assert.False entered)

    [<Fact>]
    let ``C3 durable intent uses exact normalized bytes through atomic writer and readback`` () =
        withContinuationPrivateDirectory (fun directory ->
            let name = "continuation-" + String.replicate 64 "a"
            let input = System.Text.Encoding.UTF8.GetBytes "{}"
            let expected = System.Text.Encoding.UTF8.GetBytes "{}\n"
            let persist bytes =
                match TelemetryCiApplication.continuationExactIntentForTesting directory name bytes with
                | Ok actual -> actual
                | Error errors -> failwithf "%A" errors
            Assert.Equal<byte array>(expected, persist input)
            Assert.Equal<byte array>(expected, File.ReadAllBytes(Path.Combine(directory, name + ".json")))
            Assert.Equal<byte array>(expected, persist expected)
            Assert.Equal(enum<UnixFileMode> 0o600, File.GetUnixFileMode(Path.Combine(directory, name + ".json"))))

    [<Fact>]
    let ``C3 durable intent refuses symlink and normalized bound before replacing bytes`` () =
        withContinuationPrivateDirectory (fun directory ->
            let name = "continuation-" + String.replicate 64 "b"
            let target = Path.Combine(directory, "target")
            File.WriteAllText(target, "unchanged")
            let path = Path.Combine(directory, name + ".json")
            File.CreateSymbolicLink(path, target) |> ignore
            let run bytes = TelemetryCiApplication.continuationExactIntentForTesting directory name bytes
            match run [| byte '{'; byte '}' |] with Error _ -> () | Ok _ -> failwith "symlink existing intent must refuse"
            Assert.Equal("unchanged", File.ReadAllText target)
            Assert.NotNull(FileInfo(path).LinkTarget)
            File.Delete path
            match run (Array.create (8 * 1024 * 1024) (byte 'x')) with Error _ -> () | Ok _ -> failwith "normalized bundle exceeds admitted8MiB"
            Assert.False(File.Exists path))
