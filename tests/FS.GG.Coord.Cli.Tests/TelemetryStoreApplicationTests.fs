namespace FS.GG.Coord.Cli.Tests

open System
open System.Diagnostics
open System.IO
open System.Text
open Xunit
open Microsoft.Data.Sqlite
open FS.GG.Coord
open FS.GG.Coord.Cli

module TelemetryStoreApplicationTests =
    let private approved = TelemetryStore.ApprovedLocalDurable
    let private unwrap = function Ok value -> value | Error errors -> failwithf "%A" errors
    let private batch ingest identity revision cursor input =
        Encoding.UTF8.GetBytes $"""{{"schema":"%s{TelemetryStore.BatchSchema}","ingestId":"%s{ingest}","sourceIdentity":"worker-a","generation":"g1","cursor":"%s{cursor}","eventCount":3,"events":[{{"kind":"item","identity":"UTEL-02","itemId":"UTEL-02","revision":0}},{{"kind":"usage","identity":"%s{identity}","itemId":"UTEL-02","revision":%d{revision},"provider":"OpenAI","model":"sol","effort":"medium","input":%d{input},"cachedInput":2,"cacheWriteInput":1,"output":5,"reasoning":2,"total":%d{input + 5L},"responses":1,"sessions":1,"turns":1}},{{"kind":"delivery","identity":"delivery-1","itemId":"UTEL-02","revision":0,"state":"delivered","expectedHead":null,"observedHead":null,"prNumber":3348}}]}}"""
    let private root () =
        let path = Path.Combine(Path.GetTempPath(), "fsgg-utel02-" + Guid.NewGuid().ToString("N"))
        { new IDisposable with member _.Dispose() = if Directory.Exists path then Directory.Delete(path, true) }, path
    let private pragma path value =
        use connection = new SqliteConnection($"Data Source=%s{Path.Combine(path, TelemetryStoreApplication.databaseFileName)};Pooling=False")
        connection.Open()
        use command = connection.CreateCommand()
        command.CommandText <- $"PRAGMA user_version=%d{value};"
        command.ExecuteNonQuery() |> ignore
    let private dropBudgetSchema = "DROP TABLE budget_interventions; DROP TABLE budget_breaches; DROP TABLE budget_assessment_revisions; DROP TABLE budget_epoch_membership; DROP INDEX budget_one_open_epoch; DROP TABLE budget_epochs; DROP TABLE budget_dirty_items; DROP TABLE budget_shared_cost_refs; DROP TABLE budget_intervention_facts; DROP TABLE budget_interval_facts; DROP TABLE budget_attribution_facts; DROP TABLE budget_population_facts; DELETE FROM schema_migrations WHERE version=4;"
    let private dropOperationalSchema = "DROP TABLE operational_event_times; DROP TABLE invocation_lineage; DROP TABLE expected_dispatches; DROP TABLE operational_activations; DELETE FROM schema_migrations WHERE version=5;"
    let private dropCiPopulationSchema = "DROP TABLE ci_population_coverage; DROP TABLE ci_check_runs; DROP TABLE ci_population_admissions; DELETE FROM schema_migrations WHERE version=6;"
    let private dropReviewSchema = "DROP INDEX process_review_attempt_subject; DROP INDEX process_review_item_subject; DROP INDEX activity_spans_item_attempt; DROP INDEX activity_usage_item; DROP INDEX complication_events_item; DROP TABLE complication_events; DROP TABLE activity_usage_attributions; DROP TABLE activity_spans; DROP TABLE process_reviews; DELETE FROM schema_migrations WHERE version=8;"
    let private dropNativeOutcomeSchema = dropReviewSchema + " DROP INDEX native_item_outcomes_item_observed; DROP TABLE native_item_outcomes; DELETE FROM schema_migrations WHERE version=7;"
    let private budgetBatch ingest cursor events =
        Encoding.UTF8.GetBytes $"""{{"schema":"{TelemetryStore.BatchSchema}","ingestId":"{ingest}","sourceIdentity":"budget-worker","generation":"g1","cursor":"{cursor}","eventCount":{List.length events},"events":[{String.concat "," events}]}}"""
    let private population item revision state source =
        $"""{{"kind":"budget-population","identity":"population-{item}","itemId":"{item}","revision":{revision},"originalItemId":"{item}","state":"{state}","sourceKind":"native-item","sourceRef":"{source}"}}"""
    let private attribution item revision numerator denominator coverage attribution sourceKind source =
        let number value = value |> Option.map string |> Option.defaultValue "null"
        $"""{{"kind":"budget-attribution","identity":"attribution-{item}","itemId":"{item}","revision":{revision},"dimension":"model-tokens","provider":"openai","accountingScope":"whole-item","numerator":{number numerator},"denominator":{number denominator},"coverage":"{coverage}","attribution":"{attribution}","sourceKind":"{sourceKind}","sourceRef":"{source}"}}"""
    let private operationalBatch ingest item events =
        Encoding.UTF8.GetBytes $"""{{"schema":"{TelemetryStore.BatchSchema}","ingestId":"{ingest}","sourceIdentity":"operational-observer","generation":"g1","cursor":"{ingest}","eventCount":{List.length events},"events":[{String.concat "," events}]}}"""
    let private ciPopulationBatch ingest item revision checkStatus coverage continuation =
        Encoding.UTF8.GetBytes $"""{{"schema":"{TelemetryStore.BatchSchema}","ingestId":"{ingest}","sourceIdentity":"ci-population","generation":"candidate-a","cursor":"{ingest}","eventCount":3,"events":[{{"kind":"ci-population-admission","identity":"ci-admission-a","itemId":"{item}","revision":1,"collectionId":"collection-a","repository":"o/r","prNumber":7,"baseRef":"main","baseSha":"dddddddddddddddddddddddddddddddddddddddd","head":"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa","witness":"native-pr-head"}},{{"kind":"ci-check","identity":"ci-check-a","itemId":"{item}","revision":{revision},"repository":"o/r","checkId":101,"name":"build","appSlug":"github-actions","status":"{checkStatus}","conclusion":null,"startedAt":"2026-09-08T10:00:00Z","completedAt":null}},{{"kind":"ci-population-coverage","identity":"ci-coverage-a","itemId":"{item}","revision":{revision},"collectionId":"collection-a","actions":"{coverage}","checks":"{coverage}","attempts":"{coverage}","jobs":"{coverage}","terminal":"{coverage}","timestamps":"{coverage}","continuation":"{continuation}","externalChecks":0,"gaps":"[]"}}]}}"""
    let private activation item runtime delay =
        $"""{{"kind":"operational-activation","identity":"activation-{item}","itemId":"{item}","revision":0,"activationId":"activation-{item}","scope":"explicit-future-dispatches","runtime":"{runtime}","activatedAt":"2026-09-08T10:00:00Z","clockProvenance":"host-wall","lateAfterSeconds":{delay}}}"""
    let private dispatch item id relation parent runtime minute =
        let parentValue = parent |> Option.map (fun value -> $"\"{value}\"") |> Option.defaultValue "null"
        $"""{{"kind":"expected-dispatch","identity":"expected-{item}-{id}","itemId":"{item}","revision":0,"dispatchId":"{id}","activationId":"activation-{item}","relation":"{relation}","parentDispatchId":{parentValue},"runtime":"{runtime}","expectedAt":"2026-09-08T10:{minute}:00Z","clockProvenance":"host-wall"}}"""
    let private lineage item identity dispatchId invocationId relation parentInvocation root runtime =
        let parentValue = parentInvocation |> Option.map (fun value -> $"\"{value}\"") |> Option.defaultValue "null"
        $"""{{"kind":"invocation-lineage","identity":"{identity}","itemId":"{item}","revision":0,"dispatchId":"{dispatchId}","invocationId":"{invocationId}","relation":"{relation}","parentInvocationId":{parentValue},"rootInvocationId":"{root}","runtime":"{runtime}"}}"""
    let private eventTime item identity invocation event occurred occurredClock observed observedClock =
        let timestamp value = value |> Option.map (fun text -> $"\"{text}\"") |> Option.defaultValue "null"
        let clock value = value |> Option.map (fun text -> $"\"{text}\"") |> Option.defaultValue "null"
        $"""{{"kind":"event-time","identity":"{identity}","itemId":"{item}","revision":0,"invocationId":"{invocation}","event":"{event}","occurredAt":{timestamp occurred},"occurredClockProvenance":{clock occurredClock},"observedAt":{timestamp observed},"observedClockProvenance":{clock observedClock}}}"""
    let private completeTimes item prefix invocation minute observedSecond =
        [ for event in [ "admission"; "start"; "terminal" ] do
            yield eventTime item ($"time-{prefix}-{event}") invocation event (Some $"2026-09-08T10:{minute}:00Z") (Some "provider-native") (Some $"2026-09-08T10:{minute}:{observedSecond}Z") (Some "provider-native") ]
    let private nativeOutcome item revision outcome code observedAt =
        let merge = if code = "delivered" then "\"bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb\"" else "null"
        let occurred = if code = "delivered" then "\"2026-09-08T10:04:00Z\"" else "null"
        $"""{{"kind":"native-item-outcome","identity":"native-outcome-{item}","itemId":"{item}","revision":{revision},"repository":"o/r","prNumber":7,"baseRef":"main","baseSha":"dddddddddddddddddddddddddddddddddddddddd","head":"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa","outcome":"{outcome}","codeDelivery":"{code}","mergeCommit":{merge},"occurredAt":{occurred},"observedAt":"{observedAt}","sourceKind":"routine-delivery","sourceRef":"routine-delivery:{item}"}}"""
    let private runtimeTerminal item identity invocation outcome exitCode =
        $"""{{"kind":"runtime-terminal","identity":"{identity}","itemId":"{item}","revision":0,"invocationId":"{invocation}","threadId":"thread-{invocation}","outcome":"{outcome}","exitCode":{exitCode}}}"""
    let private runtimeUsage item identity invocation total =
        $"""{{"kind":"runtime-turn-usage","identity":"{identity}","itemId":"{item}","revision":0,"invocationId":"{invocation}","threadId":"thread-{invocation}","turnId":"turn-{invocation}","turnSequence":1,"provider":"openai","requestedModel":"sol","observedModel":"sol","requestedEffort":"medium","observedEffort":"medium","backend":null,"scope":"completed-turn","provenance":"codex-exec-jsonl","input":{total - 1L},"cachedInput":0,"output":1,"reasoning":null,"total":{total}}}"""
    let private ciStep item =
        $"""{{"kind":"ci-step","identity":"ci-step-{item}","itemId":"{item}","revision":1,"repository":"o/r","runId":11,"attempt":1,"jobId":12,"number":1,"name":"routine ceremony","status":"completed","conclusion":"success","startedAt":"2026-09-08T10:02:00Z","completedAt":"2026-09-08T10:03:00Z","classification":"admin","rationale":"exact profile match"}}"""

    [<Fact>]
    let ``UTEL-02 init publish drain reopen replay and public summary are durable`` () =
        let cleanup, path = root ()
        use cleanup = cleanup
        let initialized = TelemetryStoreApplication.initialize path approved |> unwrap
        Assert.Contains("\"status\":\"ready\"", initialized)
        Assert.Matches("\"nativeEngine\":\"3\\.(5[1-9]|[6-9][0-9])", initialized)
        Assert.Contains("\"remaining\":0", TelemetryStoreApplication.drain path approved |> unwrap)
        let bytes = batch "batch-1" "usage-1" 0L "c1" 10L
        let published = TelemetryStoreApplication.publish path approved bytes |> unwrap
        Assert.Contains("\"status\":\"queued\"", published)
        Assert.Single(Directory.GetFiles(Path.Combine(path, "inbox", "worker-a"), "*.ready")) |> ignore
        let drained = TelemetryStoreApplication.drain path approved |> unwrap
        Assert.Contains("\"accepted\":3", drained)
        Assert.Empty(Directory.GetFiles(Path.Combine(path, "inbox", "worker-a"), "*.ready"))
        let first = TelemetryStoreApplication.summary path approved "UTEL-02" |> unwrap
        Assert.Contains("\"total\":15", first)
        Assert.Contains("\"populationCoverage\":\"unknown\"", first)
        Assert.Contains("\"qualification\":\"not-evaluated\"", first)
        let replay = batch "batch-2" "usage-1" 0L "c2" 10L
        TelemetryStoreApplication.ingest path approved replay |> unwrap |> ignore
        Assert.Equal(first, TelemetryStoreApplication.summary path approved "UTEL-02" |> unwrap)
        Assert.Contains("\"status\":\"ready\"", TelemetryStoreApplication.status path approved |> unwrap)

    [<Fact>]
    let ``UTEL-02 conflicting replay quarantines atomically and preserves accepted totals`` () =
        let cleanup, path = root ()
        use cleanup = cleanup
        TelemetryStoreApplication.initialize path approved |> unwrap |> ignore
        TelemetryStoreApplication.ingest path approved (batch "batch-1" "usage-1" 0L "c1" 10L) |> unwrap |> ignore
        let before = TelemetryStoreApplication.summary path approved "UTEL-02" |> unwrap
        TelemetryStoreApplication.publish path approved (batch "batch-2" "usage-1" 0L "c2" 11L) |> unwrap |> ignore
        let result = TelemetryStoreApplication.drain path approved |> unwrap
        Assert.Contains("\"quarantined\":1", result)
        Assert.Equal(before, TelemetryStoreApplication.summary path approved "UTEL-02" |> unwrap)
        Assert.Single(Directory.GetFiles(Path.Combine(path, "quarantine"), "*.rejected", SearchOption.AllDirectories)) |> ignore

    [<Fact>]
    let ``UTEL-02 live writer is never displaced and leaves ready batch queued`` () =
        if OperatingSystem.IsLinux() then
            let cleanup, path = root ()
            use cleanup = cleanup
            TelemetryStoreApplication.initialize path approved |> unwrap |> ignore
            TelemetryStoreApplication.publish path approved (batch "batch-1" "usage-1" 0L "c1" 10L) |> unwrap |> ignore
            let start = ProcessStartInfo("flock")
            start.ArgumentList.Add(Path.Combine(path, "writer.lock")); start.ArgumentList.Add("sleep"); start.ArgumentList.Add("2")
            start.UseShellExecute <- false
            use holder = Process.Start start
            System.Threading.Thread.Sleep 150
            match TelemetryStoreApplication.drain path approved with
            | Error [ "writer-busy" ] -> ()
            | result -> failwithf "unexpected lock result: %A" result
            Assert.Single(Directory.GetFiles(Path.Combine(path, "inbox", "worker-a"), "*.ready")) |> ignore
            Assert.Contains("\"total\":0", TelemetryStoreApplication.summary path approved "UTEL-02" |> unwrap)
            holder.WaitForExit()
            Assert.Contains("\"accepted\":3", TelemetryStoreApplication.drain path approved |> unwrap)

    [<Fact>]
    let ``UTEL-02 unconfigured status has no fallback creation`` () =
        let previous = Environment.GetEnvironmentVariable "FSGG_TELEMETRY_STORE"
        try
            Environment.SetEnvironmentVariable("FSGG_TELEMETRY_STORE", null)
            let code = TelemetryStoreApplication.run "status" []
            Assert.Equal(0, code)
        finally Environment.SetEnvironmentVariable("FSGG_TELEMETRY_STORE", previous)

    [<Fact>]
    let ``UTEL-02 crash boundaries retain ready replay and rollback before commit`` () =
        let cleanup, path = root ()
        use cleanup = cleanup
        TelemetryStoreApplication.initialize path approved |> unwrap |> ignore
        let payload = batch "batch-1" "usage-1" 0L "c1" 10L
        TelemetryStoreApplication.publish path approved payload |> unwrap |> ignore
        let beforeCommit: TelemetryStoreApplication.DrainHooks =
            { BeforeCommit = (fun () -> raise (OperationCanceledException "synthetic precommit termination"))
              AfterCommitBeforeDelete = fun () -> () }
        Assert.Contains("precommit", sprintf "%A" (TelemetryStoreApplication.drainWithHooks path approved beforeCommit))
        Assert.Contains("\"total\":0", TelemetryStoreApplication.summary path approved "UTEL-02" |> unwrap)
        Assert.Single(Directory.GetFiles(Path.Combine(path, "inbox", "worker-a"), "*.ready")) |> ignore
        let afterCommit: TelemetryStoreApplication.DrainHooks =
            { BeforeCommit = fun () -> ()
              AfterCommitBeforeDelete = fun () -> raise (IOException "synthetic postcommit termination") }
        Assert.Contains("committed but ready removal failed", sprintf "%A" (TelemetryStoreApplication.drainWithHooks path approved afterCommit))
        let committed = TelemetryStoreApplication.summary path approved "UTEL-02" |> unwrap
        Assert.Contains("\"total\":15", committed)
        Assert.Single(Directory.GetFiles(Path.Combine(path, "inbox", "worker-a"), "*.ready")) |> ignore
        Assert.Contains("\"replayed\":3", TelemetryStoreApplication.drain path approved |> unwrap)
        Assert.Equal(committed, TelemetryStoreApplication.summary path approved "UTEL-02" |> unwrap)

    [<Fact>]
    let ``UTEL-02 malformed ready and temporary files recover without becoming coverage`` () =
        let cleanup, path = root ()
        use cleanup = cleanup
        TelemetryStoreApplication.initialize path approved |> unwrap |> ignore
        let producer = Path.Combine(path, "inbox", "worker-a")
        Directory.CreateDirectory producer |> ignore
        File.WriteAllText(Path.Combine(producer, ".unfinished.tmp"), "partial")
        File.WriteAllText(Path.Combine(producer, "bad.deadbeef.ready"), "not-json")
        Assert.Contains("\"quarantined\":1", TelemetryStoreApplication.drain path approved |> unwrap)
        Assert.True(File.Exists(Path.Combine(producer, ".unfinished.tmp")))
        Assert.Contains("\"populationCoverage\":\"unknown\"", TelemetryStoreApplication.summary path approved "UTEL-02" |> unwrap)

    [<Fact>]
    let ``UTEL-02 newer schema leaves immutable inbox and public all-item export is bounded`` () =
        let cleanup, path = root ()
        use cleanup = cleanup
        TelemetryStoreApplication.initialize path approved |> unwrap |> ignore
        let payload = batch "batch-1" "usage-1" 0L "c1" 10L
        TelemetryStoreApplication.publish path approved payload |> unwrap |> ignore
        pragma path 9
        Assert.Contains("newer than supported", sprintf "%A" (TelemetryStoreApplication.drain path approved))
        Assert.Single(Directory.GetFiles(Path.Combine(path, "inbox", "worker-a"), "*.ready")) |> ignore
        pragma path 8
        TelemetryStoreApplication.drain path approved |> unwrap |> ignore
        let output = Path.Combine(Path.GetTempPath(), "fsgg-utel02-public-" + Guid.NewGuid().ToString("N") + ".json")
        try
            TelemetryStoreApplication.exportPublic path approved None output |> unwrap |> ignore
            let exported = File.ReadAllText output
            Assert.Contains("fsgg.telemetry.public-export/1", exported)
            Assert.Contains("UTEL-02", exported)
            Assert.True(FileInfo(output).Length <= 65536L)
            Assert.Contains("private store root", sprintf "%A" (TelemetryStoreApplication.exportPublic path approved None (Path.Combine(path, "unsafe.json"))))
        finally if File.Exists output then File.Delete output

    [<Fact>]
    let ``UTEL-02 producer inbox rejects the 129th pending batch`` () =
        let cleanup, path = root ()
        use cleanup = cleanup
        TelemetryStoreApplication.initialize path approved |> unwrap |> ignore
        for index in 1 .. 128 do
            TelemetryStoreApplication.publish path approved (batch ($"batch-%03d{index}") "usage-1" 0L ($"c%d{index}") 10L) |> unwrap |> ignore
        Assert.Contains("inbox is full", sprintf "%A" (TelemetryStoreApplication.publish path approved (batch "batch-129" "usage-1" 0L "c129" 10L)))

    [<Fact>]
    let ``UTEL-02 command shapes include publish drain and item-optional public export`` () =
        Assert.Equal(Some(Ok()), TelemetryApplication.validateInvocation [ "telemetry"; "store"; "publish"; "--input"; "batch.json" ])
        Assert.Equal(Some(Ok()), TelemetryApplication.validateInvocation [ "telemetry"; "store"; "drain" ])
        Assert.Equal(Some(Ok()), TelemetryApplication.validateInvocation [ "telemetry"; "store"; "export"; "--public"; "--output"; "public.json" ])
        Assert.Equal(Some(Ok()), TelemetryApplication.validateInvocation [ "telemetry"; "review"; "summary"; "--item"; "UTEL-08" ])
        Assert.Equal(Some(Ok()), TelemetryApplication.validateInvocation [ "telemetry"; "item-detail"; "--item"; "UTEL-08" ])

    [<Fact>]
    let ``UTEL-02 changed unsafe permissions refuse before worker publication`` () =
        if not (OperatingSystem.IsWindows()) then
            let cleanup, path = root ()
            use cleanup = cleanup
            TelemetryStoreApplication.initialize path approved |> unwrap |> ignore
            File.SetUnixFileMode(path, UnixFileMode.UserRead ||| UnixFileMode.UserWrite ||| UnixFileMode.UserExecute ||| UnixFileMode.GroupWrite)
            try
                Assert.Contains("permissions", sprintf "%A" (TelemetryStoreApplication.publish path approved (batch "batch-1" "usage-1" 0L "c1" 10L)))
                Assert.False(Directory.Exists(Path.Combine(path, "inbox")))
            finally File.SetUnixFileMode(path, UnixFileMode.UserRead ||| UnixFileMode.UserWrite ||| UnixFileMode.UserExecute)

    [<Fact>]
    let ``UTEL-03A v1 store migrates transactionally to runtime schema v2`` () =
        let cleanup, path = root ()
        use cleanup = cleanup
        TelemetryStoreApplication.initialize path approved |> unwrap |> ignore
        use connection = new SqliteConnection($"Data Source=%s{Path.Combine(path, TelemetryStoreApplication.databaseFileName)};Pooling=False")
        connection.Open()
        use command = connection.CreateCommand()
        command.CommandText <- dropNativeOutcomeSchema + " " + dropCiPopulationSchema + " " + dropOperationalSchema + " " + dropBudgetSchema + " DROP TABLE ci_coverage; DROP TABLE ci_steps; DROP TABLE ci_jobs; DROP TABLE ci_runs; DROP TABLE ci_pages; DROP TABLE ci_bindings; DROP INDEX runtime_thread_start_identity; DROP INDEX runtime_turn_start_identity; DROP INDEX runtime_turn_native_identity; DROP TABLE runtime_gaps; DROP TABLE runtime_terminals; DROP TABLE runtime_turn_usage; DROP TABLE runtime_starts; DROP TABLE runtime_admissions; DELETE FROM schema_migrations WHERE version IN (2,3); PRAGMA user_version=1;"
        command.ExecuteNonQuery() |> ignore
        connection.Close()
        let initialized = TelemetryStoreApplication.initialize path approved |> unwrap
        Assert.Contains("\"schemaVersion\":8", initialized)
        Assert.Contains("\"status\":\"ready\"", TelemetryStoreApplication.status path approved |> unwrap)

    [<Fact>]
    let ``UTEL-04A CI observations migrate ingest and summarize without private fields`` () =
        let cleanup, path = root ()
        use cleanup = cleanup
        Assert.Contains("\"schemaVersion\":8", TelemetryStoreApplication.initialize path approved |> unwrap)
        let ci = Encoding.UTF8.GetBytes $"""{{"schema":"{TelemetryStore.BatchSchema}","ingestId":"ci-batch-1","sourceIdentity":"ci-worker","generation":"g1","cursor":"1","eventCount":4,"events":[{{"kind":"ci-binding","identity":"ci-binding-1","itemId":"UTEL-04A","revision":0,"collectionId":"collection-1","repository":"o/r","head":"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa","prNumber":1,"workflow":"ci.yml","featureId":"UTEL","attemptId":"a1","parentAttemptId":null,"producerStream":"ci-worker","binding":"exact"}},{{"kind":"ci-job","identity":"ci-job-1","itemId":"UTEL-04A","revision":0,"repository":"o/r","runId":10,"attempt":1,"jobId":101,"name":"build","status":"completed","conclusion":"success","createdAt":"2026-01-01T00:00:00Z","startedAt":"2026-01-01T00:00:00Z","completedAt":"2026-01-01T00:01:00Z"}},{{"kind":"ci-step","identity":"ci-step-1","itemId":"UTEL-04A","revision":0,"repository":"o/r","runId":10,"attempt":1,"jobId":101,"number":1,"name":"test","status":"completed","conclusion":"success","startedAt":"2026-01-01T00:00:10Z","completedAt":"2026-01-01T00:00:50Z","classification":"useful-validation","rationale":"exact fixture"}},{{"kind":"ci-coverage","identity":"ci-coverage-1","itemId":"UTEL-04A","revision":0,"collectionId":"collection-1","inventory":"complete","attempts":"complete","jobPages":"complete","terminal":"complete","timestamps":"complete","lineage":"complete","classification":"complete","criticalPath":"unknown"}}]}}"""
        TelemetryStoreApplication.ingest path approved ci |> unwrap |> ignore
        let summary = TelemetryStoreApplication.ciSummary path approved "UTEL-04A" |> unwrap
        Assert.Contains("\"runnerSeconds\":60", summary)
        Assert.Contains("\"usefulValidationSeconds\":40", summary)
        Assert.Contains("\"criticalPathCoverage\":\"unknown\"", summary)
        Assert.DoesNotContain("ci-worker", summary)
        Assert.DoesNotContain("exact fixture", summary)

    [<Fact>]
    let ``UTEL-04A command shapes require explicit selected population arguments`` () =
        Assert.Equal(Some(Ok()), TelemetryApplication.validateInvocation [ "telemetry"; "ci"; "collect"; "--assignment"; "/private/a.json"; "--repo"; "o/r"; "--pr"; "1"; "--head"; String.replicate 40 "a"; "--workflow"; "ci.yml"; "--store-root"; "/durable/store" ])
        Assert.Equal(Some(Ok()), TelemetryApplication.validateInvocation [ "telemetry"; "ci"; "summary"; "--item"; "UTEL-04A" ])
        Assert.Equal(Some(Ok()), TelemetryApplication.validateInvocation [ "telemetry"; "ci"; "reconcile"; "--assignment"; "/private/a.json"; "--delivery"; "/tmp/public.json"; "--store-root"; "/durable/store" ])

    [<Fact>]
    let ``UTEL-06C first admission accepts only explicit eligible exact-head candidates`` () =
        let head = String.replicate 40 "a"
        Assert.True(TelemetryCiApplication.canCreateAdmission "ready" "not-delivered" (Some head) head)
        Assert.False(TelemetryCiApplication.canCreateAdmission "refused" "not-delivered" (Some head) head)
        Assert.False(TelemetryCiApplication.canCreateAdmission "ready" "delivered" (Some head) head)
        Assert.False(TelemetryCiApplication.canCreateAdmission "ready" "not-delivered" None head)
        Assert.False(TelemetryCiApplication.canCreateAdmission "ready" "not-delivered" (Some(String.replicate 40 "b")) head)

    [<Fact>]
    let ``UTEL-04A v2 upgrade preserves runtime observations`` () =
        let cleanup, path = root ()
        use cleanup = cleanup
        TelemetryStoreApplication.initialize path approved |> unwrap |> ignore
        use connection = new SqliteConnection($"Data Source=%s{Path.Combine(path, TelemetryStoreApplication.databaseFileName)};Pooling=False")
        connection.Open()
        use command = connection.CreateCommand()
        command.CommandText <- "INSERT INTO runtime_admissions VALUES('runtime-keep','UTEL-04A','invoke-1','UTEL','a1',NULL,'worker',NULL,NULL,NULL); " + dropNativeOutcomeSchema + " " + dropCiPopulationSchema + " " + dropOperationalSchema + " " + dropBudgetSchema + " DROP TABLE ci_coverage; DROP TABLE ci_steps; DROP TABLE ci_jobs; DROP TABLE ci_runs; DROP TABLE ci_pages; DROP TABLE ci_bindings; DELETE FROM schema_migrations WHERE version=3; PRAGMA user_version=2;"
        command.ExecuteNonQuery() |> ignore
        connection.Close()
        Assert.Contains("\"schemaVersion\":8", TelemetryStoreApplication.initialize path approved |> unwrap)
        let summary = TelemetryStoreApplication.summary path approved "UTEL-04A" |> unwrap
        Assert.Contains("\"admitted\":1", summary)

    [<Fact>]
    let ``UTEL-05A exact thresholds derive pass breach and one severe intervention`` () =
        let cleanup, path = root ()
        use cleanup = cleanup
        TelemetryStoreApplication.initialize path approved |> unwrap |> ignore
        let facts =
            [ for item,numerator in [ "budget-10",10L; "budget-11",11L; "budget-25",25L; "budget-26",26L ] do
                yield population item 0 "completed" ($"native:{item}")
                yield attribution item 0 (Some numerator) (Some 100L) "complete" "classified" "runtime" ($"usage:{item}") ]
        TelemetryStoreApplication.ingest path approved (budgetBatch "budget-boundaries" "1" facts) |> unwrap |> ignore
        Assert.Contains("\"verdict\":\"pass\"", TelemetryStoreApplication.budgetSummary path approved "budget-10" |> unwrap)
        Assert.Contains("\"verdict\":\"breach\"", TelemetryStoreApplication.budgetSummary path approved "budget-11" |> unwrap)
        Assert.Contains("\"severe\":false", TelemetryStoreApplication.budgetSummary path approved "budget-25" |> unwrap)
        Assert.Contains("\"severe\":true", TelemetryStoreApplication.budgetSummary path approved "budget-26" |> unwrap)
        let status = TelemetryStoreApplication.budgetStatus path approved |> unwrap
        Assert.Contains("\"distinctBreaches\":3", status)
        Assert.Contains("\"intervention\":\"open\"", status)

    [<Fact>]
    let ``UTEL-05A fifteenth distinct breach is sticky and verified deployment advances one epoch`` () =
        let cleanup, path = root ()
        use cleanup = cleanup
        TelemetryStoreApplication.initialize path approved |> unwrap |> ignore
        let facts =
            [ for number in 1 .. 14 do
                let item = $"budget-item-%02d{number}"
                yield population item 0 "completed" ($"native:{item}")
                yield attribution item 0 (Some 11L) (Some 100L) "complete" "classified" "runtime" ($"usage:{item}") ]
        TelemetryStoreApplication.ingest path approved (budgetBatch "budget-14" "1" facts) |> unwrap |> ignore
        let before = TelemetryStoreApplication.budgetStatus path approved |> unwrap
        Assert.Contains("\"distinctBreaches\":14", before)
        Assert.Contains("\"intervention\":\"none\"", before)
        let item15 = [ population "budget-item-15" 0 "completed" "native:budget-item-15"; attribution "budget-item-15" 0 (Some 11L) (Some 100L) "complete" "classified" "runtime" "usage:budget-item-15" ]
        TelemetryStoreApplication.ingest path approved (budgetBatch "budget-15" "2" item15) |> unwrap |> ignore
        Assert.Contains("\"intervention\":\"open\"", TelemetryStoreApplication.budgetStatus path approved |> unwrap)
        TelemetryStoreApplication.ingest path approved (budgetBatch "budget-15-replay" "3" item15) |> unwrap |> ignore
        Assert.Contains("\"distinctBreaches\":15", TelemetryStoreApplication.budgetStatus path approved |> unwrap)
        let good = [ population "budget-item-good" 0 "completed" "native:budget-item-good"; attribution "budget-item-good" 0 (Some 5L) (Some 100L) "complete" "classified" "runtime" "usage:budget-item-good" ]
        TelemetryStoreApplication.ingest path approved (budgetBatch "budget-good-after-open" "3b" good) |> unwrap |> ignore
        Assert.Contains("\"intervention\":\"open\"", TelemetryStoreApplication.budgetStatus path approved |> unwrap)
        let evidence =
            [ """{"kind":"budget-intervention","identity":"deploy-epoch-1","itemId":"budget-item-15","revision":0,"interventionId":"epoch-1-intervention","transition":"deployed","sequence":10,"result":"not-evaluated","coverage":"complete","sourceRef":"deploy:1"}"""
              """{"kind":"budget-intervention","identity":"verify-epoch-1","itemId":"budget-item-15","revision":0,"interventionId":"epoch-1-intervention","transition":"verified","sequence":11,"result":"improved","coverage":"complete","sourceRef":"verify:1"}""" ]
        TelemetryStoreApplication.ingest path approved (budgetBatch "budget-reset" "4" evidence) |> unwrap |> ignore
        let after = TelemetryStoreApplication.budgetStatus path approved |> unwrap
        Assert.Contains("\"epoch\":\"epoch-2\"", after)
        Assert.Contains("\"distinctBreaches\":0", after)
        Assert.Contains("\"intervention\":\"none\"", after)
        TelemetryStoreApplication.ingest path approved (budgetBatch "budget-old-replay" "5" item15) |> unwrap |> ignore
        Assert.Contains("\"epoch\":\"epoch-2\"", TelemetryStoreApplication.budgetStatus path approved |> unwrap)

    [<Fact>]
    let ``UTEL-05A incomplete mixed and gapped dimensions remain unknown independently`` () =
        let cleanup, path = root ()
        use cleanup = cleanup
        TelemetryStoreApplication.initialize path approved |> unwrap |> ignore
        let facts =
            [ population "budget-unknown" 0 "completed" "native:budget-unknown"
              attribution "budget-unknown" 0 (Some 99L) None "complete" "classified" "runtime" "usage:missing"
              attribution "budget-mixed" 0 (Some 99L) (Some 100L) "complete" "mixed" "runtime" "usage:mixed"
              population "budget-mixed" 0 "completed" "native:budget-mixed"
              """{"kind":"runtime-gap","identity":"gap-budget","itemId":"budget-gap","revision":0,"invocationId":"invoke-budget","code":"framing-loss"}"""
              population "budget-gap" 0 "completed" "native:budget-gap"
              attribution "budget-gap" 0 (Some 99L) (Some 100L) "complete" "classified" "runtime" "usage:gap"
              population "budget-open" 0 "open" "native:budget-open"
              attribution "budget-open" 0 (Some 100L) (Some 100L) "complete" "classified" "runtime" "usage:prefix" ]
        TelemetryStoreApplication.ingest path approved (budgetBatch "budget-unknowns" "1" facts) |> unwrap |> ignore
        Assert.Contains("\"verdict\":\"unknown\"", TelemetryStoreApplication.budgetSummary path approved "budget-unknown" |> unwrap)
        Assert.Contains("attribution-unknown", TelemetryStoreApplication.budgetSummary path approved "budget-mixed" |> unwrap)
        Assert.Contains("runtime-gap", TelemetryStoreApplication.budgetSummary path approved "budget-gap" |> unwrap)
        Assert.Contains("\"dimensions\":[]", TelemetryStoreApplication.budgetSummary path approved "budget-open" |> unwrap)
        Assert.Contains("\"distinctBreaches\":0", TelemetryStoreApplication.budgetStatus path approved |> unwrap)
        TelemetryStoreApplication.ingest path approved (budgetBatch "budget-open-completed" "2" [ population "budget-open" 1 "completed" "native:budget-open-completed" ]) |> unwrap |> ignore
        Assert.Contains("\"verdict\":\"breach\"", TelemetryStoreApplication.budgetSummary path approved "budget-open" |> unwrap)
        Assert.Contains("\"epoch\":\"epoch-1\"", TelemetryStoreApplication.budgetSummary path approved "budget-open" |> unwrap)

    [<Fact>]
    let ``UTEL-05A interval union excludes witnessed useful work without rounding`` () =
        let cleanup, path = root ()
        use cleanup = cleanup
        TelemetryStoreApplication.initialize path approved |> unwrap |> ignore
        let interval identity classification startAt endAt =
            $"""{{"kind":"budget-interval","identity":"{identity}","itemId":"budget-interval","revision":0,"dimension":"owner-time","classification":"{classification}","startNanoseconds":{startAt},"endNanoseconds":{endAt},"witnessed":true,"sourceKind":"ci","sourceRef":"interval:{identity}"}}"""
        let facts =
            [ population "budget-interval" 0 "completed" "native:budget-interval"
              """{"kind":"budget-attribution","identity":"attribution-budget-interval","itemId":"budget-interval","revision":0,"dimension":"owner-time","provider":"human","accountingScope":"whole-item","numerator":null,"denominator":400000000000,"coverage":"complete","attribution":"classified","sourceKind":"ci","sourceRef":"owner:denominator"}"""
              interval "wait-1" "administrative" 0L 120000000000L
              interval "wait-2" "administrative" 30000000000L 90000000000L
              interval "useful" "useful" 20000000000L 100000000000L ]
        TelemetryStoreApplication.ingest path approved (budgetBatch "budget-intervals" "1" facts) |> unwrap |> ignore
        let summary = TelemetryStoreApplication.budgetSummary path approved "budget-interval" |> unwrap
        Assert.Contains("\"numerator\":40000000000", summary)
        Assert.Contains("\"verdict\":\"pass\"", summary)

    [<Fact>]
    let ``UTEL-05A command surface is read-only`` () =
        Assert.Equal(Some(Ok()), TelemetryApplication.validateInvocation [ "telemetry"; "budget"; "summary"; "--item"; "UTEL-05A" ])
        Assert.Equal(Some(Ok()), TelemetryApplication.validateInvocation [ "telemetry"; "budget"; "status" ])
        Assert.True(TelemetryApplication.validateInvocation [ "telemetry"; "budget"; "reset" ] |> Option.exists Result.isError)

    [<Fact>]
    let ``UTEL-05A correction removes a mistaken breach and follow-up does not duplicate the item`` () =
        let cleanup, path = root ()
        use cleanup = cleanup
        TelemetryStoreApplication.initialize path approved |> unwrap |> ignore
        let item = "budget-correction"
        let ingest revision numerator source cursor =
            [ population item 0 "completed" "native:budget-correction"
              attribution item revision (Some numerator) (Some 100L) "complete" "classified" "runtime" source ]
            |> budgetBatch ($"budget-correction-{revision}") cursor
            |> TelemetryStoreApplication.ingest path approved
            |> unwrap |> ignore
        ingest 0 11L "usage:mistaken" "1"
        Assert.Contains("\"distinctBreaches\":1", TelemetryStoreApplication.budgetStatus path approved |> unwrap)
        ingest 1 10L "usage:corrected" "2"
        Assert.Contains("\"distinctBreaches\":0", TelemetryStoreApplication.budgetStatus path approved |> unwrap)
        Assert.Contains("\"verdict\":\"pass\"", TelemetryStoreApplication.budgetSummary path approved item |> unwrap)
        ingest 2 12L "usage:follow-up" "3"
        Assert.Contains("\"distinctBreaches\":1", TelemetryStoreApplication.budgetStatus path approved |> unwrap)

    [<Fact>]
    let ``UTEL-05A reset requires deployment followed by complete verified improvement`` () =
        let cleanup, path = root ()
        use cleanup = cleanup
        TelemetryStoreApplication.initialize path approved |> unwrap |> ignore
        let severe = [ population "budget-reset-order" 0 "completed" "native:budget-reset-order"; attribution "budget-reset-order" 0 (Some 26L) (Some 100L) "complete" "classified" "runtime" "usage:reset-order" ]
        TelemetryStoreApplication.ingest path approved (budgetBatch "budget-open-reset" "1" severe) |> unwrap |> ignore
        let verify revision sequence result coverage source =
            $"""{{"kind":"budget-intervention","identity":"verify-reset-order","itemId":"budget-reset-order","revision":{revision},"interventionId":"epoch-1-intervention","transition":"verified","sequence":{sequence},"result":"{result}","coverage":"{coverage}","sourceRef":"{source}"}}"""
        let deploy = """{"kind":"budget-intervention","identity":"deploy-reset-order","itemId":"budget-reset-order","revision":0,"interventionId":"epoch-1-intervention","transition":"deployed","sequence":2,"result":"not-evaluated","coverage":"complete","sourceRef":"deploy:reset-order"}"""
        TelemetryStoreApplication.ingest path approved (budgetBatch "verify-before-deploy" "2" [ verify 0 1L "improved" "complete" "verify:early" ]) |> unwrap |> ignore
        TelemetryStoreApplication.ingest path approved (budgetBatch "deployment-only" "3" [ deploy ]) |> unwrap |> ignore
        Assert.Contains("\"epoch\":\"epoch-1\"", TelemetryStoreApplication.budgetStatus path approved |> unwrap)
        TelemetryStoreApplication.ingest path approved (budgetBatch "failed-verification" "4" [ verify 1 3L "failed" "complete" "verify:failed" ]) |> unwrap |> ignore
        Assert.Contains("\"epoch\":\"epoch-1\"", TelemetryStoreApplication.budgetStatus path approved |> unwrap)
        TelemetryStoreApplication.ingest path approved (budgetBatch "unknown-verification" "5" [ verify 2 4L "improved" "unknown" "verify:unknown" ]) |> unwrap |> ignore
        Assert.Contains("\"epoch\":\"epoch-1\"", TelemetryStoreApplication.budgetStatus path approved |> unwrap)
        TelemetryStoreApplication.ingest path approved (budgetBatch "valid-verification" "6" [ verify 3 5L "improved" "complete" "verify:valid" ]) |> unwrap |> ignore
        Assert.Contains("\"epoch\":\"epoch-2\"", TelemetryStoreApplication.budgetStatus path approved |> unwrap)

    [<Fact>]
    let ``UTEL-05A assessment and ingestion share crash atomicity`` () =
        let cleanup, path = root ()
        use cleanup = cleanup
        TelemetryStoreApplication.initialize path approved |> unwrap |> ignore
        let payload = budgetBatch "budget-crash" "1" [ population "budget-crash" 0 "completed" "native:budget-crash"; attribution "budget-crash" 0 (Some 11L) (Some 100L) "complete" "classified" "runtime" "usage:budget-crash" ]
        TelemetryStoreApplication.publish path approved payload |> unwrap |> ignore
        let precommit : TelemetryStoreApplication.DrainHooks = { BeforeCommit = (fun () -> raise (OperationCanceledException "before assessment commit")); AfterCommitBeforeDelete = ignore }
        Assert.True(TelemetryStoreApplication.drainWithHooks path approved precommit |> Result.isError)
        Assert.Contains("\"distinctBreaches\":0", TelemetryStoreApplication.budgetStatus path approved |> unwrap)
        let postcommit : TelemetryStoreApplication.DrainHooks = { BeforeCommit = ignore; AfterCommitBeforeDelete = (fun () -> raise (IOException "after assessment commit")) }
        Assert.True(TelemetryStoreApplication.drainWithHooks path approved postcommit |> Result.isError)
        Assert.Contains("\"distinctBreaches\":1", TelemetryStoreApplication.budgetStatus path approved |> unwrap)
        Assert.Contains("\"replayed\":2", TelemetryStoreApplication.drain path approved |> unwrap)
        Assert.Contains("\"distinctBreaches\":1", TelemetryStoreApplication.budgetStatus path approved |> unwrap)

    [<Fact>]
    let ``UTEL-05A v3 upgrade preserves CI facts`` () =
        let cleanup, path = root ()
        use cleanup = cleanup
        TelemetryStoreApplication.initialize path approved |> unwrap |> ignore
        use connection = new SqliteConnection($"Data Source=%s{Path.Combine(path, TelemetryStoreApplication.databaseFileName)};Pooling=False")
        connection.Open()
        use command = connection.CreateCommand()
        command.CommandText <- "INSERT INTO ci_bindings VALUES('keep-binding','UTEL-05A','keep-collection','o/r','aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa',1,'ci.yml','UTEL','a1',NULL,'worker','exact'); " + dropNativeOutcomeSchema + " " + dropCiPopulationSchema + " " + dropOperationalSchema + " " + dropBudgetSchema + " PRAGMA user_version=3;"
        command.ExecuteNonQuery() |> ignore
        connection.Close()
        Assert.Contains("\"schemaVersion\":8", TelemetryStoreApplication.initialize path approved |> unwrap)
        use verify = new SqliteConnection($"Data Source=%s{Path.Combine(path, TelemetryStoreApplication.databaseFileName)};Pooling=False")
        verify.Open()
        use count = verify.CreateCommand()
        count.CommandText <- "SELECT count(*) FROM ci_bindings WHERE identity='keep-binding';"
        Assert.Equal(1L, Convert.ToInt64(count.ExecuteScalar()))

    [<Fact>]
    let ``UTEL-05A dimension usability is independent and shared references cannot overlap`` () =
        let cleanup, path = root ()
        use cleanup = cleanup
        TelemetryStoreApplication.initialize path approved |> unwrap |> ignore
        let facts =
            [ population "budget-dimensions" 0 "completed" "native:budget-dimensions"
              attribution "budget-dimensions" 0 (Some 11L) (Some 100L) "complete" "classified" "runtime" "usage:model"
              """{"kind":"budget-attribution","identity":"owner-budget-dimensions","itemId":"budget-dimensions","revision":0,"dimension":"owner-effort","provider":"human","accountingScope":"whole-item","numerator":null,"denominator":null,"coverage":"unknown","attribution":"unclassified","sourceKind":"native-item","sourceRef":"effort:owner"}""" ]
        TelemetryStoreApplication.ingest path approved (budgetBatch "budget-dimensions" "1" facts) |> unwrap |> ignore
        let summary = TelemetryStoreApplication.budgetSummary path approved "budget-dimensions" |> unwrap
        Assert.Contains("\"verdict\":\"breach\"", summary)
        Assert.Contains("\"verdict\":\"unknown\"", summary)
        Assert.Contains("\"distinctBreaches\":1", TelemetryStoreApplication.budgetStatus path approved |> unwrap)
        let overlap =
            [ population "budget-overlap" 0 "completed" "native:budget-overlap"
              attribution "budget-overlap" 0 (Some 6L) (Some 100L) "complete" "classified" "runtime" "shared:cost"
              """{"kind":"budget-attribution","identity":"legacy-budget-overlap","itemId":"budget-overlap","revision":0,"dimension":"model-tokens","provider":"openai","accountingScope":"legacy-whole-item","numerator":6,"denominator":100,"coverage":"complete","attribution":"classified","sourceKind":"legacy","sourceRef":"shared:cost"}""" ]
        TelemetryStoreApplication.publish path approved (budgetBatch "budget-overlap" "2" overlap) |> unwrap |> ignore
        Assert.Contains("\"quarantined\":1", TelemetryStoreApplication.drain path approved |> unwrap)
        Assert.Contains("\"dimensions\":[]", TelemetryStoreApplication.budgetSummary path approved "budget-overlap" |> unwrap)

    [<Fact>]
    let ``UTEL-05A reevaluation leaves work beyond thirty two items for the next drain`` () =
        let cleanup, path = root ()
        use cleanup = cleanup
        TelemetryStoreApplication.initialize path approved |> unwrap |> ignore
        let first =
            [ for number in 1 .. 32 do
                let item = $"budget-bound-%02d{number}"
                yield population item 0 "completed" ($"native:{item}")
                yield attribution item 0 (Some 10L) (Some 100L) "complete" "classified" "runtime" ($"usage:{item}") ]
        let item33 = [ population "budget-bound-33" 0 "completed" "native:budget-bound-33"; attribution "budget-bound-33" 0 (Some 10L) (Some 100L) "complete" "classified" "runtime" "usage:budget-bound-33" ]
        TelemetryStoreApplication.publish path approved (budgetBatch "budget-bound-first" "1" first) |> unwrap |> ignore
        TelemetryStoreApplication.publish path approved (budgetBatch "budget-bound-second" "2" item33) |> unwrap |> ignore
        TelemetryStoreApplication.drain path approved |> unwrap |> ignore
        Assert.Contains("\"dirtyItems\":1", TelemetryStoreApplication.budgetStatus path approved |> unwrap)
        Assert.Contains("\"dimensions\":[]", TelemetryStoreApplication.budgetSummary path approved "budget-bound-33" |> unwrap)
        TelemetryStoreApplication.drain path approved |> unwrap |> ignore
        Assert.Contains("\"dirtyItems\":0", TelemetryStoreApplication.budgetStatus path approved |> unwrap)
        Assert.Contains("\"verdict\":\"pass\"", TelemetryStoreApplication.budgetSummary path approved "budget-bound-33" |> unwrap)

    [<Fact>]
    let ``UTEL-05A partial CI lineage cannot become a budget verdict`` () =
        let cleanup, path = root ()
        use cleanup = cleanup
        TelemetryStoreApplication.initialize path approved |> unwrap |> ignore
        let facts =
            [ """{"kind":"ci-binding","identity":"budget-ci-binding","itemId":"budget-ci","revision":0,"collectionId":"budget-ci-collection","repository":"o/r","head":"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa","prNumber":1,"workflow":"ci.yml","featureId":"UTEL","attemptId":"a1","parentAttemptId":null,"producerStream":"budget-worker","binding":"exact"}"""
              """{"kind":"ci-coverage","identity":"budget-ci-coverage","itemId":"budget-ci","revision":0,"collectionId":"budget-ci-collection","inventory":"complete","attempts":"complete","jobPages":"partial","terminal":"complete","timestamps":"complete","lineage":"unknown","classification":"complete","criticalPath":"unknown"}"""
              population "budget-ci" 0 "completed" "native:budget-ci"
              attribution "budget-ci" 0 (Some 99L) (Some 100L) "complete" "classified" "ci" "ci:budget" ]
        TelemetryStoreApplication.ingest path approved (budgetBatch "budget-ci-partial" "1" facts) |> unwrap |> ignore
        let summary = TelemetryStoreApplication.budgetSummary path approved "budget-ci" |> unwrap
        Assert.Contains("\"verdict\":\"unknown\"", summary)
        Assert.Contains("ci-coverage", summary)

    [<Fact>]
    let ``UTEL-05A migration checksum is verified`` () =
        let cleanup, path = root ()
        use cleanup = cleanup
        TelemetryStoreApplication.initialize path approved |> unwrap |> ignore
        use connection = new SqliteConnection($"Data Source=%s{Path.Combine(path, TelemetryStoreApplication.databaseFileName)};Pooling=False")
        connection.Open()
        use corrupt = connection.CreateCommand()
        corrupt.CommandText <- "UPDATE schema_migrations SET digest='corrupt' WHERE version=4;"
        corrupt.ExecuteNonQuery() |> ignore
        connection.Close()
        Assert.Contains("migration checksum mismatch", sprintf "%A" (TelemetryStoreApplication.status path approved))
        Assert.Contains("migration checksum mismatch", sprintf "%A" (TelemetryStoreApplication.initialize path approved))

    [<Fact>]
    let ``UTEL-06A root child and follow-up dispatch identities reconcile`` () =
        let cleanup, path = root ()
        use cleanup = cleanup
        TelemetryStoreApplication.initialize path approved |> unwrap |> ignore
        let item = "operational-tree"
        let facts =
            [ activation item "codex-exec" 60L
              dispatch item "dispatch-root" "root" None "codex-exec" "01"
              lineage item "lineage-root" "dispatch-root" "invoke-root" "root" None "invoke-root" "codex-exec"
              yield! completeTimes item "root" "invoke-root" "01" "05"
              dispatch item "dispatch-child" "child" (Some "dispatch-root") "codex-exec" "02"
              lineage item "lineage-child" "dispatch-child" "invoke-child" "child" (Some "invoke-root") "invoke-root" "codex-exec"
              yield! completeTimes item "child" "invoke-child" "02" "05"
              dispatch item "dispatch-follow" "follow-up" (Some "dispatch-child") "codex-exec" "03"
              lineage item "lineage-follow" "dispatch-follow" "invoke-follow" "follow-up" (Some "invoke-child") "invoke-root" "codex-exec"
              yield! completeTimes item "follow" "invoke-follow" "03" "05" ]
        TelemetryStoreApplication.ingest path approved (operationalBatch "operational-tree" item facts) |> unwrap |> ignore
        let result = TelemetryStoreApplication.reconcile path approved item |> unwrap
        Assert.Contains("\"expected\":3", result)
        Assert.Contains("\"matched\":3", result)
        Assert.Contains("\"complete\":3", result)
        Assert.Contains("\"usageCoverage\":\"not-evaluated\"", result)
        Assert.Contains("\"terminalOutcomeCoverage\":\"not-evaluated\"", result)
        Assert.Contains("\"historicalSessionDiscovery\":false", result)

    [<Fact>]
    let ``UTEL-06A missing parent and conflicting identity remain explicit`` () =
        let cleanup, path = root ()
        use cleanup = cleanup
        TelemetryStoreApplication.initialize path approved |> unwrap |> ignore
        let missing = "operational-missing-parent"
        let missingFacts =
            [ activation missing "codex-exec" 60L
              dispatch missing "dispatch-child" "child" (Some "dispatch-absent") "codex-exec" "01"
              lineage missing "lineage-child" "dispatch-child" "invoke-child" "child" (Some "invoke-absent") "invoke-root" "codex-exec" ]
        TelemetryStoreApplication.ingest path approved (operationalBatch "operational-missing" missing missingFacts) |> unwrap |> ignore
        Assert.Contains("missing-parent", TelemetryStoreApplication.reconcile path approved missing |> unwrap)
        let conflict = "operational-conflict"
        let conflictFacts =
            [ activation conflict "codex-exec" 60L
              dispatch conflict "dispatch-root" "root" None "codex-exec" "01"
              lineage conflict "lineage-root-a" "dispatch-root" "invoke-a" "root" None "invoke-a" "codex-exec"
              lineage conflict "lineage-root-b" "dispatch-root" "invoke-b" "root" None "invoke-b" "codex-exec" ]
        TelemetryStoreApplication.ingest path approved (operationalBatch "operational-conflict" conflict conflictFacts) |> unwrap |> ignore
        Assert.Contains("conflicting-identity", TelemetryStoreApplication.reconcile path approved conflict |> unwrap)

    [<Fact>]
    let ``UTEL-06A cyclic lineage and unsupported runtimes cannot match`` () =
        let cleanup, path = root ()
        use cleanup = cleanup
        TelemetryStoreApplication.initialize path approved |> unwrap |> ignore
        let cycle = "operational-cycle"
        let cycleFacts =
            [ activation cycle "codex-exec" 60L
              dispatch cycle "dispatch-a" "child" (Some "dispatch-b") "codex-exec" "01"
              dispatch cycle "dispatch-b" "follow-up" (Some "dispatch-a") "codex-exec" "02"
              lineage cycle "lineage-a" "dispatch-a" "invoke-a" "child" (Some "invoke-b") "invoke-a" "codex-exec"
              lineage cycle "lineage-b" "dispatch-b" "invoke-b" "follow-up" (Some "invoke-a") "invoke-a" "codex-exec" ]
        TelemetryStoreApplication.ingest path approved (operationalBatch "operational-cycle" cycle cycleFacts) |> unwrap |> ignore
        Assert.Contains("cyclic-lineage", TelemetryStoreApplication.reconcile path approved cycle |> unwrap)
        let unsupported = "operational-unsupported"
        let unsupportedFacts =
            [ activation unsupported "collaboration.spawn_agent" 60L
              dispatch unsupported "dispatch-root" "root" None "collaboration.spawn_agent" "01"
              lineage unsupported "lineage-root" "dispatch-root" "invoke-root" "root" None "invoke-root" "collaboration.spawn_agent" ]
        TelemetryStoreApplication.ingest path approved (operationalBatch "operational-unsupported" unsupported unsupportedFacts) |> unwrap |> ignore
        let result = TelemetryStoreApplication.reconcile path approved unsupported |> unwrap
        Assert.Contains("runtime-unsupported", result)
        Assert.Contains("\"matched\":0", result)

    [<Fact>]
    let ``UTEL-06A missing timestamps and late observations stay non-qualifying`` () =
        let cleanup, path = root ()
        use cleanup = cleanup
        TelemetryStoreApplication.initialize path approved |> unwrap |> ignore
        let missing = "operational-missing-time"
        let missingFacts =
            [ activation missing "codex-exec" 5L
              dispatch missing "dispatch-root" "root" None "codex-exec" "01"
              lineage missing "lineage-root" "dispatch-root" "invoke-root" "root" None "invoke-root" "codex-exec"
              eventTime missing "time-missing-admission" "invoke-root" "admission" (Some "2026-09-08T10:01:00Z") (Some "provider-native") (Some "2026-09-08T10:01:01Z") (Some "provider-native")
              eventTime missing "time-missing-start" "invoke-root" "start" None None (Some "2026-09-08T10:01:01Z") (Some "provider-native")
              eventTime missing "time-missing-terminal" "invoke-root" "terminal" (Some "2026-09-08T10:01:00Z") (Some "provider-native") (Some "2026-09-08T10:01:01Z") (Some "provider-native") ]
        TelemetryStoreApplication.ingest path approved (operationalBatch "operational-missing-time" missing missingFacts) |> unwrap |> ignore
        Assert.Contains("timestamps-missing", TelemetryStoreApplication.reconcile path approved missing |> unwrap)
        let late = "operational-late"
        let lateFacts =
            [ activation late "codex-exec" 5L
              dispatch late "dispatch-root" "root" None "codex-exec" "01"
              lineage late "lineage-late-root" "dispatch-root" "invoke-late-root" "root" None "invoke-late-root" "codex-exec"
              yield! completeTimes late "late-root" "invoke-late-root" "01" "06" ]
        TelemetryStoreApplication.ingest path approved (operationalBatch "operational-late" late lateFacts) |> unwrap |> ignore
        let result = TelemetryStoreApplication.reconcile path approved late |> unwrap
        Assert.Contains("observation-late", result)
        Assert.Contains("\"late\":1", result)

    [<Fact>]
    let ``UTEL-06A arbitrary and duplicate event witnesses are invalid`` () =
        let arbitrary = "operational-arbitrary-event"
        let arbitraryEvent = eventTime arbitrary "time-foo" "invoke-root" "foo" (Some "2026-09-08T10:01:00Z") (Some "provider-native") (Some "2026-09-08T10:01:01Z") (Some "provider-native")
        match TelemetryStore.parseBatch (operationalBatch "operational-arbitrary" arbitrary [ arbitraryEvent ]) with
        | Error errors -> Assert.Contains("admission, start, or terminal", String.concat ";" errors)
        | Ok _ -> failwith "arbitrary event witness was accepted"
        let cleanup, path = root ()
        use cleanup = cleanup
        TelemetryStoreApplication.initialize path approved |> unwrap |> ignore
        let duplicate = "operational-duplicate-event"
        let facts =
            [ activation duplicate "codex-exec" 60L
              dispatch duplicate "dispatch-root" "root" None "codex-exec" "01"
              lineage duplicate "lineage-root" "dispatch-root" "invoke-root" "root" None "invoke-root" "codex-exec"
              yield! completeTimes duplicate "root" "invoke-root" "01" "01" ]
        TelemetryStoreApplication.ingest path approved (operationalBatch "operational-duplicate-base" duplicate facts) |> unwrap |> ignore
        let conflicting = eventTime duplicate "time-conflicting-terminal" "invoke-root" "terminal" (Some "2026-09-08T10:01:00Z") (Some "provider-native") (Some "2026-09-08T10:01:02Z") (Some "provider-native")
        TelemetryStoreApplication.publish path approved (operationalBatch "operational-duplicate-conflict" duplicate [ conflicting ]) |> unwrap |> ignore
        Assert.Contains("\"quarantined\":1", TelemetryStoreApplication.drain path approved |> unwrap)
        Assert.Contains("\"complete\":1", TelemetryStoreApplication.reconcile path approved duplicate |> unwrap)

    [<Fact>]
    let ``UTEL-06A incomparable clock domains never produce latency`` () =
        let cleanup, path = root ()
        use cleanup = cleanup
        TelemetryStoreApplication.initialize path approved |> unwrap |> ignore
        let item = "operational-clock-mismatch"
        let facts =
            [ activation item "codex-exec" 60L
              dispatch item "dispatch-root" "root" None "codex-exec" "01"
              lineage item "lineage-root" "dispatch-root" "invoke-root" "root" None "invoke-root" "codex-exec"
              eventTime item "time-admission" "invoke-root" "admission" (Some "2026-09-08T10:01:00Z") (Some "provider-native") (Some "2026-09-08T10:01:01Z") (Some "host-wall")
              eventTime item "time-start" "invoke-root" "start" (Some "2026-09-08T10:01:00Z") (Some "provider-native") (Some "2026-09-08T10:01:01Z") (Some "provider-native")
              eventTime item "time-terminal" "invoke-root" "terminal" (Some "2026-09-08T10:01:00Z") (Some "provider-native") (Some "2026-09-08T10:01:01Z") (Some "provider-native") ]
        TelemetryStoreApplication.ingest path approved (operationalBatch "operational-clock-mismatch" item facts) |> unwrap |> ignore
        let result = TelemetryStoreApplication.reconcile path approved item |> unwrap
        Assert.Contains("clock-domain-mismatch", result)
        Assert.Contains("\"timingCoverage\":{\"complete\":0,\"invalid\":1", result)

    [<Fact>]
    let ``UTEL-06A lifecycle ordering and common clock are required for complete timing`` () =
        let cleanup, path = root ()
        use cleanup = cleanup
        TelemetryStoreApplication.initialize path approved |> unwrap |> ignore
        let observe item events =
            let facts =
                [ activation item "codex-exec" 60L
                  dispatch item "dispatch-root" "root" None "codex-exec" "01"
                  lineage item ($"lineage-{item}") "dispatch-root" ($"invoke-{item}") "root" None ($"invoke-{item}") "codex-exec"
                  yield! events ]
            TelemetryStoreApplication.ingest path approved (operationalBatch ($"batch-{item}") item facts) |> unwrap |> ignore
            TelemetryStoreApplication.reconcile path approved item |> unwrap
        let startBefore = "operational-start-before-admission"
        let startBeforeInvocation = $"invoke-{startBefore}"
        let startBeforeEvents =
            [ eventTime startBefore "order-a-admission" startBeforeInvocation "admission" (Some "2026-09-08T10:01:01Z") (Some "provider-native") (Some "2026-09-08T10:01:01Z") (Some "provider-native")
              eventTime startBefore "order-a-start" startBeforeInvocation "start" (Some "2026-09-08T10:01:00Z") (Some "provider-native") (Some "2026-09-08T10:01:02Z") (Some "provider-native")
              eventTime startBefore "order-a-terminal" startBeforeInvocation "terminal" (Some "2026-09-08T10:01:02Z") (Some "provider-native") (Some "2026-09-08T10:01:03Z") (Some "provider-native") ]
        Assert.Contains("lifecycle-occurrence-order-invalid", observe startBefore startBeforeEvents)
        let terminalBefore = "operational-terminal-before-start"
        let terminalBeforeInvocation = $"invoke-{terminalBefore}"
        let terminalBeforeEvents =
            [ eventTime terminalBefore "order-b-admission" terminalBeforeInvocation "admission" (Some "2026-09-08T10:01:00Z") (Some "provider-native") (Some "2026-09-08T10:01:01Z") (Some "provider-native")
              eventTime terminalBefore "order-b-start" terminalBeforeInvocation "start" (Some "2026-09-08T10:01:01Z") (Some "provider-native") (Some "2026-09-08T10:01:03Z") (Some "provider-native")
              eventTime terminalBefore "order-b-terminal" terminalBeforeInvocation "terminal" (Some "2026-09-08T10:01:02Z") (Some "provider-native") (Some "2026-09-08T10:01:02Z") (Some "provider-native") ]
        Assert.Contains("lifecycle-observation-order-invalid", observe terminalBefore terminalBeforeEvents)
        let mixedClock = "operational-cross-event-clock"
        let mixedClockInvocation = $"invoke-{mixedClock}"
        let mixedClockEvents =
            [ eventTime mixedClock "clock-c-admission" mixedClockInvocation "admission" (Some "2026-09-08T10:01:00Z") (Some "provider-native") (Some "2026-09-08T10:01:01Z") (Some "provider-native")
              eventTime mixedClock "clock-c-start" mixedClockInvocation "start" (Some "2026-09-08T10:01:01Z") (Some "host-wall") (Some "2026-09-08T10:01:02Z") (Some "host-wall")
              eventTime mixedClock "clock-c-terminal" mixedClockInvocation "terminal" (Some "2026-09-08T10:01:02Z") (Some "provider-native") (Some "2026-09-08T10:01:03Z") (Some "provider-native") ]
        Assert.Contains("lifecycle-clock-domain-mismatch", observe mixedClock mixedClockEvents)

    [<Fact>]
    let ``UTEL-06A v4 stores upgrade without changing historical facts`` () =
        let cleanup, path = root ()
        use cleanup = cleanup
        TelemetryStoreApplication.initialize path approved |> unwrap |> ignore
        use connection = new SqliteConnection($"Data Source=%s{Path.Combine(path, TelemetryStoreApplication.databaseFileName)};Pooling=False")
        connection.Open()
        use command = connection.CreateCommand()
        command.CommandText <- "INSERT INTO budget_population_facts VALUES('keep-population','UTEL-06A','UTEL-06A','completed','native-item','native:keep',0); " + dropNativeOutcomeSchema + " " + dropCiPopulationSchema + " " + dropOperationalSchema + " PRAGMA user_version=4;"
        command.ExecuteNonQuery() |> ignore
        connection.Close()
        Assert.Contains("\"schemaVersion\":8", TelemetryStoreApplication.initialize path approved |> unwrap)
        use verify = new SqliteConnection($"Data Source=%s{Path.Combine(path, TelemetryStoreApplication.databaseFileName)};Pooling=False")
        verify.Open()
        use count = verify.CreateCommand()
        count.CommandText <- "SELECT count(*) FROM budget_population_facts WHERE identity='keep-population';"
        Assert.Equal(1L, Convert.ToInt64(count.ExecuteScalar()))

    [<Fact>]
    let ``UTEL-06A activation contract excludes historical session discovery`` () =
        let item = "operational-history"
        let historical = (activation item "codex-exec" 60L).Replace("explicit-future-dispatches", "historical-sessions")
        match TelemetryStore.parseBatch (operationalBatch "operational-history" item [ historical ]) with
        | Error errors -> Assert.Contains("explicit-future-dispatches", String.concat ";" errors)
        | Ok _ -> failwith "historical discovery scope was accepted"
        Assert.Equal(Some(Ok()), TelemetryApplication.validateInvocation [ "telemetry"; "store"; "reconcile"; "--item"; item ])

    [<Fact>]
    let ``UTEL-06A migration checksum is verified`` () =
        let cleanup, path = root ()
        use cleanup = cleanup
        TelemetryStoreApplication.initialize path approved |> unwrap |> ignore
        use connection = new SqliteConnection($"Data Source=%s{Path.Combine(path, TelemetryStoreApplication.databaseFileName)};Pooling=False")
        connection.Open()
        use corrupt = connection.CreateCommand()
        corrupt.CommandText <- "UPDATE schema_migrations SET digest='corrupt' WHERE version=5;"
        corrupt.ExecuteNonQuery() |> ignore
        connection.Close()
        Assert.Contains("migration checksum mismatch", sprintf "%A" (TelemetryStoreApplication.status path approved))
        Assert.Contains("migration checksum mismatch", sprintf "%A" (TelemetryStoreApplication.initialize path approved))

    [<Fact>]
    let ``UTEL-06C v5 stores upgrade and preserve prospective operational facts`` () =
        let cleanup, path = root ()
        use cleanup = cleanup
        TelemetryStoreApplication.initialize path approved |> unwrap |> ignore
        use connection = new SqliteConnection($"Data Source=%s{Path.Combine(path, TelemetryStoreApplication.databaseFileName)};Pooling=False")
        connection.Open()
        use command = connection.CreateCommand()
        command.CommandText <- "INSERT INTO operational_activations VALUES('keep-activation','UTEL-06C','activation','explicit-future-dispatches','codex-exec','2026-09-08T10:00:00Z','host-wall',60,0); " + dropNativeOutcomeSchema + " " + dropCiPopulationSchema + " PRAGMA user_version=5;"
        command.ExecuteNonQuery() |> ignore
        connection.Close()
        Assert.Contains("\"schemaVersion\":8", TelemetryStoreApplication.initialize path approved |> unwrap)
        Assert.False(TelemetryStoreApplication.ciPopulationAdmissionExists path approved "UTEL-06C" "o/r" 7 "main" "dddddddddddddddddddddddddddddddddddddddd" "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa" |> unwrap)
        use verify = new SqliteConnection($"Data Source=%s{Path.Combine(path, TelemetryStoreApplication.databaseFileName)};Pooling=False")
        verify.Open()
        use count = verify.CreateCommand()
        count.CommandText <- "SELECT count(*) FROM operational_activations WHERE identity='keep-activation';"
        Assert.Equal(1L, Convert.ToInt64(count.ExecuteScalar()))

    [<Fact>]
    let ``UTEL-06C admission correction and replay remain revision safe`` () =
        let cleanup, path = root ()
        use cleanup = cleanup
        TelemetryStoreApplication.initialize path approved |> unwrap |> ignore
        let first = TelemetryStoreApplication.ingest path approved (ciPopulationBatch "ci-first" "UTEL-06C" 1 "in_progress" "partial" "pending") |> unwrap
        Assert.Contains("\"accepted\":3", first)
        Assert.True(TelemetryStoreApplication.ciPopulationAdmissionExists path approved "UTEL-06C" "o/r" 7 "main" "dddddddddddddddddddddddddddddddddddddddd" "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa" |> unwrap)
        Assert.False(TelemetryStoreApplication.ciPopulationAdmissionExists path approved "UTEL-06C" "o/r" 7 "release" "dddddddddddddddddddddddddddddddddddddddd" "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa" |> unwrap)
        Assert.False(TelemetryStoreApplication.ciPopulationAdmissionExists path approved "UTEL-06C" "o/r" 7 "main" "eeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeee" "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa" |> unwrap)
        let corrected = TelemetryStoreApplication.ingest path approved (ciPopulationBatch "ci-corrected" "UTEL-06C" 2 "completed" "complete" "none") |> unwrap
        Assert.Contains("\"accepted\":2", corrected)
        let replayed = TelemetryStoreApplication.ingest path approved (ciPopulationBatch "ci-replay" "UTEL-06C" 2 "completed" "complete" "none") |> unwrap
        Assert.Contains("\"replayed\":3", replayed)
        let summary = TelemetryStoreApplication.ciSummary path approved "UTEL-06C" |> unwrap
        Assert.Contains("\"inventoryCoverage\":\"complete\"", summary)
        Assert.Contains("\"continuation\":\"none\"", summary)
        use connection = new SqliteConnection($"Data Source=%s{Path.Combine(path, TelemetryStoreApplication.databaseFileName)};Pooling=False")
        connection.Open()
        use count = connection.CreateCommand()
        count.CommandText <- "SELECT count(*) FROM corrections WHERE identity IN ('ci-check-a','ci-coverage-a');"
        Assert.Equal(2L, Convert.ToInt64(count.ExecuteScalar()))

    [<Fact>]
    let ``UTEL-06C migration checksum is verified`` () =
        let cleanup, path = root ()
        use cleanup = cleanup
        TelemetryStoreApplication.initialize path approved |> unwrap |> ignore
        use connection = new SqliteConnection($"Data Source=%s{Path.Combine(path, TelemetryStoreApplication.databaseFileName)};Pooling=False")
        connection.Open()
        use corrupt = connection.CreateCommand()
        corrupt.CommandText <- "UPDATE schema_migrations SET digest='corrupt' WHERE version=6;"
        corrupt.ExecuteNonQuery() |> ignore
        connection.Close()
        Assert.Contains("migration checksum mismatch", sprintf "%A" (TelemetryStoreApplication.status path approved))

    [<Fact>]
    let ``UTEL-06D machine facts derive whole-item inputs without authored budget events`` () =
        let cleanup, path = root ()
        use cleanup = cleanup
        TelemetryStoreApplication.initialize path approved |> unwrap |> ignore
        let item = "UTEL-06D-derived"
        let raw =
            [ activation item "codex-exec" 60L
              dispatch item "dispatch-root" "root" None "codex-exec" "00"
              lineage item "lineage-derived-root" "dispatch-root" "invoke-root" "root" None "invoke-root" "codex-exec"
              runtimeTerminal item "terminal-derived-root" "invoke-root" "completed" 0
              runtimeUsage item "usage-derived-root" "invoke-root" 100L
              ciStep item
              nativeOutcome item 1L "delivered" "delivered" "2026-09-08T10:04:01Z" ]
        Assert.DoesNotContain("budget-", String.concat "" raw)
        TelemetryStoreApplication.ingest path approved (operationalBatch "derived-whole-item" item raw) |> unwrap |> ignore
        let health = TelemetryStoreApplication.budgetHealth path approved item |> unwrap
        Assert.Contains("\"status\":\"complete\"", health)
        let summary = TelemetryStoreApplication.budgetSummary path approved item |> unwrap
        Assert.Contains("\"dimension\":\"model-usage\"", summary)
        Assert.Contains("\"denominator\":100", summary)
        Assert.Contains("attribution-unknown", summary)
        Assert.Contains("\"dimension\":\"owner-effort\"", summary)
        Assert.Contains("\"dimension\":\"priced-cost\"", summary)
        Assert.Contains("\"dimension\":\"critical-path-delay\"", summary)
        Assert.Contains("\"dimension\":\"ci-runner-administration\"", summary)
        Assert.Contains("source-not-applicable", summary)
        Assert.Contains("\"distinctBreaches\":0", TelemetryStoreApplication.budgetStatus path approved |> unwrap)
        use connection = new SqliteConnection($"Data Source=%s{Path.Combine(path, TelemetryStoreApplication.databaseFileName)};Pooling=False")
        connection.Open()
        use intervals = connection.CreateCommand()
        intervals.CommandText <- "SELECT count(*) FROM budget_interval_facts WHERE item_id=$item AND source_ref LIKE 'derived:%';"
        intervals.Parameters.AddWithValue("$item", item) |> ignore
        Assert.Equal(1L, Convert.ToInt64(intervals.ExecuteScalar()))

    [<Fact>]
    let ``UTEL-06D merge cannot close live child and late follow-up revises stable population`` () =
        let cleanup, path = root ()
        use cleanup = cleanup
        TelemetryStoreApplication.initialize path approved |> unwrap |> ignore
        let item = "UTEL-06D-late"
        let initial =
            [ activation item "codex-exec" 60L
              dispatch item "dispatch-root" "root" None "codex-exec" "00"
              lineage item "lineage-late-root" "dispatch-root" "invoke-root" "root" None "invoke-root" "codex-exec"
              runtimeTerminal item "terminal-late-root" "invoke-root" "completed" 0
              runtimeUsage item "usage-late-root" "invoke-root" 10L
              dispatch item "dispatch-child" "child" (Some "dispatch-root") "codex-exec" "01"
              lineage item "lineage-late-child" "dispatch-child" "invoke-child" "child" (Some "invoke-root") "invoke-root" "codex-exec"
              nativeOutcome item 1L "delivered" "delivered" "2026-09-08T10:04:01Z" ]
        TelemetryStoreApplication.ingest path approved (operationalBatch "late-initial" item initial) |> unwrap |> ignore
        Assert.Contains("\"population\":\"open\"", TelemetryStoreApplication.budgetHealth path approved item |> unwrap)
        TelemetryStoreApplication.ingest path approved (operationalBatch "late-child-terminal" item [ runtimeTerminal item "terminal-late-child" "invoke-child" "completed" 0; runtimeUsage item "usage-late-child" "invoke-child" 10L ]) |> unwrap |> ignore
        Assert.Contains("\"population\":\"completed\"", TelemetryStoreApplication.budgetHealth path approved item |> unwrap)
        let before = TelemetryStoreApplication.budgetSummary path approved item |> unwrap
        let followup =
            [ dispatch item "dispatch-follow" "follow-up" (Some "dispatch-root") "codex-exec" "05"
              lineage item "lineage-late-follow" "dispatch-follow" "invoke-follow" "follow-up" (Some "invoke-root") "invoke-root" "codex-exec" ]
        TelemetryStoreApplication.ingest path approved (operationalBatch "late-followup" item followup) |> unwrap |> ignore
        Assert.Contains("\"population\":\"open\"", TelemetryStoreApplication.budgetHealth path approved item |> unwrap)
        Assert.Contains("whole-item-incomplete", TelemetryStoreApplication.budgetSummary path approved item |> unwrap)
        TelemetryStoreApplication.ingest path approved (operationalBatch "late-followup-terminal" item [ runtimeTerminal item "terminal-late-follow" "invoke-follow" "completed" 0; runtimeUsage item "usage-late-follow" "invoke-follow" 10L ]) |> unwrap |> ignore
        Assert.Contains("\"population\":\"completed\"", TelemetryStoreApplication.budgetHealth path approved item |> unwrap)
        let after = TelemetryStoreApplication.budgetSummary path approved item |> unwrap
        Assert.False((before = after))
        use connection = new SqliteConnection($"Data Source=%s{Path.Combine(path, TelemetryStoreApplication.databaseFileName)};Pooling=False")
        connection.Open()
        use count = connection.CreateCommand()
        count.CommandText <- "SELECT count(*) FROM budget_population_facts WHERE item_id=$item AND source_ref LIKE 'derived:%';"
        count.Parameters.AddWithValue("$item", item) |> ignore
        Assert.Equal(1L, Convert.ToInt64(count.ExecuteScalar()))

    [<Fact>]
    let ``UTEL-06D refused delivered-without-runtime and observed no-op use distinct closure rules`` () =
        let cleanup, path = root ()
        use cleanup = cleanup
        TelemetryStoreApplication.initialize path approved |> unwrap |> ignore
        let refused = "UTEL-06D-refused"
        TelemetryStoreApplication.ingest path approved (operationalBatch "refused-native" refused [ nativeOutcome refused 1L "refused" "not-delivered" "2026-09-08T10:04:01Z" ]) |> unwrap |> ignore
        Assert.Contains("\"population\":\"completed\"", TelemetryStoreApplication.budgetHealth path approved refused |> unwrap)
        let missingRuntime = "UTEL-06D-missing-runtime"
        TelemetryStoreApplication.ingest path approved (operationalBatch "delivered-no-runtime" missingRuntime [ nativeOutcome missingRuntime 1L "delivered" "delivered" "2026-09-08T10:04:01Z" ]) |> unwrap |> ignore
        Assert.Contains("\"population\":\"open\"", TelemetryStoreApplication.budgetHealth path approved missingRuntime |> unwrap)
        let missingRoot = "UTEL-06D-missing-root"
        let childOnly =
            [ activation missingRoot "codex-exec" 60L
              dispatch missingRoot "dispatch-child" "child" (Some "dispatch-parent") "codex-exec" "00"
              lineage missingRoot "lineage-child-only" "dispatch-child" "invoke-child" "child" (Some "invoke-parent") "invoke-parent" "codex-exec"
              runtimeTerminal missingRoot "terminal-child-only" "invoke-child" "completed" 0
              nativeOutcome missingRoot 1L "delivered" "delivered" "2026-09-08T10:04:01Z" ]
        TelemetryStoreApplication.ingest path approved (operationalBatch "delivered-no-root" missingRoot childOnly) |> unwrap |> ignore
        Assert.Contains("\"population\":\"open\"", TelemetryStoreApplication.budgetHealth path approved missingRoot |> unwrap)
        let noOp = "UTEL-06D-no-op"
        let noOpFacts =
            [ activation noOp "codex-exec" 60L
              dispatch noOp "dispatch-root" "root" None "codex-exec" "00"
              lineage noOp "lineage-no-op" "dispatch-root" "invoke-no-op" "root" None "invoke-no-op" "codex-exec"
              runtimeTerminal noOp "terminal-no-op" "invoke-no-op" "completed" 0
              nativeOutcome noOp 1L "delivered" "delivered" "2026-09-08T10:04:01Z" ]
        TelemetryStoreApplication.ingest path approved (operationalBatch "delivered-no-op" noOp noOpFacts) |> unwrap |> ignore
        Assert.Contains("\"population\":\"completed\"", TelemetryStoreApplication.budgetHealth path approved noOp |> unwrap)
        Assert.Contains("source-coverage", TelemetryStoreApplication.budgetSummary path approved noOp |> unwrap)

    [<Fact>]
    let ``UTEL-06D final changed-head refusal is persisted before CI admission refusal`` () =
        let cleanup, path = root ()
        use cleanup = cleanup
        TelemetryStoreApplication.initialize path approved |> unwrap |> ignore
        let assignmentPath = Path.Combine(path, "assignment.json")
        let deliveryPath = Path.Combine(path, "delivery.json")
        File.WriteAllText(assignmentPath, """{"schema":"fsgg.telemetry.ci-assignment/1","featureId":"UTEL-06","itemId":"UTEL-06D-refused-driver","attemptId":"attempt-1","parentAttemptId":null,"producerStream":"routine-delivery"}""")
        if not (OperatingSystem.IsWindows()) then File.SetUnixFileMode(assignmentPath, UnixFileMode.UserRead ||| UnixFileMode.UserWrite)
        File.WriteAllText(deliveryPath, """{"schema":"fsgg.routine-delivery/v1","repo":"o/r","pr":7,"baseRef":"main","baseSha":"dddddddddddddddddddddddddddddddddddddddd","expectedHead":"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa","observedHead":"cccccccccccccccccccccccccccccccccccccccc","outcome":"refused","codeDelivery":"not-delivered","mergeCommit":null,"outcomeAt":null,"observedAt":"2026-09-08T10:04:01Z"}""")
        Assert.Equal(1, TelemetryCiApplication.runWithAssessment approved "reconcile" [ "--assignment"; assignmentPath; "--delivery"; deliveryPath; "--store-root"; path ])
        Assert.Contains("\"population\":\"completed\"", TelemetryStoreApplication.budgetHealth path approved "UTEL-06D-refused-driver" |> unwrap)

    [<Fact>]
    let ``UTEL-06D post-result bounded drain observes a just-queued admission`` () =
        let cleanup, path = root ()
        use cleanup = cleanup
        TelemetryStoreApplication.initialize path approved |> unwrap |> ignore
        let item = "UTEL-06D-drain-race"
        TelemetryStoreApplication.publish path approved (ciPopulationBatch "queued-admission" item 1L "queued" "partial" "pending") |> unwrap |> ignore
        Assert.False(TelemetryStoreApplication.ciPopulationAdmissionExists path approved item "o/r" 7 "main" (String.replicate 40 "d") (String.replicate 40 "a") |> unwrap)
        TelemetryStoreApplication.publish path approved (operationalBatch "final-native-readback" item [ nativeOutcome item 1L "delivered" "delivered" "2026-09-08T10:04:01Z" ]) |> unwrap |> ignore
        let drained = TelemetryStoreApplication.drain path approved |> unwrap
        Assert.Contains("\"remaining\":0", drained)
        Assert.True(TelemetryStoreApplication.ciPopulationAdmissionExists path approved item "o/r" 7 "main" (String.replicate 40 "d") (String.replicate 40 "a") |> unwrap)

    [<Theory>]
    [<InlineData("failed", 1)>]
    [<InlineData("cancelled", 130)>]
    [<InlineData("launch-failed", 127)>]
    let ``UTEL-06D definite runtime terminal outcomes remain in delivered population`` outcome exitCode =
        let cleanup, path = root ()
        use cleanup = cleanup
        TelemetryStoreApplication.initialize path approved |> unwrap |> ignore
        let item = "UTEL-06D-" + outcome
        let facts =
            [ activation item "codex-exec" 60L
              dispatch item "dispatch-root" "root" None "codex-exec" "00"
              lineage item ("lineage-" + outcome) "dispatch-root" ("invoke-" + outcome) "root" None ("invoke-" + outcome) "codex-exec"
              runtimeTerminal item ("terminal-" + outcome) ("invoke-" + outcome) outcome exitCode
              nativeOutcome item 1L "delivered" "delivered" "2026-09-08T10:04:01Z" ]
        TelemetryStoreApplication.ingest path approved (operationalBatch ("terminal-" + outcome) item facts) |> unwrap |> ignore
        Assert.Contains("\"population\":\"completed\"", TelemetryStoreApplication.budgetHealth path approved item |> unwrap)
        Assert.Contains("\"verdict\":\"unknown\"", TelemetryStoreApplication.budgetSummary path approved item |> unwrap)

    [<Fact>]
    let ``UTEL-06D v6 store upgrades and migration checksum is enforced`` () =
        let cleanup, path = root ()
        use cleanup = cleanup
        TelemetryStoreApplication.initialize path approved |> unwrap |> ignore
        use connection = new SqliteConnection($"Data Source=%s{Path.Combine(path, TelemetryStoreApplication.databaseFileName)};Pooling=False")
        connection.Open()
        use downgrade = connection.CreateCommand()
        downgrade.CommandText <- dropNativeOutcomeSchema + " PRAGMA user_version=6;"
        downgrade.ExecuteNonQuery() |> ignore
        connection.Close()
        Assert.Contains("\"schemaVersion\":8", TelemetryStoreApplication.initialize path approved |> unwrap)
        use verify = new SqliteConnection($"Data Source=%s{Path.Combine(path, TelemetryStoreApplication.databaseFileName)};Pooling=False")
        verify.Open()
        use corrupt = verify.CreateCommand()
        corrupt.CommandText <- "UPDATE schema_migrations SET digest='corrupt' WHERE version=7;"
        corrupt.ExecuteNonQuery() |> ignore
        verify.Close()
        Assert.Contains("migration checksum mismatch", sprintf "%A" (TelemetryStoreApplication.status path approved))

    [<Fact>]
    let ``UTEL-08 terminal reviews activities exact attribution and complications produce private item detail`` () =
        let cleanup, path = root ()
        use cleanup = cleanup
        Assert.Contains("\"schemaVersion\":8", TelemetryStoreApplication.initialize path approved |> unwrap)
        let item = "UTEL-08-detail"
        let digest = String.replicate 64 "a"
        let admission = $"""{{"kind":"runtime-admission","identity":"admission-review","itemId":"{item}","revision":0,"invocationId":"invoke-review","featureId":"UTEL","attemptId":"attempt-review","parentAttemptId":null,"producerStream":"review-worker","requestedModel":"gpt-5","requestedEffort":"medium","backend":"codex-collaboration"}}"""
        let usage = $"""{{"kind":"runtime-turn-usage","identity":"usage-review","itemId":"{item}","revision":0,"invocationId":"invoke-review","threadId":"thread-review","turnId":"turn-review","turnSequence":1,"provider":"openai","requestedModel":"gpt-5","observedModel":"gpt-5","requestedEffort":"medium","observedEffort":"medium","backend":"codex-collaboration","scope":"completed-turn","provenance":"codex-exec-jsonl","input":10,"cachedInput":2,"output":5,"reasoning":1,"total":15}}"""
        let baseFacts = [ activation item "collaboration-spawn-agent" 60L; dispatch item "dispatch-review" "root" None "collaboration-spawn-agent" "00"; lineage item "lineage-review" "dispatch-review" "invoke-review" "root" None "invoke-review" "collaboration-spawn-agent"; admission; runtimeTerminal item "terminal-review" "invoke-review" "completed" 0; usage ]
        TelemetryStoreApplication.ingest path approved (operationalBatch "review-base" item baseFacts) |> unwrap |> ignore
        let span = $"""{{"kind":"activity-span","identity":"span-review","itemId":"{item}","revision":0,"activityId":"implementation-review","invocationId":"invoke-review","attemptId":"attempt-review","category":"implementation","startedAt":"2026-09-08T10:00:00Z","endedAt":"2026-09-08T10:03:00Z","clockProvenance":"host-wall","evidence":[{{"kind":"test","digest":"{digest}"}}],"summary":"focused implementation"}}"""
        let attribution = $"""{{"kind":"activity-usage-attribution","identity":"attribute-review","itemId":"{item}","revision":0,"usageIdentity":"usage-review","activityId":"implementation-review","classification":"direct","input":10,"cachedInput":2,"output":5,"reasoning":1,"total":15}}"""
        let complication = $"""{{"kind":"complication","identity":"complication-review","itemId":"{item}","revision":0,"attemptId":"attempt-review","activityId":"implementation-review","trigger":"test-failure","cause":"product-defect","occurredAt":"2026-09-08T10:02:00Z","synopsis":"A focused test exposed the repair","evidence":[{{"kind":"test","digest":"{digest}"}}]}}"""
        let review scope attempt revision synopsis =
            $"""{{"kind":"process-review","identity":"review-{scope}-{item}","itemId":"{item}","revision":{revision},"scope":"{scope}","attemptId":{attempt},"outcomeSynopsis":"{synopsis}","wentWell":["Focused tests"],"problems":["One repair"],"avoidableDelayOrRework":[],"processObservations":["Routine route stayed bounded"],"remainingRisks":[],"concreteImprovements":["Keep exact attribution"],"evidence":[{{"kind":"test","digest":"{digest}"}}],"evidenceCoverage":"partial","populationCoverage":"complete","confidence":"high","reviewerModel":"gpt-5","reviewerEffort":"medium","reviewedAt":"2026-09-08T10:04:00Z","durationSeconds":30}}"""
        TelemetryStoreApplication.ingest path approved (operationalBatch "review-observations" item [ span; attribution; complication; review "attempt" "\"attempt-review\"" 1 "Attempt completed"; review "item" "null" 1 "Item completed" ]) |> unwrap |> ignore
        let summary = TelemetryStoreApplication.reviewSummary path approved item |> unwrap
        Assert.Contains("Attempt completed", summary)
        Assert.Contains("Item completed", summary)
        let detail = TelemetryStoreApplication.itemDetail path approved item |> unwrap
        Assert.Contains("\"schema\":\"fsgg.telemetry.item-detail/1\"", detail)
        Assert.Contains("\"nativeTotal\":15", detail)
        Assert.Contains("\"direct\":15", detail)
        Assert.Contains("\"missingAttribution\":0", detail)
        Assert.Contains("A focused test exposed the repair", detail)
        let publicPath = path + "-public.json"
        use publicCleanup = { new IDisposable with member _.Dispose() = if File.Exists publicPath then File.Delete publicPath }
        TelemetryStoreApplication.exportPublic path approved (Some item) publicPath |> unwrap |> ignore
        let publicJson = File.ReadAllText publicPath
        Assert.DoesNotContain("Attempt completed", publicJson)
        Assert.DoesNotContain("A focused test exposed the repair", publicJson)
        let duplicate = attribution.Replace("attribute-review", "attribute-review-duplicate")
        Assert.Contains("\"quarantined\":1", TelemetryStoreApplication.ingest path approved (operationalBatch "review-duplicate-attribution" item [ duplicate ]) |> unwrap)
        Assert.Contains("\"direct\":15", TelemetryStoreApplication.itemDetail path approved item |> unwrap)
        let corrected = TelemetryStoreApplication.ingest path approved (operationalBatch "review-correction" item [ review "attempt" "\"attempt-review\"" 2 "Attempt completed after correction" ]) |> unwrap
        Assert.Contains("\"accepted\":1", corrected)
        Assert.DoesNotContain("\"outcomeSynopsis\":\"Attempt completed\"", TelemetryStoreApplication.reviewSummary path approved item |> unwrap)

    [<Fact>]
    let ``UTEL-08 rejects premature reviews allocation and duplicate native attribution`` () =
        let cleanup, path = root ()
        use cleanup = cleanup
        TelemetryStoreApplication.initialize path approved |> unwrap |> ignore
        let item = "UTEL-08-reject"
        let admission = $"""{{"kind":"runtime-admission","identity":"admission-open","itemId":"{item}","revision":0,"invocationId":"invoke-open","featureId":"UTEL","attemptId":"attempt-open","parentAttemptId":null,"producerStream":"review-worker","requestedModel":"gpt-5","requestedEffort":"medium","backend":"codex-collaboration"}}"""
        TelemetryStoreApplication.ingest path approved (operationalBatch "review-open" item [ activation item "collaboration-spawn-agent" 60L; dispatch item "dispatch-open" "root" None "collaboration-spawn-agent" "00"; lineage item "lineage-open" "dispatch-open" "invoke-open" "root" None "invoke-open" "collaboration-spawn-agent"; admission ]) |> unwrap |> ignore
        let premature = $"""{{"kind":"process-review","identity":"review-open","itemId":"{item}","revision":1,"scope":"attempt","attemptId":"attempt-open","outcomeSynopsis":"Not terminal","wentWell":[],"problems":[],"avoidableDelayOrRework":[],"processObservations":[],"remainingRisks":[],"concreteImprovements":[],"evidence":[],"evidenceCoverage":"unknown","populationCoverage":"unknown","confidence":"low","reviewerModel":"gpt-5","reviewerEffort":"medium","reviewedAt":"2026-09-08T10:04:00Z","durationSeconds":1}}"""
        Assert.Contains("terminal", sprintf "%A" (TelemetryStoreApplication.ingest path approved (operationalBatch "premature-review" item [ premature ])))
        let allocated = $"""{{"kind":"activity-usage-attribution","identity":"allocated","itemId":"{item}","revision":0,"usageIdentity":"missing-usage","activityId":null,"classification":"unclassified","input":1,"cachedInput":0,"output":1,"reasoning":null,"total":2}}"""
        Assert.Contains("matching native usage", sprintf "%A" (TelemetryStoreApplication.ingest path approved (operationalBatch "allocated-review" item [ allocated ])))

    [<Fact>]
    let ``UTEL-08 v7 store upgrades without changing earlier migration receipts`` () =
        let cleanup, path = root ()
        use cleanup = cleanup
        TelemetryStoreApplication.initialize path approved |> unwrap |> ignore
        use connection = new SqliteConnection($"Data Source=%s{Path.Combine(path, TelemetryStoreApplication.databaseFileName)};Pooling=False")
        connection.Open()
        use downgrade = connection.CreateCommand()
        downgrade.CommandText <- dropReviewSchema + " PRAGMA user_version=7;"
        downgrade.ExecuteNonQuery() |> ignore
        connection.Close()
        Assert.Contains("\"schemaVersion\":8", TelemetryStoreApplication.initialize path approved |> unwrap)
        use verify = new SqliteConnection($"Data Source=%s{Path.Combine(path, TelemetryStoreApplication.databaseFileName)};Pooling=False")
        verify.Open()
        use corrupt = verify.CreateCommand()
        corrupt.CommandText <- "UPDATE schema_migrations SET digest='corrupt' WHERE version=8;"
        corrupt.ExecuteNonQuery() |> ignore
        verify.Close()
        Assert.Contains("migration checksum mismatch", sprintf "%A" (TelemetryStoreApplication.status path approved))
