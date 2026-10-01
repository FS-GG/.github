namespace FS.GG.Telemetry.Tests

open System
open System.IO
open System.IO.Compression
open System.Net
open System.Net.Http
open System.Net.Sockets
open System.Diagnostics
open System.Security.Cryptography
open System.Security.Cryptography.X509Certificates
open System.Text.Json
open System.Text.Json.Nodes
open Microsoft.AspNetCore.Builder
open Microsoft.AspNetCore.Http
open Microsoft.AspNetCore.Hosting
open Microsoft.Data.Sqlite
open System.Text
open System.Threading
open System.Threading.Tasks
open Xunit
open FS.GG.Coord
open FS.GG.Coord.GitHub
open FS.GG.Telemetry
open FS.GG.Telemetry.Host
open FS.GG.Coord.Cli

type private Handler(response: unit -> HttpResponseMessage) =
    inherit HttpMessageHandler()
    override _.SendAsync(_, _) = Task.FromResult(response ())

type private CapturedRequest =
    {
        Method: HttpMethod
        Uri: Uri
        Authorization: string option
        Body: byte array
    }

type private CapturingHandler(response: HttpRequestMessage -> HttpResponseMessage) =
    inherit HttpMessageHandler()
    let requests = ResizeArray<CapturedRequest>()

    member _.Requests = requests |> Seq.toArray

    override _.SendAsync(request, cancellationToken) =
        task {
            let! body =
                if isNull request.Content then
                    Task.FromResult(Array.empty)
                else
                    request.Content.ReadAsByteArrayAsync(cancellationToken)

            requests.Add(
                {
                    Method = request.Method
                    Uri = request.RequestUri
                    Authorization =
                        if isNull request.Headers.Authorization then
                            None
                        else
                            Some(request.Headers.Authorization.ToString())
                    Body = body
                }
            )

            return response request
        }

type private DropFirstResponseHandler(inner: HttpMessageHandler) =
    inherit DelegatingHandler(inner)
    let mutable drop = true

    member private this.Forward(request, cancellationToken) =
        base.SendAsync(request, cancellationToken)

    override this.SendAsync(request, cancellationToken) =
        task {
            let! (response: HttpResponseMessage) = this.Forward(request, cancellationToken)

            if drop then
                drop <- false
                response.Dispose()
                return raise (HttpRequestException("synthetic lost response"))
            else
                return response
        }

type private SlowContent(started: TaskCompletionSource<unit>) =
    inherit HttpContent()

    override _.TryComputeLength(length: byref<int64>) =
        length <- 0L
        false

    override _.SerializeToStreamAsync(stream, _) =
        task {
            do! stream.WriteAsync(ReadOnlyMemory<byte>([| byte '{' |])).AsTask()
            do! stream.FlushAsync()
            started.TrySetResult() |> ignore
            do! Task.Delay(30000)
        }
        :> Task

    override _.SerializeToStreamAsync(stream, _, cancellationToken) =
        task {
            do! stream.WriteAsync(ReadOnlyMemory<byte>([| byte '{' |]), cancellationToken).AsTask()
            do! stream.FlushAsync(cancellationToken)
            started.TrySetResult() |> ignore
            do! Task.Delay(Timeout.Infinite, cancellationToken)
        }
        :> Task

type private CountedSinglePageTransport(response: Transport.Response) =
    let requests = ResizeArray<Transport.Request>()
    member _.Requests = requests |> Seq.toArray
    interface Transport.ISinglePageGitHubTransport with
        member _.SendSingle request =
            requests.Add request
            Ok response
    interface IDisposable with member _.Dispose() = ()

