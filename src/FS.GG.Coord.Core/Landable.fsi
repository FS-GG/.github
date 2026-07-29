namespace FS.GG.Coord

/// IS AN OPEN PR FINISHED WORK? — the #697/#720 verdict, computed in ONE place.
///
/// `Liveness` (#581) taught the protocol that an open `item/<n>-*` PR is proof of life, and stopped there:
/// `reap` refuses such a claim and then offers exactly one exit — "close it, then reap". For a PR that is
/// green, reviewed and mergeable, that exit DESTROYS the best work on the board, and it is the path of
/// least resistance. This module reads WHAT THE PR SAYS, not merely that it exists, so `who`/`reap`/`adopt`
/// can tell abandoned work from finished work.
///
/// It is PURE and total. The IO — the PR, its head SHA's workflow runs, and that SHA's check runs — is
/// `Reads.prLandable`; the CLASSIFICATION is here, so it can be unit-tested without a fixture server, and
/// so the four historical copies of this gate (`/pnext-item`, the autofix bot, the recipe, the CLI) cannot
/// drift (#724).
module Landable =

    open Types

    /// One workflow run on a head SHA (`actions/runs`.`workflow_runs[]`).
    ///
    /// `CheckSuiteId` links a run to the check-runs it produced, so a SUPERSEDED run's check-runs can be
    /// dropped with it. `Path`/`Event`/`HeadBranch`/`PrNumbers` are the concurrency-group key #720 keys
    /// supersession on — NOT `Path` alone, which would let a `workflow_dispatch` run license the drop of a
    /// `pull_request` run and count a vacuous green (#703).
    type RunRow =
        { Path: string
          Event: string
          HeadBranch: string
          PrNumbers: int list
          RunNumber: int
          Status: string
          Conclusion: string option
          CheckSuiteId: int64 option }

    /// One check run on a head SHA (`commits/{sha}/check-runs`.`check_runs[]`).
    ///
    /// A check run is the ONLY place a non-Actions app appears, and the only place a job-level
    /// `continue-on-error` failure shows (its run concludes `success`). Both are scored, or the rollup
    /// fails open on coverage the workflow-run list can never carry (#720).
    ///
    /// `Name` is the JOB name, and it COLLIDES ACROSS WORKFLOWS — measured on `.github`: seven check runs
    /// named `fixture`, from six workflows (#698). So it is fit for `scoreRequired`'s PRESENCE test (did
    /// anything by this name report?) and for nothing else. In particular it must never key supersession:
    /// "latest check run per name" is what branch protection does, and here it would let one workflow's
    /// green `fixture` hide another's red one.
    type CheckRow =
        { Name: string
          CheckSuiteId: int64 option
          Status: string
          Conclusion: string option }

    /// Split runs into (live, dead-check-suite-ids): a run replaced by a LATER `RunNumber` of its OWN
    /// concurrency group is superseded (#720), and its check suite must be dropped with it. A run nobody
    /// replaced stays live and is still a finding (#698). This is the one expression applied to both the
    /// runs and their check-runs, so the two cannot drift.
    ///
    /// THE CONCLUSION IS NOT PART OF THE TEST (ADR-0043). The rule was once `cancelled`-only, to stop a
    /// failure being laundered by re-running it until it passed — an attack that does not exist: a re-run
    /// creates no run, only an ATTEMPT under the same row, whose `Conclusion` then reads the latest attempt
    /// (#721). What the test actually kept was a failure a DIFFERENT TRIGGER had re-evaluated on the same
    /// SHA, which is #1039. `cgroup`, not the conclusion, is what keeps this fail-closed (#703).
    val supersede: runs: RunRow list -> RunRow list * int64 list

    /// Score a PR from its mergeability and the checks on its head SHA.
    ///
    /// `mergeable`: `None` = unknown (the caller could not read it, or GitHub returned `null`) → `PrUnknown`;
    /// `Some false` = `PrConflicted`; `Some true` = look at the checks. A superseded suite's check-runs are
    /// dropped; every surviving run AND check-run is scored over BOTH lists. Zero live subjects is `PrRed`,
    /// not `PrGreen` (#606 — a missing subject is a finding, not a pass).
    val score: mergeable: bool option -> runs: RunRow list -> checks: CheckRow list -> PrState

    /// `score`, plus the NUMBER of subjects the verdict was scored over — the live runs plus the live
    /// check-runs, after the superseded suites are dropped. `landable --wait` needs the count: a `red` over
    /// ZERO subjects is the registration race ("CI has not started yet"), a `red` over some is a real
    /// finding, and only the count tells them apart (#606/#724). Conflicted/unknown are reached before any
    /// subject is scored, so their count is 0.
    val scoreN: mergeable: bool option -> runs: RunRow list -> checks: CheckRow list -> PrState * int

    /// `scoreN`, plus checks that MUST have reported by name (#737) — `scoreN` is this with `required = []`.
    ///
    /// The rollup asks "is anything red?", which is blind to a check that is ABSENT; an absent subject reads
    /// exactly like a passing one (#606). Branch protection already covers the REQUIRED set, so what this is
    /// for is a NON-required check that is nonetheless the whole reason a PR exists — `registry-coherence`
    /// on the autofix bot's standing PR, which GitHub's native auto-merge merges straight past (#642/#425).
    ///
    /// A missing required check scores `PrPending`, NOT `PrRed`: it is the pending sentence ("it has not
    /// reported"), it is usually transient (registration, or the seconds between a suite being SUPERSEDED
    /// and its replacement registering — which a bot that force-pushes manufactures on every run, #710), and
    /// `pending` never settles, so `--wait` rides out the transient case and refuses the permanent one when
    /// its tries run out. It ranks BELOW a red, so a real failure is still reported at once.
    val scoreRequired:
        required: string list ->
        mergeable: bool option ->
        runs: RunRow list ->
        checks: CheckRow list ->
            PrState * int

    /// Which `required` checks are absent from the LIVE check-runs (superseded suites dropped). Diagnostics
    /// only — `scoreRequired` owns the verdict — so the CLI can name the check that never reported instead
    /// of printing a bare `pending`. On the renamed-job case that is the difference between a diagnosis and
    /// a mystery.
    val missing: required: string list -> runs: RunRow list -> checks: CheckRow list -> string list

    /// The `--wait` poll decision (#724): given this poll's verdict, its subject count `n`, and the PREVIOUS
    /// poll's count `prev`, has the verdict SETTLED (stop) or must the loop keep waiting? `conflicted`/
    /// `unknown` settle at once; `merged`/`closed` settle at once (#1680 — a closed PR has no subject left
    /// to gate, and nothing reopens it); `red` settles only with a subject to be red about (`n > 0`, else it
    /// is the registration race); `green` settles only once the count has STOPPED GROWING (`n > 0 && n =
    /// prev`, or an early partial rollup merges a PR whose failing check had not been created yet);
    /// `pending` never settles. Pure, so both premature-green traps are held by a unit test rather than a
    /// fixture.
    ///
    /// THIS IS WHERE `--wait` STOPS POLLING A MERGED PR (#1680 AC3). The loop in `Client.landable` consults
    /// nothing else to decide break-vs-wait, so a `merged` that settles here cannot cost a second poll —
    /// which is the measured 600s (30 × 20s) the issue was filed about, driven to zero at its source rather
    /// than by a special case in the caller.
    val settled: state: PrState -> n: int -> prev: int -> bool

    /// The one-word verdict the corpus certifies (`green`/`conflicted`/`pending`/`red`/`unknown`/`merged`/
    /// `closed`), for the `who --json` `prState` field and the human table. ONE projection, so the JSON and
    /// text surfaces cannot name the same state differently.
    ///
    /// `merged` and `closed` are #1680's: the vocabulary had five words for an OPEN PR and none for a PR
    /// that is not open, so the scorer answered the recovery path's question in words that were all false.
    val name: state: PrState -> string
