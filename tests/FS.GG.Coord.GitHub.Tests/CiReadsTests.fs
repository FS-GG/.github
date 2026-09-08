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
        member _.SendSingle request = requests.Add request; queue.Dequeue()

let private response body next = Ok { Status = 200; Body = body; Headers = Map.empty; ETag = None; NextLink = next }
let private head = String.replicate 40 "a"
let private pr = response ($"""{{"head":{{"sha":"{head}"}},"merge_commit_sha":null}}""") None
let private run id attempt = $"""{{"id":{id},"run_attempt":{attempt},"path":".github/workflows/ci.yml","event":"pull_request","head_sha":"{head}","status":"completed","conclusion":"success","created_at":"2026-01-01T00:00:00Z","run_started_at":"2026-01-01T00:00:01Z","updated_at":"2026-01-01T00:01:01Z","pull_requests":[]}}"""
let private job runId attempt id startAt endAt = $"""{{"id":{id},"name":"build","status":"completed","conclusion":"success","created_at":"{startAt}","started_at":"{startAt}","completed_at":"{endAt}","runner_name":"PRIVATE-RUNNER","steps":[{{"number":1,"name":"test","status":"completed","conclusion":"success","started_at":"{startAt}","completed_at":"{endAt}"}}]}}"""

[<Fact>]
let ``UTEL-04A collects bounded run pages and attempt-specific jobs with empty native PR joins`` () =
    let next = "https://api.github.com/repos/o/r/actions/workflows/ci.yml/runs?head_sha=" + head + "&per_page=100&page=2"
    let fake = Fake [ pr; response ($"""{{"total_count":2,"workflow_runs":[{run 10 1}]}}""") (Some next); response ($"""{{"total_count":2,"workflow_runs":[{run 10 2}]}}""") None; response ($"""{{"total_count":1,"jobs":[{job 10 1 101 "2026-01-01T00:00:00Z" "2026-01-01T00:01:00Z"}]}}""") None; response ($"""{{"total_count":1,"jobs":[{job 10 2 102 "2026-01-01T00:02:00Z" "2026-01-01T00:02:40Z"}]}}""") None; response "{\"run_attempt\":2}" None ]
    let result = CiReads.collect (fake :> ISinglePageGitHubTransport) "https://api.github.com" "o" "r" 7 head "ci.yml"
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
    let fake = Fake [ pr; response ($"""{{"total_count":1,"workflow_runs":[{run 10 1}]}}""") None; response "{\"total_count\":0,\"jobs\":[]}" None; response "{\"run_attempt\":2}" None ]
    match CiReads.collect (fake :> ISinglePageGitHubTransport) "https://api.github.com" "o" "r" 7 head "ci.yml" with
    | Ok snapshot -> Assert.Equal("partial", snapshot.AttemptCoverage); Assert.Equal(Some "attempt-changed-or-unverified", snapshot.Diagnostic)
    | Error error -> failwithf "%A" error

[<Fact>]
let ``UTEL-04A unsafe continuation is retained as partial rather than complete`` () =
    let fake = Fake [ pr; response ($"""{{"total_count":2,"workflow_runs":[{run 10 1}]}}""") (Some "https://evil.invalid/repos/o/r/actions/runs?page=2") ]
    match CiReads.collect (fake :> ISinglePageGitHubTransport) "https://api.github.com" "o" "r" 7 head "ci.yml" with
    | Ok snapshot -> Assert.Equal("partial", snapshot.InventoryCoverage); Assert.Equal(Some "run-pagination-incomplete", snapshot.Diagnostic)
    | Error error -> failwithf "%A" error

[<Fact>]
let ``UTEL-04A wrong head stays unresolved and does not enumerate runs`` () =
    let fake = Fake [ response "{\"head\":{\"sha\":\"bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb\"},\"merge_commit_sha\":null}" None ]
    match CiReads.collect (fake :> ISinglePageGitHubTransport) "https://api.github.com" "o" "r" 7 head "ci.yml" with
    | Ok snapshot -> Assert.Equal("unresolved", snapshot.Binding); Assert.Equal("unknown", snapshot.InventoryCoverage); Assert.Single(fake.Requests) |> ignore
    | Error error -> failwithf "%A" error

[<Fact>]
let ``UTEL-04A conflicting duplicate jobs are rejected rather than covered`` () =
    let first = job 10 1 101 "2026-01-01T00:00:00Z" "2026-01-01T00:01:00Z"
    let changed = job 10 1 101 "2026-01-01T00:00:00Z" "2026-01-01T00:01:01Z"
    let fake = Fake [ pr; response ($"""{{"total_count":1,"workflow_runs":[{run 10 1}]}}""") None; response ($"""{{"total_count":1,"jobs":[{first},{changed}]}}""") None ]
    match CiReads.collect (fake :> ISinglePageGitHubTransport) "https://api.github.com" "o" "r" 7 head "ci.yml" with
    | Error(Malformed(_, detail)) -> Assert.Contains("conflicting duplicate", detail)
    | result -> failwithf "expected conflict, got %A" result

[<Fact>]
let ``UTEL-04A request budget stops without retry and retains partial job coverage`` () =
    let runs = [ 1L .. 19L ] |> List.map (fun id -> run id 1) |> String.concat ","
    let emptyJobs = [ for _ in 1 .. 18 -> response "{\"total_count\":0,\"jobs\":[]}" None ]
    let fake = Fake(pr :: response ($"""{{"total_count":19,"workflow_runs":[{runs}]}}""") None :: emptyJobs)
    match CiReads.collect (fake :> ISinglePageGitHubTransport) "https://api.github.com" "o" "r" 7 head "ci.yml" with
    | Ok snapshot -> Assert.Equal("partial", snapshot.JobPageCoverage); Assert.Equal(20, fake.Requests.Length)
    | Error error -> failwithf "%A" error
