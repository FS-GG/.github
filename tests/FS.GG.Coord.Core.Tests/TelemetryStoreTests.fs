namespace FS.GG.Coord.Tests

open System
open System.Text
open System.IO
open System.Text.Json
open Xunit
open FS.GG.Coord

module TelemetryStoreTests =
    let private bytes (value: string) = Encoding.UTF8.GetBytes value

    let private causalFixture name =
        Path.Combine(__SOURCE_DIRECTORY__, "..", "learn-01-analysis", "fixtures", "causal-admission", name)

    let private causalBatch eventText =
        bytes $"""{{"schema":"{TelemetryStore.BatchSchema}","ingestId":"causal-fixture","sourceIdentity":"coordination","generation":"synthetic","cursor":"causal-fixture","eventCount":1,"events":[{eventText}]}}"""

    [<Fact>]
    let ``causal admission fixtures retain exact decoded bytes and distinct retry annotation`` () =
        use manifest = JsonDocument.Parse(File.ReadAllBytes(causalFixture "manifest.json"))
        let cases = manifest.RootElement.GetProperty("cases").EnumerateArray() |> Seq.toArray
        Assert.Equal(4, manifest.RootElement.GetProperty("caseCount").GetInt32())
        Assert.Equal(4, cases.Length)
        for item in cases do
            let raw = File.ReadAllBytes(causalFixture(item.GetProperty("admissionFile").GetString()))
            let eventBytes = File.ReadAllBytes(causalFixture(item.GetProperty("eventFile").GetString()))
            Assert.Equal(item.GetProperty("admissionBytes").GetInt32(), raw.Length)
            Assert.Equal(item.GetProperty("admissionSha256").GetString(), CanonicalJson.sha256 raw)
            Assert.Equal(item.GetProperty("eventBytes").GetInt32(), eventBytes.Length)
            Assert.Equal(item.GetProperty("eventSha256").GetString(), CanonicalJson.sha256 eventBytes)
            let parsed =
                TelemetryStore.parseBatch (causalBatch (Encoding.UTF8.GetString(eventBytes).Trim()))
                |> Result.defaultWith (sprintf "%A" >> failwith)
            match parsed.Facts with
            | [ { Payload = TelemetryStore.ExecutionCausalAdmission evidence } ] ->
                Assert.Equal(Convert.ToBase64String raw, evidence.AdmissionBase64)
                Assert.Equal(item.GetProperty("invocationId").GetString(), evidence.Admission.InvocationId)
                if item.GetProperty("name").GetString() = "explicit-retry" then
                    Assert.Equal(evidence.Admission.ParentInvocationId, evidence.Admission.Declaration.RetryOfInvocationId)
            | _ -> Assert.Fail("causal fixture did not decode to one declaration")

    [<Fact>]
    let ``causal admission refuses changed bytes identity and shape before ingestion`` () =
        let root = File.ReadAllText(causalFixture "root.event.json").Trim()
        let node = System.Text.Json.Nodes.JsonNode.Parse root
        let refuse (value: string) = Assert.True(TelemetryStore.parseBatch (causalBatch value) |> Result.isError)
        refuse (root.Replace("\"revision\":0", "\"revision\":1"))
        refuse (root.Replace("\"kind\":\"execution-causal-admission/1\"", "\"kind\":\"unknown/1\""))
        refuse (root.Replace("\"admissionSha256\":\"", "\"admissionSha256\":\"0"))
        refuse (root.Replace("\"dispatchId\":\"", "\"dispatchId\":\"other-"))
        refuse (root.Replace("\"revision\":0", "\"revision\":0,\"revision\":0"))
        node["admissionBase64"] <- node["admissionBase64"].GetValue<string>() + " "
        refuse (node.ToJsonString())

    [<Fact>]
    let ``causal admission enforces exact byte and dependency edge boundaries`` () =
        let original = File.ReadAllBytes(causalFixture "root.admission.json")
        let eventText = File.ReadAllText(causalFixture "root.event.json")
        let eventFor (raw: byte array) =
            let event = System.Text.Json.Nodes.JsonNode.Parse eventText
            event["admissionBase64"] <- Convert.ToBase64String raw
            event["admissionSha256"] <- CanonicalJson.sha256 raw
            event.ToJsonString()
        let accepts raw = TelemetryStore.parseBatch (causalBatch (eventFor raw)) |> Result.isOk
        let rejects raw = TelemetryStore.parseBatch (causalBatch (eventFor raw)) |> Result.isError
        let padded size = Array.append original (Array.create (size - original.Length) 0x20uy)
        Assert.True(accepts (padded 4096))
        Assert.True(rejects (padded 4097))
        let edges count duplicate self =
            let dto = System.Text.Json.Nodes.JsonNode.Parse(Encoding.UTF8.GetString original)
            let declaration = dto["declaration"].AsObject()
            let values = declaration["dependencies"].AsArray()
            for index in 0 .. count - 1 do
                let endpoint =
                    if self then dto["invocationId"].GetValue<string>()
                    elif duplicate then "invocation-external-0"
                    else $"invocation-external-{index}"
                values.Add(System.Text.Json.Nodes.JsonNode.Parse(
                    $"""{{"originalItemId":"LEARN-01.2-SYNTHETIC","invocationId":"{endpoint}","sourceReference":"synthetic"}}"""))
            Encoding.UTF8.GetBytes(dto.ToJsonString())
        let sixteen = edges 16 false false
        let seventeen = edges 17 false false
        Assert.True(sixteen.Length <= 4096 && seventeen.Length <= 4096)
        Assert.True(accepts sixteen)
        Assert.True(rejects seventeen)
        Assert.True(rejects (edges 2 true false))
        Assert.True(rejects (edges 1 false true))
        let unknown = System.Text.Json.Nodes.JsonNode.Parse(Encoding.UTF8.GetString original)
        unknown["unrecognized"] <- true
        Assert.True(rejects (Encoding.UTF8.GetBytes(unknown.ToJsonString())))

    let private validBatch ingest identity revision : byte array =
        bytes
            $"""{{"schema":"%s{TelemetryStore.BatchSchema}","ingestId":"%s{ingest}","sourceIdentity":"worker-a","generation":"g1","cursor":"c1","eventCount":1,"events":[{{"kind":"usage","identity":"%s{identity}","itemId":"UTEL-02","revision":%d{revision},"provider":"OpenAI","model":"sol","effort":"medium","input":10,"cachedInput":2,"cacheWriteInput":1,"output":5,"reasoning":2,"total":15,"responses":1,"sessions":1,"turns":1}}]}}"""

    [<Fact>]
    let ``UTEL-02 typed batches enforce closed fields counts arithmetic and native identity`` () =
        let accepted = TelemetryStore.parseBatch (validBatch "batch-1" "usage-1" 0L)
        Assert.True(Result.isOk accepted)

        let unknown =
            Encoding.UTF8
                .GetString(validBatch "batch-1" "usage-1" 0L)
                .Replace("\"turns\":1", "\"turns\":1,\"privatePath\":\"/secret\"")
            |> bytes

        Assert.Contains("unknown field", sprintf "%A" (TelemetryStore.parseBatch unknown))

        let mismatched =
            Encoding.UTF8.GetString(validBatch "batch-1" "usage-1" 0L).Replace("\"eventCount\":1", "\"eventCount\":2")
            |> bytes

        Assert.Contains("eventCount", sprintf "%A" (TelemetryStore.parseBatch mismatched))

        let overflow =
            Encoding.UTF8
                .GetString(validBatch "batch-1" "usage-1" 0L)
                .Replace("\"input\":10", "\"input\":9223372036854775807")
            |> bytes

        Assert.Contains("overflows", sprintf "%A" (TelemetryStore.parseBatch overflow))

        Assert.True(
            TelemetryStore.parseBatch (Array.zeroCreate (TelemetryStore.MaxBatchBytes + 1))
            |> Result.isError
        )

    [<Fact>]
    let ``UTEL-02 public reduction keeps unknown coverage separate from qualification`` () =
        let batch =
            TelemetryStore.parseBatch (validBatch "batch-1" "usage-1" 0L)
            |> Result.defaultWith (sprintf "%A" >> failwith)

        let summary =
            TelemetryStore.reduce "UTEL-02" batch.Facts
            |> Result.defaultWith (sprintf "%A" >> failwith)

        Assert.Equal("unknown", summary.PopulationCoverage)
        Assert.Equal("not-evaluated", summary.Qualification)
        Assert.Equal(15L, summary.Total)
        Assert.DoesNotContain("worker-a", TelemetryStore.publicJson summary)

    [<Fact>]
    let ``UTEL-02 durability assessment fails closed`` () =
        Assert.True(
            TelemetryStore.validateStoreRoot "relative" TelemetryStore.ApprovedLocalDurable
            |> Result.isError
        )

        Assert.Contains(
            "unsafe",
            sprintf
                "%A"
                (TelemetryStore.validateStoreRoot "/durable/store" (TelemetryStore.Unsafe "network filesystem"))
        )

        Assert.Contains(
            "durability-unverified",
            sprintf
                "%A"
                (TelemetryStore.validateStoreRoot "/durable/store" (TelemetryStore.DurabilityUnverified "unknown mount"))
        )

    [<Fact>]
    let ``UTEL-02 batch observation count is capped at 64`` () =
        let events =
            [ 0..64 ]
            |> List.map (fun index ->
                $"""{{"kind":"item","identity":"item-%d{index}","itemId":"UTEL-02","revision":0}}""")
            |> String.concat ","

        let payload =
            bytes
                $"""{{"schema":"%s{TelemetryStore.BatchSchema}","ingestId":"batch-1","sourceIdentity":"worker-a","generation":"g1","cursor":"c1","eventCount":65,"events":[%s{events}]}}"""

        Assert.Contains("64 events", sprintf "%A" (TelemetryStore.parseBatch payload))

    [<Fact>]
    let ``installed origin is a closed native role fact and selector hashes are evidence`` () =
        let digest = String.replicate 64 "a"
        let fact role extra =
            bytes
                $"""{{"schema":"{TelemetryStore.BatchSchema}","ingestId":"installed-origin-1","sourceIdentity":"protected-installed-origin","generation":"7","cursor":"c1","eventCount":1,"events":[{{"kind":"learn-installed-origin/1","identity":"origin-1","revision":0,"workspaceId":"workspace-1","producerId":"producer-1","streamId":"stream-1","role":"{role}","grantId":"grant-1","grantGeneration":7,"managerReceiptSha256":"{digest}","capabilityProfileSha256":"{digest}","capabilityResultSha256":"{digest}","nativeCaptureSha256":"{digest}","nativeVerificationSha256":"{digest}","capabilityObservedAt":"2026-10-01T10:00:00Z","capabilityExpiresAt":"2026-10-01T10:05:00Z","installationSha256":"{digest}"{extra}}}]}}"""
        match TelemetryStore.parseBatch (fact "native-collector" "") with
        | Ok { Facts = [ { Payload = TelemetryStore.LearnInstalledOrigin(_, _, _, role, _, generation, _, _, _, _, _, observed, expires, _) } ] } ->
            Assert.Equal("native-collector", role)
            Assert.Equal(7L, generation)
            Assert.Equal("2026-10-01T10:00:00Z", observed)
            Assert.Equal("2026-10-01T10:05:00Z", expires)
        | value -> Assert.Fail($"unexpected parse result: {value}")
        Assert.True(TelemetryStore.parseBatch (fact "generic" "") |> Result.isError)
        Assert.Contains("unknown field", sprintf "%A" (TelemetryStore.parseBatch (fact "native-collector" ",\"callerHash\":\"self-authored\"")))

    [<Fact>]
    let ``UTEL-08 review activity attribution and complication facts are closed and bounded`` () =
        let digest = String.replicate 64 "a"

        let review =
            $"""{{"kind":"process-review","identity":"review-1","itemId":"UTEL-08","revision":1,"scope":"attempt","attemptId":"attempt-1","outcomeSynopsis":"Delivered","wentWell":["Focused checks"],"problems":[],"avoidableDelayOrRework":[],"processObservations":["Routine route held"],"remainingRisks":[],"concreteImprovements":["Keep the focused gate"],"evidence":[{{"kind":"test","digest":"{digest}"}}],"evidenceCoverage":"partial","populationCoverage":"complete","confidence":"high","reviewerModel":"gpt-5","reviewerEffort":"medium","reviewedAt":"2026-09-09T09:00:00Z","durationSeconds":30}}"""

        let activity =
            $"""{{"kind":"activity-span","identity":"span-1","itemId":"UTEL-08","revision":0,"activityId":"implementation-1","invocationId":"invoke-1","attemptId":"attempt-1","category":"implementation","startedAt":"2026-09-09T08:00:00Z","endedAt":null,"clockProvenance":"host-wall","evidence":[],"summary":"implementation"}}"""

        let attribution =
            """{"kind":"activity-usage-attribution","identity":"attribute-1","itemId":"UTEL-08","revision":0,"usageIdentity":"usage-1","activityId":null,"classification":"unclassified","input":10,"cachedInput":2,"output":5,"reasoning":null,"total":15}"""

        let complication =
            $"""{{"kind":"complication","identity":"complication-1","itemId":"UTEL-08","revision":0,"attemptId":"attempt-1","activityId":"implementation-1","trigger":"test-failure","cause":"product-defect","occurredAt":"2026-09-09T08:30:00Z","synopsis":"A focused test exposed a defect","evidence":[{{"kind":"test","digest":"{digest}"}}]}}"""

        let payload events =
            bytes
                $"""{{"schema":"{TelemetryStore.BatchSchema}","ingestId":"review-batch","sourceIdentity":"reviewer","generation":"g1","cursor":"1","eventCount":{List.length events},"events":[{String.concat "," events}]}}"""

        Assert.True(
            TelemetryStore.parseBatch (payload [ review; activity; attribution; complication ])
            |> Result.isOk
        )

        Assert.Contains(
            "unknown field",
            sprintf
                "%A"
                (TelemetryStore.parseBatch (
                    payload
                        [
                            review.Replace("\"durationSeconds\":30", "\"durationSeconds\":30,\"prompt\":\"secret\"")
                        ]
                ))
        )

        Assert.Contains(
            "exceeds 8 entries",
            sprintf
                "%A"
                (TelemetryStore.parseBatch (
                    payload
                        [
                            review.Replace(
                                "[\"Focused checks\"]",
                                "[\"1\",\"2\",\"3\",\"4\",\"5\",\"6\",\"7\",\"8\",\"9\"]"
                            )
                        ]
                ))
        )

        Assert.Contains(
            "classification",
            sprintf
                "%A"
                (TelemetryStore.parseBatch (
                    payload
                        [
                            attribution.Replace("\"classification\":\"unclassified\"", "\"classification\":\"direct\"")
                        ]
                ))
        )

    let private suiteScope =
        let digest = String('d', 64)
        let sha = String('a', 40)
        $"""{{"collectionId":"collection-1","generation":1,"repository":"o/r","repositoryId":1,"pullRequest":2,"baseRef":"main","baseSha":"{sha}","head":"{sha}","assignmentDigest":"{digest}","profileDigest":"{digest}","parserContract":"provider-checkpoint/2","producerStream":"coordination"}}"""

    let private suitePageEvent =
        let digest = String('d', 64)
        $"""{{"kind":"ci-suite-page/1","identity":"suite-page-1","itemId":"UTEL-06.3","revision":1,"scope":{suiteScope},"resourceKind":"suites","suiteId":null,"page":1,"nativeCount":1,"nativeTotal":1,"nextPage":null,"bodyDigest":"{digest}","observedAt":"2026-10-12T00:00:00Z","validatedAt":"2026-10-12T00:00:01Z","fragmentIndex":0,"fragmentCount":1,"rowOffset":0,"rows":[{{"suiteId":3,"appId":4,"appSlug":"github-actions","status":"completed","conclusion":"success"}}]}}"""

    [<Fact>]
    let ``suite page closed wire retains exact scope and inventory rows`` () =
        match TelemetryStore.parseBatch (causalBatch suitePageEvent) with
        | Ok batch ->
            match batch.Facts.Head.Payload with
            | TelemetryStore.CiSuitePage page ->
                Assert.Equal<string>("collection-1", page.Scope.CollectionId)
                Assert.Equal<int64>(1L, page.Scope.Generation)
                Assert.Equal<int64>(1L, page.NativeCount)
                match page.Rows with
                | TelemetryStore.SuiteInventoryRows [ row ] ->
                    Assert.Equal<int64>(3L, row.SuiteId)
                    Assert.Equal<int64>(4L, row.AppId)
                | _ -> Assert.Fail "expected exact single suite inventory row"
            | _ -> Assert.Fail "expected suite page payload"
        | Error errors -> Assert.Fail(String.concat ";" errors)

    [<Fact>]
    let ``suite page refuses changed shape resource correspondence and fragment bounds`` () =
        let refused event = Assert.True(TelemetryStore.parseBatch (causalBatch event) |> Result.isError)
        refused (suitePageEvent.Replace("\"fragmentIndex\":0", "\"fragmentIndex\":1"))
        refused (suitePageEvent.Replace("\"rowOffset\":0", "\"rowOffset\":1"))
        refused (suitePageEvent.Replace("\"nativeCount\":1", "\"nativeCount\":101"))
        refused (suitePageEvent.Replace("\"suiteId\":null", "\"suiteId\":3"))
        refused (suitePageEvent.Replace("\"resourceKind\":\"suites\"", "\"resourceKind\":\"foreign\""))
        refused (suitePageEvent.Replace("\"bodyDigest\":", "\"unknownField\":true,\"bodyDigest\":"))
        refused (suitePageEvent.Replace("\"fragmentCount\":1", "\"fragmentCount\":1,\"fragmentCount\":1"))
        refused (suitePageEvent.Replace("2026-10-12T00:00:01Z", "2026-10-11T23:59:59Z"))

    [<Fact>]
    let ``suite empty page requires explicit zero proof without invented continuation`` () =
        let empty =
            suitePageEvent
            |> fun value -> value.Replace("\"nativeCount\":1", "\"nativeCount\":0")
            |> fun value -> value.Replace("\"nativeTotal\":1", "\"nativeTotal\":0")
            |> fun value -> value.Replace("[{\"suiteId\":3,\"appId\":4,\"appSlug\":\"github-actions\",\"status\":\"completed\",\"conclusion\":\"success\"}]", "[]")
        Assert.True(TelemetryStore.parseBatch (causalBatch empty) |> Result.isOk)
        Assert.True(TelemetryStore.parseBatch (causalBatch (empty.Replace("\"nextPage\":null", "\"nextPage\":2"))) |> Result.isError)
        Assert.True(TelemetryStore.parseBatch (causalBatch (empty.Replace("\"fragmentCount\":1", "\"fragmentCount\":2"))) |> Result.isError)

    [<Fact>]
    let ``suite inventory accepts1001 suites while check retention remains1000`` () =
        let inventory = suitePageEvent.Replace("\"nativeTotal\":1", "\"nativeTotal\":1001").Replace("\"nextPage\":null", "\"nextPage\":2")
        Assert.True(TelemetryStore.parseBatch (causalBatch inventory) |> Result.isOk)
        let checkPage =
            inventory.Replace("\"resourceKind\":\"suites\"", "\"resourceKind\":\"suite-checks\"")
                     .Replace("\"suiteId\":null", "\"suiteId\":3")
        Assert.True(TelemetryStore.parseBatch (causalBatch checkPage) |> Result.isError)
        Assert.True(TelemetryStore.parseBatch (causalBatch (inventory.Replace("\"nativeTotal\":1001", "\"nativeTotal\":4097"))) |> Result.isError)

    let private parsedSuitePage () =
        match TelemetryStore.parseBatch (causalBatch suitePageEvent) with
        | Ok batch ->
            match batch.Facts.Head.Payload with
            | TelemetryStore.CiSuitePage page -> page
            | _ -> failwith "expected suite page"
        | Error errors -> failwith (String.concat ";" errors)

    [<Fact>]
    let ``typed suite graph digest sorts pages but preserves native row and observation facts`` () =
        let first = parsedSuitePage ()
        let second =
            { first with
                Page = 2L
                BodyDigest = String('e', 64)
            }
        let expected = TelemetryStore.ciSuiteGraphDigest [ first; second ]
        Assert.Equal<string>(expected, TelemetryStore.ciSuiteGraphDigest [ second; first ])
        let changes: TelemetryStore.CiSuitePageProof list =
            [
                { second with NativeTotal = 1001L }
                { second with NextPage = Some 3L }
                { second with ValidatedAt = "2026-10-12T00:00:02Z" }
                { second with Scope = { second.Scope with Generation = 2L } }
                { second with FragmentCount = 2L }
            ]
        for changed in changes do
            Assert.NotEqual<string>(expected, TelemetryStore.ciSuiteGraphDigest [ first; changed ])
        Assert.NotEqual<string>(expected, TelemetryStore.ciSuiteGraphDigest [ first; second; second ])

    [<Fact>]
    let ``typed suite graph digest binds exact canonical check reference and option distinctions`` () =
        let inventory = parsedSuitePage ()
        let checkReference: TelemetryStore.CiSuiteProofReference =
            {
                Identity = "check-5"
                Revision = 1L
                Digest = String('d', 64)
            }
        let check: TelemetryStore.CiSuiteCheckMembershipRow =
            {
                CheckId = 5L
                AppId = 4L
                AppSlug = Some "github-actions"
                CheckRef = checkReference
            }
        let membership =
            { inventory with
                SuiteId = Some 3L
                Rows = TelemetryStore.SuiteCheckMembershipRows [ check ]
            }
        let expected = TelemetryStore.ciSuiteGraphDigest [ inventory; membership ]
        for changed in
            [
                { check with CheckRef = { checkReference with Revision = 2L } }
                { check with CheckRef = { checkReference with Digest = String('e', 64) } }
                { check with CheckRef = { checkReference with Identity = "check-6" } }
                { check with AppSlug = None }
                { check with AppSlug = Some "" }
                { check with AppId = 5L }
            ] do
            let page =
                { membership with
                    Rows = TelemetryStore.SuiteCheckMembershipRows [ changed ]
                }
            Assert.NotEqual<string>(expected, TelemetryStore.ciSuiteGraphDigest [ inventory; page ])
        let absent =
            { membership with
                Rows = TelemetryStore.SuiteCheckMembershipRows [ { check with AppSlug = None } ]
            }
        let empty =
            { membership with
                Rows = TelemetryStore.SuiteCheckMembershipRows [ { check with AppSlug = Some "" } ]
            }
        Assert.NotEqual<string>(TelemetryStore.ciSuiteGraphDigest [ absent ], TelemetryStore.ciSuiteGraphDigest [ empty ])

    let private completePureSuiteGraph () =
        let page = parsedSuitePage ()
        let scope = page.Scope
        let fact identity kind payload: TelemetryStore.Fact =
            { Identity = identity; ItemId = Some "UTEL-06.3"; Revision = 1L; Kind = kind
              Payload = payload; Canonical = "{}"; ContentDigest = String('d', 64) }
        let reference (value: TelemetryStore.Fact): TelemetryStore.CiSuiteProofReference =
            { Identity = value.Identity; Revision = value.Revision; Digest = value.ContentDigest }
        let admission = fact "admission" "ci-population-admission" (TelemetryStore.CiPopulationAdmission(scope.CollectionId, scope.Repository, scope.PullRequest, scope.BaseRef, scope.BaseSha, scope.Head, "native-pr-head"))
        let binding = fact "binding" "ci-binding" (TelemetryStore.CiBinding(scope.CollectionId, scope.Repository, scope.Head, scope.PullRequest, "*", "UTEL", "attempt", None, scope.ProducerStream, "native-pr-head"))
        let coverage = fact "coverage" "ci-coverage" (TelemetryStore.CiCoverage(scope.CollectionId, "partial", "partial", "partial", "partial", "partial", "unknown", "unknown", "unknown"))
        let population = fact "population" "ci-population-coverage" (TelemetryStore.CiPopulationCoverage(scope.CollectionId, "partial", "partial", "partial", "partial", "partial", "partial", "pending", 0L, "[]"))
        let inventory = fact "inventory" "ci-suite-page/1" (TelemetryStore.CiSuitePage page)
        let empty =
            { page with SuiteId = Some 3L; NativeCount = 0L; NativeTotal = 0L
                        Rows = TelemetryStore.SuiteCheckMembershipRows [] }
        let membership = fact "membership" "ci-suite-page/1" (TelemetryStore.CiSuitePage empty)
        let seal: TelemetryStore.CiSuiteSealProof =
            { Scope = scope; AdmissionRef = reference admission; BindingRef = reference binding
              CoverageRef = reference coverage; PopulationCoverageRef = reference population
              GraphDigest = TelemetryStore.ciSuiteFactGraphDigest [ inventory; membership ]
              ResourceCount = 2L; FragmentCount = 2L; SuiteCount = 1L; CheckCount = 0L
              FirstSweepDigest = String('d', 64); SecondSweepDigest = String('d', 64)
              BarrierDigest = String('e', 64); ObservedFrom = page.ObservedAt; ObservedThrough = page.ValidatedAt }
        seal, [ admission; binding; coverage; population; inventory; membership ]

    [<Fact>]
    let ``suite graph validates exact complete inventory with explicit zero check family`` () =
        let seal, facts = completePureSuiteGraph ()
        Assert.True(TelemetryStore.validateCiSuiteGraph "UTEL-06.3" seal facts |> Result.isOk)
        Assert.Equal<string>(seal.GraphDigest, TelemetryStore.ciSuiteFactGraphDigest (facts |> List.filter (fun fact -> fact.Kind = "ci-suite-page/1") |> List.rev))

    [<Fact>]
    let ``suite graph refuses missing duplicate foreign or changed current facts`` () =
        let seal, facts = completePureSuiteGraph ()
        let refuses values = Assert.True(TelemetryStore.validateCiSuiteGraph "UTEL-06.3" seal values |> Result.isError)
        refuses (facts |> List.filter (fun fact -> fact.Identity <> "membership"))
        refuses (facts @ [ facts.Head ])
        refuses (facts |> List.map (fun fact -> if fact.Identity = "admission" then { fact with Revision = 2L } else fact))
        refuses (facts |> List.map (fun fact -> if fact.Identity = "population" then { fact with ItemId = Some "foreign" } else fact))
        let changed =
            facts |> List.map (fun fact ->
                match fact.Payload with
                | TelemetryStore.CiSuitePage page when fact.Identity = "membership" ->
                    { fact with Payload = TelemetryStore.CiSuitePage { page with Scope = { page.Scope with Head = String('b', 40) } } }
                | _ -> fact)
        refuses changed

    [<Fact>]
    let ``suite graph cannot use digest equality to waive native chain and roster bounds`` () =
        let seal, facts = completePureSuiteGraph ()
        let changed =
            facts |> List.map (fun fact ->
                match fact.Payload with
                | TelemetryStore.CiSuitePage page when fact.Identity = "inventory" ->
                    { fact with Payload = TelemetryStore.CiSuitePage { page with NativeTotal = 2L } }
                | _ -> fact)
        let mutated = { seal with GraphDigest = TelemetryStore.ciSuiteFactGraphDigest (changed |> List.filter (fun fact -> fact.Kind = "ci-suite-page/1")) }
        Assert.True(TelemetryStore.validateCiSuiteGraph "UTEL-06.3" mutated changed |> Result.isError)
        Assert.True(TelemetryStore.validateCiSuiteGraph "UTEL-06.3" { seal with SuiteCount = 2L } facts |> Result.isError)
        Assert.True(TelemetryStore.validateCiSuiteGraph "UTEL-06.3" { seal with FirstSweepDigest = String('e', 64) } facts |> Result.isError)
    let private passiveEvent () =
        let thread = "11111111-1111-1111-1111-111111111111"
        let turn = "22222222-2222-2222-2222-222222222222"
        let identity = "passive-" + CanonicalJson.sha256(bytes ("codex-rollout/1\u001f" + thread + "\u001f" + turn + "\u001fresponse-a"))
        System.Text.Json.Nodes.JsonNode.Parse(
            $"""{{"kind":"provider-usage-observation/1","identity":"{identity}","itemId":null,"revision":1,"threadId":"{thread}","turnId":"{turn}","responseId":"response-a","provider":null,"model":null,"effort":null,"input":10,"cachedInput":9,"cacheWriteInput":8,"output":5,"reasoning":2,"total":15,"sourceInstance":"{String('a',64)}","sourceGeneration":"source-1","sourceDigest":"{String('b',64)}","position":0,"capturedAt":"2026-10-18T07:00:00Z","occurredAt":null,"provenance":"codex-local-rollout/1","populationCoverage":"partial","attributionCoverage":"unknown","inventoryCoverage":"unknown","terminalCoverage":"unknown"}}""")

    [<Fact>]
    let ``passive Claude transcript provenance owns its own native identity namespace`` () =
        let event = passiveEvent ()
        let thread, turn = event["threadId"].GetValue<string>(), event["turnId"].GetValue<string>()
        let claudeIdentity = "passive-" + CanonicalJson.sha256(bytes ("claude-transcript/1\u001f" + thread + "\u001f" + turn + "\u001fresponse-a"))
        let parse (node: System.Text.Json.Nodes.JsonNode) = TelemetryStore.parseBatch(causalBatch (node.ToJsonString()))
        event["provenance"] <- System.Text.Json.Nodes.JsonValue.Create "claude-local-transcript/1"
        // Causal control: the same fact under the Codex namespace is refused once provenance says Claude.
        Assert.True(parse event |> Result.isError)
        event["identity"] <- System.Text.Json.Nodes.JsonValue.Create claudeIdentity
        Assert.True(parse event |> Result.isOk)
        event["provenance"] <- System.Text.Json.Nodes.JsonValue.Create "claude-statusline/1"
        Assert.True(parse event |> Result.isError)

    [<Fact>]
    let ``passive response is partial unassigned and preserves independent cache counters`` () =
        let event = passiveEvent ()
        let parsed = TelemetryStore.parseBatch(causalBatch (event.ToJsonString())) |> Result.defaultWith (sprintf "%A" >> failwith)
        let fact = List.exactlyOne parsed.Facts
        Assert.Equal(None, fact.ItemId)
        match fact.Payload with
        | TelemetryStore.PassiveProviderUsage value ->
            Assert.Equal(Some 10L, value.Input)
            Assert.Equal(Some 9L, value.CachedInput)
            Assert.Equal(Some 8L, value.CacheWriteInput)
            Assert.Equal(None, value.Model)
            Assert.Equal(None, value.OccurredAt)
        | _ -> Assert.Fail("passive counters impersonated another accounting kind")
        event["input"] <- null
        event["cachedInput"] <- null
        event["cacheWriteInput"] <- null
        Assert.True(TelemetryStore.parseBatch(causalBatch (event.ToJsonString())) |> Result.isOk)

    [<Fact>]
    let ``passive parser refuses manufactured attribution completeness and invalid dimensions`` () =
        let reject (name: string) (value: System.Text.Json.Nodes.JsonNode) =
            let event = passiveEvent ()
            event[name] <- value
            Assert.True(TelemetryStore.parseBatch(causalBatch (event.ToJsonString())) |> Result.isError, name)
        for name in [ "populationCoverage"; "attributionCoverage"; "inventoryCoverage"; "terminalCoverage" ] do
            reject name (System.Text.Json.Nodes.JsonValue.Create "complete")
        reject "itemId" (System.Text.Json.Nodes.JsonValue.Create "UTEL-06")
        reject "provenance" (System.Text.Json.Nodes.JsonValue.Create "openai-responses/1")
        reject "identity" (System.Text.Json.Nodes.JsonValue.Create "invented")
        reject "revision" (System.Text.Json.Nodes.JsonValue.Create 0)
        reject "cachedInput" (System.Text.Json.Nodes.JsonValue.Create 11)
        reject "cacheWriteInput" (System.Text.Json.Nodes.JsonValue.Create 11)
        reject "reasoning" (System.Text.Json.Nodes.JsonValue.Create 6)
        reject "total" (System.Text.Json.Nodes.JsonValue.Create 16)
        reject "input" (System.Text.Json.Nodes.JsonValue.Create -1)
        reject "cost" (System.Text.Json.Nodes.JsonValue.Create 1)
