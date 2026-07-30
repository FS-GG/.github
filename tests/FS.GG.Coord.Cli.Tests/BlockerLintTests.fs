namespace FS.GG.Coord.Cli.Tests

open Xunit
open FS.GG.Coord
open FS.GG.Coord.Types
open FS.GG.Coord.Cli

module BlockerLintTests =

    let private ref' n : Ref = { Owner = "FS-GG"; Repo = ".github"; Number = n }

    [<Fact>]
    let ``BLOCKED-NO-REASON fires only for an unreasoned open blocked row`` () =
        let verdict body blockedBy =
            Client.blockedNoReasonVerdict IssueState.Open BoardStatus.Blocked blockedBy body

        Assert.True((verdict "Paths: src/A.fs" "").IsSome)
        Assert.True((verdict "Blocked on: human/decision" "").IsNone)
        Assert.True((verdict "Blocked on: human/action" "").IsNone)
        Assert.True((verdict "Paths: src/A.fs" "FS-GG/.github#2").IsNone)
        Assert.True((Client.blockedNoReasonVerdict IssueState.Closed BoardStatus.Blocked "" "").IsNone)

    [<Fact>]
    let ``#1739 human park is noted only after every machine blocker resolves`` () =
        let blocker state = { Ref = Some(ref' 2); Raw = ".github#2"; State = state }
        let verdict body blockers =
            Client.humanParkResolvedVerdict IssueState.Open BoardStatus.Blocked blockers body

        Assert.Contains("human decision", verdict "Blocked on: human/decision" [ blocker BlockerClosed ] |> Option.defaultValue "")
        Assert.Contains("human action", verdict "Blocked on: human/action" [ blocker BlockerMerged ] |> Option.defaultValue "")
        Assert.True((verdict "Blocked on: human/decision" [ blocker BlockerOpen ]).IsNone)
        Assert.True((verdict "Blocked on: human/decision" [ blocker BlockerUnknown ]).IsNone)
        Assert.True((verdict "Blocked on: human/decision" [ blocker BlockerUnparseable ]).IsNone)
        Assert.True((verdict "Blocked on: human/decision" []).IsNone)

    [<Fact>]
    let ``BLOCKER-CYCLE reports each member of a genuine ring and ignores a chain`` () =
        let a, b, c = ref' 1, ref' 2, ref' 3
        let openBlocker target = { Ref = Some target; Raw = target.Short; State = BlockerOpen }
        let ring = [ a, [ openBlocker b ]; b, [ openBlocker a ]; c, [ openBlocker b ] ]
        let findings = Client.blockerCycleVerdicts ring

        Assert.Equal<Ref list>([ a; b ], findings |> List.map fst |> List.sortBy (fun r -> r.Number))
        Assert.All(findings, fun (_, detail) -> Assert.Contains("cycle", detail))
