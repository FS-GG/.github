module FS.GG.Coord.Core.Tests.KindTests

open Xunit
open FS.GG.Coord
open FS.GG.Coord.Types

// The `Kind:` body-line sentinel (.github#2712). These are `ClassTests`' cases one vocabulary over,
// because the grammar is deliberately the SAME grammar — #1103 decided one shape for body-line sentinels
// and `Kind.fs` inherits it rather than inventing a fourth.
//
// THE CORPUS IS TWO-SIDED ON PURPOSE, and the stakes are higher than on the `Class` axis. There, a
// mis-parse mis-reports how bad a row is. Here, a mis-parse decides whether the LIFECYCLE REDUCER RUNS
// AT ALL: a false positive exempts a real work row from its own lifecycle and makes it permanently
// unschedulable, and a false negative hands the reducer a register it may mark `Done`. So every
// spelling that must parse is paired with a lookalike that must not.

// ---- the positive side: spellings that MUST resolve -----------------------------------------------

[<Fact>]
let ``a plain Kind line is read, for every case in the vocabulary`` () =
    // DERIVED from the union, not listed: a fifth `ItemKind` is asserted here the day it is declared.
    for k in Kind.legalKinds do
        Assert.Equal(Some k, Kind.fromBody $"Kind: %s{itemKindWireName k}")

[<Fact>]
let ``the grammar is the shared body-line grammar — leading space, case, and trailing space`` () =
    Assert.Equal(Some Register, Kind.fromBody "   Kind: register")
    Assert.Equal(Some Register, Kind.fromBody "kind: register")
    Assert.Equal(Some Register, Kind.fromBody "Kind:    register   ")
    // The VALUE is case-insensitive; the KEY accepts exactly two casings.
    Assert.Equal(Some Anchor, Kind.fromBody "Kind: ANCHOR")

[<Fact>]
let ``the KEY accepts exactly the two casings Class accepts, and no more`` () =
    // PARITY WITH `Class:`, ASSERTED RATHER THAN ASSUMED. "The same grammar as `Class:`" is a claim about
    // both directions: `Kind` must accept what `Class` accepts AND refuse what `Class` refuses. An
    // all-caps key is refused by `Class.fs`'s `[Cc]lass:` — measured, not inferred:
    // `Class.fromBody "CLASS: DEFECT"` is `None` — so a `Kind` that accepted `KIND:` would be a fourth
    // body-line spelling, which is precisely the drift ADR-0045 and #1103 exist to prevent.
    Assert.Equal(None, Class.fromBody "CLASS: defect")
    Assert.Equal(None, Kind.fromBody "KIND: register")

    Assert.Equal(Some Defect, Class.fromBody "class: defect")
    Assert.Equal(Some Register, Kind.fromBody "kind: register")

[<Fact>]
let ``the line is found among other body text`` () =
    let body =
        "## Observed\n\nsome prose about a register.\n\nPaths: none\nKind: register\nClass: hardening\n"

    Assert.Equal(Some Register, Kind.fromBody body)

// ---- the negative side: lookalikes that MUST NOT resolve ------------------------------------------

[<Fact>]
let ``a FENCED Kind line is documentation, not a declaration`` () =
    // NOT CEREMONY. `Markdown`'s own docstring records that fence-awareness was learned three times and
    // shared zero, and that "the next body-parsing module added to this engine would have been
    // fence-blind BY CONSTRUCTION". This is that next module, and these are the tests that say it was
    // not — which matters concretely here, because this change's own .fsi, ADR text and
    // `docs/coordination/board-schema.md` all QUOTE the grammar inside fences.
    let body = "How to declare a kind:\n\n```\nKind: register\n```\n\nThat is the grammar.\n"
    Assert.Equal(None, Kind.fromBody body)
    Assert.Equal<string list>([], Kind.unrecognised body)

[<Fact>]
let ``four leading spaces is an indented code block, not a declaration`` () =
    Assert.Equal(None, Kind.fromBody "    Kind: register")

[<Fact>]
let ``a word this engine does not speak is UNRECOGNISED, and is not resolved to the nearest kind`` () =
    // The negatives are the shapes a human actually writes: a plural, an abbreviation, a synonym, and a
    // word from the neighbouring vocabulary. NONE may resolve — and each must be REPORTED, because
    // `fromBody`'s `None` cannot tell "no line" from "a line I could not read" (.github#1651, measured
    // twice in one run on the `Class:` axis with two different invented words).
    for bad in [ "registers"; "reg"; "standing"; "epic"; "hardening"; "anchors"; "Register Row" ] do
        let body = $"Kind: %s{bad}"
        Assert.Equal(None, Kind.fromBody body)
        Assert.Equal<string list>([ bad ], Kind.unrecognised body)

[<Fact>]
let ``an EMPTY Kind line is unrecognised, not absent`` () =
    // The key was declared and the value could not be read. #266's rule: a subject you could not
    // evaluate is never a subject that passed.
    Assert.Equal(None, Kind.fromBody "Kind:")
    Assert.Equal<string list>([ "" ], Kind.unrecognised "Kind:")

[<Fact>]
let ``a key that merely STARTS with kind is not a Kind line`` () =
    for near in [ "Kinds: register"; "Kinder: register"; "KindOf: register"; "MyKind: register" ] do
        Assert.Equal(None, Kind.fromBody near)
        Assert.Equal<string list>([], Kind.unrecognised near)

[<Fact>]
let ``a body with NO Kind line declares nothing and reports nothing`` () =
    let body = "## Observed\n\nordinary work.\n\nPaths: src/\nClass: defect\n"
    Assert.Equal(None, Kind.fromBody body)
    Assert.Equal<string list>([], Kind.unrecognised body)

// ---- dominance, the default, and the standing predicate --------------------------------------------

[<Fact>]
let ``work DOMINATES a body that declares more than one kind`` () =
    // The inverse of `Class`'s "defect dominates", and for the same underlying rule: the ambiguous body
    // must resolve toward the reading that keeps the row UNDER the machinery. Exemption is the powerful
    // outcome, so it is never what an unclear declaration buys. Asserted in BOTH orders, because
    // "whichever line the author typed first" is exactly the rule this is not.
    Assert.Equal(Some Work, Kind.fromBody "Kind: register\nKind: work")
    Assert.Equal(Some Work, Kind.fromBody "Kind: work\nKind: register")

[<Fact>]
let ``two STANDING declarations resolve deterministically and never to work`` () =
    // No safety ordering exists among the standing kinds, so the union's own declaration order decides —
    // but the answer must be STABLE under line order, and it must not fall through to `Work`, which
    // would silently re-arm the reducer on a row that twice said it was standing.
    let a = Kind.fromBody "Kind: register\nKind: anchor"
    let b = Kind.fromBody "Kind: anchor\nKind: register"
    Assert.Equal(a, b)
    Assert.True((a |> Option.map Kind.isStanding) = Some true, $"two standing declarations resolved to %A{a}")

[<Fact>]
let ``govern reads no declaration as work — the property every row on the board relies on`` () =
    Assert.Equal(Work, Kind.govern None)
    for k in Kind.legalKinds do
        Assert.Equal(k, Kind.govern (Some k))

[<Fact>]
let ``isStanding is derived as not-work, so a fifth case is standing by default`` () =
    Assert.False(Kind.isStanding Work)
    for k in Kind.legalKinds |> List.filter (fun k -> k <> Work) do
        Assert.True(Kind.isStanding k, $"%A{k} must be standing")

    // NON-VACUITY for the loop above: there must BE standing kinds to test.
    Assert.Equal(3, Kind.legalKinds |> List.filter Kind.isStanding |> List.length)
