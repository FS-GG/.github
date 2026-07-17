module FS.GG.Coord.Core.Tests.EpicBodyTests

open Xunit
open FS.GG.Coord

// The refs an epic body DECLARES as a task list — the set EPIC-UNLINKED-CHILD and the (deferred) rollup
// diff against the sub-issue graph. `#n` resolves against the epic's own repo (FS-GG/FS.GG.SDD here).
let private refs body =
    EpicBody.childRefs "FS-GG" "FS.GG.SDD" body

[<Fact>]
let ``a task-list line naming an issue declares it; prose does not`` () =
    let body =
        "Umbrella epic.\n\n- [ ] #10 the first\n- [x] #11 the second\n\nAnd see #12 in prose — a mention."

    // #12 is prose, not a checklist line, so it is not a declaration.
    Assert.Equal<string list>([ "FS-GG/FS.GG.SDD#10"; "FS-GG/FS.GG.SDD#11" ], refs body)

[<Fact>]
let ``the FIRST ref on a line is the child - a later ref is not`` () =
    // `- [x] (b) #268 — matched by substring (cf. #100)` declares #268, not #100.
    Assert.Equal<string list>([ "FS-GG/FS.GG.SDD#268" ], refs "- [x] (b) #268 — matched by substring (cf. #100)")

[<Fact>]
let ``all three task-list bullets are honoured - a gate must not fail open on a formatting choice`` () =
    let body = "* [ ] #1 star\n+ [x] #2 plus\n- [ ] #3 dash"
    Assert.Equal<string list>([ "FS-GG/FS.GG.SDD#1"; "FS-GG/FS.GG.SDD#2"; "FS-GG/FS.GG.SDD#3" ], refs body)

[<Fact>]
let ``a non-checkbox bullet is not a child declaration`` () =
    // A plain list item (no `[ ]`/`[x]`) is not a task-list line.
    Assert.Equal<string list>([], refs "- #1 just a bullet, no checkbox")

[<Fact>]
let ``all three ref spellings canonicalize to owner/repo#n against the epic's repo`` () =
    let body =
        "- [ ] #7 bare\n- [x] FS-GG/FS.GG.Rendering#8 qualified\n- [ ] https://github.com/FS-GG/FS.GG.Audio/issues/9 a url"

    Assert.Equal<string list>(
        [ "FS-GG/FS.GG.Audio#9"; "FS-GG/FS.GG.Rendering#8"; "FS-GG/FS.GG.SDD#7" ],
        refs body
    )

[<Fact>]
let ``the result is deduplicated and sorted - the set is stable and diffable`` () =
    // Sorted so the diff against the graph is order-independent; deduped so a repeat is one child.
    Assert.Equal<string list>([ "FS-GG/FS.GG.SDD#5"; "FS-GG/FS.GG.SDD#9" ], refs "- [ ] #9 a\n- [x] #5 b\n- [ ] #9 again")

[<Fact>]
let ``an empty or null body declares nothing`` () =
    Assert.Equal<string list>([], refs "")
    Assert.Equal<string list>([], EpicBody.childRefs "FS-GG" "FS.GG.SDD" null)

[<Fact>]
let ``a task line inside a fenced code block declares nothing - a quote is a mention`` () =
    // #965's own first draft quoted #672's acceptance line in a fence to DEMONSTRATE this bug, and thereby
    // declared #561 as a child of the issue reporting it. A doc that quotes a parser's input is parsed.
    let body = "This is what #672 carries today:\n\n```\n- [ ] #561's three steps land in their gated order.\n```\n\n- [ ] #900 the only real child"

    Assert.Equal<string list>([ "FS-GG/FS.GG.SDD#900" ], refs body)

[<Fact>]
let ``both fence spellings are honoured, with an info string and any indent`` () =
    let backticks = "```markdown\n- [ ] #1 quoted\n```\n- [ ] #2 real"
    Assert.Equal<string list>([ "FS-GG/FS.GG.SDD#2" ], refs backticks)

    let tildes = "~~~\n- [ ] #1 quoted\n~~~\n- [ ] #2 real"
    Assert.Equal<string list>([ "FS-GG/FS.GG.SDD#2" ], refs tildes)

    // Up to three leading spaces still opens a fence, and the closer need not share the opener's indent.
    let indented = "   ```\n- [ ] #1 quoted\n  ```\n- [ ] #2 real"
    Assert.Equal<string list>([ "FS-GG/FS.GG.SDD#2" ], refs indented)

