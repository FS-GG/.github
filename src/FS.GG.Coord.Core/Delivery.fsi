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

    /// A decision-boundary touch-set fact, drawing the same three-way distinction
    /// `Schedulability.NoTouchSet`/`DeliberatelyNoTouchSet` already draws for scheduling, so a
    /// consumer of `Delivery`'s own output can tell the three apart without opening the issue body
    /// (.github#2233 acceptance 4):
    ///   - `Known` — tokens actually declared and read (includes the `any` chore sentinel).
    ///   - `DeclaredNone` — an explicit, read `Paths: none`: a DELIBERATE empty reservation.
    ///   - `Undeclared` — a read body with no `Paths:` line at all: nobody ever declared one.
    ///   - `Unread reason` — the body was never read. This is UNKNOWN, not absent, and must never be
    ///     supplied as any of the three read cases above: a caller that has not read the touch-set has
    ///     to say so, or a decision boundary would treat a fact it never saw as a confident read.
    type DeclaredPaths =
        | Known of string list
        | DeclaredNone
        | Undeclared
        | Unread of reason: string

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
          DeclaredPaths: DeclaredPaths
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

    /// Fold one `FS.GG.Coord.Review.AcceptedReceipt` (.github#2175) into a snapshot's `Review`/
    /// `ReviewProblem` facts. `Review` owns the alternating critic/implementer state graph inside
    /// `ReviewActive`; this is the one place that graph's OUTPUT — the accepted-current-head receipt —
    /// reaches `Delivery`, and it reaches it as the SAME `Driver.ReviewChain` shape `Delivery` has always
    /// consumed. Additive and non-breaking: no existing `Stage`, `Action`, or `Snapshot` field changes,
    /// and no existing caller is required to route through this function.
    val fromReviewAcceptance: receipt: FS.GG.Coord.Review.AcceptedReceipt -> snapshot: Snapshot -> Snapshot

    /// Derive the sole legal next action from a complete snapshot; unreadable or contradictory facts
    /// never become a permissive lifecycle state.
    val inspect: Snapshot -> Verdict
