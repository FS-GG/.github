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
        { CheckSuiteId: int64 option
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

    /// A subject (run or check) is a FINDING unless it COMPLETED and concluded `success` or `skipped`.
    let private isPending (status: string) = status <> "completed"

    let private isBad (status: string) (conclusion: string option) =
        status = "completed" && conclusion <> Some "success" && conclusion <> Some "skipped"

    /// The verdict AND the number of subjects it was scored over — runs plus check-runs, after the
    /// superseded suites are dropped. `--wait` needs that count and the verdict is not enough: a `red` over
    /// ZERO subjects is "CI has not started YET" (normal for the first 20-60s after a push), a `red` over
    /// some is a real finding, and only the count tells them apart (#606/#724). A conflicted or unknown
    /// verdict is reached before any subject is scored, so its count is 0.
    let scoreN (mergeable: bool option) (runs: RunRow list) (checks: CheckRow list) : PrState * int =
        match mergeable with
        // `null` after the read is UNKNOWN — it is not "conflicted", and it is emphatically not "mergeable".
        // Fail closed: advise nothing on a guess (#697).
        | None -> PrUnknown, 0
        // A conflict gets no CI at all (GitHub cannot build `refs/pull/N/merge` while it conflicts), so its
        // check set is permanently empty — the verdict must come from mergeability, before the checks.
        | Some false -> PrConflicted, 0
        | Some true ->
            let live, dead = supersede runs
            let deadSet = Set.ofList dead

            // Every check-run whose suite was NOT superseded is scored. A non-Actions app's suite is never
            // in the runs list, so it is never in `dead`, so it is never dropped — it is scored by
            // construction, with no special case (#720).
            let liveChecks =
                checks
                |> List.filter (fun c ->
                    match c.CheckSuiteId with
                    | Some sid -> not (deadSet.Contains sid)
                    | None -> true)

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

            // ZERO SUBJECTS IS NOT GREEN (#606). "Every check passed" and "CI never started" are the same
            // empty set. A missing subject is a finding, not a pass.
            let state =
                if total = 0 then PrRed
                elif pending > 0 then PrPending
                elif bad > 0 then PrRed
                else PrGreen

            state, total

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
