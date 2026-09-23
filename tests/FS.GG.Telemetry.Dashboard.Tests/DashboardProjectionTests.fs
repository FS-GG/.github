namespace FS.GG.Telemetry.Dashboard.Tests

open System
open System.IO
open System.IO.Compression
open System.Security.Cryptography
open System.Text
open System.Text.Json
open System.Text.Json.Nodes
open Xunit
open FS.GG.Telemetry.Dashboard
open FS.GG.Coord
open FS.GG.Coord.Cli

module DashboardProjectionTests =
    let private summary (item: string) =
        JsonNode.Parse(
            $"""{{"schema":"fsgg.telemetry.public-summary/1","item":"{item}","factCount":3,"usageObservations":1,"deliveryObservations":0,"usage":{{"input":4,"cachedInput":1,"cacheWriteInput":0,"output":2,"reasoning":null,"total":6}},"launcherPopulation":{{"admitted":1,"started":1,"terminal":0,"usage":1,"missingAdmission":0,"missingStart":0,"missingTerminal":1,"missingUsage":0}},"recordValidity":"complete","joinIntegrity":"complete","populationCoverage":"partial","qualification":"not-evaluated"}}"""
        )

    let private row (json: string) = JsonNode.Parse json

    let private nodes values =
        JsonArray(values |> Array.map (fun value -> value :> JsonNode))

    let private snapshot (item: string) =
        let root = JsonObject()

        root["selection"] <-
            JsonNode.Parse(
                """{"mode":"all","requestedItem":null,"complete":true,"maxItems":200,"maxRowsPerRelation":10000}"""
            )

        root["store"] <- JsonNode.Parse("""{"schemaVersion":10,"journalMode":"wal"}""")
        root["items"] <- nodes [| JsonValue.Create(item) |]
        root["summaries"] <- nodes [| summary item |]

        let emptyNames =
            [
                "admissions"
                "starts"
                "terminals"
                "expectedDispatches"
                "lineage"
                "usage"
                "ciRuns"
                "ciJobs"
                "ciSteps"
                "budgetAssessments"
                "budgetMembership"
                "budgetEpochs"
                "budgetBreaches"
                "budgetInterventions"
                "activities"
                "activityUsageAttributions"
                "complications"
                "reviews"
            ]

        for name in emptyNames do
            root[name] <- JsonArray()

        root["populations"] <- nodes [| row $"""{{"item_id":"{item}","state":"active"}}""" |]
        root["dirtyItems"] <- nodes [| row $"""{{"item_id":"{item}"}}""" |]

        root["outcomes"] <-
            nodes
                [|
                    row
                        $"""{{"item_id":"{item}","outcome":"refused","code_delivery":"not-delivered","observed_at":"2026-09-10T08:00:00Z","private":"DO-NOT-LEAK"}}"""
                |]

        root["runtimeGaps"] <-
            nodes
                [|
                    row $"""{{"item_id":"{item}","code":"native-usage-unsupported","path":"/private/path"}}"""
                |]

        root["ciCoverage"] <-
            nodes
                [|
                    row
                        $"""{{"item_id":"{item}","inventory":"partial","attempts":"unknown","job_pages":"partial","terminal":"unknown","timestamps":"complete"}}"""
                |]

        root["ciPopulationCoverage"] <-
            nodes
                [|
                    row
                        $"""{{"item_id":"{item}","attempts":"partial","jobs":"partial","terminal":"unknown","timestamps":"complete","continuation":"pending","external_checks":2}}"""
                |]

        root["times"] <- nodes [| row $"""{{"item_id":"{item}","clock":"host-wall","command":"PRIVATE"}}""" |]
        root

    let private envelope (snapshot: JsonNode) =
        let canonical = Encoding.UTF8.GetBytes(snapshot.ToJsonString())
        use output = new MemoryStream()
        use gzip = new GZipStream(output, CompressionLevel.SmallestSize, true)
        gzip.Write canonical
        gzip.Close()

        JsonSerializer.SerializeToUtf8Bytes
            {|
                schema = "fsgg.telemetry.item-detail/2"
                observedAt = "2026-09-10T09:00:00Z"
                revision = Convert.ToHexString(SHA256.HashData canonical).ToLowerInvariant()
                canonicalSnapshotGzip = Convert.ToBase64String(output.ToArray())
                operational =
                    {|
                        pendingBatches = 2
                        consistency = "observed-outside-database-transaction"
                    |}
            |}

    let private unwrap =
        function
        | Ok value -> value
        | Error error -> failwithf "%A" error

    [<Fact>]
    let ``private item steps expose bounded timing and direct tokens without evidence text`` () =
        let value = snapshot "item-a"
        value["activities"] <-
            nodes
                [|
                    row
                        """{"item_id":"item-a","activity_id":"activity-1","category":"implementation","started_at":"2026-09-10T08:00:00Z","ended_at":"2026-09-10T08:02:00Z","evidence":"PRIVATE-EVIDENCE","summary":"PRIVATE-SUMMARY"}"""
                |]
        value["activityUsageAttributions"] <-
            nodes
                [|
                    row
                        """{"item_id":"item-a","activity_id":"activity-1","classification":"direct","total":6,"usage_identity":"PRIVATE-USAGE"}"""
                |]
        value["ciSteps"] <-
            nodes
                [|
                    row
                        """{"item_id":"item-a","name":"Run tests","classification":"useful-validation","started_at":"2026-09-10T08:03:00Z","completed_at":"2026-09-10T08:04:00Z","rationale":"PRIVATE-RATIONALE"}"""
                |]
        value["admissions"] <-
            nodes [| row """{"item_id":"item-a","invocation_id":"PRIVATE-INVOCATION"}""" |]
        value["times"] <-
            nodes
                [|
                    row """{"item_id":"item-a","invocation_id":"PRIVATE-INVOCATION","event":"start","occurred_at":"2026-09-10T08:00:00Z","occurred_clock_provenance":"host-wall"}"""
                    row """{"item_id":"item-a","invocation_id":"PRIVATE-INVOCATION","event":"terminal","occurred_at":"2026-09-10T08:02:00Z","occurred_clock_provenance":"host-wall"}"""
                |]
        value["usage"] <-
            nodes [| row """{"identity":"PRIVATE-USAGE","item_id":"item-a","invocation_id":"PRIVATE-INVOCATION","accounting_scope":"one","total":6}""" |]
        value["lineage"] <-
            nodes [| row """{"item_id":"item-a","invocation_id":"PRIVATE-INVOCATION","relation":"root"}""" |]
        let bytes = DashboardProjection.project "workspace-a" (envelope value) |> unwrap
        let output = Encoding.UTF8.GetString bytes
        Assert.DoesNotContain("PRIVATE-", output)
        use document = JsonDocument.Parse bytes
        let steps = document.RootElement.GetProperty("items").[0].GetProperty("steps")
        Assert.Equal(1, steps.GetProperty("activityCount").GetInt32())
        Assert.Equal(1, steps.GetProperty("ciStepCount").GetInt32())
        Assert.Equal(1, steps.GetProperty("runtimeCount").GetInt32())
        Assert.Equal(6L, steps.GetProperty("rows").[0].GetProperty("tokens").GetInt64())
        Assert.Equal("observed-native-partial", steps.GetProperty("rows").[0].GetProperty("tokenBasis").GetString())
        Assert.Equal("root", steps.GetProperty("rows").[0].GetProperty("classification").GetString())
        Assert.Equal(6L, steps.GetProperty("rows").[1].GetProperty("tokens").GetInt64())
        Assert.Equal("direct-attribution-partial", steps.GetProperty("rows").[1].GetProperty("tokenBasis").GetString())
        Assert.Equal("Run tests", steps.GetProperty("rows").[2].GetProperty("label").GetString())
        Assert.Equal(JsonValueKind.Null, steps.GetProperty("rows").[2].GetProperty("tokens").ValueKind)

    [<Fact>]
    let ``activity tokens refuse mixed native accounting scopes`` () =
        let value = snapshot "item-a"
        value["activities"] <-
            nodes [| row """{"item_id":"item-a","activity_id":"activity-1","category":"implementation","started_at":"2026-09-10T08:00:00Z","ended_at":"2026-09-10T08:02:00Z"}""" |]
        value["activityUsageAttributions"] <-
            nodes
                [|
                    row """{"item_id":"item-a","activity_id":"activity-1","classification":"direct","total":3,"usage_identity":"usage-one"}"""
                    row """{"item_id":"item-a","activity_id":"activity-1","classification":"direct","total":4,"usage_identity":"usage-two"}"""
                |]
        value["usage"] <-
            nodes
                [|
                    row """{"identity":"usage-one","item_id":"item-a","accounting_scope":"scope-one","total":3}"""
                    row """{"identity":"usage-two","item_id":"item-a","accounting_scope":"scope-two","total":4}"""
                |]
        let bytes = DashboardProjection.project "workspace-a" (envelope value) |> unwrap
        use document = JsonDocument.Parse bytes
        let activity = document.RootElement.GetProperty("items").[0].GetProperty("steps").GetProperty("rows").[0]
        Assert.Equal(JsonValueKind.Null, activity.GetProperty("tokens").ValueKind)
        Assert.Equal("unknown", activity.GetProperty("tokenBasis").GetString())

    [<Fact>]
    let ``real Store canonical snapshot projects without shape translation`` () =
        let root =
            Path.Combine(Path.GetTempPath(), "fsgg-dashboard-real-" + Guid.NewGuid().ToString("N"))

        use cleanup =
            { new IDisposable with
                member _.Dispose() =
                    if Directory.Exists root then
                        Directory.Delete(root, true)
            }

        let approved = TelemetryStore.ApprovedLocalDurable

        TelemetryStoreApplication.initialize root approved
        |> function
            | Ok _ -> ()
            | Error errors -> failwithf "%A" errors

        let batch =
            Encoding.UTF8.GetBytes
                $"""{{"schema":"{TelemetryStore.BatchSchema}","ingestId":"actual-shape","sourceIdentity":"fixture","generation":"g1","cursor":"1","eventCount":2,"events":[{{"kind":"item","identity":"actual-item","itemId":"actual-item","revision":0}},{{"kind":"usage","identity":"actual-usage","itemId":"actual-item","revision":0,"provider":"OpenAI","model":"sol","effort":"medium","input":4,"cachedInput":1,"cacheWriteInput":0,"output":2,"reasoning":null,"total":6,"responses":1,"sessions":1,"turns":1}}]}}"""

        TelemetryStoreApplication.ingest root approved batch
        |> function
            | Ok _ -> ()
            | Error errors -> failwithf "%A" errors

        let snapshotJson =
            TelemetryStoreApplication.dashboardSnapshot root approved None
            |> function
                | Ok value -> value
                | Error errors -> failwithf "%A" errors

        let envelopeBytes = Encoding.UTF8.GetBytes snapshotJson
        let projected = DashboardProjection.project "workspace-a" envelopeBytes |> unwrap
        use document = JsonDocument.Parse projected
        let item = document.RootElement.GetProperty("items").[0]
        Assert.Equal("actual-item", item.GetProperty("id").GetString())
        Assert.Equal(6L, item.GetProperty("usage").GetProperty("total").GetInt64())
        Assert.Equal("observed", item.GetProperty("usage").GetProperty("nativeUsage").GetString())
        Assert.Equal("unknown", item.GetProperty("coverage").GetProperty("populationCoverage").GetString())
        Assert.Equal(JsonValueKind.Null, item.GetProperty("coverage").GetProperty("externalChecks").ValueKind)

    [<Fact>]
    let ``real scoped receipt snapshots preserve pending applied and rejected counts`` () =
        let root =
            Path.Combine(Path.GetTempPath(), "fsgg-dashboard-receipts-" + Guid.NewGuid().ToString("N"))

        use cleanup =
            { new IDisposable with
                member _.Dispose() =
                    if Directory.Exists root then
                        Directory.Delete(root, true)
            }

        let approved = TelemetryStore.ApprovedLocalDurable

        let scope: TelemetryReceipt.Scope =
            {
                Workspace = "workspace-a"
                Producer = "producer-a"
                Stream = "runtime"
            }

        let unwrapStore =
            function
            | Ok value -> value
            | Error errors -> failwithf "%A" errors

        let receipt batchId ingestId identity item =
            Encoding.UTF8.GetBytes
                $"""{{"schema":"fsgg.telemetry.envelope/1","workspaceId":"{scope.Workspace}","producerId":"{scope.Producer}","streamId":"{scope.Stream}","batchId":"{batchId}","payload":{{"schema":"{TelemetryStore.BatchSchema}","ingestId":"{ingestId}","sourceIdentity":"native-source","generation":"g1","cursor":"{batchId}","eventCount":1,"events":[{{"kind":"item","identity":"{identity}","itemId":"{item}","revision":0}}]}}}}"""

        TelemetryStoreApplication.initialize root approved |> unwrapStore |> ignore

        TelemetryStoreApplication.enrollReceiptProducer root approved scope
        |> unwrapStore
        |> ignore

        TelemetryStoreApplication.submitReceipt root approved scope (receipt "batch-a" "native-a" "item-a" "item-a")
        |> unwrapStore
        |> ignore

        let projectScoped () =
            TelemetryStoreApplication.scopedDashboardSnapshot root approved scope.Workspace None
            |> unwrapStore
            |> Encoding.UTF8.GetBytes
            |> DashboardProjection.project scope.Workspace
            |> unwrap

        use pending = JsonDocument.Parse(projectScoped ())
        let pendingOperational = pending.RootElement.GetProperty("operational")
        Assert.Equal(1L, pendingOperational.GetProperty("pendingBatches").GetInt64())
        Assert.Equal(0L, pendingOperational.GetProperty("appliedReceipts").GetInt64())
        Assert.Equal(0L, pendingOperational.GetProperty("rejectedReceipts").GetInt64())
        Assert.Equal("database-transaction", pendingOperational.GetProperty("consistency").GetString())

        TelemetryStoreApplication.drainReceipts root approved scope.Workspace
        |> unwrapStore
        |> ignore

        use applied = JsonDocument.Parse(projectScoped ())
        Assert.Equal(0L, applied.RootElement.GetProperty("operational").GetProperty("pendingBatches").GetInt64())
        Assert.Equal(1L, applied.RootElement.GetProperty("operational").GetProperty("appliedReceipts").GetInt64())

        TelemetryStoreApplication.submitReceipt root approved scope (receipt "batch-b" "native-b" "item-a" "item-b")
        |> unwrapStore
        |> ignore

        TelemetryStoreApplication.drainReceipts root approved scope.Workspace
        |> unwrapStore
        |> ignore

        use rejected = JsonDocument.Parse(projectScoped ())
        let rejectedOperational = rejected.RootElement.GetProperty("operational")
        Assert.Equal(1L, rejectedOperational.GetProperty("appliedReceipts").GetInt64())
        Assert.Equal(1L, rejectedOperational.GetProperty("rejectedReceipts").GetInt64())

        Assert.Single(rejected.RootElement.GetProperty("items").EnumerateArray())
        |> ignore

    [<Fact>]
    let ``projects closed private status usage coverage and clock distinctions`` () =
        let bytes =
            DashboardProjection.project "workspace-a" (envelope (snapshot "item-a"))
            |> unwrap

        let json = Encoding.UTF8.GetString bytes
        Assert.Contains("\"workspaceId\":\"workspace-a\"", json)
        Assert.Contains("\"reasoning\":null", json)
        Assert.Contains("\"populationCoverage\":\"partial\"", json)
        Assert.Contains("\"ciContinuation\":\"pending\"", json)
        Assert.Contains("\"outcome\":\"refused\"", json)
        Assert.Contains("native-usage-unsupported", json)
        Assert.Contains("\"nativeUsage\":\"unsupported\"", json)
        Assert.Contains("host-wall", json)
        Assert.Contains("\"appliedReceipts\":null", json)
        Assert.Contains("\"rejectedReceipts\":null", json)

        for secret in [ "DO-NOT-LEAK"; "/private/path"; "PRIVATE" ] do
            Assert.DoesNotContain(secret, json)

    [<Fact>]
    let ``private projection identifies canonical members only from recorded original facts`` () =
        let memberSnapshot = snapshot "member-a"

        memberSnapshot["populations"] <-
            nodes [| row """{"item_id":"member-a","original_item_id":"original-a","state":"completed"}""" |]

        use memberDocument =
            DashboardProjection.project "workspace-a" (envelope memberSnapshot)
            |> unwrap
            |> JsonDocument.Parse

        let memberItem = memberDocument.RootElement.GetProperty("items").[0]
        Assert.Equal("canonical-item", memberItem.GetProperty("unit").GetString())
        Assert.Equal("member", memberItem.GetProperty("state").GetProperty("memberRelation").GetString())
        Assert.Equal("original-a", memberItem.GetProperty("state").GetProperty("originalItemId").GetString())

        let originalSnapshot = snapshot "original-a"

        originalSnapshot["populations"] <-
            nodes [| row """{"item_id":"original-a","original_item_id":"original-a","state":"completed"}""" |]

        use originalDocument =
            DashboardProjection.project "workspace-a" (envelope originalSnapshot)
            |> unwrap
            |> JsonDocument.Parse

        let originalState = originalDocument.RootElement.GetProperty("items").[0].GetProperty("state")
        Assert.Equal("original", originalState.GetProperty("memberRelation").GetString())

        use unknownDocument =
            DashboardProjection.project "workspace-a" (envelope (snapshot "member-a"))
            |> unwrap
            |> JsonDocument.Parse

        let unknownState = unknownDocument.RootElement.GetProperty("items").[0].GetProperty("state")
        Assert.Equal("unknown", unknownState.GetProperty("memberRelation").GetString())
        Assert.Equal(JsonValueKind.Null, unknownState.GetProperty("originalItemId").ValueKind)

    [<Fact>]
    let ``empty scoped snapshot remains explicitly empty`` () =
        let value = snapshot "placeholder"
        value["items"] <- JsonArray()
        value["summaries"] <- JsonArray()
        value["populations"] <- JsonArray()
        value["dirtyItems"] <- JsonArray()
        value["outcomes"] <- JsonArray()
        value["runtimeGaps"] <- JsonArray()
        value["ciCoverage"] <- JsonArray()
        value["ciPopulationCoverage"] <- JsonArray()
        value["times"] <- JsonArray()
        let bytes = DashboardProjection.project "workspace-a" (envelope value) |> unwrap
        Assert.Contains("\"items\":[]", Encoding.UTF8.GetString bytes)

    [<Fact>]
    let ``selected item snapshot uses the same closed projection`` () =
        let value = snapshot "item-a"
        value["selection"]["mode"] <- "item"
        value["selection"]["requestedItem"] <- "item-a"
        let bytes = DashboardProjection.project "workspace-a" (envelope value) |> unwrap
        Assert.Contains("\"id\":\"item-a\"", Encoding.UTF8.GetString bytes)

    [<Fact>]
    let ``unicode and markup item identity remains JSON encoded for text-only rendering`` () =
        let item = "café <img src=x onerror=alert(1)>"

        let bytes =
            DashboardProjection.project "workspace-a" (envelope (snapshot item)) |> unwrap

        use document = JsonDocument.Parse bytes
        Assert.Equal(item, (document.RootElement.GetProperty("items").[0].GetProperty("id").GetString()))

        Assert.Contains(
            "textContent",
            DashboardAssets.tryGet "/private/dashboard/app.js"
            |> Option.get
            |> _.Bytes
            |> Encoding.UTF8.GetString
        )

    [<Fact>]
    let ``rejects unauthorized workspace malformed envelope and revision mismatch`` () =
        Assert.Equal(Error UnauthorizedWorkspace, DashboardProjection.project "../other" (envelope (snapshot "item")))
        Assert.Equal(Error InvalidEnvelope, DashboardProjection.project "workspace" (Encoding.UTF8.GetBytes "{}"))
        let bytes = envelope (snapshot "item")
        use doc = JsonDocument.Parse bytes
        let changed = JsonNode.Parse(doc.RootElement.GetRawText()).AsObject()
        changed["revision"] <- String.replicate 64 "0"

        Assert.Equal(
            Error InvalidRevision,
            DashboardProjection.project "workspace" (Encoding.UTF8.GetBytes(changed.ToJsonString()))
        )

    [<Fact>]
    let ``operational envelope variants cannot be mixed`` () =
        use document = JsonDocument.Parse(envelope (snapshot "item"))
        let hybrid = JsonNode.Parse(document.RootElement.GetRawText()).AsObject()

        hybrid["operational"] <-
            JsonNode.Parse(
                """{"pendingBatches":0,"appliedReceipts":0,"rejectedReceipts":0,"consistency":"observed-outside-database-transaction"}"""
            )

        Assert.Equal(
            Error InvalidEnvelope,
            DashboardProjection.project "workspace" (Encoding.UTF8.GetBytes(hybrid.ToJsonString()))
        )

    [<Fact>]
    let ``malformed scalar types and envelope cap return closed errors`` () =
        let malformed = snapshot "item"
        let malformedSummary = (malformed["summaries"].AsArray()).[0].AsObject()
        malformedSummary["factCount"] <- "three"
        Assert.Equal(Error InvalidSnapshot, DashboardProjection.project "workspace" (envelope malformed))

        Assert.Equal(
            Error EnvelopeTooLarge,
            DashboardProjection.project "workspace" (Array.zeroCreate<byte>(1024 * 1024 + 1))
        )

    [<Fact>]
    let ``missing zero and stale remain distinct from unknown and incomplete`` () =
        let value = snapshot "item"
        let aggregate = value["summaries"].AsArray().[0].AsObject()
        aggregate["usageObservations"] <- 0
        let usage = aggregate["usage"].AsObject()
        usage["input"] <- 0
        usage["cachedInput"] <- 0
        usage["output"] <- 0
        usage["total"] <- 0
        value["runtimeGaps"] <- JsonArray()
        value["outcomes"].AsArray().[0].AsObject()["outcome"] <- "stale"
        value["ciPopulationCoverage"] <- JsonArray()

        let projected =
            DashboardProjection.project "workspace" (envelope value)
            |> unwrap
            |> Encoding.UTF8.GetString

        Assert.Contains("\"nativeUsage\":\"missing\"", projected)
        Assert.Contains("\"input\":0", projected)
        Assert.Contains("\"outcome\":\"stale\"", projected)
        Assert.Contains("\"ciInventory\":\"partial\"", projected)
        Assert.Contains("\"externalChecks\":null", projected)

    [<Fact>]
    let ``duplicate mixed and oversized relation selections fail closed`` () =
        let duplicate = snapshot "item"
        duplicate["items"] <- nodes [| JsonValue.Create("item"); JsonValue.Create("item") |]
        duplicate["summaries"] <- nodes [| summary "item"; summary "item" |]
        Assert.Equal(Error InvalidSnapshot, DashboardProjection.project "workspace" (envelope duplicate))
        let mixed = snapshot "item"
        mixed["items"] <- nodes [| JsonValue.Create("item"); JsonValue.Create("other") |]
        Assert.Equal(Error InvalidSnapshot, DashboardProjection.project "workspace" (envelope mixed))
        let malformedArray = snapshot "item"
        malformedArray["activities"] <- JsonObject()
        Assert.Equal(Error InvalidSnapshot, DashboardProjection.project "workspace" (envelope malformedArray))
        let oversized = snapshot "item"
        oversized["activities"] <- JsonArray(Array.init 10001 (fun _ -> JsonObject() :> JsonNode))
        Assert.Equal(Error InvalidSnapshot, DashboardProjection.project "workspace" (envelope oversized))

    [<Fact>]
    let ``rejects gzip expansion beyond canonical bound`` () =
        let expanded = Array.create (4 * 1024 * 1024 + 1) (byte 'x')
        use output = new MemoryStream()
        use gzip = new GZipStream(output, CompressionLevel.SmallestSize, true)
        gzip.Write expanded
        gzip.Close()

        let bytes =
            JsonSerializer.SerializeToUtf8Bytes
                {|
                    schema = "fsgg.telemetry.item-detail/2"
                    observedAt = "2026-09-10T09:00:00Z"
                    revision = String.replicate 64 "0"
                    canonicalSnapshotGzip = Convert.ToBase64String(output.ToArray())
                    operational =
                        {|
                            pendingBatches = 0
                            consistency = "observed-outside-database-transaction"
                        |}
                |}

        Assert.Equal(Error SnapshotTooLarge, DashboardProjection.project "workspace" bytes)

    [<Fact>]
    let ``rejects incomplete selection and incompatible store`` () =
        let incomplete = snapshot "item"
        incomplete["selection"]["complete"] <- false
        Assert.Equal(Error IncompleteSnapshot, DashboardProjection.project "workspace" (envelope incomplete))
        let incompatible = snapshot "item"
        incompatible["store"]["journalMode"] <- "delete"
        Assert.Equal(Error IncompatibleStore, DashboardProjection.project "workspace" (envelope incompatible))

    [<Fact>]
    let ``assets are fixed self contained and returned defensively`` () =
        let index = DashboardAssets.tryGet "/private/dashboard/" |> Option.get
        let script = DashboardAssets.tryGet "/private/dashboard/app.js" |> Option.get
        Assert.Equal("text/html; charset=utf-8", index.ContentType)
        let scriptText = Encoding.UTF8.GetString script.Bytes
        Assert.Contains("snapshot:\"/private/dashboard/v1/snapshot\"", scriptText)
        Assert.Contains("post(\"/private/dashboard/login\"", scriptText)
        Assert.Contains("session:\"/private/dashboard/v1/session/refresh\"", scriptText)
        Assert.Contains("logout:\"/private/dashboard/v1/logout\"", scriptText)
        Assert.Contains("principalId", Encoding.UTF8.GetString index.Bytes)
        Assert.Contains("accessKey", Encoding.UTF8.GetString index.Bytes)
        Assert.Contains("applied receipts", scriptText)
        Assert.Contains("rejected receipts", scriptText)

        for route in
            [
                "/private/dashboard/"
                "/private/dashboard/app.js"
                "/private/dashboard/styles.css"
            ] do
            let content =
                DashboardAssets.tryGet route |> Option.get |> _.Bytes |> Encoding.UTF8.GetString

            Assert.DoesNotContain("http://", content)
            Assert.DoesNotContain("https://", content)

        index.Bytes[0] <- 0uy
        Assert.NotEqual(0uy, (DashboardAssets.tryGet "/private/dashboard/" |> Option.get).Bytes[0])
        Assert.True(DashboardAssets.tryGet "/unknown" |> Option.isNone)

    [<Fact>]
    let ``local assets reuse the dashboard in explicit bootstrap mode`` () =
        let index =
            DashboardAssets.tryGetLocal "/"
            |> Option.get
            |> _.Bytes
            |> Encoding.UTF8.GetString

        let script =
            DashboardAssets.tryGetLocal "/app.js"
            |> Option.get
            |> _.Bytes
            |> Encoding.UTF8.GetString

        Assert.Contains("fsgg-dashboard-mode\" content=\"local", index)
        Assert.Contains("/styles.css", index)
        Assert.Contains("/app.js", index)
        Assert.Contains("session:\"/api/session\"", script)
        Assert.Contains("snapshot:\"/api/snapshot\"", script)
        Assert.Contains("logout:\"/api/logout\"", script)
        Assert.True(DashboardAssets.tryGetLocal "/private/dashboard/" |> Option.isNone)
