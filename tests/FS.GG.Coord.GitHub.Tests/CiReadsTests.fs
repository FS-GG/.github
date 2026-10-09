module FS.GG.Coord.GitHub.Tests.CiReadsTests

open System.Collections.Generic
open Xunit
open FS.GG.Coord.GitHub
open FS.GG.Coord.GitHub.Errors
open FS.GG.Coord.GitHub.Transport

type private Fake(responses: IoResult<Response> list) =
    let queue = Queue<IoResult<Response>>(responses)
    let requests = ResizeArray<Request>()
    member _.Requests = requests |> Seq.toList

    interface ISinglePageGitHubTransport with
        member _.SendSingle request =
            requests.Add request
            queue.Dequeue()

let private response body next =
    Ok
        {
            Status = 200
            Body = body
            Headers = Map.empty
            ETag = None
            NextLink = next
        }

let private head = String.replicate 40 "a"
let private baseSha = String.replicate 40 "d"

let private pr =
    response ($"""{{"head":{{"sha":"{head}"}},"merge_commit_sha":null}}""") None

let private run id attempt =
    $"""{{"id":{id},"run_attempt":{attempt},"path":".github/workflows/ci.yml","event":"pull_request","head_sha":"{head}","status":"completed","conclusion":"success","created_at":"2026-01-01T00:00:00Z","run_started_at":"2026-01-01T00:00:01Z","updated_at":"2026-01-01T00:01:01Z","pull_requests":[]}}"""

let private job runId attempt id startAt endAt =
    $"""{{"id":{id},"name":"build","status":"completed","conclusion":"success","created_at":"{startAt}","started_at":"{startAt}","completed_at":"{endAt}","runner_name":"PRIVATE-RUNNER","steps":[{{"number":1,"name":"test","status":"completed","conclusion":"success","started_at":"{startAt}","completed_at":"{endAt}"}}]}}"""

let private populationRun id attempt workflow event status conclusion updated =
    let conclusionValue =
        conclusion |> Option.map (sprintf "\"%s\"") |> Option.defaultValue "null"

    $"""{{"id":{id},"run_attempt":{attempt},"path":".github/workflows/{workflow}","event":"{event}","head_sha":"{head}","status":"{status}","conclusion":{conclusionValue},"created_at":"2026-01-01T00:00:00Z","run_started_at":"2026-01-01T00:00:01Z","updated_at":"{updated}","pull_requests":[{{"number":7}}]}}"""

let private populationJob id checkId status conclusion =
    let conclusionValue =
        conclusion |> Option.map (sprintf "\"%s\"") |> Option.defaultValue "null"

    let completed =
        if status = "completed" then
            "\"2026-01-01T00:01:00Z\""
        else
            "null"

    $"""{{"id":{id},"name":"build","status":"{status}","conclusion":{conclusionValue},"created_at":"2026-01-01T00:00:00Z","started_at":"2026-01-01T00:00:01Z","completed_at":{completed},"check_run_url":"https://api.github.com/repos/o/r/check-runs/{checkId}","steps":[]}}"""

let private check id app status conclusion =
    let conclusionValue =
        conclusion |> Option.map (sprintf "\"%s\"") |> Option.defaultValue "null"

    let completed =
        if status = "completed" then
            "\"2026-01-01T00:01:00Z\""
        else
            "null"

    $"""{{"id":{id},"name":"build","app":{{"slug":"{app}"}},"status":"{status}","conclusion":{conclusionValue},"started_at":"2026-01-01T00:00:01Z","completed_at":{completed}}}"""

let private populationPr sha =
    response ($"""{{"head":{{"sha":"{sha}"}},"base":{{"ref":"main","sha":"{baseSha}"}}}}""") None

let private discover fake admitted =
    CiReads.discoverPopulation
        (fake :> ISinglePageGitHubTransport)
        "https://api.github.com"
        "o"
        "r"
        7
        head
        "main"
        baseSha
        admitted

