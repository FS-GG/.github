module FS.GG.Coord.GitHub.Tests.DoneTests

open Xunit
open FS.GG.Coord.Types
open FS.GG.Coord.GitHub
open FS.GG.Coord.GitHub.Errors
open FS.GG.Coord.GitHub.Transport
open FS.GG.Coord.GitHub.Done

let private aRef =
    { Owner = "FS-GG"
      Repo = "FS.GG.SDD"
      Number = 398 }

let private parentRef =
    { Owner = "FS-GG"
      Repo = "FS.GG.SDD"
      Number = 350 }

/// A merged PR whose BODY names this issue — a true closer (`ClosesThis`), the ordinary case. Its `Repo`
/// defaults to `aRef`'s own repository — the ordinary same-repo case — so cross-repo tests (#2427) override
/// it explicitly rather than every other test needing to state the obvious.
let private closer n =
    { Number = n
      Merged = true
      MergedAt = "2026-01-01T00:00:00Z"
      Oid = "abc1234"
      Repo = "FS-GG/FS.GG.SDD"
      ClosesThis = true }

/// A merged PR the issue's own CLOSED_EVENT names as the closer, whose BODY never named the issue — so
/// GitHub does NOT list it in `closedByPullRequestsReferences` at all. This is the #558 case, in the shape
/// GitHub actually returns it (#928).
let private eventCloser n = { closer n with ClosesThis = false }

/// A closed issue with a merged PR and no children — the ordinary green case.
let private closedByPr =
    { Ref = aRef
      State = Closed
      ClosingPrs = [ closer 399 ]
      CloserPrs = []
      Children = NoChildren
      BoardStatus = InReview
      Parent = None }

// ---- the ordinary green path ------------------------------------------------------------------------

[<Fact>]
let ``a closed issue with a merged PR is DONE`` () =
    match verify None None closedByPr with
    | Green(ClosedByPullRequest(399, _, _, _)) -> ()
    | other -> failwith $"a merged PR closes it — got %A{other}"

[<Fact>]
let ``#928 the CLOSED_EVENT rescues a PR whose BODY never carried the keyword - and it is NOT listed`` () =
    // `gh pr create --fill` maps the COMMIT SUBJECT to the PR TITLE. So a worker whose commit reads
    // `fix: the thing (closes #NNN)` puts the keyword where `closingIssuesReferences` NEVER LOOKS — and the
    // squash commit still closes the issue.
    //
    // A correct, merged, green PR therefore stamped RED. Permanently: editing a merged PR's body does not
    // backfill the reference. The CLOSED_EVENT's closer is the record of the ACT, and it is what saves this.
    //
    // THE FIXTURE IS THE POINT (#928). This test used to assert `ClosingPrs = [ closer 399 ]` — "the PR is
    // LISTED (a merge that closed the issue is)" — and that premise is FALSE. A PR whose body never named the
    // issue is absent from `closedByPullRequestsReferences` ENTIRELY, because GitHub builds that connection
    // FROM the body linkage. Measured on .github#622 / PR #926: the reference list is [], the close event
    // names the squash commit, and the commit names the merged PR.
    //
    // So the old fixture hand-built the one state in which leg (B) was reachable — it proved the FILTER
    // worked and never touched the case #558 exists for, which is why the bug shipped green and stayed green.
    let facts =
        { closedByPr with
            ClosingPrs = []
            CloserPrs = [ eventCloser 399 ] }

    match verify None None facts with
    | Green(ClosedByPullRequest(399, _, _, _)) -> ()
    | other -> failwith $"the closing ACT must be honoured, not just the closing PROSE — got %A{other}"

[<Fact>]
let ``#928 a LISTED PR whose body never carried the keyword is still rescued`` () =
    // The other shape: GitHub does list the PR (some other reference put it there) but its body never named
    // the issue, so it is not a `ClosesThis` closer. The union must not regress the case that already worked —
    // and the candidate must not be DUPLICATED into the set when both records carry it.
    let facts =
        { closedByPr with
            ClosingPrs = [ { closer 399 with ClosesThis = false } ]
            CloserPrs = [ eventCloser 399 ] }

    match verify None None facts with
    | Green(ClosedByPullRequest(399, _, _, _)) -> ()
    | other -> failwith $"a listed non-ClosesThis closer named by the event is still a closer — got %A{other}"

[<Fact>]
let ``#928 an UNMERGED closer named by the close event does NOT stamp - the union cannot launder`` () =
    // THE SOUNDNESS EDGE OF THE UNION, and the reason `CloserPrs` carries merge facts rather than bare
    // numbers. `associatedPullRequests` returns the PRs that CONTAIN the closing commit — which need not be
    // merged ones. Admitting a closer by ASSUMING it merged would re-open the #543 leg-2 hole through the
    // very door #928 opened: `Merged` is still required of a candidate, whichever record produced it.
    let facts =
        { closedByPr with
            ClosingPrs = []
            CloserPrs = [ { eventCloser 399 with Merged = false } ] }

    match verify None None facts with
    | Red reasons ->
        // AND IT SAYS SO HONESTLY — it must not claim the close event named nothing (#928 ask 3).
        Assert.Contains(reasons, fun r -> r.Contains "#399" && r.Contains "MERGED")
        Assert.DoesNotContain(reasons, fun r -> r.Contains "names no PR or commit")
    | other -> failwith $"an unmerged closer has landed no work — got %A{other}"

