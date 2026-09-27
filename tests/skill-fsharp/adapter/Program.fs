namespace FS.GG.Coord.Cli.AdapterHarness

open System
open System.IO
open System.Security.Cryptography
open System.Text
open System.Text.Json
open FS.GG.Coord.Cli
open FS.GG.Coord.Cli.SkillTelemetryReaders
open FS.GG.Coord.Cli.SkillTelemetryAdapter

module Program =
    let private require condition message = if not condition then failwith message
    let private text (bytes: byte array) = Encoding.UTF8.GetString bytes

    let private option name (args: string array) =
        args
        |> Array.tryFindIndex ((=) name)
        |> Option.bind (fun index -> Array.tryItem (index + 1) args)

    let private fakeEngine (args: string array) =
        match option "--input" args, Environment.GetEnvironmentVariable "FSGG_ADAPTER_TEST_LOG" with
        | Some input, log when not (String.IsNullOrEmpty log) ->
            let encoded = File.ReadAllBytes input |> Convert.ToBase64String
            File.AppendAllText(log, encoded + "\n")
        | _ -> ()
        let failOnce = Environment.GetEnvironmentVariable "FSGG_ADAPTER_TEST_FAIL_ONCE"
        if not (String.IsNullOrEmpty failOnce) && not (File.Exists failOnce) && Array.contains "publish" args then
            File.WriteAllText(failOnce, "failed")
            Console.Error.WriteLine "delivery outcome unknown"
            1
        elif Array.contains "workspace" args && (Array.contains "status" args || Array.contains "binding" args) then
            let stateRoot = Environment.GetEnvironmentVariable "SKILL_FS_01_STATE_ROOT"
            let configPath = option "--config" args |> Option.defaultValue ""
            let repository = option "--repository" args |> Option.defaultValue ""
            Console.Out.WriteLine("{\"schema\":\"fsgg.telemetry.workspace-binding/1\",\"configPath\":\"" + configPath.Replace("\\", "\\\\") + "\",\"repository\":\"" + repository + "\",\"producerId\":\"fixture-association\",\"bindingDigest\":\"" + String.replicate 64 "a" + "\",\"destination\":\"remote\",\"privateStateRoot\":\"" + stateRoot.Replace("\\", "\\\\") + "\"}")
            0
        elif Array.contains "publisher-event" args then
            Console.Out.WriteLine "{\"schema\":\"fsgg.telemetry.dashboard-event-health/1\",\"status\":\"ready\",\"reason\":null,\"observedAt\":\"2026-09-27T00:00:00Z\",\"publicRevision\":null,\"commit\":null}"
            0
        elif Array.contains "submit" args then
            Console.Out.WriteLine "applied"
            0
        else
            Console.Out.WriteLine "{}"
            0

    let private config root =
        let store = Path.Combine(root, "store")
        Directory.CreateDirectory store |> ignore
        if not (OperatingSystem.IsWindows()) then File.SetUnixFileMode(store, enum<UnixFileMode> 0o700)
        { Path = Path.Combine(root, "telemetry.json")
          StoreRoot = store
          Engine = Environment.ProcessPath
          Repository = None
          Workspace = false
          Producer = None
          BindingDigest = None
          CredentialReference = None }

    let private resultJson result =
        use document = JsonDocument.Parse result.Stdout
        document.RootElement.Clone()

    let private lifecycle root =
        let host = config root
        let beginResult =
            run (Some host) (Begin("SKILL-FS-01", "SKILL-FS-01.3", None, "attempt-a", None, None, "root", "fixture-producer", "gpt-6-sol", "medium", 60))
        require (beginResult.ExitCode = 0) (text beginResult.Stderr)
        let token = (resultJson beginResult).GetProperty("token").GetString()
        require (token.Length = 32) "begin did not return a durable token"
        let startedResult = run (Some host) (Started(token, "native-agent-a"))
        require (startedResult.ExitCode = 0) (text startedResult.Stderr)
        let finishResult = run (Some host) (Finish(token, "completed", None))
        require (finishResult.ExitCode = 0) (text finishResult.Stderr)
        let terminal = resultJson finishResult
        require (terminal.GetProperty("status").GetString() = "terminal") "finish did not become terminal"
        require (terminal.GetProperty("coverage").GetString() = "native-collaboration-usage-unsupported") "missing usage became measured"
        let retry = run (Some host) (Finish(token, "completed", None))
        require (retry.ExitCode = 0) "idempotent finish failed"
        let changed = run (Some host) (Finish(token, "failed", None))
        require (changed.ExitCode = 1 && (text changed.Stderr).Contains "durable terminal intent") "changed terminal retry was accepted"
        token

    let private observations root token =
        let host = config root
        let activity = Path.Combine(root, "activity.json")
        File.WriteAllText(activity, "{\"schema\":\"fsgg.telemetry.activity-span-input/1\",\"revision\":0,\"activityId\":\"activity-a\",\"category\":\"validation\",\"startedAt\":\"2026-09-27T00:00:00Z\",\"endedAt\":\"2026-09-27T00:00:01Z\",\"clockProvenance\":\"fixture\",\"evidence\":[],\"summary\":\"fixture\"}")
        let complication = Path.Combine(root, "complication.json")
        File.WriteAllText(complication, "{\"schema\":\"fsgg.telemetry.complication-input/1\",\"revision\":0,\"complicationId\":\"complication-a\",\"activityId\":\"activity-a\",\"trigger\":\"fixture\",\"cause\":\"fixture\",\"occurredAt\":\"2026-09-27T00:00:01Z\",\"synopsis\":\"fixture\",\"evidence\":[]}")
        let usage = Path.Combine(root, "usage.json")
        File.WriteAllText(usage, "{\"schema\":\"fsgg.telemetry.activity-usage-attribution-input/1\",\"revision\":0,\"usageIdentity\":\"usage-a\",\"activityId\":\"activity-a\",\"classification\":\"unknown\",\"input\":0,\"cachedInput\":0,\"output\":0,\"reasoning\":0,\"total\":0}")
        let review = Path.Combine(root, "review.json")
        File.WriteAllText(review, "{\"schema\":\"fsgg.telemetry.process-review-input/1\",\"revision\":0,\"outcomeSynopsis\":\"fixture\",\"wentWell\":[],\"problems\":[],\"avoidableDelayOrRework\":[],\"processObservations\":[],\"remainingRisks\":[],\"concreteImprovements\":[],\"evidence\":[],\"evidenceCoverage\":\"fixture\",\"populationCoverage\":\"unknown\",\"confidence\":\"high\",\"reviewerModel\":\"fixture\",\"reviewerEffort\":\"fixture\",\"reviewedAt\":\"2026-09-27T00:00:01Z\",\"durationSeconds\":1}")
        for path in [ activity; complication; usage; review ] do
            if not (OperatingSystem.IsWindows()) then File.SetUnixFileMode(path, enum<UnixFileMode> 0o600)
        let commands = [ Activity(token, FileInfo activity); Complication(token, FileInfo complication); UsageAttribution(token, FileInfo usage); Review(token, "attempt", FileInfo review) ]
        commands |> List.iter (fun command -> let result = run (Some host) command in require (result.ExitCode = 0) (text result.Stderr))
        File.WriteAllText(activity, "{\"schema\":\"fsgg.telemetry.activity-span-input/1\",\"revision\":0}")
        let refused = run (Some host) (Activity(token, FileInfo activity))
        require (refused.ExitCode = 1 && (text refused.Stderr).Contains "exact") "open observation shape was accepted"

    let private ambiguousReplay root =
        let isolated = Path.Combine(root, "ambiguous")
        Directory.CreateDirectory isolated |> ignore
        let host = config isolated
        let log = Path.Combine(isolated, "publications.log")
        let marker = Path.Combine(isolated, "failed-once")
        Environment.SetEnvironmentVariable("FSGG_ADAPTER_TEST_LOG", log)
        Environment.SetEnvironmentVariable("FSGG_ADAPTER_TEST_FAIL_ONCE", marker)
        let command = Begin("SKILL-FS-01", "RECOVERY", None, "attempt-r", None, None, "root", "fixture-producer", "gpt-6-sol", "medium", 60)
        let first = run (Some host) command
        require (first.ExitCode = 1 && (text first.Stderr).Contains "outcome unknown") "ambiguous begin did not retain intent"
        let second = run (Some host) command
        require (second.ExitCode = 0) (text second.Stderr)
        let lines = File.ReadAllLines log
        require (lines.Length = 2 && lines[0] = lines[1]) "ambiguous retry changed publication bytes"
        Environment.SetEnvironmentVariable("FSGG_ADAPTER_TEST_FAIL_ONCE", null)

    let private retainedPythonState root =
        let isolated = Path.Combine(root, "retained-python")
        Directory.CreateDirectory isolated |> ignore
        let host = config isolated
        let directory = Path.Combine(host.StoreRoot, "orchestrator-dispatches")
        Directory.CreateDirectory directory |> ignore
        if not (OperatingSystem.IsWindows()) then File.SetUnixFileMode(directory, enum<UnixFileMode> 0o700)
        let token = String.replicate 32 "1"
        let invocation = String.replicate 32 "2"
        let batch = "{\"schema\":\"fsgg.telemetry.ingest/1\",\"ingestId\":\"" + invocation + "-000001\",\"sourceIdentity\":\"fixture-producer\",\"generation\":\"" + invocation + "\",\"cursor\":\"1\",\"eventCount\":1,\"events\":[{\"kind\":\"expected-dispatch\",\"identity\":\"expected-dispatch-" + String.replicate 32 "3" + "\",\"itemId\":\"SKILL-FS-01.3\",\"revision\":0,\"dispatchId\":\"" + String.replicate 32 "3" + "\"}]}"
        let state = "{\"schema\":\"fsgg.telemetry.roadmap-dispatch-state/1\",\"token\":\"" + token + "\",\"phase\":\"begin-pending\",\"sequence\":1,\"featureId\":\"SKILL-FS-01\",\"itemId\":\"SKILL-FS-01.3\",\"originalItemId\":\"SKILL-FS-01.3\",\"originalAssignmentDigest\":null,\"attemptId\":\"fixture-attempt\",\"parentAttemptId\":null,\"producerStream\":\"fixture-producer\",\"model\":\"fixture-model\",\"effort\":\"medium\",\"relation\":\"root\",\"parentDispatchId\":null,\"parentInvocationId\":null,\"lateAfterSeconds\":60,\"invocationId\":\"" + invocation + "\",\"associationProducer\":null,\"associationDigest\":null,\"pendingPublication\":{\"operation\":\"begin\",\"nextPhase\":\"expected\",\"batch\":" + batch + "}}\n"
        let statePath = Path.Combine(directory, token + ".json")
        File.WriteAllText(statePath, state)
        if not (OperatingSystem.IsWindows()) then File.SetUnixFileMode(statePath, enum<UnixFileMode> 0o600)
        let log = Path.Combine(isolated, "retained.log")
        Environment.SetEnvironmentVariable("FSGG_ADAPTER_TEST_LOG", log)
        let replay = run (Some host) (Begin("SKILL-FS-01", "SKILL-FS-01.3", None, "fixture-attempt", None, None, "root", "fixture-producer", "fixture-model", "medium", 60))
        require (replay.ExitCode = 0 && (resultJson replay).GetProperty("token").GetString() = token) (text replay.Stderr)
        let submitted = File.ReadAllLines(log) |> Array.exactlyOne |> Convert.FromBase64String |> Encoding.UTF8.GetString
        require (submitted = batch + "\n") "retained Python v1 state did not replay its exact batch bytes"

    let private nativeUsage root =
        let isolated = Path.Combine(root, "native")
        Directory.CreateDirectory isolated |> ignore
        let host = config isolated
        let codexHome = Path.Combine(isolated, "codex-home")
        let sessions = Path.Combine(codexHome, "sessions", "2026", "09", "27")
        Directory.CreateDirectory sessions |> ignore
        let rollout = Path.Combine(sessions, "fixture.jsonl")
        let usageOne = "{\"input_tokens\":12,\"cached_input_tokens\":2,\"output_tokens\":6,\"reasoning_output_tokens\":1,\"total_tokens\":18}"
        let usageTwo = "{\"input_tokens\":7,\"cached_input_tokens\":1,\"output_tokens\":3,\"reasoning_output_tokens\":1,\"total_tokens\":10}"
        File.WriteAllText(rollout,
            "{\"type\":\"token_usage_record\",\"payload\":{\"thread_id\":\"22222222-2222-2222-2222-222222222222\",\"turn_id\":\"33333333-3333-3333-3333-333333333333\",\"response_id\":\"a\",\"usage\":" + usageOne + ",\"turn_token_usage\":" + usageOne + "}}\n" +
            "{\"type\":\"token_usage_record\",\"payload\":{\"thread_id\":\"22222222-2222-2222-2222-222222222222\",\"turn_id\":\"44444444-4444-4444-4444-444444444444\",\"response_id\":\"b\",\"usage\":" + usageTwo + ",\"turn_token_usage\":" + usageTwo + "}}\n")
        let fixtureRoot =
            match Environment.GetEnvironmentVariable "SKILL_FS_01_READER_FIXTURES" with
            | value when not (String.IsNullOrEmpty value) -> value
            | _ -> Path.GetFullPath(Path.Combine(Environment.CurrentDirectory, "tests/skill-fsharp/readers/fixtures"))
        let fixture = Path.Combine(fixtureRoot, "fake-codex.py")
        let bin = Path.Combine(isolated, "bin")
        Directory.CreateDirectory bin |> ignore
        let codex = Path.Combine(bin, "codex")
        File.Copy(fixture, codex)
        if not (OperatingSystem.IsWindows()) then File.SetUnixFileMode(codex, enum<UnixFileMode> 0o700)
        let previousPath = Environment.GetEnvironmentVariable "PATH"
        Environment.SetEnvironmentVariable("PATH", bin + string Path.PathSeparator + previousPath)
        Environment.SetEnvironmentVariable("CODEX_HOME", codexHome)
        Environment.SetEnvironmentVariable("CODEX_THREAD_ID", "11111111-1111-1111-1111-111111111111")
        Environment.SetEnvironmentVariable("SKILL_FS_01_ROLLOUT", rollout)
        Environment.SetEnvironmentVariable("SKILL_FS_01_MODE", "complete")
        let log = Path.Combine(isolated, "native-publications.log")
        Environment.SetEnvironmentVariable("FSGG_ADAPTER_TEST_LOG", log)
        try
            let rootResult = run (Some host) (Begin("SKILL-FS-01", "NATIVE", None, "root", None, None, "root", "fixture", "fixture-model", "medium", 60))
            let rootToken = (resultJson rootResult).GetProperty("token").GetString()
            require ((run (Some host) (Started(rootToken, "root-agent"))).ExitCode = 0) "native root start failed"
            let childResult = run (Some host) (Begin("SKILL-FS-01", "NATIVE", None, "child", Some "root", Some rootToken, "child", "fixture", "fixture-model", "medium", 60))
            let childToken = (resultJson childResult).GetProperty("token").GetString()
            require ((run (Some host) (Started(childToken, "child_1"))).ExitCode = 0) "native child start failed"
            let terminal = run (Some host) (Finish(childToken, "completed", None))
            require (terminal.ExitCode = 0) (text terminal.Stderr)
            require ((resultJson terminal).GetProperty("coverage").GetString() = "native-collaboration-usage-unknown") "native usage coverage was promoted"
            let publications =
                File.ReadAllLines log
                |> Array.map (Convert.FromBase64String >> Encoding.UTF8.GetString)
                |> String.concat "\n"
            require (publications.Contains "\"phase\":\"turn\"") "native turn population was not published"
            require (publications.Contains "runtime-turn-usage") "native turn usage was not published"
        finally
            Environment.SetEnvironmentVariable("PATH", previousPath)
            for name in [ "CODEX_HOME"; "CODEX_THREAD_ID"; "SKILL_FS_01_ROLLOUT"; "SKILL_FS_01_MODE" ] do Environment.SetEnvironmentVariable(name, null)

    let private ciAssignment root =
        let host = config root
        let result = run (Some host) (CiAssignment("SKILL-FS-01", "SKILL-FS-01.3", "attempt-ci", None, "routine-delivery"))
        require (result.ExitCode = 0) (text result.Stderr)
        let path = (resultJson result).GetProperty("assignment").GetString()
        require (File.Exists path) "CI assignment was not written"

    let private protectedOriginalRefusal root =
        let host = config root
        let repository =
            match Configuration.canonicalRepository "https://github.com/FS-GG/.github.git" with
            | Ok value -> value
            | Error error -> failwith error.Message
        let workspace = { host with Repository = Some repository; Workspace = true; Producer = Some "fixture-association"; BindingDigest = Some(String.replicate 64 "a") }
        let token =
            Encoding.UTF8.GetBytes("population-only\u001fF\u001fF.1")
            |> SHA256.HashData
            |> Convert.ToHexString
            |> fun value -> value.ToLowerInvariant().Substring(0, 32)
        let directory = Path.Combine(host.StoreRoot, "orchestrator-original-bindings")
        Directory.CreateDirectory directory |> ignore
        if not (OperatingSystem.IsWindows()) then File.SetUnixFileMode(directory, enum<UnixFileMode> 0o700)
        let state = Path.Combine(directory, token + ".json")
        File.WriteAllText(state, "{\"schema\":\"fsgg.telemetry.original-binding-state/1\",\"token\":\"" + token + "\",\"featureId\":\"F\",\"itemId\":\"F.1\",\"originalItemId\":\"OTHER\",\"producerStream\":\"roadmap-orchestrator\",\"associationProducer\":\"fixture-association\",\"associationDigest\":\"" + String.replicate 64 "a" + "\",\"originalAssignmentDigest\":\"" + String.replicate 40 "b" + ":" + String.replicate 64 "c" + "\",\"phase\":\"pending\",\"sequence\":1,\"invocationId\":\"original-binding-" + token + "\"}\n")
        if not (OperatingSystem.IsWindows()) then File.SetUnixFileMode(state, enum<UnixFileMode> 0o600)
        let refused = run (Some workspace) (PopulationOnly("F", "F.1", "F", "roadmap-orchestrator"))
        require (refused.ExitCode = 1 && (text refused.Stderr).Contains "protected identity") "retained protected original binding was rebound"

    let private populationOnly root =
        let isolated = Path.Combine(root, "population")
        let stateRoot = Path.Combine(isolated, "state")
        Directory.CreateDirectory stateRoot |> ignore
        if not (OperatingSystem.IsWindows()) then File.SetUnixFileMode(stateRoot, enum<UnixFileMode> 0o700)
        let engineDirectory = Path.GetDirectoryName Environment.ProcessPath
        let engineName = Path.GetFileName Environment.ProcessPath
        let configPath = Path.Combine(isolated, "workspace.json")
        File.WriteAllText(configPath, "{\"schema\":\"fsgg.telemetry.workspace-config/1\",\"engine\":\"" + engineName + "\",\"associations\":[{\"producerId\":\"fixture-association\",\"repositories\":[\"FS-GG/.github\"],\"destination\":{\"credentialReference\":\"fixture-ref\"}}],\"retiredAssociations\":[]}")
        if not (OperatingSystem.IsWindows()) then File.SetUnixFileMode(configPath, enum<UnixFileMode> 0o600)
        let source = "{\"schema\":\"fsgg.telemetry.original-item-assignments/1\",\"assignments\":[{\"featureId\":\"F\",\"itemId\":\"F.2\",\"originalItemId\":\"F\"}]}"
        let encoded = Convert.ToBase64String(Encoding.UTF8.GetBytes source)
        let bin = Path.Combine(isolated, "bin")
        Directory.CreateDirectory bin |> ignore
        let gh = Path.Combine(bin, "gh")
        File.WriteAllText(gh, "#!/usr/bin/env bash\nif [[ \"$2\" == *\"git/ref/heads/main\"* ]]; then printf '%s\\n' '{\"ref\":\"refs/heads/main\",\"object\":{\"type\":\"commit\",\"sha\":\"" + String.replicate 40 "b" + "\"}}'; else printf '%s\\n' '{\"type\":\"file\",\"path\":\"docs/coordination/telemetry-original-item-assignments.json\",\"encoding\":\"base64\",\"content\":\"" + encoded + "\"}'; fi\n")
        if not (OperatingSystem.IsWindows()) then File.SetUnixFileMode(gh, enum<UnixFileMode> 0o700)
        let previousPath = Environment.GetEnvironmentVariable "PATH"
        Environment.SetEnvironmentVariable("PATH", bin + string Path.PathSeparator + engineDirectory + string Path.PathSeparator + previousPath)
        Environment.SetEnvironmentVariable("FSGG_TELEMETRY_REPOSITORY", "FS-GG/.github")
        Environment.SetEnvironmentVariable("FSGG_TELEMETRY_CREDENTIAL_FIXTURE_REF", "synthetic-loaded")
        Environment.SetEnvironmentVariable("SKILL_FS_01_STATE_ROOT", stateRoot)
        try
            let host =
                match Configuration.discover (Some configPath) with
                | Ok(Some value) -> value
                | Ok None -> failwith "workspace config was not discovered"
                | Error error -> failwith error.Message
            let command = PopulationOnly("F", "F.2", "F", "roadmap-orchestrator")
            let first = run (Some host) command
            require (first.ExitCode = 0 && (resultJson first).GetProperty("status").GetString() = "applied") (text first.Stderr)
            let second = run (Some host) command
            require (second.ExitCode = 0) (text second.Stderr)
        finally
            Environment.SetEnvironmentVariable("PATH", previousPath)
            for name in [ "FSGG_TELEMETRY_REPOSITORY"; "FSGG_TELEMETRY_CREDENTIAL_FIXTURE_REF"; "SKILL_FS_01_STATE_ROOT" ] do Environment.SetEnvironmentVariable(name, null)

    let private runHarness () =
        let absent = run None Status
        require (absent.ExitCode = 2 && text absent.Stdout = "{\"schema\":\"fsgg.telemetry.host-status/1\",\"status\":\"not-configured\"}\n") "not-configured bytes drifted"
        require (acceptsStateSchema "fsgg.telemetry.roadmap-dispatch-state/1") "v1 state schema rejected"
        require (not (acceptsStateSchema "fsgg.telemetry.roadmap-dispatch-state/2")) "unknown state schema accepted"
        let root = Path.Combine(Path.GetTempPath(), "fsgg-adapter-" + Guid.NewGuid().ToString("N"))
        Directory.CreateDirectory root |> ignore
        try
            Environment.SetEnvironmentVariable("FSGG_ADAPTER_TEST_LOG", Path.Combine(root, "publications.log"))
            let token = lifecycle root
            observations root token
            nativeUsage root
            ambiguousReplay root
            retainedPythonState root
            ciAssignment root
            protectedOriginalRefusal root
            populationOnly root
            Console.WriteLine "adapter harness: PASS"
            0
        finally
            Environment.SetEnvironmentVariable("FSGG_ADAPTER_TEST_LOG", null)
            Environment.SetEnvironmentVariable("FSGG_ADAPTER_TEST_FAIL_ONCE", null)
            Directory.Delete(root, true)

    [<EntryPoint>]
    let main args = if args.Length > 0 && args[0] = "telemetry" then fakeEngine args else runHarness ()
