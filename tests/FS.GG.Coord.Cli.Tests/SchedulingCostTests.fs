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

    let private revision (body: string) =
        SHA256.HashData(Encoding.UTF8.GetBytes body) |> Convert.ToHexString |> _.ToLowerInvariant()

    [<Literal>]
    let private DeliveryRouteMarker = "<!-- fsgg:delivery-route/v1 -->"

    let private currentRouteComment (subject: string) (body: string) =
        let rev = revision body

        let receipt =
            $"""{{"schema":"fsgg.coord.delivery-route/v1","subject":"%s{subject}","subjectRevision":"%s{rev}","route":"lightweight","agent":"fixture-2300","timestamp":"2026-01-01T00:00:00Z","reasonCodes":["fixture"],"rationale":"fixture route receipt","declaredImpacts":["internal"],"observedFacts":["localized"],"sddWorkId":null,"specHome":null,"requiredGates":[]}}"""

        DeliveryRouteMarker + "\n" + receipt

    let private ok (body: string) : Errors.IoResult<Response> =
        Ok
            { Status = 200
              Body = body
              ETag = None
              NextLink = None
              Headers = Map.empty }

    /// One board candidate. `ForbidComments`, when set, makes the fixture ERROR OUT if this issue's
    /// `/comments` endpoint is EVER requested — the sharp instrument for "prove growth is gone": a
    /// regression that starts paying for this candidate's (hypothetically huge) comment thread fails the
    /// test immediately and explicitly, rather than merely running slower.
    type private Row =
        { Number: int
          Status: string
          State: string
          Body: string
          BlockedBy: string option
          WithRoute: bool
          ForbidComments: bool }

    /// Each candidate declares a UNIQUE `Paths:` token (`src/item-<n>.fs`) — a shared literal across every
    /// row would make every candidate collide with every other under the OVERLAP check (step 6), which
    /// would refuse the whole batch for a reason that has nothing to do with what this file measures.
    let private candidate number status state =
        { Number = number
          Status = status
          State = state
          Body = $"Paths: src/item-%d{number}.fs"
          BlockedBy = None
          WithRoute = false
          ForbidComments = false }

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

    let private boardItemIn (status: string) (number: int) (blockedBy: string option) (state: string) =
        let blocked =
            blockedBy |> Option.map (fun v -> $"{{\"text\":\"%s{v}\"}}") |> Option.defaultValue "null"

        $"""{{"status":{{"name":"%s{status}"}},"blockedBy":%s{blocked},"content":{{"__typename":"Issue","number":%d{number},"title":"item %d{number}","state":"%s{state}","repository":{{"nameWithOwner":"FS-GG/FS.GG.SDD"}}}}}}"""

    /// A transport serving one repo and one board built from `rows`. Deliberately close to
    /// `ApplicationServiceTests`' own board fixture (same endpoint surface, same `describe` log
    /// vocabulary from `Fake.fs`) so the counting below reads off the SAME request classification the
    /// rest of the corpus already relies on: `issue-get`, `comment-list`, `issue-list`, `pulls-list`.
    let private world (rows: Row list) =
        let byNumber = rows |> List.map (fun r -> r.Number, r) |> Map.ofList

        let itemsDoc =
            rows
            |> List.map (fun r -> boardItemIn r.Status r.Number r.BlockedBy r.State)
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

            match req.Method, path with
            | "POST", "graphql" ->
                match req.Body with
                | Query(document, _) ->
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
                    let route =
                        if r.WithRoute then
                            [ JsonSerializer.Serialize
                                  {| id = 7000 + n
                                     body = currentRouteComment $"FS-GG/FS.GG.SDD#%d{n}" r.Body
                                     user = {| login = "EHotwagner" |}
                                     created_at = "2026-01-01T00:00:00Z"
                                     updated_at = "2026-01-01T00:00:00Z" |} ]
                        else
                            []

                    ok ("[" + String.concat "," route + "]")
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

    let private context (transport: Fake.Recorder) : Client.Context =
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

        // Zero REST for every one of the 40 closed rows: `issue-get`/`comment-list` never appear for
        // them, and only the ONE schedulable candidate's route (plus the fixed board-bootstrap and
        // bulk-issue-list overhead) is paid.
        for n in 1..40 do
            let gets, comments = readsFor transport n
            Assert.Equal(0, gets)
            Assert.Equal(0, comments)

        // The schedulable candidate's route WAS read for real (AC3: the gate still consults a receipt
        // when one could matter) — TWICE, once from `Scan.snapshot`'s own unconditional per-open-row read
        // (needed to build the `inFlight` lock set; out of this item's `Paths:`) and once from
        // `enrichDeliveryRoutes`. Both calls are pre-existing/expected for a candidate whose real answer
        // matters; nothing here claims to remove either of them for #999.
        Assert.Equal(2, countExact transport "comment-list FS-GG/FS.GG.SDD 999")

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
            // AT MOST ONE, not zero: `Scan.snapshot`'s own unconditional read is out of this item's
            // reach. The property under test is that it is not TWO.
            let gets, comments = readsFor transport n
            Assert.Equal(1, gets)
            Assert.Equal(1, comments)

        // The schedulable candidate still gets its real route read (twice — once from each of the two
        // independent call sites, exactly as before this fix; this item narrows WHO pays the cost, not
        // whether the one candidate that needs an answer gets one).
        Assert.Equal(2, countExact transport "comment-list FS-GG/FS.GG.SDD 999")

    // ---- AC4 (sharpened): growth in comment-thread size does not grow scan cost for a rejected row -----

    [<Fact>]
    let ``#2300 AC4: a rejected candidate's own comment-thread size cannot affect scan cost`` () =
        // The sharpest form of "prove the growth is gone": a candidate marked `ForbidComments` makes the
        // fixture ERROR if its `/comments` endpoint is ever hit, however large that thread would have
        // been. If the fix regresses to reading it, this fails LOUDLY and immediately rather than merely
        // running slower on a bigger fixture.
        let hugeThreadClosed = { candidate 500 "Done" "CLOSED" with ForbidComments = true }
        let ordinary = [ 1..5 ] |> List.map (fun n -> candidate n "In progress" "OPEN")

        let transport = world (ordinary @ [ hugeThreadClosed; schedulableRow 999 ])

        let code, out, _ = runQueue transport [ "batch"; "--repo"; "FS.GG.SDD"; "-n"; "1"; "--json" ]

        Assert.Equal(0, code)
        Assert.Contains("FS-GG/FS.GG.SDD#999", out)
        let gets, comments = readsFor transport 500
        Assert.Equal(0, gets)
        Assert.Equal(0, comments)

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

        // The real route WAS consulted (once from `Scan.snapshot`, once from `enrichDeliveryRoutes` —
        // same accounting as the schedulable candidate above) even though it decided nothing: this is the
        // read AC3 requires, distinct from the reads AC1/AC2 remove.
        Assert.Equal(2, countExact transport "comment-list FS-GG/FS.GG.SDD 42")

        // NEVER CHOSEN, and the refusal names the delivery route as the reason — the fail-closed behaviour
        // is unchanged by this fix. `batch --json` exits 0 on an empty-but-valid result (the same
        // convention "a clean board still emits an empty array" pins), so the negative case here is the
        // EMPTY chosen set plus the named reason, not the exit code.
        Assert.Equal(0, code)
        Assert.Equal("[]", out.Trim())
        Assert.Contains("FS.GG.SDD#42", err)
        Assert.Contains("delivery-route", err)