[<Fact>]
let ``#928 --pr names a closer the reference list never listed`` () =
    // `--pr 926` on .github#622 reported, WRONGLY, "PR #926 does not close this issue" — it filtered the same
    // empty list. The override reaches the union too, and is still held to the same closer predicate.
    let facts =
        { closedByPr with
            ClosingPrs = []
            CloserPrs = [ eventCloser 399 ] }

    match verify (Some 399) None facts with
    | Green(ClosedByPullRequest(399, _, _, _)) -> ()
    | other -> failwith $"--pr must reach a closer the event named — got %A{other}"

[<Fact>]
let ``#928 the union does not disturb #342 - the latest-merged closer still wins`` () =
    // The union ADDS a source; it must not soften a single test below it. Provenance still decides among true
    // closers, across both records: an event-named closer that merged LATER outranks a listed one.
    let facts =
        { closedByPr with
            ClosingPrs = [ { closer 89 with MergedAt = "2026-01-01T00:00:00Z"; Oid = "1111aaa" } ]
            CloserPrs = [ { eventCloser 95 with MergedAt = "2026-03-01T00:00:00Z"; Oid = "2222bbb" } ] }

    match verify None None facts with
    | Green(ClosedByPullRequest(95, "2222bbb", _, _)) -> ()
    | other -> failwith $"the latest-merged closer wins across BOTH records — got %A{other}"

// ---- #600: the green path for work resolved WITHOUT a PR --------------------------------------------

[<Fact>]
let ``#600 an item resolved WITHOUT a PR is DONE - when there is evidence`` () =
    // An item legitimately closed with no code change in this repo at all: obsolete, resolved by other
    // work, a duplicate whose detail was transplanted into the survivor (which `pnext-item` §4 explicitly
    // instructs a worker to do), a decision item whose deliverable is an ADR somewhere else.
    //
    // Every one of those stamped RED, reproducibly, on CORRECT WORK. And a red that fires reproducibly on
    // correct work is the fastest way to teach every worker that red stamps are noise — which is exactly
    // the credibility this stamp exists to have.
    let facts =
        { closedByPr with
            ClosingPrs = []
            CloserPrs = [] }

    match verify None (Some "resolved by #380/#383/#385 plus a feed re-baseline; verified empirically") facts with
    | Green(ResolvedWithoutPr evidence) -> Assert.Contains("feed re-baseline", evidence)
    | other -> failwith $"a no-PR resolution with evidence is a GREEN path, not a workaround — got %A{other}"

[<Fact>]
let ``#600 ...but the evidence is REQUIRED - a blank one is refused`` () =
    // A green path that took no argument would not be a stamp. It would be a way of switching the stamp
    // OFF, and it would be reached for by exactly the people it was not meant for.
    let facts =
        { closedByPr with
            ClosingPrs = []
            CloserPrs = [] }

    match verify None (Some "   ") facts with
    | Red reasons -> Assert.Contains(reasons, fun r -> r.Contains "blank")
    | other -> failwith $"blank evidence is no evidence — got %A{other}"

[<Fact>]
let ``a CLOSED issue that nothing closed is RED - and it names the green path`` () =
    let facts =
        { closedByPr with
            ClosingPrs = []
            CloserPrs = [] }

    match verify None None facts with
    | Red reasons ->
        Assert.Contains(reasons, fun r -> r.Contains "nothing records what closed it")
        // THE REFUSAL TELLS THE WORKER WHAT WOULD HAVE WORKED. A refusal that does not is a refusal that
        // sends somebody to read the source.
        Assert.Contains(reasons, fun r -> r.Contains "#600")
    | other -> failwith $"a closed issue with no closing act is RED — got %A{other}"

[<Fact>]
let ``the evidence does NOT override a real PR - the record wins over the prose`` () =
    // A worker who passes `--evidence` on an item that DID have a closing PR gets the PR named in the
    // stamp, not their own sentence. The stamp records what GitHub observed, not what anybody asserted.
    match verify None (Some "I say it is done") closedByPr with
    | Green(ClosedByPullRequest(399, _, _, _)) -> ()
    | other -> failwith $"the record outranks the assertion — got %A{other}"

// ---- #342: provenance — the LATEST-merged true closer, never the first mention ----------------------

[<Fact>]
let ``#342 among two true closers the LATEST-merged wins, not the lowest-numbered`` () =
    // `closedByPullRequestsReferences` is returned lowest-number-first, so taking the first stamped an
    // earlier merge. The stamp must name the PR that ACTUALLY landed the work — the latest-merged.
    let facts =
        { closedByPr with
            ClosingPrs =
                [ { closer 89 with MergedAt = "2026-01-01T00:00:00Z"; Oid = "1111aaa" }
                  { closer 95 with MergedAt = "2026-03-01T00:00:00Z"; Oid = "2222bbb" } ] }

    match verify None None facts with
    | Green(ClosedByPullRequest(95, "2222bbb", _, _)) -> ()
    | other -> failwith $"the latest-merged closer wins — got %A{other}"

[<Fact>]
let ``#342 a merged PR that only MENTIONS the issue is not a closer`` () =
    // A merged PR whose body names a DIFFERENT issue (ClosesThis = false) and which GitHub's close event
    // does not name has closed nothing here. It is a mention — our "Filed, not fixed: #N" convention is
    // exactly this — and a mention must not stamp the issue green.
    let facts =
        { closedByPr with
            ClosingPrs = [ { closer 97 with ClosesThis = false } ]
            CloserPrs = [] }

    match verify None None facts with
    | Red reasons -> Assert.Contains(reasons, fun r -> r.Contains "no merged PR closes this issue")
    | other -> failwith $"a mere mention does not close the issue — got %A{other}"

