namespace FS.GG.Coord.Tests

open Xunit
open FS.GG.Coord.OperationObservation

module OperationObservationTests =
    let private identity =
        { Issue = "FS-GG/.github#4731"; Slice = "observer"; Attempt = "original-1"; Owner = "/root/source"
          SourceRevision = String.replicate 40 "b"; IssueUpdatedAt = "native-revision"; IssueBodySha256 = String.replicate 64 "0"; Repository = "o/r"; PullRequest = 7; Head = String.replicate 40 "a"
          RunId = 42L; RunAttempt = 1; PopulationRevision = "enrolled43"; Unknowns = [ "original-unknown" ] }
    let private cursor () = start identity "boot-1" 1000L 60 |> Result.defaultWith failwith
    let private sample =
        { Head = identity.Head; RunId = 42L; RunAttempt = 1; RunStatus = "in_progress"; RunConclusion = None
          PullRequestState = "open"; Merged = false; Draft = false; Complete = true; Gaps = []
          Checks = [ { Id = 5L; Name = "build"; Status = "in_progress"; Conclusion = None; StartedAt = Some "native-start"; CompletedAt = None } ] }
    let private waiting () =
        match decide 1100L (Some sample) (cursor()) with
        | AwaitWithinDeadline(cursor, _) -> cursor
        | other -> failwithf "expected bounded wait: %A" other
    let private attention expected decision =
        match decision with
        | ReturnAttention(cursor, actual, id) ->
            Assert.Equal(expected, actual)
            Assert.Equal<string list>(identity.Unknowns, cursor.Identity.Unknowns)
            Assert.Equal(64, id.Length)
            cursor, id
        | other -> failwithf "expected attention: %A" other

    [<Fact>]
    let ``unchanged native ticks preserve revision and produce only a bounded wait`` () =
        let original = waiting()
        for now in [ 1200L; 2200L; 3200L ] do
            match decide now (Some sample) original with
            | AwaitWithinDeadline(next, delay) ->
                Assert.Equal(original.Revision, next.Revision)
                Assert.Equal(original.Budget.WorkDeadline, next.Budget.WorkDeadline)
                Assert.InRange(delay, 1, 5000)
            | other -> failwithf "unchanged tick woke caller: %A" other

    [<Fact>]
    let ``successful selected run and complete jobs return observation without readiness`` () =
        let success = { sample with RunStatus = "completed"; RunConclusion = Some "success"
                                    Checks = sample.Checks |> List.map (fun check -> { check with Status = "completed"; Conclusion = Some "success" }) }
        attention Completed (decide 1200L (Some success) (waiting())) |> ignore

    [<Fact>]
    let ``head and rerun changes fence the original identity before completeness`` () =
        attention HeadChanged (decide 1200L (Some { sample with Head = String.replicate 40 "c"; Complete = false }) (waiting())) |> ignore
        attention RunChanged (decide 1200L (Some { sample with RunAttempt = 2; Complete = false }) (waiting())) |> ignore

    [<Fact>]
    let ``missing population partial failed and unsupported conclusions retain attention`` () =
        attention Unknown (decide 1200L (Some { sample with Checks = [] }) (waiting())) |> ignore
        attention Unknown (decide 1200L (Some { sample with Complete = false }) (waiting())) |> ignore
        attention Failed (decide 1200L (Some { sample with RunStatus = "completed"; RunConclusion = Some "failure" }) (waiting())) |> ignore
        attention Unknown (decide 1200L (Some { sample with RunStatus = "completed"; RunConclusion = Some "foreign" }) (waiting())) |> ignore

    [<Fact>]
    let ``population changes and semantic changes are distinct original attention`` () =
        attention PopulationChanged (decide 1200L (Some { sample with Checks = [ { sample.Checks.Head with Id = 6L } ] }) (waiting())) |> ignore
        let changed = { sample with Draft = true }
        let retained, id = attention MaterialChange (decide 1200L (Some changed) (waiting()))
        let replay, replayId = attention MaterialChange (decide 1300L None retained)
        Assert.Equal(id, replayId)
        Assert.Equal(retained.Identity, replay.Identity)

    [<Fact>]
    let ``restart cannot renew original clock deadline identity or budgets`` () =
        let original = waiting()
        Assert.True((resume identity "boot-2" 1200L original) |> Result.isError)
        Assert.True((resume identity "boot-1" 1099L original) |> Result.isError)
        Assert.True((resume identity "boot-1" original.Budget.WorkDeadline original) |> Result.isError)
        Assert.True((resume { identity with Owner = "/root/foreign" } "boot-1" 1200L original) |> Result.isError)
        let resumed = resume identity "boot-1" 1200L original |> Result.defaultWith failwith
        Assert.Equal(original.Budget.WorkDeadline, resumed.Budget.WorkDeadline)
        Assert.Equal(original.Budget.RequestsRemaining, resumed.Budget.RequestsRemaining)
        Assert.True((resume identity "boot-1" 1200L { original with Budget = { original.Budget with RequestsRemaining = 385 } }) |> Result.isError)

    [<Fact>]
    let ``deadline and exhausted requests cannot issue another observation`` () =
        let original = waiting()
        attention Deadline (decide original.Budget.WorkDeadline None original) |> ignore
        attention BudgetExhausted (decide 1200L None { original with Budget = { original.Budget with RequestsRemaining = 0 } }) |> ignore
        let charged = chargeRequest 1200L original |> Result.defaultWith failwith
        Assert.Equal(383, charged.Budget.RequestsRemaining)
        Assert.True((chargeRequest original.Budget.WorkDeadline original) |> Result.isError)
        Assert.True((chargeOutput 4194305 original) |> Result.isError)

    [<Fact>]
    let ``pending job progress changes remain internal until actionable`` () =
        let progressed = { sample with Checks = [ { sample.Checks.Head with Status = "completed"; Conclusion = Some "success" } ] }
        match decide 1200L (Some progressed) (waiting()) with
        | AwaitWithinDeadline(cursor, _) -> Assert.Equal(2, cursor.Revision)
        | other -> failwithf "pending job progress woke caller: %A" other
