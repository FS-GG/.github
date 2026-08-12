module FS.GG.Coord.GitHub.Tests.ScanBoardTests

open System
open System.IO
open Xunit
open FS.GG.Coord.Types
open FS.GG.Coord.GitHub
open FS.GG.Coord.GitHub.Errors
open FS.GG.Coord.GitHub.Transport

/// THE READING HALF OF THE SCAN, WHICH THE ROUND-TRIP TESTS DO NOT REACH.
///
/// `ScanRoundTripTests` drives `Scan.snapshot` — the ASSEMBLER — from hand-built `Row`s. That is the right
/// test for the codec, and it found three real format bugs. But it hands `Scan.board` its answer, so the
/// thing that actually READS the board — the cursor loop, the row parser, and the cache codec — was never
/// exercised at all.
///
/// That is the gap that matters most on this module, because a board scan that silently drops rows does not
/// fail: it schedules a SMALLER board, confidently, and the items it lost are simply never offered to
/// anyone. There is no error to see.
type private Sandbox() =
    let dir =
        Path.Combine(Path.GetTempPath(), "fsgg-scan-test-" + Guid.NewGuid().ToString("N"))

    do
        Directory.CreateDirectory dir |> ignore
        Environment.SetEnvironmentVariable("FSGG_COORD_CACHE", dir)

    member _.Dir = dir

    interface IDisposable with
        member _.Dispose() =
            Environment.SetEnvironmentVariable("FSGG_COORD_CACHE", null)
            Environment.SetEnvironmentVariable("FSGG_COORD_SCAN_TTL_SEC", null)

            try
                Directory.Delete(dir, true)
            with _ ->
                ()

let private ok (body: string) =
    Ok
        { Status = 200
          Body = body
          ETag = None
          NextLink = None; Headers = Map.empty }

let private scripted (responses: IoResult<Response> list) =
    let queue = System.Collections.Generic.Queue<IoResult<Response>>(responses)

    Fake.Recorder(fun _ ->
        if queue.Count = 0 then
            failwith "the transport was called more times than the test scripted"
        else
            queue.Dequeue())

/// One page of the board. `hasNext` drives the cursor loop.
let private page (nodes: string) (hasNext: bool) (cursor: string) =
    let hn = if hasNext then "true" else "false"

    $"""{{"data":{{"organization":{{"projectV2":{{"items":{{
        "pageInfo":{{"hasNextPage":%s{hn},"endCursor":"%s{cursor}"}},
        "nodes":[%s{nodes}]}}}}}}}}}}"""

let private issueNode (number: int) (status: string) (blockedBy: string) (state: string) =
    $"""{{"status":{{"name":"%s{status}"}},
          "blockedBy":{{"text":"%s{blockedBy}"}},
          "content":{{"__typename":"Issue","number":%d{number},"title":"item %d{number}",
                      "state":"%s{state}","repository":{{"nameWithOwner":"FS-GG/FS.GG.SDD"}}}}}}"""

let private scopedIssueNode (number: int) (pathRepo: string) =
    $"""{{"status":{{"name":"Ready"}},
          "repoScope":{{"name":"%s{pathRepo}"}},
          "content":{{"__typename":"Issue","number":%d{number},"title":"item %d{number}",
                      "state":"OPEN","repository":{{"nameWithOwner":"FS-GG/.github"}}}}}}"""

/// One page of a USER-owned board — the same shape as `page`, but nested under `data.user` instead of
/// `data.organization`, exactly as GitHub answers a `user(login:)` query (#1344).
let private userPage (nodes: string) (hasNext: bool) (cursor: string) =
    let hn = if hasNext then "true" else "false"

    $"""{{"data":{{"user":{{"projectV2":{{"items":{{
        "pageInfo":{{"hasNextPage":%s{hn},"endCursor":"%s{cursor}"}},
        "nodes":[%s{nodes}]}}}}}}}}}}"""

/// One page of a VIEWER-owned board — nested under `data.viewer`, exactly as GitHub answers a `viewer`
/// query (#1349): the token's own board, resolved with no login at all.
let private viewerPage (nodes: string) (hasNext: bool) (cursor: string) =
    let hn = if hasNext then "true" else "false"

    $"""{{"data":{{"viewer":{{"projectV2":{{"items":{{
        "pageInfo":{{"hasNextPage":%s{hn},"endCursor":"%s{cursor}"}},
        "nodes":[%s{nodes}]}}}}}}}}}}"""

/// A recorder that CAPTURES the GraphQL document of every request into `docs`, then serves the scripted
/// responses in order. It is how the owner-kind tests prove which root field the query actually hit.
let private capturing (docs: System.Collections.Generic.List<string>) (responses: IoResult<Response> list) =
    let queue = System.Collections.Generic.Queue<IoResult<Response>>(responses)

    Fake.Recorder(fun req ->
        match req.Body with
        | Query(doc, _) -> docs.Add doc
        | _ -> ()

        if queue.Count = 0 then
            failwith "the transport was called more times than the test scripted"
        else
            queue.Dequeue())

// ---- owner-kind awareness (#1344) ---------------------------------------------------------------------

