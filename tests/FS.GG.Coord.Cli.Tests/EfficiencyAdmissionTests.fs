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
        // Fixture events must satisfy the decoder before receipt authority is exercised.
        TelemetryStore.parseBatch(batch name events) |> unwrap |> ignore
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
        // The existing process-review contract starts revisions at one.
        """{"kind":"process-review","identity":"review-a","itemId":"A","revision":1,"scope":"attempt","attemptId":"attempt-a","outcomeSynopsis":"Observed source work","wentWell":[],"problems":[],"avoidableDelayOrRework":[],"processObservations":[],"remainingRisks":[],"concreteImprovements":[],"evidence":[],"evidenceCoverage":"partial","populationCoverage":"unknown","confidence":"low","reviewerModel":"sol","reviewerEffort":"medium","reviewedAt":"2026-10-06T00:20:00Z","durationSeconds":1}"""
    let private usage revision =
        $"""{{"kind":"runtime-turn-usage","identity":"usage-a","itemId":"A","revision":{revision},"invocationId":"invoke-a","threadId":"thread-a","turnId":"turn-a","turnSequence":1,"provider":"openai","requestedModel":"sol","observedModel":"sol","requestedEffort":"medium","observedEffort":"medium","backend":"codex-collaboration","scope":"completed-turn","provenance":"codex-exec-jsonl","input":99,"cachedInput":0,"output":1,"reasoning":null,"total":100}}"""
    let private sourceRef event =
        let fact = (TelemetryStore.parseBatch(batch "digest-fixture" [event]) |> unwrap).Facts.Head
        JsonNode.Parse(JsonSerializer.Serialize {| id = fact.Identity; kind = fact.Kind; revision = fact.Revision; contentDigest = "sha256:" + fact.ContentDigest |})
    let private allocation () =
        let node = JsonNode.Parse("""{"kind":"efficiency-resource-allocation/1","identity":"allocation-a","itemId":null,"revision":0,"resource":{"sourceRef":null,"dimension":"model-tokens","provider":"openai","accountingScope":"completed-turn","unit":"tokens-total","amount":100},"shares":[{"itemId":"A","purpose":"direct-product","fraction":{"numerator":3,"denominator":5},"epistemicStatus":"observed","necessity":"required","evidenceRefs":[],"alternative":null},{"itemId":"A","purpose":"unknown","fraction":{"numerator":2,"denominator":5},"epistemicStatus":"unknown","necessity":"uncertain","evidenceRefs":[],"alternative":null}],"coverage":{"allocation":"complete","evidence":"partial"},"provenance":{"sourceIdentity":"test-source","authorityRole":"root-reviewer","authorityRef":null,"rootDispatchRef":null,"invocationRef":"invoke-a"},"occurredAt":null,"observedAt":"2026-10-06T00:21:00Z"}""")
        node.["resource"].["sourceRef"] <- sourceRef (usage 0)
        node.["provenance"].["authorityRef"] <- sourceRef review
        (node.["shares"].[0].["evidenceRefs"]).AsArray().Add(sourceRef (usage 0))
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
    let private assertRejected path name (node: JsonNode) =
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
        node.["resource"].["sourceRef"].["contentDigest"] <- JsonValue.Create("sha256:" + String.replicate 64 "0")
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
        | "amount" -> node.["resource"].[field] <- JsonValue.Create(99)
        | "unit" -> node.["resource"].[field] <- JsonValue.Create("tokens-output")
        | "accountingScope" -> node.["resource"].[field] <- JsonValue.Create("legacy-usage")
        | _ -> node.["resource"].[field] <- JsonValue.Create("other-provider")
        assertRejected path ("mismatch-" + field) node

    [<Fact>]
    let ``individually valid fractions cannot allocate more than original total`` () =
        let cleanup, path = fixture true
        use cleanup = cleanup
        let node = allocation ()
        node.["shares"].[1].["fraction"].["numerator"] <- JsonValue.Create(3)
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
        node.["provenance"].["authorityRole"] <- JsonValue.Create("runtime-observer")
        node.["provenance"].["authorityRef"] <- sourceRef admission
        assertRejected path "generic-native-role" node

    let private sourceOutcome =
        """{"kind":"native-item-outcome","identity":"outcome-a","itemId":"A","revision":0,"repository":"o/r","prNumber":7,"baseRef":"main","baseSha":"dddddddddddddddddddddddddddddddddddddddd","head":"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa","outcome":"delivered","codeDelivery":"delivered","mergeCommit":"bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb","occurredAt":"2026-10-06T00:20:00Z","observedAt":"2026-10-06T00:20:01Z","sourceKind":"routine-delivery","sourceRef":"routine-delivery:A"}"""
    let private analystDispatch =
        """{"kind":"expected-dispatch","identity":"analyst-dispatch","itemId":"A","revision":0,"dispatchId":"analyst","activationId":"activation-A","relation":"root","parentDispatchId":null,"runtime":"codex-exec","expectedAt":"2026-10-06T00:20:02Z","clockProvenance":"host-wall"}"""
    let private pendingFixture () =
        let cleanup,path = fixture true
        Assert.Contains("\"rejected\":0", submit path "delivered" [sourceOutcome])
        let reconciled = TelemetryStoreApplication.efficiencyAnalysisReconcile path approved principal (Some "A") |> unwrap
        Assert.Contains("\"requested\":1",reconciled)
        let pending = JsonNode.Parse(scalar path "SELECT canonical FROM efficiency_analysis_requests;")
        cleanup,path,pending
    let private claimTemplate (pending: JsonNode) claim =
        let node = JsonNode.Parse("""{"schema":"fsgg.telemetry.efficiency-analysis-claim-input/1","cas":null,"claimId":"claim","modelAlias":"sol","invocationRef":null,"authority":null,"claimedAt":"2026-10-06T00:21:00Z","limitSupport":{"inputTokens":"unavailable","outputTokens":"unavailable","seconds":"enforced"}}""")
        node["claimId"] <- JsonValue.Create(claim: string)
        node["cas"] <- JsonSerializer.SerializeToNode {| requestId=pending["requestId"].GetValue<string>(); expectedRevision=pending["revision"].GetValue<int64>(); expectedContentDigest=pending["contentDigest"].GetValue<string>() |}
        node["authority"] <- pending.["canonicalRequest"].["authority"].DeepClone()
        Encoding.UTF8.GetBytes(node.ToJsonString())

    [<Fact>]
    let ``prospective claim refuses a foreign applied dispatch without reserving budget`` () =
        let cleanup,path,pending = pendingFixture ()
        use cleanup = cleanup
        let other = TelemetryReceipt.genericPrincipal { scope with Producer="foreign";Stream="foreign-stream" }
        TelemetryStoreApplication.enrollReceiptPrincipal path approved other |> unwrap |> ignore
        Assert.Contains("\"rejected\":0",submitAs other path "foreign-dispatch" [analystDispatch])
        let result = TelemetryStoreApplication.efficiencyAnalysisClaimProspective path approved principal "analyst-dispatch" "A" (claimTemplate pending "claim-foreign")
        Assert.Equal(Error ["efficiency-prospective-dispatch-unavailable"],result)
        Assert.Equal("0",scalar path "SELECT count(*) FROM efficiency_analysis_reservations;")
        Assert.Equal("pending",scalar path "SELECT state FROM efficiency_analysis_requests;")

    [<Fact>]
    let ``two claim callers with one receiver CAS produce exactly one reservation`` () =
        let cleanup,path,pending = pendingFixture ()
        use cleanup = cleanup
        Assert.Contains("\"rejected\":0",submit path "analyst-dispatch" [analystDispatch])
        let call claim = System.Threading.Tasks.Task.Run(fun () -> TelemetryStoreApplication.efficiencyAnalysisClaimProspective path approved principal "analyst-dispatch" "A" (claimTemplate pending claim))
        let results = System.Threading.Tasks.Task.WhenAll([|call "one";call "two"|]).GetAwaiter().GetResult()
        Assert.Equal(1,results |> Array.filter Result.isOk |> Array.length)
        Assert.Equal(1,results |> Array.filter Result.isError |> Array.length)
        Assert.Equal("1",scalar path "SELECT count(*) FROM efficiency_analysis_reservations;")
        Assert.Equal("claimed",scalar path "SELECT state FROM efficiency_analysis_requests;")
        Assert.Equal("",scalar path "SELECT invocation_ref FROM efficiency_analysis_requests;")

    [<Fact>]
    let ``claimed analyst review does not trigger another request before settlement`` () =
        let cleanup,path,pending = pendingFixture ()
        use cleanup = cleanup
        Assert.Contains("\"rejected\":0",submit path "analyst-dispatch" [analystDispatch])
        TelemetryStoreApplication.efficiencyAnalysisClaimProspective path approved principal "analyst-dispatch" "A" (claimTemplate pending "active") |> unwrap |> ignore
        Assert.Contains("\"rejected\":0",submit path "new-review" [review.Replace("\"revision\":1","\"revision\":2")])
        let result = TelemetryStoreApplication.efficiencyAnalysisReconcile path approved principal (Some "A") |> unwrap
        Assert.Contains("analysis-pending-unresolved-claim",result)
        Assert.Equal("1",scalar path "SELECT count(*) FROM efficiency_analysis_requests;")

    [<Fact>]
    let ``export groups witnessed children once and keeps native acceptance denominator unknown`` () =
        let cleanup,path = fixture true
        use cleanup = cleanup
        let population item = $"""{{"kind":"budget-population","identity":"population-{item}","itemId":"{item}","revision":0,"originalItemId":"GROUP","state":"open","sourceKind":"native-item","sourceRef":"roadmap-dispatch:{item}"}}"""
        Assert.Contains("\"rejected\":0",submit path "group-members" [population "A";population "B"])
        use compact = JsonDocument.Parse(TelemetryStoreApplication.compactDashboardSnapshot path approved None |> unwrap)
        let revision = compact.RootElement.GetProperty("revision").GetString()
        use exported = JsonDocument.Parse(TelemetryStoreApplication.efficiencyExport path approved revision 200 1000 |> unwrap)
        let rows = exported.RootElement.GetProperty("items").EnumerateArray() |> Seq.toArray
        Assert.Single rows |> ignore
        Assert.Equal("GROUP",rows[0].GetProperty("itemId").GetString())
        let metrics = rows[0].GetProperty("metrics").EnumerateArray() |> Seq.toArray
        let total = metrics |> Array.find (fun metric -> metric.GetProperty("metric").GetString()="observed-resource" && metric.GetProperty("unit").GetString()="tokens-total")
        Assert.Equal(100L,total.GetProperty("value").GetProperty("numerator").GetInt64())
        let native = metrics |> Array.find (fun metric -> metric.GetProperty("metric").GetString()="cost-per-accepted")
        Assert.Equal(JsonValueKind.Null,native.GetProperty("value").GetProperty("numerator").ValueKind)
        Assert.Equal("unknown",native.GetProperty("value").GetProperty("status").GetString())

    [<Fact>]
    let ``prospective admission and lineage cannot attach an invocation without actual start`` () =
        let cleanup,path,pending = pendingFixture ()
        use cleanup = cleanup
        Assert.Contains("\"rejected\":0",submit path "analyst-dispatch" [analystDispatch])
        let claimed = TelemetryStoreApplication.efficiencyAnalysisClaimProspective path approved principal "analyst-dispatch" "A" (claimTemplate pending "attach") |> unwrap |> JsonNode.Parse
        let admissionEvent = admission.Replace("admission-a","analyst-admission").Replace("invoke-a","analyst-invocation")
        let lineageEvent = """{"kind":"invocation-lineage","identity":"analyst-lineage","itemId":"A","revision":0,"dispatchId":"analyst","invocationId":"analyst-invocation","relation":"root","parentInvocationId":null,"rootInvocationId":"analyst-invocation","runtime":"codex-exec"}"""
        Assert.Contains("\"rejected\":0",submit path "prospective-observations" [admissionEvent;lineageEvent])
        let attach = JsonNode.Parse("""{"schema":"fsgg.telemetry.efficiency-analysis-attach-invocation-input/1","cas":null,"claimId":"attach","dispatchRef":null,"invocationRef":"analyst-invocation","lineageRefs":[],"authority":null,"attachedAt":"2026-10-06T00:22:00Z"}""")
        attach["cas"] <- JsonSerializer.SerializeToNode {| requestId=claimed["requestId"].GetValue<string>(); expectedRevision=claimed["revision"].GetValue<int64>(); expectedContentDigest=claimed["contentDigest"].GetValue<string>() |}
        attach["dispatchRef"] <- claimed["dispatchRef"].DeepClone()
        attach["authority"] <- pending.["canonicalRequest"].["authority"].DeepClone()
        attach["lineageRefs"].AsArray().Add(sourceRef admissionEvent)
        attach["lineageRefs"].AsArray().Add(sourceRef lineageEvent)
        let result = TelemetryStoreApplication.efficiencyAnalysis path approved principal "attach-invocation" (Encoding.UTF8.GetBytes(attach.ToJsonString())) None
        Assert.Equal(Error ["efficiency-actual-start-witness-unavailable"],result)
        Assert.Equal("",scalar path "SELECT invocation_ref FROM efficiency_analysis_requests;")
        Assert.Equal("reserved",scalar path "SELECT state FROM efficiency_analysis_reservations;")

    [<Fact>]
    let ``private claim descriptor refuses a symlink and overbound bytes`` () =
        if OperatingSystem.IsLinux() then
            let cleanup,path = root ()
            use cleanup = cleanup
            let descriptor = Path.Combine(path,"claim.json")
            File.WriteAllText(descriptor,"{}")
            File.SetUnixFileMode(descriptor,UnixFileMode.UserRead ||| UnixFileMode.UserWrite)
            Assert.Equal<byte array>(Encoding.UTF8.GetBytes("{}"),WorkspaceTelemetryApplication.readEfficiencyClaimTemplate descriptor |> unwrap)
            let alias = Path.Combine(path,"alias.json")
            File.CreateSymbolicLink(alias,descriptor) |> ignore
            Assert.True(WorkspaceTelemetryApplication.readEfficiencyClaimTemplate alias |> Result.isError)
            File.WriteAllBytes(descriptor,Array.create 16385 (byte 'x'))
            Assert.True(WorkspaceTelemetryApplication.readEfficiencyClaimTemplate descriptor |> Result.isError)

    [<Fact>]
    let ``three failed authorized analyses consume the stable outcome budget across snapshots`` () =
        let cleanup,path,first = pendingFixture ()
        use cleanup = cleanup
        let pending = ResizeArray<JsonNode>()
        pending.Add first
        for revision in 2 .. 4 do
            let successor = review.Replace("\"revision\":1",$"\"revision\":{revision}")
            Assert.Contains("\"rejected\":0",submit path ($"review-{revision}") [successor])
            use response = JsonDocument.Parse(TelemetryStoreApplication.efficiencyAnalysisReconcile path approved principal (Some "A") |> unwrap)
            let id = (response.RootElement.GetProperty("requestIds")).[0].GetString()
            pending.Add(JsonNode.Parse(scalar path ($"SELECT canonical FROM efficiency_analysis_requests WHERE request_id='{id}';")))
        let latestAuthority = (pending.[3].["canonicalRequest"].["authority"]).DeepClone()
        for index in 0 .. 2 do
            let dispatchIdentity = $"analyst-dispatch-{index}"
            let event = analystDispatch.Replace("analyst-dispatch",dispatchIdentity).Replace("\"dispatchId\":\"analyst\"",$"\"dispatchId\":\"analyst-{index}\"")
            Assert.Contains("\"rejected\":0",submit path ($"dispatch-{index}") [event])
            let bytes = claimTemplate pending[index] ($"budget-{index}")
            let template = JsonNode.Parse bytes
            template["authority"] <- latestAuthority.DeepClone()
            let claimed = TelemetryStoreApplication.efficiencyAnalysisClaimProspective path approved principal dispatchIdentity "A" (Encoding.UTF8.GetBytes(template.ToJsonString())) |> unwrap |> JsonNode.Parse
            let settle = JsonNode.Parse("""{"schema":"fsgg.telemetry.efficiency-analysis-settle-input/1","cas":null,"claimId":"unset","state":"failed","resultAssessmentRef":null,"reason":"interrupted-analysis-outcome-unknown","usageRefs":[],"invocationOutcome":"unknown","reconciliationRefs":[],"authority":null,"settledAt":"2026-10-06T00:22:00Z","invocationRef":null}""")
            settle["cas"] <- JsonSerializer.SerializeToNode {| requestId=claimed["requestId"].GetValue<string>();expectedRevision=claimed["revision"].GetValue<int64>();expectedContentDigest=claimed["contentDigest"].GetValue<string>() |}
            settle["claimId"] <- JsonValue.Create($"budget-{index}")
            settle["authority"] <- latestAuthority.DeepClone()
            TelemetryStoreApplication.efficiencyAnalysis path approved principal "settle" (Encoding.UTF8.GetBytes(settle.ToJsonString())) None |> unwrap |> ignore
        let fourthDispatch = analystDispatch.Replace("analyst-dispatch","analyst-dispatch-4").Replace("\"dispatchId\":\"analyst\"","\"dispatchId\":\"analyst-4\"")
        Assert.Contains("\"rejected\":0",submit path "dispatch-4" [fourthDispatch])
        let fourth = JsonNode.Parse(claimTemplate pending[3] "fourth")
        fourth["authority"] <- latestAuthority.DeepClone()
        Assert.Equal(Error ["analysis-budget-exhausted"],TelemetryStoreApplication.efficiencyAnalysisClaimProspective path approved principal "analyst-dispatch-4" "A" (Encoding.UTF8.GetBytes(fourth.ToJsonString())))
        Assert.Equal("3",scalar path "SELECT count(*) FROM efficiency_analysis_reservations;")
        Assert.Equal("3",scalar path "SELECT count(*) FROM efficiency_analysis_reservations WHERE state='unknown';")

    // Every positive below is an explicitly synthetic authenticated receiver fixture.
    // It proves joins, not real exec isolation, installed capability or native baseline.
    let private execThread = "11111111-1111-4111-8111-111111111111"
    let private execDigest = String.replicate 64 "a"
    let private execTurn (completed: bool) : JsonNode =
        let node = JsonNode.Parse($"""{{"localTurnKey":{{"captureSha256":"{execDigest}","threadId":"{execThread}","startFrameOrdinal":1}},"turnSequence":1,"nativeTurnId":null}}""")
        if completed then
            node.["status"] <- JsonValue.Create "completed"
            node.["usageAvailable"] <- JsonValue.Create true
        node

    let private execFixture (originVariant: string option) =
        let cleanup,path,pending = pendingFixture()
        Assert.Contains("\"rejected\":0",submit path "exec-dispatch" [analystDispatch])
        let claimed = TelemetryStoreApplication.efficiencyAnalysisClaimProspective path approved principal "analyst-dispatch" "A" (claimTemplate pending "exec-claim") |> unwrap |> JsonNode.Parse
        let admissionEvent = admission.Replace("admission-a","analyst-admission").Replace("invoke-a","analyst-invocation")
        let lineageEvent = """{"kind":"invocation-lineage","identity":"analyst-lineage","itemId":"A","revision":0,"dispatchId":"analyst","invocationId":"analyst-invocation","relation":"root","parentInvocationId":null,"rootInvocationId":"analyst-invocation","runtime":"codex-exec"}"""
        let startEvent = """{"kind":"runtime-start","identity":"exec-start","itemId":"A","revision":0,"invocationId":"analyst-invocation","threadId":null,"turnId":null,"turnSequence":null,"processId":42,"phase":"process"}"""
        let usageEvent = (usage 0).Replace("usage-a","exec-usage").Replace("invoke-a","analyst-invocation").Replace("thread-a",execThread).Replace("\"turnId\":\"turn-a\"","\"turnId\":null")
        Assert.Contains("\"rejected\":0",submit path "exec-started" [admissionEvent;lineageEvent;startEvent;usageEvent])
        let attach = JsonNode.Parse("""{"schema":"fsgg.telemetry.efficiency-analysis-attach-invocation-input/1","cas":null,"claimId":"exec-claim","dispatchRef":null,"invocationRef":"analyst-invocation","lineageRefs":[],"authority":null,"attachedAt":"2026-10-06T00:22:00Z"}""")
        attach.["cas"] <- JsonSerializer.SerializeToNode {| requestId=claimed.["requestId"].GetValue<string>(); expectedRevision=claimed.["revision"].GetValue<int64>(); expectedContentDigest=claimed.["contentDigest"].GetValue<string>() |}
        attach.["dispatchRef"] <- claimed.["dispatchRef"].DeepClone()
        attach.["authority"] <- pending.["canonicalRequest"].["authority"].DeepClone()
        for event in [admissionEvent;lineageEvent;startEvent] do attach.["lineageRefs"].AsArray().Add(sourceRef event)
        TelemetryStoreApplication.efficiencyAnalysis path approved principal "attach-invocation" (Encoding.UTF8.GetBytes(attach.ToJsonString())) None |> unwrap |> ignore
        let nativeScope = { scope with Producer="native-producer";Stream="native-stream" }
        let native : TelemetryReceipt.Principal = { Scope=nativeScope;Role=TelemetryReceipt.NativeCollector;GrantId=Some "exec-grant";GrantGeneration=Some 7L }
        TelemetryStoreApplication.enrollReceiptPrincipal path approved native |> unwrap |> ignore
        let now = DateTimeOffset.UtcNow
        let origin = JsonNode.Parse($"""{{"kind":"learn-installed-origin/1","identity":"exec-origin","revision":0,"workspaceId":"{scope.Workspace}","producerId":"{nativeScope.Producer}","streamId":"{nativeScope.Stream}","role":"native-collector","grantId":"exec-grant","grantGeneration":7,"managerReceiptSha256":"{execDigest}","capabilityProfileSha256":"{execDigest}","capabilityResultSha256":"{execDigest}","nativeCaptureSha256":"{execDigest}","nativeVerificationSha256":"{execDigest}","capabilityObservedAt":"{now.AddMinutes(-1.).ToString("O")}","capabilityExpiresAt":"{now.AddHours(1.).ToString("O")}","installationSha256":"{execDigest}"}}""")
        originVariant |> Option.iter(fun variant -> origin.["nativeSourceVariant"] <- JsonValue.Create variant)
        Assert.Contains("\"rejected\":0",submitAs native path "exec-origin" [origin.ToJsonString()])
        let inventory = JsonNode.Parse($"""{{"kind":"runtime-native-inventory/1","identity":"exec-inventory","itemId":"A","revision":0,"inventoryId":"exec-inventory","originalItemId":"A","invocationId":"analyst-invocation","page":1,"pages":1,"turnNamespace":"codex-exec-jsonl/1","expectedTurns":[],"expectedProvider":"openai","requestedModel":"sol","requestedEffort":"medium","support":"provider-native-final-turn-counters","followupBaseline":0,"capturedAt":"{now.ToString("O")}","sourceKind":"provider-capability-and-dispatch-roster","sourceDigest":"{execDigest}"}}""")
        inventory.["expectedTurns"].AsArray().Add(execTurn false)
        let binding = JsonNode.Parse($"""{{"schema":"fsgg.telemetry.native-inventory-source-binding/2","producerIdentity":"fsgg-work-roadmap-native-collector/1","capturedAt":"{now.ToString("O")}","hostSource":"codex-exec-jsonl","rootInvocationId":"analyst-invocation","invocationId":"analyst-invocation","revision":0,"threadId":"{execThread}","captureSha256":"{execDigest}","captureBytes":123,"commandBindingSha256":"{execDigest}","custodyReceiptSha256":"{execDigest}","claimRef":null,"turnRoster":[]}}""")
        binding.["dispatchRef"] <- sourceRef analystDispatch
        binding.["runtimeStartRef"] <- sourceRef startEvent
        binding.["installedOriginRef"] <- sourceRef (origin.ToJsonString())
        binding.["claimRef"] <- JsonSerializer.SerializeToNode {| requestId=claimed.["requestId"].GetValue<string>();claimId="exec-claim";revision=claimed.["revision"].GetValue<int64>();contentDigest=claimed.["contentDigest"].GetValue<string>();owner={|producer=scope.Producer;stream=scope.Stream|};generation=1 |}
        binding.["turnRoster"].AsArray().Add(execTurn true)
        cleanup,path,native,inventory,binding,usageEvent

    let private execSource (binding: JsonNode) =
        let bytes = CanonicalJson.canonicalize(Encoding.UTF8.GetBytes(binding.ToJsonString())) |> Result.defaultWith failwith |> Encoding.UTF8.GetBytes
        let source = JsonNode.Parse($"""{{"kind":"runtime-native-inventory-source/1","identity":"exec-source","itemId":"A","revision":0,"inventoryId":"exec-inventory","originalItemId":"A","invocationId":"analyst-invocation","sourceDigest":"{execDigest}","sourceBinding":{{"schema":"fsgg.telemetry.native-inventory-source-binding/2","producerIdentity":"fsgg-work-roadmap-native-collector/1","sha256":"{CanonicalJson.sha256 bytes}","bytesBase64":"{Convert.ToBase64String bytes}"}}}}""")
        source.ToJsonString()

    let private execAllocation (source: string) (usageEvent: string) =
        let node = allocation()
        node.["identity"] <- JsonValue.Create "exec-allocation"
        node.["resource"].["sourceRef"] <- sourceRef usageEvent
        node.["provenance"].["authorityRole"] <- JsonValue.Create "runtime-observer"
        node.["provenance"].["authorityRef"] <- sourceRef source
        node.["provenance"].["invocationRef"] <- JsonValue.Create "analyst-invocation"
        node.["shares"].[0].["evidenceRefs"].AsArray().Clear()
        node.["shares"].[0].["evidenceRefs"].AsArray().Add(sourceRef usageEvent)
        node.ToJsonString()

    [<Fact>]
    let ``synthetic exec source joins exact claimed history and existing nullable usage once`` () =
        let cleanup,path,native,inventory,binding,usageEvent = execFixture (Some "codex-exec-jsonl/1")
        use cleanup = cleanup
        let source = execSource binding
        Assert.Contains("\"rejected\":0",submitAs native path "exec-witnesses" [inventory.ToJsonString();source])
        let event = execAllocation source usageEvent
        Assert.Contains("\"rejected\":0",submit path "exec-classification" [event])
        Assert.Contains("\"rejected\":0",submit path "exec-classification-replay" [event])
        Assert.Equal("1",scalar path "SELECT count(*) FROM runtime_turn_usage WHERE invocation_id='analyst-invocation';")
        Assert.Equal("1",scalar path "SELECT count(*) FROM efficiency_records WHERE identity='exec-allocation';")
        Assert.Equal("1",scalar path "SELECT count(*) FROM runtime_turn_usage WHERE identity='exec-usage' AND turn_id IS NULL AND reasoning IS NULL AND turn_sequence=1 AND total=100;")

    [<Theory>]
    [<InlineData("owner")>]
    [<InlineData("generation")>]
    [<InlineData("digest")>]
    [<InlineData("attached-revision")>]
    [<InlineData("dispatch")>]
    [<InlineData("start")>]
    [<InlineData("origin")>]
    [<InlineData("roster")>]
    let ``synthetic exec receiver refuses mismatched history lineage or local roster`` (failure: string) =
        let cleanup,path,native,inventory,binding,usageEvent = execFixture (Some "codex-exec-jsonl/1")
        use cleanup = cleanup
        match failure with
        | "owner" -> binding.["claimRef"].["owner"].["producer"] <- JsonValue.Create "foreign"
        | "generation" -> binding.["claimRef"].["generation"] <- JsonValue.Create 2
        | "digest" -> binding.["claimRef"].["contentDigest"] <- JsonValue.Create("sha256:"+String.replicate 64 "b")
        | "attached-revision" ->
            let attached = JsonNode.Parse(scalar path "SELECT canonical FROM efficiency_analysis_requests;")
            binding.["claimRef"].["revision"] <- attached.["revision"].DeepClone()
            binding.["claimRef"].["contentDigest"] <- attached.["contentDigest"].DeepClone()
        | "dispatch" -> binding.["dispatchRef"].["id"] <- JsonValue.Create "missing-dispatch"
        | "start" -> binding.["runtimeStartRef"].["id"] <- JsonValue.Create "missing-start"
        | "origin" -> binding.["installedOriginRef"].["id"] <- JsonValue.Create "missing-origin"
        | "roster" -> inventory.["expectedTurns"].[0].["localTurnKey"].["startFrameOrdinal"] <- JsonValue.Create 2
        | _ -> failwith "unknown fixture"
        let source = execSource binding
        Assert.Contains("\"rejected\":0",submitAs native path "exec-witnesses" [inventory.ToJsonString();source])
        assertRejected path "exec-classification" (JsonNode.Parse(execAllocation source usageEvent))

    [<Fact>]
    let ``old AppServer origin never upgrades an exec inventory grant`` () =
        let cleanup,path,native,inventory,binding,usageEvent = execFixture None
        use cleanup = cleanup
        let source = execSource binding
        Assert.Contains("\"rejected\":0",submitAs native path "exec-witnesses" [inventory.ToJsonString();source])
        assertRejected path "exec-classification" (JsonNode.Parse(execAllocation source usageEvent))

    [<Theory>]
    [<InlineData(false)>]
    [<InlineData(true)>]
    let ``exec source without applied NativeCollector receipt remains unavailable`` (unapplied: bool) =
        let cleanup,path,native,inventory,binding,usageEvent = execFixture (Some "codex-exec-jsonl/1")
        use cleanup = cleanup
        let source = execSource binding
        if unapplied then
            let payload = Encoding.UTF8.GetString(batch "unapplied-exec" [inventory.ToJsonString();source])
            let ns = native.Scope
            let envelope = Encoding.UTF8.GetBytes($"""{{"schema":"{TelemetryReceipt.Schema}","workspaceId":"{ns.Workspace}","producerId":"{ns.Producer}","streamId":"{ns.Stream}","batchId":"unapplied-exec","payload":{payload}}}""")
            TelemetryStoreApplication.submitReceiptPrincipal path approved native envelope |> unwrap |> ignore
            Assert.Equal("0",scalar path "SELECT count(*) FROM ingest_facts WHERE identity='exec-source';")
        else
            Assert.Contains("\"rejected\":0",submit path "generic-exec-witnesses" [inventory.ToJsonString();source])
        assertRejected path "exec-classification" (JsonNode.Parse(execAllocation source usageEvent))

    [<Fact>]
    let ``second runtime identity for one exec local turn cannot duplicate its resource`` () =
        let cleanup,path,_,_,_,usageEvent = execFixture (Some "codex-exec-jsonl/1")
        use cleanup = cleanup
        let duplicate = usageEvent.Replace("exec-usage","duplicate-exec-usage")
        Assert.Contains("\"rejected\":1",submit path "duplicate-exec-turn" [duplicate])
        Assert.Equal("1",scalar path "SELECT count(*) FROM runtime_turn_usage WHERE invocation_id='analyst-invocation';")

    // Authenticated synthetic receipts exercise receiver joins only, not HTTP or installed enforcement.
    let private responseFixtureWithCounters (status: string) (cached: string) (output: string) (total: string) =
        let cleanup,path,pending = pendingFixture()
        let populationKey = CanonicalJson.sha256(Encoding.UTF8.GetBytes("A\u001fA")).Substring(0,32)
        let population = $"""{{"kind":"budget-population","identity":"budget-population-{populationKey}","itemId":"A","revision":0,"originalItemId":"A","state":"open","sourceKind":"native-item","sourceRef":"roadmap-dispatch:{populationKey}"}}"""
        let activation = """{"kind":"operational-activation","identity":"response-activation","itemId":"A","revision":0,"activationId":"activation-A","scope":"explicit-future-dispatches","runtime":"openai-responses","activatedAt":"2026-10-06T00:20:02Z","clockProvenance":"host-wall","lateAfterSeconds":60}"""
        let dispatchEvent = analystDispatch.Replace("codex-exec","openai-responses")
        Assert.Contains("\"rejected\":0",submit path "response-dispatch" [population;activation;dispatchEvent])
        let claimed = TelemetryStoreApplication.efficiencyAnalysisClaimProspective path approved principal "analyst-dispatch" "A" (claimTemplate pending "response-claim") |> unwrap |> JsonNode.Parse
        let admissionEvent = admission.Replace("admission-a","analyst-admission").Replace("invoke-a","analyst-invocation").Replace("codex-collaboration","openai-responses")
        let lineageEvent = """{"kind":"invocation-lineage","identity":"analyst-lineage","itemId":"A","revision":0,"dispatchId":"analyst","invocationId":"analyst-invocation","relation":"root","parentInvocationId":null,"rootInvocationId":"analyst-invocation","runtime":"openai-responses"}"""
        Assert.Contains("\"rejected\":0",submit path "response-admitted" [admissionEvent;lineageEvent])
        let now = DateTimeOffset.UtcNow
        let observationEvent = $"""{{"kind":"runtime-provider-observation/1","identity":"response-observation","itemId":"A","revision":0,"invocationId":"analyst-invocation","provider":"openai","sourceVariant":"openai-responses/1","responseId":"resp_actual_opaque","responseSha256":"{execDigest}","generationRequestSha256":"{execDigest}","countRequestSha256":"{execDigest}","countResponseSha256":"{execDigest}","observedAt":"{now.ToString("O")}","providerCreatedAt":null,"status":"{status}"}}"""
        let usageEvent = $"""{{"kind":"runtime-response-usage/1","identity":"response-usage","itemId":"A","revision":0,"invocationId":"analyst-invocation","provider":"openai","sourceVariant":"openai-responses/1","responseId":"resp_actual_opaque","responseSha256":"{execDigest}","requestedModel":"sol","observedModel":"sol","requestedEffort":"medium","observedEffort":null,"scope":"provider-response","provenance":"openai-responses","input":99,"cachedInput":{cached},"cacheWriteInput":null,"output":{output},"reasoning":null,"total":{total}}}"""
        let nativeScope = { scope with Producer="native-producer";Stream="native-stream" }
        let native : TelemetryReceipt.Principal = { Scope=nativeScope;Role=TelemetryReceipt.NativeCollector;GrantId=Some "responses-grant";GrantGeneration=Some 7L }
        TelemetryStoreApplication.enrollReceiptPrincipal path approved native |> unwrap |> ignore
        let origin = JsonNode.Parse($"""{{"kind":"learn-installed-origin/1","identity":"response-origin","revision":0,"workspaceId":"{scope.Workspace}","producerId":"{nativeScope.Producer}","streamId":"{nativeScope.Stream}","role":"native-collector","grantId":"responses-grant","grantGeneration":7,"managerReceiptSha256":"{execDigest}","capabilityProfileSha256":"{execDigest}","capabilityResultSha256":"{execDigest}","nativeCaptureSha256":"{execDigest}","nativeVerificationSha256":"{execDigest}","capabilityObservedAt":"{now.AddMinutes(-1.).ToString("O")}","capabilityExpiresAt":"{now.AddHours(1.).ToString("O")}","installationSha256":"{execDigest}"}}""")
        origin.["nativeSourceVariant"] <- JsonValue.Create "openai-responses/1"
        Assert.Contains("\"rejected\":0",submitAs native path "response-captured" [origin.ToJsonString();observationEvent;usageEvent])
        let attach = JsonNode.Parse("""{"schema":"fsgg.telemetry.efficiency-analysis-attach-invocation-input/1","cas":null,"claimId":"response-claim","dispatchRef":null,"invocationRef":"analyst-invocation","lineageRefs":[],"authority":null,"attachedAt":"2026-10-06T00:22:00Z"}""")
        attach.["cas"] <- JsonSerializer.SerializeToNode {| requestId=claimed.["requestId"].GetValue<string>(); expectedRevision=claimed.["revision"].GetValue<int64>(); expectedContentDigest=claimed.["contentDigest"].GetValue<string>() |}
        attach.["dispatchRef"] <- claimed.["dispatchRef"].DeepClone()
        attach.["authority"] <- pending.["canonicalRequest"].["authority"].DeepClone()
        for event in [admissionEvent;lineageEvent;observationEvent] do attach.["lineageRefs"].AsArray().Add(sourceRef event)
        TelemetryStoreApplication.efficiencyAnalysis path approved principal "attach-invocation" (Encoding.UTF8.GetBytes(attach.ToJsonString())) None |> unwrap |> ignore
        let inventory = JsonNode.Parse($"""{{"kind":"runtime-native-inventory/1","identity":"response-inventory","itemId":"A","revision":0,"inventoryId":"response-inventory","originalItemId":"A","invocationId":"analyst-invocation","page":1,"pages":1,"sourceVariant":"openai-responses/1","expectedResponses":[{{"responseId":"resp_actual_opaque","responseSha256":"{execDigest}"}}],"expectedProvider":"openai","requestedModel":"sol","requestedEffort":"medium","support":"response-native-final-counters","followupBaseline":0,"capturedAt":"{now.ToString("O")}","sourceKind":"provider-capability-and-dispatch-roster","sourceDigest":"{execDigest}"}}""")
        let binding = JsonNode.Parse($"""{{"schema":"fsgg.telemetry.native-inventory-source-binding/3","producerIdentity":"fsgg-work-roadmap-native-collector/1","capturedAt":"{now.ToString("O")}","sourceVariant":"openai-responses/1","originalItemId":"A","invocationId":"analyst-invocation","revision":0,"responseId":"resp_actual_opaque","generationRequestSha256":"{execDigest}","countRequestSha256":"{execDigest}","countResponseSha256":"{execDigest}","responseSha256":"{execDigest}","responseBytes":123,"operationBindingSha256":"{execDigest}","claimRef":null,"expectedResponses":[{{"responseId":"resp_actual_opaque","responseSha256":"{execDigest}"}}]}}""")
        binding.["dispatchRef"] <- sourceRef dispatchEvent
        binding.["providerObservationRef"] <- sourceRef observationEvent
        binding.["usageRef"] <- sourceRef usageEvent
        binding.["installedOriginRef"] <- sourceRef (origin.ToJsonString())
        binding.["claimRef"] <- JsonSerializer.SerializeToNode {| requestId=claimed.["requestId"].GetValue<string>();claimId="response-claim";revision=claimed.["revision"].GetValue<int64>();contentDigest=claimed.["contentDigest"].GetValue<string>();owner={|producer=scope.Producer;stream=scope.Stream|};generation=1 |}
        cleanup,path,native,inventory,binding,usageEvent

    let private responseFixture (status: string) = responseFixtureWithCounters status "0" "1" "100"

    let private responseIdentity (identity: string) (event: string) =
        let node = JsonNode.Parse event
        node.["identity"] <- JsonValue.Create identity
        node.ToJsonString()

    let private responseOutcome (identity: string) =
        // A second delivery must retain its own source identity, not collide with pendingFixture.
        let node = JsonNode.Parse sourceOutcome
        node.["identity"] <- JsonValue.Create identity
        node.["sourceRef"] <- JsonValue.Create("routine-delivery:" + identity)
        node.ToJsonString()

    let private responseSource (binding: JsonNode) =
        let bytes = CanonicalJson.canonicalize(Encoding.UTF8.GetBytes(binding.ToJsonString())) |> Result.defaultWith failwith |> Encoding.UTF8.GetBytes
        let source = JsonNode.Parse($"""{{"kind":"runtime-native-inventory-source/1","identity":"response-source","itemId":"A","revision":0,"inventoryId":"response-inventory","originalItemId":"A","invocationId":"analyst-invocation","sourceDigest":"{execDigest}","sourceBinding":{{"schema":"fsgg.telemetry.native-inventory-source-binding/3","producerIdentity":"fsgg-work-roadmap-native-collector/1","sha256":"{CanonicalJson.sha256 bytes}","bytesBase64":"{Convert.ToBase64String bytes}"}}}}""")
        source.ToJsonString()


    let private responseAllocation (source: string) (usageEvent: string) =
        let node = JsonNode.Parse(execAllocation source usageEvent)
        node.["identity"] <- JsonValue.Create "response-allocation"
        node.["resource"].["accountingScope"] <- JsonValue.Create "provider-response"
        node.ToJsonString()

    [<Fact>]
    let ``synthetic response capture joins claim and native origin without fabricated runtime turn`` () =
        let cleanup,path,native,inventory,binding,usageEvent = responseFixture "completed"
        use cleanup = cleanup
        let source = responseSource binding
        Assert.Contains("\"rejected\":0",submitAs native path "response-witnesses" [inventory.ToJsonString();source])
        let allocation = responseAllocation source usageEvent
        Assert.Contains("\"rejected\":0",submit path "response-allocation" [allocation])
        Assert.Contains("\"rejected\":0",submitAs native path "response-usage-replay" [usageEvent])
        Assert.Equal("0",scalar path "SELECT count(*) FROM runtime_turn_usage WHERE invocation_id='analyst-invocation';")
        Assert.Equal("1",scalar path "SELECT count(*) FROM current_ingest_facts WHERE kind='runtime-response-usage/1';")
        Assert.Equal("1",scalar path "SELECT count(*) FROM efficiency_records WHERE identity='response-allocation';")

    [<Theory>]
    [<InlineData("owner")>]
    [<InlineData("generation")>]
    [<InlineData("digest")>]
    [<InlineData("observation")>]
    [<InlineData("usage")>]
    [<InlineData("capture")>]
    [<InlineData("roster")>]
    let ``response classification refuses detached capture grant or claimed history`` (failure: string) =
        let cleanup,path,native,inventory,binding,usageEvent = responseFixture "completed"
        use cleanup = cleanup
        match failure with
        | "owner" -> binding.["claimRef"].["owner"].["producer"] <- JsonValue.Create "foreign"
        | "generation" -> binding.["claimRef"].["generation"] <- JsonValue.Create 2
        | "digest" -> binding.["claimRef"].["contentDigest"] <- JsonValue.Create("sha256:"+String.replicate 64 "b")
        | "observation" -> binding.["providerObservationRef"].["id"] <- JsonValue.Create "missing"
        | "usage" -> binding.["usageRef"].["id"] <- JsonValue.Create "missing"
        | "capture" -> binding.["operationBindingSha256"] <- JsonValue.Create(String.replicate 64 "b")
        | "roster" -> inventory.["expectedResponses"].[0].["responseSha256"] <- JsonValue.Create(String.replicate 64 "b")
        | _ -> failwith "unknown fixture"
        let source = responseSource binding
        Assert.Contains("\"rejected\":0",submitAs native path "response-witnesses" [inventory.ToJsonString();source])
        assertRejected path "response-classification" (JsonNode.Parse(responseAllocation source usageEvent))

    [<Fact>]
    let ``incomplete response actual costs cannot establish complete native population`` () =
        let cleanup,path,native,inventory,binding,usageEvent = responseFixture "incomplete"
        use cleanup = cleanup
        let source = responseSource binding
        Assert.Contains("\"rejected\":0",submitAs native path "response-witnesses" [inventory.ToJsonString();source])
        // A failed/incomplete operation still has genuine classifiable cost.
        Assert.Contains("\"rejected\":0",submit path "incomplete-response-classification" [responseAllocation source usageEvent])
        use compact = JsonDocument.Parse(TelemetryStoreApplication.compactDashboardSnapshot path approved None |> unwrap)
        use exported = JsonDocument.Parse(TelemetryStoreApplication.efficiencyExport path approved (compact.RootElement.GetProperty("revision").GetString()) 200 1000 |> unwrap)
        let cost = (exported.RootElement.GetProperty("items")).[0].GetProperty("metrics").EnumerateArray()
                   |> Seq.find (fun metric -> metric.GetProperty("metric").GetString()="cost-per-accepted")
        let value = cost.GetProperty "value"
        Assert.Equal("unknown",value.GetProperty("status").GetString())
        Assert.Equal(JsonValueKind.Null,value.GetProperty("numerator").ValueKind)
        Assert.Equal(JsonValueKind.Null,value.GetProperty("denominator").ValueKind)
        Assert.Equal("100",scalar path "SELECT json_extract(canonical,'$.total') FROM current_ingest_facts WHERE identity='response-usage';")

    [<Fact>]
    let ``response resource cannot acquire duplicate usage identity or generic runtime row`` () =
        let cleanup,path,native,_,_,usageEvent = responseFixture "completed"
        use cleanup = cleanup
        Assert.Contains("\"rejected\":1",submitAs native path "response-duplicate" [responseIdentity "duplicate-response" usageEvent])
        let genericUsage = (usage 0).Replace("usage-a","foreign-response-turn").Replace("invoke-a","analyst-invocation")
        Assert.Contains("\"rejected\":1",submit path "response-cross-namespace" [genericUsage])
        Assert.Equal("1",scalar path "SELECT count(*) FROM current_ingest_facts WHERE kind='runtime-response-usage/1';")

    [<Fact>]
    let ``Responses preclaim grant validation is read only and never upgrades generic principal`` () =
        let cleanup,path,native,_,_,_ = responseFixture "completed"
        use cleanup = cleanup
        let before = scalar path "SELECT count(*) FROM transport_receipts;"
        TelemetryStoreApplication.validateResponsesCollectorPrincipal path approved native |> unwrap
        Assert.True(TelemetryStoreApplication.validateResponsesCollectorPrincipal path approved principal |> Result.isError)
        Assert.True(TelemetryStoreApplication.validateResponsesCollectorPrincipal path approved {native with GrantGeneration=Some 8L} |> Result.isError)
        Assert.Equal(before,scalar path "SELECT count(*) FROM transport_receipts;")

    [<Fact>]
    let ``existing exec grant cannot be promoted to Responses preclaim capability`` () =
        let cleanup,path,native,_,_,_ = execFixture (Some "codex-exec-jsonl/1")
        use cleanup = cleanup
        Assert.True(TelemetryStoreApplication.validateResponsesCollectorPrincipal path approved native |> Result.isError)

    [<Fact>]
    let ``generic receipt cannot introduce response cost despite declared native source variant`` () =
        let cleanup,path,_,_,_,usageEvent = responseFixture "completed"
        use cleanup = cleanup
        Assert.Contains("\"rejected\":1",submit path "generic-response-cost" [responseIdentity "generic-response" usageEvent])
        Assert.Equal("1",scalar path "SELECT count(*) FROM current_ingest_facts WHERE kind='runtime-response-usage/1';")

    [<Fact>]
    let ``partial response public summary preserves unknown counters instead of reporting zero`` () =
        let cleanup,path,_,_,_,_ = responseFixtureWithCounters "incomplete" "null" "null" "null"
        use cleanup = cleanup
        use summary = JsonDocument.Parse(TelemetryStoreApplication.summary path approved "A" |> unwrap)
        let counters = summary.RootElement.GetProperty "usage"
        Assert.Equal(198L,counters.GetProperty("input").GetInt64())
        for name in ["output";"total";"cachedInput";"cacheWriteInput";"reasoning"] do
            Assert.Equal(JsonValueKind.Null,counters.GetProperty(name).ValueKind)
        use detail = JsonDocument.Parse(TelemetryStoreApplication.itemDetail path approved "A" |> unwrap)
        Assert.Equal(JsonValueKind.Null,detail.RootElement.GetProperty("accounting").GetProperty("nativeTotal").ValueKind)

    [<Fact>]
    let ``response budget reader counts one resource through nested aggregate callers`` () =
        let cleanup,path,_,_,_,_ = responseFixture "completed"
        use cleanup = cleanup
        // Existing sources have 100 tokens, response has 100; double projection would yield 300.
        use summary = JsonDocument.Parse(TelemetryStoreApplication.summary path approved "A" |> unwrap)
        Assert.Equal(200L,summary.RootElement.GetProperty("usage").GetProperty("total").GetInt64())
        Assert.Equal("200",scalar path "SELECT denominator FROM budget_attribution_facts WHERE item_id='A' AND dimension='model-usage' AND provider='openai' AND source_kind='runtime';")

    [<Fact>]
    let ``complete response usage has honest missing attribution until matched once`` () =
        let cleanup,path,_,_,_,_ = responseFixture "completed"
        use cleanup = cleanup
        use before = JsonDocument.Parse(TelemetryStoreApplication.itemDetail path approved "A" |> unwrap)
        Assert.Equal(2L,before.RootElement.GetProperty("accounting").GetProperty("missingAttribution").GetInt64())
        let attribution = """{"kind":"activity-usage-attribution","identity":"response-attribution","itemId":"A","revision":0,"usageIdentity":"response-usage","activityId":null,"classification":"unclassified","input":99,"cachedInput":0,"output":1,"reasoning":null,"total":100}"""
        Assert.Contains("\"rejected\":0",submit path "response-attribution" [attribution])
        Assert.Equal("100",scalar path "SELECT total FROM activity_usage_attributions WHERE usage_identity='response-usage';")
        Assert.Contains("\"rejected\":0",submit path "response-attribution-replay" [attribution])
        Assert.Equal("1",scalar path "SELECT count(*) FROM activity_usage_attributions WHERE usage_identity='response-usage';")
        use after = JsonDocument.Parse(TelemetryStoreApplication.itemDetail path approved "A" |> unwrap)
        Assert.Equal(1L,after.RootElement.GetProperty("accounting").GetProperty("missingAttribution").GetInt64())
        Assert.Equal(200L,after.RootElement.GetProperty("accounting").GetProperty("nativeTotal").GetInt64())

    [<Fact>]
    let ``genuine response completion closes receiver epoch with actual provider reference`` () =
        let cleanup,path,native,inventory,binding,_ = responseFixture "completed"
        use cleanup = cleanup
        Assert.Contains("\"rejected\":0",submitAs native path "response-witnesses" [inventory.ToJsonString();responseSource binding])
        let outcome = responseOutcome "outcome-response"
        Assert.Contains("\"rejected\":0",submit path "response-delivered" [outcome])
        Assert.Equal("routine-delivery:outcome-response",scalar path "SELECT source_ref FROM native_item_outcomes WHERE identity='outcome-response';")
        Assert.Equal("1",scalar path "SELECT count(*) FROM efficiency_outcome_epochs WHERE state='closed' AND outcome_identity='outcome-response';")
        Assert.Equal("0",scalar path "SELECT count(*) FROM runtime_terminals WHERE invocation_id='analyst-invocation';")
        let closing = scalar path "SELECT close_refs FROM efficiency_outcome_epochs WHERE outcome_identity='outcome-response';"
        Assert.Contains("runtime-provider-observation/1",closing)
        Assert.Contains("response-observation",closing)

    [<Fact>]
    let ``Responses prospective resolver uses actual owner dispatch and no fabricated start`` () =
        let cleanup,path,native,_,_,_ = responseFixture "completed"
        use cleanup = cleanup
        let actual = TelemetryStoreApplication.resolveResponsesCollectorDispatch path approved principal "analyst-dispatch" |> unwrap
        Assert.Equal("A",actual.OriginalItemId)
        Assert.Equal("analyst-invocation",actual.InvocationId)
        Assert.Equal("0",scalar path "SELECT count(*) FROM runtime_starts WHERE invocation_id='analyst-invocation';")
        Assert.True(TelemetryStoreApplication.resolveResponsesCollectorDispatch path approved native "analyst-dispatch" |> Result.isError)
        Assert.True(TelemetryStoreApplication.resolveResponsesCollectorDispatch path approved principal "missing" |> Result.isError)

    [<Fact>]
    let ``incomplete response witness cannot close a prospective native epoch`` () =
        let cleanup,path,native,inventory,binding,_ = responseFixture "incomplete"
        use cleanup = cleanup
        Assert.Contains("\"rejected\":0",submitAs native path "incomplete-witnesses" [inventory.ToJsonString();responseSource binding])
        Assert.Contains("\"rejected\":0",submit path "incomplete-delivered" [responseOutcome "outcome-incomplete"])
        Assert.Equal("routine-delivery:outcome-incomplete",scalar path "SELECT source_ref FROM native_item_outcomes WHERE identity='outcome-incomplete';")
        Assert.Equal("1",scalar path "SELECT count(*) FROM efficiency_outcome_epochs WHERE state='open';")
        Assert.Equal("0",scalar path "SELECT count(*) FROM efficiency_outcome_epochs WHERE state='closed';")

    [<Fact>]
    let ``partial response attribution refuses invented zero counters and preserves real input`` () =
        let cleanup,path,_,_,_,_ = responseFixtureWithCounters "incomplete" "null" "null" "null"
        use cleanup = cleanup
        let invented = """{"kind":"activity-usage-attribution","identity":"invented-response-attribution","itemId":"A","revision":0,"usageIdentity":"response-usage","activityId":null,"classification":"unclassified","input":99,"cachedInput":0,"output":0,"reasoning":null,"total":99}"""
        // Decoder-valid arithmetic reaches the receiver, which refuses unknown source counters.
        TelemetryStore.parseBatch(batch "invented-counter-shape" [invented]) |> unwrap |> ignore
        Assert.Contains("\"rejected\":1",submit path "invented-response-attribution" [invented])
        Assert.Equal("0",scalar path "SELECT count(*) FROM activity_usage_attributions WHERE usage_identity='response-usage';")
        Assert.Equal("99",scalar path "SELECT json_extract(canonical,'$.input') FROM current_ingest_facts WHERE identity='response-usage';")

    [<Fact>]
    let ``authentic over policy response cost stays visible but cannot close native epoch`` () =
        let cleanup,path,native,inventory,binding,_ = responseFixtureWithCounters "completed" "0" "1501" "1600"
        use cleanup = cleanup
        Assert.Contains("\"rejected\":0",submitAs native path "over-policy-witnesses" [inventory.ToJsonString();responseSource binding])
        Assert.Contains("\"rejected\":0",submit path "over-policy-delivered" [responseOutcome "outcome-over-policy"])
        Assert.Equal("routine-delivery:outcome-over-policy",scalar path "SELECT source_ref FROM native_item_outcomes WHERE identity='outcome-over-policy';")
        Assert.Equal("1",scalar path "SELECT count(*) FROM efficiency_outcome_epochs WHERE state='open';")
        Assert.Equal("0",scalar path "SELECT count(*) FROM efficiency_outcome_epochs WHERE state='closed';")
        Assert.Equal("1600",scalar path "SELECT json_extract(canonical,'$.total') FROM current_ingest_facts WHERE identity='response-usage';")
        use summary = JsonDocument.Parse(TelemetryStoreApplication.summary path approved "A" |> unwrap)
        Assert.Equal(1700L,summary.RootElement.GetProperty("usage").GetProperty("total").GetInt64())
