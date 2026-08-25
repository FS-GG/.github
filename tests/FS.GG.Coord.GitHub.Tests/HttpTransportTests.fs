module FS.GG.Coord.GitHub.Tests.HttpTransportTests

open System
open System.Collections.Generic
open System.Net
open System.Text
open System.Threading
open Xunit
open FS.GG.Coord.Types
open FS.GG.Coord.GitHub
open FS.GG.Coord.GitHub.Errors
open FS.GG.Coord.GitHub.Transport

/// THE ADAPTER THAT ACTUALLY SHIPS, AGAINST A SERVER THAT ACTUALLY ANSWERS.
///
/// Every other test in this suite drives `Fake.Recorder`. That is right — it is how the call counts, the
/// `$GH_LOG` grammar and the fail-closed rules are pinned without a network. But it leaves a hole exactly
/// where a hole is least affordable: **`HttpTransport` is the only component that ever talks to GitHub, and
/// until this file existed it had no tests at all.**
///
/// A faithful fake proves nothing about the real adapter. It can agree with itself perfectly while the
/// thing that ships is broken — which is this codebase's own thesis (a number that only reports what it
/// looked at is how "we agreed" and "we never checked" come to print the same sentence) reproduced inside
/// its own test suite.
///
/// So these tests stand up a REAL `HttpListener` and point the REAL adapter at it — using the CONFIGURABLE
/// API BASE that ADR-0040 C1 specified for exactly this purpose, and which nothing had yet used. What is
/// under test here is the wire: `Link: rel=next` pagination, `If-None-Match`/304, the typed GraphQL
/// payload, status classification, and the timeout.
type private Server() =
    let listener = new HttpListener()

    // Port 0 is not available to HttpListener, so take a free one from the OS and hand it back.
    let port =
        use probe = new Sockets.TcpListener(IPAddress.Loopback, 0)
        probe.Start()
        let p = (probe.LocalEndpoint :?> IPEndPoint).Port
        probe.Stop()
        p

    let prefix = $"http://127.0.0.1:%d{port}/"

    /// Every request the server saw — method, path+query, and the headers/body that matter. This is the
    /// assertion surface: what did the adapter actually PUT ON THE WIRE?
    let seen = List<string * string * string option * string>()

    let mutable handler: (HttpListenerRequest -> HttpListenerResponse -> unit) = fun _ _ -> ()

    do
        listener.Prefixes.Add prefix
        listener.Start()

        let loop () =
            while listener.IsListening do
                try
                    let ctx = listener.GetContext()

                    let body =
                        use reader = new IO.StreamReader(ctx.Request.InputStream)
                        reader.ReadToEnd()

                    lock seen (fun () ->
                        seen.Add(
                            ctx.Request.HttpMethod,
                            ctx.Request.Url.PathAndQuery,
                            ctx.Request.Headers.["If-None-Match"] |> Option.ofObj,
                            body
                        ))

                    handler ctx.Request ctx.Response

                    try
                        ctx.Response.Close()
                    with _ ->
                        ()
                with _ ->
                    ()

        let t = Thread(loop)
        t.IsBackground <- true
        t.Start()

    member _.Base = prefix.TrimEnd('/')
    member _.Port = port

    member _.Requests =
        lock seen (fun () -> List.ofSeq seen)

    member _.On(f) = handler <- f

    /// Write a JSON body with a status and optional headers.
    member _.Json (response: HttpListenerResponse) (status: int) (body: string) (headers: (string * string) list) =
        response.StatusCode <- status
        response.ContentType <- "application/json"

        for (k, v) in headers do
            response.Headers.Add(k, v)

        let bytes = Encoding.UTF8.GetBytes body
        response.ContentLength64 <- int64 bytes.Length
        response.OutputStream.Write(bytes, 0, bytes.Length)

    interface IDisposable with
        member _.Dispose() =
            try
                listener.Stop()
                (listener :> IDisposable).Dispose()
            with _ ->
                ()

let private get (path: string) =
    { Method = "GET"
      Path = path
      Query = []
      Body = NoBody
      Budget = Rest
      IfNoneMatch = None
      Subject = path }

