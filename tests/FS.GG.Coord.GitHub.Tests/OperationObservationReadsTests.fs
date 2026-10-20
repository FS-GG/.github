module FS.GG.Coord.GitHub.Tests.OperationObservationReadsTests

open System
open System.Collections.Generic
open Xunit
open FS.GG.Coord.OperationObservation
open FS.GG.Coord.GitHub
open FS.GG.Coord.GitHub.Transport

let private identity : Identity =
    { Issue = "o/r#1"; Slice = "source"; Attempt = "original"; Owner = "owner"; SourceRevision = String.replicate 40 "b"; IssueUpdatedAt = "native-revision"
      IssueBodySha256 = "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855"
      Repository = "o/r"; PullRequest = 7; Head = String.replicate 40 "a"; RunId = 42L; RunAttempt = 1
      PopulationRevision = "original-population"; Unknowns = [ "unknown-1" ] }
let private response body next : Errors.IoResult<Response> =
    Ok { Status = 200; Body = body; Headers = Map.empty; ETag = None; NextLink = next }
let private issue = """{"number":1,"body":"","state":"open","updated_at":"native-revision"}"""
let private pr head = $"""{{"head":{{"sha":"{head}","repo":{{"id":8}}}},"state":"open","merged":false,"draft":false}}"""
let private run attempt = $"""{{"id":42,"run_attempt":{attempt},"head_sha":"{identity.Head}","status":"in_progress","conclusion":null,"repository":{{"full_name":"o/r"}},"pull_requests":[{{"number":7,"head":{{"sha":"{identity.Head}","repo":{{"id":8}}}}}}]}}"""
let private jobs = $"""{{"total_count":1,"jobs":[{{"id":3,"run_id":42,"run_attempt":1,"head_sha":"{identity.Head}","name":"build","status":"in_progress","conclusion":null,"started_at":"native-start","completed_at":null}}]}}"""
type private Fake(responses: Errors.IoResult<Response> list) =
    let queue = Queue<_>(responses)
    let calls = ResizeArray<Request * TimeSpan>()
    member _.Calls = calls |> Seq.toList
    interface IDeadlineSinglePageGitHubTransport with
        member _.SendSingleWithin(request, remaining) = calls.Add(request, remaining); queue.Dequeue()
let private collect fake =
    let mutable charged = 0
    let result = OperationObservationReads.collect (fake :> IDeadlineSinglePageGitHubTransport) identity (fun () -> 1000L) 61000L (fun () -> charged <- charged + 1; Ok ())
    result, charged
let private complete () =
    [ response issue None; response (pr identity.Head) None; response (run 1) None; response jobs None; response jobs None; response (run 1) None; response (pr identity.Head) None; response issue None ]

[<Fact>]
let ``complete selected sample uses only charged bounded GETs and double identity barriers`` () =
    let fake = Fake(complete())
    let result, charged = collect fake
    let sample = result |> Result.defaultWith failwith
    Assert.True(sample.Complete)
    Assert.Equal(1, sample.Checks.Length)
    Assert.Equal(8, charged)
    Assert.Equal(8, fake.Calls.Length)
    for request, remaining in fake.Calls do
        Assert.Equal<string>("GET", request.Method)
        Assert.Equal(NoBody, request.Body)
        Assert.InRange(remaining.TotalSeconds, 0.001, 30.0)

[<Fact>]
let ``head change after checks and rerun before final barrier remain original changes`` () =
    let moved = complete() |> List.mapi (fun index value -> if index = 6 then response (pr (String.replicate 40 "c")) None else value)
    let result, _ = collect (Fake moved)
    Assert.NotEqual<string>(identity.Head, (result |> Result.defaultWith failwith).Head)
    let rerun = complete() |> List.mapi (fun index value -> if index = 5 then response (run 2) None else value)
    let result, _ = collect (Fake rerun)
    Assert.Equal(2, (result |> Result.defaultWith failwith).RunAttempt)

