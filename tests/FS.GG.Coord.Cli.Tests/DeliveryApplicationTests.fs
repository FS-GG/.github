namespace FS.GG.Coord.Cli.Tests

open System
open System.IO
open Xunit
open FS.GG.Coord
open FS.GG.Coord.Cli

module DeliveryApplicationTests =
    let commentWithId id body : Driver.ReviewComment = { Id = id; Url = $"https://example.test/{id}"; Body = body }
    let comment body = commentWithId 1L body

    let guardedLandingFacts claimGeneration : Delivery.Snapshot =
        { Freshness =
            { ItemRef = ".github#2131"
              ClaimGeneration = claimGeneration
              Executor = "wren-c948"
              Branch = "item/2131-pnext-item-protocol"
              Worktree = "/tmp/2131"
              PullRequest = Some 2174
              HeadSha = "head-a"
              DeclaredPaths = Delivery.Known [ "src/FS.GG.Coord.Cli" ]
              BoardState = "In review" }
          ItemBranchCanonical = true
          ClosingLinkageCanonical = true
          PathsVerified = true
          InReview = true
          Review = Some { MarkerValid = true; CriticIdentity = Some "critic"; HeadSha = Some "head-a"; Rounds = [ 1 ]; RepairPhase = false; ChecksGreen = true; HostAccepted = true; RuntimeRouteEvidence = Some(Driver.NotMeaningful "pure adapter test"); DiffAuditRequired = false; DiffAuditHead = None }
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

    let review id url body : Driver.ReviewComment = { Id = id; Url = url; Body = body }

    [<Fact>]
    let ``#2207 client delivery adapter retains malformed parser diagnostics`` () =
        let malformed =
            [ review 10L "https://reviews/initial" "<!-- fsgg:independent-review:v1 -->\nreviewed-head: head-a\nverdict: pass\nroute-applicability: not-meaningful\nroute-not-meaningful-reason: adapter test"
              review 20L "https://reviews/accepted" "<!-- fsgg:review-accepted:v1 -->\naccepted-head: head-a\ninitial-review: https://reviews/initial\nlatest-confirmation: https://reviews/initial" ]
        let parsed, problem = Client.deliveryReviewEvidence true malformed
        let facts = { guardedLandingFacts "claim-generation-a" with Review = parsed; ReviewProblem = problem }

        match Delivery.inspect facts with
        | Delivery.Next transition ->
            match transition.Action with
            | Delivery.RefreshReview reason -> Assert.Contains("critic", reason)
            | action -> failwithf "expected malformed review refresh, got %A" action
        | Delivery.NoVerdict reason -> failwith reason

    [<Fact>]
    let ``#2207 client delivery adapter accepts a real multi-round chain for guarded land`` () =
        let initialUrl = "https://reviews/initial"
        let confirmationUrl = "https://reviews/round-1"
        let chain =
            [ review 10L initialUrl "<!-- fsgg:independent-review:v1 -->\ncritic: kestrel\nreviewed-head: head-a\nverdict: changes-required"
              review 20L confirmationUrl $"<!-- fsgg:independent-review-confirmation:v1 -->\ninitial-review: {initialUrl}\ncritic: kestrel\nround: 1\npreceding-review: {initialUrl}\nreviewed-head: head-a\nverdict: pass\nroute-applicability: not-meaningful\nroute-not-meaningful-reason: adapter test"
              review 30L "https://reviews/accepted" $"<!-- fsgg:review-accepted:v1 -->\naccepted-head: head-a\ninitial-review: {initialUrl}\nlatest-confirmation: {confirmationUrl}" ]
        let parsed, problem = Client.deliveryReviewEvidence true chain
        let facts = { guardedLandingFacts "claim-generation-a" with Review = parsed; ReviewProblem = problem }

        match Delivery.inspect facts with
        | Delivery.Next transition -> Assert.Equal(Delivery.GuardedLand, transition.Action)
        | Delivery.NoVerdict reason -> failwith reason

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
    let ``#2239 version-bearing obligation and receipt ids are accepted`` () =
        let comments =
            [ commentWithId 17L "<!-- fsgg:delivery-obligation id=new-sdd-workspace-0.9.0 kind=publication head=head-a -->"
              commentWithId 18L "<!-- fsgg:delivery-receipt id=new-sdd-workspace-0.9.0 head=head-a evidence=https://nuget.example/package -->" ]
        match DeliveryApplication.obligationsFromComments "head-a" comments with
        | Ok [ obligation ] ->
            Assert.Equal("new-sdd-workspace-0.9.0", obligation.Id)
            Assert.True(obligation.Verified)
        | other -> failwithf "expected one verified version-bearing obligation, got %A" other

    [<Fact>]
    let ``#2239 malformed obligation ids name their comment and field`` () =
        let comments = [ commentWithId 19L "<!-- fsgg:delivery-obligation id=New-Sdd kind=publication head=head-a -->" ]
        match DeliveryApplication.obligationsFromComments "head-a" comments with
        | Error reason ->
            Assert.Contains("19", reason)
            Assert.Contains("id", reason)
        | other -> failwithf "expected malformed id refusal, got %A" other

    [<Fact>]
    let ``#2239 malformed receipt ids name their comment and field`` () =
        let comments =
            [ comment "<!-- fsgg:delivery-obligation id=nuget kind=publication head=head-a -->"
              commentWithId 20L "<!-- fsgg:delivery-receipt id=New-Sdd head=head-a evidence=https://nuget.example/package -->" ]
        match DeliveryApplication.obligationsFromComments "head-a" comments with
        | Error reason ->
            Assert.Contains("20", reason)
            Assert.Contains("id", reason)
        | other -> failwithf "expected malformed receipt id refusal, got %A" other

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

    // -- repair round 1 (critic `crake-0420`, PR #2301): the `declaredPaths` JSON wire shapes were only
    // proven by the critic executing the built CLI artifact by hand — this closes that with a committed
    // `dotnet test` gate over `DeliveryApplication.run`'s real snapshot-file boundary.

    /// A complete, otherwise-valid `delivery --snapshot` JSON document with `declaredPaths` substituted
    /// in verbatim, so each case below exercises only the ONE field under test.
    let private snapshotJson (declaredPathsJson: string) =
        $$"""{"freshness":{"itemRef":"FS-GG/.github#2233","claimGeneration":"fixture-claim","executor":"heron-d4fb","branch":"item/2233-fixture","worktree":"/tmp/fixture","pullRequest":42,"headSha":"fixture-head","declaredPaths":{{declaredPathsJson}},"boardState":"In review"},"itemBranchCanonical":true,"closingLinkageCanonical":true,"pathsVerified":true,"inReview":true,"review":{"markerValid":true,"criticIdentity":"curlew-ced5","headSha":"fixture-head","rounds":[1],"repairPhase":false,"checksGreen":true,"hostAccepted":true,"routeNotMeaningfulReason":"hermetic fixture"},"landable":true,"merged":false,"mergeReachable":false,"issueClosed":false,"boardDone":false,"claimReleased":false,"pendingWrites":0,"cleanupEligible":false,"obligationsDeclared":true,"obligations":[],"parkedReason":null}"""

    /// Runs `DeliveryApplication.run` over a real `--snapshot FILE` the way the live CLI is invoked,
    /// capturing stdout/stderr rather than reaching into the private JSON parser directly.
    let private runSnapshot (declaredPathsJson: string) : int * string * string =
        let path = Path.GetTempFileName()
        File.WriteAllText(path, snapshotJson declaredPathsJson)
        try
            match Options.parse [ "delivery"; "--snapshot"; path; "--json" ] with
            | Error message -> failwith message
            | Ok opts ->
                let originalOut = Console.Out
                let originalErr = Console.Error
                use capturedOut = new StringWriter()
                use capturedErr = new StringWriter()
                Console.SetOut capturedOut
                Console.SetError capturedErr
                try
                    let exitCode = DeliveryApplication.run opts
                    exitCode, capturedOut.ToString(), capturedErr.ToString()
                finally
                    Console.SetOut originalOut
                    Console.SetError originalErr
        finally
            File.Delete path

    [<Fact>]
    let ``#2233 declaredPaths as a plain array parses as Known and reaches guarded land`` () =
        let exitCode, out, _err = runSnapshot """["src/FS.GG.Coord.Cli"]"""
        Assert.Equal(0, exitCode)
        Assert.Contains("\"verdict\":\"next\"", out)
        Assert.Contains("\"action\":\"guardedLand\"", out)

    [<Fact>]
    let ``#2233 declaredPaths as an empty array is a genuine known omission`` () =
        let exitCode, out, _err = runSnapshot "[]"
        Assert.NotEqual(0, exitCode)
        Assert.Contains("\"verdict\":\"noVerdict\"", out)
        Assert.Contains("declared paths", out)
        Assert.DoesNotContain("were not read", out)

    [<Fact>]
    let ``#2233 declaredPaths as {unread: reason} refuses with a reason naming the read`` () =
        let exitCode, out, _err = runSnapshot """{"unread":"issue body fetch timed out"}"""
        Assert.NotEqual(0, exitCode)
        Assert.Contains("\"verdict\":\"noVerdict\"", out)
        Assert.Contains("were not read", out)
        Assert.Contains("issue body fetch timed out", out)

    [<Fact>]
    let ``#2233 declaredPaths as {declaredNone: true} refuses with the deliberate-omission reason`` () =
        let exitCode, out, _err = runSnapshot """{"declaredNone":true}"""
        Assert.NotEqual(0, exitCode)
        Assert.Contains("\"verdict\":\"noVerdict\"", out)
        Assert.Contains("Paths: none", out)

    [<Fact>]
    let ``#2233 declaredPaths as {undeclared: true} refuses with the never-declared reason`` () =
        let exitCode, out, _err = runSnapshot """{"undeclared":true}"""
        Assert.NotEqual(0, exitCode)
        Assert.Contains("\"verdict\":\"noVerdict\"", out)
        Assert.Contains("no Paths: line", out)

    [<Theory>]
    [<InlineData("42")>]
    [<InlineData("null")>]
    [<InlineData("""{"foo":"bar"}""")>]
    [<InlineData("""{"unread":""}""")>]
    let ``#2233 a malformed declaredPaths shape is refused, never a confident verdict`` (malformed: string) =
        let exitCode, out, err = runSnapshot malformed
        Assert.NotEqual(0, exitCode)
        Assert.DoesNotContain("\"verdict\":\"next\"", out)
        Assert.True(String.IsNullOrEmpty out, $"expected no verdict document on stdout for a malformed snapshot, got: %s{out}")
        // Every malformed shape names ITS OWN offending field ("declaredPaths" for the shape itself,
        // or "unread" for a malformed value nested inside an otherwise well-shaped object) — never a
        // silent fallback to a confident empty read.
        Assert.False(String.IsNullOrWhiteSpace err, "expected a non-empty diagnostic on stderr")
        Assert.True(err.Contains("declaredPaths") || err.Contains("unread"), $"expected the diagnostic to name the offending field, got: %s{err}")
