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

    /// A decision-boundary touch-set fact: either genuinely read (empty or not — a chore, an explicit
    /// `Paths: none`, and a body with no `Paths:` line at all are ALL `Known []` here; that omission
    /// distinction belongs to Schedulability, not to this decision), or NOT READ, which must say why
    /// rather than pose as `Known []`. See `.github#2233`: an `Unreadable` touch-set silently collapsing
    /// to `[]` let a fact nobody looked at answer a scheduling verdict as if it were a genuine omission.
    type DeclaredPaths =
        | Known of string list
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
          /// Parser failures are evidence that review was attempted but is malformed; retaining the
          /// diagnostic keeps delivery from misdirecting the holder to wait for a review that exists.
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

    /// Folds the CASE in as well as the tokens, so `Unread "x"` cannot hash to the same token as
    /// `Known []`, or as `Known [ "x" ]` — a receipt minted from a read fact must never be redeemable
    /// against an unread one, and vice versa (.github#2233 scope item 3).
    let private declaredPathsToken =
        function
        | Known paths -> "known\n" + String.concat "\n" paths
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

    /// The reason a decision cannot proceed for the touch-set fact alone. An unread fact NAMES THE READ
    /// as the failure and never the item's own declaration — the reader is at fault, not the item
    /// (.github#2233 acceptance 2). A genuinely empty, READ touch-set answers the same generic
    /// "declared paths" reason this validator has always given a `Paths:`-less item.
    ///
    /// Skipped entirely once the snapshot is `CleanupEligible`: a terminal item (stamped, closed, claim
    /// released, nothing pending) has nothing left to touch, so `CleanupWorktree` does not need to know
    /// what it once reserved — demanding the field here would block a stamped item's cleanup transition
    /// forever on a residual `[]` (.github#2233, the measured `#2225` residue).
    let private declaredPathsProblem snapshot =
        if snapshot.CleanupEligible then
            None
        else
            match snapshot.Freshness.DeclaredPaths with
            | Unread reason -> Some $"declared paths were not read: %s{reason}"
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

    let private reviewProblem snapshot =
        match snapshot.ReviewProblem, snapshot.Review with
        | Some problem, _ when not (System.String.IsNullOrWhiteSpace problem) -> Some problem
        | _, None -> Some "independent review evidence is absent"
        | _, Some review when review.HeadSha <> Some snapshot.Freshness.HeadSha ->
            Some "independent review is for a different head SHA"
        | _, Some review ->
            let ceiling = if review.RepairPhase then 10 else 3
            match Driver.validateReviewChain ceiling review with
            | [] -> None
            | errors -> Some(String.concat "; " errors)

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
                    | None when not snapshot.Landable -> next snapshot Landable AwaitLandability
                    | None -> next snapshot Accepted GuardedLand

    let advance freshnessToken actionKey snapshot =
        match inspect snapshot with
        | Next transition when transition.FreshnessToken = freshnessToken && transition.ActionKey = actionKey ->
            Next transition
        | Next _ ->
            NoVerdict "delivery receipt is stale or does not authorize this transition"
        | NoVerdict reason -> NoVerdict reason