[<Fact>]
let ``UTEL-04A collects bounded run pages and attempt-specific jobs with empty native PR joins`` () =
    let next =
        "https://api.github.com/repos/o/r/actions/workflows/ci.yml/runs?head_sha="
        + head
        + "&per_page=100&page=2"

    let fake =
        Fake
            [
                pr
                response ($"""{{"total_count":2,"workflow_runs":[{run 10 1}]}}""") (Some next)
                response ($"""{{"total_count":2,"workflow_runs":[{run 10 2}]}}""") None
                response
                    ($"""{{"total_count":1,"jobs":[{job 10 1 101 "2026-01-01T00:00:00Z" "2026-01-01T00:01:00Z"}]}}""")
                    None
                response
                    ($"""{{"total_count":1,"jobs":[{job 10 2 102 "2026-01-01T00:02:00Z" "2026-01-01T00:02:40Z"}]}}""")
                    None
                response "{\"run_attempt\":2}" None
            ]

    let result =
        CiReads.collect (fake :> ISinglePageGitHubTransport) "https://api.github.com" "o" "r" 7 head "ci.yml"

    match result with
    | Error error -> failwithf "%A" error
    | Ok snapshot ->
        Assert.Equal("exact", snapshot.Binding)
        Assert.Equal(2, snapshot.Runs.Length)
        Assert.Equal(2, snapshot.Jobs.Length)
        Assert.Empty(snapshot.Runs.Head.PullRequests)
        let jobPaths = fake.Requests |> List.map _.Path |> List.filter (_.Contains("/jobs"))
        Assert.Contains("repos/o/r/actions/runs/10/attempts/1/jobs", jobPaths)
        Assert.Contains("repos/o/r/actions/runs/10/attempts/2/jobs", jobPaths)

[<Fact>]
let ``UTEL-04A attempt increase during collection makes coverage partial`` () =
    let fake =
        Fake
            [
                pr
                response ($"""{{"total_count":1,"workflow_runs":[{run 10 1}]}}""") None
                response "{\"total_count\":0,\"jobs\":[]}" None
                response "{\"run_attempt\":2}" None
            ]

    match CiReads.collect (fake :> ISinglePageGitHubTransport) "https://api.github.com" "o" "r" 7 head "ci.yml" with
    | Ok snapshot ->
        Assert.Equal("partial", snapshot.AttemptCoverage)
        Assert.Equal(Some "attempt-changed-or-unverified", snapshot.Diagnostic)
    | Error error -> failwithf "%A" error

[<Fact>]
let ``UTEL-04A unsafe continuation is retained as partial rather than complete`` () =
    let fake =
        Fake
            [
                pr
                response
                    ($"""{{"total_count":2,"workflow_runs":[{run 10 1}]}}""")
                    (Some "https://evil.invalid/repos/o/r/actions/runs?page=2")
            ]

    match CiReads.collect (fake :> ISinglePageGitHubTransport) "https://api.github.com" "o" "r" 7 head "ci.yml" with
    | Ok snapshot ->
        Assert.Equal("partial", snapshot.InventoryCoverage)
        Assert.Equal(Some "run-pagination-incomplete", snapshot.Diagnostic)
    | Error error -> failwithf "%A" error

[<Fact>]
let ``UTEL-04A wrong head stays unresolved and does not enumerate runs`` () =
    let fake =
        Fake
            [
                response
                    "{\"head\":{\"sha\":\"bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb\"},\"merge_commit_sha\":null}"
                    None
            ]

    match CiReads.collect (fake :> ISinglePageGitHubTransport) "https://api.github.com" "o" "r" 7 head "ci.yml" with
    | Ok snapshot ->
        Assert.Equal("unresolved", snapshot.Binding)
        Assert.Equal("unknown", snapshot.InventoryCoverage)
        Assert.Single(fake.Requests) |> ignore
    | Error error -> failwithf "%A" error

[<Fact>]
let ``UTEL-04A conflicting duplicate jobs are rejected rather than covered`` () =
    let first = job 10 1 101 "2026-01-01T00:00:00Z" "2026-01-01T00:01:00Z"
    let changed = job 10 1 101 "2026-01-01T00:00:00Z" "2026-01-01T00:01:01Z"

    let fake =
        Fake
            [
                pr
                response ($"""{{"total_count":1,"workflow_runs":[{run 10 1}]}}""") None
                response ($"""{{"total_count":1,"jobs":[{first},{changed}]}}""") None
            ]

    match CiReads.collect (fake :> ISinglePageGitHubTransport) "https://api.github.com" "o" "r" 7 head "ci.yml" with
    | Error(Malformed(_, detail)) -> Assert.Contains("conflicting duplicate", detail)
    | result -> failwithf "expected conflict, got %A" result

