module FS.GG.Coord.GitHub.Tests.ReadTests

open Xunit
open FS.GG.Coord
open FS.GG.Coord.Types
open FS.GG.Coord.GitHub
open FS.GG.Coord.GitHub.Errors
open FS.GG.Coord.GitHub.Transport

/// A transport that answers every request with one canned body.
let private serving (body: string) =
    Fake.Recorder(fun _ ->
        Ok
            { Status = 200
              Body = body
              ETag = None
              NextLink = None; Headers = Map.empty })

/// A transport that fails every request.
let private failing (error: IoError) = Fake.Recorder(fun _ -> Error error)

[<Fact>]
let ``#2134 duplicate candidates retain both closed issues and PRs`` () =
    let transport = serving """[{"number":7,"state":"closed","title":"same","body":"x"},{"number":8,"state":"open","title":"same","body":"y","pull_request":{}}]"""
    match Reads.duplicateCandidates transport "FS-GG" ".github" with
    | Ok candidates ->
        Assert.Equal<int list>([ 7; 8 ], candidates |> List.map _.Number)
        Assert.False(candidates[0].IsPullRequest)
        Assert.True(candidates[1].IsPullRequest)
    | Error e -> failwithf "candidate inventory failed: %A" e

[<Fact>]
let ``#2134 duplicate inventory refuses an unmerged continuation page`` () =
    // A TRANSPORT THAT DID NOT MERGE. One page of a set that has more, handed straight back — nought
    // elements behind a `rel="next"` link that promises a hundred ahead of it. `HttpTransport` cannot
    // produce this (it follows the link and concatenates), which is exactly why the guard must be stated
    // against the SHAPE rather than against the flag: see the `.github#2735` block below for what
    // happened when the flag alone was read as the answer.
    let transport = Fake.Recorder(fun _ -> Ok { Status = 200; Body = "[]"; ETag = None; NextLink = Some "https://api.github.test/page=2"; Headers = Map.empty })
    match Reads.duplicateCandidates transport "FS-GG" ".github" with
    | Error(Malformed(_, detail)) -> Assert.Contains("incomplete", detail)
    | other -> failwithf "an incomplete inventory must refuse: %A" other

// ---- .github#2735: the duplicate inventory over a listing that PAGINATES ----------------------------
//
// `intake apply` — the only filing path that validates its input before creating anything — could not
// file in `FS-GG/.github` at all. Every run died at the duplicate-inventory read, permanently, and the
// refusal was correct in form and wrong in fact: the inventory it refused was COMPLETE.
//
// `Transport.Send` follows `Link: rel="next"` and concatenates the pages. What it does NOT do is clear
// `NextLink` on the merged response — `follow` rebinds only `Body` and `ETag`, so the response a caller
// receives after a three-page merge still carries PAGE ONE's link (`Transport.fs`, `follow ... { acc
// with Body = merged; ETag = None }`). That is deliberate and load-bearing elsewhere: `memoisable`
// reads `NextLink.IsSome` as *this collection paginated, so its ETag may not be stored*, which is a
// question about history and is answered correctly. `duplicateCandidates` read the same flag as *there
// are pages I have not fetched*, which is a question about the future, and the same bit cannot answer
// both. In `FS-GG/.github`, whose issue listing always paginates, the misreading made the refusal
// unconditional and permanent.
//
// WHY NO `Fake.Recorder` TEST COULD HAVE CAUGHT THIS, AND WHY THAT IS THE POINT. `Fake.Recorder`
// implements `IGitHubTransport` DIRECTLY, and `Transport.Send` — the component that merges — sits BEHIND
// that interface. A recorder can therefore only hand this reader a `Response` it invented, and #2134's
// recorder above invented one (`[]` + a link) that the shipping adapter never produces. It asserted a
// refusal, the refusal happened, and the test went green over a read that could not work. The instrument
// was not wrong about what it measured; it was structurally incapable of measuring the thing that broke.
// So these tests drive the REAL `HttpTransport` over real HTTP, against real `Link` headers, which is
// the only arrangement in which the boundary is genuinely crossed.

/// A loopback HTTP server: a free port, a settable handler, and the request log the assertions read.
type private Loopback() =
    let listener = new System.Net.HttpListener()

    // Port 0 is not available to HttpListener, so take a free one from the OS and hand it back.
    let port =
        use probe = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0)
        probe.Start()
        let p = (probe.LocalEndpoint :?> System.Net.IPEndPoint).Port
        probe.Stop()
        p

    let prefix = $"http://127.0.0.1:%d{port}/"
    let seen = System.Collections.Generic.List<string>()
    let mutable handler: System.Net.HttpListenerRequest -> System.Net.HttpListenerResponse -> unit = fun _ _ -> ()

    do
        listener.Prefixes.Add prefix
        listener.Start()

        let loop () =
            while listener.IsListening do
                try
                    let ctx = listener.GetContext()
                    lock seen (fun () -> seen.Add ctx.Request.Url.PathAndQuery)
                    handler ctx.Request ctx.Response

                    try
                        ctx.Response.Close()
                    with _ ->
                        ()
                with _ ->
                    ()

        let t = System.Threading.Thread(loop)
        t.IsBackground <- true
        t.Start()

    member _.Base = prefix.TrimEnd('/')
    member _.Requests = lock seen (fun () -> List.ofSeq seen)
    member _.On(f) = handler <- f

    member _.Send (response: System.Net.HttpListenerResponse) (status: int) (body: string) (headers: (string * string) list) =
        response.StatusCode <- status
        response.ContentType <- "application/json"

        for (k, v) in headers do
            response.Headers.Add(k, v)

        let bytes = System.Text.Encoding.UTF8.GetBytes body
        response.ContentLength64 <- int64 bytes.Length
        response.OutputStream.Write(bytes, 0, bytes.Length)

    interface System.IDisposable with
        member _.Dispose() =
            try
                listener.Stop()
                (listener :> System.IDisposable).Dispose()
            with _ ->
                ()

/// One page of the `issues` listing: the fields `duplicateCandidates` actually reads, for each number.
let private issuePage (numbers: int list) =
    numbers
    |> List.map (fun n -> $"""{{"number":%d{n},"state":"open","title":"issue %d{n}","body":"body %d{n}"}}""")
    |> String.concat ","
    |> fun elements -> "[" + elements + "]"

/// `Reads.CollectionPageSize` — the page size this read requests AND the length the completeness guard
/// compares the merged body against. It is private to `Reads`, so it is restated here, and the dedicated
/// wire test below is what keeps the restatement honest.
let private pageOneSize = 100

/// The query parameters of ONE recorded request, split at `&` and `=` and unescaped — an exact map, not a
/// substring haystack.
///
/// `Assert.Contains` over the raw query string was the first cut, and it leaked in the one direction that
/// matters (.github#2735, review round 1): `"per_page=1000"` CONTAINS `"per_page=100"`, and
/// `"state=allx"` CONTAINS `"state=all"`. So a containment assertion closes only the DOWNWARD drift and
/// leaves the fail-open one — a page size LARGER than the constant the guard compares against — wide
/// open. Containment is the wrong oracle for a fault whose whole shape is a value that EXTENDS the
/// expected one; equality on the parsed value is the right one.
let private queryOf (pathAndQuery: string) =
    match pathAndQuery.IndexOf '?' with
    | -1 -> Map.empty
    | mark ->
        pathAndQuery.Substring(mark + 1).Split('&')
        |> Array.filter (System.String.IsNullOrEmpty >> not)
        |> Array.map (fun pair ->
            match pair.IndexOf '=' with
            | -1 -> System.Uri.UnescapeDataString pair, ""
            | split ->
                System.Uri.UnescapeDataString(pair.Substring(0, split)),
                System.Uri.UnescapeDataString(pair.Substring(split + 1)))
        |> Map.ofArray

/// `FS-GG/.github`'s shape, in miniature: a FULL first page (`pageOneSize`, the `per_page` this read asks
/// for) with a `rel="next"` link, and a short continuation. Page two is what each test injects its fault
/// into — the status and body of the ONLY response that differs between the green leg and the red ones.
let private paginatingListing (server: Loopback) (pageTwoStatus: int) (pageTwoBody: string) =
    server.On(fun req res ->
        if req.Url.PathAndQuery.Contains "page=2" then
            server.Send res pageTwoStatus pageTwoBody []
        else
            server.Send
                res
                200
                (issuePage [ 1..pageOneSize ])
                [ "Link",
                  $"<%s{server.Base}/repos/FS-GG/.github/issues?state=all&per_page=%d{pageOneSize}&page=2>; rel=\"next\", "
                  + $"<%s{server.Base}/repos/FS-GG/.github/issues?state=all&per_page=%d{pageOneSize}&page=2>; rel=\"last\"" ])

[<Fact>]
let ``.github#2735 the duplicate inventory MERGES the continuation page`` () =
    // THE REGRESSION. Both pages are served, the adapter merges them, and the answer must be the WHOLE
    // set — including #101..#103, which exist only on page two and are precisely the rows a duplicate
    // check would otherwise miss. Against the unrepaired reader this reds with `Malformed(..., "the
    // transport returned an unmerged next page")` even though 103 candidates were fetched and merged.
    use server = new Loopback()
    paginatingListing server 200 (issuePage [ 101..103 ])

    use transport = new HttpTransport(server.Base, "t")

    match Reads.duplicateCandidates (transport :> IGitHubTransport) "FS-GG" ".github" with
    | Ok candidates ->
        Assert.Equal(pageOneSize + 3, List.length candidates)
        Assert.Contains(candidates, fun c -> c.Number = 1)
        Assert.Contains(candidates, fun c -> c.Number = pageOneSize + 3)
        // Two round trips really happened: the answer is a merge, not a first page that looked long.
        Assert.Equal(2, List.length server.Requests)
    | Error e -> failwithf "a listing that paginates must yield a COMPLETE inventory, not a refusal: %A" e

[<Fact>]
let ``.github#2735 the duplicate inventory REQUESTS state=all at exactly the page size its guard compares against`` () =
    // THE WIRE IS ITS OWN GATE, UNDER ITS OWN NAME, and that separation is half the repair (.github#2735,
    // review round 1). These assertions used to ride along inside the merge test above, where a `per_page`
    // drift reddened a test titled `... MERGES the continuation page` — a red whose NAME describes a
    // different property from the mutation that produced it. That is not a bookkeeping nicety: a mutation
    // credited to a red that some OTHER assertion raised for some OTHER reason has not been shown to have
    // a gate at all, and two escapes went through exactly there. So the rule this file now follows is that
    // every mutation must red a test whose TITLE describes that mutation, and the wire contract gets the
    // title of this one.
    //
    // TWO PARAMETERS, BOTH LOAD-BEARING.
    //   `per_page` — the completeness guard reads "a merged body carries MORE than `CollectionPageSize`
    //   elements", and that argument holds only while the request asked for exactly that many. A LARGER
    //   `per_page` is the fail-OPEN direction: an unmerged first page would already clear the comparison,
    //   and a truncated inventory would be answered rather than refused.
    //   `state=all` — the `.fsi` calls this an ALL-STATE inventory and #2134 pins closed issues AND pull
    //   requests as candidates. Narrowed to open issues, this read answers a duplicate check from a
    //   partial set and does not know it, which is the exact class this row exists to end. Before this
    //   test, nothing in the suite pinned it: #2134's own fixture is a `Fake.Recorder`, which never sees a
    //   query at all.
    //
    // THE ASSERTION IS EQUALITY OVER THE WHOLE PARAMETER MAP, not containment over a chosen subset. Both
    // halves of that matter. Containment is what leaked (`queryOf` above says how); and asserting the
    // whole map rather than two hand-picked keys means the predicate is not the author's own guess at
    // which parameters are worth watching — a parameter added, dropped or renamed reds here too.
    //
    // NOTHING ABOUT THE RESPONSE IS ASSERTED, deliberately. A second oracle here would give this gate a
    // second reason to red, and a gate that can red for two reasons is precisely what both round-1
    // findings were made of.
    use server = new Loopback()
    server.On(fun _ res -> server.Send res 200 "[]" [])

    use transport = new HttpTransport(server.Base, "t")
    Reads.duplicateCandidates (transport :> IGitHubTransport) "FS-GG" ".github" |> ignore

    Assert.Equal(1, List.length server.Requests)

    Assert.Equal<Map<string, string>>(
        Map.ofList [ "state", "all"; "per_page", string pageOneSize ],
        queryOf (List.head server.Requests)
    )

[<Fact>]
let ``.github#2735 a continuation page that is NOT AN ARRAY is a failed read, not a short inventory`` () =
    // FAULT INJECTION, and the control is the test above: same server, same first page, same link — the
    // only difference is page two's BODY. A gateway error page, a truncated response, a proxy's HTML: the
    // inventory cannot be completed, so it must refuse rather than answer with page one's 100 rows.
    use server = new Loopback()
    paginatingListing server 200 """{"message":"Bad gateway"}"""

    use transport = new HttpTransport(server.Base, "t")

    match Reads.duplicateCandidates (transport :> IGitHubTransport) "FS-GG" ".github" with
    | Error(Malformed(_, detail)) -> Assert.Contains("not a JSON array", detail)
    | other -> failwithf "an inventory that could not be completed must refuse: %A" other

[<Fact>]
let ``.github#2735 a continuation page that ERRORS is a failed read, not a short inventory`` () =
    // The other half of the same fault: page two answers, and answers 500. Nothing about page one's 100
    // rows becomes trustworthy because the rest of the set was unreachable.
    //
    // PAGE TWO'S BODY IS A PERFECTLY VALID ARRAY, and that is the repair (.github#2735, review round 1).
    // It used to be `{"message":"Server Error"}` — not a JSON array — so the read failed at the MERGE no
    // matter what status accompanied it, and flipping the 500 to a 200 left this test GREEN. The gate was
    // named for the status and did not exercise it: it was a second copy of the not-an-array test above,
    // with a decorative 500 on the fixture. Serving a body that would merge without complaint makes the
    // STATUS the only fault present, which is what the title claims is under test.
    //
    // AND THE ORACLE IS THE SPECIFIC ERROR THAT PATH PRODUCES, not `Error _`. `Error _` cannot tell "page
    // two errored" from "page two was malformed", so it was structurally unable to notice that its two
    // faults had collapsed into one. `sendOne` classifies a non-2xx that is not a rate limit as
    // `Http(status, body)` and `follow` propagates it unchanged, so the status this fixture answered has
    // to come back out the other end for the test to pass.
    use server = new Loopback()
    paginatingListing server 500 (issuePage [ 101..103 ])

    use transport = new HttpTransport(server.Base, "t")

    match Reads.duplicateCandidates (transport :> IGitHubTransport) "FS-GG" ".github" with
    | Error(Http(500, _)) -> ()
    | other ->
        failwithf "an unreachable continuation page must refuse with the status it answered: %A" other

[<Fact>]
let ``.github#2735 a listing that genuinely has nothing reads as an EMPTY inventory, not a failure`` () =
    // THE DISCRIMINATION THE REFUSAL EXISTS TO MAKE. "I could not read the inventory" and "I read the
    // inventory and it is empty" are different answers, and a repair that collapsed them in either
    // direction would be the defect. One page, no `rel="next"`, nothing in it: `Ok []`.
    use server = new Loopback()
    server.On(fun _ res -> server.Send res 200 "[]" [])

    use transport = new HttpTransport(server.Base, "t")

    match Reads.duplicateCandidates (transport :> IGitHubTransport) "FS-GG" ".github" with
    | Ok [] -> Assert.Equal(1, List.length server.Requests)
    | other -> failwithf "an empty listing is an empty inventory: %A" other

/// A transport for `prAlive`'s TWO reads (#1055): the open-PR list, then — when no PR matches — the
/// `git/matching-refs/heads/item/<n>-` branch probe. `pulls` answers the first, `refs` the second.
let private prAndRefs (pulls: string) (refs: string) =
    Fake.Recorder(fun (req: Request) ->
        let body =
            if req.Path.Contains "matching-refs" then refs
            elif req.Path.EndsWith "/pulls" then pulls
            else "[]"

        Ok
            { Status = 200
              Body = body
              ETag = None
              NextLink = None; Headers = Map.empty })

/// `prAlive` finds no PR, then the branch probe itself FAILS — the #1055 fail-closed case. The open-PR
/// read succeeds (empty), the `matching-refs` read errors.
let private prNoneRefsFail (error: IoError) =
    Fake.Recorder(fun (req: Request) ->
        if req.Path.Contains "matching-refs" then
            Error error
        else
            Ok
                { Status = 200
                  Body = "[]"
                  ETag = None
                  NextLink = None; Headers = Map.empty })

// ---- #461: the lock is never guessed at ------------------------------------------------------------

[<Fact>]
let ``#461 a MALFORMED comments page is a failed read, NOT an empty lock`` () =
    // THE FOUNDING INCIDENT OF THIS LAYER. The claim-candidate read came back as bytes that are not JSON —
    // a truncated page, a proxy error body, a 5xx rendered as text — and `gh` EXITED 0. `$cand` was the
    // empty string, `jq 'length'` printed nothing AND exited 0 (so `set -euo pipefail` never fired), the
    // loop body never ran, and `active_claims` returned `[]`.
    //
    // A failed read wearing an empty set's clothes. And `[]` is a CLAIM — it says "I read the locks and
    // nobody holds anything." A failed scan is not entitled to make it.
    let recorder = serving "<html>502 Bad Gateway</html>"

    match Reads.markerScan recorder "FS-GG" "FS.GG.SDD" 42 with
    | Error(Malformed _) -> ()
    | Ok scan ->
        failwith $"a malformed page must NEVER read as an empty lock — got %d{List.length scan.Markers} marker(s)"
    | Error other -> failwith $"expected Malformed — got %A{other}"

[<Fact>]
let ``#461 ...and an EMPTY body is a failed read too, not an unheld item`` () =
    let recorder = serving ""

    match Reads.markerScan recorder "FS-GG" "FS.GG.SDD" 42 with
    | Error(Malformed _) -> ()
    | other -> failwith $"an empty body is not an empty result — got %A{other}"

