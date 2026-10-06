namespace FS.GG.Coord.Tests

open System
open System.Text
open System.Text.Json.Nodes
open Xunit
open FS.GG.Coord

/// Synthetic structural fixtures confer no installed capability or collector authority.
module NativeExecStoreTests =
    let private digest = String.replicate 64 "a"
    let private thread = "11111111-1111-4111-8111-111111111111"
    let private textArray (value: string) =
        let array = JsonArray()
        array.Add(JsonValue.Create value)
        array
    let private reference kind = JsonNode.Parse($"""{{"id":"ref-{kind}","kind":"{kind}","revision":0,"contentDigest":"sha256:{digest}"}}""")
    let private turn completed =
        let node = JsonNode.Parse($"""{{"localTurnKey":{{"captureSha256":"{digest}","threadId":"{thread}","startFrameOrdinal":1}},"turnSequence":1,"nativeTurnId":null}}""")
        if completed then
            node["status"] <- JsonValue.Create "completed"
            node["usageAvailable"] <- JsonValue.Create true
        node
    let private binding () =
        let node = JsonNode.Parse($"""{{"schema":"fsgg.telemetry.native-inventory-source-binding/2","producerIdentity":"fsgg-work-roadmap-native-collector/1","capturedAt":"2026-10-06T00:22:00Z","hostSource":"codex-exec-jsonl","rootInvocationId":"invoke","invocationId":"invoke","revision":0,"threadId":"{thread}","captureSha256":"{digest}","captureBytes":123,"commandBindingSha256":"{digest}","custodyReceiptSha256":"{digest}","claimRef":{{"requestId":"request","claimId":"claim","revision":1,"contentDigest":"sha256:{digest}","owner":{{"producer":"producer","stream":"stream"}},"generation":1}},"turnRoster":[]}}""")
        for name,kind in ["dispatchRef","expected-dispatch";"runtimeStartRef","runtime-start";"installedOriginRef","learn-installed-origin/1"] do
            node[name] <- reference kind
        node["turnRoster"].AsArray().Add(turn true)
        node
    let private source (bound: JsonNode) =
        let raw = CanonicalJson.canonicalize(Encoding.UTF8.GetBytes(bound.ToJsonString())) |> Result.defaultWith failwith |> Encoding.UTF8.GetBytes
        let envelope = JsonNode.Parse($"""{{"schema":"fsgg.telemetry.native-inventory-source-binding/2","producerIdentity":"fsgg-work-roadmap-native-collector/1","sha256":"{CanonicalJson.sha256 raw}","bytesBase64":"{Convert.ToBase64String raw}"}}""")
        let event = JsonNode.Parse($"""{{"kind":"runtime-native-inventory-source/1","identity":"source","itemId":"A","revision":0,"inventoryId":"inventory","originalItemId":"A","invocationId":"invoke","sourceDigest":"{digest}"}}""")
        event["sourceBinding"] <- envelope
        event
    let private inventory () =
        let node = JsonNode.Parse($"""{{"kind":"runtime-native-inventory/1","identity":"inventory","itemId":"A","revision":0,"inventoryId":"inventory","originalItemId":"A","invocationId":"invoke","page":1,"pages":1,"turnNamespace":"codex-exec-jsonl/1","expectedTurns":[],"expectedProvider":"openai","requestedModel":"sol","requestedEffort":"medium","support":"provider-native-final-turn-counters","followupBaseline":0,"capturedAt":"2026-10-06T00:22:00Z","sourceKind":"provider-capability-and-dispatch-roster","sourceDigest":"{digest}"}}""")
        node["expectedTurns"].AsArray().Add(turn false)
        node
    let private parse (event: JsonNode) =
        Encoding.UTF8.GetBytes($"""{{"schema":"{TelemetryStore.BatchSchema}","ingestId":"fixture","sourceIdentity":"synthetic","generation":"1","cursor":"1","eventCount":1,"events":[{event.ToJsonString()}]}}""") |> TelemetryStore.parseBatch

    [<Fact>]
    let ``exec inventory has a typed capture-local key not an AppServer turn identity`` () =
        Assert.True(parse(source(binding())) |> Result.isOk)
        match parse(inventory()) with
        | Ok batch ->
            match batch.Facts.Head.Payload with
            | TelemetryStore.RuntimeExecNativeInventory(_,_,_,key,_,_,_,_,_,_) ->
                Assert.Equal(1L,key.TurnSequence)
                Assert.Equal(1L,key.StartFrameOrdinal)
                Assert.Equal(Guid.Parse thread,key.ThreadId)
            | _ -> Assert.Fail "wrong namespace"
        | Error errors -> Assert.Fail(sprintf "%A" errors)

    [<Fact>]
    let ``legacy inventory remains provider ID only and exec cannot substitute IDs`` () =
        let old = inventory()
        old.AsObject().Remove "turnNamespace" |> ignore
        old.AsObject().Remove "expectedTurns" |> ignore
        old["expectedTurnIds"] <- textArray "native-turn"
        Assert.True(parse old |> Result.isOk)
        let mixed = inventory()
        mixed["expectedTurnIds"] <- textArray "native-turn"
        Assert.True(parse mixed |> Result.isError)
        old["expectedTurns"] <- inventory()["expectedTurns"].DeepClone()
        Assert.True(parse old |> Result.isError)

    [<Theory>]
    [<InlineData("turnSequence", "0")>]
    [<InlineData("turnSequence", "1.0")>]
    [<InlineData("turnSequence", "true")>]
    [<InlineData("nativeTurnId", "\"invented\"")>]
    let ``exec row refuses invented provider IDs and non-runtime ordinals`` (field: string) (raw: string) =
        let node = inventory()
        node["expectedTurns"][0][field] <- JsonNode.Parse raw
        Assert.True(parse node |> Result.isError)

    [<Theory>]
    [<InlineData("-1")>]
    [<InlineData("4096")>]
    [<InlineData("0.5")>]
    [<InlineData("9223372036854775808")>]
    let ``capture local frame ordinal is bounded strict integer`` (raw: string) =
        let node = binding()
        node["turnRoster"][0]["localTurnKey"]["startFrameOrdinal"] <- JsonNode.Parse raw
        Assert.True(parse(source node) |> Result.isError)

    [<Fact>]
    let ``claim reference is closed queue history not an ingest reference`` () =
        let node = binding()
        node["claimRef"] <- reference "runtime-start"
        Assert.True(parse(source node) |> Result.isError)
        for name in ["dispatchRef";"runtimeStartRef";"installedOriginRef"] do
            let changed = binding()
            changed[name]["contentDigest"] <- JsonValue.Create digest
            Assert.True(parse(source changed) |> Result.isError)

    [<Fact>]
    let ``binding refuses unknown fields mismatched local capture and invalid byte bounds`` () =
        let mutate action =
            let node = binding()
            action node
            Assert.True(parse(source node) |> Result.isError)
        mutate(fun node -> node["orderedTurnIds"] <- textArray "native-turn")
        mutate(fun node -> node["turnRoster"][0]["localTurnKey"]["threadId"] <- JsonValue.Create "22222222-2222-4222-8222-222222222222")
        mutate(fun node -> node["captureSha256"] <- JsonValue.Create(String.replicate 64 "0"))
        mutate(fun node -> node["captureBytes"] <- JsonValue.Create 0)
        mutate(fun node -> node["captureBytes"] <- JsonValue.Create 262145)

    [<Fact>]
    let ``fresh exec origin is explicit and unknown null variants are refused`` () =
        let node = JsonNode.Parse($"""{{"kind":"learn-installed-origin/1","identity":"origin","revision":0,"workspaceId":"workspace","producerId":"producer","streamId":"stream","role":"native-collector","grantId":"grant","grantGeneration":1,"managerReceiptSha256":"{digest}","capabilityProfileSha256":"{digest}","capabilityResultSha256":"{digest}","nativeCaptureSha256":"{digest}","nativeVerificationSha256":"{digest}","capabilityObservedAt":"2026-10-06T00:20:00Z","capabilityExpiresAt":"2026-10-06T00:30:00Z","installationSha256":"{digest}"}}""")
        Assert.True(parse node |> Result.isOk)
        node["nativeSourceVariant"] <- JsonValue.Create "codex-exec-jsonl/1"
        Assert.True(parse node |> Result.isOk)
        node["nativeSourceVariant"] <- JsonValue.Create "unknown"
        Assert.True(parse node |> Result.isError)
        node["nativeSourceVariant"] <- null
        Assert.True(parse node |> Result.isError)

    [<Fact>]
    let ``AppServer source binding keeps its explicit provider identity roster`` () =
        let legacy = JsonNode.Parse($"""{{"schema":"fsgg.telemetry.native-inventory-source-binding/1","producerIdentity":"fsgg-work-roadmap-native-collector/1","capturedAt":"2026-10-06T00:22:00Z","hostSource":"codex-app-server:thread/turns/list","rootInvocationId":"invoke","invocationId":"invoke","parentThreadId":"{thread}","threadId":"{thread}","orderedTurnIds":["native-turn"],"revision":0}}""")
        let event = source legacy
        event["sourceBinding"]["schema"] <- JsonValue.Create "fsgg.telemetry.native-inventory-source-binding/1"
        Assert.True(parse event |> Result.isOk)
        event["sourceBinding"]["schema"] <- JsonValue.Create "fsgg.telemetry.native-inventory-source-binding/2"
        Assert.True(parse event |> Result.isError)