[<Fact>]
let ``UTEL-04A request budget stops without retry and retains partial job coverage`` () =
    let runs = [ 1L .. 19L ] |> List.map (fun id -> run id 1) |> String.concat ","

    let emptyJobs =
        [ for _ in 1..18 -> response "{\"total_count\":0,\"jobs\":[]}" None ]

    let fake =
        Fake(
            pr
            :: response ($"""{{"total_count":19,"workflow_runs":[{runs}]}}""") None
            :: emptyJobs
        )

    match CiReads.collect (fake :> ISinglePageGitHubTransport) "https://api.github.com" "o" "r" 7 head "ci.yml" with
    | Ok snapshot ->
        Assert.Equal("partial", snapshot.JobPageCoverage)
        Assert.Equal(20, fake.Requests.Length)
    | Error error -> failwithf "%A" error

[<Fact>]
let ``UTEL-06C automatically discovers required and nonrequired workflows with native checks`` () =
    let first =
        populationRun 10 1 "ci.yml" "pull_request" "completed" (Some "success") "2026-01-01T00:01:01Z"

    let second =
        populationRun 20 1 "optional.yml" "pull_request" "completed" (Some "success") "2026-01-01T00:02:01Z"

    let runs = $"""{{"total_count":2,"workflow_runs":[{first},{second}]}}"""

    let checks =
        $"""{{"total_count":2,"check_runs":[{check 101 "github-actions" "completed" (Some "success")},{check 201 "github-actions" "completed" (Some "success")}]}}"""

    let fake =
        Fake
            [
                populationPr head
                response runs None
                response
                    ($"""{{"total_count":1,"jobs":[{populationJob 1001 101 "completed" (Some "success")}]}}""")
                    None
                response
                    ($"""{{"total_count":1,"jobs":[{populationJob 2001 201 "completed" (Some "success")}]}}""")
                    None
                response checks None
                response runs None
                response "{\"total_count\":2,\"check_runs\":[]}" None
            ]

    match discover fake false with
    | Error error -> failwithf "%A" error
    | Ok result ->
        Assert.True(result.AdmissionWitness)
        Assert.Equal("complete", result.Snapshot.InventoryCoverage)

        Assert.Equal<string list>(
            [ ".github/workflows/ci.yml"; ".github/workflows/optional.yml" ],
            result.Snapshot.Runs |> List.map _.Workflow
        )

        Assert.Empty(result.Pending)

[<Fact>]
let ``UTEL-06C admitted superseded head continues but arbitrary old head does not enumerate`` () =
    let moved = String.replicate 40 "b"
    let unadmitted = Fake [ populationPr moved ]

    match discover unadmitted false with
    | Ok result ->
        Assert.Equal("unresolved", result.Snapshot.Binding)
        Assert.Single(unadmitted.Requests) |> ignore
    | Error error -> failwithf "%A" error

    let admitted =
        Fake
            [
                populationPr moved
                response "{\"total_count\":0,\"workflow_runs\":[]}" None
                response "{\"total_count\":0,\"check_runs\":[]}" None
                response "{\"total_count\":0,\"workflow_runs\":[]}" None
                response "{\"total_count\":0,\"check_runs\":[]}" None
            ]

    match discover admitted true with
    | Ok result ->
        Assert.Equal("admitted-superseded", result.Snapshot.Binding)
        Assert.Equal("complete", result.Snapshot.InventoryCoverage)
    | Error error -> failwithf "%A" error

[<Fact>]
let ``UTEL-06C first admission requires native base ref and sha as well as head`` () =
    let wrongRef =
        Fake
            [
                response ($"""{{"head":{{"sha":"{head}"}},"base":{{"ref":"release","sha":"{baseSha}"}}}}""") None
            ]

    match discover wrongRef false with
    | Ok result ->
        Assert.Contains("base-ref-mismatch", result.Gaps)
        Assert.Single(wrongRef.Requests) |> ignore
    | Error error -> failwithf "%A" error

    let wrongSha =
        Fake
            [
                response
                    ($"""{{"head":{{"sha":"{head}"}},"base":{{"ref":"main","sha":"{String.replicate 40 "e"}"}}}}""")
                    None
            ]

    match discover wrongSha false with
    | Ok result ->
        Assert.Contains("base-sha-mismatch", result.Gaps)
        Assert.Single(wrongSha.Requests) |> ignore
    | Error error -> failwithf "%A" error

