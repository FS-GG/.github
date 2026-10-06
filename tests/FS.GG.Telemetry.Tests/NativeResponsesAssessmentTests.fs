namespace FS.GG.Telemetry.Tests

open System
open System.Text
open System.Text.Json.Nodes
open Xunit
open FS.GG.Coord
open FS.GG.Telemetry.Host

module NativeResponsesAssessmentTests =
    let private utf8 = Encoding.UTF8
    let private unwrap = function Ok value -> value | Error errors -> failwithf "%A" errors
    let private canonical (node: JsonNode) =
        let value = CanonicalJson.canonicalize(utf8.GetBytes(node.ToJsonString())) |> unwrap
        utf8.GetBytes value
    let private model () = JsonNode.Parse("""{"schema":"fsgg.telemetry.efficiency-assessment/1","assessmentId":"assessment-native-item","revision":1,"supersedes":null,"subject":{"itemId":"A","outcomeId":"source-a","outcomeEpoch":1,"scope":"native-item"},"evidenceDigest":"sha256:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa","evidenceRefs":[{"id":"source-a","kind":"outcome","revision":1},{"id":"review-a","kind":"process-review","revision":1}],"coverage":{"population":"complete","usage":"complete","classification":"complete","lineage":"complete","dependency":"complete"},"omissions":[],"outcomeSynopsis":"Synthetic evidence is retained.","wentWell":["Source outcome has a stable canonical reference."],"findings":[],"metricRefs":[],"improvements":[],"provenance":{"producer":"synthetic-fixture","modelAlias":"fixture-model","promptVersion":"efficiency-prompt/1","rubricVersion":"efficiency-rubric/1","taxonomyVersion":"efficiency-taxonomy/1","analysisPolicyVersion":"efficiency-analysis-policy/1","usageRefs":[],"startedAt":"2026-10-06T00:22:00Z","finishedAt":"2026-10-06T00:23:00Z","validationResult":"accepted"},"lifecycle":{"state":"partial","idempotencyKey":"6b6ab0e4cda0153bb7f88d308acaa5ab66b9557522669a52374e253afc5f7677","failureReason":null,"itemReviewRef":null,"generatedAt":"2026-10-06T00:23:00Z"},"publication":{"visibility":"private","policyVersion":null}}""")
    let private finding () = JsonNode.Parse("""{"id":"finding-a","activity":"review","purpose":"useful-assurance","trigger":"missing-observation","primaryCause":"unknown","contributingCauses":[],"necessity":"required","epistemicStatus":"observed","severity":"info","summary":"Evidence is retained.","evidenceRefs":["source-a"],"affectedObjects":[{"id":"source-a","kind":"outcome","revision":1}],"alternative":null,"recoveryRefs":[],"uncertainty":"No further observations."}""")
    let private packet (assessment: JsonNode) =
        let records = JsonArray()
        for reference in assessment.["evidenceRefs"].AsArray() do
            let row = JsonObject()
            row.["ref"] <- reference.DeepClone()
            let canonicalRef = reference.DeepClone()
            canonicalRef.["kind"] <- JsonValue.Create(if reference.["kind"].GetValue<string>()="outcome" then "native-item-outcome" else "process-review")
            canonicalRef.["contentDigest"] <- JsonValue.Create("sha256:" + String.replicate 64 "a")
            row.["canonicalRef"] <- canonicalRef
            row.["itemId"] <- JsonValue.Create "A"
            row.["payload"] <- JsonObject()
            row.["priority"] <- JsonValue.Create 1
            row.["analysisGenerated"] <- JsonValue.Create false
            records.Add row
        let result = JsonObject()
        result.["schema"] <- JsonValue.Create "fsgg.telemetry.efficiency-evidence-packet/1"
        result.["subject"] <- assessment.["subject"].DeepClone()
        result.["coverage"] <- assessment.["coverage"].DeepClone()
        result.["omissions"] <- assessment.["omissions"].DeepClone()
        result.["records"] <- records
        result
    let private fixture (assessment: JsonNode) (evidence: JsonNode) : NativeResponsesAssessment.Input =
        let bytes = canonical evidence
        let digest = "sha256:" + CanonicalJson.sha256 bytes
        assessment.["evidenceDigest"] <- JsonValue.Create digest
        let keys = JsonArray()
        for name in ["itemId";"outcomeId";"outcomeEpoch";"scope"] do
            let value = assessment.["subject"].[name]
            keys.Add(if isNull value then null else value.DeepClone())
        keys.Add(JsonValue.Create digest)
        keys.Add(JsonValue.Create "efficiency-analysis-policy/1")
        let requestId = CanonicalJson.sha256(canonical keys)
        let request = JsonObject()
        request.["requestId"] <- JsonValue.Create requestId
        request.["analysisPolicyVersion"] <- JsonValue.Create "efficiency-analysis-policy/1"
        request.["subject"] <- assessment.["subject"].DeepClone()
        request.["evidenceDigest"] <- JsonValue.Create digest
        request.["authority"] <- JsonNode.Parse("""{"sourceIdentity":"fixture","authorityRole":"root-reviewer","authorityRef":{"id":"review-a","kind":"process-review","revision":1,"contentDigest":"sha256:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa"},"rootDispatchRef":"dispatch-a","invocationRef":"invocation-a"}""")
        { OutputText=assessment.ToJsonString();PacketBytes=bytes;CanonicalRequestBytes=canonical request
          RequestId=requestId;AnalysisUsageRef="native-usage";ClaimedAt="2026-10-06T00:22:00Z";ObservedAt="2026-10-06T00:23:00Z"
          OperationId="operation-a";Producer="fixture" }

    [<Theory>]
    [<InlineData("native-partial")>]
    [<InlineData("provisional-unknown-epoch")>]
    [<InlineData("supported-evidence")>]
    let ``valid declarations remain private partial without completion authority`` (scenario: string) =
        let assessment = model()
        if scenario="provisional-unknown-epoch" then
            assessment.["subject"].["scope"] <- JsonValue.Create "provisional-delivery"
            assessment.["subject"].["outcomeEpoch"] <- null
        if scenario="supported-evidence" then assessment.["findings"].AsArray().Add(finding())
        let input = fixture assessment (packet assessment)
        let prepared = NativeResponsesAssessment.prepare input |> unwrap
        let actual = JsonNode.Parse(utf8.GetString prepared)
        Assert.Equal("partial",actual.["lifecycle"].["state"].GetValue<string>())
        Assert.Null actual.["lifecycle"].["itemReviewRef"]
        Assert.Equal(input.RequestId,actual.["lifecycle"].["idempotencyKey"].GetValue<string>())
        Assert.Equal("private",actual.["publication"].["visibility"].GetValue<string>())
        Assert.Equal("responses-assessment-operation-a",actual.["assessmentId"].GetValue<string>())

    [<Theory>]
    [<InlineData("numeric-synopsis")>]
    [<InlineData("numeric-well")>]
    [<InlineData("numeric-finding")>]
    [<InlineData("numeric-uncertainty")>]
    [<InlineData("numeric-alternative")>]
    [<InlineData("numeric-mechanism")>]
    [<InlineData("numeric-validation")>]
    [<InlineData("affected-revision")>]
    [<InlineData("finding-evidence")>]
    [<InlineData("recovery-reference")>]
    [<InlineData("supported-no-evidence")>]
    [<InlineData("hypothesis-primary-cause")>]
    [<InlineData("avoidable-no-witness")>]
    [<InlineData("unresolved-metric")>]
    [<InlineData("coverage-altered")>]
    [<InlineData("omissions-altered")>]
    [<InlineData("supersession-unavailable")>]
    [<InlineData("outcome-unresolved")>]
    [<InlineData("ambiguous-finding-id")>]
    [<InlineData("ambiguous-nonreview-id")>]
    [<InlineData("request-key")>]
    [<InlineData("packet-raw-digest")>]
    [<InlineData("duplicate-model-field")>]
    [<InlineData("unknown-output-property")>]
    [<InlineData("unknown-taxonomy")>]
    let ``unsupported model claims and altered immutable joins are refused`` (scenario: string) =
        let assessment = model()
        let evidence = packet assessment
        let claim = finding()
        assessment.["findings"].AsArray().Add claim
        match scenario with
        | "numeric-synopsis" -> assessment.["outcomeSynopsis"] <- JsonValue.Create "Two checks cost 2 tokens."
        | "numeric-well" -> assessment.["wentWell"].[0] <- JsonValue.Create "Cost 2 tokens."
        | "numeric-finding" -> claim.["summary"] <- JsonValue.Create "Cost 2 tokens."
        | "numeric-uncertainty" -> claim.["uncertainty"] <- JsonValue.Create "Missing 2 observations."
        | "numeric-alternative" -> claim.["alternative"] <- JsonValue.Create "Use 2 retries."
        | "numeric-mechanism" | "numeric-validation" ->
            let improvement = JsonNode.Parse("""{"ownerRole":"maintainer","mechanism":"Use retained evidence.","validationMethod":"Check retained evidence.","status":"proposed"}""")
            improvement.[if scenario="numeric-mechanism" then "mechanism" else "validationMethod"] <- JsonValue.Create "Use 2 observations."
            assessment.["improvements"].AsArray().Add improvement
        | "affected-revision" -> claim.["affectedObjects"].[0].["revision"] <- JsonValue.Create 2
        | "finding-evidence" -> claim.["evidenceRefs"].[0] <- JsonValue.Create "invented"
        | "recovery-reference" -> claim.["recoveryRefs"].AsArray().Add(JsonValue.Create "invented")
        | "supported-no-evidence" -> claim.["evidenceRefs"] <- JsonArray()
        | "hypothesis-primary-cause" -> claim.["epistemicStatus"] <- JsonValue.Create "hypothesis";claim.["primaryCause"] <- JsonValue.Create "infrastructure"
        | "avoidable-no-witness" -> claim.["necessity"] <- JsonValue.Create "avoidable";claim.["alternative"] <- JsonValue.Create "A model asserted permitted route."
        | "unknown-output-property" -> assessment.["extra"] <- JsonValue.Create "unknown"
        | "unknown-taxonomy" -> claim.["activity"] <- JsonValue.Create "unknown-new-activity"
        | "unresolved-metric" -> assessment.["metricRefs"].AsArray().Add(JsonValue.Create "invented")
        | "coverage-altered" -> assessment.["coverage"].["population"] <- JsonValue.Create "partial"
        | "omissions-altered" -> assessment.["omissions"].AsArray().Add(JsonValue.Create "invented")
        | "supersession-unavailable" -> assessment.["supersedes"] <- JsonValue.Create "invented"
        | "outcome-unresolved" ->
            evidence.["records"].AsArray().RemoveAt 0
            assessment.["evidenceRefs"].AsArray().RemoveAt 0
            claim.["evidenceRefs"] <- JsonArray(JsonValue.Create "review-a")
            claim.["affectedObjects"] <- JsonArray()
        | "ambiguous-finding-id" ->
            let row = evidence.["records"].[1].DeepClone()
            row.["ref"].["revision"] <- JsonValue.Create 2
            row.["canonicalRef"].["revision"] <- JsonValue.Create 2
            evidence.["records"].AsArray().Add row
            claim.["evidenceRefs"].[0] <- JsonValue.Create "review-a"
        | "ambiguous-nonreview-id" ->
            let row = evidence.["records"].[0].DeepClone()
            row.["ref"].["revision"] <- JsonValue.Create 2
            row.["canonicalRef"].["revision"] <- JsonValue.Create 2
            evidence.["records"].AsArray().Add row
        | _ -> ()
        let mutable input = fixture assessment evidence
        match scenario with
        | "request-key" ->
            let request = JsonNode.Parse(utf8.GetString input.CanonicalRequestBytes)
            request.["requestId"] <- JsonValue.Create(String.replicate 64 "a")
            input <- { input with RequestId=String.replicate 64 "a";CanonicalRequestBytes=canonical request }
        | "packet-raw-digest" -> input <- { input with PacketBytes=Array.append input.PacketBytes (utf8.GetBytes " ") }
        | "duplicate-model-field" -> input <- { input with OutputText=input.OutputText.Replace("{","{\"schema\":\"duplicate\",",StringComparison.Ordinal) }
        | _ -> ()
        let expected =
            match scenario with
            | "numeric-synopsis" | "numeric-well" | "numeric-finding" | "numeric-uncertainty"
            | "numeric-alternative" | "numeric-mechanism" | "numeric-validation" -> "assessment-numeric-prose-refused"
            | "affected-revision" -> "assessment-unresolved-evidence-revision"
            | "finding-evidence" | "recovery-reference" | "ambiguous-finding-id" -> "assessment-finding-reference-unresolved-or-ambiguous"
            | "supported-no-evidence" -> "assessment-supported-claim-without-evidence"
            | "hypothesis-primary-cause" -> "assessment-unsupported-primary-cause"
            | "avoidable-no-witness" -> "assessment-then-policy-alternative-unavailable"
            | "unresolved-metric" -> "assessment-metric-witness-unavailable"
            | "coverage-altered" | "omissions-altered" -> "assessment-coverage-or-omissions-altered"
            | "supersession-unavailable" -> "assessment-supersession-unavailable"
            | "outcome-unresolved" -> "assessment-outcome-unresolved"
            | "ambiguous-nonreview-id" -> "assessment-ambiguous-evidence-id"
            | "request-key" -> "assessment-idempotency-key-mismatch"
            | "packet-raw-digest" -> "assessment-raw-evidence-mismatch"
            | "duplicate-model-field" -> "assessment-duplicate-field"
            | _ -> "assessment-schema-refused"
        match NativeResponsesAssessment.prepare input with
        | Ok _ -> failwith "unsupported fixture became prepared assessment"
        | Error errors -> Assert.Contains(expected, errors)