[<Fact>]
let ``a fence is closed only by its OWN character, at least as long as the opener`` () =
    // GFM: a ``` block carries a ~~~ line as content, and a shorter run of its own char too. Reading either
    // as a closer would end the block early and declare the quoted children that follow.
    let crossed = "```\n~~~\n- [ ] #1 still quoted\n```\n- [ ] #2 real"
    Assert.Equal<string list>([ "FS-GG/FS.GG.SDD#2" ], refs crossed)

    let shortRun = "````\n```\n- [ ] #1 still quoted\n````\n- [ ] #2 real"
    Assert.Equal<string list>([ "FS-GG/FS.GG.SDD#2" ], refs shortRun)

    // A closer carries no info string; a run with one is still content.
    let infoOnCloser = "```\n``` not a closer\n- [ ] #1 still quoted\n```\n- [ ] #2 real"
    Assert.Equal<string list>([ "FS-GG/FS.GG.SDD#2" ], refs infoOnCloser)

[<Fact>]
let ``an unclosed fence runs to the end of the body - as GitHub renders it`` () =
    // The parser and the human must agree about where the code stops. GitHub renders an unclosed fence to
    // the end of the document, so a task line after one is code to the reader — and declares nothing.
    Assert.Equal<string list>([ "FS-GG/FS.GG.SDD#1" ], refs "- [ ] #1 real\n\n```\n- [ ] #2 quoted, forever")

[<Fact>]
let ``a backtick run whose info string contains a backtick is NOT a fence - the gate must not fail open`` () =
    // ```` ```#5``` is the ref ```` is a paragraph per CommonMark, not an opener. Reading it as one would
    // swallow every real task line after it and report the epic as childless — the gate failing OPEN, which
    // is the direction that loses declarations rather than inventing them.
    let body = "```#5``` is the ref\n- [ ] #1 real\n- [ ] #2 real"
    Assert.Equal<string list>([ "FS-GG/FS.GG.SDD#1"; "FS-GG/FS.GG.SDD#2" ], refs body)

    // The tilde spelling has no such rule: `~~~foo~bar` opens a block.
    Assert.Equal<string list>([], refs "~~~foo~bar\n- [ ] #1 quoted")

// ---- un-delegated acceptance (#965) ---------------------------------------------------------------
//
// The other half of the same scan: the acceptance lines that name NO child. `childRefs` used to drop
// these silently, so a criterion nobody was ever given became invisible to every guard the rollup has —
// and #561 was closed over one.

[<Fact>]
let ``a task line naming no issue is KEPT as un-delegated acceptance, not dropped`` () =
    let body =
        "Umbrella epic.\n\n- [ ] #10 delegated to a child\n- [ ] All six repos' locks re-pinned as a coherent set.\n- [x] #11 also delegated"

    // The two halves partition the same task lines: the ref-bearing ones are children...
    Assert.Equal<string list>([ "FS-GG/FS.GG.SDD#10"; "FS-GG/FS.GG.SDD#11" ], refs body)
    // ...and the ref-less one is acceptance nothing can discharge. It is NAMED, not silently gone.
    Assert.Equal<string list>(
        [ "- [ ] All six repos' locks re-pinned as a coherent set." ],
        EpicBody.undelegatedAcceptance body
    )

[<Fact>]
let ``an epic whose every acceptance line is a child ref has NO un-delegated acceptance`` () =
    // The state the rule drives every epic toward: the graph IS the acceptance, so the rollup is sound by
    // construction rather than by checking. This is the case that must stay silent, or the rule is noise.
    let body = "- [ ] #10 the first\n- [x] #11 the second\n\nProse about the epic, mentioning #12."
    Assert.Equal<string list>([], EpicBody.undelegatedAcceptance body)

[<Fact>]
let ``a checked-off ref-less line is STILL un-delegated - a self-report is not a discharge`` () =
    // `- [x]` is the author saying "I did this", which is the exact claim #561 was closed on. A box a human
    // ticked is a record, not reality (#266); nothing in the graph corroborates it.
    Assert.Equal<string list>(
        [ "- [x] step 3: global.json into FILES, tripwire deleted" ],
        EpicBody.undelegatedAcceptance "- [x] step 3: global.json into FILES, tripwire deleted"
    )

