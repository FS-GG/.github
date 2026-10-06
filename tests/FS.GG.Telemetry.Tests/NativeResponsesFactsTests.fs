namespace FS.GG.Telemetry.Tests

open System
open System.Text
open System.Text.Json.Nodes
open Xunit
open FS.GG.Coord
open FS.GG.Telemetry.Host

module NativeResponsesFactsTests =
    let private unwrap = function Ok value -> value | Error errors -> failwithf "%A" errors
    let private digest = String.replicate 64 "a"
    let private origin : NativeResponsesCollection.OriginEvidence =
        { Principal={ Scope={Workspace="workspace";Producer="native";Stream="stream"};Role=TelemetryReceipt.NativeCollector;GrantId=Some "grant";GrantGeneration=Some 1L }
          ManagerReceiptSha256=digest;CapabilityProfileSha256=digest;CapabilityResultSha256=digest;InstallationSha256=digest
          ObservedAt="2026-10-06T00:00:00Z";ExpiresAt="2026-10-06T01:00:00Z" }
    let private operation : NativeResponsesCollection.OperationEvidence =
        { OperationId="response-operation";ItemId="A";OriginalItemId="A";InvocationId="invoke"
          DispatchRef=Encoding.UTF8.GetBytes($"""{{"id":"dispatch","kind":"expected-dispatch","revision":0,"contentDigest":"sha256:{digest}"}}""")
          ClaimRef=Encoding.UTF8.GetBytes($"""{{"requestId":"request","claimId":"claim","revision":1,"contentDigest":"sha256:{digest}","owner":{{"producer":"owner","stream":"owner-stream"}},"generation":1}}""")
          GenerationRequestSha256=digest;CountRequestSha256=digest;CountResponseSha256=digest;ResponseSha256=digest
          ResponseBytes=123;CaptureSha256=digest;VerificationSha256=digest;ObservedAt="2026-10-06T00:00:01Z" }
    let private response (usage: string) =
        Encoding.UTF8.GetBytes($"""{{"object":"response","id":"resp_actual","model":"{NativeResponses.Model}","status":"incomplete","usage":{usage},"output":[]}}""")
        |> NativeResponses.decodeResponse |> unwrap
    let private emit usage = NativeResponsesFacts.renderFixture origin operation (response usage) |> unwrap
    let private events (packet: NativeResponsesFacts.Packet) =
        let envelope = JsonNode.Parse(Encoding.UTF8.GetString packet.EnvelopeBytes)
        envelope.["payload"].["events"].AsArray()

    [<Fact>]
    let ``all unknown costs emit only origin and actual provider observation`` () =
        let packet = emit "null"
        Assert.Equal(2,(events packet).Count)
        Assert.True(packet.UsageRef.IsNone)
        Assert.True(packet.InventoryRef.IsNone)
        Assert.True(packet.SourceRef.IsNone)
        TelemetryReceipt.parse packet.EnvelopeBytes |> unwrap |> ignore

    [<Theory>]
    [<InlineData(17L, 7L, 24L)>]
    [<InlineData(8001L, 1501L, 9502L)>]
    let ``consistent cost facts preserve measured quantities without policy trimming`` (input: int64) (output: int64) (total: int64) =
        let packet = emit $"""{{"input_tokens":{input},"output_tokens":{output},"total_tokens":{total}}}"""
        let actual = events packet
        Assert.Equal(5,actual.Count)
        Assert.Equal(input,actual.[2].["input"].GetValue<int64>())
        Assert.Equal(output,actual.[2].["output"].GetValue<int64>())
        Assert.Equal(total,actual.[2].["total"].GetValue<int64>())
        Assert.Null(actual.[2].["reasoning"])
        Assert.Equal(operation.ObservedAt,actual.[1].["observedAt"].GetValue<string>())
        Assert.Equal(origin.ObservedAt,actual.[0].["capabilityObservedAt"].GetValue<string>())
        let bindingBytes = Convert.FromBase64String(actual.[4].["sourceBinding"].["bytesBase64"].GetValue<string>())
        Assert.Equal(CanonicalJson.sha256 bindingBytes,actual.[4].["sourceBinding"].["sha256"].GetValue<string>())
        let binding = JsonNode.Parse(Encoding.UTF8.GetString bindingBytes)
        Assert.Equal(operation.CaptureSha256,binding.["operationBindingSha256"].GetValue<string>())
        Assert.Equal(Encoding.UTF8.GetString packet.ObservationRef,binding.["providerObservationRef"].ToJsonString() |> fun value -> CanonicalJson.canonicalize(Encoding.UTF8.GetBytes value) |> unwrap)

    [<Fact>]
    let ``partial authentic quantities never fill missing parent counters`` () =
        let packet = emit "{\"input_tokens\":17,\"output_tokens\":null,\"total_tokens\":null}"
        let usage = (events packet).[2]
        Assert.Equal(17L,usage.["input"].GetValue<int64>())
        Assert.Null usage.["output"]
        Assert.Null usage.["total"]
        Assert.True(packet.UsageRef.IsSome)

    [<Fact>]
    let ``declaration renderer refuses generic grant and source digest mutation fails canonical decoder`` () =
        let generic = { origin with Principal=TelemetryReceipt.genericPrincipal origin.Principal.Scope }
        Assert.True(NativeResponsesFacts.renderFixture generic operation (response "null") |> Result.isError)
        let packet = emit "{\"input_tokens\":17,\"output_tokens\":7,\"total_tokens\":24}"
        let node = JsonNode.Parse(Encoding.UTF8.GetString packet.EnvelopeBytes)
        node.["payload"].["events"].[4].["sourceBinding"].["sha256"] <- JsonValue.Create(String.replicate 64 "b")
        Assert.True(TelemetryReceipt.parse (Encoding.UTF8.GetBytes(node.ToJsonString())) |> Result.isError)
