namespace FS.GG.Coord.Cli.Tests

open Xunit
open FS.GG.Coord
open FS.GG.Coord.Types
open FS.GG.Coord.GitHub
open FS.GG.Coord.Cli

/// `lint` COMPOSES seven epic roll-up-graph codes; it does not read the graph to decide them (#1050, on
/// #945's precedent).
///
/// THE DEFECT THIS PINS. `epicFindings` lived as a closure inside `let lint`, closing over
/// `ctx.Transport` — not private-but-reachable, but unreachable at all. So the well-covered part is what
/// the rules ASK (`EpicBody.statesAcceptance`/`undelegatedAcceptance` have their own facts;
/// `Done.rollUp` is driven against a scripted transport), and the UNcovered part was `lint`'s own
/// composition of them: which code fires, on what state gate, and which ones suppress
/// `EPIC-ROLLUP-READY`. `#945` fixed this exact shape for the touch-set rule by moving the verdict to a
/// module-level pure function of already-read inputs; `epicVerdict` is that move for the epic rules.
///
/// WHY THE COMPOSITION IS LOAD-BEARING. `/check-board` §4 turns each `EPIC-ROLLUP-READY` into a question
/// put to a human — "close this epic?" — biased toward yes. Two of the gate's guards defend a VACUOUS
/// truth: `graph.Children |> List.forall (not << .Open)` is `true` over an EMPTY child list and over a
/// TRUNCATED graph whose visible children happen to be closed. `EPIC-NO-CHILDREN` and
/// `EPIC-CHILDREN-TRUNCATED` land in `refusals`, and `EPIC-ROLLUP-READY` is gated on
/// `List.isEmpty refusals` — so those two suppressions are the only thing between a human and a
/// close-this-epic question about children nobody read (#266). Nothing red-lit a reorder of that gate
/// until it could be named here.
module EpicVerdictTests =

    let private child ref isOpen : Reads.SubIssue = { Ref = ref; Open = isOpen }

    let private graph total children : Reads.SubIssueSet = { Total = total; Children = children }

    /// The codes that fired, sorted — the composition is exactly "which codes, given these inputs".
    let private codesOf (findings: Client.EpicFinding list) =
        findings |> List.map (fun f -> f.Code) |> List.sort

    // Bodies, by what the acceptance-line partition makes of them (#965/#1003).
    let private delegated = "- [ ] FS-GG/.github#1 do the thing\n- [ ] FS-GG/.github#2 the other"
    let private oneDelegated = "- [ ] FS-GG/.github#1 do the thing"
    let private undelegatedBody = "- [ ] a criterion that names no child at all"
    let private noAcceptanceBody = "just prose, no task lines here"

    // ---- each of the seven codes fires when it should ----------------------------------------------

    [<Fact>]
    let ``EPIC-NO-CHILDREN fires on an open epic with zero sub-issues`` () =
        let codes = Client.epicVerdict IssueState.Open BoardStatus.Ready delegated (graph 0 []) [] |> codesOf
        Assert.Contains("EPIC-NO-CHILDREN", codes)

    [<Fact>]
    let ``EPIC-CHILDREN-TRUNCATED fires when the graph is short`` () =
        // 5 declared, 2 visible: the read could not see the whole graph.
        let g = graph 5 [ child "FS-GG/.github#1" false; child "FS-GG/.github#2" false ]
        let codes = Client.epicVerdict IssueState.Open BoardStatus.Ready delegated g [] |> codesOf
        Assert.Contains("EPIC-CHILDREN-TRUNCATED", codes)

    [<Fact>]
    let ``EPIC-DONE-OPEN-CHILD fires when the board says Done but a child is open`` () =
        let g = graph 1 [ child "FS-GG/.github#1" true ]
        let codes = Client.epicVerdict IssueState.Open BoardStatus.Done delegated g [] |> codesOf
        Assert.Contains("EPIC-DONE-OPEN-CHILD", codes)

    [<Fact>]
    let ``EPIC-UNDELEGATED-ACCEPTANCE fires on an acceptance line naming no child`` () =
        let g = graph 1 [ child "FS-GG/.github#1" false ]
        let codes = Client.epicVerdict IssueState.Open BoardStatus.Ready undelegatedBody g [] |> codesOf
        Assert.Contains("EPIC-UNDELEGATED-ACCEPTANCE", codes)

    [<Fact>]
    let ``EPIC-NO-STATED-ACCEPTANCE fires on a body with no task lines`` () =
        let g = graph 1 [ child "FS-GG/.github#1" false ]
        let codes = Client.epicVerdict IssueState.Open BoardStatus.Ready noAcceptanceBody g [] |> codesOf
        Assert.Contains("EPIC-NO-STATED-ACCEPTANCE", codes)

    [<Fact>]
    let ``EPIC-UNLINKED-CHILD fires when the WHOLE graph misses a body-declared child`` () =
        let g = graph 1 [ child "FS-GG/.github#1" false ]
        // The caller resolved one body-declared ref absent from the graph.
        let codes =
            Client.epicVerdict IssueState.Open BoardStatus.Ready oneDelegated g [ "FS-GG/.github#99" ]
            |> codesOf

        Assert.Contains("EPIC-UNLINKED-CHILD", codes)

    [<Fact>]
    let ``EPIC-ROLLUP-READY fires when every mechanical precondition holds`` () =
        // Whole graph, all children resolved, acceptance stated and fully delegated, nothing unlinked.
        let g = graph 2 [ child "FS-GG/.github#1" false; child "FS-GG/.github#2" false ]
        let codes = Client.epicVerdict IssueState.Open BoardStatus.Ready delegated g [] |> codesOf
        Assert.Equal<string list>([ "EPIC-ROLLUP-READY" ], codes)

    // ---- the two VACUOUS TRUTHS: EPIC-ROLLUP-READY must NOT fire on them (#266) --------------------

    [<Fact>]
    let ``EPIC-ROLLUP-READY must NOT fire on a childless epic - the empty-forall vacuous truth`` () =
        // `List.forall (not << .Open) []` is TRUE. EPIC-NO-CHILDREN must suppress ROLLUP-READY.
        let codes = Client.epicVerdict IssueState.Open BoardStatus.Ready delegated (graph 0 []) [] |> codesOf
        Assert.DoesNotContain("EPIC-ROLLUP-READY", codes)
        Assert.Contains("EPIC-NO-CHILDREN", codes)

    [<Fact>]
    let ``EPIC-ROLLUP-READY must NOT fire on a truncated graph whose visible children are all closed`` () =
        // The unread children could be open. EPIC-CHILDREN-TRUNCATED must suppress ROLLUP-READY.
        let g = graph 5 [ child "FS-GG/.github#1" false; child "FS-GG/.github#2" false ]
        let codes = Client.epicVerdict IssueState.Open BoardStatus.Ready delegated g [] |> codesOf
        Assert.DoesNotContain("EPIC-ROLLUP-READY", codes)
        Assert.Contains("EPIC-CHILDREN-TRUNCATED", codes)

    [<Fact>]
    let ``EPIC-ROLLUP-READY is suppressed while any visible child is still open`` () =
        // No refusal fires (an open child alone is only EPIC-DONE-OPEN-CHILD when the board says Done),
        // but the forall gate must still withhold the note.
        let g = graph 2 [ child "FS-GG/.github#1" false; child "FS-GG/.github#2" true ]
        let codes = Client.epicVerdict IssueState.Open BoardStatus.Ready delegated g [] |> codesOf
        Assert.Empty codes

    // ---- EPIC-UNLINKED-CHILD is gated on the graph being WHOLE (belt to the caller's suspenders) ----

    [<Fact>]
    let ``EPIC-UNLINKED-CHILD is suppressed on a truncated graph even if handed an unlinked ref`` () =
        // A truncated graph makes "child X is unlinked" a claim about a set already known short (#266):
        // EPIC-CHILDREN-TRUNCATED owns that case. The verdict stays sound even if a caller passes a set.
        let g = graph 5 [ child "FS-GG/.github#1" false ]
        let codes =
            Client.epicVerdict IssueState.Open BoardStatus.Ready oneDelegated g [ "FS-GG/.github#99" ]
            |> codesOf

        Assert.DoesNotContain("EPIC-UNLINKED-CHILD", codes)
        Assert.Contains("EPIC-CHILDREN-TRUNCATED", codes)

    // ---- the two acceptance rules PARTITION the task lines: never both, on any body -----------------

    [<Fact>]
    let ``EPIC-NO-STATED-ACCEPTANCE and EPIC-UNDELEGATED-ACCEPTANCE are mutually exclusive`` () =
        // The claim used to be an argument in a comment; here it is asserted over every body shape that
        // exercises the partition — delegated, un-delegated, none, and a mix.
        let bodies =
            [ delegated
              oneDelegated
              undelegatedBody
              noAcceptanceBody
              "- [ ] FS-GG/.github#1 delegated\n- [ ] a bare criterion" ]

        let g = graph 1 [ child "FS-GG/.github#1" false ]

        for body in bodies do
            let codes = Client.epicVerdict IssueState.Open BoardStatus.Ready body g [] |> codesOf
            let both =
                List.contains "EPIC-NO-STATED-ACCEPTANCE" codes
                && List.contains "EPIC-UNDELEGATED-ACCEPTANCE" codes

            Assert.False(both, $"a body fired BOTH acceptance findings, so the partition leaks: %A{codes} for %s{body}")

    // ---- STATE GATING: the open-only rules go quiet on a closed epic (parity does not vary this) ----

    [<Fact>]
    let ``a CLOSED epic fires none of the open-only rules`` () =
        // EPIC-NO-CHILDREN, -NO-STATED-ACCEPTANCE, -UNDELEGATED-ACCEPTANCE and -ROLLUP-READY are all
        // open-only: a closed epic can never roll up again, so its shape is history, not a defect.
        let codesEmpty = Client.epicVerdict IssueState.Closed BoardStatus.Done noAcceptanceBody (graph 0 []) [] |> codesOf
        Assert.Empty codesEmpty

        let g = graph 1 [ child "FS-GG/.github#1" false ]
        let codesUndelegated = Client.epicVerdict IssueState.Closed BoardStatus.Done undelegatedBody g [] |> codesOf
        Assert.DoesNotContain("EPIC-UNDELEGATED-ACCEPTANCE", codesUndelegated)
        Assert.DoesNotContain("EPIC-ROLLUP-READY", codesUndelegated)

    [<Fact>]
    let ``EPIC-DONE-OPEN-CHILD does NOT fire when the board is not Done`` () =
        // The Done-open-child refusal is gated on the board column, not the issue state.
        let g = graph 1 [ child "FS-GG/.github#1" true ]
        let codes = Client.epicVerdict IssueState.Open BoardStatus.InProgress delegated g [] |> codesOf
        Assert.DoesNotContain("EPIC-DONE-OPEN-CHILD", codes)
