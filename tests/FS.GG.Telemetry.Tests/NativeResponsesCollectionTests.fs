namespace FS.GG.Telemetry.Tests

open System
open System.Reflection
open System.Threading
open System.Text
open Xunit
open FS.GG.Coord
open FS.GG.Telemetry.Host

module NativeResponsesCollectionTests =
    [<Fact>]
    let ``capture and verified capture have no public caller constructor`` () =
        for candidate in [ typeof<NativeResponsesCollection.Capture>; typeof<NativeResponsesCollection.VerifiedCapture> ] do
            Assert.Empty(candidate.GetConstructors(BindingFlags.Public ||| BindingFlags.Instance))
            Assert.Empty(candidate.GetMethods(BindingFlags.Public ||| BindingFlags.Static))

    [<Fact>]
    let ``absent owned capture cannot become verified source authority`` () =
        let result = NativeResponsesCollection.verify "/absent/config.json" Unchecked.defaultof<HostConfig> "/absent/store" Unchecked.defaultof<NativeResponsesCollection.Capture>
        match result with
        | Ok _ -> failwith "caller absence became verified capture"
        | Error failure ->
            Assert.True failure.ClaimReceipt.IsNone
            Assert.True failure.ClaimAttemptId.IsNone
            Assert.NotEmpty failure.Errors

    [<Fact>]
    let ``absent installed authority refuses before queue claim or provider request`` () = task {
        let principal: TelemetryReceipt.Principal =
            { Scope = { Workspace = "fixture"; Producer = "runtime"; Stream = "stream" }
              Role = TelemetryReceipt.Generic; GrantId = None; GrantGeneration = None }
        let request: NativeResponses.Request =
            { Instructions = "fixture"; InputText = "{}"; SchemaName = "fixture"; SchemaJson = Encoding.UTF8.GetBytes "{}" }
        let! result = NativeResponsesCollection.collect "/absent/responses-host/config.json" Unchecked.defaultof<HostConfig>
                        "/absent/store" principal "dispatch" "item" (Encoding.UTF8.GetBytes "{}") request CancellationToken.None
        match result with
        | Ok _ -> failwith "absent installed authority reached provider"
        | Error failure ->
            Assert.True failure.ClaimReceipt.IsNone
            Assert.True failure.ClaimAttemptId.IsNone
            Assert.NotEmpty failure.Errors
    }

    let private evidenceInput () =
        use document = System.Text.Json.JsonDocument.Parse("""{"schema":"fsgg.telemetry.efficiency-evidence-packet/1","subject":{"itemId":"A&BÅ","outcomeId":"source-a","outcomeEpoch":1,"scope":"native-item"},"coverage":{"population":"unknown","usage":"unknown","classification":"unknown","lineage":"unknown","dependency":"unknown"},"omissions":[],"records":[]}""")
        let raw = EfficiencyEvidence.encode document.RootElement |> Result.defaultWith failwith
        Encoding.UTF8.GetString raw

    [<Fact>]
    let ``preclaim packet gate returns exact retained evidence codec bytes`` () =
        let input = evidenceInput()
        match NativeResponsesCollection.validateInputPacket input with
        | Error errors -> failwithf "%A" errors
        | Ok bytes -> Assert.True((bytes = Encoding.UTF8.GetBytes input))

    [<Theory>]
    [<InlineData("whitespace")>]
    [<InlineData("property-order")>]
    [<InlineData("other-json-codec")>]
    let ``preclaim packet gate refuses altered representation without rewriting`` (scenario: string) =
        let input = evidenceInput()
        let altered =
            if scenario = "whitespace" then " " + input
            elif scenario = "other-json-codec" then
                CanonicalJson.canonicalize(Encoding.UTF8.GetBytes input) |> Result.defaultWith failwith
            else
                let original = System.Text.Json.Nodes.JsonNode.Parse(input).AsObject()
                let reordered = System.Text.Json.Nodes.JsonObject()
                for pair in original |> Seq.rev do reordered.[pair.Key] <- pair.Value.DeepClone()
                reordered.ToJsonString()
        Assert.NotEqual<string>(input, altered)
        match NativeResponsesCollection.validateInputPacket altered with
        | Ok _ -> failwith "noncanonical request would reach claim"
        | Error errors -> Assert.Contains("responses-evidence-packet-not-canonical", errors)
