namespace FS.GG.Coord.Tests

open System.Text.Json
open Xunit
open FS.GG.Coord

module EfficiencyInputTests =
    let private raw = """{"kind":"efficiency-resource-allocation/1","identity":"a","itemId":null,"revision":0,"resource":{"sourceRef":{"id":"u","kind":"runtime-turn-usage","revision":0,"contentDigest":"sha256:1111111111111111111111111111111111111111111111111111111111111111"},"dimension":"native-tokens","provider":"fixture","accountingScope":"native","unit":"tokens-total","amount":10},"shares":[],"coverage":{"allocation":"unknown","evidence":"unknown"},"provenance":{"sourceIdentity":"s","authorityRole":"runtime-observer","authorityRef":{"id":"root","kind":"runtime-admission","revision":0,"contentDigest":"sha256:1111111111111111111111111111111111111111111111111111111111111111"},"rootDispatchRef":null,"invocationRef":"i"},"occurredAt":null,"observedAt":"2026-10-06T01:02:03.125+01:00"}"""
    let private decode (text: string) =
        use document = JsonDocument.Parse text
        EfficiencyInput.parseEvent document.RootElement
    let private refused text = match decode text with Error _ -> true | Ok _ -> false

    [<Fact>]
    let ``dedicated input shape preserves source revision zero and fractional offset time`` () =
        match decode raw with
        | Error reason -> failwith reason
        | Ok record ->
            Assert.Equal(0L, (EfficiencyInput.body record).GetProperty("resource").GetProperty("sourceRef").GetProperty("revision").GetInt64())

    [<Fact>]
    let ``event wrapper does not accept standalone schema or declared extra authority`` () =
        Assert.True(refused (raw.Replace("{\"kind\"", "{\"schema\":\"fsgg.telemetry.efficiency-resource-allocation-input/1\",\"kind\"")))
        Assert.True(refused (raw.Replace("{\"kind\"", "{\"rootAuthorized\":true,\"kind\"")))

    [<Fact>]
    let ``duplicate JSON property cannot be hidden by last-wins parsing`` () =
        Assert.True(refused (raw.Replace("\"revision\":0,", "\"revision\":0,\"revision\":1,")))

    [<Fact>]
    let ``unknown resource unit or future kind refuses`` () =
        Assert.True(refused (raw.Replace("tokens-total", "rounded-money")))
        Assert.True(refused (raw.Replace("efficiency-resource-allocation/1", "efficiency-resource-allocation/2")))

    [<Fact>]
    let ``negative revision and invalid calendar timestamp refuse`` () =
        Assert.True(refused (raw.Replace("\"revision\":0", "\"revision\":-1")))
        Assert.True(refused (raw.Replace("2026-10-06", "2026-02-30")))

    [<Fact>]
    let ``generic ingest shape cannot claim an analysis request`` () =
        Assert.True(refused (raw.Replace("efficiency-resource-allocation/1", "efficiency-analysis-request/1")))

    [<Fact>]
    let ``astral Unicode identifiers use schema codepoints not UTF16 units`` () =
        let boundary = String.replicate 256 "😀"
        Assert.False(refused (raw.Replace("\"identity\":\"a\"", "\"identity\":\"" + boundary + "\"")))
        Assert.True(refused (raw.Replace("\"identity\":\"a\"", "\"identity\":\"" + boundary + "😀\"")))

    [<Fact>]
    let ``assessment outer admission ref is canonical while nested evidence refs remain semantic`` () =
        let sample = """{"kind":"efficiency-assessment/1","identity":"assessment-native-item","itemId":"A","revision":1,"assessment":{"schema":"fsgg.telemetry.efficiency-assessment/1","assessmentId":"assessment-native-item","revision":1,"supersedes":null,"subject":{"itemId":"A","outcomeId":"source-a","outcomeEpoch":1,"scope":"native-item"},"evidenceDigest":"sha256:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa","evidenceRefs":[{"id":"source-a","kind":"outcome","revision":1},{"id":"review-a","kind":"process-review","revision":1}],"coverage":{"population":"complete","usage":"complete","classification":"complete","lineage":"complete","dependency":"complete"},"omissions":[],"outcomeSynopsis":"Synthetic source change merged; native population is complete.","wentWell":["Source outcome has a stable canonical reference."],"findings":[],"metricRefs":["metric-observed-a"],"improvements":[],"provenance":{"producer":"synthetic-fixture","modelAlias":"fixture-model","promptVersion":"efficiency-prompt/1","rubricVersion":"efficiency-rubric/1","taxonomyVersion":"efficiency-taxonomy/1","analysisPolicyVersion":"efficiency-analysis-policy/1","usageRefs":[],"startedAt":"2026-10-06T00:22:00Z","finishedAt":"2026-10-06T00:23:00Z","validationResult":"accepted"},"lifecycle":{"state":"ready","idempotencyKey":"6b6ab0e4cda0153bb7f88d308acaa5ab66b9557522669a52374e253afc5f7677","failureReason":null,"itemReviewRef":"review-a","generatedAt":"2026-10-06T00:23:00Z"},"publication":{"visibility":"private","policyVersion":null}},"provenance":{"sourceIdentity":"synthetic-fixture","authorityRole":"root-reviewer","authorityRef":{"id":"native-root","kind":"runtime-admission","revision":0,"contentDigest":"sha256:1111111111111111111111111111111111111111111111111111111111111111"},"rootDispatchRef":"dispatch-a","invocationRef":"invocation-a"},"observedAt":"2026-10-06T00:24:00Z"}"""
        Assert.False(refused sample)
        Assert.True(refused (sample.Replace(",\"contentDigest\":\"sha256:1111111111111111111111111111111111111111111111111111111111111111\"", "")))
