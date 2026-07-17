namespace FS.GG.Coord.Cli.Tests

open Xunit
open FS.GG.Coord
open FS.GG.Coord.Types
open FS.GG.Coord.Cli

/// `lint` ASKS the usability rule; it does not decide it (#945, #864, #646).
///
/// THE DEFECT THIS PINS. `TouchSet.usability` was introduced by #864 so that "is this touch-set usable"
/// had ONE home, and `Schedulability` and `Lanes` were migrated onto it. `lint`'s BAD-TOUCH-SET rule was
/// left behind — it reached the same verdict with its own `List.exists` (the threshold), its own
/// `List.choose` (the offending tokens) and its own `List.forall` (the every/some split). It AGREED,
/// because #646 had taught it the partial case by hand.
///
/// Agreeing is not asking, and this org has the receipt: `Schedulability` and `Lanes` agreed too, right
/// up until they drifted into OPPOSITE verdicts on the same partly-unmatchable item — each pinned by a
/// green test asserting the negation of the other's. The rot is in the THRESHOLD, because
/// `TouchSet.unmatchable` hands back a LIST and a list leaves every caller to decide how long it has to
/// be.
///
/// WHY #864 COULD NOT JUST DELETE THE COPY, and what changed. `lint` renders a DIFFERENT SENTENCE for
/// "every token is dead" and "some are", and the partial one is the more urgent (#646). `Usability`
/// carried a single `Unusable` case, so migrating `lint` would have cost it that distinction — it would
/// have had to re-derive the split beside the ask, which is the same copy wearing a smaller hat. #945
/// moved the distinction INTO the rule (`AllUnmatchable` / `SomeUnmatchable`) and let the callers whose
/// verdict does not differ collapse it at their own call sites.
///
/// WHAT THIS FILE ASSERTS, and why it can. `Client.badTouchSetDetail` takes a `Usability` — not a
/// touch-set — so there is no threshold left inside it to get wrong, and the only thing a test must
/// check is that each case the core can produce renders the sentence it should. That is lint's whole
/// remaining share of the question.
module TouchSetLintTests =

    let private status = "Ready"

    let private detailFor (ts: TouchSet) =
        TouchSet.usability ts |> Client.badTouchSetDetail status

    [<Fact>]
    let ``a usable declaration produces NO finding`` () =
        Assert.Equal(None, detailFor (Declared [ Matchable "src/A/" ]))

    [<Fact>]
    let ``the three no-token shapes produce no finding HERE — they are other rules' business (#496)`` () =
        // `Undeclared`, `DeclaredNone` and `Unreadable` are `Usable` (they have no unusable tokens), and
        // that is emphatically not a claim that they are schedulable. Each is refused for its own reason
        // by a rule that runs BEFORE this one — NO-TOUCH-SET names the omission, the `none` sentinel is a
        // decision, an unread body is a failure to look. Conflating the three is exactly what #496 is
        // about, so BAD-TOUCH-SET must stay silent on all of them rather than claim a clean bill.
        for name, ts in
            [ "no declaration", Undeclared
              "the `none` sentinel", DeclaredNone
              "body never read", Unreadable "boom" ] do
            Assert.True(
                (detailFor ts).IsNone,
                $"%s{name}: BAD-TOUCH-SET spoke about a shape that is not its business (#496)"
            )

    [<Fact>]
    let ``EVERY token dead renders the 'as dead as no touch-set' sentence`` () =
        let detail =
            detailFor (Declared [ Unmatchable "**/x"; Unmatchable "**/y" ])

        match detail with
        | None -> failwith "an all-unmatchable declaration produced no BAD-TOUCH-SET finding"
        | Some d ->
            Assert.Contains("EVERY declared `Paths:` token is unmatchable", d)
            // The offending tokens, by name — and ONLY them.
            Assert.Contains("**/x, **/y", d)

    [<Fact>]
    let ``SOME tokens dead renders the WORSE sentence, and names only the dead ones (#646)`` () =
        // The #864/#646 case, and the one the two sentences exist to tell apart. Naming the matchable
        // token here would send the author to re-spell a path that already works.
        let detail =
            detailFor (
                Declared
                    [ Matchable "src/A/"
                      Unmatchable "**/x" ]
            )

        match detail with
        | None -> failwith "a partly-unmatchable declaration produced no BAD-TOUCH-SET finding — this is the #646 defect"
        | Some d ->
            Assert.Contains("at least one of its `Paths:` tokens is unmatchable", d)
            Assert.Contains("WORSE than every token being so", d)
            Assert.Contains("**/x", d)
            Assert.DoesNotContain("src/A/", d)

    [<Fact>]
    let ``the two sentences are DIFFERENT — the distinction #646 bought is not quietly collapsed`` () =
        // WHICH COLLAPSE THIS CATCHES, and which one the COMPILER catches first — measured, because the
        // first draft of this comment asserted the wrong one.
        //
        // Merging the branches (`| AllUnmatchable bad | SomeUnmatchable bad ->`) does NOT reach here: F#
        // rejects it outright with FS0026 "This rule will never be matched", because the surviving arm
        // below becomes unreachable. That is a stronger guard than a test and it is free — it is the
        // whole reason the every/some split lives in the TYPE (#945) rather than in a bool.
        //
        // What no compiler can see is the two arms staying alive and rendering the SAME words. That is
        // the realistic regression — an edit to one sentence copied over the other — and it is what this
        // asserts. Driven: pointing `SomeUnmatchable` at `AllUnmatchable`'s sentence fails the suite.
        //
        // Note this assertion is the weaker half of that guard. Two sentences differing SOMEWHERE is not
        // two sentences meaning different things; the `WORSE`/`at least one` assertions above are what
        // actually pin the meanings, and they are what caught the driven mutation. This one is the
        // backstop for a collapse that leaves no other trace.
        let all = detailFor (Declared [ Unmatchable "**/x" ])

        let some =
            detailFor (
                Declared
                    [ Matchable "src/A/"
                      Unmatchable "**/x" ]
            )

        Assert.True(all.IsSome && some.IsSome, "both shapes must produce a finding")
        Assert.NotEqual<string option>(all, some)

    [<Fact>]
    let ``lint's verdict is the CORE's for every shape — the agreement #864 broke, now three-way`` () =
        // THE PIN. Not "does lint agree today" — it did, for lint's whole life, and so did `Lanes` and
        // `Schedulability` until they didn't. This asserts the two cannot disagree by construction: lint
        // speaks exactly when `TouchSet.usability` says unusable, and is silent exactly when it does not.
        // Its sibling in Core.Tests (`LANES AND THE SCHEDULER AGREE ABOUT EVERY TOUCH-SET SHAPE`) pins the
        // other two consumers to the same rule, so all three surfaces are tied to one function.
        let shapes =
            [ "every token live", Declared [ Matchable "src/A/" ]
              "every token dead", Declared [ Unmatchable "**/x" ]
              "SOME tokens dead — the #864 case",
              Declared
                  [ Matchable "src/A/"
                    Unmatchable "**/x" ]
              "some dead, dead one first",
              Declared
                  [ Unmatchable "**/x"
                    Matchable "src/A/" ]
              "no declaration", Undeclared
              "the `none` sentinel", DeclaredNone
              "body never read", Unreadable "boom" ]

        for name, ts in shapes do
            let usable = TouchSet.usability ts = TouchSet.Usable
            let lintSilent = (detailFor ts).IsNone

            let said = if lintSilent then "said nothing" else "raised BAD-TOUCH-SET"

            Assert.True(
                (usable = lintSilent),
                $"%s{name}: TouchSet.usability says usable=%b{usable} but lint %s{said} — lint is deciding usability for itself again (#945)"
            )
