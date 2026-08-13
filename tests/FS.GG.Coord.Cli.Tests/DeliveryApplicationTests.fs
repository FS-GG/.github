namespace FS.GG.Coord.Cli.Tests

open System
open System.IO
open Xunit
open FS.GG.Coord
open FS.GG.Coord.Types
open FS.GG.Coord.Cli
open FS.GG.Coord.GitHub
open FS.GG.Coord.GitHub.Transport

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

    // .github#2347: `obligationDeclaration`/`obligationReceipt`/the `none` sentinel anchored their
    // regex against the comment's ENTIRE trimmed body, so the org's universal writing style — marker
    // line, blank line, explanatory prose — read as malformed (declaration/receipt) or undeclared
    // (none), even though the marker was correctly the comment's own leading line. `.github#2221`
    // made this identical whole-body-to-whole-line correction for review markers; this applies it to
    // the three delivery markers, which never received it.

    [<Fact>]
    let ``#2347 a declaration with trailing explanatory prose parses successfully`` () =
        let comments =
            [ comment "<!-- fsgg:delivery-obligation id=nuget kind=publication head=head-a -->\n\nThis obligation covers publishing the nuget package once the merge lands."
              comment "<!-- fsgg:delivery-receipt id=nuget head=head-a evidence=https://nuget.example/package -->\n\nPublished and verified on both feeds." ]
        match DeliveryApplication.obligationsFromComments "head-a" comments with
        | Ok [ obligation ] ->
            Assert.Equal("nuget", obligation.Id)
            Assert.True(obligation.Verified)
        | other -> failwithf "expected one verified obligation parsed past the trailing prose, got %A" other

    [<Fact>]
    let ``#2347 the none sentinel with trailing explanatory prose parses successfully`` () =
        let comments =
            [ comment "<!-- fsgg:delivery-obligations none head=head-a -->\n\nNo package, deployment, or registry surface moves in this change." ]
        match DeliveryApplication.obligationsFromComments "head-a" comments with
        | Ok [] -> ()
        | other -> failwithf "expected the none sentinel to clear past the trailing prose, got %A" other

    [<Fact>]
    let ``#2347 a marker merely quoted later in a comment, never its own leading line, stays inert`` () =
        // The comment does not itself start with the declaration prefix, so it is excluded by the
        // same `StartsWith` filter round 1 (.github#2264) already relies on — the leading-line fix
        // must not loosen that boundary.
        let comments =
            [ comment "For context, a declaration will look like:\n<!-- fsgg:delivery-obligation id=nuget kind=publication head=head-a -->\nonce it is posted." ]
        match DeliveryApplication.obligationsFromComments "head-a" comments with
        | Error reason -> Assert.Contains("undeclared", reason)
        | other -> failwithf "expected the quoted marker to stay inert and read as undeclared, got %A" other

    [<Fact>]
    let ``#2347 trailing text appended to the marker's own line, not a new line, is still malformed`` () =
        let comments =
            [ comment "<!-- fsgg:delivery-obligation id=nuget kind=publication head=head-a --> and more on the same line" ]
        match DeliveryApplication.obligationsFromComments "head-a" comments with
        | Error reason -> Assert.Contains("malformed", reason)
        | other -> failwithf "expected same-line trailing text to remain malformed, got %A" other

    [<Fact>]
    let ``#2347 the real kit-0.48.0 declaration from .github#2264 PR #2271 (comment 5225891717) parses`` () =
        // Reproduced verbatim (https://github.com/FS-GG/.github/issues/comments/5225891717) — the exact
        // shape the issue measured as unparseable in production: marker line, blank line, prose.
        let body =
            "<!-- fsgg:delivery-obligation id=kit-0.48.0 kind=publication head=366b28a43251962de4a03a4fdac39651dc9b72e9 -->\n\n\
This PR edits `.claude/skills/check-board/references/deep-detail.md` and its `.agents` twin, plus\n\
`mechanical-reconciliation.md` in both skill roots. `check-board` is one of the four skills\n\
`registry/repos.yml`'s `kit:` rows pack, so the packed kit manifest changes and `FS.GG.Kit` must be\n\
released past the newest published version.\n\n\
This worker does NOT tag or publish — release sequencing is the host's, per explicit dispatch\n\
instruction. The obligation remains open (no `fsgg:delivery-receipt` yet) until the merged commit is\n\
tagged `kit/v0.48.0` and the identical artifact is published to GitHub Packages and nuget.org."
        match DeliveryApplication.obligationsFromComments "366b28a43251962de4a03a4fdac39651dc9b72e9" [ comment body ] with
        | Ok [ obligation ] ->
            Assert.Equal("kit-0.48.0", obligation.Id)
            Assert.Equal("publication", obligation.Kind)
            Assert.False(obligation.Verified)
        | other -> failwithf "expected the real production declaration to parse with a real verdict, got %A" other

    // Round-1 review repair (.github#2264 PR #2271): `Client.outstandingObligations` is the extracted,
    // directly-testable core of `reconcile`'s lifecycle fold, which previously scanned live PR comments
    // with bulk, unanchored `.Contains` — a comment quoting ANOTHER obligation's receipt marker in prose
    // made `Outstanding` compute `false` while a genuine obligation was still open. These tests reproduce
    // the critic's exact scenario over the REAL production parser it now reuses.
    let private commentBody id url body : Reads.CommentBody = { Id = id; Url = url; Body = body }

    [<Fact>]
    let ``#2264 round 1: a receipt quoted in prose cannot clear a different obligation`` () =
        let comments : Reads.CommentBody list =
            [ commentBody 1L "https://example.test/1" "<!-- fsgg:delivery-obligation id=a kind=publication head=head-a -->"
              commentBody 2L "https://example.test/2" "<!-- fsgg:delivery-obligation id=b kind=publication head=head-a -->"
              commentBody 3L "https://example.test/3" "<!-- fsgg:delivery-receipt id=a head=head-a evidence=https://example.test/a -->"
              // Quotes `b`'s receipt shape in prose, in the org's ordinary reviewer-comment style — never
              // its own comment's entire body, so the anchored parser cannot mistake it for a real receipt.
              commentBody 4L "https://example.test/4" "For context, `b`'s receipt will look like:\n`<!-- fsgg:delivery-receipt id=b head=head-a evidence=https://example.test/b -->`\nonce it lands." ]
        Assert.True(Client.outstandingObligations (Ok "head-a") (Ok comments))

    [<Fact>]
    let ``#2264 round 1: every obligation genuinely receipted clears Outstanding`` () =
        let comments : Reads.CommentBody list =
            [ commentBody 1L "https://example.test/1" "<!-- fsgg:delivery-obligation id=a kind=publication head=head-a -->"
              commentBody 2L "https://example.test/2" "<!-- fsgg:delivery-receipt id=a head=head-a evidence=https://example.test/a -->" ]
        Assert.False(Client.outstandingObligations (Ok "head-a") (Ok comments))

    [<Fact>]
    let ``#2264 round 1: an unreadable head or comment thread fails closed as Outstanding`` () =
        Assert.True(Client.outstandingObligations (Error(Errors.NotFound "no head")) (Ok []))
        Assert.True(Client.outstandingObligations (Ok "head-a") (Error(Errors.NotFound "no comments")))

    [<Fact>]
    let ``#2264 round 1: a malformed or stale declaration fails closed as Outstanding`` () =
        let staleHead : Reads.CommentBody list =
            [ commentBody 1L "https://example.test/1" "<!-- fsgg:delivery-obligation id=a kind=publication head=old-head -->" ]
        Assert.True(Client.outstandingObligations (Ok "head-a") (Ok staleHead))

    [<Fact>]
    let ``#2216 stale declaration identifies its comment and append-proof repair`` () =
        let comments =
            [ commentWithId 41L "<!-- fsgg:delivery-obligation id=nuget kind=publication head=head-a -->"
              commentWithId 42L "<!-- fsgg:delivery-obligation id=nuget kind=publication head=head-b -->" ]

        match DeliveryApplication.obligationsFromComments "head-b" comments with
        | Error reason ->
            Assert.Contains("comment 41", reason)
            Assert.Contains("edit it in place or delete it", reason)
            Assert.Contains("adding a declaration cannot repair it", reason)
        | other -> failwithf "expected stale declaration repair refusal, got %A" other

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

    // .github#2395 (design slice 3 of .github#1858): `Client.rebindAuthorization` is the pure decision
    // behind `delivery`'s automatic write of the `fsgg:pr-authorization` marker `check-claim-generation.py`
    // (`scripts/check-claim-generation.py`) reads. Each case below mirrors one of that gate's own four
    // diagnoses — MISSING, STALE (a superset here: any mismatch of `gen`/`head` rebinds), and the "two
    // markers is the same as none" rule — so a fix that makes any of them pass without truly repairing the
    // marker is caught here first.

    [<Fact>]
    let ``#2395 authorizationMarker renders the exact v1 grammar the gate parses`` () =
        let marker = Client.authorizationMarker "FS-GG/.github#2395" "5267541214" "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa"
        Assert.Equal(
            "<!-- fsgg:pr-authorization v=1 item=FS-GG/.github#2395 gen=5267541214 head=aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa -->",
            marker
        )

    [<Fact>]
    let ``#2395 a body with no marker at all is rebound, not left missing`` () =
        let body = "Implements the thing.\n\nCloses #2395"
        match Client.rebindAuthorization body "FS-GG/.github#2395" "5267541214" "head-a" with
        | Client.AuthorizationRebound updated ->
            Assert.Contains(Client.authorizationMarker "FS-GG/.github#2395" "5267541214" "head-a", updated)
            Assert.Contains("Closes #2395", updated)
        | Client.AuthorizationCurrent -> failwith "expected a rebind: the body carried no marker at all"

    [<Fact>]
    let ``#2395 a marker bound to a superseded head is rebound to the current one, not left stale`` () =
        let body =
            "Implements the thing.\n\n" + Client.authorizationMarker "FS-GG/.github#2395" "5267541214" "head-old"
        match Client.rebindAuthorization body "FS-GG/.github#2395" "5267541214" "head-new" with
        | Client.AuthorizationRebound updated ->
            Assert.Contains(Client.authorizationMarker "FS-GG/.github#2395" "5267541214" "head-new", updated)
            Assert.DoesNotContain("head-old", updated)
        | Client.AuthorizationCurrent -> failwith "expected a rebind: the marker's head was superseded"

    [<Fact>]
    let ``#2395 two markers collapse to exactly one, never left duplicated`` () =
        let stale = Client.authorizationMarker "FS-GG/.github#2395" "111" "head-old"
        let alsoStale = Client.authorizationMarker "FS-GG/.github#2395" "222" "head-older"
        let body = $"Implements the thing.\n\n{stale}\n\n{alsoStale}"
        match Client.rebindAuthorization body "FS-GG/.github#2395" "5267541214" "head-new" with
        | Client.AuthorizationRebound updated ->
            let desired = Client.authorizationMarker "FS-GG/.github#2395" "5267541214" "head-new"
            let occurrences = System.Text.RegularExpressions.Regex.Matches(updated, System.Text.RegularExpressions.Regex.Escape "<!-- fsgg:pr-authorization").Count
            Assert.Equal(1, occurrences)
            Assert.Contains(desired, updated)
        | Client.AuthorizationCurrent -> failwith "expected a rebind: two stale markers must collapse to one current one"

    [<Fact>]
    let ``#2395 a body already carrying exactly the desired marker is reported current, not rewritten`` () =
        let desired = Client.authorizationMarker "FS-GG/.github#2395" "5267541214" "head-current"
        let body = $"Implements the thing.\n\n{desired}"
        match Client.rebindAuthorization body "FS-GG/.github#2395" "5267541214" "head-current" with
        | Client.AuthorizationCurrent -> ()
        | Client.AuthorizationRebound updated -> failwithf "expected no rewrite for an already-current marker, got %s" updated

    // Everything above drives `rebindAuthorization` as a pure function — real coverage of the DECISION,
    // but none of it proves the LIVE wired path actually reaches the transport with the right method,
    // path, and body. `Client.ensureAuthorization` is that wiring (`Reads.prBody`'s GET, then a
    // conditional PATCH via `ctx.Transport.Send`), and the two cases below drive it directly against a
    // `Fake.Recorder` — the same "reuse the internal seam instead of restating the whole `delivery`
    // command's board-scan/PR-facts machinery" idiom `AuthorizedMarkerTests.fs` already uses for
    // `Client.authorizedMarker`. `tests/coord-engine-e2e/writes.sh` has no live `delivery --pr --apply`
    // invocation, so this in-process pair is this change's only coverage of the real IO, not merely the
    // pure decision it wraps.

    let private jsonBody (body: string) : string =
        System.Text.Json.JsonSerializer.Serialize {| body = body |}

    let private ensureAuthorizationTransport (route: Request -> Errors.IoResult<Response>) : Fake.Recorder =
        Fake.Recorder(fun (req: Request) -> route req)

    let private okResponse (body: string) : Errors.IoResult<Response> =
        Ok
            { Status = 200
              Body = body
              ETag = None
              NextLink = None
              Headers = Map.empty }

    let private ensureAuthorizationTarget: Ref =
        { Owner = "FS-GG"; Repo = ".github"; Number = 2395 }

    let private ensureAuthorizationMarker: Reads.Marker =
        { Id = 5267541214L
          Worker = WorkerId "smew-f1e2"
          Session = None
          AgeSeconds = 30
          PreviousStatus = None
          PathRepo = None
          Raw = "<!-- fsgg:claim worker=smew-f1e2 lease=120 -->" }

    let private ensureAuthorizationContext (transport: Fake.Recorder) : Client.Context =
        { Transport = transport
          Owner = "FS-GG"
          Title = "Coordination"
          DefaultRepo = Some ".github"
          ChoreLocks = [] }

    [<Fact>]
    let ``#2395 ensureAuthorization reads the live PR body and PATCHes the rebound marker onto pulls/n`` () =
        let mutable patchedBody: string option = None

        let transport =
            ensureAuthorizationTransport (fun req ->
                match req.Method, req.Path.Trim '/' with
                | "GET", "repos/FS-GG/.github/pulls/9001" -> okResponse (jsonBody "Implements the thing.\n\nCloses #2395")
                | "PATCH", "repos/FS-GG/.github/pulls/9001" ->
                    match req.Body with
                    | Json payload ->
                        use doc = System.Text.Json.JsonDocument.Parse payload
                        patchedBody <- Some(doc.RootElement.GetProperty("body").GetString())
                        okResponse "{}"
                    | _ -> failwith "expected the authorization PATCH to carry a JSON body"
                | method', path -> Error(Errors.NotFound $"unexpected request in the #2395 fixture: %s{method'} %s{path}"))

        let head = "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb"

        match Client.ensureAuthorization (ensureAuthorizationContext transport) ensureAuthorizationTarget (Some ensureAuthorizationMarker) (Some 9001) head false with
        | Error e -> failwithf "expected ensureAuthorization to succeed, got %A" e
        | Ok() ->
            match patchedBody with
            | None -> failwith "expected a PATCH to reach repos/FS-GG/.github/pulls/9001"
            | Some body ->
                Assert.Contains(Client.authorizationMarker "FS-GG/.github#2395" "5267541214" head, body)
                Assert.Contains("Closes #2395", body)

    [<Fact>]
    let ``#2395 ensureAuthorization spends zero writes once the live PR body is already current`` () =
        let head = "cccccccccccccccccccccccccccccccccccccccc"
        let desired = Client.authorizationMarker "FS-GG/.github#2395" "5267541214" head

        let transport =
            ensureAuthorizationTransport (fun req ->
                match req.Method, req.Path.Trim '/' with
                | "GET", "repos/FS-GG/.github/pulls/9001" -> okResponse (jsonBody $"Implements the thing.\n\n{desired}")
                | "PATCH", "repos/FS-GG/.github/pulls/9001" ->
                    failwith "expected zero writes: the live PR body already carried the current marker"
                | method', path -> Error(Errors.NotFound $"unexpected request in the #2395 fixture: %s{method'} %s{path}"))

        match Client.ensureAuthorization (ensureAuthorizationContext transport) ensureAuthorizationTarget (Some ensureAuthorizationMarker) (Some 9001) head false with
        | Error e -> failwithf "expected ensureAuthorization to succeed, got %A" e
        | Ok() -> ()

    // Replaces the deleted `#2395 ensureAuthorization makes no request at all without --apply` test.
    // That test asserted the very defect #2488 measured: it is the only reason no live `item/<n>-*` PR
    // ever carried a current marker at the moment `claim-generation` evaluated it (five for five,
    // #2488's own evidence table). `apply` no longer exists as a parameter — every call at this
    // signature performs the read-modify-write below, so there is no argument left to hold "false" to
    // reproduce the old no-op. Before this fix landed, this exact call (there was no sixth `false`
    // argument to add — `ensureAuthorization` took one fewer parameter) would have failed to compile
    // against the OLD 6-argument signature, and the OLD test at the OLD signature asserted zero requests
    // for precisely this shape; reverting `Client.fs`'s call-site and signature change while keeping
    // this test is the gate-inversion mutation this change's PR records having run.
    [<Fact>]
    let ``#2488 ensureAuthorization is no longer gated on --apply: a plain live status read writes the marker too`` () =
        let mutable patchedBody: string option = None
        let head = "dddddddddddddddddddddddddddddddddddddddd"

        let transport =
            ensureAuthorizationTransport (fun req ->
                match req.Method, req.Path.Trim '/' with
                | "GET", "repos/FS-GG/.github/pulls/9001" -> okResponse (jsonBody "Implements the thing.")
                | "PATCH", "repos/FS-GG/.github/pulls/9001" ->
                    match req.Body with
                    | Json payload ->
                        use doc = System.Text.Json.JsonDocument.Parse payload
                        patchedBody <- Some(doc.RootElement.GetProperty("body").GetString())
                        okResponse "{}"
                    | _ -> failwith "expected the authorization PATCH to carry a JSON body"
                | method', path -> Error(Errors.NotFound $"unexpected request in the #2488 fixture: %s{method'} %s{path}"))

        match Client.ensureAuthorization (ensureAuthorizationContext transport) ensureAuthorizationTarget (Some ensureAuthorizationMarker) (Some 9001) head false with
        | Error e -> failwithf "expected ensureAuthorization to succeed, got %A" e
        | Ok() ->
            match patchedBody with
            | None -> failwith "expected a PATCH even though this call carries nothing resembling --apply"
            | Some body -> Assert.Contains(Client.authorizationMarker "FS-GG/.github#2395" "5267541214" head, body)

    // A genuinely still-live no-op: a merged PR's body is never rewritten, apply or not — nothing further
    // needs authorizing once landing has already happened. Not previously covered at this wired level
    // (only the apply-gated no-op was), so this is new coverage the #2488 signature change earns for
    // free rather than a behavior it changes.
    [<Fact>]
    let ``#2488 ensureAuthorization still makes no request once the PR has merged`` () =
        let transport =
            ensureAuthorizationTransport (fun req -> Error(Errors.NotFound $"expected zero requests once merged, got %s{req.Method} %s{req.Path}"))

        match Client.ensureAuthorization (ensureAuthorizationContext transport) ensureAuthorizationTarget (Some ensureAuthorizationMarker) (Some 9001) "head-a" true with
        | Error e -> failwithf "expected ensureAuthorization to succeed as a no-op, got %A" e
        | Ok() -> ()
