namespace FS.GG.Coord.Cli.Tests

open System
open System.IO
open System.Text
open System.Text.Json
open System.Text.Json.Nodes
open Microsoft.Data.Sqlite
open Xunit
open FS.GG.Coord
open FS.GG.Coord.Cli

/// Admission regressions use real enrolled receipts; claimed JSON roles are never authority.
module EfficiencyAdmissionTests =
    let private approved = TelemetryStore.ApprovedLocalDurable
    let private unwrap = function Ok value -> value | Error errors -> failwithf "%A" errors
    let private root () =
        let path = Path.Combine(Path.GetTempPath(), "fsgg-eff-admission-" + Guid.NewGuid().ToString("N"))
        Directory.CreateDirectory path |> ignore
        { new IDisposable with member _.Dispose() = if Directory.Exists path then Directory.Delete(path, true) }, path
    let private batch name events =
        Encoding.UTF8.GetBytes($"""{{"schema":"{TelemetryStore.BatchSchema}","ingestId":"{name}","sourceIdentity":"test-source","generation":"g1","cursor":"{name}","eventCount":{List.length events},"events":[{String.concat "," events}]}}""")
    let private scope : TelemetryReceipt.Scope =
        { Workspace = "eff-workspace"; Producer = "eff-producer"; Stream = "eff-stream" }
    let private principal = TelemetryReceipt.genericPrincipal scope
    let private submitAs (principal: TelemetryReceipt.Principal) path name events =
        let scope = principal.Scope
        let payload = Encoding.UTF8.GetString(batch name events)
        let bytes = Encoding.UTF8.GetBytes($"""{{"schema":"{TelemetryReceipt.Schema}","workspaceId":"{scope.Workspace}","producerId":"{scope.Producer}","streamId":"{scope.Stream}","batchId":"{name}","payload":{payload}}}""")
        TelemetryStoreApplication.submitReceiptPrincipal path approved principal bytes |> unwrap |> ignore
        TelemetryStoreApplication.drainReceipts path approved scope.Workspace |> unwrap
    let private submit path name events = submitAs principal path name events
    let private scalar path sql =
        use connection = new SqliteConnection($"Data Source={Path.Combine(path, TelemetryStoreApplication.databaseFileName)};Pooling=False")
        connection.Open()
        use command = connection.CreateCommand()
        command.CommandText <- sql
        string(command.ExecuteScalar())
    let private admission =
        """{"kind":"runtime-admission","identity":"admission-a","itemId":"A","revision":0,"invocationId":"invoke-a","featureId":"EFF","attemptId":"attempt-a","parentAttemptId":null,"producerStream":"eff-stream","requestedModel":"sol","requestedEffort":"medium","backend":"codex-collaboration"}"""
    let private terminal =
        """{"kind":"runtime-terminal","identity":"terminal-a","itemId":"A","revision":0,"invocationId":"invoke-a","threadId":"thread-a","outcome":"success","exitCode":0}"""
    let private review =
        """{"kind":"process-review","identity":"review-a","itemId":"A","revision":0,"scope":"attempt","attemptId":"attempt-a","outcomeSynopsis":"Observed source work","wentWell":[],"problems":[],"avoidableDelayOrRework":[],"processObservations":[],"remainingRisks":[],"concreteImprovements":[],"evidence":[],"evidenceCoverage":"partial","populationCoverage":"unknown","confidence":"low","reviewerModel":"sol","reviewerEffort":"medium","reviewedAt":"2026-10-06T00:20:00Z","durationSeconds":1}"""
    let private usage revision =
        $"""{{"kind":"runtime-turn-usage","identity":"usage-a","itemId":"A","revision":{revision},"invocationId":"invoke-a","threadId":"thread-a","turnId":"turn-a","turnSequence":1,"provider":"openai","requestedModel":"sol","observedModel":"sol","requestedEffort":"medium","observedEffort":"medium","backend":"codex-collaboration","scope":"completed-turn","provenance":"codex-exec-jsonl","input":99,"cachedInput":0,"output":1,"reasoning":null,"total":100}}"""
    let private sourceRef event =
        let fact = (TelemetryStore.parseBatch(batch "digest-fixture" [event]) |> unwrap).Facts.Head
        JsonNode.Parse(JsonSerializer.Serialize {| id = fact.Identity; kind = fact.Kind; revision = fact.Revision; contentDigest = "sha256:" + fact.ContentDigest |})
    let private allocation () =
        let node = JsonNode.Parse("""{"kind":"efficiency-resource-allocation/1","identity":"allocation-a","itemId":null,"revision":0,"resource":{"sourceRef":null,"dimension":"model-tokens","provider":"openai","accountingScope":"completed-turn","unit":"tokens-total","amount":100},"shares":[{"itemId":"A","purpose":"direct-product","fraction":{"numerator":3,"denominator":5},"epistemicStatus":"observed","necessity":"required","evidenceRefs":[],"alternative":null},{"itemId":"A","purpose":"unknown","fraction":{"numerator":2,"denominator":5},"epistemicStatus":"unknown","necessity":"uncertain","evidenceRefs":[],"alternative":null}],"coverage":{"allocation":"complete","evidence":"partial"},"provenance":{"sourceIdentity":"test-source","authorityRole":"root-reviewer","authorityRef":null,"rootDispatchRef":null,"invocationRef":"invoke-a"},"occurredAt":null,"observedAt":"2026-10-06T00:21:00Z"}""")
        node["resource"]["sourceRef"] <- sourceRef (usage 0)
        node["provenance"]["authorityRef"] <- sourceRef review
        node["shares"][0]["evidenceRefs"].AsArray().Add(sourceRef (usage 0))
        node
    let private fixture receiptSources =
        let cleanup, path = root ()
        TelemetryStoreApplication.initialize path approved |> unwrap |> ignore
        TelemetryStoreApplication.provisionReceiptWorkspace path approved scope.Workspace |> unwrap |> ignore
        TelemetryStoreApplication.enrollReceiptPrincipal path approved principal |> unwrap |> ignore
        if receiptSources then Assert.Contains("\"rejected\":0", submit path "sources" [admission; usage 0; terminal; review])
        else
            let otherScope = { scope with Producer = "other-producer"; Stream = "other-stream" }
            let other = TelemetryReceipt.genericPrincipal otherScope
            TelemetryStoreApplication.enrollReceiptPrincipal path approved other |> unwrap |> ignore
            Assert.Contains("\"rejected\":0", submitAs other path "sources" [admission; usage 0; terminal; review])
        cleanup, path
    let private assertRejected path name node =
        // Every negative allocation passes the decoder: refusal must come from store joins.
        TelemetryStore.parseBatch(batch name [node.ToJsonString()]) |> unwrap |> ignore
        let result = submit path name [node.ToJsonString()]
        Assert.Contains("\"rejected\":1", result)
        Assert.Equal("0", scalar path "SELECT count(*) FROM efficiency_records;")
        Assert.Equal("0", scalar path "SELECT count(*) FROM efficiency_record_history;")

    [<Fact>]
    let ``anonymous efficiency ingest is refused despite a valid allocation and prior sources`` () =
        let cleanup, path = root ()
        use cleanup = cleanup
        TelemetryStoreApplication.initialize path approved |> unwrap |> ignore
        TelemetryStoreApplication.ingest path approved (batch "sources" [admission; usage 0; terminal; review]) |> unwrap |> ignore
        let result = TelemetryStoreApplication.ingest path approved (batch "anonymous" [(allocation()).ToJsonString()])
        Assert.Equal(Error ["efficiency-authenticated-receipt-required"], result)
        Assert.Equal("0", scalar path "SELECT count(*) FROM efficiency_records;")

    [<Fact>]
    let ``declared runtime observer cannot borrow another producer authority receipt`` () =
        let cleanup, path = fixture false
        use cleanup = cleanup
        assertRejected path "unwitnessed-authority" (allocation())
        Assert.Equal("4", scalar path "SELECT count(*) FROM fact_acceptance_times;")

    [<Fact>]
    let ``same revision with wrong content digest is rejected`` () =
        let cleanup, path = fixture true
        use cleanup = cleanup
        let node = allocation ()
        node["resource"]["sourceRef"]["contentDigest"] <- JsonValue.Create("sha256:" + String.replicate 64 "0")
        assertRejected path "wrong-digest" node

    [<Fact>]
    let ``superseded canonical source revision is rejected`` () =
        let cleanup, path = fixture true
        use cleanup = cleanup
        Assert.Contains("\"rejected\":0", submit path "source-successor" [usage 1])
        assertRejected path "stale-allocation" (allocation())
        Assert.Equal("1", scalar path "SELECT revision FROM current_ingest_facts WHERE identity='usage-a';")

    [<Theory>]
    [<InlineData("amount")>]
    [<InlineData("unit")>]
    [<InlineData("accountingScope")>]
    [<InlineData("provider")>]
    let ``allocation must match the witnessed native counter and accounting scope`` field =
        let cleanup, path = fixture true
        use cleanup = cleanup
        let node = allocation ()
        match field with
        | "amount" -> node["resource"][field] <- JsonValue.Create(99)
        | "unit" -> node["resource"][field] <- JsonValue.Create("tokens-output")
        | "accountingScope" -> node["resource"][field] <- JsonValue.Create("legacy-usage")
        | _ -> node["resource"][field] <- JsonValue.Create("other-provider")
        assertRejected path ("mismatch-" + field) node

    [<Fact>]
    let ``individually valid fractions cannot allocate more than original total`` () =
        let cleanup, path = fixture true
        use cleanup = cleanup
        let node = allocation ()
        node["shares"][1]["fraction"]["numerator"] <- JsonValue.Create(3)
        assertRejected path "overallocated" node
        Assert.Equal("100", scalar path "SELECT json_extract(canonical,'$.total') FROM ingest_facts WHERE identity='usage-a';")

    [<Fact>]
    let ``accepted allocation exact replay retains original receiver clock and history`` () =
        let cleanup, path = fixture true
        use cleanup = cleanup
        let event = (allocation()).ToJsonString()
        Assert.Contains("\"rejected\":0", submit path "allocation-first" [event])
        let before = scalar path "SELECT accepted_at || '|' || receipt_key FROM efficiency_record_history WHERE identity='allocation-a';"
        Assert.False(String.IsNullOrWhiteSpace before)
        Assert.Contains("\"rejected\":0", submit path "allocation-first" [event])
        Assert.Contains("\"rejected\":0", submit path "allocation-replay" [event])
        TelemetryStoreApplication.initialize path approved |> unwrap |> ignore
        Assert.Equal(before, scalar path "SELECT accepted_at || '|' || receipt_key FROM efficiency_record_history WHERE identity='allocation-a';")
        Assert.Equal("1", scalar path "SELECT count(*) FROM efficiency_record_history;")
        Assert.Equal("1", scalar path "SELECT count(*) FROM fact_acceptance_times WHERE identity='allocation-a';")
        Assert.Equal("5", scalar path "SELECT count(*) FROM fact_acceptance_times;")

    [<Fact>]
    let ``historical anonymous facts retain unknown receiver clocks after reopen`` () =
        let cleanup, path = root ()
        use cleanup = cleanup
        TelemetryStoreApplication.initialize path approved |> unwrap |> ignore
        TelemetryStoreApplication.ingest path approved (batch "historical" [admission; usage 0; terminal; review]) |> unwrap |> ignore
        TelemetryStoreApplication.initialize path approved |> unwrap |> ignore
        Assert.Equal("4", scalar path "SELECT count(*) FROM ingest_facts;")
        Assert.Equal("0", scalar path "SELECT count(*) FROM fact_acceptance_times;")

    [<Fact>]
    let ``generic informational runtime row cannot grant native observer authority`` () =
        let cleanup,path = fixture true
        use cleanup = cleanup
        let node = allocation ()
        node["provenance"]["authorityRole"] <- JsonValue.Create("runtime-observer")
        node["provenance"]["authorityRef"] <- sourceRef admission
        assertRejected path "generic-native-role" node