// ---- #543: --pr overrides WHICH pr, never WHETHER it closed the issue --------------------------------

[<Fact>]
let ``#543 --pr cannot launder a mention into a stamp`` () =
    // The documented escape hatch used to select by NUMBER alone, so pointing `--pr` at any merged PR that
    // merely mentioned the issue stamped it green — the #342 hole, through the override. `--pr` is held to
    // the same closer predicate: it names which PR, never whether it closed the issue.
    let facts =
        { closedByPr with
            ClosingPrs = [ { closer 97 with ClosesThis = false } ]
            CloserPrs = [] }

    match verify (Some 97) None facts with
    | Red reasons -> Assert.Contains(reasons, fun r -> r.Contains "does not close this issue")
    | other -> failwith $"--pr must not launder a mention — got %A{other}"

[<Fact>]
let ``#543 --pr names WHICH true closer to stamp, among several`` () =
    // When more than one PR truly closed the issue, `--pr` picks which one the stamp names — a legitimate
    // human override of WHICH, honoured because the chosen PR really is a closer.
    let facts =
        { closedByPr with
            ClosingPrs =
                [ { closer 89 with MergedAt = "2026-01-01T00:00:00Z"; Oid = "1111aaa" }
                  { closer 95 with MergedAt = "2026-03-01T00:00:00Z"; Oid = "2222bbb" } ] }

    match verify (Some 89) None facts with
    | Green(ClosedByPullRequest(89, "1111aaa", _, _)) -> ()
    | other -> failwith $"--pr names which true closer to stamp — got %A{other}"

// ---- .github#2427: a same-repository closer outranks a foreign one, regardless of merge time ---------

[<Fact>]
let ``#2427 a same-repo true closer wins over a LATER-merged foreign-repo closer, and names what it passed over`` () =
    // Measured on .github#2343: the source fix (.github#2413) merged FIRST, in this repo. A cross-repo
    // receiver retrofit (EHotwagner/S.I.R.#195) merged ~13 minutes LATER and also registered as a true
    // closer — its body's "Source fix: FS-GG/.github#2343" line was never meant as a closing keyword, but
    // GitHub's parser matched `fix:` immediately before the cross-repo reference anyway. Pure latest-merged
    // (#342) then picked the retrofit, stamping a PR in another repository as the source of the fix.
    let facts =
        { closedByPr with
            ClosingPrs =
                [ { closer 413 with
                      MergedAt = "2026-08-12T08:06:28Z"
                      Oid = "e605d37"
                      Repo = "FS-GG/FS.GG.SDD" }
                  { closer 195 with
                      MergedAt = "2026-08-12T08:19:28Z"
                      Oid = "938020f"
                      Repo = "EHotwagner/S.I.R." } ] }

    match verify None None facts with
    | Green(ClosedByPullRequest(413, "e605d37", _, Some(195, "EHotwagner/S.I.R."))) -> ()
    | other ->
        failwith
            $"the same-repo closer must win over a later-merged foreign one, and the stamp must name the foreign PR it passed over — got %A{other}"

[<Fact>]
let ``#2427 among two SAME-repo closers, latest-merged still wins - the repository preference does not soften #342`` () =
    // The repository preference decides WHICH TIER wins; #342's latest-merged rule is unchanged for
    // deciding among closers that share a tier.
    let facts =
        { closedByPr with
            ClosingPrs =
                [ { closer 89 with MergedAt = "2026-01-01T00:00:00Z"; Oid = "1111aaa"; Repo = "FS-GG/FS.GG.SDD" }
                  { closer 95 with MergedAt = "2026-03-01T00:00:00Z"; Oid = "2222bbb"; Repo = "FS-GG/FS.GG.SDD" } ] }

    match verify None None facts with
    | Green(ClosedByPullRequest(95, "2222bbb", _, None)) -> ()
    | other -> failwith $"latest-merged must still decide among same-repo closers, with nothing passed over — got %A{other}"

[<Fact>]
let ``#2427 with no same-repo closer at all, the foreign one wins and nothing is reported passed over`` () =
    // There is no same-repo rival to prefer AWAY from, so the foreign closer legitimately wins and the
    // stamp must not claim it "passed over" a closer that never competed.
    let facts =
        { closedByPr with
            ClosingPrs = [ { closer 195 with Repo = "EHotwagner/S.I.R." } ] }

    match verify None None facts with
    | Green(ClosedByPullRequest(195, _, _, None)) -> ()
    | other -> failwith $"a lone foreign closer still stamps green, with nothing passed over — got %A{other}"

[<Fact>]
let ``#2427 --pr can still name the foreign closer explicitly - the override skips the preference, not the provenance check`` () =
    // `--pr` overrides WHICH pull request the stamp names, never whether it closed the issue (#543) — and
    // that includes the repository preference: an operator who explicitly asks for the foreign PR by number
    // gets it. Nothing is reported "passed over" because this was an explicit choice, not a silent one.
    let facts =
        { closedByPr with
            ClosingPrs =
                [ { closer 413 with Repo = "FS-GG/FS.GG.SDD" }
                  { closer 195 with Repo = "EHotwagner/S.I.R." } ] }

    match verify (Some 195) None facts with
    | Green(ClosedByPullRequest(195, _, _, None)) -> ()
    | other -> failwith $"--pr must still be able to name the foreign PR when explicitly asked — got %A{other}"

