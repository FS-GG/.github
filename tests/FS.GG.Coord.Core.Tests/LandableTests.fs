namespace FS.GG.Coord.Tests

open System.IO
open System.Text.RegularExpressions
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
    let ``#2906 red provenance names every blocking run and check but excludes advisory failures`` () =
        let blockingRun =
            run ".github/workflows/coherence.yml" "pull_request" "item/2906-x" [ 2906 ] 17 "completed" (Some "failure") (Some 17L)

        let unrelated = named "external-safety" (Some 99L) "completed" (Some "timed_out")
        let itemOwned = named "claim-generation" (Some 17L) "completed" (Some "failure")
        let advisory = named "feed" (Some 18L) "completed" (Some "failure")
        let fromMain = advisoryFrom [ "claim-generation"; "external-safety" ]

        Assert.Equal<Failure list>(
            [ WorkflowRunFailure(".github/workflows/coherence.yml", 17, Some "failure")
              CheckRunFailure("external-safety", Some 99L, Some "timed_out")
              CheckRunFailure("claim-generation", Some 17L, Some "failure") ],
            failuresDerived fromMain [] [ greenRun; blockingRun ] [ unrelated; itemOwned; advisory ]
        )

        // Counterweight: the registration-race `red` has no failed identity to invent.
        Assert.Empty(failuresDerived fromMain [] [] [])

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

    // ---- the advisory carve-out is DERIVED from branch protection (`.github#2517`) --------------------
    //
    // WHAT MOVED, AND WHAT DID NOT. `claim-generation`'s own design doc (`.github#2342` AC6) says a red
    // verdict from that job is "observed, not enforced", and `scoreRequired` scoring every live check
    // unconditionally enforced it anyway (`.github#2373`, reproduced live across three PRs in one wave).
    // That repair was right; its INPUT was a hand-written `advisoryCheckNames = Set.ofList
    // [ "claim-generation" ]` with exactly one entry, so every OTHER non-required check still gated the
    // merge. Measured on `.github` PR #2514 at `f1d6218d775d278429cf6cea252b7d617ee3c723`: all six required
    // contexts passing, the non-required `feed` arm failing, `mergeable_state` `unstable` (GitHub itself
    // would merge) — and `landable` `red`, refusing a reviewed, host-accepted PR by the org's own protocol.
    // `.github#2517` replaced the literal with the branch's own `required_status_checks.contexts`, of which
    // "advisory" is exactly the complement. Every RULE #2373/#2379/#2400/#2454 established still holds
    // below; only the input to `checkGating` moved.
    //
    // THE ADVISORY SUBJECT IS NOW `feed`, not `claim-generation`, and that substitution is itself a
    // measurement rather than a convenience: `claim-generation` IS in `.github`'s required contexts today,
    // so the derivation — correctly — scores it `Blocking` again. That direction has its own test below. It
    // is the branch-protection arming `.github#2342` §9.1 named as this carve-out's exit condition ("do it
    // in the SAME change that adds the context to `branches/main/protection/required_status_checks`, so the
    // two subsystems move together"), and deriving the set is what made it happen with no source edit.

    /// The required contexts `.github`'s `main` actually declares.
    ///
    /// *Verification:* `gh api repos/FS-GG/.github/branches/main/protection --jq
    /// '.required_status_checks.contexts'` on 2026-08-13 returned exactly this list, and
    /// `gh api repos/FS-GG/.github/rules/branches/main` carries no `required_status_checks` rule, so the
    /// union `Reads.requiredContexts` forms over the two stores is this list alone.
    let private mainContexts =
        [ "contract-coherence / coherence"
          "projection"
          "roster-closure"
          "drift"
          "reconcile"
          "claim-generation" ]

    /// The derivation branch protection produces on `.github` today.
    let private fromMain = advisoryFrom mainContexts

    /// The `feed` workflow's run — the real non-required arm `.github#2517` measured failing on PR #2514
    /// while every required context passed. It is in no required set used here, so the derivation makes it
    /// and its check-run advisory. Suite `5L`, deliberately distinct from `greenRun`'s `1L`.
    let private feedRun =
        run ".github/workflows/feed.yml" "pull_request" "item/x" [ 1 ] 5 "completed" (Some "failure") (Some 5L)

    [<Fact>]
    let ``#2517 AC3: a PR whose ONLY failure is a NON-required check is GREEN — PR #2514's own shape`` () =
        // THE ACCEPTANCE MEASUREMENT, in the unit. `feedRun`'s only check-run is the non-required `feed`,
        // failing; the required `projection` reported and passed. Under the one-name literal this scored
        // RED and refused a PR GitHub itself reported mergeable.
        let feedCheck = named "feed" (Some 5L) "completed" (Some "failure")
        let projection = named "projection" (Some 1L) "completed" (Some "success")

        Assert.Equal(PrGreen, fst (scoreDerived fromMain [] (Some true) [ greenRun; feedRun ] [ feedCheck; projection ]))

    [<Fact>]
    let ``#2517 AC1: the SAME inputs are RED with no derivation — the verdict is the branch's, not a literal's`` () =
        // The contrast that makes the test above about the derivation rather than about the fixture. This
        // is also exactly what `landable` answered before #2517, for every non-required check that was not
        // the one name somebody had remembered to add.
        let feedCheck = named "feed" (Some 5L) "completed" (Some "failure")
        let projection = named "projection" (Some 1L) "completed" (Some "success")

        Assert.Equal(PrRed, fst (scoreRequired [] (Some true) [ greenRun; feedRun ] [ feedCheck; projection ]))

    [<Fact>]
    let ``#2517 AC4: a failure that IS in the required set still reds — the fix is not "always green"`` () =
        // The controlled counterpart. `drift` is a required context, so its failure gates exactly as it did
        // before, and the run that carries it is a finding too.
        let driftCheck = named "drift" (Some 5L) "completed" (Some "failure")

        Assert.Equal(PrRed, fst (scoreDerived fromMain [] (Some true) [ greenRun; feedRun ] [ driftCheck ]))

    [<Fact>]
    let ``#2517 AC2: --require still overrides the derivation for a check the caller names`` () =
        // #2373's opt-in lever, unchanged, and it must still WIN: `--require registry-coherence` names a
        // check branch protection cannot require, and it is the skill-registry-autofix bot's whole reason
        // for calling this command (#642/#425/#737). A derivation that silently overrode the flag would
        // break the one caller the flag exists for.
        let feedCheck = named "feed" (Some 5L) "completed" (Some "failure")

        Assert.Equal(PrGreen, fst (scoreDerived fromMain [] (Some true) [ greenRun; feedRun ] [ feedCheck ]))

        let state, _ = scoreDerived fromMain [ "feed" ] (Some true) [ greenRun; feedRun ] [ feedCheck ]
        Assert.Equal(PrRed, state)

    [<Fact>]
    let ``#2517 AC5: an UNREADABLE policy fails CLOSED — every check is scored, exactly as before`` () =
        // THE #1575/#463 RECONCILIATION, and the reason this change cannot restore #463. Reading
        // `branches/{b}/protection` needs `administration: read`, which is not a valid `permissions:` scope
        // for a workflow's GITHUB_TOKEN, and `landable`'s unattended caller runs entirely under one. Failing
        // CLOSED means "nothing is advisory", which is what lands today — so a 403 costs a request, never a
        // merge, and the derivation can only ever WIDEN what merges, never narrow it.
        let feedCheck = named "feed" (Some 5L) "completed" (Some "failure")

        let unreadable =
            noDerivation "could not read FS-GG/.github branch main protection — the token may not see it"

        Assert.False(isDerived unreadable)
        Assert.Equal(PrRed, fst (scoreDerived unreadable [] (Some true) [ greenRun; feedRun ] [ feedCheck ]))

        // ...and INDISTINGUISHABLE, at the verdict AND at the subject count, from never having asked.
        Assert.Equal(
            scoreRequired [] (Some true) [ greenRun; feedRun ] [ feedCheck ],
            scoreDerived unreadable [] (Some true) [ greenRun; feedRun ] [ feedCheck ]
        )

    [<Fact>]
    let ``#2517 AC6: an EMPTY required set is NOT a derivation — complement-of-empty must not green everything`` () =
        // THE SHARPEST HAZARD IN THIS CHANGE, and a correction to AC5 as originally filed. A SUCCESSFUL read
        // can return an empty required set: `Reads.classicRequired` maps a 404 to `Ok []` and "protected,
        // but not on status checks" to `Ok []`, and the union of the two stores has no non-empty guard.
        // "Advisory = complement of required" over an empty set makes EVERY check advisory — `landable`
        // green on every repository with no branch protection at all, which is strictly worse than the
        // defect #2517 repairs. AC5 as first written covered only a FAILED read and would have shipped it.
        //
        // GATE-INVERSION, WITH THE FIXTURE BUILT SO THE MUTATION IS VISIBLE. Deleting the guard inside
        // `advisoryFrom` (returning `DerivedFrom (Set.ofList contexts)` unconditionally) turns this `PrRed`
        // into `PrGreen`: `feed` and `feedRun` both become advisory and drop out, while `greenRun` — whose
        // own suite carries no check-runs, so `runGating`'s empty-suite arm keeps it `Blocking` — survives
        // as the one scored subject, and one passing subject is a green. WITHOUT `greenRun` the mutation
        // would instead drop the count to zero, and #606's SEPARATE zero-subjects rule would supply the same
        // `PrRed` for an unrelated reason: the mutation would survive undetected, which is the masking trap
        // `.github#2400`'s own AC3 comment records.
        let feedCheck = named "feed" (Some 5L) "completed" (Some "failure")
        let empty = advisoryFrom []

        Assert.False(isDerived empty)
        Assert.Equal(PrRed, fst (scoreDerived empty [] (Some true) [ greenRun; feedRun ] [ feedCheck ]))

        // AC6's own words — "indistinguishable, at the verdict, from an unreadable one" — in one assertion.
        Assert.Equal(
            scoreDerived (noDerivation "unreadable") [] (Some true) [ greenRun; feedRun ] [ feedCheck ],
            scoreDerived empty [] (Some true) [ greenRun; feedRun ] [ feedCheck ]
        )

    [<Fact>]
    let ``#2517 AC6: a required set of BLANK contexts is empty too — the guard is on content, not length`` () =
        // `Reads` already drops empty contexts before the union, so this is defence in depth: a payload that
        // yields `[ "" ]` must not manufacture a one-element "derivation" whose complement is everything but
        // the empty string.
        Assert.False(isDerived (advisoryFrom [ "" ]))
        Assert.False(isDerived (advisoryFrom [ ""; "" ]))
        Assert.True(isDerived (advisoryFrom [ ""; "drift" ]))

    [<Fact>]
    let ``#2517: claim-generation is BLOCKING again, because main now REQUIRES it — #2342 §9.1's arming, derived`` () =
        // THE BEHAVIOUR CHANGE THIS ITEM SHIPS, stated rather than discovered. #2342's design doc named
        // removing the name as the way to arm this "for real", to be done in the same change that adds the
        // context to branch protection. The context IS added (see `mainContexts`' verification), so the
        // derivation arms it the moment protection says so — the two subsystems can no longer drift, which
        // is the drift that produced #2373 in the first place.
        let cg = named "claim-generation" (Some 5L) "completed" (Some "failure")

        Assert.Equal(PrRed, fst (scoreDerived fromMain [] (Some true) [ greenRun; feedRun ] [ cg ]))

        // ...and under the world as it WAS — protection not naming it — #2373's rule still holds exactly.
        // The rule never changed; only who supplies its input did.
        let beforeArming =
            advisoryFrom [ "contract-coherence / coherence"; "projection"; "roster-closure"; "drift"; "reconcile" ]

        Assert.Equal(PrGreen, fst (scoreDerived beforeArming [] (Some true) [ greenRun; feedRun ] [ cg ]))

    [<Fact>]
    let ``#2517: membership is EXACT — a check merely PREFIXED BY a required context is not required`` () =
        // The old literal's prefix/substring pair, at the new polarity. Broadening the membership test to
        // `requiredContexts |> Set.exists (fun r -> c.Name.StartsWith(r: string))` would classify
        // `projection-v2` — a DIFFERENT check that merely shares a prefix with the required `projection` —
        // as `Blocking` and red this fixture. Exact `Set.Contains` keeps it advisory, which is what the
        // branch's own declaration says: `projection-v2` is not a context `main` requires.
        let sibling = named "projection-v2" (Some 5L) "completed" (Some "failure")

        Assert.Equal(PrGreen, fst (scoreDerived fromMain [] (Some true) [ greenRun; feedRun ] [ sibling ]))

    [<Fact>]
    let ``#2517: ...and a check whose name is a PREFIX OF a required context is not required either`` () =
        // The other direction: `requiredContexts |> Set.exists (fun r -> r.StartsWith(c.Name: string))`
        // would make `roster` — a prefix of the required `roster-closure` — `Blocking`. Held apart from the
        // test above so a repair that closes one broadening but not the other is still caught.
        let shorter = named "roster" (Some 5L) "completed" (Some "failure")

        Assert.Equal(PrGreen, fst (scoreDerived fromMain [] (Some true) [ greenRun; feedRun ] [ shorter ]))

    [<Fact>]
    let ``#2517: scoreRequired IS scoreDerived with no derivation — the fail-closed default cannot drift`` () =
        // `Reads.prLandableRequire` scores its FIRST pass with `scoreRequired`, precisely so the green path
        // pays for no policy read. That is only safe while the two are the same function, so pin it.
        let checks = [ named "feed" (Some 2L) "completed" (Some "failure") ]

        Assert.Equal(
            scoreDerived (noDerivation "no branch policy was consulted") [ "x" ] (Some true) [ greenRun ] checks,
            scoreRequired [ "x" ] (Some true) [ greenRun ] checks
        )

    [<Fact>]
    let ``#2517 AC7: no hardcoded check-name set remains in Landable.fs — the literal is GONE, not supplemented`` () =
        // AC7 is "gone, not merely supplemented", and the only thing that holds a source literal down is a
        // test that reads the source. It scans CODE, not prose: the doc comments must stay free to NAME
        // `advisoryCheckNames` and `claim-generation`, because the whole #2373/#2400/#2454 history is
        // written there and a gate that fired on the explanation would make the lesson unwritable — which is
        // how gates actually die (`check-recipe-landable.py` draws the same prose/code line by hand).
        //
        // ALLOW-LISTED, NOT PATTERN-MATCHED. "Does this string look like a check name?" is not decidable —
        // `contract-coherence / coherence` contains a space, `feed` is an English word — and a heuristic gate
        // is one people learn to work around. So every string literal in executable code must be one of:
        // GitHub's own status/conclusion vocabulary, a verdict word this module projects, or one of the two
        // documented `NoDerivation` sentences. A NEW literal reds this test until somebody adds it here
        // deliberately, which is exactly the review moment the hand-written set never got.
        let rec repoRoot (dir: string) =
            if File.Exists(Path.Combine(dir, "src/FS.GG.Coord.Core/Landable.fs")) then
                dir
            else
                repoRoot (Directory.GetParent(dir).FullName)

        let path =
            Path.Combine(repoRoot (Directory.GetCurrentDirectory()), "src/FS.GG.Coord.Core/Landable.fs")

        // Comments cut at `//`, which is sound here because no string literal in this module contains one —
        // a property the allow-list below independently enforces, since a literal carrying `//` would be
        // truncated into something that is not in it.
        let code =
            File.ReadAllLines path
            |> Array.map (fun line ->
                match line.IndexOf "//" with
                | -1 -> line
                | i -> line.Substring(0, i))
            |> String.concat "\n"

        let allowed =
            set
                [ ""
                  "completed"
                  "success"
                  "skipped"
                  "green"
                  "conflicted"
                  "pending"
                  "red"
                  "unknown"
                  "merged"
                  "closed"
                  "the base branch requires no status checks, and an empty required set is not a derivation"
                  "no branch policy was consulted" ]

        let unexpected =
            Regex.Matches(code, "\"([^\"]*)\"")
            |> Seq.map (fun m -> m.Groups.[1].Value)
            |> Seq.distinct
            |> Seq.filter (fun s -> not (allowed.Contains s))
            |> List.ofSeq

        Assert.Equal<string list>([], unexpected)

        // ...and the deleted binding cannot come back under its own name either. (Its NAME survives in the
        // prose above it, which is the point of scanning `code` rather than the file.)
        Assert.DoesNotContain("advisoryCheckNames", code)

        // A gate that only forbids is half a gate: the derivation must actually be what populates the
        // carve-out, or "no literal remains" would also pass on a module that carves nothing out at all.
        Assert.Contains("advisoryFrom", code)

    // ---- the #2373 check-level corpus, re-expressed over the derived set -----------------------------

    [<Fact>]
    let ``a FAILED non-required check does not red the verdict — #2373's rule, derived (#2517)`` () =
        let checks = [ named "feed" (Some 2L) "completed" (Some "failure") ]
        Assert.Equal(PrGreen, fst (scoreDerived fromMain [] (Some true) [ greenRun ] checks))

    [<Fact>]
    let ``an advisory check's failure does not count toward the subject total either`` () =
        let checks = [ named "feed" (Some 2L) "completed" (Some "failure") ]
        // Only the one live run is counted; the failing advisory check contributes nothing to `n`.
        Assert.Equal((PrGreen, 1), scoreDerived fromMain [] (Some true) [ greenRun ] checks)

    [<Fact>]
    let ``an advisory check STILL RUNNING does not hold the verdict at pending either`` () =
        let checks = [ named "feed" (Some 2L) "in_progress" None ]
        Assert.Equal(PrGreen, fst (scoreDerived fromMain [] (Some true) [ greenRun ] checks))

    [<Fact>]
    let ``a REAL required check's failure is unaffected by the carve-out — it still reds`` () =
        // The carve-out is the complement of a DECLARED set — not "ignore anything that failed". A
        // genuinely failing required check must still red the PR exactly as before.
        let checks =
            [ named "feed" (Some 2L) "completed" (Some "failure")
              named "drift" (Some 3L) "completed" (Some "failure") ]

        Assert.Equal(PrRed, fst (scoreDerived fromMain [] (Some true) [ greenRun ] checks))

    [<Fact>]
    let ``a caller that explicitly --requires an advisory check opts back into scoring its failure`` () =
        // The one lever that re-arms the exemption for a single caller: name it in `required`. A missing OR
        // failing named check both stop being invisible to the rollup once asked for by name.
        let checks = [ named "feed" (Some 2L) "completed" (Some "failure") ]
        let state, _ = scoreDerived fromMain [ "feed" ] (Some true) [ greenRun ] checks
        Assert.Equal(PrRed, state)

    [<Fact>]
    let ``--require on an advisory name still treats its ABSENCE as pending, not as invisible`` () =
        let state, _ = scoreDerived fromMain [ "feed" ] (Some true) [ greenRun ] []
        Assert.NotEqual(PrGreen, state)
        Assert.NotEqual(PrRed, state)
        Assert.Equal<string list>([ "feed" ], missing [ "feed" ] [ greenRun ] [])

    [<Fact>]
    let ``an advisory check that PASSES is unaffected — green stays green either way`` () =
        let checks = [ named "feed" (Some 2L) "completed" (Some "success") ]
        Assert.Equal(PrGreen, fst (scoreDerived fromMain [] (Some true) [ greenRun ] checks))
        Assert.Equal(PrGreen, score (Some true) [ greenRun ] checks)

    // ---- advisory checks reach their CONTAINING WORKFLOW RUN too (#2400, closing #2379; #2454) -------
    //
    // #2373 filtered only the check-run half of the rollup. A workflow that runs an advisory job ALONGSIDE
    // ordinary ones still concluded `failure` whenever the advisory job failed — and `live` (the run list)
    // was never filtered, so that run's redness still gated the merge (#2379). These tests hold `runGating`'s
    // arms to the corpus #2379 and #2454 specified; only the source of "advisory" has changed (#2517).

    [<Fact>]
    let ``AC1: a run failing SOLELY because its one non-required check failed does not red the verdict (#2379)`` () =
        // `feedRun` is the run; its ONLY check-run (same suite) is the non-required `feed`, also failing.
        // Both are excluded — but `greenRun` (a DIFFERENT suite) is still live and green, so the verdict is
        // GREEN, not a #606 zero-subjects red.
        let advisoryCheck = named "feed" (Some 5L) "completed" (Some "failure")

        Assert.Equal(PrGreen, fst (scoreDerived fromMain [] (Some true) [ greenRun; feedRun ] [ advisoryCheck ]))

    [<Fact>]
    let ``AC2: a run with ONE genuinely required failing check in the same suite still reds (#2379)`` () =
        // The `registry-coherence` case (#642/#425), on the derived set: a mixed suite — one advisory job,
        // one required job that really failed — must not have its redness laundered by the advisory job
        // sharing its suite.
        let advisoryCheck = named "feed" (Some 5L) "completed" (Some "failure")
        let realCheck = named "contract-coherence / coherence" (Some 5L) "completed" (Some "failure")

        Assert.Equal(
            PrRed,
            fst (scoreDerived fromMain [] (Some true) [ greenRun; feedRun ] [ advisoryCheck; realCheck ])
        )

    [<Fact>]
    let ``AC3: a run failing with NO check-runs of its own still reds — isolated from #606's zero-subjects rule (#2379)`` () =
        // The case that makes "just stop scoring runs" wrong: GitHub failed the run before any job could
        // even report, so there is no check-run to attribute the failure to — `runGating`'s empty-suite arm
        // must stay `Blocking`, not fall open because "no advisory checks disagreed".
        //
        // `unrelatedGreenCheck` lives on a DIFFERENT suite (99L) AND carries a REQUIRED name, so its own
        // `checkGating` — unaffected by how `feedRun` is classified — keeps `total <> 0` whatever the
        // empty-suite arm decides. Both properties are load-bearing: an advisory-named check here would be
        // dropped by `checkGating` and a `| [] -> Blocking` -> `| [] -> Advisory` mutation would then reach
        // zero subjects, where #606's SEPARATE rule supplies the same `PrRed` for an unrelated reason and
        // the mutation survives undetected (the masking trap found at review in #2400 round 1).
        let unrelatedGreenCheck = named "projection" (Some 99L) "completed" (Some "success")

        Assert.Equal(PrRed, fst (scoreDerived fromMain [] (Some true) [ feedRun ] [ unrelatedGreenCheck ]))

    [<Fact>]
    let ``a run whose suite is entirely CLEAN (every check-run already passed) but the run itself concluded bad still reds (#2454)`` () =
        // The THIRD arm `runGating`'s `findings` filter introduces: a suite that has live check-runs, but
        // none of them is a FINDING (every one completed `success`), while the RUN's own conclusion is
        // nonetheless bad. Nothing in the suite explains that badness, so `runGating` stays conservative and
        // returns `Blocking`.
        //
        // Pinned with the same masking discipline AC3's comment documents: `greenRun` lives on a DIFFERENT
        // suite (1L), so it keeps `total <> 0` whatever this arm decides. A `findings = [] -> Advisory`
        // mutation would launder `feedRun`'s own `failure` conclusion, dropping it from the rollup and
        // turning this `PrRed` into a false `PrGreen`.
        let onlyCheckPassed = named "projection" (Some 5L) "completed" (Some "success")

        Assert.Equal(PrRed, fst (scoreDerived fromMain [] (Some true) [ greenRun; feedRun ] [ onlyCheckPassed ]))

    [<Fact>]
    let ``AC1 (multi-check suite, #2454): an advisory-failing check ALONGSIDE genuinely GREEN required checks does not red`` () =
        // `.github#2454`: the shape a real workflow produces, and the one the ORIGINAL #2400 fix did not
        // cover — one suite carrying the advisory job (failing) plus the workflow's other jobs, all passing.
        // The first shipped `runGating` surveyed suite MEMBERSHIP: every check-run in the suite, passing ones
        // included, had to be advisory for the run to qualify, so the mere PRESENCE of passing ordinary jobs
        // kept the run `Blocking` and its own advisory-caused `failure` still reded the verdict. Filtering to
        // FINDINGS first is what makes "solely because its advisory job did" actually mean solely.
        let advisoryFailing = named "feed" (Some 5L) "completed" (Some "failure")
        let ordinaryGreenA = named "contract-coherence / coherence" (Some 5L) "completed" (Some "success")
        let ordinaryGreenB = named "projection" (Some 5L) "completed" (Some "success")
        let ordinaryGreenC = named "roster-closure" (Some 5L) "completed" (Some "success")

        Assert.Equal(
            PrGreen,
            fst (
                scoreDerived
                    fromMain
                    []
                    (Some true)
                    [ greenRun; feedRun ]
                    [ advisoryFailing; ordinaryGreenA; ordinaryGreenB; ordinaryGreenC ]
            )
        )

    [<Fact>]
    let ``AC2 (multi-check suite, #2454): List.forall over FINDINGS still governs the SUBJECT COUNT, though not the verdict on its own`` () =
        // CORRECTED AT REVIEW (`.github#2454` round 2): a `forall` -> `exists` mutation of `runGating`'s
        // findings test cannot flip THIS shape's verdict, and the reason is structural rather than incidental
        // to the fixture. `findings` and `scoredChecks` are BOTH derived from the same `checks` list, and
        // `checkGating` classifies each check-run independently of whatever `runGating` decides for its run —
        // so a `Blocking` member of `findings`, BEING a finding and BEING non-advisory, is always also scored
        // directly through `scoredChecks` and forces the same non-green verdict either way.
        //
        // What the mutation DOES change is the SUBJECT COUNT (`#606`/`#724`'s own reason the count is a
        // first-class output). Under the correct `forall`, `feedRun` stays `Blocking` and is itself a counted
        // subject; under a mutant `exists` it is wrongly excluded and the count drops to 3. The verdict
        // assertion below pins what that mutation cannot flip; the count assertion pins what it can.
        let advisoryFailing = named "feed" (Some 5L) "completed" (Some "failure")
        let realFailing = named "drift" (Some 5L) "completed" (Some "failure")
        let ordinaryGreen = named "projection" (Some 5L) "completed" (Some "success")

        Assert.Equal(
            (PrRed, 4),
            scoreDerived fromMain [] (Some true) [ greenRun; feedRun ] [ advisoryFailing; realFailing; ordinaryGreen ]
        )

    [<Fact>]
    let ``a run whose only check-run is advisory and STILL RUNNING is not held at pending either`` () =
        // Symmetry with the check-run half: `runGating` is a function of NAMES, not status, so an
        // in-progress advisory-only run is excluded from `pending` exactly as a completed-and-failed one is
        // excluded from `bad`.
        let pendingFeedRun =
            run ".github/workflows/feed.yml" "pull_request" "item/x" [ 1 ] 5 "in_progress" None (Some 5L)

        let advisoryCheck = named "feed" (Some 5L) "in_progress" None

        Assert.Equal(PrGreen, fst (scoreDerived fromMain [] (Some true) [ greenRun; pendingFeedRun ] [ advisoryCheck ]))

    [<Fact>]
    let ``a --required advisory name reaches the run too — opting in un-hides the containing run`` () =
        // The other direction of #2373's opt-in lever, extended to runs (#2400): naming the advisory check in
        // `required` makes `checkGating` return `Blocking` for it, which flips `runGating`'s all-advisory test
        // for the run that contains it — so `--require` restores BOTH halves to the rollup.
        let advisoryCheck = named "feed" (Some 5L) "completed" (Some "failure")
        let state, _ = scoreDerived fromMain [ "feed" ] (Some true) [ greenRun; feedRun ] [ advisoryCheck ]
        Assert.Equal(PrRed, state)

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
