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
    let private budgetBatch ingest cursor events =
        Encoding.UTF8.GetBytes $"""{{"schema":"{TelemetryStore.BatchSchema}","ingestId":"{ingest}","sourceIdentity":"budget-worker","generation":"g1","cursor":"{cursor}","eventCount":{List.length events},"events":[{String.concat "," events}]}}"""
    let private population item revision state source =
        $"""{{"kind":"budget-population","identity":"population-{item}","itemId":"{item}","revision":{revision},"originalItemId":"{item}","state":"{state}","sourceKind":"native-item","sourceRef":"{source}"}}"""
    let private attribution item revision numerator denominator coverage attribution sourceKind source =
        let number value = value |> Option.map string |> Option.defaultValue "null"
        $"""{{"kind":"budget-attribution","identity":"attribution-{item}","itemId":"{item}","revision":{revision},"dimension":"model-tokens","provider":"openai","accountingScope":"whole-item","numerator":{number numerator},"denominator":{number denominator},"coverage":"{coverage}","attribution":"{attribution}","sourceKind":"{sourceKind}","sourceRef":"{source}"}}"""

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
        pragma path 5
        Assert.Contains("newer than supported", sprintf "%A" (TelemetryStoreApplication.drain path approved))
        Assert.Single(Directory.GetFiles(Path.Combine(path, "inbox", "worker-a"), "*.ready")) |> ignore
        pragma path 4
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
        command.CommandText <- dropBudgetSchema + " DROP TABLE ci_coverage; DROP TABLE ci_steps; DROP TABLE ci_jobs; DROP TABLE ci_runs; DROP TABLE ci_pages; DROP TABLE ci_bindings; DROP INDEX runtime_thread_start_identity; DROP INDEX runtime_turn_start_identity; DROP INDEX runtime_turn_native_identity; DROP TABLE runtime_gaps; DROP TABLE runtime_terminals; DROP TABLE runtime_turn_usage; DROP TABLE runtime_starts; DROP TABLE runtime_admissions; DELETE FROM schema_migrations WHERE version IN (2,3); PRAGMA user_version=1;"
        command.ExecuteNonQuery() |> ignore
        connection.Close()
        let initialized = TelemetryStoreApplication.initialize path approved |> unwrap
        Assert.Contains("\"schemaVersion\":4", initialized)
        Assert.Contains("\"status\":\"ready\"", TelemetryStoreApplication.status path approved |> unwrap)

    [<Fact>]
    let ``UTEL-04A CI observations migrate ingest and summarize without private fields`` () =
        let cleanup, path = root ()
        use cleanup = cleanup
        Assert.Contains("\"schemaVersion\":4", TelemetryStoreApplication.initialize path approved |> unwrap)
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

    [<Fact>]
    let ``UTEL-04A v2 upgrade preserves runtime observations`` () =
        let cleanup, path = root ()
        use cleanup = cleanup
        TelemetryStoreApplication.initialize path approved |> unwrap |> ignore
        use connection = new SqliteConnection($"Data Source=%s{Path.Combine(path, TelemetryStoreApplication.databaseFileName)};Pooling=False")
        connection.Open()
        use command = connection.CreateCommand()
        command.CommandText <- "INSERT INTO runtime_admissions VALUES('runtime-keep','UTEL-04A','invoke-1','UTEL','a1',NULL,'worker',NULL,NULL,NULL); " + dropBudgetSchema + " DROP TABLE ci_coverage; DROP TABLE ci_steps; DROP TABLE ci_jobs; DROP TABLE ci_runs; DROP TABLE ci_pages; DROP TABLE ci_bindings; DELETE FROM schema_migrations WHERE version=3; PRAGMA user_version=2;"
        command.ExecuteNonQuery() |> ignore
        connection.Close()
        Assert.Contains("\"schemaVersion\":4", TelemetryStoreApplication.initialize path approved |> unwrap)
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
        command.CommandText <- "INSERT INTO ci_bindings VALUES('keep-binding','UTEL-05A','keep-collection','o/r','aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa',1,'ci.yml','UTEL','a1',NULL,'worker','exact'); " + dropBudgetSchema + " PRAGMA user_version=3;"
        command.ExecuteNonQuery() |> ignore
        connection.Close()
        Assert.Contains("\"schemaVersion\":4", TelemetryStoreApplication.initialize path approved |> unwrap)
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
