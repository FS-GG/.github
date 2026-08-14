namespace FS.GG.Coord.Cli.Tests

open System
open System.IO
open System.Text.Json
open Xunit
open FS.GG.Coord
open FS.GG.Coord.Cli

/// The `review --snapshot FILE` JSON boundary (.github#2175), mirroring `DeliveryApplicationTests`'s
/// pattern: run `ReviewApplication.run` over a real `--snapshot FILE` the way the live CLI is invoked,
/// capturing stdout/stderr rather than reaching into the private JSON parser directly.
module ReviewApplicationTests =
    let private ordinaryBinding =
        """"itemRef":"FS-GG/.github#2175","pr":42,"headSha":"head-a","claimGeneration":"fixture-claim","implementerIdentity":"heron-d4fb","phase":"ordinary","round":1"""

    let private snapshotJson (bindingJson: string) (factsJson: string) =
        "{\"binding\":{" + bindingJson + "},\"facts\":" + factsJson + "}"

    let private emptyFacts (checks: string) =
        "{\"comments\":[],\"checks\":\"" + checks + "\",\"repairPhaseGranted\":null,\"repairRouteAvailable\":true}"

    let private runSnapshot (raw: string) : int * string * string =
        let path = Path.GetTempFileName()
        File.WriteAllText(path, raw)
        try
            match Options.parse [ "review"; "--snapshot"; path; "--json" ] with
            | Error message -> failwith message
            | Ok opts ->
                let originalOut = Console.Out
                let originalErr = Console.Error
                use capturedOut = new StringWriter()
                use capturedErr = new StringWriter()
                Console.SetOut capturedOut
                Console.SetError capturedErr
                try
                    let exitCode = ReviewApplication.run opts
                    exitCode, capturedOut.ToString(), capturedErr.ToString()
                finally
                    Console.SetOut originalOut
                    Console.SetError originalErr
        finally
            File.Delete path

    let private runText (raw: string) : int * string * string =
        let path = Path.GetTempFileName()
        File.WriteAllText(path, raw)
        try
            match Options.parse [ "review"; "--snapshot"; path; "--text" ] with
            | Error message -> failwith message
            | Ok opts ->
                let originalOut = Console.Out
                let originalErr = Console.Error
                use capturedOut = new StringWriter()
                use capturedErr = new StringWriter()
                Console.SetOut capturedOut
                Console.SetError capturedErr
                try
                    let exitCode = ReviewApplication.run opts
                    exitCode, capturedOut.ToString(), capturedErr.ToString()
                finally
                    Console.SetOut originalOut
                    Console.SetError originalErr
        finally
            File.Delete path

    let private structuredReviewSnapshot legacyVerdict =
        let head = String.replicate 40 "a"
        let draft : StructuredDecision.ReviewRecord =
            { Schema = StructuredDecision.ReviewSchema; Subject = "FS-GG/.github#2175/pr/42"
              Revision = 1; PreviousDigest = None; HeadSha = head; Critic = "heron-d4fb"
              Verdict = StructuredDecision.Pass; AcceptedExceptions = []
              RouteApplicability = "not-meaningful"; RouteEvidence = [ "fixture" ]
              PolicyVersion = StructuredDecision.PolicyVersion; Kind = StructuredDecision.Initial
              Round = 0; InitialReview = None; PrecedingReview = None
              Timestamp = "2026-08-14T12:00:00Z"; Digest = "" }
        let record = { draft with Digest = StructuredDecision.reviewDigest draft }
        let legacy =
            $"<!-- fsgg:independent-review:v1 -->\ncritic: heron-d4fb\nreviewed-head: %s{head}\nverdict: %s{legacyVerdict}\nroute-applicability: not-meaningful\nroute-not-meaningful-reason: fixture"
        let structured = "<!-- fsgg:review-decision/v2 -->\n" + Driver.encodeStructuredReview record
        let comments =
            JsonSerializer.Serialize
                [| {| id = 1L; url = "https://reviews/legacy"; body = legacy |}
                   {| id = 2L; url = "https://reviews/v2"; body = structured |} |]
        let binding =
            $"\"itemRef\":\"FS-GG/.github#2175\",\"pr\":42,\"headSha\":\"%s{head}\",\"claimGeneration\":\"fixture-claim\",\"implementerIdentity\":\"worker-1\",\"phase\":\"ordinary\",\"round\":1"
        let facts =
            $"{{\"comments\":%s{comments},\"checks\":\"pending\",\"repairPhaseGranted\":null,\"repairRouteAvailable\":true}}"
        snapshotJson binding facts

    [<Fact>]
    let ``#2175 an empty snapshot file refuses rather than inferring absent review`` () =
        let path = Path.GetTempFileName()
        try
            match Options.parse [ "review"; "--snapshot"; path; "--json" ] with
            | Error message -> failwith message
            | Ok opts ->
                let originalErr = Console.Error
                use capturedErr = new StringWriter()
                Console.SetError capturedErr
                try
                    let exitCode = ReviewApplication.run opts
                    Assert.NotEqual(0, exitCode)
                    Assert.Contains("empty", capturedErr.ToString())
                finally
                    Console.SetError originalErr
        finally
            File.Delete path

    [<Fact>]
    let ``#2175 a malformed snapshot document is refused, not defaulted`` () =
        let exitCode, _out, err = runSnapshot """{"binding": "not an object", "facts": {}}"""
        Assert.NotEqual(0, exitCode)
        Assert.Contains("malformed", err)

    [<Fact>]
    let ``#2175 no comments reaches awaitingInitialReview/dispatchCritic over the real CLI boundary`` () =
        let exitCode, out, _err = runSnapshot (snapshotJson ordinaryBinding (emptyFacts "pending"))
        Assert.Equal(0, exitCode)
        Assert.Contains("\"verdict\":\"next\"", out)
        Assert.Contains("\"state\":\"awaitingInitialReview\"", out)
        Assert.Contains("\"action\":\"dispatchCritic\"", out)
        Assert.Contains("\"evidenceClassification\":\"legacy-only\"", out)
        Assert.Contains("\"freshnessToken\"", out)
        Assert.Contains("\"actionKey\"", out)

    [<Theory>]
    [<InlineData("pass", "equivalent")>]
    [<InlineData("changes-required", "divergent")>]
    let ``M4 review CLI emits classified legacy and structured differences`` legacyVerdict expected =
        let exitCode, out, _ = runSnapshot (structuredReviewSnapshot legacyVerdict)
        Assert.Equal(0, exitCode)
        Assert.Contains($"\"evidenceClassification\":\"%s{expected}\"", out)

    [<Fact>]
    let ``#2175 the text projection renders one state — action line`` () =
        let exitCode, out, _err = runText (snapshotJson ordinaryBinding (emptyFacts "pending"))
        Assert.Equal(0, exitCode)
        Assert.Equal("awaitingInitialReview — dispatchCritic — evidence legacy-only", out.Trim())

    [<Fact>]
    let ``#2175 a clean accepted chain reaches Accept with the accepted-current-head receipt fields`` () =
        let comments =
            """[{"id":1,"url":"https://reviews/1","body":"<!-- fsgg:independent-review:v1 -->\ncritic: kestrel\nreviewed-head: head-a\nverdict: pass\nroute-applicability: not-meaningful\nroute-not-meaningful-reason: fixture"},{"id":2,"url":"https://reviews/2","body":"<!-- fsgg:review-accepted:v1 -->\naccepted-head: head-a\ninitial-review: https://reviews/1\nlatest-confirmation: https://reviews/1"}]"""
        let facts = $$"""{"comments":{{comments}},"checks":"green","repairPhaseGranted":null,"repairRouteAvailable":true}"""
        let exitCode, out, _err = runSnapshot (snapshotJson ordinaryBinding facts)
        Assert.Equal(0, exitCode)
        Assert.Contains("\"state\":\"accepted\"", out)
        Assert.Contains("\"action\":\"accept\"", out)
        Assert.Contains("\"headSha\":\"head-a\"", out)
        Assert.Contains("\"criticIdentity\":\"kestrel\"", out)

    [<Fact>]
    let ``#2175 an implementer acting as its own critic is refused over the real CLI boundary`` () =
        let comments =
            """[{"id":1,"url":"https://reviews/1","body":"<!-- fsgg:independent-review:v1 -->\ncritic: heron-d4fb\nreviewed-head: head-a\nverdict: pass\nroute-applicability: not-meaningful\nroute-not-meaningful-reason: fixture"}]"""
        let facts = $$"""{"comments":{{comments}},"checks":"green","repairPhaseGranted":null,"repairRouteAvailable":true}"""
        let exitCode, out, _err = runSnapshot (snapshotJson ordinaryBinding facts)
        Assert.Equal(0, exitCode)
        Assert.Contains("\"state\":\"guardViolation\"", out)
        Assert.Contains("\"action\":\"park\"", out)

    [<Fact>]
    let ``#2175 an unreadable checks word is refused, not silently defaulted`` () =
        let exitCode, _out, err = runSnapshot (snapshotJson ordinaryBinding (emptyFacts "not-a-real-state"))
        Assert.NotEqual(0, exitCode)
        Assert.Contains("checks", err)

    [<Fact>]
    let ``#2175 a repair-phase-granted receipt round-trips through the wire contract`` () =
        let facts =
            """{"comments":[],"checks":"pending","repairPhaseGranted":{"exhaustedPr":42,"escalationCommentId":99,"newClaimGeneration":"gen-2","newBranchOrPr":"item/2175-repair","newImplementerIdentity":"fresh-impl","newCriticIdentity":"fresh-critic","candidateHeadSha":"head-a"},"repairRouteAvailable":true}"""
        // Round 4 confirmations already exhausted (round field is descriptive only in the wire binding;
        // the engine derives exhaustion from `comments`), so seed enough changes-required confirmations to
        // exhaust the ordinary ceiling and force the granted-receipt reuse path.
        // `MaxAutomatedRepairRounds` is 3 and exhaustion fires on `ConfirmationCount > ceiling`, so FIVE
        // total comments (1 initial + 4 confirmations), not four, is what actually exhausts it.
        let comments =
            [ 1 .. 5 ]
            |> List.map (fun round ->
                let body =
                    if round = 1 then
                        "<!-- fsgg:independent-review:v1 -->\ncritic: kite\nreviewed-head: head-0\nverdict: changes-required"
                    else
                        $"<!-- fsgg:independent-review-confirmation:v1 -->\ninitial-review: https://reviews/1\ncritic: kite\nround: {round - 1}\npreceding-review: https://reviews/{round - 1}\nreviewed-head: head-{round - 1}\nverdict: changes-required"
                $$"""{"id":{{round}},"url":"https://reviews/{{round}}","body":"{{body.Replace("\n", "\\n")}}"}""")
            |> String.concat ","
        let factsWithComments =
            facts.Replace("\"comments\":[]", $"\"comments\":[{comments}]")
        let exitCode, out, _err = runSnapshot (snapshotJson ordinaryBinding factsWithComments)
        Assert.Equal(0, exitCode)
        Assert.Contains("\"state\":\"repairPhaseSetup\"", out)
        Assert.Contains("\"action\":\"enterRepairPhase\"", out)
        Assert.Contains("\"newCriticIdentity\":\"fresh-critic\"", out)

    // ---- .github#2549: the new state and the optional grant, ON THE WIRE ---------------------------
    //
    // WHY THESE LEGS EXIST (round-1 finding M2). `.github#2549` first shipped with no CI-gated coverage
    // of its own wire additions, on the reasoning that an exhaustive `match` plus `TreatWarningsAsErrors`
    // made the rendering safe. That does not reach: exhaustiveness proves every case has an arm and
    // proves nothing about JSON KEY NAMES, the wire spelling of a state or action, the absent-key parse
    // path, or the refusal of a malformed value — which are the four things a `fsgg.coord.review/1`
    // consumer actually depends on. The legs assert on raw JSON substrings rather than a re-parsed
    // object, deliberately: a renamed key must fail here, where a test that parsed the payload back with
    // the producer's own vocabulary would follow the rename silently.

    /// The `.github#2534` chain shape at `ordinaryBinding`'s head: an initial `changes-required`, a
    /// round-1 `pass` confirmation at the SAME head (its finding was against a PR comment, not the
    /// tree), and a bound host acceptance. `checks` is substituted so each leg varies only that field.
    let private acceptedChainFacts (checks: string) =
        let comments =
            """[{"id":1,"url":"https://reviews/1","body":"<!-- fsgg:independent-review:v1 -->\ncritic: kite\nreviewed-head: head-a\nverdict: changes-required"},{"id":2,"url":"https://reviews/2","body":"<!-- fsgg:independent-review-confirmation:v1 -->\ninitial-review: https://reviews/1\ncritic: kite\nround: 1\npreceding-review: https://reviews/1\nreviewed-head: head-a\nverdict: pass\nroute-applicability: not-meaningful\nroute-not-meaningful-reason: fixture"},{"id":3,"url":"https://reviews/3","body":"<!-- fsgg:review-accepted:v1 -->\naccepted-head: head-a\ninitial-review: https://reviews/1\nlatest-confirmation: https://reviews/2"}]"""
        $$"""{"comments":{{comments}},"checks":"{{checks}}","repairPhaseGranted":null,"repairRouteAvailable":true}"""

    /// The unmoved-head chain the grant legs answer: one initial `changes-required` and nothing else.
    let private unmovedFacts (grantJson: string) =
        let comments =
            """[{"id":1,"url":"https://reviews/1","body":"<!-- fsgg:independent-review:v1 -->\ncritic: kite\nreviewed-head: head-a\nverdict: changes-required"}]"""
        $$"""{"comments":{{comments}},"checks":"pending","repairPhaseGranted":null,"repairRouteAvailable":true{{grantJson}}}"""

    [<Fact>]
    let ``#2549 the post-acceptance pending window renders acceptedAwaitingChecks with NULL stateErrors`` () =
        let exitCode, out, _err = runSnapshot (snapshotJson ordinaryBinding (acceptedChainFacts "pending"))
        Assert.Equal(0, exitCode)
        Assert.Contains("\"state\":\"acceptedAwaitingChecks\"", out)
        Assert.Contains("\"action\":\"authorizeDelivery\"", out)
        // The row's whole purpose: a consumer must tell "this chain is broken" from "this chain is fine
        // and the next step is the delivery call" from the payload alone.
        Assert.Contains("\"stateErrors\":null", out)
        Assert.DoesNotContain("\"state\":\"malformedEvidence\"", out)
        Assert.DoesNotContain("review checks are not green", out)
        // Rendered through `Landable.name` — the same forward-only vocabulary the `checks` reader parses
        // in reverse — so the word read back is the word supplied. Matched with a regex rather than a
        // literal because `System.Text.Json` escapes the surrounding apostrophes to `\u0027`; pinning
        // the escape sequence would assert the serializer's encoding choice rather than the contract.
        Assert.Contains("\"stateReason\":\"the review chain is complete", out)
        Assert.Matches("checks are .{0,8}pending", out)

    [<Fact>]
    let ``#2549 an UNREADABLE check state renders park, never authorizeDelivery`` () =
        let exitCode, out, _err = runSnapshot (snapshotJson ordinaryBinding (acceptedChainFacts "unknown"))
        Assert.Equal(0, exitCode)
        Assert.Contains("\"state\":\"acceptedAwaitingChecks\"", out)
        Assert.Contains("\"action\":\"park\"", out)
        Assert.DoesNotContain("\"action\":\"authorizeDelivery\"", out)

    [<Fact>]
    let ``#2549 a red check state renders resumeImplementer, not broken evidence`` () =
        let _exitCode, out, _err = runSnapshot (snapshotJson ordinaryBinding (acceptedChainFacts "red"))
        Assert.Contains("\"state\":\"acceptedAwaitingChecks\"", out)
        Assert.Contains("\"action\":\"resumeImplementer\"", out)
        Assert.DoesNotContain("\"state\":\"malformedEvidence\"", out)

    [<Fact>]
    let ``#2549 green checks still render accepted, unchanged`` () =
        let _exitCode, out, _err = runSnapshot (snapshotJson ordinaryBinding (acceptedChainFacts "green"))
        Assert.Contains("\"state\":\"accepted\"", out)
        Assert.Contains("\"action\":\"accept\"", out)
        Assert.Contains("\"criticIdentity\":\"kite\"", out)

    [<Fact>]
    let ``#2549 an ABSENT repairAssertionGranted key parses exactly as before the field existed`` () =
        // The backward-compatibility case every existing snapshot producer relies on: the key is not
        // written at all, and the answer must be the pre-change one.
        let exitCode, out, _err = runSnapshot (snapshotJson ordinaryBinding (unmovedFacts ""))
        Assert.Equal(0, exitCode)
        Assert.Contains("\"state\":\"awaitingImplementerRepair\"", out)
        Assert.Contains("\"action\":\"resumeImplementer\"", out)
        Assert.DoesNotContain("refused, not consumed", out)

    [<Fact>]
    let ``#2549 an explicit NULL repairAssertionGranted is the same fact spelled out`` () =
        let exitCode, out, _err =
            runSnapshot (snapshotJson ordinaryBinding (unmovedFacts ""","repairAssertionGranted":null"""))
        Assert.Equal(0, exitCode)
        Assert.Contains("\"action\":\"resumeImplementer\"", out)
        Assert.DoesNotContain("refused, not consumed", out)

    [<Fact>]
    let ``#2549 a VALID grant is honored end to end on the wire`` () =
        let grant =
            ""","repairAssertionGranted":{"answeredReviewUrl":"https://reviews/1","candidateHeadSha":"head-a","grantedBy":"host-9b63","reason":"the obligations comment was repaired in place"}"""
        let exitCode, out, _err = runSnapshot (snapshotJson ordinaryBinding (unmovedFacts grant))
        Assert.Equal(0, exitCode)
        Assert.Contains("\"state\":\"awaitingSameCriticConfirmation\"", out)
        Assert.Contains("\"action\":\"resumeSameCritic\"", out)
        // The granter is named, because the safety argument for advancing at an unmoved head is that an
        // accountable third party — neither implementer nor critic — attested the repair.
        Assert.Contains("host-9b63", out)

    [<Fact>]
    let ``#2549 a REFUSED grant is reported distinctly from no grant at all`` () =
        let refused =
            ""","repairAssertionGranted":{"answeredReviewUrl":"https://reviews/9","candidateHeadSha":"head-a","grantedBy":"host-9b63","reason":"answers the wrong review"}"""
        let exitCode, out, _err = runSnapshot (snapshotJson ordinaryBinding (unmovedFacts refused))
        Assert.Equal(0, exitCode)
        Assert.Contains("\"action\":\"resumeImplementer\"", out)
        Assert.Contains("refused, not consumed", out)

    [<Fact>]
    let ``#2549 a MALFORMED repairAssertionGranted fails CLOSED and names the field`` () =
        // A non-object, non-null value must never read as "no grant was offered": those two lead to
        // different next actions, and in the refusing direction they look identical on the wire.
        let exitCode, _out, err =
            runSnapshot (snapshotJson ordinaryBinding (unmovedFacts ""","repairAssertionGranted":42"""))
        Assert.NotEqual(0, exitCode)
        Assert.Contains("repairAssertionGranted", err)

    [<Fact>]
    let ``#2549 a grant missing a required field fails CLOSED rather than parsing as partial`` () =
        let exitCode, _out, err =
            runSnapshot (
                snapshotJson
                    ordinaryBinding
                    (unmovedFacts ""","repairAssertionGranted":{"answeredReviewUrl":"https://reviews/1"}""")
            )
        Assert.NotEqual(0, exitCode)
        Assert.Contains("candidateHeadSha", err)
