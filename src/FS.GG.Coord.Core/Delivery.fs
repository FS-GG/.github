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

    type Freshness =
        { ItemRef: string
          ClaimGeneration: string
          Executor: string
          Branch: string
          Worktree: string
          PullRequest: int option
          HeadSha: string
          DeclaredPaths: string list
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

    let freshnessToken freshness =
        [ freshness.ItemRef
          freshness.ClaimGeneration
          freshness.Executor
          freshness.Branch
          freshness.Worktree
          freshness.PullRequest |> Option.map string |> Option.defaultValue ""
          freshness.HeadSha
          String.concat "\n" freshness.DeclaredPaths
          freshness.BoardState ]
        |> String.concat "\n"
        |> digest

    let private missing value label =
        if String.IsNullOrWhiteSpace value then Some label else None

    let private validate freshness =
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
          if List.isEmpty freshness.DeclaredPaths then Some "declared paths" else None ]
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
        match validate snapshot.Freshness with
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
