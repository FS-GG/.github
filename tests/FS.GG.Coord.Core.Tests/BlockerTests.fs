namespace FS.GG.Coord.Tests

open System
open System.IO
open System.Text.RegularExpressions
open Xunit
open FS.GG.Coord
open FS.GG.Coord.Types

/// The `Blocked by` rule. One definition, and the incidents that bought each clause.
module BlockerTests =

    let private ref n =
        { Owner = "FS-GG"
          Repo = "FS.GG.SDD"
          Number = n }

    let private blocker n state =
        { Ref = Some(ref n)
          Raw = (ref n).Short
          State = state }

    /// A blocker whose `Blocked by` text is not a ref at all — the case the record could not hold.
    let private prose text =
        { Ref = None
          Raw = text
          State = BlockerUnparseable }

    // ---- #476: MERGED resolves. This is the clause that was missing. --------------------------------
    //
    // `Blocked by` may name a PULL REQUEST, whose state is OPEN | CLOSED | MERGED. A rule that clears
    // only on CLOSED therefore unblocks when the PR is ABANDONED and blocks FOREVER once it is
    // FINISHED: the gate opened precisely when the blocking work was thrown away, and shut precisely
    // when it was done.
    //
    // Live example, and it stayed stuck for weeks: FS.GG.SDD#350 was `Blocked by: .github#449` — the
    // PR carrying the very ADR that resolved it.

    [<Fact>]
    let ``#476 a MERGED blocker is RESOLVED — the gate must not shut when the work finishes`` () =
        Assert.True(Blockers.isResolved (blocker 449 BlockerMerged))

    [<Fact>]
    let ``#476 a CLOSED blocker is resolved`` () =
        Assert.True(Blockers.isResolved (blocker 449 BlockerClosed))

    [<Fact>]
    let ``#476 an OPEN blocker holds`` () =
        Assert.False(Blockers.isResolved (blocker 449 BlockerOpen))

    // ---- #266 / #421: an unresolvable blocker BLOCKS. The safe direction is the only direction. ------
    //
    // "I could not look" is not "I looked and it is fine". A blocker that was never added to the board
    // reads as UNKNOWN — not CLOSED — and a rule that treated UNKNOWN as cleared would hand a worker
    // an item another item still owns. The inverse (UNKNOWN blocks forever) is a real cost, and it is
    // the RIGHT cost: the fix is to resolve the ref, not to guess.

    [<Fact>]
    let ``#266 an UNKNOWN blocker blocks — a failed lookup is not a cleared blocker`` () =
        Assert.False(Blockers.isResolved (blocker 999 BlockerUnknown))

    [<Fact>]
    let ``#266 an UNPARSEABLE ref blocks — prose in a dependency field is not permission`` () =
        Assert.False(Blockers.isResolved (prose "RESOLVED: shipped last week"))

    // ---- PROSE IS NOT A REF, AND THE RECORD USED TO INSIST IT WAS -----------------------------------
    //
    // `Blocker` demanded a `Ref`, so `BlockerUnparseable` — the one state whose entire meaning is "this
    // is not a ref" — was the one state the type could not hold. This test could only be written by
    // fabricating `#0` and pretending. The client fabricated too, in its own way: its `jq capture`
    // matched nothing, produced NO object, and the whole blockers array collapsed to `[]`. So an item
    // the client had just classified BLOCKED reached the engine UNBLOCKED — and the engine's answer is
    // the one a worker acts on, so a worker would have been handed it.
    //
    // The corpus caught that (a `next --repo governance` where bash passed the item over and the engine
    // did not). The type now says what was always true: the ref is an OPTION, and the prose survives.

    [<Fact>]
    let ``a prose blocker keeps its TEXT — it is the only thing there is to show a human`` () =
        let b = prose "RESOLVED: shipped last week"
        Assert.Equal("RESOLVED: shipped last week", b.Display)
        Assert.True(b.Ref.IsNone)

    [<Fact>]
    let ``a prose blocker still BLOCKS — an unreadable dependency is not an absent one`` () =
        Assert.Equal<Blocker list>(
            [ prose "blocked on the design review" ],
            Blockers.unresolved [ prose "blocked on the design review" ]
        )

    [<Fact>]
    let ``a parsed blocker still displays as its canonical ref`` () =
        Assert.Equal("FS.GG.SDD#449", (blocker 449 BlockerOpen).Display)

    // ---- the aggregate ------------------------------------------------------------------------------

    [<Fact>]
    let ``an item whose blockers are all CLOSED or MERGED is not blocked`` () =
        let blockers = [ blocker 1 BlockerClosed; blocker 2 BlockerMerged ]
        Assert.Empty(Blockers.unresolved blockers)

    [<Fact>]
    let ``one OPEN blocker among resolved ones still holds the item`` () =
        let blockers =
            [ blocker 1 BlockerClosed
              blocker 2 BlockerMerged
              blocker 3 BlockerOpen ]

        let holding = Blockers.unresolved blockers
        Assert.Equal(1, List.length holding)
        Assert.Equal(3, holding.Head.Ref.Value.Number)

    [<Fact>]
    let ``#520 the unresolved set NAMES only the blockers still holding — never a MERGED one`` () =
        // The reader-harm this prevents, and it is why the rule may not be re-spelled per surface: two
        // copies of the pre-#476 rule survived in the bash client — the BLOCKED BY column and the
        // "nothing is startable" diagnostic — so `.blocked` computed FALSE while both surfaces named a
        // MERGED pull request as the reason. A worker sent to go and look at finished work.
        let blockers = [ blocker 449 BlockerMerged; blocker 500 BlockerOpen ]
        let holding = Blockers.unresolved blockers
        Assert.DoesNotContain(blocker 449 BlockerMerged, holding)
        Assert.Contains(blocker 500 BlockerOpen, holding)

    // ---- the WRITE gate (case 13): `Blocked by` is a typed edge, not a resolution log -----------------
    //
    // Projects v2 has no dependency field, so `Blocked by` is TEXT. In bash it drifted into a free-form log
    // ("RESOLVED: #8 closed, shipped @d80a8ae"), and `.blocked` — reading that field back as refs — could
    // not parse it. `canonicalizeBlockedBy` is the gate on the WRITE: every accepted form reduces to one
    // canonical `owner/repo#n`, and prose is refused before it can be stored. `SDD` here is the BLOCKED
    // item's own owner/repo (FS-GG/FS.GG.SDD), so a bare `#n` adopts it.
    let private canon raw = Blockers.canonicalizeBlockedBy "FS-GG" "FS.GG.SDD" raw

    [<Fact>]
    let ``a full owner/repo#n ref passes through unchanged`` () =
        Assert.Equal<Result<string option, _>>(Ok(Some "FS-GG/FS.GG.SDD#8"), canon "FS-GG/FS.GG.SDD#8")

    [<Fact>]
    let ``a bare #n adopts the blocked item's OWN owner/repo`` () =
        Assert.Equal<Result<string option, _>>(Ok(Some "FS-GG/FS.GG.SDD#33"), canon "#33")

    [<Fact>]
    let ``a repo#n adopts the owner but keeps the named repo`` () =
        Assert.Equal<Result<string option, _>>(Ok(Some "FS-GG/FS.GG.Rendering#33"), canon "FS.GG.Rendering#33")

    [<Fact>]
    let ``an issue URL canonicalizes to owner/repo#n`` () =
        Assert.Equal<Result<string option, _>>(
            Ok(Some "FS-GG/FS.GG.Templates#8"),
            canon "https://github.com/FS-GG/FS.GG.Templates/issues/8"
        )

    [<Fact>]
    let ``a list canonicalizes EVERY form, in order`` () =
        Assert.Equal<Result<string option, _>>(
            Ok(Some "FS-GG/FS.GG.Rendering#33, FS-GG/FS.GG.Templates#8"),
            canon "FS.GG.Rendering#33 , https://github.com/FS-GG/FS.GG.Templates/issues/8"
        )

    [<Fact>]
    let ``refs that canonicalize alike are de-duped — the bare #n and its full form are one edge`` () =
        Assert.Equal<Result<string option, _>>(Ok(Some "FS-GG/FS.GG.SDD#8"), canon "#8, FS-GG/FS.GG.SDD#8")

    [<Fact>]
    let ``an empty value is Ok None — the caller clears the field`` () =
        Assert.Equal<Result<string option, _>>(Ok None, canon "")
        Assert.Equal<Result<string option, _>>(Ok None, canon "   ")

    [<Fact>]
    let ``a '-'/'none' placeholder is refused TOWARD clearing, not stored`` () =
        Assert.Equal<Result<string option, _>>(Error Blockers.Placeholder, canon "-")
        Assert.Equal<Result<string option, _>>(Error Blockers.Placeholder, canon "none")
        Assert.Equal<Result<string option, _>>(Error Blockers.Placeholder, canon "None")

    [<Fact>]
    let ``the WHOLE bash placeholder set is refused as a placeholder, not misrouted to prose`` () =
        // bash's `canon_blocked_by` set: a run of hyphens, an em/en dash, none / n/a / tbd / todo. All
        // point at CLEARING (Placeholder), never at Status (NotIssueRefs) — the divergence a narrower set
        // would introduce is a user typing `tbd` and being told to set a Status.
        for p in [ "--"; "---"; "—"; "–"; "n/a"; "na"; "N/A"; "TBD"; "todo"; "ToDo" ] do
            Assert.Equal<Result<string option, _>>(Error Blockers.Placeholder, canon p)

    [<Fact>]
    let ``a delivery log is prose, not a dependency`` () =
        Assert.Equal<Result<string option, _>>(Error Blockers.NotIssueRefs, canon "RESOLVED: #8 closed, shipped @d80a8ae")

    [<Fact>]
    let ``the inverted 'blocks X' edge is refused — it is the wrong direction`` () =
        Assert.Equal<Result<string option, _>>(Error Blockers.NotIssueRefs, canon "blocks FS.GG.Governance#14")

    [<Fact>]
    let ``prose TRAILING a valid ref is refused — the anchored match will not swallow it`` () =
        Assert.Equal<Result<string option, _>>(
            Error Blockers.NotIssueRefs,
            canon "FS-GG/FS.GG.SDD#8 (republish vehicle)"
        )

    [<Fact>]
    let ``a value that is not a ref at all is refused`` () =
        Assert.Equal<Result<string option, _>>(Error Blockers.NotIssueRefs, canon "not a ref")

    // ---- #889: the rule is ONE decision, and everything else ASKS ----------------------------------
    //
    // `Chore.fs` carried a `private resolved` that was byte-identical to `Blockers.isResolved`, two
    // modules later in compile order, with nothing holding them in step — and `BLOCKER-CLEARED` turned
    // on it. That is #1000's four `statusName`s and #1012's two inverse `BlockerState` renderers, a
    // third time. The copies agreed, which is why nobody saw it; agreement is not the property.

    /// THE PAIR IS ONE DECISION ASKED TWICE. `isResolved` must BE `isResolvedState` of the state — if
    /// somebody re-expands it into its own match, the two drift the moment one is edited, and the
    /// resolution rule is back to being decided in two places.
    [<Fact>]
    let ``#889 isResolved is isResolvedState of the blocker's state - over every case`` () =
        for c in FSharp.Reflection.FSharpType.GetUnionCases typeof<BlockerState> do
            let state = FSharp.Reflection.FSharpValue.MakeUnion(c, [||]) :?> BlockerState

            let viaRecord = Blockers.isResolved (blocker 1 state)
            let viaState = Blockers.isResolvedState state

            Assert.True(
                (viaRecord = viaState),
                $"{c.Name}: isResolved and isResolvedState disagree — the rule is decided twice")

    /// AND NOBODY ELSE DECIDES IT — asserted against the SOURCE, because reflection cannot see this.
    ///
    /// THE FIRST DRAFT OF THIS GUARD WAS VACUOUS, and it is worth saying how: it swept the assembly for
    /// `BlockerState -> bool` methods outside `Blockers`. The copy it was written to catch —
    /// `Chore.resolved` — took a `Blocker`, matched on `.State` INSIDE, and returned bool. MEASURED: the
    /// original defect re-introduced verbatim, **320 tests green**. A guard named for a copy it cannot
    /// see is #266's signature, and writing one inside the fix for a duplication defect is how this
    /// family keeps regenerating (#916's trap 1: the copies agree; agreement is not the property).
    ///
    /// Signatures cannot express "decides resolution" — the decision is a `match` in a BODY, under any
    /// parameter type. So this reads the source, on the same terms `DocumentedInvocationTests` does: the
    /// property is about what is WRITTEN, so the text is the honest subject.
    ///
    /// IT MATCHES ON THE ARM'S RESULT, NOT MERELY ON THE CASE NAME. Naming `BlockerMerged` is not the
    /// defect — `Schedulability.blockerText` and `Protocol.blockerMeaning` both name every case to render
    /// PROSE, which is the deliberate second vocabulary `Types.fsi` tells you not to collapse. Mapping a
    /// resolving case to a BOOL is the defect, because that is the resolution rule and `Blockers` owns
    /// it. A first cut flagged the case name alone and reported both prose renderers as offenders — a
    /// guard that cries wolf on correct code teaches exactly one lesson, and it is the wrong one.
    [<Fact>]
    let ``#889 no module outside Blockers decides a blocker's resolution`` () =
        let coreSrc =
            let rec up (d: DirectoryInfo) =
                if isNull (box d) then
                    failwith "BlockerTests: no repo root above the test binary (looked for `src/FS.GG.Coord.Core`)."
                elif Directory.Exists(Path.Combine(d.FullName, "src", "FS.GG.Coord.Core")) then
                    Path.Combine(d.FullName, "src", "FS.GG.Coord.Core")
                else
                    up d.Parent

            up (DirectoryInfo AppContext.BaseDirectory)

        // `Blockers.fs` IS the owner. Every other module in Core is subject to this.
        let files =
            Directory.GetFiles(coreSrc, "*.fs")
            |> Array.filter (fun f -> Path.GetFileName f <> "Blockers.fs")

        // A rename that emptied this list would make the sweep pass by iterating nothing — the very shape
        // this test exists to refuse.
        Assert.NotEmpty files

        // `| BlockerClosed` / `| BlockerMerged`, possibly grouped with other cases, arriving at `-> true`
        // or `-> false`. That is a resolution DECISION. The window is short so it cannot leap out of the
        // match arm it starts in.
        let decides =
            Regex(@"\|\s*Blocker(Closed|Merged)\b[\s\S]{0,120}?->\s*(true|false)\b")

        // COMMENTS ARE STRIPPED FIRST, and in this codebase that is not a nicety. Every module here
        // documents the defect it removed, in prose, quoting the code that was wrong — this file does it
        // twice. A guard that read comments would red on a module for CORRECTLY describing the bug it no
        // longer has, which is the same "cries wolf on correct code" failure as the first cut above.
        let stripComments (text: string) =
            text.Split('\n')
            |> Array.map (fun line ->
                match line.IndexOf "//" with
                | -1 -> line
                | i -> line.Substring(0, i))
            |> String.concat "\n"

        let offenders =
            files
            |> Array.filter (fun f -> decides.IsMatch(stripComments (File.ReadAllText f)))
            |> Array.map Path.GetFileName
            |> Array.toList

        Assert.True(
            List.isEmpty offenders,
            $"""these decide a blocker's resolution outside Blockers.fs: {String.concat ", " offenders} — that is the rule, decided a second time, and `BLOCKER-CLEARED` turns on it. Ask `Blockers.isResolved`/`isResolvedState` (#889).""")

    // ---- #1092: THE RING. The state four per-item repairs cannot see. ------------------------------
    //
    // #343 (a blocker naming an OPEN issue, handed out), #476 (a PR ref never clears), #602 (an EMPTY
    // blocker list), #620 (blockers ALL CLOSED) — four repairs, every one of them asking about ONE
    // item's blockers. A cycle passes all four, because every item on a ring is individually,
    // locally, perfectly well-formed: non-empty blocker list, every blocker OPEN, every ref real,
    // correctly never handed out. The defect exists only in the GRAPH.
    //
    // Live, and it cost two hours: .github#1059 → #1063 → #1073 → #1059 sat closed on the board while
    // `take` reported "Status is Blocked" over each one — the same five words it gives a block that
    // clears in ten minutes — and "this queue is BUSY, not empty" over all three. BUSY implies it
    // drains. That one could not.

    let private gh n =
        { Owner = "FS-GG"
          Repo = ".github"
          Number = n }

    /// An item as the graph sees it: its ref, and the refs it is blocked BY.
    let private node n (blockedBy: int list) =
        gh n,
        blockedBy
        |> List.map (fun b ->
            { Ref = Some(gh b)
              Raw = (gh b).Short
              State = BlockerOpen })

    let private ringOf (cycle: Ref list) = cycle |> List.map (fun r -> r.Number)

    [<Fact>]
    let ``#1092 the LIVE ring — 1059 -> 1063 -> 1073 -> 1059 is one deadlocked set of three`` () =
        let found = Blockers.cycles [ node 1059 [ 1063 ]; node 1063 [ 1073 ]; node 1073 [ 1059 ] ]

        Assert.Equal<int list list>([ [ 1059; 1063; 1073 ] ], found |> List.map ringOf)

    [<Fact>]
    let ``#1092 a two-item ring is a ring — the smallest deadlock two workers can build`` () =
        let found = Blockers.cycles [ node 1 [ 2 ]; node 2 [ 1 ] ]
        Assert.Equal<int list list>([ [ 1; 2 ] ], found |> List.map ringOf)

    [<Fact>]
    let ``#1092 an item blocked by ITSELF is a ring — a singleton is otherwise never one`` () =
        let found = Blockers.cycles [ node 1 [ 1 ] ]
        Assert.Equal<int list list>([ [ 1 ] ], found |> List.map ringOf)

    [<Fact>]
    let ``#1092 a DAG is not a ring — the false positive that would report every board as deadlocked`` () =
        // The shape the live board has AFTER the repair: 1063 -> 1073 -> 1059, and 1059 blocks nothing.
        let found = Blockers.cycles [ node 1063 [ 1073 ]; node 1073 [ 1059 ]; node 1059 [] ]
        Assert.Empty(found)

    [<Fact>]
    let ``#1092 a DIAMOND is not a ring — a shared blocker is convergence, not a cycle`` () =
        // 1 -> 2, 1 -> 3, 2 -> 4, 3 -> 4. Every node reachable from 1; none reachable BACK.
        let found = Blockers.cycles [ node 1 [ 2; 3 ]; node 2 [ 4 ]; node 3 [ 4 ]; node 4 [] ]
        Assert.Empty(found)

    [<Fact>]
    let ``#1092 two DISJOINT rings are both reported — stopping at the first hides the second`` () =
        let found =
            Blockers.cycles [ node 1 [ 2 ]; node 2 [ 1 ]; node 8 [ 9 ]; node 9 [ 8 ]; node 5 [] ]

        Assert.Equal<int list list>([ [ 1; 2 ]; [ 8; 9 ] ], found |> List.map ringOf)

    [<Fact>]
    let ``#1092 a ring whose edges are RESOLVED is NOT live — isResolved decides, and it is asked`` () =
        // The same three refs, but every blocker is CLOSED or MERGED. A resolved blocker no longer
        // holds, so it is not an edge, so there is no ring — the item is startable and the graph is
        // empty. Re-answering resolution here instead of asking `isResolved` is how a rule spelled
        // twice agrees once (#520).
        let closed n b =
            gh n,
            [ { Ref = Some(gh b)
                Raw = (gh b).Short
                State = BlockerClosed } ]

        let merged n b =
            gh n,
            [ { Ref = Some(gh b)
                Raw = (gh b).Short
                State = BlockerMerged } ]

        Assert.Empty(Blockers.cycles [ closed 1 2; merged 2 3; closed 3 1 ])

    [<Fact>]
    let ``#1092 an UNKNOWN blocker still draws its edge — it blocks, so it can deadlock`` () =
        // `BlockerUnknown`/`BlockerUnparseable` BLOCK (#266: "I could not look" is not "it is fine").
        // A blocker that holds the item is an edge, whatever we know about it — otherwise the ring
        // with the least-understood edge is the one we stay silent about.
        let unknown n b =
            gh n,
            [ { Ref = Some(gh b)
                Raw = (gh b).Short
                State = BlockerUnknown } ]

        Assert.Equal<int list list>([ [ 1; 2 ] ], Blockers.cycles [ unknown 1 2; unknown 2 1 ] |> List.map ringOf)

    [<Fact>]
    let ``#1092 a blocker OUTSIDE the graph draws no edge — under-report, never invent a deadlock`` () =
        // 1 -> 2 -> 99, and 99 is not a node we hold. We cannot see whether 99 closes a ring back to
        // 1, and asserting one we did not observe is the #266 defect pointed the other way. No edge,
        // no claimed cycle.
        let found = Blockers.cycles [ node 1 [ 2 ]; node 2 [ 99 ] ]
        Assert.Empty(found)

    [<Fact>]
    let ``#1092 prose blockers cannot close a ring — they have no ref to point at`` () =
        let found = Blockers.cycles [ (gh 1, [ prose "blocked on a human" ]); (gh 2, [ prose "waiting" ]) ]
        Assert.Empty(found)

    [<Fact>]
    let ``#1092 the empty graph and a lone unblocked item are acyclic — no noise on a healthy board`` () =
        Assert.Empty(Blockers.cycles [])
        Assert.Empty(Blockers.cycles [ node 1 [] ])

    [<Fact>]
    let ``#1092 a duplicated node collapses — one item cannot hold two blocker lists in one graph`` () =
        // First occurrence wins. Without this, a caller's accidental duplicate could contribute a
        // second, contradictory edge set for the same item.
        let found = Blockers.cycles [ node 1 [ 2 ]; node 1 [ 3 ]; node 2 [ 1 ]; node 3 [] ]
        Assert.Equal<int list list>([ [ 1; 2 ] ], found |> List.map ringOf)

    [<Fact>]
    let ``#1092 a long chain terminates and is acyclic — the input a naive walk never returns from`` () =
        // 200 items in a line. Guards the recursion as much as the verdict.
        let chain = [ for i in 1..199 -> node i [ i + 1 ] ] @ [ node 200 [] ]
        Assert.Empty(Blockers.cycles chain)

    [<Fact>]
    let ``#1092 one ring containing EVERY item terminates — the pathological whole-board deadlock`` () =
        let ring = [ for i in 1..199 -> node i [ i + 1 ] ] @ [ node 200 [ 1 ] ]
        let found = Blockers.cycles ring
        Assert.Equal(1, List.length found)
        Assert.Equal(200, List.length (List.head found))
