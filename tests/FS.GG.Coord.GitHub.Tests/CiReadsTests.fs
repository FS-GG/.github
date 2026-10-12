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

[<Fact>]
let ``UTEL-CI-02 legacy target rerun does not clone latest association into unwitnessed attempt`` () =
    let latest = (targetRun (targetRelation "o/r" head baseSha)).Replace("\"run_attempt\":1", "\"run_attempt\":2")
    let fake = Fake [ populationPr head; response ("{\"total_count\":1,\"workflow_runs\":[" + latest + "]}") None ]
    match CiReads.collect (fake :> ISinglePageGitHubTransport) "https://api.github.com" "o" "r" 7 head "ci.yml" with
    | Error error -> failwithf "%A" error
    | Ok snapshot ->
        Assert.Equal(Some "target-association:Missing:unwitnessed-attempt", snapshot.Diagnostic)
        Assert.Empty snapshot.Jobs
        Assert.Single snapshot.Runs |> ignore
        Assert.Equal(2, snapshot.Runs.Head.Attempt)
        Assert.Equal(2, fake.Requests.Length)

// Sanitized native GET evidence: run37997746345/attempt1 and jobs/check114047987738,
// captured 2026-10-09. This controlled subpopulation does not claim whole-head inventory.
let private retainedPositiveTarget = """
{
  "id": 37997746345,
  "run_attempt": 1,
  "path": ".github/workflows/routine-eligibility.yml",
  "event": "pull_request_target",
  "head_sha": "b6be731e8d726fb13d46a034bd0ea60fc01baf2a",
  "status": "completed",
  "conclusion": "success",
  "created_at": "2026-10-09T22:10:29Z",
  "run_started_at": "2026-10-09T22:10:29Z",
  "updated_at": "2026-10-09T22:10:36Z",
  "pull_requests": [
    {
      "number": 4344,
      "head": {
        "sha": "b6be731e8d726fb13d46a034bd0ea60fc01baf2a",
        "repo": {
          "id": 1269292704
        }
      },
      "base": {
        "sha": "a3415321ec33946d66ad1f39469935d01206bb98",
        "repo": {
          "id": 1269292704
        }
      }
    }
  ],
  "repository": {
    "id": 1269292704,
    "full_name": "FS-GG/.github"
  },
  "head_repository": {
    "id": 1269292704,
    "full_name": "FS-GG/.github"
  }
}
"""
let private retainedPositiveJobs = """
{
  "total_count": 1,
  "jobs": [
    {
      "id": 114047987738,
      "name": "routine-eligibility",
      "status": "completed",
      "conclusion": "success",
      "created_at": "2026-10-09T22:10:30Z",
      "started_at": "2026-10-09T22:10:31Z",
      "completed_at": "2026-10-09T22:10:36Z",
      "check_run_url": "https://api.github.com/repos/FS-GG/.github/check-runs/114047987738",
      "steps": [
        {
          "name": "Set up job",
          "status": "completed",
          "conclusion": "success",
          "number": 1,
          "started_at": "2026-10-09T22:10:32Z",
          "completed_at": "2026-10-09T22:10:32Z"
        },
        {
          "name": "Evaluate with the PR base's policy and validator",
          "status": "completed",
          "conclusion": "success",
          "number": 2,
          "started_at": "2026-10-09T22:10:32Z",
          "completed_at": "2026-10-09T22:10:35Z"
        },
        {
          "name": "Complete job",
          "status": "completed",
          "conclusion": "success",
          "number": 3,
          "started_at": "2026-10-09T22:10:35Z",
          "completed_at": "2026-10-09T22:10:35Z"
        }
      ]
    }
  ]
}
"""
let private retainedPositiveChecks = """
{
  "total_count": 1,
  "check_runs": [
    {
      "id": 114047987738,
      "name": "routine-eligibility",
      "status": "completed",
      "conclusion": "success",
      "started_at": "2026-10-09T22:10:31Z",
      "completed_at": "2026-10-09T22:10:36Z",
      "app": {
        "slug": "github-actions"
      }
    }
  ]
}
"""
let private retainedHead = "b6be731e8d726fb13d46a034bd0ea60fc01baf2a"
let private retainedBase = "a3415321ec33946d66ad1f39469935d01206bb98"
let private retainedPr =
    $"""{{"head":{{"sha":"{retainedHead}"}},"base":{{"ref":"main","sha":"{retainedBase}"}}}}"""
let private retainedRuns = "{\"total_count\":1,\"workflow_runs\":[" + retainedPositiveTarget + "]}"

