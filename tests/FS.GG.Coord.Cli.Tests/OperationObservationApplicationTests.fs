module FS.GG.Coord.Cli.Tests.OperationObservationApplicationTests

open System
open Xunit
open FS.GG.Coord.OperationObservation
open FS.GG.Coord.Cli
open FS.GG.Coord.GitHub
open FS.GG.Coord.GitHub.Transport

let private identity : Identity =
    { Issue = "o/r#1"; Slice = "source"; Attempt = "original"; Owner = "owner"; SourceRevision = String.replicate 40 "b"; IssueUpdatedAt = "native-revision"
      IssueBodySha256 = "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855"
      Repository = "o/r"; PullRequest = 7; Head = String.replicate 40 "a"; RunId = 42L; RunAttempt = 1
      PopulationRevision = "population-1"; Unknowns = [ "unknown-original" ] }
let private original () = start identity "boot-domain" 1000L 11 |> Result.defaultWith failwith
let private issue = """{"number":1,"body":"","state":"open","updated_at":"native-revision"}"""
let private pr = $"""{{"head":{{"sha":"{identity.Head}","repo":{{"id":8}}}},"state":"open","merged":false,"draft":false}}"""
let private run = $"""{{"id":42,"run_attempt":1,"head_sha":"{identity.Head}","status":"in_progress","conclusion":null,"repository":{{"full_name":"o/r"}},"pull_requests":[{{"number":7,"head":{{"sha":"{identity.Head}","repo":{{"id":8}}}}}}]}}"""
let private jobs = $"""{{"total_count":1,"jobs":[{{"id":3,"run_id":42,"run_attempt":1,"head_sha":"{identity.Head}","name":"build","status":"in_progress","conclusion":null,"started_at":null,"completed_at":null}}]}}"""
type private Unchanged() =
    let mutable calls = 0
    member _.Calls = calls
    interface IDeadlineSinglePageGitHubTransport with
        member _.SendSingleWithin(request, _) =
            calls <- calls + 1
            let body = if request.Path.Contains("/issues/") then issue elif request.Path.Contains("/pulls/") then pr elif request.Path.EndsWith("/jobs") then jobs else run
            Ok { Status = 200; Body = body; Headers = Map.empty; ETag = None; NextLink = None }

let private completedTransport () =
    { new IDeadlineSinglePageGitHubTransport with
        member _.SendSingleWithin(request, _) =
            let body =
                if request.Path.Contains("/issues/") then issue
                elif request.Path.Contains("/pulls/") then pr
                elif request.Path.EndsWith("/jobs") then jobs
                else run
            let body = body.Replace("\"in_progress\"", "\"completed\"").Replace("\"conclusion\":null", "\"conclusion\":\"success\"")
            Ok { Status = 200; Body = body; Headers = Map.empty; ETag = None; NextLink = None } }

[<Fact>]
let ``finite application coalesces unchanged ticks into one deadline result with retained spend`` () =
    let transport = Unchanged()
    let mutable now = 1000L
    let saved = ResizeArray<Cursor>()
    let mutable evidenceCalls = 0
    let decision = OperationObservationApplication.observe (transport :> IDeadlineSinglePageGitHubTransport) (fun () -> now)
                       (fun delay -> now <- now + int64 delay) saved.Add (fun _ -> evidenceCalls <- evidenceCalls + 1; "private-evidence", 1, ignore) (original())
    match decision with
    | ReturnAttention(cursor, Deadline, _) ->
        Assert.Equal(24, transport.Calls)
        Assert.Equal(360, cursor.Budget.RequestsRemaining)
        Assert.Equal(1, cursor.Revision)
        Assert.Equal<string list>(identity.Unknowns, cursor.Identity.Unknowns)
        Assert.Equal(0, evidenceCalls)
        Assert.Equal((original()).Budget.WorkDeadline, cursor.Budget.WorkDeadline)
        Assert.True(saved.Count > transport.Calls)
    | other -> failwithf "expected one bounded deadline attention: %A" other

