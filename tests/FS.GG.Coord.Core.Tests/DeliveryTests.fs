namespace FS.GG.Coord.Tests

open Xunit
open FS.GG.Coord

module DeliveryTests =
    let review head : Driver.ReviewChain =
        { MarkerValid = true
          Subject = None
          ClaimGeneration = None
          BaseSha = None
          CriticIdentity = Some "kite"
          HeadSha = Some head
          Rounds = [ 1 ]
          RepairPhase = false
          ChecksGreen = true
          HostAccepted = true
          RuntimeRouteEvidence = Some(Driver.NotMeaningful "pure lifecycle test")
          DiffAuditRequired = false
          DiffAuditHead = None }

    let freshness head : Delivery.Freshness =
        { ItemRef = "FS-GG/.github#2131"
          ClaimGeneration = "5165723183"
          Executor = "wren-c948"
          Branch = "item/2131-claim-to-done-lifecycle"
          Worktree = "/worktrees/2131"
          PullRequest = Some 99
          HeadSha = head
          DeclaredPaths = Delivery.Known [ "src/FS.GG.Coord.Core" ]
          BoardState = "In progress" }

    let snapshot head : Delivery.Snapshot =
        { Freshness = freshness head
          ItemBranchCanonical = true
          ClosingLinkageCanonical = true
          PathsVerified = true
          InReview = true
          Review = Some(review head)
          ReviewProblem = None
          Landable = true
          Merged = false
          MergeReachable = false
          IssueClosed = false
          BoardDone = false
          ClaimReleased = false
          PendingWrites = 0
          CleanupEligible = false
          ObligationsDeclared = true
          Obligations = []
          ParkedReason = None }

    let transition expectedStage expectedAction result =
        match result with
        | Delivery.Next value ->
            Assert.Equal(expectedStage, value.Stage)
            Assert.Equal(expectedAction, value.Action)
            Assert.NotEmpty(value.FreshnessToken)
            Assert.NotEmpty(value.ActionKey)
        | Delivery.NoVerdict reason -> failwith reason

    [<Fact>]
    let ``#2131 clean no-obligation item reaches guarded land with a stable action key`` () =
        let first = Delivery.inspect (snapshot "head-a")
        let second = Delivery.inspect (snapshot "head-a")
        transition Delivery.Accepted Delivery.GuardedLand first
        match first, second with
        | Delivery.Next left, Delivery.Next right -> Assert.Equal(left.ActionKey, right.ActionKey)
        | _ -> failwith "expected transitions"

    [<Fact>]
    let ``#2131 a claimed item without a pull request stays in implementation`` () =
        let prePr =
            { snapshot "head-a" with
                Freshness = { freshness "head-a" with PullRequest = None }
                ItemBranchCanonical = false
                ClosingLinkageCanonical = false
                PathsVerified = false
                InReview = false
                Review = None }
        transition Delivery.Implementation Delivery.ContinueImplementation (Delivery.inspect prePr)

    [<Fact>]
    let ``#2131 an advance receipt cannot authorize a changed head`` () =
        match Delivery.inspect (snapshot "head-a") with
        | Delivery.Next receipt ->
            match Delivery.advance receipt.FreshnessToken receipt.ActionKey (snapshot "head-b") with
            | Delivery.NoVerdict reason -> Assert.Contains("stale", reason)
            | result -> failwithf "expected a stale receipt refusal, got %A" result
        | result -> failwithf "expected a receipt, got %A" result

    [<Fact>]
    let ``#2131 changed head after acceptance requires fresh review`` () =
        let changed = { snapshot "head-b" with Review = Some(review "head-a") }
        transition Delivery.ReviewActive (Delivery.RefreshReview "independent review is for a different head SHA") (Delivery.inspect changed)

    [<Fact>]
    let ``#2207 malformed review evidence is refreshed rather than reported absent`` () =
        let malformed = { snapshot "head-a" with Review = None; ReviewProblem = Some "comment 42 is missing the required 'verdict' field" }
        transition Delivery.ReviewActive (Delivery.RefreshReview "comment 42 is missing the required 'verdict' field") (Delivery.inspect malformed)

    [<Fact>]
    let ``#2131 missing closing linkage blocks review handoff`` () =
        let invalid = { snapshot "head-a" with ClosingLinkageCanonical = false; InReview = false; Review = None }
        transition Delivery.ReviewReady (Delivery.RepairReviewHandoff "canonical closing linkage is missing") (Delivery.inspect invalid)

    [<Fact>]
    let ``#2131 landing requires an explicit machine obligation declaration`` () =
        let undeclared = { snapshot "head-a" with ObligationsDeclared = false }
        transition Delivery.ReviewReady (Delivery.RepairReviewHandoff "delivery obligations are undeclared") (Delivery.inspect undeclared)

    [<Fact>]
    let ``#2131 merged package item remains nonterminal until its obligation verifies`` () =
        let pending =
            { snapshot "head-a" with
                Merged = true
                Obligations = [ ({ Id = "nuget"; Kind = "publication"; Evidence = None; HeadSha = "head-a"; Verified = false }: Delivery.Obligation) ] }
        transition Delivery.MergedAwaitingObligations (Delivery.VerifyObligation "nuget") (Delivery.inspect pending)

    [<Fact>]
    let ``#2131 merged item routes through completion before cleanup`` () =
        let awaitingStamp =
            { snapshot "head-a" with
                Merged = true
                MergeReachable = true
                IssueClosed = false }
        transition Delivery.MergedAwaitingObligations Delivery.Complete (Delivery.inspect awaitingStamp)

    [<Fact>]
    let ``#2131 cleanup is refused before every done fact agrees`` () =
        let incomplete =
            { snapshot "head-a" with
                Merged = true
                MergeReachable = true
                IssueClosed = true
                BoardDone = true
                ClaimReleased = true
                CleanupEligible = false }
        match Delivery.inspect incomplete with
        | Delivery.NoVerdict reason -> Assert.Contains("cleanup is not eligible", reason)
        | result -> failwithf "expected no-verdict, got %A" result

    [<Fact>]
    let ``#2131 a fully observed terminal item exposes cleanup only after release and zero pending writes`` () =
        let terminal =
            { snapshot "head-a" with
                Merged = true
                MergeReachable = true
                IssueClosed = true
                BoardDone = true
                ClaimReleased = true
                PendingWrites = 0
                CleanupEligible = true }
        transition Delivery.Done Delivery.CleanupWorktree (Delivery.inspect terminal)

    // -- .github#2233: `Unreadable` no longer collapses into the same `[]` a genuine omission answers --

    [<Fact>]
    let ``#2233 an unread touch-set names the read as the failure`` () =
        let unread =
            { snapshot "head-a" with
                Freshness = { freshness "head-a" with DeclaredPaths = Delivery.Unread "issue body fetch timed out" } }
        match Delivery.inspect unread with
        | Delivery.NoVerdict reason ->
            Assert.Contains("were not read", reason)
            Assert.Contains("issue body fetch timed out", reason)
        | result -> failwithf "expected no-verdict, got %A" result

    [<Fact>]
    let ``#2233 a genuine paths-less item still answers the omission reason it answered before this change`` () =
        let empty =
            { snapshot "head-a" with
                Freshness = { freshness "head-a" with DeclaredPaths = Delivery.Known [] } }
        match Delivery.inspect empty with
        | Delivery.NoVerdict reason -> Assert.Equal("delivery facts are incomplete: declared paths", reason)
        | result -> failwithf "expected no-verdict, got %A" result

    [<Fact>]
    let ``#2233 an unread touch-set and a genuine omission answer different NoVerdict reasons`` () =
        let unreadReason =
            match Delivery.inspect { snapshot "head-a" with Freshness = { freshness "head-a" with DeclaredPaths = Delivery.Unread "issue body fetch timed out" } } with
            | Delivery.NoVerdict reason -> reason
            | result -> failwithf "expected no-verdict, got %A" result
        let omittedReason =
            match Delivery.inspect { snapshot "head-a" with Freshness = { freshness "head-a" with DeclaredPaths = Delivery.Known [] } } with
            | Delivery.NoVerdict reason -> reason
            | result -> failwithf "expected no-verdict, got %A" result
        Assert.NotEqual<string>(unreadReason, omittedReason)

    [<Fact>]
    let ``#2233 an unread touch-set at a terminal snapshot does not block cleanup`` () =
        // A stamped, closed, claim-released item has nothing left to touch; CleanupWorktree does not
        // need to know what it once reserved, so an `Unread` (or `Known []`) fact must not block it.
        let terminalButUnread =
            { snapshot "head-a" with
                Freshness = { freshness "head-a" with DeclaredPaths = Delivery.Unread "issue body fetch timed out" }
                Merged = true
                MergeReachable = true
                IssueClosed = true
                BoardDone = true
                ClaimReleased = true
                PendingWrites = 0
                CleanupEligible = true }
        transition Delivery.Done Delivery.CleanupWorktree (Delivery.inspect terminalButUnread)

    [<Fact>]
    let ``#2233 a freshness token computed over Unread differs from one computed over Known empty`` () =
        let known = { freshness "head-a" with DeclaredPaths = Delivery.Known [] }
        let unread = { freshness "head-a" with DeclaredPaths = Delivery.Unread "issue body fetch timed out" }
        Assert.NotEqual<string>(Delivery.freshnessToken known, Delivery.freshnessToken unread)

    [<Fact>]
    let ``#2233 the case is folded in, not just the text — an Unread reason cannot collide with the Known path it textually matches`` () =
        // Without a case tag, `Unread "shared text"` and `Known [ "shared text" ]` fold to the exact
        // same joined string, so this is the collision the case fold exists to prevent.
        let known = { freshness "head-a" with DeclaredPaths = Delivery.Known [ "shared text" ] }
        let unread = { freshness "head-a" with DeclaredPaths = Delivery.Unread "shared text" }
        Assert.NotEqual<string>(Delivery.freshnessToken known, Delivery.freshnessToken unread)

    [<Fact>]
    let ``#2233 a freshness token distinguishes two different unread reasons`` () =
        let timedOut = { freshness "head-a" with DeclaredPaths = Delivery.Unread "issue body fetch timed out" }
        let rateLimited = { freshness "head-a" with DeclaredPaths = Delivery.Unread "rate limited" }
        Assert.NotEqual<string>(Delivery.freshnessToken timedOut, Delivery.freshnessToken rateLimited)

    [<Fact>]
    let ``#2233 a receipt minted from a read fact cannot be redeemed against an unread one`` () =
        // Both snapshots are terminal/CleanupEligible so DeclaredPaths never blocks `inspect` itself for
        // either — the only thing that can differ between them is the freshness token, which is exactly
        // what `advance` re-checks before authorizing the SAME transition off newer facts.
        let terminal (snapshot: Delivery.Snapshot) =
            { snapshot with
                Merged = true
                MergeReachable = true
                IssueClosed = true
                BoardDone = true
                ClaimReleased = true
                PendingWrites = 0
                CleanupEligible = true }
        let read =
            terminal { snapshot "head-a" with Freshness = { freshness "head-a" with DeclaredPaths = Delivery.Known [ "src" ] } }
        let receipt =
            match Delivery.inspect read with
            | Delivery.Next transition -> transition
            | result -> failwithf "expected a receipt, got %A" result
        let sameShapeButUnread =
            { read with Freshness = { read.Freshness with DeclaredPaths = Delivery.Unread "issue body fetch timed out" } }
        match Delivery.advance receipt.FreshnessToken receipt.ActionKey sameShapeButUnread with
        | Delivery.NoVerdict reason -> Assert.Contains("stale", reason)
        | result -> failwithf "expected a stale receipt refusal, got %A" result

    // -- repair round 1 (critic `crake-0420`, PR #2301): acceptance 4's full three-way distinction --

    let private noVerdictReason (declaredPaths: Delivery.DeclaredPaths) =
        match Delivery.inspect { snapshot "head-a" with Freshness = { freshness "head-a" with DeclaredPaths = declaredPaths } } with
        | Delivery.NoVerdict reason -> reason
        | result -> failwithf "expected no-verdict, got %A" result

    [<Fact>]
    let ``#2233 DeclaredNone, Undeclared and Unread each answer their own NoVerdict reason`` () =
        let declaredNoneReason = noVerdictReason Delivery.DeclaredNone
        let undeclaredReason = noVerdictReason Delivery.Undeclared
        let unreadReason = noVerdictReason (Delivery.Unread "issue body fetch timed out")
        // Pairwise distinct: a worker can tell which of the three it is without opening the issue body.
        Assert.NotEqual<string>(declaredNoneReason, undeclaredReason)
        Assert.NotEqual<string>(declaredNoneReason, unreadReason)
        Assert.NotEqual<string>(undeclaredReason, unreadReason)
        // Only the unread reason blames the read; the other two name the item's own read state.
        Assert.Contains("were not read", unreadReason)
        Assert.DoesNotContain("were not read", declaredNoneReason)
        Assert.DoesNotContain("were not read", undeclaredReason)
        Assert.Contains("Paths: none", declaredNoneReason)
        Assert.Contains("no Paths: line", undeclaredReason)

    [<Fact>]
    let ``#2233 a freshness token distinguishes DeclaredNone from Undeclared from Known empty`` () =
        let declaredNone = { freshness "head-a" with DeclaredPaths = Delivery.DeclaredNone }
        let undeclared = { freshness "head-a" with DeclaredPaths = Delivery.Undeclared }
        let known = { freshness "head-a" with DeclaredPaths = Delivery.Known [] }
        let tokens = [ Delivery.freshnessToken declaredNone; Delivery.freshnessToken undeclared; Delivery.freshnessToken known ]
        Assert.Equal(3, tokens |> List.distinct |> List.length)

    [<Fact>]
    let ``#2233 DeclaredNone and Undeclared at a terminal snapshot do not block cleanup either`` () =
        let terminal (snapshot: Delivery.Snapshot) =
            { snapshot with
                Merged = true
                MergeReachable = true
                IssueClosed = true
                BoardDone = true
                ClaimReleased = true
                PendingWrites = 0
                CleanupEligible = true }
        for declaredPaths in [ Delivery.DeclaredNone; Delivery.Undeclared ] do
            let terminalSnapshot =
                terminal { snapshot "head-a" with Freshness = { freshness "head-a" with DeclaredPaths = declaredPaths } }
            transition Delivery.Done Delivery.CleanupWorktree (Delivery.inspect terminalSnapshot)

    /// .github#2175 acceptance 10: `Review.AcceptedReceipt` folds into `Snapshot` as exactly the
    /// `Driver.ReviewChain` shape `Delivery` has always consumed — no `Stage`/`Action`/`Snapshot` type
    /// changed to carry it, so this receipt is landable through the SAME `Delivery.inspect` this file's
    /// other tests exercise unmodified.
    [<Fact>]
    let ``#2175 fromReviewAcceptance folds a Review receipt into the same Driver.ReviewChain shape Delivery already reads`` () =
        let receipt: Review.AcceptedReceipt =
            { HeadSha = "head-a"
              CriticIdentity = "kite"
              Rounds = [ 1 ]
              RepairPhase = false
              ChecksGreen = true
              RuntimeRouteEvidence = Some(Driver.NotMeaningful "pure lifecycle test")
              DiffAuditRequired = false
              DiffAuditHead = None }

        // Start from a snapshot whose `Review`/`ReviewProblem` are UNKNOWN (as they would be before a
        // caller has consulted `Review.inspect`), so folding the receipt in is what makes it landable.
        let unfolded =
            { snapshot "head-a" with
                Review = None
                ReviewProblem = Some "independent review evidence is absent" }

        let folded = Delivery.fromReviewAcceptance receipt unfolded

        Assert.Equal((None: string option), folded.ReviewProblem)
        match folded.Review with
        | Some chain ->
            Assert.Equal(Some "head-a", chain.HeadSha)
            Assert.Equal(Some "kite", chain.CriticIdentity)
            Assert.True(chain.MarkerValid)
            Assert.True(chain.HostAccepted)
            Assert.True(chain.ChecksGreen)
        | None -> failwith "fromReviewAcceptance must populate Review"

        // Landable through the EXACT SAME `Delivery.inspect` path every other snapshot in this file
        // uses — `fromReviewAcceptance` is additive, not a second lifecycle.
        transition Delivery.Landable Delivery.AwaitLandability (Delivery.inspect { folded with Landable = false })
        transition Delivery.Accepted Delivery.GuardedLand (Delivery.inspect folded)

        // A receipt for a DIFFERENT head than the snapshot's current head does not silently authorize
        // landing at the wrong commit — `reviewProblem`'s existing head-match check applies unchanged.
        let staleFolded = Delivery.fromReviewAcceptance receipt { unfolded with Freshness = { unfolded.Freshness with HeadSha = "head-b" } }
        match Delivery.inspect staleFolded with
        | Delivery.Next transition when transition.Action = Delivery.GuardedLand ->
            failwith "GATE INVERSION: a receipt for a stale head was landable"
        | Delivery.Next transition -> Assert.Equal(Delivery.ReviewActive, transition.Stage)
        | Delivery.NoVerdict _ -> ()

    // -- .github#2575: `delivery` no longer folds check state into a REVIEW problem ------------------
    //
    // The finding was measured through the compiled engine on two supplied snapshots differing ONLY in
    // `checksGreen`. Host-measured against `main` at ae9b0dd6, BEFORE this change:
    //
    //   checksGreen=false                 -> {"stage":"reviewActive","action":"refreshReview"}
    //   checksGreen=true                  -> {"stage":"landable","action":"awaitLandability"}
    //   checksGreen=false, landable=true  -> {"stage":"reviewActive","action":"refreshReview"}
    //
    // The third line is the counterfactual that makes `reviewChecksPending` load-bearing rather than
    // defensive: before this change, the checks clause INSIDE `reviewProblem` was the only thing
    // holding that combination short of `GuardedLand`. These tests are written against `Delivery.
    // inspect` itself — they CALL the decision rather than assert on its source text — so they cannot
    // pass vacuously (.github#2551).

    /// A snapshot whose review evidence is complete and accepted at the current head, parameterised on
    /// the two facts the finding varies. Everything else is the file's ordinary landable snapshot, so
    /// any difference these tests observe is attributable to the parameters and nothing else.
    let private checksSnapshot checksGreen landable =
        { snapshot "head-a" with
            Review = Some { review "head-a" with ChecksGreen = checksGreen }
            Landable = landable }

    [<Fact>]
    let ``#2575 two snapshots differing only in checksGreen no longer differ in their review stage`` () =
        let pending = Delivery.inspect (checksSnapshot false false)
        let green = Delivery.inspect (checksSnapshot true false)

        // The pre-change answer for `pending` was `ReviewActive`/`RefreshReview "review checks are not
        // green"`. Naming it explicitly, rather than only asserting the two agree, keeps this red on the
        // pre-change engine even if some later edit moved `green` too.
        transition Delivery.Landable Delivery.AwaitLandability pending
        transition Delivery.Landable Delivery.AwaitLandability green

        match pending, green with
        | Delivery.Next left, Delivery.Next right ->
            Assert.Equal(left.Stage, right.Stage)
            Assert.Equal(left.Action, right.Action)
        | _ -> failwith "expected transitions from both snapshots"

    [<Fact>]
    let ``#2575 a complete chain whose checks are not green is never reported as a review problem`` () =
        match Delivery.inspect (checksSnapshot false false) with
        | Delivery.Next { Stage = Delivery.ReviewActive } ->
            failwith "an accepted chain awaiting a check that cannot be green yet was reported as an active review"
        | Delivery.Next { Action = Delivery.RefreshReview reason } ->
            failwithf "a check-state fact was reported as a review repair: %s" reason
        | Delivery.Next _ -> ()
        | Delivery.NoVerdict reason -> failwith reason

    [<Fact>]
    let ``#2575 a structurally broken chain is still a review problem, and never blamed on the checks`` () =
        // Non-vacuity leg: the structural half of `Driver.reviewChainProblems` must still reach
        // `RefreshReview`, so the test above is measuring the split and not a validator that stopped
        // reporting anything at all. `ChecksGreen = false` on the SAME snapshot proves the two clauses
        // are read independently.
        let broken =
            { snapshot "head-a" with
                Review = Some { review "head-a" with ChecksGreen = false; Rounds = [ 1; 2; 3; 4 ] } }
        match Delivery.inspect broken with
        | Delivery.Next { Stage = Delivery.ReviewActive; Action = Delivery.RefreshReview reason } ->
            Assert.Contains("round ceiling exceeded", reason)
            Assert.DoesNotContain("checks are not green", reason)
        | result -> failwithf "expected a structural review problem, got %A" result

    [<Fact>]
    let ``#2575 a snapshot claiming landable over a chain whose checks are not green still cannot land`` () =
        // GATE INVERSION EVIDENCE. Deleting `reviewChecksPending` from `inspect`'s guard makes exactly
        // this case reach `Accepted`/`GuardedLand`, because in a SUPPLIED snapshot `landable` and the
        // chain's `checksGreen` are independent fields. Verified by mutation: with the guard removed
        // this assertion reds and the rest of the file stays green.
        match Delivery.inspect (checksSnapshot false true) with
        | Delivery.Next { Action = Delivery.GuardedLand } ->
            failwith "GATE INVERSION: a chain whose own checks are not green authorized a guarded landing"
        | _ -> ()

        transition Delivery.Landable Delivery.AwaitLandability (Delivery.inspect (checksSnapshot false true))

    [<Fact>]
    let ``#2575 the merge gate itself is unchanged: green checks and a landable pull request still land`` () =
        transition Delivery.Accepted Delivery.GuardedLand (Delivery.inspect (checksSnapshot true true))

    [<Fact>]
    let ``#2575 reading the ceiling from Protocol.reviewPolicy preserves both literals`` () =
        // The refactor replaced a hand-written `if review.RepairPhase then 10 else 3` with the policy
        // record. Pin both boundaries so a future policy edit cannot silently move this validator, and
        // so the refactor is demonstrably behaviour-preserving rather than asserted to be.
        let atCeiling rounds repairPhase =
            { snapshot "head-a" with
                Review = Some { review "head-a" with Rounds = rounds; RepairPhase = repairPhase } }

        transition Delivery.Accepted Delivery.GuardedLand (Delivery.inspect (atCeiling [ 1; 2; 3 ] false))
        transition Delivery.Accepted Delivery.GuardedLand (Delivery.inspect (atCeiling [ 1 .. 10 ] true))

        match Delivery.inspect (atCeiling [ 1; 2; 3; 4 ] false) with
        | Delivery.Next { Stage = Delivery.ReviewActive; Action = Delivery.RefreshReview reason } ->
            Assert.Contains("round ceiling exceeded", reason)
        | result -> failwithf "expected the ordinary ceiling of 3 to be enforced, got %A" result

        match Delivery.inspect (atCeiling [ 1 .. 11 ] true) with
        | Delivery.Next { Stage = Delivery.ReviewActive; Action = Delivery.RefreshReview reason } ->
            Assert.Contains("round ceiling exceeded", reason)
        | result -> failwithf "expected the repair-phase ceiling of 10 to be enforced, got %A" result
