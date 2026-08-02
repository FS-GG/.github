namespace FS.GG.Coord.Cli.Tests

open System
open System.IO
open Xunit
open FS.GG.Coord.GitHub
open FS.GG.Coord.GitHub.Transport
open FS.GG.Coord.Cli

/// `verify-paths` catching a closing keyword next to the board's OWN `<repo>#<n>` shorthand — end to
/// end, through the real command (.github#2107).
///
/// `RefParseTests` holds the pure detector (`RefParsing.boardShorthandCloses`) and `ReadTests` holds
/// the read it is driven from (`Reads.prBody`) — but neither can assert what `verify-paths` RETURNS TO
/// A SHELL, and the exit code is the whole point: the defect this item exists to close is that the
/// PR's body silently fails to link, so a worker's own pre-merge self-check must go RED on it, not stay
/// quiet the way a bare `FSGG-PATHS OK` would. Same idiom as `LandableNotOpenTests` (#1680): only
/// driving `Client.verifyPaths` can assert that.
module VerifyPathsClosingKeywordTests =

    /// Routes on path suffix: `pulls/{n}` (serves BOTH `prHeadRef` and the new `prBody` — they read the
    /// same endpoint), `pulls/{n}/files`, and `issues/{n}`. Refuses anything else, so an unexpected read
    /// fails loud rather than serving a body it was never given.
    let private serving (prBody: string) (issueBody: string) (files: string) =
        Fake.Recorder(fun (req: Request) ->
            if req.Path.EndsWith "pulls/900/files" then
                Ok { Status = 200; Body = files; ETag = None; NextLink = None }
            elif req.Path.EndsWith "pulls/900" then
                Ok { Status = 200; Body = prBody; ETag = None; NextLink = None }
            elif req.Path.EndsWith "issues/42" then
                Ok { Status = 200; Body = issueBody; ETag = None; NextLink = None }
            else
                Error(Errors.NotFound $"unexpected read for this fixture: %s{req.Path}"))

    let private context (transport: Fake.Recorder) : Client.Context =
        { Transport = transport
          Owner = "FS-GG"
          Title = "Coordination"
          DefaultRepo = Some ".github"
          ChoreLocks = [] }

    /// Drive `Client.verifyPaths` and capture (exit code, stdout) — same cache-isolation licence as
    /// `LandableNotOpenTests.runLandable`.
    let private runVerifyPaths (transport: Fake.Recorder) (args: string list) : int * string =
        let dir = Path.Combine(Path.GetTempPath(), "fsgg-2107-" + Guid.NewGuid().ToString "n")
        let previousCache = Environment.GetEnvironmentVariable "FSGG_COORD_CACHE"
        let stdout = Console.Out
        use captured = new StringWriter()

        try
            Directory.CreateDirectory dir |> ignore
            Environment.SetEnvironmentVariable("FSGG_COORD_CACHE", dir)
            Console.SetOut captured

            let opts =
                match Options.parse args with
                | Ok o -> o
                | Error e -> failwithf "the fixture's own argv did not parse: %s" e

            let code = Client.verifyPaths (context transport) opts
            Console.Out.Flush()
            code, captured.ToString()
        finally
            Console.SetOut stdout
            Environment.SetEnvironmentVariable("FSGG_COORD_CACHE", previousCache)

            try
                Directory.Delete(dir, true)
            with _ ->
                ()

    let private issueBody = """{"number":42,"body":"Paths: some/file.txt"}"""
    let private files = """[{"filename":"some/file.txt"}]"""

    [<Fact>]
    let ``#2107 a board-shorthand closing keyword fails verify-paths even when the touch-set is clean`` () =
        let prBody =
            """{"number":900,"body":"Closes .github#42","head":{"ref":"item/42-x"},"base":{"ref":"main"}}"""

        let code, out =
            runVerifyPaths (serving prBody issueBody files) [ "verify-paths"; "--pr"; "900"; "--repo"; ".github" ]

        Assert.Equal(Client.ExitRed, code)
        Assert.Contains("FSGG-CLOSES DEFECT", out)
        Assert.Contains("`Closes .github#42`", out)
        // The touch-set itself was clean — the OK line still prints, so a reader can see BOTH facts
        // rather than losing the touch-set verdict to the new one.
        Assert.Contains("FSGG-PATHS OK", out)

    [<Fact>]
    let ``#2107 the correct same-repo form stays green`` () =
        let prBody =
            """{"number":900,"body":"Closes #42","head":{"ref":"item/42-x"},"base":{"ref":"main"}}"""

        let code, out =
            runVerifyPaths (serving prBody issueBody files) [ "verify-paths"; "--pr"; "900"; "--repo"; ".github" ]

        Assert.Equal(Client.ExitGreen, code)
        Assert.DoesNotContain("FSGG-CLOSES", out)
        Assert.Contains("FSGG-PATHS OK", out)

    [<Fact>]
    let ``#2107 the correct cross-repo owner/repo form also stays green`` () =
        let prBody =
            """{"number":900,"body":"Closes FS-GG/.github#42","head":{"ref":"item/42-x"},"base":{"ref":"main"}}"""

        let code, out =
            runVerifyPaths (serving prBody issueBody files) [ "verify-paths"; "--pr"; "900"; "--repo"; ".github" ]

        Assert.Equal(Client.ExitGreen, code)
        Assert.DoesNotContain("FSGG-CLOSES", out)

    [<Fact>]
    let ``#2107 a board-shorthand defect turns an already-RED drift verdict red for the same reason, not silently`` () =
        // A file the touch-set does not cover: genuine drift, independent of the closing-keyword defect.
        let prBody =
            """{"number":900,"body":"Closes .github#42","head":{"ref":"item/42-x"},"base":{"ref":"main"}}"""

        let driftFiles = """[{"filename":"unrelated/other.txt"}]"""

        let code, out =
            runVerifyPaths (serving prBody issueBody driftFiles) [ "verify-paths"; "--pr"; "900"; "--repo"; ".github" ]

        Assert.Equal(Client.ExitRed, code)
        Assert.Contains("FSGG-PATHS DRIFT", out)
        Assert.Contains("FSGG-CLOSES DEFECT", out)