[<Fact>]
let ``#2444 render's stdout stamp names ONLY the winner - the passed-over note does not ride stdout`` () =
    // .github#2444: `render`'s stdout value is a single-line value some caller may `grep` or diff exactly
    // (`.github#2427`'s own acceptance criterion, and #733's precedent for a candidate that existed but
    // was not chosen). Restored to its pre-#2427 single-purpose shape.
    let stamp = render aRef (Green(ClosedByPullRequest(413, "e605d37", "2026-08-12", Some(195, "EHotwagner/S.I.R."))))

    Assert.Contains("FSGG-DONE", stamp)
    Assert.Contains("PR #413", stamp)
    Assert.DoesNotContain("passed over", stamp)
    Assert.DoesNotContain("EHotwagner/S.I.R.", stamp)

[<Fact>]
let ``#2444 gate-inversion: a stamp with NO passed-over closer never gains the note`` () =
    // Inverting the intent (folding the note back into render's stdout shape unconditionally) would make
    // THIS case red too — the None-branch must still print nothing extra.
    let stamp = render aRef (Green(ClosedByPullRequest(399, "abc1234", "2026-01-01", None)))

    Assert.DoesNotContain("passed over", stamp)

[<Fact>]
let ``#2444 passedOverForeignNote names the foreign closer that render's stdout omits`` () =
    let verdict = Green(ClosedByPullRequest(413, "e605d37", "2026-08-12", Some(195, "EHotwagner/S.I.R.")))

    match passedOverForeignNote aRef verdict with
    | Some note ->
        Assert.Contains("EHotwagner/S.I.R.#195", note)
        Assert.Contains("passed over", note)
        Assert.Contains("PR #413", note)
    | None -> failwith "a verdict WITH a passed-over foreign closer must produce a note"

[<Fact>]
let ``#2444 passedOverForeignNote is None when nothing was passed over`` () =
    let verdict = Green(ClosedByPullRequest(399, "abc1234", "2026-01-01", None))

    match passedOverForeignNote aRef verdict with
    | None -> ()
    | Some note -> failwith $"no foreign closer was passed over — got a note anyway: %s{note}"

[<Fact>]
let ``#2444 passedOverForeignNote is None off a red or unverified verdict`` () =
    Assert.True((passedOverForeignNote aRef (Red [ "nope" ])).IsNone)
    Assert.True((passedOverForeignNote aRef (NoVerdict "could not read")).IsNone)

[<Fact>]
let ``#2444 renderReceipt DELIBERATELY diverges from render - the durable comment keeps the note`` () =
    let verdict = Green(ClosedByPullRequest(413, "e605d37", "2026-08-12", Some(195, "EHotwagner/S.I.R.")))
    let stdout = render aRef verdict
    let receipt = renderReceipt aRef verdict

    // stdout stays clean (re-asserted here so the divergence itself, not just each half, is pinned)...
    Assert.DoesNotContain("passed over", stdout)
    // ...while the durable receipt keeps both the stamp AND the provenance note.
    Assert.Contains("FSGG-DONE", receipt)
    Assert.Contains("PR #413", receipt)
    Assert.Contains("EHotwagner/S.I.R.#195", receipt)
    Assert.Contains("passed over", receipt)

[<Fact>]
let ``#2444 renderReceipt matches render exactly when nothing was passed over`` () =
    let verdict = Green(ClosedByPullRequest(399, "abc1234", "2026-01-01", None))

    Assert.Equal(render aRef verdict, renderReceipt aRef verdict)

// ---- #583: open children ----------------------------------------------------------------------------

[<Fact>]
let ``#583 a parent with OPEN sub-issues is not done, whatever its board says`` () =
    let facts =
        { closedByPr with
            Children = SomeOpen [ 401; 402 ]
            BoardStatus = Done }

    match verify None None facts with
    | Red reasons ->
        Assert.Contains(reasons, fun r -> r.Contains "#401")
        Assert.Contains(reasons, fun r -> r.Contains "#402")
    | other -> failwith $"open children block the stamp — got %A{other}"

// ---- the truncation check ---------------------------------------------------------------------------

[<Fact>]
let ``an UNVERIFIABLE child set is NoVerdict - never green, and never a confident red either`` () =
    // `totalCount` and the nodes we were handed disagree, so the page was cut short. An unverifiable subject
    // must not report green — and a truncated page that happened to show only CLOSED children would sail
    // straight through every test below it.
    //
    // `NoVerdict`, not `Red`: we are not saying the work is unfinished. We are saying we could not tell, and
    // those are different sentences with different remedies.
    let facts =
        { closedByPr with
            Children = Unverifiable(120, 100) }

    match verify None None facts with
    | NoVerdict reason ->
        Assert.Contains("truncated", reason)
        Assert.Contains("120", reason)
    | other -> failwith $"a subject we could not fully read is UNVERIFIED — got %A{other}"

[<Fact>]
let ``truncation is checked FIRST - before the closing PR, before everything`` () =
    // A truncated page showing only closed children, on an issue with a perfectly good merged PR, would
    // otherwise pass every test below it and print a confident green over work nobody looked at.
    let facts =
        { closedByPr with
            ClosingPrs = [ closer 399 ]
            Children = Unverifiable(120, 100) }

    match verify None None facts with
    | NoVerdict _ -> ()
    | Green _ -> failwith "a truncated child set produced a GREEN stamp — this is the confident-green over an unread subject"
    | other -> failwith $"expected NoVerdict — got %A{other}"