[<Fact>]
let ``UTEL-CI-02 retained native positive target passes exact relation and population fences`` () =
    use document = System.Text.Json.JsonDocument.Parse retainedPositiveTarget
    Assert.Equal(CiReads.Exact, CiReads.targetAssociation "FS-GG/.github" 4344 retainedHead (Some retainedBase) document.RootElement)
    Assert.Equal(CiReads.Conflicting, CiReads.targetAssociation "FS-GG/.github" 4344 retainedHead (Some baseSha) document.RootElement)
    let fake = Fake [ response retainedPr None; response retainedRuns None; response retainedPositiveJobs None
                      response retainedPositiveChecks None; response retainedRuns None; response retainedPositiveChecks None ]
    match CiReads.discoverPopulation (fake :> ISinglePageGitHubTransport) "https://api.github.com" "FS-GG" ".github" 4344 retainedHead "main" retainedBase false with
    | Error error -> failwithf "%A" error
    | Ok result ->
        Assert.Empty result.Gaps
        Assert.Empty result.Pending
        Assert.True result.AdmissionWitness
        Assert.Equal("exact", result.Snapshot.Binding)
        Assert.Equal("complete", result.Snapshot.LineageCoverage)
        Assert.Equal("complete", result.CheckCoverage)
        let nativeRun = Assert.Single result.Snapshot.Runs
        Assert.Equal(37997746345L, nativeRun.Id)
        Assert.Equal(1, nativeRun.Attempt)
        Assert.Equal(CiReads.Exact, nativeRun.TargetAssociation)
        let nativeJob = Assert.Single result.Snapshot.Jobs
        Assert.Equal(114047987738L, nativeJob.Id)
        Assert.Equal(Some 114047987738L, nativeJob.CheckRunId)
        Assert.Equal("routine-eligibility", nativeJob.Name)
        Assert.Equal("Evaluate with the PR base's policy and validator", nativeJob.Steps[1].Name)
        Assert.Contains(fake.Requests, fun request -> request.Path.EndsWith("/runs/37997746345/attempts/1/jobs"))
        Assert.Equal(6, fake.Requests.Length)

[<Fact>]
let ``UTEL-CI-02 retained native positive target passes legacy attempt-specific collection`` () =
    let fake = Fake [ response retainedPr None; response retainedRuns None; response retainedPositiveJobs None
                      response "{\"run_attempt\":1}" None ]
    match CiReads.collect (fake :> ISinglePageGitHubTransport) "https://api.github.com" "FS-GG" ".github" 4344 retainedHead "routine-eligibility.yml" with
    | Error error -> failwithf "%A" error
    | Ok snapshot ->
        Assert.Equal(CiReads.Exact, snapshot.Runs.Head.TargetAssociation)
        Assert.Equal(37997746345L, snapshot.Jobs.Head.RunId)
        Assert.Equal(1, snapshot.Jobs.Head.Attempt)
        Assert.Equal(3, snapshot.Jobs.Head.Steps.Length)
        Assert.Contains(fake.Requests, fun request -> request.Path.EndsWith("/runs/37997746345/attempts/1/jobs"))

