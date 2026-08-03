namespace FS.GG.Coord.Tests

open Xunit
open FS.GG.Coord

module DeliveryTests =
    let review head : Driver.ReviewChain =
        { MarkerValid = true
          CriticIdentity = Some "kite"
          HeadSha = Some head
          Rounds = [ 1 ]
          RepairPhase = false
          ChecksGreen = true
          HostAccepted = true
          RuntimeRouteEvidence = Some(Driver.NotMeaningful "pure lifecycle test") }

    let freshness head : Delivery.Freshness =
        { ItemRef = "FS-GG/.github#2131"
          ClaimGeneration = "5165723183"
          Executor = "wren-c948"
          Branch = "item/2131-claim-to-done-lifecycle"
          Worktree = "/worktrees/2131"
          PullRequest = Some 99
          HeadSha = head
          DeclaredPaths = [ "src/FS.GG.Coord.Core" ]
          BoardState = "In progress" }

    let snapshot head : Delivery.Snapshot =
        { Freshness = freshness head
          ItemBranchCanonical = true
          ClosingLinkageCanonical = true
          PathsVerified = true
          InReview = true
          Review = Some(review head)
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