[<Fact>]
let ``UTEL-06C collects every attempt and cancelled terminal is complete`` () =
    let latest =
        populationRun 10 2 "ci.yml" "pull_request" "completed" (Some "cancelled") "2026-01-01T00:03:01Z"

    let earlier =
        populationRun 10 1 "ci.yml" "pull_request" "completed" (Some "failure") "2026-01-01T00:02:01Z"

    let listing = $"""{{"total_count":1,"workflow_runs":[{latest}]}}"""

    let checks =
        $"""{{"total_count":2,"check_runs":[{check 101 "github-actions" "completed" (Some "failure")},{check 102 "github-actions" "completed" (Some "cancelled")}]}}"""

    let fake =
        Fake
            [
                populationPr head
                response listing None
                response earlier None
                response
                    ($"""{{"total_count":1,"jobs":[{populationJob 1001 101 "completed" (Some "failure")}]}}""")
                    None
                response
                    ($"""{{"total_count":1,"jobs":[{populationJob 1002 102 "completed" (Some "cancelled")}]}}""")
                    None
                response checks None
                response listing None
                response "{\"total_count\":2,\"check_runs\":[]}" None
            ]

    match discover fake false with
    | Ok result ->
        Assert.Equal(2, result.Snapshot.Runs.Length)
        Assert.Equal("complete", result.Snapshot.AttemptCoverage)
        Assert.Equal("complete", result.Snapshot.TerminalCoverage)
    | Error error -> failwithf "%A" error

[<Fact>]
let ``UTEL-06C late or concurrently added run leaves bounded continuation pending`` () =
    let row =
        populationRun 10 1 "ci.yml" "pull_request" "completed" (Some "success") "2026-01-01T00:01:01Z"

    let listing = $"""{{"total_count":1,"workflow_runs":[{row}]}}"""

    let fake =
        Fake
            [
                populationPr head
                response listing None
                response
                    ($"""{{"total_count":1,"jobs":[{populationJob 1001 101 "completed" (Some "success")}]}}""")
                    None
                response
                    ($"""{{"total_count":1,"check_runs":[{check 101 "github-actions" "completed" (Some "success")}]}}""")
                    None
                response "{\"total_count\":2,\"workflow_runs\":[]}" None
                response "{\"total_count\":1,\"check_runs\":[]}" None
            ]

    match discover fake false with
    | Ok result ->
        Assert.Equal("partial", result.Snapshot.InventoryCoverage)
        Assert.Contains("actions-runs-changed", result.Pending)
    | Error error -> failwithf "%A" error

[<Fact>]
let ``UTEL-06C concurrent check inventory mutation remains partial`` () =
    let emptyRuns = "{\"total_count\":0,\"workflow_runs\":[]}"

    let fake =
        Fake
            [
                populationPr head
                response emptyRuns None
                response "{\"total_count\":0,\"check_runs\":[]}" None
                response emptyRuns None
                response "{\"total_count\":1,\"check_runs\":[]}" None
            ]

    match discover fake false with
    | Ok result ->
        Assert.Equal("partial", result.CheckCoverage)
        Assert.Contains("check-runs-changed", result.Pending)
    | Error error -> failwithf "%A" error

[<Fact>]
let ``UTEL-06C completion observed after the parent delivery revises terminal coverage`` () =
    let observe status conclusion updated =
        let row = populationRun 10 1 "ci.yml" "pull_request" status conclusion updated
        let listing = $"""{{"total_count":1,"workflow_runs":[{row}]}}"""
        let checkStatus = if status = "completed" then "completed" else "in_progress"

        Fake
            [
                populationPr head
                response listing None
                response ($"""{{"total_count":1,"jobs":[{populationJob 1001 101 checkStatus conclusion}]}}""") None
                response
                    ($"""{{"total_count":1,"check_runs":[{check 101 "github-actions" checkStatus conclusion}]}}""")
                    None
                response listing None
                response "{\"total_count\":1,\"check_runs\":[]}" None
            ]

    match discover (observe "in_progress" None "2026-01-01T00:00:30Z") false with
    | Ok result -> Assert.Equal("partial", result.Snapshot.TerminalCoverage)
    | Error error -> failwithf "%A" error

    match discover (observe "completed" (Some "success") "2026-01-01T00:02:00Z") false with
    | Ok result -> Assert.Equal("complete", result.Snapshot.TerminalCoverage)
    | Error error -> failwithf "%A" error