[<Fact>]
let ``a QUOTED ref-less task line declares nothing and is un-delegated acceptance NOWHERE`` () =
    // The fence rule (#972) applies to both halves of the scan, or this rule would go red on every issue
    // that quotes an epic's acceptance to talk about it — which is what this issue's own body does.
    let body = "```\n- [ ] a quoted acceptance line with no ref\n```\n- [ ] #10 real"

    Assert.Equal<string list>([], EpicBody.undelegatedAcceptance body)
    Assert.Equal<string list>([ "FS-GG/FS.GG.SDD#10" ], refs body)

[<Fact>]
let ``un-delegated lines come back in BODY order, not sorted`` () =
    // A human has to find each line to fix it. `childRefs` sorts because a ref set is diffed; these are
    // prose, and re-ordering them would scramble the document the reader is about to edit.
    let body = "- [ ] zebra last in the body\n- [ ] alpha second\n- [ ] #10 a real child"

    Assert.Equal<string list>(
        [ "- [ ] zebra last in the body"; "- [ ] alpha second" ],
        EpicBody.undelegatedAcceptance body
    )

// ---- "no acceptance stated" is not "all delegated" (#1003) -----------------------------------------
//
// The hole #965 shipped with. `childRefs` and `undelegatedAcceptance` partition the task lines, so when
// there are none BOTH return [] — and the rollup read that as "every criterion is a child".

[<Fact>]
let ``a body of PROSE bullets states NO acceptance - #889's real shape`` () =
    // Verbatim the shape that closed .github#889 ~10 minutes after #965's guard landed: `-` bullets with
    // no checkbox. `taskLine` requires `[ ]`, so none of this is acceptance to the parser, and the #965
    // guard saw nothing to object to.
    let body =
        "## The work\n\nFold the remaining restatements into generated regions:\n\n- `pnext-item` — §0's mint ritual, §1's exit-code table\n- `cross-repo-coordination`\n- `check-board`"

    Assert.Equal<string list>([], EpicBody.undelegatedAcceptance body) // ...what #965 asked, and why it passed
    Assert.Equal<string list>([], refs body)
    Assert.False(EpicBody.statesAcceptance body) // ...and the fact that tells the two apart

[<Fact>]
let ``#561's shape - prose steps, four closed children, and NOTHING the guard could read`` () =
    // The false closure #965 was WRITTEN ABOUT, and the one its own fix could not see. Step 3 was a
    // sentence. A fix that does not catch this has not addressed #965.
    let body =
        "Roll the org SDK pin out to the four unpinned repos, THEN enforce.\n\nStep 1 lands the receivers. Step 2 flips the tripwire.\nStep 3: add `global.json` to `FILES` and delete the tripwire."

    Assert.False(EpicBody.statesAcceptance body)

[<Fact>]
let ``a body with even ONE task line states acceptance - the guard is not a blanket refusal`` () =
    // The line between #1003's refusal and #965's: once a task line exists, #965's rules govern it and
    // this fact is satisfied. A checked-off, ref-less line still counts as STATED — it is un-delegated,
    // which is #965's finding, not this one's. The two must not both fire on it.
    Assert.True(EpicBody.statesAcceptance "- [ ] #10 delegated")
    Assert.True(EpicBody.statesAcceptance "- [x] step 3: the tripwire")
    Assert.True(EpicBody.statesAcceptance "Prose about it.\n\n- [ ] #10 a child\n\nMore prose.")

[<Fact>]
let ``a QUOTED task list states no acceptance - it is a mention, and fences are #972's rule`` () =
    // An issue that quotes an epic's acceptance to talk about it has stated none of its own. If this
    // returned true, quoting a task list would be enough to let a parent close.
    Assert.False(EpicBody.statesAcceptance "```\n- [ ] #10 quoted, not mine\n```\nprose")
    Assert.True(EpicBody.statesAcceptance "```\n- [ ] #10 quoted\n```\n- [ ] #11 mine")

[<Fact>]
let ``an EMPTY body states no acceptance, and does not throw`` () =
    Assert.False(EpicBody.statesAcceptance "")
    Assert.False(EpicBody.statesAcceptance null)
