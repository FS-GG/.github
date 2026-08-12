namespace FS.GG.Coord.Cli.Tests

open System
open System.Collections.Generic
open System.IO
open System.Security.Cryptography
open System.Text
open System.Text.Json
open Xunit
open FS.GG.Coord.GitHub
open FS.GG.Coord.GitHub.Transport
open FS.GG.Coord.Cli

/// .github#2459 — `claim <ref>` now runs the SAME #353 collision scan `widen`/`overlap --active` already
/// share (`Client.activeCollisions`), against the item's OWN declared touch-set, before it reports success
/// — and REPORTS (default) or REFUSES (`--refuse-overlap`) a live collision it finds.
///
/// THE GAP THIS CLOSES. `claim` is the SAME lock `take`/`batch` use, but WITHOUT their upstream
/// overlap-avoidance: a candidate `take`/`batch` would never even offer (because it collides with a live
/// claim) is reachable through a bare `claim` with nothing warning either worker. The live incident the
/// issue measured: one worker held `.github#2216` via `take` (which pre-filters), a second reached
/// `.github#2395` — an INTERSECTING touch-set — through a `claim` used to recover a stranded item, and
/// nothing surfaced the collision until a merge conflict after a full independent review round had
/// already been spent on the second PR.
///
/// THE ACCEPTANCE CRITERIA THESE PIN:
///   AC1/AC4 — `claim` runs the scan and reports the OTHER holder and the shared tokens.
///   AC2     — the report is a WARNING that still claims by default (exit stays green).
///   AC3     — `--refuse-overlap` refuses instead (`ExitContended`, mirroring `overlap --active`/`take`).
///   (AC5's inversion is documented on the module, since it is the same fixture read twice.)
///
/// GATE-INVERSION EVIDENCE. Reverting `Client.claim`'s `Ok []` arm to the pre-#2459 body (dropping the
/// `earlyExit`/`overlapCollisions` block entirely) turns every `OVERLAP`/`ExitContended`/`collisions`
/// assertion below red: the claim still wins the lock (correctly — the LOCK mechanism is untouched), but
/// silently, with no OVERLAP line on stderr, no notice posted to the other holder, and `[]` in
/// `--json`'s `collisions` array — exactly the silent gap #2459 reports. Recorded by hand against the
/// pre-fix source at review time, alongside the disjoint-set control leg, which stays green throughout
/// (it has nothing to invert: no collision exists for either version of the code to find).
module ClaimOverlapTests =

    let private ok (body: string) : Errors.IoResult<Response> =
        Ok
            { Status = 200
              Body = body
              ETag = None
              NextLink = None
              Headers = Map.empty }

    let private currentRouteComment (paths: string) =
        let revision = SHA256.HashData(Encoding.UTF8.GetBytes paths) |> Convert.ToHexString |> _.ToLowerInvariant()

        "<!-- fsgg:delivery-route/v1 -->\n"
        + $"""{{"schema":"fsgg.coord.delivery-route/v1","subject":"FS-GG/FS.GG.SDD#42","subjectRevision":"%s{revision}","route":"lightweight","agent":"fixture-2459","timestamp":"2026-01-01T00:00:00Z","reasonCodes":["fixture"],"rationale":"fixture route receipt","declaredImpacts":["internal"],"observedFacts":["localized"],"sddWorkId":null,"specHome":null,"requiredGates":[]}}"""

    /// The board: one project, one Status field, and a single OPEN row for the item under claim (#42).
    /// `activeCollisions`'s closed-unstamped scan reads this same board and finds nothing to add, because
    /// #42 is OPEN — so this fixture never has to answer for a closed-but-unstamped candidate.
    let private graphqlAnswer (document: string) : string option =
        if document.Contains "projectsV2" then
            Some
                """{"data":{"organization":{"projectsV2":{"nodes":[{"number":12,"title":"Coordination","id":"PVT_coord"}]}}},"rateLimit":{"cost":1,"remaining":4977}}"""
        elif document.Contains "fields(first" then
            Some
                """{"data":{"organization":{"projectV2":{"fields":{"nodes":[{"id":"PVTSSF_status","name":"Status","dataType":"SINGLE_SELECT","options":[{"id":"opt_ready","name":"Ready"},{"id":"opt_wip","name":"In progress"}]}]}}}},"rateLimit":{"cost":1,"remaining":4977}}"""
        elif document.Contains "items(first" then
            Some
                // `status` reads "In progress" already — the claim's own board WRITE is deliberately not
                // served (`ForceStealTests`' own licence: the LOCK is what these fixtures test), so the
                // post-claim READBACK must already show the state a successful write would have produced,
                // or `Converged` reads false over a lock that is, in fact, held.
                """{"data":{"organization":{"projectV2":{"items":{"pageInfo":{"hasNextPage":false,"endCursor":null},"nodes":[{"status":{"name":"In progress"},"blockedBy":null,"content":{"__typename":"Issue","number":42,"title":"item 42","state":"OPEN","repository":{"nameWithOwner":"FS-GG/FS.GG.SDD"}}}]}}}},"rateLimit":{"cost":1,"remaining":4977}}"""
        elif document.Contains "projectItems(first: 20)" && document.Contains "fieldValueByName(name: \"Status\")" then
            // `Board.itemStatus`'s per-item RESOLVER read (`repositoryItemStatus`, `readPreviousStatus`'s
            // pre-claim read AND the post-claim receipt readback both use it) — a SEPARATE query shape from
            // the board-wide `items(first...)` scan above, so it needs its own answer or the readback 404s
            // even though the scan above already "sees" the item.
            Some
                """{"data":{"repository":{"issue":{"projectItems":{"nodes":[{"project":{"number":12},"fieldValueByName":{"name":"In progress"}}]}}}},"rateLimit":{"cost":1,"remaining":4977}}"""
        else
            None

    /// A LIVE comment thread on the item under claim (#42), mutated the way GitHub would: a POST adds a
    /// comment with the next id. #42 holds NO claim marker of its own at the start of every leg here — the
    /// whole point is a FRESH claim on a free item that happens to share paths with someone else's.
    type private Thread() =
        let comments = Dictionary<int64, string>()
        let posted = ResizeArray<string>()
        let mutable nextId = 9000L

        member _.Posted = List.ofSeq posted

        member _.Json(paths: string) =
            let ts = DateTime.UtcNow.ToString "yyyy-MM-ddTHH:mm:ssZ"

            let route =
                JsonSerializer.Serialize
                    {| id = 7001L
                       body = currentRouteComment paths
                       user = {| login = "EHotwagner" |}
                       created_at = ts
                       updated_at = ts |}

            let claims =
                comments
                |> Seq.sortBy (fun kv -> kv.Key)
                |> Seq.map (fun kv ->
                    $"""{{"id":%d{kv.Key},"body":"%s{kv.Value}","user":{{"login":"EHotwagner"}},"created_at":"%s{ts}","updated_at":"%s{ts}"}}""")
                |> String.concat ","

            "[" + route + (if String.IsNullOrEmpty claims then "" else "," + claims) + "]"

        member _.Add(body: string) =
            nextId <- nextId + 1L
            comments.[nextId] <- body.Replace("\n", "\\n").Replace("\"", "\\\"")
            posted.Add body
            nextId

        /// Same shape `ForceStealTests.Thread.Bodies` serves: the SAME thread the REST `/comments` arm
        /// reads from, as bare bodies oldest-first, for `requireCurrentDeliveryRoute`'s bounded GraphQL
        /// `comments(last: N)` marker search.
        member _.Bodies(paths: string) =
            let claims =
                comments
                |> Seq.sortBy (fun kv -> kv.Key)
                |> Seq.map (fun kv -> kv.Value.Replace("\\n", "\n").Replace("\\\"", "\""))
                |> List.ofSeq

            currentRouteComment paths :: claims

    /// The world: item #42 (free, under claim, declaring `paths`) in `FS-GG/FS.GG.SDD`, plus — when
    /// `other` is `Some (number, holder, otherPaths)` — a SECOND open issue in the SAME repo carrying a
    /// live claim marker for `holder`, declaring `otherPaths`. `other = None` is the disjoint control:
    /// no second item exists on the board OR the open-issues listing at all.
    let private world (paths: string) (other: (int * string * string) option) (thread: Thread) =
        let otherComments (holder: string) =
            let ts = DateTime.UtcNow.ToString "yyyy-MM-ddTHH:mm:ssZ"

            $"""[{{"id":8070,"body":"<!-- fsgg:claim worker=%s{holder} lease=120 -->\nheld","user":{{"login":"EHotwagner"}},"created_at":"%s{ts}","updated_at":"%s{ts}"}}]"""

        Fake.Recorder(fun (req: Request) ->
            let path = req.Path.Trim '/'

            match req.Method, path with
            // `requireCurrentDeliveryRoute`'s bounded marker search — served from the SAME thread the
            // REST `/comments` arm below reads, exactly as `ForceStealTests.world` serves it.
            | "POST", "graphql" when
                (match req.Body with
                 | Query(document, _) -> document.Contains "comments(last:"
                 | _ -> false)
                ->
                match req.Body with
                | Query(_, variables) ->
                    let lastVar =
                        variables
                        |> List.tryFind (fun (k, _) -> k = "last")
                        |> Option.bind (fun (_, v) -> match v with VNumber n -> Some(int n) | _ -> None)

                    match lastVar with
                    | Some last ->
                        let recent =
                            thread.Bodies paths
                            |> List.rev
                            |> List.truncate last
                            |> List.rev
                            |> List.map (fun body -> {| body = body |})
                            |> JsonSerializer.Serialize

                        let payload =
                            "{\"data\":{\"repository\":{\"issue\":{\"comments\":{\"nodes\":"
                            + recent
                            + "}}}},\"rateLimit\":{\"cost\":1,\"remaining\":4977}}"

                        ok payload
                    | None -> Error(Errors.NotFound "the recent-comments query is missing a `last` variable")
                | _ -> Error(Errors.NotFound "a graphql call with no document")
            | "POST", "graphql" ->
                match req.Body with
                | Query(document, _) ->
                    match graphqlAnswer document with
                    | Some answer -> ok answer
                    | None -> Error(Errors.NotFound "the fixture serves no board WRITE — the lock is what is under test")
                | _ -> Error(Errors.NotFound "a graphql call with no document")
            | "GET", "rate_limit" -> ok """{"resources":{"graphql":{"remaining":4980,"limit":5000}}}"""
            // `Reads.openIssues` — the #353 token shortlist's candidate universe.
            | "GET", "repos/FS-GG/FS.GG.SDD/issues" ->
                match other with
                | Some(number, _, otherPaths) ->
                    [ {| number = number
                         state = "open"
                         body = otherPaths |} ]
                    |> JsonSerializer.Serialize
                    |> ok
                | None -> ok "[]"
            | "GET", "repos/FS-GG/FS.GG.SDD/issues/42/comments" -> ok (thread.Json paths)
            | "POST", "repos/FS-GG/FS.GG.SDD/issues/42/comments" ->
                let body =
                    match req.Body with
                    | Json payload -> JsonDocument.Parse(payload).RootElement.GetProperty("body").GetString()
                    | _ -> ""

                ok (sprintf """{"id":%d}""" (thread.Add body))
            | "GET", "repos/FS-GG/FS.GG.SDD/issues/42" -> ok (JsonSerializer.Serialize {| number = 42; body = paths |})
            | "GET", p when other |> Option.exists (fun (n, _, _) -> p = $"repos/FS-GG/FS.GG.SDD/issues/%d{n}/comments") ->
                let _, holder, _ = other.Value
                ok (otherComments holder)
            // The courtesy notice `notifyOverlap` posts on the OTHER holder's item, default (warn) path only.
            | "POST", p when other |> Option.exists (fun (n, _, _) -> p = $"repos/FS-GG/FS.GG.SDD/issues/%d{n}/comments") ->
                ok """{"id":8099}"""
            | m, p -> Error(Errors.NotFound $"the fixture serves no %s{m} %s{p}"))

    let private context (transport: Fake.Recorder) : Client.Context =
        { Transport = transport
          Owner = "FS-GG"
          Title = "Coordination"
          DefaultRepo = Some "FS.GG.SDD"
          ChoreLocks = [] }

    /// Same identity ladder `ForceStealTests` pins: BOTH halves (`FSGG_AGENT_SESSION_ID` and `FSGG_WORKER`)
    /// must agree with argv's `--worker`, or `claim` refuses before it reads anything (#1646).
    let private sessionVars =
        [ "CLAUDE_CODE_SESSION_ID"; "OPENCODE_SESSION_ID"; "FSGG_AGENT_SESSION_ID"; "FSGG_WORKER" ]

    let private runClaim (transport: Fake.Recorder) (args: string list) : int * string * string =
        let dir = Path.Combine(Path.GetTempPath(), "fsgg-2459-" + Guid.NewGuid().ToString "n")
        let previousCache = Environment.GetEnvironmentVariable "FSGG_COORD_CACHE"
        let previousKitRoot = Environment.GetEnvironmentVariable "FSGG_KIT_ROOT"
        let previousSessions = sessionVars |> List.map (fun v -> v, Environment.GetEnvironmentVariable v)
        let stdout = Console.Out
        let stderr = Console.Error
        use capturedOut = new StringWriter()
        use capturedErr = new StringWriter()

        try
            Directory.CreateDirectory dir |> ignore
            Environment.SetEnvironmentVariable("FSGG_COORD_CACHE", dir)
            Environment.SetEnvironmentVariable("FSGG_KIT_ROOT", dir)
            Environment.SetEnvironmentVariable("CLAUDE_CODE_SESSION_ID", null)
            Environment.SetEnvironmentVariable("OPENCODE_SESSION_ID", null)
            Environment.SetEnvironmentVariable("FSGG_AGENT_SESSION_ID", "ed60050b")
            Environment.SetEnvironmentVariable("FSGG_WORKER", "vole-418")
            Console.SetOut capturedOut
            Console.SetError capturedErr

            let opts =
                match Options.parse args with
                | Ok o -> o
                | Error e -> failwithf "the fixture's own argv did not parse: %s" e

            let code = Client.claim (context transport) opts
            Console.Out.Flush()
            Console.Error.Flush()
            code, capturedOut.ToString(), capturedErr.ToString()
        finally
            Console.SetOut stdout
            Console.SetError stderr
            Environment.SetEnvironmentVariable("FSGG_COORD_CACHE", previousCache)
            Environment.SetEnvironmentVariable("FSGG_KIT_ROOT", previousKitRoot)

            for name, value in previousSessions do
                Environment.SetEnvironmentVariable(name, value)

            try
                Directory.Delete(dir, true)
            with _ ->
                ()

    let private claimArgs (extra: string list) =
        [ "claim"; "FS.GG.SDD#42"; "--worker"; "vole-418"; "--json" ] @ extra

    // ---- AC1/AC2/AC4: default is a WARNING that still claims -------------------------------------------

    [<Fact>]
    let ``#2459 claim warns on an OVERLAP by default and STILL claims (exit green)`` () =
        let thread = Thread()
        let paths = "Paths: src/Thing.fs"
        let transport = world paths (Some(43, "smew-e1d9", paths)) thread

        let code, out, err = runClaim transport (claimArgs [])

        Assert.Equal(0, code)

        // AC4: the report names the OTHER holder and the shared tokens.
        Assert.Contains("OVERLAP", err)
        Assert.Contains("FS.GG.SDD#43", err)
        Assert.Contains("smew-e1d9", err)
        Assert.Contains("src/Thing.fs", err)
        Assert.Contains("MERGE-SEQUENCE", err)

        // The receipt still reports a WON claim, and now carries the collision machine-readably.
        let receipt = JsonDocument.Parse(out.Trim()).RootElement
        Assert.Equal("claimed", receipt.GetProperty("kind").GetString())
        Assert.True(receipt.GetProperty("converged").GetBoolean())

        let collisions = receipt.GetProperty("collisions").EnumerateArray() |> List.ofSeq
        Assert.Single(collisions) |> ignore
        Assert.Equal("smew-e1d9", collisions.[0].GetProperty("worker").GetString())
        Assert.True(collisions.[0].GetProperty("notified").GetBoolean())

        // The courtesy notice actually landed on the OTHER holder's item, not just this one's.
        Assert.True(transport.Logged "comment-post FS-GG/FS.GG.SDD 43")

    // ---- AC3: `--refuse-overlap` refuses instead --------------------------------------------------------

    [<Fact>]
    let ``#2459 claim --refuse-overlap REFUSES an overlapping claim instead of warning`` () =
        let thread = Thread()
        let paths = "Paths: src/Thing.fs"
        let transport = world paths (Some(43, "smew-e1d9", paths)) thread

        let code, _, err = runClaim transport (claimArgs [ "--refuse-overlap" ])

        Assert.Equal(Client.ExitContended, code)
        Assert.Contains("OVERLAP", err)
        Assert.Contains("refusing to claim", err)

        // Nothing was posted or claimed: no marker comment on #42, no notice on #43.
        Assert.Empty(thread.Posted)
        Assert.False(transport.Logged "comment-post FS-GG/FS.GG.SDD 43")

    // ---- AC5 (this leg): the control — disjoint touch-sets never warn, never refuse --------------------

    [<Fact>]
    let ``#2459 claim on a DISJOINT touch-set never warns, with or without --refuse-overlap`` () =
        let thread1 = Thread()

        let transport1 =
            world "Paths: src/Thing.fs" (Some(43, "smew-e1d9", "Paths: src/Other.fs")) thread1

        let code1, out1, err1 = runClaim transport1 (claimArgs [])

        Assert.Equal(0, code1)
        Assert.DoesNotContain("OVERLAP", err1)

        let receipt1 = JsonDocument.Parse(out1.Trim()).RootElement
        Assert.Empty(receipt1.GetProperty("collisions").EnumerateArray() |> List.ofSeq)

        let thread2 = Thread()

        let transport2 =
            world "Paths: src/Thing.fs" (Some(43, "smew-e1d9", "Paths: src/Other.fs")) thread2

        let code2, _, err2 = runClaim transport2 (claimArgs [ "--refuse-overlap" ])

        Assert.Equal(0, code2)
        Assert.DoesNotContain("OVERLAP", err2)

    [<Fact>]
    let ``#2459 claim with no other live claim at all never warns`` () =
        let thread = Thread()
        let transport = world "Paths: src/Thing.fs" None thread

        let code, out, err = runClaim transport (claimArgs [])

        Assert.Equal(0, code)
        Assert.DoesNotContain("OVERLAP", err)

        let receipt = JsonDocument.Parse(out.Trim()).RootElement
        Assert.Empty(receipt.GetProperty("collisions").EnumerateArray() |> List.ofSeq)
