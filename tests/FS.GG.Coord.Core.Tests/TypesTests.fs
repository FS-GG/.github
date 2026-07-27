namespace FS.GG.Coord.Tests

open FSharp.Reflection
open Xunit
open FS.GG.Coord
open FS.GG.Coord.Types

/// THE WIRE VOCABULARY IS ONE FUNCTION, AND THESE ARE THE PROPERTIES THE COMPILER CANNOT GIVE US.
///
/// `Types.statusWireName` is a TOTAL match, so the compiler already owns coverage: a new `BoardStatus`
/// case fails the build rather than rendering as something. That is the half reflection does not need to
/// check, and `ProtocolTests` says so in as many words about `Schedulability.kind`.
///
/// What the compiler cannot see is what a case is named ONCE somebody is forced to name it. It will
/// accept `""`, it will accept a name another case already uses, and it will accept `" Ready "` — each
/// of which reaches the board and none of which is a type error. That is this module's subject.
module TypesTests =

    /// Every `BoardStatus` case, built by reflection rather than typed out here.
    ///
    /// A hand-written list is the fifth copy of the vocabulary wearing a different hat: it would be
    /// correct on the day it was typed and silently short by one the day a case is added — which is the
    /// precise failure #983 exists to end, reproduced inside its own regression test.
    let private everyCase: (string * BoardStatus) list =
        FSharpType.GetUnionCases typeof<BoardStatus>
        |> Array.map (fun c -> c.Name, FSharpValue.MakeUnion(c, [||]) :?> BoardStatus)
        |> Array.toList

    [<Fact>]
    let ``reflection can actually see the union - the guard below is not vacuous`` () =
        // If `everyCase` silently came back empty, every `for` loop under it would pass by iterating
        // nothing, and this module would report green over a vocabulary it never looked at. That is
        // #266's signature, and it is exactly what these tests are meant to catch elsewhere.
        Assert.Equal(7, List.length everyCase)

    [<Fact>]
    let ``exactly ONE case renders empty on the wire, and it is NoStatus`` () =
        // `NoStatus -> ""` is the wire being honest: an unset column really is the empty string. But it
        // is a licence to render nothing, and the next case added must not quietly take it. A case that
        // renders "" would SET A COLUMN TO NOTHING and read back as `NoStatus` — a write that silently
        // unsets the very field it was asked to set.
        let empty = everyCase |> List.filter (fun (_, s) -> statusWireName s = "")

        Assert.Equal<string list>([ "NoStatus" ], empty |> List.map fst)

    [<Fact>]
    let ``no two cases share a wire name`` () =
        // Two cases spelling the same option name makes the board AMBIGUOUS in the read direction: the
        // column comes back as one string and there is no longer a fact about which case wrote it.
        let names = everyCase |> List.map (snd >> statusWireName)

        Assert.Equal<string list>(List.distinct names, names)

    [<Fact>]
    let ``a wire name is exactly what the board stores - no padding, no casing drift`` () =
        // Projects v2 matches its option names literally. `" Ready "` or `"in progress"` type-checks,
        // renders fine in a terminal, and matches NO option on the board — the write fails, or worse,
        // silently no-ops.
        for name, s in everyCase do
            let w = statusWireName s

            if name <> "NoStatus" then
                Assert.False(System.String.IsNullOrWhiteSpace w, $"case {name} renders blank on the wire")
                Assert.Equal(w.Trim(), w)

    [<Fact>]
    let ``the wire spellings are pinned to what the BOARD actually calls its columns`` () =
        // The one place the vocabulary is written down twice ON PURPOSE — here, against the engine. This
        // is the assertion that fails if somebody "tidies" `In progress` into `InProgress`, which is a
        // change no compiler and no other test in this file can object to.
        //
        // These are the six option names on the FS-GG "Coordination" board, plus the empty string for an
        // unset column. Sentence case, and `In progress`/`In review` are NOT title-cased — the board
        // spells them that way, and the board is the authority (#437 was a worker grepping for a status
        // that was rendered differently from how they were told).
        Assert.Equal("", statusWireName NoStatus)
        Assert.Equal("Backlog", statusWireName Backlog)
        Assert.Equal("Ready", statusWireName Ready)
        Assert.Equal("In progress", statusWireName InProgress)
        Assert.Equal("Blocked", statusWireName Blocked)
        Assert.Equal("In review", statusWireName InReview)
        Assert.Equal("Done", statusWireName Done)

    // ---- the BLOCKER wire vocabulary (#1012) ---------------------------------------------------------
    // The same subject as above with one property added, and the added one is the whole issue: this
    // vocabulary has an INVERSE. `Scan` renders and `Snapshot` parses, so the two must compose to the
    // identity — and until #1012 nothing said so. Both were `private`, in different projects, and
    // `ScanRoundTripTests` exercised exactly one case, so `merged -> "MERGED"` left 775 tests green.
    //
    // The round-trip is now a property of ONE module, and it is checked over every case by reflection
    // rather than over the cases somebody remembered to type out.

    /// Every `BlockerState` case, built by reflection — a hand-written list here is the copy this issue
    /// removed, wearing a test's hat.
    let private everyBlocker: (string * BlockerState) list =
        FSharpType.GetUnionCases typeof<BlockerState>
        |> Array.map (fun c -> c.Name, FSharpValue.MakeUnion(c, [||]) :?> BlockerState)
        |> Array.toList

    [<Fact>]
    let ``reflection can actually see BlockerState - the guards below are not vacuous`` () =
        // #266's signature: an empty `everyBlocker` would make every loop below pass by iterating nothing,
        // and this file would report green over a vocabulary it never looked at.
        Assert.Equal(5, List.length everyBlocker)

    [<Fact>]
    let ``#1012 every blocker state round-trips through the wire - the property that was missing`` () =
        // THE ONE THAT WOULD HAVE CAUGHT IT. Render then parse, for every case, not just `BlockerOpen`.
        for name, b in everyBlocker do
            let wire = blockerStateWireName b

            Assert.True(
                blockerStateOfWireName wire = Some b,
                $"{name} renders as '{wire}' and does not parse back — Scan writes what Snapshot cannot read"
            )

    [<Fact>]
    let ``#1012 no two blocker states share a wire name`` () =
        // Two cases spelling one string makes the scan AMBIGUOUS in the read direction: there is no longer
        // a fact about which case wrote it, and `merged`-vs-`closed` is exactly the distinction #476 turns
        // on — clear on both, block on neither.
        let names = everyBlocker |> List.map (snd >> blockerStateWireName)
        Assert.Equal<string list>(List.distinct names, names)

    [<Fact>]
    let ``#1012 no blocker state renders blank or padded`` () =
        // Unlike `BoardStatus`, NO case here may render empty: a blocker has no "unset" state, and a blank
        // would parse back as "not a blocker state at all" (`None`) rather than as itself.
        for name, b in everyBlocker do
            let w = blockerStateWireName b
            Assert.False(System.String.IsNullOrWhiteSpace w, $"case {name} renders blank on the wire")
            Assert.Equal(w.Trim(), w)

    [<Fact>]
    let ``#1012 the blocker wire spellings are LOWER case, and pinned to what the scan emits`` () =
        // Written down twice ON PURPOSE, as the `BoardStatus` pin above is. `check-board` §3 selects on
        // these strings in `jq` and does not parse, so a "tidy" to `Merged` reaches no compiler and no
        // other test — it just silently stops matching, and every merged blocker reads as still-holding.
        // The lower case is not incidental: an ISSUE's state is upper case on the same wire, and the two
        // conventions are deliberately opposite.
        Assert.Equal("open", blockerStateWireName BlockerOpen)
        Assert.Equal("closed", blockerStateWireName BlockerClosed)
        Assert.Equal("merged", blockerStateWireName BlockerMerged)
        Assert.Equal("unknown", blockerStateWireName BlockerUnknown)
        Assert.Equal("unparseable", blockerStateWireName BlockerUnparseable)

    [<Fact>]
    let ``#1012 a string that is not a blocker state is None, not BlockerUnknown`` () =
        // A parse failure must not wear a verdict's clothes. `BlockerUnknown` means "the ref parsed and we
        // could not learn its state" — a fact about the blocker. Garbage on the wire is a fact about the
        // DOCUMENT, and collapsing them lets a corrupt snapshot read as a legitimately-unreadable blocker
        // (#266).
        Assert.True(Option.isNone (blockerStateOfWireName ""))
        Assert.True(Option.isNone (blockerStateOfWireName "nonsense"))
        Assert.True(Option.isNone (blockerStateOfWireName "Blocked")) // a BoardStatus, not a blocker state

    [<Fact>]
    let ``#1012 the SPELLING pin is what a check-board reader depends on - the round-trip alone was never enough`` () =
        // WHY BOTH PINS EXIST, stated where somebody deleting one will read it.
        //
        // #1012's motivating mutation was `merged -> "MERGED"`. Under the OLD two-copy design the
        // round-trip could not have seen it at all: `Snapshot` lower-cased its input, so a renderer
        // emitting `"MERGED"` still parsed back to `BlockerMerged`. The engine absorbed its own
        // divergence, and the only thing that broke was OUTSIDE it — `check-board` §3 selects
        // `.state == "merged"` in `jq` and does not parse.
        //
        // Deriving the parser FROM the renderer closed the CASE half: `"MERGED"` now fails the
        // round-trip, because the derived lookup compares lower-cased input against what the renderer
        // actually emits. A real dividend of the one-owner shape.
        //
        // It does NOT close the RENAME half, and that is why this pin exists. MEASURED: changing
        // `"merged"` to `"landed"` leaves the round-trip GREEN — render `landed`, parse `landed`, same
        // case back — and reds only the two exact-spelling assertions. The engine agrees with itself
        // perfectly while every `jq` selector in `check-board` silently stops matching, which is #476's
        // bite arriving through a refactor nobody would call risky.
        //
        // The round-trip proves the engine agrees with ITSELF; only these pins prove it agrees with its
        // READERS. Delete them and a rename is unobserved.
        Assert.Equal("merged", blockerStateWireName BlockerMerged)
        Assert.Equal(Some BlockerMerged, blockerStateOfWireName "merged")

    [<Fact>]
    let ``#1012 the parser is forgiving about SURROUNDING space and case, as it always was`` () =
        // Behaviour `Snapshot.blockerState` had before this moved: `.Trim().ToLowerInvariant()`. Preserved
        // deliberately — this is a parser reading somebody else's JSON, and it is liberal on input while
        // the renderer stays exact on output.
        Assert.Equal(Some BlockerMerged, blockerStateOfWireName "  MERGED  ")
        Assert.Equal(Some BlockerOpen, blockerStateOfWireName "Open")

    // ---- ItemClass: the same pin, on a vocabulary that is THREE wires at birth (.github#1588) ---------
    //
    // `BlockerState` earned these guards the hard way — a render/parse pair in two modules, nothing
    // asserting they were inverse, 775 tests green while every merged blocker read as still-holding
    // (#1012). `ItemClass` gets them on day one, and it needs them MORE: the same three strings are the
    // Projects v2 option names, the values a filer writes in a `Class:` body line, and the rows of the
    // options table in `docs/coordination/board-schema.md`. Only the first two are reachable from here;
    // the third is gated by `scripts/project-field-options check --field Class`, which compares that table
    // against this vocabulary. The exact-spelling pins below are what make that cross-tool check meaningful
    // — without them a rename here would leave the engine agreeing with itself and disagreeing with the
    // documented board.

    /// Every `ItemClass` case, by reflection. A hand-written list is the copy this pattern exists to refuse.
    let private everyClass: (string * ItemClass) list =
        FSharpType.GetUnionCases typeof<ItemClass>
        |> Array.map (fun c -> c.Name, FSharpValue.MakeUnion(c, [||]) :?> ItemClass)
        |> Array.toList

    [<Fact>]
    let ``reflection can actually see ItemClass - the guards below are not vacuous`` () =
        Assert.Equal(3, List.length everyClass)

    [<Fact>]
    let ``#1588 every item class round-trips through the wire`` () =
        for name, c in everyClass do
            let wire = itemClassWireName c

            Assert.True(
                itemClassOfWireName wire = Some c,
                $"{name} renders as '{wire}' and does not parse back — reconcile would write what lint cannot read"
            )

    [<Fact>]
    let ``#1588 no two item classes share a wire name`` () =
        // Two cases spelling one string would make the body line AMBIGUOUS: there would be no fact about
        // which class an author declared, and `defect` vs `hardening` is the entire distinction a driver's
        // stopping rule turns on.
        let names = everyClass |> List.map (snd >> itemClassWireName)
        Assert.Equal<string list>(List.distinct names, names)

    [<Fact>]
    let ``#1588 no item class renders blank or padded`` () =
        // No case may render empty. Unlike `BoardStatus`, there is no "unset" class — that is `None`, the
        // ABSENCE of a class — and a blank would parse back as "not a class at all", so an item would be
        // reported untriaged while carrying a case that renders to nothing.
        for name, c in everyClass do
            let w = itemClassWireName c
            Assert.False(System.String.IsNullOrWhiteSpace w, $"case {name} renders blank on the wire")
            Assert.Equal(w.Trim(), w)

    [<Fact>]
    let ``#1588 the class wire spellings are pinned to the documented board options`` () =
        // Written down twice ON PURPOSE, exactly as the blocker-state pins are and for the identical
        // reason: the round-trip proves the engine agrees with ITSELF, and a RENAME leaves it green.
        // Rename `defect` to `bug` here and the round-trip still passes — while the board's `Class` field,
        // the `Class:` lines in ~50 issue bodies, and the options table `project-field-options check
        // --field Class` gates would all silently stop matching. These three assertions are the only place
        // that rename is observed.
        Assert.Equal("defect", itemClassWireName Defect)
        Assert.Equal("hardening", itemClassWireName Hardening)
        Assert.Equal("decision", itemClassWireName Decision)

    [<Fact>]
    let ``#1588 the class parser is forgiving about surrounding space and case`` () =
        // A parser reading a line a HUMAN typed into an issue body, so it is liberal on input while the
        // renderer stays exact on output — `blockerStateOfWireName`'s rule, one vocabulary over.
        Assert.Equal(Some Defect, itemClassOfWireName "  DEFECT  ")
        Assert.Equal(Some Decision, itemClassOfWireName "Decision")

    [<Fact>]
    let ``#1588 an unrecognised word is None, never the nearest class`` () =
        // AC3, at the parser. Resolving `Class: bug` onto `defect` would be a GUESS carrying a parser's
        // authority — and it would be invisible, because the item would then look triaged. `None` sends it
        // back to `lint`'s CLASS-UNSET and to a human.
        Assert.Equal(None, itemClassOfWireName "bug")
        Assert.Equal(None, itemClassOfWireName "P1")
        Assert.Equal(None, itemClassOfWireName "")
        Assert.Equal(None, itemClassOfWireName null)
