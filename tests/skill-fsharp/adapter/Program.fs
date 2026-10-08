namespace FS.GG.Coord.Cli.AdapterHarness

open System
open System.Buffers.Binary
open System.Collections.Generic
open System.IO
open System.Security.Cryptography
open System.Text
open System.Text.Json
open System.Text.Json.Nodes
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

    let private fakeAcceptedEngine (args: string array) =
        match option "--input" args, Environment.GetEnvironmentVariable "FSGG_ADAPTER_TEST_LOG" with
        | Some input, log when not (String.IsNullOrEmpty log) ->
            // Synthetic receiver enforces the production parser's cross-kind identity rule.
            use batch = JsonDocument.Parse(File.ReadAllBytes input)
            let identities = batch.RootElement.GetProperty("events").EnumerateArray() |> Seq.map (fun value -> value.GetProperty("identity").GetString()) |> Seq.toArray
            require (identities.Length = (identities |> Set.ofArray |> Set.count)) "batch contains duplicate native fact identities"
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
        elif Array.contains "efficiency" args && Array.contains "analysis" args && Array.contains "reconcile" args then
            require (option "--repository" args = Some "FS-GG/.github") "canonical reconcile lost discovered repository"
            require (option "--config" args |> Option.isSome) "canonical reconcile lost config custody"
            let expected = Environment.GetEnvironmentVariable "FSGG_ADAPTER_TEST_RECONCILE_CONFIG"
            if not (isNull expected) then require (option "--config" args = Some expected) "dashboard override changed reconciliation config"
            Console.Out.WriteLine "{}"
            0
        elif Array.contains "publisher-event" args then
            let expected = Environment.GetEnvironmentVariable "FSGG_ADAPTER_TEST_PUBLISHER_CONFIG"
            if not (isNull expected) then require (option "--config" args = Some expected) "dashboard publisher lost explicit projection config"
            match Environment.GetEnvironmentVariable "FSGG_ADAPTER_TEST_DASHBOARD_MODE" with
            | "failure" -> Console.Error.WriteLine "synthetic advisory failure"; 1
            | "invalid" -> Console.Out.WriteLine "{}"; 0
            | _ ->
                Console.Out.WriteLine "{\"schema\":\"fsgg.telemetry.dashboard-event-health/1\",\"status\":\"ready\",\"reason\":null,\"observedAt\":\"2026-09-27T00:00:00Z\",\"publicRevision\":null,\"commit\":null}"
                0
        elif Array.contains "submit" args then
            use batch = JsonDocument.Parse(File.ReadAllBytes(option "--input" args |> Option.get))
            let responseMode = Environment.GetEnvironmentVariable "FSGG_ADAPTER_TEST_RECEIPT"
            let batchId = batch.RootElement.GetProperty("ingestId").GetString()
            let status = if responseMode = "durable" then "durably-received" else "applied"
            let receipt = JsonSerializer.Serialize
                              {| schema = "fsgg.telemetry.receipt/1"; workspaceId = "fixture-workspace"
                                 producerId = if responseMode = "foreign" then "foreign-producer" else "fixture-association"
                                 streamId = "runtime"; batchId = batchId; digest = String.replicate 64 "a"; status = status; code = (None : string option) |}
            match responseMode with
            | "old-text" -> Console.Out.WriteLine "applied"
            | "duplicate" -> Console.Out.WriteLine(receipt.Replace("{", "{\"status\":\"applied\","))
            | "trailing" -> Console.Out.WriteLine(receipt + "{}")
            | "oversize" -> Console.Out.WriteLine(receipt + String.replicate 4096 " ")
            | "wrong-batch" -> Console.Out.WriteLine(receipt.Replace(batchId,"foreign-batch"))
            | "malformed-code" -> Console.Out.WriteLine(receipt.Replace("\"code\":null","\"code\":42"))
            | _ -> Console.Out.WriteLine receipt
            0
        else
            Console.Out.WriteLine "{}"
            0

    let private fakeEngine (args: string array) =
        let mode = Environment.GetEnvironmentVariable "FSGG_ADAPTER_TEST_REJECTION"
        if Array.contains "submit" args && mode = "workspace-invalid-request" then
            Console.Error.WriteLine "invalid-request: synthetic workspace rejection"
            1
        elif Array.contains "publish" args && not (String.IsNullOrEmpty mode) then
            let bytes = File.ReadAllBytes(option "--input" args |> Option.get)
            use input = JsonDocument.Parse bytes
            let batch = input.RootElement
            let get (name: string) = batch.GetProperty(name).GetString()
            let executable = FileInfo(Environment.ProcessPath)
            let target = executable.ResolveLinkTarget true
            let enginePath = if isNull target then executable.FullName else target.FullName
            let assemblyPath = Reflection.Assembly.GetEntryAssembly().Location |> Path.GetFullPath
            let hashFile path =
                use stream = File.OpenRead path
                SHA256.HashData stream |> Convert.ToHexString |> _.ToLowerInvariant()
            let errors =
                match FS.GG.Coord.TelemetryStore.parseBatch bytes with
                | Error errors -> errors
                | Ok _ -> [ "synthetic forged rejection of valid payload" ]
            let proof = JsonSerializer.Serialize
                            {| schema = "fsgg.telemetry.local-parser-rejection/1"; code = "invalid-batch"
                               boundary = "before-publication-io"
                               inputSha256 = if mode = "wrong-batch" then String.replicate 64 "0" else SHA256.HashData bytes |> Convert.ToHexString |> _.ToLowerInvariant()
                               enginePath = enginePath; engineSha256 = if mode = "wrong-engine" then String.replicate 64 "0" else hashFile enginePath
                               assemblyPath = assemblyPath; assemblySha256 = hashFile assemblyPath
                               storeRoot = if mode = "foreign-store" then "/foreign" else option "--store-root" args |> Option.get |> Path.GetFullPath
                               ingestId = get "ingestId"; sourceIdentity = get "sourceIdentity"
                               generation = get "generation"; cursor = get "cursor"; errors = errors |}
            if mode = "unknown" then Console.Error.WriteLine "invalid-request: delivery outcome unknown"
            else Console.Error.WriteLine("fsgg-coord-engine: telemetry store: " + proof)
            1
        else fakeAcceptedEngine args

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
          CredentialReference = None
          Destination = None }

    let private resultJson result =
        use document = JsonDocument.Parse result.Stdout
        document.RootElement.Clone()

    let private lifecycle root =
        let host = config root
        let beginResult =
            run (Some host) (Begin("SKILL-FS-01", "SKILL-FS-01.3", None, "attempt-a", None, None, "root", "fixture-producer", "gpt-6-sol", "medium", 60))
        require (beginResult.ExitCode = 0) (text beginResult.Stderr)
        let log = Environment.GetEnvironmentVariable "FSGG_ADAPTER_TEST_LOG"
        use batch = JsonDocument.Parse(Convert.FromBase64String(File.ReadAllLines(log)[0]))
        let events = batch.RootElement.GetProperty("events").EnumerateArray() |> Seq.toArray
        let item = events |> Array.find (fun value -> value.GetProperty("kind").GetString() = "item")
        require (item.GetProperty("identity").GetString() = "SKILL-FS-01.3") "distinct logical item identity changed"
        let token = (resultJson beginResult).GetProperty("token").GetString()
        require (token.Length = 32) "begin did not return a durable token"
        let startedResult = run (Some host) (Started(token, "native-agent-a"))
        require (startedResult.ExitCode = 0) (text startedResult.Stderr)
        require host.Repository.IsNone "canonical fixture unexpectedly has a repository"
        let projectionPath = Path.Combine(root, "dashboard-projection.json")
        let selectedEnvironment =
            [ "FSGG_TELEMETRY_REPOSITORY", "FS-GG/.github"
              "FSGG_TELEMETRY_DASHBOARD_CONFIG", projectionPath
              "FSGG_ADAPTER_TEST_PUBLISHER_CONFIG", projectionPath
              "FSGG_ADAPTER_TEST_RECONCILE_CONFIG", host.Path ]
        let previous = selectedEnvironment |> List.map (fun (key, _) -> key, Environment.GetEnvironmentVariable key)
        let finishResult =
            selectedEnvironment |> List.iter (fun (key, value) -> Environment.SetEnvironmentVariable(key, value))
            try run (Some host) (Finish(token, "completed", None))
            finally previous |> List.iter (fun (key, value) -> Environment.SetEnvironmentVariable(key, value))
        require (finishResult.ExitCode = 0) (text finishResult.Stderr)
        require ((resultJson finishResult).GetProperty("assessmentReconciliation").GetProperty("status").GetString() = "requested") "canonical repository None refused reconciliation"
        require ((resultJson finishResult).GetProperty("dashboardPublication").GetProperty("status").GetString() = "observed") "explicit projection config prevented dashboard publication"
        let terminal = resultJson finishResult
        require (terminal.GetProperty("status").GetString() = "terminal") "finish did not become terminal"
        require (terminal.GetProperty("coverage").GetString() = "native-collaboration-usage-unsupported") "missing usage became measured"
        let retry = run (Some host) (Finish(token, "completed", None))
        require (retry.ExitCode = 0) "idempotent finish failed"
        let changed = run (Some host) (Finish(token, "failed", None))
        require (changed.ExitCode = 1 && (text changed.Stderr).Contains "durable terminal intent") "changed terminal retry was accepted"
        token

    let private equalFeatureItem root =
        let isolated = Path.Combine(root, "equal-feature-item")
        Directory.CreateDirectory isolated |> ignore
        let host = config isolated
        let log = Path.Combine(isolated, "publications.log")
        let previousLog = Environment.GetEnvironmentVariable "FSGG_ADAPTER_TEST_LOG"
        Environment.SetEnvironmentVariable("FSGG_ADAPTER_TEST_LOG", log)
        try
            let command = Begin("SAME", "SAME", None, "equal-attempt", None, None, "root", "fixture-producer", "fixture-model", "medium", 60)
            let first = run (Some host) command
            require (first.ExitCode = 0) (text first.Stderr)
            let retry = run (Some host) command
            require (retry.ExitCode = 0 && first.Stdout = retry.Stdout) "equal identity retry changed the dispatch token"
            let publications = File.ReadAllLines log
            require (publications.Length = 1) "equal identity retry republished the batch"
            use batch = JsonDocument.Parse(Convert.FromBase64String publications[0])
            let events = batch.RootElement.GetProperty("events").EnumerateArray() |> Seq.toArray
            let identities = events |> Array.map (fun value -> value.GetProperty("identity").GetString())
            require (identities.Length = (identities |> Set.ofArray |> Set.count)) "equal feature/item emitted duplicate native fact identities"
            let item = events |> Array.find (fun value -> value.GetProperty("kind").GetString() = "item")
            require (item.GetProperty("identity").GetString().StartsWith("roadmap-item-")) "colliding item fact was not namespaced"
            require (item.GetProperty("itemId").GetString() = "SAME" && item.GetProperty("featureId").GetString() = "SAME") "logical item/feature references changed"
            let feature = events |> Array.find (fun value -> value.GetProperty("kind").GetString() = "feature")
            require (feature.GetProperty("identity").GetString() = "SAME") "existing feature fact identity changed"
        finally
            Environment.SetEnvironmentVariable("FSGG_ADAPTER_TEST_LOG", previousLog)

    let private observations root token =
        let host = config root
        let activity = Path.Combine(root, "activity.json")
        File.WriteAllText(activity, "{\"schema\":\"fsgg.telemetry.activity-span-input/1\",\"revision\":0,\"activityId\":\"activity-a\",\"category\":\"validation\",\"startedAt\":\"2026-09-27T00:00:00Z\",\"endedAt\":\"2026-09-27T00:00:01Z\",\"clockProvenance\":\"host-wall\",\"evidence\":[],\"summary\":\"fixture\"}")
        let complication = Path.Combine(root, "complication.json")
        File.WriteAllText(complication, "{\"schema\":\"fsgg.telemetry.complication-input/1\",\"revision\":0,\"complicationId\":\"complication-a\",\"activityId\":\"activity-a\",\"trigger\":\"fixture\",\"cause\":\"fixture\",\"occurredAt\":\"2026-09-27T00:00:01Z\",\"synopsis\":\"fixture\",\"evidence\":[]}")
        let usage = Path.Combine(root, "usage.json")
        File.WriteAllText(usage, "{\"schema\":\"fsgg.telemetry.activity-usage-attribution-input/1\",\"revision\":0,\"usageIdentity\":\"usage-a\",\"activityId\":\"activity-a\",\"classification\":\"direct\",\"input\":0,\"cachedInput\":0,\"output\":0,\"reasoning\":0,\"total\":0}")
        let review = Path.Combine(root, "review.json")
        File.WriteAllText(review, "{\"schema\":\"fsgg.telemetry.process-review-input/1\",\"revision\":1,\"outcomeSynopsis\":\"fixture\",\"wentWell\":[],\"problems\":[],\"avoidableDelayOrRework\":[],\"processObservations\":[],\"remainingRisks\":[],\"concreteImprovements\":[],\"evidence\":[],\"evidenceCoverage\":\"unknown\",\"populationCoverage\":\"unknown\",\"confidence\":\"high\",\"reviewerModel\":\"fixture\",\"reviewerEffort\":\"fixture\",\"reviewedAt\":\"2026-09-27T00:00:01Z\",\"durationSeconds\":1}")
        for path in [ activity; complication; usage; review ] do
            if not (OperatingSystem.IsWindows()) then File.SetUnixFileMode(path, enum<UnixFileMode> 0o600)
        let previousProjection = Environment.GetEnvironmentVariable "FSGG_TELEMETRY_DASHBOARD_CONFIG"
        let previousExpectedPublisher = Environment.GetEnvironmentVariable "FSGG_ADAPTER_TEST_PUBLISHER_CONFIG"
        let observed =
            Environment.SetEnvironmentVariable("FSGG_TELEMETRY_DASHBOARD_CONFIG", null)
            Environment.SetEnvironmentVariable("FSGG_ADAPTER_TEST_PUBLISHER_CONFIG", host.Path)
            try run (Some host) (Activity(token, FileInfo activity))
            finally
                Environment.SetEnvironmentVariable("FSGG_TELEMETRY_DASHBOARD_CONFIG", previousProjection)
                Environment.SetEnvironmentVariable("FSGG_ADAPTER_TEST_PUBLISHER_CONFIG", previousExpectedPublisher)
        require (observed.ExitCode = 0) (text observed.Stderr)
        require ((resultJson observed).GetProperty("dashboardPublication").GetProperty("status").GetString() = "observed") "dashboard publication hook was not observed"
        let previousDashboardConfig = Environment.GetEnvironmentVariable "FSGG_TELEMETRY_DASHBOARD_CONFIG"
        for invalidPath in [ " "; "relative-projection.json" ] do
            Environment.SetEnvironmentVariable("FSGG_TELEMETRY_DASHBOARD_CONFIG", invalidPath)
            try
                let invalidConfig = run (Some host) (Activity(token, FileInfo activity))
                require (invalidConfig.ExitCode = 0 &&
                         (resultJson invalidConfig).GetProperty("dashboardPublication").GetProperty("reason").GetString() = "publisher-event-config-invalid") "invalid projection path changed telemetry result or reached publisher"
            finally Environment.SetEnvironmentVariable("FSGG_TELEMETRY_DASHBOARD_CONFIG", previousDashboardConfig)
        Environment.SetEnvironmentVariable("FSGG_ADAPTER_TEST_DASHBOARD_MODE", "invalid")
        let invalidDashboard = run (Some host) (Activity(token, FileInfo activity))
        require (invalidDashboard.ExitCode = 0 &&
                 (resultJson invalidDashboard).GetProperty("dashboardPublication").GetProperty("reason").GetString() = "publisher-event-result-invalid") "malformed dashboard health changed the telemetry result"
        Environment.SetEnvironmentVariable("FSGG_ADAPTER_TEST_DASHBOARD_MODE", "failure")
        let failedDashboard = run (Some host) (Activity(token, FileInfo activity))
        require (failedDashboard.ExitCode = 0 &&
                 (resultJson failedDashboard).GetProperty("dashboardPublication").GetProperty("reason").GetString() = "publisher-event-subprocess-failed") "dashboard subprocess failure changed the telemetry result"
        Environment.SetEnvironmentVariable("FSGG_ADAPTER_TEST_DASHBOARD_MODE", null)
        let commands = [ Complication(token, FileInfo complication); UsageAttribution(token, FileInfo usage); Review(token, "attempt", FileInfo review) ]
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
            require (publications.Contains "runtime-native-inventory/1" && publications.Contains "runtime-native-inventory-source/1") "source-bound native inventory facts were not published"
            let statePath = Path.Combine(host.StoreRoot, "orchestrator-dispatches", childToken + ".json")
            let readState () = JsonNode.Parse(File.ReadAllText statePath) :?> JsonObject
            let saveState (state: JsonObject) = File.WriteAllText(statePath, state.ToJsonString() + "\n")
            let state = readState ()
            require (state["nativeInventoryPublished"].GetValue<bool>()) "native inventory authority acknowledgement was not retained"
            let integration = state["nativeInventoryIntegration"] :?> JsonObject
            let sourceBinding = state["nativeInventorySourceBinding"] :?> JsonObject
            require (integration["status"].GetValue<string>() = "published") "native inventory integration did not settle"
            require (sourceBinding["sha256"].GetValue<string>().Length = 64) "native source binding was not retained"
            use sourceHash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256)
            let appendChunk (bytes: byte array) =
                let length = Array.zeroCreate<byte> 8
                BinaryPrimitives.WriteInt64BigEndian(length.AsSpan(), int64 bytes.Length)
                sourceHash.AppendData length
                sourceHash.AppendData bytes
            appendChunk (Convert.FromBase64String(sourceBinding["bytesBase64"].GetValue<string>()))
            for record in state["nativeInventoryAppServerResponses"] :?> JsonArray do
                appendChunk (Convert.FromBase64String(record["requestBytesBase64"].GetValue<string>()))
                appendChunk (Convert.FromBase64String(record["responseBytesBase64"].GetValue<string>()))
            for record in state["nativeInventoryRolloutRecords"] :?> JsonArray do
                appendChunk (Convert.FromBase64String(record["bytesBase64"].GetValue<string>()))
            let computedSourceDigest = Convert.ToHexString(sourceHash.GetHashAndReset()).ToLowerInvariant()
            require (computedSourceDigest = state["nativeInventorySourceDigest"].GetValue<string>()) "retained native source bytes did not bind the published digest"
            let originalLedger = state["usageLedger"] :?> JsonObject
            let firstTurn = originalLedger |> Seq.head
            let firstTurnId = firstTurn.Key
            let firstUsage = firstTurn.Value.DeepClone()
            originalLedger.Remove(firstTurnId) |> ignore
            state["usageIntent"] <- JsonObject([ KeyValuePair("turnId", JsonValue.Create firstTurnId :> JsonNode);
                                                    KeyValuePair("revision", firstUsage["revision"].DeepClone());
                                                    KeyValuePair("hash", firstUsage["hash"].DeepClone()) ])
            state["turnRosterPublishedCount"] <- JsonValue.Create 0
            let roster = state["expectedTurnRoster"] :?> JsonArray
            let threadId = state["nativeThreadId"].GetValue<string>()
            let entries = JsonArray()
            for index in 0 .. roster.Count - 1 do
                let turnId = roster[index].GetValue<string>()
                let source = $"[\"{threadId}\",\"{turnId}\",{index + 1},\"codex-app-server-thread-turns-list\"]"
                let hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes source)).ToLowerInvariant()
                entries.Add(JsonObject([ KeyValuePair("turnId", JsonValue.Create turnId :> JsonNode);
                                         KeyValuePair("turnSequence", JsonValue.Create(index + 1) :> JsonNode);
                                         KeyValuePair("hash", JsonValue.Create hash :> JsonNode) ]))
            state["rosterIntent"] <- JsonObject([ KeyValuePair("offset", JsonValue.Create 0 :> JsonNode);
                                                   KeyValuePair("entries", entries :> JsonNode) ])
            saveState state
            let priorPublications = File.ReadAllLines(log).Length
            let resumed = run (Some host) (UsageReconcile childToken)
            require (resumed.ExitCode = 0) (text resumed.Stderr)
            let settled = readState ()
            require (isNull settled["rosterIntent"] && isNull settled["usageIntent"] &&
                     settled["turnRosterPublishedCount"].GetValue<int>() = roster.Count &&
                     (settled["usageLedger"] :?> JsonObject).ContainsKey(firstTurnId)) "acknowledged native intents did not settle"
            require (File.ReadAllLines(log).Length = priorPublications) "acknowledged native intents were republished"
            let pendingState = readState ()
            let sequence = pendingState["sequence"].GetValue<int>() + 1
            let invocationId = pendingState["invocationId"].GetValue<string>()
            let cursor = sequence.ToString("000000")
            let pendingIntegration = pendingState["nativeInventoryIntegration"] :?> JsonObject
            let facts = JsonArray()
            facts.Add(pendingIntegration["fact"].DeepClone())
            facts.Add(pendingIntegration["sourceFact"].DeepClone())
            let batch = JsonObject()
            batch["schema"] <- JsonValue.Create "fsgg.telemetry.ingest/1"
            batch["ingestId"] <- JsonValue.Create($"{invocationId}-{cursor}")
            batch["sourceIdentity"] <- pendingState["producerStream"].DeepClone()
            batch["generation"] <- pendingState["invocationId"].DeepClone()
            batch["cursor"] <- JsonValue.Create(string sequence)
            batch["eventCount"] <- JsonValue.Create 2
            batch["events"] <- facts
            pendingState["sequence"] <- JsonValue.Create sequence
            pendingState["nativeInventoryPublished"] <- JsonValue.Create false
            pendingIntegration["status"] <- JsonValue.Create "ready"
            let pending = JsonObject()
            pending["operation"] <- JsonValue.Create "native-inventory-authority"
            pending["nextPhase"] <- JsonValue.Create "terminal"
            pending["batch"] <- batch
            pendingState["pendingPublication"] <- pending
            saveState pendingState
            let replay = run (Some host) (UsageReconcile childToken)
            require (replay.ExitCode = 0) (text replay.Stderr)
            let replayed = readState ()
            let replayedIntegration = replayed["nativeInventoryIntegration"] :?> JsonObject
            require (isNull replayed["pendingPublication"] && replayed["nativeInventoryPublished"].GetValue<bool>() &&
                     replayedIntegration["status"].GetValue<string>() = "published") "retained native authority intent did not settle"
            require (File.ReadAllLines(log).Length = priorPublications + 1) "retained native authority batch was not replayed exactly once"
            let pendingRoster = readState ()
            let rosterSequence = pendingRoster["sequence"].GetValue<int>() + 1
            let rosterBatch = JsonObject()
            rosterBatch["schema"] <- JsonValue.Create "fsgg.telemetry.ingest/1"
            rosterBatch["ingestId"] <- JsonValue.Create(invocationId + "-" + rosterSequence.ToString("000000"))
            rosterBatch["sourceIdentity"] <- pendingRoster["producerStream"].DeepClone()
            rosterBatch["generation"] <- pendingRoster["invocationId"].DeepClone()
            rosterBatch["cursor"] <- JsonValue.Create(string rosterSequence)
            rosterBatch["eventCount"] <- JsonValue.Create 0
            rosterBatch["events"] <- JsonArray()
            let rosterPending = JsonObject()
            rosterPending["operation"] <- JsonValue.Create "native-roster:retained-fixture"
            rosterPending["nextPhase"] <- JsonValue.Create "terminal"
            rosterPending["batch"] <- rosterBatch
            pendingRoster["sequence"] <- JsonValue.Create rosterSequence
            pendingRoster["turnRosterPublishedCount"] <- JsonValue.Create 0
            let rosterIntent = JsonObject()
            rosterIntent["offset"] <- JsonValue.Create 0
            rosterIntent["entries"] <- entries.DeepClone()
            pendingRoster["rosterIntent"] <- rosterIntent
            pendingRoster["pendingPublication"] <- rosterPending
            saveState pendingRoster
            let rosterReplay = run (Some host) (UsageReconcile childToken)
            require (rosterReplay.ExitCode = 0) (text rosterReplay.Stderr)
            let rosterSettled = readState ()
            require (isNull rosterSettled["pendingPublication"] && isNull rosterSettled["rosterIntent"] &&
                     rosterSettled["turnRosterPublishedCount"].GetValue<int>() = roster.Count) "retained v1 roster intent did not settle"
            require (File.ReadAllLines(log).Length = priorPublications + 2) "retained roster batch was not replayed exactly once"
            let followup = run (Some host) (Begin("SKILL-FS-01", "NATIVE", None, "followup", Some "child", Some childToken,
                                                "follow-up", "fixture", "fixture-model", "medium", 60))
            require (followup.ExitCode = 0) (text followup.Stderr)
            let followupToken = (resultJson followup).GetProperty("token").GetString()
            let followupState = JsonNode.Parse(File.ReadAllText(Path.Combine(host.StoreRoot, "orchestrator-dispatches", followupToken + ".json"))) :?> JsonObject
            require (followupState["usageBaselineKnown"].GetValue<bool>() &&
                     (followupState["baselineTurnIds"] :?> JsonArray).Count = roster.Count &&
                     followupState["baselineSourceBinding"] <> null) "follow-up baseline evidence was not captured"
            require ((run (Some host) (Started(followupToken, "child_1"))).ExitCode = 0) "follow-up start failed"
            require ((run (Some host) (Finish(followupToken, "completed", None))).ExitCode = 0) "follow-up finish failed"
            let followupTerminal = JsonNode.Parse(File.ReadAllText(Path.Combine(host.StoreRoot, "orchestrator-dispatches", followupToken + ".json"))) :?> JsonObject
            require ((followupTerminal["expectedTurnRoster"] :?> JsonArray).Count = 0 &&
                     followupTerminal["turnRosterPublishedCount"].GetValue<int>() = 0) "follow-up counted baseline turns"
            Environment.SetEnvironmentVariable("CODEX_THREAD_ID", "55555555-5555-5555-5555-555555555555")
            let unknown = run (Some host) (Begin("SKILL-FS-01", "NATIVE", None, "unknown-baseline", Some "child", Some childToken,
                                               "follow-up", "fixture", "fixture-model", "medium", 60))
            require (unknown.ExitCode = 0) (text unknown.Stderr)
            let unknownToken = (resultJson unknown).GetProperty("token").GetString()
            let unknownState = JsonNode.Parse(File.ReadAllText(Path.Combine(host.StoreRoot, "orchestrator-dispatches", unknownToken + ".json"))) :?> JsonObject
            require (not (unknownState["usageBaselineKnown"].GetValue<bool>())) "unproven follow-up baseline was promoted"
            require ((run (Some host) (Started(unknownToken, "child_1"))).ExitCode = 0) "unknown-baseline start failed"
            require ((run (Some host) (Finish(unknownToken, "completed", None))).ExitCode = 0) "unknown-baseline finish failed"
            let unknownTerminal = JsonNode.Parse(File.ReadAllText(Path.Combine(host.StoreRoot, "orchestrator-dispatches", unknownToken + ".json"))) :?> JsonObject
            require (isNull unknownTerminal["nativeInventoryPublished"]) "unknown follow-up baseline published inventory"
            let changedRoster = readState ()
            let truncated = JsonArray()
            let originalRoster = changedRoster["expectedTurnRoster"] :?> JsonArray
            truncated.Add(originalRoster[0].DeepClone())
            changedRoster["expectedTurnRoster"] <- truncated
            saveState changedRoster
            let refusedRoster = run (Some host) (UsageReconcile childToken)
            require (refusedRoster.ExitCode = 1 && (text refusedRoster.Stderr).Contains "published native inventory roster cannot change") "published native roster changed"
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
        let malformed = File.ReadAllText(state).Replace("\"originalItemId\":\"OTHER\"", "\"originalItemId\":\"F\"").Replace(String.replicate 64 "c" + "\"", String.replicate 64 "c" + "\\n\"")
        File.WriteAllText(state, malformed)
        let newlineDigest = run (Some workspace) (PopulationOnly("F", "F.1", "F", "roadmap-orchestrator"))
        require (newlineDigest.ExitCode = 1 && (text newlineDigest.Stderr).Contains "malformed") "trailing LF assignment digest was accepted"

    let private populationOnly root =
        let isolated = Path.Combine(root, "population")
        let stateRoot = Path.Combine(isolated, "state")
        Directory.CreateDirectory stateRoot |> ignore
        if not (OperatingSystem.IsWindows()) then File.SetUnixFileMode(stateRoot, enum<UnixFileMode> 0o700)
        let engineDirectory = Path.GetDirectoryName Environment.ProcessPath
        let engineName = Path.GetFileName Environment.ProcessPath
        let configPath = Path.Combine(isolated, "workspace.json")
        File.WriteAllText(configPath, "{\"schema\":\"fsgg.telemetry.workspace-config/1\",\"engine\":\"" + engineName + "\",\"associations\":[{\"producerId\":\"fixture-association\",\"repositories\":[\"FS-GG/.github\"],\"destination\":{\"kind\":\"remote\",\"endpoint\":\"https://collector.invalid\",\"credentialReference\":\"fixture-ref\",\"spoolRoot\":\"" + stateRoot.Replace("\\", "\\\\") + "\"}}],\"retiredAssociations\":[]}")
        if not (OperatingSystem.IsWindows()) then File.SetUnixFileMode(configPath, enum<UnixFileMode> 0o600)
        let source = "{\"schema\":\"fsgg.telemetry.original-item-assignments/1\",\"assignments\":[{\"featureId\":\"F\",\"itemId\":\"F.2\",\"originalItemId\":\"F\"},{\"featureId\":\"F\",\"itemId\":\"F\",\"originalItemId\":\"OTHER\"}]}"
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
            // A workspace receiver error supplies no exact no-publication-IO proof.
            Environment.SetEnvironmentVariable("FSGG_ADAPTER_TEST_REJECTION", "workspace-invalid-request")
            try
                let rejected = run (Some host) (Begin("WORKSPACE", "WORKSPACE", None, "workspace-rejection", None, None, "root", "fixture-producer", "fixture-model", "medium", 60))
                require (rejected.ExitCode = 1 && (text rejected.Stderr).Contains "invalid-request") "workspace rejection was not reached"
                let files = Directory.GetFiles(Path.Combine(host.StoreRoot, "orchestrator-dispatches"), "*.json")
                require (files.Length = 1) "workspace fixture produced unexpected dispatch population"
                let retained = JsonNode.Parse(File.ReadAllBytes files[0]) :?> JsonObject
                require (retained["sequence"].GetValue<int>() = 1 && not (isNull retained["pendingPublication"])) "workspace ambiguous rejection lost intent"
            finally Environment.SetEnvironmentVariable("FSGG_ADAPTER_TEST_REJECTION", null)
            let mutable retainedIntent: string option = None
            for responseMode in [ "old-text"; "duplicate"; "trailing"; "oversize"; "foreign"; "wrong-batch"; "malformed-code"; "durable" ] do
                Environment.SetEnvironmentVariable("FSGG_ADAPTER_TEST_RECEIPT", responseMode)
                try
                    let refused = run (Some host) (PopulationOnly("F", "F.2", "F", "roadmap-orchestrator"))
                    require (refused.ExitCode = 1) ("receipt mutation unexpectedly applied: " + responseMode)
                    let files = Directory.GetFiles(Path.Combine(host.StoreRoot,"orchestrator-original-bindings"),"*.json")
                    let pending = files |> Array.map (fun path -> JsonNode.Parse(File.ReadAllBytes path).AsObject()) |> Array.filter (fun state -> not (isNull state["pendingPublication"]))
                    require (pending.Length = 1) "receipt refusal discarded pending intent"
                    let retainedState = pending[0]
                    let retainedPublication = retainedState.["pendingPublication"]
                    let retainedSequence = retainedState.["sequence"]
                    let exactIntent = retainedPublication.ToJsonString() + ":" + string (retainedSequence.GetValue<int>())
                    match retainedIntent with
                    | Some prior -> require (exactIntent = prior) "receipt refusal changed exact batch/sequence"
                    | None -> retainedIntent <- Some exactIntent
                finally Environment.SetEnvironmentVariable("FSGG_ADAPTER_TEST_RECEIPT", null)
            let command = PopulationOnly("F", "F.2", "F", "roadmap-orchestrator")
            let first = run (Some host) command
            require (first.ExitCode = 0 && (resultJson first).GetProperty("status").GetString() = "applied") (text first.Stderr)
            let second = run (Some host) command
            require (second.ExitCode = 0) (text second.Stderr)
            let same = run (Some host) (PopulationOnly("F", "F", "OTHER", "roadmap-orchestrator"))
            require (same.ExitCode = 0) (text same.Stderr)
        finally
            Environment.SetEnvironmentVariable("PATH", previousPath)
            for name in [ "FSGG_TELEMETRY_REPOSITORY"; "FSGG_TELEMETRY_CREDENTIAL_FIXTURE_REF"; "SKILL_FS_01_STATE_ROOT" ] do Environment.SetEnvironmentVariable(name, null)

    let private boundedPrivateState root =
        let isolated = Path.Combine(root, "bounded-state")
        let host = config isolated
        let beginCommand attempt = Begin("STATE", "STATE.1", None, attempt, None, None, "root", "fixture", "fixture-model", "medium", 60)
        let initial = run (Some host) (beginCommand "large")
        require (initial.ExitCode = 0) (text initial.Stderr)
        let token = (resultJson initial).GetProperty("token").GetString()
        let path = Path.Combine(host.StoreRoot, "orchestrator-dispatches", token + ".json")
        let state = JsonNode.Parse(File.ReadAllBytes path) :?> JsonObject
        // Public synthetic metadata stands in for retained native inventory; no private state is copied.
        state["retainedFixtureMetadata"] <- JsonValue.Create(String.replicate 294183 "x")
        let compact = JsonSerializerOptions(Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping)
        let writeFixture (value: JsonObject) =
            File.WriteAllText(path, value.ToJsonString(compact) + "\n", UTF8Encoding(false))
            if not (OperatingSystem.IsWindows()) then File.SetUnixFileMode(path, enum<UnixFileMode> 0o600)
        writeFixture state
        let largeBytes = File.ReadAllBytes path
        require (largeBytes.Length > 294183 && largeBytes.Length < 1024 * 1024) "large-state fixture is outside the regression window"
        let independent = run (Some host) (beginCommand "independent")
        require (independent.ExitCode = 0) (text independent.Stderr)
        require (File.ReadAllBytes path = largeBytes) "inventory matching changed another dispatch"
        let started = run (Some host) (Started(token, "large-native"))
        require (started.ExitCode = 0) (text started.Stderr)
        let finished = run (Some host) (Finish(token, "completed", None))
        require (finished.ExitCode = 0) (text finished.Stderr)
        let terminal = JsonNode.Parse(File.ReadAllBytes path) :?> JsonObject
        require (terminal["retainedFixtureMetadata"].GetValue<string>() = String.replicate 294183 "x") "large metadata was trimmed"
        require (terminal["token"].GetValue<string>() = token && terminal["attemptId"].GetValue<string>() = "large") "large-state identity changed"
        require ((resultJson finished).GetProperty("coverage").GetString() = "native-collaboration-usage-unsupported") "large state invented native usage"
        let followup = run (Some host) (Begin("STATE", "STATE.1", None, "followup", Some "large", Some token,
                                            "follow-up", "fixture", "fixture-model", "medium", 60))
        require (followup.ExitCode = 0) (text followup.Stderr)
        let followupToken = (resultJson followup).GetProperty("token").GetString()
        let followupPath = Path.Combine(host.StoreRoot, "orchestrator-dispatches", followupToken + ".json")
        let followupState = JsonNode.Parse(File.ReadAllBytes followupPath) :?> JsonObject
        require (followupState["parentDispatchId"].GetValue<string>() = terminal["dispatchId"].GetValue<string>() &&
                 followupState["parentAttemptId"].GetValue<string>() = "large" &&
                 not (followupState["usageBaselineKnown"].GetValue<bool>())) "large-state follow-up lost lineage or invented baseline"

        // A reader-valid exact-bound state must be preserved if a transition would exceed the writer bound.
        let edge = JsonNode.Parse(File.ReadAllBytes followupPath) :?> JsonObject
        edge["retainedFixtureMetadata"] <- JsonValue.Create ""
        let fixedBytes = Encoding.UTF8.GetByteCount(edge.ToJsonString(compact)) + 1
        edge["retainedFixtureMetadata"] <- JsonValue.Create(String.replicate (1024 * 1024 - fixedBytes) "x")
        File.WriteAllText(followupPath, edge.ToJsonString(compact) + "\n", UTF8Encoding(false))
        let before = File.ReadAllBytes followupPath
        require (before.Length = 1024 * 1024) "writer-bound fixture length drifted"
        let log = Environment.GetEnvironmentVariable "FSGG_ADAPTER_TEST_LOG"
        let publications = File.ReadAllLines(log).Length
        let rejectedWrite = run (Some host) (Started(followupToken, "would-exceed-bound"))
        require (rejectedWrite.ExitCode = 1 && text rejectedWrite.Stderr = "fsgg roadmap telemetry: private telemetry state exceeds 1 MiB\n") "oversize writer did not refuse before publication"
        require (File.ReadAllBytes followupPath = before && File.ReadAllLines(log).Length = publications) "oversize writer changed retained state or published"

        let refusesForeignState label =
            let rejected = run (Some host) (beginCommand label)
            require (rejected.ExitCode = 1 && text rejected.Stderr = "fsgg roadmap telemetry: dispatch state is unavailable\n") (label + " foreign state was skipped or accepted")
            require (File.ReadAllLines(log).Length = publications) (label + " published despite invalid inventory")
        File.WriteAllBytes(followupPath, Array.append before [| byte ' ' |])
        refusesForeignState "above-bound"
        File.WriteAllText(followupPath, "{", UTF8Encoding(false))
        refusesForeignState "malformed"
        File.WriteAllBytes(followupPath, before)
        if not (OperatingSystem.IsWindows()) then
            File.SetUnixFileMode(followupPath, enum<UnixFileMode> 0o644)
            refusesForeignState "permissions"
            File.SetUnixFileMode(followupPath, enum<UnixFileMode> 0o600)
        let target = Path.Combine(isolated, "symlink-target.json")
        File.Move(followupPath, target)
        File.CreateSymbolicLink(followupPath, target) |> ignore
        refusesForeignState "symlink"
        File.Delete followupPath
        File.Move(target, followupPath)
        require (Directory.GetFiles(Path.GetDirectoryName followupPath, "*.tmp").Length = 0) "state refusal left temporary files"

    let private observationPrevalidation root =
        let isolated = Path.Combine(root, "observation-prevalidation")
        Directory.CreateDirectory isolated |> ignore
        let host = config isolated
        let log = Path.Combine(isolated, "publications.log")
        Environment.SetEnvironmentVariable("FSGG_ADAPTER_TEST_LOG", log)
        let started = run (Some host) (Begin("F", "VALIDATION", None, "validation-attempt", None, None, "root", "fixture-producer", "fixture-model", "medium", 60))
        require (started.ExitCode = 0) (text started.Stderr)
        let token = (resultJson started).GetProperty("token").GetString()
        require ((run (Some host) (Started(token, "validation-native"))).ExitCode = 0) "fixture start failed"
        let path = Path.Combine(host.StoreRoot, "orchestrator-dispatches", token + ".json")
        let input = Path.Combine(isolated, "activity.json")
        let activity = """{"schema":"fsgg.telemetry.activity-span-input/1","revision":0,"activityId":"activity-a","category":"validation","startedAt":"2026-09-27T00:00:00Z","endedAt":null,"clockProvenance":"host-wall","evidence":[],"summary":"fixture"}"""
        let reject (value: string) command =
            let before = File.ReadAllBytes path
            let calls = File.ReadAllLines(log).Length
            File.WriteAllText(input, value)
            if not (OperatingSystem.IsWindows()) then File.SetUnixFileMode(input, enum<UnixFileMode> 0o600)
            let result = run (Some host) (command (FileInfo input))
            require (result.ExitCode = 1 && File.ReadAllBytes path = before) "semantic refusal changed durable original state"
            require (File.ReadAllLines(log).Length = calls) "semantic refusal invoked the publisher"
        reject (activity.Replace("\"evidence\":[]", "\"evidence\":[\"invalid\"]")) (fun file -> Activity(token, file))
        reject (activity.Replace("validation", "invalid-category")) (fun file -> Activity(token, file))
        reject (activity.Replace("2026-09-27T00:00:00Z", "not-a-time")) (fun file -> Activity(token, file))
        // A valid retained state can carry a long attempt field; the wire batch has its own64KiB bound.
        let retained = File.ReadAllBytes path
        let large = JsonNode.Parse(retained) :?> JsonObject
        large["attemptId"] <- JsonValue.Create(String.replicate 70000 "a")
        File.WriteAllText(path, large.ToJsonString() + "\n")
        reject activity (fun file -> Activity(token, file))
        File.WriteAllBytes(path, retained)
        File.WriteAllText(input, activity)
        require ((run (Some host) (Activity(token, FileInfo input))).ExitCode = 0) "valid open activity refused"
        File.WriteAllText(input, activity.Replace("\"revision\":0", "\"revision\":1").Replace("\"endedAt\":null", "\"endedAt\":\"2026-09-27T00:00:01Z\""))
        require ((run (Some host) (Activity(token, FileInfo input))).ExitCode = 0) "valid closed activity refused"
        require ((run (Some host) (Finish(token, "completed", Some 0))).ExitCode = 0) "fixture finish failed"
        let usage = """{"schema":"fsgg.telemetry.activity-usage-attribution-input/1","revision":0,"usageIdentity":"turn-a","activityId":null,"classification":"unclassified","input":-1,"cachedInput":0,"output":0,"reasoning":0,"total":0}"""
        reject usage (fun file -> UsageAttribution(token, file))

    let private retainedParserRejection root =
        let isolated = Path.Combine(root, "retained-parser-rejection")
        Directory.CreateDirectory isolated |> ignore
        let host = config isolated
        let log = Path.Combine(isolated, "publications.log")
        Environment.SetEnvironmentVariable("FSGG_ADAPTER_TEST_LOG", log)
        let result = run (Some host) (Begin("F", "REJECTION", None, "rejection-attempt", None, None, "root", "fixture-producer", "fixture-model", "medium", 60))
        require (result.ExitCode = 0) (text result.Stderr)
        let token = (resultJson result).GetProperty("token").GetString()
        require ((run (Some host) (Started(token, "rejection-native"))).ExitCode = 0) "fixture start failed"
        let path = Path.Combine(host.StoreRoot, "orchestrator-dispatches", token + ".json")
        let before = File.ReadAllBytes path
        let state = JsonNode.Parse(before) :?> JsonObject
        let sequence = state["sequence"].GetValue<int>() + 1
        let activityId = "rejected-activity"
        let identity =
            "activity-span-" + (Encoding.UTF8.GetBytes("REJECTION\u001f" + activityId) |> SHA256.HashData |> Convert.ToHexString |> _.ToLowerInvariant()).Substring(0,32)
        let input = Path.Combine(isolated, "activity.json")
        let activity = JsonNode.Parse("""{"schema":"fsgg.telemetry.activity-span-input/1","revision":0,"activityId":"rejected-activity","category":"validation","startedAt":"2026-09-27T00:00:00Z","endedAt":null,"clockProvenance":"host-wall","evidence":["invalid"],"summary":"fixture"}""") :?> JsonObject
        File.WriteAllText(input, activity.ToJsonString())
        if not (OperatingSystem.IsWindows()) then File.SetUnixFileMode(input, enum<UnixFileMode> 0o600)
        // Construct an independent synthetic retained legacy intent in production field order.
        let observation = JsonObject()
        observation["kind"] <- JsonValue.Create "activity-span"
        observation["identity"] <- JsonValue.Create identity
        observation["itemId"] <- state["itemId"].DeepClone()
        observation["invocationId"] <- state["invocationId"].DeepClone()
        observation["attemptId"] <- state["attemptId"].DeepClone()
        for name in [ "revision"; "activityId"; "category"; "startedAt"; "endedAt"; "clockProvenance"; "evidence"; "summary" ] |> List.sort do
            observation[name] <- if isNull activity[name] then null else activity[name].DeepClone()
        let events = JsonArray()
        events.Add observation
        let batch = JsonObject()
        batch["schema"] <- JsonValue.Create "fsgg.telemetry.ingest/1"
        batch["ingestId"] <- JsonValue.Create(state["invocationId"].GetValue<string>() + "-" + sequence.ToString("000000"))
        batch["sourceIdentity"] <- state["producerStream"].DeepClone()
        batch["generation"] <- state["invocationId"].DeepClone()
        batch["cursor"] <- JsonValue.Create(string sequence)
        batch["eventCount"] <- JsonValue.Create 1
        batch["events"] <- events
        let pending = JsonObject()
        pending["operation"] <- JsonValue.Create("observation:activity-span:" + identity)
        pending["nextPhase"] <- state["phase"].DeepClone()
        pending["batch"] <- batch
        state["sequence"] <- JsonValue.Create sequence
        state["pendingPublication"] <- pending
        File.WriteAllText(path, state.ToJsonString() + "\n")
        let poisoned = File.ReadAllBytes path
        let calls = File.ReadAllLines(log).Length
        try
            for mode in [ "unknown"; "wrong-batch"; "wrong-engine"; "foreign-store" ] do
                Environment.SetEnvironmentVariable("FSGG_ADAPTER_TEST_REJECTION", mode)
                let refused = run (Some host) (Activity(token, FileInfo input))
                require (refused.ExitCode = 1 && File.ReadAllBytes path = poisoned) (mode + " incorrectly cleared retained intent")
            Environment.SetEnvironmentVariable("FSGG_ADAPTER_TEST_REJECTION", "exact")
            let rejected = run (Some host) (Activity(token, FileInfo input))
            require (rejected.ExitCode = 1 && File.ReadAllBytes path = before) "exact pre-IO parser refusal did not restore original sequence/state"
            require (File.ReadAllLines(log).Length = calls) "rejected parser payload was published"
            Environment.SetEnvironmentVariable("FSGG_ADAPTER_TEST_REJECTION", null)
            activity["evidence"] <- JsonArray()
            File.WriteAllText(input, activity.ToJsonString())
            require ((run (Some host) (Activity(token, FileInfo input))).ExitCode = 0) "corrected observation refused after classified rejection"
            require (File.ReadAllLines(log).Length = calls + 1) "corrected observation was not published exactly once"
        finally Environment.SetEnvironmentVariable("FSGG_ADAPTER_TEST_REJECTION", null)

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
            equalFeatureItem root
            observations root token
            nativeUsage root
            ambiguousReplay root
            retainedPythonState root
            ciAssignment root
            protectedOriginalRefusal root
            populationOnly root
            boundedPrivateState root
            observationPrevalidation root
            retainedParserRejection root
            Console.WriteLine "adapter harness: PASS"
            0
        finally
            Environment.SetEnvironmentVariable("FSGG_ADAPTER_TEST_LOG", null)
            Environment.SetEnvironmentVariable("FSGG_ADAPTER_TEST_FAIL_ONCE", null)
            Directory.Delete(root, true)

    [<EntryPoint>]
    let main args = if args.Length > 0 && args[0] = "telemetry" then fakeEngine args else runHarness ()
