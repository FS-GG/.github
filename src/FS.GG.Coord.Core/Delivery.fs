namespace FS.GG.Coord

module Delivery =
    open System
    open System.Security.Cryptography
    open System.Text

    type Stage =
        | Claimed
        | Implementation
        | ReviewReady
        | ReviewActive
        | Accepted
        | Landable
        | MergedAwaitingObligations
        | Done
        | Parked

    type Obligation =
        { Id: string
          Kind: string
          Evidence: string option
          HeadSha: string
          Verified: bool }

    type DeclaredPaths =
        | Known of string list
        | DeclaredNone
        | Undeclared
        | Unread of reason: string

    type Freshness =
        { ItemRef: string
          ClaimGeneration: string
          Executor: string
          Branch: string
          Worktree: string
          PullRequest: int option
          HeadSha: string
          DeclaredPaths: DeclaredPaths
          BoardState: string }

    type Snapshot =
        { Freshness: Freshness
          ItemBranchCanonical: bool
          ClosingLinkageCanonical: bool
          PathsVerified: bool
          InReview: bool
          Review: Driver.ReviewChain option
          ReviewProblem: string option
          Landable: bool
          Merged: bool
          MergeReachable: bool
          IssueClosed: bool
          BoardDone: bool
          ClaimReleased: bool
          PendingWrites: int
          CleanupEligible: bool
          ObligationsDeclared: bool
          Obligations: Obligation list
          ParkedReason: string option }

    type Action =
        | ContinueImplementation
        | RepairReviewHandoff of reason: string
        | MoveToReview
        | AwaitIndependentReview
        | RefreshReview of reason: string
        | AwaitLandability
        | GuardedLand
        | VerifyObligation of name: string
        | Complete
        | CleanupWorktree
        | RouteFollowUp of reason: string

    type Transition =
        { Stage: Stage
          Action: Action
          FreshnessToken: string
          ActionKey: string }

    type Verdict =
        | Next of Transition
        | NoVerdict of reason: string

    let private digest (value: string) =
        value
        |> Encoding.UTF8.GetBytes
        |> SHA256.HashData
        |> Convert.ToHexString
        |> fun hash -> hash.ToLowerInvariant()

    // Folds the CASE in as well as the tokens, so none of the four cases can hash to the same token as
    // another — `Unread "x"` cannot collide with `Known [ "x" ]`, and `DeclaredNone` cannot collide
    // with `Undeclared` or an empty `Known []` — a receipt minted from one fact must never be
    // redeemable against a differently-cased one (.github#2233 scope item 3).
    let private declaredPathsToken =
        function
        | Known paths -> "known\n" + String.concat "\n" paths
        | DeclaredNone -> "declaredNone"
        | Undeclared -> "undeclared"
        | Unread reason -> "unread\n" + reason

    let freshnessToken freshness =
        [ freshness.ItemRef
          freshness.ClaimGeneration
          freshness.Executor
          freshness.Branch
          freshness.Worktree
          freshness.PullRequest |> Option.map string |> Option.defaultValue ""
          freshness.HeadSha
          declaredPathsToken freshness.DeclaredPaths
          freshness.BoardState ]
        |> String.concat "\n"
        |> digest

    let private missing value label =
        if String.IsNullOrWhiteSpace value then Some label else None

    // The reason a decision cannot proceed for the touch-set fact alone. Each of the three empty-ish
    // cases gets its OWN reason text, so a worker reading a `noVerdict` can tell which one it is
    // without opening the issue body (.github#2233 acceptance 4): `Unread` NAMES THE READ as the
    // failure and never the item's own declaration — the reader is at fault, not the item (acceptance
    // 2) — while `DeclaredNone` and `Undeclared` are both facts genuinely read off the body, so they
    // name the item's own state rather than a reader failure, and each keeps the "declared paths"
    // family wording so the omission is still recognizable as the same KIND of gap this validator has
    // always reported (acceptance 2's "the omission reason it answers today").
    //
    // Skipped entirely once the snapshot is `CleanupEligible`: a terminal item (stamped, closed, claim
    // released, nothing pending) has nothing left to touch, so `CleanupWorktree` does not need to know
    // what it once reserved — demanding the field here would block a stamped item's cleanup transition
    // forever on a residual empty fact (.github#2233, the measured `#2225` residue).
    let private declaredPathsProblem snapshot =
        if snapshot.CleanupEligible then
            None
        else
            match snapshot.Freshness.DeclaredPaths with
            | Unread reason -> Some $"declared paths were not read: %s{reason}"
            | Undeclared -> Some "declared paths were never declared (no Paths: line)"
            | DeclaredNone -> Some "declared paths are deliberately empty (Paths: none)"
            | Known [] -> Some "declared paths"
            | Known _ -> None

    let private validate snapshot =
        let freshness = snapshot.Freshness
        [ missing freshness.ItemRef "item ref"
          missing freshness.ClaimGeneration "claim generation"
          missing freshness.Executor "executor identity"
          missing freshness.Branch "branch"
          missing freshness.Worktree "worktree"
          match freshness.PullRequest with
          | Some value when value <= 0 -> Some "pull request"
          | _ -> None
          missing freshness.HeadSha "head SHA"
          missing freshness.BoardState "board state"
          declaredPathsProblem snapshot ]
        |> List.choose id

    let private next snapshot stage action =
        let token = freshnessToken snapshot.Freshness
        let actionKey = digest $"%s{token}\n%A{stage}\n%A{action}"
        Next { Stage = stage; Action = action; FreshnessToken = token; ActionKey = actionKey }

    let private handoffProblem snapshot =
        if not snapshot.ItemBranchCanonical then Some "item branch is not canonical"
        elif not snapshot.ClosingLinkageCanonical then Some "canonical closing linkage is missing"
        elif not snapshot.PathsVerified then Some "declared paths are not verified"
        else None

    // The round ceiling this chain is judged against, read from the ONE policy record that defines
    // both literals (`Protocol.reviewPolicy`) rather than restated here. It was spelled `if
    // review.RepairPhase then 10 else 3` for as long as this function existed, which is the same
    // second-copy-of-one-rule shape `.github#2575` acceptance 2 names — `Driver.receiptFresh` already
    // reads the policy record for exactly this quantity, so the two spellings were one silent reword
    // away from disagreeing. The values are identical today (`MaxAutomatedRepairRounds = 3`,
    // `RepairPhaseMaxRounds = 10`), so this is behaviour-preserving by construction.
    let private reviewCeiling (review: Driver.ReviewChain) =
        if review.RepairPhase then Protocol.reviewPolicy.RepairPhaseMaxRounds
        else Protocol.reviewPolicy.MaxAutomatedRepairRounds

    // What is wrong with the review EVIDENCE — and only that (.github#2575).
    //
    // This reads `Driver.validateReviewChainStructure`, not `Driver.validateReviewChain`. The full
    // list also carries one LIVENESS clause, "review checks are not green", which is a fact about a CI
    // run that has not reported yet rather than anything wrong with what the critic and host durably
    // wrote. Folding it in here made a complete, host-accepted chain report `stage: reviewActive,
    // action: refreshReview` — the state whose taught recovery is to go looking for a repair round
    // that does not exist. `.github#2549` removed exactly that conflation from `review`; leaving it
    // here meant the verb `review`'s own `authorizeDelivery` action routes to answered the corrected
    // question with the uncorrected answer.
    //
    // Worse, the check it fell over was structurally unable to be green yet: `claim-generation` cannot
    // report until the live `delivery` call PATCHes `fsgg:pr-authorization` onto the head
    // (`.github#2504`), and that call is the very one whose output this is.
    //
    // `Driver.validateReviewChain` is untouched and remains the single definition of chain validity;
    // the structural/liveness split lives once, at its source, in `Driver.reviewChainProblems`
    // (acceptance 2). Delivery does not fork a second copy and cannot drift from it.
    let private reviewProblem snapshot =
        match snapshot.ReviewProblem, snapshot.Review with
        | Some problem, _ when not (System.String.IsNullOrWhiteSpace problem) -> Some problem
        | _, None -> Some "independent review evidence is absent"
        | _, Some review when review.HeadSha <> Some snapshot.Freshness.HeadSha ->
            Some "independent review is for a different head SHA"
        | _, Some review ->
            match Driver.validateReviewChainStructure (reviewCeiling review) review with
            | [] -> None
            | errors -> Some(String.concat "; " errors)

    // The one liveness fact `reviewProblem` above deliberately no longer reports, kept as a SEPARATE
    // question so that dropping it from the review problem list cannot loosen what may merge.
    //
    // This guard is load-bearing rather than defensive. In the live adapter `Landable` and the chain's
    // `ChecksGreen` are both derived from the same `landable = PrGreen` read, so they cannot disagree
    // there — but a SUPPLIED snapshot carries them as two independent fields, and host-measured
    // against the pre-fix engine, `landable=true` with `checksGreen=false` was held short of
    // `GuardedLand` by the checks clause inside `reviewProblem` and by nothing else. Removing that
    // clause without this guard would have made that combination authorize a merge on a chain whose
    // own checks are not green, which is the one thing `.github#2575` acceptance 6 forbids. The
    // answer stays `Landable`/`AwaitLandability`: fail-closed, and no new merge authority anywhere.
    let private reviewChecksPending snapshot =
        snapshot.Review |> Option.exists (fun review -> not review.ChecksGreen)

    let fromReviewAcceptance (receipt: Review.AcceptedReceipt) (snapshot: Snapshot) : Snapshot =
        let chain: Driver.ReviewChain =
            { MarkerValid = true
              CriticIdentity = Some receipt.CriticIdentity
              HeadSha = Some receipt.HeadSha
              Rounds = receipt.Rounds
              RepairPhase = receipt.RepairPhase
              ChecksGreen = receipt.ChecksGreen
              HostAccepted = true
              RuntimeRouteEvidence = receipt.RuntimeRouteEvidence
              DiffAuditRequired = receipt.DiffAuditRequired
              DiffAuditHead = receipt.DiffAuditHead }
        { snapshot with Review = Some chain; ReviewProblem = None }

    let inspect snapshot =
        match validate snapshot with
        | missingFacts when not (List.isEmpty missingFacts) ->
            let names = String.concat ", " missingFacts
            NoVerdict $"delivery facts are incomplete: %s{names}"
        | _ when snapshot.PendingWrites < 0 ->
            NoVerdict "pending board writes cannot be negative"
        | _ ->
            match snapshot.ParkedReason with
            | Some reason when not (String.IsNullOrWhiteSpace reason) -> next snapshot Parked (RouteFollowUp reason)
            | _ when snapshot.Merged ->
                if not snapshot.ObligationsDeclared then NoVerdict "delivery obligations are undeclared"
                else
                    match snapshot.Obligations |> List.tryFind (fun obligation -> not obligation.Verified) with
                    | Some obligation -> next snapshot MergedAwaitingObligations (VerifyObligation obligation.Id)
                    | None when not snapshot.MergeReachable -> NoVerdict "merged pull request is not reachable from the default branch"
                    | None when not snapshot.IssueClosed -> next snapshot MergedAwaitingObligations Complete
                    | None when not snapshot.BoardDone -> next snapshot MergedAwaitingObligations Complete
                    | None when not snapshot.ClaimReleased -> next snapshot MergedAwaitingObligations Complete
                    | None when snapshot.PendingWrites <> 0 -> next snapshot MergedAwaitingObligations Complete
                    | None when not snapshot.CleanupEligible -> NoVerdict "cleanup is not eligible before completion"
                    | None -> next snapshot Done CleanupWorktree
            | _ when Option.isNone snapshot.Freshness.PullRequest ->
                next snapshot Implementation ContinueImplementation
            | _ ->
                match handoffProblem snapshot with
                | Some problem -> next snapshot ReviewReady (RepairReviewHandoff problem)
                | None when not snapshot.InReview -> next snapshot ReviewReady MoveToReview
                | None when not snapshot.ObligationsDeclared -> next snapshot ReviewReady (RepairReviewHandoff "delivery obligations are undeclared")
                | None ->
                    match reviewProblem snapshot with
                    | Some "independent review evidence is absent" -> next snapshot ReviewActive AwaitIndependentReview
                    | Some problem -> next snapshot ReviewActive (RefreshReview problem)
                    // Nothing is wrong with the evidence. What remains is a CI verdict, and the stage
                    // that names it already exists: `Landable`/`AwaitLandability` is what this same
                    // snapshot answered the moment `checksGreen` flipped true, and it is also literally
                    // the next step `pnext-item` section 6 prescribes after the live `delivery` call —
                    // wait on `landable` for this exact head. So the two snapshots the finding was
                    // measured with no longer differ in their REVIEW stage, and no parallel vocabulary
                    // is invented for a window `Landable` already describes (acceptance 1 and 3).
                    | None when reviewChecksPending snapshot || not snapshot.Landable ->
                        next snapshot Landable AwaitLandability
                    | None -> next snapshot Accepted GuardedLand

    let advance freshnessToken actionKey snapshot =
        match inspect snapshot with
        | Next transition when transition.FreshnessToken = freshnessToken && transition.ActionKey = actionKey ->
            Next transition
        | Next _ ->
            NoVerdict "delivery receipt is stale or does not authorize this transition"
        | NoVerdict reason -> NoVerdict reason
