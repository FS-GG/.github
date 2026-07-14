namespace FS.GG.Coord.GitHub

module Board =

    open System
    open System.Collections.Generic
    open System.Text.Json
    open Errors
    open Transport

    type FieldType =
        | SingleSelect of options: Map<string, string>
        | Text
        | Number
        | Date
        | Iteration

    type Field = { Id: string; Type: FieldType }

    type BoardMap =
        { Number: int
          Id: string
          Owner: string
          Title: string
          Fields: Map<string, Field> }

    type FieldWrite =
        | Set of value: string
        | Clear

    type WriteOutcome =
        | Written
        | Deferred
        | NotOnBoard

    // ---- GraphQL responses -------------------------------------------------------------------------

    /// Read a GraphQL response, distinguishing the four things it can be.
    ///
    /// **THE ORDER IS THE CONTRACT.** A rate limit is tested FIRST, before the errors are looked at as a
    /// partial write — because GitHub reports an exhausted GraphQL budget as an HTTP **200** carrying
    /// `errors`, exactly like a failed alias. Test the partial arm first and an exhausted budget is
    /// misreported as *"the board is half-written"*: the caller would then refuse to queue it (a partial is
    /// never queued), and the write would be silently lost on a condition that was only ever temporary.
    let private graphQlData (subject: string) (body: string) : IoResult<JsonElement> =
        if String.IsNullOrWhiteSpace body then
            Error(Malformed(subject, "the GraphQL response body was empty"))
        else

        try
            let doc = JsonDocument.Parse body
            let root = doc.RootElement

            let errors =
                match root.TryGetProperty "errors" with
                | true, e when e.ValueKind = JsonValueKind.Array && e.GetArrayLength() > 0 -> Some e
                | _ -> None

            match errors with
            | None ->
                match root.TryGetProperty "data" with
                | true, data when data.ValueKind <> JsonValueKind.Null -> Ok data
                | _ -> Error(Malformed(subject, "the GraphQL response carried neither `data` nor `errors`"))

            | Some e ->
                let messages =
                    e.EnumerateArray()
                    |> Seq.map (fun err ->
                        match err.TryGetProperty "message" with
                        | true, m when m.ValueKind = JsonValueKind.String -> m.GetString()
                        | _ -> "(no message)")
                    |> List.ofSeq

                // RATE LIMIT FIRST. See above — this ordering is load-bearing.
                if messages |> List.exists Budget.isRateLimited then
                    Error(RateLimited None)
                else
                    Error(GraphQlErrors messages)

        with :? JsonException as e ->
            Error(Malformed(subject, $"the GraphQL response is not JSON: %s{e.Message}"))

    let private query (document: string) (variables: (string * Var) list) (subject: string) =
        { Method = "POST"
          Path = "graphql"
          Query = []
          Body = Query(document, variables)
          Budget = GraphQl
          IfNoneMatch = None
          Subject = subject }

    // ---- bootstrap ---------------------------------------------------------------------------------

    [<Literal>]
    let private ProjectsDoc =
        "query($owner: String!) { organization(login: $owner) { projectsV2(first: 50) { nodes { number title id } } } rateLimit { cost remaining } }"

    [<Literal>]
    let private FieldsDoc =
        "query($owner: String!, $number: Int!) { organization(login: $owner) { projectV2(number: $number) { fields(first: 50) { nodes { ... on ProjectV2FieldCommon { id name dataType } ... on ProjectV2SingleSelectField { id name dataType options { id name } } } } } } rateLimit { cost remaining } }"

    let private fieldTypeOf (dataType: string) (node: JsonElement) =
        match dataType with
        | "SINGLE_SELECT" ->
            let options =
                match node.TryGetProperty "options" with
                | true, opts when opts.ValueKind = JsonValueKind.Array ->
                    opts.EnumerateArray()
                    |> Seq.choose (fun o ->
                        let get (n: string) =
                            match o.TryGetProperty n with
                            | true, v when v.ValueKind = JsonValueKind.String -> Some(v.GetString())
                            | _ -> None

                        match get "name", get "id" with
                        | Some name, Some id -> Some(name, id)
                        | _ -> None)
                    |> Map.ofSeq
                | _ -> Map.empty

            Some(SingleSelect options)

        | "TEXT" -> Some Text
        | "NUMBER" -> Some Number
        | "DATE" -> Some Date
        | "ITERATION" -> Some Iteration
        // A FIELD TYPE WE WERE NEVER TAUGHT IS SKIPPED, NOT GUESSED. Coercing it to TEXT would route a
        // value into a mutation shape the API will reject — or, worse, accept and store wrong.
        | _ -> None

    let bootstrap (transport: IGitHubTransport) (owner: string) (title: string) : IoResult<BoardMap> =
        let subject = $"the board '%s{title}' in %s{owner}"

        match transport.Send(query ProjectsDoc [ "owner", VString owner ] subject) with
        | Error e -> Error e
        | Ok projectsResponse ->

        match graphQlData subject projectsResponse.Body with
        | Error e -> Error e
        | Ok data ->

        let project =
            try
                data.GetProperty("organization").GetProperty("projectsV2").GetProperty("nodes").EnumerateArray()
                |> Seq.tryFind (fun n ->
                    match n.TryGetProperty "title" with
                    | true, t when t.ValueKind = JsonValueKind.String -> t.GetString() = title
                    | _ -> false)
            with :? KeyNotFoundException ->
                None

        match project with
        | None ->
            // A BOARD WE READ AND DID NOT FIND IS A REAL ANSWER, and it is a configuration error, not a
            // transient. Naming the title back is what makes it fixable — the usual cause is
            // `FSGG_COORD_PROJECT` pointing at a board that was renamed.
            Error(NotFound $"no Projects v2 board titled '%s{title}' in %s{owner}")

        | Some p ->

        let number = p.GetProperty("number").GetInt32()
        let id = p.GetProperty("id").GetString()

        match transport.Send(query FieldsDoc [ "owner", VString owner; "number", VNumber(double number) ] subject) with
        | Error e -> Error e
        | Ok fieldsResponse ->

        match graphQlData subject fieldsResponse.Body with
        | Error e -> Error e
        | Ok fieldsData ->

        let fields =
            try
                fieldsData
                    .GetProperty("organization")
                    .GetProperty("projectV2")
                    .GetProperty("fields")
                    .GetProperty("nodes")
                    .EnumerateArray()
                |> Seq.choose (fun n ->
                    let get (name: string) =
                        match n.TryGetProperty name with
                        | true, v when v.ValueKind = JsonValueKind.String -> Some(v.GetString())
                        | _ -> None

                    match get "name", get "id", get "dataType" with
                    | Some name, Some fid, Some dt ->
                        fieldTypeOf dt n |> Option.map (fun t -> name, { Id = fid; Type = t })
                    | _ -> None)
                |> Map.ofSeq
            with :? KeyNotFoundException ->
                Map.empty

        if Map.isEmpty fields then
            // A BOARD WITH NO FIELDS IS A READ THAT WENT WRONG. Every board has at least `Status`, so an
            // empty map is not an austere board — it is a document we failed to walk, and caching it would
            // make every subsequent write fail with "no field named Status" for a day.
            Error(Malformed(subject, "the board reported no fields at all — refusing to cache a field map we could not read"))
        else
            Ok
                { Number = number
                  Id = id
                  Owner = owner
                  Title = title
                  Fields = fields }

    // ---- the item id -------------------------------------------------------------------------------

    [<Literal>]
    let private ItemIdDoc =
        "query($owner: String!, $repo: String!, $number: Int!) { repository(owner: $owner, name: $repo) { issue(number: $number) { projectItems(first: 20) { nodes { id project { number } } } } } rateLimit { cost remaining } }"

    let itemId
        (transport: IGitHubTransport)
        (board: BoardMap)
        (owner: string)
        (repo: string)
        (number: int)
        : IoResult<string option> =

        let subject = $"%s{owner}/%s{repo}#%d{number}"

        let request =
            query
                ItemIdDoc
                [ "owner", VString owner
                  "repo", VString repo
                  "number", VNumber(double number) ]
                subject

        match transport.Send request with
        // #421, AND IT IS THE WHOLE FUNCTION. An exhausted budget propagates as `RateLimited`. It does NOT
        // become `Ok None` — because `Ok None` is what licenses `item-add`, and adding an item on a failed
        // lookup CREATES A DUPLICATE for an issue that was already on the board. A failed lookup is not an
        // absence.
        | Error e -> Error e
        | Ok response ->

        match graphQlData subject response.Body with
        | Error e -> Error e
        | Ok data ->

        try
            let issue = data.GetProperty("repository").GetProperty("issue")

            if issue.ValueKind = JsonValueKind.Null then
                Error(NotFound subject)
            else

            let found =
                issue.GetProperty("projectItems").GetProperty("nodes").EnumerateArray()
                |> Seq.tryPick (fun n ->
                    let onThisBoard =
                        match n.TryGetProperty "project" with
                        | true, p when p.ValueKind = JsonValueKind.Object ->
                            match p.TryGetProperty "number" with
                            | true, num when num.ValueKind = JsonValueKind.Number -> num.GetInt32() = board.Number
                            | _ -> false
                        | _ -> false

                    // AN ISSUE CAN BE ON SEVERAL BOARDS. Narrowing to OURS is not a detail: writing a Status
                    // to another board's item is a silent cross-board write, and it would look like a
                    // no-op here and like vandalism over there.
                    if onThisBoard then
                        match n.TryGetProperty "id" with
                        | true, i when i.ValueKind = JsonValueKind.String -> Some(i.GetString())
                        | _ -> None
                    else
                        None)

            // `None` HERE IS A SUCCESSFUL READ. We reached the board, we walked its items, and this issue is
            // not among them. That is the only path to `item-add`, and it cannot be reached from a failure.
            Ok found

        with :? KeyNotFoundException ->
            Error(Malformed(subject, "the item lookup response is missing `repository.issue.projectItems`"))

    // ---- one field write ----------------------------------------------------------------------------

    /// GraphQL string syntax IS JSON string syntax, so a JSON encoder is a correct GraphQL string encoder.
    /// The aliased batch document interpolates its values inline (it cannot use variables and aliases
    /// together without naming N×4 of them), so this is what stands between a title containing a quote and
    /// a document that does not parse.
    let private gqlStr (s: string) = JsonSerializer.Serialize s

    /// The value clause, and the shape of the mutation, for one write.
    ///
    /// A NUMBER IS VALIDATED BY A REAL NUMERIC PARSE, not a character class. `1.2.3`, `e`, `+`, and `--`
    /// are all made of legal numeric characters, and every one of them emits a document that does not
    /// parse — a whole batch lost to a value nobody checked.
    let private valueClause (field: Field) (write: FieldWrite) : Result<string * (string * Var) list, string> =
        match write with
        | Clear -> Ok("", [])
        | Set "" ->
            // REFUSED, LOUDLY. `Set ""` means one of two things and we cannot know which — and the API's
            // own answer to an empty text write is "no changes to make", a silent no-op that leaves the old
            // value on the board. A caller who means to clear has a case for it.
            Error
                "an empty value is ambiguous: it is not a write of the empty string, and the API would treat it as a no-op that silently leaves the old value in place. Use `Clear` if you mean to clear the field."

        | Set value ->
            match field.Type with
            | SingleSelect options ->
                match Map.tryFind value options with
                | Some optionId -> Ok("value: {singleSelectOptionId: $optionId}", [ "optionId", VId optionId ])
                | None ->
                    let known = options |> Map.keys |> String.concat ", "

                    Error $"'%s{value}' is not an option on this field. Known options: %s{known}"

            | Text -> Ok("value: {text: $text}", [ "text", VString value ])

            | Number ->
                match Double.TryParse(value, Globalization.NumberStyles.Float, Globalization.CultureInfo.InvariantCulture) with
                | true, n -> Ok("value: {number: $number}", [ "number", VNumber n ])
                | _ -> Error $"'%s{value}' is not a number, and a NUMBER field cannot hold it."

            | Date -> Ok("value: {date: $date}", [ "date", VString value ])
            | Iteration -> Ok("value: {iterationId: $iterationId}", [ "iterationId", VId value ])

    let setField
        (transport: IGitHubTransport)
        (board: BoardMap)
        (itemId: string)
        (fieldName: string)
        (write: FieldWrite)
        : IoResult<unit> =

        let subject = $"the board field '%s{fieldName}'"

        match Map.tryFind fieldName board.Fields with
        | None ->
            let known = board.Fields |> Map.keys |> String.concat ", "

            // A REJECTED VALUE MUST COST ZERO GRAPHQL. We refuse before a single point is spent, on the
            // budget that dies first.
            Error(Http(422, $"no field named '%s{fieldName}' on this board. Known fields: %s{known}"))

        | Some field ->

        match valueClause field write with
        | Error message -> Error(Http(422, $"%s{fieldName}: %s{message}"))
        | Ok(clause, valueVars) ->

        let document, variables =
            let common =
                [ "projectId", VId board.Id
                  "itemId", VId itemId
                  "fieldId", VId field.Id ]

            match write with
            | Clear ->
                // A DIFFERENT MUTATION ENTIRELY. `updateProjectV2ItemFieldValue` with an empty value is a
                // no-op; this is the call that actually removes the value.
                "mutation($projectId: ID!, $itemId: ID!, $fieldId: ID!) { clearProjectV2ItemFieldValue(input: {projectId: $projectId, itemId: $itemId, fieldId: $fieldId}) { clientMutationId } }",
                common

            | Set _ ->
                let varDecls =
                    valueVars
                    |> List.map (fun (name, v) ->
                        let t =
                            match v with
                            | VId _ -> "ID!"
                            | VNumber _ -> "Float!"
                            | VString _ -> "String!"

                        $"$%s{name}: %s{t}")
                    |> String.concat ", "

                $"mutation($projectId: ID!, $itemId: ID!, $fieldId: ID!, %s{varDecls}) {{ updateProjectV2ItemFieldValue(input: {{projectId: $projectId, itemId: $itemId, fieldId: $fieldId, %s{clause}}}) {{ clientMutationId }} }}",
                common @ valueVars

        match transport.Send(query document variables subject) with
        | Error e -> Error e
        | Ok response -> graphQlData subject response.Body |> Result.map ignore

    // ---- the aliased batch (#448) --------------------------------------------------------------------

    let setFieldBatch
        (transport: IGitHubTransport)
        (board: BoardMap)
        (itemId: string)
        (writes: (string * FieldWrite) list)
        : IoResult<unit> =

        let subject = $"%d{List.length writes} board field(s)"

        if List.isEmpty writes then
            Error(Http(422, "a batch with no writes in it writes nothing. Say what you mean to set."))
        else

        // EVERYTHING IS RESOLVED AND VALIDATED BEFORE A SINGLE MUTATION IS EMITTED. A bad pair caught late
        // would not merely waste a point: mutations run SERIALLY, so it would fail the document AFTER its
        // earlier aliases had already been written to the board. A half-written board is a much worse
        // outcome than a refused one, and it is entirely avoidable — the check is free and it is here.
        let resolved =
            writes
            |> List.mapi (fun i (name, write) ->
                match Map.tryFind name board.Fields with
                | None ->
                    let known = board.Fields |> Map.keys |> String.concat ", "
                    Error $"no field named '%s{name}' on this board. Known fields: %s{known}"
                | Some field ->
                    match valueClause field write with
                    | Error m -> Error $"%s{name}: %s{m}"
                    | Ok(_, valueVars) -> Ok(i, field, write, valueVars))

        match resolved |> List.tryPick (function | Error m -> Some m | Ok _ -> None) with
        | Some message -> Error(Http(422, message))
        | None ->

        let entries = resolved |> List.choose (function | Ok r -> Some r | Error _ -> None)

        // The aliases interpolate INLINE rather than using variables: aliasing N mutations would otherwise
        // need N×4 declared variables, and the document becomes unreadable. `gqlStr` is what makes that
        // safe — GraphQL string syntax is JSON string syntax.
        let aliases =
            entries
            |> List.map (fun (i, field, write, valueVars) ->
                let common =
                    $"projectId: %s{gqlStr board.Id}, itemId: %s{gqlStr itemId}, fieldId: %s{gqlStr field.Id}"

                match write with
                | Clear -> $"f%d{i}: clearProjectV2ItemFieldValue(input: {{%s{common}}}) {{ clientMutationId }}"
                | Set _ ->
                    let inline' =
                        valueVars
                        |> List.map (fun (name, v) ->
                            let rendered =
                                match v with
                                | VId s -> gqlStr s
                                | VString s -> gqlStr s
                                | VNumber n -> string n

                            let key =
                                match name with
                                | "optionId" -> "singleSelectOptionId"
                                | other -> other

                            $"%s{key}: %s{rendered}")
                        |> String.concat ", "

                    $"f%d{i}: updateProjectV2ItemFieldValue(input: {{%s{common}, value: {{%s{inline'}}}}}) {{ clientMutationId }}")
            |> String.concat " "

        // NO `rateLimit` SELECTION. It is a field of the QUERY root, not the Mutation root, so selecting it
        // here is a document that does not parse. This is the one call whose cost the meter cannot read —
        // which is fine, because its cost is exactly the thing this function makes constant: one document,
        // one request, one point at the floor.
        let document = $"mutation {{ %s{aliases} }}"

        match transport.Send(query document [] subject) with
        | Error e -> Error e
        | Ok response ->

        // THE PARTIAL-APPLY ARM. A GraphQL failure mid-document arrives as HTTP 200 carrying BOTH `data`
        // and `errors`, and `errors[].path[0]` names the failing ALIAS. Mutations execute SERIALLY, so the
        // aliases before the failure DID land — the body tells us exactly which.
        match graphQlData subject response.Body with
        | Ok _ -> Ok()

        // A rate limit is not a partial write. `graphQlData` tests for it FIRST, so by the time we get a
        // `GraphQlErrors` we know the budget was not the cause.
        | Error(RateLimited r) -> Error(RateLimited r)

        | Error(GraphQlErrors _) ->
            try
                use doc = JsonDocument.Parse response.Body
                let root = doc.RootElement

                let failedAliases =
                    match root.TryGetProperty "errors" with
                    | true, errs when errs.ValueKind = JsonValueKind.Array ->
                        errs.EnumerateArray()
                        |> Seq.choose (fun e ->
                            let alias =
                                match e.TryGetProperty "path" with
                                | true, p when p.ValueKind = JsonValueKind.Array && p.GetArrayLength() > 0 ->
                                    let first = p.EnumerateArray() |> Seq.head
                                    if first.ValueKind = JsonValueKind.String then Some(first.GetString()) else None
                                | _ -> None

                            let message =
                                match e.TryGetProperty "message" with
                                | true, m when m.ValueKind = JsonValueKind.String -> m.GetString()
                                | _ -> "(no message)"

                            alias |> Option.map (fun a -> a, message))
                        |> List.ofSeq
                    | _ -> []

                let applied =
                    match root.TryGetProperty "data" with
                    | true, data when data.ValueKind = JsonValueKind.Object ->
                        data.EnumerateObject()
                        |> Seq.filter (fun p -> p.Value.ValueKind <> JsonValueKind.Null)
                        |> Seq.map (fun p -> p.Name)
                        |> List.ofSeq
                    | _ -> []

                if List.isEmpty applied then
                    // NOTHING LANDED. This is a clean failure, and it is safe to queue or retry: the board
                    // is exactly as it was.
                    Error(GraphQlErrors(failedAliases |> List.map snd))
                else
                    // SOME OF IT LANDED. `EX_PARTIAL`, and it is NEVER queued — replaying the document would
                    // rewrite the half that already took effect.
                    Error(Partial(applied, failedAliases))

            with :? JsonException ->
                Error(Malformed(subject, "the batch response carried errors we could not read"))

        | Error e -> Error e

    // ---- THE ONE BOARD WRITE (#510) ------------------------------------------------------------------

    /// ATTEMPT the write. No queue, no policy — just "did it land?".
    ///
    /// THE SPLIT IS THE POINT, AND IT IS NOT COSMETIC. `boardWrite` and `flush` want the same ATTEMPT and
    /// opposite POLICIES: a first attempt that meets an exhausted budget should be QUEUED; a REPLAY of an
    /// already-queued entry that meets an exhausted budget must NOT be — it is already in the queue, and
    /// appending it again duplicates it.
    ///
    /// Folding the policy into the attempt is how `flush` came to re-queue every entry it replayed: each
    /// pass under a dead budget doubled the queue, forever, while reporting that it had written nothing and
    /// backing off from nothing. One function that both callers can share, and each adds its own policy on
    /// top.
    let private attempt
        (transport: IGitHubTransport)
        (board: BoardMap)
        (owner: string)
        (repo: string)
        (number: int)
        (field: string)
        (write: FieldWrite)
        : IoResult<WriteOutcome> =

        match itemId transport board owner repo number with
        | Error e -> Error e

        // A SUCCESSFUL READ THAT FOUND NOTHING. The issue is genuinely not on this board — permanent, and
        // NOT queued: `flush` would drop it too, which would be a second promise nobody could keep. This is
        // reachable ONLY from a successful lookup, which is the whole of #421.
        | Ok None -> Ok NotOnBoard

        | Ok(Some item) ->
            match setField transport board item field write with
            | Error e -> Error e
            | Ok() ->
                // FOLD OUR OWN WRITE INTO THE CACHED SCAN rather than invalidating it. Invalidating would
                // send the very next `take` back to a full-board scan — and a claim is ALWAYS followed by a
                // take — so the cache would never survive the loop it exists for.
                // THE BOARD'S OWN NAME, not a hardcoded one. The scan cache is keyed on (owner, title),
                // and `FSGG_COORD_OWNER`/`PROJECT` can point this client at a different board — so a
                // hardcoded title would fold the write into another board's cache file, or into none at
                // all. Both are invisible.
                Cache.patchScan
                    board.Owner
                    board.Title
                    repo
                    number
                    field
                    (match write with
                     | Set v -> v
                     | Clear -> "")

                Ok Written

    let boardWrite
        (transport: IGitHubTransport)
        (board: BoardMap)
        (owner: string)
        (repo: string)
        (number: int)
        (field: string)
        (write: FieldWrite)
        (worker: string)
        : IoResult<WriteOutcome> =

        match attempt transport board owner repo number field write with
        | Ok outcome -> Ok outcome

        | Error e ->
            // THE QUEUE'S GATE, IN ONE PLACE. `Cache.defer` takes the ERROR, so it cannot be called on a
            // failure that does not license it — and only an exhausted budget does. Everything else is
            // permanent: `flush` would replay it forever, each replay failing identically, the queue never
            // draining, and the refusal never reaching the human who could fix it. That is #510.
            if isQueueable e then
                let entry: Cache.Deferred =
                    { Ref = $"%s{owner}/%s{repo}#%d{number}"
                      Field = field
                      Value =
                        match write with
                        | Set v -> v
                        | Clear -> ""
                      At = DateTimeOffset.UtcNow.ToString("o")
                      Worker = worker }

                match Cache.defer e entry with
                | Ok() -> Ok Deferred
                | Error queueError -> Error queueError
            else
                // REPORTED, NEVER SWALLOWED. A refusal nobody can read is a refusal that did not happen.
                Error e

    let flush (transport: IGitHubTransport) (board: BoardMap) : IoResult<int> =
        match Cache.pending () with
        | Error e -> Error e
        | Ok [] -> Ok 0
        | Ok entries ->

        let mutable written = 0
        let mutable stopped = None

        for entry in entries do
            if stopped.IsNone then
                let parts = entry.Ref.Split([| '/'; '#' |], StringSplitOptions.RemoveEmptyEntries)

                // `int parts.[2]` would THROW on a ref whose number is not a number, and an exception here
                // aborts the whole flush — abandoning every entry after it, on the strength of one bad line.
                // A queue entry we cannot parse will never land; it is dropped, not retried, because
                // retrying it is an infinite loop that reports progress.
                let parsed =
                    if parts.Length < 3 then
                        None
                    else
                        match Int32.TryParse parts.[2] with
                        | true, n -> Some(parts.[0], parts.[1], n)
                        | _ -> None

                match parsed with
                | None -> Cache.dropPending entry
                | Some(owner, repo, number) ->

                    let write =
                        if entry.Value = "" then Clear else Set entry.Value

                    // REPLAY THROUGH `attempt`, NOT `boardWrite`. `boardWrite` carries the DEFER policy —
                    // it queues on an exhausted budget — and that is exactly right for a FIRST attempt and
                    // exactly wrong for a replay: this entry is ALREADY in the queue, so deferring it again
                    // appends a second copy. Every flush under a dead budget would double the queue,
                    // forever, while reporting that it had written nothing and backing off from nothing.
                    match attempt transport board owner repo number entry.Field write with
                    | Ok Written
                    | Ok NotOnBoard ->
                        // `NotOnBoard` is DROPPED, not retried. It is permanent, and `boardWrite` would not
                        // have queued it in the first place — but an entry queued before the item was
                        // removed from the board can still reach here, and carrying it forever would mean
                        // the queue never drains.
                        Cache.dropPending entry
                        written <- written + 1

                    | Ok Deferred ->
                        // `attempt` cannot return this — it has no queue. The case exists so that a future
                        // outcome added to the DU forces a decision here rather than silently falling into
                        // "drop it".
                        ()

                    | Error(RateLimited _ as e) ->
                        // AN EXHAUSTED BUDGET STOPS THE WHOLE FLUSH. The rest would fail identically, and
                        // spending REST calls to confirm that is exactly the back-off EX_RATE exists to
                        // signal. The remainder STAYS QUEUED — untouched, and not re-appended.
                        stopped <- Some e

                    | Error _ ->
                        // A PERMANENTLY UN-WRITABLE ENTRY IS DROPPED, LOUDLY. It will never land, and
                        // carrying it forever means the queue never drains and nobody is ever told why.
                        Cache.dropPending entry

        match stopped with
        | Some e -> Error e
        | None -> Ok written
