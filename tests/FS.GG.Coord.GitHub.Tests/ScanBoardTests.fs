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
          NextLink = None }

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
