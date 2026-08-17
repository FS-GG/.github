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

    // ---- Severity (.github#1901) -------------------------------------------------------------------

    let private everySeverity: (string * Severity) list =
        FSharpType.GetUnionCases typeof<Severity>
        |> Array.map (fun c -> c.Name, FSharpValue.MakeUnion(c, [||]) :?> Severity)
        |> Array.toList

    [<Fact>]
    let ``#1901 Severity is the exact closed ordered board vocabulary`` () =
        Assert.Equal(5, List.length everySeverity)
        Assert.Equal<string list>(
            [ "Critical"; "High"; "Medium"; "Low"; "Unset" ],
            everySeverity |> List.map (snd >> severityWireName)
        )

        Assert.Equal<int list>([ 0; 1; 2; 3; 4 ], everySeverity |> List.map (snd >> severityOrder))

    [<Fact>]
    let ``#1901 every Severity round-trips and unknown words remain unknown`` () =
        for name, severity in everySeverity do
            let wire = severityWireName severity
            Assert.Equal(Some severity, severityOfWireName wire)
            Assert.False(System.String.IsNullOrWhiteSpace wire, $"case {name} renders blank")

        Assert.Equal(Some Critical, severityOfWireName " critical ")
        Assert.Equal(None, severityOfWireName "urgent")
        Assert.Equal(None, severityOfWireName null)

    // ================================================================================================
    // THE PHASE VOCABULARY (.github#1598) — `everyItemClass`'s pattern, one column over.
    // ================================================================================================
    // `Phase` is TWO wires, not three: the live Projects v2 option name, and the `repo-phase-map` table in
    // `docs/coordination/board-schema.md`. There is no body-line grammar — `Phase` is a column a human
    // sets, and no item text declares it.
    //
    // NOTHING GATES THE SECOND WIRE. `scripts/project-field-options check` has a `Repo Scope` leg and a
    // `Class` leg and no `Phase` leg, so the nine strings below are transcribed by hand from a documented
    // table with no tool comparing them. That is exactly the situation the exact-spelling pins are for.

    /// Every `Phase` case, by reflection. A hand-written list is the copy this pattern exists to refuse.
    let private everyPhase: (string * Phase) list =
        FSharpType.GetUnionCases typeof<Phase>
        |> Array.map (fun c -> c.Name, FSharpValue.MakeUnion(c, [||]) :?> Phase)
        |> Array.toList

    [<Fact>]
    let ``reflection can actually see Phase - the guards below are not vacuous`` () =
        Assert.Equal(9, List.length everyPhase)

    [<Fact>]
    let ``#1598 every phase round-trips through the wire`` () =
        for name, p in everyPhase do
            let wire = phaseWireName p

            Assert.True(
                phaseOfWireName wire = Some p,
                $"{name} renders as '{wire}' and does not parse back — the scan would read the board's own column as no phase at all"
            )

    [<Fact>]
    let ``#1598 no two phases share a wire name, and none renders blank or padded`` () =
        let names = everyPhase |> List.map (snd >> phaseWireName)
        Assert.Equal<string list>(List.distinct names, names)

        for name, p in everyPhase do
            let w = phaseWireName p
            Assert.False(System.String.IsNullOrWhiteSpace w, $"case {name} renders blank on the wire")
            Assert.Equal(w.Trim(), w)

    [<Fact>]
    let ``#1598 phaseOrder is a TOTAL ORDER over the union — no two phases tie`` () =
        // A tie would make the third rank term silently stop discriminating between two phases, which is
        // invisible: the batch would still be produced, just ordered by the term below it.
        let orders = everyPhase |> List.map (snd >> phaseOrder)
        Assert.Equal<int list>(List.distinct orders, orders)
        Assert.Equal<int list>(List.sort orders, orders)

    [<Fact>]
    let ``#1598 the phase wire spellings are pinned to the documented board options`` () =
        // Written down twice ON PURPOSE, exactly as the `Class` and blocker-state pins are. The round-trip
        // above proves the engine agrees with ITSELF and stays green through a rename; these nine
        // assertions are the ONLY place a rename is observed, and here that matters more than it does for
        // `Class` — there is no `project-field-options` leg for `Phase` to catch it downstream.
        Assert.Equal("P0 Decisions", phaseWireName P0Decisions)
        Assert.Equal("P1 Rendering", phaseWireName P1Rendering)
        Assert.Equal("P2 SDD", phaseWireName P2Sdd)
        Assert.Equal("P3 Governance", phaseWireName P3Governance)
        Assert.Equal("P4 Templates", phaseWireName P4Templates)
        Assert.Equal("P5 Versioning", phaseWireName P5Versioning)
        Assert.Equal("P6 Game", phaseWireName P6Game)
        Assert.Equal("P7 Audio", phaseWireName P7Audio)
        Assert.Equal("P8 Net", phaseWireName P8Net)

    [<Fact>]
    let ``#1598 the phase parser is forgiving about surrounding space and case`` () =
        Assert.Equal(Some P0Decisions, phaseOfWireName "  p0 decisions  ")
        Assert.Equal(Some P2Sdd, phaseOfWireName "P2 sdd")

    [<Fact>]
    let ``#1598 an unrecognised column value is None, and emphatically not P0`` () =
        // P0 outranks every other phase, so resolving an unknown word onto it would make a typo — or a
        // board option somebody adds without touching this engine — the highest-priority work on the
        // board. `None` ranks the row LAST, which is the direction that cannot hurt.
        Assert.Equal(None, phaseOfWireName "P9 Something")
        Assert.Equal(None, phaseOfWireName "P0")
        Assert.Equal(None, phaseOfWireName "Decisions")
        Assert.Equal(None, phaseOfWireName "")
        Assert.Equal(None, phaseOfWireName null)

    // ---- .github#2712 — the `ItemKind` wire vocabulary --------------------------------------------
    //
    // Pinned on `ItemClass`'s exact terms above, and for its exact reasons: this one string is three
    // wires at birth — the Projects v2 `Kind` option name, the value a filer writes in a `Kind:` body
    // line, and the rows of the `kind-options` table in `docs/coordination/board-schema.md`. Only the
    // first two are reachable from here; the third is gated by `scripts/project-field-options check
    // --field Kind`, and the exact-spelling pins below are what make that cross-tool check mean anything.

    /// Every `ItemKind` case, by reflection. A hand-written list is the copy this pattern refuses.
    let private everyKind: (string * ItemKind) list =
        FSharpType.GetUnionCases typeof<ItemKind>
        |> Array.map (fun c -> c.Name, FSharpValue.MakeUnion(c, [||]) :?> ItemKind)
        |> Array.toList

    [<Fact>]
    let ``2712 reflection can actually see ItemKind - the guards below are not vacuous`` () =
        Assert.Equal(4, List.length everyKind)

    [<Fact>]
    let ``2712 every item kind round-trips through the wire`` () =
        for name, k in everyKind do
            let wire = itemKindWireName k

            Assert.True(
                itemKindOfWireName wire = Some k,
                $"{name} renders as '{wire}' and does not parse back — reconcile would write what the body parser cannot read"
            )

    [<Fact>]
    let ``2712 no two item kinds share a wire name`` () =
        // Two cases spelling one string would make the body line AMBIGUOUS about the one question that
        // decides whether the lifecycle reducer runs at all.
        let names = everyKind |> List.map (snd >> itemKindWireName)
        Assert.Equal<string list>(List.distinct names, names)

    [<Fact>]
    let ``2712 no item kind renders blank or padded`` () =
        // A blank would parse back as "not a kind at all", which `Kind.govern` reads as `Work` — so a
        // register whose case rendered empty would be silently returned to the lifecycle reducer.
        for name, k in everyKind do
            let w = itemKindWireName k
            Assert.False(System.String.IsNullOrWhiteSpace w, $"case {name} renders blank on the wire")
            Assert.Equal(w.Trim(), w)

    [<Fact>]
    let ``2712 the kind wire spellings are pinned to the documented board options`` () =
        // Written down twice ON PURPOSE, exactly as the class pins above are: the round-trip proves the
        // engine agrees with ITSELF and a RENAME leaves it green, while the board's `Kind` field, the
        // `Kind:` lines in live issue bodies and the `kind-options` table would all stop matching.
        Assert.Equal("work", itemKindWireName Work)
        Assert.Equal("anchor", itemKindWireName Anchor)
        Assert.Equal("register", itemKindWireName Register)
        Assert.Equal("directive", itemKindWireName Directive)

    [<Fact>]
    let ``2712 the kind parser is forgiving about surrounding space and case, and refuses everything else`` () =
        Assert.Equal(Some Register, itemKindOfWireName "  REGISTER  ")
        Assert.Equal(Some Anchor, itemKindOfWireName "Anchor")
        // TWO-SIDED. Lookalikes must NOT resolve — and resolving one would be worse here than on the
        // `Class` axis, because the wrong answer removes a real work row from its own lifecycle.
        for lookalike in [ "registers"; "reg"; "anchors"; "directives"; "standing"; "epic"; ""; "  "; "work " + "​" ] do
            Assert.Equal(None, itemKindOfWireName lookalike)

    [<Fact>]
    let ``2712 Kind.legalKinds is the union itself, so a fifth case reaches every diagnostic`` () =
        Assert.Equal<Set<ItemKind>>(everyKind |> List.map snd |> Set.ofList, Kind.legalKinds |> Set.ofList)