[<Fact>]
let ``#461 the guard must NOT fire on a legitimately empty comment list`` () =
    // The counterweight, and it is as important as the guard. A real, successful scan that found no markers
    // is a valid answer — the item is genuinely free. A fail-closed rule that also refuses the good path
    // would deadlock the board.
    let recorder = serving "[]"

    match Reads.markerScan recorder "FS-GG" "FS.GG.SDD" 42 with
    | Ok { Markers = []; Unreadable = [] } -> ()
    | other -> failwith $"a successful scan with no markers is an empty set — got %A{other}"

[<Fact>]
let ``the marker read is NEVER conditional - a 304 could hide a live lock`` () =
    let recorder = serving "[]"
    Reads.markerScan recorder "FS-GG" "FS.GG.SDD" 42 |> ignore

    // **A lock may never be read from a cache.** A 304 serving a body captured before the marker was posted
    // would report zero comments over a live claim. Going direct means there is no ETag to be stale.
    Assert.True(recorder.Logged "comment-list FS-GG/FS.GG.SDD 42")
    Assert.Equal(0, recorder.GraphQlCalls)
    Assert.Equal(1, recorder.RestCalls)

// ---- the marker grammar ----------------------------------------------------------------------------

let private comment (id: int) (body: string) (updatedAt: string) =
    let escaped = body.Replace("\"", "\\\"")
    $"""{{"id":%d{id},"body":"%s{escaped}","updated_at":"%s{updatedAt}"}}"""

let private now = System.DateTimeOffset.UtcNow.ToString("o")

[<Fact>]
let ``a marker is ANCHORED - a say message that QUOTES one cannot forge a lock`` () =
    // Un-anchor the pattern and any free-form `say` message whose text merely mentions
    // `<!-- fsgg:claim worker=ghost -->` takes the lock on the item it was posted to. This is a security
    // property, not a style one.
    let forgery =
        "I tried to claim it but saw <!-- fsgg:claim worker=ghost --> already there"

    let recorder = serving $"[{comment 901 forgery now}]"

    match Reads.markerScan recorder "FS-GG" "FS.GG.SDD" 42 with
    | Ok { Markers = []; Unreadable = [] } -> ()
    | other -> failwith $"a quoted marker is not a marker — got %A{other}"

[<Fact>]
let ``a marker we cannot parse a WORKER out of is held by nobody - and it BLOCKS`` () =
    // A half-written lock must fail CLOSED. If an unparseable marker vanished, the item would read as free
    // and a second worker would be handed it — which is the one thing a lock exists to prevent. So it
    // becomes a claim held by `unparsed-marker`: nobody can heartbeat it, nobody can release it by name,
    // and it holds the item until somebody reaps it deliberately.
    let recorder =
        serving $"""[{comment 901 "<!-- fsgg:claim lease=120 -->" now}]"""

    match Reads.markerScan recorder "FS-GG" "FS.GG.SDD" 42 with
    | Ok { Markers = [ m ]; Unreadable = [] } -> Assert.Equal(WorkerId "unparsed-marker", m.Worker)
    | other -> failwith $"an unparseable marker must still block — got %A{other}"

[<Fact>]
let ``the marker's prev= column is decoded, and %% comes out LAST`` () =
    // `enc_status` encodes `%` FIRST, so it must be decoded LAST — otherwise a status containing a literal
    // `%20` decodes into a space that was never there. It is the classic escaping-order bug, and the board
    // column it corrupts is the one `release` puts back (#481).
    let body =
        "<!-- fsgg:claim worker=vole-418 lease=120 prev=In%20progress -->"

    let recorder = serving $"[{comment 901 body now}]"

    match Reads.markerScan recorder "FS-GG" "FS.GG.SDD" 42 with
    | Ok { Markers = [ m ]; Unreadable = [] } -> Assert.Equal(Some InProgress, m.PreviousStatus)
    | other -> failwith $"the previous column must be recovered — got %A{other}"

[<Fact>]
let ``#1732 a marker carries path scope while legacy markers remain readable`` () =
    let scoped =
        "<!-- fsgg:claim worker=vole-418 lease=120 pathRepo=FS.GG.Rendering -->"

    let legacy =
        "<!-- fsgg:claim worker=kite-461 lease=120 -->"

    let recorder = serving $"[{comment 901 scoped now},{comment 902 legacy now}]"

    match Reads.markerScan recorder "FS-GG" ".github" 1732 with
    | Ok { Markers = [ first; second ]; Unreadable = [] } ->
        Assert.Equal(Some "FS.GG.Rendering", first.PathRepo)
        Assert.Equal(None, second.PathRepo)
    | other -> failwith $"both new and legacy markers must parse — got %A{other}"

// ---- the CAS's total order -------------------------------------------------------------------------

[<Fact>]
let ``the CAS winner is the LOWEST LIVE comment id`` () =
    // GitHub issues comment ids from ONE server-side sequence, so "lowest id wins" is a total order that
    // every racer observes identically. That is what makes this a real compare-and-swap with a real
    // linearisation point, rather than a hopeful convention — and ADR-0040 C4 keeps it exactly as it is.
    let markers =
        [ { Reads.Id = 903L
            Reads.Worker = WorkerId "late"
            Reads.Session = None
            Reads.AgeSeconds = 10
            Reads.PreviousStatus = None
            Reads.PathRepo = None
            Reads.Raw = "" }
          { Reads.Id = 901L
            Reads.Worker = WorkerId "first"
            Reads.Session = None
            Reads.AgeSeconds = 10
            Reads.PreviousStatus = None
            Reads.PathRepo = None
            Reads.Raw = "" } ]

    match Reads.winner 120 markers with
    | Some m -> Assert.Equal(WorkerId "first", m.Worker)
    | None -> failwith "a live marker must win"

[<Fact>]
let ``a STALE marker does not win - but an unreadable AGE is not stale`` () =
    // A negative age means we could not read the marker's timestamp. Reading that as an EXPIRED lease would
    // reap a live claim on the strength of a field we failed to parse — a failed read deciding a lock,
    // which is the exact substitution this layer exists to make impossible.
    let stale =
        { Reads.Id = 901L
          Reads.Worker = WorkerId "dead"
          Reads.Session = None
          Reads.AgeSeconds = 99999
          Reads.PreviousStatus = None
          Reads.PathRepo = None
          Reads.Raw = "" }

    let ageUnknown =
        { stale with
            Reads.Id = 902L
            Reads.Worker = WorkerId "unknown-age"
            Reads.AgeSeconds = -1 }

    Assert.True(Reads.isStale 120 stale)
    Assert.False(Reads.isStale 120 ageUnknown)

    match Reads.winner 120 [ stale; ageUnknown ] with
    | Some m -> Assert.Equal(WorkerId "unknown-age", m.Worker)
    | None -> failwith "the marker whose age we could not read still holds the item"

// ---- #476: MERGED is not CLOSED --------------------------------------------------------------------

[<Fact>]
let ``#476 a MERGED pull request resolves as BlockerMerged, not BlockerClosed`` () =
    // An issue's state is OPEN | CLOSED. A PR's is OPEN | CLOSED | **MERGED**. A rule that clears a blocker
    // only on CLOSED therefore unblocks when the blocking PR is ABANDONED and blocks forever once it is
    // FINISHED — the gate opens precisely when the work is thrown away, and shuts precisely when it is
    // done.
    let recorder =
        serving """{"state":"closed","pull_request":{"merged_at":"2026-07-14T10:00:00Z"}}"""

    match Reads.blockerState recorder "FS-GG" "FS.GG.SDD" 8 with
    | Ok BlockerMerged -> ()
    | other -> failwith $"a merged PR must resolve as MERGED — got %A{other}"

[<Fact>]
let ``#476 ...and an ABANDONED pull request is BlockerClosed`` () =
    let recorder =
        serving """{"state":"closed","pull_request":{"merged_at":null}}"""

    match Reads.blockerState recorder "FS-GG" "FS.GG.SDD" 8 with
    | Ok BlockerClosed -> ()
    | other -> failwith $"a closed-unmerged PR is CLOSED — got %A{other}"

[<Fact>]
let ``a blocker we could not READ is Unknown - and Unknown BLOCKS`` () =
    // "I could not look" is not "I looked and it is fine" (#266, #421). The safe direction on a lock is
    // always to hold it — an unresolvable blocker keeps the item blocked and says so.
    let recorder = failing (Transport "connection reset")

    match Reads.blockerState recorder "FS-GG" "FS.GG.SDD" 8 with
    | Ok BlockerUnknown -> ()
    | other -> failwith $"an unreadable blocker must block — got %A{other}"

[<Fact>]
let ``one unreadable blocker does not STARVE the board`` () =
    // A 502 on ONE issue is local to that issue. The item it blocks stays blocked and explains itself,
    // while every other item on the board is still schedulable. Failing the whole scan on one bad ref would
    // be fail-closed in the wrong place — it would turn one unreachable issue into a dead queue.
    let recorder = failing (Http(500, "boom"))
    let result = Reads.blockerState recorder "FS-GG" "FS.GG.SDD" 8
    Assert.True(Result.isOk result)

[<Fact>]
let ``#534 an EXHAUSTED BUDGET is NOT degraded to 'blocker unknown' - it is propagated`` () =
    // THE DISTINCTION THE ARM ABOVE DEPENDS ON, AND THE BUG THIS FILE ALMOST SHIPPED.
    //
    // "One unreadable blocker must not starve the board" is right for a TRANSIENT — a 502 on one issue. It
    // is catastrophically wrong for a RATE LIMIT, because a rate limit is not a fact about this ref: it is
    // a fact about the CLIENT, and the very next resolution fails identically.
    //
    // Degrade it, and EVERY blocker on the board resolves `Unknown`; every `Unknown` blocks; the tool
    // reports "nothing schedulable" over a full queue and exits **0**. That is #534 (the budget-exhausted
    // message swallowed, the worker told there is nothing to do) wearing #421's clothes (a budget failure
    // reported as a fact about an item) — and the caller would never back off, because it was never told
    // to.
    let recorder = failing (RateLimited(UnknownBudget, None))

    match Reads.blockerState recorder "FS-GG" "FS.GG.SDD" 8 with
    | Error(RateLimited _) -> ()
    | other -> failwith $"an exhausted budget must not masquerade as an unresolvable blocker — got %A{other}"

[<Fact>]
let ``#534 ...and prAlive propagates it too - reap must not decide liveness on a read it cannot make`` () =
    let recorder = failing (RateLimited(UnknownBudget, None))

    match Reads.prAlive recorder "FS-GG" "FS.GG.SDD" 42 with
    | Error(RateLimited _) -> ()
    | other -> failwith $"an exhausted budget must not masquerade as unknown liveness — got %A{other}"

// ---- #581: the lease is not the life ---------------------------------------------------------------

[<Fact>]
let ``#581 an OPEN item PR is proof of life - the lease lapsed, the WORK did not`` () =
    // Lease expiry is EVIDENCE of abandonment, never PROOF, and its false positive is systematic: work that
    // simply takes longer than the lease. An open PR on the item's own `item/<n>-*` branch is the worktree
    // protocol's own artifact and is server-side proof that the worker is still there.
    let recorder =
        serving """[{"number":77,"head":{"ref":"item/42-the-thing"}}]"""

    match Reads.prAlive recorder "FS-GG" "FS.GG.SDD" 42 with
    | Ok(LeaseExpiredPrOpen 77) -> ()
    | other -> failwith $"an open item PR is proof of life — got %A{other}"

[<Fact>]
let ``#581 a PR on ANOTHER item's branch is not proof of life for this one`` () =
    // No PR matches item 42, and no `item/42-*` branch is pushed either (#1055) — so this is a genuinely
    // dead claim, `LeaseExpiredNoPr`.
    let recorder =
        prAndRefs """[{"number":77,"head":{"ref":"item/99-something-else"}}]""" "[]"

    match Reads.prAlive recorder "FS-GG" "FS.GG.SDD" 42 with
    | Ok LeaseExpiredNoPr -> ()
    | other -> failwith $"another item's PR AND no branch says nothing about this one — got %A{other}"

[<Fact>]
let ``#1055 a pushed item branch with NO PR is proof of life - LeaseExpiredBranchPushed`` () =
    // §5 opens the PR only AFTER the work, so a worker in §3 has a pushed `item/42-*` branch and no PR yet.
    // A REST outage can expire the lease in that window (heartbeat is REST too), and reap must NOT collect a
    // worker who is visibly still there. The branch is the proof.
    let recorder =
        prAndRefs "[]" """[{"ref":"refs/heads/item/42-wip","object":{"sha":"abc123"}}]"""

    match Reads.prAlive recorder "FS-GG" "FS.GG.SDD" 42 with
    | Ok LeaseExpiredBranchPushed -> ()
    | other -> failwith $"a pushed item branch with no PR is proof of life — got %A{other}"

[<Fact>]
let ``#1055 no PR and NO branch is a genuinely dead claim - LeaseExpiredNoPr`` () =
    let recorder = prAndRefs "[]" "[]"

    match Reads.prAlive recorder "FS-GG" "FS.GG.SDD" 42 with
    | Ok LeaseExpiredNoPr -> ()
    | other -> failwith $"no PR and no branch is a dead claim — got %A{other}"

[<Fact>]
let ``#1055 the branch probe FAILS CLOSED - an unreadable probe is Unknown, never 'no branch'`` () =
    // The whole point of #1055 is that the REST outage that expired the lease is the LIKELY reason the branch
    // probe also fails — so a failed probe must be `LivenessUnknown` (reap refuses), never `LeaseExpiredNoPr`
    // (reap collects). Same #266/#581 rule the open-PR read already obeys, one read over.
    let recorder = prNoneRefsFail (Http(502, "bad gateway"))

    match Reads.prAlive recorder "FS-GG" "FS.GG.SDD" 42 with
    | Ok LivenessUnknown -> ()
    | other -> failwith $"an unreadable branch probe must not read as 'no branch' — got %A{other}"

[<Fact>]
let ``#1055 the branch probe propagates a RATE LIMIT - not swallowed as Unknown`` () =
    // A rate limit is a fact about the CLIENT (EX_RATE), not this item — the caller must back off, not go on
    // deciding liveness from a read it cannot make. The open-PR read propagates it; so must the branch probe.
    let recorder = prNoneRefsFail (RateLimited(RestBudget(Some "core"), None))

    match Reads.prAlive recorder "FS-GG" "FS.GG.SDD" 42 with
    | Error(RateLimited _) -> ()
    | other -> failwith $"an exhausted budget on the branch probe must propagate — got %A{other}"

[<Fact>]
let ``#581 a FAILED pr read is Unknown, NOT 'no PR' - this is what reaped live work`` () =
    // The distinction that stops a transient 5xx from collecting the claim of a worker who is visibly,
    // demonstrably still working. `LivenessUnknown` and `LeaseExpiredNoPr` are different facts, and only
    // one of them licenses a reap.
    let recorder = failing (Http(502, "bad gateway"))

    match Reads.prAlive recorder "FS-GG" "FS.GG.SDD" 42 with
    | Ok LivenessUnknown -> ()
    | other -> failwith $"an unreadable PR list must not read as 'no PR' — got %A{other}"

// ---- #641: a pull request is not an issue ----------------------------------------------------------

[<Fact>]
let ``#641 the open-issue scan EXCLUDES pull requests`` () =
    // A PR is an issue in REST, and it is not an item of work. `fsgg-coord issues` listed PRs as issues, so
    // the duplicate-check read a PR as "already filed" and silently suppressed a real finding.
    let recorder =
        serving
            """[{"number":42,"body":"Paths: src/**"},
                {"number":43,"body":"a PR","pull_request":{"url":"https://api.github.com/pulls/43"}}]"""

    match Reads.openIssues recorder "FS-GG" "FS.GG.SDD" with
    | Ok issues ->
        Assert.Equal(1, List.length issues)
        Assert.Equal(42, issues.[0].Number)
    | other -> failwith $"a PR is not an issue — got %A{other}"

[<Fact>]
let ``#461 a malformed issue list is an error, not an empty candidate set`` () =
    let recorder = serving "not json at all"

    match Reads.openIssues recorder "FS-GG" "FS.GG.SDD" with
    | Error(Malformed _) -> ()
    | other -> failwith $"an unreadable issue list must refuse — got %A{other}"

// ---- .github#1794: the open-issue scan manufactures NO answer out of what it could not read ---------
//
// This read is the #353 collision gate's candidate set (.github#1779) and the scheduler's off-board sweep.
// Both fail OPEN on a wrong answer here — a row that "declares nothing" collides with nobody, and there is
// no CAS on a file — so every leg below is about the DIRECTION of the failure, not merely that one occurs.

[<Fact>]
let ``#1794 an element with NO number REFUSES the read - it is never silently dropped`` () =
    // THE FIRST FABRICATION. An element with no numeric `number` used to vanish from the candidate set
    // entirely: a lock on it reserved nothing, and nothing anywhere reported an element had been discarded.
    // It cannot be carried as an unreadable ENTRY either — a marker scan is keyed on the number, so there
    // is no lock to look up and no ref to report — so the whole read refuses. #266: never "I looked and it
    // was fine".
    let recorder =
        serving """[{"number":42,"body":"Paths: src/**"},{"title":"no number at all","body":"Paths: src/**"}]"""

    match Reads.openIssues recorder "FS-GG" "FS.GG.SDD" with
    | Error(Malformed(_, detail)) ->
        // AC2 — the count is a fact the caller is entitled to. The refusal locates the element in a payload
        // an operator can go and look at: "element 1 of 2".
        Assert.Contains("element 1 of 2", detail)
    | other -> failwith $"an unidentifiable element must refuse the read, not disappear from it — got %A{other}"

[<Fact>]
let ``#1794 an element whose number is the STRING 42 refuses - a JSON kind change is not an identity`` () =
    // The realistic trigger is not "GitHub returned garbage" — `Transport.Send` refuses a non-JSON body
    // outright. It is a per-ELEMENT anomaly in an otherwise valid array: a schema change, a proxy that
    // rewrites elements. `"42"` is exactly that shape, and it used to be dropped.
    let recorder = serving """[{"number":"42","body":"Paths: src/**"}]"""

    match Reads.openIssues recorder "FS-GG" "FS.GG.SDD" with
    | Error(Malformed(_, detail)) -> Assert.Contains("element 0 of 1", detail)
    | other -> failwith $"a non-numeric `number` must refuse the read — got %A{other}"

