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
    let private run item attempt =
        $"""{{"kind":"ci-run","identity":"run-{attempt}-{item}","itemId":"{item}","revision":0,"repository":"o/r","runId":10,"attempt":{attempt},"workflow":"ci.yml","event":"pull_request","head":"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa","status":"completed","conclusion":"success","createdAt":"2026-10-06T00:00:00Z","startedAt":"2026-10-06T00:00:01Z","updatedAt":"2026-10-06T00:00:10Z"}}"""
    let private job item attempt id created started completed =
        $"""{{"kind":"ci-job","identity":"job-{id}-{item}","itemId":"{item}","revision":0,"repository":"o/r","runId":10,"attempt":{attempt},"jobId":{id},"name":"build","status":"completed","conclusion":"success","createdAt":{created},"startedAt":{started},"completedAt":{completed}}}"""
    let private timestamp value = JsonSerializer.Serialize(value:string)
    let private coverage item state =
        $"""{{"kind":"ci-coverage","identity":"coverage-{item}","itemId":"{item}","revision":0,"collectionId":"collection","inventory":"{state}","attempts":"{state}","jobPages":"{state}","terminal":"{state}","timestamps":"{state}","lineage":"{state}","classification":"unknown","criticalPath":"unknown"}}"""
    let private export path =
        use compact = JsonDocument.Parse(TelemetryStoreApplication.compactDashboardSnapshot path approved None |> unwrap)
        TelemetryStoreApplication.efficiencyExport path approved (compact.RootElement.GetProperty("revision").GetString()) 200 1000 |> unwrap |> JsonDocument.Parse
    let private metric (document:JsonDocument) name scope =
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
        ingest path "known" [run "A" 1;run "A" 2;coverage "A" "complete";job "A" 1 11 created started ended;job "A" 1 12 created started ended;job "A" 2 21 created started ended]
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
        ingest path "partial" [run "A" 1;run "A" 2;coverage "A" "partial";job "A" 1 11 first next next;job "A" 2 21 "null" next "null"]
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
        let events = [run "A" 1;coverage "A" "complete";job "A" 1 11 first next next]
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
        ingest path "missing" [run "A" 2;coverage "A" "complete";job "A" 2 21 stamp stamp stamp]
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
        ingest path "group" [population "A";population "B";run "A" 1;run "B" 2;coverage "A" "complete";job "A" 1 11 first next ended;job "B" 2 21 first next ended]
        use result = export path
        Assert.Equal(1,result.RootElement.GetProperty("items").GetArrayLength())
        Assert.Equal("GROUP",result.RootElement.GetProperty("items")[0].GetProperty("itemId").GetString())
        ratio 1L 2L (metric result "retry-burden" "ci-observed")
