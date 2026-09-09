namespace FS.GG.Coord.Tests

open System
open System.Text
open Xunit
open FS.GG.Coord

module TelemetryStoreTests =
    let private bytes (value: string) = Encoding.UTF8.GetBytes value
    let private validBatch ingest identity revision : byte array =
        bytes $"""{{"schema":"%s{TelemetryStore.BatchSchema}","ingestId":"%s{ingest}","sourceIdentity":"worker-a","generation":"g1","cursor":"c1","eventCount":1,"events":[{{"kind":"usage","identity":"%s{identity}","itemId":"UTEL-02","revision":%d{revision},"provider":"OpenAI","model":"sol","effort":"medium","input":10,"cachedInput":2,"cacheWriteInput":1,"output":5,"reasoning":2,"total":15,"responses":1,"sessions":1,"turns":1}}]}}"""

    [<Fact>]
    let ``UTEL-02 typed batches enforce closed fields counts arithmetic and native identity`` () =
        let accepted = TelemetryStore.parseBatch (validBatch "batch-1" "usage-1" 0L)
        Assert.True(Result.isOk accepted)
        let unknown = Encoding.UTF8.GetString(validBatch "batch-1" "usage-1" 0L).Replace("\"turns\":1", "\"turns\":1,\"privatePath\":\"/secret\"") |> bytes
        Assert.Contains("unknown field", sprintf "%A" (TelemetryStore.parseBatch unknown))
        let mismatched = Encoding.UTF8.GetString(validBatch "batch-1" "usage-1" 0L).Replace("\"eventCount\":1", "\"eventCount\":2") |> bytes
        Assert.Contains("eventCount", sprintf "%A" (TelemetryStore.parseBatch mismatched))
        let overflow = Encoding.UTF8.GetString(validBatch "batch-1" "usage-1" 0L).Replace("\"input\":10", "\"input\":9223372036854775807") |> bytes
        Assert.Contains("overflows", sprintf "%A" (TelemetryStore.parseBatch overflow))
        Assert.True(TelemetryStore.parseBatch (Array.zeroCreate (TelemetryStore.MaxBatchBytes + 1)) |> Result.isError)

    [<Fact>]
    let ``UTEL-02 public reduction keeps unknown coverage separate from qualification`` () =
        let batch = TelemetryStore.parseBatch (validBatch "batch-1" "usage-1" 0L) |> Result.defaultWith (sprintf "%A" >> failwith)
        let summary = TelemetryStore.reduce "UTEL-02" batch.Facts |> Result.defaultWith (sprintf "%A" >> failwith)
        Assert.Equal("unknown", summary.PopulationCoverage)
        Assert.Equal("not-evaluated", summary.Qualification)
        Assert.Equal(15L, summary.Total)
        Assert.DoesNotContain("worker-a", TelemetryStore.publicJson summary)

    [<Fact>]
    let ``UTEL-02 durability assessment fails closed`` () =
        Assert.True(TelemetryStore.validateStoreRoot "relative" TelemetryStore.ApprovedLocalDurable |> Result.isError)
        Assert.Contains("unsafe", sprintf "%A" (TelemetryStore.validateStoreRoot "/durable/store" (TelemetryStore.Unsafe "network filesystem")))
        Assert.Contains("durability-unverified", sprintf "%A" (TelemetryStore.validateStoreRoot "/durable/store" (TelemetryStore.DurabilityUnverified "unknown mount")))

    [<Fact>]
    let ``UTEL-02 batch observation count is capped at 64`` () =
        let events =
            [ 0 .. 64 ]
            |> List.map (fun index -> $"""{{"kind":"item","identity":"item-%d{index}","itemId":"UTEL-02","revision":0}}""")
            |> String.concat ","
        let payload = bytes $"""{{"schema":"%s{TelemetryStore.BatchSchema}","ingestId":"batch-1","sourceIdentity":"worker-a","generation":"g1","cursor":"c1","eventCount":65,"events":[%s{events}]}}"""
        Assert.Contains("64 events", sprintf "%A" (TelemetryStore.parseBatch payload))

    [<Fact>]
    let ``UTEL-08 review activity attribution and complication facts are closed and bounded`` () =
        let digest = String.replicate 64 "a"
        let review = $"""{{"kind":"process-review","identity":"review-1","itemId":"UTEL-08","revision":1,"scope":"attempt","attemptId":"attempt-1","outcomeSynopsis":"Delivered","wentWell":["Focused checks"],"problems":[],"avoidableDelayOrRework":[],"processObservations":["Routine route held"],"remainingRisks":[],"concreteImprovements":["Keep the focused gate"],"evidence":[{{"kind":"test","digest":"{digest}"}}],"evidenceCoverage":"partial","populationCoverage":"complete","confidence":"high","reviewerModel":"gpt-5","reviewerEffort":"medium","reviewedAt":"2026-09-09T09:00:00Z","durationSeconds":30}}"""
        let activity = $"""{{"kind":"activity-span","identity":"span-1","itemId":"UTEL-08","revision":0,"activityId":"implementation-1","invocationId":"invoke-1","attemptId":"attempt-1","category":"implementation","startedAt":"2026-09-09T08:00:00Z","endedAt":null,"clockProvenance":"host-wall","evidence":[],"summary":"implementation"}}"""
        let attribution = """{"kind":"activity-usage-attribution","identity":"attribute-1","itemId":"UTEL-08","revision":0,"usageIdentity":"usage-1","activityId":null,"classification":"unclassified","input":10,"cachedInput":2,"output":5,"reasoning":null,"total":15}"""
        let complication = $"""{{"kind":"complication","identity":"complication-1","itemId":"UTEL-08","revision":0,"attemptId":"attempt-1","activityId":"implementation-1","trigger":"test-failure","cause":"product-defect","occurredAt":"2026-09-09T08:30:00Z","synopsis":"A focused test exposed a defect","evidence":[{{"kind":"test","digest":"{digest}"}}]}}"""
        let payload events = bytes $"""{{"schema":"{TelemetryStore.BatchSchema}","ingestId":"review-batch","sourceIdentity":"reviewer","generation":"g1","cursor":"1","eventCount":{List.length events},"events":[{String.concat "," events}]}}"""
        Assert.True(TelemetryStore.parseBatch (payload [ review; activity; attribution; complication ]) |> Result.isOk)
        Assert.Contains("unknown field", sprintf "%A" (TelemetryStore.parseBatch (payload [ review.Replace("\"durationSeconds\":30", "\"durationSeconds\":30,\"prompt\":\"secret\"") ])))
        Assert.Contains("exceeds 8 entries", sprintf "%A" (TelemetryStore.parseBatch (payload [ review.Replace("[\"Focused checks\"]", "[\"1\",\"2\",\"3\",\"4\",\"5\",\"6\",\"7\",\"8\",\"9\"]") ])))
        Assert.Contains("classification", sprintf "%A" (TelemetryStore.parseBatch (payload [ attribution.Replace("\"classification\":\"unclassified\"", "\"classification\":\"direct\"") ])))