// ---- pagination: the Link header ---------------------------------------------------------------------

[<Fact>]
let ``the adapter FOLLOWS Link rel=next and CONCATENATES the pages`` () =
    // A LOCK HAS NO HUNDRED-ISSUE LIMIT. The claim scan runs over the open-issue list, and a first page read
    // as the whole set is a scan that cannot see the markers past it — it would report those items FREE.
    //
    // The bash client got this from `gh --paginate`. The adapter has to earn it, and until now nothing
    // checked that it did.
    use server = new Server()

    server.On(fun req res ->
        if req.Url.PathAndQuery.Contains "page=2" then
            server.Json res 200 """[{"number":3}]""" []
        else
            server.Json
                res
                200
                """[{"number":1},{"number":2}]"""
                [ "Link", $"<%s{server.Base}/repos/o/r/issues?page=2>; rel=\"next\", <%s{server.Base}/x>; rel=\"last\"" ])

    use transport = new HttpTransport(server.Base, "t")
    let t = transport :> IGitHubTransport

    match t.Send(get "repos/o/r/issues") with
    | Ok response ->
        // BOTH PAGES, MERGED — not just the first, and not two documents glued end to end.
        Assert.Contains("\"number\":1", response.Body)
        Assert.Contains("\"number\":3", response.Body)

        use doc = Text.Json.JsonDocument.Parse response.Body
        Assert.Equal(3, doc.RootElement.GetArrayLength())

        // And it really made two round trips.
        Assert.Equal(2, List.length server.Requests)

    | Error e -> failwith $"pagination must succeed — got %A{e}"

[<Fact>]
let ``#2905 the adapter also paginates GitHub wrapper collections without losing runs`` () =
    use server = new Server()

    server.On(fun req res ->
        if req.Url.PathAndQuery.Contains "page=2" then
            server.Json
                res
                200
                """{"total_count":2,"workflow_runs":[{"id":2,"conclusion":"failure"}]}"""
                []
        else
            server.Json
                res
                200
                """{"total_count":2,"workflow_runs":[{"id":1,"conclusion":"success"}]}"""
                [ "Link", $"<%s{server.Base}/repos/o/r/actions/runs?page=2>; rel=\"next\"" ])

    use transport = new HttpTransport(server.Base, "t")
    let t = transport :> IGitHubTransport

    match t.Send(get "repos/o/r/actions/runs") with
    | Ok response ->
        use doc = Text.Json.JsonDocument.Parse response.Body
        let runs = doc.RootElement.GetProperty("workflow_runs")
        Assert.Equal(2, runs.GetArrayLength())
        Assert.Equal(2, List.length server.Requests)
        Assert.Equal(None, response.ETag)
    | Error e -> failwith $"wrapper pagination must preserve every run — got %A{e}"

[<Fact>]
let ``#2308 markerScan keeps a stale claim found only on REST page two`` () =
    use server = new Server()
    let old = DateTimeOffset.UtcNow.AddHours(-3).ToString("yyyy-MM-ddTHH:mm:ssZ")

    server.On(fun req res ->
        if req.Url.PathAndQuery.Contains "page=2" then
            server.Json res 200 $"""[{{"id":7,"body":"<!-- fsgg:claim worker=page-two-holder lease=120 -->","updated_at":"%s{old}"}}]""" []
        else
            server.Json
                res
                200
                "[]"
                [ "Link", $"<%s{server.Base}/repos/FS-GG/FS.GG.SDD/issues/42/comments?page=2>; rel=\"next\"" ])

    use transport = new HttpTransport(server.Base, "t")

    match Reads.markerScan (transport :> IGitHubTransport) "FS-GG" "FS.GG.SDD" 42 with
    | Ok scan ->
        Assert.Contains(scan.Markers, fun marker -> marker.Worker = WorkerId "page-two-holder")
        Assert.Equal(2, List.length server.Requests)
    | Error e -> failwith $"page-two marker must remain visible to the complete scan — got %A{e}"

