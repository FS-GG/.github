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
///   green ≠ conflicted ≠ pending ≠ red · zero-checks is RED not GREEN (#606) · a superseded `cancelled`
///   run is NOT a failure (#720) · a null mergeable is UNKNOWN, never mergeable.
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
        { CheckSuiteId = suite
          Status = status
          Conclusion = concl }

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

    // ---- the name projection -------------------------------------------------------------------------

    [<Fact>]
    let ``name is the one-word verdict the corpus certifies`` () =
        Assert.Equal("green", name PrGreen)
        Assert.Equal("conflicted", name PrConflicted)
        Assert.Equal("pending", name PrPending)
        Assert.Equal("red", name PrRed)
        Assert.Equal("unknown", name PrUnknown)
