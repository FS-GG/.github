module FS.GG.Coord.Core.Tests.ClassTests

open Xunit
open FS.GG.Coord
open FS.GG.Coord.Types

// The `Class:` body-line sentinel (.github#1588, ADR-0066). These are `HumanBlockTests`' cases one
// vocabulary over, because the grammar is deliberately the SAME grammar — #1103 decided one shape for
// body-line sentinels and `Class.fs` inherits it rather than inventing a third.
//
// The fence cases are not ceremony. `Markdown`'s own docstring records that fence-awareness was learned
// three times and shared zero, and that "the next body-parsing module added to this engine would have
// been fence-blind BY CONSTRUCTION". This is that next module; these are the tests that say it was not.

[<Fact>]
let ``a plain Class line is read`` () =
    Assert.Equal(Some Defect, Class.fromBody "Class: defect")
    Assert.Equal(Some Hardening, Class.fromBody "Class: hardening")
    Assert.Equal(Some Decision, Class.fromBody "Class: decision")

[<Fact>]
let ``a body declaring nothing yields None - which is a real answer, not a default`` () =
    Assert.Equal(None, Class.fromBody "Just some prose about the work.\n\nPaths: src/**")
    Assert.Equal(None, Class.fromBody "")
    Assert.Equal(None, Class.fromBody null)

[<Fact>]
let ``the line is found anywhere in the body, with up to three leading spaces`` () =
    // `Paths:`/`Blocked on:`'s grammar exactly: `^ {0,3}`. Spaces, not tabs — a token indented with a tab
    // is code to the surrounding markdown anyway.
    Assert.Equal(Some Defect, Class.fromBody "## Observed\n\nsomething is red.\n\n   Class: defect\n\nPaths: src/**")
    Assert.Equal(None, Class.fromBody "    Class: defect")
    Assert.Equal(None, Class.fromBody "\tClass: defect")

[<Fact>]
let ``the label takes either case on its FIRST letter only, exactly as its siblings do`` () =
    // `HumanBlock`'s label is `[Bb]locked [Oo]n:` and `TouchSet`'s is `[Pp]aths:` — first letter either
    // case, the rest lower. This inherits that rather than widening to `IgnoreCase`, and the restraint is
    // deliberate: #1103 decided ONE grammar for body-line sentinels, and a third module quietly accepting
    // a spelling the other two reject is how "one grammar" becomes three again (#972). If all-caps labels
    // are wanted, all three change together, in one place, with the docs.
    Assert.Equal(Some Defect, Class.fromBody "class:   DEFECT   ")
    Assert.Equal(Some Hardening, Class.fromBody "Class: Hardening")
    Assert.Equal(None, Class.fromBody "CLASS: hardening")

[<Fact>]
let ``the VALUE is case-insensitive and trimmed, because a human types it`` () =
    // The label is the grammar and the value is the vocabulary, and they are liberal on different terms:
    // `itemClassOfWireName` trims and lower-cases, the way `blockerStateOfWireName` does.
    Assert.Equal(Some Defect, Class.fromBody "Class:   DEFECT   ")
    Assert.Equal(Some Decision, Class.fromBody "Class: Decision")

[<Fact>]
let ``#277 a FENCED Class line is a QUOTATION and declares nothing`` () =
    // The rule every body-line parser in this engine shares, and the one a new module gets wrong for free
    // if it does not ask `Markdown.unfenced`. The ADR and the schema doc both quote this grammar in fenced
    // blocks; so will every follow-up issue explaining it.
    let body = "How to declare it:\n\n```\nClass: defect\n```\n\nNothing is declared here."
    Assert.Equal(None, Class.fromBody body)

[<Fact>]
let ``#277 a fenced line does not suppress a real one elsewhere`` () =
    let body = "```\nClass: hardening\n```\n\nClass: defect"
    Assert.Equal(Some Defect, Class.fromBody body)

[<Fact>]
let ``an unrecognised value is NOT a declaration`` () =
    // AC3 at the body line. `Class: bug` is a filer using a vocabulary this engine does not speak; mapping
    // it onto the nearest of three would be the guess the whole item forbids, and it would look triaged.
    Assert.Equal(None, Class.fromBody "Class: bug")
    Assert.Equal(None, Class.fromBody "Class: P1")
    Assert.Equal(None, Class.fromBody "Class:")

