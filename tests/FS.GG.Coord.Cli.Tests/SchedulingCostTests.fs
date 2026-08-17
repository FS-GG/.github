namespace FS.GG.Coord.Cli.Tests

open System
open System.IO
open System.Security.Cryptography
open System.Text
open System.Text.Json
open Xunit
open FS.GG.Coord.GitHub
open FS.GG.Coord.GitHub.Transport
open FS.GG.Coord.Cli

/// .github#2300: `enrichDeliveryRoutes` (`Client.fs`) used to map `readDeliveryRouteVerdict` — an
/// `issueBody` read plus a `commentBodies` read that PAGINATES with the issue's own comment count — over
/// EVERY scheduling candidate, including the closed, wrong-column, and blocked rows a scan can already
/// refuse for free off facts it already holds. On an 887-candidate board that was the whole of a measured
/// ~4,300-request `take`.
///
/// These pin the fix AT THE BOUNDARY THE ISSUE MEASURED: `Client.take`/`Client.batch` driven end-to-end
/// against a scripted `Fake.Recorder`, counting `RestCalls` (.github#2300's own unit) — not
/// `enrichDeliveryRoutes` in isolation, which could pass while never being wired into the live scan.
///
/// WHAT THIS FIX DOES NOT REACH, NAMED HONESTLY: `Scan.snapshot` (`FS.GG.Coord.GitHub/Scan.fs`, outside
/// this item's `Paths:`) already pays one `issueBody` + one `markerScan` read for every OPEN (or
/// closed-but-not-`Done`) candidate, UNCONDITIONALLY — needed to build the whole board's `inFlight`
/// reservation set for the collision check, not to decide any one candidate's own verdict, so it cannot
/// be skipped by asking "would THIS candidate be rejected anyway" the way the route read can. That is a
/// real, separate, comment-count-sensitive cost (`markerScan` also sends `per_page=100` and rides the
/// transport's own pagination) filed apart as .github#2306's sibling finding rather than folded in here
/// or silently left unproven. The measurements below are honest about which population each claim
/// covers: FULL elimination for closed-and-`Done` candidates (the issue's own "mostly closed" majority),
/// HALVING (not elimination) for open candidates rejected on column/blocker/human grounds.
module SchedulingCostTests =

    let private currentRouteComment (subject: string) (body: string) =
        StructuredFixtures.routeComment subject (Some FS.GG.Coord.DeliveryRoute.Lightweight) "fixture-2300" None

    let private ok (body: string) : Errors.IoResult<Response> =
        Ok
            { Status = 200
              Body = body
              ETag = None
              NextLink = None
              Headers = Map.empty }

    /// One board candidate. `ForbidComments`, when set, makes the fixture ERROR OUT if this issue's
    /// `/comments` endpoint (REST) is EVER requested — the sharp instrument for "prove growth is gone": a
    /// regression that starts paying for this candidate's (hypothetically huge) comment thread fails the
    /// test immediately and explicitly, rather than merely running slower.
    ///
    /// `Thread`, when `Some`, is the candidate's FULL comment history, chronological oldest-first — the
    /// single source both the REST `/comments` endpoint (still used by `Scan.snapshot`'s `markerScan`,
    /// out of this item's `Paths:`) and the new bounded GraphQL `comments(last: N)` query serve from,
    /// exactly as production reads both off the same underlying issue. `None` falls back to the ordinary
    /// `WithRoute` single-marker-or-nothing shape every earlier test in this file already uses.
    type private Row =
        { Number: int
          Status: string
          State: string
          Body: string
          IsPullRequest: bool
          BlockedBy: string option
          WithRoute: bool
          ForbidComments: bool
          Thread: string list option }

    /// Each candidate declares a UNIQUE `Paths:` token (`src/item-<n>.fs`) — a shared literal across every
    /// row would make every candidate collide with every other under the OVERLAP check (step 6), which
    /// would refuse the whole batch for a reason that has nothing to do with what this file measures.
    let private candidate number status state =
        { Number = number
          Status = status
          State = state
          Body = $"Paths: src/item-%d{number}.fs"
          IsPullRequest = false
          BlockedBy = None
          WithRoute = false
          ForbidComments = false
          Thread = None }

    /// The candidate's full comment history, oldest first — `Thread` if the test set one explicitly,
    /// else the legacy single-marker-or-nothing shape.
    let private effectiveThread (r: Row) =
        match r.Thread with
        | Some t -> t
        | None -> if r.WithRoute then [ currentRouteComment $"FS-GG/FS.GG.SDD#%d{r.Number}" r.Body ] else []

    let private graphqlAnswer (items: string) (query: string) : string option =
        if query.Contains "projectsV2" then
            Some
                """{"data":{"organization":{"projectsV2":{"nodes":[{"number":12,"title":"Coordination","id":"PVT_coord"}]}}},"rateLimit":{"cost":1,"remaining":4977}}"""
        elif query.Contains "fields(first" then
            Some
                """{"data":{"organization":{"projectV2":{"fields":{"nodes":[{"id":"PVTSSF_status","name":"Status","dataType":"SINGLE_SELECT","options":[{"id":"opt_ready","name":"Ready"},{"id":"opt_wip","name":"In progress"},{"id":"opt_blocked","name":"Blocked"},{"id":"opt_backlog","name":"Backlog"},{"id":"opt_done","name":"Done"}]},{"id":"PVTF_blocked","name":"Blocked by","dataType":"TEXT"}]}}}},"rateLimit":{"cost":1,"remaining":4977}}"""
        elif query.Contains "items(first" then
            Some
                $"""{{"data":{{"organization":{{"projectV2":{{"items":{{"pageInfo":{{"hasNextPage":false,"endCursor":null}},"nodes":[%s{items}]}}}}}}}},"rateLimit":{{"cost":1,"remaining":4977}}}}"""
        else
            None

    let private boardItemIn (status: string) (number: int) (blockedBy: string option) (state: string) (body: string) (isPullRequest: bool) =
        let blocked =
            blockedBy |> Option.map (fun v -> $"{{\"text\":\"%s{v}\"}}") |> Option.defaultValue "null"

        let encodedBody = JsonSerializer.Serialize body

        let typename = if isPullRequest then "PullRequest" else "Issue"

        $"""{{"status":{{"name":"%s{status}"}},"blockedBy":%s{blocked},"content":{{"__typename":"%s{typename}","number":%d{number},"title":"item %d{number}","state":"%s{state}","body":%s{encodedBody},"repository":{{"nameWithOwner":"FS-GG/FS.GG.SDD"}}}}}}"""

    /// A transport serving one repo and one board built from `rows`. Deliberately close to
    /// `ApplicationServiceTests`' own board fixture (same endpoint surface, same `describe` log
    /// vocabulary from `Fake.fs`) so the counting below reads off the SAME request classification the
    /// rest of the corpus already relies on: `issue-get`, `comment-list`, `issue-list`, `pulls-list`.
    let private worldWithQueries (rows: Row list) (queries: ResizeArray<string> option) =
        let byNumber = rows |> List.map (fun r -> r.Number, r) |> Map.ofList

        let itemsDoc =
            rows
            |> List.map (fun r -> boardItemIn r.Status r.Number r.BlockedBy r.State r.Body r.IsPullRequest)
            |> String.concat ","

        Fake.Recorder(fun (req: Request) ->
            let path = req.Path.Trim '/'

            let issueNumber (suffix: string) =
                let prefix = "repos/FS-GG/FS.GG.SDD/issues/"

                if path.StartsWith prefix && path.EndsWith suffix then
                    let middle = path.Substring(prefix.Length, path.Length - prefix.Length - suffix.Length)

                    match Int32.TryParse middle with
                    | true, n -> Some n
                    | _ -> None
                else
                    None

            /// The GraphQL body variable helper — `req.Body`'s `Query(document, variables)` carries
            /// `variables` as a typed `(string * Var) list`, not serialized JSON, so this reads them
            /// directly rather than re-parsing a payload.
            let numberVar (variables: (string * Var) list) =
                variables
                |> List.tryFind (fun (k, _) -> k = "number")
                |> Option.bind (fun (_, v) -> match v with VNumber n -> Some(int n) | _ -> None)

            let lastVar (variables: (string * Var) list) =
                variables
                |> List.tryFind (fun (k, _) -> k = "last")
                |> Option.bind (fun (_, v) -> match v with VNumber n -> Some(int n) | _ -> None)

            match req.Method, path with
            | "POST", "graphql" ->
                match req.Body with
                | Query(document, variables) when document.Contains "comments(last:" ->
                    queries |> Option.iter (fun captured -> captured.Add document)
                    // `Reads.recentCommentBodies` — .github#2300 repair 2. Honour `last` for real (a
                    // fixture that ignored it and always returned everything would validate nothing about
                    // the fail-closed-beyond-the-bound case), and match the exact "tail of the list"
                    // semantics the Relay `last:` connection argument has: the most recent `last` items,
                    // still oldest-of-that-window first.
                    match numberVar variables, lastVar variables with
                    | Some n, Some last ->
                        match Map.tryFind n byNumber with
                        | Some r when r.ForbidComments ->
                            Error(Errors.NotFound $"#2300 AC4: the route GraphQL read for #%d{n} must NEVER be requested — this candidate is rejected on locally-known grounds")
                        | Some r ->
                            let recent =
                                effectiveThread r
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
                        | None -> Error(Errors.NotFound $"no thread fixture for #%d{n}")
                    | _ -> Error(Errors.NotFound $"the recent-comments query is missing owner/repo/number/last variables: %A{variables}")
                | Query(document, _) ->
                    queries |> Option.iter (fun captured -> captured.Add document)
                    match graphqlAnswer itemsDoc document with
                    | Some answer -> ok answer
                    | None -> Error(Errors.NotFound $"the fixture serves no answer for: %s{document}")
                | _ -> Error(Errors.NotFound "a graphql call with no document")
            | "GET", "rate_limit" -> ok """{"resources":{"graphql":{"remaining":4980,"limit":5000}}}"""
            | "GET", "repos/FS-GG/FS.GG.SDD/issues" ->
                rows
                |> List.filter (fun r -> r.State = "OPEN")
                |> List.map (fun r -> {| number = r.Number; state = "open"; body = r.Body |})
                |> JsonSerializer.Serialize
                |> ok
            | "GET", _ when (issueNumber "/comments").IsSome ->
                let n = (issueNumber "/comments").Value

                match Map.tryFind n byNumber with
                | Some r when r.ForbidComments ->
                    Error(Errors.NotFound $"#2300 AC4: /comments for #%d{n} must NEVER be requested — this candidate is rejected on locally-known grounds")
                | Some r ->
                    let comments =
                        effectiveThread r
                        |> List.mapi (fun i body ->
                            JsonSerializer.Serialize
                                {| id = 7000 + n * 1000 + i
                                   body = body
                                   user = {| login = "EHotwagner" |}
                                   created_at = "2026-01-01T00:00:00Z"
                                   updated_at = "2026-01-01T00:00:00Z" |})

                    ok ("[" + String.concat "," comments + "]")
                | None -> Error(Errors.NotFound $"no comments fixture for #%d{n}")
            | ("GET" | "PATCH"), _ when (issueNumber "").IsSome ->
                let n = (issueNumber "").Value

                match Map.tryFind n byNumber with
                | Some r when r.ForbidComments ->
                    Error(Errors.NotFound $"#2300 AC4: the body of #%d{n} must NEVER be requested — this candidate is rejected on locally-known grounds")
                | Some r ->
                    ok (JsonSerializer.Serialize {| number = n; state = (if r.State = "OPEN" then "open" else "closed"); body = r.Body |})
                | None -> Error(Errors.NotFound $"no issue #%d{n}")
            | m, p -> Error(Errors.NotFound $"the fixture serves no %s{m} %s{p}"))

    let private world (rows: Row list) = worldWithQueries rows None

    let private context (transport: Fake.Recorder) : Kernel.Context =
        { Transport = transport
          Owner = "FS-GG"
          Title = "Coordination"
          DefaultRepo = Some "FS.GG.SDD"
          ChoreLocks = [] }

    let private options (args: string list) : Options.Options =
        match Options.parse args with
        | Ok o -> o
        | Error e -> failwithf "the fixture's own argv did not parse: %s" e

    /// Run a queue verb against a THROWAWAY cache root, so every measurement here is COLD by
    /// construction. The correction posted to .github#2300 established that the headline cost is a
    /// COLD-CACHE cost — a warm run measured ~4 REST regardless of board size — so a test that let two
    /// runs share a cache directory would measure the warm path and prove nothing about the number this
    /// issue is about. A fresh `Guid`-named directory per call is what makes that true by construction
    /// rather than by discipline.
    let private runQueue (transport: Fake.Recorder) (args: string list) : int * string * string =
        let dir = Path.Combine(Path.GetTempPath(), "fsgg-2300-" + Guid.NewGuid().ToString "n")
        let previousCache = Environment.GetEnvironmentVariable "FSGG_COORD_CACHE"
        let previousKitRoot = Environment.GetEnvironmentVariable "FSGG_KIT_ROOT"

        let identityVars =
            [ "FSGG_WORKER"; "CLAUDE_CODE_SESSION_ID"; "OPENCODE_SESSION_ID"; "FSGG_AGENT_SESSION_ID" ]

        let previousIdentity = identityVars |> List.map (fun v -> v, Environment.GetEnvironmentVariable v)

        let stdout = Console.Out
        let stderr = Console.Error
        use capturedOut = new StringWriter()
        use capturedErr = new StringWriter()

        try
            Directory.CreateDirectory dir |> ignore

            for v in identityVars do
                Environment.SetEnvironmentVariable(v, null)

            Environment.SetEnvironmentVariable("FSGG_COORD_CACHE", dir)
            Environment.SetEnvironmentVariable("FSGG_KIT_ROOT", dir)
            Console.SetOut capturedOut
            Console.SetError capturedErr

            let opts = options args

            let code =
                match opts.Command with
                | Options.Take -> Client.take (context transport) opts
                | Options.Next -> Client.next (context transport) opts
                | Options.BatchCmd -> Client.batch (context transport) opts
                | other -> failwithf "this fixture drives take/next/batch only, got %A" other

            Console.Out.Flush()
            Console.Error.Flush()
            code, capturedOut.ToString(), capturedErr.ToString()
        finally
            Console.SetOut stdout
            Console.SetError stderr
            Environment.SetEnvironmentVariable("FSGG_COORD_CACHE", previousCache)
            Environment.SetEnvironmentVariable("FSGG_KIT_ROOT", previousKitRoot)

            for v, previous in previousIdentity do
                Environment.SetEnvironmentVariable(v, previous)

            try
                Directory.Delete(dir, true)
            with _ ->
                ()

    let private schedulableRow number =
        { candidate number "Ready" "OPEN" with WithRoute = true }

    /// EXACT log-line matching, deliberately not `transport.Count`'s substring match: `Fake.fs`'s
    /// `describe` renders a bare trailing number with no delimiter after it
    /// (`$"issue-get %s{nwo} %s{n}"`), so `Count "FS-GG/FS.GG.SDD 1"` would also match `...SDD 10`,
    /// `...SDD 19`, and `...SDD 199` — exactly the kind of false pass a cost-bound test cannot afford.
    let private countExact (transport: Fake.Recorder) (line: string) =
        transport.Log |> List.filter (fun l -> l = line) |> List.length

    let private readsFor (transport: Fake.Recorder) (n: int) =
        countExact transport $"issue-get FS-GG/FS.GG.SDD %d{n}", countExact transport $"comment-list FS-GG/FS.GG.SDD %d{n}"

    /// How many times the BOUNDED GraphQL route read (`Reads.recentCommentBodies`, repair 2) fired for
    /// this candidate — the log line `Fake.fs`'s `describe` renders for any GraphQL call it does not
    /// otherwise classify: `"graphql " + request.Subject`, and `Reads.recentCommentBodies`'s own subject
    /// names the issue and the window.
    let private routeGraphQlCalls (transport: Fake.Recorder) (n: int) =
        countExact transport $"graphql FS-GG/FS.GG.SDD#%d{n} recent comments (last 100)"

    [<Fact>]
    let ``#2313 reconciling carries swept bodies in its board pages, without per-row REST reads or scheduling leakage`` () =
        let closed =
            [ 1..3 ]
            |> List.map (fun n ->
                { candidate n "Done" "CLOSED" with
                    Body = $"Class: maintenance\nPaths: src/closed-%d{n}.fs" })

        let closed =
            { candidate 4 "Done" "CLOSED" with
                IsPullRequest = true
                Body = "Class: maintenance\nPaths: src/closed-pr.fs" }
            :: closed

        let fixtureTitle = "Coordination-2313-" + Guid.NewGuid().ToString "n"

        // .github#2525 — ISOLATE THE CACHE **ROOT**, NOT ONLY THE KEY. The unique `fixtureTitle` above
        // already gives this test its own cache FILENAME, which is what stops it colliding with another
        // test. It does nothing about WHERE that file is written: `Cache.root()` falls back to the
        // developer's `~/.cache/fsgg-coord`, so every run of this suite deposited one more never-read
        // `scan-fs-gg-coordination-2313-<guid>.json` there. Unlike the `ChoresTests` legs this cannot
        // poison a live read — the key differs from the real board's — but litter that accumulates once
        // per run is still this suite writing outside itself, and a unique key is a coincidence away from
        // not being unique.
        let previousCacheRoot = Environment.GetEnvironmentVariable "FSGG_COORD_CACHE"

        let cacheDir =
            IO.Path.Combine(IO.Path.GetTempPath(), "fsgg-2313-" + Guid.NewGuid().ToString "n")

        IO.Directory.CreateDirectory cacheDir |> ignore
        Environment.SetEnvironmentVariable("FSGG_COORD_CACHE", cacheDir)

        try

        let schedulingQueries = ResizeArray<string>()
        let scheduling = worldWithQueries closed (Some schedulingQueries)

        match Scan.board scheduling Cache.Scheduling "FS-GG" fixtureTitle 12 with
        | Error e -> failwithf "scheduling board scan failed: %A" e
        | Ok rows ->
            Assert.All(rows, fun row -> Assert.Equal(None, row.SweptBody))

        Assert.Single(schedulingQueries) |> ignore
        Assert.DoesNotContain("... on Issue { number title state createdAt body", schedulingQueries.[0])

        for row in closed do
            Assert.Equal(0, countExact scheduling $"issue-get FS-GG/FS.GG.SDD %d{row.Number}")

        let reconcilingQueries = ResizeArray<string>()
        let reconciling = worldWithQueries closed (Some reconcilingQueries)

        match Scan.board reconciling Cache.Reconciling "FS-GG" fixtureTitle 12 with
        | Error e -> failwithf "reconciling board scan failed: %A" e
        | Ok rows ->
            Assert.Equal(4, rows.Length)

            for row in rows do
                let expected = closed |> List.find (fun candidate -> candidate.Number = row.Ref.Number)
                Assert.Equal(Some(Ok expected.Body), row.SweptBody)
                Assert.Equal(0, countExact reconciling $"issue-get FS-GG/FS.GG.SDD %d{row.Ref.Number}")

        Assert.Single(reconcilingQueries) |> ignore
        Assert.Contains("body", reconcilingQueries.[0])
        Assert.Contains("... on PullRequest { id number title state createdAt body", reconcilingQueries.[0])

        finally
            Environment.SetEnvironmentVariable("FSGG_COORD_CACHE", previousCacheRoot)

            try
                IO.Directory.Delete(cacheDir, true)
            with _ ->
                ()

    // ---- AC1/AC2 — bounded by the SCHEDULABLE set, not the candidate set --------------------------------

    [<Fact>]
    let ``#2300 AC1/AC2: closed-and-Done candidates never pay a delivery-route read, however many there are`` () =
        // 40 closed-and-Done rows, ALL marked `ForbidComments` — under the pre-fix code every one of them
        // was still mapped through `readDeliveryRouteVerdict` (.github#2300's root cause), so this would
        // have failed with the fixture's explicit "#2300 AC4" error the instant the old code ran. Under
        // the fix, `Schedulability.IssueClosed` fires at step 1 and the candidate is never enriched.
        let closedDone =
            [ 1..40 ] |> List.map (fun n -> { candidate n "Done" "CLOSED" with ForbidComments = true })

        let transport = world (closedDone @ [ schedulableRow 999 ])

        let code, out, _ = runQueue transport [ "batch"; "--repo"; "FS.GG.SDD"; "-n"; "1"; "--json" ]

        Assert.Equal(0, code)
        Assert.Contains("FS-GG/FS.GG.SDD#999", out)

        // Zero REST AND zero GraphQL for every one of the 40 closed rows: neither `issue-get`/`comment-list`
        // (Scan.snapshot's kind of read) nor the bounded route GraphQL call ever appear for them.
        for n in 1..40 do
            let gets, comments = readsFor transport n
            Assert.Equal(0, gets)
            Assert.Equal(0, comments)
            Assert.Equal(0, routeGraphQlCalls transport n)

        // The schedulable candidate's route is read for real: one marker scan and one complete decision
        // ledger read. M4 deliberately pays the second REST read so no buried v2 predecessor disappears.
        Assert.Equal(2, countExact transport "comment-list FS-GG/FS.GG.SDD 999")
        Assert.Equal(0, routeGraphQlCalls transport 999)

    [<Fact>]
    let ``#2300 AC1/AC2: candidates rejected on column, blocker, or human grounds pay at most ONE issue read each, not two`` () =
        // Before the fix, EVERY one of these paid a SECOND `issue-get` + `comment-list` pair from
        // `enrichDeliveryRoutes`, on top of whatever `Scan.snapshot` already reads to build the board's
        // `inFlight` set (.github#2300's own root-cause section: two REST reads per candidate). The fix
        // removes exactly the second pair — the one this item's `Paths:` can reach — leaving at most the
        // first, which `Scan.snapshot` (outside this item's `Paths:`) still pays unconditionally for any
        // OPEN row. That residual is real and is not what this assertion claims to remove; see the
        // module doc comment above.
        let wrongStatus = [ 1..10 ] |> List.map (fun n -> candidate n "In progress" "OPEN")

        let blocked =
            [ 11..15 ]
            |> List.map (fun n -> { candidate n "Ready" "OPEN" with BlockedBy = Some "FS-GG/FS.GG.SDD#1" })

        let transport = world (wrongStatus @ blocked @ [ schedulableRow 999 ])

        let code, out, _ = runQueue transport [ "batch"; "--repo"; "FS.GG.SDD"; "-n"; "1"; "--json" ]

        Assert.Equal(0, code)
        Assert.Contains("FS-GG/FS.GG.SDD#999", out)

        for n in (wrongStatus @ blocked) |> List.map (fun r -> r.Number) do
            // AT MOST ONE REST comment read, not zero: `Scan.snapshot`'s own unconditional read is out of
            // this item's reach. The property under test is that it is not TWO — and the bounded GraphQL
            // route call must be zero, since these candidates never reach `enrichDeliveryRoutes` at all.
            let gets, comments = readsFor transport n
            Assert.Equal(1, gets)
            Assert.Equal(1, comments)
            Assert.Equal(0, routeGraphQlCalls transport n)

        // The schedulable candidate still gets its real route read in addition to the marker scan.
        Assert.Equal(2, countExact transport "comment-list FS-GG/FS.GG.SDD 999")
        Assert.Equal(0, routeGraphQlCalls transport 999)

    // ---- AC4 (sharpened): growth in comment-thread size does not grow scan cost for a rejected row -----

    [<Fact>]
    let ``#2300 AC4: a rejected candidate's own comment-thread size cannot affect scan cost`` () =
        // The sharpest form of "prove the growth is gone": a candidate marked `ForbidComments` makes the
        // fixture ERROR if its `/comments` endpoint OR its bounded route GraphQL call is ever hit, however
        // large that thread would have been. If the fix regresses to reading it, this fails LOUDLY and
        // immediately rather than merely running slower on a bigger fixture.
        let hugeThreadClosed = { candidate 500 "Done" "CLOSED" with ForbidComments = true }
        let ordinary = [ 1..5 ] |> List.map (fun n -> candidate n "In progress" "OPEN")

        let transport = world (ordinary @ [ hugeThreadClosed; schedulableRow 999 ])

        let code, out, _ = runQueue transport [ "batch"; "--repo"; "FS.GG.SDD"; "-n"; "1"; "--json" ]

        Assert.Equal(0, code)
        Assert.Contains("FS-GG/FS.GG.SDD#999", out)
        let gets, comments = readsFor transport 500
        Assert.Equal(0, gets)
        Assert.Equal(0, comments)
        Assert.Equal(0, routeGraphQlCalls transport 500)

    [<Fact>]
    let ``#2300 repair 1: a human-held candidate never pays a delivery-route read, however stale its receipt would be`` () =
        // Independent review, round 1: `AwaitingHuman` is one of the four verdicts `routeCannotChangeVerdict`
        // matches to skip enrichment, and IS route-independent by `Schedulability.schedulable`'s own order
        // (step 3b, `Blocked on: human/...`, strictly BEFORE step 3c's route check) — the critic confirmed
        // this by reading the source. But nothing exercised it: removing the `AwaitingHuman` arm in a local
        // mutation left the full 650-test Cli corpus green, because no fixture combined a human hold with a
        // NON-`Current` route.
        //
        // NOT `ForbidComments` here (unlike the AC1/AC2/AC4 tests above): `Scan.snapshot`'s own
        // unconditional per-open-row read (out of this item's `Paths:`, .github#2308) ALSO reads this
        // candidate's body and markers regardless of the `AwaitingHuman` arm, so `ForbidComments` would
        // fail the fixture for a reason unrelated to what this test pins. The precise, arm-specific
        // signal is the EXACT COUNT: `Scan.snapshot` alone pays ONE `issue-get` + ONE `comment-list`; a
        // SECOND pair means `enrichDeliveryRoutes` ran too, which only happens if the arm is gone.
        let humanHeld =
            { candidate 77 "Ready" "OPEN" with
                Body = "Paths: src/item-77.fs\nBlocked on: human/action"
                WithRoute = false }

        let transport = world (humanHeld :: [ schedulableRow 999 ])

        let code, out, _ = runQueue transport [ "batch"; "--repo"; "FS.GG.SDD"; "-n"; "1"; "--json" ]

        Assert.Equal(0, code)
        Assert.Contains("FS-GG/FS.GG.SDD#999", out)

        let gets, comments = readsFor transport 77
        Assert.Equal(1, gets)
        Assert.Equal(1, comments)
        Assert.Equal(0, routeGraphQlCalls transport 77)

    // ---- AC3 — the gate still fails closed ---------------------------------------------------------------

    [<Fact>]
    let ``#2300 AC3: a genuinely schedulable row with NO delivery-route receipt is still refused`` () =
        // `routeCannotChangeVerdict` previews `schedulable` with a NEUTRAL placeholder route so it can
        // never itself decide the outcome. A row that clears every LOCAL check (open, Ready, unblocked,
        // no human hold) must still have its REAL receipt read — and here there is none, so the real
        // decision must refuse it exactly as it did before this fix. This is the negative case the
        // sharpened AC5 names: no path added by this item may be satisfied by skipping the check for an
        // otherwise-schedulable row.
        let noReceipt = candidate 42 "Ready" "OPEN"
        let transport = world [ noReceipt ]

        let code, out, err = runQueue transport [ "batch"; "--repo"; "FS.GG.SDD"; "-n"; "1"; "--json" ]

        // The real route WAS consulted (one REST marker scan and one complete REST decision-ledger read)
        // though it decided nothing: this is the read AC3 requires, distinct from the reads AC1/AC2
        // remove.
        Assert.Equal(2, countExact transport "comment-list FS-GG/FS.GG.SDD 42")
        Assert.Equal(0, routeGraphQlCalls transport 42)

        // NEVER CHOSEN, and the refusal names the delivery route as the reason — the fail-closed behaviour
        // is unchanged by this fix. `batch --json` exits 0 on an empty-but-valid result (the same
        // convention "a clean board still emits an empty array" pins), so the negative case here is the
        // EMPTY chosen set plus the named reason, not the exit code.
        Assert.Equal(0, code)
        Assert.Equal("[]", out.Trim())
        Assert.Contains("FS.GG.SDD#42", err)
        Assert.Contains("delivery-route", err)

    // ---- M4: route authorization reads the complete append-only ledger -----------------------------

    [<Fact>]
    let ``M4 a complete route ledger finds a recent marker in a 251-comment thread`` () =
        let deepButRecent =
            let marker = currentRouteComment "FS-GG/FS.GG.SDD#600" "Paths: src/item-600.fs"
            let noise = List.init 250 (fun i -> $"unrelated discussion comment %d{i}")
            { candidate 600 "Ready" "OPEN" with Thread = Some(noise @ [ marker ]) }

        let shallow = schedulableRow 999

        let transport = world [ deepButRecent; shallow ]

        let code, out, _ = runQueue transport [ "batch"; "--repo"; "FS.GG.SDD"; "-n"; "2"; "--json" ]

        Assert.Equal(0, code)
        // BOTH are found and scheduled — the 251-comment thread's marker was still recent enough.
        Assert.Contains("FS-GG/FS.GG.SDD#600", out)
        Assert.Contains("FS-GG/FS.GG.SDD#999", out)

        Assert.Equal(0, routeGraphQlCalls transport 600)
        Assert.Equal(0, routeGraphQlCalls transport 999)
        Assert.Equal(2, countExact transport "comment-list FS-GG/FS.GG.SDD 600")
        Assert.Equal(2, countExact transport "comment-list FS-GG/FS.GG.SDD 999")

    [<Fact>]
    let ``M4 a buried route receipt remains authoritative through the complete ledger read`` () =
        let buriedMarker =
            let marker = currentRouteComment "FS-GG/FS.GG.SDD#700" "Paths: src/item-700.fs"
            let noise = List.init 250 (fun i -> $"unrelated discussion comment %d{i}")
            { candidate 700 "Ready" "OPEN" with Thread = Some(marker :: noise) }

        let transport = world [ buriedMarker ]

        let code, out, err = runQueue transport [ "batch"; "--repo"; "FS.GG.SDD"; "-n"; "1"; "--json" ]

        Assert.Equal(0, code)
        Assert.Contains("FS-GG/FS.GG.SDD#700", out)
        Assert.DoesNotContain("delivery-route", err)

        // BOUNDED EVEN ON A MISS: exactly one GraphQL call — never a retry, and never a fallback to the
        // unbounded REST search this repair exists to remove. `gets` is 2, not 1: `readDeliveryRouteVerdict`
        // still reads the issue BODY over REST (unchanged by repair 2, only the comment search moved to
        // GraphQL), on top of `Scan.snapshot`'s own unavoidable `issue-get` (out of this item's `Paths:`)
        // — this candidate is NOT locally rejected, so both fire, same as every other schedulable-reaching
        // candidate in this file. `comments` stays 1: the only REST `/comments` call left is
        // `Scan.snapshot`'s `markerScan`.
        Assert.Equal(0, routeGraphQlCalls transport 700)
        let gets, comments = readsFor transport 700
        Assert.Equal(1, gets)
        Assert.Equal(2, comments)