[<Fact>]
let ``#1794 a NULL body is BodyRead empty - a real, observed, empty declaration`` () =
    // THE LINE THAT MUST NOT MOVE. GitHub serves `"body": null` for an issue nobody wrote a description
    // for. The issue exists and declares nothing, `TouchSet.parse ""` calls that `Undeclared`, and that is
    // the CORRECT verdict. The defect was never `null` — it was that a null body and an unreadable one were
    // the same value. Fixing this by making `null` unreadable would red the scheduler for every
    // description-less issue on the board.
    let recorder = serving """[{"number":42,"body":null}]"""

    match Reads.openIssues recorder "FS-GG" "FS.GG.SDD" with
    | Ok [ { Number = 42; Body = Reads.BodyRead "" } ] -> ()
    | other -> failwith $"a null body is a successfully-observed EMPTY body — got %A{other}"

[<Fact>]
let ``#1794 an ABSENT body field is BodyUnread - NOT an issue that declares nothing`` () =
    // THE SECOND FABRICATION, and the one #1150 already closed one function away: `TouchSet.parse ""`
    // answers `Undeclared`, and `TouchSet.conflicts` reads `Undeclared` as colliding with nothing. Reading
    // an absent field as `""` therefore ASSERTS "this issue declares nothing" about a row nobody read —
    // and on the #353 gate that assertion is a false DISJOINT with nothing downstream of it.
    let recorder = serving """[{"number":42,"title":"no body field"}]"""

    match Reads.openIssues recorder "FS-GG" "FS.GG.SDD" with
    | Ok [ { Number = 42; Body = Reads.BodyUnread _ } ] -> ()
    | Ok [ { Body = Reads.BodyRead b } ] ->
        failwith $"an absent `body` must NOT read as a body — got BodyRead %A{b}, which parses to Undeclared"
    | other -> failwith $"an absent `body` must be BodyUnread — got %A{other}"

[<Fact>]
let ``#1794 an ILL-TYPED body is BodyUnread - and it still names the issue, so its lock is still read`` () =
    // A body that is an object/number/array is not a body. It is `BodyUnread` rather than a refusal of the
    // whole read, and that distinction is deliberate: the NUMBER was readable, so this row still has a
    // marker route. Callers can therefore ask the one question that settles it — is anything actually
    // holding this? — instead of the read reddening a scan over a row nobody holds.
    let recorder = serving """[{"number":42,"body":{"rewritten":"by a proxy"}}]"""

    match Reads.openIssues recorder "FS-GG" "FS.GG.SDD" with
    | Ok [ { Number = 42; Body = Reads.BodyUnread _ } ] -> ()
    | other -> failwith $"an ill-typed `body` must be BodyUnread, keyed by its still-readable number — got %A{other}"

[<Fact>]
let ``#1794 a PR is still dropped - a POSITIVE identification is not a failure to identify`` () =
    // The one silent drop this read still makes, and it must survive the fix. #641's exclusion is a
    // positive identification of a thing that is not an item of work — not an element we could not read —
    // so it is neither a refusal nor an unreadable entry. A PR with no `number` at all is STILL a PR.
    let recorder =
        serving
            """[{"number":42,"body":"Paths: src/**"},
                {"pull_request":{"url":"u"},"body":"a PR with no number"}]"""

    match Reads.openIssues recorder "FS-GG" "FS.GG.SDD" with
    | Ok [ { Number = 42 } ] -> ()
    | other -> failwith $"a PR is excluded before identity is ever asked — got %A{other}"

// ---- the issue body --------------------------------------------------------------------------------

[<Fact>]
let ``an issue with a NULL body reads as empty - that is a successful read, not a failure`` () =
    // GitHub returns `"body": null` for an issue nobody wrote a description for, and that is a real,
    // successfully-observed fact: the issue exists and declares nothing. `TouchSet.parse` will call it
    // `Undeclared` — an OMISSION — which is the correct verdict and a different one from `Unreadable`.
    let recorder = serving """{"number":42,"body":null}"""

    match Reads.issueBody recorder "FS-GG" "FS.GG.SDD" 42 with
    | Ok "" -> ()
    | other -> failwith $"a null body is an empty body — got %A{other}"

// ---- .github#2107: a PR's own body, for the board-shorthand closing-keyword check -------------------

[<Fact>]
let ``a PR with a NULL body reads as empty - that is a successful read, not a failure`` () =
    // `issueBody`'s exact sibling rule: a PR nobody described is a real, successfully-observed fact.
    let recorder = serving """{"number":801,"body":null}"""

    match Reads.prBody recorder "FS-GG" ".github" 801 with
    | Ok "" -> ()
    | other -> failwith $"a null PR body is an empty body — got %A{other}"

[<Fact>]
let ``a PR body we could NOT read is an error - never an empty string`` () =
    let recorder = failing (Http(502, "bad gateway"))

    match Reads.prBody recorder "FS-GG" ".github" 801 with
    | Error(Http(502, _)) -> ()
    | other -> failwith $"an unreadable PR body must not become an empty read — got %A{other}"

[<Fact>]
let ``a PR body that IS a string reads back exactly, closing-keyword defects and all`` () =
    // The whole reason this read exists (.github#2107): `verify-paths` scans exactly this text for a
    // closing keyword next to the board's own '<repo>#<n>' shorthand.
    let recorder = serving """{"number":801,"body":"Closes .github#2095"}"""

    match Reads.prBody recorder "FS-GG" ".github" 801 with
    | Ok "Closes .github#2095" -> ()
    | other -> failwith $"expected the body verbatim — got %A{other}"

[<Fact>]
let ``an issue body we could NOT read is an error - never an empty touch-set`` () =
    // This is the one that matters. Coercing an unread body to `Undeclared` would report a confident
    // OMISSION about an item nobody looked at — and then schedule every other item against a surface we
    // cannot see. The engine's own `TouchSet.Unreadable` case exists for exactly this, and it can only be
    // produced by a caller that KNOWS the read failed.
    let recorder = failing (Http(502, "bad gateway"))

    match Reads.issueBody recorder "FS-GG" "FS.GG.SDD" 42 with
    | Error(Http(502, _)) -> ()
    | other -> failwith $"an unreadable body must refuse to become a touch-set — got %A{other}"

[<Theory>]
[<InlineData("OPEN")>]
[<InlineData("CLOSED")>]
let ``a per-ref issue-state read distinguishes valid open and closed refs off board`` (wire: string) =
    let recorder = serving $"""{{"number":42,"state":"%s{wire}"}}"""
    let expected = if wire = "OPEN" then IssueState.Open else IssueState.Closed
    Assert.Equal(Ok expected, Reads.issueState recorder "FS-GG" "FS.GG.SDD" 42)

[<Fact>]
let ``a pull request or malformed state is UNKNOWN to reconciliation, never closed`` () =
    let pr = serving """{"number":42,"state":"CLOSED","pull_request":{}}"""
    let malformed = serving """{"number":42,"state":"GONE"}"""
    Assert.True(Result.isError (Reads.issueState pr "FS-GG" "FS.GG.SDD" 42))
    Assert.True(Result.isError (Reads.issueState malformed "FS-GG" "FS.GG.SDD" 42))

// ---- #421, at the read ----------------------------------------------------------------------------

[<Fact>]
let ``#421 a rate-limited read propagates as RateLimited - never as 'not there'`` () =
    // The read layer must carry the budget failure OUT, intact. The moment it degrades to an empty result
    // the caller cannot tell an exhausted budget from an absent subject — and it then acts on the second
    // one, with all the confidence of a read it never got (#421). The remediation itself is harmless — an
    // `item-add` for an issue already on the board is idempotent (#871); the invented certainty is not.
    let recorder = failing (RateLimited(UnknownBudget, None))

    match Reads.markerScan recorder "FS-GG" "FS.GG.SDD" 42 with
    | Error(RateLimited _) -> ()
    | other -> failwith $"an exhausted budget must not become an empty lock — got %A{other}"

    match Reads.issueBody recorder "FS-GG" "FS.GG.SDD" 42 with
    | Error(RateLimited _) -> ()
    | other -> failwith $"an exhausted budget must not become an empty body — got %A{other}"

// ---- the sub-issue graph (lint / rollup) -----------------------------------------------------------

[<Fact>]
let ``subIssues reads the total apart from the visible nodes, with each child's ref and state`` () =
    let transport =
        serving
            """{"data":{"repository":{"issue":{"subIssues":{"totalCount":2,"nodes":[
                 {"number":51,"state":"OPEN","repository":{"nameWithOwner":"FS-GG/FS.GG.SDD"}},
                 {"number":52,"state":"CLOSED","repository":{"nameWithOwner":"FS-GG/FS.GG.SDD"}}]}}}}}"""

    match Reads.subIssues transport "FS-GG" "FS.GG.SDD" 50 with
    | Ok set ->
        Assert.Equal(2, set.Total)
        Assert.Equal<Reads.SubIssue list>(
            [ ({ Ref = "FS-GG/FS.GG.SDD#51"; Open = true }: Reads.SubIssue)
              { Ref = "FS-GG/FS.GG.SDD#52"; Open = false } ],
            set.Children
        )
    | Error e -> failwith $"the graph must resolve — got %A{e}"

[<Fact>]
let ``subIssues keeps a truncated graph honest - Total exceeds the visible nodes`` () =
    // The distinction EPIC-CHILDREN-TRUNCATED and the rollup depend on: five children, only two returned.
    let transport =
        serving
            """{"data":{"repository":{"issue":{"subIssues":{"totalCount":5,"nodes":[
                 {"number":1,"state":"CLOSED","repository":{"nameWithOwner":"FS-GG/FS.GG.SDD"}},
                 {"number":2,"state":"CLOSED","repository":{"nameWithOwner":"FS-GG/FS.GG.SDD"}}]}}}}}"""

    match Reads.subIssues transport "FS-GG" "FS.GG.SDD" 50 with
    | Ok set -> Assert.True(set.Total > List.length set.Children)
    | Error e -> failwith $"the graph must resolve — got %A{e}"

[<Fact>]
let ``subIssues FAILS CLOSED - an unreadable graph is an error, never an empty set`` () =
    // An epic whose children could not be read must not roll up as "no children".
    match Reads.subIssues (failing (RateLimited(UnknownBudget, None))) "FS-GG" "FS.GG.SDD" 50 with
    | Error(RateLimited _) -> ()
    | other -> failwith $"a failed graph read must be an error — got %A{other}"

[<Fact>]
let ``refIsPullRequest is true iff the issues payload carries a pull_request object`` () =
    let asPr =
        serving """{"number":418,"pull_request":{"url":"https://github.com/x/y/pull/418"}}"""

    let asIssue = serving """{"number":414,"body":"a plain issue"}"""

    match Reads.refIsPullRequest asPr "FS-GG" "FS.GG.SDD" 418 with
    | Ok true -> ()
    | other -> failwith $"a PR payload must probe true — got %A{other}"

    match Reads.refIsPullRequest asIssue "FS-GG" "FS.GG.SDD" 414 with
    | Ok false -> ()
    | other -> failwith $"a plain issue must probe false — got %A{other}"

// ---- body-edit provenance (.github#2477) -------------------------------------------------------------

[<Fact>]
let ``contentEditProvenance reads the total apart from the visible edits, with each edit's time and editor`` () =
    let transport =
        serving
            """{"data":{"repository":{"issueOrPullRequest":{"userContentEdits":{"totalCount":2,"nodes":[
                 {"editedAt":"2026-08-01T10:00:00Z","editor":{"login":"alice"}},
                 {"editedAt":"2026-08-02T11:30:00Z","editor":{"login":"bob"}}]}}}}}"""

    match Reads.contentEditProvenance transport "FS-GG" ".github" 2417 with
    | Ok provenance ->
        Assert.Equal(2, provenance.Total)

        Assert.Equal<Reads.ContentEdit list>(
            [ ({ EditedAt = System.DateTimeOffset.Parse "2026-08-01T10:00:00Z"
                 EditorLogin = Some "alice" }: Reads.ContentEdit)
              { EditedAt = System.DateTimeOffset.Parse "2026-08-02T11:30:00Z"
                EditorLogin = Some "bob" } ],
            provenance.Edits
        )
    | Error e -> failwith $"the provenance must resolve — got %A{e}"

[<Fact>]
let ``contentEditProvenance reports a genuine zero - measured, not defaulted`` () =
    let transport =
        serving """{"data":{"repository":{"issueOrPullRequest":{"userContentEdits":{"totalCount":0,"nodes":[]}}}}}"""

    match Reads.contentEditProvenance transport "FS-GG" ".github" 2417 with
    | Ok provenance ->
        Assert.Equal(0, provenance.Total)
        Assert.Empty(provenance.Edits)
    | Error e -> failwith $"a genuine zero must resolve, not refuse — got %A{e}"

[<Fact>]
let ``contentEditProvenance keeps a truncated connection honest - Total exceeds the visible nodes`` () =
    // The 100-item cap on `userContentEdits(first: 100)` means a caller must be able to tell "5 edits, all
    // listed" from "127 edits, 100 shown" — the same `SubIssueSet` distinction, over a different connection.
    let transport =
        serving
            """{"data":{"repository":{"issueOrPullRequest":{"userContentEdits":{"totalCount":127,"nodes":[
                 {"editedAt":"2026-08-01T10:00:00Z","editor":{"login":"alice"}}]}}}}}"""

    match Reads.contentEditProvenance transport "FS-GG" ".github" 2417 with
    | Ok provenance -> Assert.True(provenance.Total > List.length provenance.Edits)
    | Error e -> failwith $"the connection must resolve — got %A{e}"

[<Fact>]
let ``contentEditProvenance tolerates a deleted editor - a null actor is not a parse failure`` () =
    let transport =
        serving
            """{"data":{"repository":{"issueOrPullRequest":{"userContentEdits":{"totalCount":1,"nodes":[
                 {"editedAt":"2026-08-01T10:00:00Z","editor":null}]}}}}}"""

    match Reads.contentEditProvenance transport "FS-GG" ".github" 2417 with
    | Ok provenance ->
        Assert.Equal(1, provenance.Total)

        Assert.Equal<Reads.ContentEdit list>(
            [ ({ EditedAt = System.DateTimeOffset.Parse "2026-08-01T10:00:00Z"
                 EditorLogin = None }: Reads.ContentEdit) ],
            provenance.Edits
        )
    | Error e -> failwith $"a deleted editor must not fail the read — got %A{e}"

// ---- AC4 (.github#2477): a failed or unauthorized read is a FAILED READ, never "no edits" -------------
//
// `.github#2456`'s whole point is that a REST-timeline "no edits found" is NOT_MEASURED, not a negative
// result. A GraphQL read that folded any of these failures into `Ok { Total = 0; Edits = [] }` would
// silently manufacture exactly that false negative through the "authoritative" path instead.

[<Fact>]
let ``#2477 contentEditProvenance FAILS CLOSED - a null issueOrPullRequest is an error, never zero edits`` () =
    // Not found, wrong type, or not visible to this token — GraphQL answers with
    // `data.repository.issueOrPullRequest: null`.
    let transport = serving """{"data":{"repository":{"issueOrPullRequest":null}}}"""

    match Reads.contentEditProvenance transport "FS-GG" ".github" 999999 with
    | Error(Malformed _) -> ()
    | Ok p -> failwith $"a null issueOrPullRequest must refuse, not report zero edits — got Total=%d{p.Total}"
    | Error other -> failwith $"expected Malformed — got %A{other}"

[<Fact>]
let ``#2477 contentEditProvenance FAILS CLOSED - a null repository is an error, never zero edits`` () =
    let transport = serving """{"data":{"repository":null}}"""

    match Reads.contentEditProvenance transport "FS-GG" ".github" 2417 with
    | Error(Malformed _) -> ()
    | Ok p -> failwith $"a null repository must refuse, not report zero edits — got Total=%d{p.Total}"
    | Error other -> failwith $"expected Malformed — got %A{other}"

[<Fact>]
let ``#2477 contentEditProvenance FAILS CLOSED - an empty body is an error, never zero edits`` () =
    let transport = serving ""

    match Reads.contentEditProvenance transport "FS-GG" ".github" 2417 with
    | Error(Malformed _) -> ()
    | Ok p -> failwith $"an unreadable body must refuse, not report zero edits — got Total=%d{p.Total}"
    | Error other -> failwith $"expected Malformed — got %A{other}"

[<Fact>]
let ``#2477 contentEditProvenance FAILS CLOSED - a non-object JSON root is an error, never an unhandled crash`` () =
    // The critic's finding on PR #2480 round 1: a 2xx body that is SYNTACTICALLY VALID JSON but not an
    // object — `[]` here — made `TryGetProperty "errors"` throw `InvalidOperationException` from a call
    // site that sat OUTSIDE the try/with a few lines below. That surfaced as an unhandled crash
    // (`ExitDefect`, a raw stack trace) rather than a reported failure — AC4's letter ("a failed or
    // unauthorized read must be reported as a FAILED READ") is not met by a read that does not complete
    // at all. `Budget.readMeter` guards this identical non-object-root case for the identical reason
    // (`.github#2418`/PR #2419); this proves the same guard closes it here. If this test throws instead
    // of returning `Error`, xUnit reports it as a failed test with the escaping exception's stack trace —
    // which is itself the observable shape of the defect this test exists to keep closed.
    let transport = serving "[]"

    match Reads.contentEditProvenance transport "FS-GG" ".github" 2417 with
    | Error(Malformed _) -> ()
    | Ok p -> failwith $"a non-object root must refuse, not report zero edits — got Total=%d{p.Total}"
    | Error other -> failwith $"expected Malformed — got %A{other}"

