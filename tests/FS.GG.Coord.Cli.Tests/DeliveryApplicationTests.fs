namespace FS.GG.Coord.Cli.Tests

open Xunit
open FS.GG.Coord
open FS.GG.Coord.Cli

module DeliveryApplicationTests =
    let comment body : Driver.ReviewComment = { Id = 1L; Url = "https://example.test/1"; Body = body }

    let guardedLandingFacts claimGeneration : Delivery.Snapshot =
        { Freshness =
            { ItemRef = ".github#2131"
              ClaimGeneration = claimGeneration
              Executor = "wren-c948"
              Branch = "item/2131-pnext-item-protocol"
              Worktree = "/tmp/2131"
              PullRequest = Some 2174
              HeadSha = "head-a"
              DeclaredPaths = [ "src/FS.GG.Coord.Cli" ]
              BoardState = "In review" }
          ItemBranchCanonical = true
          ClosingLinkageCanonical = true
          PathsVerified = true
          InReview = true
          Review = Some { MarkerValid = true; CriticIdentity = Some "critic"; HeadSha = Some "head-a"; Rounds = [ 1 ]; RepairPhase = false; ChecksGreen = true; HostAccepted = true; RuntimeRouteEvidence = Some(Driver.NotMeaningful "pure adapter test") }
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

    [<Fact>]
    let ``#2131 non-empty obligation receipt is head-bound and verifies only its declared id`` () =
        let comments =
            [ comment "<!-- fsgg:delivery-obligation id=nuget kind=publication head=head-a -->"
              comment "<!-- fsgg:delivery-receipt id=nuget head=head-a evidence=https://nuget.example/package -->" ]
        match DeliveryApplication.obligationsFromComments "head-a" comments with
        | Ok [ obligation ] ->
            Assert.Equal("nuget", obligation.Id)
            Assert.Equal("publication", obligation.Kind)
            Assert.True(obligation.Verified)
        | other -> failwithf "expected one verified obligation, got %A" other

    [<Fact>]
    let ``#2131 stale and undeclared obligation facts are refused`` () =
        match DeliveryApplication.obligationsFromComments "head-b" [ comment "<!-- fsgg:delivery-obligation id=nuget kind=publication head=head-a -->" ] with
        | Error reason -> Assert.Contains("stale", reason)
        | other -> failwithf "expected stale declaration refusal, got %A" other

        match DeliveryApplication.obligationsFromComments "head-a" [] with
        | Error reason -> Assert.Contains("undeclared", reason)
        | other -> failwithf "expected undeclared refusal, got %A" other

    [<Fact>]
    let ``#2131 delivery adapter refuses a stale claim generation before issuing a merge`` () =
        let facts = guardedLandingFacts "claim-generation-a"
        let transition =
            match Delivery.inspect facts with
            | Delivery.Next next -> next
            | Delivery.NoVerdict reason -> failwith reason
        let mutable mergeCalls = 0
        let attemptMerge () = mergeCalls <- mergeCalls + 1; "merge endpoint was called"

        match DeliveryApplication.guardedLanding transition.FreshnessToken transition.ActionKey facts (Some "claim-generation-b") attemptMerge with
        | Ok result -> failwith result
        | Error reason -> Assert.Contains("generation changed", reason)

        Assert.Equal(0, mergeCalls)