// ---- an open issue ----------------------------------------------------------------------------------

[<Fact>]
let ``an OPEN issue cannot be stamped - the stamp records that work is finished, it does not finish it`` () =
    let facts = { closedByPr with State = Open }

    match verify None None facts with
    | Red reasons -> Assert.Contains(reasons, fun r -> r.Contains "still OPEN")
    | other -> failwith $"an open issue is not done — got %A{other}"

// ---- the stamp's own prose --------------------------------------------------------------------------

[<Fact>]
let ``a green stamp and a red stamp do not look alike`` () =
    let green = render aRef (Green(ClosedByPullRequest(399, "abc1234", "2026-01-01", None)))
    let red = render aRef (Red [ "nope" ])
    let unverified = render aRef (NoVerdict "could not read")

    Assert.Contains("FSGG-DONE", green)
    Assert.Contains("FSGG-NOT-DONE", red)
    Assert.Contains("FSGG-UNVERIFIED", unverified)

    // An UNVERIFIED stamp is not a red one, and a worker must be able to tell them apart at a glance: one
    // means "your work is incomplete" and the other means "I could not check".
    Assert.DoesNotContain("FSGG-NOT-DONE", unverified)

// ---- #614: the roll-up and the PARTIAL child --------------------------------------------------------

let private ok (body: string) =
    Ok
        { Status = 200
          Body = body
          ETag = None
          NextLink = None; Headers = Map.empty }

let private board: Board.BoardMap =
    { Number = 12
      Id = "PVT_coord"
      Owner = "FS-GG"
      Title = "Coordination"
      Fields =
        Map.ofList
            [ "Status",
              { Id = "PVTSSF_status"
                Type = Board.SingleSelect(Map.ofList [ "Done", "opt_done" ]) } ] }

[<Fact>]
let ``#614 a PARTIAL child does NOT close its parent - even when it is the ONLY child`` () =
    // THE INCIDENT, EXACTLY. FS.GG.SDD#350 needed an ADR *and* a code change. A worker split out the
    // disclosure-only half as #398 — whose body said **in bold** that it does NOT complete FS.GG.SDD#350 — and linked it
    // as a sub-issue, exactly as the recipe instructs.
    //
    // When #398 merged, the roll-up saw "all children complete", CLOSED THE PARENT, and climbed a hop further to
    // stamp an epic Done over it. None of #350's actual work existed. No ADR was written.
    //
    // The roll-up assumed children PARTITION their parent. They do not — and whether they do is a fact only
    // the child's author knows. So it is an ARGUMENT, it has no default, and it is honoured before a single
    // read is made.
    let transport = Fake.Recorder(fun _ -> failwith "a PARTIAL child must not read, write, or close ANYTHING")

    match rollUp transport board "godwit-24dc" parentRef (Partial "#398 is the disclosure-only half; #350 also requires an ADR") with
    | Ok [ ParentLeftOpen(p, reasons) ] ->
        Assert.Equal(parentRef, p)
        Assert.Contains(reasons, fun r -> r.Contains "PARTIAL")
        Assert.Contains(reasons, fun r -> r.Contains "#614")
    | other -> failwith $"a partial child must leave its parent OPEN — got %A{other}"

[<Fact>]
let ``#614 ...and it does not even LOOK at the board - the refusal is free and total`` () =
    // The `Fake` above throws on any request at all. That it never fires is the assertion: a `Partial`
    // discharge stops the climb before a single read, so there is no path on which a partial fix touches
    // its parent's board column, its issue state, or the epic above it.
    let transport = Fake.Recorder(fun _ -> failwith "no IO may happen")

    rollUp transport board "w" parentRef (Partial "half of it") |> ignore
    Assert.Equal(0, transport.GraphQlCalls)
    Assert.Equal(0, transport.RestCalls)

// ---- #613: the board and the issue must not disagree ------------------------------------------------

/// A parent whose children are all resolved, with no parent of its own.
let private parentAllDone =
    """{"data":{"repository":{"issue":{
        "number":350,"state":"OPEN",
        "closedByPullRequestsReferences":{"nodes":[]},
        "timelineItems":{"nodes":[]},
        "subIssues":{"totalCount":1,"nodes":[{"number":398,"state":"CLOSED"}]},
        "projectItems":{"nodes":[{"project":{"number":12},"status":{"name":"In progress"}}]},
        "parent":null}}}}"""

let private itemOnBoard =
    """{"data":{"repository":{"issue":{"projectItems":{"nodes":[{"id":"PVTI_350","project":{"number":12}}]}}}}}"""

let private scripted (responses: IoResult<Response> list) =
    let queue = System.Collections.Generic.Queue<IoResult<Response>>(responses)

    Fake.Recorder(fun _ ->
        if queue.Count = 0 then
            failwith "the transport was called more times than the test scripted"
        else
            queue.Dequeue())

/// The roll-up writes, and a write folds itself into the scan cache — which lives, by default, in the
/// DEVELOPER'S OWN `~/.cache/fsgg-coord`. A test that reaches into it is a test with a side effect on the
/// machine it runs on, and the fold is exactly the kind of thing you would not notice it doing.
type private Sandbox() =
    let dir =
        System.IO.Path.Combine(System.IO.Path.GetTempPath(), "fsgg-done-test-" + System.Guid.NewGuid().ToString("N"))

    do
        System.IO.Directory.CreateDirectory dir |> ignore
        System.Environment.SetEnvironmentVariable("FSGG_COORD_CACHE", dir)

    interface System.IDisposable with
        member _.Dispose() =
            System.Environment.SetEnvironmentVariable("FSGG_COORD_CACHE", null)

            try
                System.IO.Directory.Delete(dir, true)
            with _ ->
                ()

