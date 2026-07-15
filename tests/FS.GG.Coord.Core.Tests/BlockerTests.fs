namespace FS.GG.Coord.Tests

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
    // the client had just classified BLOCKED reached the engine UNBLOCKED, and under `--engine=fs` a
    // worker would have been handed it.
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
