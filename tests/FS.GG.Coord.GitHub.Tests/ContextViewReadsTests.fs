module FS.GG.Coord.GitHub.Tests.ContextViewReadsTests

open System
open System.Collections.Generic
open Xunit
open FS.GG.Coord.ContextView
open FS.GG.Coord.GitHub
open FS.GG.Coord.GitHub.Transport

let private head = String.replicate 40 "a"
let private response body : Errors.IoResult<Response> =
    Ok { Status = 200; Body = body; Headers = Map.empty; ETag = None; NextLink = None }

type private Fake(responses: Errors.IoResult<Response> list) =
    let queue = Queue<_>(responses)
    let calls = ResizeArray<Request>()
    member _.Calls = calls |> Seq.toList
    interface IDeadlineSinglePageGitHubTransport with
        member _.SendSingleWithin(request, _) = calls.Add request; queue.Dequeue()

let private issueJson = """{"number":4940,"title":"T","state":"open","updated_at":"2026-10-30T00:00:00Z","body":"## Done when\n\n- x"}"""
let private prJson = $"""{{"number":9,"html_url":"https://github.com/o/r/pull/9","state":"open","draft":false,"merged":false,"head":{{"sha":"{head}","ref":"routine/x"}},"base":{{"ref":"main"}},"changed_files":3,"body":null}}"""
let private checksJson total = $"""{{"total_count":{total},"check_runs":[{{"name":"build","status":"completed","conclusion":"success"}},{{"name":"test","status":"in_progress","conclusion":null}}]}}"""
let private issueRef = { Repository = "o/r"; Number = 4940 }

[<Fact>]
let ``issue read is one GET with no body and carries the verbatim native body`` () =
    let fake = Fake [ response issueJson ]
    match ContextViewReads.readIssue fake issueRef with
    | Supplied fact ->
        Assert.Equal("## Done when\n\n- x", fact.Body)
        Assert.Equal("open", fact.State)
    | other -> failwithf "%A" other
    let call = Assert.Single fake.Calls
    Assert.Equal("GET", call.Method)
    Assert.Equal("repos/o/r/issues/4940", call.Path)
    Assert.Equal(NoBody, call.Body)
    Assert.Equal(Rest, call.Budget)

[<Fact>]
let ``a pull request number a wrong number a failure and malformed JSON are unavailable never empty`` () =
    let unavailable (value: Errors.IoResult<Response>) =
        match ContextViewReads.readIssue (Fake [ value ]) issueRef with
        | Unavailable reason -> reason
        | other -> failwithf "expected Unavailable, got %A" other
    Assert.Contains("pull request", unavailable (response (issueJson.Replace("\"body\"", "\"pull_request\":{},\"body\""))))
    Assert.Contains("differs", unavailable (response (issueJson.Replace("4940", "4941"))))
    Assert.Contains("404", unavailable (Ok { Status = 404; Body = "{}"; Headers = Map.empty; ETag = None; NextLink = None }))
    Assert.Contains("duplicate", unavailable (response """{"number":4940,"number":4940}"""))
    Assert.Contains("malformed", unavailable (response "{not json"))
    Assert.Contains("continuation", unavailable (Ok { Status = 200; Body = issueJson; Headers = Map.empty; ETag = None; NextLink = Some "next" }))
    Assert.NotEmpty(unavailable (Error(Errors.Transport "boom")))
    Assert.Contains("1 MiB", unavailable (response (issueJson.Replace("- x", String.replicate 1100000 "x"))))

[<Fact>]
let ``pull request read is two GETs and null body becomes empty text`` () =
    let fake = Fake [ response prJson; response (checksJson 2) ]
    match ContextViewReads.readPullRequest fake "o/r" 9 with
    | Supplied pr ->
        Assert.Equal(head, pr.Head)
        Assert.Equal("", pr.Body)
        Assert.Equal(3, pr.ChangedFiles)
        match pr.Checks with
        | Supplied population ->
            Assert.True(population.Complete)
            Assert.Equal(2, population.Checks.Length)
            Assert.Equal(None, population.Checks[1].Conclusion)
        | other -> failwithf "%A" other
    | other -> failwithf "%A" other
    Assert.Equal<string list>([ "repos/o/r/pulls/9"; $"repos/o/r/commits/{head}/check-runs" ], fake.Calls |> List.map _.Path)
    Assert.True(fake.Calls |> List.forall (fun c -> c.Method = "GET" && c.Body = NoBody))

[<Fact>]
let ``a larger check population than the page read is marked incomplete not complete`` () =
    match ContextViewReads.readPullRequest (Fake [ response prJson; response (checksJson 130) ]) "o/r" 9 with
    | Supplied { Checks = Supplied population } -> Assert.False(population.Complete)
    | other -> failwithf "%A" other

[<Fact>]
let ``a failed check read leaves the pull request supplied with unavailable checks`` () =
    match ContextViewReads.readPullRequest (Fake [ response prJson; Error(Errors.Transport "boom") ]) "o/r" 9 with
    | Supplied { Checks = Unavailable _; Head = h } -> Assert.Equal(head, h)
    | other -> failwithf "%A" other
    match ContextViewReads.readPullRequest (Fake [ response (prJson.Replace("\"number\":9", "\"number\":8")) ]) "o/r" 9 with
    | Unavailable reason -> Assert.Contains("differs", reason)
    | other -> failwithf "%A" other
