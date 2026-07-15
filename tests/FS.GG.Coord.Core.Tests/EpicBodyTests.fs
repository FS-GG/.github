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
