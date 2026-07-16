namespace FS.GG.Coord

module Landable =

    open Types

    type RunRow =
        { Path: string
          Event: string
          HeadBranch: string
          PrNumbers: int list
          RunNumber: int
          Status: string
          Conclusion: string option
          CheckSuiteId: int64 option }

    type CheckRow =
        { Name: string
          CheckSuiteId: int64 option
          Status: string
          Conclusion: string option }

    /// The concurrency group `cancel-in-progress` actually keys on. Matching it EXACTLY — not on `.Path`
    /// alone — is what stops the drop rule being a hole: a `workflow_dispatch` run on the same branch shares
    /// the SHA and the path and carries a HIGHER run number, but it is a different `github.ref`, so it
    /// supersedes nothing, and a gate job that `if: github.event_name == 'pull_request'` SKIPS in it and
    /// concludes `success`. Dropping the cancelled `pull_request` run in its favour would count a vacuous
    /// green (#703).
    let private cgroup (r: RunRow) : string * string * string * int list =
        r.Path, r.Event, r.HeadBranch, List.sort r.PrNumbers

    let supersede (runs: RunRow list) : RunRow list * int64 list =
        // A run is REPLACED when a run of its own concurrency group carries a higher run number.
        let replaced (r: RunRow) =
            runs |> List.exists (fun o -> cgroup o = cgroup r && o.RunNumber > r.RunNumber)

        // SUPERSEDED, and nothing else: a CANCELLED run a later run of its group replaced. A cancelled run
        // nobody re-ran is still a finding (stays live); a failed run is never dropped — so this cannot
        // fail open.
        let live =
            runs
            |> List.filter (fun r -> r.Conclusion <> Some "cancelled" || not (replaced r))

        let dead =
            runs
            |> List.filter (fun r -> r.Conclusion = Some "cancelled" && replaced r)
            |> List.choose (fun r -> r.CheckSuiteId)

        live, dead

    /// The check-runs still worth scoring: every one whose suite was NOT superseded. A non-Actions app's
    /// suite is never in the runs list, so it is never dropped — it is scored by construction, with no
    /// special case (#720).
    ///
    /// ONE derivation, because `scoreRequired` and `missing` must agree about which checks are live: the
    /// first decides that a required check is absent, the second says WHICH. Two copies could drift into
    /// `pending` with an empty reason — a verdict that refuses and cannot say why, which is the one thing
    /// worse than either answer alone.
    let private liveChecks (runs: RunRow list) (checks: CheckRow list) : CheckRow list =
        let _, dead = supersede runs
        let deadSet = Set.ofList dead

        checks
        |> List.filter (fun c ->
            match c.CheckSuiteId with
            | Some sid -> not (deadSet.Contains sid)
            | None -> true)

    /// Which `required` names no check in `live` carries. The single answer `scoreRequired` decides on and
    /// `missing` reports.
    let private missingFrom (required: string list) (live: CheckRow list) : string list =
        let names = live |> List.map (fun c -> c.Name) |> Set.ofList
        required |> List.filter (fun name -> not (names.Contains name))

    /// A subject (run or check) is a FINDING unless it COMPLETED and concluded `success` or `skipped`.
    let private isPending (status: string) = status <> "completed"

    let private isBad (status: string) (conclusion: string option) =
        status = "completed" && conclusion <> Some "success" && conclusion <> Some "skipped"

    /// The verdict AND the number of subjects it was scored over — runs plus check-runs, after the
    /// superseded suites are dropped. `--wait` needs that count and the verdict is not enough: a `red` over
    /// ZERO subjects is "CI has not started YET" (normal for the first 20-60s after a push), a `red` over
    /// some is a real finding, and only the count tells them apart (#606/#724). A conflicted or unknown
    /// verdict is reached before any subject is scored, so its count is 0.
    ///
    /// `required` NAMES CHECKS THAT MUST HAVE REPORTED (#737). The rollup above answers "is anything red?",
    /// and that question is blind to a check that is ABSENT: an absent subject reads exactly like a passing
    /// one in any "are all checks green?" test, which is #606's whole lesson. Branch protection covers the
    /// REQUIRED set, so the sharp edge is a NON-required check that is nonetheless the reason the PR exists
    /// — `registry-coherence` on the skill-registry autofix bot's standing PR, whose redness means "this
    /// snapshot is OBSOLETE" and which GitHub's native auto-merge would merge straight past (#642/#425).
    /// Naming it here is what lets that bot call this command instead of hand-rolling a fifth copy of the
    /// gate (#724).
    ///
    /// A MISSING REQUIRED CHECK IS `pending`, NOT `red` — deliberately, and it is the one subtle call here.
    /// "The check has not reported" is literally the pending sentence, and the state is usually TRANSIENT:
    /// GitHub registers a PR's checks over 20-60s, and — worse for a bot that manufactures supersession on
    /// every reconcile — a required check whose suite was just SUPERSEDED is absent for the seconds between
    /// the drop and its replacement registering. Calling that red would refuse the PR the bot had just
    /// pushed, which is #710 restored. `pending` never settles, so `--wait` rides it out on the transient
    /// case and, when the check is absent because it was RENAMED, exhausts its tries and refuses — the same
    /// no-merge, reached honestly. It cannot fail open: `pending` is never a green.
    let scoreRequired
        (required: string list)
        (mergeable: bool option)
        (runs: RunRow list)
        (checks: CheckRow list)
        : PrState * int =
        match mergeable with
        // `null` after the read is UNKNOWN — it is not "conflicted", and it is emphatically not "mergeable".
        // Fail closed: advise nothing on a guess (#697).
        | None -> PrUnknown, 0
        // A conflict gets no CI at all (GitHub cannot build `refs/pull/N/merge` while it conflicts), so its
        // check set is permanently empty — the verdict must come from mergeability, before the checks.
        | Some false -> PrConflicted, 0
        | Some true ->
            let live, _ = supersede runs
            let liveChecks = liveChecks runs checks

            // The rollup is over BOTH lists (#606): a run can fail with no check-runs at all
            // (`startup_failure`), and a check-run can fail while its run SUCCEEDS (job-level
            // `continue-on-error`). Neither list alone is the truth.
            let pending =
                (live |> List.filter (fun r -> isPending r.Status) |> List.length)
                + (liveChecks |> List.filter (fun c -> isPending c.Status) |> List.length)

            let bad =
                (live |> List.filter (fun r -> isBad r.Status r.Conclusion) |> List.length)
                + (liveChecks |> List.filter (fun c -> isBad c.Status c.Conclusion) |> List.length)

            let total = List.length live + List.length liveChecks

            // A `--require`d check that is not among the LIVE check-runs has not reported. Matched on the
            // live set, so a superseded suite's copy cannot satisfy it — the check that was cancelled is
            // exactly the one whose verdict we do not have. Same derivation `missing` reports from, so the
            // verdict and its reason cannot disagree.
            let missingRequired = missingFrom required liveChecks

            // ZERO SUBJECTS IS NOT GREEN (#606). "Every check passed" and "CI never started" are the same
            // empty set. A missing subject is a finding, not a pass.
            //
            // The missing-required test sits BELOW `bad` on purpose: a red check is a settled finding and
            // must be reported as `red` at once, whereas a missing required check is a "not yet" that
            // `--wait` should ride out. Reporting the softer verdict over a hard red would make the loop
            // spin for its whole budget before announcing a failure it already knew.
            let state =
                if total = 0 then PrRed
                elif pending > 0 then PrPending
                elif bad > 0 then PrRed
                elif not missingRequired.IsEmpty then PrPending
                else PrGreen

            state, total

    /// The names a `--require`d check could not be matched against: the LIVE check-runs, superseded suites
    /// already dropped. Diagnostics only — the verdict is `scoreRequired`'s. It exists so the CLI can say
    /// WHICH check never reported rather than printing a bare `pending`, which on the renamed-job case is
    /// the difference between a diagnosis and a mystery.
    let missing (required: string list) (runs: RunRow list) (checks: CheckRow list) : string list =
        missingFrom required (liveChecks runs checks)

    let scoreN (mergeable: bool option) (runs: RunRow list) (checks: CheckRow list) : PrState * int =
        scoreRequired [] mergeable runs checks

    let score (mergeable: bool option) (runs: RunRow list) (checks: CheckRow list) : PrState =
        scoreN mergeable runs checks |> fst

    /// The `--wait` poll decision (#724): given this poll's verdict and subject count `n`, and the PREVIOUS
    /// poll's count `prev`, has the verdict SETTLED — may the loop stop — or must it keep waiting? Pure, so
    /// the two traps a naive "poll until not pending" walks into are held by a unit test, not a fixture.
    ///
    ///   * `conflicted`/`unknown` SETTLE at once — no amount of waiting fixes a conflict, and `unknown` is
    ///     the fail-closed answer; spinning on either just delays the bad news.
    ///   * `red` settles only once there is a subject to be red ABOUT (`n > 0`). A `red` over zero subjects is
    ///     the REGISTRATION RACE — GitHub has not created the runs yet — so the waiter keeps polling; only if
    ///     they never register does the red stand, and then it is the honest #606 finding.
    ///   * `green` settles only once the subject count has STOPPED GROWING (`n > 0 && n = prev`). GitHub
    ///     schedules a PR's workflows over 20-60s, so an early poll can see "1 run, green" while the failing
    ///     one has not been CREATED yet; believing it merges a PR most of whose checks never ran (#606 at one
    ///     remove). Requiring the count to be stable across two consecutive polls is the cheapest insurance.
    ///   * `pending` never settles — a run still going is the one verdict worth waiting on.
    let settled (state: PrState) (n: int) (prev: int) : bool =
        match state with
        | PrConflicted
        | PrUnknown -> true
        | PrRed -> n > 0
        | PrGreen -> n > 0 && n = prev
        | PrPending -> false

    let name (state: PrState) : string =
        match state with
        | PrGreen -> "green"
        | PrConflicted -> "conflicted"
        | PrPending -> "pending"
        | PrRed -> "red"
        | PrUnknown -> "unknown"
