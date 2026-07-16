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
    type CheckRow =
        { CheckSuiteId: int64 option
          Status: string
          Conclusion: string option }

    /// Split runs into (live, dead-check-suite-ids): a `cancelled` run replaced by a LATER `RunNumber` of
    /// its OWN concurrency group is superseded (#720), and its check suite must be dropped with it. A
    /// cancelled run nobody re-ran stays live (still a finding); a FAILED run is never dropped. This is the
    /// one expression applied to both the runs and their check-runs, so the two cannot drift.
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

    /// The `--wait` poll decision (#724): given this poll's verdict, its subject count `n`, and the PREVIOUS
    /// poll's count `prev`, has the verdict SETTLED (stop) or must the loop keep waiting? `conflicted`/
    /// `unknown` settle at once; `red` settles only with a subject to be red about (`n > 0`, else it is the
    /// registration race); `green` settles only once the count has STOPPED GROWING (`n > 0 && n = prev`, or
    /// an early partial rollup merges a PR whose failing check had not been created yet); `pending` never
    /// settles. Pure, so both premature-green traps are held by a unit test rather than a fixture.
    val settled: state: PrState -> n: int -> prev: int -> bool

    /// The one-word verdict the corpus certifies (`green`/`conflicted`/`pending`/`red`/`unknown`), for the
    /// `who --json` `prState` field and the human table. ONE projection, so the JSON and text surfaces
    /// cannot name the same state differently.
    val name: state: PrState -> string