module RemoteTelemetryTests =
    let scope: TelemetryReceipt.Scope =
        {
            Workspace = "workspace-a"
            Producer = "producer-a"
            Stream = "runtime"
        }

    let browserSession =
        {
            IdleSeconds = 300
            AbsoluteSeconds = 3600
            MaximumSessions = 32
            LoginAttemptsPerMinute = 16
            LoginAdmission = 4
            QueryAdmission = 4
            QueryTimeoutSeconds = 10
        }

    let makeEnvelope (who: TelemetryReceipt.Scope) batch revision =
        Encoding.UTF8.GetBytes
            $"""{{"schema":"fsgg.telemetry.envelope/1","workspaceId":"{who.Workspace}","producerId":"{who.Producer}","streamId":"{who.Stream}","batchId":"{batch}","payload":{{"schema":"{TelemetryStore.BatchSchema}","ingestId":"native-batch","sourceIdentity":"native-source","generation":"g1","cursor":"1","eventCount":1,"events":[{{"kind":"item","identity":"item-a","itemId":"item-a","revision":{revision}}}]}}}}"""

    let envelope batch = makeEnvelope scope batch 0

    let receipt batch status =
        let parsed =
            TelemetryReceipt.parse (envelope batch)
            |> Result.defaultWith (fun e -> failwithf "%A" e)

        Encoding.UTF8.GetBytes
            $"""{{"schema":"{RemoteContract.ReceiptSchema}","workspaceId":"{scope.Workspace}","producerId":"{scope.Producer}","streamId":"{scope.Stream}","batchId":"{batch}","digest":"{parsed.Digest}","status":"{status}","code":null}}"""

    let config: RemoteContract.ClientConfig =
        {
            Endpoint = Uri("https://telemetry.example/")
            CredentialReference = "main"
        }

    let resolve _ _ = Task.FromResult(Some(String('x', 32)))

    [<Fact>]
    let ``LEARN pre-dispatch facts are typed and bounded by the existing batch contract`` () =
        let bytes =
            Encoding.UTF8.GetBytes
                """{"schema":"fsgg.telemetry.ingest/1","ingestId":"learn-batch","sourceIdentity":"producer","generation":"g1","cursor":"1","eventCount":3,"events":[{"kind":"learn-task-snapshot","identity":"snapshot-1","itemId":"LEARN-01.2","revision":1,"snapshotId":"task-1","rubricVersion":"rubric-v1","snapshotDigest":"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa","capturedAt":"2026-09-27T08:00:00Z"},{"kind":"learn-context-manifest","identity":"manifest-1","itemId":"LEARN-01.2","revision":1,"recipeId":"recipe-v1","recipeDigest":"bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb","manifestId":"manifest-v1","manifestDigest":"cccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccc"},{"kind":"learn-experiment-assignment","identity":"assignment-1","itemId":"LEARN-01.2","revision":1,"windowId":"window-v1","policyId":"learn-01-current-focused-v1","arm":"focused","assignedAt":"2026-09-27T08:01:00Z","deviation":null}]}"""

        match TelemetryStore.parseBatch bytes with
        | Ok batch ->
            Assert.Equal(3, batch.Facts.Length)
            Assert.Contains(batch.Facts, fun fact -> fact.Kind = "learn-experiment-assignment")
        | Error errors -> Assert.Fail(String.concat "; " errors)

    [<Fact>]
    let ``LEARN assignment rejects unsupported arms before persistence`` () =
        let bytes =
            Encoding.UTF8.GetBytes
                """{"schema":"fsgg.telemetry.ingest/1","ingestId":"learn-batch","sourceIdentity":"producer","generation":"g1","cursor":"1","eventCount":1,"events":[{"kind":"learn-experiment-assignment","identity":"assignment-1","itemId":"LEARN-01.2","revision":1,"windowId":"window-v1","policyId":"learn-01-current-focused-v1","arm":"outcome-derived","assignedAt":"2026-09-27T08:01:00Z","deviation":null}]}"""

        Assert.True(TelemetryStore.parseBatch bytes |> Result.isError)

    [<Fact>]
    let ``LEARN task snapshot and context manifest revisions are refused by the schema-11 store`` () =
        let root = Path.Combine(Path.GetTempPath(), "learn-predispatch-immutable-" + Guid.NewGuid().ToString("N"))
        let original =
            Encoding.UTF8.GetBytes
                """{"schema":"fsgg.telemetry.ingest/1","ingestId":"learn-pre-1","sourceIdentity":"producer","generation":"g1","cursor":"1","eventCount":2,"events":[{"kind":"learn-task-snapshot","identity":"snapshot-1","itemId":"LEARN-01.2","revision":1,"snapshotId":"task-1","rubricVersion":"rubric-v1","snapshotDigest":"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa","capturedAt":"2026-09-27T08:00:00Z"},{"kind":"learn-context-manifest","identity":"manifest-1","itemId":"LEARN-01.2","revision":1,"recipeId":"recipe-v1","recipeDigest":"bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb","manifestId":"manifest-v1","manifestDigest":"cccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccc"}]}"""
        let snapshotRevision =
            Encoding.UTF8.GetBytes
                """{"schema":"fsgg.telemetry.ingest/1","ingestId":"learn-pre-2","sourceIdentity":"producer","generation":"g1","cursor":"2","eventCount":1,"events":[{"kind":"learn-task-snapshot","identity":"snapshot-1","itemId":"LEARN-01.2","revision":2,"snapshotId":"task-1","rubricVersion":"rubric-v1","snapshotDigest":"dddddddddddddddddddddddddddddddddddddddddddddddddddddddddddddddd","capturedAt":"2026-09-27T08:00:00Z"}]}"""
        let manifestRevision =
            Encoding.UTF8.GetBytes
                """{"schema":"fsgg.telemetry.ingest/1","ingestId":"learn-pre-3","sourceIdentity":"producer","generation":"g1","cursor":"3","eventCount":1,"events":[{"kind":"learn-context-manifest","identity":"manifest-1","itemId":"LEARN-01.2","revision":2,"recipeId":"recipe-v1","recipeDigest":"eeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeee","manifestId":"manifest-v1","manifestDigest":"ffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffff"}]}"""

        try
            TelemetryStoreApplication.initialize root TelemetryStore.ApprovedLocalDurable
            |> Result.defaultWith (String.concat "; " >> failwith)
            |> ignore
            Assert.True(TelemetryStoreApplication.ingest root TelemetryStore.ApprovedLocalDurable original |> Result.isOk)
            Assert.True(TelemetryStoreApplication.ingest root TelemetryStore.ApprovedLocalDurable original |> Result.isOk)

            for changed, label in [ snapshotRevision, "snapshot"; manifestRevision, "manifest" ] do
                match TelemetryStoreApplication.ingest root TelemetryStore.ApprovedLocalDurable changed with
                | Error errors -> Assert.Contains("immutable after pre-dispatch persistence", String.concat "; " errors)
                | Ok _ -> Assert.Fail $"{label} revision must be refused"
        finally
            if Directory.Exists root then Directory.Delete(root, true)

    [<Fact>]
    let ``LEARN v3 accounting facts are closed typed and immutable in schema 11`` () =
        let root = Path.Combine(Path.GetTempPath(), "learn-v3-" + Guid.NewGuid().ToString("N"))
        let exported = Path.Combine(Path.GetTempPath(), "learn-v3-public-" + Guid.NewGuid().ToString("N") + ".json")
        let batch ingest revision cutoff sharedTokens =
            Encoding.UTF8.GetBytes
                $"""{{"schema":"fsgg.telemetry.ingest/1","ingestId":"{ingest}","sourceIdentity":"producer","generation":"g1","cursor":"{revision}","eventCount":5,"events":[{{"kind":"learn-accounting-inventory/1","identity":"accounting-1","itemId":"LEARN-01.2","revision":{revision},"inventoryId":"accounting-v1","windowId":"window-v1","policyId":"learn-01-current-focused-v1","scope":"whole-original-item","cutoffAt":"{cutoff}","capturedAt":"2026-09-27T07:59:00Z","ciApplicability":"not-applicable","expectedDispatchIds":["dispatch-1"],"expectedSharedCostIds":["shared-1"],"sourceKind":"prospective-independent-roster","sourceDigest":"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa"}},{{"kind":"runtime-native-inventory/1","identity":"native-1","itemId":"LEARN-01.2","revision":{revision},"inventoryId":"native-v1","originalItemId":"LEARN-01.2","invocationId":"invocation-1","page":1,"pages":1,"expectedTurnIds":["turn-1"],"expectedProvider":"openai","requestedModel":"gpt-fixed","requestedEffort":"medium","support":"provider-native-final-turn-counters","followupBaseline":0,"capturedAt":"2026-09-27T08:00:00Z","sourceKind":"provider-capability-and-dispatch-roster","sourceDigest":"bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb"}},{{"kind":"learn-shared-cost/1","identity":"shared-1-fact","itemId":"LEARN-01.2","revision":{revision},"nativeCostId":"shared-1","provider":"openai","providerTotalTokens":20,"allocations":[{{"originalItemId":"LEARN-01.2","tokens":{sharedTokens}}}],"sourceKind":"native-shared-cost","sourceDigest":"bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb"}},{{"kind":"learn-shared-cost-allocation/1","identity":"shared-1-allocation","itemId":"LEARN-01.2","revision":{revision},"nativeCostId":"shared-1","policyId":"learn-01-current-focused-v1","windowId":"window-v1","frozenAt":"2026-09-27T07:59:30Z","allocationRule":"equal-largest-remainder-v1","allocationRoster":["LEARN-01.2"]}},{{"kind":"learn-shared-cost-authority/1","identity":"shared-1-authority","itemId":"LEARN-01.2","revision":{revision},"nativeCostId":"shared-1","sourceInventoryId":"native-v1","sourceInvocationId":"invocation-1","sourceKind":"retained-native-shared-cost-source","sourceDigest":"bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb"}}]}}"""

        try
            let original = batch "learn-v3-1" 1 "2026-10-27T08:00:00Z" 20
            match TelemetryStore.parseBatch original with
            | Ok parsed -> Assert.Equal(5, parsed.Facts.Length)
            | Error errors -> Assert.Fail(String.concat "; " errors)
            Assert.True(batch "learn-v3-bad" 1 "2026-10-27T08:00:00Z" 19 |> TelemetryStore.parseBatch |> Result.isError)
            let zeroDigest =
                Encoding.UTF8.GetString(original).Replace(String('a', 64), String('0', 64))
                |> Encoding.UTF8.GetBytes
            Assert.True(zeroDigest |> TelemetryStore.parseBatch |> Result.isError)

            TelemetryStoreApplication.initialize root TelemetryStore.ApprovedLocalDurable
            |> Result.defaultWith (String.concat "; " >> failwith)
            |> ignore
            Assert.True(TelemetryStoreApplication.ingest root TelemetryStore.ApprovedLocalDurable original |> Result.isOk)
            Assert.True(TelemetryStoreApplication.ingest root TelemetryStore.ApprovedLocalDurable original |> Result.isOk)
            let correction = batch "learn-v3-2" 2 "2026-11-27T08:00:00Z" 20
            match TelemetryStoreApplication.ingest root TelemetryStore.ApprovedLocalDurable correction with
            | Error errors -> Assert.Contains("immutable after pre-dispatch persistence", String.concat "; " errors)
            | Ok _ -> Assert.Fail "inventory correction must be refused"
            TelemetryStoreApplication.exportPublic root TelemetryStore.ApprovedLocalDurable None exported
            |> Result.defaultWith (String.concat "; " >> failwith) |> ignore
            use publicDocument = JsonDocument.Parse(File.ReadAllText exported)
            Assert.Equal(0, publicDocument.RootElement.GetProperty("items").GetArrayLength())
            publicDocument.Dispose()
            TelemetryStoreApplication.exportPublic root TelemetryStore.ApprovedLocalDurable (Some "LEARN-01.2") exported
            |> Result.defaultWith (String.concat "; " >> failwith) |> ignore
            use directDocument = JsonDocument.Parse(File.ReadAllText exported)
            Assert.Equal(0L, directDocument.RootElement.GetProperty("factCount").GetInt64())
        finally
            if Directory.Exists root then Directory.Delete(root, true)
            if File.Exists exported then File.Delete exported

    [<Fact>]
    let ``LEARN shared allocation is durably ordered before assignment`` () =
        let root = Path.Combine(Path.GetTempPath(), "learn-shared-order-" + Guid.NewGuid().ToString("N"))
        let allocation cost identity =
            Encoding.UTF8.GetBytes
                $"""{{"schema":"fsgg.telemetry.ingest/1","ingestId":"{identity}","sourceIdentity":"producer","generation":"g1","cursor":"1","eventCount":1,"events":[{{"kind":"learn-shared-cost-allocation/1","identity":"{identity}","itemId":"I-001","revision":1,"nativeCostId":"{cost}","policyId":"learn-01-current-focused-v1","windowId":"window-v1","frozenAt":"2025-12-31T23:59:00Z","allocationRule":"equal-largest-remainder-v1","allocationRoster":["I-001"]}}]}}"""
        let assignment =
            Encoding.UTF8.GetBytes
                """{"schema":"fsgg.telemetry.ingest/1","ingestId":"assignment-batch","sourceIdentity":"producer","generation":"g1","cursor":"2","eventCount":1,"events":[{"kind":"learn-experiment-assignment","identity":"assignment-I-001","itemId":"I-001","revision":1,"windowId":"window-v1","policyId":"learn-01-current-focused-v1","arm":"current","assignedAt":"2026-01-01T00:00:00Z","deviation":null}]}"""
        try
            TelemetryStoreApplication.initialize root TelemetryStore.ApprovedLocalDurable
            |> Result.defaultWith (String.concat "; " >> failwith) |> ignore
            TelemetryStoreApplication.ingest root TelemetryStore.ApprovedLocalDurable (allocation "shared-1" "allocation-1")
            |> Result.defaultWith (String.concat "; " >> failwith) |> ignore
            TelemetryStoreApplication.ingest root TelemetryStore.ApprovedLocalDurable assignment
            |> Result.defaultWith (String.concat "; " >> failwith) |> ignore
            TelemetryStoreApplication.ingest root TelemetryStore.ApprovedLocalDurable (allocation "shared-1" "allocation-1")
            |> Result.defaultWith (String.concat "; " >> failwith) |> ignore
            match TelemetryStoreApplication.ingest root TelemetryStore.ApprovedLocalDurable (allocation "shared-2" "allocation-2") with
            | Error errors -> Assert.Contains("must be persisted before assignment", String.concat "; " errors)
            | Ok _ -> Assert.Fail "backdated post-assignment allocation must be refused"

            let orders () =
                let snapshot =
                    TelemetryStoreApplication.dashboardSnapshot root TelemetryStore.ApprovedLocalDurable None
                    |> Result.defaultWith (String.concat "; " >> failwith)
                use envelope = JsonDocument.Parse snapshot
                let compressed = Convert.FromBase64String(envelope.RootElement.GetProperty("canonicalSnapshotGzip").GetString())
                use input = new MemoryStream(compressed)
                use gzip = new GZipStream(input, CompressionMode.Decompress)
                use canonical = JsonDocument.Parse gzip
                canonical.RootElement.GetProperty("learningObservations").EnumerateArray()
                |> Seq.map (fun row -> row.GetProperty("kind").GetString(), row.GetProperty("ingest_order").GetInt64())
                |> Map.ofSeq

            let beforeVacuum = orders ()
            Assert.True(beforeVacuum["learn-shared-cost-allocation/1"] < beforeVacuum["learn-experiment-assignment"])
            let databasePath = Path.Combine(root, "telemetry.sqlite3")
            use database = new SqliteConnection($"Data Source={databasePath}")
            database.Open()
            use count = database.CreateCommand()
            count.CommandText <- "SELECT count(*) FROM learning_fact_order WHERE identity IN ('allocation-1','allocation-2');"
            Assert.Equal(1L, Convert.ToInt64(count.ExecuteScalar()))
            use vacuum = database.CreateCommand()
            vacuum.CommandText <- "VACUUM;"
            vacuum.ExecuteNonQuery() |> ignore
            database.Close()
            Assert.Equal<Map<string, int64>>(beforeVacuum, orders ())

            // A schema-11 store keeps its durable order, but migration cannot invent receipt provenance.
            use downgrade11 = new SqliteConnection($"Data Source={databasePath};Pooling=False")
            downgrade11.Open()
            use downgrade11Command = downgrade11.CreateCommand()
            downgrade11Command.CommandText <-
                "DROP TABLE fact_admissions; DROP TABLE receipt_admissions; ALTER TABLE receipt_producers DROP COLUMN grant_generation; ALTER TABLE receipt_producers DROP COLUMN grant_id; ALTER TABLE receipt_producers DROP COLUMN authority_role; DELETE FROM schema_migrations WHERE version=12; PRAGMA user_version=11;"
            downgrade11Command.ExecuteNonQuery() |> ignore
            downgrade11.Close()
            TelemetryStoreApplication.initialize root TelemetryStore.ApprovedLocalDurable
            |> Result.defaultWith (String.concat "; " >> failwith) |> ignore
            Assert.Equal<Map<string, int64>>(beforeVacuum, orders ())
            let migrated11 =
                TelemetryStoreApplication.dashboardSnapshot root TelemetryStore.ApprovedLocalDurable None
                |> Result.defaultWith (String.concat "; " >> failwith)
            use migrated11Envelope = JsonDocument.Parse migrated11
            let migrated11Compressed = Convert.FromBase64String(migrated11Envelope.RootElement.GetProperty("canonicalSnapshotGzip").GetString())
            use migrated11Input = new MemoryStream(migrated11Compressed)
            use migrated11Gzip = new GZipStream(migrated11Input, CompressionMode.Decompress)
            use migrated11Canonical = JsonDocument.Parse migrated11Gzip
            for row in migrated11Canonical.RootElement.GetProperty("learningObservations").EnumerateArray() do
                Assert.Equal(JsonValueKind.Null, row.GetProperty("receipt_role").ValueKind)

            use downgrade = new SqliteConnection($"Data Source={databasePath};Pooling=False")
            downgrade.Open()
            use downgradeCommand = downgrade.CreateCommand()
            downgradeCommand.CommandText <-
                "DROP TABLE fact_admissions; DROP TABLE receipt_admissions; ALTER TABLE receipt_producers DROP COLUMN grant_generation; ALTER TABLE receipt_producers DROP COLUMN grant_id; ALTER TABLE receipt_producers DROP COLUMN authority_role; DELETE FROM schema_migrations WHERE version=12; DROP TABLE learning_fact_order; DELETE FROM schema_migrations WHERE version=11; PRAGMA user_version=10;"
            downgradeCommand.ExecuteNonQuery() |> ignore
            downgrade.Close()
            TelemetryStoreApplication.initialize root TelemetryStore.ApprovedLocalDurable
            |> Result.defaultWith (String.concat "; " >> failwith) |> ignore
            let upgraded =
                TelemetryStoreApplication.dashboardSnapshot root TelemetryStore.ApprovedLocalDurable None
                |> Result.defaultWith (String.concat "; " >> failwith)
            use upgradedEnvelope = JsonDocument.Parse upgraded
            let upgradedCompressed = Convert.FromBase64String(upgradedEnvelope.RootElement.GetProperty("canonicalSnapshotGzip").GetString())
            use upgradedInput = new MemoryStream(upgradedCompressed)
            use upgradedGzip = new GZipStream(upgradedInput, CompressionMode.Decompress)
            use upgradedCanonical = JsonDocument.Parse upgradedGzip
            for row in upgradedCanonical.RootElement.GetProperty("learningObservations").EnumerateArray() do
                Assert.Equal(JsonValueKind.Null, row.GetProperty("ingest_order").ValueKind)
        finally
            if Directory.Exists root then Directory.Delete(root, true)

    [<Fact>]
    let ``LEARN native inventory source binding is canonical durable and closed`` () =
        let root = Path.Combine(Path.GetTempPath(), "learn-native-source-" + Guid.NewGuid().ToString("N"))
        let binding =
            """{"capturedAt":"2026-09-27T08:00:00Z","hostSource":"codex-app-server:thread/turns/list","invocationId":"invocation-1","orderedTurnIds":["turn-1"],"parentThreadId":"11111111-1111-4111-8111-111111111111","producerIdentity":"fsgg-work-roadmap-native-collector/1","revision":1,"rootInvocationId":"invocation-1","schema":"fsgg.telemetry.native-inventory-source-binding/1","threadId":"22222222-2222-4222-8222-222222222222"}"""
        let encoded = Convert.ToBase64String(Encoding.UTF8.GetBytes binding)
        let digest = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes binding)).ToLowerInvariant()
        let batch invocation revision bindingDigest bindingBytes =
            Encoding.UTF8.GetBytes
                $"""{{"schema":"fsgg.telemetry.ingest/1","ingestId":"native-source-{invocation}-{revision}","sourceIdentity":"roadmap","generation":"g1","cursor":"{revision}","eventCount":1,"events":[{{"kind":"runtime-native-inventory-source/1","identity":"native-source-1","itemId":"LEARN-01.2","revision":{revision},"inventoryId":"native-v1","originalItemId":"LEARN-01.2","invocationId":"{invocation}","sourceDigest":"bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb","sourceBinding":{{"schema":"fsgg.telemetry.native-inventory-source-binding/1","producerIdentity":"fsgg-work-roadmap-native-collector/1","sha256":"{bindingDigest}","bytesBase64":"{bindingBytes}"}}}}]}}"""

        let valid = batch "invocation-1" 1 digest encoded
        match TelemetryStore.parseBatch valid with
        | Ok parsed ->
            Assert.Single(parsed.Facts) |> ignore
            match parsed.Facts.Head.Payload with
            | TelemetryStore.RuntimeNativeInventorySource _ -> ()
            | _ -> Assert.Fail "native source fact parsed to the wrong payload"
        | Error errors -> Assert.Fail(String.concat "; " errors)

        Assert.True(batch "foreign-invocation" 1 digest encoded |> TelemetryStore.parseBatch |> Result.isError)
        Assert.True(batch "invocation-1" 2 digest encoded |> TelemetryStore.parseBatch |> Result.isError)
        Assert.True(batch "invocation-1" 1 (String('0', 64)) encoded |> TelemetryStore.parseBatch |> Result.isError)
        let noncanonical = binding + " "
        let noncanonicalBytes = Convert.ToBase64String(Encoding.UTF8.GetBytes noncanonical)
        let noncanonicalDigest =
            Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes noncanonical)).ToLowerInvariant()
        Assert.True(batch "invocation-1" 1 noncanonicalDigest noncanonicalBytes |> TelemetryStore.parseBatch |> Result.isError)
        try
            TelemetryStoreApplication.initialize root TelemetryStore.ApprovedLocalDurable
            |> Result.defaultWith (String.concat "; " >> failwith) |> ignore
            TelemetryStoreApplication.ingest root TelemetryStore.ApprovedLocalDurable valid
            |> Result.defaultWith (String.concat "; " >> failwith) |> ignore
            let snapshot =
                TelemetryStoreApplication.dashboardSnapshot root TelemetryStore.ApprovedLocalDurable (Some "LEARN-01.2")
                |> Result.defaultWith (String.concat "; " >> failwith)
            use envelope = JsonDocument.Parse snapshot
            let compressed = Convert.FromBase64String(envelope.RootElement.GetProperty("canonicalSnapshotGzip").GetString())
            use input = new MemoryStream(compressed)
            use gzip = new GZipStream(input, CompressionMode.Decompress)
            use canonical = JsonDocument.Parse gzip
            let observations = canonical.RootElement.GetProperty("learningObservations")
            Assert.Equal(1, observations.GetArrayLength())
            Assert.Equal("runtime-native-inventory-source/1", observations[0].GetProperty("kind").GetString())
        finally
            if Directory.Exists root then Directory.Delete(root, true)

    [<Fact>]
    let ``LEARN assignment replay is idempotent and redraw is refused by the schema-11 store`` () =
        let root = Path.Combine(Path.GetTempPath(), "learn-observation-" + Guid.NewGuid().ToString("N"))
        let batch ingest revision arm =
            Encoding.UTF8.GetBytes
                $"""{{"schema":"fsgg.telemetry.ingest/1","ingestId":"{ingest}","sourceIdentity":"producer","generation":"g1","cursor":"{revision}","eventCount":1,"events":[{{"kind":"learn-experiment-assignment","identity":"assignment-1","itemId":"LEARN-01.2","revision":{revision},"windowId":"window-v1","policyId":"learn-01-current-focused-v1","arm":"{arm}","assignedAt":"2026-09-27T08:01:00Z","deviation":null}}]}}"""

        try
            TelemetryStoreApplication.initialize root TelemetryStore.ApprovedLocalDurable
            |> Result.defaultWith (String.concat "; " >> failwith)
            |> ignore
            let original = batch "learn-1" 1 "focused"
            Assert.True(TelemetryStoreApplication.ingest root TelemetryStore.ApprovedLocalDurable original |> Result.isOk)
            Assert.True(TelemetryStoreApplication.ingest root TelemetryStore.ApprovedLocalDurable original |> Result.isOk)
            let redraw = batch "learn-2" 2 "current"
            let result = TelemetryStoreApplication.ingest root TelemetryStore.ApprovedLocalDurable redraw
            match result with
            | Error errors -> Assert.Contains("immutable after pre-dispatch persistence", String.concat "; " errors)
            | Ok _ -> Assert.Fail "assignment redraw must be refused"
        finally
            if Directory.Exists root then Directory.Delete(root, true)

    [<Fact>]
    let ``LEARN private snapshot retains workspace provenance and public export excludes private facts`` () =
        let root = Path.Combine(Path.GetTempPath(), "learn-private-" + Guid.NewGuid().ToString("N"))
        let exported = Path.Combine(Path.GetTempPath(), "learn-public-" + Guid.NewGuid().ToString("N") + ".json")
        let directExport = Path.Combine(Path.GetTempPath(), "learn-direct-" + Guid.NewGuid().ToString("N") + ".json")
        let privateScope =
            { scope with
                Workspace = "workspace-private"
                Producer = "producer-private"
            }
        let bytes =
            Encoding.UTF8.GetBytes
                """{"schema":"fsgg.telemetry.envelope/1","workspaceId":"workspace-private","producerId":"producer-private","streamId":"runtime","batchId":"learn-private-batch","payload":{"schema":"fsgg.telemetry.ingest/1","ingestId":"ignored-by-receipt-boundary","sourceIdentity":"producer","generation":"g1","cursor":"1","eventCount":3,"events":[{"kind":"learn-task-snapshot","identity":"snapshot-private","itemId":"private-item","revision":1,"snapshotId":"task-1","rubricVersion":"rubric-v1","snapshotDigest":"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa","capturedAt":"2026-09-27T08:00:00Z"},{"kind":"learn-context-manifest","identity":"manifest-private","itemId":"private-item","revision":1,"recipeId":"recipe-v1","recipeDigest":"bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb","manifestId":"manifest-v1","manifestDigest":"cccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccc"},{"kind":"learn-experiment-assignment","identity":"assignment-private","itemId":"private-item","revision":1,"windowId":"window-v1","policyId":"learn-01-current-focused-v1","arm":"focused","assignedAt":"2026-09-27T08:01:00Z","deviation":null}]}}"""

        try
            TelemetryStoreApplication.initialize root TelemetryStore.ApprovedLocalDurable
            |> Result.defaultWith (String.concat "; " >> failwith)
            |> ignore
            TelemetryStoreApplication.enrollReceiptProducer root TelemetryStore.ApprovedLocalDurable privateScope
            |> Result.defaultWith (String.concat "; " >> failwith)
            |> ignore
            TelemetryStoreApplication.submitReceipt root TelemetryStore.ApprovedLocalDurable privateScope bytes
            |> Result.defaultWith (String.concat "; " >> failwith)
            |> ignore
            TelemetryStoreApplication.drainReceipts root TelemetryStore.ApprovedLocalDurable privateScope.Workspace
            |> Result.defaultWith (String.concat "; " >> failwith)
            |> ignore

            let snapshot =
                TelemetryStoreApplication.dashboardSnapshot root TelemetryStore.ApprovedLocalDurable None
                |> Result.defaultWith (String.concat "; " >> failwith)
            use envelope = JsonDocument.Parse snapshot
            Assert.Equal("workspace-private", envelope.RootElement.GetProperty("workspaceId").GetString())
            let compressed = Convert.FromBase64String(envelope.RootElement.GetProperty("canonicalSnapshotGzip").GetString())
            use input = new MemoryStream(compressed)
            use gzip = new GZipStream(input, CompressionMode.Decompress)
            use canonical = JsonDocument.Parse gzip
            Assert.Equal("workspace-private", canonical.RootElement.GetProperty("workspaceId").GetString())
            Assert.Equal("fsgg.telemetry.learn-item-detail/4", canonical.RootElement.GetProperty("learningSnapshotSchema").GetString())
            Assert.Equal(3, canonical.RootElement.GetProperty("learningObservations").GetArrayLength())

            TelemetryStoreApplication.exportPublic root TelemetryStore.ApprovedLocalDurable None exported
            |> Result.defaultWith (String.concat "; " >> failwith)
            |> ignore
            use publicDocument = JsonDocument.Parse(File.ReadAllText exported)
            Assert.Equal(0, publicDocument.RootElement.GetProperty("items").GetArrayLength())

            TelemetryStoreApplication.exportPublic root TelemetryStore.ApprovedLocalDurable (Some "private-item") directExport
            |> Result.defaultWith (String.concat "; " >> failwith)
            |> ignore
            use directDocument = JsonDocument.Parse(File.ReadAllText directExport)
            Assert.Equal(0L, directDocument.RootElement.GetProperty("factCount").GetInt64())
        finally
            if Directory.Exists root then Directory.Delete(root, true)
            if File.Exists exported then File.Delete exported
            if File.Exists directExport then File.Delete directExport

    [<Fact>]
    let ``LEARN private analysis retains chronology but refuses generic producer authority`` () =
        let root = Path.Combine(Path.GetTempPath(), "learn-v3-e2e-" + Guid.NewGuid().ToString("N"))
        let authorityRoot = Path.Combine(Path.GetTempPath(), "learn-v3-authority-" + Guid.NewGuid().ToString("N"))
        let collectorRoot = Path.Combine(Path.GetTempPath(), "learn-v4-collector-" + Guid.NewGuid().ToString("N"))
        let foreignRoot = Path.Combine(Path.GetTempPath(), "learn-v3-foreign-" + Guid.NewGuid().ToString("N"))
        let futureRoot = Path.Combine(Path.GetTempPath(), "learn-v3-future-" + Guid.NewGuid().ToString("N"))
        let ciRoot = Path.Combine(Path.GetTempPath(), "learn-v3-ci-" + Guid.NewGuid().ToString("N"))
        let snapshotPath = Path.Combine(Path.GetTempPath(), "learn-v3-snapshot-" + Guid.NewGuid().ToString("N") + ".json")
        let privateScope =
            { scope with
                Workspace = "workspace-v3"
                Producer = "producer-v3"
            }
        let bytes =
            Encoding.UTF8.GetBytes
                """{"schema":"fsgg.telemetry.envelope/1","workspaceId":"workspace-v3","producerId":"producer-v3","streamId":"runtime","batchId":"learn-v3-complete","payload":{"schema":"fsgg.telemetry.ingest/1","ingestId":"ignored","sourceIdentity":"producer-v3","generation":"g1","cursor":"1","eventCount":10,"events":[{"kind":"learn-task-snapshot","identity":"snapshot-v3","itemId":"I-001","revision":1,"snapshotId":"task-I-001","rubricVersion":"learn-01-rubric-v1","snapshotDigest":"1111111111111111111111111111111111111111111111111111111111111111","capturedAt":"2025-12-31T23:59:00Z"},{"kind":"learn-context-manifest","identity":"manifest-v3","itemId":"I-001","revision":1,"recipeId":"focused-recipe-v1","recipeDigest":"1111111111111111111111111111111111111111111111111111111111111111","manifestId":"manifest-I-001","manifestDigest":"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa"},{"kind":"learn-experiment-assignment","identity":"assignment-v3","itemId":"I-001","revision":1,"windowId":"window-2026-01","policyId":"learn-01-current-focused-v1","arm":"current","assignedAt":"2026-01-01T00:00:00Z","deviation":null},{"kind":"learn-accounting-inventory/1","identity":"accounting-v3","itemId":"I-001","revision":1,"inventoryId":"accounting-v1","windowId":"window-2026-01","policyId":"learn-01-current-focused-v1","scope":"whole-original-item","cutoffAt":"2026-02-02T00:00:00Z","capturedAt":"2025-12-31T23:59:30Z","ciApplicability":"not-applicable","expectedDispatchIds":["dispatch-v3"],"expectedSharedCostIds":["shared-v3"],"sourceKind":"prospective-independent-roster","sourceDigest":"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa"},{"kind":"runtime-native-inventory/1","identity":"native-v3","itemId":"I-001","revision":1,"inventoryId":"native-v1","originalItemId":"I-001","invocationId":"inv-v3","page":1,"pages":1,"expectedTurnIds":["turn-v3"],"expectedProvider":"openai","requestedModel":"gpt-fixed","requestedEffort":"medium","support":"provider-native-final-turn-counters","followupBaseline":0,"capturedAt":"2026-01-01T00:00:01Z","sourceKind":"provider-capability-and-dispatch-roster","sourceDigest":"bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb"},{"kind":"runtime-admission","identity":"admission-v3","itemId":"I-001","revision":1,"invocationId":"inv-v3","featureId":"LEARN-01","attemptId":"attempt-v3","parentAttemptId":null,"producerStream":"runtime","requestedModel":"gpt-fixed","requestedEffort":"medium","backend":"codex"},{"kind":"expected-dispatch","identity":"expected-v3","itemId":"I-001","revision":1,"dispatchId":"dispatch-v3","activationId":"activation-v3","relation":"root","parentDispatchId":null,"runtime":"codex","expectedAt":"2026-01-01T00:00:00Z","clockProvenance":"host-wall"},{"kind":"invocation-lineage","identity":"lineage-v3","itemId":"I-001","revision":1,"dispatchId":"dispatch-v3","invocationId":"inv-v3","relation":"root","parentInvocationId":null,"rootInvocationId":"inv-v3","runtime":"codex"},{"kind":"runtime-turn-usage","identity":"usage-v3","itemId":"I-001","revision":1,"invocationId":"inv-v3","threadId":"thread-v3","turnId":"turn-v3","turnSequence":1,"provider":"openai","requestedModel":"gpt-fixed","observedModel":"gpt-fixed","requestedEffort":"medium","observedEffort":"medium","backend":"codex","scope":"completed-turn","provenance":"codex-exec-jsonl","input":99,"cachedInput":0,"output":1,"reasoning":null,"total":100},{"kind":"runtime-terminal","identity":"terminal-v3","itemId":"I-001","revision":1,"invocationId":"inv-v3","threadId":"thread-v3","outcome":"completed","exitCode":0}]}}"""

        try
            TelemetryStoreApplication.initialize root TelemetryStore.ApprovedLocalDurable
            |> Result.defaultWith (String.concat "; " >> failwith) |> ignore
            TelemetryStoreApplication.enrollReceiptProducer root TelemetryStore.ApprovedLocalDurable privateScope
            |> Result.defaultWith (String.concat "; " >> failwith) |> ignore
            TelemetryStoreApplication.submitReceipt root TelemetryStore.ApprovedLocalDurable privateScope bytes
            |> Result.defaultWith (String.concat "; " >> failwith) |> ignore
            TelemetryStoreApplication.drainReceipts root TelemetryStore.ApprovedLocalDurable privateScope.Workspace
            |> Result.defaultWith (String.concat "; " >> failwith) |> ignore
            let snapshot =
                TelemetryStoreApplication.scopedDashboardSnapshot root TelemetryStore.ApprovedLocalDurable privateScope.Workspace None
                |> Result.defaultWith (String.concat "; " >> failwith)
            File.WriteAllText(snapshotPath, snapshot)

            let repository = Path.GetFullPath(Path.Combine(__SOURCE_DIRECTORY__, "..", ".."))
            let start = ProcessStartInfo("python3")
            start.WorkingDirectory <- repository
            start.RedirectStandardOutput <- true
            start.RedirectStandardError <- true
            start.ArgumentList.Add(Path.Combine(repository, "tools", "learn-01-analysis.py"))
            start.ArgumentList.Add(Path.Combine(repository, "policy", "learn-01-current-focused-v1.json"))
            start.ArgumentList.Add("--observations")
            start.ArgumentList.Add(snapshotPath)
            use childProcess = Process.Start start
            let output = childProcess.StandardOutput.ReadToEnd()
            let errors = childProcess.StandardError.ReadToEnd()
            childProcess.WaitForExit()
            Assert.True(childProcess.ExitCode = 0, errors)
            use report = JsonDocument.Parse output
            Assert.False(report.RootElement.GetProperty("tokenComparisonQualified").GetBoolean())
            let reasons =
                report.RootElement.GetProperty("incompleteTokenReasons").GetProperty("I-001").EnumerateArray()
                |> Seq.map _.GetString()
                |> Set.ofSeq
            Assert.Contains("independent-inventory-source-unavailable", reasons)
            Assert.Equal(0L, report.RootElement.GetProperty("providerTotalTokensByArm").GetProperty("current").GetInt64())

            let nativeThread = "22222222-2222-4222-8222-222222222222"
            let binding =
                $"""{{"capturedAt":"2026-01-01T00:00:01Z","hostSource":"codex-app-server:thread/turns/list","invocationId":"inv-v3","orderedTurnIds":["turn-v3"],"parentThreadId":"11111111-1111-4111-8111-111111111111","producerIdentity":"fsgg-work-roadmap-native-collector/1","revision":1,"rootInvocationId":"inv-v3","schema":"fsgg.telemetry.native-inventory-source-binding/1","threadId":"{nativeThread}"}}"""
            let bindingBytes = Encoding.UTF8.GetBytes binding
            let bindingDigest = Convert.ToHexString(SHA256.HashData bindingBytes).ToLowerInvariant()
            let sourceEvent =
                $"""{{"kind":"runtime-native-inventory-source/1","identity":"native-source-v3","itemId":"I-001","revision":1,"inventoryId":"native-v1","originalItemId":"I-001","invocationId":"inv-v3","sourceDigest":"bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb","sourceBinding":{{"schema":"fsgg.telemetry.native-inventory-source-binding/1","producerIdentity":"fsgg-work-roadmap-native-collector/1","sha256":"{bindingDigest}","bytesBase64":"{Convert.ToBase64String bindingBytes}"}}}}"""
            let allocationEnvelope =
                Encoding.UTF8.GetBytes
                    """{"schema":"fsgg.telemetry.envelope/1","workspaceId":"workspace-v3","producerId":"producer-v3","streamId":"runtime","batchId":"learn-v3-allocation","payload":{"schema":"fsgg.telemetry.ingest/1","ingestId":"ignored","sourceIdentity":"producer-v3","generation":"g1","cursor":"0","eventCount":1,"events":[{"kind":"learn-shared-cost-allocation/1","identity":"allocation-v3","itemId":"I-001","revision":1,"nativeCostId":"shared-v3","policyId":"learn-01-current-focused-v1","windowId":"window-2026-01","frozenAt":"2025-12-31T23:59:45Z","allocationRule":"equal-largest-remainder-v1","allocationRoster":["I-001"]}]}}"""
            let baseEnvelope =
                Encoding.UTF8.GetString(bytes).Replace("thread-v3", nativeThread)
                |> Encoding.UTF8.GetBytes
            let authorityEvent =
                """{"kind":"learn-shared-cost-authority/1","identity":"authority-v3","itemId":"I-001","revision":1,"nativeCostId":"shared-v3","sourceInventoryId":"native-v1","sourceInvocationId":"inv-v3","sourceKind":"retained-native-shared-cost-source","sourceDigest":"bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb"}"""
            let sharedCostEvent =
                """{"kind":"learn-shared-cost/1","identity":"shared-cost-v3","itemId":"I-001","revision":1,"nativeCostId":"shared-v3","provider":"openai","providerTotalTokens":100,"allocations":[{"originalItemId":"I-001","tokens":100}],"sourceKind":"native-shared-cost","sourceDigest":"bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb"}"""
            let completionEnvelope =
                Encoding.UTF8.GetBytes
                    $"""{{"schema":"fsgg.telemetry.envelope/1","workspaceId":"workspace-v3","producerId":"producer-v3","streamId":"runtime","batchId":"learn-v3-authority","payload":{{"schema":"fsgg.telemetry.ingest/1","ingestId":"ignored","sourceIdentity":"producer-v3","generation":"g1","cursor":"2","eventCount":3,"events":[{sourceEvent},{authorityEvent},{sharedCostEvent}]}}}}"""
            TelemetryStoreApplication.initialize authorityRoot TelemetryStore.ApprovedLocalDurable
            |> Result.defaultWith (String.concat "; " >> failwith) |> ignore
            TelemetryStoreApplication.enrollReceiptProducer authorityRoot TelemetryStore.ApprovedLocalDurable privateScope
            |> Result.defaultWith (String.concat "; " >> failwith) |> ignore
            for envelopeBytes in [ allocationEnvelope; baseEnvelope; completionEnvelope ] do
                TelemetryStoreApplication.submitReceipt authorityRoot TelemetryStore.ApprovedLocalDurable privateScope envelopeBytes
                |> Result.defaultWith (String.concat "; " >> failwith) |> ignore
                TelemetryStoreApplication.drainReceipts authorityRoot TelemetryStore.ApprovedLocalDurable privateScope.Workspace
                |> Result.defaultWith (String.concat "; " >> failwith) |> ignore
            TelemetryStoreApplication.scopedDashboardSnapshot authorityRoot TelemetryStore.ApprovedLocalDurable privateScope.Workspace None
            |> Result.defaultWith (String.concat "; " >> failwith)
            |> fun value -> File.WriteAllText(snapshotPath, value)
            use authorityProcess = Process.Start start
            let authorityOutput = authorityProcess.StandardOutput.ReadToEnd()
            let authorityErrors = authorityProcess.StandardError.ReadToEnd()
            authorityProcess.WaitForExit()
            Assert.True(authorityProcess.ExitCode = 0, authorityErrors)
            use authorityReport = JsonDocument.Parse authorityOutput
            Assert.False(authorityReport.RootElement.GetProperty("tokenComparisonQualified").GetBoolean())
            Assert.Equal(0L, authorityReport.RootElement.GetProperty("providerTotalTokensByArm").GetProperty("current").GetInt64())
            let authorityReasons =
                authorityReport.RootElement.GetProperty("incompleteTokenReasons").GetProperty("I-001").EnumerateArray()
                |> Seq.map _.GetString()
                |> Set.ofSeq
            Assert.Contains("independent-shared-cost-authority-unavailable", authorityReasons)
            Assert.Contains("collector-principal-unavailable", authorityReasons)
            Assert.Contains("snapshot-origin-unverified", authorityReasons)

            let collectorScope = { privateScope with Producer = "collector-v3"; Stream = "native-inventory" }
            let collectorPrincipal: TelemetryReceipt.Principal =
                { Scope = collectorScope; Role = TelemetryReceipt.NativeCollector
                  GrantId = Some "collector-grant"; GrantGeneration = Some 1L }
            let collectorCompletion =
                Encoding.UTF8.GetString(completionEnvelope)
                    .Replace("\"producerId\":\"producer-v3\"", "\"producerId\":\"collector-v3\"")
                    .Replace("\"streamId\":\"runtime\"", "\"streamId\":\"native-inventory\"")
                |> Encoding.UTF8.GetBytes
            TelemetryStoreApplication.initialize collectorRoot TelemetryStore.ApprovedLocalDurable
            |> Result.defaultWith (String.concat "; " >> failwith) |> ignore
            TelemetryStoreApplication.enrollReceiptProducer collectorRoot TelemetryStore.ApprovedLocalDurable privateScope
            |> Result.defaultWith (String.concat "; " >> failwith) |> ignore
            TelemetryStoreApplication.enrollReceiptPrincipal collectorRoot TelemetryStore.ApprovedLocalDurable collectorPrincipal
            |> Result.defaultWith (String.concat "; " >> failwith) |> ignore
            for envelopeBytes in [ allocationEnvelope; baseEnvelope ] do
                TelemetryStoreApplication.submitReceipt collectorRoot TelemetryStore.ApprovedLocalDurable privateScope envelopeBytes
                |> Result.defaultWith (String.concat "; " >> failwith) |> ignore
                TelemetryStoreApplication.drainReceipts collectorRoot TelemetryStore.ApprovedLocalDurable privateScope.Workspace
                |> Result.defaultWith (String.concat "; " >> failwith) |> ignore
            TelemetryStoreApplication.submitReceiptPrincipal collectorRoot TelemetryStore.ApprovedLocalDurable collectorPrincipal collectorCompletion
            |> Result.defaultWith (String.concat "; " >> failwith) |> ignore
            TelemetryStoreApplication.drainReceipts collectorRoot TelemetryStore.ApprovedLocalDurable privateScope.Workspace
            |> Result.defaultWith (String.concat "; " >> failwith) |> ignore
            TelemetryStoreApplication.scopedDashboardSnapshot collectorRoot TelemetryStore.ApprovedLocalDurable privateScope.Workspace None
            |> Result.defaultWith (String.concat "; " >> failwith)
            |> fun value -> File.WriteAllText(snapshotPath, value)
            use collectorProcess = Process.Start start
            let collectorOutput = collectorProcess.StandardOutput.ReadToEnd()
            let collectorErrors = collectorProcess.StandardError.ReadToEnd()
            collectorProcess.WaitForExit()
            Assert.True(collectorProcess.ExitCode = 0, collectorErrors)
            use collectorReport = JsonDocument.Parse collectorOutput
            let collectorReasons =
                collectorReport.RootElement.GetProperty("incompleteTokenReasons").GetProperty("I-001").EnumerateArray()
                |> Seq.map _.GetString() |> Set.ofSeq
            Assert.False(collectorReport.RootElement.GetProperty("tokenComparisonQualified").GetBoolean())
            Assert.DoesNotContain("collector-principal-unavailable", collectorReasons)
            Assert.Contains("native-source-verification-unavailable", collectorReasons)
            Assert.Contains("snapshot-origin-unverified", collectorReasons)

            let foreignTerminal =
                Encoding.UTF8.GetString(bytes)
                    .Replace("\"identity\":\"terminal-v3\",\"itemId\":\"I-001\"", "\"identity\":\"terminal-v3\",\"itemId\":\"foreign-item\"")
                |> Encoding.UTF8.GetBytes
            TelemetryStoreApplication.initialize foreignRoot TelemetryStore.ApprovedLocalDurable
            |> Result.defaultWith (String.concat "; " >> failwith) |> ignore
            TelemetryStoreApplication.enrollReceiptProducer foreignRoot TelemetryStore.ApprovedLocalDurable privateScope
            |> Result.defaultWith (String.concat "; " >> failwith) |> ignore
            TelemetryStoreApplication.submitReceipt foreignRoot TelemetryStore.ApprovedLocalDurable privateScope foreignTerminal
            |> Result.defaultWith (String.concat "; " >> failwith) |> ignore
            TelemetryStoreApplication.drainReceipts foreignRoot TelemetryStore.ApprovedLocalDurable privateScope.Workspace
            |> Result.defaultWith (String.concat "; " >> failwith) |> ignore
            TelemetryStoreApplication.scopedDashboardSnapshot foreignRoot TelemetryStore.ApprovedLocalDurable privateScope.Workspace None
            |> Result.defaultWith (String.concat "; " >> failwith)
            |> fun value -> File.WriteAllText(snapshotPath, value)
            use foreignProcess = Process.Start start
            let foreignOutput = foreignProcess.StandardOutput.ReadToEnd()
            let foreignErrors = foreignProcess.StandardError.ReadToEnd()
            foreignProcess.WaitForExit()
            Assert.Equal(2, foreignProcess.ExitCode)
            Assert.Contains("terminal crosses original-item identity", foreignErrors + foreignOutput)

            let futureCapture =
                Encoding.UTF8.GetString(bytes).Replace("2026-01-01T00:00:01Z", "2027-01-01T00:00:00Z")
                |> Encoding.UTF8.GetBytes
            TelemetryStoreApplication.initialize futureRoot TelemetryStore.ApprovedLocalDurable
            |> Result.defaultWith (String.concat "; " >> failwith) |> ignore
            TelemetryStoreApplication.enrollReceiptProducer futureRoot TelemetryStore.ApprovedLocalDurable privateScope
            |> Result.defaultWith (String.concat "; " >> failwith) |> ignore
            TelemetryStoreApplication.submitReceipt futureRoot TelemetryStore.ApprovedLocalDurable privateScope futureCapture
            |> Result.defaultWith (String.concat "; " >> failwith) |> ignore
            TelemetryStoreApplication.drainReceipts futureRoot TelemetryStore.ApprovedLocalDurable privateScope.Workspace
            |> Result.defaultWith (String.concat "; " >> failwith) |> ignore
            TelemetryStoreApplication.scopedDashboardSnapshot futureRoot TelemetryStore.ApprovedLocalDurable privateScope.Workspace None
            |> Result.defaultWith (String.concat "; " >> failwith)
            |> fun value -> File.WriteAllText(snapshotPath, value)
            use futureProcess = Process.Start start
            let futureOutput = futureProcess.StandardOutput.ReadToEnd()
            let futureErrors = futureProcess.StandardError.ReadToEnd()
            futureProcess.WaitForExit()
            Assert.Equal(2, futureProcess.ExitCode)
            Assert.Contains("capture must fall between assignment and cutoff", futureErrors + futureOutput)

            let requiredCi =
                Encoding.UTF8.GetString(bytes).Replace("\"ciApplicability\":\"not-applicable\"", "\"ciApplicability\":\"required\"")
                |> Encoding.UTF8.GetBytes
            TelemetryStoreApplication.initialize ciRoot TelemetryStore.ApprovedLocalDurable
            |> Result.defaultWith (String.concat "; " >> failwith) |> ignore
            TelemetryStoreApplication.enrollReceiptProducer ciRoot TelemetryStore.ApprovedLocalDurable privateScope
            |> Result.defaultWith (String.concat "; " >> failwith) |> ignore
            TelemetryStoreApplication.submitReceipt ciRoot TelemetryStore.ApprovedLocalDurable privateScope requiredCi
            |> Result.defaultWith (String.concat "; " >> failwith) |> ignore
            TelemetryStoreApplication.drainReceipts ciRoot TelemetryStore.ApprovedLocalDurable privateScope.Workspace
            |> Result.defaultWith (String.concat "; " >> failwith) |> ignore
            TelemetryStoreApplication.scopedDashboardSnapshot ciRoot TelemetryStore.ApprovedLocalDurable privateScope.Workspace None
            |> Result.defaultWith (String.concat "; " >> failwith)
            |> fun value -> File.WriteAllText(snapshotPath, value)
            use ciProcess = Process.Start start
            let ciOutput = ciProcess.StandardOutput.ReadToEnd()
            let ciErrors = ciProcess.StandardError.ReadToEnd()
            ciProcess.WaitForExit()
            Assert.True(ciProcess.ExitCode = 0, ciErrors)
            use ciReport = JsonDocument.Parse ciOutput
            let ciReasons =
                ciReport.RootElement.GetProperty("incompleteTokenReasons").GetProperty("I-001").EnumerateArray()
                |> Seq.map _.GetString()
                |> Set.ofSeq
            Assert.Contains("missing-ci-population-coverage", ciReasons)
        finally
            if Directory.Exists root then Directory.Delete(root, true)
            if Directory.Exists authorityRoot then Directory.Delete(authorityRoot, true)
            if Directory.Exists collectorRoot then Directory.Delete(collectorRoot, true)
            if Directory.Exists foreignRoot then Directory.Delete(foreignRoot, true)
            if Directory.Exists futureRoot then Directory.Delete(futureRoot, true)
            if Directory.Exists ciRoot then Directory.Delete(ciRoot, true)
            if File.Exists snapshotPath then File.Delete snapshotPath

    [<Fact>]
    let ``LEARN private snapshot refuses overflow before claiming complete selection`` () =
        let root = Path.Combine(Path.GetTempPath(), "learn-overflow-" + Guid.NewGuid().ToString("N"))

        try
            TelemetryStoreApplication.initialize root TelemetryStore.ApprovedLocalDurable
            |> Result.defaultWith (String.concat "; " >> failwith) |> ignore
            use connection =
                new SqliteConnection(
                    $"Data Source={Path.Combine(root, TelemetryStoreApplication.databaseFileName)};Pooling=False"
                )
            connection.Open()
            use insert = connection.CreateCommand()
            insert.CommandText <-
                "WITH RECURSIVE n(x) AS (VALUES(1) UNION ALL SELECT x+1 FROM n WHERE x<10001) INSERT INTO ingest_facts(identity,kind,item_id,revision,content_digest,canonical) SELECT 'overflow-'||x,'learn-task-snapshot','I-overflow',1,'aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa','{}' FROM n;"
            Assert.Equal(10001, insert.ExecuteNonQuery())
            match TelemetryStoreApplication.dashboardSnapshot root TelemetryStore.ApprovedLocalDurable None with
            | Error errors -> Assert.Contains("learning observation snapshot row bound exceeded", String.concat "; " errors)
            | Ok _ -> Assert.Fail "overflowed private learning rows must not claim a complete selection"
        finally
            if Directory.Exists root then Directory.Delete(root, true)

    [<Fact>]
    let ``client accepts only a complete bound receipt`` () =
        task {
            use client =
                new HttpClient(
                    new Handler(fun () ->
                        new HttpResponseMessage(
                            HttpStatusCode.Accepted,
                            Content = new ByteArrayContent(receipt "batch-a" "durably-received")
                        ))
                )

            let! result =
                RemoteClient.submitWithClientForTesting
                    client
                    config
                    resolve
                    scope
                    (envelope "batch-a")
                    CancellationToken.None

            match result with
            | RemoteClient.Acknowledged value -> Assert.Equal("batch-a", value.BatchId)
            | _ -> Assert.Fail "expected acknowledgement"
        }

    [<Fact>]
    let ``202 alone and receipt-looking server errors are never acknowledgements`` () =
        task {
            for status, body in
                [
                    HttpStatusCode.Accepted, [||]
                    HttpStatusCode.InternalServerError, receipt "batch-a" "durably-received"
                ] do
                use client =
                    new HttpClient(
                        new Handler(fun () -> new HttpResponseMessage(status, Content = new ByteArrayContent(body)))
                    )

                let! result =
                    RemoteClient.submitWithClientForTesting
                        client
                        config
                        resolve
                        scope
                        (envelope "batch-a")
                        CancellationToken.None

                match result with
                | RemoteClient.Unacknowledged _ -> ()
                | _ -> Assert.Fail "invalid acknowledgement"
        }

    [<Fact>]
    let ``client retains closed terminal error identity`` () =
        task {
            use client =
                new HttpClient(
                    new Handler(fun () ->
                        new HttpResponseMessage(
                            HttpStatusCode.Conflict,
                            Content = new ByteArrayContent(RemoteContract.writeError "identity-conflict")
                        ))
                )

            let! result =
                RemoteClient.submitWithClientForTesting
                    client
                    config
                    resolve
                    scope
                    (envelope "batch-a")
                    CancellationToken.None

            Assert.Equal(RemoteClient.Unacknowledged "identity-conflict", result)
        }

    [<Fact>]
    let ``client refuses redirects and bounds malformed responses to five attempts`` () =
        task {
            let mutable redirects = 0

            use redirectClient =
                new HttpClient(
                    new Handler(fun () ->
                        redirects <- redirects + 1
                        new HttpResponseMessage(HttpStatusCode.TemporaryRedirect))
                )

            let! redirected =
                RemoteClient.submitWithClientForTesting
                    redirectClient
                    config
                    resolve
                    scope
                    (envelope "batch-a")
                    CancellationToken.None

            Assert.Equal(RemoteClient.Unacknowledged "redirect-refused", redirected)
            Assert.Equal(1, redirects)

            for body in [ Array.zeroCreate 4097; Encoding.UTF8.GetBytes "not-json" ] do
                let mutable attempts = 0

                use malformedClient =
                    new HttpClient(
                        new Handler(fun () ->
                            attempts <- attempts + 1
                            new HttpResponseMessage(HttpStatusCode.Accepted, Content = new ByteArrayContent(body)))
                    )

                let! malformed =
                    RemoteClient.submitWithClientForTesting
                        malformedClient
                        config
                        resolve
                        scope
                        (envelope "batch-a")
                        CancellationToken.None

                Assert.Equal(RemoteClient.Unacknowledged "receipt-unavailable", malformed)
                Assert.Equal(5, attempts)
        }

    [<Fact>]
    let ``GS2-08.9 client is confined to telemetry submission and rejects authority escalation`` () =
        task {
            let fakeCredential = String('f', 48)
            let mutable credentialResolutions = 0

            let fakeResolve _ _ =
                credentialResolutions <- credentialResolutions + 1
                Task.FromResult(Some fakeCredential)

            let loopbackConfig: RemoteContract.ClientConfig =
                {
                    Endpoint = Uri("https://127.0.0.1:44443/")
                    CredentialReference = "fake-loopback-only"
                }

            use handler =
                new CapturingHandler(fun _ ->
                    new HttpResponseMessage(
                        HttpStatusCode.Accepted,
                        Content = new ByteArrayContent(receipt "batch-boundary" "durably-received")
                    ))

            use client = new HttpClient(handler)
            let submittedEnvelope = envelope "batch-boundary"

            let! accepted =
                RemoteClient.submitWithClientForTesting
                    client
                    loopbackConfig
                    fakeResolve
                    scope
                    submittedEnvelope
                    CancellationToken.None

            match accepted with
            | RemoteClient.Acknowledged value -> Assert.Equal("batch-boundary", value.BatchId)
            | _ -> Assert.Fail "loopback telemetry receipt was not acknowledged"

            let submission = Assert.Single(handler.Requests)
            Assert.Equal(HttpMethod.Post, submission.Method)
            Assert.Equal(Uri("https://127.0.0.1:44443/v1/batches"), submission.Uri)
            Assert.Equal(Some $"Bearer {fakeCredential}", submission.Authorization)
            Assert.True(submittedEnvelope.AsSpan().SequenceEqual(submission.Body))
            Assert.Equal(1, credentialResolutions)

            let otherScope = { scope with Producer = "coordination-authority" }

            let! escalation =
                RemoteClient.submitWithClientForTesting
                    client
                    loopbackConfig
                    fakeResolve
                    scope
                    (makeEnvelope otherScope "authority-escalation" 0)
                    CancellationToken.None

            Assert.Equal(RemoteClient.Unacknowledged "unauthorized-scope", escalation)
            Assert.Single(handler.Requests) |> ignore
            Assert.Equal(1, credentialResolutions)

            use redirectHandler =
                new CapturingHandler(fun _ ->
                    let response = new HttpResponseMessage(HttpStatusCode.TemporaryRedirect)
                    response.Headers.Location <- Uri("https://api.github.com/repos/FS-GG/.github/issues/1")
                    response)

            use redirectClient = new HttpClient(redirectHandler)

            let! redirected =
                RemoteClient.submitWithClientForTesting
                    redirectClient
                    loopbackConfig
                    fakeResolve
                    scope
                    submittedEnvelope
                    CancellationToken.None

            Assert.Equal(RemoteClient.Unacknowledged "redirect-refused", redirected)
            let refused = Assert.Single(redirectHandler.Requests)
            Assert.Equal(Uri("https://127.0.0.1:44443/v1/batches"), refused.Uri)

            use productionHandler = RemoteClient.createHandler ()
            Assert.False(productionHandler.AllowAutoRedirect)
        }

    [<Fact>]
    let ``receipt decoder is closed and validates digest and rejection code`` () =
        Assert.True(RemoteContract.parseReceipt (receipt "batch-a" "applied") |> Result.isOk)
        let original = Encoding.UTF8.GetString(receipt "batch-a" "applied")
        let extra = "{\"extra\":true," + original.Substring(1)
        Assert.True(RemoteContract.parseReceipt (Encoding.UTF8.GetBytes extra) |> Result.isError)

        let semantic =
            Encoding.UTF8
                .GetString(receipt "batch-a" "rejected")
                .Replace("\"code\":null", "\"code\":\"semantic-conflict\"")

        Assert.True(RemoteContract.parseReceipt (Encoding.UTF8.GetBytes semantic) |> Result.isOk)

    [<Fact>]
    let ``client config accepts only an HTTPS origin and bounded reference`` () =
        for uri in
            [
                "http://example.test/"
                "https://u:p@example.test/"
                "https://example.test/path"
                "https://example.test/?x=1"
            ] do
            Assert.True(
                RemoteContract.validateClientConfig { config with Endpoint = Uri uri }
                |> Result.isError
            )

        Assert.True(RemoteContract.validateClientConfig config |> Result.isOk)

    [<Fact>]
    let ``credential rotation keeps scope while revocation and cross-scope token reuse fail`` () =
        let token = String('z', 32)

        let hash =
            System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes token)

        let entry =
            {
                Principal = TelemetryReceipt.genericPrincipal scope
                TokenHash = hash
                Revoked = false
            }

        Assert.Equal(Some(TelemetryReceipt.genericPrincipal scope), Runtime.authenticate (Map.ofList [ "old", entry; "new", entry ]) token)
        Assert.Equal(None, Runtime.authenticate (Map.ofList [ "revoked", { entry with Revoked = true } ]) token)

    [<Fact>]
    let ``service lock is exclusive and stale path is reusable`` () =
        let path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".lock")

        try
            let first = Runtime.ServiceLock.Acquire(path) |> Result.defaultWith failwith

            match Runtime.ServiceLock.Acquire(path) with
            | Error _ -> ()
            | Ok second ->
                (second :> IDisposable).Dispose()
                Assert.Fail "second lock acquired"

            (first :> IDisposable).Dispose()
            use restarted = Runtime.ServiceLock.Acquire(path) |> Result.defaultWith failwith
            Assert.NotNull restarted
        finally
            if File.Exists path then
                File.Delete path

    [<Fact>]
    let ``host config rejects duplicate members before deserialization`` () =
        let path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".json")

        try
            File.WriteAllText(
                path,
                "{\"ListenUrl\":\"https://127.0.0.1:1\",\"ListenUrl\":\"https://127.0.0.1:2\",\"CertificatePath\":\"/x\",\"CertificatePasswordFile\":\"/y\",\"ServiceLockPath\":\"/z\",\"Stores\":[],\"Credentials\":[]}"
            )

            File.SetUnixFileMode(path, UnixFileMode.UserRead ||| UnixFileMode.UserWrite)
            Assert.True(Configuration.load path |> Result.isError)
        finally
            if File.Exists path then
                File.Delete path

    [<Fact>]
    let ``host config rejects multiple or non-origin listeners`` () =
        for listen in
            [
                "https://127.0.0.1:5001/;http://127.0.0.1:5002"
                "https://user@127.0.0.1:5001"
                "https://127.0.0.1:5001/path"
            ] do
            let candidate =
                {
                    Schema = "fsgg.telemetry.host-config/1"
                    ListenUrl = listen
                    CertificatePath = "/missing"
                    CertificatePasswordFile = "/missing"
                    ServiceLockPath = "/tmp/fsgg.lock"
                    Stores = [||]
                    Credentials = [||]
                    BrowserPrincipals = [||]
                    BrowserSession = browserSession
                }

            let errors =
                match Configuration.validate candidate with
                | Error values -> values
                | Ok _ -> failwith "invalid listener accepted"

            Assert.Contains("listenUrl must be one HTTPS origin", errors)

    [<Fact>]
    let ``host config rejects credential symlinks and public secret permissions`` () =
        let root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"))
        Directory.CreateDirectory root |> ignore

        try
            let secret = Path.Combine(root, "secret")
            let link = Path.Combine(root, "link")
            File.WriteAllText(secret, String('s', 32))
            File.SetUnixFileMode(secret, UnixFileMode.UserRead ||| UnixFileMode.GroupRead)
            File.CreateSymbolicLink(link, secret) |> ignore

            let candidate =
                {
                    Schema = "fsgg.telemetry.host-config/1"
                    ListenUrl = "https://127.0.0.1:1"
                    CertificatePath = secret
                    CertificatePasswordFile = secret
                    ServiceLockPath = Path.Combine(root, "lock")
                    Stores =
                        [|
                            {
                                WorkspaceId = "workspace-a"
                                Root = Path.Combine(root, "store")
                            }
                        |]
                    Credentials =
                        [|
                            {
                                Reference = "producer"
                                SecretFile = link
                                WorkspaceId = "workspace-a"
                                ProducerId = "producer-a"
                                StreamId = "runtime"
                                Role = null
                                GrantId = null
                                GrantGeneration = 0L
                                Revoked = false
                            }
                        |]
                    BrowserPrincipals = [||]
                    BrowserSession = browserSession
                }

            let errors =
                match Configuration.validate candidate with
                | Error values -> values
                | Ok _ -> failwith "invalid config accepted"

            Assert.Contains<string>(errors, fun e -> e.Contains("symbolic link"))
            Assert.Contains<string>(errors, fun e -> e.Contains("permissions"))
        finally
            Directory.Delete(root, true)

    [<Fact>]
    let ``host config v2 binds collector role and grant to the protected credential`` () =
        let root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"))
        Directory.CreateDirectory root |> ignore
        let privateFile name (content: string) =
            let path = Path.Combine(root, name)
            File.WriteAllText(path, content)
            File.SetUnixFileMode(path, UnixFileMode.UserRead ||| UnixFileMode.UserWrite)
            path
        try
            let secret = privateFile "secret" (String('s', 32))
            let secondSecret = privateFile "second-secret" (String('t', 32))
            let certificate = privateFile "certificate" "fixture"
            let password = privateFile "password" "fixture"
            let credential role grant generation =
                { Reference = role; SecretFile = secret; WorkspaceId = "workspace-a"; ProducerId = "producer-a"
                  StreamId = "native-inventory"; Role = role; GrantId = grant
                  GrantGeneration = generation; Revoked = false }
            let config credentials =
                { Schema = "fsgg.telemetry.host-config/2"; ListenUrl = "https://127.0.0.1:1"
                  CertificatePath = certificate; CertificatePasswordFile = password
                  ServiceLockPath = Path.Combine(root, "lock")
                  Stores = [| { WorkspaceId = "workspace-a"; Root = Path.Combine(root, "store") } |]
                  Credentials = credentials; BrowserPrincipals = [||]; BrowserSession = browserSession }

            let collector = credential "native-collector" "grant-a" 1L
            let valid = config [| collector |] |> Configuration.validate |> Result.defaultWith (String.concat "; " >> failwith)
            let configPath = privateFile "host.json" (JsonSerializer.Serialize(config [| collector |]))
            let loaded = Configuration.load configPath |> Result.defaultWith (String.concat "; " >> failwith)
            Assert.Equal("fsgg.telemetry.host-config/2", loaded.Schema)
            Assert.Equal("native-collector", loaded.Credentials[0].Role)
            let authenticated = Configuration.credentials valid |> fun entries -> Runtime.authenticate entries (String('s', 32))
            match authenticated with
            | Some principal ->
                Assert.Equal(TelemetryReceipt.NativeCollector, principal.Role)
                Assert.Equal(Some "grant-a", principal.GrantId)
                Assert.Equal(Some 1L, principal.GrantGeneration)
            | None -> Assert.Fail "protected collector credential did not authenticate"

            let alias = credential "generic" "grant-b" 2L
            let errors =
                match config [| collector; alias |] |> Configuration.validate with
                | Error values -> values
                | Ok _ -> failwith "alias accepted"
            Assert.Contains("credential secret is assigned to incompatible authority", errors)
            let distinctSecretAlias = { alias with SecretFile = secondSecret }
            let scopeErrors =
                match config [| collector; distinctSecretAlias |] |> Configuration.validate with
                | Error values -> values
                | Ok _ -> failwith "incompatible authority accepted for one credential scope"
            Assert.Contains("credential scope is assigned to incompatible authority", scopeErrors)
            let injected = { collector with Role = "administrator" }
            Assert.True(config [| injected |] |> Configuration.validate |> Result.isError)
            let revoked = { collector with Revoked = true }
            let revokedConfig = config [| revoked |] |> Configuration.validate |> Result.defaultWith (String.concat "; " >> failwith)
            Assert.True(Configuration.credentials revokedConfig |> fun entries -> Runtime.authenticate entries (String('s', 32)) |> Option.isNone)
        finally
            Directory.Delete(root, true)

    [<Fact>]
    let ``host exact retry refuses a different credential role or grant`` () =
        task {
            let root = Path.Combine(Path.GetTempPath(), "collector-replay-" + Guid.NewGuid().ToString("N"))

            try
                TelemetryStoreApplication.initialize root TelemetryStore.ApprovedLocalDurable
                |> Result.defaultWith (String.concat "; " >> failwith)
                |> ignore

                let collector: TelemetryReceipt.Principal =
                    { Scope = scope; Role = TelemetryReceipt.NativeCollector
                      GrantId = Some "collector-grant"; GrantGeneration = Some 1L }

                TelemetryStoreApplication.enrollReceiptPrincipal
                    root
                    TelemetryStore.ApprovedLocalDurable
                    collector
                |> Result.defaultWith (String.concat "; " >> failwith)
                |> ignore

                let bytes = envelope "principal-bound-replay"

                TelemetryStoreApplication.submitReceiptPrincipal
                    root
                    TelemetryStore.ApprovedLocalDurable
                    collector
                    bytes
                |> Result.defaultWith (String.concat "; " >> failwith)
                |> ignore

                let config =
                    { Schema = "fsgg.telemetry.host-config/2"; ListenUrl = "https://127.0.0.1:1"
                      CertificatePath = "/unused"; CertificatePasswordFile = "/unused"
                      ServiceLockPath = "/unused"; Stores = [| { WorkspaceId = scope.Workspace; Root = root } |]
                      Credentials = [||]; BrowserPrincipals = [||]; BrowserSession = browserSession }

                use state = new Runtime.HostState(config, fun _ -> TelemetryStore.ApprovedLocalDurable)
                let! accepted = Runtime.submitPrincipal state collector bytes CancellationToken.None
                Assert.Equal(200, accepted.Status)

                let! wrongRole =
                    Runtime.submitPrincipal
                        state
                        (TelemetryReceipt.genericPrincipal scope)
                        bytes
                        CancellationToken.None
                Assert.Equal(403, wrongRole.Status)
                Assert.Equal(Some "unauthorized-scope", RemoteContract.parseError wrongRole.Body)

                let! wrongGrant =
                    Runtime.submitPrincipal
                        state
                        { collector with GrantGeneration = Some 2L }
                        bytes
                        CancellationToken.None
                Assert.Equal(403, wrongGrant.Status)
                Assert.Equal(Some "unauthorized-scope", RemoteContract.parseError wrongGrant.Body)
            finally
                if Directory.Exists root then
                    Directory.Delete(root, true)
        }

    [<Theory>]
    [<InlineData(false)>]
    [<InlineData(true)>]
    let ``protected native collector resolves durable dispatch and replays retained envelope`` (qualified: bool) =
        let root = Path.Combine(Path.GetTempPath(), "native-collector-command-" + Guid.NewGuid().ToString("N"))
        Directory.CreateDirectory root |> ignore
        File.SetUnixFileMode(root, UnixFileMode.UserRead ||| UnixFileMode.UserWrite ||| UnixFileMode.UserExecute)

        let privateFile name (content: string) =
            let path = Path.Combine(root, name)
            File.WriteAllText(path, content)
            File.SetUnixFileMode(path, UnixFileMode.UserRead ||| UnixFileMode.UserWrite)
            path

        try
            let store = Path.Combine(root, "store")
            let codexHome = Path.Combine(root, "codex-home")
            let evidenceRoot = Path.Combine(root, "evidence")
            let sessions = Path.Combine(codexHome, "sessions")

            for directory in [ codexHome; evidenceRoot; sessions ] do
                Directory.CreateDirectory directory |> ignore
                File.SetUnixFileMode(directory, UnixFileMode.UserRead ||| UnixFileMode.UserWrite ||| UnixFileMode.UserExecute)

            let rollout = Path.Combine(sessions, "rollout.jsonl")
            File.WriteAllText(
                rollout,
                """{"type":"token_usage_record","payload":{"thread_id":"22222222-2222-2222-2222-222222222222","turn_id":"33333333-3333-3333-3333-333333333333","response_id":"r1","usage":{"input_tokens":5,"cached_input_tokens":1,"output_tokens":2,"reasoning_output_tokens":1,"total_tokens":7},"turn_token_usage":{"input_tokens":5,"cached_input_tokens":1,"output_tokens":2,"reasoning_output_tokens":1,"total_tokens":7}}}
{"type":"token_usage_record","payload":{"thread_id":"22222222-2222-2222-2222-222222222222","turn_id":"44444444-4444-4444-4444-444444444444","response_id":"r2","usage":{"input_tokens":3,"cached_input_tokens":0,"output_tokens":1,"reasoning_output_tokens":0,"total_tokens":4},"turn_token_usage":{"input_tokens":3,"cached_input_tokens":0,"output_tokens":1,"reasoning_output_tokens":0,"total_tokens":4}}}
"""
            )
            let counter = Path.Combine(root, "reader-count")
            let reader = Path.Combine(root, "protected-codex")
            let readerFixture =
                Path.GetFullPath(Path.Combine(__SOURCE_DIRECTORY__, "..", "skill-fsharp", "readers", "fixtures", "fake-codex.py"))
            let patchedFixture = Path.Combine(root, "fake-codex.py")
            File.ReadAllText(readerFixture)
                .Replace(
                    "    for line in sys.stdin:\n        request = json.loads(line)",
                    "    for line in sys.stdin:\n        if not line.strip():\n            continue\n        request = json.loads(line)"
                )
            |> fun content -> File.WriteAllText(patchedFixture, content)
            File.WriteAllText(
                reader,
                $"""#!/bin/sh
if [ "${{LEAK_ME+x}}" = x ]; then exit 9; fi
count=0
if [ -f "{counter}" ]; then count=$(/bin/cat "{counter}"); fi
count=$((count + 1))
/usr/bin/echo -n "$count" > "{counter}"
export SKILL_FS_01_ROLLOUT="{rollout}"
exec /usr/bin/python3 "{patchedFixture}" "$@"
"""
            )
            File.SetUnixFileMode(reader, UnixFileMode.UserRead ||| UnixFileMode.UserWrite ||| UnixFileMode.UserExecute)

            TelemetryStoreApplication.initialize store TelemetryStore.ApprovedLocalDurable
            |> Result.defaultWith (String.concat "; " >> failwith)
            |> ignore

            let collectorScope = { scope with Producer = "protected-collector"; Stream = "native-inventory" }
            let collector: TelemetryReceipt.Principal =
                { Scope = collectorScope; Role = TelemetryReceipt.NativeCollector
                  GrantId = Some "collector-grant"; GrantGeneration = Some 1L }
            TelemetryStoreApplication.provisionReceiptWorkspace store TelemetryStore.ApprovedLocalDurable scope.Workspace
            |> Result.defaultWith (String.concat "; " >> failwith) |> ignore
            TelemetryStoreApplication.enrollReceiptProducer store TelemetryStore.ApprovedLocalDurable scope
            |> Result.defaultWith (String.concat "; " >> failwith) |> ignore

            let events =
                """{"kind":"budget-population","identity":"population-child","itemId":"LEARN-child","revision":0,"originalItemId":"LEARN-root","state":"open","sourceKind":"native-item","sourceRef":"fixture:population"},
{"kind":"expected-dispatch","identity":"expected-child","itemId":"LEARN-child","revision":0,"dispatchId":"dispatch-child","activationId":"activation-root","relation":"child","parentDispatchId":"dispatch-root","runtime":"collaboration-spawn-agent","expectedAt":"2026-09-28T08:00:00Z","clockProvenance":"host-wall"},
{"kind":"invocation-lineage","identity":"lineage-child","itemId":"LEARN-child","revision":0,"dispatchId":"dispatch-child","invocationId":"invocation-child","relation":"child","parentInvocationId":"invocation-root","rootInvocationId":"invocation-root","runtime":"collaboration-spawn-agent"},
{"kind":"runtime-admission","identity":"admission-child","itemId":"LEARN-child","revision":0,"invocationId":"invocation-child","featureId":"LEARN-01","attemptId":"attempt-child","parentAttemptId":"attempt-root","producerStream":"roadmap","requestedModel":"fixture-model","requestedEffort":"medium","backend":"codex-collaboration"},
{"kind":"runtime-start","identity":"process-child","itemId":"LEARN-child","revision":0,"invocationId":"invocation-child","threadId":"child_1","turnId":null,"turnSequence":null,"processId":0,"phase":"process"},
{"kind":"runtime-terminal","identity":"terminal-child","itemId":"LEARN-child","revision":0,"invocationId":"invocation-child","threadId":"22222222-2222-2222-2222-222222222222","outcome":"completed","exitCode":0}"""
            let batch =
                Encoding.UTF8.GetBytes(
                    $"""{{"schema":"{TelemetryStore.BatchSchema}","ingestId":"collector-dispatch-fixture","sourceIdentity":"fixture","generation":"g1","cursor":"1","eventCount":6,"events":[{events}]}}"""
                )
            let dispatchEnvelope =
                Encoding.UTF8.GetBytes(
                    $"""{{"schema":"{TelemetryReceipt.Schema}","workspaceId":"{scope.Workspace}","producerId":"{scope.Producer}","streamId":"{scope.Stream}","batchId":"dispatch-fixture","payload":{Encoding.UTF8.GetString batch}}}"""
                )
            TelemetryStoreApplication.submitReceipt store TelemetryStore.ApprovedLocalDurable scope dispatchEnvelope
            |> Result.defaultWith (String.concat "; " >> failwith) |> ignore
            let drained =
                TelemetryStoreApplication.drainReceipts store TelemetryStore.ApprovedLocalDurable scope.Workspace
                |> Result.defaultWith (String.concat "; " >> failwith)
            Assert.Contains("\"rejected\":0", drained)
            let resolved =
                TelemetryStoreApplication.resolveNativeCollectorDispatch
                    store TelemetryStore.ApprovedLocalDurable "dispatch-child" "child_1"
                |> Result.defaultWith (String.concat "; " >> failwith)
            Assert.Equal("invocation-child", resolved.InvocationId)
            use runtimeConnection =
                new SqliteConnection($"Data Source={Path.Combine(store, TelemetryStoreApplication.databaseFileName)};Pooling=False")
            runtimeConnection.Open()
            let setRuntime value =
                for table in [ "expected_dispatches"; "invocation_lineage" ] do
                    use update = runtimeConnection.CreateCommand()
                    update.CommandText <- $"UPDATE {table} SET runtime=$runtime WHERE item_id='LEARN-child';"
                    update.Parameters.AddWithValue("$runtime", value) |> ignore
                    Assert.Equal(1, update.ExecuteNonQuery())
            setRuntime "codex-exec"
            match
                TelemetryStoreApplication.resolveNativeCollectorDispatch
                    store TelemetryStore.ApprovedLocalDurable "dispatch-child" "child_1"
            with
            | Ok _ -> Assert.Fail "unrelated durable runtime was accepted as a native collector dispatch"
            | Error errors -> Assert.Contains("native collector dispatch is unavailable", errors)
            setRuntime "collaboration-spawn-agent"
            runtimeConnection.Close()

            let secret = privateFile "secret" (String('s', 32))
            let certificate = privateFile "certificate" "fixture"
            let password = privateFile "password" "fixture"
            let config =
                { Schema = "fsgg.telemetry.host-config/2"; ListenUrl = "https://127.0.0.1:1"
                  CertificatePath = certificate; CertificatePasswordFile = password
                  ServiceLockPath = Path.Combine(root, "service.lock")
                  Stores = [| { WorkspaceId = scope.Workspace; Root = store } |]
                  Credentials =
                    [| { Reference = "collector"; SecretFile = secret; WorkspaceId = scope.Workspace
                         ProducerId = collectorScope.Producer; StreamId = collectorScope.Stream
                         Role = "native-collector"; GrantId = "collector-grant"; GrantGeneration = 1L; Revoked = false } |]
                  BrowserPrincipals = [||]; BrowserSession = browserSession }
            let configPath = privateFile "host.json" (JsonSerializer.Serialize config)
            let installation =
                { Schema = if qualified then "fsgg.telemetry.native-collector-installation/2" else "fsgg.telemetry.native-collector-installation/1"
                  CredentialReference = "collector"
                  ExecutablePath = reader; CodexHome = codexHome; EvidenceRoot = evidenceRoot
                  Provider = "openai"; Model = "fixture-model"; Effort = "medium"; NativeVerifier = None }
            let installationJson value =
                let node = JsonSerializer.SerializeToNode(value).AsObject()
                node.Remove "NativeVerifier" |> ignore
                if qualified then
                    node["ExecutableSha256"] <- System.Text.Json.Nodes.JsonValue.Create(
                        Convert.ToHexString(SHA256.HashData(File.ReadAllBytes reader)).ToLowerInvariant())
                match value.NativeVerifier with
                | Some verifier -> node["NativeVerifier"] <- JsonSerializer.SerializeToNode verifier
                | None -> ()
                node.ToJsonString()
            let installationPath =
                privateFile "host.json.native-collector.json" (installationJson installation)

            let command =
                [| "collect-native"; "--config"; configPath; "--dispatch"; "dispatch-child"
                   "--parent-thread"; "11111111-1111-1111-1111-111111111111"; "--native-agent"; "child_1" |]
            let previous = Environment.GetEnvironmentVariable "LEAK_ME"
            try
                Environment.SetEnvironmentVariable("LEAK_ME", "must-not-cross")
                let unrelated =
                    [| "collect-native"; "--config"; configPath; "--dispatch"; "dispatch-foreign"
                       "--parent-thread"; "11111111-1111-1111-1111-111111111111"; "--native-agent"; "foreign_1" |]
                Assert.Equal(3, Operations.runWithAssessment unrelated (fun _ -> TelemetryStore.ApprovedLocalDurable))
                Assert.False(File.Exists counter)
                File.WriteAllText(configPath, JsonSerializer.Serialize { config with Schema = "fsgg.telemetry.host-config/1" })
                Assert.Equal(2, Operations.runWithAssessment command (fun _ -> TelemetryStore.ApprovedLocalDurable))
                Assert.False(File.Exists counter)
                File.WriteAllText(configPath, JsonSerializer.Serialize config)
                let mismatchedInstallation = { installation with Model = "wrong-model" }
                File.WriteAllText(installationPath, installationJson mismatchedInstallation)
                Assert.Equal(3, Operations.runWithAssessment command (fun _ -> TelemetryStore.ApprovedLocalDurable))
                Assert.False(File.Exists counter)
                File.WriteAllText(installationPath, installationJson installation)
                if qualified then
                    let wrongPin = System.Text.Json.Nodes.JsonNode.Parse(installationJson installation)
                    wrongPin["ExecutableSha256"] <- System.Text.Json.Nodes.JsonValue.Create(String('0', 64))
                    File.WriteAllText(installationPath, wrongPin.ToJsonString())
                    Assert.Equal(3, Operations.runWithAssessment command (fun _ -> TelemetryStore.ApprovedLocalDurable))
                    Assert.False(File.Exists counter)
                    File.WriteAllText(installationPath, installationJson installation)
                Assert.Equal(3, Operations.runWithAssessment command (fun _ -> TelemetryStore.ApprovedLocalDurable))
                Assert.Equal("1", File.ReadAllText counter)
                TelemetryStoreApplication.enrollReceiptPrincipal store TelemetryStore.ApprovedLocalDurable collector
                |> Result.defaultWith (String.concat "; " >> failwith) |> ignore
                Assert.Equal(0, Operations.runWithAssessment command (fun _ -> TelemetryStore.ApprovedLocalDurable))
                Assert.Equal("1", File.ReadAllText counter)
                Assert.Equal(0, Operations.runWithAssessment command (fun _ -> TelemetryStore.ApprovedLocalDurable))
                Assert.Equal("1", File.ReadAllText counter)
                let retained = Directory.GetFiles evidenceRoot
                Assert.Single retained |> ignore
                Assert.Equal(UnixFileMode.UserRead ||| UnixFileMode.UserWrite, File.GetUnixFileMode retained[0])
                let exportCommand = [| "export-learning"; "--config"; configPath |]
                let originalOutput = Console.Out
                use output = new StringWriter()
                try
                    Console.SetOut output
                    let code = Operations.runWithAssessment exportCommand (fun _ -> TelemetryStore.ApprovedLocalDurable)
                    Assert.Equal((if qualified then 0 else 3), code)
                finally
                    Console.SetOut originalOutput
                if qualified then
                    use exported = JsonDocument.Parse(output.ToString())
                    Assert.Equal("fsgg.telemetry.protected-learning-export/1", exported.RootElement.GetProperty("schema").GetString())
                    let captures = exported.RootElement.GetProperty("captures").EnumerateArray() |> Seq.toArray
                    Assert.Single captures |> ignore
                    let turns = captures[0].GetProperty("turns").EnumerateArray() |> Seq.toArray
                    Assert.Equal(2, turns.Length)
                    Assert.Equal(11L, turns |> Array.sumBy (fun turn -> turn.GetProperty("total").GetInt64()))
                    Assert.DoesNotContain(secret, output.ToString())
                    Assert.DoesNotContain(String('s', 32), output.ToString())
                    let retainedFile = Assert.Single(Directory.GetFiles(evidenceRoot, "*.capture.json"))
                    let retainedBytes = File.ReadAllBytes retainedFile
                    use retainedDocument = JsonDocument.Parse retainedBytes
                    Assert.Equal("fsgg.telemetry.protected-native-capture/2",
                                 retainedDocument.RootElement.GetProperty("schema").GetString())
                    Assert.NotEmpty(retainedDocument.RootElement.GetProperty("appServerResponses").EnumerateArray())
                    Assert.Equal(2, retainedDocument.RootElement.GetProperty("rolloutRecords").GetArrayLength())

                    let tamperedSource = JsonNode.Parse(retainedBytes).AsObject()
                    let firstRollout = (tamperedSource["rolloutRecords"].AsArray()[0]).AsObject()
                    let replacement = Encoding.UTF8.GetBytes("{}\n")
                    firstRollout["bytesBase64"] <- JsonValue.Create(Convert.ToBase64String replacement)
                    firstRollout["sha256"] <- JsonValue.Create(
                        Convert.ToHexString(SHA256.HashData replacement).ToLowerInvariant())
                    File.WriteAllText(retainedFile, tamperedSource.ToJsonString())
                    Assert.Equal(3, Operations.runWithAssessment exportCommand (fun _ -> TelemetryStore.ApprovedLocalDurable))
                    File.WriteAllBytes(retainedFile, retainedBytes)

                    let tamperedTurn = JsonNode.Parse(retainedBytes).AsObject()
                    let firstTurn = (tamperedTurn["turns"].AsArray()[0]).AsObject()
                    firstTurn["total"] <- JsonValue.Create(firstTurn["total"].GetValue<int64>() + 1L)
                    File.WriteAllText(retainedFile, tamperedTurn.ToJsonString())
                    Assert.Equal(3, Operations.runWithAssessment exportCommand (fun _ -> TelemetryStore.ApprovedLocalDurable))
                    File.WriteAllBytes(retainedFile, retainedBytes)
                    Assert.Equal(3, Operations.runWithAssessment
                                        [| "collect-installed-origin"; "--config"; configPath |]
                                        (fun _ -> TelemetryStore.ApprovedLocalDurable))

                    let hashBytes (bytes: byte array) =
                        Convert.ToHexString(SHA256.HashData bytes).ToLowerInvariant()
                    let writeEvidence (name: string) (bytes: byte array) =
                        let target = Path.Combine(evidenceRoot, name)
                        File.WriteAllBytes(target, bytes)
                        File.SetUnixFileMode(target, UnixFileMode.UserRead ||| UnixFileMode.UserWrite)
                        target
                    let serialize (value: 'value) =
                        JsonSerializer.SerializeToUtf8Bytes<'value> value
                    let fixedNow = DateTimeOffset.Parse "2026-10-01T10:01:00.0000000+00:00"
                    let observed = fixedNow.AddMinutes(-1.).ToString("O")
                    let started = fixedNow.AddMinutes(-1.).AddSeconds(-2.).ToString("O")
                    let completed = fixedNow.AddMinutes(-1.).AddSeconds(1.).ToString("O")
                    let profileExpires = fixedNow.AddMinutes(2.).ToString("O")
                    let evidenceExpires = fixedNow.AddMinutes(5.).ToString("O")
                    let executableSha = hashBytes(File.ReadAllBytes reader)
                    let profileBytes =
                        serialize
                            {| schema = "fsgg.orchestration.host-fixed-native-capability/1"
                               operation = "codex-native-capability/1"; revision = "fixture-profile-1"
                               hostExecutableSha256 = executableSha; providerExecutable = reader
                               providerExecutableSha256 = executableSha; expectedAdapterVersion = "fixture-adapter/1"
                               expectedCodexVersion = "fixture-version"; environmentAllowList = [| "PATH" |]
                               credentialScope = "fixture-read-only"; maximumRuntimeSeconds = 10
                               maximumStreamBytes = 65536; expiresAt = profileExpires
                               disposableWorkspace = Path.Combine(root, "disposable")
                               cleanup = "delete-owned-workspace/1" |}
                    let profileSha = hashBytes profileBytes
                    writeEvidence "fixed-native-capability-profile.json" profileBytes |> ignore
                    let cleanup =
                        {| processTreeTerminationRequired = true; processTreeTerminated = true
                           workspaceRemovalAttempted = true; workspaceRemoved = true |}
                    let resultBytes =
                        serialize
                            {| schema = "fsgg.orchestration.host-fixed-native-capability-result/1"
                               operation = "codex-native-capability/1"; profileRevision = "fixture-profile-1"
                               profileSha256 = profileSha; hostExecutableSha256 = executableSha
                               providerExecutableSha256 = executableSha; adapterVersion = "fixture-adapter/1"
                               credentialScope = "fixture-read-only"; environmentAllowList = [| "PATH" |]
                               maximumRuntimeSeconds = 10; maximumStreamBytes = 65536
                               requestedModel = "gpt-test"; requestedEffort = installation.Effort
                               startedAt = started; completedAt = completed; disposition = "advertised-supported"
                               detail = "requested-selection-advertised"; authenticationState = "authenticated"
                               authenticationProvenance = "installed-account"; evidenceSchema = "fsgg.learning-selection-evidence/1"
                               evidenceProvenance = "fixture"; evidenceObservedAt = observed; evidenceExpiresAt = evidenceExpires
                               modelSessionStarts = 0; cleanup = cleanup |}
                    writeEvidence "fixed-native-capability-result.json" resultBytes |> ignore
                    let fixtureRoot = Path.Combine(__SOURCE_DIRECTORY__, "Fixtures", "NativeSource")
                    let captureBytes = File.ReadAllBytes(Path.Combine(fixtureRoot, "capture.json"))
                    writeEvidence "native-source-capture.json" captureBytes |> ignore
                    let snapshotBytes = File.ReadAllBytes(Path.Combine(fixtureRoot, "snapshot.json"))
                    let snapshotPath = writeEvidence "native-source-snapshot.json" snapshotBytes
                    let verificationBytes = File.ReadAllBytes(Path.Combine(fixtureRoot, "verification.json"))
                    let verificationPath = writeEvidence "native-source-verification.json" verificationBytes
                    let runtime = FileInfo("/usr/bin/python3").ResolveLinkTarget(true).FullName
                    let modulePath = Path.GetFullPath(Path.Combine(__SOURCE_DIRECTORY__, "../../tools/learn_01_native_source.py"))
                    let runtimeSha = hashBytes(File.ReadAllBytes runtime)
                    let moduleSha = hashBytes(File.ReadAllBytes modulePath)
                    let manifestBytes =
                        [| runtime, FileInfo(runtime).Length, runtimeSha; modulePath, FileInfo(modulePath).Length, moduleSha |]
                        |> Array.sortBy (fun (path, _, _) -> path)
                        |> Array.map (fun (path, length, digest) -> {| path = path; bytes = length; sha256 = digest |})
                        |> fun files ->
                            serialize
                                {| schema = "fsgg.telemetry.native-verifier-runtime/1"
                                   sourceRevision = String('1', 40)
                                   runtimeImageDigest = "sha256:" + hashBytes(Array.append (File.ReadAllBytes runtime) (File.ReadAllBytes modulePath))
                                   runtimeExecutablePath = runtime; modulePath = modulePath; files = files |}
                    let manifestPath = privateFile "native-verifier-runtime.json" (Encoding.UTF8.GetString manifestBytes)
                    let manifestSha = hashBytes manifestBytes
                    let verifier =
                        { RuntimeExecutablePath = runtime; RuntimeExecutableSha256 = runtimeSha
                          ModulePath = modulePath; ModuleSha256 = moduleSha
                          RuntimeManifestPath = manifestPath; RuntimeManifestSha256 = manifestSha }
                    let verifiedInstallation =
                        { installation with Schema = "fsgg.telemetry.native-collector-installation/3"
                                            Model = "gpt-test"; NativeVerifier = Some verifier }
                    File.WriteAllText(installationPath, installationJson verifiedInstallation)
                    let sourceReferencePath =
                        privateFile
                            "source-reference.json"
                            (JsonSerializer.Serialize
                                {| schema = "fsgg.telemetry.persistent-source-references/3"; profileSha256 = profileSha
                                   nativeSourceVolume = "fixture"; developmentTarget = "/fixture"
                                   collectorReadOnlyTarget = codexHome; readerProfileSha256 = profileSha
                                   captureQualified = false; verifierRuntimeManifestSha256 = manifestSha |})
                    let sidecarBytes = File.ReadAllBytes installationPath
                    privateFile
                        "host.json.native-collector.receipt.json"
                        (JsonSerializer.Serialize
                            {| schema = "fsgg.telemetry.native-collector-installation-receipt/3"; status = "installed"
                               ownerUid = 0; hostConfigSha256 = hashBytes(File.ReadAllBytes configPath)
                               sidecarSha256 = hashBytes sidecarBytes; executableSha256 = executableSha
                               credentialReference = installation.CredentialReference; workspaceId = collectorScope.Workspace
                               producerId = collectorScope.Producer; streamId = collectorScope.Stream
                               grantId = collector.GrantId.Value; grantGeneration = collector.GrantGeneration.Value
                               sourceVerification = "unknown"; snapshotOrigin = "unknown"
                               sharedCostCompleteness = "unknown"; activationAuthorized = false
                               sourceReferenceSha256 = hashBytes(File.ReadAllBytes sourceReferencePath)
                               verifierRuntimeManifestSha256 = manifestSha |})
                    |> ignore
                    let installedSidecarBytes = File.ReadAllBytes installationPath
                    let assertInstallationUnavailable (mutate: JsonObject -> unit) =
                        let candidate = JsonNode.Parse(installedSidecarBytes).AsObject()
                        mutate candidate
                        File.WriteAllText(installationPath, candidate.ToJsonString())
                        Assert.True(Configuration.loadNativeCollectorInstallation configPath config |> Result.isError)
                        File.WriteAllBytes(installationPath, installedSidecarBytes)
                    assertInstallationUnavailable (fun candidate ->
                        candidate["NativeVerifier"]["RuntimeExecutablePath"] <-
                            JsonValue.Create(Path.Combine(root, "missing-runtime")))
                    assertInstallationUnavailable (fun candidate ->
                        candidate["NativeVerifier"]["RuntimeManifestPath"] <-
                            JsonValue.Create(Path.Combine(root, "missing-manifest")))
                    assertInstallationUnavailable (fun candidate ->
                        candidate["NativeVerifier"]["ModuleSha256"] <- JsonValue.Create(String('0', 64)))
                    let captureOutput instant (argv: string array) =
                        let current = Console.Out
                        use writer = new StringWriter()
                        try
                            Console.SetOut writer
                            Assert.Equal(0, Operations.runWithDependenciesAt instant argv
                                                (fun _ -> TelemetryStore.ApprovedLocalDurable)
                                                (fun _ -> failwith "unexpected native delivery transport"))
                            writer.ToString()
                        finally Console.SetOut current
                    let collectAt instant =
                        Operations.runWithDependenciesAt instant
                            [| "collect-installed-origin"; "--config"; configPath |]
                            (fun _ -> TelemetryStore.ApprovedLocalDurable)
                            (fun _ -> failwith "unexpected native delivery transport")
                    let originalProfileBytes = File.ReadAllBytes(Path.Combine(evidenceRoot, "fixed-native-capability-profile.json"))
                    let originalResultBytes = File.ReadAllBytes(Path.Combine(evidenceRoot, "fixed-native-capability-result.json"))
                    let originalSourceReferenceBytes = File.ReadAllBytes sourceReferencePath
                    let managerReceiptPath = configPath + ".native-collector.receipt.json"
                    let originalManagerReceiptBytes = File.ReadAllBytes managerReceiptPath
                    let restoreTemporalFixture () =
                        File.WriteAllBytes(Path.Combine(evidenceRoot, "fixed-native-capability-profile.json"), originalProfileBytes)
                        File.WriteAllBytes(Path.Combine(evidenceRoot, "fixed-native-capability-result.json"), originalResultBytes)
                        File.WriteAllBytes(sourceReferencePath, originalSourceReferenceBytes)
                        File.WriteAllBytes(managerReceiptPath, originalManagerReceiptBytes)
                    let mutateResult (mutate: JsonObject -> unit) =
                        let candidate = JsonNode.Parse(originalResultBytes).AsObject()
                        mutate candidate
                        File.WriteAllText(Path.Combine(evidenceRoot, "fixed-native-capability-result.json"), candidate.ToJsonString())
                    mutateResult (fun candidate ->
                        candidate["startedAt"] <- JsonValue.Create((DateTimeOffset.Parse observed).AddSeconds(2.).ToString("O"))
                        candidate["completedAt"] <- JsonValue.Create((DateTimeOffset.Parse observed).AddSeconds(3.).ToString("O")))
                    Assert.Equal(3, collectAt fixedNow)
                    restoreTemporalFixture ()
                    mutateResult (fun candidate ->
                        candidate["completedAt"] <- JsonValue.Create(fixedNow.AddSeconds(1.).ToString("O")))
                    Assert.Equal(3, collectAt fixedNow)
                    restoreTemporalFixture ()
                    let expiredProfile = JsonNode.Parse(originalProfileBytes).AsObject()
                    expiredProfile["expiresAt"] <- JsonValue.Create(fixedNow.AddTicks(-1L).ToString("O"))
                    let expiredProfileBytes = Encoding.UTF8.GetBytes(expiredProfile.ToJsonString())
                    File.WriteAllBytes(Path.Combine(evidenceRoot, "fixed-native-capability-profile.json"), expiredProfileBytes)
                    let expiredProfileSha = hashBytes expiredProfileBytes
                    let profileBoundResult = JsonNode.Parse(originalResultBytes).AsObject()
                    profileBoundResult["profileSha256"] <- JsonValue.Create expiredProfileSha
                    File.WriteAllText(Path.Combine(evidenceRoot, "fixed-native-capability-result.json"), profileBoundResult.ToJsonString())
                    let profileBoundSource = JsonNode.Parse(originalSourceReferenceBytes).AsObject()
                    profileBoundSource["readerProfileSha256"] <- JsonValue.Create expiredProfileSha
                    let profileBoundSourceBytes = Encoding.UTF8.GetBytes(profileBoundSource.ToJsonString())
                    File.WriteAllBytes(sourceReferencePath, profileBoundSourceBytes)
                    let profileBoundManager = JsonNode.Parse(originalManagerReceiptBytes).AsObject()
                    profileBoundManager["sourceReferenceSha256"] <- JsonValue.Create(hashBytes profileBoundSourceBytes)
                    File.WriteAllText(managerReceiptPath, profileBoundManager.ToJsonString())
                    Assert.Equal(3, collectAt fixedNow)
                    restoreTemporalFixture ()
                    mutateResult (fun candidate ->
                        candidate["evidenceExpiresAt"] <- JsonValue.Create(fixedNow.AddTicks(-1L).ToString("O")))
                    Assert.Equal(3, collectAt fixedNow)
                    restoreTemporalFixture ()
                    use installed =
                        JsonDocument.Parse(captureOutput fixedNow [| "collect-installed-origin"; "--config"; configPath |])
                    Assert.Equal("fsgg.learn.installed-producer-receipt/1",
                                 installed.RootElement.GetProperty("schema").GetString())
                    use reread =
                        JsonDocument.Parse(captureOutput fixedNow [| "read-installed-origin"; "--config"; configPath |])
                    Assert.Equal(
                        installed.RootElement.GetProperty("source").GetProperty("recordId").GetString(),
                        reread.RootElement.GetProperty("source").GetProperty("recordId").GetString())
                    let readAt instant =
                        Operations.runWithDependenciesAt instant
                            [| "read-installed-origin"; "--config"; configPath |]
                            (fun _ -> TelemetryStore.ApprovedLocalDurable)
                            (fun _ -> failwith "unexpected native delivery transport")
                    Assert.Equal(3, readAt (DateTimeOffset.Parse evidenceExpires))
                    Assert.Equal(3, readAt ((DateTimeOffset.Parse evidenceExpires).AddTicks(1L)))
                    File.Move(snapshotPath, snapshotPath + ".held")
                    Assert.Equal(3, readAt fixedNow)
                    File.Move(snapshotPath + ".held", snapshotPath)
                    let changedCapture = JsonNode.Parse(captureBytes).AsObject()
                    let changedProjection = changedCapture["projection"].AsObject()
                    let changedThreads = changedProjection["threads"].AsArray()
                    let changedThread = changedThreads[0].AsObject()
                    changedThread["provider"] <- JsonValue.Create "forged-provider"
                    changedCapture.Remove "captureDigest" |> ignore
                    let changedDigest =
                        CanonicalJson.canonicalize(Encoding.UTF8.GetBytes(changedCapture.ToJsonString()))
                        |> Result.map Encoding.UTF8.GetBytes
                        |> Result.map hashBytes
                        |> Result.defaultWith failwith
                    changedCapture["captureDigest"] <- JsonValue.Create changedDigest
                    let plausibleVerification = JsonNode.Parse(verificationBytes).AsObject()
                    plausibleVerification["captureDigest"] <- JsonValue.Create changedDigest
                    File.WriteAllText(Path.Combine(evidenceRoot, "native-source-capture.json"), changedCapture.ToJsonString())
                    File.WriteAllText(verificationPath, plausibleVerification.ToJsonString())
                    Assert.Equal(3, readAt fixedNow)
                    File.WriteAllBytes(Path.Combine(evidenceRoot, "native-source-capture.json"), captureBytes)
                    File.WriteAllBytes(verificationPath, verificationBytes)
                    let changedVerification = JsonNode.Parse(verificationBytes).AsObject()
                    changedVerification["status"] <- JsonValue.Create "unverified"
                    File.WriteAllText(verificationPath, changedVerification.ToJsonString())
                    Assert.Equal(3, readAt fixedNow)
                    // Changing the operator pin cannot qualify already retained evidence.
                    File.AppendAllText(reader, "\n# changed executable\n")
                    Assert.Equal(3, Operations.runWithAssessment exportCommand (fun _ -> TelemetryStore.ApprovedLocalDurable))
            finally
                Environment.SetEnvironmentVariable("LEAK_ME", previous)

            let snapshot =
                TelemetryStoreApplication.dashboardSnapshot store TelemetryStore.ApprovedLocalDurable (Some "LEARN-child")
                |> Result.defaultWith (String.concat "; " >> failwith)
            Assert.DoesNotContain(String('s', 32), snapshot)
            use envelope = JsonDocument.Parse snapshot
            use compressed = new MemoryStream(Convert.FromBase64String(envelope.RootElement.GetProperty("canonicalSnapshotGzip").GetString()))
            use gzip = new GZipStream(compressed, CompressionMode.Decompress)
            use canonical = JsonDocument.Parse gzip
            let observations = canonical.RootElement.GetProperty("learningObservations").EnumerateArray() |> Seq.toArray
            Assert.Equal(2, observations.Length)
            Assert.All(observations, fun row -> Assert.Equal("native-collector", row.GetProperty("receipt_role").GetString()))
            Assert.All(observations, fun row -> Assert.Equal("collector-grant", row.GetProperty("receipt_grant_id").GetString()))
        finally
            if Directory.Exists root then Directory.Delete(root, true)

    [<Fact>]
    let ``native source verifier bounds processes and removes private copies`` () =
        let root = Path.Combine(Path.GetTempPath(), "native-source-verifier-" + Guid.NewGuid().ToString("N"))
        Directory.CreateDirectory root |> ignore
        File.SetUnixFileMode(root, UnixFileMode.UserRead ||| UnixFileMode.UserWrite ||| UnixFileMode.UserExecute)
        try
            let fixtureRoot = Path.Combine(__SOURCE_DIRECTORY__, "Fixtures", "NativeSource")
            let capture = File.ReadAllBytes(Path.Combine(fixtureRoot, "capture.json"))
            let snapshot = File.ReadAllBytes(Path.Combine(fixtureRoot, "snapshot.json"))
            let verification = File.ReadAllBytes(Path.Combine(fixtureRoot, "verification.json"))
            let hash (bytes: byte array) = Convert.ToHexString(SHA256.HashData bytes).ToLowerInvariant()
            let privateFile name (bytes: byte array) executable =
                let path = Path.Combine(root, name)
                File.WriteAllBytes(path, bytes)
                File.SetUnixFileMode(path,
                    UnixFileMode.UserRead ||| UnixFileMode.UserWrite |||
                    (if executable then UnixFileMode.UserExecute else enum 0))
                path
            let modulePath = Path.GetFullPath(Path.Combine(__SOURCE_DIRECTORY__, "../../tools/learn_01_native_source.py"))
            let limits: NativeSourceVerification.Limits =
                { Timeout = TimeSpan.FromMilliseconds 200.; StdoutBytes = 1024; StderrBytes = 1024 }
            let run index body =
                let runtime = privateFile ($"runtime-{index}.sh") (Encoding.UTF8.GetBytes("#!/bin/sh\n" + body + "\n")) true
                let runtimeBytes, moduleBytes = File.ReadAllBytes runtime, File.ReadAllBytes modulePath
                let entries =
                    [| runtime, runtimeBytes; modulePath, moduleBytes |]
                    |> Array.sortBy fst
                    |> Array.map (fun (path, bytes) -> {| path = path; bytes = int64 bytes.Length; sha256 = hash bytes |})
                let manifestBytes =
                    JsonSerializer.SerializeToUtf8Bytes
                        {| schema = "fsgg.telemetry.native-verifier-runtime/1"; sourceRevision = String('2', 40)
                           runtimeImageDigest = "sha256:" + hash(Array.append runtimeBytes moduleBytes)
                           runtimeExecutablePath = runtime; modulePath = modulePath; files = entries |}
                let manifest = privateFile ($"manifest-{index}.json") manifestBytes false
                let verifier =
                    { RuntimeExecutablePath = runtime; RuntimeExecutableSha256 = hash runtimeBytes
                      ModulePath = modulePath; ModuleSha256 = hash moduleBytes
                      RuntimeManifestPath = manifest; RuntimeManifestSha256 = hash manifestBytes }
                Assert.Equal(
                    Error [ "native source verification unavailable" ],
                    NativeSourceVerification.verifyRetainedWithLimits limits verifier root capture snapshot verification)
                Assert.Empty(Directory.GetDirectories(root, ".native-source-verification-*"))
            run 1 "exit 7"
            run 2 "/usr/bin/head -c 2048 /dev/zero"
            run 3 "/usr/bin/head -c 2048 /dev/zero >&2; exit 7"
            run 4 "printf '{}{}'"
            let started = Stopwatch.StartNew()
            run 5 "/usr/bin/sleep 5"
            Assert.True(started.Elapsed < TimeSpan.FromSeconds 2.)
        finally
            if Directory.Exists root then Directory.Delete(root, true)

    [<Fact>]
    let ``native delivery command retains one primary read and exports only by explicit negotiation`` () =
        let root = Path.Combine(Path.GetTempPath(), "native-delivery-command-" + Guid.NewGuid().ToString("N"))
        Directory.CreateDirectory root |> ignore
        File.SetUnixFileMode(root, UnixFileMode.UserRead ||| UnixFileMode.UserWrite ||| UnixFileMode.UserExecute)
        let privateFile name (content: string) mode =
            let path = Path.Combine(root, name)
            File.WriteAllText(path, content)
            File.SetUnixFileMode(path, mode)
            path
        let privateDirectory name =
            let path = Path.Combine(root, name)
            Directory.CreateDirectory path |> ignore
            File.SetUnixFileMode(path, UnixFileMode.UserRead ||| UnixFileMode.UserWrite ||| UnixFileMode.UserExecute)
            path
        try
            let store = Path.Combine(root, "store")
            let codexHome = privateDirectory "codex-home"
            let evidence = privateDirectory "evidence"
            let executable = privateFile "collector" "#!/bin/sh\nexit 0\n" (UnixFileMode.UserRead ||| UnixFileMode.UserWrite ||| UnixFileMode.UserExecute)
            let secret = privateFile "secret" (String('s', 32)) (UnixFileMode.UserRead ||| UnixFileMode.UserWrite)
            let githubToken = privateFile "github-token" "fixture-token" (UnixFileMode.UserRead ||| UnixFileMode.UserWrite)
            let certificate = privateFile "certificate" "fixture" (UnixFileMode.UserRead ||| UnixFileMode.UserWrite)
            let password = privateFile "password" "fixture" (UnixFileMode.UserRead ||| UnixFileMode.UserWrite)
            TelemetryStoreApplication.initialize store TelemetryStore.ApprovedLocalDurable
            |> Result.defaultWith (String.concat "; " >> failwith) |> ignore
            TelemetryStoreApplication.provisionReceiptWorkspace store TelemetryStore.ApprovedLocalDurable scope.Workspace
            |> Result.defaultWith (String.concat "; " >> failwith) |> ignore
            TelemetryStoreApplication.enrollReceiptProducer store TelemetryStore.ApprovedLocalDurable scope
            |> Result.defaultWith (String.concat "; " >> failwith) |> ignore
            let sourceRef = "routine-delivery:delivery-item"
            let candidateBatch =
                Encoding.UTF8.GetBytes
                    $"""{{"schema":"{TelemetryStore.BatchSchema}","ingestId":"delivery-candidate","sourceIdentity":"routine-delivery","generation":"g1","cursor":"1","eventCount":1,"events":[{{"kind":"native-item-outcome","identity":"native-delivery-candidate","itemId":"delivery-item","revision":1,"repository":"FS-GG/.github","prNumber":7,"baseRef":"main","baseSha":"dddddddddddddddddddddddddddddddddddddddd","head":"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa","outcome":"delivered","codeDelivery":"delivered","mergeCommit":"bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb","occurredAt":"2026-09-29T08:00:00Z","observedAt":"2026-09-29T08:00:01Z","sourceKind":"routine-delivery","sourceRef":"{sourceRef}"}}]}}"""
            let candidateEnvelope =
                Encoding.UTF8.GetBytes
                    $"""{{"schema":"{TelemetryReceipt.Schema}","workspaceId":"{scope.Workspace}","producerId":"{scope.Producer}","streamId":"{scope.Stream}","batchId":"delivery-candidate","payload":{Encoding.UTF8.GetString candidateBatch}}}"""
            TelemetryStoreApplication.submitReceipt store TelemetryStore.ApprovedLocalDurable scope candidateEnvelope
            |> Result.defaultWith (String.concat "; " >> failwith) |> ignore
            TelemetryStoreApplication.drainReceipts store TelemetryStore.ApprovedLocalDurable scope.Workspace
            |> Result.defaultWith (String.concat "; " >> failwith) |> ignore
            let collectorScope = { scope with Producer = "delivery-collector"; Stream = "native-delivery" }
            let collector: TelemetryReceipt.Principal =
                { Scope = collectorScope; Role = TelemetryReceipt.NativeCollector
                  GrantId = Some "collector-grant"; GrantGeneration = Some 1L }
            TelemetryStoreApplication.enrollReceiptPrincipal store TelemetryStore.ApprovedLocalDurable collector
            |> Result.defaultWith (String.concat "; " >> failwith) |> ignore
            let config =
                { Schema = "fsgg.telemetry.host-config/2"; ListenUrl = "https://127.0.0.1:1"
                  CertificatePath = certificate; CertificatePasswordFile = password
                  ServiceLockPath = Path.Combine(root, "service.lock")
                  Stores = [| { WorkspaceId = scope.Workspace; Root = store } |]
                  Credentials =
                    [| { Reference = "collector"; SecretFile = secret; WorkspaceId = scope.Workspace
                         ProducerId = collectorScope.Producer; StreamId = collectorScope.Stream
                         Role = "native-collector"; GrantId = "collector-grant"; GrantGeneration = 1L; Revoked = false } |]
                  BrowserPrincipals = [||]; BrowserSession = browserSession }
            let configPath = privateFile "host.json" (JsonSerializer.Serialize config) (UnixFileMode.UserRead ||| UnixFileMode.UserWrite)
            let installation =
                { Schema = "fsgg.telemetry.native-collector-installation/2"; CredentialReference = "collector"
                  ExecutablePath = executable; CodexHome = codexHome; EvidenceRoot = evidence
                  Provider = "openai"; Model = "fixture-model"; Effort = "medium"; NativeVerifier = None }
            let installNode = JsonSerializer.SerializeToNode(installation).AsObject()
            installNode.Remove "NativeVerifier" |> ignore
            installNode["ExecutableSha256"] <- System.Text.Json.Nodes.JsonValue.Create(
                Convert.ToHexString(SHA256.HashData(File.ReadAllBytes executable)).ToLowerInvariant())
            privateFile "host.json.native-collector.json" (installNode.ToJsonString()) (UnixFileMode.UserRead ||| UnixFileMode.UserWrite) |> ignore
            let sourceInstallation =
                { Schema = "fsgg.telemetry.native-delivery-source-installation/1"
                  CredentialReference = "collector"; GitHubCredentialFile = githubToken
                  AllowedRepositories = [| "FS-GG/.github" |] }
            let sourceInstallationPath =
                privateFile "host.json.native-delivery-source.json" (JsonSerializer.Serialize sourceInstallation)
                    (UnixFileMode.UserRead ||| UnixFileMode.UserWrite)
            let response: Transport.Response =
                { Status = 200
                  Body = """{"number":7,"state":"closed","merged":true,"head":{"sha":"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa"},"base":{"ref":"main","sha":"dddddddddddddddddddddddddddddddddddddddd","repo":{"full_name":"FS-GG/.github"}},"merge_commit_sha":"bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb","merged_at":"2026-09-29T08:00:00Z"}"""
                  Headers = Map.empty; ETag = None; NextLink = None }
            let transport = new CountedSinglePageTransport(response)
            let transportFor _ = (transport :> IDisposable), (transport :> Transport.ISinglePageGitHubTransport)
            let run args = Operations.runWithDependencies args (fun _ -> TelemetryStore.ApprovedLocalDurable) transportFor
            let command = [| "collect-native-delivery"; "--config"; configPath; "--source-ref"; sourceRef |]
            Configuration.load configPath
            |> Result.bind (Configuration.loadNativeDeliverySourceInstallation configPath)
            |> Result.defaultWith (String.concat "; " >> failwith) |> ignore
            let resolvedCandidate =
                TelemetryStoreApplication.resolveNativeDeliveryCandidate store TelemetryStore.ApprovedLocalDurable sourceRef
                |> Result.defaultWith (String.concat "; " >> failwith)
            let changedResponse =
                { response with Body = response.Body.Replace(String('a', 40), String('c', 40), StringComparison.Ordinal) }
            let changedTransport = new CountedSinglePageTransport(changedResponse)
            Assert.True(
                NativeDeliverySource.acquire (changedTransport :> Transport.ISinglePageGitHubTransport) resolvedCandidate
                |> Result.isError)
            Assert.Single changedTransport.Requests |> ignore
            let closedResponse =
                { response with
                    Body = """{"number":7,"state":"closed","merged":false,"head":{"sha":"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa"},"base":{"ref":"main","sha":"dddddddddddddddddddddddddddddddddddddddd","repo":{"full_name":"FS-GG/.github"}},"merge_commit_sha":"eeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeee","merged_at":null}""" }
            let closedTransport = new CountedSinglePageTransport(closedResponse)
            let closed =
                NativeDeliverySource.acquire (closedTransport :> Transport.ISinglePageGitHubTransport) resolvedCandidate
                |> Result.defaultWith (String.concat "; " >> failwith)
            Assert.Equal("closed-unmerged", closed.State)
            Assert.Null(closed.MergeCommit |> Option.toObj)
            let openResponse =
                { closedResponse with Body = closedResponse.Body.Replace("\"state\":\"closed\"", "\"state\":\"open\"") }
            let opened = NativeDeliverySource.decodeResponse resolvedCandidate openResponse.Body
                         |> Result.defaultWith (String.concat "; " >> failwith)
            Assert.Equal("open", opened.State)
            Assert.Null(opened.MergeCommit |> Option.toObj)
            for malformed in
                [ openResponse.Body.Replace(String('e', 40), "not-a-sha", StringComparison.Ordinal)
                  openResponse.Body.Replace("\"merged_at\":null", "\"merged_at\":\"2026-09-29T08:00:00Z\"") ] do
                Assert.True(NativeDeliverySource.decodeResponse resolvedCandidate malformed |> Result.isError)
            let paginatedTransport = new CountedSinglePageTransport({ response with NextLink = Some "https://example.invalid/next" })
            Assert.True(
                NativeDeliverySource.acquire (paginatedTransport :> Transport.ISinglePageGitHubTransport) resolvedCandidate
                |> Result.isError)
            File.WriteAllText(sourceInstallationPath, JsonSerializer.Serialize { sourceInstallation with AllowedRepositories = [| "FS-GG/foreign" |] })
            Assert.Equal(3, run command)
            Assert.Empty transport.Requests
            File.WriteAllText(sourceInstallationPath, JsonSerializer.Serialize sourceInstallation)
            Assert.Equal(0, run command)
            Assert.Equal(0, run command)
            Assert.Single transport.Requests |> ignore
            let request = transport.Requests[0]
            Assert.Equal("GET", request.Method)
            Assert.Equal("repos/FS-GG/.github/pulls/7", request.Path)
            Assert.Equal(Transport.NoBody, request.Body)
            let export args =
                let original = Console.Out
                use output = new StringWriter()
                try
                    Console.SetOut output
                    Assert.Equal(0, run args)
                    output.ToString()
                finally Console.SetOut original
            use v1 = JsonDocument.Parse(export [| "export-learning"; "--config"; configPath |])
            Assert.Equal("fsgg.telemetry.protected-learning-export/1", v1.RootElement.GetProperty("schema").GetString())
            Assert.False(v1.RootElement.TryGetProperty("deliveryCaptures") |> fst)
            use v2 = JsonDocument.Parse(export [| "export-learning"; "--config"; configPath; "--include-native-delivery" |])
            Assert.Equal("fsgg.telemetry.protected-learning-export/2", v2.RootElement.GetProperty("schema").GetString())
            let deliveryCaptures = v2.RootElement.GetProperty("deliveryCaptures").EnumerateArray() |> Seq.toArray
            Assert.Single deliveryCaptures |> ignore
            let candidateBinding = deliveryCaptures[0].GetProperty("candidateBinding")
            Assert.Equal("generic", candidateBinding.GetProperty("receiptRole").GetString())
            Assert.Equal(TelemetryReceipt.key scope.Producer "delivery-candidate",
                         candidateBinding.GetProperty("receiptKey").GetString())
            let deliveryFile = Assert.Single(Directory.GetFiles(evidence, "*.delivery-capture.json"))
            let retainedBytes = File.ReadAllBytes deliveryFile
            let changedCapture = System.Text.Json.Nodes.JsonNode.Parse(retainedBytes).AsObject()
            changedCapture["responseBody"] <- System.Text.Json.Nodes.JsonValue.Create openResponse.Body
            changedCapture["sourceDigest"] <- System.Text.Json.Nodes.JsonValue.Create(
                Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes openResponse.Body)).ToLowerInvariant())
            File.WriteAllText(deliveryFile, changedCapture.ToJsonString())
            Assert.Equal(3, run [| "export-learning"; "--config"; configPath; "--include-native-delivery" |])
            File.WriteAllBytes(deliveryFile, retainedBytes)
            File.WriteAllText(githubToken, "changed-token")
            Assert.Equal(3, run command)
            Assert.Equal(3, run [| "export-learning"; "--config"; configPath; "--include-native-delivery" |])
            Assert.Equal(1, transport.Requests.Length)
        finally
            if Directory.Exists root then Directory.Delete(root, true)

    [<Fact>]
    let ``host global census fails closed on inconsistent accepted obligations`` () =
        task {
            let root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"))

            try
                let stores = [| for index in 1..2 -> Path.Combine(root, string index) |]

                for index, path in Array.indexed stores do
                    let enrolled =
                        { scope with
                            Workspace = $"workspace-{index}"
                            Producer = $"producer-{index}"
                        }

                    TelemetryStoreApplication.initialize path TelemetryStore.ApprovedLocalDurable
                    |> Result.defaultWith (fun e -> failwithf "%A" e)
                    |> ignore

                    TelemetryStoreApplication.enrollReceiptProducer path TelemetryStore.ApprovedLocalDurable enrolled
                    |> Result.defaultWith (fun e -> failwithf "%A" e)
                    |> ignore

                    use connection =
                        new SqliteConnection(
                            $"Data Source={Path.Combine(path, TelemetryStoreApplication.databaseFileName)};Pooling=False"
                        )

                    connection.Open()
                    use transaction = connection.BeginTransaction()

                    for batch in 1..1 do
                        use command = connection.CreateCommand()
                        command.Transaction <- transaction

                        command.CommandText <-
                            "INSERT INTO transport_receipts(producer,batch,stream,digest,payload_bytes,state) VALUES($p,$b,'runtime',$d,1,'durably-received');"

                        command.Parameters.AddWithValue("$p", enrolled.Producer) |> ignore
                        command.Parameters.AddWithValue("$b", string batch) |> ignore
                        command.Parameters.AddWithValue("$d", String('a', 64)) |> ignore
                        command.ExecuteNonQuery() |> ignore

                    transaction.Commit()

                let config =
                    {
                        Schema = "fsgg.telemetry.host-config/1"
                        ListenUrl = "https://127.0.0.1:1"
                        CertificatePath = "/unused"
                        CertificatePasswordFile = "/unused"
                        ServiceLockPath = "/unused"
                        Stores =
                            [|
                                {
                                    WorkspaceId = "workspace-0"
                                    Root = stores[0]
                                }
                                {
                                    WorkspaceId = "workspace-1"
                                    Root = stores[1]
                                }
                            |]
                        Credentials = [||]
                        BrowserPrincipals = [||]
                        BrowserSession = browserSession
                    }

                use state =
                    new Runtime.HostState(config, fun _ -> TelemetryStore.ApprovedLocalDurable)

                let incoming =
                    { scope with
                        Workspace = "workspace-0"
                        Producer = "producer-0"
                    }

                let bytes =
                    envelope "overload"
                    |> fun value ->
                        Encoding.UTF8
                            .GetString(value)
                            .Replace("workspace-a", "workspace-0")
                            .Replace("producer-a", "producer-0")
                        |> Encoding.UTF8.GetBytes

                let! reply = Runtime.submit state incoming bytes CancellationToken.None
                Assert.Equal(503, reply.Status)
                Assert.Equal(Some "storage-unavailable", RemoteContract.parseError reply.Body)
                state.Ready <- true
                state.StartDrain()

                for _ in 1..50 do
                    if state.Ready then
                        do! Task.Delay 20

                Assert.False(state.Ready, "a failed drain must withdraw readiness")
            finally
                if Directory.Exists root then
                    Directory.Delete(root, true)
        }

    [<Fact(Timeout = 30000)>]
    let ``actual host aggregate refuses new global capacity but preserves identity retries`` () =
        task {
            let root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"))

            try
                let roots = [| Path.Combine(root, "a"); Path.Combine(root, "b") |]
                let mutable replayScope = scope
                let mutable replayBytes = [||]

                for storeIndex, store in Array.indexed roots do
                    TelemetryStoreApplication.initialize store TelemetryStore.ApprovedLocalDurable
                    |> Result.defaultWith (fun e -> failwithf "%A" e)
                    |> ignore

                    Directory.CreateDirectory(Path.Combine(store, "receipt-inbox")) |> ignore

                    for producerIndex in 0..3 do
                        let enrolled: TelemetryReceipt.Scope =
                            {
                                Workspace = $"workspace-{storeIndex}"
                                Producer = $"producer-{storeIndex}-{producerIndex}"
                                Stream = "runtime"
                            }

                        TelemetryStoreApplication.enrollReceiptProducer
                            store
                            TelemetryStore.ApprovedLocalDurable
                            enrolled
                        |> Result.defaultWith (fun e -> failwithf "%A" e)
                        |> ignore

                    use connection =
                        new SqliteConnection(
                            $"Data Source={Path.Combine(store, TelemetryStoreApplication.databaseFileName)};Pooling=False"
                        )

                    connection.Open()
                    use transaction = connection.BeginTransaction()

                    for producerIndex in 0..3 do
                        let enrolled: TelemetryReceipt.Scope =
                            {
                                Workspace = $"workspace-{storeIndex}"
                                Producer = $"producer-{storeIndex}-{producerIndex}"
                                Stream = "runtime"
                            }

                        for batchIndex in 0..127 do
                            let batch = $"batch-{batchIndex}"
                            let bytes = makeEnvelope enrolled batch 0

                            let parsed =
                                TelemetryReceipt.parse bytes |> Result.defaultWith (fun e -> failwithf "%A" e)

                            File.WriteAllText(
                                Path.Combine(
                                    store,
                                    "receipt-inbox",
                                    TelemetryReceipt.key enrolled.Producer batch + ".ready"
                                ),
                                parsed.Canonical
                            )

                            use command = connection.CreateCommand()
                            command.Transaction <- transaction

                            command.CommandText <-
                                "INSERT INTO transport_receipts(producer,batch,stream,digest,payload_bytes,state) VALUES($p,$b,$s,$d,$n,'durably-received');"

                            command.Parameters.AddWithValue("$p", enrolled.Producer) |> ignore
                            command.Parameters.AddWithValue("$b", batch) |> ignore
                            command.Parameters.AddWithValue("$s", enrolled.Stream) |> ignore
                            command.Parameters.AddWithValue("$d", parsed.Digest) |> ignore

                            command.Parameters.AddWithValue("$n", Encoding.UTF8.GetByteCount parsed.Canonical)
                            |> ignore

                            command.ExecuteNonQuery() |> ignore

                            if storeIndex = 0 && producerIndex = 0 && batchIndex = 0 then
                                replayScope <- enrolled
                                replayBytes <- bytes

                    transaction.Commit()

                let config =
                    {
                        Schema = "fsgg.telemetry.host-config/1"
                        ListenUrl = "https://127.0.0.1:1"
                        CertificatePath = "/unused"
                        CertificatePasswordFile = "/unused"
                        ServiceLockPath = "/unused"
                        Stores =
                            [|
                                {
                                    WorkspaceId = "workspace-0"
                                    Root = roots[0]
                                }
                                {
                                    WorkspaceId = "workspace-1"
                                    Root = roots[1]
                                }
                            |]
                        Credentials = [||]
                        BrowserPrincipals = [||]
                        BrowserSession = browserSession
                    }

                use state =
                    new Runtime.HostState(config, fun _ -> TelemetryStore.ApprovedLocalDurable)

                let newBytes = makeEnvelope replayScope "new-batch" 0
                let! overloaded = Runtime.submit state replayScope newBytes CancellationToken.None
                Assert.Equal(429, overloaded.Status)
                let! replay = Runtime.submit state replayScope replayBytes CancellationToken.None
                Assert.Equal(200, replay.Status)
                let changed = makeEnvelope replayScope "batch-0" 1
                let! conflict = Runtime.submit state replayScope changed CancellationToken.None
                Assert.Equal(409, conflict.Status)
            finally
                if Directory.Exists root then
                    Directory.Delete(root, true)
        }

    [<Fact>]
    let ``host global capacity uses aggregate counts and bytes`` () =
        Assert.True(Capacity.admitsNewIdentity 999999L 1023L (64L * 1024L * 1024L - 10L) 10L)
        Assert.False(Capacity.admitsNewIdentity 1000000L 0L 0L 1L)
        Assert.False(Capacity.admitsNewIdentity 0L 1024L 0L 1L)
        Assert.False(Capacity.admitsNewIdentity 0L 0L (64L * 1024L * 1024L) 1L)

    [<Fact>]
    let ``Host restores a 0.1.2 schema 9 backup into separate schema 12 state`` () =
        let root = Path.Combine(Path.GetTempPath(), "host-schema-restore-" + Guid.NewGuid().ToString("N"))
        Directory.CreateDirectory root |> ignore

        try
            let backup = Path.Combine(root, "backup")
            let restored = Path.Combine(root, "restored")
            let workspace = "proof-workspace"
            let source = Path.Combine(backup, workspace, "telemetry.sqlite3")
            let target = Path.Combine(restored, workspace, "telemetry.sqlite3")

            ZipFile.ExtractToDirectory(
                Path.Combine(__SOURCE_DIRECTORY__, "fixtures", "host-0.1.2-schema9-backup.zip"),
                backup
            )

            let version path =
                use connection = new SqliteConnection($"Data Source={path};Mode=ReadOnly")
                connection.Open()
                use command = connection.CreateCommand()
                command.CommandText <- "PRAGMA user_version;"
                command.ExecuteScalar() :?> int64 |> int

            let sourceDigest = SHA256.HashData(File.ReadAllBytes source)
            let certificate = Path.Combine(root, "cert.pfx")
            let password = Path.Combine(root, "cert.pass")
            let config = Path.Combine(root, "host.json")
            File.WriteAllText(certificate, "disposable certificate")
            File.WriteAllText(password, "disposable password")

            File.WriteAllText(
                config,
                $"""{{"Schema":"fsgg.telemetry.host-config/1","ListenUrl":"https://127.0.0.1:18445","CertificatePath":"{certificate}","CertificatePasswordFile":"{password}","ServiceLockPath":"{Path.Combine(root, "host.lock")}","Stores":[{{"WorkspaceId":"{workspace}","Root":"{Path.Combine(restored, workspace)}"}}],"Credentials":[],"BrowserPrincipals":[],"BrowserSession":{{"IdleSeconds":300,"AbsoluteSeconds":3600,"MaximumSessions":32,"LoginAttemptsPerMinute":16,"LoginAdmission":4,"QueryAdmission":4,"QueryTimeoutSeconds":10}}}}"""
            )

            for path in [ certificate; password; config ] do
                File.SetUnixFileMode(path, UnixFileMode.UserRead ||| UnixFileMode.UserWrite)

            Assert.Equal(
                0,
                Operations.runWithAssessment
                    [| "restore"; "--config"; config; "--input"; backup; "--state-root"; restored |]
                    (fun _ -> TelemetryStore.ApprovedLocalDurable)
            )

            Assert.Equal(12, version target)
            Assert.Equal(9, version source)
            Assert.Equal<byte>(sourceDigest, SHA256.HashData(File.ReadAllBytes source))
            let receiptScope: TelemetryReceipt.Scope =
                { Workspace = workspace; Producer = "proof-producer"; Stream = "runtime" }

            let restoredReceipt =
                TelemetryStoreApplication.lookupReceipt
                    (Path.Combine(restored, workspace))
                    TelemetryStore.ApprovedLocalDurable
                    receiptScope
                    "proof-applied"
                |> Result.defaultWith (fun errors -> failwithf "%A" errors)

            Assert.Contains("\"status\":\"applied\"", restoredReceipt)
            Assert.True(
                TelemetryStoreApplication.status
                    (Path.Combine(restored, workspace))
                    TelemetryStore.ApprovedLocalDurable
                |> Result.isOk
            )
        finally
            if Directory.Exists root then
                Directory.Delete(root, true)

    [<Fact(Timeout = 30000)>]
    let ``real TLS receiver preserves scoped receipt across restart and duplicate submit`` () =
        task {
            let root =
                Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "fsgg-h1-test-" + Guid.NewGuid().ToString("N")
                )

            let ambientEndpoint =
                Environment.GetEnvironmentVariable("Kestrel__Endpoints__ambient__Url")

            Environment.SetEnvironmentVariable("Kestrel__Endpoints__ambient__Url", "http://127.0.0.1:1")
            Directory.CreateDirectory root |> ignore

            try
                let store = Path.Combine(root, "store")

                TelemetryStoreApplication.initialize store TelemetryStore.ApprovedLocalDurable
                |> Result.defaultWith (fun e -> failwithf "%A" e)
                |> ignore

                TelemetryStoreApplication.enrollReceiptProducer store TelemetryStore.ApprovedLocalDurable scope
                |> Result.defaultWith (fun e -> failwithf "%A" e)
                |> ignore

                let otherScope = { scope with Producer = "producer-b" }

                TelemetryStoreApplication.enrollReceiptProducer store TelemetryStore.ApprovedLocalDurable otherScope
                |> Result.defaultWith (fun e -> failwithf "%A" e)
                |> ignore

                use listener = new TcpListener(IPAddress.Loopback, 0)
                listener.Start()
                let port = (listener.LocalEndpoint :?> Net.IPEndPoint).Port
                listener.Stop()
                let password = "test-password"
                use rsa = RSA.Create(2048)

                let request =
                    CertificateRequest("CN=localhost", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1)

                request.CertificateExtensions.Add(X509BasicConstraintsExtension(false, false, 0, false))
                request.CertificateExtensions.Add(X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature, false))
                let san = SubjectAlternativeNameBuilder()
                san.AddDnsName("localhost")
                san.AddIpAddress(IPAddress.Loopback)
                request.CertificateExtensions.Add(san.Build())

                use certificate =
                    request.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow.AddDays(1))

                let certificatePath = Path.Combine(root, "server.pfx")
                let passwordPath = Path.Combine(root, "certificate-password")
                let tokenPath = Path.Combine(root, "producer-token")
                let rotatedTokenPath = Path.Combine(root, "producer-token-rotated")
                let revokedTokenPath = Path.Combine(root, "producer-token-revoked")
                let otherTokenPath = Path.Combine(root, "other-token")
                let browserKeyPath = Path.Combine(root, "browser-key.json")
                let configPath = Path.Combine(root, "host.json")
                let lockPath = Path.Combine(root, "host.lock")
                File.WriteAllBytes(certificatePath, certificate.Export(X509ContentType.Pfx, password))
                File.WriteAllText(passwordPath, password)
                let token = String('t', 48)
                let rotatedToken = String('r', 48)
                let revokedToken = String('v', 48)
                let otherToken = String('o', 48)

                let browserAccessKey =
                    Convert
                        .ToBase64String(RandomNumberGenerator.GetBytes 32)
                        .TrimEnd('=')
                        .Replace('+', '-')
                        .Replace('/', '_')

                File.WriteAllText(tokenPath, token)
                File.WriteAllText(rotatedTokenPath, rotatedToken)
                File.WriteAllText(revokedTokenPath, revokedToken)
                File.WriteAllText(otherTokenPath, otherToken)

                let browserHash =
                    Convert
                        .ToHexString(
                            SHA256.HashData(
                                Convert.FromBase64String(browserAccessKey.Replace('-', '+').Replace('_', '/') + "=")
                            )
                        )
                        .ToLowerInvariant()

                File.WriteAllText(
                    browserKeyPath,
                    $"""{{"schema":"fsgg.telemetry.browser-key/1","algorithm":"sha256","keyHash":"{browserHash}"}}"""
                )

                for path in
                    [
                        certificatePath
                        passwordPath
                        tokenPath
                        rotatedTokenPath
                        revokedTokenPath
                        otherTokenPath
                        browserKeyPath
                    ] do
                    File.SetUnixFileMode(path, UnixFileMode.UserRead ||| UnixFileMode.UserWrite)

                let json =
                    $"""{{"Schema":"fsgg.telemetry.host-config/1","ListenUrl":"https://127.0.0.1:{port}","CertificatePath":"{certificatePath}","CertificatePasswordFile":"{passwordPath}","ServiceLockPath":"{lockPath}","Stores":[{{"WorkspaceId":"{scope.Workspace}","Root":"{store}"}}],"Credentials":[{{"Reference":"producer-main","SecretFile":"{tokenPath}","WorkspaceId":"{scope.Workspace}","ProducerId":"{scope.Producer}","StreamId":"{scope.Stream}","Revoked":false}},{{"Reference":"producer-rotated","SecretFile":"{rotatedTokenPath}","WorkspaceId":"{scope.Workspace}","ProducerId":"{scope.Producer}","StreamId":"{scope.Stream}","Revoked":false}},{{"Reference":"producer-revoked","SecretFile":"{revokedTokenPath}","WorkspaceId":"{scope.Workspace}","ProducerId":"{scope.Producer}","StreamId":"{scope.Stream}","Revoked":true}},{{"Reference":"producer-other","SecretFile":"{otherTokenPath}","WorkspaceId":"{otherScope.Workspace}","ProducerId":"{otherScope.Producer}","StreamId":"{otherScope.Stream}","Revoked":false}}],"BrowserPrincipals":[{{"PrincipalId":"operator-a","KeyHashFile":"{browserKeyPath}","WorkspaceIds":["{scope.Workspace}"],"Revoked":false}}],"BrowserSession":{{"IdleSeconds":300,"AbsoluteSeconds":3600,"MaximumSessions":32,"LoginAttemptsPerMinute":16,"LoginAdmission":4,"QueryAdmission":4,"QueryTimeoutSeconds":10}}}}"""

                File.WriteAllText(configPath, json)
                File.SetUnixFileMode(configPath, UnixFileMode.UserRead ||| UnixFileMode.UserWrite)

                match Configuration.load configPath with
                | Ok _ -> ()
                | Error errors -> Assert.Fail(sprintf "config rejected: %A" errors)

                let loaded =
                    Configuration.load configPath |> Result.defaultWith (fun e -> failwithf "%A" e)

                Assert.False(File.Exists lockPath)

                Assert.Equal(
                    0,
                    Operations.runWithAssessment [| "status"; "--config"; configPath |] (fun _ ->
                        TelemetryStore.ApprovedLocalDurable)
                )

                Assert.False(File.Exists lockPath)

                Assert.Equal(
                    0,
                    Operations.runWithAssessment [| "preflight"; "--config"; configPath |] (fun _ ->
                        TelemetryStore.ApprovedLocalDurable)
                )

                let credentials = Configuration.credentials loaded

                let start () =
                    task {
                        let state =
                            new Runtime.HostState(loaded, fun _ -> TelemetryStore.ApprovedLocalDurable)

                        state.Ready <- true
                        state.StartDrain()
                        let builder = Hosting.createBuilder loaded.ListenUrl certificate
                        let app = builder.Build()
                        Endpoints.configure app state credentials
                        do! app.StartAsync()
                        return app, state
                    }

                let handler = new HttpClientHandler(AllowAutoRedirect = false)

                handler.ServerCertificateCustomValidationCallback <-
                    fun _ cert _ errors ->
                        errors = Net.Security.SslPolicyErrors.RemoteCertificateChainErrors
                        && cert.GetCertHashString() = certificate.GetCertHashString()

                use client = new HttpClient(handler)
                client.DefaultRequestHeaders.Authorization <- Headers.AuthenticationHeaderValue("Bearer", token)

                let waitReady () =
                    task {
                        let mutable ready = false

                        for _ in 1..50 do
                            if not ready then
                                try
                                    let! response = client.GetAsync($"https://127.0.0.1:{port}/private/health") in
                                    ready <- response.StatusCode = HttpStatusCode.OK
                                with _ ->
                                    do! Task.Delay 100

                        Assert.True(ready, "receiver did not become ready")
                    }

                let! firstApp, firstState = start ()
                do! waitReady ()
                Assert.Equal<string>([| loaded.ListenUrl |], firstApp.Urls |> Seq.toArray)

                let remoteConfig: RemoteContract.ClientConfig =
                    {
                        Endpoint = Uri($"https://127.0.0.1:{port}/")
                        CredentialReference = "local-secret-ref"
                    }

                let dropTransport = new HttpClientHandler(AllowAutoRedirect = false)

                dropTransport.ServerCertificateCustomValidationCallback <-
                    handler.ServerCertificateCustomValidationCallback

                use droppedClient = new HttpClient(new DropFirstResponseHandler(dropTransport))

                let! recoveredLostResponse =
                    RemoteClient.submitWithClientForTesting
                        droppedClient
                        remoteConfig
                        (fun _ _ -> Task.FromResult(Some token))
                        scope
                        (envelope "batch-lost-response")
                        CancellationToken.None

                match recoveredLostResponse with
                | RemoteClient.Acknowledged value -> Assert.Equal("batch-lost-response", value.BatchId)
                | _ -> Assert.Fail "same-identity retry did not recover a lost durable response"

                let! accepted =
                    RemoteClient.submitWithClientForTesting
                        client
                        remoteConfig
                        (fun _ _ -> Task.FromResult(Some token))
                        scope
                        (envelope "batch-tls")
                        CancellationToken.None

                match accepted with
                | RemoteClient.Acknowledged value -> Assert.Equal("batch-tls", value.BatchId)
                | _ -> Assert.Fail "client did not verify durable receipt"

                client.DefaultRequestHeaders.Authorization <-
                    Headers.AuthenticationHeaderValue("Bearer", String('w', 48))

                use! denied = client.GetAsync($"https://127.0.0.1:{port}/v1/receipts/batch-tls")
                Assert.Equal(HttpStatusCode.Unauthorized, denied.StatusCode)
                client.DefaultRequestHeaders.Authorization <- Headers.AuthenticationHeaderValue("Bearer", token)
                use! invalidLookup = client.GetAsync($"https://127.0.0.1:{port}/v1/receipts/bad!")
                Assert.Equal(HttpStatusCode.BadRequest, invalidLookup.StatusCode)

                Assert.Equal(
                    Some "invalid-request",
                    RemoteContract.parseError (invalidLookup.Content.ReadAsByteArrayAsync().Result)
                )

                use! spoofed =
                    client.PostAsync(
                        $"https://127.0.0.1:{port}/v1/batches",
                        new ByteArrayContent(makeEnvelope otherScope "spoofed-scope" 0)
                    )

                Assert.Equal(HttpStatusCode.Forbidden, spoofed.StatusCode)
                client.DefaultRequestHeaders.Authorization <- Headers.AuthenticationHeaderValue("Bearer", otherToken)
                use! crossScope = client.GetAsync($"https://127.0.0.1:{port}/v1/receipts/batch-tls")
                Assert.NotEqual(HttpStatusCode.OK, crossScope.StatusCode)
                client.DefaultRequestHeaders.Authorization <- Headers.AuthenticationHeaderValue("Bearer", revokedToken)
                use! revoked = client.GetAsync($"https://127.0.0.1:{port}/v1/receipts/batch-tls")
                Assert.Equal(HttpStatusCode.Unauthorized, revoked.StatusCode)
                client.DefaultRequestHeaders.Authorization <- Headers.AuthenticationHeaderValue("Bearer", rotatedToken)
                use! rotated = client.GetAsync($"https://127.0.0.1:{port}/v1/receipts/batch-tls")
                Assert.Equal(HttpStatusCode.OK, rotated.StatusCode)
                client.DefaultRequestHeaders.Authorization <- Headers.AuthenticationHeaderValue("Bearer", token)

                use! invalid =
                    client.PostAsync($"https://127.0.0.1:{port}/v1/batches", new ByteArrayContent([| 0xffuy |]))

                Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode)

                let unsupported =
                    Encoding.UTF8.GetString(envelope "new-version").Replace("envelope/1", "envelope/2")

                use! wrongVersion =
                    client.PostAsync($"https://127.0.0.1:{port}/v1/batches", new StringContent(unsupported))

                Assert.Equal(HttpStatusCode.BadRequest, wrongVersion.StatusCode)

                Assert.Equal(
                    Some "unsupported-version",
                    RemoteContract.parseError (wrongVersion.Content.ReadAsByteArrayAsync().Result)
                )

                use! oversized =
                    client.PostAsync(
                        $"https://127.0.0.1:{port}/v1/batches",
                        new ByteArrayContent(Array.zeroCreate 73729)
                    )

                Assert.Equal(HttpStatusCode.RequestEntityTooLarge, oversized.StatusCode)

                use! duplicate =
                    client.PostAsync($"https://127.0.0.1:{port}/v1/batches", new ByteArrayContent(envelope "batch-tls"))

                Assert.True(
                    duplicate.StatusCode = HttpStatusCode.OK
                    || duplicate.StatusCode = HttpStatusCode.Accepted
                )

                let started =
                    TaskCompletionSource<unit>(TaskCreationOptions.RunContinuationsAsynchronously)

                use slowCts = new CancellationTokenSource()

                let slow =
                    client.PostAsync($"https://127.0.0.1:{port}/v1/batches", new SlowContent(started), slowCts.Token)

                do! started.Task

                let countAvailable () =
                    let mutable count = 0

                    while firstState.TryAcquireSlot() do
                        count <- count + 1

                    for _ in 1..count do
                        firstState.ReleaseSlot()

                    count

                let mutable available = 16

                for _ in 1..20 do
                    if available = 16 then
                        do! Task.Delay 50
                        available <- countAvailable ()

                Assert.Equal(15, available)
                let mutable held = 0

                while held < 15 && firstState.TryAcquireSlot() do
                    held <- held + 1

                Assert.Equal(15, held)

                Assert.False(
                    firstState.TryAcquireSlot(),
                    "slow HTTP body must hold the sixteenth global admission slot"
                )

                for _ in 1..held do
                    firstState.ReleaseSlot()

                for _ in 1..240 do
                    if countAvailable () <> 16 then
                        do! Task.Delay 50

                Assert.Equal(16, countAvailable ())
                slowCts.Cancel()

                try
                    let! response = slow in response.Dispose()
                with :? OperationCanceledException ->
                    ()

                let abortedStarted =
                    TaskCompletionSource<unit>(TaskCreationOptions.RunContinuationsAsynchronously)

                use abortedCts = new CancellationTokenSource()

                let aborted =
                    client.PostAsync(
                        $"https://127.0.0.1:{port}/v1/batches",
                        new SlowContent(abortedStarted),
                        abortedCts.Token
                    )

                do! abortedStarted.Task
                abortedCts.Cancel()

                try
                    let! response = aborted in response.Dispose()
                with :? OperationCanceledException ->
                    ()

                for _ in 1..50 do
                    if countAvailable () <> 16 then
                        do! Task.Delay 20

                Assert.Equal(16, countAvailable ())
                do! firstApp.StopAsync()
                do! firstApp.DisposeAsync().AsTask()
                (firstState :> IDisposable).Dispose()
                let! secondApp, secondState = start ()
                do! waitReady ()
                use! lookup = client.GetAsync($"https://127.0.0.1:{port}/v1/receipts/batch-tls")
                Assert.Equal(HttpStatusCode.OK, lookup.StatusCode)
                let! body = lookup.Content.ReadAsByteArrayAsync()

                match RemoteContract.parseReceipt body with
                | Ok value -> Assert.Equal("applied", value.Status)
                | Error e -> Assert.Fail e

                do! secondApp.StopAsync()
                do! secondApp.DisposeAsync().AsTask()
                (secondState :> IDisposable).Dispose()
                let backupPath = Path.Combine(root, "backup")
                let restoredRoot = Path.Combine(root, "restored")

                Assert.Equal(
                    0,
                    Operations.runWithAssessment
                        [| "backup"; "--config"; configPath; "--output"; backupPath |]
                        (fun _ -> TelemetryStore.ApprovedLocalDurable)
                )

                TelemetryStoreApplication.submitReceipt
                    store
                    TelemetryStore.ApprovedLocalDurable
                    scope
                    (envelope "after-backup")
                |> Result.defaultWith (fun e -> failwithf "%A" e)
                |> ignore

                TelemetryStoreApplication.drainReceipts store TelemetryStore.ApprovedLocalDurable scope.Workspace
                |> Result.defaultWith (fun e -> failwithf "%A" e)
                |> ignore

                let restoredStore = Path.Combine(restoredRoot, scope.Workspace)
                let restoreConfigPath = Path.Combine(root, "host.restore.json")
                File.WriteAllText(restoreConfigPath, json.Replace(store, restoredStore))
                File.SetUnixFileMode(restoreConfigPath, UnixFileMode.UserRead ||| UnixFileMode.UserWrite)

                Assert.Equal(
                    0,
                    Operations.runWithAssessment
                        [|
                            "restore"
                            "--config"
                            restoreConfigPath
                            "--input"
                            backupPath
                            "--state-root"
                            restoredRoot
                        |]
                        (fun _ -> TelemetryStore.ApprovedLocalDurable)
                )

                Assert.Equal(
                    0,
                    Operations.runWithAssessment [| "preflight"; "--config"; restoreConfigPath |] (fun _ ->
                        TelemetryStore.ApprovedLocalDurable)
                )

                Assert.True(
                    TelemetryStoreApplication.lookupReceipt
                        restoredStore
                        TelemetryStore.ApprovedLocalDurable
                        scope
                        "batch-tls"
                    |> Result.isOk
                )

                Assert.Equal(
                    Error [ "receipt-unavailable" ],
                    TelemetryStoreApplication.lookupReceipt
                        restoredStore
                        TelemetryStore.ApprovedLocalDurable
                        scope
                        "after-backup"
                )

                let setManifest = Path.Combine(backupPath, "backup-manifest.json")
                let validSetManifest = File.ReadAllText setManifest

                for name, invalid in
                    [
                        "malformed", "{"
                        "wrong-type", validSetManifest.Replace("\"workspaces\":[", "\"workspaces\":{")
                        "open-workspace-entry",
                        validSetManifest.Replace("\"workspaceId\":", "\"unexpected\":true,\"workspaceId\":")
                    ] do
                    File.WriteAllText(setManifest, invalid)
                    let target = Path.Combine(root, name + "-restore")

                    Assert.Equal(
                        5,
                        Operations.runWithAssessment
                            [|
                                "restore"
                                "--config"
                                restoreConfigPath
                                "--input"
                                backupPath
                                "--state-root"
                                target
                            |]
                            (fun _ -> TelemetryStore.ApprovedLocalDurable)
                    )

                    Assert.False(Directory.Exists target)

                File.WriteAllText(setManifest, validSetManifest)

                File.AppendAllText(
                    Path.Combine(backupPath, scope.Workspace, TelemetryStoreApplication.databaseFileName),
                    "corrupt"
                )

                Assert.Equal(
                    5,
                    Operations.runWithAssessment
                        [|
                            "restore"
                            "--config"
                            restoreConfigPath
                            "--input"
                            backupPath
                            "--state-root"
                            Path.Combine(root, "corrupt-restore")
                        |]
                        (fun _ -> TelemetryStore.ApprovedLocalDurable)
                )
            finally
                Environment.SetEnvironmentVariable("Kestrel__Endpoints__ambient__Url", ambientEndpoint)

                if Directory.Exists root then
                    Directory.Delete(root, true)
        }