[<Fact>]
let ``#613 a rolled-up parent is stamped Done AND CLOSED - not one or the other`` () =
    use _sandbox = new Sandbox()

    // The defect: `epic_rollup`'s terminal action was exactly ONE write — the board column. The issue stayed
    // OPEN. So the board and the issue disagreed about the same work (live at FS.GG.Rendering#361: board
    // `Done`, issue OPEN, all four children closed), and the upward climb died at the next hop, because the
    // grandparent then read an OPEN child.
    let transport =
        scripted
            [ ok parentAllDone // the parent's facts
              // The EPIC-UNLINKED-CHILD check re-reads the body + graph (#325): a body declaring no
              // extra children clears it, so the roll-up proceeds.
              // The parent's body. It STATES ACCEPTANCE, and since #1003 it has to: a parent whose body
              // carries no task line has nothing to check against its graph and is refused. This fixture
              // read `"Paths: none"` — written when a body was only ever read for unlinked children — and
              // that is now a body that cannot close. The line delegates to #398, the child in the graph
              // below, which is what a rollup-able parent looks like.
              ok """{"number":350,"body":"- [ ] #398 the only criterion, and it IS a child"}""" // the parent's body
              ok """{"data":{"repository":{"issue":{"subIssues":{"totalCount":1,"nodes":[{"number":398,"state":"CLOSED","repository":{"nameWithOwner":"FS-GG/FS.GG.SDD"}}]}}}}}""" // its graph, with refs
              ok itemOnBoard // boardWrite: resolve the item
              ok """{"data":{"updateProjectV2ItemFieldValue":{"clientMutationId":null}}}""" // the Status write
              ok """{"number":350,"state":"closed"}""" ] // the ISSUE close

    match rollUp transport board "godwit-24dc" parentRef Completes with
    | Ok [ ParentClosed p ] ->
        Assert.Equal(parentRef, p)

        // BOTH. The board column AND the issue state.
        Assert.True(transport.Logged "--single-select-option-id opt_done")
        Assert.True(transport.Logged "issue-patch FS-GG/FS.GG.SDD 350")

    | other -> failwith $"a rolled-up parent must be stamped AND closed — got %A{other}"

[<Fact>]
let ``#325 a parent whose BODY declares an unlinked child is left open, and names it`` () =
    use _sandbox = new Sandbox()

    // "All children resolved" is a claim about the sub-issue GRAPH. #350's graph holds only #398 (closed) —
    // but its BODY declares #399, which the graph does not contain. Closing the parent here would close it
    // over a criterion split out and never linked, so the roll-up must REFUSE and name #399.
    let transport =
        scripted
            [ ok parentAllDone // facts: graph {#398 closed}, AllResolved
              ok """{"number":350,"body":"- [ ] #398 the linked half\n- [ ] #399 the UNLINKED half"}""" // body declares #399
              ok """{"data":{"repository":{"issue":{"subIssues":{"totalCount":1,"nodes":[{"number":398,"state":"CLOSED","repository":{"nameWithOwner":"FS-GG/FS.GG.SDD"}}]}}}}}""" // graph {#398}
              ok """{"number":399,"body":"a plain issue"}""" ] // the PR-probe for #399 -> not a PR -> KEPT

    match rollUp transport board "godwit-24dc" parentRef Completes with
    | Ok [ ParentLeftOpen(p, reasons) ] ->
        Assert.Equal(parentRef, p)
        let joined = String.concat " " reasons
        Assert.Contains("FS.GG.SDD#399", joined)
        Assert.Contains("fsgg-coord child", joined)
        // It must NOT have written the board or closed the issue.
        Assert.False(transport.Logged "--single-select-option-id opt_done")
        Assert.False(transport.Logged "issue-patch FS-GG/FS.GG.SDD 350")
    | other -> failwith $"a body-unlinked parent must be left open, naming the child — got %A{other}"

[<Fact>]
let ``#965 a parent whose body states UN-DELEGATED acceptance is left open, and names the lines`` () =
    use _sandbox = new Sandbox()

    // THE HOLE #561 FELL THROUGH. Every other guard reasons over the sub-issue GRAPH — and a criterion the
    // parent kept for ITSELF is not in the graph to be reasoned about. #561's four children were all closed,
    // its graph was whole, its body declared no unlinked child: every guard was satisfied. Its step 3 was
    // never taken, and closing it laundered that into every ancestor's acceptance.
    //
    // The script carries the facts and the body, AND NOTHING ELSE — see the assertion below.
    let transport =
        scripted
            [ ok parentAllDone // facts: graph {#398 closed}, AllResolved
              ok """{"number":350,"body":"- [ ] #398 the delegated half\n- [ ] step 3: global.json into FILES, tripwire deleted"}""" ]

    match rollUp transport board "godwit-24dc" parentRef Completes with
    | Ok [ ParentLeftOpen(p, reasons) ] ->
        Assert.Equal(parentRef, p)
        let joined = String.concat " " reasons

        // The refusal must NAME the line. "This epic has un-delegated acceptance" sends the reader to find
        // it themselves in a body that is routinely hundreds of lines long.
        Assert.Contains("step 3: global.json into FILES, tripwire deleted", joined)
        Assert.Contains("acceptance IS its children", joined)
        // The DELEGATED line is not a finding — naming it would teach the reader the rule is noise.
        Assert.DoesNotContain("the delegated half", joined)

        // It must not have written the board or closed the issue.
        Assert.False(transport.Logged "--single-select-option-id opt_done")
        Assert.False(transport.Logged "issue-patch FS-GG/FS.GG.SDD 350")

    | other -> failwith $"a parent with un-delegated acceptance must be left open, naming it — got %A{other}"