[<Fact>]
let ``a MERGED response carries NO ETag - page one's validator dies at the merge`` () =
    // THE VALIDATOR BELONGS TO THE REQUEST THAT RETURNED IT, AND THAT REQUEST WAS PAGE ONE. The adapter
    // concatenates the pages above; the ETag describes only the first of them. Hand it back on a merged body
    // and a caller may store the two together, then revalidate the WHOLE collection against its first page —
    // and a set that grows a page while page one stays byte-identical answers 304, so the merge never runs
    // again and a one-page body is served for a two-page set. #461, deciding whether to merge.
    //
    // This is asserted HERE because this is the only layer that can know: a caller receives one `Response`
    // and cannot tell how many round trips paid for it.
    use server = new Server()

    server.On(fun req res ->
        if req.Url.PathAndQuery.Contains "page=2" then
            server.Json res 200 """[{"number":3}]""" [ "ETag", "W/\"page-two\"" ]
        else
            server.Json
                res
                200
                """[{"number":1},{"number":2}]"""
                [ "ETag", "W/\"page-one\""
                  "Link", $"<%s{server.Base}/repos/o/r/issues?page=2>; rel=\"next\", <%s{server.Base}/x>; rel=\"last\"" ])

    use transport = new HttpTransport(server.Base, "t")
    let t = transport :> IGitHubTransport

    match t.Send(get "repos/o/r/issues") with
    | Ok response ->
        // The merge really happened...
        use doc = Text.Json.JsonDocument.Parse response.Body
        Assert.Equal(3, doc.RootElement.GetArrayLength())

        // ...and it carries NO validator. Not page one's, and not the last page's either — neither of them
        // describes this body.
        Assert.Equal(None, response.ETag)

    | Error e -> failwith $"pagination must succeed — got %A{e}"

[<Fact>]
let ``a SINGLE-page response KEEPS its ETag - the counterweight`` () =
    // The counterweight, and it is what stops the rule above being satisfied by dropping every ETag always.
    // When there is no `Link`, page one IS the whole answer and its validator describes the whole body — so
    // it survives, and the conditional re-read that saves the fleet's REST budget is still possible.
    use server = new Server()
    server.On(fun _ res -> server.Json res 200 """[{"number":1}]""" [ "ETag", "W/\"whole-thing\"" ])

    use transport = new HttpTransport(server.Base, "t")
    let t = transport :> IGitHubTransport

    match t.Send(get "repos/o/r/issues") with
    | Ok response -> Assert.Equal(Some "W/\"whole-thing\"", response.ETag)
    | Error e -> failwith $"a single-page read must succeed — got %A{e}"

[<Fact>]
let ``a page that is NOT a JSON array refuses - it is never silently truncated to the first page`` () =
    // `gh` exits 0 on a truncated page or a proxy's HTML error body. If the adapter quietly kept page one
    // and dropped the rest, a claim scan would report a partial set as a complete one — #461's shape,
    // arriving through pagination.
    use server = new Server()

    server.On(fun req res ->
        if req.Url.PathAndQuery.Contains "page=2" then
            server.Json res 200 "<html>502 Bad Gateway</html>" []
        else
            server.Json res 200 """[{"number":1}]""" [ "Link", $"<%s{server.Base}/x?page=2>; rel=\"next\"" ])

    use transport = new HttpTransport(server.Base, "t")
    let t = transport :> IGitHubTransport

    match t.Send(get "repos/o/r/issues") with
    | Error(Malformed _) -> ()
    | Ok r -> failwith $"a broken second page must refuse, not truncate to page one — got a body of %d{r.Body.Length} bytes"
    | other -> failwith $"expected Malformed — got %A{other}"

[<Fact>]
let ``no Link header means ONE request - the adapter does not invent pages`` () =
    use server = new Server()
    server.On(fun _ res -> server.Json res 200 """[{"number":1}]""" [])

    use transport = new HttpTransport(server.Base, "t")
    (transport :> IGitHubTransport).Send(get "repos/o/r/issues") |> ignore

    Assert.Equal(1, List.length server.Requests)

// ---- the conditional request -------------------------------------------------------------------------

