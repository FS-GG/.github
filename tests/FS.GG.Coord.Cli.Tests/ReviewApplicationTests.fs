namespace FS.GG.Coord.Cli.Tests

open System
open System.IO
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
        Assert.Contains("\"freshnessToken\"", out)
        Assert.Contains("\"actionKey\"", out)

    [<Fact>]
    let ``#2175 the text projection renders one state — action line`` () =
        let exitCode, out, _err = runText (snapshotJson ordinaryBinding (emptyFacts "pending"))
        Assert.Equal(0, exitCode)
        Assert.Equal("awaitingInitialReview — dispatchCritic", out.Trim())

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
