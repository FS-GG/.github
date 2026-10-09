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