[<Fact>]
let ``If-None-Match is SENT when given, and a 304 comes back as a SUCCESS with an empty body`` () =
    // A 304 IS NOT AN ERROR. It says "what you already have is current", and it is the whole reason the
    // conditional path costs nothing. Classifying it as a failure would send the caller down the error
    // branch on the cheapest correct answer the server can give.
    use server = new Server()

    server.On(fun req res ->
        if req.Headers.["If-None-Match"] = "\"etag-v1\"" then
            res.StatusCode <- 304
        else
            server.Json res 200 """[{"number":1}]""" [ "ETag", "\"etag-v1\"" ])

    use transport = new HttpTransport(server.Base, "t")
    let t = transport :> IGitHubTransport

    // First: unconditional, and it hands back the validator.
    match t.Send(get "repos/o/r/issues") with
    | Ok r -> Assert.Equal(Some "\"etag-v1\"", r.ETag)
    | Error e -> failwith $"the first read must succeed — got %A{e}"

    // Second: conditional.
    match t.Send { get "repos/o/r/issues" with IfNoneMatch = Some "\"etag-v1\"" } with
    | Ok r ->
        Assert.Equal(304, r.Status)
        Assert.Equal("", r.Body)
    | Error e -> failwith $"a 304 is a SUCCESS, not a failure — got %A{e}"

    // And the header really went on the wire.
    let conditional =
        server.Requests |> List.filter (fun (_, _, inm, _) -> inm.IsSome)

    Assert.Equal(1, List.length conditional)

[<Fact>]
let ``a request with NO IfNoneMatch sends NO validator - which is what the LOCK depends on`` () =
    // **A lock may never be read from a cache.** A 304 serving a body captured before a claim marker was
    // posted would report `comments: 0` over a LIVE lock. `Reads.markerScan` sets `IfNoneMatch = None`, and
    // this is the assertion that the adapter honours it rather than adding one of its own.
    use server = new Server()
    server.On(fun _ res -> server.Json res 200 "[]" [])

    use transport = new HttpTransport(server.Base, "t")
    Reads.markerScan (transport :> IGitHubTransport) "FS-GG" "FS.GG.SDD" 42 |> ignore

    match server.Requests with
    | [ (_, path, inm, _) ] ->
        Assert.True(inm.IsNone, "the marker read must carry NO If-None-Match — a 304 could hide a live lock")
        Assert.Contains("/issues/42/comments", path)
    | other -> failwith $"expected exactly one marker read — got %A{other}"

// ---- the typed GraphQL payload -----------------------------------------------------------------------

[<Fact>]
let ``a GraphQL variable is serialised WITH ITS TYPE - a number is a number, not a quoted string`` () =
    // This is the trap that was documented and then closed. Projects v2 field mutations are typed: a NUMBER
    // field wants `{"number": 42}` and REJECTS `{"number": "42"}`. Everything used to serialise as a string.
    //
    // The fake cannot catch this — it reads the `Var` back out of the request record, so it sees the type
    // whatever the encoder does. Only the actual BYTES on the wire settle it, and that is this test.
    use server = new Server()
    server.On(fun _ res -> server.Json res 200 """{"data":{}}""" [])

    use transport = new HttpTransport(server.Base, "t")

    (transport :> IGitHubTransport).Send
        { get "graphql" with
            Method = "POST"
            Budget = GraphQl
            Body =
                Query(
                    "mutation($n: Float!, $id: ID!) { x(a: $n, b: $id) { y } }",
                    [ "n", VNumber 42.0; "id", VId "PVTSSF_status" ]
                ) }
    |> ignore

    match server.Requests with
    | [ (method, _, _, body) ] ->
        Assert.Equal("POST", method)

        // THE BYTES. A number, unquoted.
        Assert.Contains("\"n\":42", body)
        Assert.DoesNotContain("\"n\":\"42\"", body)

        // An ID is a string on the wire — but it is a DISTINCT case in `Var`, so it cannot be handed to a
        // field expecting a number by accident.
        Assert.Contains("\"id\":\"PVTSSF_status\"", body)

        // The document travels in the payload, not concatenated into a URL.
        Assert.Contains("\"query\":", body)

    | other -> failwith $"expected exactly one POST — got %A{other}"

