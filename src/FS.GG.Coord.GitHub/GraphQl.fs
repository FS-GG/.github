namespace FS.GG.Coord.GitHub

module GraphQl =
    open System
    open System.Collections.Generic
    open System.Text.Json
    open Errors
    open Transport

    type RetryClassification =
        | Retryable of RateLimitKind
        | NotRetryable

    type FailureMetadata =
        { Retry: RetryClassification
          RateLimit: (RateLimitResource * DateTimeOffset option) option }

    type DrainLimits =
        { MaxPages: int
          MaxItems: int }

    type Page<'item> =
        private
            { Items: 'item list
              Keys: string list
              NextCursor: string option
              TotalCount: int option }

    let classify error =
        match error with
        | RateLimited(resource, resetAt) ->
            { Retry = Retryable(Errors.rateLimitKind error |> Option.defaultValue Unknown)
              RateLimit = Some(resource, resetAt) }
        | _ ->
            { Retry = NotRetryable
              RateLimit = None }

    let private messages (errors: JsonElement) =
        errors.EnumerateArray()
        |> Seq.map (fun error ->
            match error.TryGetProperty "message" with
            | true, message when message.ValueKind = JsonValueKind.String -> message.GetString()
            | _ -> "(no message)")
        |> List.ofSeq

    let private envelope subject body =
        if String.IsNullOrWhiteSpace body then
            Error(Malformed(subject, "the GraphQL response body was empty"))
        else
            try
                use document = JsonDocument.Parse body
                let root = document.RootElement

                if root.ValueKind <> JsonValueKind.Object then
                    Error(Malformed(subject, $"the GraphQL response is not a JSON object (%A{root.ValueKind}) — a GraphQL response is always `{{...}}`, so this is a FAILED READ, never an empty answer"))
                else
                    match root.TryGetProperty "errors" with
                    | true, errors when errors.ValueKind = JsonValueKind.Array && errors.GetArrayLength() > 0 ->
                        let failures = messages errors
                        match Budget.ofGraphQlErrors failures with
                        | Some limited -> Error limited
                        | None -> Error(GraphQlErrors failures)
                    | _ ->
                        match root.TryGetProperty "data" with
                        | true, data when data.ValueKind <> JsonValueKind.Null -> Ok(data.Clone())
                        | _ -> Error(Malformed(subject, "the GraphQL response carried neither `data` nor `errors`"))
            with :? JsonException as error ->
                Error(Malformed(subject, $"the GraphQL response is not JSON: %s{error.Message}"))

    let decode (subject: string) (body: string) (decoder: JsonElement -> IoResult<'value>) : IoResult<'value> =
        match envelope subject body with
        | Error error -> Error error
        | Ok data ->
            try decoder data
            with
            | :? KeyNotFoundException -> Error(Malformed(subject, "the GraphQL data omitted a required field"))
            | :? InvalidOperationException as error -> Error(Malformed(subject, $"the GraphQL data had an invalid shape: %s{error.Message}"))
            | :? FormatException as error -> Error(Malformed(subject, $"the GraphQL data had an invalid value: %s{error.Message}"))

    let read (transport: IGitHubTransport) (request: Request) (decoder: JsonElement -> IoResult<'value>) : IoResult<'value> =
        if request.Method <> "POST" || request.Path <> "graphql" || request.Budget <> GraphQl then
            Error(Malformed(request.Subject, "the GraphQL adapter received a non-GraphQL request"))
        else
            match transport.Send request with
            | Error error -> Error error
            | Ok response -> decode request.Subject response.Body decoder

    let private pageCore
        (subject: string)
        (what: string)
        (window: int option)
        (key: 'item -> string)
        (decodeNode: JsonElement -> IoResult<'item>)
        (connection: JsonElement)
        : IoResult<Page<'item>> =
        if connection.ValueKind <> JsonValueKind.Object then
            Error(Malformed(subject, $"%s{what} is not a JSON object (%A{connection.ValueKind}) — this is a FAILED READ"))
        else
            match connection.TryGetProperty "nodes", connection.TryGetProperty "pageInfo" with
            | (true, nodes), (true, pageInfo) when nodes.ValueKind = JsonValueKind.Array && pageInfo.ValueKind = JsonValueKind.Object ->
                let decoded =
                    nodes.EnumerateArray()
                    |> Seq.map decodeNode
                    |> List.ofSeq

                match decoded |> List.tryPick (function Error error -> Some error | Ok _ -> None) with
                | Some error -> Error error
                | None ->
                    let items = decoded |> List.choose (function Ok item -> Some item | Error _ -> None)
                    let keys = items |> List.map key
                    if keys |> List.exists String.IsNullOrWhiteSpace then
                        Error(Malformed(subject, $"%s{what} returned an item with no stable identity"))
                    elif (keys |> Set.ofList |> Set.count) <> keys.Length then
                        Error(Malformed(subject, $"%s{what} repeated an item identity within one page — the connection mutated while it was read"))
                    else
                        let totalCount =
                            match connection.TryGetProperty "totalCount" with
                            | true, total when total.ValueKind = JsonValueKind.Number ->
                                match total.TryGetInt32() with
                                | true, value when value >= 0 -> Some value
                                | _ -> None
                            | _ -> None

                        match pageInfo.TryGetProperty "hasNextPage" with
                        | true, hasNext when hasNext.ValueKind = JsonValueKind.False ->
                            Ok { Items = items; Keys = keys; NextCursor = None; TotalCount = totalCount }
                        | true, hasNext when hasNext.ValueKind = JsonValueKind.True ->
                            match pageInfo.TryGetProperty "endCursor" with
                            | true, cursor when cursor.ValueKind = JsonValueKind.String && not (String.IsNullOrWhiteSpace(cursor.GetString())) ->
                                if items.IsEmpty then
                                    Error(Malformed(subject, $"%s{what} returned an empty page while announcing another page"))
                                else
                                    Ok { Items = items; Keys = keys; NextCursor = Some(cursor.GetString()); TotalCount = totalCount }
                            | _ -> Error(Malformed(subject, $"%s{what} has another page but no usable cursor"))
                        | _ -> Error(Malformed(subject, $"%s{what}'s `pageInfo.hasNextPage` is missing or is not a Boolean"))
            | _, (true, pageInfo) when pageInfo.ValueKind <> JsonValueKind.Object ->
                Error(Malformed(subject, $"%s{what}'s `pageInfo` is missing or is not an object"))
            | (true, nodes), (false, _) when nodes.ValueKind = JsonValueKind.Array ->
                match window with
                | Some limit when limit > 0 && nodes.GetArrayLength() < limit ->
                    let decoded = nodes.EnumerateArray() |> Seq.map decodeNode |> List.ofSeq
                    match decoded |> List.tryPick (function Error error -> Some error | Ok _ -> None) with
                    | Some error -> Error error
                    | None ->
                        let items = decoded |> List.choose (function Ok item -> Some item | Error _ -> None)
                        let keys = items |> List.map key
                        if keys |> List.exists String.IsNullOrWhiteSpace then Error(Malformed(subject, $"%s{what} returned an item with no stable identity"))
                        elif (keys |> Set.ofList |> Set.count) <> keys.Length then Error(Malformed(subject, $"%s{what} repeated an item identity within one page — the connection mutated while it was read"))
                        else Ok { Items = items; Keys = keys; NextCursor = None; TotalCount = Some items.Length }
                | _ -> Error(Malformed(subject, $"%s{what}'s `pageInfo` is missing or is not an object"))
            | (false, _), _ -> Error(Malformed(subject, $"%s{what} is missing an array `nodes`"))
            | _ -> Error(Malformed(subject, $"%s{what} is missing an array `nodes` or object `pageInfo`"))

    let page subject what key decodeNode connection = pageCore subject what None key decodeNode connection

    let pageWithin subject what window key decodeNode connection =
        pageCore subject what (Some window) key decodeNode connection

    let drain
        (subject: string)
        (what: string)
        (limits: DrainLimits)
        (fetch: string option -> IoResult<Page<'item>>)
        : IoResult<'item list> =
        if limits.MaxPages <= 0 || limits.MaxItems < 0 then
            Error(Malformed(subject, $"%s{what} has invalid drain limits"))
        else
            let rec loop
                (cursor: string option)
                (pages: int)
                (expectedTotal: int option)
                (seenCursors: Set<string>)
                (seenKeys: Set<string>)
                (acc: 'item list)
                : IoResult<'item list> =
                if pages >= limits.MaxPages then
                    Error(Malformed(subject, $"%s{what} did not terminate within %d{limits.MaxPages} pages"))
                else
                    match fetch cursor with
                    | Error error -> Error error
                    | Ok page ->
                        let total =
                            match expectedTotal, page.TotalCount with
                            | None, value -> Ok value
                            | Some expected, Some actual when expected <> actual ->
                                Error(Malformed(subject, $"%s{what}'s totalCount changed from %d{expected} to %d{actual} while it was read"))
                            | current, _ -> Ok current

                        match total with
                        | Error error -> Error error
                        | Ok stableTotal ->
                            match page.Keys |> List.tryFind (fun itemKey -> Set.contains itemKey seenKeys) with
                            | Some duplicate -> Error(Malformed(subject, $"%s{what} repeated item `%s{duplicate}` across pages — the connection mutated while it was read"))
                            | None ->
                                let all = acc @ page.Items
                                if all.Length > limits.MaxItems then
                                    Error(Malformed(subject, $"%s{what} exceeded the explicit %d{limits.MaxItems}-item limit"))
                                else
                                    match page.NextCursor with
                                    | Some next when Set.contains next seenCursors ->
                                        Error(Malformed(subject, $"%s{what} repeated cursor `%s{next}`"))
                                    | Some next ->
                                        loop (Some next) (pages + 1) stableTotal (Set.add next seenCursors) (Set.union seenKeys (Set.ofList page.Keys)) all
                                    | None ->
                                        match stableTotal with
                                        | Some expected when expected <> all.Length ->
                                            Error(Malformed(subject, $"%s{what} ended with %d{all.Length} items but reported totalCount %d{expected}"))
                                        | _ -> Ok all

            loop None 0 None Set.empty Set.empty []

    let partialMutation (subject: string) (body: string) : IoResult<string list * (string * string) list> =
        if String.IsNullOrWhiteSpace body then Error(Malformed(subject, "the GraphQL response body was empty")) else
        try
            use document = JsonDocument.Parse body
            let root = document.RootElement
            if root.ValueKind <> JsonValueKind.Object then Error(Malformed(subject, "the batch response was not an object")) else
            let failures =
                match root.TryGetProperty "errors" with
                | true, errors when errors.ValueKind = JsonValueKind.Array ->
                    errors.EnumerateArray()
                    |> Seq.choose (fun error ->
                        let alias =
                            match error.TryGetProperty "path" with
                            | true, path when path.ValueKind = JsonValueKind.Array && path.GetArrayLength() > 0 ->
                                let first = path.EnumerateArray() |> Seq.head
                                if first.ValueKind = JsonValueKind.String then Some(first.GetString()) else None
                            | _ -> None
                        let message =
                            match error.TryGetProperty "message" with
                            | true, value when value.ValueKind = JsonValueKind.String -> value.GetString()
                            | _ -> "(no message)"
                        alias |> Option.map (fun value -> value, message))
                    |> List.ofSeq
                | _ -> []
            let applied =
                match root.TryGetProperty "data" with
                | true, data when data.ValueKind = JsonValueKind.Object ->
                    data.EnumerateObject()
                    |> Seq.filter (fun property -> property.Value.ValueKind <> JsonValueKind.Null)
                    |> Seq.map (fun property -> property.Name)
                    |> List.ofSeq
                | _ -> []
            Ok(applied, failures)
        with :? JsonException -> Error(Malformed(subject, "the batch response carried errors we could not read"))