[<Fact>]
let ``a USER-owned board is scanned through user(login:) and its rows resolve (#1344)`` () =
    use _sandbox = new Sandbox()

    // A board owned by a personal account answers to `user(login:)`, and its items come back nested under
    // `data.user`. `FSGG_COORD_OWNER_TYPE=user` WITH an explicit `FSGG_COORD_OWNER` selects that shape;
    // without it, a user login queried through `organization(login:)` resolves to null and the whole board is
    // unreachable. (With NO explicit owner, `user` falls to viewer-scoping — a separate test, #1349.)
    Environment.SetEnvironmentVariable("FSGG_COORD_OWNER_TYPE", "user")
    Environment.SetEnvironmentVariable("FSGG_COORD_OWNER", "EHotwagner")

    try
        let docs = System.Collections.Generic.List<string>()

        let transport =
            capturing docs [ ok (userPage (issueNode 7 "Ready" "" "OPEN") false "") ]

        match Scan.board transport Cache.Scheduling "EHotwagner" "TowerDefense" 3 with
        | Ok [ row ] ->
            Assert.Equal(7, row.Ref.Number)
            Assert.Equal(Ready, row.Status)

            // The document hit the USER node, not the organization node — that is the whole fix.
            Assert.All(
                docs,
                fun d ->
                    Assert.Contains("user(login: $owner)", d)
                    Assert.DoesNotContain("organization(login: $owner)", d)
            )
        | other -> failwith $"a user-owned board must resolve through user(login:) — got %A{other}"
    finally
        Environment.SetEnvironmentVariable("FSGG_COORD_OWNER_TYPE", null)
        Environment.SetEnvironmentVariable("FSGG_COORD_OWNER", null)

[<Fact>]
let ``a VIEWER-owned board is scanned through viewer, with no login in config (#1349)`` () =
    use _sandbox = new Sandbox()

    // `FSGG_COORD_OWNER_TYPE=user` with NO explicit `FSGG_COORD_OWNER` scans the board through the token's own
    // `viewer` identity: its items come back nested under `data.viewer`, the document selects the
    // argument-less `viewer` root, and it carries no `$owner` variable at all. No login travels to the API.
    Environment.SetEnvironmentVariable("FSGG_COORD_OWNER_TYPE", "user")
    Environment.SetEnvironmentVariable("FSGG_COORD_OWNER", null)

    try
        let docs = System.Collections.Generic.List<string>()

        let transport =
            capturing docs [ ok (viewerPage (issueNode 7 "Ready" "" "OPEN") false "") ]

        match Scan.board transport Cache.Scheduling "@me" "TowerDefense" 3 with
        | Ok [ row ] ->
            Assert.Equal(7, row.Ref.Number)
            Assert.Equal(Ready, row.Status)

            // The document hit the `viewer` root — not organization, not user(login:) — and declares no
            // `$owner` variable: no login reaches the API.
            Assert.All(
                docs,
                fun d ->
                    Assert.Contains("viewer {", d)
                    Assert.DoesNotContain("organization(login: $owner)", d)
                    Assert.DoesNotContain("user(login: $owner)", d)
                    Assert.DoesNotContain("$owner", d)
            )
        | other -> failwith $"a viewer-owned board must resolve through viewer — got %A{other}"
    finally
        Environment.SetEnvironmentVariable("FSGG_COORD_OWNER_TYPE", null)

[<Fact>]
let ``an ORG-owned scan still queries organization(login:) - user support is OFF by default (#1344)`` () =
    use _sandbox = new Sandbox()

    // THE REGRESSION GUARD. With the env var unset, the org path must be byte-identical to what preceded
    // #1344 — same root field, same `data.organization` parse — so nothing on the FS-GG board moves.
    Environment.SetEnvironmentVariable("FSGG_COORD_OWNER_TYPE", null)

    let docs = System.Collections.Generic.List<string>()

    let transport =
        capturing docs [ ok (page (issueNode 1 "Ready" "" "OPEN") false "") ]

    match Scan.board transport Cache.Scheduling "FS-GG" "Coordination" 12 with
    | Ok [ row ] ->
        Assert.Equal(1, row.Ref.Number)

        Assert.All(
            docs,
            fun d ->
                Assert.Contains("organization(login: $owner)", d)
                Assert.DoesNotContain("user(login: $owner)", d)
        )
    | other -> failwith $"the org scan must resolve unchanged — got %A{other}"

// ---- the cursor loop ----------------------------------------------------------------------------------

[<Fact>]
let ``the board scan PAGINATES on the cursor - a second page is not lost`` () =
    use _sandbox = new Sandbox()

    // A board scan that stops at the first page does not FAIL. It schedules a SMALLER BOARD, confidently,
    // and the items past item 100 are never offered to anybody — with no error anywhere to see. The live
    // board has 640 items, so this is seven pages, and six of them would vanish.
    let transport =
        scripted
            [ ok (page (issueNode 1 "Ready" "" "OPEN") true "CUR1")
              ok (page (issueNode 2 "Ready" "" "OPEN") false "") ]

    match Scan.board transport Cache.Scheduling "FS-GG" "Coordination" 12 with
    | Ok rows ->
        Assert.Equal(2, List.length rows)
        Assert.Equal<int list>([ 1; 2 ], rows |> List.map (fun r -> r.Ref.Number))
        Assert.Equal(2, transport.GraphQlCalls)
    | Error e -> failwith $"the scan must follow the cursor — got %A{e}"

[<Fact>]
let ``a failed page ABORTS the scan - it never returns a partial board as a complete one`` () =
    use _sandbox = new Sandbox()

    // A HALF-READ BOARD REPORTED AS A WHOLE ONE is the worst outcome available here. Every item on the pages
    // we did not get would read as "not on the board" — which is #421's premise, and it would make the tool
    // offer `item-add` for items that are already there.
    let transport =
        scripted
            [ ok (page (issueNode 1 "Ready" "" "OPEN") true "CUR1")
              Error(Http(502, "bad gateway")) ]

    match Scan.board transport Cache.Scheduling "FS-GG" "Coordination" 12 with
    | Error(Http(502, _)) -> ()
    | Ok rows -> failwith $"a failed page produced a 'complete' board of %d{List.length rows} row(s) — the rest silently vanished"
    | other -> failwith $"expected the scan to refuse — got %A{other}"

[<Fact>]
let ``#421 a rate-limited scan PROPAGATES - it is not an empty board`` () =
    use _sandbox = new Sandbox()
    let transport = Fake.Recorder(fun _ -> Error(RateLimited(UnknownBudget, None)))

    match Scan.board transport Cache.Scheduling "FS-GG" "Coordination" 12 with
    | Error(RateLimited _) -> ()
    | Ok [] -> failwith "an exhausted budget reported an EMPTY BOARD — this is #344, and every worker is told to go home"
    | other -> failwith $"expected RateLimited — got %A{other}"

[<Fact>]
let ``a GraphQL rate limit arrives as HTTP 200 with errors, and is still RateLimited`` () =
    use _sandbox = new Sandbox()

    // GitHub reports an exhausted GraphQL budget as a **200** carrying `errors` — not a 403. A scan that only
    // classified on the status code would read this as a successful, empty board.
    let transport =
        scripted [ ok """{"errors":[{"message":"API rate limit exceeded"}]}""" ]

    match Scan.board transport Cache.Scheduling "FS-GG" "Coordination" 12 with
    | Error(RateLimited _) -> ()
    | other -> failwith $"a 200-with-errors rate limit must still be RateLimited — got %A{other}"

// ---- the row parser -----------------------------------------------------------------------------------

[<Fact>]
let ``a DRAFT board card is skipped - it has no issue, so it cannot be claimed`` () =
    use _sandbox = new Sandbox()

    // A draft item is a card with no issue behind it. It has no ref, so it cannot be reserved, blocked or
    // done — and inventing one would put a phantom on the queue that no worker could ever claim or close.
    let draft = """{"status":{"name":"Ready"},"blockedBy":null,"content":null}"""
    let one = issueNode 1 "Ready" "" "OPEN"
    let nodes = draft + "," + one

    let transport = scripted [ ok (page nodes false "") ]

    match Scan.board transport Cache.Scheduling "FS-GG" "Coordination" 12 with
    | Ok rows ->
        Assert.Equal(1, List.length rows)
        Assert.Equal(1, rows.[0].Ref.Number)
    | Error e -> failwith $"a draft card must be skipped, not fatal — got %A{e}"

[<Fact>]
let ``an item with NO Status is NoStatus - which is its own state, not Backlog`` () =
    use _sandbox = new Sandbox()

    // #437 / #485(c). An item on the board with no Status was INVISIBLE to every scheduler. It is not
    // `Backlog`, it is not an error — it is its own state, and a three-valued thing modelled as two values
    // is how it stayed invisible.
    let noStatus =
        """{"status":null,"blockedBy":null,
            "content":{"__typename":"Issue","number":7,"title":"t","state":"OPEN",
                       "repository":{"nameWithOwner":"FS-GG/FS.GG.SDD"}}}"""

    let transport = scripted [ ok (page noStatus false "") ]

    match Scan.board transport Cache.Scheduling "FS-GG" "Coordination" 12 with
    | Ok [ row ] ->
        Assert.Equal(NoStatus, row.Status)
        Assert.NotEqual(Backlog, row.Status)
    | other -> failwith $"a Status-less item must still be a row — got %A{other}"

[<Fact>]
let ``#641 a PULL REQUEST on the board is FLAGGED as one`` () =
    use _sandbox = new Sandbox()

    let pr =
        """{"status":{"name":"Ready"},"blockedBy":null,
            "content":{"__typename":"PullRequest","number":9,"title":"a pr","state":"OPEN",
                       "repository":{"nameWithOwner":"FS-GG/FS.GG.SDD"}}}"""

    let transport = scripted [ ok (page pr false "") ]

    match Scan.board transport Cache.Scheduling "FS-GG" "Coordination" 12 with
    | Ok [ row ] -> Assert.True(row.IsPullRequest)
    | other -> failwith $"a PR must be flagged, so the candidate filter can drop it — got %A{other}"

[<Fact>]
let ``a MERGED pull request reads as CLOSED, not OPEN`` () =
    use _sandbox = new Sandbox()

    // A PR's state is OPEN | CLOSED | MERGED, and `IssueState` has two cases. A merged PR is not open — and
    // reading MERGED as OPEN would make a merged blocker block forever, which is #476's exact bug.
    let merged =
        """{"status":null,"blockedBy":null,
            "content":{"__typename":"PullRequest","number":9,"title":"t","state":"MERGED",
                       "repository":{"nameWithOwner":"FS-GG/FS.GG.SDD"}}}"""

    let transport = scripted [ ok (page merged false "") ]

    match Scan.board transport Cache.Scheduling "FS-GG" "Coordination" 12 with
    | Ok [ row ] -> Assert.Equal(Closed, row.State)
    | other -> failwith $"a MERGED PR is not OPEN — got %A{other}"

// ---- the cache ----------------------------------------------------------------------------------------

[<Fact>]
let ``a second scan inside the TTL spends ZERO GraphQL - and returns the SAME rows`` () =
    use _sandbox = new Sandbox()

    // #418: the budget is 5,000 pt/hr for the WHOLE FLEET, and five workers looping `take` drained it in
    // fifteen minutes. The cache is what makes a fan-out affordable — but only if what it serves back is
    // actually the board, which means the row codec has to round-trip.
    let n1 = issueNode 1 "Ready" "FS.GG.SDD#2" "OPEN"
    let n2 = issueNode 2 "Blocked" "" "CLOSED"
    let transport = scripted [ ok (page (n1 + "," + n2) false "") ]

    let first = Scan.board transport Cache.Scheduling "FS-GG" "Coordination" 12
    let second = Scan.board transport Cache.Scheduling "FS-GG" "Coordination" 12

    match first, second with
    | Ok a, Ok b ->
        // ZERO additional calls: the scripted transport would THROW if a second scan were attempted.
        Assert.Equal(1, transport.GraphQlCalls)

        // AND THE ROWS SURVIVE THE CODEC. A cache that serves back a different board is worse than no cache:
        // it is a wrong answer, confidently, to the whole fleet, for ninety seconds.
        Assert.Equal<int list>(a |> List.map (fun r -> r.Ref.Number), b |> List.map (fun r -> r.Ref.Number))
        Assert.Equal<BoardStatus list>(a |> List.map (fun r -> r.Status), b |> List.map (fun r -> r.Status))
        Assert.Equal<string list>(a |> List.map (fun r -> r.BlockedByRaw), b |> List.map (fun r -> r.BlockedByRaw))
        Assert.Equal<IssueState list>(a |> List.map (fun r -> r.State), b |> List.map (fun r -> r.State))

    | x, y -> failwith $"both scans must succeed — got %A{x} then %A{y}"

[<Fact>]
let ``a RECONCILING scan never serves the cache, however fresh it is`` () =
    use _sandbox = new Sandbox()

    // `ready` / `lint` / `who` exist to say what is true RIGHT NOW. A cached "truth" is how a reconciler
    // reports drift that was already fixed — or misses drift that is still there. `--fresh` maps here.
    let transport =
        scripted
            [ ok (page (issueNode 1 "Ready" "" "OPEN") false "")
              ok (page (issueNode 1 "Ready" "" "OPEN") false "") ]

    Scan.board transport Cache.Scheduling "FS-GG" "Coordination" 12 |> ignore
    Scan.board transport Cache.Reconciling "FS-GG" "Coordination" 12 |> ignore

    // The reconciler PAID. If it had been served the cache, the scripted transport would still hold its
    // second response and this would be 1.
    Assert.Equal(2, transport.GraphQlCalls)

[<Fact>]
let ``#344 a FAILED scan is never written to the cache`` () =
    use sandbox = new Sandbox()
    let transport = Fake.Recorder(fun _ -> Error(Http(500, "boom")))

    Scan.board transport Cache.Scheduling "FS-GG" "Coordination" 12 |> ignore

    // A failed scan that reached the cache would write "the board is empty" into it and hand that,
    // confidently, to the next ninety seconds of workers. One failed read, multiplied by the fleet.
    let cached = Directory.GetFiles(sandbox.Dir, "scan-*.json")
    Assert.Empty(cached)

// ---- `Scan.scope` — the `--repo` filter's one home (#979) --------------------------------------------
//
// The filter was hand-rolled FIVE times (snapshot, ready, who, inbox, lint) and every copy fell open the
// same way: a `--repo` naming no repo resolved to itself, matched nothing, and reported an EMPTY QUEUE
// WITH A GREEN EXIT — indistinguishable from a repo that genuinely has no items. That is what kept #962
// silent through three occurrences (#381, #446, #962): each presented as "an empty queue" rather than "I
// could not find that repo", so each repair added the missing verb instead of removing the list.
//
// These are pure — no board, no transport. `scripts/check-repo-filter-monopoly.py` is the other half:
// it makes a sixth copy unwritable.

let private scopeRow (repo: string) (n: int) : Scan.Row =
    { Ref = { Owner = "FS-GG"; Repo = repo; Number = n }
      Title = $"item %d{n}"
      Status = BoardStatus.Ready
      BlockedByRaw = ""
      State = IssueState.Open
      IsPullRequest = false
      PathRepo = repo
      BoardClass = None
      Severity = Unset
      Phase = None
      CreatedAt = None
      SweptBody = None
      NodeId = Some $"I_scope_%d{n}" }

let private scopeBoard =
    [ scopeRow "FS.GG.SDD" 99; scopeRow "FS.GG.Rendering" 202; scopeRow ".github" 54 ]

[<Fact>]
let ``#1732 Repo Scope supplies the path repository while Ref remains the issue repository`` () =
    use sandbox = new Sandbox()
    let transport = scripted [ ok (page (scopedIssueNode 1732 "audio") false "") ]

    match Scan.board transport Cache.Scheduling "FS-GG" "Coordination" 12 with
    | Ok [ row ] ->
        Assert.Equal(".github", row.Ref.Repo)
        Assert.Equal("audio", row.PathRepo)
    | other -> failwithf "expected one scoped row, got %A" other

// ---- .github#2254 REPAIR 1: `scanFresh`'s own gate on `Row.SweptBody` -------------------------------

/// A closed-and-`Done` board node for `#398`, `Class` column under `boardClass`'s control — the
/// `FS.GG.Templates#398` shape #2254's issue names, reproduced here because the fixture cannot reach a
/// second repo.
let private closedDoneNode (boardClass: string option) =
    let classField =
        match boardClass with
        | None -> "null"
        | Some c -> $"""{{"name":"%s{c}"}}"""

    $"""{{"status":{{"name":"Done"}},"class":%s{classField},
          "content":{{"__typename":"Issue","number":398,"title":"a Done row declaring its own class",
                      "state":"CLOSED","repository":{{"nameWithOwner":"FS-GG/.github"}}}}}}"""

[<Fact>]
let ``#2254 Cache.Reconciling enriches SweptBody for a closed Done row with an EMPTY Class column`` () =
    // THE POSITIVE LEG. `scanFresh` pays exactly ONE extra REST read — scripted second, so `scripted`
    // itself proves nothing more is asked — and the parsed body reaches `Row.SweptBody`.
    use sandbox = new Sandbox()

    let transport =
        scripted
            [ ok (page (closedDoneNode None) false "")
              ok """{"number":398,"body":"Paths: none\n\nClass: hardening\n"}""" ]

    match Scan.board transport Cache.Reconciling "FS-GG" "Coordination" 12 with
    | Ok [ row ] ->
        match row.SweptBody with
        | Some(Ok body) -> Assert.Contains("Class: hardening", body)
        | other -> failwithf "a Reconciling scan of an empty-column closed row must populate SweptBody — got %A" other
    | other -> failwithf "expected one row — got %A" other

[<Fact>]
let ``#2254 Cache.Scheduling never enriches SweptBody, even for the identical empty-column closed row`` () =
    // THE CRITIC'S OWN REGRESSION, PINNED AT ITS SOURCE. `heron-fef6` measured `+1 GET .../issues/398` on
    // `batch --json` at the pre-repair head — this is `batch`'s own read, `Scan.board`, called with
    // `Cache.Scheduling`. `scripted` carries exactly ONE response (the board page); a second call of ANY
    // shape — the extra read the critic measured — throws "called more times than scripted" rather than
    // merely going unobserved, so a green run is proof the read never happened.
    use sandbox = new Sandbox()
    Environment.SetEnvironmentVariable("FSGG_COORD_SCAN_TTL_SEC", "0") // force scanFresh, not a cache hit

    let transport = scripted [ ok (page (closedDoneNode None) false "") ]

    match Scan.board transport Cache.Scheduling "FS-GG" "Coordination" 12 with
    | Ok [ row ] -> Assert.Equal(None, row.SweptBody)
    | other -> failwithf "expected one row — got %A" other

[<Fact>]
let ``#2254 Cache.Reconciling does NOT enrich SweptBody when the Class column already carries a value`` () =
    // THE BOUND, PINNED AT ITS SOURCE. A closed row that already carries SOME `Class` value costs
    // `Reconciling` nothing extra either — `scripted`'s single response is the same proof as the leg above.
    use sandbox = new Sandbox()

    let transport = scripted [ ok (page (closedDoneNode (Some "hardening")) false "") ]

    match Scan.board transport Cache.Reconciling "FS-GG" "Coordination" 12 with
    | Ok [ row ] -> Assert.Equal(None, row.SweptBody)
    | other -> failwithf "expected one row — got %A" other

[<Fact>]
let ``#979 a --repo naming no board row REPORTS, and does not merely return empty`` () =
    let scoped = Scan.scope (Some "sd") scopeBoard

    Assert.Empty scoped.Rows

    // The whole bug: `[]` was the entire answer. The advisory is what makes a green exit MEAN something.
    let msg = Assert.True(scoped.Advisory.IsSome, "a --repo that names no row must be reported"); scoped.Advisory.Value

    Assert.Contains("no board row names repo `sd`", msg)
    // The known set is derived from the ROWS IN HAND — never a roster (#266's corollary: compare against
    // reality, not a record of it). So it cannot go stale, and the `kind: client` shim never needs repos.yml.
    Assert.Contains("FS.GG.SDD", msg)
    Assert.Contains("FS.GG.Rendering", msg)
    Assert.Contains(".github", msg)

[<Fact>]
let ``#979 a --repo that MATCHES is silent — the advisory is not noise on the happy path`` () =
    let scoped = Scan.scope (Some "FS.GG.SDD") scopeBoard

    Assert.Equal(1, List.length scoped.Rows)
    // A gate that cries wolf on the happy path teaches exactly one lesson: that its warnings are noise.
    Assert.True(scoped.Advisory.IsNone, "a --repo that names a real row must say nothing")

[<Fact>]
let ``#1732 canonical command scope selects a short-id Repo Scope`` () =
    let row = { scopeRow ".github" 1732 with PathRepo = "audio" }
    let scoped = Scan.scope (Some "FS.GG.Audio") [ row ]

    Assert.Single(scoped.Rows) |> ignore
    Assert.Equal(row, List.head scoped.Rows)
    Assert.True(scoped.Advisory.IsNone)

// ---- .github#2363 criterion 4: the receiver-fixture proof --------------------------------------------
//
// `#1732`'s test above proves ONE row, spelled ONE way, is found by ITS OWN canonical name. What #2363
// asks is broader: a board can carry the SAME repo spelled differently across DIFFERENT rows (a human
// editing `Repo Scope` by hand has no reason to spell it consistently), and `batch --repo sir`,
// `batch --repo S.I.R.`, and the current-checkout default (which resolves to the same canonical name
// before it ever reaches here — `Options.fs:1533-1542`, `.github#2398`) must all select the SAME set —
// never a spelling-dependent subset that quietly drops a row the caller has every reason to expect.

let private sirBoard =
    [ { scopeRow "S.I.R." 1 with PathRepo = "sir" } // the roster short-id, as a human would type it
      { scopeRow "S.I.R." 2 with PathRepo = "S.I.R." } // the canonical name, as `enrich` would write it
      { scopeRow "S.I.R." 3 with PathRepo = "Sir" } // a casing a human would also type without thinking
      scopeRow "FS.GG.SDD" 4 ] // control: a different repo must never be swept in

[<Fact>]
let ``.github#2363 criterion 4: --repo sir and --repo S.I.R. select the identical row set from a mixed-spelling board``
    ()
    =
    // Every real `--repo` argument reaching `Scan.scope` is ALREADY canonical — `Options.resolveRepo`
    // resolves it before the parser ever calls a verb (#962/#2398) — so a caller typing `sir` and one
    // typing `S.I.R.` both hand `Scan.scope` the identical `Some "S.I.R."`. What this test isolates is
    // the ROW side: that `Scan.scope`'s own `RepoScope.resolve r.PathRepo` finds a row no matter which of
    // the three spellings the BOARD carries for it.
    let scoped = Scan.scope (Some "S.I.R.") sirBoard

    Assert.Equal(3, List.length scoped.Rows)
    Assert.Equal<int list>([ 1; 2; 3 ], scoped.Rows |> List.map (fun r -> r.Ref.Number) |> List.sort)
    Assert.True(scoped.Advisory.IsNone)

/// Gate-inversion evidence, actually run: reverting `Scan.scope`'s row-side match from
/// `RepoScope.resolve r.PathRepo |> function Repository n -> String.Equals(n, name, ...) | ... -> false`
/// back to the pre-#2398 shape — comparing the RAW `r.PathRepo` token directly against `name`
/// (`String.Equals(r.PathRepo, name, StringComparison.OrdinalIgnoreCase)`) — turned 2 tests red:
/// `dotnet test tests/FS.GG.Coord.GitHub.Tests --filter FullyQualifiedName~ScanBoardTests` reported
/// `Failed: 2, Passed: 45` — THIS test (1 row selected instead of 3: only the already-canonical
/// `"S.I.R."`-spelled row matched the raw compare, `"sir"` and `"Sir"` did not) and the pre-existing
/// `#1732 canonical command scope selects a short-id Repo Scope` test above (`audio` no longer matches
/// `FS.GG.Audio` by raw compare either) — confirming the mutation is a genuine regression of #2398's own
/// fix, not an artifact of this test alone. Restoring `RepoScope.resolve` reran green: `Failed: 0,
/// Passed: 47`.

[<Fact>]
let ``#979 no --repo is the identity, and says nothing`` () =
    let scoped = Scan.scope None scopeBoard

    Assert.Equal(3, List.length scoped.Rows)
    Assert.True(scoped.Advisory.IsNone)

[<Fact>]
let ``#979 the match is case-insensitive, as every hand-rolled copy was`` () =
    let scoped = Scan.scope (Some "fs.gg.sdd") scopeBoard

    Assert.Equal(1, List.length scoped.Rows)
    Assert.True(scoped.Advisory.IsNone)

[<Fact>]
let ``#979 an EMPTY board yields NO advisory — #266's defect, inside the fix for it`` () =
    // With zero rows there is no known-repo set, so "no row names `X`" would be a confident claim about a
    // board nobody could see, and "The board knows: " would name nothing. A failed scan is never an empty
    // one (#344), so this is a genuinely empty board: the emptiness is the BOARD's, not the scope's.
    let scoped = Scan.scope (Some "anything") []

    Assert.Empty scoped.Rows
    Assert.True(scoped.Advisory.IsNone, "an empty board cannot support a claim about which repos exist")

[<Fact>]
let ``#979 the advisory states BOTH readings — a typo and a repo with no items are one fact from here`` () =
    let scoped = Scan.scope (Some "FS.GG.Audio") scopeBoard

    // FS.GG.Audio is a REAL, rostered repo that simply has no board items. It is indistinguishable from a
    // typo at this layer, so the message must not pronounce it one — it says both, and the exit stays 0.
    let msg = scoped.Advisory.Value
    Assert.Contains("Check the spelling, or this repo has no items on the board yet", msg)

[<Fact>]
let ``#979 the known-repo set is DEDUPED and ordered — a board of N items lists each repo once`` () =
    let board = scopeBoard @ [ scopeRow "FS.GG.SDD" 100; scopeRow "fs.gg.sdd" 101 ]
    let scoped = Scan.scope (Some "nope") board

    let msg = scoped.Advisory.Value
    let line = msg.Split('\n') |> Array.find (fun l -> l.Contains "The board knows")

    // Three distinct repos, case-folded — not five entries, and not "FS.GG.SDD" twice in two casings.
    Assert.Equal(3, line.Split(',').Length)

[<Fact>]
let ``#979 snapshot carries the advisory OUT, on the receipt next-batch-take read`` () =
    // THE SEAM THIS PINS is the one the fix itself got wrong first time round. `snapshot` scoped
    // correctly from the start, and `next`/`batch`/`take` destructured its receipt as `Ok(_, doc, _)` —
    // so the advisory was computed and DROPPED, and `take --repo <typo>` went on reporting an empty
    // queue over a full board. That is the whole of #979, surviving inside #979's own repair, in the one
    // verb family that matters most: `--repo <short-id>` is the documented spelling, a typo is the
    // likeliest thing a worker types, and `take` is the one command in a worker's loop.
    //
    // The transport ERRORS on every call, and that is deliberate: a scoped-out `--repo` yields zero
    // candidates, so a snapshot that reads nothing proves the advisory is computed from the rows in hand
    // and needs no IO to say so. If this ever starts failing with a transport error, snapshot has begun
    // reading per-candidate data for candidates it does not have.
    let transport = Fake.Recorder(fun _ -> Error(Http(500, "no read should happen here")))

    match Scan.snapshot transport scopeBoard (Some "sd") false None 120 with
    | Error e -> failwith $"snapshot must not need IO to scope: %A{e}"
    | Ok(_, receipt) ->
        Assert.Equal(0, receipt.Candidates)

        let msg =
            Assert.True(receipt.RepoAdvisory.IsSome, "the receipt must carry the advisory out")
            receipt.RepoAdvisory.Value

        Assert.Contains("no board row names repo `sd`", msg)

[<Fact>]
let ``#979 snapshot says NOTHING for a --repo that matches — no noise on the happy path`` () =
    let transport = Fake.Recorder(fun _ -> Error(Http(500, "boom")))

    // FS.GG.SDD matches a row, so the scope is silent. (The snapshot itself then fails on the body read
    // for that candidate — which is the point of the assertion below: we are pinning the ADVISORY, and a
    // matched repo must produce none whatever else the scan goes on to do.)
    match Scan.snapshot transport scopeBoard (Some "FS.GG.SDD") false None 120 with
    | Error _ -> ()
    | Ok(_, receipt) -> Assert.True(receipt.RepoAdvisory.IsNone, "a matched --repo must say nothing")

// ---- .github#1794: the OFF-BOARD sweep carries the unreadable body to the wire ------------------------
//
// `Scan.snapshot`'s candidate loop has said this since #1150 — *"an unreadable one is `bodyUnreadable`,
// NOT an empty body"* — and then the OFF-BOARD sweep, reading the same kind of body off the issue-LIST
// route instead of the per-issue route, let an unreadable one fall through a `| _ -> ()` and reserve
// nothing. `TouchSet.parse ""` answers `Undeclared`, which conflicts with nothing, so a live off-board
// claim whose body could not be read reserved NO FILES and a candidate overlapping its real tree was
// scheduled straight over it. That is #1150's own fail-open on the arm #1150 did not reach.

/// A transport that answers the three routes the off-board sweep uses, keyed on path. `list` is the
/// issue-LIST body (where the anomaly lives), `comments` the marker read, `body` the per-issue read.
let private offBoardRoutes (list: string) (comments: int -> string) (body: string) =
    Fake.Recorder(fun (req: Request) ->
        let path = req.Path

        if path = "graphql" && req.Subject = "fresh issue body and comment-count facts" then
            let aliases =
                match req.Body with
                | Query(_, variables) ->
                    variables
                    |> List.mapi (fun i (_, value) ->
                        let id =
                            match value with
                            | VId value -> value
                            | _ -> failwith "node-facts ids must use the GraphQL ID type"

                        $"\"n%d{i}\":{{\"id\":\"%s{id}\",\"body\":%s{System.Text.Json.JsonSerializer.Serialize body},\"comments\":{{\"totalCount\":1}}}}")
                    |> String.concat ","
                | _ -> failwith "node facts must be GraphQL"

            ok $"{{\"data\":{{%s{aliases}}}}}"
        elif path.EndsWith "/comments" then
            let n =
                path.Split('/') |> Array.filter (fun s -> s <> "") |> Array.item 4 |> int

            ok (comments n)
        elif path.EndsWith "/issues" then
            ok list
        elif path.Contains "/pulls" || path.Contains "matching-refs" then
            ok "[]"
        else
            ok body)

/// A live claim marker on `n`, held by `worker`. `updated_at` is NOW, so the lease has not lapsed and
/// `reserver` and `winner` agree about it — this test must not depend on the `.github#1792` question.
let private liveMarker (worker: string) (n: int) =
    let now = DateTimeOffset.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ")

    $"""[{{"id":%d{7000 + n},"body":"<!-- fsgg:claim worker=%s{worker} lease=120 -->\nheld","user":{{"login":"EHotwagner"}},"updated_at":"%s{now}"}}]"""

/// One readable live marker plus one comment whose body cannot be classified. The readable marker is a
/// lower bound, not permission to reserve only its holder's declared surface.
let private incompleteLiveMarker (worker: string) (n: int) =
    let now = DateTimeOffset.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ")

    $"""[{{"id":%d{7000 + n},"body":"<!-- fsgg:claim worker=%s{worker} lease=120 -->\nheld","updated_at":"%s{now}"}},
         {{"id":%d{8000 + n},"body":null,"updated_at":"%s{now}"}}]"""

let private nodeFactsResponse (id: string) (body: string) (commentCount: int) =
    let encodedBody = System.Text.Json.JsonSerializer.Serialize body
    $"""{{"data":{{"n0":{{"id":"%s{id}","body":%s{encodedBody},"comments":{{"totalCount":%d{commentCount}}}}}}}}}"""

[<Fact>]
let ``#2308 exact fresh zero-comment facts eliminate both per-row REST reads`` () =
    use _sandbox = new Sandbox()

    let row = scopeRow "FS.GG.SDD" 99
    let transport =
        Fake.Recorder(fun (req: Request) ->
            match req.Path, req.Subject with
            | "graphql", "fresh issue body and comment-count facts" ->
                ok (nodeFactsResponse row.NodeId.Value "Paths: src/Board/**" 0)
            | path, _ when path.EndsWith "/issues" -> ok "[]"
            | path, _ when path.EndsWith "/comments" -> Error(Http(500, "zero comments must not start a marker scan"))
            | path, _ when path.Contains "/issues/" -> Error(Http(500, "fresh body facts must replace issue-get"))
            | _, _ -> Error(Http(500, "unexpected request")))

    match Scan.snapshot transport [ row ] (Some "FS.GG.SDD") false None 120 with
    | Error e -> failwith $"an exact zero comment fact must be enough to assemble the snapshot — got %A{e}"
    | Ok(document, _) ->
        Assert.Contains("src/Board/**", document)
        Assert.Equal(0, transport.Count "issue-get")
        Assert.Equal(0, transport.Count "comment-list")
        Assert.Equal(2, transport.RestCalls) // the separate off-board reservation sweep; no candidate REST reads

[<Fact>]
let ``#2308 positive count still sees an old marker beyond a 100-comment bound`` () =
    use _sandbox = new Sandbox()

    let row = scopeRow "FS.GG.SDD" 99
    let old = DateTimeOffset.UtcNow.AddHours(-3).ToString("yyyy-MM-ddTHH:mm:ssZ")
    let noise =
        [ 1 .. 100 ]
        |> List.map (fun id -> $"{{\"id\":%d{9000 + id},\"body\":\"ordinary comment\",\"updated_at\":\"%s{old}\"}}")

    let oldClaim =
        $"{{\"id\":7,\"body\":\"<!-- fsgg:claim worker=old-holder lease=120 -->\\nheld\",\"updated_at\":\"%s{old}\"}}"

    let comments = "[" + String.concat "," (noise @ [ oldClaim ]) + "]"

    let transport =
        Fake.Recorder(fun (req: Request) ->
            match req.Path, req.Subject with
            | "graphql", "fresh issue body and comment-count facts" ->
                ok (nodeFactsResponse row.NodeId.Value "Paths: src/Board/**" 101)
            | path, _ when path.EndsWith "/comments" -> ok comments
            | path, _ when path.EndsWith "/issues" -> ok "[]"
            | path, _ when path.Contains "/issues/" -> Error(Http(500, "fresh body facts must replace issue-get"))
            | _, _ -> Error(Http(500, "unexpected request")))

    match Scan.snapshot transport [ row ] (Some "FS.GG.SDD") false None 120 with
    | Error e -> failwith $"a complete positive-count marker scan must keep an old unreaped claim — got %A{e}"
    | Ok(document, _) ->
        Assert.Contains("old-holder", document)
        Assert.Equal(0, transport.Count "issue-get")
        Assert.Equal(1, transport.Count "comment-list")

[<Fact>]
let ``#2308 a missing or mismatched fresh node fact refuses rather than deciding a cached lock`` () =
    use _sandbox = new Sandbox()

    let row = scopeRow "FS.GG.SDD" 99
    let transport =
        Fake.Recorder(fun (req: Request) ->
            match req.Path, req.Subject with
            | "graphql", "fresh issue body and comment-count facts" ->
                ok (nodeFactsResponse "I_wrong_node" "Paths: src/Board/**" 0)
            | _, _ -> Error(Http(500, "facts must fail before any fallback lock read")))

    match Scan.snapshot transport [ row ] (Some "FS.GG.SDD") false None 120 with
    | Error(Malformed(_, detail)) -> Assert.Contains("do not match", detail)
    | other -> failwith $"a mismatched fresh node fact must fail closed — got %A{other}"

[<Fact>]
let ``#2308 a legacy cache row without a node id takes the former fresh REST lock path`` () =
    use _sandbox = new Sandbox()

    let row = { scopeRow "FS.GG.SDD" 99 with NodeId = None }
    let transport = offBoardRoutes "[]" (fun _ -> "[]") """{"number":99,"body":"Paths: src/Board/**"}"""

    match Scan.snapshot transport [ row ] (Some "FS.GG.SDD") false None 120 with
    | Error e -> failwith $"a legacy cache row must remain safely schedulable through REST — got %A{e}"
    | Ok(document, _) ->
        Assert.Contains("src/Board/**", document)
        Assert.Equal(1, transport.Count "issue-get")
        Assert.Equal(1, transport.Count "comment-list")

[<Fact>]
let ``#1896 a board candidate with an incomplete marker scan refuses the scheduler snapshot`` () =
    use _sandbox = new Sandbox()
    Environment.SetEnvironmentVariable("FSGG_COORD_SCAN_TTL_SEC", "0")

    let transport =
        offBoardRoutes
            "[]"
            (fun n -> if n = 99 then incompleteLiveMarker "visible-holder" 99 else "[]")
            """{"number":99,"body":"Paths: src/Board/**"}"""

    match Scan.snapshot transport [ scopeRow "FS.GG.SDD" 99 ] (Some "FS.GG.SDD") false None 120 with
    | Error(Malformed(_, detail)) ->
        Assert.Contains("claim-marker scan is incomplete", detail)
        Assert.Contains("comment 1", detail)
    | other -> failwith $"the candidate loop must refuse an incomplete lock read — got %A{other}"

[<Fact>]
let ``#1896 an off-board issue with an incomplete marker scan refuses the scheduler snapshot`` () =
    use _sandbox = new Sandbox()
    Environment.SetEnvironmentVariable("FSGG_COORD_SCAN_TTL_SEC", "0")

    let list =
        """[{"number":99,"state":"open","body":"Paths: src/Board/**"},
            {"number":500,"state":"open","body":"Paths: src/OffBoard/**"}]"""

    let transport =
        offBoardRoutes
            list
            (fun n -> if n = 500 then incompleteLiveMarker "visible-holder" 500 else "[]")
            """{"number":99,"body":"Paths: src/Board/**"}"""

    match Scan.snapshot transport [ scopeRow "FS.GG.SDD" 99 ] (Some "FS.GG.SDD") false None 120 with
    | Error(Malformed(_, detail)) ->
        Assert.Contains("claim-marker scan is incomplete", detail)
        Assert.Contains("comment 1", detail)
    | other -> failwith $"the off-board sweep must refuse an incomplete lock read — got %A{other}"

[<Fact>]
let ``#1794 an OFF-BOARD live claim whose body could not be read reserves an UNKNOWN surface, not nothing`` () =
    use _sandbox = new Sandbox()
    Environment.SetEnvironmentVariable("FSGG_COORD_SCAN_TTL_SEC", "0")

    // #99 is the board row. #500 is OFF the board — a claim whose column flip failed, or one that never
    // reached the board — and its LIST element carries no `body` field at all. It holds a live claim.
    let list =
        """[{"number":99,"state":"open","body":"Paths: src/Board/**"},
            {"number":500,"state":"open"}]"""

    let transport =
        offBoardRoutes
            list
            (fun n -> if n = 500 then liveMarker "offboard-holder" 500 else "[]")
            """{"number":99,"body":"Paths: src/Board/**"}"""

    match Scan.snapshot transport [ scopeRow "FS.GG.SDD" 99 ] (Some "FS.GG.SDD") false None 120 with
    | Error e -> failwith $"the sweep must produce a snapshot — got %A{e}"
    | Ok(document, _) ->
        // The wire convention (#1150): EITHER a `paths` array OR a `pathsUnreadable` reason, never both.
        // The reader keys on which is present and reconstructs `TouchSet.Unreadable`, which
        // `Batch.schedule` reds the batch on (`unusableReservation`).
        Assert.Contains("pathsUnreadable", document)

        // AND THE FAIL-OPEN IS NAMED, not merely absent. Before .github#1794 this claim was DROPPED from
        // `inFlight` entirely — so the assertion that would have passed on the broken engine is "the
        // snapshot does not mention 500", and this is its negation.
        Assert.Contains("offboard-holder", document)

[<Fact>]
let ``#1794 an off-board claim with a READABLE body still reserves its NAMED paths - the control`` () =
    use _sandbox = new Sandbox()
    Environment.SetEnvironmentVariable("FSGG_COORD_SCAN_TTL_SEC", "0")

    // The same arrangement with #500's body PRESENT. Without this leg, the test above could pass on an
    // engine that called every off-board body unreadable — which would red every batch on the board.
    let list =
        """[{"number":99,"state":"open","body":"Paths: src/Board/**"},
            {"number":500,"state":"open","body":"Paths: src/OffBoard/**"}]"""

    let transport =
        offBoardRoutes
            list
            (fun n -> if n = 500 then liveMarker "offboard-holder" 500 else "[]")
            """{"number":99,"body":"Paths: src/Board/**"}"""

    match Scan.snapshot transport [ scopeRow "FS.GG.SDD" 99 ] (Some "FS.GG.SDD") false None 120 with
    | Error e -> failwith $"the sweep must produce a snapshot — got %A{e}"
    | Ok(document, _) ->
        Assert.Contains("src/OffBoard/**", document)
        Assert.DoesNotContain("pathsUnreadable", document)

[<Fact>]
let ``#1794 an off-board issue with an unreadable body and NO claim reserves nothing - a comment is not a lock`` () =
    use _sandbox = new Sandbox()
    Environment.SetEnvironmentVariable("FSGG_COORD_SCAN_TTL_SEC", "0")

    // THE PRECISION LEG. An unreadable body only matters where something is RESERVED, and only a claim
    // reserves. A chatty issue nobody holds must not become an unreadable reservation that reds every
    // batch on the board — that would trade a rare fail-open for a frequent outage, which is the trade
    // `.github#1779` was careful not to make.
    let list =
        """[{"number":99,"state":"open","body":"Paths: src/Board/**"},
            {"number":500,"state":"open"}]"""

    let transport = offBoardRoutes list (fun _ -> "[]") """{"number":99,"body":"Paths: src/Board/**"}"""

    match Scan.snapshot transport [ scopeRow "FS.GG.SDD" 99 ] (Some "FS.GG.SDD") false None 120 with
    | Error e -> failwith $"the sweep must produce a snapshot — got %A{e}"
    | Ok(document, _) -> Assert.DoesNotContain("pathsUnreadable", document)

// ---- the Phase column and the issue's age (.github#1598) ----------------------------------------------
//
// Two facts the board has always held and the scheduler never read. `Phase` is a single-select column; the
// age comes off `content.createdAt`. Both ride the same 7-point full scan the cost model measures — a
// resolver field and a scalar on a node already selected — and both must survive the cache codec, because
// a fact that round-trips wrong ranks the whole board wrong for the cache's lifetime.

/// An issue node carrying the two new facts, spelled exactly as GitHub answers them.
let private rankedNode (number: int) (phase: string) (createdAt: string) =
    let phaseField =
        if phase = "" then "" else $""""phase":{{"name":"%s{phase}"}},"""

    $"""{{"status":{{"name":"Ready"}},
          "blockedBy":{{"text":""}},
          %s{phaseField}
          "content":{{"__typename":"Issue","number":%d{number},"title":"item %d{number}",
                      "state":"OPEN","createdAt":"%s{createdAt}",
                      "repository":{{"nameWithOwner":"FS-GG/FS.GG.SDD"}}}}}}"""

[<Fact>]
let ``#1901 Severity is read from the board and survives the scan cache`` () =
    use _sandbox = new Sandbox()

    let node =
        """{"status":{"name":"Ready"},"blockedBy":{"text":""},"severity":{"name":"High"},
             "content":{"__typename":"Issue","number":1,"title":"item 1","state":"OPEN",
                        "createdAt":"2026-01-02T03:04:05Z",
                        "repository":{"nameWithOwner":"FS-GG/FS.GG.SDD"}}}"""

    let transport = scripted [ ok (page node false "") ]
    let first = Scan.board transport Cache.Scheduling "FS-GG" "Coordination" 12
    let second = Scan.board transport Cache.Scheduling "FS-GG" "Coordination" 12

    match first, second with
    | Ok [ a ], Ok [ b ] ->
        Assert.Equal(High, a.Severity)
        Assert.Equal(a.Severity, b.Severity)
        Assert.Equal(1, transport.GraphQlCalls)
    | other -> failwith $"Severity did not survive the board/cache boundary — got %A{other}"

[<Fact>]
let ``#1901 a missing or unknown Severity is Unset and cannot promote the row`` () =
    use _sandbox = new Sandbox()

    let unknown =
        """{"status":{"name":"Ready"},"blockedBy":{"text":""},"severity":{"name":"Urgent"},
             "content":{"__typename":"Issue","number":1,"title":"item 1","state":"OPEN",
                        "repository":{"nameWithOwner":"FS-GG/FS.GG.SDD"}}}"""

    let transport = scripted [ ok (page unknown false "") ]

    match Scan.board transport Cache.Scheduling "FS-GG" "Coordination" 12 with
    | Ok [ row ] -> Assert.Equal(Unset, row.Severity)
    | other -> failwith $"unknown Severity must read as Unset — got %A{other}"

[<Fact>]
let ``#1598 the Phase column and createdAt are READ off the board`` () =
    use _sandbox = new Sandbox()

    let transport =
        scripted [ ok (page (rankedNode 1 "P2 SDD" "2026-01-02T03:04:05Z") false "") ]

    match Scan.board transport Cache.Scheduling "FS-GG" "Coordination" 12 with
    | Ok [ row ] ->
        Assert.Equal(Some P2Sdd, row.Phase)
        Assert.True(row.CreatedAt.IsSome, "createdAt was on the node and did not reach the row")
        Assert.Equal(2026, row.CreatedAt.Value.Year)
        // UTC, not the runner's local time. An age in days survives a timezone shift, but a rank input
        // that moves by a timezone on one machine and not another is non-determinism in the batch.
        Assert.Equal(TimeSpan.Zero, row.CreatedAt.Value.Offset)
    | other -> failwith $"expected one row carrying both rank inputs — got %A{other}"

[<Fact>]
let ``#1598 a board with NO Phase field reads as None and does not fail the scan`` () =
    use _sandbox = new Sandbox()

    // Every board but the live one, and every parity fixture. `Option.bind` on the resolved name, exactly
    // as `class` does — a project that has never heard of `Phase` must scan, not die.
    let transport =
        scripted [ ok (page (rankedNode 1 "" "2026-01-02T03:04:05Z") false "") ]

    match Scan.board transport Cache.Scheduling "FS-GG" "Coordination" 12 with
    | Ok [ row ] -> Assert.Equal(None, row.Phase)
    | other -> failwith $"a phase-less board must still scan — got %A{other}"

[<Fact>]
let ``#1598 a Phase value this engine does not speak is None, never the nearest phase`` () =
    use _sandbox = new Sandbox()

    // `P0 Decisions` outranks every other phase, so resolving an unknown option onto it would make a board
    // edit nobody told the engine about the highest-priority work there is. `None` sorts LAST.
    let transport =
        scripted [ ok (page (rankedNode 1 "P9 Something" "2026-01-02T03:04:05Z") false "") ]

    match Scan.board transport Cache.Scheduling "FS-GG" "Coordination" 12 with
    | Ok [ row ] -> Assert.Equal(None, row.Phase)
    | other -> failwith $"an unknown phase must read as None — got %A{other}"

[<Fact>]
let ``#1598 both rank inputs survive the cache codec`` () =
    use _sandbox = new Sandbox()

    // THE ROUND-TRIP IS THE POINT. The cache serves the whole fleet for its TTL, so a `phase` that renders
    // and does not parse back would silently de-rank every item on the board — with no error anywhere, and
    // a batch that still looks perfectly well-formed.
    let transport =
        scripted [ ok (page (rankedNode 1 "P0 Decisions" "2025-12-31T23:59:59Z") false "") ]

    let first = Scan.board transport Cache.Scheduling "FS-GG" "Coordination" 12
    let second = Scan.board transport Cache.Scheduling "FS-GG" "Coordination" 12

    match first, second with
    | Ok [ a ], Ok [ b ] ->
        Assert.Equal(1, transport.GraphQlCalls)
        Assert.Equal(Some P0Decisions, a.Phase)
        Assert.Equal(a.Phase, b.Phase)
        // The INSTANT round-trips, not a precomputed age — an `ageDays` written to disk would be wrong by
        // the cache's own lifetime the moment it was read back.
        Assert.Equal(a.CreatedAt, b.CreatedAt)
    | other -> failwith $"the rank inputs did not survive the cache — got %A{other}"

[<Fact>]
let ``#1598 an entry written before this existed reads as None, never as a zero age`` () =
    use _sandbox = new Sandbox()

    // A pre-#1598 cache entry has neither key. `None` under-prioritises the row for at most one cache
    // lifetime; a zero age would make it the YOUNGEST possible item, which is the one reading that can
    // never trigger starvation escalation.
    let transport =
        scripted [ ok (page (issueNode 1 "Ready" "" "OPEN") false "") ]

    match Scan.board transport Cache.Scheduling "FS-GG" "Coordination" 12 with
    | Ok [ row ] ->
        Assert.Equal(None, row.Phase)
        Assert.Equal(None, row.CreatedAt)
    | other -> failwith $"expected a row with neither rank input — got %A{other}"

// ---- .github#1933: a body-only `Blocked by:` line is INERT — `blockers` reads the FIELD alone -----------
//
// `Blocked by` is a Projects v2 board FIELD; a `Blocked by:` line written into the issue BODY is a
// different medium that nothing resolving a blocker ever reads (ADR-0045, `Rooms.fsi`, `HumanBlock.fsi`).
// Two agents independently read a body-only line, found no field edge, and concluded there was none — one
// filed a false defect (.github#1931), and a `reconcile` pass twice proposed promoting a row whose real
// blocker was still open because its FIELD, unlike its body, had gone stale (`FS.GG.Templates#348`).
//
// `Scan.blockersOf` (the private function this pins from the outside) builds `blockers` from
// `row.BlockedByRaw` alone — the field — and reads `body` on a completely separate call
// (`Reads.issueBody`) for a DIFFERENT key on the wire. This is the AC1 claim of .github#1933, pinned as a
// fixture rather than left to restate in prose: a body carrying `Blocked by: <ref>` with an EMPTY field
// must produce `blockers: []`, and the body text must still be readable on `body` — proving the scan saw
// the line and still would not treat it as an edge.
[<Fact>]
let ``#1933 a body-only 'Blocked by:' line with an EMPTY field yields blockers=[] - the field is the only edge`` () =
    use _sandbox = new Sandbox()
    Environment.SetEnvironmentVariable("FSGG_COORD_SCAN_TTL_SEC", "0")

    let row = { scopeRow "FS.GG.SDD" 99 with Status = BoardStatus.Blocked; BlockedByRaw = "" }

    let transport =
        offBoardRoutes
            "[]"
            (fun _ -> "[]")
            """{"number":99,"body":"Some park notes.\n\nBlocked by: FS-GG/FS.GG.SDD#77"}"""

    match Scan.snapshot transport [ row ] (Some "FS.GG.SDD") false None 120 with
    | Error e -> failwith $"a readable body and an empty field must still produce a snapshot — got %A{e}"
    | Ok(document, _) ->
        use doc = System.Text.Json.JsonDocument.Parse(document: string)
        let item = doc.RootElement.GetProperty("items").[0]

        Assert.Equal(0, item.GetProperty("blockers").GetArrayLength())
        Assert.Contains("Blocked by: FS-GG/FS.GG.SDD#77", item.GetProperty("body").GetString())

// THE CONTROL. Without this leg, the test above could pass on an engine that dropped `blockers` for every
// `Blocked` row regardless of the field — the same class of false-negative #1794's control guarded against.
// Setting the FIELD (and nothing else) must make the edge appear, so the two legs together pin the field as
// the only thing that moves the answer.
//
// The blocker (`#77`) is put ON THE BOARD, resolved for free (`onBoard`, .github#1933's own read of
// `Scan.fs`), so this leg needs no extra REST route for it: its `PathRepo` differs from the subject's so
// `--repo FS.GG.SDD` excludes it from `candidates` (`scope`, keyed on `PathRepo`) while `onBoard` — keyed
// on the ISSUE ref, not the path repo — still resolves it.
[<Fact>]
let ``#1933 the SAME body, with the field set instead, yields the edge - the control`` () =
    use _sandbox = new Sandbox()
    Environment.SetEnvironmentVariable("FSGG_COORD_SCAN_TTL_SEC", "0")

    let row =
        { scopeRow "FS.GG.SDD" 99 with
            Status = BoardStatus.Blocked
            BlockedByRaw = "FS-GG/FS.GG.SDD#77" }

    let blocker =
        { scopeRow "FS.GG.SDD" 77 with
            State = IssueState.Open
            PathRepo = "FS.GG.SDD-blockers-only" }

    let transport =
        offBoardRoutes
            "[]"
            (fun _ -> "[]")
            """{"number":99,"state":"open","body":"Some park notes.\n\nBlocked by: FS-GG/FS.GG.SDD#77"}"""

    match Scan.snapshot transport [ row; blocker ] (Some "FS.GG.SDD") false None 120 with
    | Error e -> failwith $"a readable body and a set field must still produce a snapshot — got %A{e}"
    | Ok(document, _) ->
        use doc = System.Text.Json.JsonDocument.Parse(document: string)
        let item = doc.RootElement.GetProperty("items").[0]

        Assert.Equal(1, item.GetProperty("blockers").GetArrayLength())

// ---- .github#2450: the CLAIMED-ROW probe must cover 'In review' too, not just 'In progress' -------------
//
// `LIFECYCLE-PROJECTION-LAG`'s SECOND consumer (`Client.fs`'s lifecycle projector, in the Cli project)
// reads `Item.ItemPr` for a row that is ALREADY `In review` to decide whether it STAYS there. The probe at
// `Scan.fs:1298` used to fire only for `Status = InProgress`, so a claimed row already `In review` never had
// `ItemPr` populated — the projector then read a live claim with no PR fact and projected the row BACKWARD
// to `In progress`, on every reconcile pass, for as long as the review lasted. Same failure shape as
// `BLOCKER-CLEARED` at Scan.fs:1240-1246 (the section above): a probe bounded to its FIRST consumer's
// column left a SECOND, later consumer blind to its own subject.
//
// These tests stay inside this project's boundary and prove the fact the probe COLLECTS — exactly as the
// `#1738` tests above do for the `BLOCKER-CLEARED` arm — because the second consumer (`Client.fs`) lives in
// the Cli project, outside this item's declared `Paths:`.

/// A claimed board row already sitting `In review` — the exact column the probe used to skip.
let private claimedInReviewRow: Scan.Row =
    { scopeRow "FS.GG.SDD" 99 with Status = BoardStatus.InReview }

/// A transport for the claimed-row legs. Fresh node facts supply the body and a comment count of 1 (the
/// claim marker itself is the one comment); the marker read answers with a LIVE, WITHIN-LEASE claim; and
/// `pulls`/`matching-refs` are counted so a test can assert the probe fired exactly once, and no more.
let private claimedRowTransport (openPrs: string) =
    let mutable pullsReads = 0
    let mutable matchingRefsReads = 0

    let recorder =
        Fake.Recorder(fun (req: Request) ->
            match req.Path, req.Subject with
            | "graphql", "fresh issue body and comment-count facts" ->
                ok (nodeFactsResponse claimedInReviewRow.NodeId.Value "Paths: src/Board/**" 1)
            | path, _ when path.EndsWith "/comments" -> ok (liveMarker "curlew-8afd" 99)
            | path, _ when path.EndsWith "/issues" -> ok "[]"
            | path, _ when path.Contains "/pulls" ->
                pullsReads <- pullsReads + 1
                ok openPrs
            | path, _ when path.Contains "matching-refs" ->
                matchingRefsReads <- matchingRefsReads + 1
                ok "[]"
            | _, _ -> Error(Http(500, "unexpected request")))

    recorder, (fun () -> pullsReads), (fun () -> matchingRefsReads)

[<Fact>]
let ``#2450 a CLAIMED, OPEN row already 'In review' with an open item PR still populates ItemPr`` () =
    use _sandbox = new Sandbox()
    Environment.SetEnvironmentVariable("FSGG_COORD_SCAN_TTL_SEC", "0")

    let openPrs = """[{"number":1911,"head":{"ref":"item/99-already-written"}}]"""
    let transport, pullsReads, _ = claimedRowTransport openPrs

    match Scan.snapshot transport [ claimedInReviewRow ] (Some "FS.GG.SDD") false None 120 with
    | Error e -> failwith $"the scan must produce a snapshot — got %A{e}"
    | Ok(document, _) ->
        use doc = System.Text.Json.JsonDocument.Parse(document: string)
        let item = doc.RootElement.GetProperty("items").[0]

        Assert.Equal(1, pullsReads ())

        // THE FIELD IS POPULATED — the half .github#2450 was filed over. Before the fix this arm matched
        // `| Some _ -> ()` and `itemPr` never appeared, so the lifecycle projector read a live claim with
        // no PR fact and proposed `In progress` for a row that is legitimately `In review`.
        match item.TryGetProperty "itemPr" with
        | true, v -> Assert.Equal(1911, v.GetInt32())
        | false, _ ->
            failwith "an 'In review' claimed row with an open item PR must populate itemPr — this is .github#2450"

[<Fact>]
let ``#2450 a CLAIMED, OPEN row already 'In review' with NO open item PR is still probed - the mate`` () =
    // The mate to the test above, over the real writer — without it, "the probe reaches the second
    // consumer's column" is satisfied by a scan that always finds a PR, and a row that is genuinely
    // PR-less (an anomaly, but not one this probe should special-case) would go untested.
    use _sandbox = new Sandbox()
    Environment.SetEnvironmentVariable("FSGG_COORD_SCAN_TTL_SEC", "0")

    let transport, pullsReads, matchingRefsReads = claimedRowTransport "[]"

    match Scan.snapshot transport [ claimedInReviewRow ] (Some "FS.GG.SDD") false None 120 with
    | Error e -> failwith $"the scan must produce a snapshot — got %A{e}"
    | Ok(document, _) ->
        use doc = System.Text.Json.JsonDocument.Parse(document: string)
        let item = doc.RootElement.GetProperty("items").[0]

        Assert.Equal(1, pullsReads ())
        Assert.Equal(1, matchingRefsReads ())
        Assert.False(fst (item.TryGetProperty "itemPr"))

[<Fact>]
let ``#2450 a CLAIMED, OPEN 'Blocked' row is NOT probed by this arm - the widened probe buys no request it need not``
    ()
    =
    // THE BOUND, STATED AS A TEST. Widening from one column to two costs at most one additional REST
    // request per row that is claimed, Open, and currently `In review` — not one for every claimed row
    // regardless of column. A claimed row parked `Blocked` still falls to `| Some _ -> ()`, exactly as
    // before this fix: this arm is not `| Open, _ ->`.
    use _sandbox = new Sandbox()
    Environment.SetEnvironmentVariable("FSGG_COORD_SCAN_TTL_SEC", "0")

    let blockedRow = { claimedInReviewRow with Status = BoardStatus.Blocked }
    let transport, pullsReads, _ =
        claimedRowTransport """[{"number":1911,"head":{"ref":"item/99-already-written"}}]"""

    match Scan.snapshot transport [ blockedRow ] (Some "FS.GG.SDD") false None 120 with
    | Error e -> failwith $"the scan must produce a snapshot — got %A{e}"
    | Ok(document, _) ->
        use doc = System.Text.Json.JsonDocument.Parse(document: string)
        let item = doc.RootElement.GetProperty("items").[0]

        Assert.Equal(0, pullsReads ())
        Assert.False(fst (item.TryGetProperty "itemPr"))

// ---- .github#2384: the MARKERLESS-ROW probe must cover 'In review' too, not just 'Ready'/'Backlog' -----
//
// The mate to #2450 above, one column left of it. `LIFECYCLE-PROJECTION-LAG`'s projector
// (`LifecycleProjection.project` in `Client.fs`) reads `Item.ItemPr` the SAME way whether or not the row
// is claimed — only `Claim.Value` differs. The probe at `Scan.fs:1334` used to fire only for
// `Ready`/`Backlog`/cleared-`Blocked`, so an UNCLAIMED row already `In review` never had `ItemPr`
// populated: the projector then read no claim and no PR fact and fell through to its `Ready` default,
// flipping a row that is legitimately `In review` back to `Ready` on every reconcile pass — and because
// `Ready` IS probed, the very next pass finds the same open PR and flips forward again. This is
// `.github#2216`'s own repro: a `LIFECYCLE-PROJECTION-LAG` remedy oscillating `Ready` <-> `In review` with
// no claim and nothing about the issue or its PR changing in between.
//
// These tests stay inside this project's boundary and prove the fact the probe COLLECTS, exactly as the
// `#2450` tests above do for the claimed arm — the projector itself lives in the Cli project, outside this
// item's declared `Paths:`.

/// A markerless board row already sitting `In review` — the exact column the unclaimed probe used to skip.
let private unclaimedInReviewRow: Scan.Row =
    { scopeRow "FS.GG.SDD" 99 with Status = BoardStatus.InReview }

/// A transport for the markerless-row legs. `/comments` answers with NO claim marker at all (holder =
/// `None`), so these legs stay on the `| None ->` arm exactly as #2216's own repro was: an OPEN,
/// `In review` row nobody currently holds. `pulls`/`matching-refs` are counted so a test can assert the
/// probe fired exactly once, and no more.
let private unclaimedRowTransport (openPrs: string) =
    let mutable pullsReads = 0
    let mutable matchingRefsReads = 0

    let recorder =
        Fake.Recorder(fun (req: Request) ->
            match req.Path, req.Subject with
            | "graphql", "fresh issue body and comment-count facts" ->
                ok (nodeFactsResponse unclaimedInReviewRow.NodeId.Value "Paths: src/Board/**" 0)
            | path, _ when path.EndsWith "/comments" -> ok "[]"
            | path, _ when path.EndsWith "/issues" -> ok "[]"
            | path, _ when path.Contains "/pulls" ->
                pullsReads <- pullsReads + 1
                ok openPrs
            | path, _ when path.Contains "matching-refs" ->
                matchingRefsReads <- matchingRefsReads + 1
                ok "[]"
            | _, _ -> Error(Http(500, "unexpected request")))

    recorder, (fun () -> pullsReads), (fun () -> matchingRefsReads)

[<Fact>]
let ``#2384 an UNCLAIMED, OPEN row already 'In review' with an open item PR still populates ItemPr`` () =
    use _sandbox = new Sandbox()
    Environment.SetEnvironmentVariable("FSGG_COORD_SCAN_TTL_SEC", "0")

    let openPrs = """[{"number":2216,"head":{"ref":"item/99-already-written"}}]"""
    let transport, pullsReads, _ = unclaimedRowTransport openPrs

    match Scan.snapshot transport [ unclaimedInReviewRow ] (Some "FS.GG.SDD") false None 120 with
    | Error e -> failwith $"the scan must produce a snapshot — got %A{e}"
    | Ok(document, _) ->
        use doc = System.Text.Json.JsonDocument.Parse(document: string)
        let item = doc.RootElement.GetProperty("items").[0]

        Assert.Equal(1, pullsReads ())

        // THE FIELD IS POPULATED — the half .github#2384 was filed over. Before the fix this arm matched
        // `| _ -> ()` and `itemPr` never appeared, so the lifecycle projector read no claim and no PR fact
        // and proposed `Ready` for a row that is legitimately `In review`.
        match item.TryGetProperty "itemPr" with
        | true, v -> Assert.Equal(2216, v.GetInt32())
        | false, _ ->
            failwith "an 'In review' unclaimed row with an open item PR must populate itemPr — this is .github#2384"

[<Fact>]
let ``#2384 an UNCLAIMED, OPEN row already 'In review' with NO open item PR is still probed - the mate`` () =
    // The mate to the test above, over the real writer — without it, "the probe reaches the projector's
    // column" is satisfied by a scan that always finds a PR, and a row that is genuinely PR-less (an
    // anomaly, but not one this probe should special-case) would go untested.
    use _sandbox = new Sandbox()
    Environment.SetEnvironmentVariable("FSGG_COORD_SCAN_TTL_SEC", "0")

    let transport, pullsReads, matchingRefsReads = unclaimedRowTransport "[]"

    match Scan.snapshot transport [ unclaimedInReviewRow ] (Some "FS.GG.SDD") false None 120 with
    | Error e -> failwith $"the scan must produce a snapshot — got %A{e}"
    | Ok(document, _) ->
        use doc = System.Text.Json.JsonDocument.Parse(document: string)
        let item = doc.RootElement.GetProperty("items").[0]

        Assert.Equal(1, pullsReads ())
        Assert.Equal(1, matchingRefsReads ())
        Assert.False(fst (item.TryGetProperty "itemPr"))

[<Fact>]
let ``#2384 an UNCLAIMED, OPEN 'Blocked' row with an unresolved blocker is NOT probed by this arm - the widened probe buys no request it need not``
    ()
    =
    // THE BOUND, STATED AS A TEST. Widening from three columns to four costs at most one additional REST
    // request per row that is unclaimed, Open, and currently `In review` — not one for every unclaimed row
    // regardless of column. An unclaimed row parked `Blocked` with an unresolved blocker still falls to
    // `| _ -> ()`, exactly as before this fix: this arm is not `| Open, _ ->`.
    use _sandbox = new Sandbox()
    Environment.SetEnvironmentVariable("FSGG_COORD_SCAN_TTL_SEC", "0")

    // `BlockedByRaw = ""` keeps `Blockers.cleared` false (it requires at least one blocker, every one
    // resolved), so this is a `Blocked` row with no cleared blocker to promote — the population the
    // existing `Blocked` arm already declines, unaffected by this fix.
    let blockedRow =
        { unclaimedInReviewRow with
            Status = BoardStatus.Blocked
            BlockedByRaw = "" }

    let transport, pullsReads, _ =
        unclaimedRowTransport """[{"number":2216,"head":{"ref":"item/99-already-written"}}]"""

    match Scan.snapshot transport [ blockedRow ] (Some "FS.GG.SDD") false None 120 with
    | Error e -> failwith $"the scan must produce a snapshot — got %A{e}"
    | Ok(document, _) ->
        use doc = System.Text.Json.JsonDocument.Parse(document: string)
        let item = doc.RootElement.GetProperty("items").[0]

        Assert.Equal(0, pullsReads ())
        Assert.False(fst (item.TryGetProperty "itemPr"))
