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

    // ---- `Blocked by:` body-line declarations (.github#2079) ----------------------------------------
    //
    // The `FS.GG.Templates#348` shape: a park's edge was recorded as a `Blocked by:` BODY line instead
    // of the board field. `parseBlockedByLines` hands the raw declarations back, un-canonicalized, so a
    // caller holding the field's own value can tell whether the body agrees with it or diverges.

    [<Fact>]
    let ``no Blocked by line is the empty list`` () =
        Assert.Equal<string list>([], HumanBlock.parseBlockedByLines "Just an ordinary body.\n\nPaths: src/A")

    [<Fact>]
    let ``one Blocked by line is returned raw, un-canonicalized`` () =
        Assert.Equal<string list>(
            [ "FS-GG/FS.GG.SDD#9" ],
            HumanBlock.parseBlockedByLines "x\n\nBlocked by: FS-GG/FS.GG.SDD#9"
        )

    [<Fact>]
    let ``every matching line is returned, in body order`` () =
        let body = "x\n\nBlocked by: #1\n\nsome prose\n\nBlocked by: #2, #3"
        Assert.Equal<string list>([ "#1"; "#2, #3" ], HumanBlock.parseBlockedByLines body)

    [<Fact>]
    let ``leading space and case of the KEY do not matter — the VALUE is returned raw`` () =
        // Unlike `Blocked on:` (whose values are a closed two-member vocabulary the classifier trims),
        // `parseBlockedByLines` hands back the value UN-TRIMMED — canonicalizing it is the caller's job
        // (`Blockers.canonicalizeBlockedBy`, which trims internally), and this function promises only
        // "what the line said", not "what it meant".
        Assert.Equal<string list>(
            [ "fs-gg/fs.gg.sdd#9  " ],
            HumanBlock.parseBlockedByLines "x\n\n  Blocked By:   fs-gg/fs.gg.sdd#9  "
        )

    [<Fact>]
    let ``a Blocked by line inside a fence is a QUOTATION, not a declaration (#277)`` () =
        let body =
            "How to record a dependency:\n\
             \n\
             ```\n\
             Blocked by: FS-GG/FS.GG.SDD#9\n\
             ```\n\
             \n\
             ...and this issue does not declare one."

        Assert.Equal<string list>([], HumanBlock.parseBlockedByLines body)

    [<Fact>]
    let ``Blocked on and Blocked by are DIFFERENT lines — one does not satisfy the other`` () =
        Assert.Equal<string list>([], HumanBlock.parseBlockedByLines "x\n\nBlocked on: human/decision")
        Assert.Equal(None, HumanBlock.parse "x\n\nBlocked by: FS-GG/FS.GG.SDD#9")