[<Fact>]
let ``DEFECT DOMINATES when a body declares more than one`` () =
    // The inverse of `HumanBlock`'s rule, and stated for the mirrored reason. There the strongest claim is
    // the most RESTRICTIVE and must not be weakened; here it is "something is broken NOW", and a body
    // saying both must not be quietly downgraded to the reading that lets a burn-down stop.
    Assert.Equal(Some Defect, Class.fromBody "Class: hardening\n\nClass: defect")
    Assert.Equal(Some Defect, Class.fromBody "Class: defect\n\nClass: hardening")
    Assert.Equal(Some Defect, Class.fromBody "Class: decision\n\nClass: defect")

[<Fact>]
let ``DECISION beats HARDENING when both are declared and there is no defect`` () =
    Assert.Equal(Some Decision, Class.fromBody "Class: hardening\n\nClass: decision")
    Assert.Equal(Some Decision, Class.fromBody "Class: decision\n\nClass: hardening")

[<Fact>]
let ``ADR-0045's human-decision sentinel derives `decision` with no Class line`` () =
    // AC5's zero-cost derivation: the fact is already on the item, so it must not have to be written twice.
    Assert.Equal(Some Decision, Class.fromBody "Blocked on: human/decision")
    Assert.Equal(Some Decision, Class.fromBody "Some prose.\n\n  Blocked on: HUMAN/DECISION\n\nPaths: none")

[<Fact>]
let ``human/ACTION derives NOTHING - it says nothing about how bad the item is`` () =
    // The distinction that matters: `human/action` is a park on somebody DOING something (a scope grant, a
    // credential), and a DEFECT can be parked on one. Reading it as `decision` would class every
    // waiting-on-an-action defect as a row no driver may ever schedule, which is the opposite of the
    // failure #1588 exists to fix.
    Assert.Equal(None, Class.fromBody "Blocked on: human/action")

[<Fact>]
let ``an explicit Class line beats the sentinel's derivation`` () =
    // A defect parked on a human decision is still a defect. The explicit statement is somebody answering
    // the question; the sentinel is a convention being read.
    Assert.Equal(Some Defect, Class.fromBody "Class: defect\n\nBlocked on: human/decision")

[<Fact>]
let ``#277 a fenced sentinel derives nothing either`` () =
    // The derivation goes through `HumanBlock.parse`, which is fence-aware — asserted here rather than
    // assumed, because "we asked the module that already knows" is exactly the claim worth checking.
    Assert.Equal(None, Class.fromBody "```\nBlocked on: human/decision\n```")

[<Fact>]
let ``the [decision] title prefix derives decision`` () =
    // The convention the board already uses (#1547, #1589, #1611) and which AC3 names as evidence.
    Assert.Equal(Some Decision, Class.fromTitle "[decision] pick one of three digest implementations")
    Assert.Equal(Some Decision, Class.fromTitle "  [DECISION] something")

[<Fact>]
let ``it is a PREFIX, not a substring - a title MENTIONING a decision is not a decision item`` () =
    // `lint`'s epic rule scans `[epic]` anywhere in a title and that is a known wart. This vocabulary
    // decides whether a driver may STOP, so it does not inherit it.
    Assert.Equal(None, Class.fromTitle "record the [decision] we already made")
    Assert.Equal(None, Class.fromTitle "decision: do the thing")
    Assert.Equal(None, Class.fromTitle "")
    Assert.Equal(None, Class.fromTitle null)

[<Fact>]
let ``there is no title convention for defect or hardening, so nothing is invented`` () =
    Assert.Equal(None, Class.fromTitle "[defect] the gate is red")
    Assert.Equal(None, Class.fromTitle "[bug] the gate is red")

[<Fact>]
let ``derive prefers the BODY over the title`` () =
    // Adopting a `Class:` line can never be a downgrade for an item that already carried the prefix, and
    // an explicit statement outranks a convention.
    Assert.Equal(Some Defect, Class.derive "[decision] something" "Class: defect")
    Assert.Equal(Some Decision, Class.derive "[decision] something" "no declaration here")
    Assert.Equal(Some Hardening, Class.derive "ordinary title" "Class: hardening")
    Assert.Equal(None, Class.derive "ordinary title" "no declaration here")
