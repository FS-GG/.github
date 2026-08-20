namespace FS.GG.Coord.Cli.Tests

open Xunit
open FS.GG.Coord
open FS.GG.Coord.Cli.Tests.ApplicationServiceTests

/// .github#2306 — A REFUSED `widen`/`set-paths` MUST NOT MUTATE THE DECLARATION.
///
/// THE DEFECT. `updateTouchSet` (`Client.fs`) ran its `#353` collision scan BEFORE the write — so an
/// UNREADABLE scan already refused with the body untouched (`Writes.fsi`'s own header comment calls this
/// "GATES the PATCH") — but the scan's own VERDICT never gated anything. A scan that completed and found a
/// real collision still issued the `Writes.widen` PATCH unconditionally; only the notify branch and exit
/// code below it depended on whether `collisions` was empty. So a refused widen — the caller sees a
/// non-zero exit and an `OVERLAP` verdict, which every worker is instructed to treat as "nothing happened"
/// — had, in fact, already rewritten the item's `Paths:`. `.github#2248` is the board-data damage this
/// caused: two refused widens each still landed the paths they named, so the item ended up declaring three
/// files it was never granted, colliding with two other live claims by construction.
///
/// THE FIX, split across this item's four files:
///   * `TouchSet.decideUpdate` (`FS.GG.Coord.Core/TouchSet.fs`/`.fsi`) states the ALL-OR-NOTHING refusal
///     rule explicitly, as a published, testable decision rather than an inline `if` at the CLI call site
///     — mirroring `TouchSet.usability`'s own ANY-not-every idiom one level up in the same module.
///   * `Client.updateTouchSet` (`FS.GG.Coord.Cli/Client.fs`) now asks that decision BEFORE calling
///     `Writes.widen`, so the PATCH only fires on `CommitUpdate`. Everything else — the exit code, the
///     JSON receipt shape, the courtesy notice to a colliding holder, the `#1740 AC5` narrowing-vs-not
///     stderr wording — is unchanged; only the wording that used to claim a completed write ("I widened
///     … to `Paths: …`") was corrected, because that claim is no longer true on the refused path.
///
/// WHY THESE LEGS READ THE FAKE TRANSPORT'S LOG RATHER THAN A RE-READ BODY. `ApplicationServiceTests.fs`'s
/// own fixture accepts a PATCH but does not persist it ("the #523 re-check compares the touch-set it
/// REWROTE in memory, never a re-read, so persisting it would test nothing extra") — so a second read
/// through this fixture cannot distinguish "the PATCH never fired" from "it fired and changed nothing
/// visible". `Fake.Recorder.Count` reading the `issue-patch <nwo> <n>` log line IS the fixture's own
/// mutation-proof idiom (the hermetic `tests/coord-engine-e2e`/`tests/coord-engine-parity` scripts use the
/// same technique against a real HTTP log, via `patches_on`) — zero occurrences is the fixture's honest
/// equivalent of "the body PATCH never happened", which is what "byte-identical to before the call" means
/// for a caller that can only observe this transport's own record of what it sent.
///
/// #2323 ROUND 1 REPAIR. The critic reproduced a second half of the same defect: the Text projection's
/// "Paths: unchanged" line was fixed, but `--json`'s `paths` field (`Render.fsi`'s "the tokens the item
/// now declares") still unconditionally reported the REQUESTED tokens, even on a refusal that committed
/// none of them — a caller that reads JSON rather than greps text got a coherent-looking lie. The `AC1`/
/// `AC2` legs below, across both verbs, are the regression coverage for that repair: they assert the JSON
/// `paths` array itself, not merely `verdict`, on both a full and a partial collision.
module WidenRefusalTests =

    // ---- TouchSet.decideUpdate — the published all-or-nothing decision, in isolation (AC2) -------------

    [<Fact>]
    let ``decideUpdate refuses when the caller's scan reports a collision`` () =
        Assert.Equal(TouchSet.RefuseUpdate, TouchSet.decideUpdate true)

    [<Fact>]
    let ``decideUpdate commits when the caller's scan reports no collision`` () =
        Assert.Equal(TouchSet.CommitUpdate, TouchSet.decideUpdate false)

    // ---- AC1: a FULL collision refuses, and writes NOTHING ----------------------------------------------

    [<Theory>]
    [<InlineData "widen">]
    [<InlineData "set-paths">]
    let ``AC1/AC3: a fully-colliding update leaves zero PATCH on the subject item`` (verb: string) =
        // #74 (ours) vs #75 (otter-9c21's, declaring `src/Shared.fs`) — `overlappingWorld` from
        // `ApplicationServiceTests.fs`, reused rather than re-modelled: both verbs share ONE
        // implementation (`Client.updateTouchSet`), so exercising `set-paths` here directly is what makes
        // AC3 ("the same failure mode, checked") a claim this suite backs rather than infers from `widen`.
        let world = overlappingWorld false

        let code, out =
            run world [ verb; "FS.GG.SDD#74"; "--worker"; "kite-469"; "--json"; "--paths"; "src/Shared.fs" ]

        Assert.Equal(6, code)
        Assert.Equal("overlap", str "verdict" (parsed out))

        // THE CORE OF #2306. Not "the exit code was non-zero" — the item's OWN `Paths:` was never PATCHed.
        Assert.Equal(0, world.Count "issue-patch FS-GG/FS.GG.SDD 74")

    // ---- AC2: a PARTIAL collision — one of several requested paths — refuses the WHOLE call -------------

    [<Fact>]
    let ``AC2: a widen naming one clear path and one colliding path commits NEITHER`` () =
        let world = overlappingWorld false

        let code, out =
            run
                world
                [ "widen"
                  "FS.GG.SDD#74"
                  "--worker"
                  "kite-469"
                  "--json"
                  "--paths"
                  "docs/unrelated.md"
                  "src/Shared.fs" ]

        Assert.Equal(6, code)
        Assert.Equal("overlap", str "verdict" (parsed out))

        // ALL-OR-NOTHING, not "the colliding token alone was withheld": `docs/unrelated.md` — which does
        // NOT collide with anything — is not committed either, because this call refuses as one unit.
        Assert.Equal(0, world.Count "issue-patch FS-GG/FS.GG.SDD 74")

    // ---- #2323 round 1 repair: the JSON `paths` field must also report the UNCHANGED declaration --------

    [<Theory>]
    [<InlineData "widen">]
    [<InlineData "set-paths">]
    let ``AC1: a fully-colliding update's JSON paths reports the PRIOR declaration, not the request`` (verb: string) =
        // Before the repair, `receipt`'s `Paths` field was `proposed.Tokens` unconditionally — so a
        // refused widen's JSON still showed `["scripts/fsgg-coord"; "src/Shared.fs"]` (the rejected token
        // included) and a refused set-paths showed `["src/Shared.fs"]` (falsely implying a replacement),
        // even though `AC1/AC3` above already proves zero PATCH landed. A program that gates on the exit
        // code and then reads `paths` — the documented "tokens the item now declares" — got a
        // coherent-looking lie. #74's ONLY legitimate declaration in `overlappingWorld` is
        // `scripts/fsgg-coord`; that is what the JSON must report on refusal, for both verbs.
        let world = overlappingWorld false

        let code, out =
            run world [ verb; "FS.GG.SDD#74"; "--worker"; "kite-469"; "--json"; "--paths"; "src/Shared.fs" ]

        Assert.Equal(6, code)
        let receipt = parsed out
        Assert.Equal("overlap", str "verdict" receipt)
        Assert.Equal<string list>([ "scripts/fsgg-coord" ], strings "paths" receipt)

    [<Theory>]
    [<InlineData "widen">]
    [<InlineData "set-paths">]
    let ``AC2: a partial-collision update's JSON paths ALSO reports the PRIOR declaration`` (verb: string) =
        // Same repair, over the multi-path request AC2 is about: naming one clear path and one colliding
        // path must not leak the clear one into the JSON `paths` either — the whole call committed
        // nothing, so the JSON must say exactly what was true before the call, same as the Text form.
        let world = overlappingWorld false

        let code, out =
            run
                world
                [ verb
                  "FS.GG.SDD#74"
                  "--worker"
                  "kite-469"
                  "--json"
                  "--paths"
                  "docs/unrelated.md"
                  "src/Shared.fs" ]

        Assert.Equal(6, code)
        let receipt = parsed out
        Assert.Equal("overlap", str "verdict" receipt)
        Assert.Equal<string list>([ "scripts/fsgg-coord" ], strings "paths" receipt)

    // ---- AC5: the negative control — a genuinely disjoint update still succeeds AND still writes --------

    [<Theory>]
    [<InlineData "widen">]
    [<InlineData "set-paths">]
    let ``AC5: a genuinely disjoint update still commits — the fix does not turn it into a no-op`` (verb: string) =
        let world = disjointWorld ()

        let code, out =
            run world [ verb; "FS.GG.SDD#74"; "--worker"; "kite-469"; "--json"; "--paths"; "docs/new.md" ]

        Assert.Equal(0, code)
        Assert.Equal("disjoint", str "verdict" (parsed out))
        Assert.Equal(1, world.Count "issue-patch FS-GG/FS.GG.SDD 74")

    // ---- the human receipt line no longer claims a write that did not happen ----------------------------

    [<Theory>]
    [<InlineData("widen", "add paths to")>]
    [<InlineData("set-paths", "replace")>]
    let ``a refused update's human line names the refusal, not a completed write`` (verb: string, action: string) =
        let world = overlappingWorld false

        let code, out =
            run world [ verb; "FS.GG.SDD#74"; "--worker"; "kite-469"; "--paths"; "src/Shared.fs" ]

        Assert.Equal(6, code)
        // The OLD line — "widened FS.GG.SDD#74 → Paths: …" / "set FS.GG.SDD#74 → Paths: …" — asserted a
        // completed mutation. #2306 is precisely that this claim was false on the OVERLAP branch, so its
        // absence here is the behavioural assertion, not merely a style change.
        Assert.DoesNotContain("widened FS.GG.SDD#74 →", out)
        Assert.DoesNotContain("set FS.GG.SDD#74 →", out)
        Assert.Contains("refused to " + action + " FS.GG.SDD#74's touch-set", out)
        Assert.Contains("Paths: unchanged", out)