[<Fact>]
let ``interruption retains original allowance and continuation roundtrip`` () =
    let transport = Unchanged()
    let mutable saved = original()
    let result = OperationObservationApplication.observe (transport :> IDeadlineSinglePageGitHubTransport) (fun () -> 1000L)
                     (fun _ -> raise (OperationCanceledException())) (fun cursor -> saved <- cursor) (fun _ -> "evidence", 1, ignore) saved
    match result with
    | ReturnAttention(cursor, Interrupted, id) ->
        Assert.Equal(376, cursor.Budget.RequestsRemaining)
        let decoded = OperationObservationApplication.encodeCursor cursor |> OperationObservationApplication.decodeCursor |> Result.defaultWith failwith
        Assert.Equal(cursor, decoded)
        match decide 1001L None decoded with
        | ReturnAttention(_, Interrupted, replay) -> Assert.Equal<string>(id, replay)
        | other -> failwithf "lost original attention: %A" other
    | other -> failwithf "expected interruption: %A" other

[<Fact>]
let ``corrupt missing unknown and duplicate cursor properties refuse before native IO`` () =
    let raw = OperationObservationApplication.encodeCursor (original())
    Assert.True((OperationObservationApplication.decodeCursor "{}") |> Result.isError)
    Assert.True((OperationObservationApplication.decodeCursor (raw.Replace("unknown-original", "changed-original"))) |> Result.isError)
    Assert.True((OperationObservationApplication.decodeCursor (raw.Replace("{\"schema\":", "{\"schema\":\"duplicate\",\"schema\":"))) |> Result.isError)
    Assert.True((OperationObservationApplication.parseInput "{\"schema\":\"unsupported\"}") |> Result.isError)

[<Fact>]
let ``late evidence or terminal persistence cannot emit completed observation`` () =
    let mutable now = 1000L
    let mutable saves = 0
    let completed = completedTransport()
    let result = OperationObservationApplication.observe completed (fun () -> now) ignore
                     (fun cursor -> saves <- saves + 1; if cursor.Evidence.IsSome then now <- cursor.Budget.WorkDeadline)
                     (fun _ -> "private-evidence", 10, (fun () -> ())) (original())
    match result with
    | ReturnAttention(_, Deadline, _) -> Assert.True(saves > 8)
    | other -> failwithf "late evidence accepted a positive result: %A" other

[<Fact>]
let ``crash before evidence custody leaves only charged nonterminal continuation`` () =
    let mutable saved = original()
    let mutable evidenceWrites = 0
    let staged = ResizeArray<Cursor>()
    let persist cursor = saved <- cursor; staged.Add cursor
    Assert.Throws<InvalidOperationException>(fun () ->
        OperationObservationApplication.observe (completedTransport()) (fun () -> 1000L) ignore persist
            (fun _ -> "private-evidence", 10, (fun () ->
                evidenceWrites <- evidenceWrites + 1
                invalidOp "injected stop before evidence custody")) (original()) |> ignore) |> ignore
    Assert.Equal(1, evidenceWrites)
    Assert.True(staged |> Seq.forall (fun cursor -> cursor.Terminal <> Some Completed))
    Assert.True(saved.Terminal.IsNone)
    Assert.True(saved.Evidence.IsNone)
    Assert.Equal(376, saved.Budget.RequestsRemaining)
    Assert.Equal((original()).Budget.OutputBytesRemaining - 10, saved.Budget.OutputBytesRemaining)
    let recovered = saved |> OperationObservationApplication.encodeCursor |> OperationObservationApplication.decodeCursor |> Result.defaultWith failwith
    let resumed = resume identity "boot-domain" 1001L recovered |> Result.defaultWith failwith
    match decide 1001L None resumed with
    | ObserveOriginal _ -> ()
    | other -> failwithf "pre-evidence crash left a replayable terminal: %A" other
    let mutable evidenceRetained = false
    let accepted = OperationObservationApplication.observe (completedTransport()) (fun () -> 1001L) ignore
                       (fun cursor ->
                           if cursor.Terminal = Some Completed then Assert.True(evidenceRetained && cursor.Evidence = Some "recovered-evidence")
                           saved <- cursor)
                       (fun _ -> "recovered-evidence", 10, (fun () -> evidenceRetained <- true)) resumed
    match accepted with
    | ReturnAttention(cursor, Completed, _) ->
        Assert.True(evidenceRetained)
        Assert.Equal(368, cursor.Budget.RequestsRemaining)
        Assert.Equal((original()).Budget.WorkDeadline, cursor.Budget.WorkDeadline)
        Assert.Equal<string list>(identity.Unknowns, cursor.Identity.Unknowns)
    | other -> failwithf "fresh original observation did not establish custody: %A" other
