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
