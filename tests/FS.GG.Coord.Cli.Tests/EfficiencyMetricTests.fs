namespace FS.GG.Coord.Cli.Tests

open System
open System.IO
open System.Text
open System.Text.Json
open Xunit
open FS.GG.Coord
open FS.GG.Coord.Cli

/// These fixtures exercise canonical ingest/export, not an alternate dashboard calculator.
module EfficiencyMetricTests =
    let private approved = TelemetryStore.ApprovedLocalDurable
    let private unwrap = function Ok value -> value | Error errors -> failwithf "%A" errors
    let private create () =
        let path = Path.Combine(Path.GetTempPath(), "fsgg-eff-metric-" + Guid.NewGuid().ToString("N"))
        Directory.CreateDirectory path |> ignore
        TelemetryStoreApplication.initialize path approved |> unwrap |> ignore
        { new IDisposable with member _.Dispose() = Directory.Delete(path,true) },path
    let private ingest path name events =
        let payload = $"""{{"schema":"{TelemetryStore.BatchSchema}","ingestId":"{name}","sourceIdentity":"ci-worker","generation":"g1","cursor":"{name}","eventCount":{List.length events},"events":[{String.concat "," events}]}}"""
        TelemetryStoreApplication.ingest path approved (Encoding.UTF8.GetBytes payload) |> unwrap |> ignore
    let private run (item: string) (attempt: int) =
        $"""{{"kind":"ci-run","identity":"run-{attempt}-{item}","itemId":"{item}","revision":0,"repository":"o/r","runId":10,"attempt":{attempt},"workflow":"ci.yml","event":"pull_request","head":"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa","status":"completed","conclusion":"success","createdAt":"2026-10-06T00:00:00Z","startedAt":"2026-10-06T00:00:01Z","updatedAt":"2026-10-06T00:00:10Z"}}"""
    let private job (item: string) (attempt: int) (id: int64) (created: string) (started: string) (completed: string) =
        $"""{{"kind":"ci-job","identity":"job-{id}-{item}","itemId":"{item}","revision":0,"repository":"o/r","runId":10,"attempt":{attempt},"jobId":{id},"name":"build","status":"completed","conclusion":"success","createdAt":{created},"startedAt":{started},"completedAt":{completed}}}"""
    let private timestamp value = JsonSerializer.Serialize(value:string)
    let private binding (item: string) =
        $"""{{"kind":"ci-binding","identity":"binding-{item}","itemId":"{item}","revision":0,"collectionId":"collection-{item}","repository":"o/r","head":"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa","prNumber":7,"workflow":"ci.yml","featureId":"EFF","attemptId":"test","parentAttemptId":null,"producerStream":"ci-worker","binding":"exact"}}"""
    let private coverage (item: string) (state: string) =
        $"""{{"kind":"ci-coverage","identity":"coverage-{item}","itemId":"{item}","revision":0,"collectionId":"collection-{item}","inventory":"{state}","attempts":"{state}","jobPages":"{state}","terminal":"{state}","timestamps":"{state}","lineage":"{state}","classification":"unknown","criticalPath":"unknown"}}"""
    let private export path =
        use compact = JsonDocument.Parse(TelemetryStoreApplication.compactDashboardSnapshot path approved None |> unwrap)
        TelemetryStoreApplication.efficiencyExport path approved (compact.RootElement.GetProperty("revision").GetString()) 200 1000 |> unwrap |> JsonDocument.Parse
    let private metric (document:JsonDocument) (name: string) (scope: string) =
        document.RootElement.GetProperty("items")[0].GetProperty("metrics").EnumerateArray()
        |> Seq.find (fun metric -> metric.GetProperty("metric").GetString()=name && metric.GetProperty("population").GetProperty("acceptanceScope").GetString()=scope)
    let private ratio (expectedNumerator:int64) (expectedDenominator:int64) (metric:JsonElement) =
        let value = metric.GetProperty "value"
        Assert.Equal(expectedNumerator,value.GetProperty("numerator").GetInt64())
        Assert.Equal(expectedDenominator,value.GetProperty("denominator").GetInt64())

    [<Fact>]
    let ``parallel job resource adds while provider queue intervals union with fractional offsets`` () =
        let cleanup,path = create ()
        use cleanup = cleanup
        let created = timestamp "2026-10-06T00:00:00.250Z"
        let started = timestamp "2026-10-06T02:00:01.500+02:00"
        let ended = timestamp "2026-10-06T00:00:02.000Z"
        ingest path "known" [binding "A";run "A" 1;run "A" 2;coverage "A" "complete";job "A" 1 11 created started ended;job "A" 1 12 created started ended;job "A" 2 21 created started ended]
        use result = export path
        let incidence = metric result "retry-incidence" "ci-observed"
        let burden = metric result "retry-burden" "ci-observed"
        let wait = metric result "wait-time" "ci-observed"
        ratio 1L 1L incidence
        ratio 1L 3L burden
        ratio 3L 2L (metric result "observed-resource" "ci-observed")
        ratio 5L 4L wait
        Assert.Equal("known",burden.GetProperty("value").GetProperty("status").GetString())
        Assert.Equal("unknown",(metric result "critical-path-delay" "native-item").GetProperty("value").GetProperty("status").GetString())

    [<Fact>]
    let ``missing job timestamps keep retry burden unknown and witnessed queue exposure partial`` () =
        let cleanup,path = create ()
        use cleanup = cleanup
        let first = timestamp "2026-10-06T00:00:00Z"
        let next = timestamp "2026-10-06T00:00:01Z"
        ingest path "partial" [binding "A";run "A" 1;run "A" 2;coverage "A" "partial";job "A" 1 11 first next next;job "A" 2 21 "null" next "null"]
        use result = export path
        Assert.Equal("unknown",(metric result "retry-burden" "ci-observed").GetProperty("value").GetProperty("status").GetString())
        let wait = metric result "wait-time" "ci-observed"
        ratio 1L 1L wait
        Assert.Equal("partial",wait.GetProperty("value").GetProperty("status").GetString())

    [<Fact>]
    let ``same facts replay do not add retry resource or provider queue wait`` () =
        let cleanup,path = create ()
        use cleanup = cleanup
        let first = timestamp "2026-10-06T00:00:00Z"
        let next = timestamp "2026-10-06T00:00:01Z"
        let events = [binding "A";run "A" 1;coverage "A" "complete";job "A" 1 11 first next next]
        ingest path "initial" events
        ingest path "replay" events
        use result = export path
        ratio 0L 1L (metric result "retry-incidence" "ci-observed")
        ratio 1L 1L (metric result "wait-time" "ci-observed")
        Assert.Equal("unknown",(metric result "retry-burden" "ci-observed").GetProperty("value").GetProperty("status").GetString())

    [<Fact>]
    let ``attempt two without first attempt does not establish a retry denominator`` () =
        let cleanup,path = create ()
        use cleanup = cleanup
        let stamp = timestamp "2026-10-06T00:00:00Z"
        ingest path "missing" [binding "A";run "A" 2;coverage "A" "complete";job "A" 2 21 stamp stamp stamp]
        use result = export path
        Assert.Equal("unknown",(metric result "retry-incidence" "ci-observed").GetProperty("value").GetProperty("status").GetString())
        Assert.Equal("unknown",(metric result "first-pass-delivery" "native-item").GetProperty("value").GetProperty("status").GetString())

    [<Fact>]
    let ``original group members conserve run resource and alias replay once`` () =
        let cleanup,path = create ()
        use cleanup = cleanup
        let population item = $"""{{"kind":"budget-population","identity":"population-{item}","itemId":"{item}","revision":0,"originalItemId":"GROUP","state":"open","sourceKind":"native-item","sourceRef":"roadmap-dispatch:{item}"}}"""
        let first = timestamp "2026-10-06T00:00:00Z"
        let next = timestamp "2026-10-06T00:00:01Z"
        let ended = timestamp "2026-10-06T00:00:02Z"
        ingest path "group" [population "A";population "B";binding "A";binding "B";coverage "B" "complete";run "A" 1;run "B" 2;coverage "A" "complete";job "A" 1 11 first next ended;job "B" 2 21 first next ended]
        use result = export path
        Assert.Equal(1,result.RootElement.GetProperty("items").GetArrayLength())
        Assert.Equal("GROUP",result.RootElement.GetProperty("items")[0].GetProperty("itemId").GetString())
        ratio 1L 2L (metric result "retry-burden" "ci-observed")

    [<Fact>]
    let ``unbound collection completeness cannot establish the selected run population`` () =
        let cleanup,path = create ()
        use cleanup = cleanup
        let first = timestamp "2026-10-06T00:00:00Z"
        let next = timestamp "2026-10-06T00:00:01Z"
        ingest path "unbound" [run "A" 1;coverage "A" "complete";job "A" 1 11 first next next]
        use result = export path
        Assert.Equal("unknown",(metric result "retry-incidence" "ci-observed").GetProperty("value").GetProperty("status").GetString())
        Assert.Equal("partial",(metric result "wait-time" "ci-observed").GetProperty("value").GetProperty("status").GetString())

    [<Fact>]
    let ``supported correction moves the metric cohort without duplicating resource or resetting ordinals`` () =
        let cleanup,path = create ()
        use cleanup = cleanup
        let first = timestamp "2026-10-06T00:00:00Z"
        let next = timestamp "2026-10-06T00:00:01Z"
        let ended = timestamp "2026-10-06T00:00:02Z"
        let outcome = """{"kind":"native-item-outcome","identity":"routine-delivery:A","itemId":"A","revision":0,"repository":"o/r","prNumber":7,"baseRef":"main","baseSha":"dddddddddddddddddddddddddddddddddddddddd","head":"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa","outcome":"delivered","codeDelivery":"delivered","mergeCommit":"bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb","occurredAt":"2026-10-06T00:00:02Z","observedAt":"2026-10-06T00:00:03Z","sourceKind":"routine-delivery","sourceRef":"routine-delivery:A"}"""
        let admission = """{"kind":"ci-population-admission","identity":"ci-admission","itemId":"A","revision":0,"collectionId":"collection-A","repository":"o/r","prNumber":7,"baseRef":"main","baseSha":"dddddddddddddddddddddddddddddddddddddddd","head":"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa","witness":"native-pr-head"}"""
        ingest path "prior" [outcome;admission;binding "A";coverage "A" "complete";run "A" 1;run "A" 2;job "A" 1 11 first next ended;job "A" 2 21 first next ended]
        use before = export path
        ratio 1L 2L (metric before "retry-burden" "ci-observed")
        let assignment item : TelemetryCi.Assignment =
            { FeatureId="EFF";ItemId=item;AttemptId="test";ParentAttemptId=None;ProducerStream="ci-worker" }
        let request: TelemetryCi.CorrectionRequest =
            { CorrectionId="metric-correction";ExpectedPredecessor=None;Repository="o/r";PullRequest=7L
              BaseRef="main";BaseSha=String.replicate 40 "d";Head=String.replicate 40 "a";MergeCommit=String.replicate 40 "b"
              Prior=assignment "A";Effective=assignment "B";EvidenceSha256=String.replicate 64 "e"
              Reason="Recovered exact CI fixture assignment";OperatorSource="retained-test-receipt";ObservedAt="2026-10-06T00:01:00Z" }
        let plan = TelemetryStoreApplication.ciCorrectionPlan path approved request |> unwrap |> Encoding.UTF8.GetBytes
        Assert.Contains("applied",TelemetryStoreApplication.ciCorrect path approved plan |> unwrap)
        Assert.Contains("already-applied",TelemetryStoreApplication.ciCorrect path approved plan |> unwrap)
        use after = export path
        Assert.Equal(1,after.RootElement.GetProperty("items").GetArrayLength())
        Assert.Equal("B",after.RootElement.GetProperty("items")[0].GetProperty("itemId").GetString())
        ratio 1L 2L (metric after "retry-burden" "ci-observed")
        ratio 2L 1L (metric after "observed-resource" "ci-observed")
        Assert.NotEqual(before.RootElement.GetProperty("sourceFingerprint").GetString(),after.RootElement.GetProperty("sourceFingerprint").GetString())

    [<Fact>]
    let ``finer than supported clock precision is unavailable instead of rounded into a false zero`` () =
        let cleanup,path = create ()
        use cleanup = cleanup
        let first = timestamp "2026-10-06T00:00:00.000000001Z"
        let next = timestamp "2026-10-06T00:00:00.000000002Z"
        ingest path "precision" [binding "A";run "A" 1;coverage "A" "complete";job "A" 1 11 first next next]
        use result = export path
        Assert.Equal("unknown",(metric result "wait-time" "ci-observed").GetProperty("value").GetProperty("status").GetString())
        Assert.Equal("unknown",(metric result "observed-resource" "ci-observed").GetProperty("value").GetProperty("status").GetString())

    [<Fact>]
    let ``late CI native time and health rows preserve the original group's open population`` () =
        let cleanup,path = create ()
        use cleanup = cleanup
        let first = timestamp "2026-10-06T00:00:00Z"
        let next = timestamp "2026-10-06T00:00:01Z"
        ingest path "open-group" [binding "A";run "A" 1;coverage "A" "complete";job "A" 1 11 first next next]
        use result = export path
        for name,scope in ["wait-time","ci-observed";"lead-time","native-item";"first-pass-delivery","native-item";"data-health","receiver-observed"] do
            let row = metric result name scope
            Assert.Equal(1,row.GetProperty("population").GetProperty("openItems").GetInt32())
        Assert.Equal("unknown",(metric result "lead-time" "native-item").GetProperty("value").GetProperty("status").GetString())
