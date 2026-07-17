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
