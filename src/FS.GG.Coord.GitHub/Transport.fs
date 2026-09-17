namespace FS.GG.Coord.GitHub

module Transport =

    open System
    open System.Net.Http
    open System.Security.Cryptography
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
        | VDate of string
        | VNumber of double

    type Payload =
        | NoBody
        | Json of string
        | Query of document: string * variables: (string * Var) list

    type Request =
        {
            Method: string
            Path: string
            Query: (string * string) list
            Body: Payload
            Budget: Budget
            IfNoneMatch: string option
            Subject: string
        }

    type MutationIntent = { EffectId: string; Request: Request }

    type Response =
        {
            Status: int
            Body: string
            Headers: Map<string, string>
            ETag: string option
            NextLink: string option
        }

    let (|NotModified|_|) (response: Response) =
        if response.Status = 304 then Some() else None

    let header (name: string) (response: Response) =
        response.Headers
        |> Map.tryFind name
        |> Option.orElseWith (fun () ->
            response.Headers
            |> Map.tryPick (fun actual value ->
                if actual.Equals(name, StringComparison.OrdinalIgnoreCase) then
                    Some value
                else
                    None))

    type IGitHubTransport =
        abstract Send: request: Request -> IoResult<Response>
        abstract SendMutation: mutation: MutationIntent -> IoResult<Response>
        abstract RetryMutation: effectId: string -> IoResult<Response>

    type IProviderGitHubTransport =
        abstract Send: request: Request -> IoResult<Response>
        abstract SendMutationOnce: request: Request -> IoResult<Response>

    type ISinglePageGitHubTransport =
        abstract SendSingle: request: Request -> IoResult<Response>

    [<Literal>]
    let private DefaultApiBase = "https://api.github.com"

    let apiBaseFromEnv () =
        match Environment.GetEnvironmentVariable "FSGG_GITHUB_API_BASE" with
        | null
        | "" -> DefaultApiBase
        | b -> b.TrimEnd('/')

    type OwnerKind =
        | Org
        | User
        | Viewer

    module OwnerKind =

        let ownerField (kind: OwnerKind) =
            match kind with
            | Org -> "organization"
            | User -> "user"
            | Viewer -> "viewer"

        let forOwner (kind: OwnerKind) (orgQuery: string) =
            match kind with
            | Org -> orgQuery
            | User -> orgQuery.Replace("organization(login: $owner)", "user(login: $owner)")
            | Viewer ->
                orgQuery
                    .Replace("organization(login: $owner)", "viewer")
                    // Drop the `$owner` variable declaration — `viewer` never references it. Exactly one of
                    // these two forms is present (comma-joined when other variables follow, bare otherwise).
                    .Replace("query($owner: String!, ", "query(")
                    .Replace("query($owner: String!)", "query")

        let ownerVars (kind: OwnerKind) (owner: string) : (string * Var) list =
            match kind with
            | Org
            | User -> [ "owner", VString owner ]
            | Viewer -> []

        let fromEnv () =
            match Environment.GetEnvironmentVariable "FSGG_COORD_OWNER_TYPE" with
            | null -> Org
            | v ->
                match v.Trim().ToLowerInvariant() with
                | "user" ->
                    match Environment.GetEnvironmentVariable "FSGG_COORD_OWNER" with
                    | null -> Viewer
                    | o when o.Trim() = "" -> Viewer
                    | _ -> User
                | _ -> Org

    // The `Link` header's `rel="next"`, if there is one.
    //
    // GitHub paginates with `Link: <https://…?page=2>; rel="next", <…>; rel="last"`. Parsing it is how the
    // adapter earns the `--paginate` the bash client passed to `gh` — and pagination is not an
    // optimisation on the claim scan, it is correctness: a lock has no 100-issue limit, and a first page
    // read as the whole set is a scan that cannot see the markers past it.
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

    // Concatenate two collection pages. GitHub uses both root arrays (`issues`) and wrapper objects
    // (`workflow_runs`, `check_runs`); both must remain complete across `Link: rel=next`.
    //
    // A page that is not an array is a FAILED READ, never an empty one. `gh` exits 0 on a truncated page
    // or a proxy's HTML error body, and the empty string that falls out of `jq` reads as "nothing here" —
    // that is #461, and it is the reason this returns an error instead of an empty array.
    let private mergePages (first: string) (second: string) : Result<string, string> =
        try
            use a = JsonDocument.Parse first
            use b = JsonDocument.Parse second

            match a.RootElement.ValueKind, b.RootElement.ValueKind with
            | JsonValueKind.Array, JsonValueKind.Array ->
                let merged = JsonArray()

                for item in a.RootElement.EnumerateArray() do
                    merged.Add(JsonNode.Parse(item.GetRawText()))

                for item in b.RootElement.EnumerateArray() do
                    merged.Add(JsonNode.Parse(item.GetRawText()))

                Ok(merged.ToJsonString())
            | JsonValueKind.Object, JsonValueKind.Object ->
                let merged = JsonNode.Parse(first).AsObject()

                let commonArrays =
                    b.RootElement.EnumerateObject()
                    |> Seq.choose (fun property ->
                        match a.RootElement.TryGetProperty property.Name with
                        | true, firstValue when
                            firstValue.ValueKind = JsonValueKind.Array
                            && property.Value.ValueKind = JsonValueKind.Array
                            ->
                            Some(property.Name, property.Value)
                        | _ -> None)
                    |> List.ofSeq

                if List.isEmpty commonArrays then
                    Error "paginated wrapper pages carry no common JSON array property"
                else
                    for name, source in commonArrays do
                        let target = merged[name].AsArray()

                        for item in source.EnumerateArray() do
                            target.Add(JsonNode.Parse(item.GetRawText()))

                    Ok(merged.ToJsonString())
            | JsonValueKind.Array, _
            | _, JsonValueKind.Array -> Error "a paginated response whose page is not a JSON array"
            | _ -> Error "paginated response pages do not share a mergeable JSON collection shape"

        with :? JsonException as e ->
            Error $"a page of the response is not JSON: %s{e.Message}"

    // A variable's JSON, DERIVED FROM ITS TYPE.
    //
    // A NUMBER field mutation sending `{"number": "42"}` is rejected by the API, and a DATE field sending a
    // quoted number is worse — it is accepted and wrong. GraphQL is typed; so is this.
    let private varJson (v: Var) : JsonNode =
        match v with
        | VString s -> JsonValue.Create s
        | VId s -> JsonValue.Create s
        | VDate s -> JsonValue.Create s
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

    let canonicalMutationBytes (request: Request) =
        let bodyKind, body =
            match request.Body with
            | NoBody -> "none", Array.empty<byte>
            | Json value -> "json", Encoding.UTF8.GetBytes value
            | Query(document, variables) -> "graphql", Encoding.UTF8.GetBytes(graphQlPayload document variables)

        let root = JsonObject()
        root.["bodyBase64"] <- JsonValue.Create(Convert.ToBase64String body)
        root.["bodyKind"] <- JsonValue.Create bodyKind

        let budget =
            match request.Budget with
            | GraphQl -> "graphql"
            | Rest -> "rest"
            | Free -> "free"

        root.["budget"] <- JsonValue.Create budget

        match request.IfNoneMatch with
        | Some value -> root.["ifNoneMatch"] <- JsonValue.Create value
        | None -> root.["ifNoneMatch"] <- null

        root.["method"] <- JsonValue.Create(request.Method.ToUpperInvariant())
        root.["path"] <- JsonValue.Create request.Path
        let query = JsonArray()

        for key, value in request.Query do
            let pair = JsonArray()
            pair.Add(JsonValue.Create key)
            pair.Add(JsonValue.Create value)
            query.Add pair

        root.["query"] <- query

        root.ToJsonString()
        |> FS.GG.Coordination.GitHub.ShardedJournalAdapter.canonicalJson
        |> Result.defaultWith invalidOp

    let mutationResponseEvidence (request: Request) (response: Response) =
        let digest =
            response.Body
            |> Encoding.UTF8.GetBytes
            |> SHA256.HashData
            |> Convert.ToHexString
            |> _.ToLowerInvariant()
            |> FS.GG.Coordination.GitHub.V1AdmissionRegistry.sha256Digest
            |> Result.defaultWith invalidOp

        let evidenceReason reason =
            $"%s{reason};status=%d{response.Status};response-sha256=%s{FS.GG.Coordination.GitHub.V1AdmissionRegistry.sha256Value digest}"

        match response.Status, request.Budget with
        | (202 | 206 | 207), _ -> V1Admission.Indeterminate(evidenceReason "provider-response-not-complete")
        | status, _ when status < 200 || status >= 300 ->
            V1Admission.Indeterminate(evidenceReason "provider-response-non-success")
        | 200, GraphQl ->
            try
                use document = JsonDocument.Parse response.Body
                let root = document.RootElement

                if root.ValueKind <> JsonValueKind.Object then
                    V1Admission.Indeterminate(evidenceReason "graphql-response-not-object")
                else
                    let hasErrors, errors = root.TryGetProperty "errors"

                    let hasMutationResult =
                        let hasData, data = root.TryGetProperty "data"

                        hasData
                        && data.ValueKind = JsonValueKind.Object
                        && (data.EnumerateObject() |> Seq.isEmpty |> not)
                        && (data.EnumerateObject()
                            |> Seq.exists (fun property -> property.Value.ValueKind <> JsonValueKind.Null))

                    let carriesErrors =
                        hasErrors
                        && errors.ValueKind = JsonValueKind.Array
                        && errors.GetArrayLength() > 0

                    if hasErrors && errors.ValueKind <> JsonValueKind.Array then
                        V1Admission.Indeterminate(evidenceReason "graphql-errors-invalid")
                    elif carriesErrors then
                        if hasMutationResult then
                            V1Admission.Partial(evidenceReason "graphql-partial-data-with-errors")
                        else
                            V1Admission.Indeterminate(evidenceReason "graphql-errors")
                    elif hasMutationResult then
                        V1Admission.Applied digest
                    else
                        V1Admission.Indeterminate(evidenceReason "graphql-response-without-mutation-result")
            with :? JsonException ->
                V1Admission.Indeterminate(evidenceReason "graphql-response-invalid-json")
        | _, GraphQl -> V1Admission.Indeterminate(evidenceReason "graphql-response-not-definitive")
        | (200 | 201 | 204), (Rest | Free) -> V1Admission.Applied digest
        | _ -> V1Admission.Indeterminate(evidenceReason "provider-response-not-definitive")

    let private requestFromCanonicalBytes effectId (bytes: byte array) =
        try
            use document = JsonDocument.Parse bytes
            let root = document.RootElement
            let bodyKind = root.GetProperty("bodyKind").GetString()

            let bodyBytes =
                root.GetProperty("bodyBase64").GetString() |> Convert.FromBase64String

            let body =
                match bodyKind with
                | "none" when bodyBytes.Length = 0 -> NoBody
                | "json"
                | "graphql" -> Json(Encoding.UTF8.GetString bodyBytes)
                | _ -> invalidOp "canonical-mutation-body"

            let budget =
                match root.GetProperty("budget").GetString() with
                | "graphql" -> GraphQl
                | "rest" -> Rest
                | "free" -> Free
                | _ -> invalidOp "canonical-mutation-budget"

            let query =
                root.GetProperty("query").EnumerateArray()
                |> Seq.map (fun pair ->
                    let values = pair.EnumerateArray() |> Seq.toArray

                    if values.Length <> 2 then
                        invalidOp "canonical-mutation-query"

                    values[0].GetString(), values[1].GetString())
                |> List.ofSeq

            let ifNoneMatch =
                let value = root.GetProperty("ifNoneMatch")

                if value.ValueKind = JsonValueKind.Null then
                    None
                else
                    Some(value.GetString())

            Ok
                {
                    Method = root.GetProperty("method").GetString()
                    Path = root.GetProperty("path").GetString()
                    Query = query
                    Body = body
                    Budget = budget
                    IfNoneMatch = ifNoneMatch
                    Subject = "v1 retry " + effectId
                }
        with _ ->
            Error "persisted canonical request is unreadable"

    type HttpTransport(apiBase: string, token: string) as this =

        let client = new HttpClient()
        let singlePageHandler = new HttpClientHandler(AllowAutoRedirect = false)
        let singlePageClient = new HttpClient(singlePageHandler)
        let base' = apiBase.TrimEnd('/')

        do
            // GitHub rejects a request with no User-Agent. `Accept` pins the v3 REST media type; the
            // GraphQL endpoint ignores it.
            client.DefaultRequestHeaders.UserAgent.ParseAdd "fsgg-coord"
            client.DefaultRequestHeaders.Accept.ParseAdd "application/vnd.github+json"
            singlePageClient.DefaultRequestHeaders.UserAgent.ParseAdd "fsgg-coord"
            singlePageClient.DefaultRequestHeaders.Accept.ParseAdd "application/vnd.github+json"

            // A REQUEST THAT NEVER RETURNS IS WORSE THAN ONE THAT FAILS. The client is invoked once per
            // scheduling call, on every worker, in a fan-out — and a worker blocked forever on a hung
            // socket holds its claim, keeps its lease alive, and reserves its touch-set the whole time. It
            // does not look like a failure to anybody; it looks like slow work. A bounded timeout turns
            // that into a `Transport` error the caller can actually act on.
            client.Timeout <- TimeSpan.FromSeconds 30.0
            singlePageClient.Timeout <- TimeSpan.FromSeconds 30.0

            if not (String.IsNullOrWhiteSpace token) then
                client.DefaultRequestHeaders.Authorization <- Headers.AuthenticationHeaderValue("Bearer", token)

                singlePageClient.DefaultRequestHeaders.Authorization <-
                    Headers.AuthenticationHeaderValue("Bearer", token)

        // Send one HTTP request. The URL is absolute, because pagination hands us a fully-qualified
        // `Link` to follow rather than a path to rebuild.
        let sendOne
            (http: HttpClient)
            (request: Request)
            (url: string)
            (maximumBytes: int option)
            (rawMutationResponse: bool)
            : IoResult<Response> =
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

                use response = http.Send message
                let status = int response.StatusCode

                let body =
                    match maximumBytes with
                    | None ->
                        use reader = new IO.StreamReader(response.Content.ReadAsStream())
                        reader.ReadToEnd()
                    | Some maximum ->
                        match response.Content.Headers.ContentLength with
                        | value when value.HasValue && value.Value > int64 maximum ->
                            raise (IO.InvalidDataException "response exceeds 4 MiB")
                        | _ -> ()

                        use stream = response.Content.ReadAsStream()
                        use memory = new IO.MemoryStream()
                        let buffer = Array.zeroCreate<byte> 8192
                        let mutable reading = true

                        while reading do
                            let count = stream.Read(buffer, 0, buffer.Length)

                            if count = 0 then
                                reading <- false
                            elif memory.Length + int64 count > int64 maximum then
                                raise (IO.InvalidDataException "response exceeds 4 MiB")
                            else
                                memory.Write(buffer, 0, count)

                        Encoding.UTF8.GetString(memory.ToArray())

                let headers =
                    response.Headers
                    |> Seq.collect (fun h -> h.Value |> Seq.map (fun value -> h.Key, value))
                    |> Map.ofSeq

                let headerValue (name: string) =
                    headers
                    |> Map.tryFind name
                    |> Option.orElseWith (fun () ->
                        headers
                        |> Map.tryPick (fun actual value ->
                            if actual.Equals(name, StringComparison.OrdinalIgnoreCase) then
                                Some value
                            else
                                None))

                let etag = headerValue "ETag"

                // The real response, not a later account summary, is authoritative for the resource that
                // just answered. This is deliberately before the status split: a 403's headers are the
                // most important observation we receive.
                if request.Budget = Rest && not (String.IsNullOrWhiteSpace token) then
                    Budget.observeRestHeaders token headerValue

                if status >= 200 && status < 300 && request.Budget = GraphQl then
                    Budget.observeGraphQlBody body

                if rawMutationResponse then
                    // Mutation settlement must observe the provider's actual response before ordinary
                    // HTTP/GraphQL error mapping can discard it. The fence classifies this response and
                    // durably records Partial/Indeterminate evidence before refusing it to the caller.
                    Ok
                        {
                            Status = status
                            Body = body
                            Headers = headers
                            ETag = etag
                            NextLink = headerValue "Link" |> Option.bind parseNextLink
                        }
                // A 304 IS A SUCCESS. It says "what you already have is current", and it is the whole
                // reason the conditional path costs nothing. Classifying it as a failure would send the
                // caller down the error branch on the cheapest correct answer the server can give.
                else if status = 304 then
                    Ok
                        {
                            Status = 304
                            Body = ""
                            Headers = headers
                            ETag = etag
                            NextLink = None
                        }
                elif status >= 200 && status < 300 then
                    // The GraphQL counterpart of `observeRestHeaders` above, and it was missing until #2418:
                    // every query document selects `rateLimit { cost remaining }`, `Budget.readMeter` parsed
                    // it correctly, and NOTHING CALLED IT. The fleet paid to transmit its own meter and threw
                    // the reading away, which is why an exhausted budget could not be attributed to anything.
                    Ok
                        {
                            Status = status
                            Body = body
                            Headers = headers
                            ETag = etag
                            NextLink = headerValue "Link" |> Option.bind parseNextLink
                        }
                else
                    // NO RETRY ON A RATE LIMIT. An exhausted budget is not a transient blip — retrying it
                    // three times spends three more calls confirming the same 403, and delays the back-off
                    // the caller actually needs (EX_RATE) by exactly that long.
                    //
                    // `headerValue` is handed over so the classifier can read `X-RateLimit-Resource` and
                    // `X-RateLimit-Reset` OFF THIS VERY RESPONSE. Note what is deliberately NOT passed:
                    // `request.Budget`. Which budget we BELIEVED we were spending is not evidence — the
                    // 403 itself says which bucket refused, and on a redirect or a proxied call those two
                    // can differ. Read the answer; do not infer it.
                    Error(Budget.classify request.Subject status body headerValue)

            with
            | :? HttpRequestException as e ->
                // WE DID NOT OBSERVE ANYTHING. This must never become an empty list: a connection reset is
                // not a board with no items on it (#344).
                let rec chain (x: exn) =
                    match x.InnerException with
                    | null -> x.Message
                    | inner -> $"%s{x.Message} <- %s{chain inner}"

                Error(Transport(chain e))
            | :? TaskCanceledException as e -> Error(Transport $"timed out: %s{e.Message}")
            | :? IO.InvalidDataException as e -> Error(Malformed(request.Subject, e.Message))

        interface IGitHubTransport with
            member _.Send(request: Request) : IoResult<Response> =
                let url = base' + "/" + request.Path.TrimStart('/') + buildQuery request.Query

                match sendOne client request url None false with
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
                            match sendOne client request link None false with
                            | Error e -> Error e
                            | Ok page ->
                                match mergePages acc.Body page.Body with
                                | Error detail -> Error(Malformed(request.Subject, detail))
                                // THE VALIDATOR DIES AT THE MERGE. `acc.ETag` is PAGE ONE'S — it was returned by
                                // the first request and it describes that request's answer, not this
                                // concatenation. Carry it forward and a caller could store it against the merged
                                // body, then revalidate the whole collection against its first page: a set that
                                // grows a page while page one stays byte-identical answers 304, this merge never
                                // runs again, and a one-page body is served for a two-page set (#461).
                                //
                                // Dropping it HERE is the difference between a rule and a guarantee. This is the
                                // only layer that knows a merge happened — a caller sees one `Response` and
                                // cannot tell how many requests paid for it, so a rule asking it to reason about
                                // that is one it gets wrong once, silently, forever.
                                | Ok merged -> follow { acc with Body = merged; ETag = None } page.NextLink (guard - 1)

                    follow first first.NextLink 100

            member _.SendMutation(mutation: MutationIntent) : IoResult<Response> =
                Error(Malformed(mutation.Request.Subject, "raw HTTP mutation transport is not fenced"))

            member _.RetryMutation(effectId: string) : IoResult<Response> =
                Error(Malformed(effectId, "raw HTTP mutation retry is not fenced"))

        interface IProviderGitHubTransport with
            member _.Send(request: Request) = (this :> IGitHubTransport).Send request

            member _.SendMutationOnce(request: Request) =
                let url = base' + "/" + request.Path.TrimStart('/') + buildQuery request.Query
                sendOne singlePageClient request url None true

        interface ISinglePageGitHubTransport with
            member _.SendSingle(request: Request) : IoResult<Response> =
                let url = base' + "/" + request.Path.TrimStart('/') + buildQuery request.Query
                sendOne singlePageClient request url (Some(4 * 1024 * 1024)) false

        interface IDisposable with
            member _.Dispose() =
                client.Dispose()
                singlePageClient.Dispose()

    type private ProviderAttempt =
        { Request: Request; Response: Response }

    type FencedTransport(inner: IProviderGitHubTransport, fence: V1Admission.IMutationFence) =

        let responseEvidence attempt =
            mutationResponseEvidence attempt.Request attempt.Response

        let dispatch effectId request =
            let method = request.Method.ToUpperInvariant()

            if not (Set.contains method (set [ "POST"; "PUT"; "PATCH"; "DELETE" ])) then
                Error(Malformed(request.Subject, "SendMutation requires a mutating HTTP method"))
            else
                // Snapshot at the public entry boundary. The durable record and provider callback receive
                // independent copies, so a caller cannot change the authorized bytes after admission.
                let canonical = canonicalMutationBytes request |> Array.copy

                match
                    fence.Dispatch(
                        effectId,
                        canonical,
                        (fun () ->
                            inner.SendMutationOnce request
                            |> Result.map (fun response ->
                                {
                                    Request = request
                                    Response = response
                                })),
                        responseEvidence
                    )
                with
                | Ok(V1Admission.AppliedResponse attempt)
                | Ok(V1Admission.UnresolvedResponse attempt) -> Ok attempt.Response
                | Ok(V1Admission.ProviderFailed error) -> Error error
                | Error reasons ->
                    Error(Malformed(request.Subject, "v1 admission refused: " + String.Join("; ", reasons)))

        interface IGitHubTransport with
            // Temporary P1/P2/P3 migration bypass. S2 removes this raw mutation-capable Send surface
            // once every business write uses SendMutation.
            member _.Send(request: Request) = inner.Send request

            member _.SendMutation(mutation: MutationIntent) =
                dispatch mutation.EffectId mutation.Request

            member _.RetryMutation(effectId: string) =
                match
                    fence.RetryProvenAbsent(
                        effectId,
                        (fun bytes ->
                            match requestFromCanonicalBytes effectId (Array.copy bytes) with
                            | Error reason -> Error(Malformed(effectId, reason))
                            | Ok request ->
                                inner.SendMutationOnce request
                                |> Result.map (fun response ->
                                    {
                                        Request = request
                                        Response = response
                                    })),
                        responseEvidence
                    )
                with
                | Ok(V1Admission.AppliedResponse attempt)
                | Ok(V1Admission.UnresolvedResponse attempt) -> Ok attempt.Response
                | Ok(V1Admission.ProviderFailed error) -> Error error
                | Error reasons -> Error(Malformed(effectId, "v1 retry refused: " + String.Join("; ", reasons)))
