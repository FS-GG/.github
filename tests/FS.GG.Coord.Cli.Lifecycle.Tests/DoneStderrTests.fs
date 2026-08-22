namespace FS.GG.Coord.Cli.Tests

open System
open System.IO
open System.Text.RegularExpressions
open Xunit
open FS.GG.Coord.Types
open FS.GG.Coord.GitHub
open FS.GG.Coord.GitHub.Transport
open FS.GG.Coord.Cli

/// .github#2444 — `done`'s passed-over-foreign-closer advisory (.github#2427) must ride STDERR, never the
/// stdout FSGG-DONE line: `.github#2427`'s own acceptance criterion says so, and #733's `AfterDone` chore
/// offer set the precedent this codebase already keeps for "a candidate existed but was not chosen"
/// (`tests/coord-engine-e2e/writes.sh:456-462`). `DoneTests` (`FS.GG.Coord.GitHub.Tests`) pins the pure
/// `Done.render`/`renderReceipt`/`passedOverForeignNote` split; THIS file is the thing those tests cannot
/// be — the actual process boundary `FS.GG.Coord.Cli.Lifecycle.LiveHandlers.doneCmd (fun _ _ _ -> ())` writes across, exercised end to end through a fake
/// transport, the same technique `LandableNotOpenTests`/`ForceStealTests` already use for a full-command
/// round trip in this test project.
module DoneStderrTests =

    let private ref =
        { Owner = "FS-GG"
          Repo = ".github"
          Number = 9001 }

    let private ok (body: string) : Errors.IoResult<Response> =
        Ok
            { Status = 200
              Body = body
              ETag = None
              NextLink = None; Headers = Map.empty }

    /// The board `bootstrapCached` needs — just enough for it to resolve a `Status` field. A fresh
    /// `FSGG_COORD_CACHE` per test (same licence as `ForceStealTests`) means nothing carries over between
    /// runs.
    let private boardAnswer (query: string) : string option =
        if query.Contains "projectsV2" then
            Some
                """{"data":{"organization":{"projectsV2":{"nodes":[{"number":12,"title":"Coordination","id":"PVT_coord"}]}}},"rateLimit":{"cost":1,"remaining":4977}}"""
        elif query.Contains "fields(first" then
            Some
                """{"data":{"organization":{"projectV2":{"fields":{"nodes":[{"id":"PVTSSF_status","name":"Status","dataType":"SINGLE_SELECT","options":[{"id":"opt_done","name":"Done"}]}]}}}},"rateLimit":{"cost":1,"remaining":4977}}"""
        else
            None

    /// A `closedByPullRequestsReferences` node: a MERGED true closer (its `closingIssuesReferences` names
    /// `ref` itself), in the given repo, merged at the given instant. Mirrors
    /// `FS.GG.Coord.GitHub.Tests.DoneFactsTests`'s `closesThis`, generalised over repo/number/mergedAt so
    /// it can build both the same-repository winner and the foreign-repository closer passed over.
    ///
    /// PLAIN CONCATENATION, not a `$$"""..."""` interpolated string: JSON's own adjacent `}}` (two
    /// objects closing back to back) is indistinguishable from a `$$` string's own hole-closing delimiter,
    /// and F# refuses the literal as an unmatched interpolation (measured while authoring this fixture —
    /// `error FS1249`). `DoneFactsTests`'s `closesThis` and `ForceStealTests`' fixtures both sidestep it
    /// the same way.
    let private trueCloser (number: int) (repo: string) (mergedAt: string) (oid: string) =
        """{"number":"""
        + string number
        + ""","merged":true,"mergedAt":"""
        + "\""
        + mergedAt
        + "\""
        + ""","mergeCommit":{"abbreviatedOid":"""
        + "\""
        + oid
        + "\""
        + """},"repository":{"nameWithOwner":"""
        + "\""
        + repo
        + "\""
        + """},"closingIssuesReferences":{"nodes":[{"number":"""
        + string ref.Number
        + ""","repository":{"nameWithOwner":"""
        + "\""
        + ref.Owner
        + "/"
        + ref.Repo
        + "\""
        + """}}]}}"""

    /// `Done.facts`' one query (`FactsDoc`), answered with a SAME-repository true closer that merged FIRST
    /// and a FOREIGN-repository true closer that merged LATER — .github#2427's own preference (same-repo
    /// wins regardless of merge time) makes #413 the winner and passes #195 over, which is the exact shape
    /// `passedOverForeignNote`/`renderReceipt` exist for.
    let private factsAnswer =
        let closingPrs =
            trueCloser 413 "FS-GG/.github" "2026-08-10T00:00:00Z" "e605d37"
            + ","
            + trueCloser 195 "EHotwagner/S.I.R." "2026-08-12T00:00:00Z" "9f9f9f9"

        """{"data":{"repository":{"issue":{"number":"""
        + string ref.Number
        + ""","state":"CLOSED","closedByPullRequestsReferences":{"nodes":["""
        + closingPrs
        + """]},"timelineItems":{"nodes":[]},"subIssues":{"totalCount":0,"nodes":[]},"projectItems":{"nodes":[{"project":{"number":12},"status":{"name":"In progress"}}]},"parent":null}}},"rateLimit":{"cost":1,"remaining":4977}}"""

    /// Posted comment bodies, so a test can also assert the durable receipt's divergence from stdout —
    /// not just that stdout stayed clean.
    let private postedComments = System.Collections.Generic.List<string>()

    let private world =
        Fake.Recorder(fun (req: Request) ->
            let path = req.Path.Trim '/'

            match req.Method, path with
            | "POST", "graphql" ->
                match req.Body with
                | Query(document, _) when document.Contains "comments(last:" ->
                    // `Writes.verifyHeld`'s marker scan — nobody holds this fixture's item, so an empty
                    // thread answers it and `doneCmd` takes the `DoesNotHold` branch quietly.
                    ok """{"data":{"repository":{"issue":{"comments":{"nodes":[]}}}},"rateLimit":{"cost":1,"remaining":4977}}"""
                | Query(document, _) when document.Contains "closedByPullRequestsReferences" -> ok factsAnswer
                | Query(document, _) ->
                    match boardAnswer document with
                    | Some answer -> ok answer
                    | None ->
                        // The board Status MUTATION lands here (and nowhere else) — deliberately unserved,
                        // same licence as `ForceStealTests`: the write under test is the stdout/stderr
                        // split, not the board projection, and `boardWriteNote`'s failure path is silent to
                        // the exit code (.github#2444 does not touch that behaviour).
                        Error(Errors.NotFound "the fixture serves no board WRITE — done's stdout/stderr split is what is under test")
                | _ -> Error(Errors.NotFound "a graphql call with no document")
            | "GET", "rate_limit" -> ok """{"resources":{"graphql":{"remaining":4980,"limit":5000}}}"""
            | "GET", p when p.EndsWith "issues/9001/comments" -> ok "[]"
            | "POST", p when p.EndsWith "issues/9001/comments" ->
                match req.Body with
                | Json payload ->
                    let body =
                        System.Text.Json.JsonDocument.Parse(payload).RootElement.GetProperty("body").GetString()

                    postedComments.Add body
                    ok """{"id":7042}"""
                | _ -> Error(Errors.NotFound "a comment POST with no JSON body")
            | m, p -> Error(Errors.NotFound $"the fixture serves no %s{m} %s{p}"))

    let private context : Kernel.Context =
        { Transport = world
          Owner = "FS-GG"
          Title = "Coordination"
          DefaultRepo = Some ".github"
          ChoreLocks = [] }

    /// Drive `FS.GG.Coord.Cli.Lifecycle.LiveHandlers.doneCmd (fun _ _ _ -> ())` end to end, capturing stdout and stderr SEPARATELY — the two streams
    /// `.github#2444` is about — against a throwaway cache/queue root, same licence as
    /// `LandableNotOpenTests.runLandable`/`ForceStealTests.runClaim`: `AssemblyInfo.fs` disables
    /// cross-class parallelism, so pointing the process-global `FSGG_COORD_CACHE` somewhere private per
    /// call is safe.
    let private runDone () : int * string * string =
        let dir = Path.Combine(Path.GetTempPath(), "fsgg-2444-" + Guid.NewGuid().ToString "n")
        let previousCache = Environment.GetEnvironmentVariable "FSGG_COORD_CACHE"
        let previousKitRoot = Environment.GetEnvironmentVariable "FSGG_KIT_ROOT"
        let stdout = Console.Out
        let stderr = Console.Error
        use capturedOut = new StringWriter()
        use capturedErr = new StringWriter()

        try
            Directory.CreateDirectory dir |> ignore
            Environment.SetEnvironmentVariable("FSGG_COORD_CACHE", dir)
            Environment.SetEnvironmentVariable("FSGG_KIT_ROOT", dir)
            Console.SetOut capturedOut
            Console.SetError capturedErr

            let opts =
                match Options.parse [ "done"; "FS-GG/.github#9001"; "--worker"; "snipe-2444" ] with
                | Ok o -> o
                | Error e -> failwithf "the fixture's own argv did not parse: %s" e

            let code = FS.GG.Coord.Cli.Lifecycle.LiveHandlers.doneCmd (fun _ _ _ -> ()) context opts
            Console.Out.Flush()
            Console.Error.Flush()
            code, capturedOut.ToString(), capturedErr.ToString()
        finally
            Console.SetOut stdout
            Console.SetError stderr
            Environment.SetEnvironmentVariable("FSGG_COORD_CACHE", previousCache)
            Environment.SetEnvironmentVariable("FSGG_KIT_ROOT", previousKitRoot)

            try
                Directory.Delete(dir, true)
            with _ ->
                ()

    [<Fact>]
    let ``#2444 done's stdout carries only the FSGG-DONE verdict - the passed-over note is not on it`` () =
        postedComments.Clear()
        let code, stdout, stderr = runDone ()

        Assert.Equal(0, code)
        Assert.Contains("FSGG-DONE", stdout)
        Assert.Contains("PR #413", stdout)
        // NOT ON STDOUT: a caller that `grep`s or diffs done's stdout exactly must see nothing beyond the
        // verdict, matching `.github#2427`'s own acceptance criterion and #733's stderr precedent.
        Assert.DoesNotContain("passed over", stdout)
        Assert.DoesNotContain("EHotwagner/S.I.R.", stdout)

        // ON STDERR, exactly: this is the channel `.github#2427` asked for and #2439 missed.
        Assert.Contains("passed over", stderr)
        Assert.Contains("EHotwagner/S.I.R.#195", stderr)
        Assert.Contains("PR #413", stderr)

        // EXACTLY ONCE — `.github#2444`'s own acceptance criterion, and the property `Assert.Contains`
        // above CANNOT see: `render`/`renderReceipt` are both called for the SAME verdict (once for the
        // console line, once for the durable receipt), so a caller that re-derives and re-prints the note
        // at each call site would print it TWICE, and every `Contains`/`DoesNotContain` assertion in this
        // test passes identically either way — containment cannot detect cardinality. Counted on the
        // stable, specific sentence `Done.passedOverForeignNote` emits (not the bare "passed over", which
        // a coincidental second occurrence elsewhere in stderr could satisfy without being the SAME print).
        let occurrences =
            Regex.Matches(stderr, Regex.Escape "a foreign-repository closer was passed over").Count

        Assert.Equal(1, occurrences)

    [<Fact>]
    let ``#2444 the durable receipt comment DELIBERATELY keeps the note stdout no longer carries`` () =
        postedComments.Clear()
        runDone () |> ignore

        // Exactly one comment posted (the done-receipt) — and it is the RECEIPT, not a repeat of stdout's
        // clean line: it deliberately diverges from stdout by keeping the cross-repo provenance note.
        Assert.Single(postedComments) |> ignore
        let receipt = postedComments.[0]
        Assert.Contains("fsgg:done-receipt", receipt)
        Assert.Contains("FSGG-DONE", receipt)
        Assert.Contains("PR #413", receipt)
        Assert.Contains("EHotwagner/S.I.R.#195", receipt)
        Assert.Contains("passed over", receipt)
