namespace FS.GG.Telemetry.Tests

open System
open System.IO
open System.Text
open System.Text.Json
open System.Security.Cryptography
open Microsoft.Data.Sqlite
open Xunit
open FS.GG.Coord
open FS.GG.Coord.Cli

module HistoricalExportTests =
    let private approved = TelemetryStore.ApprovedLocalDurable
    let private scope:TelemetryReceipt.Scope = { Workspace="main-fsharp-dev"; Producer="migration-producer"; Stream="coordination" }

    let private payload (ingest:string) (revision:int) (events:string list) =
        let rendered = String.concat "," events
        Encoding.UTF8.GetBytes $"{{\"schema\":\"%s{TelemetryStore.BatchSchema}\",\"ingestId\":\"%s{ingest}\",\"sourceIdentity\":\"fixture\",\"generation\":\"g1\",\"cursor\":\"%d{revision}\",\"eventCount\":%d{events.Length},\"events\":[%s{rendered}]}}"

    let private item (identity:string) (revision:int) (feature:string) =
        $"{{\"kind\":\"item\",\"identity\":\"%s{identity}\",\"itemId\":\"%s{identity}\",\"revision\":%d{revision},\"featureId\":%s{feature}}}"

    let private activation itemId identity =
        $"{{\"kind\":\"operational-activation\",\"identity\":\"%s{identity}\",\"itemId\":\"%s{itemId}\",\"revision\":0,\"activationId\":\"activation\",\"scope\":\"explicit-future-dispatches\",\"runtime\":\"codex\",\"activatedAt\":\"2026-09-10T00:00:00Z\",\"clockProvenance\":\"host-wall\",\"lateAfterSeconds\":60}}"

    let private expected itemId identity dispatch relation parent =
        $"{{\"kind\":\"expected-dispatch\",\"identity\":\"%s{identity}\",\"itemId\":\"%s{itemId}\",\"revision\":0,\"dispatchId\":\"%s{dispatch}\",\"activationId\":\"activation\",\"relation\":\"%s{relation}\",\"parentDispatchId\":%s{parent},\"runtime\":\"codex\",\"expectedAt\":\"2026-09-10T00:00:01Z\",\"clockProvenance\":\"host-wall\"}}"

    let private ciBinding index =
        $"{{\"kind\":\"ci-binding\",\"identity\":\"binding-%02d{index}\",\"itemId\":\"ci-item\",\"revision\":0,\"collectionId\":\"collection-%02d{index}\",\"repository\":\"FS-GG/.github\",\"head\":\"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa\",\"prNumber\":1,\"workflow\":\"ci.yml\",\"featureId\":\"ci-item\",\"attemptId\":\"attempt\",\"parentAttemptId\":null,\"producerStream\":\"fixture\",\"binding\":\"exact\"}}"

    let private ciRun =
        "{\"kind\":\"ci-run\",\"identity\":\"aaa-run\",\"itemId\":\"ci-item\",\"revision\":0,\"repository\":\"FS-GG/.github\",\"runId\":1,\"attempt\":1,\"workflow\":\"ci.yml\",\"event\":\"pull_request\",\"head\":\"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa\",\"status\":\"completed\",\"conclusion\":\"success\",\"createdAt\":\"2026-09-10T00:00:00Z\",\"startedAt\":\"2026-09-10T00:00:01Z\",\"updatedAt\":\"2026-09-10T00:00:02Z\"}"

    let private ciJob =
        "{\"kind\":\"ci-job\",\"identity\":\"aaa-job\",\"itemId\":\"ci-item\",\"revision\":0,\"repository\":\"FS-GG/.github\",\"runId\":1,\"attempt\":1,\"jobId\":10,\"name\":\"test\",\"status\":\"completed\",\"conclusion\":\"success\",\"createdAt\":\"2026-09-10T00:00:00Z\",\"startedAt\":\"2026-09-10T00:00:01Z\",\"completedAt\":\"2026-09-10T00:00:02Z\"}"

    let private ciStep =
        "{\"kind\":\"ci-step\",\"identity\":\"aaa-step\",\"itemId\":\"ci-item\",\"revision\":0,\"repository\":\"FS-GG/.github\",\"runId\":1,\"attempt\":1,\"jobId\":10,\"number\":1,\"name\":\"test\",\"status\":\"completed\",\"conclusion\":\"success\",\"startedAt\":\"2026-09-10T00:00:01Z\",\"completedAt\":\"2026-09-10T00:00:02Z\",\"classification\":\"useful-validation\",\"rationale\":\"fixture\"}"

    let private envelope (bytes:byte array) =
        use payloadDocument = JsonDocument.Parse bytes
        let batch = TelemetryStore.parseBatch bytes |> Result.defaultWith(fun errors -> failwith(String.concat "; " errors))
        Encoding.UTF8.GetBytes $"{{\"schema\":\"%s{TelemetryReceipt.Schema}\",\"workspaceId\":\"%s{scope.Workspace}\",\"producerId\":\"%s{scope.Producer}\",\"streamId\":\"%s{scope.Stream}\",\"batchId\":\"%s{batch.IngestId}\",\"payload\":%s{payloadDocument.RootElement.GetRawText()}}}"

    let private createRoot parent name receiptScoped =
        let root=Path.Combine(parent,name)
        TelemetryStoreApplication.initialize root approved |> Result.defaultWith(fun errors->failwith(String.concat "; " errors)) |> ignore
        if receiptScoped then
            TelemetryStoreApplication.provisionReceiptWorkspace root approved scope.Workspace |> Result.defaultWith(fun errors->failwith(String.concat "; " errors)) |> ignore
            TelemetryStoreApplication.enrollReceiptProducer root approved scope |> Result.defaultWith(fun errors->failwith(String.concat "; " errors)) |> ignore
        root

    let private ingest root receiptScoped bytes =
        if receiptScoped then
            let wrapped=envelope bytes
            TelemetryStoreApplication.submitReceipt root approved scope wrapped |> Result.defaultWith(fun errors->failwith(String.concat "; " errors)) |> ignore
            TelemetryStoreApplication.drainReceipts root approved scope.Workspace |> Result.defaultWith(fun errors->failwith(String.concat "; " errors)) |> ignore
            let parsed=TelemetryReceipt.parse wrapped |> Result.defaultWith(fun errors->failwith(String.concat "; " errors))
            TelemetryStoreApplication.lookupReceipt root approved scope parsed.BatchId |> Result.defaultWith(fun errors->failwith(String.concat "; " errors))
        else
            TelemetryStoreApplication.ingest root approved bytes |> Result.defaultWith(fun errors->failwith(String.concat "; " errors))

    let private privateParent () =
        let root=Path.Combine(Path.GetTempPath(),Guid.NewGuid().ToString("N"))
        Directory.CreateDirectory root |> ignore
        File.SetUnixFileMode(root,UnixFileMode.UserRead|||UnixFileMode.UserWrite|||UnixFileMode.UserExecute)
        root

    let private setLegacySchema8 root =
        use connection=new SqliteConnection($"Data Source={Path.Combine(root,TelemetryStoreApplication.databaseFileName)};Pooling=False")
        connection.Open()
        use command=connection.CreateCommand()
        command.CommandText <- "DROP INDEX transport_pending; DROP TABLE transport_receipts; DROP TABLE receipt_producers; DELETE FROM schema_migrations WHERE version=9; PRAGMA user_version=8;"
        command.ExecuteNonQuery() |> ignore

    [<Fact>]
    let ``historical export previews revisions and imports only through native receipts`` () =
        let parent=privateParent()
        try
            let source=createRoot parent "source" false
            let target=createRoot parent "target" true
            ingest source false (payload "source" 1 [item "identical" 0 "null";item "correction" 2 "null";item "target-newer" 0 "null";item "absent" 0 "null"]) |> ignore
            setLegacySchema8 source
            ingest target true (payload "target" 1 [item "identical" 0 "null";item "correction" 1 "null";item "target-newer" 3 "null"]) |> ignore
            let output=Path.Combine(parent,"export")
            TelemetryStoreApplication.exportHistorical source approved target approved scope output |> Result.defaultWith(fun errors->failwith(String.concat "; " errors)) |> ignore
            use manifest=JsonDocument.Parse(File.ReadAllBytes(Path.Combine(output,"manifest.json")))
            let disposition=manifest.RootElement.GetProperty("disposition")
            Assert.Equal(8,manifest.RootElement.GetProperty("sourceStoreSchema").GetInt32())
            Assert.Equal(9,manifest.RootElement.GetProperty("targetStoreSchema").GetInt32())
            Assert.Equal(2,disposition.GetProperty("emitted").GetInt32())
            Assert.Equal(1,disposition.GetProperty("identical").GetInt32())
            Assert.Equal(1,disposition.GetProperty("corrected").GetInt32())
            Assert.Equal(1,disposition.GetProperty("targetNewer").GetInt32())
            for entry in manifest.RootElement.GetProperty("batches").EnumerateArray() do
                let bytes=File.ReadAllBytes(Path.Combine(output,entry.GetProperty("file").GetString()))
                let receipt=ingest target true bytes
                use receiptDocument=JsonDocument.Parse receipt
                Assert.Equal("applied",receiptDocument.RootElement.GetProperty("status").GetString())
                Assert.Equal(entry.GetProperty("envelopeDigest").GetString(),receiptDocument.RootElement.GetProperty("digest").GetString())
            let finalOutput=Path.Combine(parent,"final")
            TelemetryStoreApplication.exportHistorical source approved target approved scope finalOutput |> Result.defaultWith(fun errors->failwith(String.concat "; " errors)) |> ignore
            use finalManifest=JsonDocument.Parse(File.ReadAllBytes(Path.Combine(finalOutput,"manifest.json")))
            Assert.Equal(0,finalManifest.RootElement.GetProperty("disposition").GetProperty("emitted").GetInt32())
            Assert.Equal(3,finalManifest.RootElement.GetProperty("disposition").GetProperty("identical").GetInt32())
            Assert.Equal(1,finalManifest.RootElement.GetProperty("disposition").GetProperty("targetNewer").GetInt32())
        finally Directory.Delete(parent,true)

    [<Fact>]
    let ``historical export binds the preview to the target receipt workspace and enrollment`` () =
        let parent=privateParent()
        try
            let source=createRoot parent "source" false
            let target=createRoot parent "target" true
            ingest source false (payload "source" 1 [item "historical" 0 "null"]) |> ignore
            let wrongWorkspace={scope with Workspace="another-workspace"}
            let wrongProducer={scope with Producer="another-producer"}
            for candidate,name in [wrongWorkspace,"wrong-workspace";wrongProducer,"wrong-producer"] do
                let output=Path.Combine(parent,name)
                Assert.Equal(Error ["historical export target is not enrolled for the supplied scope"],TelemetryStoreApplication.exportHistorical source approved target approved candidate output)
                Assert.False(Directory.Exists output)
        finally Directory.Delete(parent,true)

    [<Fact>]
    let ``equal revision conflict refuses atomically while unchanged snapshots reproduce bytes`` () =
        let parent=privateParent()
        try
            let source=createRoot parent "source" false
            let target=createRoot parent "target" true
            ingest source false (payload "source" 1 [item "conflict" 1 "null"]) |> ignore
            ingest target true (payload "target" 1 [item "conflict" 1 "\"different\""]) |> ignore
            let refused=Path.Combine(parent,"refused")
            Assert.Equal(Error ["historical export found 1 kind or equal-revision semantic conflicts"],TelemetryStoreApplication.exportHistorical source approved target approved scope refused)
            Assert.False(Directory.Exists refused)
            let empty=createRoot parent "empty" true
            let sourceDatabase=Path.Combine(source,TelemetryStoreApplication.databaseFileName)
            let emptyDatabase=Path.Combine(empty,TelemetryStoreApplication.databaseFileName)
            let beforeSource=SHA256.HashData(File.ReadAllBytes sourceDatabase)
            let beforeTarget=SHA256.HashData(File.ReadAllBytes emptyDatabase)
            let first=Path.Combine(parent,"first")
            let second=Path.Combine(parent,"second")
            TelemetryStoreApplication.exportHistorical source approved empty approved scope first |> Result.defaultWith(fun errors->failwith(String.concat "; " errors)) |> ignore
            TelemetryStoreApplication.exportHistorical source approved empty approved scope second |> Result.defaultWith(fun errors->failwith(String.concat "; " errors)) |> ignore
            let relative root = Directory.GetFiles(root) |> Array.map(fun path->Path.GetFileName path,File.ReadAllBytes path) |> Array.sortBy fst
            let left,right=relative first,relative second
            Assert.True((left |> Array.map fst) = (right |> Array.map fst))
            Array.zip left right |> Array.iter(fun ((_,a),(_,b))->Assert.Equal<byte>(a,b))
            Assert.Equal<byte>(beforeSource,SHA256.HashData(File.ReadAllBytes sourceDatabase))
            Assert.Equal<byte>(beforeTarget,SHA256.HashData(File.ReadAllBytes emptyDatabase))
            Assert.Equal(UnixFileMode.UserRead|||UnixFileMode.UserWrite|||UnixFileMode.UserExecute,File.GetUnixFileMode first)
            Directory.GetFiles(first) |> Array.iter(fun path->Assert.Equal(UnixFileMode.UserRead|||UnixFileMode.UserWrite,File.GetUnixFileMode path))
        finally Directory.Delete(parent,true)

    [<Fact>]
    let ``historical export orders nested references and refuses malformed or linked inputs`` () =
        let parent=privateParent()
        try
            let source=createRoot parent "source" false
            let target=createRoot parent "target" true
            let events=
                [ expected "item-a" "bbb-a-child" "child" "child" "\"root\""
                  expected "item-a" "aaa-a-root" "root" "root" "null"
                  expected "item-b" "aaa-b-child" "child" "child" "\"root\""
                  expected "item-b" "zzz-b-root" "root" "root" "null"
                  activation "item-a" "activation-a"
                  activation "item-b" "activation-b" ]
            ingest source false (payload "relations" 1 events) |> ignore
            let output=Path.Combine(parent,"ordered")
            TelemetryStoreApplication.exportHistorical source approved target approved scope output |> Result.defaultWith(fun errors->failwith(String.concat "; " errors)) |> ignore
            use manifest=JsonDocument.Parse(File.ReadAllBytes(Path.Combine(output,"manifest.json")))
            let identities =
                manifest.RootElement.GetProperty("batches").EnumerateArray()
                |> Seq.collect(fun entry ->
                    let bytes=File.ReadAllBytes(Path.Combine(output,entry.GetProperty("file").GetString()))
                    (TelemetryStore.parseBatch bytes |> Result.defaultWith(fun errors->failwith(String.concat "; " errors))).Facts)
                |> Seq.map _.Identity |> Seq.toArray
            let position identity=identities |> Array.findIndex((=)identity)
            Assert.True(position "activation-a" < position "aaa-a-root")
            Assert.True(position "aaa-a-root" < position "bbb-a-child")
            Assert.True(position "activation-b" < position "zzz-b-root")
            Assert.True(position "zzz-b-root" < position "aaa-b-child")

            use connection=new SqliteConnection($"Data Source={Path.Combine(source,TelemetryStoreApplication.databaseFileName)};Pooling=False")
            connection.Open()
            use command=connection.CreateCommand()
            command.CommandText <- "UPDATE ingest_facts SET canonical='{}' WHERE identity='zzz-b-root';"
            command.ExecuteNonQuery() |> ignore
            let malformed=Path.Combine(parent,"malformed")
            Assert.True(TelemetryStoreApplication.exportHistorical source approved target approved scope malformed |> Result.isError)
            Assert.False(Directory.Exists malformed)

            let linkedRoot=Path.Combine(parent,"linked-source")
            Directory.CreateDirectory linkedRoot |> ignore
            File.SetUnixFileMode(linkedRoot,UnixFileMode.UserRead|||UnixFileMode.UserWrite|||UnixFileMode.UserExecute)
            File.CreateSymbolicLink(Path.Combine(linkedRoot,TelemetryStoreApplication.databaseFileName),Path.Combine(target,TelemetryStoreApplication.databaseFileName)) |> ignore
            Assert.True(TelemetryStoreApplication.exportHistorical linkedRoot approved target approved scope (Path.Combine(parent,"linked-output")) |> Result.isError)
        finally Directory.Delete(parent,true)

    [<Fact>]
    let ``post-preview target race is rejected by normal receipt semantics`` () =
        let parent=privateParent()
        try
            let source=createRoot parent "source" false
            let target=createRoot parent "target" true
            ingest source false (payload "source" 1 [item "raced" 0 "null"]) |> ignore
            let output=Path.Combine(parent,"export")
            TelemetryStoreApplication.exportHistorical source approved target approved scope output |> Result.defaultWith(fun errors->failwith(String.concat "; " errors)) |> ignore
            ingest target true (payload "racer" 1 [item "raced" 0 "\"new-target-value\""]) |> ignore
            use manifest=JsonDocument.Parse(File.ReadAllBytes(Path.Combine(output,"manifest.json")))
            let entry=manifest.RootElement.GetProperty("batches")[0]
            let bytes=File.ReadAllBytes(Path.Combine(output,entry.GetProperty("file").GetString()))
            let wrapped=envelope bytes
            TelemetryStoreApplication.submitReceipt target approved scope wrapped |> Result.defaultWith(fun errors->failwith(String.concat "; " errors)) |> ignore
            TelemetryStoreApplication.drainReceipts target approved scope.Workspace |> Result.defaultWith(fun errors->failwith(String.concat "; " errors)) |> ignore
            let parsed=TelemetryReceipt.parse wrapped |> Result.defaultWith(fun errors->failwith(String.concat "; " errors))
            let receipt=TelemetryStoreApplication.lookupReceipt target approved scope parsed.BatchId |> Result.defaultWith(fun errors->failwith(String.concat "; " errors))
            use receiptDocument=JsonDocument.Parse receipt
            Assert.Equal("rejected",receiptDocument.RootElement.GetProperty("status").GetString())
            Assert.Equal("semantic-conflict",receiptDocument.RootElement.GetProperty("code").GetString())
        finally Directory.Delete(parent,true)

    [<Fact>]
    let ``mixed CI history keeps binding run job and step order across batches`` () =
        let parent=privateParent()
        try
            let source=createRoot parent "source" false
            let target=createRoot parent "target" true
            ingest source false (payload "bindings" 1 [for index in 0..63 -> ciBinding index]) |> ignore
            ingest source false (payload "ci-details" 2 [ciRun;ciJob;ciStep]) |> ignore
            let output=Path.Combine(parent,"export")
            TelemetryStoreApplication.exportHistorical source approved target approved scope output |> Result.defaultWith(fun errors->failwith(String.concat "; " errors)) |> ignore
            use manifest=JsonDocument.Parse(File.ReadAllBytes(Path.Combine(output,"manifest.json")))
            let batches=manifest.RootElement.GetProperty("batches").EnumerateArray() |> Seq.toArray
            Assert.True(batches.Length >= 2)
            let identities = ResizeArray<string*int>()
            for batchIndex,entry in Array.indexed batches do
                let bytes=File.ReadAllBytes(Path.Combine(output,entry.GetProperty("file").GetString()))
                let parsed=TelemetryStore.parseBatch bytes |> Result.defaultWith(fun errors->failwith(String.concat "; " errors))
                parsed.Facts |> List.iter(fun fact->identities.Add(fact.Identity,batchIndex))
                let receipt=ingest target true bytes
                use receiptDocument=JsonDocument.Parse receipt
                Assert.Equal("applied",receiptDocument.RootElement.GetProperty("status").GetString())
            let position identity=identities |> Seq.findIndex(fun (candidate,_)->candidate=identity)
            let batchOf identity=identities |> Seq.find(fun (candidate,_)->candidate=identity) |> snd
            Assert.True(batchOf "binding-63" < batchOf "aaa-run")
            Assert.True(position "aaa-run" < position "aaa-job")
            Assert.True(position "aaa-job" < position "aaa-step")
        finally Directory.Delete(parent,true)

    [<Fact>]
    let ``large historical export obeys native event and byte bounds`` () =
        let parent=privateParent()
        try
            let source=createRoot parent "source" false
            let target=createRoot parent "target" true
            for page in 0..54 do
                let events=[for offset in 0..63 do let index=page*64+offset in if index<3449 then yield item ($"item-%04d{index}") 0 "null"]
                if not events.IsEmpty then ingest source false (payload ($"source-%d{page}") page events) |> ignore
            let output=Path.Combine(parent,"export")
            TelemetryStoreApplication.exportHistorical source approved target approved scope output |> Result.defaultWith(fun errors->failwith(String.concat "; " errors)) |> ignore
            use manifest=JsonDocument.Parse(File.ReadAllBytes(Path.Combine(output,"manifest.json")))
            Assert.Equal(3449,manifest.RootElement.GetProperty("disposition").GetProperty("emitted").GetInt32())
            for entry in manifest.RootElement.GetProperty("batches").EnumerateArray() do
                Assert.InRange(entry.GetProperty("eventCount").GetInt32(),1,TelemetryStore.MaxEvents)
                Assert.InRange(entry.GetProperty("bytes").GetInt32(),1,TelemetryStore.MaxBatchBytes)
                let bytes=File.ReadAllBytes(Path.Combine(output,entry.GetProperty("file").GetString()))
                Assert.True(TelemetryStore.parseBatch bytes |> Result.isOk)
        finally Directory.Delete(parent,true)
