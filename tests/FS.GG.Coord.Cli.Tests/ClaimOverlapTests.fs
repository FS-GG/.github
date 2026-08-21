namespace FS.GG.Coord.Cli.Tests

open System
open System.Collections.Generic
open System.IO
open System.Security.Cryptography
open System.Text
open System.Text.Json
open Xunit
open FS.GG.Coord
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

    let private item number : Types.Ref =
        { Owner = "FS-GG"; Repo = ".github"; Number = number }

    let private wait waiter waiterGeneration predecessor predecessorGeneration tokens host =
        let draft: Client.OverlapWaitReceipt =
            { Waiter = waiter
              WaiterGeneration = waiterGeneration
              Predecessor = predecessor
              PredecessorGeneration = predecessorGeneration
              SharedTokens = tokens
              Host = host
              Digest = "" }

        { draft with Digest = Client.waitReceiptDigest draft }

    let private cycleFixture () =
        let a, b = item 2772, item 2797
        let aWait = wait a "5365000001" b "5365000002" [ "src/FS.GG.Coord.Cli/Client.fs" ] "host/root"
        let bWait = wait b "5365000002" a "5365000001" [ "src/FS.GG.Coord.Cli/Client.fs" ] "host/root"
        let snapshot: Client.MutualOverlapSnapshot =
            { Readable = true
              Claims =
                [ { Item = a; Generation = "5365000001"; Live = true }
                  { Item = b; Generation = "5365000002"; Live = true } ]
              Relations = [ { Left = a; Right = b; SharedTokens = [ "src/FS.GG.Coord.Cli/Client.fs" ] } ]
              Waits = [ aWait; bWait ]
              DurableDependencies = []
              RelatedRoomCycleDigests = [] }
        a, b, aWait, bWait, snapshot

    let private detected snapshot =
        match Client.detectMutualOverlap snapshot with
        | Client.MutualOverlapCycle cycle -> cycle
        | other -> failwithf "expected mutual overlap cycle, got %A" other

    [<Fact>]
    let ``#2801 detects the authoritative two-cycle independent of receipt and claim order`` () =
        let _, _, _, _, snapshot = cycleFixture ()
        let first = detected snapshot
        let reordered = detected { snapshot with Claims = List.rev snapshot.Claims; Waits = List.rev snapshot.Waits }
        Assert.Equal(first, reordered)
        Assert.Equal<string list>([ "src/FS.GG.Coord.Cli/Client.fs" ], first.SharedTokens)

    [<Fact>]
    let ``#2801 one-way wait and unrelated room are negative controls`` () =
        let _, _, aWait, _, snapshot = cycleFixture ()
        let actual =
            Client.detectMutualOverlap
                { snapshot with
                    Waits = [ aWait ]
                    RelatedRoomCycleDigests = [ "unrelated-room-cycle" ] }
        Assert.Equal(Client.NoMutualOverlapCycle, actual)

    [<Fact>]
    let ``#2801 detector refuses unreadable self stale missing changed nonoverlap dependency and conflict inversions`` () =
        let a, b, aWait, bWait, snapshot = cycleFixture ()
        let selfDraft = wait a "5365000001" a "5365000001" [ "src/FS.GG.Coord.Cli/Client.fs" ] "host/root"
        let conflicting = wait a "5365000001" (item 2801) "5365000003" [ "src/FS.GG.Coord.Cli/Client.fs" ] "host/root"
        let badDigest = { aWait with Digest = "tampered" }
        let cases =
            [ "unreadable", "unreadable", { snapshot with Readable = false }
              "self", "self wait", { snapshot with Waits = [ selfDraft ] }
              "stale", "stale", { snapshot with Claims = { snapshot.Claims.Head with Live = false } :: snapshot.Claims.Tail }
              "missing", "missing", { snapshot with Claims = [ snapshot.Claims.Head ] }
              "generation", "generation", { snapshot with Claims = { snapshot.Claims.Head with Generation = "5365999999" } :: snapshot.Claims.Tail }
              "cleared", "cleared", { snapshot with Relations = [] }
              "changed tokens", "tokens changed", { snapshot with Relations = [ { Left = a; Right = b; SharedTokens = [ "tests/other" ] } ] }
              "dependency", "Blocked-by", { snapshot with DurableDependencies = [ a, b ] }
              "bad digest", "digest", { snapshot with Waits = [ badDigest; bWait ] }
              "conflicting edge", "conflict", { snapshot with Waits = [ aWait; bWait; conflicting ] } ]
        for name, expected, candidate in cases do
            match Client.detectMutualOverlap candidate with
            | Client.MutualOverlapRefused reason -> Assert.Contains(expected, reason, StringComparison.OrdinalIgnoreCase)
            | other -> failwithf "%s should refuse, got %A" name other

    let private precedence (cycle: Client.MutualOverlapCycle) revision previous winner loser reason =
        let draft: Client.OverlapPrecedenceReceipt =
            { CycleDigest = cycle.Digest
              Revision = revision
              PreviousDigest = previous
              Winner = winner
              Loser = loser
              Host = "host/root"
              Reason = reason
              Digest = "" }
        { draft with Digest = Client.precedenceReceiptDigest draft }

    [<Fact>]
    let ``#2801 precedence accepts one current revision and measured digest-linked reversal`` () =
        let a, b, _, _, snapshot = cycleFixture ()
        let cycle = detected snapshot
        let first = precedence cycle 1 None a b None
        let reversed = precedence cycle 2 (Some first.Digest) b a (Some "winner CI is red; loser is green at exact head")
        Assert.Equal(Ok reversed, Client.validateOverlapPrecedence cycle [ first; reversed ])

    [<Fact>]
    let ``#2801 precedence refuses missing same-revision stale-chain participant digest and unmeasured reversal inversions`` () =
        let a, b, _, _, snapshot = cycleFixture ()
        let cycle = detected snapshot
        let first = precedence cycle 1 None a b None
        let sameRevision = precedence cycle 1 None b a None
        let staleChain = precedence cycle 3 (Some first.Digest) a b None
        let foreign = item 2801
        let wrongParticipant = precedence cycle 1 None a foreign None
        let badDigest = { first with Digest = "tampered" }
        let unmeasured = precedence cycle 2 (Some first.Digest) b a None
        let cases =
            [ "missing", []
              "same revision", [ first; sameRevision ]
              "stale chain", [ first; staleChain ]
              "participant", [ wrongParticipant ]
              "digest", [ badDigest ]
              "unmeasured reversal", [ first; unmeasured ] ]
        for name, receipts in cases do
            match Client.validateOverlapPrecedence cycle receipts with
            | Error _ -> ()
            | Ok value -> failwithf "%s should refuse, got %A" name value

    [<Fact>]
    let ``#2801 loser resume requires every winner-land rebase re-overlap re-widen and review predicate`` () =
        let green: Client.LoserResumeFacts =
            { WinnerLanded = true
              LoserClaimGenerationCurrent = true
              FetchedWinnerBase = true
              RebasedHead = true
              OverlapClear = true
              ExplicitlyRewidened = true
              ReviewRequired = true
              ExactHeadReviewed = true }
        Assert.Empty(Client.validateLoserResume green)
        let mutations =
            [ { green with WinnerLanded = false }
              { green with LoserClaimGenerationCurrent = false }
              { green with FetchedWinnerBase = false }
              { green with RebasedHead = false }
              { green with OverlapClear = false }
              { green with ExplicitlyRewidened = false }
              { green with ExactHeadReviewed = false } ]
        for mutation in mutations do
            Assert.Single(Client.validateLoserResume mutation) |> ignore
        Assert.Empty(Client.validateLoserResume { green with ReviewRequired = false; ExactHeadReviewed = false })

    let private orchestratorLease repo holder generation expires commentId =
        let draft: Client.BoardOrchestratorLease =
            { Board = "FS-GG/.github#2801"
              HolderRepo = repo
              Holder = holder
              Generation = generation
              ExpiresAtUnix = expires
              CommentId = commentId
              Digest = "" }
        { draft with Digest = Client.boardOrchestratorLeaseDigest draft }

    let private orchestratorRequest repo key generation commentId workRef =
        let draft: Client.BoardOrchestratorRequest =
            { Board = "FS-GG/.github#2801"
              RequestingRepo = repo
              RequestKey = key
              CoordinationRef = workRef
              LeaseGeneration = generation
              CommentId = commentId
              Digest = "" }
        { draft with Digest = Client.boardOrchestratorRequestDigest draft }

    let private orchestratorSnapshot leases requests : Client.BoardOrchestratorSnapshot =
        { Readable = true
          NowUnix = 1000L
          Board = "FS-GG/.github#2801"
          Leases = leases
          Requests = requests }

    [<Fact>]
    let ``#2801 live board orchestrator makes external repo route instead of competing`` () =
        let active = orchestratorLease ".github" "host-a" 7L 2000L 100L
        Assert.Equal(Client.RouteRequestTo active, Client.decideBoardOrchestrator "FS.GG.SDD" "host-b" (orchestratorSnapshot [ active ] []))

    [<Fact>]
    let ``#2801 board orchestrator promotes an idempotent external block ahead of ordinary work`` () =
        let active = orchestratorLease ".github" "host-a" 7L 2000L 100L
        let later = orchestratorRequest "z-repo" "block-z" 7L 102L (item 2797)
        let highest = orchestratorRequest "a-repo" "block-a" 7L 101L (item 2801)
        match Client.decideBoardOrchestrator ".github" "host-a" (orchestratorSnapshot [ active ] [ later; highest; highest ]) with
        | Client.RunBoardOrchestrator(actual, Some priority) ->
            Assert.Equal(active, actual)
            Assert.Equal(highest, priority)
        | other -> failwithf "expected active A with promoted request, got %A" other

    [<Fact>]
    let ``#2801 absent or expired authority permits only the next generation takeover`` () =
        Assert.Equal(Client.AcquireBoardOrchestrator 1L, Client.decideBoardOrchestrator "B" "host-b" (orchestratorSnapshot [] []))
        let stale = orchestratorLease ".github" "host-a" 4L 999L 100L
        Assert.Equal(Client.AcquireBoardOrchestrator 5L, Client.decideBoardOrchestrator "B" "host-b" (orchestratorSnapshot [ stale ] []))

    [<Fact>]
    let ``#2801 request written under a stale A generation fails closed`` () =
        let active = orchestratorLease ".github" "host-a" 8L 2000L 110L
        let staleRequest = orchestratorRequest "B" "block" 7L 111L (item 2801)
        match Client.decideBoardOrchestrator "B" "host-b" (orchestratorSnapshot [ active ] [ staleRequest ]) with
        | Client.BoardOrchestratorRefused reason -> Assert.Contains("stale", reason)
        | other -> failwithf "expected stale-generation refusal, got %A" other

    [<Fact>]
    let ``#2801 two-B generation race never authorizes two live orchestrators`` () =
        let b1 = orchestratorLease "B1" "host-b1" 9L 2000L 120L
        let b2 = orchestratorLease "B2" "host-b2" 9L 2000L 121L
        match Client.decideBoardOrchestrator "B1" "host-b1" (orchestratorSnapshot [ b1; b2 ] []) with
        | Client.BoardOrchestratorRefused reason -> Assert.Contains("conflict", reason)
        | other -> failwithf "expected generation-race refusal, got %A" other

    let private ok (body: string) : Errors.IoResult<Response> =
        Ok
            { Status = 200
              Body = body
              ETag = None
              NextLink = None
              Headers = Map.empty }

    let private currentRouteComment (paths: string) =
        StructuredFixtures.routeComment "FS-GG/FS.GG.SDD#42" (Some FS.GG.Coord.DeliveryRoute.Lightweight) "fixture-2459" None

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
        elif document.Contains "projectItems(first: 20)" && document.Contains "fieldValueByName(name: \"Blocked by\")" then
            // .github#2645 — `Board.itemBlockedBy`'s resolver read, the source `claim` now resolves this
            // item's live blocker edges from (ADR-0045 makes the COLUMN the typed dependency edge). A null
            // value is the ordinary "#42 has no dependency" answer, and costs zero further reads.
            Some
                """{"data":{"repository":{"issue":{"projectItems":{"nodes":[{"project":{"number":12},"fieldValueByName":null}]}}}},"rateLimit":{"cost":1,"remaining":4977}}"""
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
    ///
    /// `scanFails` (round-1 repair, review comment
    /// https://github.com/FS-GG/.github/pull/2463#issuecomment-5269036122) makes the `#353` scan itself
    /// UNREADABLE: `Reads.openIssues`'s own REST endpoint (`GET .../issues`, read above `other`'s own
    /// per-item shaping) answers a transport error instead of a list, so `collisionScan()` in `Client.fs`
    /// resolves to `Error _` regardless of `other`. This is a DIFFERENT failure surface than `other`
    /// (which shapes a successfully-read candidate set) — the two are independent knobs on purpose, so a
    /// scan-failure leg cannot be confused with a disjoint- or colliding-candidate leg by accident.
    let private world (paths: string) (other: (int * string * string) option) (scanFails: bool) (thread: Thread) =
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
            // `Reads.openIssues` — the #353 token shortlist's candidate universe. `scanFails` makes THIS
            // read fail — a transient 500, not a 404 — the one call `collisionScan()` cannot get past.
            | "GET", "repos/FS-GG/FS.GG.SDD/issues" when scanFails ->
                Error(Errors.Transport "fixture: the #353 candidate-issue list is UNREADABLE (round-1 repair)")
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
            | "GET", "repos/FS-GG/FS.GG.SDD/issues/42" ->
                // `state` is .github#2645's addition: `claim` now derives its board destination from this
                // item's LIVE facts (`Reads.issueState` reads exactly this field off exactly this response),
                // so a fixture that omitted it was describing an item whose OPEN/CLOSED state cannot be read
                // — which now correctly WITHHOLDS the column write. The body is unchanged.
                ok (JsonSerializer.Serialize {| number = 42; state = "open"; body = paths |})
            // .github#2645 — the item's own open-PR probe (`Reads.prAlive`) and, when it finds none, its
            // pushed-branch probe. #42 has NEITHER, so this fixture's item projects `In progress` exactly as
            // it did before, but now because the reads SAID so rather than because the observation asserted
            // it. Both must be served: an unreadable probe is not "no PR", and withholds.
            | "GET", "repos/FS-GG/FS.GG.SDD/pulls" -> ok "[]"
            | "GET", "repos/FS-GG/FS.GG.SDD/git/matching-refs/heads/item/42-" -> ok "[]"
            | "GET", p when other |> Option.exists (fun (n, _, _) -> p = $"repos/FS-GG/FS.GG.SDD/issues/%d{n}/comments") ->
                let _, holder, _ = other.Value
                ok (otherComments holder)
            // The courtesy notice `notifyOverlap` posts on the OTHER holder's item, default (warn) path only.
            | "POST", p when other |> Option.exists (fun (n, _, _) -> p = $"repos/FS-GG/FS.GG.SDD/issues/%d{n}/comments") ->
                ok """{"id":8099}"""
            | m, p -> Error(Errors.NotFound $"the fixture serves no %s{m} %s{p}"))

    let private context (transport: Fake.Recorder) : Kernel.Context =
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

    type private WaitThread() =
        let comments = Dictionary<int, ResizeArray<string>>()
        let posted = ResizeArray<int * string>()
        let issueBodies = Dictionary<int, string>()
        let mutable roomBody: string option = None
        let mutable roomCreates = 0

        do
            comments.[42] <- ResizeArray [ "<!-- fsgg:claim worker=vole-418 lease=120 -->\nheld" ]
            comments.[43] <- ResizeArray [ "<!-- fsgg:claim worker=smew-e1d9 lease=120 -->\nheld" ]
            comments.[99] <- ResizeArray()
            issueBodies.[42] <- "Paths: src/Thing.fs"
            issueBodies.[43] <- "Paths: src/Thing.fs"

        member _.Bodies number = comments.[number] |> List.ofSeq
        member _.Posted = List.ofSeq posted
        member _.Body number = issueBodies.[number]
        member _.SetBody(number, body) = issueBodies.[number] <- body
        member _.RoomBody with get () = roomBody and set value = roomBody <- value
        member _.RoomCreates with get () = roomCreates and set value = roomCreates <- value

        member _.Add(number, body) =
            if not (comments.ContainsKey number) then comments.[number] <- ResizeArray()
            comments.[number].Add body
            posted.Add(number, body)
            9100L + int64 posted.Count

        member this.Json number =
            let timestamp = DateTime.UtcNow.ToString "yyyy-MM-ddTHH:mm:ssZ"
            this.Bodies number
            |> List.mapi (fun index body ->
                {| id = (if number = 99 then 9101L + int64 index elif index = 0 then (if number = 42 then 8001L else 8070L) else 9100L + int64 index)
                   html_url = $"https://example.invalid/comments/%d{9100 + index}"
                   body = body
                   user = {| login = "EHotwagner" |}
                   created_at = timestamp
                   updated_at = timestamp |})
            |> JsonSerializer.Serialize

    let private jsonBody (request: Request) =
        match request.Body with
        | Json payload -> JsonDocument.Parse(payload).RootElement.GetProperty("body").GetString()
        | _ -> ""

    let private waitWorld (thread: WaitThread) =
        Fake.Recorder(fun (req: Request) ->
            let path = req.Path.Trim '/'
            match req.Method, path with
            | "POST", "graphql" ->
                match req.Body with
                | Query(document, _) ->
                    match graphqlAnswer document with
                    | Some answer -> ok answer
                    | None -> Error(Errors.NotFound "wait fixture has no answer for this GraphQL document")
                | _ -> Error(Errors.NotFound "wait fixture expected GraphQL")
            | "GET", "repos/FS-GG/FS.GG.SDD/issues/42/comments" -> ok (thread.Json 42)
            | "GET", "repos/FS-GG/FS.GG.SDD/issues/43/comments" -> ok (thread.Json 43)
            | "GET", "repos/FS-GG/FS.GG.SDD/issues/99/comments" -> ok (thread.Json 99)
            | "POST", "repos/FS-GG/FS.GG.SDD/issues/42/comments" ->
                ok (sprintf """{"id":%d}""" (thread.Add(42, jsonBody req)))
            | "POST", "repos/FS-GG/FS.GG.SDD/issues/99/comments" ->
                ok (sprintf """{"id":%d}""" (thread.Add(99, jsonBody req)))
            | "GET", "repos/FS-GG/FS.GG.SDD/issues/42" -> ok (JsonSerializer.Serialize {| state = "open"; body = thread.Body 42 |})
            | "GET", "repos/FS-GG/FS.GG.SDD/issues/43" -> ok (JsonSerializer.Serialize {| state = "open"; body = thread.Body 43 |})
            | "PATCH", "repos/FS-GG/FS.GG.SDD/issues/42" -> thread.SetBody(42, jsonBody req); ok "{}"
            | "PATCH", "repos/FS-GG/FS.GG.SDD/issues/43" -> thread.SetBody(43, jsonBody req); ok "{}"
            | "GET", "repos/FS-GG/FS.GG.SDD/issues" ->
                [ yield {| number = 42; state = "open"; body = thread.Body 42 |}
                  yield {| number = 43; state = "open"; body = thread.Body 43 |}
                  match thread.RoomBody with
                  | Some body -> yield {| number = 99; state = "open"; body = body |}
                  | None -> () ]
                |> JsonSerializer.Serialize
                |> ok
            | "POST", "repos/FS-GG/FS.GG.SDD/issues" ->
                let body = jsonBody req
                thread.RoomBody <- Some body
                thread.RoomCreates <- thread.RoomCreates + 1
                ok """{"number":99}"""
            | _ -> Error(Errors.NotFound $"wait fixture has no response for %s{req.Method} %s{path}"))

    let private runOverlapCommand (transport: Fake.Recorder) args =
        let dir = Path.Combine(Path.GetTempPath(), "fsgg-2801-" + Guid.NewGuid().ToString "n")
        let previousCache = Environment.GetEnvironmentVariable "FSGG_COORD_CACHE"
        let previousKitRoot = Environment.GetEnvironmentVariable "FSGG_KIT_ROOT"
        let previousSessions = sessionVars |> List.map (fun v -> v, Environment.GetEnvironmentVariable v)
        let stdout, stderr = Console.Out, Console.Error
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
            let opts = Options.parse args |> Result.defaultWith failwith
            let code = Client.overlapCmd (context transport) opts
            Console.Out.Flush()
            Console.Error.Flush()
            code, capturedOut.ToString(), capturedErr.ToString()
        finally
            Console.SetOut stdout
            Console.SetError stderr
            Environment.SetEnvironmentVariable("FSGG_COORD_CACHE", previousCache)
            Environment.SetEnvironmentVariable("FSGG_KIT_ROOT", previousKitRoot)
            for name, value in previousSessions do Environment.SetEnvironmentVariable(name, value)
            try Directory.Delete(dir, true) with _ -> ()

    let private runOverlapWait transport =
        runOverlapCommand transport [ "overlap"; "wait"; "FS.GG.SDD#42"; "FS.GG.SDD#43"; "host/root"; "--worker"; "vole-418" ]

    let private runOverlapArbitrate transport =
        runOverlapCommand transport [ "overlap"; "arbitrate"; "FS.GG.SDD#43"; "FS.GG.SDD#42"; "host/root"; "--worker"; "vole-418" ]

    let private runOverlapOrchestrate transport =
        runOverlapCommand transport [ "overlap"; "orchestrate"; "FS.GG.SDD#99"; "FS.GG.SDD"; "coord-fix-42"; "FS.GG.SDD#42"; "host-b" ]

    [<Fact>]
    let ``#2801 compiled no-A route acquires one authoritative board-orchestrator generation`` () =
        let thread = WaitThread()
        let code, output, errors = runOverlapOrchestrate (waitWorld thread)
        Assert.Equal(0, code)
        Assert.Empty(errors)
        Assert.Contains("ACQUIRED", output)
        Assert.Single(thread.Bodies 99) |> ignore
        Assert.Contains("fsgg.coord.board-orchestrator-lease/v1", thread.Bodies(99).Head)

    [<Fact>]
    let ``#2801 compiled overlap wait route writes one generation-bound receipt and is retry-idempotent`` () =
        let thread = WaitThread()
        let transport = waitWorld thread
        let firstCode, firstOut, firstErr = runOverlapWait transport
        Assert.Equal(0, firstCode)
        Assert.Empty(firstErr)
        Assert.Contains("WAIT RECORDED", firstOut)
        Assert.Single(thread.Posted) |> ignore
        let _, receipt = thread.Posted.Head
        Assert.Contains("fsgg.coord.overlap-wait/v1", receipt)
        Assert.Contains("\"waiterGeneration\":\"8001\"", receipt)
        Assert.Contains("\"predecessorGeneration\":\"8070\"", receipt)
        Assert.Contains("\"sharedTokens\":[\"src/Thing.fs\"]", receipt)
        let secondCode, secondOut, secondErr = runOverlapWait transport
        Assert.Equal(0, secondCode)
        Assert.Empty(secondErr)
        Assert.Contains("WAIT RECORDED", secondOut)
        Assert.Single(thread.Posted) |> ignore

    [<Fact>]
    let ``#2801 compiled reciprocal wait route creates one automatic room and backrefs both items`` () =
        let thread = WaitThread()
        let a: Types.Ref = { Owner = "FS-GG"; Repo = "FS.GG.SDD"; Number = 42 }
        let b: Types.Ref = { Owner = "FS-GG"; Repo = "FS.GG.SDD"; Number = 43 }
        let reciprocal = wait b "8070" a "8001" [ "src/Thing.fs" ] "host/root"
        let reciprocalBody =
            "<!-- fsgg:overlap-wait-key/v1 waiter=FS-GG/FS.GG.SDD#43 generation=8070 -->\n"
            + "<!-- fsgg:overlap-wait/v1 -->\n"
            + JsonSerializer.Serialize
                {| schema = "fsgg.coord.overlap-wait/v1"
                   waiter = reciprocal.Waiter.Canonical
                   waiterGeneration = reciprocal.WaiterGeneration
                   predecessor = reciprocal.Predecessor.Canonical
                   predecessorGeneration = reciprocal.PredecessorGeneration
                   sharedTokens = reciprocal.SharedTokens
                   host = reciprocal.Host
                   digest = reciprocal.Digest |}
        thread.Add(43, reciprocalBody) |> ignore
        let transport = waitWorld thread
        let code, output, errors = runOverlapWait transport
        Assert.True((code = Kernel.ExitContended), $"expected mutual-cycle exit, got %d{code}; stdout=%s{output}; stderr=%s{errors}")
        Assert.Empty(errors)
        Assert.Contains("MUTUAL OVERLAP", output)
        Assert.Contains("#99", output)
        Assert.True(thread.RoomBody.IsSome)
        Assert.Contains("fsgg:mutual-overlap-room/v1", thread.RoomBody.Value)
        Assert.Contains("Rooms: #99", thread.Body 42)
        Assert.Contains("Rooms: #99", thread.Body 43)
        Assert.Equal(1, thread.RoomCreates)

    [<Fact>]
    let ``#2801 compiled arbitration route records precedence and narrows loser while its claim remains held`` () =
        let thread = WaitThread()
        let a: Types.Ref = { Owner = "FS-GG"; Repo = "FS.GG.SDD"; Number = 42 }
        let b: Types.Ref = { Owner = "FS-GG"; Repo = "FS.GG.SDD"; Number = 43 }
        let aWait = wait a "8001" b "8070" [ "src/Thing.fs" ] "host/root"
        let bWait = wait b "8070" a "8001" [ "src/Thing.fs" ] "host/root"
        let receiptBody (receipt: Client.OverlapWaitReceipt) =
            $"<!-- fsgg:overlap-wait-key/v1 waiter=%s{receipt.Waiter.Canonical} generation=%s{receipt.WaiterGeneration} -->\n"
            + "<!-- fsgg:overlap-wait/v1 -->\n"
            + JsonSerializer.Serialize
                {| schema = "fsgg.coord.overlap-wait/v1"
                   waiter = receipt.Waiter.Canonical
                   waiterGeneration = receipt.WaiterGeneration
                   predecessor = receipt.Predecessor.Canonical
                   predecessorGeneration = receipt.PredecessorGeneration
                   sharedTokens = receipt.SharedTokens
                   host = receipt.Host
                   digest = receipt.Digest |}
        thread.Add(42, receiptBody aWait) |> ignore
        thread.Add(43, receiptBody bWait) |> ignore
        let snapshot: Client.MutualOverlapSnapshot =
            { Readable = true
              Claims = [ { Item = a; Generation = "8001"; Live = true }; { Item = b; Generation = "8070"; Live = true } ]
              Relations = [ { Left = a; Right = b; SharedTokens = [ "src/Thing.fs" ] } ]
              Waits = [ aWait; bWait ]
              DurableDependencies = []
              RelatedRoomCycleDigests = [] }
        let cycle = detected snapshot
        thread.RoomBody <- Some($"<!-- fsgg:mutual-overlap-room/v1 cycle=%s{cycle.Digest} -->\n\nPaths: none")
        thread.Add(99, "ordinary room message") |> ignore
        let transport = waitWorld thread
        let code, output, errors = runOverlapArbitrate transport
        Assert.True((code = 0), $"expected arbitration success, got %d{code}; stdout=%s{output}; stderr=%s{errors}")
        Assert.Empty(errors)
        Assert.Contains("PRECEDENCE APPLIED", output)
        Assert.Contains("Paths: any", thread.Body 42)
        Assert.Contains("fsgg:claim worker=vole-418", thread.Bodies(42).Head)
        let precedence = thread.Bodies 99 |> List.filter (fun body -> body.Contains "fsgg.coord.overlap-precedence/v1")
        Assert.Single(precedence) |> ignore

    // ---- AC1/AC2/AC4: default is a WARNING that still claims -------------------------------------------

    [<Fact>]
    let ``#2459 claim warns on an OVERLAP by default and STILL claims (exit green)`` () =
        let thread = Thread()
        let paths = "Paths: src/Thing.fs"
        let transport = world paths (Some(43, "smew-e1d9", paths)) false thread

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
        let transport = world paths (Some(43, "smew-e1d9", paths)) false thread

        let code, _, err = runClaim transport (claimArgs [ "--refuse-overlap" ])

        Assert.Equal(Kernel.ExitContended, code)
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
            world "Paths: src/Thing.fs" (Some(43, "smew-e1d9", "Paths: src/Other.fs")) false thread1

        let code1, out1, err1 = runClaim transport1 (claimArgs [])

        Assert.Equal(0, code1)
        Assert.DoesNotContain("OVERLAP", err1)

        let receipt1 = JsonDocument.Parse(out1.Trim()).RootElement
        Assert.Empty(receipt1.GetProperty("collisions").EnumerateArray() |> List.ofSeq)

        let thread2 = Thread()

        let transport2 =
            world "Paths: src/Thing.fs" (Some(43, "smew-e1d9", "Paths: src/Other.fs")) false thread2

        let code2, _, err2 = runClaim transport2 (claimArgs [ "--refuse-overlap" ])

        Assert.Equal(0, code2)
        Assert.DoesNotContain("OVERLAP", err2)

    [<Fact>]
    let ``#2459 claim with no other live claim at all never warns`` () =
        let thread = Thread()
        let transport = world "Paths: src/Thing.fs" None false thread

        let code, out, err = runClaim transport (claimArgs [])

        Assert.Equal(0, code)
        Assert.DoesNotContain("OVERLAP", err)

        let receipt = JsonDocument.Parse(out.Trim()).RootElement
        Assert.Empty(receipt.GetProperty("collisions").EnumerateArray() |> List.ofSeq)

    // ---- Round-1 repair (review https://github.com/FS-GG/.github/pull/2463#issuecomment-5269036122) --
    // ---- Client.fs:4928-4935 — an UNREADABLE scan is a THIRD outcome, distinct from "no collision" and
    // ---- "collision found", and it is where the AC2/AC3 polarity actually lives: the guard on
    // ---- `opts.RefuseOverlap` at the scan-error arm is what keeps `claim` degrading to a WARNING (never
    // ---- a refusal) by default, and what makes `--refuse-overlap` refuse rather than claim blind. The
    // ---- critic reproduced a SURGICAL polarity swap here (`Error e when opts.RefuseOverlap ->` to
    // ---- `Error e when not opts.RefuseOverlap ->`) and the full 767-test suite still passed — these two
    // ---- legs are what makes that inversion visible. See the gate-inversion note below the second leg.

    [<Fact>]
    let ``#2459 round-1: an UNREADABLE collision scan still WARNS-and-claims by default (AC2)`` () =
        let thread = Thread()

        let transport =
            world "Paths: src/Thing.fs" (Some(43, "smew-e1d9", "Paths: src/Thing.fs")) true thread

        let code, out, err = runClaim transport (claimArgs [])

        // AC2, over a DEGRADED scan specifically: `claim` must keep working (exit green, marker posted)
        // rather than fail closed on a scan it could not run — orphan recovery is the entire reason
        // `claim` exists outside the scheduler (#2459's own "Why anyone uses `claim`" section), and this
        // very repair chain adopted a stranded item (`.github#2216`) through exactly that path today.
        Assert.Equal(0, code)
        Assert.Contains("could not run", err)
        Assert.Contains("claiming", err)
        Assert.Contains("anyway", err)
        Assert.Contains("--refuse-overlap", err)
        // NOT an OVERLAP line: the scan never reached a verdict, so it must not report one it does not
        // have — "I could not look" is never laundered into "I looked, and it collides" either.
        Assert.DoesNotContain("OVERLAP", err)

        let receipt = JsonDocument.Parse(out.Trim()).RootElement
        Assert.True(receipt.GetProperty("converged").GetBoolean())
        // `[]` here is NOT a claim of disjointness — the scan never ran — but it is what the wire
        // contract has to say today: `ClaimReceipt.Collisions`'s own `.fsi` doc names this exact case.
        Assert.Empty(receipt.GetProperty("collisions").EnumerateArray() |> List.ofSeq)

        // The lock actually landed: a marker was posted on #42.
        Assert.NotEmpty(thread.Posted)

    [<Fact>]
    let ``#2459 round-1: claim --refuse-overlap REFUSES on an UNREADABLE collision scan (AC3)`` () =
        let thread = Thread()

        let transport =
            world "Paths: src/Thing.fs" (Some(43, "smew-e1d9", "Paths: src/Thing.fs")) true thread

        let code, _, err = runClaim transport (claimArgs [ "--refuse-overlap" ])

        // `--refuse-overlap` cannot GUARANTEE disjointness over a scan it never completed, so it refuses
        // rather than claim blind (#523's doctrine, applied to `claim` too). This is Errors.exitCode's
        // ordinary `Transport` mapping (`Kernel.ExitError`, 1) — NOT `ExitContended` (6), which is
        // reserved for a scan that COMPLETED and found a real collision. An unreadable scan is not a
        // collision, and conflating the two exit codes would make a caller unable to tell "definitely
        // overlaps" from "could not check" from the number alone.
        Assert.Equal(Kernel.ExitError, code)
        Assert.Contains("could not reach GitHub", err)
        Assert.Contains("UNREADABLE (round-1 repair)", err)
        Assert.DoesNotContain("OVERLAP", err)

        // Nothing landed: no marker on #42, no notice on #43 — there was nothing yet to coordinate around.
        Assert.Empty(thread.Posted)
        Assert.False(transport.Logged "comment-post FS-GG/FS.GG.SDD 43")

    // GATE-INVERSION EVIDENCE (round-1 repair, recorded by hand at review time, per the critic's own
    // surgical-mutation method — a blanket mutation "can fail tests for incidental reasons and give a
    // false sense of coverage", per review comment 5269036122, quoting the `.github#2454` lesson).
    //
    // MUTATION: in `src/FS.GG.Coord.Cli/Client.fs`, swap the scan-error arm's guard —
    //     | Error e when opts.RefuseOverlap ->        (before)
    //     | Error e when not opts.RefuseOverlap ->    (after — the critic's own surgical swap)
    // OBSERVED RED, both new legs above, against the mutated binary:
    //   - "an UNREADABLE collision scan still WARNS-and-claims by default (AC2)": the DEFAULT call now
    //     hits the (originally --refuse-overlap-only) `failWith` arm and REFUSES — `Assert.Equal(0, code)`
    //     fails (`Kernel.ExitError` observed instead), and the marker is never posted
    //     (`Assert.NotEmpty(thread.Posted)` fails on an empty list).
    //   - "claim --refuse-overlap REFUSES on an UNREADABLE collision scan (AC3)": `--refuse-overlap` now
    //     hits the (originally default-only) warn arm and CLAIMS BLIND — `Assert.Equal(Kernel.ExitError,
    //     code)` fails (`0` observed instead), and `Assert.Empty(thread.Posted)` fails (a marker WAS
    //     posted).
    // RESTORED, rebuilt, both legs green again — matching the four pre-existing `ClaimOverlapTests` legs,
    // which the same mutation does not touch (they never exercise `collisionScan()` returning `Error`).