[<Fact>]
let ``UTEL-06C partial pagination is explicit and never sleeps or retries`` () =
    let row =
        populationRun 10 1 "ci.yml" "pull_request" "completed" (Some "success") "2026-01-01T00:01:01Z"

    let fake =
        Fake
            [
                populationPr head
                response
                    ($"""{{"total_count":2,"workflow_runs":[{row}]}}""")
                    (Some "https://evil.invalid/repos/o/r/actions/runs?page=2")
                response
                    ($"""{{"total_count":1,"jobs":[{populationJob 1001 101 "completed" (Some "success")}]}}""")
                    None
                response
                    ($"""{{"total_count":1,"check_runs":[{check 101 "github-actions" "completed" (Some "success")}]}}""")
                    None
                response "{\"total_count\":1,\"check_runs\":[]}" None
            ]

    match discover fake false with
    | Ok result ->
        Assert.Equal("partial", result.Snapshot.InventoryCoverage)
        Assert.Contains("actions-runs", result.Pending)
        Assert.Equal(5, fake.Requests.Length)
    | Error error -> failwithf "%A" error

[<Fact>]
let ``UTEL-06C omitted Actions run is caught by GitHub Actions check inventory`` () =
    let empty = "{\"total_count\":0,\"workflow_runs\":[]}"

    let fake =
        Fake
            [
                populationPr head
                response empty None
                response
                    ($"""{{"total_count":1,"check_runs":[{check 101 "github-actions" "completed" (Some "success")}]}}""")
                    None
                response empty None
                response "{\"total_count\":1,\"check_runs\":[]}" None
            ]

    match discover fake false with
    | Ok result ->
        Assert.Equal("partial", result.CheckCoverage)
        Assert.Contains("workflow-check-inventory-mismatch", result.Pending)
    | Error error -> failwithf "%A" error

[<Fact>]
let ``UTEL-06C unsupported trigger and external checks remain explicit gaps`` () =
    let row =
        populationRun 10 1 "ci.yml" "push" "completed" (Some "success") "2026-01-01T00:01:01Z"

    let listing = $"""{{"total_count":1,"workflow_runs":[{row}]}}"""

    let checks =
        $"""{{"total_count":2,"check_runs":[{check 101 "github-actions" "completed" (Some "success")},{check 202 "external-ci" "completed" (Some "success")}]}}"""

    let fake =
        Fake
            [
                populationPr head
                response listing None
                response
                    ($"""{{"total_count":1,"jobs":[{populationJob 1001 101 "completed" (Some "success")}]}}""")
                    None
                response checks None
                response listing None
                response "{\"total_count\":2,\"check_runs\":[]}" None
            ]

    match discover fake false with
    | Ok result ->
        Assert.Contains("unsupported-event:push", result.Gaps)
        Assert.Contains("external-checks-not-attributed", result.Gaps)
        Assert.Equal(1, result.ExternalChecks)
    | Error error -> failwithf "%A" error

let private targetRelation repository relationHead relationBase =
    $"""{{"number":7,"head":{{"sha":"{relationHead}","repo":{{"id":42,"full_name":"{repository}"}}}},"base":{{"sha":"{relationBase}","repo":{{"id":42,"full_name":"o/r"}}}}}}"""

let private targetRun relations =
    (run 123 1).Replace("\"pull_request\"", "\"pull_request_target\"")
        .Replace("\"pull_requests\":[]", "\"repository\":{\"id\":42,\"full_name\":\"o/r\"},\"pull_requests\":[" + relations + "]")

[<Fact>]
let ``UTEL-CI-02 native target relation requires exactly one complete revision tuple`` () =
    let exact = targetRelation "o/r" head baseSha
    let cases =
        [ exact, CiReads.Exact
          "", CiReads.Missing
          exact + "," + exact, CiReads.Conflicting
          targetRelation "foreign/r" head baseSha, CiReads.Conflicting
          targetRelation "o/r" (String.replicate 40 "b") baseSha, CiReads.Conflicting
          targetRelation "o/r" head (String.replicate 40 "b"), CiReads.Conflicting
          exact.Replace("\"number\":7", "\"number\":8"), CiReads.Conflicting
          "{\"number\":7}", CiReads.Missing ]
    for relations, expected in cases do
        use doc = System.Text.Json.JsonDocument.Parse(targetRun relations)
        Assert.Equal(expected, CiReads.targetAssociation "o/r" 7 head (Some baseSha) doc.RootElement)
    use title = System.Text.Json.JsonDocument.Parse((targetRun "").Replace("\"status\"", "\"display_title\":\"PR 7 head " + head + "\",\"status\""))
    Assert.Equal(CiReads.Missing, CiReads.targetAssociation "o/r" 7 head (Some baseSha) title.RootElement)