[<Fact>]
let ``#965 un-delegated acceptance is refused BEFORE the graph read is paid for`` () =
    use _sandbox = new Sandbox()

    // The check is a pure property of the body, so an epic that cannot legally close is refused without
    // spending a request on proving the rest. `scripted` throws once the queue is empty, so a script holding
    // ONLY the facts and the body is itself the assertion: reaching the graph read would fail this test.
    let transport =
        scripted
            [ ok parentAllDone
              ok """{"number":350,"body":"- [ ] an acceptance line delegated to nobody"}""" ]

    match rollUp transport board "godwit-24dc" parentRef Completes with
    | Ok [ ParentLeftOpen _ ] ->
        // EXACTLY the two reads scripted: the facts (GraphQL) and the body (REST). A third would be the
        // graph, and counting is what makes this assertion real — `Logged "subIssues"` would pass whether or
        // not the read happened, since the recorder never spells a query that way. A test that cannot see
        // its own subject is the defect this whole item is about.
        Assert.Equal(1, transport.GraphQlCalls)
        Assert.Equal(1, transport.RestCalls)
    | other -> failwith $"expected a refusal that pays for no graph read — got %A{other}"

[<Fact>]
let ``#965 the rule holds for a parent that is NOT [epic]-titled - which is #561 exactly`` () =
    use _sandbox = new Sandbox()

    // THE TIDY-UP THAT MUST NEVER HAPPEN. `lint` scopes its twin rule to titles carrying `[epic]`, so
    // narrowing this one to match looks like consistency. It would sail straight past the case the guard
    // exists for: #561 is titled `[cross-repo]`, has four children of its own, and is precisely the parent
    // that was closed over a criterion delegated to nobody.
    //
    // "Epic" is a fact about the GRAPH, not the title. `rollUp` never reads a title, and this pins that it
    // must not start: the parent below is titled `[cross-repo]` and is refused all the same.
    let transport =
        scripted
            [ ok parentAllDone
              ok """{"number":350,"title":"[cross-repo] Roll the org SDK pin out to the four unpinned repos","body":"- [ ] #398 the delegated half\n- [ ] step 3: add global.json to FILES, delete the tripwire"}""" ]

    match rollUp transport board "godwit-24dc" parentRef Completes with
    | Ok [ ParentLeftOpen(_, reasons) ] ->
        Assert.Contains("step 3: add global.json to FILES, delete the tripwire", String.concat " " reasons)
    | other -> failwith $"a non-epic PARENT must be refused too — a title is not what makes acceptance rollup-able — got %A{other}"

[<Fact>]
let ``#1003 a parent whose body states NO task-line acceptance is left open - #889's shape`` () =
    use _sandbox = new Sandbox()

    // THE HOLE #965 SHIPPED WITH, AND THE ONE THAT BIT WITHIN THE HOUR. This body is #889's: prose
    // bullets, no checkbox. `undelegatedAcceptance` returns [] — there are no task lines to be ref-less —
    // so #965's guard reported itself satisfied and this parent CLOSED, over a `## The work` section
    // naming three driver skills of which one had never been done.
    //
    // #561, the false closure #965 was written about, has this same shape. A fix that does not catch it
    // has not addressed #965.
    let transport =
        scripted
            [ ok parentAllDone
              ok """{"number":350,"body":"## The work\n\nFold the restatements into generated regions:\n\n- `pnext-item` — the mint ritual\n- `check-board`"}""" ]

    match rollUp transport board "godwit-24dc" parentRef Completes with
    | Ok [ ParentLeftOpen(p, reasons) ] ->
        Assert.Equal(parentRef, p)
        let joined = String.concat " " reasons

        // It must say what it could NOT verify — a silent refusal teaches nothing, and the whole defect
        // here was a guard reporting green on a subject it never read.
        Assert.Contains("states NO task-line acceptance", joined)
        Assert.Contains("#561", joined)

        Assert.False(transport.Logged "--single-select-option-id opt_done")
        Assert.False(transport.Logged "issue-patch FS-GG/FS.GG.SDD 350")

    | other -> failwith $"a parent stating no acceptance must not close — got %A{other}"

[<Fact>]
let ``#1003 the refusal costs no graph read either - it is a property of the body`` () =
    use _sandbox = new Sandbox()

    // Same discipline as #965's guard: `scripted` throws once its queue empties, so a script of exactly
    // the facts and the body IS the assertion, and the call counts make it non-vacuous.
    let transport = scripted [ ok parentAllDone; ok """{"number":350,"body":"just prose, no criteria"}""" ]

    match rollUp transport board "godwit-24dc" parentRef Completes with
    | Ok [ ParentLeftOpen _ ] ->
        Assert.Equal(1, transport.GraphQlCalls)
        Assert.Equal(1, transport.RestCalls)
    | other -> failwith $"expected a refusal paying for no graph read — got %A{other}"

