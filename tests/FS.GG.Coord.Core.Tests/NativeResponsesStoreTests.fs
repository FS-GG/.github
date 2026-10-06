namespace FS.GG.Coord.Tests

open System
open System.Text
open System.Text.Json
open System.Text.Json.Nodes
open Xunit
open FS.GG.Coord

/// Synthetic structural fixtures prove no installed collector or provider authority.
module NativeResponsesStoreTests =
    let private digest = String.replicate 64 "a"
    let private parse (event: JsonNode) =
        Encoding.UTF8.GetBytes($"""{{"schema":"{TelemetryStore.BatchSchema}","ingestId":"fixture","sourceIdentity":"synthetic","generation":"1","cursor":"1","eventCount":1,"events":[{event.ToJsonString()}]}}""") |> TelemetryStore.parseBatch
    let private usage () =
        JsonNode.Parse($"""{{"kind":"runtime-response-usage/1","identity":"usage-response","itemId":"A","revision":0,"invocationId":"invoke","provider":"openai","sourceVariant":"openai-responses/1","responseId":"opaque-provider-id","responseSha256":"{digest}","requestedModel":"sol","observedModel":"actual-sol","requestedEffort":"medium","observedEffort":null,"scope":"provider-response","provenance":"openai-responses","input":99,"cachedInput":null,"cacheWriteInput":null,"output":1,"reasoning":null,"total":100}}""")
    let private observation () =
        JsonNode.Parse($"""{{"kind":"runtime-provider-observation/1","identity":"provider-observed","itemId":"A","revision":0,"invocationId":"invoke","provider":"openai","sourceVariant":"openai-responses/1","responseId":"opaque-provider-id","responseSha256":"{digest}","generationRequestSha256":"{digest}","countRequestSha256":"{digest}","countResponseSha256":"{digest}","observedAt":"2026-10-06T00:22:00Z","providerCreatedAt":null,"status":"incomplete"}}""")
    let private reference (kind: string) =
        JsonNode.Parse($"""{{"id":"ref-{kind}","kind":"{kind}","revision":0,"contentDigest":"sha256:{digest}"}}""")
    let private resource () =
        JsonNode.Parse($"""{{"responseId":"opaque-provider-id","responseSha256":"{digest}"}}""")
    let private binding () =
        let node=JsonNode.Parse($"""{{"schema":"fsgg.telemetry.native-inventory-source-binding/3","producerIdentity":"fsgg-work-roadmap-native-collector/1","capturedAt":"2026-10-06T00:22:00Z","sourceVariant":"openai-responses/1","originalItemId":"A","invocationId":"invoke","revision":0,"responseId":"opaque-provider-id","responseSha256":"{digest}","generationRequestSha256":"{digest}","countRequestSha256":"{digest}","countResponseSha256":"{digest}","responseBytes":123,"operationBindingSha256":"{digest}","claimRef":{{"requestId":"request","claimId":"claim","revision":1,"contentDigest":"sha256:{digest}","owner":{{"producer":"producer","stream":"stream"}},"generation":1}},"expectedResponses":[]}}""")
        for name,kind in ["dispatchRef","expected-dispatch";"providerObservationRef","runtime-provider-observation/1";"usageRef","runtime-response-usage/1";"installedOriginRef","learn-installed-origin/1"] do
            node.[name] <- reference kind
        node.["expectedResponses"].AsArray().Add(resource())
        node
    let private source (bound: JsonNode) =
        let raw=CanonicalJson.canonicalize(Encoding.UTF8.GetBytes(bound.ToJsonString())) |> Result.defaultWith failwith |> Encoding.UTF8.GetBytes
        let node=JsonNode.Parse($"""{{"kind":"runtime-native-inventory-source/1","identity":"source","itemId":"A","revision":0,"inventoryId":"inventory","originalItemId":"A","invocationId":"invoke","sourceDigest":"{digest}"}}""")
        node.["sourceBinding"] <- JsonNode.Parse($"""{{"schema":"fsgg.telemetry.native-inventory-source-binding/3","producerIdentity":"fsgg-work-roadmap-native-collector/1","sha256":"{CanonicalJson.sha256 raw}","bytesBase64":"{Convert.ToBase64String raw}"}}""")
        node

    [<Fact>]
    let ``provider response identity has no synthetic thread or turn`` () =
        match parse(usage()) with
        | Ok batch ->
            match batch.Facts.Head.Payload with
            | TelemetryStore.RuntimeResponseUsage value ->
                Assert.Equal("opaque-provider-id",value.Resource.ResponseId)
                Assert.Equal(Some 99L,value.Input)
                Assert.True(value.CachedInput.IsNone && value.CacheWriteInput.IsNone && value.Reasoning.IsNone)
            | _ -> Assert.Fail "wrong namespace"
        | Error errors -> Assert.Fail(sprintf "%A" errors)
        let node=usage()
        node.["threadId"] <- JsonValue.Create "fabricated-thread"
        Assert.True(parse node |> Result.isError)

    [<Fact>]
    let ``partial response counters preserve genuine siblings without zero defaults`` () =
        let node=usage()
        node.["output"] <- null
        node.["total"] <- null
        match parse node with
        | Ok batch ->
            match batch.Facts.Head.Payload with
            | TelemetryStore.RuntimeResponseUsage value ->
                Assert.Equal(Some 99L,value.Input)
                Assert.True(value.Output.IsNone && value.Total.IsNone)
            | _ -> Assert.Fail "wrong namespace"
        | Error errors -> Assert.Fail(sprintf "%A" errors)

    [<Theory>]
    [<InlineData("-1")>]
    [<InlineData("0.5")>]
    [<InlineData("true")>]
    [<InlineData("\"1\"")>]
    [<InlineData("9223372036854775808")>]
    [<InlineData("1e2")>]
    let ``response counters require exact nonnegative Int64`` (raw: string) =
        let node=usage()
        node.["input"] <- JsonNode.Parse raw
        Assert.True(parse node |> Result.isError)

    [<Fact>]
    let ``response totals and inclusive breakouts cannot inflate source cost`` () =
        for field,value in ["total",101L;"cachedInput",100L;"cacheWriteInput",100L;"reasoning",2L] do
            let node=usage()
            node.[field] <- JsonValue.Create value
            Assert.True(parse node |> Result.isError)
        let overflow=usage()
        overflow.["input"] <- JsonValue.Create Int64.MaxValue
        overflow.["total"] <- JsonValue.Create Int64.MaxValue
        Assert.True(parse overflow |> Result.isError)

    [<Fact>]
    let ``incomplete observation is structural evidence not invented successful start`` () =
        Assert.True(parse(observation()) |> Result.isOk)
        let node=observation()
        node.["processId"] <- JsonValue.Create 123
        Assert.True(parse node |> Result.isError)
        node.AsObject().Remove "processId" |> ignore
        node.["status"] <- JsonValue.Create "success"
        Assert.True(parse node |> Result.isError)

    [<Fact>]
    let ``response source binds exact response roster and queue history`` () =
        Assert.True(parse(source(binding())) |> Result.isOk)
        let node=binding()
        node.["expectedResponses"].[0].["responseId"] <- JsonValue.Create "another-response"
        Assert.True(parse(source node) |> Result.isError)
        let changed=binding()
        changed.["claimRef"] <- reference "runtime-provider-observation/1"
        Assert.True(parse(source changed) |> Result.isError)

    [<Theory>]
    [<InlineData("responseBytes", "0")>]
    [<InlineData("responseBytes", "262145")>]
    [<InlineData("turnSequence", "1")>]
    [<InlineData("threadId", "\"invented\"")>]
    let ``response source refuses empty overbound or foreign identity fields`` (field: string) (raw: string) =
        let node=binding()
        node.[field] <- JsonNode.Parse raw
        Assert.True(parse(source node) |> Result.isError)

    [<Fact>]
    let ``public Core reducer counts response resource and keeps unknown breakouts nullable`` () =
        let facts = parse(usage()) |> Result.defaultWith (fun errors -> failwithf "%A" errors)
        let reduced = TelemetryStore.reduce "A" facts.Facts |> Result.defaultWith (fun errors -> failwithf "%A" errors)
        use result = JsonDocument.Parse(TelemetryStore.publicJson reduced)
        Assert.Equal(1L,result.RootElement.GetProperty("usageObservations").GetInt64())
        let counters = result.RootElement.GetProperty "usage"
        Assert.Equal(99L,counters.GetProperty("input").GetInt64())
        Assert.Equal(100L,counters.GetProperty("total").GetInt64())
        for name in ["cachedInput";"cacheWriteInput";"reasoning"] do
            Assert.Equal(JsonValueKind.Null,counters.GetProperty(name).ValueKind)

    [<Fact>]
    let ``public Core reducer never converts unknown response cost into zero`` () =
        let node = usage()
        node.["output"] <- null
        node.["total"] <- null
        let facts = parse node |> Result.defaultWith (fun errors -> failwithf "%A" errors)
        let reduced = TelemetryStore.reduce "A" facts.Facts |> Result.defaultWith (fun errors -> failwithf "%A" errors)
        use result = JsonDocument.Parse(TelemetryStore.publicJson reduced)
        let counters = result.RootElement.GetProperty "usage"
        Assert.Equal(99L,counters.GetProperty("input").GetInt64())
        Assert.Equal(JsonValueKind.Null,counters.GetProperty("output").ValueKind)
        Assert.Equal(JsonValueKind.Null,counters.GetProperty("total").ValueKind)

    [<Fact>]
    let ``public Core reducer refuses duplicate response resource identities`` () =
        let first = parse(usage()) |> Result.defaultWith (fun errors -> failwithf "%A" errors)
        let duplicate = usage()
        duplicate.["identity"] <- JsonValue.Create "second-response-usage"
        let second = parse duplicate |> Result.defaultWith (fun errors -> failwithf "%A" errors)
        Assert.True(TelemetryStore.reduce "A" (first.Facts @ second.Facts) |> Result.isError)

    [<Theory>]
    [<InlineData("100", "null", "1", "null", "null", "null")>]
    [<InlineData("null", "100", "1", "null", "null", "null")>]
    [<InlineData("null", "1", "1", "10", "null", "null")>]
    [<InlineData("null", "1", "1", "null", "10", "null")>]
    [<InlineData("99", "null", "100", "null", "null", "101")>]
    [<InlineData("null", "null", "1", "null", "null", "10")>]
    let ``known partial response counters cannot exceed observed inclusive whole``
        (input: string) (output: string) (total: string) (cached: string) (written: string) (reasoning: string) =
        let node = usage()
        for field,raw in ["input",input;"output",output;"total",total;"cachedInput",cached;"cacheWriteInput",written;"reasoning",reasoning] do
            node.[field] <- JsonNode.Parse raw
        Assert.True(parse node |> Result.isError)

    [<Fact>]
    let ``partial containment does not invent counters or disjoint cached subsets`` () =
        let node = usage()
        node.["output"] <- null
        node.["total"] <- null
        node.["cachedInput"] <- JsonValue.Create 99L
        node.["cacheWriteInput"] <- JsonValue.Create 99L
        node.["reasoning"] <- null
        match parse node with
        | Ok batch ->
            match batch.Facts.Head.Payload with
            | TelemetryStore.RuntimeResponseUsage value ->
                Assert.True(value.Output.IsNone && value.Total.IsNone && value.Reasoning.IsNone)
                Assert.Equal(Some 99L,value.CachedInput)
                Assert.Equal(Some 99L,value.CacheWriteInput)
            | _ -> Assert.Fail "wrong namespace"
        | Error errors -> Assert.Fail(sprintf "%A" errors)

    [<Fact>]
    let ``structurally consistent over policy response cost remains a factual observation`` () =
        let node = usage()
        node.["input"] <- JsonValue.Create 9000L
        node.["output"] <- JsonValue.Create 2000L
        node.["total"] <- JsonValue.Create 11000L
        Assert.True(parse node |> Result.isOk)

    [<Theory>]
    [<InlineData("null", "1", "10", "10", "null", "null")>]
    [<InlineData("null", "null", "null", "9223372036854775807", "null", "1")>]
    let ``joint response subsets must fit one inclusive Int64 total``
        (input: string) (output: string) (total: string) (cached: string) (written: string) (reasoning: string) =
        let node = usage()
        for field,raw in ["input",input;"output",output;"total",total;"cachedInput",cached;"cacheWriteInput",written;"reasoning",reasoning] do
            node.[field] <- JsonNode.Parse raw
        Assert.True(parse node |> Result.isError)
