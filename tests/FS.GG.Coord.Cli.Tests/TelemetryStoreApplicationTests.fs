namespace FS.GG.Coord.Cli.Tests

open System
open System.Diagnostics
open System.IO
open System.IO.Compression
open System.Security.Cryptography
open System.Text
open System.Text.Json
open Xunit
open Microsoft.Data.Sqlite
open FS.GG.Coord
open FS.GG.Coord.Cli

module TelemetryStoreApplicationTests =
    let private approved = TelemetryStore.ApprovedLocalDurable

    let private causalFixture name =
        Path.Combine(__SOURCE_DIRECTORY__, "..", "learn-01-analysis", "fixtures", "causal-admission", name)

    let private causalBatch name event =
        Encoding.UTF8.GetBytes
            $"""{{"schema":"{TelemetryStore.BatchSchema}","ingestId":"causal-{name}","sourceIdentity":"coordination","generation":"synthetic-{name}","cursor":"causal-{name}","eventCount":1,"events":[{event}]}}"""

    let private causalEvent name = File.ReadAllText(causalFixture(name + ".event.json")).Trim()

    let private unwrap =
        function
        | Ok value -> value
        | Error errors -> failwithf "%A" errors

    let private batch ingest identity revision cursor input =
        Encoding.UTF8.GetBytes
            $"""{{"schema":"%s{TelemetryStore.BatchSchema}","ingestId":"%s{ingest}","sourceIdentity":"worker-a","generation":"g1","cursor":"%s{cursor}","eventCount":3,"events":[{{"kind":"item","identity":"UTEL-02","itemId":"UTEL-02","revision":0}},{{"kind":"usage","identity":"%s{identity}","itemId":"UTEL-02","revision":%d{revision},"provider":"OpenAI","model":"sol","effort":"medium","input":%d{input},"cachedInput":2,"cacheWriteInput":1,"output":5,"reasoning":2,"total":%d{input + 5L},"responses":1,"sessions":1,"turns":1}},{{"kind":"delivery","identity":"delivery-1","itemId":"UTEL-02","revision":0,"state":"delivered","expectedHead":null,"observedHead":null,"prNumber":3348}}]}}"""

    let private root () =
        let path =
            Path.Combine(Path.GetTempPath(), "fsgg-utel02-" + Guid.NewGuid().ToString("N"))

        { new IDisposable with
            member _.Dispose() =
                if Directory.Exists path then
                    Directory.Delete(path, true)
        },
        path

    [<Fact>]
    let ``causal declaration survives schema14 reopen and refuses immutable correction atomically`` () =
        let cleanup, path = root ()
        use cleanup = cleanup
        TelemetryStoreApplication.initialize path approved |> unwrap |> ignore
        let rootEvent = causalEvent "root"
        let batch = causalBatch "root" rootEvent
        Assert.Contains("\"accepted\":1", TelemetryStoreApplication.ingest path approved batch |> unwrap)
        Assert.Contains("\"replayed\":1", TelemetryStoreApplication.ingest path approved batch |> unwrap)
        let snapshot () =
            use envelope = JsonDocument.Parse(TelemetryStoreApplication.dashboardSnapshot path approved None |> unwrap)
            let compressed = Convert.FromBase64String(envelope.RootElement.GetProperty("canonicalSnapshotGzip").GetString())
            use input = new MemoryStream(compressed)
            use unzip = new GZipStream(input, CompressionMode.Decompress)
            use plain = new MemoryStream()
            unzip.CopyTo plain
            JsonDocument.Parse(plain.ToArray())
        use first = snapshot ()
        let rows = first.RootElement.GetProperty("learningObservations")
        Assert.Equal(1, rows.GetArrayLength())
        Assert.Equal("execution-causal-admission/1", rows.[0].GetProperty("kind").GetString())
        use originalEvent = JsonDocument.Parse rootEvent
        use retainedEvent = JsonDocument.Parse(rows.[0].GetProperty("canonical").GetString())
        Assert.Equal(originalEvent.RootElement.GetProperty("admissionBase64").GetString(),
                     retainedEvent.RootElement.GetProperty("admissionBase64").GetString())
        let event = System.Text.Json.Nodes.JsonNode.Parse rootEvent
        let admission = Convert.FromBase64String(event["admissionBase64"].GetValue<string>())
        let changed = Encoding.UTF8.GetString(admission).Replace("\"implementation\"", "\"review\"") |> Encoding.UTF8.GetBytes
        event["admissionBase64"] <- Convert.ToBase64String changed
        event["admissionSha256"] <- CanonicalJson.sha256 changed
        Assert.True(TelemetryStoreApplication.ingest path approved (causalBatch "changed" (event.ToJsonString())) |> Result.isError)
        use after = snapshot ()
        Assert.Equal(first.RootElement.GetProperty("learningObservations").[0].GetProperty("content_digest").GetString(),
                     after.RootElement.GetProperty("learningObservations").[0].GetProperty("content_digest").GetString())
        Assert.Equal(14, after.RootElement.GetProperty("store").GetProperty("schemaVersion").GetInt32())

    let private pragma path value =
        use connection =
            new SqliteConnection(
                $"Data Source=%s{Path.Combine(path, TelemetryStoreApplication.databaseFileName)};Pooling=False"
            )

        connection.Open()
        use command = connection.CreateCommand()
        command.CommandText <- $"PRAGMA user_version=%d{value};"
        command.ExecuteNonQuery() |> ignore

    let private dropBudgetSchema =
        "DROP TABLE budget_interventions; DROP TABLE budget_breaches; DROP TABLE budget_assessment_revisions; DROP TABLE budget_epoch_membership; DROP INDEX budget_one_open_epoch; DROP TABLE budget_epochs; DROP TABLE budget_dirty_items; DROP TABLE budget_shared_cost_refs; DROP TABLE budget_intervention_facts; DROP TABLE budget_interval_facts; DROP TABLE budget_attribution_facts; DROP TABLE budget_population_facts; DELETE FROM schema_migrations WHERE version=4;"

    let private dropOperationalSchema =
        "DROP TABLE operational_event_times; DROP TABLE invocation_lineage; DROP TABLE expected_dispatches; DROP TABLE operational_activations; DELETE FROM schema_migrations WHERE version=5;"

    let private dropCiPopulationSchema =
        "DROP TABLE ci_population_coverage; DROP TABLE ci_check_runs; DROP TABLE ci_population_admissions; DELETE FROM schema_migrations WHERE version=6;"

    // Genuine legacy reconstruction removes the complete later additive schema first.
    let private dropEfficiencySchema =
        """
DROP VIEW efficiency_current_allocations;
DROP TABLE efficiency_epoch_gaps;
DROP TABLE efficiency_outcome_epochs;
DROP TABLE efficiency_receiver_order;
DROP TABLE efficiency_analysis_reservations;
DROP TABLE efficiency_analysis_history;
DROP TABLE efficiency_analysis_requests;
DROP TABLE fact_acceptance_times;
DROP TABLE efficiency_allocation_context;
DROP TABLE efficiency_record_history;
DROP TABLE efficiency_records;
DELETE FROM schema_migrations WHERE version=14;
"""

    let private dropCorrectionSchema =
        dropEfficiencySchema + "DROP VIEW current_ingest_facts; DROP TABLE ci_effective_attribution; DROP TABLE ci_correction_evidence; DROP TABLE ci_attribution_corrections; DELETE FROM schema_migrations WHERE version=13; DELETE FROM store_metadata WHERE key='ciCorrectionStoreId';"

    let private dropReviewSchema =
        dropCorrectionSchema + "DROP TABLE fact_admissions; DROP TABLE receipt_admissions; ALTER TABLE receipt_producers DROP COLUMN grant_generation; ALTER TABLE receipt_producers DROP COLUMN grant_id; ALTER TABLE receipt_producers DROP COLUMN authority_role; DELETE FROM schema_migrations WHERE version=12; DROP TABLE learning_fact_order; DELETE FROM schema_migrations WHERE version=11; DELETE FROM schema_migrations WHERE version=10; DROP INDEX transport_pending; DROP TABLE transport_receipts; DROP TABLE receipt_producers; DELETE FROM schema_migrations WHERE version=9; DROP INDEX process_review_attempt_subject; DROP INDEX process_review_item_subject; DROP INDEX activity_spans_item_attempt; DROP INDEX activity_usage_item; DROP INDEX complication_events_item; DROP TABLE complication_events; DROP TABLE activity_usage_attributions; DROP TABLE activity_spans; DROP TABLE process_reviews; DELETE FROM schema_migrations WHERE version=8;"

    let private dropNativeOutcomeSchema =
        dropReviewSchema
        + " DROP INDEX native_item_outcomes_item_observed; DROP TABLE native_item_outcomes; DELETE FROM schema_migrations WHERE version=7;"

    let private budgetBatch ingest cursor events =
        Encoding.UTF8.GetBytes
            $"""{{"schema":"{TelemetryStore.BatchSchema}","ingestId":"{ingest}","sourceIdentity":"budget-worker","generation":"g1","cursor":"{cursor}","eventCount":{List.length events},"events":[{String.concat "," events}]}}"""

    let private population item revision state source =
        $"""{{"kind":"budget-population","identity":"population-{item}","itemId":"{item}","revision":{revision},"originalItemId":"{item}","state":"{state}","sourceKind":"native-item","sourceRef":"{source}"}}"""

    let private roadmapPopulation item original =
        let key =
            SHA256.HashData(Encoding.UTF8.GetBytes(item + "\u001f" + original))
            |> Convert.ToHexString
            |> fun value -> value.ToLowerInvariant().Substring(0, 32)

        $"""{{"kind":"budget-population","identity":"budget-population-{key}","itemId":"{item}","revision":0,"originalItemId":"{original}","state":"open","sourceKind":"native-item","sourceRef":"roadmap-dispatch:{key}"}}"""

    let private attribution item revision numerator denominator coverage attribution sourceKind source =
        let number value =
            value |> Option.map string |> Option.defaultValue "null"

        $"""{{"kind":"budget-attribution","identity":"attribution-{item}","itemId":"{item}","revision":{revision},"dimension":"model-tokens","provider":"openai","accountingScope":"whole-item","numerator":{number numerator},"denominator":{number denominator},"coverage":"{coverage}","attribution":"{attribution}","sourceKind":"{sourceKind}","sourceRef":"{source}"}}"""

    let private operationalBatch ingest item events =
        Encoding.UTF8.GetBytes
            $"""{{"schema":"{TelemetryStore.BatchSchema}","ingestId":"{ingest}","sourceIdentity":"operational-observer","generation":"g1","cursor":"{ingest}","eventCount":{List.length events},"events":[{String.concat "," events}]}}"""

    [<Fact>]
    let ``local parser rejection is byte bound and precedes inbox facts and cursors`` () =
        let cleanup, path = root ()
        use cleanup = cleanup
        TelemetryStoreApplication.initialize path approved |> unwrap |> ignore
        let counts () =
            use connection = new SqliteConnection($"Data Source={Path.Combine(path, TelemetryStoreApplication.databaseFileName)};Mode=ReadOnly;Pooling=False")
            connection.Open()
            use command = connection.CreateCommand()
            command.CommandText <- "SELECT (SELECT count(*) FROM ingest_facts) + (SELECT count(*) FROM ingest_batches) + (SELECT count(*) FROM source_cursors);"
            Convert.ToInt64(command.ExecuteScalar())
        let before = counts ()
        let activity = """{"kind":"activity-span","identity":"span-a","itemId":"item-a","revision":0,"activityId":"activity-a","invocationId":"invocation-a","attemptId":"attempt-a","category":"validation","startedAt":"2026-10-07T00:00:00Z","endedAt":null,"clockProvenance":"test","evidence":["invalid"],"summary":null}"""
        let bytes = operationalBatch "parser-rejected" "item-a" [ activity ]
        match TelemetryStoreApplication.publish path approved bytes with
        | Ok _ -> failwith "malformed activity published"
        | Error [ response ] ->
            use proof = JsonDocument.Parse response
            let value = proof.RootElement
            Assert.Equal("fsgg.telemetry.local-parser-rejection/1", value.GetProperty("schema").GetString())
            Assert.Equal("invalid-batch", value.GetProperty("code").GetString())
            Assert.Equal("before-publication-io", value.GetProperty("boundary").GetString())
            Assert.Equal(CanonicalJson.sha256 bytes, value.GetProperty("inputSha256").GetString())
            Assert.Equal("parser-rejected", value.GetProperty("ingestId").GetString())
            Assert.Equal(Path.GetFullPath path, value.GetProperty("storeRoot").GetString())
            Assert.NotEmpty(value.GetProperty("errors").EnumerateArray())
        | other -> failwithf "unexpected parser refusal %A" other
        Assert.Equal(before, counts ())
        Assert.False(Directory.Exists(Path.Combine(path, "inbox")))
        let invalidRoot = "relative-store-root"
        match TelemetryStoreApplication.publish invalidRoot TelemetryStore.ApprovedLocalDurable bytes with
        | Error errors -> Assert.DoesNotContain("local-parser-rejection", String.concat "\n" errors)
        | _ -> failwith "unsupported destination accepted"

    [<Fact>]
    let ``installed origin resolves only through current native admission and exact retained selectors`` () =
        let cleanup, path = root ()
        use cleanup = cleanup
        TelemetryStoreApplication.initialize path approved |> unwrap |> ignore
        let scope: TelemetryReceipt.Scope =
            { Workspace = "workspace-origin"; Producer = "producer-origin"; Stream = "stream-origin" }
        let principal: TelemetryReceipt.Principal =
            { Scope = scope; Role = TelemetryReceipt.NativeCollector
              GrantId = Some "grant-origin"; GrantGeneration = Some 7L }
        TelemetryStoreApplication.provisionReceiptWorkspace path approved scope.Workspace |> unwrap |> ignore
        TelemetryStoreApplication.enrollReceiptPrincipal path approved principal |> unwrap |> ignore
        let digest = String.replicate 64 "a"
        let event identity manager observed expires =
            $"""{{"kind":"learn-installed-origin/1","identity":"{identity}","revision":0,"workspaceId":"{scope.Workspace}","producerId":"{scope.Producer}","streamId":"{scope.Stream}","role":"native-collector","grantId":"grant-origin","grantGeneration":7,"managerReceiptSha256":"{manager}","capabilityProfileSha256":"{digest}","capabilityResultSha256":"{digest}","nativeCaptureSha256":"{digest}","nativeVerificationSha256":"{digest}","capabilityObservedAt":"{observed}","capabilityExpiresAt":"{expires}","installationSha256":"{digest}"}}"""
        let envelope batch fact =
            Encoding.UTF8.GetBytes
                $"""{{"schema":"{TelemetryReceipt.Schema}","workspaceId":"{scope.Workspace}","producerId":"{scope.Producer}","streamId":"{scope.Stream}","batchId":"{batch}","payload":{{"schema":"{TelemetryStore.BatchSchema}","ingestId":"receipt-{batch}","sourceIdentity":"protected-installed-origin","generation":"7","cursor":"{batch}","eventCount":1,"events":[{fact}]}}}}"""
        let admit identity manager observed expires =
            TelemetryStoreApplication.submitReceiptPrincipal path approved principal
                (envelope identity (event identity manager observed expires)) |> unwrap |> ignore
            Assert.Contains("\"rejected\":0", TelemetryStoreApplication.drainReceipts path approved scope.Workspace |> unwrap)
        admit "origin-1" digest "2026-10-01T10:00:00.0000000+00:00" "2026-10-01T10:05:00.0000000+00:00"
        let query: TelemetryStoreApplication.InstalledOriginQuery =
            { WorkspaceId = scope.Workspace; ProducerId = scope.Producer; StreamId = scope.Stream
              Role = "native-collector"; GrantId = "grant-origin"; GrantGeneration = 7L
              ManagerReceiptSha256 = digest; CapabilityProfileSha256 = digest
              CapabilityResultSha256 = digest; NativeCaptureSha256 = digest
              NativeVerificationSha256 = digest; InstallationSha256 = digest }
        let at minute candidate =
            TelemetryStoreApplication.resolveInstalledOriginAt
                (DateTimeOffset.Parse($"2026-10-01T10:{minute}:00.0000000+00:00")) path approved candidate
        let resolved = at "01" query |> unwrap
        Assert.Equal("origin-1", resolved.RecordId)
        Assert.Equal(0L, resolved.Revision)
        Assert.Equal(Error [ "learning-installed-origin-unavailable" ],
                     at "01" { query with ManagerReceiptSha256 = String.replicate 64 "b" })
        Assert.Equal(Error [ "learning-installed-origin-unavailable" ], at "05" query)
        Assert.Equal(Error [ "learning-installed-origin-unavailable" ], at "06" query)

        let renewed = String.replicate 64 "b"
        admit "origin-2" renewed "2026-10-01T10:02:00.0000000+00:00" "2026-10-01T10:10:00.0000000+00:00"
        let renewedQuery = { query with ManagerReceiptSha256 = renewed }
        Assert.Equal(Error [ "learning-installed-origin-unavailable" ], at "01" renewedQuery)
        Assert.Equal("origin-1", (at "03" query |> unwrap).RecordId)
        Assert.Equal("origin-2", (at "03" renewedQuery |> unwrap).RecordId)
        admit "origin-3" renewed "2026-10-01T10:02:00.0000000+00:00" "2026-10-01T10:10:00.0000000+00:00"
        Assert.Equal(Error [ "learning-installed-origin-ambiguous" ], at "03" renewedQuery)

        let genericScope = { scope with Producer = "generic-origin" }
        let generic = TelemetryReceipt.genericPrincipal genericScope
        TelemetryStoreApplication.enrollReceiptPrincipal path approved generic |> unwrap |> ignore
        let genericEnvelope =
            Encoding.UTF8.GetString(envelope "origin-generic" (event "origin-generic" digest "2026-10-01T10:00:00.0000000+00:00" "2026-10-01T10:05:00.0000000+00:00"))
                .Replace(scope.Producer, genericScope.Producer)
            |> Encoding.UTF8.GetBytes
        TelemetryStoreApplication.submitReceiptPrincipal path approved generic genericEnvelope |> unwrap |> ignore
        Assert.Contains("\"rejected\":1", TelemetryStoreApplication.drainReceipts path approved scope.Workspace |> unwrap)
        Assert.Equal(Error [ "native route original mapping is unavailable" ],
                     TelemetryStoreApplication.readNativeRoutePopulation path approved "missing-original")

    let private ciPopulationBatch ingest item revision checkStatus coverage continuation =
        Encoding.UTF8.GetBytes
            $"""{{"schema":"{TelemetryStore.BatchSchema}","ingestId":"{ingest}","sourceIdentity":"ci-population","generation":"candidate-a","cursor":"{ingest}","eventCount":3,"events":[{{"kind":"ci-population-admission","identity":"ci-admission-a","itemId":"{item}","revision":1,"collectionId":"collection-a","repository":"o/r","prNumber":7,"baseRef":"main","baseSha":"dddddddddddddddddddddddddddddddddddddddd","head":"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa","witness":"native-pr-head"}},{{"kind":"ci-check","identity":"ci-check-a","itemId":"{item}","revision":{revision},"repository":"o/r","checkId":101,"name":"build","appSlug":"github-actions","status":"{checkStatus}","conclusion":null,"startedAt":"2026-09-08T10:00:00Z","completedAt":null}},{{"kind":"ci-population-coverage","identity":"ci-coverage-a","itemId":"{item}","revision":{revision},"collectionId":"collection-a","actions":"{coverage}","checks":"{coverage}","attempts":"{coverage}","jobs":"{coverage}","terminal":"{coverage}","timestamps":"{coverage}","continuation":"{continuation}","externalChecks":0,"gaps":"[]"}}]}}"""

    let private activation item runtime delay =
        $"""{{"kind":"operational-activation","identity":"activation-{item}","itemId":"{item}","revision":0,"activationId":"activation-{item}","scope":"explicit-future-dispatches","runtime":"{runtime}","activatedAt":"2026-09-08T10:00:00Z","clockProvenance":"host-wall","lateAfterSeconds":{delay}}}"""

    let private dispatch item id relation parent runtime minute =
        let parentValue =
            parent |> Option.map (fun value -> $"\"{value}\"") |> Option.defaultValue "null"

        $"""{{"kind":"expected-dispatch","identity":"expected-{item}-{id}","itemId":"{item}","revision":0,"dispatchId":"{id}","activationId":"activation-{item}","relation":"{relation}","parentDispatchId":{parentValue},"runtime":"{runtime}","expectedAt":"2026-09-08T10:{minute}:00Z","clockProvenance":"host-wall"}}"""

    let private lineage item identity dispatchId invocationId relation parentInvocation root runtime =
        let parentValue =
            parentInvocation
            |> Option.map (fun value -> $"\"{value}\"")
            |> Option.defaultValue "null"

        $"""{{"kind":"invocation-lineage","identity":"{identity}","itemId":"{item}","revision":0,"dispatchId":"{dispatchId}","invocationId":"{invocationId}","relation":"{relation}","parentInvocationId":{parentValue},"rootInvocationId":"{root}","runtime":"{runtime}"}}"""

    let private eventTime item identity invocation event occurred occurredClock observed observedClock =
        let timestamp value =
            value |> Option.map (fun text -> $"\"{text}\"") |> Option.defaultValue "null"

        let clock value =
            value |> Option.map (fun text -> $"\"{text}\"") |> Option.defaultValue "null"

        $"""{{"kind":"event-time","identity":"{identity}","itemId":"{item}","revision":0,"invocationId":"{invocation}","event":"{event}","occurredAt":{timestamp occurred},"occurredClockProvenance":{clock occurredClock},"observedAt":{timestamp observed},"observedClockProvenance":{clock observedClock}}}"""

    let private completeTimes item prefix invocation minute observedSecond =
        [
            for event in [ "admission"; "start"; "terminal" ] do
                yield
                    eventTime
                        item
                        ($"time-{prefix}-{event}")
                        invocation
                        event
                        (Some $"2026-09-08T10:{minute}:00Z")
                        (Some "provider-native")
                        (Some $"2026-09-08T10:{minute}:{observedSecond}Z")
                        (Some "provider-native")
        ]

    let private nativeOutcome item revision outcome code observedAt =
        let merge =
            if code = "delivered" then
                "\"bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb\""
            else
                "null"

        let occurred =
            if code = "delivered" then
                "\"2026-09-08T10:04:00Z\""
            else
                "null"

        $"""{{"kind":"native-item-outcome","identity":"native-outcome-{item}","itemId":"{item}","revision":{revision},"repository":"o/r","prNumber":7,"baseRef":"main","baseSha":"dddddddddddddddddddddddddddddddddddddddd","head":"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa","outcome":"{outcome}","codeDelivery":"{code}","mergeCommit":{merge},"occurredAt":{occurred},"observedAt":"{observedAt}","sourceKind":"routine-delivery","sourceRef":"routine-delivery:{item}"}}"""

    [<Fact>]
    let ``orchestration delivery outcome is stored with its own source kind`` () =
        let cleanup, path = root ()
        use cleanup = cleanup
        TelemetryStoreApplication.initialize path approved |> unwrap |> ignore
        let item = "work-item-v1-orchestration-fixture"
        let fact =
            (nativeOutcome item 1L "delivered" "delivered" "2026-09-08T10:04:01Z")
                .Replace("routine-delivery", "orchestration-delivery", StringComparison.Ordinal)

        TelemetryStoreApplication.ingest path approved (operationalBatch "orchestration-outcome" item [ fact ])
        |> unwrap
        |> ignore

        use connection = new SqliteConnection($"Data Source=%s{Path.Combine(path, TelemetryStoreApplication.databaseFileName)};Pooling=False")
        connection.Open()
        use command = connection.CreateCommand()
        command.CommandText <- "SELECT source_kind FROM native_item_outcomes WHERE item_id=$item;"
        command.Parameters.AddWithValue("$item", item) |> ignore
        Assert.Equal("orchestration-delivery", string (command.ExecuteScalar()))

    [<Fact>]
    let ``Main two-event delivery batch applies once as outcome and completed population`` () =
        let cleanup, path = root ()
        use cleanup = cleanup
        TelemetryStoreApplication.initialize path approved |> unwrap |> ignore
        let item = "work-item-v1-main-delivery"
        let source = "orchestration-delivery:30000000-0000-0000-0000-000000000007"
        let outcome =
            $"""{{"kind":"native-item-outcome","identity":"native-item-outcome-main","itemId":"{item}","revision":638937000000000000,"repository":"FS-GG/.github","prNumber":453,"baseRef":"main","baseSha":"dddddddddddddddddddddddddddddddddddddddd","head":"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa","outcome":"delivered","codeDelivery":"delivered","mergeCommit":"bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb","occurredAt":"2026-09-20T10:04:00Z","observedAt":"2026-09-20T10:04:01Z","sourceKind":"orchestration-delivery","sourceRef":"{source}"}}"""

        let population =
            $"""{{"kind":"budget-population","identity":"budget-population-main","itemId":"{item}","revision":638937000000000000,"originalItemId":"{item}","state":"completed","sourceKind":"native-item","sourceRef":"{source}"}}"""

        let batch =
            Encoding.UTF8.GetBytes
                $"""{{"schema":"{TelemetryStore.BatchSchema}","ingestId":"batch-main-delivery","sourceIdentity":"coordination","generation":"host-outcome-40000000-0000-0000-0000-000000000001","cursor":"main-delivery","eventCount":2,"events":[{outcome},{population}]}}"""

        TelemetryStoreApplication.ingest path approved batch |> unwrap |> ignore
        TelemetryStoreApplication.ingest path approved batch |> unwrap |> ignore
        use connection = new SqliteConnection($"Data Source=%s{Path.Combine(path, TelemetryStoreApplication.databaseFileName)};Pooling=False")
        connection.Open()
        use command = connection.CreateCommand()
        command.CommandText <-
            "SELECT (SELECT count(*) FROM native_item_outcomes WHERE item_id=$item AND source_kind='orchestration-delivery'),(SELECT count(*) FROM budget_population_facts WHERE item_id=$item AND state='completed');"
        command.Parameters.AddWithValue("$item", item) |> ignore
        use reader = command.ExecuteReader()
        Assert.True(reader.Read())
        Assert.Equal(1L, reader.GetInt64 0)
        Assert.Equal(1L, reader.GetInt64 1)


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

        Assert.Single(Directory.GetFiles(Path.Combine(path, "inbox", "worker-a"), "*.ready"))
        |> ignore

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

        TelemetryStoreApplication.ingest path approved (batch "batch-1" "usage-1" 0L "c1" 10L)
        |> unwrap
        |> ignore

        let before = TelemetryStoreApplication.summary path approved "UTEL-02" |> unwrap

        TelemetryStoreApplication.publish path approved (batch "batch-2" "usage-1" 0L "c2" 11L)
        |> unwrap
        |> ignore

        let result = TelemetryStoreApplication.drain path approved |> unwrap
        Assert.Contains("\"quarantined\":1", result)
        Assert.Equal(before, TelemetryStoreApplication.summary path approved "UTEL-02" |> unwrap)

        Assert.Single(Directory.GetFiles(Path.Combine(path, "quarantine"), "*.rejected", SearchOption.AllDirectories))
        |> ignore

    [<Fact>]
    let ``UTEL-DASH-08 coherent snapshot does not mix a concurrent correction`` () =
        let cleanup, path = root ()
        use cleanup = cleanup
        TelemetryStoreApplication.initialize path approved |> unwrap |> ignore

        TelemetryStoreApplication.ingest path approved (batch "snapshot-first" "usage-1" 0L "s1" 10L)
        |> unwrap
        |> ignore

        let hooks: TelemetryStoreApplication.DashboardSnapshotHooks =
            {
                AfterFirstRead =
                    fun () ->
                        TelemetryStoreApplication.ingest
                            path
                            approved
                            (batch "snapshot-correction" "usage-1" 1L "s2" 20L)
                        |> unwrap
                        |> ignore
            }

        let during =
            TelemetryStoreApplication.dashboardSnapshotWithHooks path approved hooks None
            |> unwrap

        use duringDocument = JsonDocument.Parse during

        let decodeSnapshot (document: JsonDocument) =
            let compressed =
                Convert.FromBase64String(document.RootElement.GetProperty("canonicalSnapshotGzip").GetString())

            use compressedStream = new MemoryStream(compressed)
            use decompressor = new GZipStream(compressedStream, CompressionMode.Decompress)
            JsonDocument.Parse decompressor

        use duringSnapshot = decodeSnapshot duringDocument
        let duringSummary = duringSnapshot.RootElement.GetProperty("summaries")[0]
        Assert.Equal(15L, duringSummary.GetProperty("usage").GetProperty("total").GetInt64())

        use afterDocument =
            JsonDocument.Parse(TelemetryStoreApplication.dashboardSnapshot path approved None |> unwrap)

        use afterSnapshot = decodeSnapshot afterDocument
        let afterSummary = afterSnapshot.RootElement.GetProperty("summaries")[0]
        Assert.Equal(25L, afterSummary.GetProperty("usage").GetProperty("total").GetInt64())

        Assert.False(
            duringDocument.RootElement.GetProperty("revision").GetString() =
                afterDocument.RootElement.GetProperty("revision").GetString()
        )

    [<Fact>]
    let ``scoped dashboard binds workspace and refuses unprovenanced batches`` () =
        let cleanup, path = root ()
        use cleanup = cleanup
        TelemetryStoreApplication.initialize path approved |> unwrap |> ignore

        let scope: TelemetryReceipt.Scope =
            {
                Workspace = "workspace-a"
                Producer = "producer-a"
                Stream = "runtime"
            }

        TelemetryStoreApplication.enrollReceiptProducer path approved scope
        |> unwrap
        |> ignore

        Assert.True(
            TelemetryStoreApplication.scopedDashboardSnapshot path approved scope.Workspace None
            |> Result.isOk
        )

        Assert.Equal(
            Error [ "projection-unavailable" ],
            TelemetryStoreApplication.scopedDashboardSnapshot path approved "workspace-b" None
        )

        match
            TelemetryStoreApplication.publish
                path
                approved
                (batch "legacy-after-enrollment" "usage-legacy" 0L "legacy" 10L)
        with
        | Error [ "scoped-store-requires-receipt-ingestion" ] -> ()
        | other -> failwithf "unexpected scoped publish result: %A" other

        use connection =
            new SqliteConnection(
                $"Data Source={Path.Combine(path, TelemetryStoreApplication.databaseFileName)};Pooling=False"
            )

        connection.Open()
        use command = connection.CreateCommand()

        command.CommandText <-
            "INSERT INTO ingest_batches(ingest_id,content_digest,source_identity,generation,cursor,accepted_count,replay_count) VALUES('legacy-unassigned','aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa','legacy','g1','c1',0,0);"

        command.ExecuteNonQuery() |> ignore

        Assert.Equal(
            Error [ "projection-unavailable" ],
            TelemetryStoreApplication.scopedDashboardSnapshot path approved scope.Workspace None
        )

    [<Fact>]
    let ``scoped dashboard accepts only applied receipt batch provenance`` () =
        let cleanup, path = root ()
        use cleanup = cleanup
        TelemetryStoreApplication.initialize path approved |> unwrap |> ignore

        let scope: TelemetryReceipt.Scope =
            {
                Workspace = "workspace-a"
                Producer = "producer-a"
                Stream = "runtime"
            }

        TelemetryStoreApplication.enrollReceiptProducer path approved scope
        |> unwrap
        |> ignore

        let envelope =
            Encoding.UTF8.GetBytes
                $"""{{"schema":"fsgg.telemetry.envelope/1","workspaceId":"{scope.Workspace}","producerId":"{scope.Producer}","streamId":"{scope.Stream}","batchId":"batch-a","payload":{{"schema":"{TelemetryStore.BatchSchema}","ingestId":"native-batch","sourceIdentity":"native-source","generation":"g1","cursor":"1","eventCount":1,"events":[{{"kind":"item","identity":"item-a","itemId":"item-a","revision":0}}]}}}}"""

        TelemetryReceipt.parse envelope |> unwrap |> ignore

        TelemetryStoreApplication.submitReceipt path approved scope envelope
        |> unwrap
        |> ignore

        TelemetryStoreApplication.drainReceipts path approved scope.Workspace
        |> unwrap
        |> ignore

        let rejectedEnvelope =
            Encoding.UTF8.GetBytes
                $"""{{"schema":"fsgg.telemetry.envelope/1","workspaceId":"{scope.Workspace}","producerId":"{scope.Producer}","streamId":"{scope.Stream}","batchId":"batch-b","payload":{{"schema":"{TelemetryStore.BatchSchema}","ingestId":"conflicting-native-batch","sourceIdentity":"native-source","generation":"g1","cursor":"2","eventCount":1,"events":[{{"kind":"item","identity":"item-a","itemId":"item-b","revision":0}}]}}}}"""

        TelemetryStoreApplication.submitReceipt path approved scope rejectedEnvelope
        |> unwrap
        |> ignore

        TelemetryStoreApplication.drainReceipts path approved scope.Workspace
        |> unwrap
        |> ignore

        use snapshot =
            JsonDocument.Parse(
                TelemetryStoreApplication.scopedDashboardSnapshot path approved scope.Workspace None
                |> unwrap
            )

        let operational = snapshot.RootElement.GetProperty("operational")
        Assert.Equal(0L, operational.GetProperty("pendingBatches").GetInt64())
        Assert.Equal(1L, operational.GetProperty("appliedReceipts").GetInt64())
        Assert.Equal(1L, operational.GetProperty("rejectedReceipts").GetInt64())
        Assert.Equal("database-transaction", operational.GetProperty("consistency").GetString())

    [<Fact>]
    let ``scoped provenance proof remains receipt-linear across pages`` () =
        let cleanup, path = root ()
        use cleanup = cleanup
        TelemetryStoreApplication.initialize path approved |> unwrap |> ignore

        let scope: TelemetryReceipt.Scope =
            {
                Workspace = "workspace-a"
                Producer = "producer-a"
                Stream = "runtime"
            }

        TelemetryStoreApplication.enrollReceiptProducer path approved scope
        |> unwrap
        |> ignore

        do
            use populated =
                new SqliteConnection(
                    $"Data Source={Path.Combine(path, TelemetryStoreApplication.databaseFileName)};Pooling=False"
                )

            populated.Open()
            use transaction = populated.BeginTransaction()

            for revision in 0..299 do
                let batchId = $"batch-{revision:D3}"
                let digest = String(char (int 'a' + revision % 6), 64)
                use receipt = populated.CreateCommand()
                receipt.Transaction <- transaction

                receipt.CommandText <-
                    "INSERT INTO transport_receipts(producer,batch,stream,digest,payload_bytes,state,code,terminal_utc) VALUES($producer,$batch,'runtime',$digest,1,'applied','applied','2026-09-10T00:00:00Z');"

                receipt.Parameters.AddWithValue("$producer", scope.Producer) |> ignore
                receipt.Parameters.AddWithValue("$batch", batchId) |> ignore
                receipt.Parameters.AddWithValue("$digest", digest) |> ignore
                receipt.ExecuteNonQuery() |> ignore
                use ingest = populated.CreateCommand()
                ingest.Transaction <- transaction

                ingest.CommandText <-
                    "INSERT INTO ingest_batches(ingest_id,content_digest,source_identity,generation,cursor,accepted_count,replay_count) VALUES($id,$digest,'native-source','g1',$cursor,1,0);"

                ingest.Parameters.AddWithValue("$id", "receipt-" + TelemetryReceipt.key scope.Producer batchId)
                |> ignore

                ingest.Parameters.AddWithValue("$digest", digest) |> ignore
                ingest.Parameters.AddWithValue("$cursor", string revision) |> ignore
                ingest.ExecuteNonQuery() |> ignore

            transaction.Commit()

        let mutable keyComputations = 0

        let hooks: TelemetryStoreApplication.ScopedDashboardSnapshotHooks =
            {
                AfterFirstRead = ignore
                ReceiptKeyComputed = fun () -> keyComputations <- keyComputations + 1
            }

        TelemetryStoreApplication.scopedDashboardSnapshotWithHooks path approved scope.Workspace hooks None
        |> unwrap
        |> ignore

        Assert.Equal(300, keyComputations)

        use connection =
            new SqliteConnection(
                $"Data Source={Path.Combine(path, TelemetryStoreApplication.databaseFileName)};Pooling=False"
            )

        connection.Open()
        use plan = connection.CreateCommand()

        plan.CommandText <-
            "EXPLAIN QUERY PLAN SELECT rowid,producer,batch,digest FROM transport_receipts NOT INDEXED WHERE rowid>$cursor AND state='applied' ORDER BY rowid LIMIT 256;"

        plan.Parameters.AddWithValue("$cursor", 0L) |> ignore
        use reader = plan.ExecuteReader()

        let details =
            [|
                while reader.Read() do
                    yield reader.GetString 3
            |]

        Assert.Contains(details, fun detail -> detail.Contains("INTEGER PRIMARY KEY", StringComparison.Ordinal))
        Assert.DoesNotContain(details, fun detail -> detail.Contains("TEMP B-TREE", StringComparison.Ordinal))

    [<Fact>]
    let ``receipt backup restores pending obligation into fresh root`` () =
        let cleanup, path = root ()
        use cleanup = cleanup
        let backup = path + "-backup"
        let restored = path + "-restored"

        try
            TelemetryStoreApplication.initialize path approved |> unwrap |> ignore

            let scope: TelemetryReceipt.Scope =
                {
                    Workspace = "workspace-a"
                    Producer = "producer-a"
                    Stream = "runtime"
                }

            TelemetryStoreApplication.provisionReceiptWorkspace path approved scope.Workspace
            |> unwrap
            |> ignore

            TelemetryStoreApplication.enrollReceiptProducer path approved scope
            |> unwrap
            |> ignore

            let envelope =
                Encoding.UTF8.GetBytes
                    $"""{{"schema":"fsgg.telemetry.envelope/1","workspaceId":"{scope.Workspace}","producerId":"{scope.Producer}","streamId":"{scope.Stream}","batchId":"batch-a","payload":{{"schema":"{TelemetryStore.BatchSchema}","ingestId":"native-batch","sourceIdentity":"native-source","generation":"g1","cursor":"1","eventCount":1,"events":[{{"kind":"item","identity":"item-a","itemId":"item-a","revision":0}}]}}}}"""

            TelemetryStoreApplication.submitReceipt path approved scope envelope
            |> unwrap
            |> ignore

            use pendingSnapshot =
                JsonDocument.Parse(
                    TelemetryStoreApplication.scopedDashboardSnapshot path approved scope.Workspace None
                    |> unwrap
                )

            Assert.Equal(
                1L,
                pendingSnapshot.RootElement.GetProperty("operational").GetProperty("pendingBatches").GetInt64()
            )

            TelemetryStoreApplication.backupReceiptStore path approved scope.Workspace backup
            |> unwrap
            |> ignore

            TelemetryStoreApplication.restoreReceiptStore backup restored approved scope.Workspace
            |> unwrap
            |> ignore

            let receipt =
                TelemetryStoreApplication.lookupReceipt restored approved scope "batch-a"
                |> unwrap

            Assert.Contains("\"status\":\"durably-received\"", receipt)

            TelemetryStoreApplication.drainReceipts restored approved scope.Workspace
            |> unwrap
            |> ignore

            Assert.True(
                TelemetryStoreApplication.scopedDashboardSnapshot restored approved scope.Workspace None
                |> Result.isOk
            )
        finally
            if Directory.Exists backup then
                Directory.Delete(backup, true)

            if Directory.Exists restored then
                Directory.Delete(restored, true)

    [<Fact>]
    let ``UTEL-02 live writer is never displaced and leaves ready batch queued`` () =
        if OperatingSystem.IsLinux() then
            let cleanup, path = root ()
            use cleanup = cleanup
            TelemetryStoreApplication.initialize path approved |> unwrap |> ignore

            TelemetryStoreApplication.publish path approved (batch "batch-1" "usage-1" 0L "c1" 10L)
            |> unwrap
            |> ignore

            let start = ProcessStartInfo("flock")
            start.ArgumentList.Add(Path.Combine(path, "writer.lock"))
            start.ArgumentList.Add("sleep")
            start.ArgumentList.Add("2")
            start.UseShellExecute <- false
            use holder = Process.Start start
            System.Threading.Thread.Sleep 150

            match TelemetryStoreApplication.drain path approved with
            | Error [ "writer-busy" ] -> ()
            | result -> failwithf "unexpected lock result: %A" result

            Assert.Single(Directory.GetFiles(Path.Combine(path, "inbox", "worker-a"), "*.ready"))
            |> ignore

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
        finally
            Environment.SetEnvironmentVariable("FSGG_TELEMETRY_STORE", previous)

    [<Fact>]
    let ``UTEL-02 crash boundaries retain ready replay and rollback before commit`` () =
        let cleanup, path = root ()
        use cleanup = cleanup
        TelemetryStoreApplication.initialize path approved |> unwrap |> ignore
        let payload = batch "batch-1" "usage-1" 0L "c1" 10L
        TelemetryStoreApplication.publish path approved payload |> unwrap |> ignore

        let beforeCommit: TelemetryStoreApplication.DrainHooks =
            {
                BeforeCommit = (fun () -> raise (OperationCanceledException "synthetic precommit termination"))
                AfterCommitBeforeDelete = fun () -> ()
            }

        Assert.Contains("precommit", sprintf "%A" (TelemetryStoreApplication.drainWithHooks path approved beforeCommit))
        Assert.Contains("\"total\":0", TelemetryStoreApplication.summary path approved "UTEL-02" |> unwrap)

        Assert.Single(Directory.GetFiles(Path.Combine(path, "inbox", "worker-a"), "*.ready"))
        |> ignore

        let afterCommit: TelemetryStoreApplication.DrainHooks =
            {
                BeforeCommit = fun () -> ()
                AfterCommitBeforeDelete = fun () -> raise (IOException "synthetic postcommit termination")
            }

        Assert.Contains(
            "committed but ready removal failed",
            sprintf "%A" (TelemetryStoreApplication.drainWithHooks path approved afterCommit)
        )

        let committed = TelemetryStoreApplication.summary path approved "UTEL-02" |> unwrap
        Assert.Contains("\"total\":15", committed)

        Assert.Single(Directory.GetFiles(Path.Combine(path, "inbox", "worker-a"), "*.ready"))
        |> ignore

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

        Assert.Contains(
            "\"populationCoverage\":\"unknown\"",
            TelemetryStoreApplication.summary path approved "UTEL-02" |> unwrap
        )

    [<Fact>]
    let ``UTEL-02 newer schema leaves immutable inbox and public all-item export is bounded`` () =
        let cleanup, path = root ()
        use cleanup = cleanup
        TelemetryStoreApplication.initialize path approved |> unwrap |> ignore
        let payload = batch "batch-1" "usage-1" 0L "c1" 10L
        TelemetryStoreApplication.publish path approved payload |> unwrap |> ignore
        pragma path 15
        Assert.Contains("newer than supported", sprintf "%A" (TelemetryStoreApplication.drain path approved))

        Assert.Single(Directory.GetFiles(Path.Combine(path, "inbox", "worker-a"), "*.ready"))
        |> ignore

        pragma path 14
        TelemetryStoreApplication.drain path approved |> unwrap |> ignore

        let output =
            Path.Combine(Path.GetTempPath(), "fsgg-utel02-public-" + Guid.NewGuid().ToString("N") + ".json")

        try
            TelemetryStoreApplication.exportPublic path approved None output
            |> unwrap
            |> ignore

            let exported = File.ReadAllText output
            Assert.Contains("fsgg.telemetry.public-export/1", exported)
            Assert.Contains("UTEL-02", exported)
            Assert.True(FileInfo(output).Length <= 65536L)

            Assert.Contains(
                "private store root",
                sprintf
                    "%A"
                    (TelemetryStoreApplication.exportPublic path approved None (Path.Combine(path, "unsafe.json")))
            )
        finally
            if File.Exists output then
                File.Delete output

    [<Fact>]
    let ``UTEL-02 producer inbox rejects the 129th pending batch`` () =
        let cleanup, path = root ()
        use cleanup = cleanup
        TelemetryStoreApplication.initialize path approved |> unwrap |> ignore

        for index in 1..128 do
            TelemetryStoreApplication.publish
                path
                approved
                (batch ($"batch-%03d{index}") "usage-1" 0L ($"c%d{index}") 10L)
            |> unwrap
            |> ignore

        Assert.Contains(
            "inbox is full",
            sprintf "%A" (TelemetryStoreApplication.publish path approved (batch "batch-129" "usage-1" 0L "c129" 10L))
        )

    [<Fact>]
    let ``UTEL-02 command shapes include publish drain and item-optional public export`` () =
        Assert.Equal(
            Some(Ok()),
            TelemetryApplication.validateInvocation [ "telemetry"; "store"; "publish"; "--input"; "batch.json" ]
        )

        Assert.Equal(Some(Ok()), TelemetryApplication.validateInvocation [ "telemetry"; "store"; "drain" ])

        Assert.Equal(
            Some(Ok()),
            TelemetryApplication.validateInvocation
                [ "telemetry"; "store"; "export"; "--public"; "--output"; "public.json" ]
        )

        Assert.Equal(
            Some(Ok()),
            TelemetryApplication.validateInvocation [ "telemetry"; "review"; "summary"; "--item"; "UTEL-08" ]
        )

        Assert.Equal(
            Some(Ok()),
            TelemetryApplication.validateInvocation [ "telemetry"; "item-detail"; "--item"; "UTEL-08" ]
        )

        Assert.Equal(
            Some(Ok()),
            TelemetryApplication.validateInvocation [ "telemetry"; "item-detail"; "--format-version"; "2"; "--all" ]
        )

    [<Fact>]
    let ``UTEL-02 changed unsafe permissions refuse before worker publication`` () =
        if not (OperatingSystem.IsWindows()) then
            let cleanup, path = root ()
            use cleanup = cleanup
            TelemetryStoreApplication.initialize path approved |> unwrap |> ignore

            File.SetUnixFileMode(
                path,
                UnixFileMode.UserRead
                ||| UnixFileMode.UserWrite
                ||| UnixFileMode.UserExecute
                ||| UnixFileMode.GroupWrite
            )

            try
                Assert.Contains(
                    "permissions",
                    sprintf
                        "%A"
                        (TelemetryStoreApplication.publish path approved (batch "batch-1" "usage-1" 0L "c1" 10L))
                )

                Assert.False(Directory.Exists(Path.Combine(path, "inbox")))
            finally
                File.SetUnixFileMode(
                    path,
                    UnixFileMode.UserRead ||| UnixFileMode.UserWrite ||| UnixFileMode.UserExecute
                )

    [<Fact>]
    let ``UTEL-04A CI observations migrate ingest and summarize without private fields`` () =
        let cleanup, path = root ()
        use cleanup = cleanup
        Assert.Contains("\"schemaVersion\":14", TelemetryStoreApplication.initialize path approved |> unwrap)

        let ci =
            Encoding.UTF8.GetBytes
                $"""{{"schema":"{TelemetryStore.BatchSchema}","ingestId":"ci-batch-1","sourceIdentity":"ci-worker","generation":"g1","cursor":"1","eventCount":4,"events":[{{"kind":"ci-binding","identity":"ci-binding-1","itemId":"UTEL-04A","revision":0,"collectionId":"collection-1","repository":"o/r","head":"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa","prNumber":1,"workflow":"ci.yml","featureId":"UTEL","attemptId":"a1","parentAttemptId":null,"producerStream":"ci-worker","binding":"exact"}},{{"kind":"ci-job","identity":"ci-job-1","itemId":"UTEL-04A","revision":0,"repository":"o/r","runId":10,"attempt":1,"jobId":101,"name":"build","status":"completed","conclusion":"success","createdAt":"2026-01-01T00:00:00Z","startedAt":"2026-01-01T00:00:00Z","completedAt":"2026-01-01T00:01:00Z"}},{{"kind":"ci-step","identity":"ci-step-1","itemId":"UTEL-04A","revision":0,"repository":"o/r","runId":10,"attempt":1,"jobId":101,"number":1,"name":"test","status":"completed","conclusion":"success","startedAt":"2026-01-01T00:00:10Z","completedAt":"2026-01-01T00:00:50Z","classification":"useful-validation","rationale":"exact fixture"}},{{"kind":"ci-coverage","identity":"ci-coverage-1","itemId":"UTEL-04A","revision":0,"collectionId":"collection-1","inventory":"complete","attempts":"complete","jobPages":"complete","terminal":"complete","timestamps":"complete","lineage":"complete","classification":"complete","criticalPath":"unknown"}}]}}"""

        TelemetryStoreApplication.ingest path approved ci |> unwrap |> ignore
        let summary = TelemetryStoreApplication.ciSummary path approved "UTEL-04A" |> unwrap
        Assert.Contains("\"runnerSeconds\":60", summary)
        Assert.Contains("\"usefulValidationSeconds\":40", summary)
        Assert.Contains("\"criticalPathCoverage\":\"unknown\"", summary)
        Assert.DoesNotContain("ci-worker", summary)
        Assert.DoesNotContain("exact fixture", summary)

    [<Fact>]
    let ``UTEL-04A binding correction preserves collected child evidence`` () =
        let cleanup, path = root ()
        use cleanup = cleanup
        TelemetryStoreApplication.initialize path approved |> unwrap |> ignore

        let first =
            Encoding.UTF8.GetBytes
                $"""{{"schema":"{TelemetryStore.BatchSchema}","ingestId":"ci-parent-first","sourceIdentity":"ci-worker","generation":"g1","cursor":"1","eventCount":3,"events":[{{"kind":"ci-binding","identity":"ci-parent-binding","itemId":"UTEL-04A","revision":0,"collectionId":"ci-parent-collection","repository":"o/r","head":"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa","prNumber":1,"workflow":"ci.yml","featureId":"UTEL","attemptId":"a1","parentAttemptId":null,"producerStream":"ci-worker","binding":"exact"}},{{"kind":"ci-page","identity":"ci-parent-page","itemId":"UTEL-04A","revision":0,"collectionId":"ci-parent-collection","resource":"runs","page":1,"count":1,"total":1}},{{"kind":"ci-coverage","identity":"ci-parent-coverage","itemId":"UTEL-04A","revision":0,"collectionId":"ci-parent-collection","inventory":"complete","attempts":"complete","jobPages":"complete","terminal":"complete","timestamps":"complete","lineage":"complete","classification":"complete","criticalPath":"unknown"}}]}}"""

        Assert.Contains("\"accepted\":3", TelemetryStoreApplication.ingest path approved first |> unwrap)

        let corrected =
            Encoding.UTF8
                .GetString(first)
                .Replace("ci-parent-first", "ci-parent-corrected")
                .Replace("\"cursor\":\"1\"", "\"cursor\":\"2\"")
                .Replace(
                    "\"identity\":\"ci-parent-binding\",\"itemId\":\"UTEL-04A\",\"revision\":0",
                    "\"identity\":\"ci-parent-binding\",\"itemId\":\"UTEL-04A\",\"revision\":1"
                )
                .Replace("\"producerStream\":\"ci-worker\"", "\"producerStream\":\"routine-delivery\"")
            |> Encoding.UTF8.GetBytes

        Assert.Contains("\"accepted\":1", TelemetryStoreApplication.ingest path approved corrected |> unwrap)

        use connection =
            new SqliteConnection(
                $"Data Source=%s{Path.Combine(path, TelemetryStoreApplication.databaseFileName)};Pooling=False"
            )

        connection.Open()
        use command = connection.CreateCommand()

        command.CommandText <-
            "SELECT (SELECT producer_stream FROM ci_bindings WHERE identity='ci-parent-binding'),(SELECT count(*) FROM ci_pages WHERE collection_id='ci-parent-collection'),(SELECT count(*) FROM ci_coverage WHERE collection_id='ci-parent-collection');"

        use reader = command.ExecuteReader()
        Assert.True(reader.Read())
        Assert.Equal("routine-delivery", reader.GetString(0))
        Assert.Equal(1L, reader.GetInt64(1))
        Assert.Equal(1L, reader.GetInt64(2))

    [<Fact>]
    let ``UTEL-04A command shapes require explicit selected population arguments`` () =
        Assert.Equal(
            Some(Ok()),
            TelemetryApplication.validateInvocation
                [
                    "telemetry"
                    "ci"
                    "collect"
                    "--assignment"
                    "/private/a.json"
                    "--repo"
                    "o/r"
                    "--pr"
                    "1"
                    "--head"
                    String.replicate 40 "a"
                    "--workflow"
                    "ci.yml"
                    "--store-root"
                    "/durable/store"
                ]
        )

        Assert.Equal(
            Some(Ok()),
            TelemetryApplication.validateInvocation [ "telemetry"; "ci"; "summary"; "--item"; "UTEL-04A" ]
        )

        Assert.Equal(
            Some(Ok()),
            TelemetryApplication.validateInvocation
                [
                    "telemetry"
                    "ci"
                    "reconcile"
                    "--assignment"
                    "/private/a.json"
                    "--delivery"
                    "/tmp/public.json"
                    "--store-root"
                    "/durable/store"
                ]
        )

    [<Fact>]
    let ``UTEL-06C first admission accepts only explicit eligible exact-head candidates`` () =
        let head = String.replicate 40 "a"
        Assert.True(TelemetryCiApplication.canCreateAdmission "ready" "not-delivered" (Some head) head)
        Assert.False(TelemetryCiApplication.canCreateAdmission "refused" "not-delivered" (Some head) head)
        Assert.False(TelemetryCiApplication.canCreateAdmission "ready" "delivered" (Some head) head)
        Assert.False(TelemetryCiApplication.canCreateAdmission "ready" "not-delivered" None head)

        Assert.False(
            TelemetryCiApplication.canCreateAdmission "ready" "not-delivered" (Some(String.replicate 40 "b")) head
        )

    [<Fact>]
    let ``UTEL-05A exact thresholds derive pass breach and one severe intervention`` () =
        let cleanup, path = root ()
        use cleanup = cleanup
        TelemetryStoreApplication.initialize path approved |> unwrap |> ignore

        let facts =
            [
                for item, numerator in [ "budget-10", 10L; "budget-11", 11L; "budget-25", 25L; "budget-26", 26L ] do
                    yield population item 0 "completed" ($"native:{item}")

                    yield
                        attribution
                            item
                            0
                            (Some numerator)
                            (Some 100L)
                            "complete"
                            "classified"
                            "runtime"
                            ($"usage:{item}")
            ]

        TelemetryStoreApplication.ingest path approved (budgetBatch "budget-boundaries" "1" facts)
        |> unwrap
        |> ignore

        Assert.Contains(
            "\"verdict\":\"pass\"",
            TelemetryStoreApplication.budgetSummary path approved "budget-10" |> unwrap
        )

        Assert.Contains(
            "\"verdict\":\"breach\"",
            TelemetryStoreApplication.budgetSummary path approved "budget-11" |> unwrap
        )

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
            [
                for number in 1..14 do
                    let item = $"budget-item-%02d{number}"
                    yield population item 0 "completed" ($"native:{item}")
                    yield attribution item 0 (Some 11L) (Some 100L) "complete" "classified" "runtime" ($"usage:{item}")
            ]

        TelemetryStoreApplication.ingest path approved (budgetBatch "budget-14" "1" facts)
        |> unwrap
        |> ignore

        let before = TelemetryStoreApplication.budgetStatus path approved |> unwrap
        Assert.Contains("\"distinctBreaches\":14", before)
        Assert.Contains("\"intervention\":\"none\"", before)

        let item15 =
            [
                population "budget-item-15" 0 "completed" "native:budget-item-15"
                attribution
                    "budget-item-15"
                    0
                    (Some 11L)
                    (Some 100L)
                    "complete"
                    "classified"
                    "runtime"
                    "usage:budget-item-15"
            ]

        TelemetryStoreApplication.ingest path approved (budgetBatch "budget-15" "2" item15)
        |> unwrap
        |> ignore

        Assert.Contains("\"intervention\":\"open\"", TelemetryStoreApplication.budgetStatus path approved |> unwrap)

        TelemetryStoreApplication.ingest path approved (budgetBatch "budget-15-replay" "3" item15)
        |> unwrap
        |> ignore

        Assert.Contains("\"distinctBreaches\":15", TelemetryStoreApplication.budgetStatus path approved |> unwrap)

        let good =
            [
                population "budget-item-good" 0 "completed" "native:budget-item-good"
                attribution
                    "budget-item-good"
                    0
                    (Some 5L)
                    (Some 100L)
                    "complete"
                    "classified"
                    "runtime"
                    "usage:budget-item-good"
            ]

        TelemetryStoreApplication.ingest path approved (budgetBatch "budget-good-after-open" "3b" good)
        |> unwrap
        |> ignore

        Assert.Contains("\"intervention\":\"open\"", TelemetryStoreApplication.budgetStatus path approved |> unwrap)

        let evidence =
            [
                """{"kind":"budget-intervention","identity":"deploy-epoch-1","itemId":"budget-item-15","revision":0,"interventionId":"epoch-1-intervention","transition":"deployed","sequence":10,"result":"not-evaluated","coverage":"complete","sourceRef":"deploy:1"}"""
                """{"kind":"budget-intervention","identity":"verify-epoch-1","itemId":"budget-item-15","revision":0,"interventionId":"epoch-1-intervention","transition":"verified","sequence":11,"result":"improved","coverage":"complete","sourceRef":"verify:1"}"""
            ]

        TelemetryStoreApplication.ingest path approved (budgetBatch "budget-reset" "4" evidence)
        |> unwrap
        |> ignore

        let after = TelemetryStoreApplication.budgetStatus path approved |> unwrap
        Assert.Contains("\"epoch\":\"epoch-2\"", after)
        Assert.Contains("\"distinctBreaches\":0", after)
        Assert.Contains("\"intervention\":\"none\"", after)

        TelemetryStoreApplication.ingest path approved (budgetBatch "budget-old-replay" "5" item15)
        |> unwrap
        |> ignore

        Assert.Contains("\"epoch\":\"epoch-2\"", TelemetryStoreApplication.budgetStatus path approved |> unwrap)

    [<Fact>]
    let ``UTEL-05A incomplete mixed and gapped dimensions remain unknown independently`` () =
        let cleanup, path = root ()
        use cleanup = cleanup
        TelemetryStoreApplication.initialize path approved |> unwrap |> ignore

        let facts =
            [
                population "budget-unknown" 0 "completed" "native:budget-unknown"
                attribution "budget-unknown" 0 (Some 99L) None "complete" "classified" "runtime" "usage:missing"
                attribution "budget-mixed" 0 (Some 99L) (Some 100L) "complete" "mixed" "runtime" "usage:mixed"
                population "budget-mixed" 0 "completed" "native:budget-mixed"
                """{"kind":"runtime-gap","identity":"gap-budget","itemId":"budget-gap","revision":0,"invocationId":"invoke-budget","code":"framing-loss"}"""
                population "budget-gap" 0 "completed" "native:budget-gap"
                attribution "budget-gap" 0 (Some 99L) (Some 100L) "complete" "classified" "runtime" "usage:gap"
                population "budget-open" 0 "open" "native:budget-open"
                attribution "budget-open" 0 (Some 100L) (Some 100L) "complete" "classified" "runtime" "usage:prefix"
            ]

        TelemetryStoreApplication.ingest path approved (budgetBatch "budget-unknowns" "1" facts)
        |> unwrap
        |> ignore

        Assert.Contains(
            "\"verdict\":\"unknown\"",
            TelemetryStoreApplication.budgetSummary path approved "budget-unknown" |> unwrap
        )

        Assert.Contains(
            "attribution-unknown",
            TelemetryStoreApplication.budgetSummary path approved "budget-mixed" |> unwrap
        )

        Assert.Contains("runtime-gap", TelemetryStoreApplication.budgetSummary path approved "budget-gap" |> unwrap)

        Assert.Contains(
            "\"dimensions\":[]",
            TelemetryStoreApplication.budgetSummary path approved "budget-open" |> unwrap
        )

        Assert.Contains("\"distinctBreaches\":0", TelemetryStoreApplication.budgetStatus path approved |> unwrap)

        TelemetryStoreApplication.ingest
            path
            approved
            (budgetBatch
                "budget-open-completed"
                "2"
                [ population "budget-open" 1 "completed" "native:budget-open-completed" ])
        |> unwrap
        |> ignore

        Assert.Contains(
            "\"verdict\":\"breach\"",
            TelemetryStoreApplication.budgetSummary path approved "budget-open" |> unwrap
        )

        Assert.Contains(
            "\"epoch\":\"epoch-1\"",
            TelemetryStoreApplication.budgetSummary path approved "budget-open" |> unwrap
        )

    [<Fact>]
    let ``UTEL-05A interval union excludes witnessed useful work without rounding`` () =
        let cleanup, path = root ()
        use cleanup = cleanup
        TelemetryStoreApplication.initialize path approved |> unwrap |> ignore

        let interval identity classification startAt endAt =
            $"""{{"kind":"budget-interval","identity":"{identity}","itemId":"budget-interval","revision":0,"dimension":"owner-time","classification":"{classification}","startNanoseconds":{startAt},"endNanoseconds":{endAt},"witnessed":true,"sourceKind":"ci","sourceRef":"interval:{identity}"}}"""

        let facts =
            [
                population "budget-interval" 0 "completed" "native:budget-interval"
                """{"kind":"budget-attribution","identity":"attribution-budget-interval","itemId":"budget-interval","revision":0,"dimension":"owner-time","provider":"human","accountingScope":"whole-item","numerator":null,"denominator":400000000000,"coverage":"complete","attribution":"classified","sourceKind":"ci","sourceRef":"owner:denominator"}"""
                interval "wait-1" "administrative" 0L 120000000000L
                interval "wait-2" "administrative" 30000000000L 90000000000L
                interval "useful" "useful" 20000000000L 100000000000L
            ]

        TelemetryStoreApplication.ingest path approved (budgetBatch "budget-intervals" "1" facts)
        |> unwrap
        |> ignore

        let summary =
            TelemetryStoreApplication.budgetSummary path approved "budget-interval"
            |> unwrap

        Assert.Contains("\"numerator\":40000000000", summary)
        Assert.Contains("\"verdict\":\"pass\"", summary)

    [<Fact>]
    let ``UTEL-05A command surface is read-only`` () =
        Assert.Equal(
            Some(Ok()),
            TelemetryApplication.validateInvocation [ "telemetry"; "budget"; "summary"; "--item"; "UTEL-05A" ]
        )

        Assert.Equal(Some(Ok()), TelemetryApplication.validateInvocation [ "telemetry"; "budget"; "status" ])

        Assert.True(
            TelemetryApplication.validateInvocation [ "telemetry"; "budget"; "reset" ]
            |> Option.exists Result.isError
        )

    [<Fact>]
    let ``UTEL-05A correction removes a mistaken breach and follow-up does not duplicate the item`` () =
        let cleanup, path = root ()
        use cleanup = cleanup
        TelemetryStoreApplication.initialize path approved |> unwrap |> ignore
        let item = "budget-correction"

        let ingest revision numerator source cursor =
            [
                population item 0 "completed" "native:budget-correction"
                attribution item revision (Some numerator) (Some 100L) "complete" "classified" "runtime" source
            ]
            |> budgetBatch ($"budget-correction-{revision}") cursor
            |> TelemetryStoreApplication.ingest path approved
            |> unwrap
            |> ignore

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

        let severe =
            [
                population "budget-reset-order" 0 "completed" "native:budget-reset-order"
                attribution
                    "budget-reset-order"
                    0
                    (Some 26L)
                    (Some 100L)
                    "complete"
                    "classified"
                    "runtime"
                    "usage:reset-order"
            ]

        TelemetryStoreApplication.ingest path approved (budgetBatch "budget-open-reset" "1" severe)
        |> unwrap
        |> ignore

        let verify revision sequence result coverage source =
            $"""{{"kind":"budget-intervention","identity":"verify-reset-order","itemId":"budget-reset-order","revision":{revision},"interventionId":"epoch-1-intervention","transition":"verified","sequence":{sequence},"result":"{result}","coverage":"{coverage}","sourceRef":"{source}"}}"""

        let deploy =
            """{"kind":"budget-intervention","identity":"deploy-reset-order","itemId":"budget-reset-order","revision":0,"interventionId":"epoch-1-intervention","transition":"deployed","sequence":2,"result":"not-evaluated","coverage":"complete","sourceRef":"deploy:reset-order"}"""

        TelemetryStoreApplication.ingest
            path
            approved
            (budgetBatch "verify-before-deploy" "2" [ verify 0 1L "improved" "complete" "verify:early" ])
        |> unwrap
        |> ignore

        TelemetryStoreApplication.ingest path approved (budgetBatch "deployment-only" "3" [ deploy ])
        |> unwrap
        |> ignore

        Assert.Contains("\"epoch\":\"epoch-1\"", TelemetryStoreApplication.budgetStatus path approved |> unwrap)

        TelemetryStoreApplication.ingest
            path
            approved
            (budgetBatch "failed-verification" "4" [ verify 1 3L "failed" "complete" "verify:failed" ])
        |> unwrap
        |> ignore

        Assert.Contains("\"epoch\":\"epoch-1\"", TelemetryStoreApplication.budgetStatus path approved |> unwrap)

        TelemetryStoreApplication.ingest
            path
            approved
            (budgetBatch "unknown-verification" "5" [ verify 2 4L "improved" "unknown" "verify:unknown" ])
        |> unwrap
        |> ignore

        Assert.Contains("\"epoch\":\"epoch-1\"", TelemetryStoreApplication.budgetStatus path approved |> unwrap)

        TelemetryStoreApplication.ingest
            path
            approved
            (budgetBatch "valid-verification" "6" [ verify 3 5L "improved" "complete" "verify:valid" ])
        |> unwrap
        |> ignore

        Assert.Contains("\"epoch\":\"epoch-2\"", TelemetryStoreApplication.budgetStatus path approved |> unwrap)

    [<Fact>]
    let ``UTEL-05A assessment and ingestion share crash atomicity`` () =
        let cleanup, path = root ()
        use cleanup = cleanup
        TelemetryStoreApplication.initialize path approved |> unwrap |> ignore

        let payload =
            budgetBatch
                "budget-crash"
                "1"
                [
                    population "budget-crash" 0 "completed" "native:budget-crash"
                    attribution
                        "budget-crash"
                        0
                        (Some 11L)
                        (Some 100L)
                        "complete"
                        "classified"
                        "runtime"
                        "usage:budget-crash"
                ]

        TelemetryStoreApplication.publish path approved payload |> unwrap |> ignore

        let precommit: TelemetryStoreApplication.DrainHooks =
            {
                BeforeCommit = (fun () -> raise (OperationCanceledException "before assessment commit"))
                AfterCommitBeforeDelete = ignore
            }

        Assert.True(
            TelemetryStoreApplication.drainWithHooks path approved precommit
            |> Result.isError
        )

        Assert.Contains("\"distinctBreaches\":0", TelemetryStoreApplication.budgetStatus path approved |> unwrap)

        let postcommit: TelemetryStoreApplication.DrainHooks =
            {
                BeforeCommit = ignore
                AfterCommitBeforeDelete = (fun () -> raise (IOException "after assessment commit"))
            }

        Assert.True(
            TelemetryStoreApplication.drainWithHooks path approved postcommit
            |> Result.isError
        )

        Assert.Contains("\"distinctBreaches\":1", TelemetryStoreApplication.budgetStatus path approved |> unwrap)
        Assert.Contains("\"replayed\":2", TelemetryStoreApplication.drain path approved |> unwrap)
        Assert.Contains("\"distinctBreaches\":1", TelemetryStoreApplication.budgetStatus path approved |> unwrap)

    [<Fact>]
    let ``UTEL-05A dimension usability is independent and shared references cannot overlap`` () =
        let cleanup, path = root ()
        use cleanup = cleanup
        TelemetryStoreApplication.initialize path approved |> unwrap |> ignore

        let facts =
            [
                population "budget-dimensions" 0 "completed" "native:budget-dimensions"
                attribution "budget-dimensions" 0 (Some 11L) (Some 100L) "complete" "classified" "runtime" "usage:model"
                """{"kind":"budget-attribution","identity":"owner-budget-dimensions","itemId":"budget-dimensions","revision":0,"dimension":"owner-effort","provider":"human","accountingScope":"whole-item","numerator":null,"denominator":null,"coverage":"unknown","attribution":"unclassified","sourceKind":"native-item","sourceRef":"effort:owner"}"""
            ]

        TelemetryStoreApplication.ingest path approved (budgetBatch "budget-dimensions" "1" facts)
        |> unwrap
        |> ignore

        let summary =
            TelemetryStoreApplication.budgetSummary path approved "budget-dimensions"
            |> unwrap

        Assert.Contains("\"verdict\":\"breach\"", summary)
        Assert.Contains("\"verdict\":\"unknown\"", summary)
        Assert.Contains("\"distinctBreaches\":1", TelemetryStoreApplication.budgetStatus path approved |> unwrap)

        let overlap =
            [
                population "budget-overlap" 0 "completed" "native:budget-overlap"
                attribution "budget-overlap" 0 (Some 6L) (Some 100L) "complete" "classified" "runtime" "shared:cost"
                """{"kind":"budget-attribution","identity":"legacy-budget-overlap","itemId":"budget-overlap","revision":0,"dimension":"model-tokens","provider":"openai","accountingScope":"legacy-whole-item","numerator":6,"denominator":100,"coverage":"complete","attribution":"classified","sourceKind":"legacy","sourceRef":"shared:cost"}"""
            ]

        TelemetryStoreApplication.publish path approved (budgetBatch "budget-overlap" "2" overlap)
        |> unwrap
        |> ignore

        Assert.Contains("\"quarantined\":1", TelemetryStoreApplication.drain path approved |> unwrap)

        Assert.Contains(
            "\"dimensions\":[]",
            TelemetryStoreApplication.budgetSummary path approved "budget-overlap" |> unwrap
        )

    [<Fact>]
    let ``UTEL-05A reevaluation leaves work beyond thirty two items for the next drain`` () =
        let cleanup, path = root ()
        use cleanup = cleanup
        TelemetryStoreApplication.initialize path approved |> unwrap |> ignore

        let first =
            [
                for number in 1..32 do
                    let item = $"budget-bound-%02d{number}"
                    yield population item 0 "completed" ($"native:{item}")
                    yield attribution item 0 (Some 10L) (Some 100L) "complete" "classified" "runtime" ($"usage:{item}")
            ]

        let item33 =
            [
                population "budget-bound-33" 0 "completed" "native:budget-bound-33"
                attribution
                    "budget-bound-33"
                    0
                    (Some 10L)
                    (Some 100L)
                    "complete"
                    "classified"
                    "runtime"
                    "usage:budget-bound-33"
            ]

        TelemetryStoreApplication.publish path approved (budgetBatch "budget-bound-first" "1" first)
        |> unwrap
        |> ignore

        TelemetryStoreApplication.publish path approved (budgetBatch "budget-bound-second" "2" item33)
        |> unwrap
        |> ignore

        TelemetryStoreApplication.drain path approved |> unwrap |> ignore
        Assert.Contains("\"dirtyItems\":1", TelemetryStoreApplication.budgetStatus path approved |> unwrap)

        Assert.Contains(
            "\"dimensions\":[]",
            TelemetryStoreApplication.budgetSummary path approved "budget-bound-33"
            |> unwrap
        )

        TelemetryStoreApplication.drain path approved |> unwrap |> ignore
        Assert.Contains("\"dirtyItems\":0", TelemetryStoreApplication.budgetStatus path approved |> unwrap)

        Assert.Contains(
            "\"verdict\":\"pass\"",
            TelemetryStoreApplication.budgetSummary path approved "budget-bound-33"
            |> unwrap
        )

    [<Fact>]
    let ``UTEL-05A partial CI lineage cannot become a budget verdict`` () =
        let cleanup, path = root ()
        use cleanup = cleanup
        TelemetryStoreApplication.initialize path approved |> unwrap |> ignore

        let facts =
            [
                """{"kind":"ci-binding","identity":"budget-ci-binding","itemId":"budget-ci","revision":0,"collectionId":"budget-ci-collection","repository":"o/r","head":"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa","prNumber":1,"workflow":"ci.yml","featureId":"UTEL","attemptId":"a1","parentAttemptId":null,"producerStream":"budget-worker","binding":"exact"}"""
                """{"kind":"ci-coverage","identity":"budget-ci-coverage","itemId":"budget-ci","revision":0,"collectionId":"budget-ci-collection","inventory":"complete","attempts":"complete","jobPages":"partial","terminal":"complete","timestamps":"complete","lineage":"unknown","classification":"complete","criticalPath":"unknown"}"""
                population "budget-ci" 0 "completed" "native:budget-ci"
                attribution "budget-ci" 0 (Some 99L) (Some 100L) "complete" "classified" "ci" "ci:budget"
            ]

        TelemetryStoreApplication.ingest path approved (budgetBatch "budget-ci-partial" "1" facts)
        |> unwrap
        |> ignore

        let summary =
            TelemetryStoreApplication.budgetSummary path approved "budget-ci" |> unwrap

        Assert.Contains("\"verdict\":\"unknown\"", summary)
        Assert.Contains("ci-coverage", summary)

    [<Fact>]
    let ``UTEL-05A migration checksum is verified`` () =
        let cleanup, path = root ()
        use cleanup = cleanup
        TelemetryStoreApplication.initialize path approved |> unwrap |> ignore

        use connection =
            new SqliteConnection(
                $"Data Source=%s{Path.Combine(path, TelemetryStoreApplication.databaseFileName)};Pooling=False"
            )

        connection.Open()
        use corrupt = connection.CreateCommand()
        corrupt.CommandText <- "UPDATE schema_migrations SET digest='corrupt' WHERE version=4;"
        corrupt.ExecuteNonQuery() |> ignore
        connection.Close()
        Assert.Contains("migration checksum mismatch", sprintf "%A" (TelemetryStoreApplication.status path approved))

        Assert.Contains(
            "migration checksum mismatch",
            sprintf "%A" (TelemetryStoreApplication.initialize path approved)
        )

    [<Fact>]
    let ``UTEL-06A root child and follow-up dispatch identities reconcile`` () =
        let cleanup, path = root ()
        use cleanup = cleanup
        TelemetryStoreApplication.initialize path approved |> unwrap |> ignore
        let item = "operational-tree"

        let facts =
            [
                activation item "codex-exec" 60L
                dispatch item "dispatch-root" "root" None "codex-exec" "01"
                lineage item "lineage-root" "dispatch-root" "invoke-root" "root" None "invoke-root" "codex-exec"
                yield! completeTimes item "root" "invoke-root" "01" "05"
                dispatch item "dispatch-child" "child" (Some "dispatch-root") "codex-exec" "02"
                lineage
                    item
                    "lineage-child"
                    "dispatch-child"
                    "invoke-child"
                    "child"
                    (Some "invoke-root")
                    "invoke-root"
                    "codex-exec"
                yield! completeTimes item "child" "invoke-child" "02" "05"
                dispatch item "dispatch-follow" "follow-up" (Some "dispatch-child") "codex-exec" "03"
                lineage
                    item
                    "lineage-follow"
                    "dispatch-follow"
                    "invoke-follow"
                    "follow-up"
                    (Some "invoke-child")
                    "invoke-root"
                    "codex-exec"
                yield! completeTimes item "follow" "invoke-follow" "03" "05"
            ]

        TelemetryStoreApplication.ingest path approved (operationalBatch "operational-tree" item facts)
        |> unwrap
        |> ignore

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
            [
                activation missing "codex-exec" 60L
                dispatch missing "dispatch-child" "child" (Some "dispatch-absent") "codex-exec" "01"
                lineage
                    missing
                    "lineage-child"
                    "dispatch-child"
                    "invoke-child"
                    "child"
                    (Some "invoke-absent")
                    "invoke-root"
                    "codex-exec"
            ]

        TelemetryStoreApplication.ingest path approved (operationalBatch "operational-missing" missing missingFacts)
        |> unwrap
        |> ignore

        Assert.Contains("missing-parent", TelemetryStoreApplication.reconcile path approved missing |> unwrap)
        let conflict = "operational-conflict"

        let conflictFacts =
            [
                activation conflict "codex-exec" 60L
                dispatch conflict "dispatch-root" "root" None "codex-exec" "01"
                lineage conflict "lineage-root-a" "dispatch-root" "invoke-a" "root" None "invoke-a" "codex-exec"
                lineage conflict "lineage-root-b" "dispatch-root" "invoke-b" "root" None "invoke-b" "codex-exec"
            ]

        TelemetryStoreApplication.ingest path approved (operationalBatch "operational-conflict" conflict conflictFacts)
        |> unwrap
        |> ignore

        Assert.Contains("conflicting-identity", TelemetryStoreApplication.reconcile path approved conflict |> unwrap)

    [<Fact>]
    let ``UTEL-06A cyclic lineage and unsupported runtimes cannot match`` () =
        let cleanup, path = root ()
        use cleanup = cleanup
        TelemetryStoreApplication.initialize path approved |> unwrap |> ignore
        let cycle = "operational-cycle"

        let cycleFacts =
            [
                activation cycle "codex-exec" 60L
                dispatch cycle "dispatch-a" "child" (Some "dispatch-b") "codex-exec" "01"
                dispatch cycle "dispatch-b" "follow-up" (Some "dispatch-a") "codex-exec" "02"
                lineage cycle "lineage-a" "dispatch-a" "invoke-a" "child" (Some "invoke-b") "invoke-a" "codex-exec"
                lineage cycle "lineage-b" "dispatch-b" "invoke-b" "follow-up" (Some "invoke-a") "invoke-a" "codex-exec"
            ]

        TelemetryStoreApplication.ingest path approved (operationalBatch "operational-cycle" cycle cycleFacts)
        |> unwrap
        |> ignore

        Assert.Contains("cyclic-lineage", TelemetryStoreApplication.reconcile path approved cycle |> unwrap)
        let unsupported = "operational-unsupported"

        let unsupportedFacts =
            [
                activation unsupported "collaboration.spawn_agent" 60L
                dispatch unsupported "dispatch-root" "root" None "collaboration.spawn_agent" "01"
                lineage
                    unsupported
                    "lineage-root"
                    "dispatch-root"
                    "invoke-root"
                    "root"
                    None
                    "invoke-root"
                    "collaboration.spawn_agent"
            ]

        TelemetryStoreApplication.ingest
            path
            approved
            (operationalBatch "operational-unsupported" unsupported unsupportedFacts)
        |> unwrap
        |> ignore

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
            [
                activation missing "codex-exec" 5L
                dispatch missing "dispatch-root" "root" None "codex-exec" "01"
                lineage missing "lineage-root" "dispatch-root" "invoke-root" "root" None "invoke-root" "codex-exec"
                eventTime
                    missing
                    "time-missing-admission"
                    "invoke-root"
                    "admission"
                    (Some "2026-09-08T10:01:00Z")
                    (Some "provider-native")
                    (Some "2026-09-08T10:01:01Z")
                    (Some "provider-native")
                eventTime
                    missing
                    "time-missing-start"
                    "invoke-root"
                    "start"
                    None
                    None
                    (Some "2026-09-08T10:01:01Z")
                    (Some "provider-native")
                eventTime
                    missing
                    "time-missing-terminal"
                    "invoke-root"
                    "terminal"
                    (Some "2026-09-08T10:01:00Z")
                    (Some "provider-native")
                    (Some "2026-09-08T10:01:01Z")
                    (Some "provider-native")
            ]

        TelemetryStoreApplication.ingest
            path
            approved
            (operationalBatch "operational-missing-time" missing missingFacts)
        |> unwrap
        |> ignore

        Assert.Contains("timestamps-missing", TelemetryStoreApplication.reconcile path approved missing |> unwrap)
        let late = "operational-late"

        let lateFacts =
            [
                activation late "codex-exec" 5L
                dispatch late "dispatch-root" "root" None "codex-exec" "01"
                lineage
                    late
                    "lineage-late-root"
                    "dispatch-root"
                    "invoke-late-root"
                    "root"
                    None
                    "invoke-late-root"
                    "codex-exec"
                yield! completeTimes late "late-root" "invoke-late-root" "01" "06"
            ]

        TelemetryStoreApplication.ingest path approved (operationalBatch "operational-late" late lateFacts)
        |> unwrap
        |> ignore

        let result = TelemetryStoreApplication.reconcile path approved late |> unwrap
        Assert.Contains("observation-late", result)
        Assert.Contains("\"late\":1", result)

    [<Fact>]
    let ``UTEL-06A arbitrary and duplicate event witnesses are invalid`` () =
        let arbitrary = "operational-arbitrary-event"

        let arbitraryEvent =
            eventTime
                arbitrary
                "time-foo"
                "invoke-root"
                "foo"
                (Some "2026-09-08T10:01:00Z")
                (Some "provider-native")
                (Some "2026-09-08T10:01:01Z")
                (Some "provider-native")

        match TelemetryStore.parseBatch (operationalBatch "operational-arbitrary" arbitrary [ arbitraryEvent ]) with
        | Error errors -> Assert.Contains("admission, start, or terminal", String.concat ";" errors)
        | Ok _ -> failwith "arbitrary event witness was accepted"

        let cleanup, path = root ()
        use cleanup = cleanup
        TelemetryStoreApplication.initialize path approved |> unwrap |> ignore
        let duplicate = "operational-duplicate-event"

        let facts =
            [
                activation duplicate "codex-exec" 60L
                dispatch duplicate "dispatch-root" "root" None "codex-exec" "01"
                lineage duplicate "lineage-root" "dispatch-root" "invoke-root" "root" None "invoke-root" "codex-exec"
                yield! completeTimes duplicate "root" "invoke-root" "01" "01"
            ]

        TelemetryStoreApplication.ingest path approved (operationalBatch "operational-duplicate-base" duplicate facts)
        |> unwrap
        |> ignore

        let conflicting =
            eventTime
                duplicate
                "time-conflicting-terminal"
                "invoke-root"
                "terminal"
                (Some "2026-09-08T10:01:00Z")
                (Some "provider-native")
                (Some "2026-09-08T10:01:02Z")
                (Some "provider-native")

        TelemetryStoreApplication.publish
            path
            approved
            (operationalBatch "operational-duplicate-conflict" duplicate [ conflicting ])
        |> unwrap
        |> ignore

        Assert.Contains("\"quarantined\":1", TelemetryStoreApplication.drain path approved |> unwrap)
        Assert.Contains("\"complete\":1", TelemetryStoreApplication.reconcile path approved duplicate |> unwrap)

    [<Fact>]
    let ``UTEL-06A incomparable clock domains never produce latency`` () =
        let cleanup, path = root ()
        use cleanup = cleanup
        TelemetryStoreApplication.initialize path approved |> unwrap |> ignore
        let item = "operational-clock-mismatch"

        let facts =
            [
                activation item "codex-exec" 60L
                dispatch item "dispatch-root" "root" None "codex-exec" "01"
                lineage item "lineage-root" "dispatch-root" "invoke-root" "root" None "invoke-root" "codex-exec"
                eventTime
                    item
                    "time-admission"
                    "invoke-root"
                    "admission"
                    (Some "2026-09-08T10:01:00Z")
                    (Some "provider-native")
                    (Some "2026-09-08T10:01:01Z")
                    (Some "host-wall")
                eventTime
                    item
                    "time-start"
                    "invoke-root"
                    "start"
                    (Some "2026-09-08T10:01:00Z")
                    (Some "provider-native")
                    (Some "2026-09-08T10:01:01Z")
                    (Some "provider-native")
                eventTime
                    item
                    "time-terminal"
                    "invoke-root"
                    "terminal"
                    (Some "2026-09-08T10:01:00Z")
                    (Some "provider-native")
                    (Some "2026-09-08T10:01:01Z")
                    (Some "provider-native")
            ]

        TelemetryStoreApplication.ingest path approved (operationalBatch "operational-clock-mismatch" item facts)
        |> unwrap
        |> ignore

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
                [
                    activation item "codex-exec" 60L
                    dispatch item "dispatch-root" "root" None "codex-exec" "01"
                    lineage
                        item
                        ($"lineage-{item}")
                        "dispatch-root"
                        ($"invoke-{item}")
                        "root"
                        None
                        ($"invoke-{item}")
                        "codex-exec"
                    yield! events
                ]

            TelemetryStoreApplication.ingest path approved (operationalBatch ($"batch-{item}") item facts)
            |> unwrap
            |> ignore

            TelemetryStoreApplication.reconcile path approved item |> unwrap

        let startBefore = "operational-start-before-admission"
        let startBeforeInvocation = $"invoke-{startBefore}"

        let startBeforeEvents =
            [
                eventTime
                    startBefore
                    "order-a-admission"
                    startBeforeInvocation
                    "admission"
                    (Some "2026-09-08T10:01:01Z")
                    (Some "provider-native")
                    (Some "2026-09-08T10:01:01Z")
                    (Some "provider-native")
                eventTime
                    startBefore
                    "order-a-start"
                    startBeforeInvocation
                    "start"
                    (Some "2026-09-08T10:01:00Z")
                    (Some "provider-native")
                    (Some "2026-09-08T10:01:02Z")
                    (Some "provider-native")
                eventTime
                    startBefore
                    "order-a-terminal"
                    startBeforeInvocation
                    "terminal"
                    (Some "2026-09-08T10:01:02Z")
                    (Some "provider-native")
                    (Some "2026-09-08T10:01:03Z")
                    (Some "provider-native")
            ]

        Assert.Contains("lifecycle-occurrence-order-invalid", observe startBefore startBeforeEvents)
        let terminalBefore = "operational-terminal-before-start"
        let terminalBeforeInvocation = $"invoke-{terminalBefore}"

        let terminalBeforeEvents =
            [
                eventTime
                    terminalBefore
                    "order-b-admission"
                    terminalBeforeInvocation
                    "admission"
                    (Some "2026-09-08T10:01:00Z")
                    (Some "provider-native")
                    (Some "2026-09-08T10:01:01Z")
                    (Some "provider-native")
                eventTime
                    terminalBefore
                    "order-b-start"
                    terminalBeforeInvocation
                    "start"
                    (Some "2026-09-08T10:01:01Z")
                    (Some "provider-native")
                    (Some "2026-09-08T10:01:03Z")
                    (Some "provider-native")
                eventTime
                    terminalBefore
                    "order-b-terminal"
                    terminalBeforeInvocation
                    "terminal"
                    (Some "2026-09-08T10:01:02Z")
                    (Some "provider-native")
                    (Some "2026-09-08T10:01:02Z")
                    (Some "provider-native")
            ]

        Assert.Contains("lifecycle-observation-order-invalid", observe terminalBefore terminalBeforeEvents)
        let mixedClock = "operational-cross-event-clock"
        let mixedClockInvocation = $"invoke-{mixedClock}"

        let mixedClockEvents =
            [
                eventTime
                    mixedClock
                    "clock-c-admission"
                    mixedClockInvocation
                    "admission"
                    (Some "2026-09-08T10:01:00Z")
                    (Some "provider-native")
                    (Some "2026-09-08T10:01:01Z")
                    (Some "provider-native")
                eventTime
                    mixedClock
                    "clock-c-start"
                    mixedClockInvocation
                    "start"
                    (Some "2026-09-08T10:01:01Z")
                    (Some "host-wall")
                    (Some "2026-09-08T10:01:02Z")
                    (Some "host-wall")
                eventTime
                    mixedClock
                    "clock-c-terminal"
                    mixedClockInvocation
                    "terminal"
                    (Some "2026-09-08T10:01:02Z")
                    (Some "provider-native")
                    (Some "2026-09-08T10:01:03Z")
                    (Some "provider-native")
            ]

        Assert.Contains("lifecycle-clock-domain-mismatch", observe mixedClock mixedClockEvents)

    [<Fact>]
    let ``UTEL-06A activation contract excludes historical session discovery`` () =
        let item = "operational-history"

        let historical =
            (activation item "codex-exec" 60L).Replace("explicit-future-dispatches", "historical-sessions")

        match TelemetryStore.parseBatch (operationalBatch "operational-history" item [ historical ]) with
        | Error errors -> Assert.Contains("explicit-future-dispatches", String.concat ";" errors)
        | Ok _ -> failwith "historical discovery scope was accepted"

        Assert.Equal(
            Some(Ok()),
            TelemetryApplication.validateInvocation [ "telemetry"; "store"; "reconcile"; "--item"; item ]
        )

        Assert.Equal(
            Some(Ok()),
            TelemetryApplication.validateInvocation
                [ "telemetry"; "dashboard"; "status"; "--repository"; "FS-GG/.github" ]
        )

        Assert.Equal(
            Some(Ok()),
            TelemetryApplication.validateInvocation
                [
                    "telemetry"
                    "dashboard"
                    "serve"
                    "--repository"
                    "FS-GG/.github"
                    "--no-open"
                ]
        )

        Assert.True(
            TelemetryApplication.validateInvocation [ "telemetry"; "dashboard"; "status"; "--no-open" ]
            |> Option.exists Result.isError
        )

    [<Fact>]
    let ``UTEL-06A migration checksum is verified`` () =
        let cleanup, path = root ()
        use cleanup = cleanup
        TelemetryStoreApplication.initialize path approved |> unwrap |> ignore

        use connection =
            new SqliteConnection(
                $"Data Source=%s{Path.Combine(path, TelemetryStoreApplication.databaseFileName)};Pooling=False"
            )

        connection.Open()
        use corrupt = connection.CreateCommand()
        corrupt.CommandText <- "UPDATE schema_migrations SET digest='corrupt' WHERE version=5;"
        corrupt.ExecuteNonQuery() |> ignore
        connection.Close()
        Assert.Contains("migration checksum mismatch", sprintf "%A" (TelemetryStoreApplication.status path approved))

        Assert.Contains(
            "migration checksum mismatch",
            sprintf "%A" (TelemetryStoreApplication.initialize path approved)
        )

    [<Fact>]
    let ``UTEL-06C admission correction and replay remain revision safe`` () =
        let cleanup, path = root ()
        use cleanup = cleanup
        TelemetryStoreApplication.initialize path approved |> unwrap |> ignore

        let first =
            TelemetryStoreApplication.ingest
                path
                approved
                (ciPopulationBatch "ci-first" "UTEL-06C" 1 "in_progress" "partial" "pending")
            |> unwrap

        Assert.Contains("\"accepted\":3", first)

        Assert.True(
            TelemetryStoreApplication.ciPopulationAdmissionExists
                path
                approved
                "UTEL-06C"
                "o/r"
                7
                "main"
                "dddddddddddddddddddddddddddddddddddddddd"
                "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa"
            |> unwrap
        )

        Assert.False(
            TelemetryStoreApplication.ciPopulationAdmissionExists
                path
                approved
                "UTEL-06C"
                "o/r"
                7
                "release"
                "dddddddddddddddddddddddddddddddddddddddd"
                "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa"
            |> unwrap
        )

        Assert.False(
            TelemetryStoreApplication.ciPopulationAdmissionExists
                path
                approved
                "UTEL-06C"
                "o/r"
                7
                "main"
                "eeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeee"
                "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa"
            |> unwrap
        )

        let corrected =
            TelemetryStoreApplication.ingest
                path
                approved
                (ciPopulationBatch "ci-corrected" "UTEL-06C" 2 "completed" "complete" "none")
            |> unwrap

        Assert.Contains("\"accepted\":2", corrected)

        let admissionRevision =
            ciPopulationBatch "ci-admission-revision" "UTEL-06C" 2 "completed" "complete" "none"
            |> Encoding.UTF8.GetString
            |> fun value ->
                value.Replace("\"revision\":1,\"collectionId\"", "\"revision\":2,\"collectionId\"")
                |> Encoding.UTF8.GetBytes

        Assert.Contains("\"accepted\":1", TelemetryStoreApplication.ingest path approved admissionRevision |> unwrap)

        let replayedBatch =
            ciPopulationBatch "ci-replay" "UTEL-06C" 2 "completed" "complete" "none"
            |> Encoding.UTF8.GetString
            |> fun value ->
                value.Replace("\"revision\":1,\"collectionId\"", "\"revision\":2,\"collectionId\"")
                |> Encoding.UTF8.GetBytes

        let replayed =
            TelemetryStoreApplication.ingest path approved replayedBatch |> unwrap

        Assert.Contains("\"replayed\":3", replayed)
        let summary = TelemetryStoreApplication.ciSummary path approved "UTEL-06C" |> unwrap
        Assert.Contains("\"inventoryCoverage\":\"complete\"", summary)
        Assert.Contains("\"continuation\":\"none\"", summary)

        use connection =
            new SqliteConnection(
                $"Data Source=%s{Path.Combine(path, TelemetryStoreApplication.databaseFileName)};Pooling=False"
            )

        connection.Open()
        use count = connection.CreateCommand()
        count.CommandText <- "SELECT count(*) FROM corrections WHERE identity IN ('ci-check-a','ci-coverage-a');"
        Assert.Equal(2L, Convert.ToInt64(count.ExecuteScalar()))

    [<Fact>]
    let ``UTEL-06C migration checksum is verified`` () =
        let cleanup, path = root ()
        use cleanup = cleanup
        TelemetryStoreApplication.initialize path approved |> unwrap |> ignore

        use connection =
            new SqliteConnection(
                $"Data Source=%s{Path.Combine(path, TelemetryStoreApplication.databaseFileName)};Pooling=False"
            )

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
            [
                activation item "codex-exec" 60L
                dispatch item "dispatch-root" "root" None "codex-exec" "00"
                lineage item "lineage-derived-root" "dispatch-root" "invoke-root" "root" None "invoke-root" "codex-exec"
                runtimeTerminal item "terminal-derived-root" "invoke-root" "completed" 0
                runtimeUsage item "usage-derived-root" "invoke-root" 100L
                ciStep item
                nativeOutcome item 1L "delivered" "delivered" "2026-09-08T10:04:01Z"
            ]

        Assert.DoesNotContain("budget-", String.concat "" raw)

        TelemetryStoreApplication.ingest path approved (operationalBatch "derived-whole-item" item raw)
        |> unwrap
        |> ignore

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

        use connection =
            new SqliteConnection(
                $"Data Source=%s{Path.Combine(path, TelemetryStoreApplication.databaseFileName)};Pooling=False"
            )

        connection.Open()
        use intervals = connection.CreateCommand()

        intervals.CommandText <-
            "SELECT count(*) FROM budget_interval_facts WHERE item_id=$item AND source_ref LIKE 'derived:%';"

        intervals.Parameters.AddWithValue("$item", item) |> ignore
        Assert.Equal(1L, Convert.ToInt64(intervals.ExecuteScalar()))

    [<Fact>]
    let ``derived completion keeps the prospective original across two delivered members`` () =
        let cleanup, path = root ()
        use cleanup = cleanup
        TelemetryStoreApplication.initialize path approved |> unwrap |> ignore
        let original = "UTEL-group"

        for memberItem in [ "UTEL-group-a"; "UTEL-group-b" ] do
            let observed =
                [
                    roadmapPopulation memberItem original
                    activation memberItem "codex-exec" 60L
                    dispatch memberItem ("dispatch-" + memberItem) "root" None "codex-exec" "00"
                    lineage memberItem ("lineage-" + memberItem) ("dispatch-" + memberItem) ("invoke-" + memberItem) "root" None ("invoke-" + memberItem) "codex-exec"
                    runtimeTerminal memberItem ("terminal-" + memberItem) ("invoke-" + memberItem) "completed" 0
                    nativeOutcome memberItem 1L "delivered" "delivered" "2026-09-08T10:04:01Z"
                ]

            TelemetryStoreApplication.ingest path approved (operationalBatch ("member-" + memberItem) memberItem observed)
            |> unwrap
            |> ignore

        use connection =
            new SqliteConnection(
                $"Data Source=%s{Path.Combine(path, TelemetryStoreApplication.databaseFileName)};Pooling=False"
            )

        connection.Open()
        use members = connection.CreateCommand()
        members.CommandText <-
            "SELECT count(*) FROM budget_population_facts WHERE original_item_id=$original AND state='completed' AND source_ref LIKE 'derived:%';"
        members.Parameters.AddWithValue("$original", original) |> ignore
        Assert.Equal(2L, Convert.ToInt64(members.ExecuteScalar()))

        let untrusted = "UTEL-group-d"

        TelemetryStoreApplication.ingest
            path
            approved
            (operationalBatch
                "untrusted-original"
                untrusted
                [
                    $"""{{"kind":"budget-population","identity":"foreign-population","itemId":"{untrusted}","revision":0,"originalItemId":"{original}","state":"completed","sourceKind":"native-item","sourceRef":"foreign-source"}}"""
                    activation untrusted "codex-exec" 60L
                    dispatch untrusted "dispatch-untrusted" "root" None "codex-exec" "00"
                    lineage untrusted "lineage-untrusted" "dispatch-untrusted" "invoke-untrusted" "root" None "invoke-untrusted" "codex-exec"
                    runtimeTerminal untrusted "terminal-untrusted" "invoke-untrusted" "completed" 0
                    nativeOutcome untrusted 1L "delivered" "delivered" "2026-09-08T10:04:01Z"
                ])
        |> unwrap
        |> ignore

        Assert.Contains(
            "\"population\":\"open\"",
            TelemetryStoreApplication.budgetHealth path approved untrusted |> unwrap
        )
        Assert.Equal(2L, Convert.ToInt64(members.ExecuteScalar()))

        let unfinished = "UTEL-group-c"

        TelemetryStoreApplication.ingest
            path
            approved
            (operationalBatch
                "unfinished-member"
                unfinished
                [
                    roadmapPopulation unfinished original
                    activation unfinished "codex-exec" 60L
                    dispatch unfinished "dispatch-unfinished" "root" None "codex-exec" "00"
                    nativeOutcome unfinished 1L "delivered" "delivered" "2026-09-08T10:04:01Z"
                ])
        |> unwrap
        |> ignore

        Assert.Contains(
            "\"population\":\"open\"",
            TelemetryStoreApplication.budgetHealth path approved unfinished |> unwrap
        )
        Assert.Equal(2L, Convert.ToInt64(members.ExecuteScalar()))

        TelemetryStoreApplication.ingest
            path
            approved
            (operationalBatch
                "conflicting-original"
                "UTEL-group-a"
                [
                    roadmapPopulation "UTEL-group-a" "OTHER"
                ])
        |> unwrap
        |> ignore

        Assert.Contains(
            "\"population\":\"open\"",
            TelemetryStoreApplication.budgetHealth path approved "UTEL-group-a" |> unwrap
        )
        Assert.Equal(1L, Convert.ToInt64(members.ExecuteScalar()))

    [<Fact>]
    let ``UTEL-06D merge cannot close live child and late follow-up revises stable population`` () =
        let cleanup, path = root ()
        use cleanup = cleanup
        TelemetryStoreApplication.initialize path approved |> unwrap |> ignore
        let item = "UTEL-06D-late"

        let initial =
            [
                activation item "codex-exec" 60L
                dispatch item "dispatch-root" "root" None "codex-exec" "00"
                lineage item "lineage-late-root" "dispatch-root" "invoke-root" "root" None "invoke-root" "codex-exec"
                runtimeTerminal item "terminal-late-root" "invoke-root" "completed" 0
                runtimeUsage item "usage-late-root" "invoke-root" 10L
                dispatch item "dispatch-child" "child" (Some "dispatch-root") "codex-exec" "01"
                lineage
                    item
                    "lineage-late-child"
                    "dispatch-child"
                    "invoke-child"
                    "child"
                    (Some "invoke-root")
                    "invoke-root"
                    "codex-exec"
                nativeOutcome item 1L "delivered" "delivered" "2026-09-08T10:04:01Z"
            ]

        TelemetryStoreApplication.ingest path approved (operationalBatch "late-initial" item initial)
        |> unwrap
        |> ignore

        Assert.Contains("\"population\":\"open\"", TelemetryStoreApplication.budgetHealth path approved item |> unwrap)

        TelemetryStoreApplication.ingest
            path
            approved
            (operationalBatch
                "late-child-terminal"
                item
                [
                    runtimeTerminal item "terminal-late-child" "invoke-child" "completed" 0
                    runtimeUsage item "usage-late-child" "invoke-child" 10L
                ])
        |> unwrap
        |> ignore

        Assert.Contains(
            "\"population\":\"completed\"",
            TelemetryStoreApplication.budgetHealth path approved item |> unwrap
        )

        let before = TelemetryStoreApplication.budgetSummary path approved item |> unwrap

        let followup =
            [
                dispatch item "dispatch-follow" "follow-up" (Some "dispatch-root") "codex-exec" "05"
                lineage
                    item
                    "lineage-late-follow"
                    "dispatch-follow"
                    "invoke-follow"
                    "follow-up"
                    (Some "invoke-root")
                    "invoke-root"
                    "codex-exec"
            ]

        TelemetryStoreApplication.ingest path approved (operationalBatch "late-followup" item followup)
        |> unwrap
        |> ignore

        Assert.Contains("\"population\":\"open\"", TelemetryStoreApplication.budgetHealth path approved item |> unwrap)
        Assert.Contains("whole-item-incomplete", TelemetryStoreApplication.budgetSummary path approved item |> unwrap)

        TelemetryStoreApplication.ingest
            path
            approved
            (operationalBatch
                "late-followup-terminal"
                item
                [
                    runtimeTerminal item "terminal-late-follow" "invoke-follow" "completed" 0
                    runtimeUsage item "usage-late-follow" "invoke-follow" 10L
                ])
        |> unwrap
        |> ignore

        Assert.Contains(
            "\"population\":\"completed\"",
            TelemetryStoreApplication.budgetHealth path approved item |> unwrap
        )

        let after = TelemetryStoreApplication.budgetSummary path approved item |> unwrap
        Assert.False((before = after))

        use connection =
            new SqliteConnection(
                $"Data Source=%s{Path.Combine(path, TelemetryStoreApplication.databaseFileName)};Pooling=False"
            )

        connection.Open()
        use count = connection.CreateCommand()

        count.CommandText <-
            "SELECT count(*) FROM budget_population_facts WHERE item_id=$item AND source_ref LIKE 'derived:%';"

        count.Parameters.AddWithValue("$item", item) |> ignore
        Assert.Equal(1L, Convert.ToInt64(count.ExecuteScalar()))

    [<Fact>]
    let ``native collaboration delivery closes only after every expected child settles`` () =
        let cleanup, path = root ()
        use cleanup = cleanup
        TelemetryStoreApplication.initialize path approved |> unwrap |> ignore
        let item = "UTEL-native-completion"
        let runtime = "collaboration-spawn-agent"

        TelemetryStoreApplication.ingest
            path
            approved
            (operationalBatch
                "native-completion-open"
                item
                [
                    activation item runtime 60L
                    dispatch item "dispatch-root" "root" None runtime "00"
                    lineage item "lineage-native-root" "dispatch-root" "invoke-root" "root" None "invoke-root" runtime
                    runtimeTerminal item "terminal-native-root" "invoke-root" "completed" 0
                    dispatch item "dispatch-child" "child" (Some "dispatch-root") runtime "01"
                    lineage
                        item
                        "lineage-native-child"
                        "dispatch-child"
                        "invoke-child"
                        "child"
                        (Some "invoke-root")
                        "invoke-root"
                        runtime
                    nativeOutcome item 1L "delivered" "delivered" "2026-09-08T10:04:01Z"
                ])
        |> unwrap
        |> ignore

        Assert.Contains("\"population\":\"open\"", TelemetryStoreApplication.budgetHealth path approved item |> unwrap)

        TelemetryStoreApplication.ingest
            path
            approved
            (operationalBatch
                "native-completion-terminal"
                item
                [ runtimeTerminal item "terminal-native-child" "invoke-child" "completed" 0 ])
        |> unwrap
        |> ignore

        Assert.Contains("\"population\":\"completed\"", TelemetryStoreApplication.budgetHealth path approved item |> unwrap)

        TelemetryStoreApplication.ingest
            path
            approved
            (operationalBatch
                "native-completion-unsupported-followup"
                item
                [ dispatch item "dispatch-unknown" "follow-up" (Some "dispatch-root") "unknown-runtime" "05" ])
        |> unwrap
        |> ignore

        Assert.Contains("\"population\":\"open\"", TelemetryStoreApplication.budgetHealth path approved item |> unwrap)

    [<Fact>]
    let ``native completion rule reprojects retained outcomes after an engine upgrade`` () =
        let cleanup, path = root ()
        use cleanup = cleanup
        TelemetryStoreApplication.initialize path approved |> unwrap |> ignore
        let item = "UTEL-native-upgrade"
        let runtime = "collaboration-spawn-agent"

        TelemetryStoreApplication.ingest
            path
            approved
            (operationalBatch
                "native-upgrade-facts"
                item
                [
                    activation item runtime 60L
                    dispatch item "dispatch-root" "root" None runtime "00"
                    lineage item "lineage-upgrade" "dispatch-root" "invoke-root" "root" None "invoke-root" runtime
                    runtimeTerminal item "terminal-upgrade" "invoke-root" "completed" 0
                    nativeOutcome item 1L "delivered" "delivered" "2026-09-08T10:04:01Z"
                ])
        |> unwrap
        |> ignore

        use connection =
            new SqliteConnection($"Data Source=%s{Path.Combine(path, TelemetryStoreApplication.databaseFileName)};Pooling=False")

        connection.Open()
        use prior = connection.CreateCommand()
        prior.CommandText <- "UPDATE budget_population_facts SET state='open' WHERE item_id=$item AND source_ref LIKE 'derived:%'; DELETE FROM store_metadata WHERE key='completedPopulationDerivation';"
        prior.Parameters.AddWithValue("$item", item) |> ignore
        prior.ExecuteNonQuery() |> ignore
        connection.Close()

        Assert.Contains("\"remaining\":0", TelemetryStoreApplication.drain path approved |> unwrap)
        Assert.Contains("\"population\":\"completed\"", TelemetryStoreApplication.budgetHealth path approved item |> unwrap)

    [<Fact>]
    let ``mixed codex and native dispatches require matching runtime activations`` () =
        let cleanup, path = root ()
        use cleanup = cleanup
        TelemetryStoreApplication.initialize path approved |> unwrap |> ignore
        let item = "UTEL-mixed-runtime-completion"

        let nativeActivation =
            (activation item "collaboration-spawn-agent" 60L)
                .Replace("activation-" + item, "activation-native-" + item)

        let nativeChild =
            (dispatch item "dispatch-child" "child" (Some "dispatch-root") "collaboration-spawn-agent" "01")
                .Replace("\"activationId\":\"activation-" + item + "\"", "\"activationId\":\"activation-native-" + item + "\"")

        TelemetryStoreApplication.ingest
            path
            approved
            (operationalBatch
                "mixed-runtime-completion"
                item
                [
                    activation item "codex-exec" 60L
                    nativeActivation
                    dispatch item "dispatch-root" "root" None "codex-exec" "00"
                    lineage item "lineage-mixed-root" "dispatch-root" "invoke-root" "root" None "invoke-root" "codex-exec"
                    runtimeTerminal item "terminal-mixed-root" "invoke-root" "completed" 0
                    nativeChild
                    lineage
                        item
                        "lineage-mixed-child"
                        "dispatch-child"
                        "invoke-child"
                        "child"
                        (Some "invoke-root")
                        "invoke-root"
                        "collaboration-spawn-agent"
                    runtimeTerminal item "terminal-mixed-child" "invoke-child" "completed" 0
                    nativeOutcome item 1L "delivered" "delivered" "2026-09-08T10:04:01Z"
                ])
        |> unwrap
        |> ignore

        Assert.Contains("\"population\":\"completed\"", TelemetryStoreApplication.budgetHealth path approved item |> unwrap)

    [<Fact>]
    let ``UTEL-06D refused delivered-without-runtime and observed no-op use distinct closure rules`` () =
        let cleanup, path = root ()
        use cleanup = cleanup
        TelemetryStoreApplication.initialize path approved |> unwrap |> ignore
        let refused = "UTEL-06D-refused"

        TelemetryStoreApplication.ingest
            path
            approved
            (operationalBatch
                "refused-native"
                refused
                [ nativeOutcome refused 1L "refused" "not-delivered" "2026-09-08T10:04:01Z" ])
        |> unwrap
        |> ignore

        Assert.Contains(
            "\"population\":\"completed\"",
            TelemetryStoreApplication.budgetHealth path approved refused |> unwrap
        )

        let missingRuntime = "UTEL-06D-missing-runtime"

        TelemetryStoreApplication.ingest
            path
            approved
            (operationalBatch
                "delivered-no-runtime"
                missingRuntime
                [
                    nativeOutcome missingRuntime 1L "delivered" "delivered" "2026-09-08T10:04:01Z"
                ])
        |> unwrap
        |> ignore

        Assert.Contains(
            "\"population\":\"open\"",
            TelemetryStoreApplication.budgetHealth path approved missingRuntime |> unwrap
        )

        let missingRoot = "UTEL-06D-missing-root"

        let childOnly =
            [
                activation missingRoot "codex-exec" 60L
                dispatch missingRoot "dispatch-child" "child" (Some "dispatch-parent") "codex-exec" "00"
                lineage
                    missingRoot
                    "lineage-child-only"
                    "dispatch-child"
                    "invoke-child"
                    "child"
                    (Some "invoke-parent")
                    "invoke-parent"
                    "codex-exec"
                runtimeTerminal missingRoot "terminal-child-only" "invoke-child" "completed" 0
                nativeOutcome missingRoot 1L "delivered" "delivered" "2026-09-08T10:04:01Z"
            ]

        TelemetryStoreApplication.ingest path approved (operationalBatch "delivered-no-root" missingRoot childOnly)
        |> unwrap
        |> ignore

        Assert.Contains(
            "\"population\":\"open\"",
            TelemetryStoreApplication.budgetHealth path approved missingRoot |> unwrap
        )

        let noOp = "UTEL-06D-no-op"

        let noOpFacts =
            [
                activation noOp "codex-exec" 60L
                dispatch noOp "dispatch-root" "root" None "codex-exec" "00"
                lineage noOp "lineage-no-op" "dispatch-root" "invoke-no-op" "root" None "invoke-no-op" "codex-exec"
                runtimeTerminal noOp "terminal-no-op" "invoke-no-op" "completed" 0
                nativeOutcome noOp 1L "delivered" "delivered" "2026-09-08T10:04:01Z"
            ]

        TelemetryStoreApplication.ingest path approved (operationalBatch "delivered-no-op" noOp noOpFacts)
        |> unwrap
        |> ignore

        Assert.Contains(
            "\"population\":\"completed\"",
            TelemetryStoreApplication.budgetHealth path approved noOp |> unwrap
        )

        Assert.Contains("source-coverage", TelemetryStoreApplication.budgetSummary path approved noOp |> unwrap)

    [<Fact>]
    let ``UTEL-06D final changed-head refusal is persisted before CI admission refusal`` () =
        let cleanup, path = root ()
        use cleanup = cleanup
        TelemetryStoreApplication.initialize path approved |> unwrap |> ignore
        let assignmentPath = Path.Combine(path, "assignment.json")
        let deliveryPath = Path.Combine(path, "delivery.json")

        File.WriteAllText(
            assignmentPath,
            """{"schema":"fsgg.telemetry.ci-assignment/1","featureId":"UTEL-06","itemId":"UTEL-06D-refused-driver","attemptId":"attempt-1","parentAttemptId":null,"producerStream":"routine-delivery"}"""
        )

        if not (OperatingSystem.IsWindows()) then
            File.SetUnixFileMode(assignmentPath, UnixFileMode.UserRead ||| UnixFileMode.UserWrite)

        File.WriteAllText(
            deliveryPath,
            """{"schema":"fsgg.routine-delivery/v1","repo":"o/r","pr":7,"baseRef":"main","baseSha":"dddddddddddddddddddddddddddddddddddddddd","expectedHead":"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa","observedHead":"cccccccccccccccccccccccccccccccccccccccc","outcome":"refused","codeDelivery":"not-delivered","mergeCommit":null,"outcomeAt":null,"observedAt":"2026-09-08T10:04:01Z"}"""
        )

        Assert.Equal(
            1,
            TelemetryCiApplication.runWithAssessment
                approved
                "reconcile"
                [
                    "--assignment"
                    assignmentPath
                    "--delivery"
                    deliveryPath
                    "--store-root"
                    path
                ]
        )

        Assert.Contains(
            "\"population\":\"completed\"",
            TelemetryStoreApplication.budgetHealth path approved "UTEL-06D-refused-driver"
            |> unwrap
        )

    [<Fact>]
    let ``UTEL-06D post-result bounded drain observes a just-queued admission`` () =
        let cleanup, path = root ()
        use cleanup = cleanup
        TelemetryStoreApplication.initialize path approved |> unwrap |> ignore
        let item = "UTEL-06D-drain-race"

        TelemetryStoreApplication.publish
            path
            approved
            (ciPopulationBatch "queued-admission" item 1L "queued" "partial" "pending")
        |> unwrap
        |> ignore

        Assert.False(
            TelemetryStoreApplication.ciPopulationAdmissionExists
                path
                approved
                item
                "o/r"
                7
                "main"
                (String.replicate 40 "d")
                (String.replicate 40 "a")
            |> unwrap
        )

        TelemetryStoreApplication.publish
            path
            approved
            (operationalBatch
                "final-native-readback"
                item
                [ nativeOutcome item 1L "delivered" "delivered" "2026-09-08T10:04:01Z" ])
        |> unwrap
        |> ignore

        let drained = TelemetryStoreApplication.drain path approved |> unwrap
        Assert.Contains("\"remaining\":0", drained)

        Assert.True(
            TelemetryStoreApplication.ciPopulationAdmissionExists
                path
                approved
                item
                "o/r"
                7
                "main"
                (String.replicate 40 "d")
                (String.replicate 40 "a")
            |> unwrap
        )

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
            [
                activation item "codex-exec" 60L
                dispatch item "dispatch-root" "root" None "codex-exec" "00"
                lineage
                    item
                    ("lineage-" + outcome)
                    "dispatch-root"
                    ("invoke-" + outcome)
                    "root"
                    None
                    ("invoke-" + outcome)
                    "codex-exec"
                runtimeTerminal item ("terminal-" + outcome) ("invoke-" + outcome) outcome exitCode
                nativeOutcome item 1L "delivered" "delivered" "2026-09-08T10:04:01Z"
            ]

        TelemetryStoreApplication.ingest path approved (operationalBatch ("terminal-" + outcome) item facts)
        |> unwrap
        |> ignore

        Assert.Contains(
            "\"population\":\"completed\"",
            TelemetryStoreApplication.budgetHealth path approved item |> unwrap
        )

        Assert.Contains("\"verdict\":\"unknown\"", TelemetryStoreApplication.budgetSummary path approved item |> unwrap)

    [<Fact>]
    let ``UTEL-08 terminal reviews activities exact attribution and complications produce private item detail`` () =
        let cleanup, path = root ()
        use cleanup = cleanup
        Assert.Contains("\"schemaVersion\":14", TelemetryStoreApplication.initialize path approved |> unwrap)
        let item = "UTEL-08-detail"
        let digest = String.replicate 64 "a"

        let admission =
            $"""{{"kind":"runtime-admission","identity":"admission-review","itemId":"{item}","revision":0,"invocationId":"invoke-review","featureId":"UTEL","attemptId":"attempt-review","parentAttemptId":null,"producerStream":"review-worker","requestedModel":"gpt-5","requestedEffort":"medium","backend":"codex-collaboration"}}"""

        let usage =
            $"""{{"kind":"runtime-turn-usage","identity":"usage-review","itemId":"{item}","revision":0,"invocationId":"invoke-review","threadId":"thread-review","turnId":"turn-review","turnSequence":1,"provider":"openai","requestedModel":"gpt-5","observedModel":"gpt-5","requestedEffort":"medium","observedEffort":"medium","backend":"codex-collaboration","scope":"completed-turn","provenance":"codex-exec-jsonl","input":10,"cachedInput":2,"output":5,"reasoning":1,"total":15}}"""

        let baseFacts =
            [
                activation item "collaboration-spawn-agent" 60L
                dispatch item "dispatch-review" "root" None "collaboration-spawn-agent" "00"
                lineage
                    item
                    "lineage-review"
                    "dispatch-review"
                    "invoke-review"
                    "root"
                    None
                    "invoke-review"
                    "collaboration-spawn-agent"
                admission
                runtimeTerminal item "terminal-review" "invoke-review" "completed" 0
                usage
            ]

        TelemetryStoreApplication.ingest path approved (operationalBatch "review-base" item baseFacts)
        |> unwrap
        |> ignore

        let span =
            $"""{{"kind":"activity-span","identity":"span-review","itemId":"{item}","revision":0,"activityId":"implementation-review","invocationId":"invoke-review","attemptId":"attempt-review","category":"implementation","startedAt":"2026-09-08T10:00:00Z","endedAt":"2026-09-08T10:03:00Z","clockProvenance":"host-wall","evidence":[{{"kind":"test","digest":"{digest}"}}],"summary":"focused implementation"}}"""

        let attribution =
            $"""{{"kind":"activity-usage-attribution","identity":"attribute-review","itemId":"{item}","revision":0,"usageIdentity":"usage-review","activityId":"implementation-review","classification":"direct","input":10,"cachedInput":2,"output":5,"reasoning":1,"total":15}}"""

        let complication =
            $"""{{"kind":"complication","identity":"complication-review","itemId":"{item}","revision":0,"attemptId":"attempt-review","activityId":"implementation-review","trigger":"test-failure","cause":"product-defect","occurredAt":"2026-09-08T10:02:00Z","synopsis":"A focused test exposed the repair","evidence":[{{"kind":"test","digest":"{digest}"}}]}}"""

        let review scope attempt revision synopsis =
            $"""{{"kind":"process-review","identity":"review-{scope}-{item}","itemId":"{item}","revision":{revision},"scope":"{scope}","attemptId":{attempt},"outcomeSynopsis":"{synopsis}","wentWell":["Focused tests"],"problems":["One repair"],"avoidableDelayOrRework":[],"processObservations":["Routine route stayed bounded"],"remainingRisks":[],"concreteImprovements":["Keep exact attribution"],"evidence":[{{"kind":"test","digest":"{digest}"}}],"evidenceCoverage":"partial","populationCoverage":"complete","confidence":"high","reviewerModel":"gpt-5","reviewerEffort":"medium","reviewedAt":"2026-09-08T10:04:00Z","durationSeconds":30}}"""

        TelemetryStoreApplication.ingest
            path
            approved
            (operationalBatch
                "review-observations"
                item
                [
                    span
                    attribution
                    complication
                    review "attempt" "\"attempt-review\"" 1 "Attempt completed"
                    review "item" "null" 1 "Item completed"
                ])
        |> unwrap
        |> ignore

        let summary = TelemetryStoreApplication.reviewSummary path approved item |> unwrap
        Assert.Contains("Attempt completed", summary)
        Assert.Contains("Item completed", summary)
        let detail = TelemetryStoreApplication.itemDetail path approved item |> unwrap
        Assert.Contains("\"schema\":\"fsgg.telemetry.item-detail/1\"", detail)
        Assert.Contains("\"nativeTotal\":15", detail)
        Assert.Contains("\"direct\":15", detail)
        Assert.Contains("\"missingAttribution\":0", detail)
        Assert.Contains("A focused test exposed the repair", detail)

        let snapshot1 =
            TelemetryStoreApplication.dashboardSnapshot path approved (Some item) |> unwrap

        let snapshot2 =
            TelemetryStoreApplication.dashboardSnapshot path approved (Some item) |> unwrap

        use snapshotDocument1 = JsonDocument.Parse snapshot1
        use snapshotDocument2 = JsonDocument.Parse snapshot2
        Assert.Equal("fsgg.telemetry.item-detail/2", snapshotDocument1.RootElement.GetProperty("schema").GetString())

        let compressed =
            Convert.FromBase64String(snapshotDocument1.RootElement.GetProperty("canonicalSnapshotGzip").GetString())

        use compressedStream = new MemoryStream(compressed)
        use decompressor = new GZipStream(compressedStream, CompressionMode.Decompress)
        use canonicalStream = new MemoryStream()
        decompressor.CopyTo canonicalStream
        let canonical = canonicalStream.ToArray()
        use canonicalDocument = JsonDocument.Parse canonical
        Assert.Equal(14, canonicalDocument.RootElement.GetProperty("store").GetProperty("schemaVersion").GetInt32())
        Assert.Equal(snapshotDocument1.RootElement.GetProperty("revision").GetString(), CanonicalJson.sha256 canonical)

        Assert.Equal(
            snapshotDocument1.RootElement.GetProperty("revision").GetString(),
            snapshotDocument2.RootElement.GetProperty("revision").GetString()
        )

        Assert.False(snapshotDocument1.RootElement.GetProperty("observedAt").GetString() = "")
        let publicPath = path + "-public.json"

        use publicCleanup =
            { new IDisposable with
                member _.Dispose() =
                    if File.Exists publicPath then
                        File.Delete publicPath
            }

        TelemetryStoreApplication.exportPublic path approved (Some item) publicPath
        |> unwrap
        |> ignore

        let publicJson = File.ReadAllText publicPath
        Assert.DoesNotContain("Attempt completed", publicJson)
        Assert.DoesNotContain("A focused test exposed the repair", publicJson)

        let duplicate =
            attribution.Replace("attribute-review", "attribute-review-duplicate")

        Assert.Contains(
            "\"quarantined\":1",
            TelemetryStoreApplication.ingest
                path
                approved
                (operationalBatch "review-duplicate-attribution" item [ duplicate ])
            |> unwrap
        )

        Assert.Contains("\"direct\":15", TelemetryStoreApplication.itemDetail path approved item |> unwrap)

        let corrected =
            TelemetryStoreApplication.ingest
                path
                approved
                (operationalBatch
                    "review-correction"
                    item
                    [ review "attempt" "\"attempt-review\"" 2 "Attempt completed after correction" ])
            |> unwrap

        Assert.Contains("\"accepted\":1", corrected)

        Assert.DoesNotContain(
            "\"outcomeSynopsis\":\"Attempt completed\"",
            TelemetryStoreApplication.reviewSummary path approved item |> unwrap
        )

        use snapshotDocument3 =
            JsonDocument.Parse(TelemetryStoreApplication.dashboardSnapshot path approved (Some item) |> unwrap)

        Assert.False(
            snapshotDocument1.RootElement.GetProperty("revision").GetString() =
                snapshotDocument3.RootElement.GetProperty("revision").GetString()
        )

    [<Fact>]
    let ``UTEL-08 rejects premature reviews allocation and duplicate native attribution`` () =
        let cleanup, path = root ()
        use cleanup = cleanup
        TelemetryStoreApplication.initialize path approved |> unwrap |> ignore
        let item = "UTEL-08-reject"

        let admission =
            $"""{{"kind":"runtime-admission","identity":"admission-open","itemId":"{item}","revision":0,"invocationId":"invoke-open","featureId":"UTEL","attemptId":"attempt-open","parentAttemptId":null,"producerStream":"review-worker","requestedModel":"gpt-5","requestedEffort":"medium","backend":"codex-collaboration"}}"""

        TelemetryStoreApplication.ingest
            path
            approved
            (operationalBatch
                "review-open"
                item
                [
                    activation item "collaboration-spawn-agent" 60L
                    dispatch item "dispatch-open" "root" None "collaboration-spawn-agent" "00"
                    lineage
                        item
                        "lineage-open"
                        "dispatch-open"
                        "invoke-open"
                        "root"
                        None
                        "invoke-open"
                        "collaboration-spawn-agent"
                    admission
                ])
        |> unwrap
        |> ignore

        let premature =
            $"""{{"kind":"process-review","identity":"review-open","itemId":"{item}","revision":1,"scope":"attempt","attemptId":"attempt-open","outcomeSynopsis":"Not terminal","wentWell":[],"problems":[],"avoidableDelayOrRework":[],"processObservations":[],"remainingRisks":[],"concreteImprovements":[],"evidence":[],"evidenceCoverage":"unknown","populationCoverage":"unknown","confidence":"low","reviewerModel":"gpt-5","reviewerEffort":"medium","reviewedAt":"2026-09-08T10:04:00Z","durationSeconds":1}}"""

        Assert.Contains(
            "terminal",
            sprintf
                "%A"
                (TelemetryStoreApplication.ingest path approved (operationalBatch "premature-review" item [ premature ]))
        )

        let allocated =
            $"""{{"kind":"activity-usage-attribution","identity":"allocated","itemId":"{item}","revision":0,"usageIdentity":"missing-usage","activityId":null,"classification":"unclassified","input":1,"cachedInput":0,"output":1,"reasoning":null,"total":2}}"""

        Assert.Contains(
            "matching native usage",
            sprintf
                "%A"
                (TelemetryStoreApplication.ingest path approved (operationalBatch "allocated-review" item [ allocated ]))
        )


    let private correctionRequest () : TelemetryCi.CorrectionRequest =
        let assignment feature item attempt : TelemetryCi.Assignment =
            { FeatureId = feature; ItemId = item; AttemptId = attempt; ParentAttemptId = None; ProducerStream = "routine-delivery" }
        { CorrectionId = "correction-one"; ExpectedPredecessor = None; Repository = "o/r"; PullRequest = 7L
          BaseRef = "main"; BaseSha = String.replicate 40 "d"; Head = String.replicate 40 "a"; MergeCommit = String.replicate 40 "b"
          Prior = assignment "V2-LANG-01" "V2-LANG-01.2" "wrong-attempt"
          Effective = assignment "GOV-423" "GOV-423-C3" "genuine-attempt"
          EvidenceSha256 = String.replicate 64 "e"; Reason = "Recovered exact source assignment"
          OperatorSource = "private-source02"; ObservedAt = "2026-10-06T12:00:00Z" }

    let private correctionSql path sql =
        use connection = new SqliteConnection($"Data Source={Path.Combine(path, TelemetryStoreApplication.databaseFileName)};Pooling=False")
        connection.Open()
        use command = connection.CreateCommand()
        command.CommandText <- sql
        string (command.ExecuteScalar())

    let private correctionSnapshot expectedFormat (json: string) =
        let expectedSchema =
            match expectedFormat with
            | 2 -> "fsgg.telemetry.item-detail/2"
            | 3 -> "fsgg.telemetry.item-detail/3"
            | _ -> invalidArg "expectedFormat" "Only the two explicit dashboard contracts are supported"
        use envelope = JsonDocument.Parse json
        Assert.Equal(expectedSchema, envelope.RootElement.GetProperty("schema").GetString())
        use compressed = new MemoryStream(Convert.FromBase64String(envelope.RootElement.GetProperty("canonicalSnapshotGzip").GetString()))
        use decompressor = new GZipStream(compressed, CompressionMode.Decompress)
        use decoded = new MemoryStream()
        decompressor.CopyTo decoded
        let bytes = decoded.ToArray()
        Assert.Equal(envelope.RootElement.GetProperty("revision").GetString(), CanonicalJson.sha256 bytes)
        JsonDocument.Parse(Encoding.UTF8.GetString bytes)

    let private correctionFixture fullPopulation =
        let cleanup, path = root ()
        TelemetryStoreApplication.initialize path approved |> unwrap |> ignore
        let request = correctionRequest ()
        let item = request.Prior.ItemId
        let population =
            if not fullPopulation then []
            else
                [ $"""{{"kind":"ci-population-admission","identity":"correction-admission","itemId":"{item}","revision":1,"collectionId":"correction-collection","repository":"o/r","prNumber":7,"baseRef":"main","baseSha":"{request.BaseSha}","head":"{request.Head}","witness":"native-pr-head"}}"""
                  $"""{{"kind":"ci-binding","identity":"correction-binding","itemId":"{item}","revision":1,"collectionId":"correction-collection","repository":"o/r","head":"{request.Head}","prNumber":7,"workflow":"*","featureId":"{request.Prior.FeatureId}","attemptId":"{request.Prior.AttemptId}","parentAttemptId":null,"producerStream":"routine-delivery","binding":"exact"}}"""
                  $"""{{"kind":"ci-page","identity":"correction-page","itemId":"{item}","revision":1,"collectionId":"correction-collection","resource":"runs","page":1,"count":1,"total":1}}"""
                  $"""{{"kind":"ci-run","identity":"correction-run","itemId":"{item}","revision":1,"repository":"o/r","runId":10,"attempt":1,"workflow":"ci.yml","event":"pull_request","head":"{request.Head}","status":"completed","conclusion":"success","createdAt":"2026-09-08T10:00:00Z","startedAt":"2026-09-08T10:00:10Z","updatedAt":"2026-09-08T10:01:00Z"}}"""
                  $"""{{"kind":"ci-job","identity":"correction-job","itemId":"{item}","revision":1,"repository":"o/r","runId":10,"attempt":1,"jobId":101,"name":"build","status":"completed","conclusion":"success","createdAt":"2026-09-08T10:00:00Z","startedAt":"2026-09-08T10:00:10Z","completedAt":"2026-09-08T10:01:00Z"}}"""
                  $"""{{"kind":"ci-step","identity":"correction-step","itemId":"{item}","revision":1,"repository":"o/r","runId":10,"attempt":1,"jobId":101,"number":1,"name":"test","status":"completed","conclusion":"success","startedAt":"2026-09-08T10:00:10Z","completedAt":"2026-09-08T10:01:00Z","classification":"useful-validation","rationale":"fixture exact"}}"""
                  $"""{{"kind":"ci-check","identity":"correction-check","itemId":"{item}","revision":1,"repository":"o/r","checkId":1001,"name":"build","appSlug":"github-actions","status":"completed","conclusion":"success","startedAt":"2026-09-08T10:00:10Z","completedAt":"2026-09-08T10:01:00Z"}}"""
                  $"""{{"kind":"ci-coverage","identity":"correction-coverage","itemId":"{item}","revision":1,"collectionId":"correction-collection","inventory":"complete","attempts":"complete","jobPages":"complete","terminal":"complete","timestamps":"complete","lineage":"complete","classification":"complete","criticalPath":"unknown"}}"""
                  $"""{{"kind":"ci-population-coverage","identity":"correction-population-coverage","itemId":"{item}","revision":1,"collectionId":"correction-collection","actions":"complete","checks":"complete","attempts":"complete","jobs":"complete","terminal":"complete","timestamps":"complete","continuation":"none","externalChecks":0,"gaps":"[]"}}""" ]
        TelemetryStoreApplication.ingest path approved
            (operationalBatch "correction-fixture" item ((nativeOutcome item 1L "delivered" "delivered" "2026-09-08T10:04:01Z") :: population))
        |> unwrap |> ignore
        cleanup, path, request

    [<Theory>]
    [<InlineData(true)>]
    [<InlineData(false)>]
    let ``UTEL-06.8 correction moves one effective delivery and CI while immutable bytes survive replay and restart`` fullPopulation =
        let cleanup, path, request = correctionFixture fullPopulation
        use cleanup = cleanup
        let original = correctionSql path "SELECT group_concat(content_digest || canonical,'|') FROM (SELECT content_digest,canonical FROM ingest_facts ORDER BY identity);"
        let plan = TelemetryStoreApplication.ciCorrectionPlan path approved request |> unwrap |> Encoding.UTF8.GetBytes
        let parsed = TelemetryCi.parseCorrectionPlan plan |> unwrap
        Assert.Equal((if fullPopulation then 10 else 1), parsed.Targets.Length)
        let before = TelemetryStoreApplication.ciSummary path approved request.Prior.ItemId |> unwrap
        Assert.Contains("\"deliveries\":1", before)
        Assert.Contains("\"status\":\"applied\"", TelemetryStoreApplication.ciCorrect path approved plan |> unwrap)
        // Reopen/migration readback and lost-acknowledgement exact retry use new connections.
        TelemetryStoreApplication.initialize path approved |> unwrap |> ignore
        Assert.Contains("already-applied", TelemetryStoreApplication.ciCorrect path approved plan |> unwrap)
        Assert.Equal(original, correctionSql path "SELECT group_concat(content_digest || canonical,'|') FROM (SELECT content_digest,canonical FROM ingest_facts ORDER BY identity);")
        Assert.Equal("1", correctionSql path "SELECT count(*) FROM ci_attribution_corrections;")
        Assert.Equal("1", correctionSql path "SELECT count(*) FROM native_item_outcomes;")
        let oldReport = TelemetryStoreApplication.ciSummary path approved request.Prior.ItemId |> unwrap
        let newReport = TelemetryStoreApplication.ciSummary path approved request.Effective.ItemId |> unwrap
        Assert.Contains("\"deliveries\":0", oldReport)
        Assert.Contains("\"deliveries\":1", newReport)
        Assert.Contains("GOV-423", newReport)
        Assert.Contains("genuine-attempt", newReport)
        if fullPopulation then
            Assert.Contains("\"runnerSeconds\":50", newReport)
            Assert.Contains("\"jobs\":0", oldReport)
        else Assert.Contains("\"inventoryCoverage\":\"unknown\"", newReport)
        Assert.Equal("0", correctionSql path $"SELECT count(*) FROM budget_population_facts WHERE item_id='{request.Prior.ItemId}' AND source_ref LIKE 'derived:%%';")
        Assert.Equal("0", correctionSql path "SELECT count(*) FROM budget_interventions;")
        let history = TelemetryStoreApplication.ciCorrectionHistory path approved request.CorrectionId |> unwrap
        Assert.Contains("\"counting\":false", history)
        Assert.Contains("wrong-attempt", history)
        Assert.Contains("genuine-attempt", history)
        Assert.Contains("\"active\":true", history)
        Assert.Equal(Error [ "corrected-native-delivery-source-unsupported" ], TelemetryStoreApplication.resolveNativeDeliveryCandidate path approved ("routine-delivery:" + request.Prior.ItemId))
        Assert.Contains(request.Effective.ItemId, TelemetryStoreApplication.itemDetail path approved request.Effective.ItemId |> unwrap)
        use snapshot = TelemetryStoreApplication.dashboardSnapshot path approved None |> unwrap |> correctionSnapshot 2
        let items = snapshot.RootElement.GetProperty("items").EnumerateArray() |> Seq.map _.GetString() |> Seq.toList
        Assert.Contains(request.Effective.ItemId, items)
        Assert.DoesNotContain(request.Prior.ItemId, items)
        let outcomes = snapshot.RootElement.GetProperty("outcomes").EnumerateArray() |> Seq.toList
        let delivered = Assert.Single outcomes
        Assert.Equal(request.Effective.ItemId, delivered.GetProperty("item_id").GetString())
        Assert.Equal(request.Head, delivered.GetProperty("head").GetString())
        Assert.Equal(request.MergeCommit, delivered.GetProperty("merge_commit").GetString())
        let currentSummary = snapshot.RootElement.GetProperty("summaries").EnumerateArray() |> Seq.find (fun row -> row.GetProperty("item").GetString() = request.Effective.ItemId)
        Assert.Equal((if fullPopulation then 10L else 1L), currentSummary.GetProperty("factCount").GetInt64())

    [<Fact>]
    let ``UTEL-06.8 rollback stale concurrent predecessor cross-store and content conflicts have zero partial writes`` () =
        let cleanup, path, request = correctionFixture true
        use cleanup = cleanup
        let plan = TelemetryStoreApplication.ciCorrectionPlan path approved request |> unwrap |> Encoding.UTF8.GetBytes
        let competing = TelemetryStoreApplication.ciCorrectionPlan path approved { request with CorrectionId = "competing" } |> unwrap |> Encoding.UTF8.GetBytes
        Assert.Equal(Error [ "fixture-before-commit" ], TelemetryStoreApplication.ciCorrectWithHook path approved plan (fun () -> invalidOp "fixture-before-commit"))
        Assert.Equal("0", correctionSql path "SELECT count(*) FROM ci_attribution_corrections;")
        Assert.Equal(request.Prior.ItemId, correctionSql path "SELECT item_id FROM native_item_outcomes;")
        TelemetryStoreApplication.ciCorrect path approved plan |> unwrap |> ignore
        Assert.Equal(Error [ "ci-correction-target-unavailable" ], TelemetryStoreApplication.ciCorrect path approved competing)
        let changed = Encoding.UTF8.GetString(plan).Replace("Recovered exact source assignment", "Changed content") |> Encoding.UTF8.GetBytes
        Assert.Equal(Error [ "ci-correction-identity-content-conflict" ], TelemetryStoreApplication.ciCorrect path approved changed)
        let otherCleanup, otherPath, _ = correctionFixture false
        use otherCleanup = otherCleanup
        Assert.Equal(Error [ "ci-correction-cross-store-conflict" ], TelemetryStoreApplication.ciCorrect otherPath approved plan)
        let chainedRequest = { request with CorrectionId = "correction-two"; ExpectedPredecessor = Some request.CorrectionId; Prior = request.Effective; Effective = { request.Effective with ItemId = "GOV-423-C3-corrected"; AttemptId = "genuine-attempt-two" } }
        let chained = TelemetryStoreApplication.ciCorrectionPlan path approved chainedRequest |> unwrap |> Encoding.UTF8.GetBytes
        TelemetryStoreApplication.ciCorrect path approved chained |> unwrap |> ignore
        Assert.Equal("2", correctionSql path "SELECT count(*) FROM ci_attribution_corrections;")
        Assert.Equal("1", correctionSql path "SELECT count(*) FROM native_item_outcomes;")
        Assert.Contains("genuine-attempt-two", TelemetryStoreApplication.ciSummary path approved chainedRequest.Effective.ItemId |> unwrap)
        Assert.Equal("0", correctionSql path $"SELECT count(*) FROM current_ingest_facts WHERE item_id='{request.Effective.ItemId}';")
        Assert.Equal("10", correctionSql path $"SELECT count(*) FROM ingest_facts WHERE item_id='{request.Prior.ItemId}';")
        Assert.Contains("\"factCount\":0", TelemetryStoreApplication.summary path approved request.Effective.ItemId |> unwrap)
        use currentSnapshot = TelemetryStoreApplication.dashboardSnapshot path approved None |> unwrap |> correctionSnapshot 2
        let currentItems = currentSnapshot.RootElement.GetProperty("items").EnumerateArray() |> Seq.map _.GetString() |> Seq.toList
        Assert.Contains(chainedRequest.Effective.ItemId, currentItems)
        Assert.DoesNotContain(request.Prior.ItemId, currentItems)
        Assert.DoesNotContain(request.Effective.ItemId, currentItems)
        let currentOutcome = Assert.Single(currentSnapshot.RootElement.GetProperty("outcomes").EnumerateArray() |> Seq.toList)
        Assert.Equal(chainedRequest.Effective.ItemId, currentOutcome.GetProperty("item_id").GetString())
        Assert.Contains("\"deliveries\":0", TelemetryStoreApplication.ciSummary path approved request.Effective.ItemId |> unwrap)
        Assert.Contains("\"deliveries\":1", TelemetryStoreApplication.ciSummary path approved chainedRequest.Effective.ItemId |> unwrap)
        // Target the current outcome so this isolates the obsolete predecessor,
        // rather than refusing the now-unavailable first effective assignment.
        let stale =
            { chainedRequest with
                CorrectionId = "stale"
                Prior = chainedRequest.Effective
                Effective = { chainedRequest.Effective with ItemId = "GOV-423-C3-next"; AttemptId = "genuine-attempt-next" } }
        let unchangedSql =
            "SELECT (SELECT group_concat(content_digest || canonical) FROM ingest_facts) || (SELECT group_concat(identity || item_id) FROM current_ingest_facts) || (SELECT group_concat(plan_digest || plan) FROM ci_attribution_corrections) || (SELECT group_concat(digest || canonical) FROM ci_correction_evidence);"
        let beforeStale = correctionSql path unchangedSql
        Assert.Equal(Error [ "ci-correction-predecessor-conflict" ], TelemetryStoreApplication.ciCorrectionPlan path approved stale)
        Assert.Equal(beforeStale, correctionSql path unchangedSql)
        Assert.Equal("2", correctionSql path "SELECT count(*) FROM ci_attribution_corrections;")
        Assert.Equal(chainedRequest.Effective.ItemId, correctionSql path "SELECT item_id FROM native_item_outcomes;")

    [<Fact>]
    let ``UTEL-06.8 source replay cannot resurrect wrong assignment or duplicate a corrected reconcile candidate`` () =
        let cleanup, path, request = correctionFixture false
        use cleanup = cleanup
        let plan = TelemetryStoreApplication.ciCorrectionPlan path approved request |> unwrap |> Encoding.UTF8.GetBytes
        TelemetryStoreApplication.ciCorrect path approved plan |> unwrap |> ignore
        let rawBefore = correctionSql path "SELECT content_digest || canonical FROM ingest_facts;"
        let ledgerBefore = correctionSql path "SELECT plan_digest || plan FROM ci_attribution_corrections;"
        let evidenceBefore = correctionSql path "SELECT digest || canonical FROM ci_correction_evidence;"
        let original = nativeOutcome request.Prior.ItemId 2L "delivered" "delivered" "2026-09-08T10:04:02Z"
        Assert.True(TelemetryStoreApplication.ingest path approved (operationalBatch "late-original" request.Prior.ItemId [ original ]) |> Result.isError)
        let corrected = nativeOutcome request.Effective.ItemId 2L "delivered" "delivered" "2026-09-08T10:04:02Z"
        Assert.True(TelemetryStoreApplication.ingest path approved (operationalBatch "late-corrected" request.Effective.ItemId [ corrected ]) |> Result.isError)
        Assert.Equal("1", correctionSql path "SELECT count(*) FROM native_item_outcomes;")
        Assert.Equal(request.Effective.ItemId, correctionSql path "SELECT item_id FROM native_item_outcomes;")
        Assert.Equal(rawBefore, correctionSql path "SELECT content_digest || canonical FROM ingest_facts;")
        Assert.Equal(ledgerBefore, correctionSql path "SELECT plan_digest || plan FROM ci_attribution_corrections;")
        Assert.Equal(evidenceBefore, correctionSql path "SELECT digest || canonical FROM ci_correction_evidence;")
        Assert.Empty(Directory.GetFiles(Path.Combine(path, "inbox"), "*.ready", SearchOption.AllDirectories))
        let rejected = Directory.GetFiles(Path.Combine(path, "quarantine"), "*.rejected", SearchOption.AllDirectories)
        Assert.Equal(2, rejected.Length)
        let reasons = rejected |> Array.map (fun file -> File.ReadAllText(file + ".reason"))
        Assert.Contains("ci-attribution-corrected-fact-is-immutable", reasons)
        Assert.Contains("ci-attribution-corrected-candidate-reconcile-refused", reasons)
        let distinctHead = String.replicate 40 "c"
        let independent = corrected.Replace(request.Head, distinctHead)
        TelemetryStoreApplication.ingest path approved (operationalBatch "distinct-head" request.Effective.ItemId [ independent ]) |> unwrap |> ignore
        Assert.Equal("2", correctionSql path "SELECT count(*) FROM native_item_outcomes;")
        Assert.Equal("1", correctionSql path $"SELECT count(*) FROM native_item_outcomes WHERE head='{request.Head}' AND item_id='{request.Effective.ItemId}';")
        Assert.Equal("1", correctionSql path $"SELECT count(*) FROM native_item_outcomes WHERE head='{distinctHead}' AND item_id='{request.Effective.ItemId}';")
        Assert.Equal(ledgerBefore, correctionSql path "SELECT plan_digest || plan FROM ci_attribution_corrections;")
        Assert.Equal(evidenceBefore, correctionSql path "SELECT digest || canonical FROM ci_correction_evidence;")

    [<Fact>]
    let ``UTEL-06.8 schema12 migration and stale digest refuse before mutation`` () =
        let cleanup, path, request = correctionFixture false
        use cleanup = cleanup
        correctionSql path (dropCorrectionSchema + " PRAGMA user_version=12;") |> ignore
        Assert.Equal(Error [ "unsupported-version" ], TelemetryStoreApplication.ciCorrectionPlan path approved request)
        let rawBeforeMigration = correctionSql path "SELECT group_concat(content_digest || canonical,'|') FROM (SELECT content_digest,canonical FROM ingest_facts ORDER BY identity);"
        let receiptsBeforeMigration = correctionSql path "SELECT group_concat(version || ':' || digest,'|') FROM (SELECT version,digest FROM schema_migrations ORDER BY version);"
        correctionSql path "CREATE TRIGGER fixture_migration_abort BEFORE INSERT ON store_metadata WHEN NEW.key='ciCorrectionStoreId' BEGIN SELECT RAISE(ABORT,'fixture-schema13-rollback'); END; SELECT 1;" |> ignore
        Assert.True(TelemetryStoreApplication.initialize path approved |> Result.isError)
        Assert.Equal("12", correctionSql path "PRAGMA user_version;")
        Assert.Equal("0", correctionSql path "SELECT count(*) FROM sqlite_master WHERE name IN ('ci_attribution_corrections','ci_correction_evidence','ci_effective_attribution','current_ingest_facts');")
        Assert.Equal(receiptsBeforeMigration, correctionSql path "SELECT group_concat(version || ':' || digest,'|') FROM (SELECT version,digest FROM schema_migrations ORDER BY version);")
        Assert.Equal(rawBeforeMigration, correctionSql path "SELECT group_concat(content_digest || canonical,'|') FROM (SELECT content_digest,canonical FROM ingest_facts ORDER BY identity);")
        Assert.True(TelemetryStoreApplication.migrate13ToCurrent path approved |> Result.isError)
        Assert.Equal(rawBeforeMigration, correctionSql path "SELECT group_concat(content_digest || canonical,'|') FROM (SELECT content_digest,canonical FROM ingest_facts ORDER BY identity);")

    [<Fact>]
    let ``UTEL-06.8 writer contention refuses and unrelated delivery runtime explicit population survive`` () =
        let cleanup, path, request = correctionFixture true
        use cleanup = cleanup
        let unrelated =
            (nativeOutcome request.Prior.ItemId 1L "delivered" "delivered" "2026-09-08T10:05:00Z")
                .Replace("native-outcome-" + request.Prior.ItemId, "unrelated-native-outcome")
                .Replace("o/r", "other/repo").Replace("routine-delivery:" + request.Prior.ItemId, "routine-delivery:unrelated")
        let terminal = runtimeTerminal request.Prior.ItemId "unrelated-terminal" "unrelated-invocation" "complete" 0
        let explicitPopulation = population request.Prior.ItemId 1L "open" "explicit-unrelated"
        TelemetryStoreApplication.ingest path approved (operationalBatch "unrelated-facts" request.Prior.ItemId [ unrelated; terminal; explicitPopulation ]) |> unwrap |> ignore
        let unrelatedBytes = correctionSql path "SELECT group_concat(canonical,'|') FROM (SELECT canonical FROM ingest_facts WHERE identity IN ('unrelated-native-outcome','unrelated-terminal') ORDER BY identity);"
        let plan = TelemetryStoreApplication.ciCorrectionPlan path approved request |> unwrap |> Encoding.UTF8.GetBytes
        let mutable contention = None
        TelemetryStoreApplication.ciCorrectWithHook path approved plan (fun () -> contention <- Some(TelemetryStoreApplication.ciCorrect path approved plan)) |> unwrap |> ignore
        Assert.Equal(Some(Error [ "writer-busy" ]), contention)
        Assert.Equal(unrelatedBytes, correctionSql path "SELECT group_concat(canonical,'|') FROM (SELECT canonical FROM ingest_facts WHERE identity IN ('unrelated-native-outcome','unrelated-terminal') ORDER BY identity);")
        Assert.Equal(request.Prior.ItemId, correctionSql path "SELECT item_id FROM native_item_outcomes WHERE identity='unrelated-native-outcome';")
        Assert.Equal(request.Prior.ItemId, correctionSql path "SELECT item_id FROM runtime_terminals WHERE identity='unrelated-terminal';")
        Assert.Equal("1", correctionSql path "SELECT count(*) FROM budget_population_facts WHERE source_ref='explicit-unrelated';")
        Assert.Equal("1", correctionSql path $"SELECT count(*) FROM native_item_outcomes WHERE item_id='{request.Prior.ItemId}';")
        Assert.Equal("1", correctionSql path $"SELECT count(*) FROM native_item_outcomes WHERE item_id='{request.Effective.ItemId}';")

    [<Fact>]
    let ``UTEL-06.8 ambiguous check ownership refuses without moving any effective rows`` () =
        let cleanup, path, request = correctionFixture true
        use cleanup = cleanup
        let another =
            (nativeOutcome request.Prior.ItemId 1L "delivered" "delivered" "2026-09-08T10:05:00Z")
                .Replace("native-outcome-" + request.Prior.ItemId, "another-candidate")
                .Replace(request.Head, String.replicate 40 "c").Replace("routine-delivery:" + request.Prior.ItemId, "routine-delivery:another")
        TelemetryStoreApplication.ingest path approved (operationalBatch "another-candidate" request.Prior.ItemId [ another ]) |> unwrap |> ignore
        Assert.Equal(Error [ "ci-correction-check-ownership-ambiguous" ], TelemetryStoreApplication.ciCorrectionPlan path approved request)
        Assert.Equal("0", correctionSql path "SELECT count(*) FROM ci_attribution_corrections;")
        Assert.Equal(request.Prior.ItemId, correctionSql path "SELECT item_id FROM ci_bindings;")

    [<Fact>]
    let ``UTEL-06.8 real CLI plans applies retries and reads history and coherent reports after restart`` () =
        let cleanup, path, request = correctionFixture true
        use cleanup = cleanup
        let input = Path.Combine(path, "request.json")
        let output = Path.Combine(path, "plan.json")
        let proposed = TelemetryStoreApplication.ciCorrectionPlan path approved request |> unwrap
        use document = JsonDocument.Parse proposed
        File.WriteAllText(input, document.RootElement.GetProperty("request").GetRawText())
        File.SetUnixFileMode(input, UnixFileMode.UserRead ||| UnixFileMode.UserWrite)
        let invoke action arguments =
            let priorOut, priorError = Console.Out, Console.Error
            use stdout = new StringWriter()
            use stderr = new StringWriter()
            try
                Console.SetOut stdout
                Console.SetError stderr
                let code = TelemetryCiApplication.runWithAssessment approved action ([ "--store-root"; path ] @ arguments)
                code, stdout.ToString(), stderr.ToString()
            finally
                Console.SetOut priorOut
                Console.SetError priorError
        let code, planned, error = invoke "correction-plan" [ "--input"; input; "--output"; output ]
        Assert.Equal(0, code)
        Assert.Equal("", error)
        Assert.Contains("planned", planned)
        Assert.Equal(UnixFileMode.UserRead ||| UnixFileMode.UserWrite, File.GetUnixFileMode output)
        let code, applied, error = invoke "correct" [ "--plan"; output ]
        Assert.Equal(0, code)
        Assert.Equal("", error)
        Assert.Contains("\"status\":\"applied\"", applied)
        TelemetryStoreApplication.initialize path approved |> unwrap |> ignore
        let code, retried, _ = invoke "correct" [ "--plan"; output ]
        Assert.Equal(0, code)
        Assert.Contains("already-applied", retried)
        let code, history, _ = invoke "correction-history" [ "--correction-id"; request.CorrectionId ]
        Assert.Equal(0, code)
        Assert.Contains("\"counting\":false", history)
        let code, oldReport, _ = invoke "summary" [ "--item"; request.Prior.ItemId ]
        Assert.Equal(0, code)
        Assert.Contains("\"deliveries\":0", oldReport)
        let code, newReport, _ = invoke "summary" [ "--item"; request.Effective.ItemId ]
        Assert.Equal(0, code)
        Assert.Contains("\"deliveries\":1", newReport)
        Assert.Contains("\"runnerSeconds\":50", newReport)

    [<Fact>]
    let ``UTEL-06.8 correction removes stale derived current breach inputs while frozen epochs and history survive`` () =
        let cleanup, path, request = correctionFixture false
        use cleanup = cleanup
        let old = request.Prior.ItemId
        correctionSql path $"""
INSERT INTO budget_epoch_membership VALUES('epoch-1','{old}','{old}');
INSERT INTO budget_assessment_revisions VALUES('{old}','ci-wall','github','whole-item',1,'epoch-1','breach',30,100,1,'old-derived','old-digest');
INSERT INTO budget_breaches VALUES('epoch-1','{old}','ci-wall','github','whole-item',1,1);
INSERT INTO budget_epochs VALUES('frozen-epoch',999,'verified');
INSERT INTO budget_assessment_revisions VALUES('{old}','frozen-scope','github','whole-item',1,'frozen-epoch','breach',30,100,1,'frozen-derived','frozen-digest');
INSERT INTO budget_breaches VALUES('frozen-epoch','{old}','frozen-scope','github','whole-item',1,1);
SELECT 1;
        """ |> ignore
        let frozen = correctionSql path "SELECT group_concat(epoch_id || item_id || assessment_revision,'|') FROM budget_breaches WHERE epoch_id='frozen-epoch';"
        let plan = TelemetryStoreApplication.ciCorrectionPlan path approved request |> unwrap |> Encoding.UTF8.GetBytes
        TelemetryStoreApplication.ciCorrect path approved plan |> unwrap |> ignore
        Assert.Contains("\"verdict\":\"unknown\"", TelemetryStoreApplication.budgetSummary path approved old |> unwrap)
        Assert.Contains("\"distinctBreaches\":0", TelemetryStoreApplication.budgetStatus path approved |> unwrap)
        Assert.Equal(frozen, correctionSql path "SELECT group_concat(epoch_id || item_id || assessment_revision,'|') FROM budget_breaches WHERE epoch_id='frozen-epoch';")
        Assert.Equal("2", correctionSql path "SELECT count(*) FROM budget_assessment_revisions WHERE assessment_revision=1;")
        Assert.Equal("0", correctionSql path "SELECT count(*) FROM budget_interventions;")


    let private compactCiSql path sql =
        use connection = new SqliteConnection($"Data Source={Path.Combine(path, TelemetryStoreApplication.databaseFileName)};Pooling=False")
        connection.Open()
        use command = connection.CreateCommand()
        command.CommandText <- sql
        command.ExecuteNonQuery() |> ignore

    let private compactCiSnapshot path =
        TelemetryStoreApplication.compactDashboardSnapshot path approved None |> unwrap |> correctionSnapshot 3

    [<Fact>]
    let ``compact dashboard CI preserves empty summary and version two`` () =
        let cleanup, path = root ()
        use cleanup = cleanup
        TelemetryStoreApplication.initialize path approved |> unwrap |> ignore
        compactCiSql path "INSERT INTO budget_population_facts VALUES('p','item','item','open','test','test:p',0);"
        use compact = compactCiSnapshot path
        let summary = compact.RootElement.GetProperty("ciSummaries")[0]
        use expected = JsonDocument.Parse(TelemetryStoreApplication.ciSummary path approved "item" |> unwrap)
        Assert.Equal(CanonicalJson.canonicalize (Encoding.UTF8.GetBytes(expected.RootElement.GetRawText())) |> unwrap, CanonicalJson.canonicalize (Encoding.UTF8.GetBytes(summary.GetRawText())) |> unwrap)
        Assert.Equal(0L, summary.GetProperty("steps").GetInt64())
        Assert.Equal(JsonValueKind.Null, summary.GetProperty("runnerSeconds").ValueKind)
        let mutable omitted = Unchecked.defaultof<JsonElement>
        Assert.False(compact.RootElement.TryGetProperty("ciSteps", &omitted))
        use original = TelemetryStoreApplication.dashboardSnapshot path approved None |> unwrap |> correctionSnapshot 2
        Assert.Equal(JsonValueKind.Array, original.RootElement.GetProperty("ciSteps").ValueKind)

    [<Fact>]
    let ``compact dashboard CI uses exact overlap unions for all five classes`` () =
        let cleanup, path = root ()
        use cleanup = cleanup
        TelemetryStoreApplication.initialize path approved |> unwrap |> ignore
        compactCiSql path "INSERT INTO budget_population_facts VALUES('p','item','item','open','test','test:p',0);"
        let classes = [ "useful-validation", "usefulValidationSeconds"; "admin", "administrativeSeconds"; "necessary-setup", "necessarySetupSeconds"; "mixed", "mixedSeconds"; "unclassified", "unclassifiedSeconds" ]
        for index, (classification, _) in List.indexed classes do
            compactCiSql path ($"INSERT INTO ci_steps VALUES('s{index}a','item','FS-GG/.github',1,1,1,{index * 3},'step','completed','success','2026-10-06T00:00:00Z','2026-10-06T00:00:10Z','{classification}','test'),('s{index}b','item','FS-GG/.github',1,1,1,{index * 3 + 1},'step','completed','success','2026-10-06T00:00:05Z','2026-10-06T00:00:15Z','{classification}','test'),('s{index}c','item','FS-GG/.github',1,1,1,{index * 3 + 2},'step','completed','success','2026-10-06T00:00:15Z','2026-10-06T00:00:00Z','{classification}','test');")
        use compact = compactCiSnapshot path
        let summary = compact.RootElement.GetProperty("ciSummaries")[0]
        use expected = JsonDocument.Parse(TelemetryStoreApplication.ciSummary path approved "item" |> unwrap)
        Assert.Equal(CanonicalJson.canonicalize (Encoding.UTF8.GetBytes(expected.RootElement.GetRawText())) |> unwrap, CanonicalJson.canonicalize (Encoding.UTF8.GetBytes(summary.GetRawText())) |> unwrap)
        Assert.Equal(15L, summary.GetProperty("steps").GetInt64())
        for _, metric in classes do Assert.Equal(15L, summary.GetProperty(metric).GetInt64())

    [<Fact>]
    let ``compact dashboard CI does not truncate a large step population`` () =
        let cleanup, path = root ()
        use cleanup = cleanup
        TelemetryStoreApplication.initialize path approved |> unwrap |> ignore
        compactCiSql path "INSERT INTO budget_population_facts VALUES('p','item','item','open','test','test:p',0); WITH RECURSIVE n(x) AS (SELECT 1 UNION ALL SELECT x+1 FROM n WHERE x<10001) INSERT INTO ci_steps SELECT 's'||x,'item','FS-GG/.github',1,1,1,x,'step','completed','success','2026-10-06T00:00:00Z','2026-10-06T00:00:15Z','useful-validation','test' FROM n;"
        Assert.True(TelemetryStoreApplication.dashboardSnapshot path approved None |> Result.isError)
        use compact = compactCiSnapshot path
        let summary = compact.RootElement.GetProperty("ciSummaries")[0]
        Assert.Equal(10001L, summary.GetProperty("steps").GetInt64())
        Assert.Equal(15L, summary.GetProperty("usefulValidationSeconds").GetInt64())

    [<Fact>]
    let ``compact dashboard CI stays in original WAL snapshot after first read`` () =
        let cleanup, path = root ()
        use cleanup = cleanup
        TelemetryStoreApplication.initialize path approved |> unwrap |> ignore
        compactCiSql path "INSERT INTO budget_population_facts VALUES('p','item','item','open','test','test:p',0);"
        let hooks: TelemetryStoreApplication.DashboardSnapshotHooks =
            { AfterFirstRead = fun () -> compactCiSql path "INSERT INTO ci_steps VALUES('late','item','FS-GG/.github',1,1,1,1,'step','completed','success',NULL,NULL,'admin','test');" }
        use compact = TelemetryStoreApplication.compactDashboardSnapshotWithHooks path approved hooks None |> unwrap |> correctionSnapshot 3
        let observedSummary = compact.RootElement.GetProperty("ciSummaries")[0]
        Assert.Equal(0L, observedSummary.GetProperty("steps").GetInt64())
        use after = JsonDocument.Parse(TelemetryStoreApplication.ciSummary path approved "item" |> unwrap)
        Assert.Equal(1L, after.RootElement.GetProperty("steps").GetInt64())

    [<Fact>]
    let ``compact dashboard keeps item and non CI relation bounds`` () =
        let cleanup, path = root ()
        use cleanup = cleanup
        TelemetryStoreApplication.initialize path approved |> unwrap |> ignore
        compactCiSql path "WITH RECURSIVE n(x) AS (SELECT 1 UNION ALL SELECT x+1 FROM n WHERE x<201) INSERT INTO budget_population_facts SELECT 'p'||x,'item'||x,'item'||x,'open','test','test:'||x,0 FROM n;"
        Assert.True(TelemetryStoreApplication.compactDashboardSnapshot path approved None |> Result.isError)
        compactCiSql path "DELETE FROM budget_population_facts; WITH RECURSIVE n(x) AS (SELECT 1 UNION ALL SELECT x+1 FROM n WHERE x<10001) INSERT INTO budget_epochs SELECT 'bound-epoch'||x,x+1,'verified' FROM n;"
        Assert.True(TelemetryStoreApplication.compactDashboardSnapshot path approved None |> Result.isError)


    [<Fact>]
    let ``compact dashboard keeps original byte bounds`` () =
        let cleanup, path = root ()
        use cleanup = cleanup
        TelemetryStoreApplication.initialize path approved |> unwrap |> ignore
        compactCiSql path "INSERT INTO budget_population_facts VALUES('p','item','item','open','test','test:p',0); WITH RECURSIVE n(x) AS (SELECT 1 UNION ALL SELECT x+1 FROM n WHERE x<40) INSERT INTO budget_epochs SELECT hex(randomblob(65536)),x+1,'verified' FROM n;"
        Assert.True(TelemetryStoreApplication.compactDashboardSnapshot path approved None |> Result.isError)


    [<Fact>]
    let ``compact dashboard CI preserves fractional offset and disjoint unions`` () =
        let cleanup, path = root ()
        use cleanup = cleanup
        TelemetryStoreApplication.initialize path approved |> unwrap |> ignore
        compactCiSql path "INSERT INTO budget_population_facts VALUES('p','item','item','open','test','test:p',0); INSERT INTO ci_jobs VALUES('j1','item','FS-GG/.github',1,1,1,'job','completed','success','2026-10-06T00:00:00Z','2026-10-06T00:00:00.100Z','2026-10-06T00:00:01.900Z'),('j2','item','FS-GG/.github',1,1,2,'job','completed','success','2026-10-06T01:00:00.500+01:00','2026-10-06T01:00:01.200+01:00','2026-10-06T01:00:03.800+01:00'); INSERT INTO ci_steps VALUES('s1','item','FS-GG/.github',1,1,1,1,'step','completed','success','2026-10-06T00:00:00.100Z','2026-10-06T00:00:01.900Z','useful-validation','test'),('s2','item','FS-GG/.github',1,1,1,2,'step','completed','success','2026-10-06T01:00:01.200+01:00','2026-10-06T01:00:03.800+01:00','useful-validation','test'),('s3','item','FS-GG/.github',1,1,1,3,'step','completed','success','2026-10-06T00:00:10.100Z','2026-10-06T00:00:10.900Z','useful-validation','test');"
        use compact = compactCiSnapshot path
        let summary = compact.RootElement.GetProperty("ciSummaries")[0]
        use expected = JsonDocument.Parse(TelemetryStoreApplication.ciSummary path approved "item" |> unwrap)
        Assert.Equal(CanonicalJson.canonicalize (Encoding.UTF8.GetBytes(expected.RootElement.GetRawText())) |> unwrap, CanonicalJson.canonicalize (Encoding.UTF8.GetBytes(summary.GetRawText())) |> unwrap)
        Assert.Equal(3L, summary.GetProperty("runnerSeconds").GetInt64())
        Assert.Equal(3L, summary.GetProperty("wallSeconds").GetInt64())
        Assert.Equal(0L, summary.GetProperty("queueSeconds").GetInt64())
        Assert.Equal(3L, summary.GetProperty("usefulValidationSeconds").GetInt64())

    // Source-only proposed qualification. Genuine schema13 is constructed from the
    // exact historical additive DDL1..13, never a schema14 database with altered pragma.
    let private genuineSchema13 path =
        Directory.CreateDirectory path |> ignore
        use connection = new SqliteConnection($"Data Source={Path.Combine(path, TelemetryStoreApplication.databaseFileName)};Pooling=False")
        connection.Open()
        use command = connection.CreateCommand()
        command.CommandText <- "PRAGMA foreign_keys=ON; PRAGMA journal_mode=DELETE; PRAGMA synchronous=FULL;"
        command.ExecuteNonQuery() |> ignore
        let migrations : string array = [|
            """
CREATE TABLE store_metadata(key TEXT PRIMARY KEY, value TEXT NOT NULL) STRICT;
CREATE TABLE schema_migrations(version INTEGER PRIMARY KEY, digest TEXT NOT NULL, applied_utc TEXT NOT NULL) STRICT;
CREATE TABLE items(identity TEXT PRIMARY KEY, item_id TEXT, feature_id TEXT) STRICT;
CREATE TABLE features(identity TEXT PRIMARY KEY, item_id TEXT, name TEXT NOT NULL) STRICT;
CREATE TABLE attempts(identity TEXT PRIMARY KEY, item_id TEXT, parent_item_id TEXT NOT NULL, status TEXT NOT NULL) STRICT;
CREATE TABLE parent_child(identity TEXT PRIMARY KEY, item_id TEXT, parent_id TEXT NOT NULL, child_id TEXT NOT NULL) STRICT;
CREATE TABLE pr_heads(identity TEXT PRIMARY KEY, item_id TEXT, repository TEXT NOT NULL, pr_number INTEGER NOT NULL CHECK(pr_number >= 0), head TEXT NOT NULL) STRICT;
CREATE TABLE source_cursors(source_identity TEXT NOT NULL, generation TEXT NOT NULL, cursor TEXT NOT NULL, batch_digest TEXT NOT NULL, PRIMARY KEY(source_identity,generation)) STRICT;
CREATE TABLE usage_observations(identity TEXT PRIMARY KEY, item_id TEXT, provider TEXT NOT NULL, model TEXT NOT NULL, effort TEXT NOT NULL, input_count INTEGER NOT NULL CHECK(input_count >= 0), cached_input INTEGER NOT NULL CHECK(cached_input >= 0), cache_write_input INTEGER NOT NULL CHECK(cache_write_input >= 0), output_count INTEGER NOT NULL CHECK(output_count >= 0), reasoning INTEGER, total INTEGER NOT NULL CHECK(total >= 0), responses INTEGER NOT NULL CHECK(responses >= 0), sessions INTEGER NOT NULL CHECK(sessions >= 0), turns INTEGER NOT NULL CHECK(turns >= 0)) STRICT;
CREATE TABLE delivery_observations(identity TEXT PRIMARY KEY, item_id TEXT, state TEXT NOT NULL, expected_head TEXT, observed_head TEXT, pr_number INTEGER) STRICT;
CREATE TABLE evidence_observations(identity TEXT PRIMARY KEY, item_id TEXT, digest TEXT NOT NULL, availability TEXT NOT NULL) STRICT;
CREATE TABLE coverage_observations(identity TEXT PRIMARY KEY, item_id TEXT, record_validity TEXT NOT NULL, join_integrity TEXT NOT NULL, population_coverage TEXT NOT NULL, qualification TEXT NOT NULL, eligible INTEGER, observed INTEGER) STRICT;
CREATE TABLE health_diagnostics(identity TEXT PRIMARY KEY, item_id TEXT, code TEXT NOT NULL, severity TEXT NOT NULL) STRICT;
CREATE TABLE ingest_batches(ingest_id TEXT PRIMARY KEY, content_digest TEXT NOT NULL, source_identity TEXT NOT NULL, generation TEXT NOT NULL, cursor TEXT NOT NULL, accepted_count INTEGER NOT NULL, replay_count INTEGER NOT NULL) STRICT;
CREATE TABLE ingest_facts(identity TEXT PRIMARY KEY, kind TEXT NOT NULL, item_id TEXT, revision INTEGER NOT NULL CHECK(revision >= 0), content_digest TEXT NOT NULL, canonical TEXT NOT NULL) STRICT;
CREATE TABLE corrections(sequence INTEGER PRIMARY KEY AUTOINCREMENT, kind TEXT NOT NULL, identity TEXT NOT NULL, old_revision INTEGER NOT NULL, new_revision INTEGER NOT NULL, old_digest TEXT NOT NULL, new_digest TEXT NOT NULL) STRICT;
PRAGMA user_version=1;
"""
            """
CREATE TABLE runtime_admissions(identity TEXT PRIMARY KEY, item_id TEXT NOT NULL, invocation_id TEXT NOT NULL UNIQUE, feature_id TEXT NOT NULL, attempt_id TEXT NOT NULL, parent_attempt_id TEXT, producer_stream TEXT NOT NULL, requested_model TEXT, requested_effort TEXT, backend TEXT) STRICT;
CREATE TABLE runtime_starts(identity TEXT PRIMARY KEY, item_id TEXT NOT NULL, invocation_id TEXT NOT NULL, thread_id TEXT, turn_id TEXT, turn_sequence INTEGER CHECK(turn_sequence >= 0), process_id INTEGER NOT NULL CHECK(process_id >= 0), phase TEXT NOT NULL) STRICT;
CREATE UNIQUE INDEX runtime_thread_start_identity ON runtime_starts(invocation_id,thread_id) WHERE phase='thread' AND thread_id IS NOT NULL;
CREATE UNIQUE INDEX runtime_turn_start_identity ON runtime_starts(invocation_id,thread_id,turn_sequence) WHERE phase='turn';
CREATE TABLE runtime_turn_usage(identity TEXT PRIMARY KEY, item_id TEXT NOT NULL, invocation_id TEXT NOT NULL, thread_id TEXT NOT NULL, turn_id TEXT, turn_sequence INTEGER NOT NULL CHECK(turn_sequence >= 0), provider TEXT, requested_model TEXT, observed_model TEXT, requested_effort TEXT, observed_effort TEXT, backend TEXT, accounting_scope TEXT NOT NULL, provenance TEXT NOT NULL, input_count INTEGER NOT NULL CHECK(input_count >= 0), cached_input INTEGER NOT NULL CHECK(cached_input >= 0), output_count INTEGER NOT NULL CHECK(output_count >= 0), reasoning INTEGER, total INTEGER NOT NULL CHECK(total >= 0), UNIQUE(invocation_id,thread_id,turn_sequence)) STRICT;
CREATE UNIQUE INDEX runtime_turn_native_identity ON runtime_turn_usage(invocation_id,thread_id,turn_id) WHERE turn_id IS NOT NULL;
CREATE TABLE runtime_terminals(identity TEXT PRIMARY KEY, item_id TEXT NOT NULL, invocation_id TEXT NOT NULL UNIQUE, thread_id TEXT, outcome TEXT NOT NULL, exit_code INTEGER NOT NULL CHECK(exit_code >= 0)) STRICT;
CREATE TABLE runtime_gaps(identity TEXT PRIMARY KEY, item_id TEXT NOT NULL, invocation_id TEXT NOT NULL, code TEXT NOT NULL) STRICT;
PRAGMA user_version=2;
"""
            """
CREATE TABLE ci_bindings(identity TEXT PRIMARY KEY, item_id TEXT NOT NULL, collection_id TEXT NOT NULL UNIQUE, repository TEXT NOT NULL, head TEXT NOT NULL, pr_number INTEGER NOT NULL CHECK(pr_number > 0), workflow TEXT NOT NULL, feature_id TEXT NOT NULL, attempt_id TEXT NOT NULL, parent_attempt_id TEXT, producer_stream TEXT NOT NULL, binding TEXT NOT NULL) STRICT;
CREATE TABLE ci_pages(identity TEXT PRIMARY KEY, item_id TEXT NOT NULL, collection_id TEXT NOT NULL REFERENCES ci_bindings(collection_id), resource TEXT NOT NULL, page INTEGER NOT NULL CHECK(page > 0), count INTEGER NOT NULL CHECK(count BETWEEN 0 AND 100), total INTEGER NOT NULL CHECK(total BETWEEN 0 AND 1000), UNIQUE(collection_id,resource,page)) STRICT;
CREATE TABLE ci_runs(identity TEXT PRIMARY KEY, item_id TEXT NOT NULL, repository TEXT NOT NULL, run_id INTEGER NOT NULL, attempt INTEGER NOT NULL CHECK(attempt > 0), workflow TEXT NOT NULL, event TEXT NOT NULL, head TEXT NOT NULL, status TEXT NOT NULL, conclusion TEXT, created_at TEXT, started_at TEXT, updated_at TEXT, UNIQUE(repository,run_id,attempt)) STRICT;
CREATE TABLE ci_jobs(identity TEXT PRIMARY KEY, item_id TEXT NOT NULL, repository TEXT NOT NULL, run_id INTEGER NOT NULL, attempt INTEGER NOT NULL CHECK(attempt > 0), job_id INTEGER NOT NULL, name TEXT NOT NULL, status TEXT NOT NULL, conclusion TEXT, created_at TEXT, started_at TEXT, completed_at TEXT, UNIQUE(repository,run_id,attempt,job_id)) STRICT;
CREATE TABLE ci_steps(identity TEXT PRIMARY KEY, item_id TEXT NOT NULL, repository TEXT NOT NULL, run_id INTEGER NOT NULL, attempt INTEGER NOT NULL CHECK(attempt > 0), job_id INTEGER NOT NULL, number INTEGER NOT NULL CHECK(number >= 0), name TEXT NOT NULL, status TEXT NOT NULL, conclusion TEXT, started_at TEXT, completed_at TEXT, classification TEXT NOT NULL, rationale TEXT NOT NULL, UNIQUE(repository,run_id,attempt,job_id,number)) STRICT;
CREATE TABLE ci_coverage(identity TEXT PRIMARY KEY, item_id TEXT NOT NULL, collection_id TEXT NOT NULL REFERENCES ci_bindings(collection_id), inventory TEXT NOT NULL, attempts TEXT NOT NULL, job_pages TEXT NOT NULL, terminal TEXT NOT NULL, timestamps TEXT NOT NULL, lineage TEXT NOT NULL, classification TEXT NOT NULL, critical_path TEXT NOT NULL) STRICT;
PRAGMA user_version=3;
"""
            """
CREATE TABLE budget_population_facts(identity TEXT PRIMARY KEY, item_id TEXT NOT NULL, original_item_id TEXT NOT NULL, state TEXT NOT NULL CHECK(state IN ('open','completed')), source_kind TEXT NOT NULL, source_ref TEXT NOT NULL UNIQUE, fact_revision INTEGER NOT NULL CHECK(fact_revision >= 0)) STRICT;
CREATE TABLE budget_attribution_facts(identity TEXT PRIMARY KEY, item_id TEXT NOT NULL, dimension TEXT NOT NULL, provider TEXT NOT NULL, accounting_scope TEXT NOT NULL, numerator INTEGER CHECK(numerator >= 0), denominator INTEGER CHECK(denominator >= 0), coverage TEXT NOT NULL, attribution TEXT NOT NULL, source_kind TEXT NOT NULL, source_ref TEXT NOT NULL UNIQUE, fact_revision INTEGER NOT NULL CHECK(fact_revision >= 0)) STRICT;
CREATE TABLE budget_interval_facts(identity TEXT PRIMARY KEY, item_id TEXT NOT NULL, dimension TEXT NOT NULL, classification TEXT NOT NULL CHECK(classification IN ('administrative','useful','productive')), start_ns INTEGER NOT NULL CHECK(start_ns >= 0), end_ns INTEGER NOT NULL CHECK(end_ns >= start_ns), witnessed INTEGER NOT NULL CHECK(witnessed IN (0,1)), source_kind TEXT NOT NULL, source_ref TEXT NOT NULL UNIQUE, fact_revision INTEGER NOT NULL CHECK(fact_revision >= 0)) STRICT;
CREATE TABLE budget_intervention_facts(identity TEXT PRIMARY KEY, item_id TEXT NOT NULL, intervention_id TEXT NOT NULL, transition TEXT NOT NULL CHECK(transition IN ('deployed','verified')), sequence INTEGER NOT NULL CHECK(sequence >= 0), result TEXT NOT NULL, coverage TEXT NOT NULL, source_ref TEXT NOT NULL UNIQUE, fact_revision INTEGER NOT NULL CHECK(fact_revision >= 0)) STRICT;
CREATE TABLE budget_shared_cost_refs(identity TEXT PRIMARY KEY, item_id TEXT NOT NULL, source_ref TEXT NOT NULL UNIQUE, dimension TEXT NOT NULL, provider TEXT NOT NULL, accounting_scope TEXT NOT NULL) STRICT;
CREATE TABLE budget_dirty_items(item_id TEXT PRIMARY KEY) STRICT;
CREATE TABLE budget_epochs(epoch_id TEXT PRIMARY KEY, ordinal INTEGER NOT NULL UNIQUE CHECK(ordinal > 0), state TEXT NOT NULL CHECK(state IN ('open','verified'))) STRICT;
CREATE UNIQUE INDEX budget_one_open_epoch ON budget_epochs(state) WHERE state='open';
CREATE TABLE budget_epoch_membership(epoch_id TEXT NOT NULL REFERENCES budget_epochs(epoch_id), item_id TEXT NOT NULL, original_item_id TEXT NOT NULL, PRIMARY KEY(epoch_id,item_id), UNIQUE(item_id)) STRICT;
CREATE TABLE budget_assessment_revisions(item_id TEXT NOT NULL, dimension TEXT NOT NULL, provider TEXT NOT NULL, accounting_scope TEXT NOT NULL, assessment_revision INTEGER NOT NULL CHECK(assessment_revision > 0), epoch_id TEXT NOT NULL REFERENCES budget_epochs(epoch_id), verdict TEXT NOT NULL CHECK(verdict IN ('unknown','not-applicable','pass','breach')), numerator INTEGER, denominator INTEGER, severe INTEGER NOT NULL CHECK(severe IN (0,1)), reason TEXT NOT NULL, source_digest TEXT NOT NULL, PRIMARY KEY(item_id,dimension,provider,accounting_scope,assessment_revision)) STRICT;
CREATE TABLE budget_breaches(epoch_id TEXT NOT NULL REFERENCES budget_epochs(epoch_id), item_id TEXT NOT NULL, dimension TEXT NOT NULL, provider TEXT NOT NULL, accounting_scope TEXT NOT NULL, assessment_revision INTEGER NOT NULL, severe INTEGER NOT NULL CHECK(severe IN (0,1)), PRIMARY KEY(epoch_id,item_id,dimension,provider,accounting_scope)) STRICT;
CREATE TABLE budget_interventions(intervention_id TEXT PRIMARY KEY, epoch_id TEXT NOT NULL UNIQUE REFERENCES budget_epochs(epoch_id), state TEXT NOT NULL CHECK(state IN ('open','verified')), trigger_item_id TEXT NOT NULL, trigger_kind TEXT NOT NULL CHECK(trigger_kind IN ('fifteenth-distinct','severe')), deployed_ref TEXT, verified_ref TEXT) STRICT;
INSERT INTO budget_epochs(epoch_id,ordinal,state) VALUES('epoch-1',1,'open');
PRAGMA user_version=4;
"""
            """
CREATE TABLE operational_activations(identity TEXT PRIMARY KEY, item_id TEXT NOT NULL, activation_id TEXT NOT NULL, scope TEXT NOT NULL CHECK(scope='explicit-future-dispatches'), runtime TEXT NOT NULL, activated_at TEXT NOT NULL, clock_provenance TEXT NOT NULL, late_after_seconds INTEGER NOT NULL CHECK(late_after_seconds >= 0), fact_revision INTEGER NOT NULL CHECK(fact_revision >= 0), UNIQUE(item_id,activation_id)) STRICT;
CREATE TABLE expected_dispatches(identity TEXT PRIMARY KEY, item_id TEXT NOT NULL, dispatch_id TEXT NOT NULL, activation_id TEXT NOT NULL, relation TEXT NOT NULL CHECK(relation IN ('root','child','follow-up')), parent_dispatch_id TEXT, runtime TEXT NOT NULL, expected_at TEXT NOT NULL, clock_provenance TEXT NOT NULL, fact_revision INTEGER NOT NULL CHECK(fact_revision >= 0), UNIQUE(item_id,dispatch_id)) STRICT;
CREATE TABLE invocation_lineage(identity TEXT PRIMARY KEY, item_id TEXT NOT NULL, dispatch_id TEXT NOT NULL, invocation_id TEXT NOT NULL, relation TEXT NOT NULL CHECK(relation IN ('root','child','follow-up')), parent_invocation_id TEXT, root_invocation_id TEXT NOT NULL, runtime TEXT NOT NULL, fact_revision INTEGER NOT NULL CHECK(fact_revision >= 0)) STRICT;
CREATE INDEX invocation_lineage_dispatch ON invocation_lineage(dispatch_id);
CREATE INDEX invocation_lineage_invocation ON invocation_lineage(invocation_id);
CREATE TABLE operational_event_times(identity TEXT PRIMARY KEY, item_id TEXT NOT NULL, invocation_id TEXT NOT NULL, event TEXT NOT NULL CHECK(event IN ('admission','start','terminal')), occurred_at TEXT, occurred_clock_provenance TEXT, observed_at TEXT, observed_clock_provenance TEXT, fact_revision INTEGER NOT NULL CHECK(fact_revision >= 0), UNIQUE(item_id,invocation_id,event)) STRICT;
CREATE INDEX operational_event_times_invocation ON operational_event_times(invocation_id);
PRAGMA user_version=5;
"""
            """
CREATE TABLE ci_population_admissions(identity TEXT PRIMARY KEY, item_id TEXT NOT NULL, collection_id TEXT NOT NULL UNIQUE, repository TEXT NOT NULL, pr_number INTEGER NOT NULL CHECK(pr_number > 0), base_ref TEXT NOT NULL, base_sha TEXT NOT NULL, head TEXT NOT NULL, witness TEXT NOT NULL CHECK(witness='native-pr-head'), fact_revision INTEGER NOT NULL CHECK(fact_revision >= 0), UNIQUE(item_id,repository,pr_number,base_ref,base_sha,head)) STRICT;
CREATE TABLE ci_check_runs(identity TEXT PRIMARY KEY, item_id TEXT NOT NULL, repository TEXT NOT NULL, check_id INTEGER NOT NULL, name TEXT NOT NULL, app_slug TEXT, status TEXT NOT NULL, conclusion TEXT, started_at TEXT, completed_at TEXT, fact_revision INTEGER NOT NULL CHECK(fact_revision >= 0), UNIQUE(repository,check_id)) STRICT;
CREATE TABLE ci_population_coverage(identity TEXT PRIMARY KEY, item_id TEXT NOT NULL, collection_id TEXT NOT NULL REFERENCES ci_population_admissions(collection_id), actions TEXT NOT NULL CHECK(actions IN ('complete','partial','unknown')), checks TEXT NOT NULL CHECK(checks IN ('complete','partial','unknown')), attempts TEXT NOT NULL CHECK(attempts IN ('complete','partial','unknown')), jobs TEXT NOT NULL CHECK(jobs IN ('complete','partial','unknown')), terminal TEXT NOT NULL CHECK(terminal IN ('complete','partial','unknown')), timestamps TEXT NOT NULL CHECK(timestamps IN ('complete','partial','unknown')), continuation TEXT NOT NULL CHECK(continuation IN ('none','pending')), external_checks INTEGER NOT NULL CHECK(external_checks >= 0), gaps TEXT NOT NULL, fact_revision INTEGER NOT NULL CHECK(fact_revision >= 0), UNIQUE(collection_id)) STRICT;
PRAGMA user_version=6;
"""
            """
CREATE TABLE native_item_outcomes(identity TEXT PRIMARY KEY, item_id TEXT NOT NULL, repository TEXT NOT NULL, pr_number INTEGER NOT NULL CHECK(pr_number > 0), base_ref TEXT NOT NULL, base_sha TEXT NOT NULL, head TEXT NOT NULL, outcome TEXT NOT NULL, code_delivery TEXT NOT NULL, merge_commit TEXT, occurred_at TEXT, observed_at TEXT NOT NULL, source_kind TEXT NOT NULL CHECK(source_kind='routine-delivery'), source_ref TEXT NOT NULL UNIQUE, fact_revision INTEGER NOT NULL CHECK(fact_revision >= 0)) STRICT;
CREATE INDEX native_item_outcomes_item_observed ON native_item_outcomes(item_id,observed_at);
PRAGMA user_version=7;
"""
            """
CREATE TABLE process_reviews(identity TEXT PRIMARY KEY, item_id TEXT NOT NULL, scope TEXT NOT NULL CHECK(scope IN ('attempt','item')), attempt_id TEXT, outcome_synopsis TEXT NOT NULL, went_well TEXT NOT NULL, problems TEXT NOT NULL, avoidable_delay_rework TEXT NOT NULL, process_observations TEXT NOT NULL, remaining_risks TEXT NOT NULL, concrete_improvements TEXT NOT NULL, evidence TEXT NOT NULL, evidence_coverage TEXT NOT NULL CHECK(evidence_coverage IN ('complete','partial','unknown')), population_coverage TEXT NOT NULL CHECK(population_coverage IN ('complete','partial','unknown')), confidence TEXT NOT NULL CHECK(confidence IN ('low','medium','high')), reviewer_model TEXT NOT NULL, reviewer_effort TEXT NOT NULL, reviewed_at TEXT NOT NULL, duration_seconds INTEGER NOT NULL CHECK(duration_seconds BETWEEN 0 AND 86400), fact_revision INTEGER NOT NULL CHECK(fact_revision > 0), CHECK((scope='attempt' AND attempt_id IS NOT NULL) OR (scope='item' AND attempt_id IS NULL))) STRICT;
CREATE UNIQUE INDEX process_review_attempt_subject ON process_reviews(item_id,attempt_id) WHERE scope='attempt';
CREATE UNIQUE INDEX process_review_item_subject ON process_reviews(item_id) WHERE scope='item';
CREATE TABLE activity_spans(identity TEXT PRIMARY KEY, item_id TEXT NOT NULL, activity_id TEXT NOT NULL, invocation_id TEXT NOT NULL, attempt_id TEXT NOT NULL, category TEXT NOT NULL CHECK(category IN ('planning','implementation','review','validation','delivery','repair','operations','other','unclassified')), started_at TEXT NOT NULL, ended_at TEXT, clock_provenance TEXT NOT NULL, evidence TEXT NOT NULL, summary TEXT, fact_revision INTEGER NOT NULL CHECK(fact_revision >= 0), UNIQUE(item_id,activity_id)) STRICT;
CREATE INDEX activity_spans_item_attempt ON activity_spans(item_id,attempt_id);
CREATE TABLE activity_usage_attributions(identity TEXT PRIMARY KEY, item_id TEXT NOT NULL, usage_identity TEXT NOT NULL UNIQUE, activity_id TEXT, classification TEXT NOT NULL CHECK(classification IN ('direct','mixed','unclassified')), input_count INTEGER NOT NULL CHECK(input_count >= 0), cached_input INTEGER NOT NULL CHECK(cached_input >= 0), output_count INTEGER NOT NULL CHECK(output_count >= 0), reasoning INTEGER, total INTEGER NOT NULL CHECK(total >= 0), fact_revision INTEGER NOT NULL CHECK(fact_revision >= 0), CHECK((classification='direct' AND activity_id IS NOT NULL) OR (classification IN ('mixed','unclassified') AND activity_id IS NULL))) STRICT;
CREATE INDEX activity_usage_item ON activity_usage_attributions(item_id);
CREATE TABLE complication_events(identity TEXT PRIMARY KEY, item_id TEXT NOT NULL, attempt_id TEXT, activity_id TEXT, trigger TEXT NOT NULL, cause TEXT NOT NULL, occurred_at TEXT NOT NULL, synopsis TEXT NOT NULL, evidence TEXT NOT NULL, fact_revision INTEGER NOT NULL CHECK(fact_revision >= 0)) STRICT;
CREATE INDEX complication_events_item ON complication_events(item_id,occurred_at);
PRAGMA user_version=8;
"""
            """
CREATE TABLE receipt_producers(producer TEXT NOT NULL, stream TEXT NOT NULL, PRIMARY KEY(producer,stream)) STRICT;
CREATE TABLE transport_receipts(producer TEXT NOT NULL, batch TEXT NOT NULL, stream TEXT NOT NULL, digest TEXT NOT NULL, payload_bytes INTEGER NOT NULL, state TEXT NOT NULL CHECK(state IN ('durably-received','applied','rejected')), code TEXT, terminal_utc TEXT, PRIMARY KEY(producer,batch)) STRICT;
CREATE INDEX transport_pending ON transport_receipts(state,producer);
PRAGMA user_version=9;
"""
            """
CREATE TABLE native_item_outcomes_v10(identity TEXT PRIMARY KEY, item_id TEXT NOT NULL, repository TEXT NOT NULL, pr_number INTEGER NOT NULL CHECK(pr_number > 0), base_ref TEXT NOT NULL, base_sha TEXT NOT NULL, head TEXT NOT NULL, outcome TEXT NOT NULL, code_delivery TEXT NOT NULL, merge_commit TEXT, occurred_at TEXT, observed_at TEXT NOT NULL, source_kind TEXT NOT NULL CHECK(source_kind IN ('routine-delivery','orchestration-delivery')), source_ref TEXT NOT NULL UNIQUE, fact_revision INTEGER NOT NULL CHECK(fact_revision >= 0)) STRICT;
INSERT INTO native_item_outcomes_v10 SELECT * FROM native_item_outcomes;
DROP TABLE native_item_outcomes;
ALTER TABLE native_item_outcomes_v10 RENAME TO native_item_outcomes;
CREATE INDEX native_item_outcomes_item_observed ON native_item_outcomes(item_id,observed_at);
PRAGMA user_version=10;
"""
            """
CREATE TABLE learning_fact_order(sequence INTEGER PRIMARY KEY AUTOINCREMENT, identity TEXT NOT NULL UNIQUE REFERENCES ingest_facts(identity)) STRICT;
PRAGMA user_version=11;
"""
            """
ALTER TABLE receipt_producers ADD COLUMN authority_role TEXT NOT NULL DEFAULT 'generic' CHECK(authority_role IN ('generic','native-collector'));
ALTER TABLE receipt_producers ADD COLUMN grant_id TEXT;
ALTER TABLE receipt_producers ADD COLUMN grant_generation INTEGER;
CREATE TABLE receipt_admissions(producer TEXT NOT NULL, batch TEXT NOT NULL, stream TEXT NOT NULL, authority_role TEXT NOT NULL CHECK(authority_role IN ('generic','native-collector')), grant_id TEXT, grant_generation INTEGER, receipt_key TEXT NOT NULL, envelope_digest TEXT NOT NULL, PRIMARY KEY(producer,batch), FOREIGN KEY(producer,batch) REFERENCES transport_receipts(producer,batch), CHECK((grant_id IS NULL AND grant_generation IS NULL) OR (grant_id IS NOT NULL AND grant_generation > 0))) STRICT;
CREATE TABLE fact_admissions(identity TEXT PRIMARY KEY REFERENCES ingest_facts(identity), producer TEXT NOT NULL, stream TEXT NOT NULL, authority_role TEXT NOT NULL CHECK(authority_role IN ('generic','native-collector')), grant_id TEXT, grant_generation INTEGER, receipt_key TEXT NOT NULL, envelope_digest TEXT NOT NULL, CHECK((grant_id IS NULL AND grant_generation IS NULL) OR (grant_id IS NOT NULL AND grant_generation > 0))) STRICT;
PRAGMA user_version=12;
"""
            """
CREATE TABLE ci_attribution_corrections(correction_id TEXT PRIMARY KEY, plan_digest TEXT NOT NULL, plan TEXT NOT NULL, outcome_identity TEXT NOT NULL, predecessor TEXT REFERENCES ci_attribution_corrections(correction_id), repository TEXT NOT NULL, pr_number INTEGER NOT NULL, base_ref TEXT NOT NULL, base_sha TEXT NOT NULL, head TEXT NOT NULL, merge_commit TEXT NOT NULL, prior_item TEXT NOT NULL, effective_item TEXT NOT NULL, effective_feature TEXT NOT NULL, effective_attempt TEXT NOT NULL, observed_at TEXT NOT NULL, applied_at TEXT NOT NULL) STRICT;
CREATE TABLE ci_correction_evidence(correction_id TEXT NOT NULL REFERENCES ci_attribution_corrections(correction_id), identity TEXT NOT NULL, table_name TEXT NOT NULL, revision INTEGER NOT NULL, digest TEXT NOT NULL, canonical TEXT NOT NULL, PRIMARY KEY(correction_id,identity)) STRICT;
CREATE TABLE ci_effective_attribution(identity TEXT PRIMARY KEY REFERENCES ingest_facts(identity), correction_id TEXT NOT NULL REFERENCES ci_attribution_corrections(correction_id)) STRICT;
CREATE VIEW current_ingest_facts AS SELECT f.identity,f.kind,coalesce(c.effective_item,f.item_id) AS item_id,f.revision,f.content_digest,f.canonical FROM ingest_facts f LEFT JOIN ci_effective_attribution e ON e.identity=f.identity LEFT JOIN ci_attribution_corrections c ON c.correction_id=e.correction_id;
CREATE TRIGGER ci_correction_immutable_update BEFORE UPDATE ON ci_attribution_corrections BEGIN SELECT RAISE(ABORT,'correction ledger is immutable'); END;
CREATE TRIGGER ci_correction_immutable_delete BEFORE DELETE ON ci_attribution_corrections BEGIN SELECT RAISE(ABORT,'correction ledger is immutable'); END;
CREATE TRIGGER ci_correction_evidence_immutable_update BEFORE UPDATE ON ci_correction_evidence BEGIN SELECT RAISE(ABORT,'correction evidence is immutable'); END;
CREATE TRIGGER ci_correction_evidence_immutable_delete BEFORE DELETE ON ci_correction_evidence BEGIN SELECT RAISE(ABORT,'correction evidence is immutable'); END;
INSERT INTO store_metadata(key,value) VALUES('ciCorrectionStoreId',lower(hex(randomblob(16))));
PRAGMA user_version=13;
"""
        |]
        for index in 0..migrations.Length-1 do
            command.CommandText <- migrations[index]
            command.ExecuteNonQuery() |> ignore
            command.CommandText <- "INSERT INTO schema_migrations(version,digest,applied_utc) VALUES($version,$digest,$utc);"
            command.Parameters.Clear()
            command.Parameters.AddWithValue("$version", index+1) |> ignore
            command.Parameters.AddWithValue("$digest", CanonicalJson.sha256(Encoding.UTF8.GetBytes migrations[index])) |> ignore
            command.Parameters.AddWithValue("$utc", "2026-10-01T00:00:00.0000000+00:00") |> ignore
            command.ExecuteNonQuery() |> ignore
            command.Parameters.Clear()
        command.CommandText <- "INSERT INTO store_metadata(key,value) VALUES('schema','fsgg.telemetry.sqlite-store/1'),('nativeEngine',sqlite_version()); INSERT INTO ingest_facts(identity,kind,item_id,revision,content_digest,canonical) VALUES('historical-item','item','MIGRATION-13',0,$digest,$canonical); INSERT INTO source_cursors VALUES('historical-source','historical-generation','cursor-before',$digest); INSERT INTO ingest_batches VALUES('historical-batch',$digest,'historical-source','historical-generation','cursor-before',1,0);"
        let canonical = "{\"identity\":\"historical-item\",\"itemId\":\"MIGRATION-13\",\"kind\":\"item\",\"revision\":0}"
        command.Parameters.AddWithValue("$digest", CanonicalJson.sha256(Encoding.UTF8.GetBytes canonical)) |> ignore
        command.Parameters.AddWithValue("$canonical", canonical) |> ignore
        command.ExecuteNonQuery() |> ignore
        let inbox = Path.Combine(path,"inbox","historical-source")
        Directory.CreateDirectory inbox |> ignore
        File.WriteAllBytes(Path.Combine(inbox,"historical.ready"), Encoding.UTF8.GetBytes "preserved ready fixture\n")
        File.WriteAllBytes(Path.Combine(inbox,".unfinished.tmp"), Encoding.UTF8.GetBytes "preserved partial fixture")

    let private migration13Snapshot path =
        [ "SELECT group_concat(version||':'||digest||':'||applied_utc,'|') FROM (SELECT * FROM schema_migrations ORDER BY version);"
          "SELECT group_concat(content_digest||canonical,'|') FROM (SELECT content_digest,canonical FROM ingest_facts ORDER BY identity);"
          "SELECT group_concat(source_identity||generation||cursor||batch_digest,'|') FROM source_cursors;"
          "SELECT group_concat(ingest_id||content_digest||cursor||accepted_count||replay_count,'|') FROM ingest_batches;"
          "SELECT value FROM store_metadata WHERE key='ciCorrectionStoreId';" ]
        |> List.map (correctionSql path)

    let private migration13Files path =
        Directory.GetFiles(Path.Combine(path,"inbox"),"*",SearchOption.AllDirectories)
        |> Array.sort
        |> Array.map (fun file -> Path.GetRelativePath(path,file),Convert.ToHexString(SHA256.HashData(File.ReadAllBytes file)))

    [<Fact>]
    let ``schema13 direct migration preserves rows cursors files and historical unknown acceptance`` () =
        let cleanup,path = root ()
        use cleanup = cleanup
        genuineSchema13 path
        Assert.Equal("13",correctionSql path "PRAGMA user_version;")
        let before = migration13Snapshot path
        let files = migration13Files path
        Assert.Contains("\"schemaVersion\":14",TelemetryStoreApplication.migrate13ToCurrent path approved |> unwrap)
        Assert.Equal("14",correctionSql path "PRAGMA user_version;")
        // Old13 rows have no receiver acceptance witness; migration must not invent one.
        Assert.Equal("0",correctionSql path "SELECT count(*) FROM fact_acceptance_times;")
        Assert.Equal<string list>(before[1..],(migration13Snapshot path)[1..])
        Assert.Equal<(string * string) array>(files,migration13Files path)
        let journal = correctionSql path "SELECT group_concat(version||':'||digest||':'||applied_utc,'|') FROM (SELECT * FROM schema_migrations ORDER BY version);"
        TelemetryStoreApplication.migrate13ToCurrent path approved |> unwrap |> ignore
        Assert.Equal(journal,correctionSql path "SELECT group_concat(version||':'||digest||':'||applied_utc,'|') FROM (SELECT * FROM schema_migrations ORDER BY version);")
        Assert.Equal("1",correctionSql path "SELECT count(*) FROM schema_migrations WHERE version=14;")
        Assert.Equal(before[0],correctionSql path "SELECT group_concat(version||':'||digest||':'||applied_utc,'|') FROM (SELECT * FROM schema_migrations WHERE version<=13 ORDER BY version);")

    [<Fact>]
    let ``schema13 journal abort rolls back all14DDL and retry commits once`` () =
        let cleanup,path = root ()
        use cleanup = cleanup
        genuineSchema13 path
        let before = migration13Snapshot path
        let files = migration13Files path
        correctionSql path "CREATE TRIGGER fixture_abort14 BEFORE INSERT ON schema_migrations WHEN NEW.version=14 BEGIN SELECT RAISE(ABORT,'fixture-14-journal-abort'); END; SELECT 1;" |> ignore
        let objects = correctionSql path "SELECT group_concat(type||':'||name||':'||coalesce(sql,''),'|') FROM (SELECT type,name,sql FROM sqlite_master ORDER BY type,name);"
        Assert.True(TelemetryStoreApplication.migrate13ToCurrent path approved |> Result.isError)
        Assert.Equal("13",correctionSql path "PRAGMA user_version;")
        Assert.Equal<string list>(before,migration13Snapshot path)
        Assert.Equal<(string * string) array>(files,migration13Files path)
        Assert.Equal(objects,correctionSql path "SELECT group_concat(type||':'||name||':'||coalesce(sql,''),'|') FROM (SELECT type,name,sql FROM sqlite_master ORDER BY type,name);")
        Assert.Equal("0",correctionSql path "SELECT count(*) FROM schema_migrations WHERE version=14;")
        correctionSql path "DROP TRIGGER fixture_abort14; SELECT 1;" |> ignore
        TelemetryStoreApplication.migrate13ToCurrent path approved |> unwrap |> ignore
        TelemetryStoreApplication.migrate13ToCurrent path approved |> unwrap |> ignore
        Assert.Equal("14",correctionSql path "PRAGMA user_version;")
        Assert.Equal("1",correctionSql path "SELECT count(*) FROM schema_migrations WHERE version=14;")
        Assert.Equal<string list>(before[1..],(migration13Snapshot path)[1..])
        Assert.Equal<(string * string) array>(files,migration13Files path)

    [<Fact>]
    let ``schema13 damaged receipt refuses before14DDL or file mutation`` () =
        let cleanup,path = root ()
        use cleanup = cleanup
        genuineSchema13 path
        correctionSql path "UPDATE schema_migrations SET digest='corrupt' WHERE version=13; SELECT 1;" |> ignore
        let before = migration13Snapshot path
        let files = migration13Files path
        Assert.True(TelemetryStoreApplication.migrate13ToCurrent path approved |> Result.isError)
        Assert.Equal("13",correctionSql path "PRAGMA user_version;")
        Assert.Equal<string list>(before,migration13Snapshot path)
        Assert.Equal<(string * string) array>(files,migration13Files path)
        Assert.Equal("0",correctionSql path "SELECT count(*) FROM sqlite_master WHERE name='efficiency_records';")

    let private writeSchema13BackupFixture path schema =
        let database = Path.Combine(path,TelemetryStoreApplication.databaseFileName)
        let manifest = JsonSerializer.Serialize {| schema = "fsgg.telemetry.host-backup/1"; storeSchemaVersion = schema; workspaceId = "migration-workspace"; files = [| {| path = TelemetryStoreApplication.databaseFileName; sha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes database)).ToLowerInvariant(); bytes = FileInfo(database).Length |} |] |}
        File.WriteAllText(Path.Combine(path,"manifest.json"),manifest+"\n")

    [<Fact>]
    let ``schema13 authentic backup restores through migration into fresh root`` () =
        let cleanup,path = root ()
        use cleanup = cleanup
        let restoredCleanup,restored = root ()
        use restoredCleanup = restoredCleanup
        genuineSchema13 path
        // Empty receipt-population fixture: no claim about pending-receipt coverage.
        correctionSql path "DELETE FROM ingest_batches; INSERT INTO store_metadata VALUES('receiptWorkspace','migration-workspace'); SELECT 1;" |> ignore
        let facts = correctionSql path "SELECT content_digest||canonical FROM ingest_facts;"
        let cursor = correctionSql path "SELECT cursor||batch_digest FROM source_cursors;"
        Directory.Delete(Path.Combine(path,"inbox"),true)
        writeSchema13BackupFixture path 13
        let originalDatabase = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(Path.Combine(path,TelemetryStoreApplication.databaseFileName))))
        TelemetryStoreApplication.import13ReceiptBackup path restored approved "migration-workspace" |> unwrap |> ignore
        Assert.Equal("14",correctionSql restored "PRAGMA user_version;")
        Assert.Equal("13",correctionSql path "PRAGMA user_version;")
        Assert.Equal(facts,correctionSql restored "SELECT content_digest||canonical FROM ingest_facts;")
        Assert.Equal(cursor,correctionSql restored "SELECT cursor||batch_digest FROM source_cursors;")
        Assert.Equal(originalDatabase,Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(Path.Combine(path,TelemetryStoreApplication.databaseFileName)))))
        Assert.True(TelemetryStoreApplication.import13ReceiptBackup path restored approved "migration-workspace" |> Result.isError)

    [<Fact>]
    let ``schema13 mislabeled14 database refuses before restore publication`` () =
        let cleanup,path = root ()
        use cleanup = cleanup
        let restoredCleanup,restored = root ()
        use restoredCleanup = restoredCleanup
        genuineSchema13 path
        TelemetryStoreApplication.migrate13ToCurrent path approved |> unwrap |> ignore
        correctionSql path "DELETE FROM ingest_batches; INSERT INTO store_metadata VALUES('receiptWorkspace','migration-workspace'); SELECT 1;" |> ignore
        Directory.Delete(Path.Combine(path,"inbox"),true)
        // The known closed fixture initializer leaves writer.lock; a backup contains
        // only its pinned database/manifest. Remove that fixture-only lock explicitly.
        File.Delete(Path.Combine(path,"writer.lock"))
        writeSchema13BackupFixture path 13
        Assert.Equal<string array>([|"manifest.json";TelemetryStoreApplication.databaseFileName|],Directory.GetFileSystemEntries(path) |> Array.map Path.GetFileName |> Array.sort)
        Assert.Equal("14",correctionSql path "PRAGMA user_version;")
        Assert.Equal(Error ["backup-integrity-failed"],TelemetryStoreApplication.import13ReceiptBackup path restored approved "migration-workspace")
        Assert.False(Directory.Exists restored)
        // Same eligible physical backup, truthful14manifest: proves the negative
        // reached the new DBversion/manifest gate rather than an earlier census guard.
        writeSchema13BackupFixture path 14
        TelemetryStoreApplication.restoreReceiptStore path restored approved "migration-workspace" |> unwrap |> ignore
        Assert.Equal("14",correctionSql restored "PRAGMA user_version;")

    [<Fact>]
    let ``schema13 representative dispatch survives actual adapter1MiB read framing`` () =
        // Invoke the exact read-only private reader to isolate framing from association
        // and active receipts. This proves no begin/finish or migration acceptance.
        // Representative JSON reader fixture; no private row/hash/environment dependency.
        let prefix = "{\"schema\":\"fsgg.telemetry.roadmap-dispatch-state/1\",\"token\":\"00000000000000000000000000000000\",\"padding\":\""
        let suffix = "\"}\n"
        let actual = Encoding.UTF8.GetBytes(prefix+String('x',300000-Encoding.UTF8.GetByteCount(prefix+suffix))+suffix)
        Assert.Equal(300000,actual.Length)
        Assert.Equal(byte '\n',actual[actual.Length-1])
        let cleanup,path = root ()
        use cleanup = cleanup
        Directory.CreateDirectory path |> ignore
        let copied = Path.Combine(path,"dispatch.json")
        File.WriteAllBytes(copied,actual)
        if not (OperatingSystem.IsWindows()) then File.SetUnixFileMode(copied,enum<UnixFileMode> 0o600)
        let moduleType = typeof<SkillTelemetryAdapter.CommandResult>.DeclaringType
        let reader = moduleType.GetMethod("readObject",System.Reflection.BindingFlags.Static ||| System.Reflection.BindingFlags.NonPublic)
        Assert.NotNull reader
        let parsed = reader.Invoke(null,[|box copied;box "fixture-unavailable"|]) :?> System.Text.Json.Nodes.JsonObject
        Assert.Equal("fsgg.telemetry.roadmap-dispatch-state/1",parsed["schema"].GetValue<string>())
        Assert.Equal<byte array>(actual,File.ReadAllBytes copied)
        // Closed JSON plus whitespace at exact persisted-byte bounds, including newline.
        let boundary length = Encoding.UTF8.GetBytes("{}"+String(' ',length-3)+"\n")
        File.WriteAllBytes(copied,boundary 1048576)
        Assert.NotNull(reader.Invoke(null,[|box copied;box "fixture-unavailable"|]))
        File.WriteAllBytes(copied,boundary 1048577)
        let refused = Assert.Throws<System.Reflection.TargetInvocationException>(fun () -> reader.Invoke(null,[|box copied;box "fixture-unavailable"|]) |> ignore)
        // The exception is private to the module signature; pin its assembly identity
        // and exact F# payload without exposing a new production API.
        let adapterErrorType = moduleType.GetNestedType("AdapterError",System.Reflection.BindingFlags.Public ||| System.Reflection.BindingFlags.NonPublic)
        Assert.NotNull adapterErrorType
        Assert.Equal<Type>(adapterErrorType,refused.InnerException.GetType())
        Assert.Equal<obj array>([|box "fixture-unavailable"|],Microsoft.FSharp.Reflection.FSharpValue.GetExceptionFields(refused.InnerException, bindingFlags = (System.Reflection.BindingFlags.Public ||| System.Reflection.BindingFlags.NonPublic)))

    [<Fact>]
    let ``current correction rejects a changed source after planning`` () =
        let cleanup, path, request = correctionFixture false
        use cleanup = cleanup
        let plan = TelemetryStoreApplication.ciCorrectionPlan path approved request |> unwrap |> Encoding.UTF8.GetBytes
        let changed = nativeOutcome request.Prior.ItemId 2L "delivered" "delivered" "2026-09-08T10:04:02Z"
        TelemetryStoreApplication.ingest path approved (operationalBatch "pre-apply-change" request.Prior.ItemId [ changed ]) |> unwrap |> ignore
        Assert.Equal(Error [ "ci-correction-stale-plan" ], TelemetryStoreApplication.ciCorrect path approved plan)
        Assert.Equal("0", correctionSql path "SELECT count(*) FROM ci_attribution_corrections;")

    [<Theory>]
    [<InlineData(1)>]
    [<InlineData(2)>]
    [<InlineData(3)>]
    [<InlineData(4)>]
    [<InlineData(5)>]
    [<InlineData(6)>]
    [<InlineData(7)>]
    [<InlineData(8)>]
    [<InlineData(9)>]
    [<InlineData(10)>]
    [<InlineData(11)>]
    [<InlineData(12)>]
    [<InlineData(13)>]
    let ``normal init refuses all existing historical version labels without data mutation`` version =
        let cleanup, path = root ()
        use cleanup = cleanup
        TelemetryStoreApplication.initialize path approved |> unwrap |> ignore
        // Synthetic version label isolates the admission gate; genuine13 preservation
        // and transaction fixtures above independently exercise the supported transition.
        correctionSql path $"PRAGMA user_version={version}; SELECT 1;" |> ignore
        let before = migration13Snapshot path
        Assert.True(TelemetryStoreApplication.initialize path approved |> Result.isError)
        Assert.Equal(string version, correctionSql path "PRAGMA user_version;")
        Assert.Equal<string list>(before, migration13Snapshot path)
        if version <> 13 then Assert.True(TelemetryStoreApplication.migrate13ToCurrent path approved |> Result.isError)

    [<Fact>]
    let ``normal init refuses genuine13 while explicit migration preserves it`` () =
        let cleanup, path = root ()
        use cleanup = cleanup
        genuineSchema13 path
        let before = migration13Snapshot path
        Assert.True(TelemetryStoreApplication.initialize path approved |> Result.isError)
        Assert.Equal<string list>(before, migration13Snapshot path)
        TelemetryStoreApplication.migrate13ToCurrent path approved |> unwrap |> ignore

    [<Fact>]
    let ``normal restore refuses13 and import refuses mislabeled13 as14`` () =
        let cleanup, path = root ()
        use cleanup = cleanup
        let targetCleanup, target = root ()
        use targetCleanup = targetCleanup
        genuineSchema13 path
        correctionSql path "DELETE FROM ingest_batches; INSERT INTO store_metadata VALUES('receiptWorkspace','migration-workspace'); SELECT 1;" |> ignore
        Directory.Delete(Path.Combine(path,"inbox"),true)
        writeSchema13BackupFixture path 13
        Assert.Equal(Error [ "backup-incompatible" ], TelemetryStoreApplication.restoreReceiptStore path target approved "migration-workspace")
        Assert.False(Directory.Exists target)
        writeSchema13BackupFixture path 14
        Assert.Equal(Error [ "backup-integrity-failed" ], TelemetryStoreApplication.restoreReceiptStore path target approved "migration-workspace")
        Assert.Equal(Error [ "backup-incompatible" ], TelemetryStoreApplication.import13ReceiptBackup path target approved "migration-workspace")
        Assert.False(Directory.Exists target)

    [<Fact>]
    let ``explicit13 migration refuses damaged schema with an intact journal`` () =
        let cleanup, path = root ()
        use cleanup = cleanup
        genuineSchema13 path
        correctionSql path "DROP TRIGGER ci_correction_immutable_update; SELECT 1;" |> ignore
        let before = migration13Snapshot path
        Assert.True(TelemetryStoreApplication.migrate13ToCurrent path approved |> Result.isError)
        Assert.Equal("13", correctionSql path "PRAGMA user_version;")
        Assert.Equal<string list>(before, migration13Snapshot path)
        Assert.Equal("0", correctionSql path "SELECT count(*) FROM sqlite_schema WHERE name='efficiency_records';")


    [<Theory>]
    [<InlineData("constraint", "store schema definition integrity mismatch")>]
    [<InlineData("column-order", "store schema column integrity mismatch")>]
    let ``current schema retains compact constraints and refuses definition or column order drift`` (mutation: string) (expectedError: string) =
        let cleanup, path = root ()
        use cleanup = cleanup
        TelemetryStoreApplication.initialize path approved |> unwrap |> ignore
        TelemetryStoreApplication.initialize path approved |> unwrap |> ignore
        TelemetryStoreApplication.status path approved |> unwrap |> ignore
        Assert.Equal("epoch_id|item_id|original_item_id", correctionSql path "SELECT group_concat(name,'|') FROM (SELECT name FROM pragma_table_info('budget_epoch_membership') ORDER BY cid);")
        let replacement =
            match mutation with
            | "constraint" -> "epoch_id TEXT NOT NULL REFERENCES budget_epochs(epoch_id), item_id TEXT NOT NULL, original_item_id TEXT NOT NULL, PRIMARY KEY(epoch_id,item_id)"
            | "column-order" -> "item_id TEXT NOT NULL, epoch_id TEXT NOT NULL REFERENCES budget_epochs(epoch_id), original_item_id TEXT NOT NULL, PRIMARY KEY(epoch_id,item_id), UNIQUE(item_id)"
            | _ -> failwith "unknown schema mutation"
        correctionSql path ("DROP TABLE budget_epoch_membership; CREATE TABLE budget_epoch_membership(" + replacement + ") STRICT; SELECT 1;") |> ignore
        Assert.Equal(Error [ expectedError ], TelemetryStoreApplication.initialize path approved)
        Assert.True(TelemetryStoreApplication.status path approved |> Result.isError)
        Assert.Equal("14", correctionSql path "PRAGMA user_version;")
        Assert.Equal("14", correctionSql path "SELECT count(*) FROM schema_migrations;")