[<Fact>]
let ``missing pages oversize duplicate fields and missing association never become complete`` () =
    let badBodies =
        [ jobs.Replace("\"total_count\":1", "\"total_count\":2")
          jobs.Replace("\"total_count\":1", "\"total_count\":1,\"total_count\":1")
          String.replicate 262145 " " ]
    for body in badBodies do
        let fake = Fake [ response issue None; response (pr identity.Head) None; response (run 1) None; response body None ]
        let result, _ = collect fake
        Assert.True(result |> Result.isError)
    let fake = Fake [ response issue None; response (pr identity.Head) None; response ((run 1).Replace("\"pull_requests\":[", "\"unsupported\":[")) None ]
    Assert.True((collect fake |> fst) |> Result.isError)

[<Fact>]
let ``exhausted allowance and elapsed deadline issue no native request`` () =
    let fake = Fake []
    let denied = OperationObservationReads.collect (fake :> IDeadlineSinglePageGitHubTransport) identity (fun () -> 1000L) 61000L (fun () -> Error "spent")
    let expired = OperationObservationReads.collect (fake :> IDeadlineSinglePageGitHubTransport) identity (fun () -> 61000L) 61000L (fun () -> Ok ())
    Assert.True(Result.isError denied && Result.isError expired)
    Assert.Empty fake.Calls

[<Fact>]
let ``two complete128job sweeps charge exact explicit pages and reject bad second page`` () =
    let job id = $"""{{"id":{id},"run_id":42,"run_attempt":1,"head_sha":"{identity.Head}","name":"build","status":"in_progress","conclusion":null,"started_at":null,"completed_at":null}}"""
    let page ids = "{\"total_count\":128,\"jobs\":[" + (ids |> List.map job |> String.concat ",") + "]}"
    let first = response (page [1..100]) (Some "provider-next-page")
    let second = response (page [101..128]) None
    let prefix = [ response issue None; response (pr identity.Head) None; response (run 1) None ]
    let suffix = [ response (run 1) None; response (pr identity.Head) None; response issue None ]
    let fake = Fake(prefix @ [first;second;first;second] @ suffix)
    let result, charged = collect fake
    Assert.Equal(128, (result |> Result.defaultWith failwith).Checks.Length)
    Assert.Equal(10, charged)
    let pages = fake.Calls |> List.filter (fun (request, _) -> request.Path.EndsWith("/jobs"))
    Assert.Equal<string list>([ "1"; "2"; "1"; "2" ], pages |> List.map (fun (request, _) -> request.Query |> List.find (fst >> (=) "page") |> snd))
    let badSeconds =
        [ response ((page [101..128]).Replace("\"total_count\":128", "\"total_count\":127")) None
          response (page [100..127]) None
          response "{" None
          response (page [101..128]) (Some "unexpected-third-page") ]
    for badSecond in badSeconds do
        Assert.True((collect (Fake(prefix @ [first;badSecond])) |> fst) |> Result.isError)

[<Fact>]
let ``native read failure and response arriving after original deadline cannot become success`` () =
    Assert.True((collect (Fake [ Error(Errors.Transport "native unavailable") ]) |> fst) |> Result.isError)
    let mutable now = 1000L
    let mutable calls = 0
    let late =
        { new IDeadlineSinglePageGitHubTransport with
            member _.SendSingleWithin(_, _) = calls <- calls + 1; now <- 2000L; response issue None }
    let result = OperationObservationReads.collect late identity (fun () -> now) 1500L (fun () -> Ok ())
    Assert.True(result |> Result.isError)
    Assert.Equal(1, calls)

[<Fact>]
let ``owning issue revision changes and jobs population changes preserve explicit gaps`` () =
    let changedIssue = complete() |> List.mapi (fun index value -> if index = 7 then response (issue.Replace("native-revision", "changed-revision")) None else value)
    Assert.True((collect (Fake changedIssue) |> fst) |> Result.isError)
    let changedJobs = complete() |> List.mapi (fun index value -> if index = 4 then response (jobs.Replace("\"id\":3", "\"id\":4")) None else value)
    Assert.True((collect (Fake changedJobs) |> fst) |> Result.isError)