module ProviderContinuationTests =
    open System
    open System.Text
    open FS.GG.Coord.TelemetryCi.ProviderCheckpoint

    let private now = DateTimeOffset.Parse "2026-10-12T00:00:00Z"
    let private identity : Identity =
        {
            Assignment = { FeatureId = "UTEL"; ItemId = "UTEL-06.3"; AttemptId = "fixture"; ParentAttemptId = None; ProducerStream = "routine-delivery" };
            AssignmentDigest = digest (Encoding.UTF8.GetBytes "assignment");
            StoreId = "store";
            Collection = "collection";
            ApiOrigin = "https://api.github.com";
            Repository = "o/r";
            RepositoryId = 123L;
            PullRequest = 7L;
            BaseRef = "main";
            BaseSha = baseSha;
            Head = head;
            ProfileDigest = digest Array.empty;
            ParserContract = "provider/1";
        }
    let private nativeRun id =
        (populationRun id 1 "ci.yml" "pull_request" "completed" (Some "success") "2026-01-01T00:01:00Z")
            .Replace("\"pull_requests\":", "\"repository\":{\"id\":123,\"full_name\":\"o/r\"},\"pull_requests\":")
    let private nativePr =
        $"""{{"number":7,"head":{{"sha":"{head}"}},"base":{{"ref":"main","sha":"{baseSha}","repo":{{"id":123,"full_name":"o/r"}}}}}}"""

    let private nativeSuite id =
        $"""{{"id":{id},"head_sha":"{head}","repository":{{"id":123,"full_name":"o/r"}},"app":{{"id":1,"slug":"github-actions"}},"status":"completed","conclusion":"success","check_runs_url":"https://api.github.com/repos/o/r/check-suites/{id}/check-runs"}}"""

    let private nativeCheck id app status conclusion =
        (check id app status conclusion).Replace("\"app\":{", $"\"head_sha\":\"{head}\",\"check_suite\":{{\"id\":5001}},\"app\":{{\"id\":1,")

    type private NativeFake(runCount: int, mutate: Request -> int -> string -> string) =
        let requests = ResizeArray<Request * TimeSpan>()
        let counts = Dictionary<string, int>()
        member _.Requests = Seq.toList requests
        member _.Count path = match counts.TryGetValue path with true, count -> count | _ -> 0
        interface IDeadlineSinglePageGitHubTransport with
            member _.SendSingleWithin(request, remaining) =
                requests.Add(request, remaining)
                let count = match counts.TryGetValue request.Path with true, count -> count + 1 | _ -> 1
                counts[request.Path] <- count
                let numbers = request.Path.Split('/') |> Array.choose (fun value -> match Int64.TryParse value with true, number -> Some number | _ -> None)
                let body =
                    if request.Path.Contains("/contents/") then
                        "{\"type\":\"file\",\"path\":\".fsgg/telemetry-ci-attribution.json\",\"encoding\":\"base64\",\"content\":\"\"}"
                    elif request.Path.Contains("/pulls/") then nativePr
                    elif request.Path.EndsWith("/check-suites") then
                        $"""{{"total_count":1,"check_suites":[{nativeSuite 5001}]}}"""
                    elif request.Path.EndsWith("/check-runs") then
                        let rows = [ for id in 1 .. runCount -> nativeCheck (1000 + id) "github-actions" "completed" (Some "success") ]
                        $"""{{"total_count":{runCount},"check_runs":[{String.concat "," rows}]}}"""
                    elif request.Path.EndsWith("/jobs") then
                        let id = int numbers[0]
                        $"""{{"total_count":1,"jobs":[{populationJob (2000 + id) (1000 + id) "completed" (Some "success")}]}}"""
                    elif request.Path.Contains("/attempts/") then nativeRun (int numbers[0])
                    else
                        let rows = [ for id in 1 .. runCount -> nativeRun id ]
                        $"""{{"total_count":{runCount},"workflow_runs":[{String.concat "," rows}]}}"""
                response (mutate request count body) None

    let private resume fake original =
        CiReads.resumePopulation (fake :> IDeadlineSinglePageGitHubTransport) identity (fun () -> now) false 0L original

    let private finishFrom fake original =
        let mutable state = original
        let mutable terminal = None
        for _ in 1 .. 200 do
            if terminal.IsNone then
                match resume fake state with
                | Error error -> failwithf "%A" error
                | Ok result ->
                    Assert.InRange(result.Requests, 1, 40)
                    state <- Some result.Checkpoint
                    if result.Population.IsSome then terminal <- Some result
        match terminal with Some result -> result | None -> failwith "bounded continuation did not finish"

    let private finish fake = finishFrom fake None

    [<Fact>]
    let ``provider continuation resumes more than forty resources without restarting collected jobs`` () =
        let fake = NativeFake(25, fun _ _ body -> body)
        let first = match resume fake None with Ok result -> result | Error error -> failwithf "%A" error
        Assert.Equal(40, first.Requests)
        Assert.True(first.Population.IsNone)
        let retainedJobs = first.Checkpoint.Resources |> List.filter (fun resource -> resource.Key.Kind = Jobs)
        Assert.NotEmpty retainedJobs
        let second = match resume fake (Some first.Checkpoint) with Ok result -> result | Error error -> failwithf "%A" error
        Assert.InRange(second.Requests, 1, 40)
        Assert.True(second.Checkpoint.Resources.Length >= first.Checkpoint.Resources.Length)
        for _, remaining in fake.Requests do Assert.InRange(remaining.TotalSeconds, Double.Epsilon, 30.0)
        Assert.DoesNotContain("/pulls/", (fst fake.Requests[41]).Path)
        let actual = finishFrom fake (Some second.Checkpoint)
        Assert.Equal(25, actual.Population.Value.Snapshot.Jobs.Length)
        Assert.Equal("complete", actual.Population.Value.Snapshot.InventoryCoverage)

    [<Fact>]
    let ``provider continuation observes changed terminal job steps before full coverage`` () =
        let fake =
            NativeFake(1,
                fun request count body ->
                    if request.Path.EndsWith("/jobs") && count >= 2 then
                        body.Replace("\"steps\":[]", "\"steps\":[{\"number\":1,\"name\":\"late\",\"status\":\"completed\",\"conclusion\":\"success\"}]")
                    else body)
        let actual = finish fake
        Assert.Single(actual.Population.Value.Snapshot.Jobs.Head.Steps) |> ignore
        Assert.True(fake.Count("repos/o/r/actions/runs/1/attempts/1/jobs") >= 4)

    [<Fact>]
    let ``provider continuation rejects malformed steps without dropping them into a complete page`` () =
        let fake =
            NativeFake(1,
                fun request _ body ->
                    if request.Path.EndsWith("/jobs") then body.Replace("\"steps\":[]", "\"steps\":[{\"number\":1,\"status\":\"completed\"}]") else body)
        match resume fake None with
        | Error _ -> ()
        | Ok result ->
            Assert.True(result.Population.IsNone)
            Assert.Contains("malformed-provider-string:name", result.Gap.Value)
            Assert.DoesNotContain(result.Checkpoint.Resources, fun resource -> resource.Key.Kind = Jobs)

    [<Fact>]
    let ``provider continuation refuses changed admission and profile without manufacturing coverage`` () =
        let mutations: (Request -> int -> string -> string) list =
            [
                (fun (request: Request) _ body -> if request.Path.Contains("/pulls/") then body.Replace(baseSha, String.replicate 40 "e") else body)
                (fun request _ body -> if request.Path.Contains("/contents/") then body.Replace("\"content\":\"\"", "\"content\":\"eA==\"") else body)
            ]
        for change in mutations do
            let fake = NativeFake(1, change)
            match resume fake None with
            | Error _ -> ()
            | Ok result -> Assert.True(result.Population.IsNone)

    [<Fact>]
    let ``provider continuation refuses bare 304 without the exact retained body`` () =
        let fake =
            { new IDeadlineSinglePageGitHubTransport with
                member _.SendSingleWithin(request, _) =
                    if request.Path.Contains("/contents/") then
                        response "{\"type\":\"file\",\"path\":\".fsgg/telemetry-ci-attribution.json\",\"encoding\":\"base64\",\"content\":\"\"}" None
                    else Ok { Status = 304; Body = ""; Headers = Map.empty; ETag = Some "\"foreign\""; NextLink = None } }
        match CiReads.resumePopulation fake identity (fun () -> now) false 0L None with
        | Error error -> failwithf "%A" error
        | Ok result ->
            Assert.True(result.Population.IsNone)
            Assert.Contains("304-without-exact-retained-body", result.Gap.Value)

    [<Fact>]
    let ``provider continuation stops at first rate limit without retry or empty coverage`` () =
        let baseline = NativeFake(1, fun _ _ body -> body)
        let mutable refused = 0
        let fake =
            { new IDeadlineSinglePageGitHubTransport with
                member _.SendSingleWithin(request, remaining) =
                    if request.Path.Contains("/contents/") then (baseline :> IDeadlineSinglePageGitHubTransport).SendSingleWithin(request, remaining)
                    else
                        refused <- refused + 1
                        Error(RateLimited(RestBudget(Some "core"), None)) }
        match CiReads.resumePopulation fake identity (fun () -> now) false 0L None with
        | Error error -> failwithf "%A" error
        | Ok result ->
            Assert.Equal(1, refused)
            Assert.Equal(2, result.Requests)
            Assert.True(result.Population.IsNone)
            Assert.Empty(result.Checkpoint.Resources)
            Assert.Contains("RateLimited", result.Gap.Value)

    [<Fact>]
    let ``provider continuation preserves authentic admitted supersession but refuses fresh admission`` () =
        let changed = String.replicate 40 "c"
        let fake = NativeFake(1, fun request _ body -> if request.Path.Contains("/pulls/") then body.Replace(head, changed) else body)
        match CiReads.resumePopulation (fake :> IDeadlineSinglePageGitHubTransport) identity (fun () -> now) true 0L None with
        | Error error -> failwithf "%A" error
        | Ok result ->
            Assert.Equal("admitted-superseded", result.Population.Value.Snapshot.Binding)
            Assert.False(result.Population.Value.AdmissionWitness)
            Assert.Equal(head, result.Population.Value.Snapshot.Head)
        match resume fake None with
        | Error _ -> ()
        | Ok result -> Assert.True(result.Population.IsNone)

    [<Fact>]
    let ``provider same count run replacement cannot erase old resource lineage into full coverage`` () =
        let fake =
            NativeFake(1,
                fun request count body ->
                    if request.Path = "repos/o/r/actions/runs" && count >= 2 then body.Replace("\"id\":1,", "\"id\":2,") else body)
        match resume fake None with
        | Error(Malformed(_, detail)) -> Assert.Contains("disappeared-or-foreign", detail)
        | Error error -> failwithf "%A" error
        | Ok result -> Assert.True(result.Population.IsNone)

    [<Fact>]
    let ``provider late inventory addition requires new attempt jobs checks and two renewed sweeps`` () =
        let mutable enlarged = false
        let fake =
            NativeFake(1,
                fun request count body ->
                    if request.Path = "repos/o/r/actions/runs" && count >= 2 then
                        enlarged <- true
                        $"""{{"total_count":2,"workflow_runs":[{nativeRun 1},{nativeRun 2}]}}"""
                    elif request.Path.EndsWith("/check-runs") && enlarged then
                        $"""{{"total_count":2,"check_runs":[{nativeCheck 1001 "github-actions" "completed" (Some "success")},{nativeCheck 1002 "github-actions" "completed" (Some "success")}]}}"""
                    else body)
        let actual = finish fake
        Assert.Equal(2, actual.Population.Value.Snapshot.Runs.Length)
        Assert.Equal(2, actual.Population.Value.Snapshot.Jobs.Length)
        Assert.Equal(2, actual.Population.Value.Checks.Length)
        Assert.Equal("complete", actual.Population.Value.CheckCoverage)
        Assert.DoesNotContain("provider-check-suite-inventory-unproven", actual.Population.Value.Gaps)
        Assert.True(fake.Count("repos/o/r/actions/runs/2/attempts/1/jobs") >= 3)

    [<Fact>]
    let ``provider continuation refuses a pending batch until exact receiver readback`` () =
        let complete = finish (NativeFake(1, fun _ _ body -> body))
        let bytes = Encoding.UTF8.GetBytes "unchanged-original-batch"
        let pending =
            { complete.Checkpoint with
                Phase = PublicationPending;
                PendingBatchDigest = digest bytes;
                PendingBatchBase64 = Convert.ToBase64String bytes;
            }
        let fake = NativeFake(1, fun _ _ body -> body)
        Assert.True(resume fake (Some pending) |> Result.isError)
        Assert.Empty(fake.Requests)

    [<Fact>]
    let ``provider continuation resumes a middle job page under the original per invocation ceiling`` () =
        let baseline = NativeFake(25, fun _ _ body -> body)
        let requests = ResizeArray<Request>()
        let fake =
            { new IDeadlineSinglePageGitHubTransport with
                member _.SendSingleWithin(request, remaining) =
                    requests.Add request
                    if request.Path.EndsWith("/jobs") then
                        let id = request.Path.Split('/') |> Array.choose (fun value -> match Int32.TryParse value with true, value -> Some value | _ -> None) |> Array.head
                        let page = request.Query |> List.find (fst >> (=) "page") |> snd |> int
                        let checkId = 1000 + 2 * id + page
                        let body = $"""{{"total_count":2,"jobs":[{populationJob (2000 + 2 * id + page) checkId "completed" (Some "success")}]}}"""
                        let next = if page = 1 then Some($"https://api.github.com/{request.Path}?per_page=100&page=2") else None
                        response body next
                    elif request.Path.EndsWith("/check-suites") then
                        response ($"""{{"total_count":1,"check_suites":[{nativeSuite 5001}]}}""") None
                    elif request.Path.EndsWith("/check-runs") then
                        let rows = [ for id in 1 .. 25 do for page in 1 .. 2 -> nativeCheck (1000 + 2 * id + page) "github-actions" "completed" (Some "success") ]
                        response ($"""{{"total_count":50,"check_runs":[{String.concat "," rows}]}}""") None
                    else (baseline :> IDeadlineSinglePageGitHubTransport).SendSingleWithin(request, remaining) }
        let mutable checkpoint = None
        let mutable complete = None
        for _ in 1 .. 200 do
            if complete.IsNone then
                match CiReads.resumePopulation fake identity (fun () -> now) false 0L checkpoint with
                | Error error -> failwithf "%A" error
                | Ok result ->
                    Assert.InRange(result.Requests, 1, 40)
                    checkpoint <- Some result.Checkpoint
                    if result.Population.IsSome then complete <- Some result.Population.Value
        Assert.True(complete.IsSome)
        Assert.Equal(50, complete.Value.Snapshot.Jobs.Length)
        Assert.Contains(requests, fun request -> List.contains ("page", "2") request.Query && request.Path.EndsWith("/jobs"))

    [<Fact>]
    let ``provider continuation retains every older native attempt instead of latest only`` () =
        let fake =
            NativeFake(1,
                fun request _ body ->
                    if request.Path = "repos/o/r/actions/runs" || request.Path.Contains("/attempts/2") then
                        body.Replace("\"run_attempt\":1", "\"run_attempt\":2")
                    else body)
        let actual = finish fake
        Assert.Equal<int list>([ 1; 2 ], actual.Population.Value.Snapshot.Runs |> List.map _.Attempt |> List.sort)
        Assert.Equal<int list>([ 1; 2 ], actual.Population.Value.Snapshot.Jobs |> List.map _.Attempt |> List.sort)
        Assert.Equal("complete", actual.Population.Value.Snapshot.AttemptCoverage)

    [<Fact>]
    let ``provider continuation refuses an unknown repository witness before dependent reads`` () =
        let fake = NativeFake(1, fun _ _ body -> body)
        Assert.True(CiReads.resumePopulation (fake :> IDeadlineSinglePageGitHubTransport) { identity with RepositoryId = 0L } (fun () -> now) false 0L None |> Result.isError)
        Assert.Empty(fake.Requests)

    [<Fact>]
    let ``provider final fresh PR drift cannot retain an earlier stable admission`` () =
        let fake =
            NativeFake(1,
                fun request count body ->
                    if request.Path.Contains("/pulls/") && count >= 4 then body.Replace(baseSha, String.replicate 40 "e") else body)
        match resume fake None with
        | Error _ -> ()
        | Ok result ->
            Assert.True(result.Population.IsNone)
            Assert.Contains("admission-head-base", result.Gap.Value)

    [<Fact>]
    let ``provider truncated inventory and duplicate rows never authorize a partial page`` () =
        let mutations: (Request -> int -> string -> string) list =
            [
                (fun (request: Request) _ body -> if request.Path = "repos/o/r/actions/runs" then body.Replace("\"total_count\":1", "\"total_count\":2") else body)
                (fun request _ body -> if request.Path = "repos/o/r/actions/runs" then $"""{{"total_count":2,"workflow_runs":[{nativeRun 1},{nativeRun 1}]}}""" else body)
            ]
        for mutate in mutations do
            let fake = NativeFake(1, mutate)
            match resume fake None with
            | Error _ -> ()
            | Ok result -> Assert.True(result.Population.IsNone)

    [<Fact>]
    let ``provider malformed native timestamp is refused instead of discarded into complete facts`` () =
        let fake =
            NativeFake(1,
                fun request _ body ->
                    if request.Path.EndsWith("/jobs") then body.Replace("2026-01-01T00:01:00Z", "not-a-clock") else body)
        match resume fake None with
        | Error _ -> ()
        | Ok result ->
            Assert.True(result.Population.IsNone)
            Assert.Contains("malformed-provider-timestamp", result.Gap.Value)

    [<Fact>]
    let ``provider changed barrier followed by failure clears both old sweep proofs`` () =
        let initial = finish (NativeFake(1, fun _ _ body -> body))
        let checkpoint = { initial.Checkpoint with Phase = FinalBarrier; Cursor = 0 }
        let baseline = NativeFake(1, fun request _ body -> if request.Path.Contains("/pulls/") then body + " " else body)
        let fake =
            { new IDeadlineSinglePageGitHubTransport with
                member _.SendSingleWithin(request, remaining) =
                    if request.Path = "repos/o/r/actions/runs" then Error(RateLimited(RestBudget(Some "core"), None))
                    else (baseline :> IDeadlineSinglePageGitHubTransport).SendSingleWithin(request, remaining) }
        let partial =
            match CiReads.resumePopulation fake identity (fun () -> now) false 0L (Some checkpoint) with
            | Ok value -> value
            | Error error -> failwithf "%A" error
        Assert.True(partial.Population.IsNone)
        Assert.Equal(Acquiring, partial.Checkpoint.Phase)
        Assert.Equal("", partial.Checkpoint.FirstSweepDigest)
        Assert.Equal("", partial.Checkpoint.SecondSweepDigest)
        let resumed = finishFrom baseline (Some partial.Checkpoint)
        Assert.Equal(Stable, resumed.Checkpoint.Phase)
        Assert.True(baseline.Count("repos/o/r/actions/runs/1/attempts/1/jobs") >= 2)

    [<Fact>]
    let ``provider unchanged bodies advancing barrier to stable receive a new logical revision`` () =
        let initial = finish (NativeFake(1, fun _ _ body -> body))
        let checkpoint = { initial.Checkpoint with Phase = FinalBarrier; Cursor = 0 }
        let resumed = finishFrom (NativeFake(1, fun _ _ body -> body)) (Some checkpoint)
        Assert.Equal(populationDigest checkpoint.Resources, populationDigest resumed.Checkpoint.Resources)
        Assert.Equal(Stable, resumed.Checkpoint.Phase)
        Assert.True(resumed.Checkpoint.Revision > checkpoint.Revision)

    [<Fact>]
    let ``provider checks explicitly request all attempts under a complete suite graph`` () =
        let fake = NativeFake(1, fun _ _ body -> body)
        let actual = finish fake
        for request, _ in fake.Requests do
            if request.Path.EndsWith("/check-runs") then Assert.Contains(("filter", "all"), request.Query)
        Assert.Equal("complete", actual.Population.Value.CheckCoverage)
        Assert.DoesNotContain("provider-check-suite-inventory-unproven", actual.Population.Value.Gaps)

    [<Fact>]
    let ``provider pending steps or checks cannot claim terminal coverage`` () =
        let changes: (Request -> int -> string -> string) list =
            [
                (fun request _ body ->
                    if request.Path.EndsWith("/jobs") then
                        body.Replace("\"steps\":[]", "\"steps\":[{\"number\":1,\"name\":\"pending\",\"status\":\"in_progress\",\"conclusion\":null}]")
                    else body)
                (fun request _ body ->
                    if request.Path.EndsWith("/check-runs") then
                        body.Replace("\"status\":\"completed\"", "\"status\":\"in_progress\"")
                    else body)
            ]
        for change in changes do
            let actual = finish (NativeFake(1, change))
            Assert.Equal("partial", actual.Population.Value.Snapshot.TerminalCoverage)
            Assert.Contains("native-population-nonterminal", actual.Population.Value.Pending)

    [<Fact>]
    let ``provider check pagination refuses loss of the all attempt filter`` () =
        let baseline = NativeFake(1, fun _ _ body -> body)
        let fake =
            { new IDeadlineSinglePageGitHubTransport with
                member _.SendSingleWithin(request, remaining) =
                    if request.Path.EndsWith("/check-runs") then
                        response ($"""{{"total_count":2,"check_runs":[{nativeCheck 1001 "github-actions" "completed" (Some "success")}]}}""")
                            (Some($"https://api.github.com/{request.Path}?per_page=100&page=2"))
                    else (baseline :> IDeadlineSinglePageGitHubTransport).SendSingleWithin(request, remaining) }
        match CiReads.resumePopulation fake identity (fun () -> now) false 0L None with
        | Error _ -> ()
        | Ok actual ->
            Assert.True(actual.Population.IsNone)
            Assert.Contains("foreign-or-noncontiguous", actual.Gap.Value)

    [<Fact>]
    let ``provider inventories more than one thousand suites instead of capped reference checks`` () =
        let baseline = NativeFake(1, fun _ _ body -> body)
        let requests = ResizeArray<Request>()
        let fake =
            { new IDeadlineSinglePageGitHubTransport with
                member _.SendSingleWithin(request, remaining) =
                    requests.Add request
                    if request.Path.EndsWith("/check-suites") then
                        let page = request.Query |> List.find (fst >> (=) "page") |> snd |> int
                        let rows = [ for id in 5001 + (page - 1) * 100 .. min 6001 (5000 + page * 100) -> nativeSuite id ]
                        let next = if page < 11 then Some($"https://api.github.com/{request.Path}?per_page=100&page={page + 1}") else None
                        response ($"""{{"total_count":1001,"check_suites":[{String.concat "," rows}]}}""") next
                    elif request.Path.Contains("/check-suites/") && not (request.Path.Contains("/5001/")) then
                        response "{\"total_count\":0,\"check_runs\":[]}" None
                    else (baseline :> IDeadlineSinglePageGitHubTransport).SendSingleWithin(request, remaining) }
        let mutable checkpoint = None
        let mutable final = None
        for _ in 1 .. 200 do
            if final.IsNone then
                match CiReads.resumePopulation fake identity (fun () -> now) false 0L checkpoint with
                | Error error -> failwithf "%A" error
                | Ok result ->
                    Assert.InRange(result.Requests, 1, 40)
                    checkpoint <- Some result.Checkpoint
                    final <- result.Population
        Assert.True(final.IsSome)
        Assert.Equal(1001, checkpoint.Value.Resources |> List.filter (fun resource -> resource.Key.Kind = SuiteChecks) |> List.length)
        Assert.Equal("complete", final.Value.CheckCoverage)
        Assert.DoesNotContain(requests, fun request -> request.Path = $"repos/o/r/commits/{head}/check-runs")

    [<Fact>]
    let ``provider suite and check foreign identities refuse without complete inventory`` () =
        let mutations: (Request -> int -> string -> string) list =
            [
                (fun (request: Request) _ body -> if request.Path.EndsWith("/check-suites") then body.Replace(head, String.replicate 40 "e") else body)
                (fun request _ body -> if request.Path.EndsWith("/check-suites") then body.Replace("o/r", "foreign/repo") else body)
                (fun request _ body -> if request.Path.EndsWith("/check-runs") then body.Replace("\"check_suite\":{\"id\":5001}", "\"check_suite\":{\"id\":5002}") else body)
                (fun request _ body -> if request.Path.EndsWith("/check-runs") then body.Replace("\"app\":{\"id\":1,", "\"app\":{\"id\":2,") else body)
            ]
        for mutate in mutations do
            match resume (NativeFake(1, mutate)) None with
            | Error _ -> ()
            | Ok result -> Assert.True(result.Population.IsNone)

    [<Fact>]
    let ``provider missing or cyclic suite page never completes inventory`` () =
        let baseline = NativeFake(1, fun _ _ body -> body)
        for cyclic in [ false; true ] do
            let fake =
                { new IDeadlineSinglePageGitHubTransport with
                    member _.SendSingleWithin(request, remaining) =
                        if request.Path.EndsWith("/check-suites") then
                            response ($"""{{"total_count":2,"check_suites":[{nativeSuite 5001}]}}""")
                                (if cyclic then Some($"https://api.github.com/{request.Path}?per_page=100&page=1") else None)
                        else (baseline :> IDeadlineSinglePageGitHubTransport).SendSingleWithin(request, remaining) }
            match CiReads.resumePopulation fake identity (fun () -> now) false 0L None with
            | Error _ -> ()
            | Ok result -> Assert.True(result.Population.IsNone)

    [<Fact>]
    let ``provider late suite arrival is collected and validated in renewed sweeps`` () =
        let fake =
            NativeFake(1,
                fun request count body ->
                    if request.Path.EndsWith("/check-suites") && count >= 2 then
                        $"""{{"total_count":2,"check_suites":[{nativeSuite 5001},{nativeSuite 5002}]}}"""
                    elif request.Path.Contains("/check-suites/5002/") then "{\"total_count\":0,\"check_runs\":[]}"
                    else body)
        let actual = finish fake
        Assert.Equal(2, actual.Checkpoint.Resources |> List.filter (fun resource -> resource.Key.Kind = SuiteChecks) |> List.length)
        Assert.True(fake.Count("repos/o/r/check-suites/5002/check-runs") >= 3)
        Assert.Equal("complete", actual.Population.Value.CheckCoverage)

    [<Fact>]
    let ``provider suite all filter retains distinct earlier attempt check identities`` () =
        let fake =
            NativeFake(1,
                fun request _ body ->
                    if request.Path.EndsWith("/jobs") then
                        let attempt = request.Path.Split('/') |> Array.choose (fun value -> match Int32.TryParse value with true, number -> Some number | _ -> None) |> Array.last
                        body.Replace("\"id\":2001", $"\"id\":{2000 + attempt}").Replace("/check-runs/1001", $"/check-runs/{1000 + attempt}")
                    elif request.Path.EndsWith("/check-runs") then
                        $"""{{"total_count":2,"check_runs":[{nativeCheck 1001 "github-actions" "completed" (Some "success")},{nativeCheck 1002 "github-actions" "completed" (Some "success")}]}}"""
                    elif request.Path = "repos/o/r/actions/runs" || request.Path.Contains("/attempts/2") then
                        body.Replace("\"run_attempt\":1", "\"run_attempt\":2")
                    else body)
        let actual = finish fake
        Assert.Equal<int64 list>([ 1001L; 1002L ], actual.Population.Value.Checks |> List.map _.Id |> List.sort)
        Assert.Equal<int64 list>([ 1001L; 1002L ], actual.Population.Value.Snapshot.Jobs |> List.choose _.CheckRunId |> List.sort)
        Assert.Equal("complete", actual.Population.Value.CheckCoverage)

    [<Fact>]
    let ``provider matching validator 304 cannot ignore contradictory supplied pagination`` () =
        let initial = finish (NativeFake(1, fun _ _ body -> body))
        let retained = { initial.Checkpoint with Resources = initial.Checkpoint.Resources |> List.map (fun resource -> { resource with Validator = "\"exact\"" }) }
        let baseline = NativeFake(1, fun _ _ body -> body)
        let fake =
            { new IDeadlineSinglePageGitHubTransport with
                member _.SendSingleWithin(request, remaining) =
                    if request.Path.Contains("/pulls/") then
                        Assert.Equal(Some "\"exact\"", request.IfNoneMatch)
                        Ok
                            {
                                Status = 304;
                                Body = "";
                                Headers = Map.empty;
                                ETag = Some "\"exact\"";
                                NextLink = Some "https://api.github.com/repos/o/r/pulls/7?page=2";
                            }
                    else (baseline :> IDeadlineSinglePageGitHubTransport).SendSingleWithin(request, remaining) }
        match CiReads.resumePopulation fake identity (fun () -> now) false 0L (Some retained) with
        | Error error -> failwithf "%A" error
        | Ok actual ->
            Assert.True(actual.Population.IsNone)
            Assert.Contains("304-conflicting-pagination-topology", actual.Gap.Value)
            Assert.Equal(populationDigest retained.Resources, populationDigest actual.Checkpoint.Resources)

    [<Fact>]
    let ``provider retained 304 requires matching supplied validator while absent links reuse exact topology`` () =
        let initial = finish (NativeFake(1, fun _ _ body -> body))
        let retained = { initial.Checkpoint with Resources = initial.Checkpoint.Resources |> List.map (fun resource -> { resource with Validator = "\"exact\"" }) }
        let baseline = NativeFake(1, fun _ _ body -> body)
        for validator in [ "\"exact\""; "\"foreign\"" ] do
            let fake =
                { new IDeadlineSinglePageGitHubTransport with
                    member _.SendSingleWithin(request, remaining) =
                        if request.Path.Contains("/contents/") then (baseline :> IDeadlineSinglePageGitHubTransport).SendSingleWithin(request, remaining)
                        else Ok { Status = 304; Body = ""; Headers = Map.empty; ETag = Some validator; NextLink = None } }
            match CiReads.resumePopulation fake identity (fun () -> now) false 0L (Some retained) with
            | Error error -> failwithf "%A" error
            | Ok actual ->
                if validator = "\"exact\"" then
                    Assert.True(actual.Population.IsSome)
                    Assert.Equal(populationDigest retained.Resources, populationDigest actual.Checkpoint.Resources)
                else
                    Assert.True(actual.Population.IsNone)
                    Assert.Contains("304-without-exact-retained-body", actual.Gap.Value)

    [<Fact>]
    let ``retained suite graph accessor returns only validated typed native membership`` () =
        let actual = finish (NativeFake(1, fun _ _ body -> body))
        let pages =
            match CiReads.retainedSuiteGraph identity now false actual.Checkpoint with
            | Ok pages -> pages
            | Error error -> failwithf "%A" error
        Assert.Equal(2, pages.Length)
        Assert.Equal<int64 list>([ 5001L ], pages |> List.collect _.Suites |> List.map _.SuiteId)
        let memberRow = pages |> List.collect _.Checks |> List.exactlyOne
        Assert.Equal(1001L, memberRow.CheckId)
        Assert.Equal(5001L, memberRow.SuiteId)
        Assert.Equal(1L, memberRow.AppId)
        Assert.Equal<string option>(Some "github-actions", memberRow.AppSlug)
        Assert.Equal<CiReads.Check list>(actual.Population.Value.Checks, pages |> List.collect _.CheckObservations)
        for page in pages do
            Assert.Equal(now, page.ObservedAt)
            Assert.Equal(now, page.ValidatedAt)
            Assert.Equal(None, page.NextPage)

    [<Fact>]
    let ``retained suite graph accessor reparses pinned malformed and foreign member bodies`` () =
        let actual = finish (NativeFake(1, fun _ _ body -> body))
        let refuse checkpoint =
            match CiReads.retainedSuiteGraph identity now false checkpoint with
            | Error _ -> ()
            | Ok _ -> failwith "malformed retained graph must refuse"
        let replaceBody (mutate: string -> string) =
            let resources =
                actual.Checkpoint.Resources |> List.map (fun resource ->
                    if resource.Key.Kind = SuiteChecks then
                        let body = mutate resource.Body
                        let bodyDigest = digest (Encoding.UTF8.GetBytes body)
                        { resource with Body = body; BodyDigest = bodyDigest; ProjectionDigest = bodyDigest }
                    else resource)
            let current = populationDigest resources
            { actual.Checkpoint with Resources = resources; FirstSweepDigest = current; SecondSweepDigest = current }
        refuse (replaceBody (fun _ -> "{"))
        refuse (replaceBody (fun body -> body.Replace("\"check_suite\":{\"id\":5001}", "\"check_suite\":{\"id\":5002}")))
        refuse (replaceBody (fun body -> body.Replace("\"id\":1,\"slug\":\"github-actions\"", "\"id\":2,\"slug\":\"foreign-app\"")))
        refuse { actual.Checkpoint with Resources = actual.Checkpoint.Resources |> List.filter (fun resource -> resource.Key.Kind <> SuiteChecks) }
        match CiReads.retainedSuiteGraph { identity with Head = String.replicate 40 "f" } now false actual.Checkpoint with
        | Error _ -> ()
        | Ok _ -> failwith "changed candidate identity must refuse"