[<Fact>]
let ``UTEL-CI-02 population retains empty target join gap without collecting attributable jobs`` () =
    let row = targetRun ""
    let page = response ("{\"total_count\":1,\"workflow_runs\":[" + row + "]}") None
    let fake = Fake [ populationPr head; page; response "{\"total_count\":0,\"check_runs\":[]}" None; page; response "{\"total_count\":0,\"check_runs\":[]}" None ]
    match discover fake false with
    | Error error -> failwithf "%A" error
    | Ok result ->
        Assert.Single(result.Snapshot.Runs) |> ignore
        Assert.Empty result.Snapshot.Jobs
        Assert.Contains("target-association:123:1:Missing", result.Gaps)
        Assert.Equal("partial", result.Snapshot.LineageCoverage)
        Assert.DoesNotContain(fake.Requests, fun request -> request.Path.EndsWith("/jobs"))

[<Fact>]
let ``UTEL-CI-01 contents endpoint pins repository and immutable head and refuses alternate shapes`` () =
    let bytes = System.Text.Encoding.UTF8.GetBytes("{\"schema\":\"fsgg.telemetry.ci-attribution/1\",\"rules\":[]}")
    let body = $"""{{"type":"file","path":".fsgg/telemetry-ci-attribution.json","encoding":"base64","content":"{System.Convert.ToBase64String bytes}"}}"""
    let fake = Fake [ response body None ]
    Assert.Equal(Ok(Some bytes), CiReads.readAttributionProfile (fake :> ISinglePageGitHubTransport) "o/r" head)
    Assert.Equal("repos/o/r/contents/.fsgg/telemetry-ci-attribution.json", fake.Requests.Head.Path)
    Assert.Equal<(string * string) list>([ "ref", head ], fake.Requests.Head.Query)
    for invalid in [ "[]"; body.Replace("\"file\"", "\"symlink\""); body.Replace("base64", "none"); body.Replace("telemetry-ci-attribution.json", "other.json") ] do
        let bad = Fake [ response invalid None ]
        Assert.Equal(Ok None, CiReads.readAttributionProfile (bad :> ISinglePageGitHubTransport) "o/r" head)

[<Fact>]
let ``UTEL-CI-02 native abbreviated repositories use IDs and conflicting source fields refuse`` () =
    let exact = targetRun (targetRelation "o/r" head baseSha)
    use native = System.Text.Json.JsonDocument.Parse(exact.Replace("\"repo\":{\"id\":42,\"full_name\":\"o/r\"}", "\"repo\":{\"id\":42}"))
    Assert.Equal(CiReads.Exact, CiReads.targetAssociation "o/r" 7 head (Some baseSha) native.RootElement)
    for body in [ exact.Replace("\"repo\":{\"id\":42", "\"repo\":{\"id\":43")
                  exact.Replace("\"run_attempt\":1", "\"run_attempt\":0")
                  exact.Replace("\"number\":7", "\"number\":7,\"number\":8")
                  exact.Replace("\"status\"", "\"head_repository\":{\"id\":99},\"status\"") ] do
        use doc = System.Text.Json.JsonDocument.Parse body
        Assert.NotEqual(CiReads.Exact, CiReads.targetAssociation "o/r" 7 head (Some baseSha) doc.RootElement)
    for relation in [ "null"; "false"; "7"; "\"title\"" ] do
        use doc = System.Text.Json.JsonDocument.Parse(targetRun relation)
        Assert.Equal(CiReads.Missing, CiReads.targetAssociation "o/r" 7 head (Some baseSha) doc.RootElement)

[<Fact>]
let ``UTEL-CI-02 legacy collection refuses empty target and accepts complete native tuple`` () =
    for relations, exact in [ "", false; targetRelation "o/r" head baseSha, true ] do
        let responses =
            [ populationPr head
              response ("{\"total_count\":1,\"workflow_runs\":[" + targetRun relations + "]}") None ]
            @ (if exact then [ response "{\"total_count\":0,\"jobs\":[]}" None; response "{\"run_attempt\":1}" None ] else [])
        let fake = Fake responses
        match CiReads.collect (fake :> ISinglePageGitHubTransport) "https://api.github.com" "o" "r" 7 head "ci.yml", exact with
        | Ok snapshot, true -> Assert.Equal(CiReads.Exact, snapshot.Runs.Head.TargetAssociation)
        | Ok snapshot, false ->
            Assert.Equal(Some "target-association:Missing", snapshot.Diagnostic)
            Assert.Empty snapshot.Jobs
        | other -> failwithf "%A" other
