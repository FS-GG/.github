namespace FS.GG.Coord.Cli.Tests

open System
open System.IO
open System.Text.Json
open Xunit
open FS.GG.Coord.GitHub
open FS.GG.Coord.GitHub.Transport
open FS.GG.Coord.Cli

/// `verify-paths` no longer reporting an `sdd-required` item's OWN mandatory `work/<id>/` +
/// `readiness/<id>/` output as touch-set drift (.github#2324) — end to end, through the real command.
///
/// `DeliveryRouteTests` holds the pure derivation (`DeliveryRoute.mandatorySddPaths`), but the defect
/// this item closes is not a derivation: it is the VERDICT a worker's own pre-merge check returns, and
/// the mid-flight `widen` that verdict forces on every single `sdd-required` item. Only driving
/// `Client.verifyPaths` against a scripted transport can assert that — the same idiom
/// `VerifyPathsClosingKeywordTests` (#2107) and `LandableNotOpenTests` (#1680) use.
module VerifyPathsSddPackageTests =

    /// The delivery-route receipt exactly as it lives on an issue: the marker line, a newline, then the
    /// JSON. `Client`'s search matches on that literal prefix, so the fixture must reproduce it rather
    /// than approximate it.
    let private receiptComment (subjectRevision: string) (route: string) (workId: string option) =
        let marker = "<!-- fsgg:delivery-route/v1 -->"

        // `requiredGates` is REQUIRED by the decoder on BOTH routes — a lightweight receipt carries it
        // empty, it does not omit it. Omitting it makes the receipt fail to DECODE, which reads as
        // `Stale` ("receipt is missing") and would silently turn a lightweight-route test into a second
        // stale-receipt test. (Measured: the first cut of this fixture did exactly that, and the
        // fail-open inversion below reddened the "lightweight" case for the wrong reason.)
        let bindings =
            match workId with
            | Some id ->
                $""","sddWorkId":"%s{id}","specHome":"work/%s{id}/spec.md","requiredGates":["implementationReady","analyze","verify","ship"]"""
            | None -> ""","requiredGates":[]"""

        marker
        + "\n{\"schema\":\"fsgg.coord.delivery-route/v1\",\"subject\":\"FS-GG/.github#42\",\"subjectRevision\":\""
        + subjectRevision
        + "\",\"route\":\""
        + route
        + "\",\"agent\":\"fixture-host\",\"timestamp\":\"2026-08-13T00:00:00Z\",\"reasonCodes\":[\"multi-phase\"],"
        + "\"rationale\":\"fixture\",\"declaredImpacts\":[\"engine\"],\"observedFacts\":[\"process-wide\"]"
        + bindings
        + "}"

    /// The engine hashes the issue body's own canonical SUBJECT to decide whether a receipt is current
    /// (.github#2392), and that hash is `private`. Rather than restate the formula — which would make
    /// this fixture a second implementation of the very rule under test — the fixture serves a receipt
    /// per candidate revision and lets the engine pick: the LEGACY whole-body SHA-256 is the one form a
    /// test can compute from public facts alone, and `decideDeliveryRoute`'s documented migration bridge
    /// accepts it. A receipt bound to neither is what the `stale` fixture below deliberately serves.
    let private legacyRevision (body: string) =
        body
        |> Text.Encoding.UTF8.GetBytes
        |> Security.Cryptography.SHA256.HashData
        |> Convert.ToHexString
        |> _.ToLowerInvariant()

    let private graphQlComments (bodies: string list) =
        let nodes =
            bodies
            |> List.map (fun b -> $"""{{"body":%s{JsonSerializer.Serialize b}}}""")
            |> String.concat ","

        $"""{{"data":{{"repository":{{"issue":{{"comments":{{"nodes":[%s{nodes}]}}}}}}}},"data2":null}}"""

    /// Routes on path suffix, and REFUSES anything else so an unexpected read fails loud rather than
    /// being served a body it was never given. `graphql` is the receipt window
    /// (`Reads.recentCommentBodies`); a `commentBodies` argument of `None` serves an ERROR there, which
    /// is how the fail-closed leg is driven.
    let private serving (issueBody: string) (files: string) (comments: string list option) =
        let prBody =
            """{"number":900,"body":"Closes #42","head":{"ref":"item/42-x"},"base":{"ref":"main"}}"""

        Fake.Recorder(fun (req: Request) ->
            if req.Path.EndsWith "graphql" then
                match comments with
                | Some bodies -> Ok { Status = 200; Body = graphQlComments bodies; ETag = None; NextLink = None; Headers = Map.empty }
                | None -> Error(Errors.Malformed("receipt window", "the fixture refuses this read"))
            elif req.Path.EndsWith "pulls/900/files" then
                Ok { Status = 200; Body = files; ETag = None; NextLink = None; Headers = Map.empty }
            elif req.Path.EndsWith "pulls/900" then
                Ok { Status = 200; Body = prBody; ETag = None; NextLink = None; Headers = Map.empty }
            elif req.Path.EndsWith "issues/42" then
                Ok { Status = 200; Body = issueBody; ETag = None; NextLink = None; Headers = Map.empty }
            else
                Error(Errors.NotFound $"unexpected read for this fixture: %s{req.Path}"))

    let private context (transport: Fake.Recorder) : Client.Context =
        { Transport = transport
          Owner = "FS-GG"
          Title = "Coordination"
          DefaultRepo = Some ".github"
          ChoreLocks = [] }

    /// Drive `Client.verifyPaths` and capture (exit code, stdout) — same cache-isolation licence as
    /// `VerifyPathsClosingKeywordTests.runVerifyPaths`.
    let private runVerifyPaths (transport: Fake.Recorder) : int * string =
        let dir = Path.Combine(Path.GetTempPath(), "fsgg-2324-" + Guid.NewGuid().ToString "n")
        let previousCache = Environment.GetEnvironmentVariable "FSGG_COORD_CACHE"
        let stdout = Console.Out
        use captured = new StringWriter()

        try
            Directory.CreateDirectory dir |> ignore
            Environment.SetEnvironmentVariable("FSGG_COORD_CACHE", dir)
            Console.SetOut captured

            let opts =
                match Options.parse [ "verify-paths"; "--pr"; "900"; "--repo"; ".github" ] with
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

    /// The declared touch-set names ONLY the implementation file — exactly the shape every `sdd-required`
    /// item carries at filing time, because `work/<id>/` and `readiness/<id>/` cannot be named before the
    /// item exists.
    let private declaredBody = "Paths: src/Impl.fs"
    let private issue = $"""{{"number":42,"body":%s{JsonSerializer.Serialize declaredBody}}}"""
    let private sddReceipt = receiptComment (legacyRevision declaredBody) "sdd-required" (Some "2324-mandatory-sdd-output-enforcement")

    let private files (names: string list) =
        names
        |> List.map (fun n -> $"""{{"filename":%s{JsonSerializer.Serialize n}}}""")
        |> String.concat ","
        |> sprintf "[%s]"

    let private packageFiles =
        [ "src/Impl.fs"
          "work/2324-mandatory-sdd-output-enforcement/spec.md"
          "readiness/2324-mandatory-sdd-output-enforcement/analysis.json" ]

    [<Fact>]
    let ``#2324 AC-001 the item's own sdd package is expected output, not drift`` () =
        let code, out = runVerifyPaths (serving issue (files packageFiles) (Some [ sddReceipt ]))

        Assert.Equal(Client.ExitGreen, code)
        Assert.Contains("FSGG-PATHS OK", out)
        Assert.DoesNotContain("FSGG-PATHS DRIFT", out)
        Assert.Contains("sdd package (expected)", out)
        Assert.Contains("work/2324-mandatory-sdd-output-enforcement/spec.md", out)
        Assert.Contains("readiness/2324-mandatory-sdd-output-enforcement/analysis.json", out)
        // REPORTED, not silently swallowed: the exemption is a fact a reviewer can check.
        Assert.DoesNotContain("undeclared (review)", out)

    [<Fact>]
    let ``#2324 AC-002 a genuinely undeclared file still drifts beside the exempted package`` () =
        let code, out =
            runVerifyPaths (serving issue (files (packageFiles @ [ "src/Sneaky.fs" ])) (Some [ sddReceipt ]))

        Assert.Equal(Client.ExitRed, code)
        Assert.Contains("FSGG-PATHS DRIFT", out)
        Assert.Contains("undeclared (review)", out)
        Assert.Contains("src/Sneaky.fs", out)
        // The package files are still reported as expected, and are NOT in the undeclared list — the
        // exemption removes exactly those files from the verdict, never anything else.
        Assert.Contains("sdd package (expected)", out)
        let undeclaredSection = out.Substring(out.IndexOf "undeclared (review)")
        let expectedIndex = undeclaredSection.IndexOf "sdd package (expected)"
        let undeclaredOnly = undeclaredSection.Substring(0, expectedIndex)
        Assert.DoesNotContain("work/2324-mandatory-sdd-output-enforcement", undeclaredOnly)

    [<Fact>]
    let ``#2324 AC-003 a CURRENT lightweight route exempts nothing at all`` () =
        // The receipt is genuinely `Current` here — it decodes and validates — so this is the ROUTE
        // deciding, not a read failure standing in for one. The package directories are spelled in the
        // bare item-number form `.github#2496` actually used, so an implementation that derived them from
        // the ISSUE NUMBER instead of from the receipt's own `sddWorkId` would wrongly exempt them and
        // this assertion would catch it.
        let lightweight = receiptComment (legacyRevision declaredBody) "lightweight" None

        let numberShapedPackage = [ "src/Impl.fs"; "work/42/spec.md"; "readiness/42/analysis.json" ]

        let code, out = runVerifyPaths (serving issue (files numberShapedPackage) (Some [ lightweight ]))

        Assert.Equal(Client.ExitRed, code)
        Assert.Contains("FSGG-PATHS DRIFT", out)
        Assert.Contains("undeclared (review)", out)
        Assert.Contains("work/42/spec.md", out)
        Assert.Contains("readiness/42/analysis.json", out)
        Assert.DoesNotContain("sdd package (expected)", out)

    [<Fact>]
    let ``#2324 AC-004 an unreadable receipt window subtracts nothing rather than everything`` () =
        let code, out = runVerifyPaths (serving issue (files packageFiles) None)

        Assert.Equal(Client.ExitRed, code)
        Assert.Contains("FSGG-PATHS DRIFT", out)
        Assert.Contains("work/2324-mandatory-sdd-output-enforcement/spec.md", out)
        Assert.DoesNotContain("sdd package (expected)", out)

    [<Fact>]
    let ``#2324 AC-004 a stale receipt is not a licence to exempt`` () =
        // Bound to a revision that is neither the canonical subject hash nor the legacy whole-body hash,
        // so `decideDeliveryRoute` answers `Stale` through both candidates.
        let stale = receiptComment "not-this-body" "sdd-required" (Some "2324-mandatory-sdd-output-enforcement")

        let code, out = runVerifyPaths (serving issue (files packageFiles) (Some [ stale ]))

        Assert.Equal(Client.ExitRed, code)
        Assert.Contains("FSGG-PATHS DRIFT", out)
        Assert.DoesNotContain("sdd package (expected)", out)

    [<Fact>]
    let ``#2324 AC-005 another item's sdd package is ordinary drift`` () =
        let otherItemsPackage =
            [ "src/Impl.fs"; "work/9999-someone-elses-item/spec.md"; "readiness/9999-someone-elses-item/analysis.json" ]

        let code, out = runVerifyPaths (serving issue (files otherItemsPackage) (Some [ sddReceipt ]))

        Assert.Equal(Client.ExitRed, code)
        Assert.Contains("FSGG-PATHS DRIFT", out)
        Assert.Contains("undeclared (review)", out)
        Assert.Contains("work/9999-someone-elses-item/spec.md", out)
        Assert.Contains("readiness/9999-someone-elses-item/analysis.json", out)
        Assert.DoesNotContain("sdd package (expected)", out)

    [<Fact>]
    let ``#2324 AC-007 a PR with no drift asks for no receipt at all`` () =
        let transport = serving issue (files [ "src/Impl.fs" ]) (Some [ sddReceipt ])
        let code, out = runVerifyPaths transport

        Assert.Equal(Client.ExitGreen, code)
        Assert.Contains("FSGG-PATHS OK", out)
        Assert.DoesNotContain("sdd package (expected)", out)
        // THE COST ASSERTION, not merely the output one: `List.isEmpty drift` must gate the network call,
        // or every green PR in the org pays a GraphQL round-trip for a subtraction it does not need. The
        // receipt window is this command's ONLY GraphQL-billed read, so the meter is an exact witness.
        Assert.Equal(0, transport.GraphQlCalls)

    [<Fact>]
    let ``#2324 the receipt window is read exactly once when there IS drift to subtract from`` () =
        let transport = serving issue (files packageFiles) (Some [ sddReceipt ])
        let code, _ = runVerifyPaths transport

        Assert.Equal(Client.ExitGreen, code)
        Assert.Equal(1, transport.GraphQlCalls)