[<Fact>]
let ``a GraphQL document with NO variables omits the variables object entirely`` () =
    // The aliased batch (#448) interpolates inline and declares no variables. An empty `variables: {}` is
    // accepted by GitHub, but emitting it would mean the batch document's bytes differ from what the corpus
    // greps for — and the corpus asserts on those bytes.
    use server = new Server()
    server.On(fun _ res -> server.Json res 200 """{"data":{}}""" [])

    use transport = new HttpTransport(server.Base, "t")

    (transport :> IGitHubTransport).Send
        { get "graphql" with
            Method = "POST"
            Budget = GraphQl
            Body = Query("mutation { f0: update(input: {}) { id } }", []) }
    |> ignore

    match server.Requests with
    | [ (_, _, _, body) ] ->
        Assert.Contains("f0: update", body)
        Assert.DoesNotContain("\"variables\"", body)
    | other -> failwith $"expected one request — got %A{other}"

// ---- status classification, on the real wire ----------------------------------------------------------

[<Fact>]
let ``#421 a 403 carrying a rate-limit message is RateLimited, not Unauthorized`` () =
    // The two readings of a 403 have OPPOSITE remedies — "wait" and "your token is wrong" — and telling a
    // worker to wait out a permissions failure is an infinite loop that reports progress.
    use server = new Server()
    server.On(fun _ res -> server.Json res 403 """{"message":"API rate limit exceeded for user ID 1."}""" [])

    use transport = new HttpTransport(server.Base, "t")

    match (transport :> IGitHubTransport).Send(get "repos/o/r/issues/1") with
    | Error(RateLimited _) -> ()
    | other -> failwith $"an exhausted budget must be RateLimited — got %A{other}"

[<Fact>]
let ``a 403 that is NOT a rate limit is Unauthorized, and NAMES ITS SUBJECT`` () =
    use server = new Server()
    server.On(fun _ res -> server.Json res 403 """{"message":"Resource not accessible by integration"}""" [])

    use transport = new HttpTransport(server.Base, "t")

    match (transport :> IGitHubTransport).Send(get "repos/o/r/issues/1") with
    | Error(Unauthorized subject) -> Assert.Equal("repos/o/r/issues/1", subject)
    | other -> failwith $"a permissions failure is not a budget failure — got %A{other}"

[<Fact>]
let ``a 404 is NotFound - the server SAID it is not there`` () =
    use server = new Server()
    server.On(fun _ res -> server.Json res 404 """{"message":"Not Found"}""" [])

    use transport = new HttpTransport(server.Base, "t")

    match (transport :> IGitHubTransport).Send(get "repos/o/r/issues/999") with
    | Error(NotFound _) -> ()
    | other -> failwith $"a 404 is an absence, not a failed read — got %A{other}"

[<Fact>]
let ``a rate limit is NOT RETRIED - one request, and the back-off is the caller's`` () =
    // An exhausted budget is not a transient blip. Retrying it three times spends three more calls
    // confirming the same 403 — and delays the EX_RATE the caller actually needs by exactly that long.
    use server = new Server()
    server.On(fun _ res -> server.Json res 403 """{"message":"API rate limit exceeded"}""" [])

    use transport = new HttpTransport(server.Base, "t")
    (transport :> IGitHubTransport).Send(get "repos/o/r/issues/1") |> ignore

    Assert.Equal(1, List.length server.Requests)

// ---- the transport failing entirely -------------------------------------------------------------------

[<Fact>]
let ``an unreachable host is a Transport error - we did not observe an empty board, we observed NOTHING`` () =
    // #344. A connection reset is not a board with no items on it, and this must never degrade to `Ok []`.
    // The port is dead — nothing is listening.
    let dead =
        use probe = new Sockets.TcpListener(IPAddress.Loopback, 0)
        probe.Start()
        let p = (probe.LocalEndpoint :?> IPEndPoint).Port
        probe.Stop()
        $"http://127.0.0.1:%d{p}"

    use transport = new HttpTransport(dead, "t")

    match (transport :> IGitHubTransport).Send(get "repos/o/r/issues") with
    | Error(Transport _) -> ()
    | Ok r -> failwith $"an unreachable host produced a SUCCESS with %d{r.Body.Length} bytes — this is #344"
    | other -> failwith $"expected a Transport error — got %A{other}"

