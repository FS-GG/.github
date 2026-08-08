namespace FS.GG.Coord

/// Pure, fail-closed lifecycle decisions for a single claimed coordination item.
module Delivery =
    /// The durable stage established by the facts currently available for an item.
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

    /// A release, publication, registry, dispatch, or deployment receipt required after merge.
    type Obligation =
        { Id: string
          Kind: string
          Evidence: string option
          HeadSha: string
          Verified: bool }

    /// Facts which must stay identical between inspection and a following mutating transition.
    type Freshness =
        { ItemRef: string
          ClaimGeneration: string
          Executor: string
          Branch: string
          Worktree: string
          /// None while the claimed work has not reached a reviewable pull request.
          PullRequest: int option
          HeadSha: string
          DeclaredPaths: string list
          BoardState: string }

    /// The complete delivery fact set read by the application/GitHub boundary.
    type Snapshot =
        { Freshness: Freshness
          ItemBranchCanonical: bool
          ClosingLinkageCanonical: bool
          PathsVerified: bool
          InReview: bool
          Review: Driver.ReviewChain option
          /// Parser failures are evidence that review was attempted but is malformed.
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

    /// The one action a worker or host may take next. Judgement remains outside this union.
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

    /// A transition carries the exact fact token and a deterministic idempotency key.
    type Transition =
        { Stage: Stage
          Action: Action
          FreshnessToken: string
          ActionKey: string }

    type Verdict =
        | Next of Transition
        | NoVerdict of reason: string

    /// Confirm that a caller is still advancing the exact transition it inspected.  A changed claim,
    /// PR head, board state, or declared touch-set makes the receipt unusable rather than permitting a
    /// write against newer facts.
    val advance: freshnessToken: string -> actionKey: string -> Snapshot -> Verdict

    /// Hash the complete mutation subject. Callers must present this token when advancing it.
    val freshnessToken: Freshness -> string

    /// Derive the sole legal next action from a complete snapshot; unreadable or contradictory facts
    /// never become a permissive lifecycle state.
    val inspect: Snapshot -> Verdict
