namespace FS.GG.Coord.Cli.Tests

open System
open System.IO
open System.Text.Json
open Xunit
open FS.GG.Coord
open FS.GG.Coord.Cli

module ReviewApplicationTests =
    let private head = String.replicate 40 "a"
    let private subject = "FS-GG/.github#2175/pr/42"

    let private snapshot comments checks =
        JsonSerializer.Serialize
            {| binding =
                {| itemRef = "FS-GG/.github#2175"; pr = 42; headSha = head
                   claimGeneration = "fixture-claim"; implementerIdentity = "worker-1"
                   phase = "ordinary"; round = 1 |}
               facts =
                {| comments = comments; checks = checks; repairPhaseGranted = null
                   repairRouteAvailable = true |} |}

    let private run (projection: string) (raw: string) =
        let path = Path.GetTempFileName()
        File.WriteAllText(path, raw)
        try
            let opts = Options.parse [ "review"; "--snapshot"; path; projection ] |> function Ok value -> value | Error error -> failwith error
            let oldOut, oldErr = Console.Out, Console.Error
            use stdout = new StringWriter()
            use stderr = new StringWriter()
            Console.SetOut stdout
            Console.SetError stderr
            try ReviewApplication.run opts, stdout.ToString(), stderr.ToString()
            finally Console.SetOut oldOut; Console.SetError oldErr
        finally File.Delete path

    let private comments values =
        values
        |> List.map (fun (id, url, body) -> {| id = id; url = url; body = body |})
        |> List.toArray

    [<Fact>]
    let empty_thread_dispatches_critic () =
        let code, output, _ = run "--json" (snapshot (comments []) "pending")
        Assert.Equal(0, code)
        Assert.Contains("\"state\":\"awaitingInitialReview\"", output)
        Assert.Contains("\"action\":\"dispatchCritic\"", output)
        Assert.DoesNotContain("evidenceClassification", output)

    [<Fact>]
    let typed_acceptance_reaches_accept () =
        let chain = StructuredFixtures.acceptedReviewComments subject head "kestrel-1" |> comments
        let code, output, _ = run "--json" (snapshot chain "green")
        Assert.Equal(0, code)
        Assert.Contains("\"state\":\"accepted\"", output)
        Assert.Contains("\"action\":\"accept\"", output)
        Assert.Contains(head, output)
        Assert.Contains("kestrel-1", output)

    [<Fact>]
    let malformed_structured_record_fails_closed () =
        let malformed = comments [ 1L, "https://reviews/1", "<!-- fsgg:review-decision/v2 -->\n{}" ]
        let code, output, error = run "--json" (snapshot malformed "green")
        Assert.True(code <> 0 || output.Contains("malformedEvidence"), output + " | " + error)
        Assert.True(error.Contains("required field") || output.Contains("stateErrors"))

    [<Fact>]
    let historical_prose_cannot_authorize () =
        let retired = "<!-- fsgg:independent-review/" + "v1 -->\ncritic: kestrel-1\nreviewed-head: " + head + "\nverdict: pass"
        let legacy = comments [ 1L, "https://reviews/old", retired ]
        let code, output, error = run "--json" (snapshot legacy "green")
        Assert.Equal(0, code)
        Assert.Contains("\"state\":\"awaitingInitialReview\"", output)
        Assert.Contains("\"action\":\"dispatchCritic\"", output)
        Assert.DoesNotContain("\"action\":\"accept\"", output)
        Assert.Empty error

    [<Fact>]
    let text_projection_has_no_dual_read_classification () =
        let code, output, _ = run "--text" (snapshot (comments []) "pending")
        Assert.Equal(0, code)
        Assert.Contains("awaitingInitialReview", output)
        Assert.Contains("dispatchCritic", output)
        Assert.DoesNotContain("evidence", output)

    [<Fact>]
    let malformed_snapshot_is_refused () =
        let code, _, error = run "--json" "{\"binding\":\"not-an-object\",\"facts\":{}}"
        Assert.NotEqual(0, code)
        Assert.Contains("malformed", error)
