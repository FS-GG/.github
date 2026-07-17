namespace FS.GG.Coord.Tests

open Xunit
open FS.GG.Coord
open FS.GG.Coord.Types

/// The `Blocked on: human/...` sentinel grammar (#1103 leg 2). It shares `Paths:`'s fence-skipping and
/// up-to-three-leading-spaces rule, so every incident that family had is a test to inherit here.
module HumanBlockTests =

    [<Fact>]
    let ``no Blocked on line is None`` () =
        Assert.Equal(None, HumanBlock.parse "Just an ordinary body.\n\nPaths: src/A")

    [<Fact>]
    let ``human/decision parses to AwaitingHumanDecision`` () =
        Assert.Equal(Some AwaitingHumanDecision, HumanBlock.parse "x\n\nBlocked on: human/decision")

    [<Fact>]
    let ``human/action parses to AwaitingHumanAction`` () =
        Assert.Equal(Some AwaitingHumanAction, HumanBlock.parse "x\n\nBlocked on: human/action")

    [<Fact>]
    let ``case and surrounding space do not matter`` () =
        Assert.Equal(Some AwaitingHumanDecision, HumanBlock.parse "x\n\n  Blocked On:   HUMAN/DECISION  ")

    [<Fact>]
    let ``a Blocked on line inside a fence is a QUOTATION, not a declaration (#277)`` () =
        let body =
            "How to park on a human:\n\
             \n\
             ```\n\
             Blocked on: human/decision\n\
             ```\n\
             \n\
             ...and this issue does not declare one."

        Assert.Equal(None, HumanBlock.parse body)

    [<Fact>]
    let ``an unrecognised Blocked on value is NOT a sentinel — a stray ref is not this line`` () =
        // `Blocked on: #123` is a `Blocked by` ref written in the wrong place; reading it as a human-block
        // would refuse an item that has an ordinary, resolvable blocker.
        Assert.Equal(None, HumanBlock.parse "x\n\nBlocked on: #123")

    [<Fact>]
    let ``decision DOMINATES action when a body carries both`` () =
        // Never weaken the stronger "a human must choose" to a mere pending action.
        let body = "x\n\nBlocked on: human/action\n\nBlocked on: human/decision"
        Assert.Equal(Some AwaitingHumanDecision, HumanBlock.parse body)