[<Fact>]
let ``#2477 contentEditProvenance FAILS CLOSED - a 200-with-errors rate limit is RateLimited, never zero edits`` () =
    // GitHub reports an exhausted GraphQL budget as an HTTP 200 carrying `errors`, exactly like a
    // genuinely partial response — the same shape `Board.GraphQl.decode` guards. `errors` must be read
    // BEFORE `data` is trusted, so this is a RateLimited error, not the generic Malformed the
    // missing-data arm would produce if `errors` were never inspected first.
    let transport =
        serving
            """{"data":null,"errors":[{"type":"RATE_LIMITED","message":"API rate limit exceeded for installation ID 123."}]}"""

    match Reads.contentEditProvenance transport "FS-GG" ".github" 2417 with
    | Error(RateLimited _) -> ()
    | Ok p -> failwith $"a rate-limited response must refuse, not report zero edits — got Total=%d{p.Total}"
    | Error other -> failwith $"expected RateLimited — got %A{other}"

[<Fact>]
let ``#2477 contentEditProvenance FAILS CLOSED - a generic GraphQL error is GraphQlErrors, never zero edits`` () =
    let transport =
        serving """{"data":null,"errors":[{"type":"FORBIDDEN","message":"Resource not accessible by integration"}]}"""

    match Reads.contentEditProvenance transport "FS-GG" ".github" 2417 with
    | Error(GraphQlErrors messages) -> Assert.Contains("Resource not accessible by integration", messages)
    | Ok p -> failwith $"a forbidden response must refuse, not report zero edits — got Total=%d{p.Total}"
    | Error other -> failwith $"expected GraphQlErrors — got %A{other}"

[<Fact>]
let ``#2477 contentEditProvenance FAILS CLOSED - a transport failure propagates as-is, never zero edits`` () =
    match Reads.contentEditProvenance (failing (RateLimited(UnknownBudget, None))) "FS-GG" ".github" 2417 with
    | Error(RateLimited _) -> ()
    | Ok p -> failwith $"a transport failure must refuse, not report zero edits — got Total=%d{p.Total}"
    | other -> failwith $"expected the transport's own error to propagate — got %A{other}"

// ---- messages: the say/inbox channel ---------------------------------------------------------------

/// One `fsgg:msg` comment, rendered exactly as `Writes.say` writes the body: REAL newlines separating the
/// marker comment, the `**from → to**` header, and the text.
let private msgComment (cid: int) (fromW: string) (dest: string) (text: string) =
    let body = $"<!-- fsgg:msg from={fromW} to={dest} -->\n**{fromW} → {dest}**\n\n{text}"
    let jbody = System.Text.Json.JsonSerializer.Serialize body
    $"""{{"id":{cid},"body":{jbody},"created_at":"2026-07-16T00:00:0{cid}Z"}}"""

[<Fact>]
let ``messages parses an fsgg:msg comment - id, from, to, and the text with the header peeled off`` () =
    let recorder =
        serving ("[" + msgComment 7 "finch-a3f" "smew-f31" "I own src/Audio until Friday." + "]")

    match Reads.messages recorder "FS-GG" "FS.GG.SDD" 42 with
    | Ok [ m ] ->
        Assert.Equal(7L, m.Id)
        Assert.Equal("finch-a3f", m.From)
        Assert.Equal("smew-f31", m.To)
        // The `<!-- … -->` marker and the `**from → to**` header are peeled; the message itself remains.
        Assert.Equal("I own src/Audio until Friday.", m.Text)
    | other -> failwith $"expected one parsed message — got %A{other}"

[<Fact>]
let ``messages keeps a broadcast (to=*) and orders by comment id`` () =
    let page =
        "[" + msgComment 9 "finch-a3f" "*" "second" + "," + msgComment 4 "finch-a3f" "smew-f31" "first" + "]"

    match Reads.messages (serving page) "FS-GG" "FS.GG.SDD" 42 with
    | Ok [ a; b ] ->
        // Lowest comment id first — the same total order `markers` returns, so a cursor keyed on the id is
        // monotone regardless of the order GitHub returned the page in.
        Assert.Equal(4L, a.Id)
        Assert.Equal(9L, b.Id)
        Assert.Equal("*", b.To)
    | other -> failwith $"expected two ordered messages — got %A{other}"

[<Fact>]
let ``messages ignores a claim marker and any non-message comment`` () =
    // A comments page carries claim markers and plain comments too. `messages` reads ONLY `fsgg:msg`, so a
    // lock marker on the same issue never surfaces as mail.
    let marker =
        comment 1 "<!-- fsgg:claim worker=ghost -->" now

    let plain = comment 2 "just a normal human comment" now
    let page = "[" + marker + "," + plain + "," + msgComment 3 "finch-a3f" "smew-f31" "the only message" + "]"

    match Reads.messages (serving page) "FS-GG" "FS.GG.SDD" 42 with
    | Ok [ m ] -> Assert.Equal("the only message", m.Text)
    | other -> failwith $"a claim marker and a plain comment are not messages — got %A{other}"

[<Fact>]
let ``messages does NOT deliver a comment whose TEXT merely quotes a msg marker (anchored)`` () =
    // The same forgery `markerRe` refuses: an un-anchored match would let a message BODY that quotes an
    // `fsgg:msg` header be read as a real message header. The regex is anchored at the start of the body.
    let forgery =
        comment 5 "look what I can write: <!-- fsgg:msg from=ghost to=victim -->" now

    match Reads.messages (serving ("[" + forgery + "]")) "FS-GG" "FS.GG.SDD" 42 with
    | Ok [] -> ()
    | other -> failwith $"a quoted marker mid-body is not a message — got %A{other}"

[<Fact>]
let ``messages FAILS CLOSED on a malformed page - a lost message is not an empty mailbox`` () =
    // A message is not a lock, so a single unparseable message is DROPPED — but a page we could not read at
    // all is still an error, never an empty mailbox that reports "no new mail" over an unread warning.
    match Reads.messages (serving "<html>502</html>") "FS-GG" "FS.GG.SDD" 42 with
    | Error(Malformed _) -> ()
    | other -> failwith $"a malformed page must be an error, not an empty mailbox — got %A{other}"

// ---- issues: the ETag-revalidated REST list (#446/#418) --------------------------------------------

/// A private cache directory for the ETag round-trip. `Reads.issues` stores the body + its validator on
/// disk (that is what makes a later 304 answerable), so a test of it owns a throwaway cache the way
/// `CacheTests.Sandbox` does — an inherited cache would be testing whatever ran before it.
type private IssuesCache() =
    let dir =
        System.IO.Path.Combine(System.IO.Path.GetTempPath(), "fsgg-issues-test-" + System.Guid.NewGuid().ToString("N"))

    do
        System.IO.Directory.CreateDirectory dir |> ignore
        System.Environment.SetEnvironmentVariable("FSGG_COORD_CACHE", dir)

    interface System.IDisposable with
        member _.Dispose() =
            System.Environment.SetEnvironmentVariable("FSGG_COORD_CACHE", null)

            try
                System.IO.Directory.Delete(dir, true)
            with _ ->
                ()

/// A stateful transport: 200 + ETag for an UNCONDITIONAL read, 304 for a conditional one whose validator
/// matches — exactly how GitHub answers `If-None-Match`. It is the ETag revalidation the command is built on.
let private etagServer (body: string) (etag: string) =
    Fake.Recorder(fun (req: Request) ->
        match req.IfNoneMatch with
        | Some e when e = etag -> Ok { Status = 304; Body = ""; ETag = Some etag; NextLink = None; Headers = Map.empty }
        | _ -> Ok { Status = 200; Body = body; ETag = Some etag; NextLink = None; Headers = Map.empty })