// ---- the API base is configurable, which is the whole of ADR-0040 C1 ----------------------------------

[<Fact>]
let ``the API base is read from the environment - the mechanism C1 specified`` () =
    // ADR-0040 C1: *"the shell corpus drives the tool through a configurable API base so it keeps its
    // black-box character."* This is that base. Every test in this file uses it, which is the first time
    // anything has.
    let previous = Environment.GetEnvironmentVariable "FSGG_GITHUB_API_BASE"

    try
        Environment.SetEnvironmentVariable("FSGG_GITHUB_API_BASE", "http://127.0.0.1:9999/")
        // The trailing slash is trimmed, or every URL would carry a double slash — which some proxies 404.
        Assert.Equal("http://127.0.0.1:9999", apiBaseFromEnv ())

        Environment.SetEnvironmentVariable("FSGG_GITHUB_API_BASE", null)
        Assert.Equal("https://api.github.com", apiBaseFromEnv ())
    finally
        Environment.SetEnvironmentVariable("FSGG_GITHUB_API_BASE", previous)

// ---- #2418: the meter is observed ON THE WIRE ---------------------------------------------------------

[<Fact>]
let ``#2418 a GraphQL response's rateLimit cost is ACCUMULATED by the real adapter`` () =
    // THIS TEST EXISTS BECAUSE THE UNIT TESTS COULD NOT SEE THE BUG.
    //
    // `Budget.readMeter` was correct, and unit-tested, from the day it was written — and had NO PRODUCTION
    // CALLER until #2418. Every query document pays to select `rateLimit { cost remaining }`; the answer was
    // parsed by nothing and dropped. A parser test cannot catch that: it proves the function works, never
    // that anything calls it. The accumulator tests in `BudgetTests` have the same blind spot — with the
    // `Budget.observeGraphQlBody` call deleted from `HttpTransport`, all 489 of them still pass.
    //
    // So this drives the REAL adapter against a REAL server and asserts the spend moved. Delete the wiring
    // in `Transport.fs` and this test — and only this test — goes red.
    Budget.resetGraphQlSpend ()

    use server = new Server()
    server.On(fun _ res -> server.Json res 200 """{"data":{"rateLimit":{"cost":13,"remaining":4987}}}""" [])

    use transport = new HttpTransport(server.Base, "t")
    let t = transport :> IGitHubTransport

    let query =
        { Method = "POST"
          Path = "graphql"
          Query = []
          Body = Query("query { rateLimit { cost remaining } }", [])
          Budget = GraphQl
          IfNoneMatch = None
          Subject = "meter" }

    match t.Send query with
    | Ok _ ->
        let spend = Budget.graphQlSpend ()
        Assert.Equal(13, spend.Points)
        Assert.Equal(1, spend.Calls)
        Assert.Equal(Some 4987, spend.LastRemaining)
    | Error e -> failwith $"the query must succeed — got %A{e}"

[<Fact>]
let ``#2418 a REST response is NOT counted against the GraphQL meter`` () =
    // The two budgets are not interchangeable (ADR-0034 §3), and a REST body that happened to contain the
    // word `rateLimit` must not be billed to GraphQL. The adapter gates on `request.Budget`, not on the
    // shape of the body — so this asserts the gate, not the parser.
    Budget.resetGraphQlSpend ()

    use server = new Server()
    server.On(fun _ res -> server.Json res 200 """{"data":{"rateLimit":{"cost":99,"remaining":1}}}""" [])

    use transport = new HttpTransport(server.Base, "t")
    let t = transport :> IGitHubTransport

    match t.Send(get "repos/o/r/issues") with
    | Ok _ ->
        let spend = Budget.graphQlSpend ()
        Assert.Equal(0, spend.Points)
        Assert.Equal(0, spend.Calls)
    | Error e -> failwith $"the REST read must succeed — got %A{e}"
