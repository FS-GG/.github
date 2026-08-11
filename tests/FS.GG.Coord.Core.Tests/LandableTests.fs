namespace FS.GG.Coord.Tests

open Xunit
open FS.GG.Coord
open FS.GG.Coord.Types
open FS.GG.Coord.Landable

/// IS AN OPEN PR FINISHED WORK? (#697/#720) — the scorer, unit-tested without a fixture server.
///
/// `Reads.prLandable` does the three reads; this module holds `Landable.score` to the corpus's certified
/// verdicts. Every test is a state in which the WRONG answer points a destructive verb at finished work, or
/// lands work that is not finished:
///   green ≠ conflicted ≠ pending ≠ red · zero-checks is RED not GREEN (#606) · a superseded run is NOT a
///   finding, whatever it concluded (#720/#1039) · a null mergeable is UNKNOWN, never mergeable.
module LandableTests =

    let private run path event branch prs num status concl suite : RunRow =
        { Path = path
          Event = event
          HeadBranch = branch
          PrNumbers = prs
          RunNumber = num
          Status = status
          Conclusion = concl
          CheckSuiteId = suite }

    let private check suite status concl : CheckRow =
        { Name = "job"
          CheckSuiteId = suite
          Status = status
          Conclusion = concl }

    /// `check`, with the check-run NAME that `--require` matches on (#737).
    let private named name suite status concl : CheckRow =
        { check suite status concl with Name = name }

    /// A single green Actions run, no third-party checks.
    let private greenRun =
        run ".github/workflows/build.yml" "pull_request" "item/970-x" [ 970 ] 1 "completed" (Some "success") (Some 1L)

    // ---- mergeability, before anything else ----------------------------------------------------------

    [<Fact>]
    let ``a null mergeable is UNKNOWN — never mergeable, never conflicted (#697)`` () =
        Assert.Equal(PrUnknown, score None [ greenRun ] [])

    [<Fact>]
    let ``mergeable=false is CONFLICTED, and the checks are not even consulted`` () =
        // A conflicted PR gets no CI at all, so its check set is empty forever — the verdict comes from
        // mergeability, before the checks, or an empty set would read as red and mask the real conflict.
        Assert.Equal(PrConflicted, score (Some false) [] [])

    // ---- the rollup, once mergeable ------------------------------------------------------------------

    [<Fact>]
    let ``mergeable + every live check passed (and there is one) is GREEN`` () =
        Assert.Equal(PrGreen, score (Some true) [ greenRun ] [ check (Some 1L) "completed" (Some "success") ])

    [<Fact>]
    let ``mergeable + ZERO checks is RED, not green (#606 — a missing subject is a finding)`` () =
        Assert.Equal(PrRed, score (Some true) [] [])

    [<Fact>]
    let ``mergeable + a check still running is PENDING — not green YET is not not-green`` () =
        let pending = run ".github/workflows/test.yml" "pull_request" "item/976-x" [ 976 ] 1 "in_progress" None (Some 2L)
        Assert.Equal(PrPending, score (Some true) [ greenRun; pending ] [])

    [<Fact>]
    let ``mergeable + a completed check that FAILED is RED`` () =
        let failed = run ".github/workflows/test.yml" "pull_request" "item/x" [ 1 ] 1 "completed" (Some "failure") (Some 2L)
        Assert.Equal(PrRed, score (Some true) [ greenRun; failed ] [])

    [<Fact>]
    let ``a skipped conclusion counts as passed, not as a finding`` () =
        let skipped = run ".github/workflows/lint.yml" "pull_request" "item/x" [ 1 ] 1 "completed" (Some "skipped") (Some 2L)
        Assert.Equal(PrGreen, score (Some true) [ greenRun; skipped ] [])

    // ---- a NON-Actions app appears only in the check-runs, and is scored by construction (#720) -------

    [<Fact>]
    let ``a third-party check-run's failure is scored even though it is in no workflow run`` () =
        // The foreign app's suite (99) is never in the runs list, so it is never dropped as superseded.
        let foreign = check (Some 99L) "completed" (Some "failure")
        Assert.Equal(PrRed, score (Some true) [ greenRun ] [ foreign ])

    // ---- supersession (#720) -------------------------------------------------------------------------

    [<Fact>]
    let ``a CANCELLED run replaced by a later run of its own group is superseded, and the PR stays GREEN`` () =
        // run 1 cancelled, run 2 of the SAME concurrency group succeeded — a force-push during CI. Raw
        // aggregation would see `cancelled` and call a green PR red; supersession drops it.
        let g = ".github/workflows/build.yml", "pull_request", "item/718-x", [ 718 ]
        let path, ev, br, prs = g
        let cancelled = run path ev br prs 1 "completed" (Some "cancelled") (Some 10L)
        let later = run path ev br prs 2 "completed" (Some "success") (Some 11L)
        Assert.Equal(PrGreen, score (Some true) [ cancelled; later ] [])

    [<Fact>]
    let ``a superseded run's CHECK-RUNS are dropped with it (its dead suite is not scored)`` () =
        let g = ".github/workflows/build.yml", "pull_request", "item/718-x", [ 718 ]
        let path, ev, br, prs = g
        let cancelled = run path ev br prs 1 "completed" (Some "cancelled") (Some 10L)
        let later = run path ev br prs 2 "completed" (Some "success") (Some 11L)
        // The cancelled run's check-run (suite 10) would be `failure` — but it belongs to the dead suite,
        // so it is dropped; only the live suite (11) is scored, and the PR is green.
        let deadCheck = check (Some 10L) "completed" (Some "failure")
        let liveCheck = check (Some 11L) "completed" (Some "success")
        Assert.Equal(PrGreen, score (Some true) [ cancelled; later ] [ deadCheck; liveCheck ])

    [<Fact>]
    let ``a cancelled run NOBODY re-ran stays live — it is still a finding (RED), not dropped`` () =
        let lone = run ".github/workflows/build.yml" "pull_request" "item/x" [ 1 ] 1 "completed" (Some "cancelled") (Some 10L)
        Assert.Equal(PrRed, score (Some true) [ lone ] [])

    [<Fact>]
    let ``a higher run of a DIFFERENT group does not supersede — the cancelled run is still a finding (#703)`` () =
        // Same path/branch/PR but a different EVENT (workflow_dispatch) is a different concurrency group, so
        // it supersedes nothing — dropping the cancelled pull_request run in its favour would count a
        // vacuous green.
        let cancelled = run ".github/workflows/build.yml" "pull_request" "item/x" [ 1 ] 1 "completed" (Some "cancelled") (Some 10L)
        let dispatch = run ".github/workflows/build.yml" "workflow_dispatch" "item/x" [ 1 ] 2 "completed" (Some "success") (Some 11L)
        Assert.Equal(PrRed, score (Some true) [ cancelled; dispatch ] [])

    // ---- a superseded FAILURE is superseded too (#1039 / ADR-0043) ------------------------------------
    //
    // The rule above was once `cancelled`-only, to stop a failure being laundered by re-running it until it
    // passed. That attack needs a re-run to CREATE a run, and it does not: it adds an attempt to the same
    // row, whose Conclusion then reads the latest attempt (#721). So these two runs are never a re-run —
    // every run scored is on ONE head SHA, and a `synchronize` changes the SHA. A second run of a group on
    // one SHA is a metadata RE-EVALUATION, and the failure it replaced is stale by construction.

    [<Fact>]
    let ``a FAILED run replaced by a later run of its own group is superseded, and the PR is GREEN (#1039)`` () =
        // PR #1036, head 752e95c, measured: architecture-map run 938 `failure` (it read the PRE-edit body out
        // of the event payload), then run 940 `success` after the opt-out line the gate itself told the
        // worker to add. Same path, same event, same branch. Nothing cancelled 938 — it had COMPLETED, and
        // architecture-map declares no `concurrency` block — so the `cancelled`-only rule kept it and scored
        // the PR `red` permanently, while GitHub called it `clean` and the merge was correct.
        let g = ".github/workflows/architecture-map.yml", "pull_request", "item/1026-chore-lock-embed", [ 1026 ]
        let path, ev, br, prs = g
        let failed = run path ev br prs 938 "completed" (Some "failure") (Some 10L)
        let later = run path ev br prs 940 "completed" (Some "success") (Some 11L)
        Assert.Equal(PrGreen, score (Some true) [ failed; later ] [])

    [<Fact>]
    let ``a superseded FAILURE's check-runs are dropped with its suite, exactly as a cancelled one's are`` () =
        let g = ".github/workflows/architecture-map.yml", "pull_request", "item/1026-x", [ 1026 ]
        let path, ev, br, prs = g
        let failed = run path ev br prs 938 "completed" (Some "failure") (Some 10L)
        let later = run path ev br prs 940 "completed" (Some "success") (Some 11L)
        // `reconcile` is architecture-map's job and a REQUIRED check: its failing copy belongs to the dead
        // suite, so it must not be scored — otherwise the run is dropped and its check reds the PR anyway.
        let deadCheck = named "reconcile" (Some 10L) "completed" (Some "failure")
        let liveCheck = named "reconcile" (Some 11L) "completed" (Some "success")
        Assert.Equal(PrGreen, score (Some true) [ failed; later ] [ deadCheck; liveCheck ])

    [<Fact>]
    let ``a FAILED run NOBODY replaced stays live — dropping a failure needs a REPLACEMENT, not a mood`` () =
        // The fail-closed half: supersession is a fact about a later run of the group, so a lone failure is
        // the latest in its own group and is scored. A red PR cannot go green by having no successor.
        let lone = run ".github/workflows/build.yml" "pull_request" "item/x" [ 1 ] 1 "completed" (Some "failure") (Some 10L)
        Assert.Equal(PrRed, score (Some true) [ lone ] [])

    [<Fact>]
    let ``a higher run of a DIFFERENT group does not supersede a FAILURE either (#703 holds for failures)`` () =
        // #703's hole, aimed at the case this change opens up. `workflow_dispatch` shares the path, the
        // branch and the SHA and carries a HIGHER run number, but it is a different `github.ref` — so it
        // supersedes nothing. A gate job that `if: github.event_name == 'pull_request'` SKIPS in the dispatch
        // run and concludes `success`; letting it drop the failed pull_request run would count a vacuous
        // green and merge a PR whose gate never passed. `cgroup` — not the conclusion — is what forbids it.
        let failed = run ".github/workflows/build.yml" "pull_request" "item/x" [ 1 ] 1 "completed" (Some "failure") (Some 10L)
        let dispatch = run ".github/workflows/build.yml" "workflow_dispatch" "item/x" [ 1 ] 2 "completed" (Some "success") (Some 11L)
        Assert.Equal(PrRed, score (Some true) [ failed; dispatch ] [])

    [<Fact>]
    let ``a superseded run STILL RUNNING is dropped too — a group's verdict is its LATEST run's`` () =
        // The `Status` leaves the test along with the `Conclusion`. Under the cancelled-only rule an
        // in-progress run was never `cancelled`, so it stayed live and held the PR at `pending` until it
        // finished. A workflow with no `concurrency` block (architecture-map declares none) never cancels its
        // predecessor, so that wait was for a run whose verdict was stale before it started: it is scoring an
        // OLDER read of the same SHA's metadata. Its successor has already answered.
        let g = ".github/workflows/architecture-map.yml", "pull_request", "item/x", [ 1 ]
        let path, ev, br, prs = g
        let stillGoing = run path ev br prs 938 "in_progress" None (Some 10L)
        let later = run path ev br prs 940 "completed" (Some "success") (Some 11L)
        Assert.Equal(PrGreen, score (Some true) [ stillGoing; later ] [])

    [<Fact>]
    let ``supersession keys on the RUN NUMBER, not on list order — a later run listed FIRST still wins`` () =
        // The runs API returns newest-first, so the superseding run arrives BEFORE the one it replaces. A
        // rule that trusted position rather than RunNumber would score this backwards and red a green PR.
        let g = ".github/workflows/architecture-map.yml", "pull_request", "item/x", [ 1 ]
        let path, ev, br, prs = g
        let later = run path ev br prs 940 "completed" (Some "success") (Some 11L)
        let failed = run path ev br prs 938 "completed" (Some "failure") (Some 10L)
        Assert.Equal(PrGreen, score (Some true) [ later; failed ] [])

    // ---- scoreN: the verdict AND the count `--wait` polls on (#724) ----------------------------------

    [<Fact>]
    let ``scoreN returns the verdict AND the number of LIVE subjects it scored`` () =
        // One live run + one live check = 2 subjects, and the verdict agrees with `score`.
        let state, n = scoreN (Some true) [ greenRun ] [ check (Some 1L) "completed" (Some "success") ]
        Assert.Equal(PrGreen, state)
        Assert.Equal(2, n)

    [<Fact>]
    let ``scoreN counts ZERO subjects for the empty set (#606) — the registration-race red`` () =
        // Zero runs, zero checks: the verdict is RED, but the count is what tells --wait this is "CI has not
        // started" rather than "CI failed".
        Assert.Equal((PrRed, 0), scoreN (Some true) [] [])

    [<Fact>]
    let ``scoreN drops a superseded suite from the count, not just the verdict`` () =
        // The cancelled run and its check-run are superseded, so only the live run + live check are counted.
        let cancelled = run ".github/workflows/build.yml" "pull_request" "item/718-x" [ 718 ] 1 "completed" (Some "cancelled") (Some 10L)
        let later = run ".github/workflows/build.yml" "pull_request" "item/718-x" [ 718 ] 2 "completed" (Some "success") (Some 11L)
        let deadCheck = check (Some 10L) "completed" (Some "failure")
        let liveCheck = check (Some 11L) "completed" (Some "success")
        let state, n = scoreN (Some true) [ cancelled; later ] [ deadCheck; liveCheck ]
        Assert.Equal(PrGreen, state)
        // live run (11) + live check (11) = 2; the dead suite's run and check are both dropped.
        Assert.Equal(2, n)

    [<Fact>]
    let ``scoreN counts 0 for conflicted and unknown — the verdict precedes any subject`` () =
        Assert.Equal((PrConflicted, 0), scoreN (Some false) [ greenRun ] [])
        Assert.Equal((PrUnknown, 0), scoreN None [ greenRun ] [])

    [<Fact>]
    let ``two check-runs SHARE a job name and one FAILS — the failure still reds it (#698, the open trap)`` () =
        // "Latest check run per NAME" is what branch protection does, and here it FAILS OPEN: check-run
        // `.name` is the JOB name and job names COLLIDE ACROSS WORKFLOWS — measured on `.github`, seven
        // runs named `fixture` from six workflows. Collapse by name and a genuinely FAILING `fixture`
        // (pin-coherence) is hidden by another workflow's passing `fixture` (timeout-coherence): "all
        // green", merge, red check landed.
        //
        // The scorer cannot do this — it keys supersession on the concurrency GROUP and joins check-runs
        // by SUITE ID, never by name. This test exists because #737 gave `CheckRow` a `Name` for
        // `scoreRequired` to match on, which makes the trap REACHABLE for the first time: the field is
        // fit for a presence test and for nothing else. Nothing is cancelled here, so nothing may be
        // dropped — both `fixture`s are live verdicts.
        let pin = run ".github/workflows/pin-coherence.yml" "pull_request" "b" [ 595 ] 30 "completed" (Some "success") (Some 7L)
        let timeout = run ".github/workflows/timeout-coherence.yml" "pull_request" "b" [ 595 ] 31 "completed" (Some "success") (Some 8L)
        let redFixture = named "fixture" (Some 7L) "completed" (Some "failure")
        let greenFixture = named "fixture" (Some 8L) "completed" (Some "success")
        Assert.Equal(PrRed, score (Some true) [ pin; timeout ] [ redFixture; greenFixture ])

    [<Fact>]
    let ``--require is satisfied by a check of that name even when another shares it and FAILS`` () =
        // The other half of the same rule: `--require` is a PRESENCE test, so a name collision cannot make
        // it unsatisfiable — but it also cannot launder the red. The verdict is still RED, from the rollup.
        let a = run ".github/workflows/a.yml" "pull_request" "b" [ 1 ] 1 "completed" (Some "success") (Some 7L)
        let b = run ".github/workflows/b.yml" "pull_request" "b" [ 1 ] 1 "completed" (Some "success") (Some 8L)
        let red = named "registry-coherence" (Some 7L) "completed" (Some "failure")
        let green = named "registry-coherence" (Some 8L) "completed" (Some "success")
        let state, _ = scoreRequired [ "registry-coherence" ] (Some true) [ a; b ] [ red; green ]
        Assert.Equal(PrRed, state)
        Assert.Empty(missing [ "registry-coherence" ] [ a; b ] [ red; green ])

    // ---- scoreRequired: a check that must have REPORTED, by name (#737) ------------------------------
    //
    // The rollup above answers "is anything red?", and that question CANNOT see a check that is absent —
    // an absent subject reads exactly like a passing one (#606). Branch protection covers the REQUIRED
    // set, so what is asserted here is a NON-required check that nonetheless decides the PR: the autofix
    // bot's `registry-coherence`, whose redness means "this snapshot is OBSOLETE" and which GitHub's own
    // auto-merge would merge straight past (#642/#425). These are the legs that let that bot stop
    // hand-rolling the gate.

    [<Fact>]
    let ``scoreRequired: the required check reported and is green — so is the PR`` () =
        let checks = [ named "registry-coherence" (Some 1L) "completed" (Some "success") ]
        Assert.Equal((PrGreen, 2), scoreRequired [ "registry-coherence" ] (Some true) [ greenRun ] checks)

    [<Fact>]
    let ``scoreRequired: an ABSENT required check is PENDING, never green — the #606 hole, closed`` () =
        // Everything present is green, and without --require this is a GREEN that merges. The required
        // check never reported, so the thing it was to verify was never verified: not green.
        let checks = [ named "some-other-job" (Some 1L) "completed" (Some "success") ]
        let state, _ = scoreRequired [ "registry-coherence" ] (Some true) [ greenRun ] checks
        Assert.Equal(PrPending, state)
        // ...and without the requirement, the very same input IS green. That contrast is the whole point.
        Assert.Equal(PrGreen, score (Some true) [ greenRun ] checks)

    [<Fact>]
    let ``scoreRequired: an absent required check is PENDING, not RED — so --wait rides out the race`` () =
        // Deliberate, and the subtle call. `pending` never settles, so --wait keeps polling: a check that
        // has not REGISTERED yet, and a required check whose suite was just SUPERSEDED (the state a bot
        // that force-pushes manufactures on every run, #710), both resolve on a later poll. Calling it RED
        // would settle at once and refuse the PR the bot had just pushed. It cannot fail OPEN: the one
        // thing `pending` is not, is green.
        let state, _ = scoreRequired [ "registry-coherence" ] (Some true) [ greenRun ] []
        Assert.NotEqual(PrRed, state)
        Assert.NotEqual(PrGreen, state)

    [<Fact>]
    let ``scoreRequired: a RED check outranks a missing required one — a finding is reported at once`` () =
        // Ordering matters: `red` settles immediately, `pending` polls for the whole budget. Reporting the
        // softer verdict over a hard red would make --wait spin out its tries before announcing a failure
        // it already knew.
        let checks = [ named "build" (Some 1L) "completed" (Some "failure") ]
        let state, _ = scoreRequired [ "registry-coherence" ] (Some true) [ greenRun ] checks
        Assert.Equal(PrRed, state)

    [<Fact>]
    let ``scoreRequired: a SUPERSEDED copy of the required check does NOT satisfy it (#710)`` () =
        // The cancelled run's `registry-coherence` is dropped with its suite — and it is precisely the
        // check whose verdict we do not have. The replacement has not registered, so: pending, and the
        // next poll sees it. Satisfying the requirement from a dropped check would merge on a verdict
        // that was cancelled before it could be reached.
        let cancelled = run ".github/workflows/registry.yml" "pull_request" "auto/registry" [ 9 ] 1 "completed" (Some "cancelled") (Some 10L)
        let later = run ".github/workflows/registry.yml" "pull_request" "auto/registry" [ 9 ] 2 "completed" (Some "success") (Some 11L)
        let deadCheck = named "registry-coherence" (Some 10L) "completed" (Some "cancelled")
        let state, _ = scoreRequired [ "registry-coherence" ] (Some true) [ cancelled; later ] [ deadCheck ]
        Assert.Equal(PrPending, state)

    [<Fact>]
    let ``scoreRequired: EVERY named check must report — a set is not its first element`` () =
        // The parse appends rather than last-wins; this is the scoring half of the same rule.
        let checks = [ named "a" (Some 1L) "completed" (Some "success") ]
        let state, _ = scoreRequired [ "a"; "b" ] (Some true) [ greenRun ] checks
        Assert.Equal(PrPending, state)

    [<Fact>]
    let ``scoreRequired with an empty require-list IS scoreN — the default cannot change behaviour`` () =
        let checks = [ named "build" (Some 1L) "completed" (Some "success") ]
        Assert.Equal(scoreN (Some true) [ greenRun ] checks, scoreRequired [] (Some true) [ greenRun ] checks)

    [<Fact>]
    let ``scoreRequired: a required check is not consulted before mergeability`` () =
        // A conflicted PR gets no CI at all, so every required check is absent — by construction, not by
        // fault. Reporting that as "pending on registry-coherence" would send the reader hunting a check
        // that was never going to run; the conflict is the finding.
        Assert.Equal((PrConflicted, 0), scoreRequired [ "registry-coherence" ] (Some false) [] [])
        Assert.Equal((PrUnknown, 0), scoreRequired [ "registry-coherence" ] None [] [])

    [<Fact>]
    let ``missing: names the required checks that did not report, superseded copies not counted`` () =
        let cancelled = run ".github/workflows/registry.yml" "pull_request" "auto/registry" [ 9 ] 1 "completed" (Some "cancelled") (Some 10L)
        let later = run ".github/workflows/registry.yml" "pull_request" "auto/registry" [ 9 ] 2 "completed" (Some "success") (Some 11L)
        let dead = named "registry-coherence" (Some 10L) "completed" (Some "cancelled")
        let live = named "build" (Some 11L) "completed" (Some "success")
        Assert.Equal<string list>([ "registry-coherence" ], missing [ "registry-coherence"; "build" ] [ cancelled; later ] [ dead; live ])

    [<Fact>]
    let ``missing: nothing is missing when every required check reported`` () =
        let checks = [ named "registry-coherence" (Some 1L) "completed" (Some "success") ]
        Assert.Empty(missing [ "registry-coherence" ] [ greenRun ] checks)

    // ---- advisory checks: a named check's failure does not gate the verdict (#2373) ------------------
    //
    // `claim-generation`'s own design doc (`.github#2342` AC6) says a red verdict from that job is
    // "observed, not enforced" — but `scoreRequired` used to score EVERY live check unconditionally, so
    // its FINDING conclusion turned `landable` red anyway. Reproduced live across three PRs in one wave
    // (`.github#2373`'s host-authored comment): the PR without a hand-added `fsgg:pr-authorization`
    // marker read `landable` RED; an otherwise-identical PR with one read GREEN. These tests hold the
    // fix to that corpus.

    [<Fact>]
    let ``a FAILED claim-generation check does not red the verdict — it is advisory, not required (#2373)`` () =
        let checks = [ named "claim-generation" (Some 2L) "completed" (Some "failure") ]
        Assert.Equal(PrGreen, score (Some true) [ greenRun ] checks)

    [<Fact>]
    let ``an advisory check's failure does not count toward the subject total either`` () =
        let checks = [ named "claim-generation" (Some 2L) "completed" (Some "failure") ]
        // Only the one live run is counted; the failing advisory check contributes nothing to `n`.
        Assert.Equal((PrGreen, 1), scoreN (Some true) [ greenRun ] checks)

    [<Fact>]
    let ``an advisory check STILL RUNNING does not hold the verdict at pending either`` () =
        let checks = [ named "claim-generation" (Some 2L) "in_progress" None ]
        Assert.Equal(PrGreen, score (Some true) [ greenRun ] checks)

    [<Fact>]
    let ``a REAL required check's failure is unaffected by the advisory carve-out — it still reds`` () =
        // The carve-out is a NAMED, narrow set — not "ignore anything non-required". A genuinely failing,
        // ordinary (non-advisory) check must still red the PR exactly as before.
        let checks =
            [ named "claim-generation" (Some 2L) "completed" (Some "failure")
              named "build" (Some 3L) "completed" (Some "failure") ]
        Assert.Equal(PrRed, score (Some true) [ greenRun ] checks)

    [<Fact>]
    let ``a caller that explicitly --requires claim-generation opts back into scoring its failure`` () =
        // The one lever that re-arms the exemption for a single caller: name it in `required`. A missing
        // OR failing named check both stop being invisible to the rollup once asked for by name.
        let checks = [ named "claim-generation" (Some 2L) "completed" (Some "failure") ]
        let state, _ = scoreRequired [ "claim-generation" ] (Some true) [ greenRun ] checks
        Assert.Equal(PrRed, state)

    [<Fact>]
    let ``--require claim-generation still treats its ABSENCE as pending, not as invisible`` () =
        let state, _ = scoreRequired [ "claim-generation" ] (Some true) [ greenRun ] []
        Assert.NotEqual(PrGreen, state)
        Assert.NotEqual(PrRed, state)
        Assert.Equal<string list>([ "claim-generation" ], missing [ "claim-generation" ] [ greenRun ] [])

    [<Fact>]
    let ``an advisory check that PASSES is unaffected — green stays green either way`` () =
        let checks = [ named "claim-generation" (Some 2L) "completed" (Some "success") ]
        Assert.Equal(PrGreen, score (Some true) [ greenRun ] checks)

    [<Fact>]
    let ``a check that only SHARES A PREFIX with an advisory name is still scored, and still reds (#2373 repair 1)`` () =
        // The exemption is a NAMED, EXACT set — not a prefix/substring match. `claim-generation-v2` is a
        // DIFFERENT check that merely happens to share a prefix with the one advisory entry
        // (`claim-generation`); nothing about this fix's own reasoning extends the "observed, not
        // enforced" promise to it, and it must still gate the merge like any other ordinary check.
        //
        // This is the boundary the independent critic's own mutation found undefended: broadening
        // `advisoryCheckNames.Contains c.Name` (exact) to
        // `advisoryCheckNames |> Set.exists (fun a -> c.Name.StartsWith(a: string))` (prefix) left the
        // whole 729-test suite green, because no existing case used a name that shares a prefix with
        // `"claim-generation"` without BEING it. This test is written to fail under that broadening and
        // pass under the current exact match — both directions demonstrated below.
        let checks = [ named "claim-generation-v2" (Some 2L) "completed" (Some "failure") ]
        Assert.Equal(PrRed, score (Some true) [ greenRun ] checks)

    [<Fact>]
    let ``a check whose name merely CONTAINS an advisory name as a substring is still scored, and still reds`` () =
        // The substring-broadening sibling of the prefix case above: `advisoryCheckNames |> Set.exists
        // (fun a -> c.Name.Contains(a: string))` would ALSO match a name like
        // `pre-claim-generation-suffix`, which shares no prefix with `claim-generation` but does contain
        // it. Held apart from the prefix test so a repair that fixes one broadening but not the other is
        // still caught.
        let checks = [ named "pre-claim-generation-suffix" (Some 2L) "completed" (Some "failure") ]
        Assert.Equal(PrRed, score (Some true) [ greenRun ] checks)

    // ---- settled: the --wait break-vs-keep-waiting decision (#724) -----------------------------------

    [<Fact>]
    let ``settled: conflicted stops at once — no amount of waiting fixes a conflict`` () =
        // n and prev are irrelevant; a conflict has no CI to grow.
        Assert.True(settled PrConflicted 0 -1)

    [<Fact>]
    let ``settled: unknown stops at once — the fail-closed answer does not clear by waiting`` () =
        Assert.True(settled PrUnknown 0 -1)

    [<Fact>]
    let ``settled: a red over ZERO subjects KEEPS WAITING — it is the registration race, not a failure`` () =
        // The trap that rejects every PR for being new: N==0 red is "CI has not started YET".
        Assert.False(settled PrRed 0 -1)

    [<Fact>]
    let ``settled: a red over some subjects STOPS — a real finding does not clear by waiting`` () =
        Assert.True(settled PrRed 3 3)

    [<Fact>]
    let ``settled: a green whose count is STILL GROWING keeps waiting — the partial-rollup trap`` () =
        // First observation (prev = -1): the count has not been confirmed stable, so an early all-green is
        // not believed. This is the leg that would otherwise merge a PR whose failing check had not been
        // created yet.
        Assert.False(settled PrGreen 1 -1)
        // Grew between polls (prev 1, now 2): still not stable.
        Assert.False(settled PrGreen 2 1)

    [<Fact>]
    let ``settled: a green whose count has STOPPED GROWING stops — believed only when stable`` () =
        Assert.True(settled PrGreen 2 2)

    [<Fact>]
    let ``settled: a green over zero subjects never stops — there is nothing to be green about`` () =
        // Defensive: score never emits PrGreen at n=0 (that is #606's PrRed), but settled must not either.
        Assert.False(settled PrGreen 0 0)

    [<Fact>]
    let ``settled: pending never stops — a run still going is the one verdict worth waiting on`` () =
        Assert.False(settled PrPending 2 2)

    // ---- settled: a PR that is NOT OPEN (#1680) ------------------------------------------------------
    //
    // THE MEASURED COST THIS DRIVES TO ZERO. `landable 1675 --wait` — on a PR merged as `d52362c`, whose
    // head carried 30 check-runs, 30 `success`, zero pending — spent its ENTIRE default budget (30 tries x
    // 20s = 600s) and then answered `pending`. `settled` is the only thing `Client.landable`'s poll loop
    // consults to decide break-vs-wait, so these two assertions ARE AC3: with them true, the loop cannot
    // reach a second poll on a closed PR.

    [<Fact>]
    let ``#1680 AC3 settled: a MERGED pr stops at once — --wait must never poll a settled fact`` () =
        // `prev = -1` is the FIRST observation, which is the leg that matters: the loop must break before
        // it ever sleeps. Asserting only the stable-count form would let a 20s sleep survive the test.
        Assert.True(settled PrMerged 0 -1)
        // And it does not depend on the subject count. A merged PR has no live check set, so `n` is 0 —
        // exactly the count at which `PrRed` and `PrGreen` deliberately KEEP waiting. If merged-ness were
        // ever routed through a count-sensitive arm it would inherit that wait, which is the bug again.
        Assert.True(settled PrMerged 0 0)
        Assert.True(settled PrMerged 30 30)

    [<Fact>]
    let ``#1680 AC4 settled: a CLOSED-unmerged pr stops at once too — the neighbouring terminal case`` () =
        Assert.True(settled PrClosed 0 -1)
        Assert.True(settled PrClosed 0 0)

    [<Fact>]
    let ``#1680 the two not-open verdicts are the ONLY new ones that settle at n=0 on first look`` () =
        // The guard that keeps this fix from being written as "settle everything at zero subjects", which
        // would silently disarm #606's registration race (a `red` at n=0 is "CI has not started YET") and
        // #724's partial-rollup trap. Both must still keep waiting on the first observation.
        Assert.False(settled PrRed 0 -1)
        Assert.False(settled PrGreen 0 -1)
        Assert.False(settled PrPending 0 -1)

    // ---- the name projection -------------------------------------------------------------------------

    [<Fact>]
    let ``name is the one-word verdict the corpus certifies`` () =
        Assert.Equal("green", name PrGreen)
        Assert.Equal("conflicted", name PrConflicted)
        Assert.Equal("pending", name PrPending)
        Assert.Equal("red", name PrRed)
        Assert.Equal("unknown", name PrUnknown)
        // #1680 AC2: the caller must be able to tell "already landed" from "checks still running" from the
        // verdict ALONE, with no second REST read. These are the words that carry that.
        Assert.Equal("merged", name PrMerged)
        Assert.Equal("closed", name PrClosed)

    [<Fact>]
    let ``#1680 AC2 every verdict has a DISTINCT word — merged must not render as pending`` () =
        // The issue's complaint in one assertion: `pending` meant BOTH "checks are still running" AND
        // "this PR is merged and gone", and "the caller cannot tell which, because the two render
        // identically". A projection that collapsed any two states would restore exactly that.
        let all = [ PrGreen; PrConflicted; PrPending; PrRed; PrUnknown; PrMerged; PrClosed ]
        let words = all |> List.map name

        Assert.Equal<string list>(words |> List.distinct, words)
        Assert.NotEqual<string>(name PrPending, name PrMerged)
        Assert.NotEqual<string>(name PrMerged, name PrClosed)
