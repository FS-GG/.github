namespace FS.GG.Coord.GitHub

module Transport =

    open System
    open System.Net.Http
    open System.Text
    open System.Text.Json
    open System.Text.Json.Nodes
    open System.Threading.Tasks
    open Errors

    type Budget =
        | GraphQl
        | Rest
        | Free

    type Var =
        | VString of string
        | VId of string
        | VNumber of double

    type Payload =
        | NoBody
        | Json of string
        | Query of document: string * variables: (string * Var) list

    type Request =
        { Method: string
          Path: string
          Query: (string * string) list
          Body: Payload
          Budget: Budget
          IfNoneMatch: string option
          Subject: string }

    type Response =
        { Status: int
          Body: string
          ETag: string option
          NextLink: string option }

    let (|NotModified|_|) (response: Response) =
        if response.Status = 304 then Some() else None

    type IGitHubTransport =
        abstract Send: request: Request -> IoResult<Response>

    [<Literal>]
    let private DefaultApiBase = "https://api.github.com"

    let apiBaseFromEnv () =
        match Environment.GetEnvironmentVariable "FSGG_GITHUB_API_BASE" with
        | null
        | "" -> DefaultApiBase
        | b -> b.TrimEnd('/')

    /// The `Link` header's `rel="next"`, if there is one.
    ///
    /// GitHub paginates with `Link: <https://…?page=2>; rel="next", <…>; rel="last"`. Parsing it is how the
    /// adapter earns the `--paginate` the bash client passed to `gh` — and pagination is not an
    /// optimisation on the claim scan, it is correctness: a lock has no 100-issue limit, and a first page
    /// read as the whole set is a scan that cannot see the markers past it.
    let private parseNextLink (value: string) =
        if String.IsNullOrWhiteSpace value then
            None
        else
            value.Split(',')
            |> Array.tryPick (fun part ->
                let segments = part.Split(';')

                if segments.Length < 2 then
                    None
                else

                let isNext =
                    segments
                    |> Array.skip 1
                    |> Array.exists (fun s -> s.Trim().Replace("\"", "").Replace(" ", "") = "rel=next")

                if not isNext then
                    None
                else
                    let url = segments.[0].Trim().TrimStart('<').TrimEnd('>')
                    if String.IsNullOrWhiteSpace url then None else Some url)

    let private buildQuery (query: (string * string) list) =
        if List.isEmpty query then
            ""
        else
            let encoded =
                query
                |> List.map (fun (k, v) -> $"%s{Uri.EscapeDataString k}=%s{Uri.EscapeDataString v}")
                |> String.concat "&"

            "?" + encoded

    /// Concatenate two JSON array pages.
    ///
    /// A page that is not an array is a FAILED READ, never an empty one. `gh` exits 0 on a truncated page
    /// or a proxy's HTML error body, and the empty string that falls out of `jq` reads as "nothing here" —
    /// that is #461, and it is the reason this returns an error instead of an empty array.
    let private mergeArrays (first: string) (second: string) : Result<string, string> =
        try
            use a = JsonDocument.Parse first
            use b = JsonDocument.Parse second

            if a.RootElement.ValueKind <> JsonValueKind.Array || b.RootElement.ValueKind <> JsonValueKind.Array then
                Error "a paginated response whose page is not a JSON array"
            else
                let merged = JsonArray()

                for item in a.RootElement.EnumerateArray() do
                    merged.Add(JsonNode.Parse(item.GetRawText()))

                for item in b.RootElement.EnumerateArray() do
                    merged.Add(JsonNode.Parse(item.GetRawText()))

                Ok(merged.ToJsonString())

        with :? JsonException as e ->
            Error $"a page of the response is not JSON: %s{e.Message}"

    /// A variable's JSON, DERIVED FROM ITS TYPE.
    ///
    /// A NUMBER field mutation sending `{"number": "42"}` is rejected by the API, and a DATE field sending a
    /// quoted number is worse — it is accepted and wrong. GraphQL is typed; so is this.
    let private varJson (v: Var) : JsonNode =
        match v with
        | VString s -> JsonValue.Create s
        | VId s -> JsonValue.Create s
        | VNumber n -> JsonValue.Create n

    let private graphQlPayload (document: string) (variables: (string * Var) list) =
        let node = JsonObject()
        node.["query"] <- JsonValue.Create document

        if not (List.isEmpty variables) then
            let varsNode = JsonObject()

            for (k, v) in variables do
                varsNode.[k] <- varJson v

            node.["variables"] <- varsNode

        node.ToJsonString()

    type HttpTransport(apiBase: string, token: string) =

        let client = new HttpClient()
        let base' = apiBase.TrimEnd('/')

        do
            // GitHub rejects a request with no User-Agent. `Accept` pins the v3 REST media type; the
            // GraphQL endpoint ignores it.
            client.DefaultRequestHeaders.UserAgent.ParseAdd "fsgg-coord"
            client.DefaultRequestHeaders.Accept.ParseAdd "application/vnd.github+json"

            // A REQUEST THAT NEVER RETURNS IS WORSE THAN ONE THAT FAILS. The client is invoked once per
            // scheduling call, on every worker, in a fan-out — and a worker blocked forever on a hung
            // socket holds its claim, keeps its lease alive, and reserves its touch-set the whole time. It
            // does not look like a failure to anybody; it looks like slow work. A bounded timeout turns
            // that into a `Transport` error the caller can actually act on.
            client.Timeout <- TimeSpan.FromSeconds 30.0

            if not (String.IsNullOrWhiteSpace token) then
                client.DefaultRequestHeaders.Authorization <-
                    Headers.AuthenticationHeaderValue("Bearer", token)

        /// Send one HTTP request. The URL is absolute, because pagination hands us a fully-qualified
        /// `Link` to follow rather than a path to rebuild.
        let sendOne (request: Request) (url: string) : IoResult<Response> =
            try
                let method =
                    match request.Method.ToUpperInvariant() with
                    | "GET" -> HttpMethod.Get
                    | "POST" -> HttpMethod.Post
                    | "PATCH" -> HttpMethod.Patch
                    | "PUT" -> HttpMethod.Put
                    | "DELETE" -> HttpMethod.Delete
                    | m -> HttpMethod m

                use message = new HttpRequestMessage(method, url)

                match request.IfNoneMatch with
                | Some etag -> message.Headers.TryAddWithoutValidation("If-None-Match", etag) |> ignore
                | None -> ()

                match request.Body with
                | NoBody -> ()
                | Json body -> message.Content <- new StringContent(body, Encoding.UTF8, "application/json")
                | Query(document, variables) ->
                    // Variables ride in the PAYLOAD, never interpolated into the document text. A document
                    // built by concatenation is an injection waiting to be found, and it defeats the
                    // server-side query cache besides.
                    message.Content <-
                        new StringContent(graphQlPayload document variables, Encoding.UTF8, "application/json")

                use response = client.Send message
                let status = int response.StatusCode

                let body =
                    use reader = new IO.StreamReader(response.Content.ReadAsStream())
                    reader.ReadToEnd()

                let headerValue (name: string) =
                    match response.Headers.TryGetValues name with
                    | true, values -> values |> Seq.tryHead
                    | _ -> None

                let etag = headerValue "ETag"

                // A 304 IS A SUCCESS. It says "what you already have is current", and it is the whole
                // reason the conditional path costs nothing. Classifying it as a failure would send the
                // caller down the error branch on the cheapest correct answer the server can give.
                if status = 304 then
                    Ok
                        { Status = 304
                          Body = ""
                          ETag = etag
                          NextLink = None }
                elif status >= 200 && status < 300 then
                    Ok
                        { Status = status
                          Body = body
                          ETag = etag
                          NextLink = headerValue "Link" |> Option.bind parseNextLink }
                else
                    // NO RETRY ON A RATE LIMIT. An exhausted budget is not a transient blip — retrying it
                    // three times spends three more calls confirming the same 403, and delays the back-off
                    // the caller actually needs (EX_RATE) by exactly that long.
                    Error(Budget.classify request.Subject status body)

            with
            | :? HttpRequestException as e ->
                // WE DID NOT OBSERVE ANYTHING. This must never become an empty list: a connection reset is
                // not a board with no items on it (#344).
                Error(Transport e.Message)
            | :? TaskCanceledException as e -> Error(Transport $"timed out: %s{e.Message}")

        interface IGitHubTransport with
            member _.Send(request: Request) : IoResult<Response> =
                let url = base' + "/" + request.Path.TrimStart('/') + buildQuery request.Query

                match sendOne request url with
                | Error e -> Error e
                | Ok first ->

                let rec follow (acc: Response) (next: string option) (guard: int) : IoResult<Response> =
                    match next with
                    | None -> Ok acc
                    | Some _ when guard <= 0 ->
                        // A pagination loop is a bug, and an unbounded one is a bug that never returns.
                        // Refuse rather than spin: a collection we stopped gathering is not a complete one,
                        // and reporting it as complete is the whole failure class this port exists to end.
                        Error(Malformed(request.Subject, "pagination did not terminate within 100 pages"))
                    | Some link ->
                        match sendOne request link with
                        | Error e -> Error e
                        | Ok page ->
                            match mergeArrays acc.Body page.Body with
                            | Error detail -> Error(Malformed(request.Subject, detail))
                            | Ok merged -> follow { acc with Body = merged } page.NextLink (guard - 1)

                follow first first.NextLink 100

        interface IDisposable with
            member _.Dispose() = client.Dispose()