[<Fact>]
let ``issues returns the raw body, then revalidates with the stored ETag and serves the 304 from cache (#418)`` () =
    // The command's whole reason to exist: a repeat listing costs NOTHING. The first read is unconditional
    // (inm=none) and caches the body with its validator; the second sends the ETag, the server answers 304,
    // and the body is served FROM CACHE — the budget-free read.
    use _cache = new IssuesCache()
    let body = """[{"number":501},{"number":502}]"""
    let etag = "W/\"issues-v1\""
    let recorder = etagServer body etag

    match Reads.issues recorder "FS-GG" "FS.GG.SDD" "open" None false with
    | Ok b -> Assert.Equal(body, b)
    | other -> failwith $"first read must return the body — got %A{other}"

    Assert.True(recorder.Logged "issue-list FS-GG/FS.GG.SDD paginate=1 inm=none")

    match Reads.issues recorder "FS-GG" "FS.GG.SDD" "open" None false with
    | Ok b -> Assert.Equal(body, b)
    | other -> failwith $"a 304 must serve the cached body, never an empty result — got %A{other}"

    Assert.True(recorder.Logged $"issue-list FS-GG/FS.GG.SDD paginate=1 inm={etag}")

[<Fact>]
let ``issues --refresh drops the stored ETag and re-reads unconditionally`` () =
    // `--refresh` (fresh=true) is the caller saying "ignore the cache". Even with a warm body+etag, the
    // read goes out UNCONDITIONAL (inm=none), so a caller who suspects a stale cache can force a full read.
    use _cache = new IssuesCache()
    let body = """[{"number":501}]"""
    let etag = "W/\"issues-v1\""
    let recorder = etagServer body etag

    Reads.issues recorder "FS-GG" "FS.GG.SDD" "open" None false |> ignore // warm the cache

    match Reads.issues recorder "FS-GG" "FS.GG.SDD" "open" None true with
    | Ok b -> Assert.Equal(body, b)
    | other -> failwith $"a --refresh read must return the fresh body — got %A{other}"

    // Both requests carried NO validator — the second because --refresh dropped it.
    Assert.Equal(2, recorder.Count "issue-list FS-GG/FS.GG.SDD paginate=1 inm=none")

[<Fact>]
let ``issues fails closed on an unreadable list - never an empty array`` () =
    // A listing we could not read is an ERROR, not "this repo has no issues" — the same fail-closed rule
    // the rest of this layer holds. The body is passed through raw, so an empty-but-present array `[]` is a
    // real answer; a 502 is not.
    use _cache = new IssuesCache()

    match Reads.issues (failing (Http(502, "bad gateway"))) "FS-GG" "FS.GG.SDD" "open" None false with
    | Error(Http(502, _)) -> ()
    | other -> failwith $"an unreadable listing must refuse — got %A{other}"

[<Fact>]
let ``issues fails closed on a 200 that is not a JSON array - a proxy error page is not an empty listing`` () =
    // The #461 rule at the `issues` surface: a 200 carrying a proxy's HTML error body (or a truncated page)
    // must NOT be emitted verbatim as if it were the issue list — it is a failed read. A present-but-empty
    // `[]` passes (a real answer); garbage does not, and nothing is cached for a later 304 to serve.
    use _cache = new IssuesCache()

    match Reads.issues (serving "<html>502 Bad Gateway</html>") "FS-GG" "FS.GG.SDD" "open" None false with
    | Error(Malformed _) -> ()
    | other -> failwith $"a non-JSON 200 must be a failed read, not a listing — got %A{other}"

    match Reads.issues (serving "[]") "FS-GG" "FS.GG.SDD" "open" None false with
    | Ok "[]" -> ()
    | other -> failwith $"a present-but-empty array is a real answer — got %A{other}"


// ---- the conditional landable reads (the `--wait` poll loop) ---------------------------------------
//
// `landable --wait` polls `prLandableRequire` up to 30 times at 20s intervals, and each poll reads THREE
// REST paths: the PR object, its head SHA's workflow runs, and its check-runs. That is ~90 REST calls per
// wait, per worker, per item — on the budget the whole fleet shares — and `pnext-item` drives a wait on
// every item. A poll that finds no change is exactly the 304 case, because "nothing has changed yet" is
// what waiting MEANS. So all three revalidate.
//
// What makes that safe is NOT that the reads are cheap. It is that a 304 is the server asserting the body
// we hold is current, and that the validator is only ever stored where it can stand for the WHOLE answer:
// a single resource, or a page with headroom (`Reads.memoisable`). These tests hold both lines.

/// A per-path ETag server. 200 + a path-derived validator on an unconditional read; 304 when the caller
/// sends that validator back — how GitHub answers `If-None-Match`. It RECORDS the validator every request
/// carried, per path, which is the one fact the fake's log grammar does not carry for these paths.
///
/// `runs` is how many workflow runs the page carries, which is how a test drives the headroom boundary.
/// `nextLink` makes every 200 advertise a next page.
type private LandableServer(sha: string, ?runs: int, ?nextLink: string) =
    let seen = System.Collections.Generic.List<string * string option>()
    let runCount = defaultArg runs 1

    /// `runCount` green runs, all in the same check suite — the page whose SIZE the headroom rule reads.
    let runsBody =
        let one (i: int) =
            $"""{{"path":".github/workflows/b%d{i}.yml","event":"pull_request","head_branch":"item/42-x","run_number":%d{i},"status":"completed","conclusion":"success","check_suite_id":1,"pull_requests":[{{"number":801}}]}}"""

        let items = [ 1..runCount ] |> List.map one |> String.concat ","
        $"""{{"total_count":%d{runCount},"workflow_runs":[%s{items}]}}"""

    let bodies =
        [ "repos/FS-GG/FS.GG.SDD/pulls/801",
          "{\"number\":801,\"state\":\"open\",\"mergeable\":true,\"head\":{\"ref\":\"item/42-x\",\"sha\":\""
          + sha
          + "\"}}"
          "repos/FS-GG/FS.GG.SDD/actions/runs", runsBody
          "repos/FS-GG/FS.GG.SDD/commits/" + sha + "/check-runs",
          """{"total_count":1,"check_runs":[{"name":"build","check_suite":{"id":1},"status":"completed","conclusion":"success"}]}""" ]

    /// The validator is derived from the path AND the sha, so a test can prove one subject's body is never
    /// served as another's — a WRONG answer, not merely a stale one, feeding a decision to merge.
    let etagOf (path: string) = $"W/\"%s{path}@%s{sha}\""

    member _.Validators(path: string) =
        seen |> Seq.filter (fun (p, _) -> p = path) |> Seq.map snd |> List.ofSeq

    member _.Recorder =
        Fake.Recorder(fun (req: Request) ->
            seen.Add(req.Path, req.IfNoneMatch)

            match bodies |> List.tryFind (fun (p, _) -> p = req.Path) with
            | None -> Error(NotFound req.Path)
            | Some(_, body) ->
                let etag = etagOf req.Path

                match req.IfNoneMatch with
                | Some e when e = etag -> Ok { Status = 304; Body = ""; ETag = Some etag; NextLink = None; Headers = Map.empty }
                | _ ->
                    Ok
                        { Status = 200
                          Body = body
                          ETag = Some etag
                          NextLink = nextLink; Headers = Map.empty })

/// The three reads of one `landable` poll.
let private pollPaths (sha: string) =
    [ "repos/FS-GG/FS.GG.SDD/pulls/801"
      "repos/FS-GG/FS.GG.SDD/actions/runs"
      $"repos/FS-GG/FS.GG.SDD/commits/%s{sha}/check-runs" ]

[<Fact>]
let ``every read of the landable poll revalidates on the second look, and the 304s reach the SAME verdict`` () =
    // THE WIN, AND ITS SAFETY ARGUMENT, IN ONE TEST. The first poll is unconditional and caches each body
    // with its validator; the second sends them back, is served 304s — and still scores GREEN. A cache that
    // changed the verdict would be a cache deciding whether to merge.
    use _cache = new IssuesCache()
    let server = LandableServer "sha-green"

    Assert.Equal(PrGreen, Reads.prLandable server.Recorder "FS-GG" "FS.GG.SDD" 801)
    Assert.Equal(PrGreen, Reads.prLandable server.Recorder "FS-GG" "FS.GG.SDD" 801)

    for path in pollPaths "sha-green" do
        match server.Validators path with
        | [ None; Some _ ] -> ()
        | other -> failwith $"%s{path}: poll one must be unconditional and poll two must revalidate — got %A{other}"

[<Fact>]
let ``a page with NO headroom is never memoised - the boundary the whole rule exists to refuse`` () =
    // THE PROOF'S EDGE. A page carrying exactly `per_page` items and no `Link` looks complete and is not
    // provably so: if the set later grows, the new items land on page two and page one can stay
    // byte-identical — so the server would answer 304 and we would serve a one-page body for a two-page set,
    // scoring a merge verdict over runs we never saw (#461). Only a page with HEADROOM (`n < per_page`)
    // guarantees that growth rewrites page one. So a full page stores no validator and the next poll pays.
    use _cache = new IssuesCache()
    let server = LandableServer("sha-green", runs = 100) // per_page is 100 — a full page, no headroom

    Reads.prLandable server.Recorder "FS-GG" "FS.GG.SDD" 801 |> ignore
    Reads.prLandable server.Recorder "FS-GG" "FS.GG.SDD" 801 |> ignore

    let conditional =
        server.Validators "repos/FS-GG/FS.GG.SDD/actions/runs" |> List.filter Option.isSome

    if not conditional.IsEmpty then
        failwith $"a FULL page cannot prove headroom and must not be memoised — got %A{conditional}"

    // ...while the same poll's PR object — a single resource, which cannot paginate — still revalidates. The
    // rule is per-subject, not a blanket retreat.
    match server.Validators "repos/FS-GG/FS.GG.SDD/pulls/801" with
    | [ None; Some _ ] -> ()
    | other -> failwith $"a single resource still revalidates — got %A{other}"

[<Fact>]
let ``a response that PAGINATES stores no validator, whatever its shape`` () =
    // A merged response's ETag is page one's alone. Storing it would revalidate a two-page set against its
    // first page — the hazard headroom exists to make unreachable, and this is the backstop under it.
    use _cache = new IssuesCache()
    let server = LandableServer("sha-green", nextLink = "https://api.github.com/x?page=2")

    Reads.prLandable server.Recorder "FS-GG" "FS.GG.SDD" 801 |> ignore
    Reads.prLandable server.Recorder "FS-GG" "FS.GG.SDD" 801 |> ignore

    for path in pollPaths "sha-green" do
        let conditional = server.Validators path |> List.filter Option.isSome

        if not conditional.IsEmpty then
            failwith $"%s{path}: a paginated response's page-one ETag must never be stored — got %A{conditional}"

[<Fact>]
let ``the runs cache is keyed on the head SHA - one commit's green is never served as another's`` () =
    // `actions/runs` is the SAME PATH for every commit; the SHA rides in the QUERY. Key on the path alone and
    // a force-push would be served the PREVIOUS commit's green — not a stale answer but a WRONG one, and what
    // it decides is whether to merge. So the cache key carries the query.
    use _cache = new IssuesCache()

    Reads.prLandable (LandableServer "sha-one").Recorder "FS-GG" "FS.GG.SDD" 801 |> ignore

    let second = LandableServer "sha-two"
    Assert.Equal(PrGreen, Reads.prLandable second.Recorder "FS-GG" "FS.GG.SDD" 801)

    match second.Validators "repos/FS-GG/FS.GG.SDD/actions/runs" with
    | [ None ] -> ()
    | other -> failwith $"a different head SHA must not reuse the previous commit's validator — got %A{other}"

[<Fact>]
let ``issues judges headroom on the RAW page, not on the filtered projection (#641)`` () =
    // THE SUBTLE ONE. `issues` caches a PROJECTION — pull requests dropped (#641) — but `memoisable` asks a
    // question about the PAGE the server sent and what its ETag stands for. Serve a FULL page of 100 raw
    // items that filters down to 60 issues: judged on the filtered body it would "prove" headroom (60 < 100)
    // and memoise a validator that cannot vouch for the set; judged on the raw page (100 = per_page, no
    // headroom) it must refuse. Getting this backwards would serve a one-page body for a two-page list once
    // a repo crossed 100 open issues — #461, laundered through our own filter, on a delay.
    use _cache = new IssuesCache()

    let raw =
        let issue (i: int) = "{\"number\":" + string i + "}"
        let pr (i: int) = "{\"number\":" + string i + ",\"pull_request\":{\"url\":\"u\"}}"
        // 60 issues + 40 PRs = a full page of 100.
        let items = [ for i in 1..60 -> issue i ] @ [ for i in 61..100 -> pr i ]
        "[" + String.concat "," items + "]"

    let seen = System.Collections.Generic.List<string option>()

    let recorder =
        Fake.Recorder(fun (req: Request) ->
            seen.Add req.IfNoneMatch
            Ok { Status = 200; Body = raw; ETag = Some "W/\"full-page\""; NextLink = None; Headers = Map.empty })

    match Reads.issues recorder "FS-GG" "FS.GG.SDD" "open" None false with
    | Ok body -> Assert.DoesNotContain("pull_request", body) // the projection still drops PRs
    | other -> failwith $"the listing must be returned — got %A{other}"

    Reads.issues recorder "FS-GG" "FS.GG.SDD" "open" None false |> ignore

    if seen |> Seq.exists Option.isSome then
        failwith
            $"a full RAW page has no headroom and must not be memoised, however few items survive the #641 filter — got %A{List.ofSeq seen}"

[<Fact>]
let ``a page we cannot COUNT is never memoised - headroom unproven is headroom refused`` () =
    // The fail-closed clause of the headroom rule. `memoisable` proves headroom by COUNTING the page; a body
    // that parses but is not shaped as the caller declared (here: no `workflow_runs` array) yields no count,
    // and no count means no proof. It must refuse rather than assume — the cost of not memoising is one paid
    // read, and the cost of assuming is a validator vouching for a set nobody measured.
    use _cache = new IssuesCache()
    let seen = System.Collections.Generic.List<string * string option>()

    let recorder =
        Fake.Recorder(fun (req: Request) ->
            seen.Add(req.Path, req.IfNoneMatch)

            let body =
                if req.Path.EndsWith "actions/runs" then
                    // Valid JSON, and countable by nobody: the declared `workflow_runs` array is absent.
                    """{"total_count":0}"""
                elif req.Path.EndsWith "check-runs" then
                    """{"total_count":1,"check_runs":[{"name":"build","check_suite":{"id":1},"status":"completed","conclusion":"success"}]}"""
                else
                    "{\"number\":801,\"state\":\"open\",\"mergeable\":true,\"head\":{\"ref\":\"item/42-x\",\"sha\":\"sha-x\"}}"

            Ok { Status = 200; Body = body; ETag = Some "W/\"v1\""; NextLink = None; Headers = Map.empty })

    Reads.prLandable recorder "FS-GG" "FS.GG.SDD" 801 |> ignore
    Reads.prLandable recorder "FS-GG" "FS.GG.SDD" 801 |> ignore

    let validatorsFor (needle: string) =
        seen
        |> Seq.filter (fun (p, _) -> p.EndsWith needle)
        |> Seq.map snd
        |> List.ofSeq

    // The uncountable runs page proved no headroom, so it stored nothing and BOTH polls went out
    // unconditional.
    match validatorsFor "actions/runs" with
    | [ None; None ] -> ()
    | other -> failwith $"an uncountable page must never be memoised — got %A{other}"

    // THE COUNTERWEIGHT, and it is what stops this passing for the wrong reason: the same poll's countable
    // reads DO revalidate. A blanket failure to memoise anything would satisfy the assertion above.
    match validatorsFor "check-runs" with
    | [ None; Some _ ] -> ()
    | other -> failwith $"a countable page with headroom must still revalidate — got %A{other}"

/// Drives the mergeability re-read budget without paying its wall-clock: the production delay is ~1s and the
/// budget is 3, so an un-nulled `FSGG_COORD_MERGEABLE_RETRY_MS` would cost these tests ~2s each to prove a
/// decision that has nothing to do with the sleeping.
type private NoMergeableRetryDelay() =
    do System.Environment.SetEnvironmentVariable("FSGG_COORD_MERGEABLE_RETRY_MS", "0")

    interface System.IDisposable with
        member _.Dispose() =
            System.Environment.SetEnvironmentVariable("FSGG_COORD_MERGEABLE_RETRY_MS", null)

/// A PR whose `mergeable` is whatever the caller says, counting how many times it was read. `mergeable` is
/// rendered as a raw JSON token so a test can serve `null` (COMPUTING) and `true`/`false` through one fixture.
type private MergeablePrServer(tokenFor: int -> string) =
    let mutable prReads = 0

    member _.PrReads = prReads

    member _.Recorder =
        Fake.Recorder(fun (req: Request) ->
            let body =
                if req.Path.EndsWith "pulls/801" then
                    prReads <- prReads + 1

                    $"""{{"number":801,"state":"open","mergeable":%s{tokenFor prReads},"head":{{"ref":"item/42-x","sha":"sha-x"}}}}"""
                elif req.Path.EndsWith "actions/runs" then
                    """{"total_count":1,"workflow_runs":[{"path":".github/workflows/b.yml","event":"pull_request","head_branch":"item/42-x","run_number":1,"status":"completed","conclusion":"success","check_suite_id":1,"pull_requests":[{"number":801}]}]}"""
                else
                    """{"total_count":1,"check_runs":[{"name":"build","check_suite":{"id":1},"status":"completed","conclusion":"success"}]}"""

            Ok { Status = 200; Body = body; ETag = None; NextLink = None; Headers = Map.empty })

[<Fact>]
let ``a mergeable still COMPUTING past the re-read budget is pending, not unknown — so --wait waits`` () =
    // #950. GitHub computes `mergeable` in a background job and serves `null` until it lands, which is the
    // NORMAL first answer for a seconds-old PR — exactly the PR §5 tells a worker to run `landable --wait`
    // on, immediately after opening it. A `null` outliving the bounded re-read is therefore a job that has
    // not finished, not a PR nobody can read: it is GUARANTEED to change. Read as `PrUnknown` it settled at
    // once (`Landable.settled`), so `--wait` returned exit 4 without waiting at all and §5's `|| exit 1`
    // walked the worker away from finished, green, mergeable work.
    use _cache = new IssuesCache()
    use _fast = new NoMergeableRetryDelay()
    let server = MergeablePrServer(fun _ -> "null")

    let state, n, unmet = Reads.prLandableRequire server.Recorder "FS-GG" "FS.GG.SDD" 801 [] None

    Assert.Equal(PrPending, state)
    Assert.Equal(0, n)

    // No unmet reason: that channel carries assertions the CALLER added, and its stderr tells the operator
    // "These are assertions you asked for". Nobody asked for GitHub's background job, so a reason here would
    // be a true sentence printed under a false one.
    Assert.Empty unmet

    // A pending verdict is one `--wait` may never stop on. This is the half that actually fixes the bug:
    // a `PrPending` that settled would return exit 7 just as promptly as the exit 4 it replaced.
    Assert.False(Landable.settled state n 0, "a computing mergeable must NEVER settle — --wait must poll it")

    // The re-read budget is unchanged (#697): 3 tries, no more. `--wait` does the waiting now, so widening
    // the budget here would only make every single-shot caller pay for it.
    Assert.Equal(3, server.PrReads)

[<Fact>]
let ``an ABSENT mergeable field is still unknown, and still settles — the fail-closed half`` () =
    // The counterweight to the test above, and the reason `Computing` and `Absent` are split rather than
    // both promoted. An absent field is not a background job: it will not appear on a second look, so
    // "pending" would promise a resolution that can never arrive and `--wait` would poll until its cap.
    // Unknown/4 is the honest, fail-closed answer — and a re-read would be a no-op dressed as diligence.
    use _cache = new IssuesCache()
    use _fast = new NoMergeableRetryDelay()

    let recorder =
        Fake.Recorder(fun (req: Request) ->
            let body =
                if req.Path.EndsWith "pulls/801" then
                    // Parses, and carries no `mergeable` at all.
                    """{"number":801,"state":"open","head":{"ref":"item/42-x","sha":"sha-x"}}"""
                else
                    """{"total_count":0,"workflow_runs":[]}"""

            Ok { Status = 200; Body = body; ETag = None; NextLink = None; Headers = Map.empty })

    let state, _, _ = Reads.prLandableRequire recorder "FS-GG" "FS.GG.SDD" 801 [] None

    Assert.Equal(PrUnknown, state)
    Assert.True(Landable.settled state 0 0, "an absent mergeable cannot change — it must settle")

[<Fact>]
let ``a mergeable that lands WITHIN the budget is still scored on the spot, not deferred to --wait`` () =
    // The third leg, and the one that stops the fix passing for the wrong reason: promoting `Computing` to
    // `PrPending` must not make the bounded re-read pointless. A `null` that flips to `true` on the second
    // look is still resolved inside ONE call — a single-shot caller (`adopt`, `who`, `reap`) gets its real
    // verdict without a `--wait` loop, exactly as before.
    use _cache = new IssuesCache()
    use _fast = new NoMergeableRetryDelay()
    let server = MergeablePrServer(fun reads -> if reads = 1 then "null" else "true")

    let state, _, _ = Reads.prLandableRequire server.Recorder "FS-GG" "FS.GG.SDD" 801 [] None

    Assert.Equal(PrGreen, state)
    Assert.Equal(2, server.PrReads)

/// A PR whose `mergeable` token AND head SHA are both the caller's to choose — the two facts #955 is about.
/// `MergeablePrServer` above fixes the SHA at `sha-x`, which is exactly the degree of freedom these tests
/// need: the bug is a verdict computed for a commit that is NOT the one the caller pushed.
type private MergeablePrAtSha(token: string, headSha: string) =
    let mutable prReads = 0

    member _.PrReads = prReads

    member _.Recorder =
        Fake.Recorder(fun (req: Request) ->
            let body =
                if req.Path.EndsWith "pulls/801" then
                    prReads <- prReads + 1

                    $"""{{"number":801,"state":"open","mergeable":%s{token},"head":{{"ref":"item/42-x","sha":"%s{headSha}"}}}}"""
                elif req.Path.EndsWith "actions/runs" then
                    """{"total_count":1,"workflow_runs":[{"path":".github/workflows/b.yml","event":"pull_request","head_branch":"item/42-x","run_number":1,"status":"completed","conclusion":"success","check_suite_id":1,"pull_requests":[{"number":801}]}]}"""
                else
                    """{"total_count":1,"check_runs":[{"name":"build","check_suite":{"id":1},"status":"completed","conclusion":"success"}]}"""

            Ok { Status = 200; Body = body; ETag = None; NextLink = None; Headers = Map.empty })

[<Fact>]
let ``a CONFLICTED about the commit you replaced is pending, not a verdict — the false arm reaches --sha`` () =
    // #955, and the arm the head-SHA guard could not reach. `mergeable` is a fact about the commit GitHub
    // LAST EVALUATED; after a force-push `pulls/{n}` names the PREVIOUS commit for a window, and its `false`
    // is about code the caller has already replaced. The `false` arm was ordered first and bound the SHA to
    // `_`, so `--sha` — the flag that exists to say "gate on THIS commit" — could not reach it, and the
    // stale `false` was returned as TERMINAL.
    //
    // Live on PR #951: `landable --wait` said `conflicted`/3 while the API said `mergeable=true`, and the PR
    // merged cleanly minutes later. §5 defines 3 as "a conflicted PR needs a rebase", so the recipe's own
    // prescription against a stale `false` is to rebase the commit that caused it — a loop with no exit.
    use _cache = new IssuesCache()
    use _fast = new NoMergeableRetryDelay()
    let server = MergeablePrAtSha("false", "sha-OLD")

    let state, n, unmet =
        Reads.prLandableRequire server.Recorder "FS-GG" "FS.GG.SDD" 801 [] (Some "sha-NEW")

    Assert.Equal(PrPending, state)
    Assert.Equal(0, n)

    // The half that actually fixes the bug: a verdict that settled would return just as promptly as the
    // exit 3 it replaces. `--wait` must poll until GitHub re-points the PR at the pushed commit.
    Assert.False(
        Landable.settled state n 0,
        "a conflicted computed for a commit the caller did not push must NEVER settle — --wait must poll it"
    )

    // `--sha` is an assertion the CALLER added, so — unlike the #950 arm — it earns a reason on the unmet
    // channel. A `pending` that never resolves is otherwise one honest word and no thread to pull.
    Assert.Contains(
        unmet,
        fun (r: Reads.Unmet) ->
            match r with
            | Reads.Asserted reason -> reason.Contains "sha-OLD" && reason.Contains "sha-NEW"
            | _ -> false
    )

[<Fact>]
let ``a CONFLICTED about the commit you ASKED to gate is still terminal — the guard widens reach, not claim`` () =
    // THE COUNTERWEIGHT, and the one that stops the fix passing for the wrong reason. Demoting every `false`
    // to `pending` would satisfy the test above and destroy the verdict: a genuinely conflicted PR never gets
    // CI at all (GitHub cannot build `refs/pull/N/merge`), so `--wait` would poll to its cap and report
    // `pending` on a PR that needs a rebase — turning the one immediately-actionable verdict into a timeout.
    use _cache = new IssuesCache()
    use _fast = new NoMergeableRetryDelay()
    let server = MergeablePrAtSha("false", "sha-NEW")

    let state, _, _ =
        Reads.prLandableRequire server.Recorder "FS-GG" "FS.GG.SDD" 801 [] (Some "sha-NEW")

    Assert.Equal(PrConflicted, state)
    Assert.True(Landable.settled state 0 0, "a conflict about the gated commit is real — it must settle")

[<Fact>]
let ``without --sha a conflicted stays terminal — an ASSERTION nobody made demotes nothing`` () =
    // The second counterweight, and the contract line. §5 prescribes `landable <pr> --wait`, which passes
    // `expected = None` and therefore asserts NOTHING about which commit it means. There is nothing to
    // reconcile against, so today's behaviour stands unchanged: this fix widens the guard's REACH (it now
    // governs `false` as well as `true`), never its CLAIM.
    //
    // That is the deliberate limit of the narrow repair. A caller that has just pushed KNOWS the SHA it
    // pushed and can say so; one that has not made the assertion is not second-guessed here.
    use _cache = new IssuesCache()
    use _fast = new NoMergeableRetryDelay()
    let server = MergeablePrAtSha("false", "sha-OLD")

    let state, _, _ = Reads.prLandableRequire server.Recorder "FS-GG" "FS.GG.SDD" 801 [] None

    Assert.Equal(PrConflicted, state)

[<Fact>]
let ``the false arm reconciles on ONE read budget — the guard costs no extra PR read`` () =
    // The reconciliation is hoisted ahead of the arms, so it is asked once over one bound read. Asking
    // `readMerge` again per-arm would be invisible to every verdict assertion above and would silently double
    // the PR reads of every caller — on the budget that carries the claim lock (ADR-0034 §3).
    use _cache = new IssuesCache()
    use _fast = new NoMergeableRetryDelay()
    let server = MergeablePrAtSha("false", "sha-OLD")

    Reads.prLandableRequire server.Recorder "FS-GG" "FS.GG.SDD" 801 [] (Some "sha-NEW")
    |> ignore

    // A definite `false` is not re-read (only `Computing` is), so the budget spends exactly one.
    Assert.Equal(1, server.PrReads)

/// A PR that is `mergeable: false` at `headSha`, whose BRANCH tip is `tipSha` — the two facts #989 turns into
/// a verdict. Counts the ref reads, because WHEN the extra REST read is spent is half of what #989 decides:
/// it is the whole cost of the fix, and it lands on the budget the claim lock lives on (ADR-0034 §3).
type private ConflictedPrWithBranch(headSha: string, tipSha: string) =
    let mutable refReads = 0

    member _.RefReads = refReads

    member _.Recorder =
        Fake.Recorder(fun (req: Request) ->
            let body =
                if req.Path.Contains "git/ref/heads/" then
                    refReads <- refReads + 1
                    $"""{{"ref":"refs/heads/item/42-x","object":{{"sha":"%s{tipSha}","type":"commit"}}}}"""
                elif req.Path.EndsWith "pulls/801" then
                    $"""{{"number":801,"state":"open","mergeable":false,"head":{{"ref":"item/42-x","sha":"%s{headSha}"}}}}"""
                else
                    """{"total_count":0,"workflow_runs":[]}"""

            Ok { Status = 200; Body = body; ETag = None; NextLink = None; Headers = Map.empty })

[<Fact>]
let ``a conflicted whose PR head LAGS the branch tip is pending — no --sha, and none needed`` () =
    // #989. #955 made the head-SHA guard reachable for a `false`, but only a caller passing `--sha` can reach
    // it — and §5 prescribes `landable <pr> --wait`, which passes none. So the repair could not fire on the
    // one path every worker runs.
    //
    // Asking the RECIPE to pass `--sha "$(git rev-parse HEAD)"` was #955's own recommendation and has a race
    // it did not see: a bot pushes to your item branch (lockfile-sync is a `workflow_call` reusable ending in
    // a bare push; feed-autofix triggers `on: pull_request` and force-pushes), and your worktree HEAD is then
    // not the PR's head — turning a TRANSIENT false `conflicted` into a PERMANENT false `pending`.
    //
    // So ask git. The push updated the ref synchronously; the PR object is re-pointed by a background job.
    // A tip that disagrees with `head.sha` is GitHub disagreeing with ITSELF — "has not caught up" measured,
    // not asserted, and immune to what any bot does to the branch.
    use _cache = new IssuesCache()
    use _fast = new NoMergeableRetryDelay()
    let server = ConflictedPrWithBranch(headSha = "sha-OLD", tipSha = "sha-NEW")

    let state, n, _ = Reads.prLandableRequire server.Recorder "FS-GG" "FS.GG.SDD" 801 [] None

    Assert.Equal(PrPending, state)
    Assert.Equal(0, n)

    // The half that fixes the bug: a verdict that settled would return exit 3 just as promptly.
    Assert.False(
        Landable.settled state n 0,
        "a conflicted whose PR head lags the branch tip must NEVER settle — --wait must poll it"
    )

[<Fact>]
let ``a conflicted whose PR head IS the branch tip stays terminal — a real conflict, still exit 3`` () =
    // THE COUNTERWEIGHT. Demoting every `false` to `pending` would satisfy the test above and destroy the
    // verdict: a genuinely conflicted PR never gets CI at all (GitHub cannot build `refs/pull/N/merge`), so
    // `--wait` would poll to its cap and report `pending` on a PR that needs a rebase — replacing an
    // immediately-actionable verdict with a timeout. An AGREEING tip means the PR names the branch's real
    // head, so its `false` is about the code that would actually be merged.
    use _cache = new IssuesCache()
    use _fast = new NoMergeableRetryDelay()
    let server = ConflictedPrWithBranch(headSha = "sha-SAME", tipSha = "sha-SAME")

    let state, _, _ = Reads.prLandableRequire server.Recorder "FS-GG" "FS.GG.SDD" 801 [] None

    Assert.Equal(PrConflicted, state)
    Assert.True(Landable.settled state 0 0, "a conflict about the branch's real head is real — it must settle")

[<Fact>]
let ``a branch tip we cannot READ leaves the conflict standing — fail-closed, not fail-pending`` () =
    // #266's rule, on the new read. An unreadable ref proves NOTHING, and "I could not look" must not become
    // "GitHub has not caught up" any more than it may become a green. The conflict stands, `--wait` stops,
    // and the worker is told to rebase — which is what they would have been told before #989 existed.
    use _cache = new IssuesCache()
    use _fast = new NoMergeableRetryDelay()

    let recorder =
        Fake.Recorder(fun (req: Request) ->
            if req.Path.Contains "git/ref/heads/" then
                // The ref read fails. It must not manufacture a verdict in either direction.
                Ok { Status = 404; Body = """{"message":"Not Found"}"""; ETag = None; NextLink = None; Headers = Map.empty }
            elif req.Path.EndsWith "pulls/801" then
                Ok
                    { Status = 200
                      Body =
                        """{"number":801,"state":"open","mergeable":false,"head":{"ref":"item/42-x","sha":"sha-x"}}"""
                      ETag = None
                      NextLink = None; Headers = Map.empty }
            else
                Ok { Status = 200; Body = """{"total_count":0,"workflow_runs":[]}"""; ETag = None; NextLink = None; Headers = Map.empty })

    let state, _, _ = Reads.prLandableRequire recorder "FS-GG" "FS.GG.SDD" 801 [] None

    Assert.Equal(PrConflicted, state)

[<Fact>]
let ``--sha ANSWERS the question, so the branch tip is never read — the caller does not pay twice`` () =
    // The cost half of #989, and the reason the ref read is bound past the `--sha` early return rather than
    // inside the match arm. A caller that passed `--sha` has already been answered — here it AGREES with the
    // PR head, so the conflict is about the commit they named and is terminal. Reading the branch tip to
    // second-guess an assertion the caller already made would spend a REST request, on the budget the claim
    // lock lives on, to re-answer a settled question.
    use _cache = new IssuesCache()
    use _fast = new NoMergeableRetryDelay()
    let server = ConflictedPrWithBranch(headSha = "sha-SAME", tipSha = "sha-MOVED-ON")

    let state, _, _ =
        Reads.prLandableRequire server.Recorder "FS-GG" "FS.GG.SDD" 801 [] (Some "sha-SAME")

    Assert.Equal(PrConflicted, state)
    Assert.Equal(0, server.RefReads)

[<Fact>]
let ``the green path reconciles ONCE — #989's bound, as #995 revised it`` () =
    // THE COST BOUND ON THE HOT PATH, and it MOVED — deliberately, so this test moved with it.
    //
    // #989 asserted 0 here: the ref read was for the `false` arm, and a `true` was answered from the PR
    // object alone. #995 found that #989's own evidence condemned the `true` arm too — inside the force-push
    // window `pulls/{n}` names the REPLACED commit and its checks are green, so scoring them merges untested
    // code. That fails OPEN, which #955's `false` did not, so the guard had to reach here as well.
    //
    // The bound is now ONE, not zero, and one is what makes it affordable: `--wait` stops at the first
    // settling verdict, so the green arm is reached once per invocation — one REST request per merge, never
    // one per poll. If this ever reads twice, the guard has migrated onto the polling path and every worker
    // pays it on every item, on the budget the claim lock lives on (ADR-0034 §3).
    use _cache = new IssuesCache()
    use _fast = new NoMergeableRetryDelay()
    let mutable refReads = 0

    let recorder =
        Fake.Recorder(fun (req: Request) ->
            let body =
                if req.Path.Contains "git/ref/heads/" then
                    refReads <- refReads + 1
                    """{"ref":"refs/heads/item/42-x","object":{"sha":"sha-x","type":"commit"}}"""
                elif req.Path.EndsWith "pulls/801" then
                    """{"number":801,"state":"open","mergeable":true,"head":{"ref":"item/42-x","sha":"sha-x"}}"""
                elif req.Path.EndsWith "actions/runs" then
                    """{"total_count":1,"workflow_runs":[{"path":".github/workflows/b.yml","event":"pull_request","head_branch":"item/42-x","run_number":1,"status":"completed","conclusion":"success","check_suite_id":1,"pull_requests":[{"number":801}]}]}"""
                else
                    """{"total_count":1,"check_runs":[{"name":"build","check_suite":{"id":1},"status":"completed","conclusion":"success"}]}"""

            Ok { Status = 200; Body = body; ETag = None; NextLink = None; Headers = Map.empty })

    let state, _, _ = Reads.prLandableRequire recorder "FS-GG" "FS.GG.SDD" 801 [] None

    // The fixture's ref tip AGREES with the PR head, so the green stands — reconciled, not doubted.
    Assert.Equal(PrGreen, state)
    Assert.Equal(1, refReads)

/// A GREEN, mergeable PR at `headSha` whose BRANCH tip is `tipSha` — the force-push window, on the arm that
/// merges. Counts ref reads: WHEN this read is spent is the whole cost of #995, and it lands on the budget
/// the claim lock lives on (ADR-0034 §3).
type private GreenPrWithBranch(headSha: string, tipSha: string, conclusion: string) =
    let mutable refReads = 0

    member _.RefReads = refReads

    member _.Recorder =
        Fake.Recorder(fun (req: Request) ->
            let body =
                if req.Path.Contains "git/ref/heads/" then
                    refReads <- refReads + 1
                    $"""{{"ref":"refs/heads/item/42-x","object":{{"sha":"%s{tipSha}","type":"commit"}}}}"""
                elif req.Path.EndsWith "pulls/801" then
                    $"""{{"number":801,"state":"open","mergeable":true,"head":{{"ref":"item/42-x","sha":"%s{headSha}"}}}}"""
                elif req.Path.EndsWith "actions/runs" then
                    $"""{{"total_count":1,"workflow_runs":[{{"path":".github/workflows/b.yml","event":"pull_request","head_branch":"item/42-x","run_number":1,"status":"completed","conclusion":"%s{conclusion}","check_suite_id":1,"pull_requests":[{{"number":801}}]}}]}}"""
                else
                    $"""{{"total_count":1,"check_runs":[{{"name":"build","check_suite":{{"id":1}},"status":"completed","conclusion":"%s{conclusion}"}}]}}"""

            Ok { Status = 200; Body = body; ETag = None; NextLink = None; Headers = Map.empty })

[<Fact>]
let ``a GREEN scored over the commit you REPLACED is pending, not green — the fail-OPEN twin of #955`` () =
    // #995, epic #266. #955/#989 repaired this stale head on the `false` arm, where it failed CLOSED. Here it
    // fails OPEN: `pulls/{n}` names the pre-rebase commit for a beat after a force-push, its runs are
    // COMPLETE and GREEN, and they are not about the code that would be merged. Score them and `--wait`
    // settles green on its FIRST read; §5 merges, and the merge takes the PR's head at merge time — which by
    // then is the NEW commit. New code, merged on the dead commit's checks.
    //
    // Measured on PR #993: `ref-tip=NEW  pr.head=OLD  mergeable=true` the instant the push returned.
    use _cache = new IssuesCache()
    use _fast = new NoMergeableRetryDelay()
    let server = GreenPrWithBranch(headSha = "sha-OLD", tipSha = "sha-NEW", conclusion = "success")

    let state, n, _ = Reads.prLandableRequire server.Recorder "FS-GG" "FS.GG.SDD" 801 [] None

    Assert.Equal(PrPending, state)

    Assert.False(
        Landable.settled state n 0,
        "a green scored over a replaced commit must NEVER settle — merging it is the bug"
    )

[<Fact>]
let ``a GREEN whose PR head IS the branch tip still settles green — the gate must still let work land`` () =
    // THE COUNTERWEIGHT, and the one that matters most: this arm is how EVERY item merges. Demoting greens
    // would satisfy the test above and break the entire protocol — `--wait` would poll finished work to its
    // cap and §5's `|| exit 1` would walk every worker away from every item.
    use _cache = new IssuesCache()
    use _fast = new NoMergeableRetryDelay()
    let server = GreenPrWithBranch(headSha = "sha-SAME", tipSha = "sha-SAME", conclusion = "success")

    let state, _, _ = Reads.prLandableRequire server.Recorder "FS-GG" "FS.GG.SDD" 801 [] None

    Assert.Equal(PrGreen, state)

    // `settled` for a green is `n > 0 && n = prev` — the run set must have STOPPED GROWING (#724), so the
    // steady-state poll is the one that lands. Passing prev=0 here would assert the FIRST poll settles, which
    // it must not.
    Assert.True(Landable.settled state 1 1, "a green about the branch's real head is finished work — land it")

    // Exactly ONE ref read: the green arm is reached once per `--wait` invocation (it stops at the first
    // settling verdict), so the guard costs one REST request per merge, not one per poll.
    Assert.Equal(1, server.RefReads)

[<Fact>]
let ``a RED never reads the ref — a PR that is not merging need not prove which commit it is`` () =
    // The cost bound, downward. The guard exists to stop a GREEN being believed about the wrong commit; a red
    // is already not merging, so reconciling it buys nothing and would spend a request on every failing poll.
    use _cache = new IssuesCache()
    use _fast = new NoMergeableRetryDelay()
    let server = GreenPrWithBranch(headSha = "sha-OLD", tipSha = "sha-NEW", conclusion = "failure")

    let state, _, _ = Reads.prLandableRequire server.Recorder "FS-GG" "FS.GG.SDD" 801 [] None

    Assert.Equal(PrRed, state)
    Assert.Equal(0, server.RefReads)

[<Fact>]
let ``--sha suppresses the green guard too — the caller asserted the commit and was answered for free`` () =
    // Consistent with #989's `false` arm. A caller passing `--sha` is reconciled ABOVE at no cost: a mismatch
    // is already `pending`, so reaching here means their SHA agrees with the PR head and they have had their
    // answer. Reading the ref to second-guess an assertion they made themselves would spend a request on the
    // budget the claim lock lives on, to re-answer a settled question.
    use _cache = new IssuesCache()
    use _fast = new NoMergeableRetryDelay()
    let server = GreenPrWithBranch(headSha = "sha-SAME", tipSha = "sha-MOVED-ON", conclusion = "success")

    let state, _, _ =
        Reads.prLandableRequire server.Recorder "FS-GG" "FS.GG.SDD" 801 [] (Some "sha-SAME")

    Assert.Equal(PrGreen, state)
    Assert.Equal(0, server.RefReads)

[<Fact>]
let ``a ref we cannot read leaves the GREEN standing — fail-closed must not strand every merge`` () =
    // #266's rule, pointed the other way. On the `false` arm an unreadable ref leaves the conflict standing;
    // here it must leave the GREEN standing. A `pending` manufactured from a read we could not make would
    // strand every worker whose ref read 404s — the guard failing open in the opposite direction, and a gate
    // that blocks all correct work is not safer than one that blocks none.
    use _cache = new IssuesCache()
    use _fast = new NoMergeableRetryDelay()

    let recorder =
        Fake.Recorder(fun (req: Request) ->
            if req.Path.Contains "git/ref/heads/" then
                Ok { Status = 404; Body = """{"message":"Not Found"}"""; ETag = None; NextLink = None; Headers = Map.empty }
            elif req.Path.EndsWith "pulls/801" then
                Ok
                    { Status = 200
                      Body =
                        """{"number":801,"state":"open","mergeable":true,"head":{"ref":"item/42-x","sha":"sha-x"}}"""
                      ETag = None
                      NextLink = None; Headers = Map.empty }
            elif req.Path.EndsWith "actions/runs" then
                Ok
                    { Status = 200
                      Body =
                        """{"total_count":1,"workflow_runs":[{"path":".github/workflows/b.yml","event":"pull_request","head_branch":"item/42-x","run_number":1,"status":"completed","conclusion":"success","check_suite_id":1,"pull_requests":[{"number":801}]}]}"""
                      ETag = None
                      NextLink = None; Headers = Map.empty }
            else
                Ok
                    { Status = 200
                      Body =
                        """{"total_count":1,"check_runs":[{"name":"build","check_suite":{"id":1},"status":"completed","conclusion":"success"}]}"""
                      ETag = None
                      NextLink = None; Headers = Map.empty })

    let state, _, _ = Reads.prLandableRequire recorder "FS-GG" "FS.GG.SDD" 801 [] None

    Assert.Equal(PrGreen, state)

// ---- #1575: a required context that never REPORTED is not a passing one ----------------------------
//
// `landable` returned `green`, exit 0, for FS.GG.Rendering#1027 — which GitHub then refused to merge
// ("the base branch policy prohibits the merge"). `mergeable=MERGEABLE`, `mergeStateStatus=BLOCKED`, all
// 18 reporting check runs SUCCESS, and the required context `skill-union / skill-union` had NO CHECK RUN
// AT ALL: the workflow that produces it was armed on `main` after that PR's head was pushed, so GitHub
// never created the run. A context that never reports is not a context that fails, and an "is anything
// red?" rollup cannot see the difference (#606).
//
// #1575 PRESCRIBED DERIVING THE REQUIRED SET FROM `branches/{b}/protection`. That read needs
// `administration: read`, which is not a valid `permissions:` scope for a workflow's GITHUB_TOKEN at all
// — and `landable`'s unattended caller runs entirely under one. A verdict resting on it would 403 there
// forever: #463, where a protection probe 403'd on every receiver and stopped the kit landing anywhere.
// So the VERDICT comes from `mergeable_state`, which rides in the PR object already read, and the policy
// read is DIAGNOSIS that is allowed to fail. The `403 still refuses` leg below is that whole argument.

/// The two stores GitHub keeps required status checks in — the DIAGNOSTIC read, reached only when GitHub
/// has already said it will refuse.
let private protectionRequiring (contexts: string list) =
    let checks =
        contexts
        |> List.map (fun c -> $"""{{"context":"%s{c}","app_id":15368}}""")
        |> String.concat ","

    $"""{{"required_status_checks":{{"strict":false,"checks":[%s{checks}]}}}}"""

let private isProtectionRead (path: string) =
    path.Contains "/branches/" && path.EndsWith "/protection"

let private isRulesetRead (path: string) = path.Contains "/rules/branches/"

/// A green, mergeable PR into `main` at `sha-head` whose branch tip agrees (so the #995 guard is
/// satisfied), carrying whatever `mergeable_state` the test names and whatever check runs it lists.
/// Counts the POLICY reads, so the cost bound is measured rather than asserted.
type private MergeStatePrServer
    (
        state: string,
        reported: string list,
        ?demanded: string list,
        ?rules: string,
        ?protection: IoError,
        ?conclusion: string
    ) =
    let mutable policyReads = 0
    let concl = defaultArg conclusion "success"

    member _.PolicyReads = policyReads

    member _.Recorder =
        Fake.Recorder(fun (req: Request) ->
            if isProtectionRead req.Path then
                policyReads <- policyReads + 1

                match protection with
                | Some e -> Error e
                | None ->
                    Ok
                        { Status = 200
                          Body = protectionRequiring (defaultArg demanded [])
                          ETag = None
                          NextLink = None; Headers = Map.empty }
            elif isRulesetRead req.Path then
                policyReads <- policyReads + 1
                Ok { Status = 200; Body = defaultArg rules "[]"; ETag = None; NextLink = None; Headers = Map.empty }
            else

            let body =
                if req.Path.Contains "git/ref/heads/" then
                    """{"ref":"refs/heads/item/42-x","object":{"sha":"sha-head","type":"commit"}}"""
                elif req.Path.EndsWith "pulls/801" then
                    // `mergeable_state` rides in the SAME object as `mergeable` — same lazy background
                    // job, same request. That is what makes the guard free.
                    $"""{{"number":801,"state":"open","mergeable":true,"mergeable_state":"%s{state}","base":{{"ref":"main"}},"head":{{"ref":"item/42-x","sha":"sha-head"}}}}"""
                elif req.Path.EndsWith "actions/runs" then
                    $"""{{"total_count":1,"workflow_runs":[{{"path":".github/workflows/gate.yml","event":"pull_request","head_branch":"item/42-x","run_number":1,"status":"completed","conclusion":"%s{concl}","check_suite_id":1,"pull_requests":[{{"number":801}}]}}]}}"""
                else
                    let runs =
                        reported
                        |> List.map (fun n ->
                            $"""{{"name":"%s{n}","check_suite":{{"id":1}},"status":"completed","conclusion":"%s{concl}"}}""")
                        |> String.concat ","

                    $"""{{"total_count":%d{List.length reported},"check_runs":[%s{runs}]}}"""

            Ok { Status = 200; Body = body; ETag = None; NextLink = None; Headers = Map.empty })

[<Fact>]
let ``#1575 a BLOCKED PR whose reporting checks are all green is pending — the green GitHub refused`` () =
    use _cache = new IssuesCache()
    use _fast = new NoMergeableRetryDelay()

    let server =
        MergeStatePrServer(
            state = "blocked",
            reported = [ "build"; "test" ],
            demanded = [ "skill-union / skill-union" ]
        )

    let state, _, unmet = Reads.prLandableRequire server.Recorder "FS-GG" "FS.GG.SDD" 801 [] None

    Assert.Equal(PrPending, state)

    // The refusal is GitHub's own, and it is typed apart from `--require`'s assertions: an operator must
    // not be told that the base branch's policy is something they asked for (AC2).
    match unmet with
    | Reads.Refused("blocked", "main") :: rest ->
        // ...and the DIAGNOSIS names the context, so `pending` is not one word with no thread to pull.
        match rest with
        | [ Reads.NotReported("skill-union / skill-union", "main") ] -> ()
        | other -> failwith $"expected the unreported context to be named — got %A{other}"
    | other -> failwith $"expected a Refused naming the state and the base branch first — got %A{other}"

    // AC5: `--wait` must WAIT here. `pending` never settles, so the loop rides out the registration case
    // and refuses the permanent one — where the defect returned immediately, because from the rollup's
    // point of view nothing was pending.
    Assert.False(Landable.settled state 2 2, "a refused merge must NEVER settle — --wait must poll it")

[<Fact>]
let ``#1575 ...and the SAME world reported CLEAN is green — the gate must still let work land`` () =
    // THE COUNTERWEIGHT, and the one that matters most: this arm is how every item merges.
    use _cache = new IssuesCache()
    use _fast = new NoMergeableRetryDelay()

    let server =
        MergeStatePrServer(
            state = "clean",
            reported = [ "build"; "test"; "skill-union / skill-union" ],
            demanded = [ "skill-union / skill-union" ]
        )

    let state, _, unmet = Reads.prLandableRequire server.Recorder "FS-GG" "FS.GG.SDD" 801 [] None

    Assert.Equal(PrGreen, state)
    Assert.Empty unmet

    // And the merge path pays NOTHING for the guard. `mergeable_state` was already in the PR object; the
    // policy read happens only where GitHub has said it will refuse.
    Assert.Equal(0, server.PolicyReads)

[<Fact>]
let ``#1575 a policy we may not READ still REFUSES — the verdict never rested on it (#463)`` () =
    // THE LOAD-BEARING LEG, and the reason this does not derive the verdict from branch protection.
    // Reading `branches/{b}/protection` needs `administration: read`, which is not a valid `permissions:`
    // scope for a workflow's GITHUB_TOKEN — and `landable`'s unattended caller runs entirely under one.
    // A verdict resting on that read would 403 forever, which is #463: a protection probe that 403'd on
    // every receiver, fell through to the fail-closed arm, and stopped the kit landing ANYWHERE. A gate
    // that fails closed on a question nobody can answer does not fail closed; it fails always.
    //
    // So the 403 costs a SENTENCE, not a verdict. The refusal still stands, on GitHub's own word.
    use _cache = new IssuesCache()
    use _fast = new NoMergeableRetryDelay()

    let server =
        MergeStatePrServer(
            state = "blocked",
            reported = [ "build" ],
            protection = Unauthorized "FS-GG/FS.GG.SDD branch main protection"
        )

    let state, _, unmet = Reads.prLandableRequire server.Recorder "FS-GG" "FS.GG.SDD" 801 [] None

    Assert.Equal(PrPending, state)

    match unmet with
    | [ Reads.Refused("blocked", "main"); Reads.PolicyUnreadable why ] ->
        Assert.Contains("administration: read", why)
    | other -> failwith $"a 403 must degrade the SENTENCE and leave the refusal standing — got %A{other}"

[<Fact>]
let ``#1575 a RULESET's required check is named too — protection and rulesets are two stores`` () =
    // #574's lesson, on the diagnosis. `branches/<b>/protection` does not report ruleset rules and
    // `rules/branches/<b>` does not report classic protection; a branch may be governed by either.
    use _cache = new IssuesCache()
    use _fast = new NoMergeableRetryDelay()

    let server =
        MergeStatePrServer(
            state = "blocked",
            reported = [ "build" ],
            demanded = [],
            rules =
                """[{"type":"required_status_checks","parameters":{"required_status_checks":[{"context":"coherence","integration_id":15368}]}}]"""
        )

    let _, _, unmet = Reads.prLandableRequire server.Recorder "FS-GG" "FS.GG.SDD" 801 [] None

    Assert.Contains(
        unmet,
        fun (r: Reads.Unmet) ->
            match r with
            | Reads.NotReported("coherence", "main") -> true
            | _ -> false
    )

[<Fact>]
let ``#1575 UNSTABLE is not a refusal — a non-required check failing is a merge GitHub takes`` () =
    // The allow-list is of REFUSALS, not of permissions. `unstable` means a NON-required check failed,
    // which GitHub merges; treating it as a refusal would demote a landable PR on GitHub's own reading.
    // (Our own rollup reds a failing check first — a house rule stricter than GitHub's, and unchanged.)
    use _cache = new IssuesCache()
    use _fast = new NoMergeableRetryDelay()
    let server = MergeStatePrServer(state = "unstable", reported = [ "build" ])

    let state, _, _ = Reads.prLandableRequire server.Recorder "FS-GG" "FS.GG.SDD" 801 [] None

    Assert.Equal(PrGreen, state)
    Assert.Equal(0, server.PolicyReads)

[<Fact>]
let ``#1575 an ABSENT mergeable_state is NO OPINION, not a refusal — it must not strand every caller`` () =
    // The compatibility half of the allow-list. `mergeable_state` is not a permission-gated read; it is
    // part of a body we already hold. A payload that omits it is not github.com, and manufacturing a
    // refusal from it would demote every verdict against such a payload — the fail-always direction.
    use _cache = new IssuesCache()
    use _fast = new NoMergeableRetryDelay()

    let recorder =
        Fake.Recorder(fun (req: Request) ->
            if isProtectionRead req.Path || isRulesetRead req.Path then
                failwith "a PR GitHub has no opinion about must not cost a policy read"
            elif req.Path.Contains "git/ref/heads/" then
                Ok
                    { Status = 200
                      Body = """{"ref":"refs/heads/item/42-x","object":{"sha":"sha-head","type":"commit"}}"""
                      ETag = None
                      NextLink = None; Headers = Map.empty }
            elif req.Path.EndsWith "pulls/801" then
                Ok
                    { Status = 200
                      Body =
                        """{"number":801,"state":"open","mergeable":true,"base":{"ref":"main"},"head":{"ref":"item/42-x","sha":"sha-head"}}"""
                      ETag = None
                      NextLink = None; Headers = Map.empty }
            elif req.Path.EndsWith "actions/runs" then
                Ok
                    { Status = 200
                      Body =
                        """{"total_count":1,"workflow_runs":[{"path":".github/workflows/gate.yml","event":"pull_request","head_branch":"item/42-x","run_number":1,"status":"completed","conclusion":"success","check_suite_id":1,"pull_requests":[{"number":801}]}]}"""
                      ETag = None
                      NextLink = None; Headers = Map.empty }
            else
                Ok
                    { Status = 200
                      Body =
                        """{"total_count":1,"check_runs":[{"name":"build","check_suite":{"id":1},"status":"completed","conclusion":"success"}]}"""
                      ETag = None
                      NextLink = None; Headers = Map.empty })

    let state, _, _ = Reads.prLandableRequire recorder "FS-GG" "FS.GG.SDD" 801 [] None

    Assert.Equal(PrGreen, state)

[<Fact>]
let ``#1575 BEHIND and DRAFT refuse too — the other two states GitHub will not merge`` () =
    use _cache = new IssuesCache()
    use _fast = new NoMergeableRetryDelay()

    for refusing in [ "behind"; "draft" ] do
        let server = MergeStatePrServer(state = refusing, reported = [ "build" ])
        let state, _, unmet = Reads.prLandableRequire server.Recorder "FS-GG" "FS.GG.SDD" 801 [] None

        Assert.Equal(PrPending, state)

        Assert.Contains(
            unmet,
            fun (r: Reads.Unmet) ->
                match r with
                | Reads.Refused(s, "main") -> s = refusing
                | _ -> false
        )

[<Fact>]
let ``#1575/#2517 a RED that GitHub also refuses reads no policy — it is already not merging`` () =
    // The cost bound, MEASURED on the recorder actually being driven. #1575's diagnostic read happens only
    // where the rollup is otherwise green and GitHub has said it will refuse. #2517 added a SECOND reader of
    // the same endpoints — the derived advisory carve-out — and it is skipped on exactly this shape: a PR
    // GitHub reports `blocked` cannot be rescued by reclassifying a check, so neither pass asks.
    use _cache = new IssuesCache()
    use _fast = new NoMergeableRetryDelay()

    let server =
        MergeStatePrServer(state = "blocked", reported = [ "build" ], conclusion = "failure")

    let state, _, _ = Reads.prLandableRequire server.Recorder "FS-GG" "FS.GG.SDD" 801 [] None

    Assert.Equal(PrRed, state)
    Assert.Equal(0, server.PolicyReads)

[<Fact>]
let ``#1575 --require still binds a context the base branch does NOT require`` () =
    // The flag keeps its whole reason for existing (#737): `registry-coherence` decides the autofix bot's
    // PR and branch protection cannot require it, so nothing but this assertion will ever look at it.
    use _cache = new IssuesCache()
    use _fast = new NoMergeableRetryDelay()
    let server = MergeStatePrServer(state = "clean", reported = [ "build" ])

    let state, _, unmet =
        Reads.prLandableRequire server.Recorder "FS-GG" "FS.GG.SDD" 801 [ "registry-coherence" ] None

    Assert.Equal(PrPending, state)

    match unmet with
    | [ Reads.Asserted reason ] -> Assert.Contains("registry-coherence", reason)
    | other -> failwith $"a --require the caller named stays an ASSERTION, not a branch policy — got %A{other}"

// ---- .github#2517: the advisory carve-out is DERIVED, not a source literal ---------------------------
//
// `Landable`'s carve-out was a hand-written set of ONE name, so every OTHER non-required check reded the
// org's merge gate. Measured on this repo's PR #2514 at `f1d6218d775d278429cf6cea252b7d617ee3c723`: the six
// required contexts all passing, the non-required `feed` arm failing, `gh pr view --json mergeStateStatus`
// = `UNSTABLE` (GitHub itself permits the merge) — and `scripts/fsgg-coord landable 2514 --repo .github`
// answering `red`, refusing a fully reviewed, host-accepted PR by the org's own protocol.
//
// THE VERDICT STILL DOES NOT REST ON THE POLICY READ (#1575/#463), and these legs are where that is
// measured rather than asserted. The derivation is a SECOND pass, reached only when the fail-closed first
// pass is not already green, when it scored at least one subject, and when GitHub has not itself said it
// will refuse. So the merge path pays nothing (the `clean`/green leg above still asserts 0 policy reads), an
// unreadable policy scores exactly what it scored before (`administration: read` is not a valid workflow
// GITHUB_TOKEN scope, and `landable`'s unattended caller runs entirely under one), and an empty-but-readable
// policy is indistinguishable from an unreadable one.

/// A PR whose rollup is NOT green — one green workflow run with a passing `projection` check, and a failing
/// `feed` run whose only check-run is the failing `feed` — into a `main` whose required contexts the test
/// chooses. This is PR #2514's shape. Counts the POLICY reads, so the cost is measured on the recorder
/// actually being driven rather than argued for.
type private DerivedAdvisoryPrServer(state: string, ?demanded: string list, ?protection: IoError) =
    let mutable policyReads = 0

    member _.PolicyReads = policyReads

    member _.Recorder =
        Fake.Recorder(fun (req: Request) ->
            if isProtectionRead req.Path then
                policyReads <- policyReads + 1

                match protection with
                | Some e -> Error e
                | None ->
                    Ok
                        { Status = 200
                          Body = protectionRequiring (defaultArg demanded [])
                          ETag = None
                          NextLink = None; Headers = Map.empty }
            elif isRulesetRead req.Path then
                policyReads <- policyReads + 1
                Ok { Status = 200; Body = "[]"; ETag = None; NextLink = None; Headers = Map.empty }
            else

            let body =
                if req.Path.Contains "git/ref/heads/" then
                    """{"ref":"refs/heads/item/42-x","object":{"sha":"sha-head","type":"commit"}}"""
                elif req.Path.EndsWith "pulls/801" then
                    $"""{{"number":801,"state":"open","mergeable":true,"mergeable_state":"%s{state}","base":{{"ref":"main"}},"head":{{"ref":"item/42-x","sha":"sha-head"}}}}"""
                elif req.Path.EndsWith "actions/runs" then
                    """{"total_count":2,"workflow_runs":[
                         {"path":".github/workflows/coherence.yml","event":"pull_request","head_branch":"item/42-x","run_number":1,"status":"completed","conclusion":"success","check_suite_id":1,"pull_requests":[{"number":801}]},
                         {"path":".github/workflows/feed.yml","event":"pull_request","head_branch":"item/42-x","run_number":1,"status":"completed","conclusion":"failure","check_suite_id":2,"pull_requests":[{"number":801}]}]}"""
                else
                    """{"total_count":2,"check_runs":[
                         {"name":"projection","check_suite":{"id":1},"status":"completed","conclusion":"success"},
                         {"name":"feed","check_suite":{"id":2},"status":"completed","conclusion":"failure"}]}"""

            Ok { Status = 200; Body = body; ETag = None; NextLink = None; Headers = Map.empty })

[<Fact>]
let ``#2517 AC3: a PR whose ONLY failing check is NON-required is GREEN — PR #2514's own shape`` () =
    // THE ACCEPTANCE MEASUREMENT. `feed` is not among the branch's required contexts, so its failure — and
    // the failure of the run that contains nothing else — cannot hold this merge. Before #2517 this scored
    // `red` on the strength of one non-required arm, and a merge gate that reds on non-required checks is a
    // gate operators learn to override.
    use _cache = new IssuesCache()
    use _fast = new NoMergeableRetryDelay()
    let server = DerivedAdvisoryPrServer(state = "unstable", demanded = [ "projection" ])

    let state, n, unmet = Reads.prLandableRequire server.Recorder "FS-GG" "FS.GG.SDD" 801 [] None

    Assert.Equal(PrGreen, state)
    Assert.Equal(2, n)
    Assert.Empty unmet

    // The derivation is what produced that green, and it cost exactly the two reads of the two stores a
    // branch's required contexts can live in (#574) — paid ONLY because the first pass was not green.
    Assert.Equal(2, server.PolicyReads)

[<Fact>]
let ``#2517 AC4: the same PR is RED when the failing check IS required — the fix is not "always green"`` () =
    // The controlled counterpart. Same fixture, same failing check, one word different in the branch's own
    // declaration — and the gate refuses, exactly as it must.
    use _cache = new IssuesCache()
    use _fast = new NoMergeableRetryDelay()
    let server = DerivedAdvisoryPrServer(state = "unstable", demanded = [ "projection"; "feed" ])

    let state, _, _ = Reads.prLandableRequire server.Recorder "FS-GG" "FS.GG.SDD" 801 [] None

    Assert.Equal(PrRed, state)

[<Fact>]
let ``#2517 AC5: a policy we may not READ fails CLOSED — the verdict is what it was before the derivation`` () =
    // #463, NOT RESTORED. A verdict that RESTED on `branches/{b}/protection` would 403 forever under the
    // unattended caller's GITHUB_TOKEN and stop the kit landing anywhere. Failing closed here means "nothing
    // is advisory", which is precisely what this PR scored before #2517 — so an unreadable policy costs a
    // request, never a merge, and the derivation can only ever widen what lands.
    use _cache = new IssuesCache()
    use _fast = new NoMergeableRetryDelay()

    let server =
        DerivedAdvisoryPrServer(
            state = "unstable",
            demanded = [ "projection" ],
            protection = Unauthorized "FS-GG/FS.GG.SDD branch main protection"
        )

    let state, _, _ = Reads.prLandableRequire server.Recorder "FS-GG" "FS.GG.SDD" 801 [] None

    Assert.Equal(PrRed, state)
    // ONE read, not two: `requiredContexts` short-circuits on the classic store's failure — a list we
    // already cannot complete is not worth a second request.
    Assert.Equal(1, server.PolicyReads)

[<Fact>]
let ``#2517 AC6: an EMPTY required set fails closed on the SAME rule as an unreadable one`` () =
    // THE SHARPEST HAZARD, and it arrives through a SUCCESSFUL read: `classicRequired` answers `Ok []` for a
    // 404 and for "protected, but not on status checks", and the union of the two stores has no non-empty
    // guard. Complement-of-empty is everything, so a naive derivation would make EVERY check advisory and
    // score `landable` green on any repository with no branch protection at all — a fleet-wide fail-open
    // strictly worse than the defect #2517 repairs. `Landable.advisoryFrom` refuses to build a derivation
    // from an empty set, so this scores what an unreadable policy scores.
    //
    // At THIS layer the assertion is that the empty read is WIRED to that guard; the gate-inversion — a
    // fixture whose verdict flips from red to green when the guard is deleted — is held in
    // `LandableTests`, where the rule lives and where a suite of exactly two runs on two suites can be
    // shaped to make the mutation visible instead of masked by #606's zero-subjects rule.
    use _cache = new IssuesCache()
    use _fast = new NoMergeableRetryDelay()
    let server = DerivedAdvisoryPrServer(state = "unstable", demanded = [])

    let state, _, _ = Reads.prLandableRequire server.Recorder "FS-GG" "FS.GG.SDD" 801 [] None

    Assert.Equal(PrRed, state)
    Assert.Equal(2, server.PolicyReads)

[<Fact>]
let ``#2517 AC2: --require overrides the derivation — the flag still binds a non-required check`` () =
    // #737's flag is the autofix bot's whole reason for calling this command: `registry-coherence` decides
    // that bot's PR and branch protection cannot require it. A derivation that silently overrode `--require`
    // would break the one caller the flag exists for, so the flag is tested BEFORE the derivation.
    use _cache = new IssuesCache()
    use _fast = new NoMergeableRetryDelay()
    let server = DerivedAdvisoryPrServer(state = "unstable", demanded = [ "projection" ])

    let state, _, _ = Reads.prLandableRequire server.Recorder "FS-GG" "FS.GG.SDD" 801 [ "feed" ] None

    Assert.Equal(PrRed, state)

[<Fact>]
let ``#2517: a red GitHub itself REFUSES pays no policy read — the derivation cannot rescue it`` () =
    // THE COST BOUND, RE-MEASURED. Before #2517 the rule was "a red never reads the policy". It is now the
    // sharper one: a PR GitHub reports `blocked`/`behind`/`draft` is not landable whatever the derivation
    // says, so the second pass is not attempted at all.
    //
    // AND THAT GUARD IS A CORRECTNESS PROPERTY, NOT ONLY A COST ONE. The derived set is compared by NAME,
    // and a required CONTEXT is not always a check-run name — one satisfied by a legacy commit status, or
    // naming a job since renamed, matches nothing on the head. Treating such a check as advisory would drop
    // a genuinely required finding and fail OPEN. But a required context with no check run on the head is
    // exactly GitHub's `blocked` (#1575's own measurement), so that PR never reaches the derivation: every
    // PR that does has already had its required contexts confirmed satisfied by GitHub itself.
    use _cache = new IssuesCache()
    use _fast = new NoMergeableRetryDelay()
    let server = DerivedAdvisoryPrServer(state = "blocked", demanded = [ "projection" ])

    let state, _, _ = Reads.prLandableRequire server.Recorder "FS-GG" "FS.GG.SDD" 801 [] None

    Assert.Equal(PrRed, state)
    Assert.Equal(0, server.PolicyReads)

// ---- #1680: a PR that is NOT OPEN ------------------------------------------------------------------
//
// THE MEASUREMENT THIS BLOCK PINS. `landable 1675 --repo .github --wait` — on a PR merged as `d52362c`,
// whose head `a8f446d` carried 30 check-runs, 30 `success`, zero pending, zero failing — spent its entire
// default budget (30 tries x 20s = 600s) and answered `pending`, exit 7: the ONE code the exit contract
// defines as worth retrying, returned for the most terminal state GitHub has. `landable 1669` (merged as
// `3fc96ca`) answered the same, so it was general to merged PRs, not a property of one.
//
// THE MECHANISM, MEASURED RATHER THAN ASSUMED. #1680's body reasoned that `pending` was produced "above
// `score`, most plausibly on the must-have-reported path #1575 added". It is not. GitHub reports
// `mergeable: null` and `mergeable_state: "unknown"` for a merged PR (confirmed on #1675 over REST:
// `state=closed merged=true mergeable=null`), so `prFacts` reads `Computing`, and #950's arm — correctly,
// for the case it was written for — maps a `null` that outlives the re-read budget to `PrPending`. The
// must-have-reported path is never reached, because the runs are never read. That matters for the fix: no
// change to `Landable.scoreRequired` could have repaired this, which is also why the issue's own declared
// `Paths:` could not hold it.

/// A PR whose `state`/`merged` the caller chooses, counting reads PER PATH. The counts are the point: a
/// closed PR must cost ONE PR read (no re-read budget spent on a background job that will never run again)
/// and ZERO runs/check-runs reads (there is nothing to score).
type private ClosedPrServer(state: string, merged: bool) =
    let mutable prReads = 0
    let mutable otherReads = 0

    member _.PrReads = prReads
    member _.OtherReads = otherReads

    member _.Recorder =
        Fake.Recorder(fun (req: Request) ->
            let body =
                if req.Path.EndsWith "pulls/801" then
                    prReads <- prReads + 1
                    // Shaped exactly as GitHub answers a closed PR: `mergeable` is null and
                    // `mergeable_state` is "unknown". This is the body that used to score `pending`.
                    $"""{{"number":801,"state":"%s{state}","merged":%b{merged},"mergeable":null,"mergeable_state":"unknown","head":{{"ref":"item/42-x","sha":"sha-x"}},"base":{{"ref":"main"}}}}"""
                else
                    otherReads <- otherReads + 1
                    """{"total_count":0,"workflow_runs":[]}"""

            Ok { Status = 200; Body = body; ETag = None; NextLink = None; Headers = Map.empty })

[<Fact>]
let ``#1680 AC1 a MERGED pr is NOT pending and NOT exit 7`` () =
    use _cache = new IssuesCache()
    use _fast = new NoMergeableRetryDelay()
    let server = ClosedPrServer("closed", true)

    let state, n, unmet = Reads.prLandableRequire server.Recorder "FS-GG" "FS.GG.SDD" 801 [] None

    // AC1, asserted as the issue words it: whatever it returns, it must not be the retryable verdict.
    Assert.NotEqual(PrPending, state)
    // AC2: and it must NAME merged-ness, so a caller can tell "already landed" from "checks still
    // running" without a second REST read.
    Assert.Equal(PrMerged, state)
    Assert.Equal("merged", Landable.name state)
    Assert.Equal(0, n)
    Assert.Empty unmet

[<Fact>]
let ``#1680 AC3 --wait never polls a merged pr — it settles, and costs ONE read`` () =
    use _cache = new IssuesCache()
    use _fast = new NoMergeableRetryDelay()
    let server = ClosedPrServer("closed", true)

    let state, n, _ = Reads.prLandableRequire server.Recorder "FS-GG" "FS.GG.SDD" 801 [] None

    // The 600s: `Client.landable`'s poll loop consults `settled` and nothing else, so this IS "--wait does
    // not poll it". `prev = 0` is deliberately the value a first poll passes.
    Assert.True(Landable.settled state n 0, "a merged PR must SETTLE — this is the 600s of --wait budget the issue measured")

    // The quieter wait, and the one a single-shot caller pays: GitHub stops computing mergeability once a
    // PR leaves `open`, so the bounded `mergeable` re-read (3 tries, ~1s apart) waits on a job that will
    // never run again. One read, not three.
    Assert.Equal(1, server.PrReads)

    // And the runs/check-runs are never read at all. There is no live check set on a merged PR, and the
    // three-read poll is the per-poll REST cost the whole fleet shares.
    Assert.Equal(0, server.OtherReads)

[<Fact>]
let ``#1680 AC4 a CLOSED-UNMERGED pr is decided too, and says so`` () =
    use _cache = new IssuesCache()
    use _fast = new NoMergeableRetryDelay()
    let server = ClosedPrServer("closed", false)

    let state, _, _ = Reads.prLandableRequire server.Recorder "FS-GG" "FS.GG.SDD" 801 [] None

    // NOT `PrMerged`: nothing landed, so a recovery path must not stamp the item done. The two states
    // share a shape and differ in the act they call for, which is why they are held apart.
    Assert.Equal(PrClosed, state)
    Assert.Equal("closed", Landable.name state)
    Assert.NotEqual(PrPending, state)
    Assert.True(Landable.settled state 0 0, "a closed PR cannot reopen by waiting")
    Assert.Equal(1, server.PrReads)
    Assert.Equal(0, server.OtherReads)

[<Fact>]
let ``#1680 AC5 the four fixtures side by side — open+green, open+pending, merged, closed-unmerged`` () =
    // The whole point of the row: FOUR distinct verdicts from four PR states, in one place, so a future
    // change that collapses any two of them fails here rather than in a worker's poll loop. Before this
    // fix, rows 2 and 3 were the SAME verdict and the SAME exit code.
    use _cache = new IssuesCache()
    use _fast = new NoMergeableRetryDelay()

    // open + green: a mergeable PR whose one run and one check-run both passed.
    let green = LandableServer "sha-green"
    Assert.Equal(PrGreen, Reads.prLandable green.Recorder "FS-GG" "FS.GG.SDD" 801)

    // open + pending: `mergeable` still computing past the re-read budget (#950).
    let pending = MergeablePrServer(fun _ -> "null")
    let pendingState, _, _ = Reads.prLandableRequire pending.Recorder "FS-GG" "FS.GG.SDD" 801 [] None
    Assert.Equal(PrPending, pendingState)

    // merged, and closed-unmerged.
    let mergedState, _, _ =
        Reads.prLandableRequire (ClosedPrServer("closed", true)).Recorder "FS-GG" "FS.GG.SDD" 801 [] None

    let closedState, _, _ =
        Reads.prLandableRequire (ClosedPrServer("closed", false)).Recorder "FS-GG" "FS.GG.SDD" 801 [] None

    Assert.Equal(PrMerged, mergedState)
    Assert.Equal(PrClosed, closedState)

    // All four DISTINCT — the property the issue is about. `merged` rendering as `pending` is the defect.
    let verdicts = [ PrGreen; pendingState; mergedState; closedState ] |> List.map Landable.name
    Assert.Equal<string list>([ "green"; "pending"; "merged"; "closed" ], verdicts)
    Assert.Equal<string list>(verdicts |> List.distinct, verdicts)

[<Fact>]
let ``#1680 an OPEN pr carrying merged:false is untouched — the guard reads state, not the flag alone`` () =
    // The regression guard on the new arm's REACH. Every open PR carries `"merged": false`, so a guard
    // keyed on that flag alone — or one that forgot to require `state = "closed"` — would divert every
    // healthy PR in the fleet to a terminal verdict and stop `--wait` working at all. The arm must fire on
    // CLOSED-ness and read `merged` only to choose between the two closed words.
    use _cache = new IssuesCache()
    use _fast = new NoMergeableRetryDelay()

    let recorder =
        Fake.Recorder(fun (req: Request) ->
            let body =
                if req.Path.EndsWith "pulls/801" then
                    """{"number":801,"state":"open","merged":false,"mergeable":true,"head":{"ref":"item/42-x","sha":"sha-green"}}"""
                elif req.Path.EndsWith "actions/runs" then
                    """{"total_count":1,"workflow_runs":[{"path":".github/workflows/b.yml","event":"pull_request","head_branch":"item/42-x","run_number":1,"status":"completed","conclusion":"success","check_suite_id":1,"pull_requests":[{"number":801}]}]}"""
                else
                    """{"total_count":1,"check_runs":[{"name":"build","check_suite":{"id":1},"status":"completed","conclusion":"success"}]}"""

            Ok { Status = 200; Body = body; ETag = None; NextLink = None; Headers = Map.empty })

    let state, n, _ = Reads.prLandableRequire recorder "FS-GG" "FS.GG.SDD" 801 [] None

    Assert.Equal(PrGreen, state)
    Assert.Equal(2, n)

[<Fact>]
let ``#1680 a PR body with no `merged` field at all is NOT read as merged`` () =
    // Fail-closed in the direction that matters. "Already landed" is the one verdict that tells a recovery
    // path to STAMP an item, so a malformed or minimal body must never manufacture it. An absent `merged`
    // reads false, and the verdict falls through to the ordinary open-PR scoring.
    use _cache = new IssuesCache()
    use _fast = new NoMergeableRetryDelay()

    let recorder =
        Fake.Recorder(fun (req: Request) ->
            let body =
                if req.Path.EndsWith "pulls/801" then
                    """{"number":801,"state":"closed","mergeable":null,"head":{"ref":"item/42-x","sha":"sha-x"}}"""
                else
                    """{"total_count":0,"workflow_runs":[]}"""

            Ok { Status = 200; Body = body; ETag = None; NextLink = None; Headers = Map.empty })

    let state, _, _ = Reads.prLandableRequire recorder "FS-GG" "FS.GG.SDD" 801 [] None

    Assert.NotEqual(PrMerged, state)
    Assert.Equal(PrClosed, state)

[<Fact>]
let ``#1680 a merged pr still REPORTS a --sha the caller named that is not what landed`` () =
    // The verdict is right and the fact is still owed. `--sha` is an assertion the caller made, and this
    // arm returns before the reconciliation that would ordinarily check it — so dropping it silently
    // would be #1680's own defect in miniature: an answer that is true while hiding the thing that was
    // asked. The verdict does NOT change (the PR is merged, and that is the answer); what changes is that
    // the caller is told the merge was not the commit they named.
    use _cache = new IssuesCache()
    use _fast = new NoMergeableRetryDelay()
    let server = ClosedPrServer("closed", true)

    let state, _, unmet =
        Reads.prLandableRequire server.Recorder "FS-GG" "FS.GG.SDD" 801 [] (Some "sha-SOMEONE-ELSE")

    Assert.Equal(PrMerged, state)

    match unmet with
    | [ Reads.Asserted reason ] ->
        Assert.Contains("sha-SOMEONE-ELSE", reason)
        Assert.Contains("sha-x", reason)
    | other -> failwith $"an unmet --sha on a merged PR must still be REPORTED — got %A{other}"

[<Fact>]
let ``#1680 a merged pr whose head IS the asserted sha reports nothing extra`` () =
    // The counterweight: agreement must stay silent, or every recovering worker who correctly names the
    // commit that landed is handed a warning about nothing.
    use _cache = new IssuesCache()
    use _fast = new NoMergeableRetryDelay()
    let server = ClosedPrServer("closed", true)

    let state, _, unmet = Reads.prLandableRequire server.Recorder "FS-GG" "FS.GG.SDD" 801 [] (Some "sha-x")

    Assert.Equal(PrMerged, state)
    Assert.Empty unmet

// ---- .github#2365: a present-but-null GraphQL node refuses, it does not throw -----------------------
//
// A ref GraphQL cannot resolve (e.g. the noncanonical owner/repo spelling `EHotwagner/S.I.R`, missing
// its trailing dot) answers `"repository": null` — PRESENT, not missing. `JsonElement.GetProperty` AND
// `TryGetProperty` both throw `InvalidOperationException` (never `NullReferenceException`) when called
// on a `Null`-kind element, because the element itself is a real JSON value of the wrong shape. The old
// catch list (`KeyNotFoundException`, `NullReferenceException`) covered a MISSING property, not a
// present NULL one, so the exception escaped the typed read and crashed the process instead of
// producing a refusal.

[<Fact>]
let ``#2365 recentCommentBodies refuses a null repository instead of throwing`` () =
    let transport = serving """{"data":{"repository":null}}"""

    match Reads.recentCommentBodies transport "EHotwagner" "S.I.R" 146 20 with
    | Error(Malformed _) -> ()
    | other -> failwith $"a null repository must refuse, not throw or succeed — got %A{other}"

[<Fact>]
let ``#2365 recentCommentBodies refuses a null issue`` () =
    let transport = serving """{"data":{"repository":{"issue":null}}}"""

    match Reads.recentCommentBodies transport "FS-GG" ".github" 2365 20 with
    | Error(Malformed _) -> ()
    | other -> failwith $"a null issue must refuse, not throw — got %A{other}"

[<Fact>]
let ``#2365 recentCommentBodies refuses null comments`` () =
    let transport = serving """{"data":{"repository":{"issue":{"comments":null}}}}"""

    match Reads.recentCommentBodies transport "FS-GG" ".github" 2365 20 with
    | Error(Malformed _) -> ()
    | other -> failwith $"null comments must refuse, not throw — got %A{other}"

[<Fact>]
let ``#2365 recentCommentBodies refuses a null element inside nodes`` () =
    let transport =
        serving """{"data":{"repository":{"issue":{"comments":{"nodes":[null,{"body":"hi"}]}}}}}"""

    match Reads.recentCommentBodies transport "FS-GG" ".github" 2365 20 with
    | Error(Malformed _) -> ()
    | other -> failwith $"a null node must refuse, not throw — got %A{other}"

[<Fact>]
let ``#2365 recentCommentBodies still reads real comments`` () =
    // The counterweight: the added null case must not shadow the working path.
    let transport =
        serving """{"data":{"repository":{"issue":{"comments":{"nodes":[{"body":"first"},{"body":"second"}]}}}}}"""

    match Reads.recentCommentBodies transport "FS-GG" ".github" 2365 20 with
    | Ok bodies -> Assert.Equal<string list>([ "first"; "second" ], bodies)
    | Error e -> failwith $"a well-formed comments page must still resolve — got %A{e}"

[<Fact>]
let ``#2365 subIssues refuses a null repository instead of throwing`` () =
    let transport = serving """{"data":{"repository":null}}"""

    match Reads.subIssues transport "EHotwagner" "S.I.R" 146 with
    | Error(Malformed _) -> ()
    | other -> failwith $"a null repository must refuse, not throw — got %A{other}"

[<Fact>]
let ``#2365 prClosingRef treats a null repository the same as an unreadable graph`` () =
    // #2365 CLOSED THE CRASH; `.github#2534` CLOSED THE ANSWER IT CRASHED INTO. This leg used to assert
    // `Ok None` — "this PR closes nothing" — for a null `repository`, on the reasoning that the read
    // "already folds an unreadable graph into `Ok None`". That fold was the defect: `verify-paths` reads
    // `Ok None` as "this PR implements no tracked item" and prints a GREEN skip, so an unreadable graph
    // was laundered into a passing touch-set verdict. The null case is still not a crash — it is now an
    // ERROR, which is what `Reads.fsi` documented all along.
    let transport = serving """{"data":{"repository":null}}"""

    match Reads.prClosingRef transport "EHotwagner" "S.I.R" 146 with
    | Error(Malformed(_, detail)) -> Assert.Contains("FAILED READ", detail)
    | other -> failwith $"a null repository is a failed read, not 'closes nothing' — got %A{other}"
