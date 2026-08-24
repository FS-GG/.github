namespace FS.GG.Coord

// NOT WIRED TO THE REVIEW-PROTOCOL CHAIN, AND THAT IS THE STATED CONTRACT (`.github#2360`,
// `.github#2417`). Everything below scores GitHub check-run/workflow-run state alone — it reads no PR
// comment, no review marker, no critic identity, and no `Review.NextAction`. `.github#2417` adds a
// critic-succession recovery path to `Review.fs` that changes WHO may produce an accepted review
// chain when a chain's critic despawns mid-round; it changes nothing here. A granted critic succession
// is never itself evidence of a green build, and a green verdict from this module is never itself
// evidence of a satisfied review chain — the accepted-head structured host-acceptance decision
// marker and a green verdict from `scoreRequired` remain two independently required facts before
// merge, exactly as before this module gained the advisory-check exemption (#2373/#2400) and exactly
// as before the review protocol gained a repair phase or, now, critic succession.
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

    /// A completed, non-successful subject that participates in the settled red verdict. The GitHub read
    /// boundary binds these identities to the evaluated head SHA before presenting them to an operator.
    type Failure =
        | WorkflowRunFailure of path: string * runNumber: int * conclusion: string option
        | CheckRunFailure of name: string * checkSuiteId: int64 option * conclusion: string option

    // The concurrency group `cancel-in-progress` actually keys on. Matching it EXACTLY — not on `.Path`
    // alone — is what stops the drop rule being a hole: a `workflow_dispatch` run on the same branch shares
    // the SHA and the path and carries a HIGHER run number, but it is a different `github.ref`, so it
    // supersedes nothing, and a gate job that `if: github.event_name == 'pull_request'` SKIPS in it and
    // concludes `success`. Dropping the cancelled `pull_request` run in its favour would count a vacuous
    // green (#703).
    let private cgroup (r: RunRow) : string * string * string * int list =
        r.Path, r.Event, r.HeadBranch, List.sort r.PrNumbers

    // SUPERSESSION IS THE GROUP'S OWN LATER RUN, whatever the earlier one concluded (ADR-0043). Only the
    // highest run number in each concurrency group is scored; every earlier run of that group is dropped
    // with its check suite.
    //
    // THE CONCLUSION IS NOT PART OF THE TEST, and it used to be: the rule dropped a superseded run ONLY
    // when it was `cancelled`, on the reasoning that "a failed run is never dropped — so this cannot fail
    // open" (#698/#719). That reasoning assumed a failure could be laundered by RE-RUNNING it until it
    // passed. **A re-run cannot do that, because a re-run creates no run.** It adds an ATTEMPT under the
    // same id and the same run number, and the row's `Conclusion` reads the LATEST attempt (#721 — the same
    // fact that makes a re-run re-execute a stale `@main`). So a failure re-run until it passes is ONE row
    // reading `success`, which every rule here scores green, and the `cancelled`-only test bought no
    // protection from it — it had none to buy. What it actually kept was a failure that a DIFFERENT TRIGGER
    // had already re-evaluated, which is #1039: `architecture-map` reads the PR BODY out of the event
    // payload, so its verdict is not a function of the head SHA. You follow the remedy it prints, the
    // `edited` run passes, and the completed failure stays immortal beside it — `red`, forever, on a PR
    // GitHub calls `clean`. The escape was to force-push a tree-identical commit to launder the verdict.
    //
    // AND THE EVENT NEED NOT BE CONSULTED to tell those apart, though #1039 first proposed it: every run
    // scored here is on ONE head SHA (`Reads.workflowRuns` keys the read on it), and a `synchronize`
    // CHANGES the head SHA by definition. So a second run of a group on a fixed SHA is always a
    // re-evaluation, never a re-run — the SHA scoping decides it, with no event list to go stale (#381/
    // #446/#962). It could not have been consulted anyway: `edited` and `synchronize` are webhook ACTIONS,
    // and the runs API carries only `event` — `pull_request` for both.
    //
    // IT STILL CANNOT FAIL OPEN, and `cgroup` is what does that work — not the conclusion. A run is
    // dropped only for a later run of its OWN group, so a `workflow_dispatch` (a different `github.ref`,
    // hence a different group) supersedes nothing and cannot vacuously green a `pull_request` run it
    // skipped its gate job in (#703). A cancelled or failed run NOBODY replaced is the latest in its group,
    // stays live, and is still a finding (#698).
    //
    // THE `Status` LEAVES THE TEST TOO: a superseded run still IN PROGRESS is dropped, where the old rule
    // waited for it. Nothing cancels it when its workflow declares no `concurrency` block, and it is scoring
    // an OLDER read of the same SHA's metadata than the run that already replaced it.
    //
    // THE COST, NAMED: a FLAKE is now laundered by any later trigger of its group on the same SHA — a label
    // toggle included, since metadata gates trigger on `labeled`/`unlabeled`. The `cancelled`-only clause
    // only APPEARED to close that door: re-running the failed run mutates its own row to `success`, which
    // every rule here scores green, so laundering was always one click away and the clause merely kept the
    // HONEST path (follow the gate's printed remedy) red. Branch protection — the authority that actually
    // gates the merge — scores the latest run per workflow and greens both cases regardless (ADR-0043).
    let supersede (runs: RunRow list) : RunRow list * int64 list =
        // A run is REPLACED when a run of its own concurrency group carries a higher run number.
        let replaced (r: RunRow) =
            runs |> List.exists (fun o -> cgroup o = cgroup r && o.RunNumber > r.RunNumber)

        let live = runs |> List.filter (fun r -> not (replaced r))

        let dead =
            runs |> List.filter replaced |> List.choose (fun r -> r.CheckSuiteId)

        live, dead

    // The check-runs still worth scoring: every one whose suite was NOT superseded. A non-Actions app's
    // suite is never in the runs list, so it is never dropped — it is scored by construction, with no
    // special case (#720).
    //
    // ONE derivation, because `scoreRequired` and `missing` must agree about which checks are live: the
    // first decides that a required check is absent, the second says WHICH. Two copies could drift into
    // `pending` with an empty reason — a verdict that refuses and cannot say why, which is the one thing
    // worse than either answer alone.
    let private liveChecks (runs: RunRow list) (checks: CheckRow list) : CheckRow list =
        let _, dead = supersede runs
        let deadSet = Set.ofList dead

        checks
        |> List.filter (fun c ->
            match c.CheckSuiteId with
            | Some sid -> not (deadSet.Contains sid)
            | None -> true)

    // Which `required` names no check in `live` carries. The single answer `scoreRequired` decides on and
    // `missing` reports.
    let private missingFrom (required: string list) (live: CheckRow list) : string list =
        let names = live |> List.map (fun c -> c.Name) |> Set.ofList
        required |> List.filter (fun name -> not (names.Contains name))

    // WHERE THE ADVISORY CARVE-OUT COMES FROM (`.github#2517`) — the branch's OWN
    // `required_status_checks.contexts`, never a source literal. "Advisory" is the COMPLEMENT of
    // "required": a check the base branch does not require cannot hold ITS merge, so it must not hold this
    // verdict either.
    //
    // WHAT IT REPLACED, AND WHY THE LITERAL WAS WRONG. This was `advisoryCheckNames: Set<string> =
    // Set.ofList [ "claim-generation" ]`, hand-populated, and by its own comment "THE ONE DECLARED PLACE
    // ... the sole source of advisory names". It had exactly one entry, so EVERY other non-required check
    // on the repository was scored as gating whether or not `main` required it. Measured on `.github` PR
    // #2514 at `f1d6218d775d278429cf6cea252b7d617ee3c723`: all six required contexts passing, the
    // non-required `feed` arm failing, `mergeable_state` `unstable` (GitHub itself permits the merge) — and
    // `landable` `red`, refusing a fully reviewed, host-accepted PR by its own protocol. A merge gate that
    // reds on non-required checks is a gate operators learn to override, and the override habit is what
    // makes the NEXT genuinely-red required check land unnoticed (`.github#2517`, epic #266).
    //
    // THE HISTORY THE LITERAL CARRIED IS PRESERVED, NOT DISCARDED. `claim-generation`'s own design doc
    // (`.github#2342` AC6) declared its verdict "observed, not enforced", and `scoreRequired` scoring every
    // live check unconditionally enforced it anyway (`.github#2373`, reproduced live across three PRs in one
    // wave). That repair was right; only its INPUT was hand-written. #2342's design doc (§9.1) named the
    // exit condition itself — remove the name in the SAME change that adds the context to
    // `branches/main/protection/required_status_checks`, "so the two subsystems move together and cannot
    // drift back out of agreement". Deriving the set makes that simultaneity structural rather than
    // remembered: `claim-generation` IS in `.github`'s required contexts today (`gh api
    // repos/FS-GG/.github/branches/main/protection --jq '.required_status_checks.contexts'`, 2026-08-13:
    // `["contract-coherence / coherence","projection","roster-closure","drift","reconcile",
    // "claim-generation"]`), so this derivation now scores it `Blocking` — the arming #2342 §9.1 asked for,
    // applied the moment protection changed and with no source edit at all.
    //
    // AN EMPTY REQUIRED SET IS NOT A DERIVATION, and this is the sharpest hazard in the whole change.
    // `Reads.classicRequired` maps a 404 to `Ok []` and "protected, but not on status checks" to `Ok []`,
    // and the union of the two stores has no non-empty guard — so a SUCCESSFUL read can legitimately return
    // an empty required set. Complement-of-empty is EVERYTHING, which would make every check advisory and
    // score `landable` green on repositories with no branch protection at all: a fleet-wide fail-open
    // strictly worse than the defect being repaired. `advisoryFrom` is the ONE constructor of `DerivedFrom`
    // and refuses to build one from an empty set; an empty read is therefore `NoDerivation`, verdict-identical
    // to an UNREADABLE one. The union's cases are `private` for exactly this reason — there is no way to
    // hand-build a `DerivedFrom Set.empty` past the guard.
    //
    // `NoDerivation` MEANS NOTHING IS ADVISORY — every live check is scored, which is the pre-#2517
    // behaviour and the fail-CLOSED direction. That is what reconciles this change with `#1575`/`#463`:
    // reading `branches/{b}/protection` needs `administration: read`, which is not a valid `permissions:`
    // scope for a workflow's GITHUB_TOKEN, and `landable`'s unattended caller runs entirely under one. A
    // verdict that RESTED on that read would 403 forever (#463: a protection probe that 403'd on every
    // receiver and stopped the kit landing anywhere). Failing closed to "everything gating" means nothing
    // that lands today stops landing when the read fails; the derivation can only ever WIDEN what merges,
    // never narrow it.
    //
    // NAME COLLISIONS ARE STILL NOT CLOSED (`.github#2374`, open, deliberately), and the keying is
    // unchanged: a check-run `Name` is the JOB name and collides across workflows (seven runs named
    // `fixture`, from six workflows, measured on `.github`). What #2517 changes is WHERE the names come
    // from, not what they are matched on. The converse mismatch — a required CONTEXT that names no
    // check-run (a legacy commit status, a renamed job) — would classify a genuinely required check
    // advisory and fail OPEN; `Reads.prLandableRequire` is where that is closed, by never deriving at all
    // while GitHub itself reports the PR refused, and a required context with no check run is exactly the
    // `blocked` state (#1575).
    type AdvisorySet =
        private
        | DerivedFrom of requiredContexts: Set<string>
        | NoDerivation of why: string

    // The reason recorded when the read SUCCEEDED and returned nothing. Held apart from an unreadable
    // policy's reason in the SENTENCE only: both are `NoDerivation`, and the verdict cannot tell them apart.
    let private emptyIsNotADerivation =
        "the base branch requires no status checks, and an empty required set is not a derivation"

    // The reason recorded on the path that never asks: `scoreRequired`/`scoreN`/`score` score every live
    // check because no policy was consulted at all.
    let private noPolicyConsulted = "no branch policy was consulted"

    // THE ONLY CONSTRUCTOR OF A `DerivedFrom` (`.github#2517`), and the only place the empty-set guard
    // lives. Deleting the guard here is what an AC6 gate-inversion mutates, and it must red a fixture.
    let advisoryFrom (requiredContexts: string list) : AdvisorySet =
        let contexts = requiredContexts |> List.filter (fun c -> c <> "") |> Set.ofList

        if contexts.IsEmpty then
            NoDerivation emptyIsNotADerivation
        else
            DerivedFrom contexts

    let noDerivation (why: string) : AdvisorySet = NoDerivation why

    let isDerived (advisory: AdvisorySet) : bool =
        match advisory with
        | DerivedFrom _ -> true
        | NoDerivation _ -> false

    let noDerivationReason (advisory: AdvisorySet) : string option =
        match advisory with
        | DerivedFrom _ -> None
        | NoDerivation why -> Some why

    // How a CI result participates in the landable verdict (#2400): every subject `scoreRequired` scores is
    // EITHER `Blocking` (an ordinary finding — a bad or still-pending result withholds green) or `Advisory`
    // (reported, but never withholds a green verdict on its own, unless the caller `--require`d it by name,
    // #2373). This is the type `score`/`scoreN`/`scoreRequired` now MATCH ON, in place of the private
    // `Set<string>` the rollup used to consult ad hoc: `TreatWarningsAsErrors` (this project's `.fsproj`)
    // turns an incomplete match (FS0025) into a build failure, so a third `Gating` case added later is a
    // compile error at every one of them, not a silently-unfiltered default.
    type private Gating =
        | Blocking
        | Advisory

    // A check-run's `Gating` (#2400's typed classification, #2517's derived input): `Advisory` iff a
    // derivation exists, the derived required set does NOT name this check, AND the caller did not
    // `--require` it by that same name.
    //
    // `required` WINS OVER THE DERIVATION, and must (#2373's opt-in lever, restated as `.github#2517` AC2).
    // `--require registry-coherence` names a check branch protection cannot require — the autofix bot's
    // whole reason for calling this command (#642/#425/#737) — so a derivation that silently overrode the
    // flag would break the one caller the flag exists for. It is tested FIRST here so that no reachable
    // combination of derivation and flag can make a caller-named check advisory.
    //
    // `NoDerivation` IS FAIL-CLOSED: every check is `Blocking`, exactly as before #2517 for every name but
    // `claim-generation`, and exactly as before #2373 for that one.
    let private checkGating (advisory: AdvisorySet) (required: Set<string>) (c: CheckRow) : Gating =
        if required.Contains c.Name then
            Blocking
        else
            match advisory with
            | NoDerivation _ -> Blocking
            | DerivedFrom requiredContexts -> if requiredContexts.Contains c.Name then Blocking else Advisory

    // A subject (run or check) is a FINDING unless it COMPLETED and concluded `success` or `skipped`.
    let private isPending (status: string) = status <> "completed"

    let private isBad (status: string) (conclusion: string option) =
        status = "completed" && conclusion <> Some "success" && conclusion <> Some "skipped"

    // A workflow run's `Gating` (#2400/#2454, closing #2379): a run is `Advisory` only when EVERY check-run
    // in its suite that is itself a FINDING — `isBad` or still `isPending` — is `Advisory`. A check-run that
    // already PASSED takes no part in the decision; the run's own redness is then wholly attributable to
    // findings the BASE BRANCH itself does not require (`.github#2517`'s derived `AdvisorySet` — until then,
    // #2373's hand-written `advisoryCheckNames`).
    //
    // `.github#2454`: the FIRST shipped version of this rule surveyed suite MEMBERSHIP instead of findings —
    // every check-run in the suite, passing ones included, had to be advisory-NAMED for the run to qualify.
    // That keyed the decision on whether a non-advisory check-run EXISTS, never on whether one FAILED, so
    // `coherence.yml`'s real shape (`claim-generation` advisory and failing, five ordinary jobs green) still
    // read `Blocking` — the five PASSING jobs' mere presence held the run's own `failure` conclusion in the
    // rollup, even though none of them was bad. Filtering to FINDINGS first is what makes "solely because
    // its advisory job did" actually mean solely, rather than "and nothing else is present".
    //
    // A run with NO live check-runs in its suite stays `Blocking` (#2379 AC3) — that is `startup_failure`:
    // GitHub failed the run before any job could report, so there is no check-run to attribute the failure
    // to, and calling that "advisory" would silently swallow a whole class of genuine failures that #606
    // exists to catch. A run whose OWN check-runs are all finding-free (every one already passed) also stays
    // `Blocking` — nothing in its suite explains a bad or pending run status, so leaving it scored is the
    // conservative answer. A MIXED suite of findings (one advisory job, one genuinely failing ordinary job —
    // the `registry-coherence` case, #642/#425) also stays `Blocking` (#2379 AC2): one non-advisory FINDING
    // in the suite is enough to keep the run a real finding.
    let private runGating
        (advisory: AdvisorySet)
        (required: Set<string>)
        (liveChecksAll: CheckRow list)
        (r: RunRow)
        : Gating =
        match r.CheckSuiteId with
        | None -> Blocking
        | Some suiteId ->
            match liveChecksAll |> List.filter (fun c -> c.CheckSuiteId = Some suiteId) with
            | [] -> Blocking
            | ownChecks ->
                match ownChecks |> List.filter (fun c -> isPending c.Status || isBad c.Status c.Conclusion) with
                | [] -> Blocking
                | findings ->
                    let allAdvisory =
                        findings
                        |> List.forall (fun c ->
                            match checkGating advisory required c with
                            | Advisory -> true
                            | Blocking -> false)

                    if allAdvisory then Advisory else Blocking

    let private scoredSubjects
        (advisory: AdvisorySet)
        (required: string list)
        (runs: RunRow list)
        (checks: CheckRow list)
        : RunRow list * CheckRow list * CheckRow list =
        let live, _ = supersede runs
        let liveChecksAll = liveChecks runs checks
        let requiredSet = Set.ofList required

        let scoredChecks =
            liveChecksAll
            |> List.filter (fun c ->
                match checkGating advisory requiredSet c with
                | Blocking -> true
                | Advisory -> false)

        let scoredRuns =
            live
            |> List.filter (fun r ->
                match runGating advisory requiredSet liveChecksAll r with
                | Blocking -> true
                | Advisory -> false)

        scoredRuns, scoredChecks, liveChecksAll

    /// Exact identities of the bad runs and check-runs that the same classification used by
    /// `scoreDerived` keeps in its blocking rollup. A registration-race red over zero subjects therefore
    /// returns no failures, while advisory and superseded subjects cannot leak into the diagnostic.
    let failuresDerived
        (advisory: AdvisorySet)
        (required: string list)
        (runs: RunRow list)
        (checks: CheckRow list)
        : Failure list =
        let scoredRuns, scoredChecks, _ = scoredSubjects advisory required runs checks

        [ yield!
              scoredRuns
              |> List.choose (fun r ->
                  if isBad r.Status r.Conclusion then
                      Some(WorkflowRunFailure(r.Path, r.RunNumber, r.Conclusion))
                  else
                      None)
          yield!
              scoredChecks
              |> List.choose (fun c ->
                  if isBad c.Status c.Conclusion then
                      Some(CheckRunFailure(c.Name, c.CheckSuiteId, c.Conclusion))
                  else
                      None) ]

    // The verdict AND the number of subjects it was scored over — runs plus check-runs, after the
    // superseded suites are dropped. `--wait` needs that count and the verdict is not enough: a `red` over
    // ZERO subjects is "CI has not started YET" (normal for the first 20-60s after a push), a `red` over
    // some is a real finding, and only the count tells them apart (#606/#724). A conflicted or unknown
    // verdict is reached before any subject is scored, so its count is 0.
    //
    // `required` NAMES CHECKS THAT MUST HAVE REPORTED (#737). The rollup above answers "is anything red?",
    // and that question is blind to a check that is ABSENT: an absent subject reads exactly like a passing
    // one in any "are all checks green?" test, which is #606's whole lesson. Branch protection covers the
    // REQUIRED set, so the sharp edge is a NON-required check that is nonetheless the reason the PR exists
    // — `registry-coherence` on the skill-registry autofix bot's standing PR, whose redness means "this
    // snapshot is OBSOLETE" and which GitHub's native auto-merge would merge straight past (#642/#425).
    // Naming it here is what lets that bot call this command instead of hand-rolling a fifth copy of the
    // gate (#724).
    //
    // A MISSING REQUIRED CHECK IS `pending`, NOT `red` — deliberately, and it is the one subtle call here.
    // "The check has not reported" is literally the pending sentence, and the state is usually TRANSIENT:
    // GitHub registers a PR's checks over 20-60s, and — worse for a bot that manufactures supersession on
    // every reconcile — a required check whose suite was just SUPERSEDED is absent for the seconds between
    // the drop and its replacement registering. Calling that red would refuse the PR the bot had just
    // pushed, which is #710 restored. `pending` never settles, so `--wait` rides it out on the transient
    // case and, when the check is absent because it was RENAMED, exhausts its tries and refuses — the same
    // no-merge, reached honestly. It cannot fail open: `pending` is never a green.
    //
    // `advisory` IS HOW THE CARVE-OUT WAS POPULATED (`.github#2517`), and it is a parameter rather than a
    // literal because this module is PURE: `required_status_checks.contexts` is IO, so the only honest
    // place to read it is the caller that already makes requests (`Reads.prLandableRequire`). Passing
    // `noDerivation _` reproduces the pre-#2517 rollup exactly, which is why `scoreRequired` below can
    // remain the fail-closed default with an unchanged signature.
    let scoreDerived
        (advisory: AdvisorySet)
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
            let scoredRuns, scoredChecks, liveChecksAll = scoredSubjects advisory required runs checks

            // Advisory checks (#2373/#2400, derived since #2517) are excluded from the bad/pending/total
            // rollup UNLESS the caller explicitly named them in `required` — an opt-in override, never a
            // silent one. This filters only the ROLLUP's own inputs; `missingFrom` below is still handed the
            // FULL `liveChecksAll`, so a `--require`d advisory name is still satisfied by its presence
            // exactly as any other required name would be — only its `bad`/`pending` contribution is what
            // the advisory carve-out withholds by default.
            // The rollup is over BOTH lists (#606): a run can fail with no check-runs at all
            // (`startup_failure`), and a check-run can fail while its run SUCCEEDS (job-level
            // `continue-on-error`). Neither list alone is the truth.
            let pending =
                (scoredRuns |> List.filter (fun r -> isPending r.Status) |> List.length)
                + (scoredChecks |> List.filter (fun c -> isPending c.Status) |> List.length)

            let bad =
                (scoredRuns |> List.filter (fun r -> isBad r.Status r.Conclusion) |> List.length)
                + (scoredChecks |> List.filter (fun c -> isBad c.Status c.Conclusion) |> List.length)

            let total = List.length scoredRuns + List.length scoredChecks

            // A `--require`d check that is not among the LIVE check-runs has not reported. Matched on the
            // live set, so a superseded suite's copy cannot satisfy it — the check that was cancelled is
            // exactly the one whose verdict we do not have. Same derivation `missing` reports from, so the
            // verdict and its reason cannot disagree. Uses the UNFILTERED `liveChecksAll`, not `scoredChecks`
            // — an advisory check the caller `--require`d is still satisfied by its mere presence.
            let missingRequired = missingFrom required liveChecksAll

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

    let missing (required: string list) (runs: RunRow list) (checks: CheckRow list) : string list =
        missingFrom required (liveChecks runs checks)

    let scoreRequired
        (required: string list)
        (mergeable: bool option)
        (runs: RunRow list)
        (checks: CheckRow list)
        : PrState * int =
        scoreDerived (NoDerivation noPolicyConsulted) required mergeable runs checks

    let scoreN (mergeable: bool option) (runs: RunRow list) (checks: CheckRow list) : PrState * int =
        scoreRequired [] mergeable runs checks

    let score (mergeable: bool option) (runs: RunRow list) (checks: CheckRow list) : PrState =
        scoreN mergeable runs checks |> fst

    let settled (state: PrState) (n: int) (prev: int) : bool =
        match state with
        | PrConflicted
        | PrUnknown -> true
        // A CLOSED PR SETTLES AT ONCE, MERGED OR NOT (#1680 AC3). This is the strongest form of the rule
        // the arm above states: no amount of waiting reopens a PR, and a merged one is the most terminal
        // state GitHub has. It is listed separately from `PrConflicted`/`PrUnknown` rather than folded in
        // because the REASON differs — those two settle because waiting cannot IMPROVE them, these because
        // there is no longer a subject to gate at all — and `n` is meaningless for both (there is no live
        // check set on a closed PR, so the count is 0 and must not be consulted).
        | PrMerged
        | PrClosed -> true
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
        // The two words #1680 added. They are the WHOLE of AC2's distinguishability requirement on stdout:
        // a caller reading one word must be able to tell "already landed, nothing to gate" from "checks
        // still running" without a second REST read, and these are the words that do it.
        | PrMerged -> "merged"
        | PrClosed -> "closed"