[<Fact>]
let ``#1003 does not fire on a body #965 already governs - the two guards do not double-report`` () =
    use _sandbox = new Sandbox()

    // A ref-less task line STATES acceptance (badly). That is #965's finding and #965 must own it: if
    // #1003's rule also fired, the reader would get two refusals for one defect and the remedies differ.
    let transport =
        scripted
            [ ok parentAllDone
              ok """{"number":350,"body":"- [ ] step 3: global.json into FILES, tripwire deleted"}""" ]

    match rollUp transport board "godwit-24dc" parentRef Completes with
    | Ok [ ParentLeftOpen(_, reasons) ] ->
        let joined = String.concat " " reasons
        Assert.Contains("delegate to NO child", joined) // #965's wording
        Assert.DoesNotContain("states NO task-line acceptance", joined) // ...not #1003's
    | other -> failwith $"expected #965's refusal, not #1003's — got %A{other}"

[<Fact>]
let ``#965 an epic whose every acceptance line is a child ref still rolls up`` () =
    use _sandbox = new Sandbox()

    // THE RULE MUST NOT BREAK THE HAPPY PATH. An epic that already delegates everything is the state the
    // rule drives toward, and it must close exactly as before — otherwise the guard is a wall, not a fence.
    let transport =
        scripted
            [ ok parentAllDone
              ok """{"number":350,"body":"- [ ] #398 the only criterion, and it IS a child"}"""
              ok """{"data":{"repository":{"issue":{"subIssues":{"totalCount":1,"nodes":[{"number":398,"state":"CLOSED","repository":{"nameWithOwner":"FS-GG/FS.GG.SDD"}}]}}}}}"""
              ok itemOnBoard
              ok """{"data":{"updateProjectV2ItemFieldValue":{"clientMutationId":null}}}"""
              ok """{"number":350,"state":"closed"}""" ]

    match rollUp transport board "godwit-24dc" parentRef Completes with
    | Ok [ ParentClosed p ] -> Assert.Equal(parentRef, p)
    | other -> failwith $"a fully-delegated epic must still roll up — got %A{other}"

[<Fact>]
let ``a parent with an OPEN sibling is left open, and says which`` () =
    let openSibling =
        """{"data":{"repository":{"issue":{
            "number":350,"state":"OPEN",
            "closedByPullRequestsReferences":{"nodes":[]},
            "timelineItems":{"nodes":[]},
            "subIssues":{"totalCount":2,"nodes":[{"number":398,"state":"CLOSED"},{"number":401,"state":"OPEN"}]},
            "projectItems":{"nodes":[]},
            "parent":null}}}}"""

    let transport = scripted [ ok openSibling ]

    match rollUp transport board "w" parentRef Completes with
    | Ok [ ParentLeftOpen(_, reasons) ] ->
        Assert.Contains(reasons, fun r -> r.Contains "#401")

        // AND NOTHING WAS WRITTEN. A parent that is not finished must not have its column moved either.
        Assert.False(transport.Logged "issue-patch")
    | other -> failwith $"an open sibling keeps the parent open — got %A{other}"

[<Fact>]
let ``a parent whose child set is TRUNCATED is left open - we could not see them all`` () =
    let truncated =
        """{"data":{"repository":{"issue":{
            "number":350,"state":"OPEN",
            "closedByPullRequestsReferences":{"nodes":[]},
            "timelineItems":{"nodes":[]},
            "subIssues":{"totalCount":120,"nodes":[{"number":398,"state":"CLOSED"}]},
            "projectItems":{"nodes":[]},
            "parent":null}}}}"""

    let transport = scripted [ ok truncated ]

    match rollUp transport board "w" parentRef Completes with
    | Ok [ ParentLeftOpen(_, reasons) ] ->
        Assert.Contains(reasons, fun r -> r.Contains "truncated")
        Assert.False(transport.Logged "issue-patch")
    | other -> failwith $"a parent we could not fully read must not be closed — got %A{other}"

[<Fact>]
let ``a parent reporting NO children is a CONTRADICTION - we climbed to it from one`` () =
    // We reached this parent by following an edge FROM one of its children. A zero-child answer means the
    // read disagrees with the edge we followed — and closing an issue on the strength of a contradiction is
    // exactly the shape of #614.
    let noChildren =
        """{"data":{"repository":{"issue":{
            "number":350,"state":"OPEN",
            "closedByPullRequestsReferences":{"nodes":[]},
            "timelineItems":{"nodes":[]},
            "subIssues":{"totalCount":0,"nodes":[]},
            "projectItems":{"nodes":[]},
            "parent":null}}}}"""

    let transport = scripted [ ok noChildren ]

    match rollUp transport board "w" parentRef Completes with
    | Ok [ ParentLeftOpen(_, reasons) ] -> Assert.Contains(reasons, fun r -> r.Contains "contradiction")
    | other -> failwith $"a contradiction must not close an issue — got %A{other}"

[<Fact>]
let ``a rate-limited roll-up PROPAGATES - it does not silently leave the parent open`` () =
    // "I could not read the parent" and "the parent is not finished" are different facts. Reporting the
    // second when the first is true would leave a genuinely-complete epic open forever, and nobody would
    // ever know why.
    let transport = Fake.Recorder(fun _ -> Error(RateLimited(UnknownBudget, None)))

    match rollUp transport board "w" parentRef Completes with
    | Error(RateLimited _) -> ()
    | other -> failwith $"an exhausted budget must not read as an unfinished parent — got %A{other}"
