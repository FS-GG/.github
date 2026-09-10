namespace FS.GG.Coord.Cli.Tests

open System
open System.IO
open System.Text
open System.Text.Json
open Xunit
open FS.GG.Coord
open FS.GG.Coord.Cli

module TelemetryRuntimeApplicationTests =
    let private approved = TelemetryStore.ApprovedLocalDurable
    let private unwrap = function Ok value -> value | Error errors -> failwithf "%A" errors
    let private assignment producer : TelemetryRuntime.Assignment =
        { FeatureId = "UTEL-03A"; ItemId = "UTEL-03A"; AttemptId = producer; ParentAttemptId = Some "parent-1"; ProducerStream = producer }
    let private temp () =
        let path = Path.Combine(Path.GetTempPath(), "fsgg-utel03a-" + Guid.NewGuid().ToString("N"))
        Directory.CreateDirectory path |> ignore
        { new IDisposable with member _.Dispose() = if Directory.Exists path then Directory.Delete(path, true) }, path
    let private script (root: string) (name: string) (lines: string list) (exitCode: int) =
        let path = Path.Combine(root, name)
        let content = "#!/bin/sh\n" + (lines |> List.map (fun line -> "printf '%s\\n' '" + line.Replace("'", "'\\''") + "'") |> String.concat "\n") + $"\nexit %d{exitCode}\n"
        File.WriteAllText(path, content)
        File.SetUnixFileMode(path, UnixFileMode.UserRead ||| UnixFileMode.UserWrite ||| UnixFileMode.UserExecute)
        path
    let private batchFacts bytes = TelemetryStore.parseBatch bytes |> unwrap |> _.Facts

    let private observed executable parent relation exitCode =
        let published = ResizeArray<byte array>()
        let publish bytes = published.Add bytes; Ok "queued"
        let actual =
            TelemetryRuntimeApplication.runObservedCodexExecWith executable (assignment "worker-observer") parent relation
                (Some "/durable/private/fsgg-telemetry") 60L None
                [ "--json"; "--ephemeral"; "-m"; "gpt-test"; "-c"; "model_reasoning_effort=high"; "--sandbox"; "workspace-write"; "synthetic" ] publish
        Assert.Equal(exitCode, actual)
        published |> Seq.collect batchFacts |> Seq.toList

    [<Fact>]
    let ``UTEL-03A assignment is closed and projector discards content-bearing fields`` () =
        let valid = Encoding.UTF8.GetBytes """{"schema":"fsgg.telemetry.codex-assignment/1","featureId":"UTEL-03A","itemId":"UTEL-03A","attemptId":"a1","parentAttemptId":null,"producerStream":"worker-1"}"""
        Assert.True(TelemetryRuntime.parseAssignment valid |> Result.isOk)
        Assert.True(TelemetryRuntime.parseAssignment(Encoding.UTF8.GetBytes """{"schema":"fsgg.telemetry.codex-assignment/1","featureId":"UTEL-03A","itemId":"UTEL-03A","attemptId":"a1","producerStream":"worker-1","task":"PRIVATE-SENTINEL"}""") |> Result.isError)
        Assert.True(TelemetryRuntime.projectLine (Some "thread-1") 1L """{"type":"item.completed","item":{"text":"PRIVATE-SENTINEL","command":"PRIVATE-COMMAND","path":"/private/path"}}""" |> Option.isNone)
        match TelemetryRuntime.projectLine (Some "thread-1") 1L """{"type":"turn.completed","usage":{"input_tokens":10,"cached_input_tokens":3,"output_tokens":4}}""" with
        | Some(TelemetryRuntime.TurnUsageCompleted usage) -> Assert.Equal(14L, usage.Total); Assert.True usage.TurnId.IsNone; Assert.True usage.ObservedModel.IsNone
        | value -> failwithf "unexpected projection %A" value

        match TelemetryRuntime.projectLine (Some "thread-1") 2L """{"type":"turn.completed","turn_id":"turn-2","model":"observed","effort":"high","usage":{"input_tokens":5,"cached_input_tokens":1,"output_tokens":2,"reasoning_output_tokens":1}}""" with
        | Some(TelemetryRuntime.TurnUsageCompleted usage) ->
            Assert.Equal(Some "turn-2", usage.TurnId)
            Assert.Equal(2L, usage.TurnSequence)
            Assert.Equal(Some "observed", usage.ObservedModel)
            Assert.Equal(Some 1L, usage.Reasoning)
        | value -> failwithf "unexpected second projection %A" value
        Assert.Equal(Some(TelemetryRuntime.Gap "malformed-json-frame"), TelemetryRuntime.projectLine None 1L "{")
        Assert.Equal(Some(TelemetryRuntime.Gap "malformed-turn-usage"), TelemetryRuntime.projectLine (Some "thread-1") 1L """{"type":"turn.completed","usage":{"input_tokens":1,"cached_input_tokens":2,"output_tokens":0}}""")
        Assert.Equal(Some(TelemetryRuntime.TurnStarted("thread-1", Some "turn-3", 3L)), TelemetryRuntime.projectLine (Some "thread-1") 3L """{"type":"turn.started","turn_id":"turn-3"}""")

    [<Fact>]
    let ``UTEL-03A three admitted workers preserve population and do not invent counts`` () =
        if not (OperatingSystem.IsWindows()) then
            let cleanup, root = temp ()
            use cleanup = cleanup
            let runtimeCleanup, runtimeRoot = temp ()
            use runtimeCleanup = runtimeCleanup
            TelemetryStoreApplication.initialize root approved |> unwrap |> ignore
            let success = script runtimeRoot "fake-codex-success" [ "{\"type\":\"thread.started\",\"thread_id\":\"native-thread\"}"; "{\"type\":\"item.completed\",\"item\":{\"text\":\"PRIVATE-SENTINEL\",\"path\":\"/private/path\"}}"; "{\"type\":\"turn.started\",\"turn_id\":\"native-turn\"}"; "{\"type\":\"turn.completed\",\"turn_id\":\"native-turn\",\"usage\":{\"input_tokens\":10,\"cached_input_tokens\":2,\"output_tokens\":5}}" ] 0
            let failure = script runtimeRoot "fake-codex-failure" [ "{\"type\":\"thread.started\",\"thread_id\":\"native-thread\"}"; "{\"type\":\"error\",\"message\":\"discarded-runtime-error\"}" ] 7
            let publish bytes = TelemetryStoreApplication.publish root approved bytes
            Assert.Equal(0, TelemetryRuntimeApplication.runCodexExecWith success (assignment "worker-1") [ "--json"; "--ephemeral"; "synthetic" ] publish)
            Assert.Equal(0, TelemetryRuntimeApplication.runCodexExecWith success (assignment "worker-2") [ "--json"; "--ephemeral"; "synthetic" ] publish)
            Assert.Equal(7, TelemetryRuntimeApplication.runCodexExecWith failure (assignment "worker-3") [ "--json"; "--ephemeral"; "synthetic" ] publish)
            TelemetryStoreApplication.drain root approved |> unwrap |> ignore
            let summary = TelemetryStoreApplication.summary root approved "UTEL-03A" |> unwrap
            Assert.Contains("\"admitted\":3", summary)
            Assert.Contains("\"started\":3", summary)
            Assert.Contains("\"terminal\":3", summary)
            Assert.Contains("\"usage\":2", summary)
            Assert.Contains("\"missingUsage\":1", summary)
            Assert.Contains("\"total\":30", summary)
            let privateFiles = Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories) |> Seq.filter (fun path -> not (path.EndsWith(".sqlite3-wal") || path.EndsWith(".sqlite3-shm")))
            for path in privateFiles do
                let bytes = File.ReadAllBytes path
                if bytes |> Array.forall (fun value -> value = 9uy || value = 10uy || value = 13uy || value >= 32uy) then Assert.DoesNotContain("PRIVATE-SENTINEL", Encoding.UTF8.GetString bytes)
            let exported = Path.Combine(runtimeRoot, "public.json")
            TelemetryStoreApplication.exportPublic root approved (Some "UTEL-03A") exported |> unwrap |> ignore
            let publicJson = File.ReadAllText exported
            Assert.DoesNotContain("PRIVATE-SENTINEL", publicJson)
            Assert.DoesNotContain("worker-1", publicJson)
            Assert.DoesNotContain("native-thread", publicJson)

    [<Fact>]
    let ``UTEL-03A native exit and output survive total publication failure`` () =
        if not (OperatingSystem.IsWindows()) then
            let cleanup, root = temp ()
            use cleanup = cleanup
            let failing = script root "fake-codex" [ "{\"type\":\"thread.started\",\"thread_id\":\"late-thread\"}" ] 9
            let code = TelemetryRuntimeApplication.runCodexExecWith failing (assignment "worker-fail") [ "--json"; "--ephemeral" ] (fun _ -> Error [ "offline" ])
            Assert.Equal(9, code)

    [<Fact>]
    let ``workspace binding is inherited by the launched child`` () =
        if OperatingSystem.IsLinux() then
            let cleanup, root = temp ()
            use cleanup = cleanup
            let executable = Path.Combine(root, "binding-child")
            File.WriteAllText(executable, "#!/bin/sh\nprintf '%s\\n' \"{\\\"type\\\":\\\"error\\\",\\\"config\\\":\\\"$FSGG_TELEMETRY_CONFIG\\\",\\\"repository\\\":\\\"$FSGG_TELEMETRY_REPOSITORY\\\"}\"\nexit 0\n")
            File.SetUnixFileMode(executable, UnixFileMode.UserRead ||| UnixFileMode.UserWrite ||| UnixFileMode.UserExecute)
            let captured = Path.Combine(root,"binding.txt")
            File.WriteAllText(executable, $"#!/bin/sh\nprintf '%%s\\n%%s\\n%%s\\n' \"$FSGG_TELEMETRY_CONFIG\" \"$FSGG_TELEMETRY_REPOSITORY\" \"$FSGG_TELEMETRY_BINDING_DIGEST\" > '{captured}'\nexit 0\n")
            File.SetUnixFileMode(executable, UnixFileMode.UserRead ||| UnixFileMode.UserWrite ||| UnixFileMode.UserExecute)
            let code = TelemetryRuntimeApplication.runObservedCodexExecWith executable (assignment "worker-binding") None TelemetryRuntime.Root None 60L (Some("/private/config.json","FS-GG/.github",String('a',64))) ["--json";"--ephemeral"] (fun _ -> Error ["offline"])
            Assert.Equal(0, code)
            let output=File.ReadAllText captured
            Assert.Contains("/private/config.json", output)
            Assert.Contains("FS-GG/.github", output)
            Assert.Contains(String('a',64), output)

    [<Fact>]
    let ``UTEL-03A bounded publisher records exhaustion without delaying child`` () =
        if not (OperatingSystem.IsWindows()) then
            let cleanup, root = temp ()
            use cleanup = cleanup
            let frames = [ "{\"type\":\"thread.started\",\"thread_id\":\"queue-thread\"}" ] @ [ for _ in 1..256 -> "{\"type\":\"error\"}" ]
            let executable = script root "fake-codex-fast" frames 0
            let published = ResizeArray<byte array>()
            let publish bytes =
                lock published (fun () -> published.Add bytes)
                System.Threading.Thread.Sleep 2
                Ok "queued"
            Assert.Equal(0, TelemetryRuntimeApplication.runCodexExecWith executable (assignment "queue-worker") [ "--json"; "--ephemeral" ] publish)
            let facts = published |> Seq.collect (fun bytes -> (TelemetryStore.parseBatch bytes |> unwrap).Facts) |> Seq.toList
            Assert.Contains(facts, function | { Payload = TelemetryStore.RuntimeGap(_, "publisher-queue-exhausted") } -> true | _ -> false)

    [<Fact>]
    let ``UTEL-03A assignment file must be bounded private absolute and non-symlinked`` () =
        if not (OperatingSystem.IsWindows()) then
            let cleanup, root = temp ()
            use cleanup = cleanup
            let path = Path.Combine(root, "assignment.json")
            File.WriteAllText(path, "{}")
            File.SetUnixFileMode(path, UnixFileMode.UserRead ||| UnixFileMode.GroupRead)
            Assert.Equal(2, TelemetryRuntimeApplication.runCodexExec [ "--assignment"; path; "--"; "--json"; "--ephemeral" ])
            File.SetUnixFileMode(path, UnixFileMode.UserRead ||| UnixFileMode.UserWrite)
            let link = Path.Combine(root, "assignment-link.json")
            File.CreateSymbolicLink(link, path) |> ignore
            Assert.Equal(2, TelemetryRuntimeApplication.runCodexExec [ "--assignment"; link; "--"; "--json"; "--ephemeral" ])
            let oversized = Path.Combine(root, "oversized.json")
            File.WriteAllBytes(oversized, Array.zeroCreate 8193)
            File.SetUnixFileMode(oversized, UnixFileMode.UserRead ||| UnixFileMode.UserWrite)
            Assert.Equal(2, TelemetryRuntimeApplication.runCodexExec [ "--assignment"; oversized; "--"; "--json"; "--ephemeral" ])

    [<Fact>]
    let ``UTEL-03A runtime batches replay and conflicting native usage stays atomic`` () =
        let eventJson identity input = $"""{{"schema":"%s{TelemetryStore.BatchSchema}","ingestId":"batch-{input}","sourceIdentity":"worker-1","generation":"g1","cursor":"{input}","eventCount":1,"events":[{{"kind":"runtime-turn-usage","identity":"{identity}","itemId":"UTEL-03A","revision":0,"invocationId":"inv-1","threadId":"thread-1","turnId":null,"turnSequence":1,"provider":null,"requestedModel":null,"observedModel":null,"requestedEffort":null,"observedEffort":null,"backend":null,"scope":"completed-turn","provenance":"codex-exec-jsonl","input":{input},"cachedInput":0,"output":1,"reasoning":null,"total":{input + 1}}}]}}""" |> Encoding.UTF8.GetBytes
        let first = TelemetryStore.parseBatch(eventJson "usage-fixed" 2) |> unwrap
        let replay = TelemetryStore.parseBatch(eventJson "usage-fixed" 2) |> unwrap
        let conflict = TelemetryStore.parseBatch(eventJson "usage-fixed" 3) |> unwrap
        Assert.Equal(first.Facts.Head.ContentDigest, replay.Facts.Head.ContentDigest)
        Assert.NotEqual<string>(first.Facts.Head.ContentDigest, conflict.Facts.Head.ContentDigest)

    [<Fact>]
    let ``UTEL-03A launcher population reports missing lifecycle signals independently`` () =
        let cleanup, root = temp ()
        use cleanup = cleanup
        TelemetryStoreApplication.initialize root approved |> unwrap |> ignore
        let bytes = Encoding.UTF8.GetBytes $"""{{"schema":"%s{TelemetryStore.BatchSchema}","ingestId":"population-gaps","sourceIdentity":"worker-gaps","generation":"g1","cursor":"1","eventCount":3,"events":[{{"kind":"runtime-admission","identity":"admission-a","itemId":"UTEL-03A","revision":0,"invocationId":"inv-a","featureId":"UTEL-03A","attemptId":"attempt-a","parentAttemptId":null,"producerStream":"worker-gaps","requestedModel":null,"requestedEffort":null,"backend":null}},{{"kind":"runtime-start","identity":"start-unregistered","itemId":"UTEL-03A","revision":0,"invocationId":"inv-unregistered","threadId":null,"turnId":null,"turnSequence":null,"processId":42,"phase":"process"}},{{"kind":"runtime-terminal","identity":"terminal-unregistered","itemId":"UTEL-03A","revision":0,"invocationId":"inv-unregistered","threadId":null,"outcome":"failed","exitCode":1}}]}}"""
        TelemetryStoreApplication.ingest root approved bytes |> unwrap |> ignore
        let summary = TelemetryStoreApplication.summary root approved "UTEL-03A" |> unwrap
        Assert.Contains("\"admitted\":1", summary)
        Assert.Contains("\"missingAdmission\":1", summary)
        Assert.Contains("\"missingStart\":1", summary)
        Assert.Contains("\"missingTerminal\":1", summary)
        Assert.Contains("\"missingUsage\":1", summary)

    [<Fact>]
    let ``UTEL-03A command shapes require explicit ephemeral JSON and report collaboration unsupported`` () =
        Assert.Equal(Some(Ok()), TelemetryApplication.validateInvocation [ "telemetry"; "runtime"; "status" ])
        Assert.Equal(Some(Ok()), TelemetryApplication.validateInvocation [ "telemetry"; "runtime"; "codex-exec"; "--relation"; "follow-up"; "--"; "--json"; "--ephemeral" ])
        Assert.Equal(Some(Ok()), TelemetryApplication.validateInvocation [ "telemetry"; "runtime"; "codex-exec"; "--assignment"; "/private/attempt.json"; "--late-after-seconds"; "60"; "--"; "--json"; "--ephemeral" ])
        Assert.Equal(2, TelemetryRuntimeApplication.runCodexExecWith "unused" (assignment "worker-1") [ "--json" ] (fun _ -> Ok ""))

    [<Fact>]
    let ``UTEL-06_2 packaged launcher machine observes inherited root child grandchild follow-up and retries`` () =
        if not (OperatingSystem.IsWindows()) then
            let cleanup, root = temp ()
            use cleanup = cleanup
            let contextPath name = Path.Combine(root, name + ".context")
            let argumentPath name = Path.Combine(root, name + ".args")
            let workingPath name = Path.Combine(root, name + ".cwd")
            let executable name delay exitCode =
                let path = Path.Combine(root, name)
                let content =
                    "#!/bin/sh\n" +
                    (sprintf "printf '%%s' \"$%s\" > '%s'\n" TelemetryRuntime.InvocationContextEnvironment (contextPath name)) +
                    (sprintf "printf '%%s\\n' \"$@\" > '%s'\n" (argumentPath name)) +
                    (sprintf "pwd > '%s'\n" (workingPath name)) +
                    (if delay then "sleep 0.05\n" else "") +
                    "printf '%s\\n' '{\"type\":\"thread.started\",\"thread_id\":\"native-thread\"}'\n" +
                    "printf '%s\\n' '{\"type\":\"turn.completed\",\"turn_id\":\"native-turn\",\"usage\":{\"input_tokens\":3,\"cached_input_tokens\":1,\"output_tokens\":2}}'\n" +
                    $"exit %d{exitCode}\n"
                File.WriteAllText(path, content)
                File.SetUnixFileMode(path, UnixFileMode.UserRead ||| UnixFileMode.UserWrite ||| UnixFileMode.UserExecute)
                path
            let parseContext name = File.ReadAllBytes(contextPath name) |> TelemetryRuntime.parseInvocationContext |> unwrap
            let facts = ResizeArray<TelemetryStore.Fact>()
            let rootExecutable = executable "root" false 0
            observed rootExecutable None TelemetryRuntime.Root 0 |> List.iter facts.Add
            let rootContext = parseContext "root"
            let childExecutable = executable "child" true 0
            observed childExecutable (Some rootContext) TelemetryRuntime.Child 0 |> List.iter facts.Add
            let childContext = parseContext "child"
            let grandchildExecutable = executable "grandchild" false 0
            observed grandchildExecutable (Some childContext) TelemetryRuntime.Child 0 |> List.iter facts.Add
            let grandchildContext = parseContext "grandchild"
            let followExecutable = executable "follow" false 0
            observed followExecutable (Some childContext) TelemetryRuntime.FollowUp 0 |> List.iter facts.Add
            let followContext = parseContext "follow"
            let retryExecutable = executable "retry" false 0
            observed retryExecutable (Some rootContext) TelemetryRuntime.Child 0 |> List.iter facts.Add
            let retryContext = parseContext "retry"

            Assert.Equal(rootContext.ActivationId, childContext.ActivationId)
            Assert.Equal(rootContext.ActivationId, grandchildContext.ActivationId)
            Assert.Equal(rootContext.InvocationId, childContext.RootInvocationId)
            Assert.Equal(rootContext.InvocationId, grandchildContext.RootInvocationId)
            Assert.Equal(rootContext.InvocationId, followContext.RootInvocationId)
            Assert.NotEqual<string>(childContext.InvocationId, retryContext.InvocationId)
            Assert.Equal(Directory.GetCurrentDirectory(), File.ReadAllText(workingPath "root").Trim())
            let arguments = File.ReadAllLines(argumentPath "root")
            Assert.Equal("exec", arguments[0])
            Assert.Contains("gpt-test", arguments)
            Assert.Contains("workspace-write", arguments)

            let activations = facts |> Seq.choose (fun fact -> match fact.Payload with TelemetryStore.OperationalActivation _ -> Some fact | _ -> None) |> Seq.length
            let dispatches = facts |> Seq.choose (fun fact -> match fact.Payload with TelemetryStore.ExpectedDispatch(a,b,c,d,e,f,g) -> Some(a,b,c,d,e,f,g) | _ -> None) |> Seq.toList
            let lineages = facts |> Seq.choose (fun fact -> match fact.Payload with TelemetryStore.InvocationLineage(a,b,c,d,e,f) -> Some(a,b,c,d,e,f) | _ -> None) |> Seq.toList
            let admissions = facts |> Seq.filter (fun fact -> match fact.Payload with TelemetryStore.RuntimeAdmission _ -> true | _ -> false) |> Seq.length
            let eventTimes = facts |> Seq.choose (fun fact -> match fact.Payload with TelemetryStore.EventTime(_,event,_,_,_,_) -> Some event | _ -> None) |> Seq.toList
            Assert.Equal(1, activations)
            Assert.Equal(5, dispatches.Length)
            Assert.Equal(5, lineages.Length)
            Assert.Equal(5, admissions)
            Assert.Equal(5, eventTimes |> List.filter ((=) "admission") |> List.length)
            Assert.Equal(5, eventTimes |> List.filter ((=) "start") |> List.length)
            Assert.Equal(5, eventTimes |> List.filter ((=) "terminal") |> List.length)
            Assert.Contains(dispatches, fun (_,_,relation,parent,_,_,_) -> relation = "root" && parent.IsNone)
            Assert.Contains(dispatches, fun (_,_,relation,parent,_,_,_) -> relation = "follow-up" && parent = Some childContext.DispatchId)
            Assert.Contains(lineages, fun (_,invocation,relation,parent,rootInvocation,_) -> invocation = grandchildContext.InvocationId && relation = "child" && parent = Some childContext.InvocationId && rootInvocation = rootContext.InvocationId)
            Assert.Contains(facts, function | { Payload = TelemetryStore.RuntimeAdmission(_,_,_,_,_,Some "gpt-test",Some "high",_) } -> true | _ -> false)

    [<Fact>]
    let ``UTEL-06_2 launch failure cancellation and no-op stay native while facts remain machine authored`` () =
        if not (OperatingSystem.IsWindows()) then
            let cleanup, root = temp ()
            use cleanup = cleanup
            let noOp = script root "fake-codex-no-op" [] 0
            let cancelled = script root "fake-codex-cancelled" [] 130
            let noOpFacts = observed noOp None TelemetryRuntime.Root 0
            let cancelledFacts = observed cancelled None TelemetryRuntime.Root 130
            let failedFacts = observed (Path.Combine(root, "missing-codex")) None TelemetryRuntime.Root 127
            Assert.Contains(noOpFacts, function | { Payload = TelemetryStore.RuntimeGap(_, "exit-zero-without-usage") } -> true | _ -> false)
            Assert.Contains(cancelledFacts, function | { Payload = TelemetryStore.RuntimeTerminal(_,_,"cancelled",130L) } -> true | _ -> false)
            Assert.Contains(failedFacts, function | { Payload = TelemetryStore.RuntimeTerminal(_,_,"launch-failed",127L) } -> true | _ -> false)
            Assert.DoesNotContain(failedFacts, function | { Payload = TelemetryStore.EventTime(_,"start",_,_,_,_) } -> true | _ -> false)
            Assert.Contains(failedFacts, function | { Payload = TelemetryStore.EventTime(_,"terminal",_,_,_,_) } -> true | _ -> false)

    [<Fact>]
    let ``UTEL-06_2 executable launcher facts ingest through migration five and reconcile without authored batches`` () =
        if not (OperatingSystem.IsWindows()) then
            let cleanup, root = temp ()
            use cleanup = cleanup
            let runtimeCleanup, runtimeRoot = temp ()
            use runtimeCleanup = runtimeCleanup
            TelemetryStoreApplication.initialize root approved |> unwrap |> ignore
            let executable =
                script runtimeRoot "fake-codex-reconcile"
                    [ "{\"type\":\"thread.started\",\"thread_id\":\"native-thread\"}"
                      "{\"type\":\"turn.completed\",\"turn_id\":\"native-turn\",\"usage\":{\"input_tokens\":2,\"cached_input_tokens\":0,\"output_tokens\":1}}" ] 0
            let publish bytes = TelemetryStoreApplication.publish root approved bytes
            let code =
                TelemetryRuntimeApplication.runObservedCodexExecWith executable (assignment "migration-five-worker") None TelemetryRuntime.Root
                    (Some root) 60L None [ "--json"; "--ephemeral"; "synthetic" ] publish
            Assert.Equal(0, code)
            TelemetryStoreApplication.drain root approved |> unwrap |> ignore
            let reconciliation = TelemetryStoreApplication.reconcile root approved "UTEL-03A" |> unwrap
            Assert.Contains("\"matched\":1", reconciliation)
            Assert.Contains("\"complete\":1", reconciliation)

    [<Fact>]
    let ``UTEL-06_2 packaged command consumes inherited context after root process death`` () =
        if not (OperatingSystem.IsWindows()) then
            let cleanup, root = temp ()
            use cleanup = cleanup
            let assignmentPath = Path.Combine(root, "assignment.json")
            File.WriteAllText(assignmentPath, """{"schema":"fsgg.telemetry.codex-assignment/1","featureId":"UTEL-06","itemId":"UTEL-06.2","attemptId":"attempt-1","parentAttemptId":null,"producerStream":"worker-command"}""")
            File.SetUnixFileMode(assignmentPath, UnixFileMode.UserRead ||| UnixFileMode.UserWrite)
            let contextPath = Path.Combine(root, "observed.context")
            let codex = Path.Combine(root, "codex")
            File.WriteAllText(codex,
                "#!/bin/sh\n" +
                (sprintf "printf '%%s' \"$%s\" > '%s'\n" TelemetryRuntime.InvocationContextEnvironment contextPath) +
                "printf '%s\\n' '{\"type\":\"thread.started\",\"thread_id\":\"command-thread\"}'\n" +
                "printf '%s\\n' '{\"type\":\"turn.completed\",\"usage\":{\"input_tokens\":1,\"cached_input_tokens\":0,\"output_tokens\":1}}'\n")
            File.SetUnixFileMode(codex, UnixFileMode.UserRead ||| UnixFileMode.UserWrite ||| UnixFileMode.UserExecute)
            let originalPath = Environment.GetEnvironmentVariable "PATH"
            let originalContext = Environment.GetEnvironmentVariable TelemetryRuntime.InvocationContextEnvironment
            try
                Environment.SetEnvironmentVariable("PATH", root + string Path.PathSeparator + originalPath)
                Environment.SetEnvironmentVariable(TelemetryRuntime.InvocationContextEnvironment, null)
                Assert.Equal(0, TelemetryRuntimeApplication.runCodexExec [ "--assignment"; assignmentPath; "--"; "--json"; "--ephemeral"; "root" ])
                let rootContext = File.ReadAllBytes contextPath |> TelemetryRuntime.parseInvocationContext |> unwrap
                Environment.SetEnvironmentVariable(TelemetryRuntime.InvocationContextEnvironment, File.ReadAllText contextPath)
                Assert.Equal(0, TelemetryRuntimeApplication.runCodexExec [ "--relation"; "follow-up"; "--"; "--json"; "--ephemeral"; "follow" ])
                let followContext = File.ReadAllBytes contextPath |> TelemetryRuntime.parseInvocationContext |> unwrap
                Assert.Equal(rootContext.ActivationId, followContext.ActivationId)
                Assert.Equal(rootContext.InvocationId, followContext.RootInvocationId)
                Assert.NotEqual<string>(rootContext.InvocationId, followContext.InvocationId)
            finally
                Environment.SetEnvironmentVariable("PATH", originalPath)
                Environment.SetEnvironmentVariable(TelemetryRuntime.InvocationContextEnvironment, originalContext)
