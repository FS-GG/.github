namespace FS.GG.Coord.GitHub

module Board =

    open System
    open System.Collections.Generic
    open System.IO
    open System.Text.Json
    open FS.GG.Coord.Types
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

                // RATE LIMIT FIRST. See above — this ordering is load-bearing. `ofGraphQlErrors` also
                // tells a SECONDARY limit from the primary budget (#1666); this site used to call
                // `isRateLimited`, which matches both, and reported either as the GraphQL budget.
                match Budget.ofGraphQlErrors messages with
                | Some limited -> Error limited
                | None -> Error(GraphQlErrors messages)

        with :? JsonException as e ->
            Error(Malformed(subject, $"the GraphQL response is not JSON: %s{e.Message}"))

    /// **"IS THERE ANOTHER PAGE, AND WHERE DOES IT START?"** — the one place `pageInfo` becomes an answer.
    ///
    /// COMPLETENESS IS A REQUIRED BOOLEAN, NOT AN OPTIONAL HINT. A missing, null, or non-Boolean
    /// `hasNextPage` used to fall through as `false` in the external-owner walk and turn an incomplete read
    /// into the definite absence `Ok None` — and `Ok None` is what licenses `item-add` (#2166). Every arm
    /// that cannot establish "that was all" is therefore a refusal.
    ///
    /// Hoisted out of `externalItemId` by `.github#2535`, which needed the identical walk for the project
    /// list, with the messages preserved byte-for-byte via `what`. Two copies of a completeness guard is
    /// the drift that item is about, so the paginating remedy has exactly one implementation, as the
    /// `totalCount` remedy (`Reads.connectionComplete`) does.
    let private nextPage (subject: string) (what: string) (connection: JsonElement) : IoResult<string option> =
        // `TryGetProperty` THROWS on a non-object rather than answering `false` (`.github#2418`), and both
        // call sites sit inside a `try/with :? KeyNotFoundException` that would not catch it.
        if connection.ValueKind <> JsonValueKind.Object then
            Error(Malformed(subject, $"%s{what} is not a JSON object (%A{connection.ValueKind}) — this is a FAILED READ"))
        else

        match connection.TryGetProperty "pageInfo" with
        | true, pageInfo when pageInfo.ValueKind = JsonValueKind.Object ->
            match pageInfo.TryGetProperty "hasNextPage" with
            | true, hasNext when hasNext.ValueKind = JsonValueKind.False -> Ok None
            | true, hasNext when hasNext.ValueKind = JsonValueKind.True ->
                match pageInfo.TryGetProperty "endCursor" with
                | true, value when
                    value.ValueKind = JsonValueKind.String && not (String.IsNullOrWhiteSpace(value.GetString()))
                    ->
                    Ok(Some(value.GetString()))
                | _ -> Error(Malformed(subject, $"%s{what} has another page but no usable cursor"))
            | _ ->
                // COMPLETENESS IS A REQUIRED BOOLEAN, not an optional hint. Missing/null/string used to
                // fall through as `false` and turn an incomplete read into the definite absence `Ok None`.
                Error(Malformed(subject, $"%s{what}'s `pageInfo.hasNextPage` is missing or is not a Boolean"))
        | _ -> Error(Malformed(subject, $"%s{what}'s `pageInfo` is missing or is not an object"))

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
        "query($owner: String!, $cursor: String) { organization(login: $owner) { projectsV2(first: 50, after: $cursor) { pageInfo { hasNextPage endCursor } nodes { number title id } } } rateLimit { cost remaining } }"

    [<Literal>]
    let private FieldsDoc =
        "query($owner: String!, $number: Int!) { organization(login: $owner) { projectV2(number: $number) { fields(first: 50) { totalCount nodes { ... on ProjectV2FieldCommon { id name dataType } ... on ProjectV2SingleSelectField { id name dataType options { id name } } } } } } rateLimit { cost remaining } }"

    /// `FieldsDoc`'s own window, as the guard must see it — the two MUST agree, and
    /// `.github#2535 the connection windows in the documents agree with the guards` pins them together.
    [<Literal>]
    let private FieldsWindow = 50

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

        // OWNER-KIND AWARE (#1344, #1349). An org-owned board answers to `organization(login:)`, a user-owned
        // one to `user(login:)`, and a token's OWN board to `viewer` (no login). `Org` is the default and its
        // document/parse path are byte-identical to what preceded this — the FS-GG board does not move.
        // `ownerVars` binds `$owner` for Org/User and NOTHING for Viewer, in lockstep with the document.
        let kind = OwnerKind.fromEnv ()
        let ownerField = OwnerKind.ownerField kind
        let ownerVars = OwnerKind.ownerVars kind owner

        // **THE BOARD LOOKUP WALKS EVERY PROJECT, NOT THE FIRST FIFTY** (`.github#2535`). `projectsV2(first:
        // 50)` with no cursor answers the same shape whether the owner has 50 projects or 500, so a board
        // that happened to be #51 came back as `NotFound` — printed to the operator as *"no Projects v2
        // board titled 'X'"*, a definite CONFIGURATION verdict manufactured from a window nobody announced,
        // and one whose remedy ("check `FSGG_COORD_PROJECT`") is wrong for the actual condition. This is
        // `externalItemId`'s walk, sharing its `nextPage` guard and its 100-page ceiling: a missing or
        // non-Boolean `hasNextPage` is a refusal, never "that was all".
        let rec findProject (cursor: string option) (guard: int) : IoResult<(int * string) option> =
            if guard <= 0 then
                Error(Malformed(subject, "the board lookup did not terminate within 100 pages of projects"))
            else

            let variables =
                ownerVars
                @ (match cursor with
                   | Some value -> [ "cursor", VString value ]
                   | None -> [])

            match transport.Send(query (OwnerKind.forOwner kind ProjectsDoc) variables subject) with
            | Error e -> Error e
            | Ok projectsResponse ->

            match graphQlData subject projectsResponse.Body with
            | Error e -> Error e
            | Ok data ->

            let connection =
                try
                    let root = data.GetProperty ownerField

                    if root.ValueKind = JsonValueKind.Null then
                        None
                    else
                        Some(root.GetProperty "projectsV2")
                with :? KeyNotFoundException ->
                    None

            match connection with
            | None -> Error(Malformed(subject, $"the project-list response is missing `%s{ownerField}.projectsV2`"))
            | Some connection ->

            let matched =
                try
                    connection.GetProperty("nodes").EnumerateArray()
                    |> Seq.tryFind (fun n ->
                        match n.TryGetProperty "title" with
                        | true, t when t.ValueKind = JsonValueKind.String -> t.GetString() = title
                        | _ -> false)
                    |> Ok
                with :? KeyNotFoundException ->
                    Error(Malformed(subject, $"the project-list response is missing `%s{ownerField}.projectsV2.nodes`"))

            match matched with
            | Error e -> Error e
            | Ok(Some p) ->
                // A BOARD WE FOUND AND CANNOT NAME IS A FAILED READ, not an absence and not a crash. The
                // pre-`.github#2535` code read `number`/`id` unguarded, outside every `try`.
                match p.TryGetProperty "number", p.TryGetProperty "id" with
                | (true, number), (true, id) when
                    number.ValueKind = JsonValueKind.Number && id.ValueKind = JsonValueKind.String
                    ->
                    match number.TryGetInt32() with
                    | true, value -> Ok(Some(value, id.GetString()))
                    | _ -> Error(Malformed(subject, "the board's `number` is not a 32-bit integer"))
                | _ ->
                    Error(Malformed(subject, $"the board titled '%s{title}' was found but carries no readable `number`/`id`"))

            // NOT ON THIS PAGE IS NOT NOT-ANYWHERE. Ask `pageInfo` before turning it into an absence.
            | Ok None ->
                match nextPage subject "the board lookup" connection with
                | Error e -> Error e
                | Ok None -> Ok None
                | Ok(Some next) -> findProject (Some next) (guard - 1)

        match findProject None 100 with
        | Error e -> Error e
        | Ok None ->
            // A BOARD WE READ AND DID NOT FIND IS A REAL ANSWER, and it is a configuration error, not a
            // transient. Naming the title back is what makes it fixable — the usual cause is
            // `FSGG_COORD_PROJECT` pointing at a board that was renamed. Since `.github#2535` this sentence
            // is entitled to be said: it is now measured over EVERY project, not the first page of them.
            Error(NotFound $"no Projects v2 board titled '%s{title}' in %s{owner}")

        | Ok(Some(number, id)) ->

        match transport.Send(query (OwnerKind.forOwner kind FieldsDoc) (ownerVars @ [ "number", VNumber(double number) ]) subject) with
        | Error e -> Error e
        | Ok fieldsResponse ->

        match graphQlData subject fieldsResponse.Body with
        | Error e -> Error e
        | Ok fieldsData ->

        let fieldsConnection =
            try
                let root = fieldsData.GetProperty ownerField

                if root.ValueKind = JsonValueKind.Null then
                    None
                else
                    let project = root.GetProperty "projectV2"

                    if project.ValueKind = JsonValueKind.Null then
                        None
                    else
                        Some(project.GetProperty "fields")
            with :? KeyNotFoundException ->
                None

        match fieldsConnection with
        | None -> Error(Malformed(subject, $"the field-map response is missing `%s{ownerField}.projectV2.fields`"))
        | Some fieldsConnection ->

        // **A PARTIAL FIELD MAP IS REFUSED, NOT CACHED** (`.github#2535`). `fields(first: 50)` with no
        // cursor silently drops the tail of a board with more than 50 fields, and the all-empty guard below
        // cannot see a PARTIAL map — only a wholly unreadable one. So a board map missing exactly the field
        // a later write needs was accepted, cached for a day, and every write to that field failed with the
        // misleading `no field named 'X' on this board. Known fields: …`, which recites the TRUNCATED list
        // as if it were the board's own. The completeness question is asked here, once, before anything is
        // built from the nodes — and a `totalCount` we cannot read is a refusal too.
        match Reads.connectionComplete subject "the board's field map" FieldsWindow fieldsConnection with
        | Error e -> Error e
        | Ok() ->

        let fields =
            try
                fieldsConnection.GetProperty("nodes").EnumerateArray()
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
            //
            // THIS GUARD IS STILL DISTINCT FROM THE COMPLETENESS CHECK ABOVE and neither subsumes the
            // other: `connectionComplete` catches a map truncated by the window (`totalCount` 60, 50
            // returned), this one catches a map that arrived complete-as-far-as-the-wire-goes and was
            // nonetheless unwalkable — `totalCount` 0 with an empty `nodes`, or nodes whose `dataType` we
            // were never taught, both of which are complete reads that produce nothing usable.
            Error(Malformed(subject, "the board reported no fields at all — refusing to cache a field map we could not read"))
        else
            Ok
                { Number = number
                  Id = id
                  Owner = owner
                  Title = title
                  Fields = fields }

    // ---- the board map, serialised (#418) ----------------------------------------------------------

    let private dataTypeName (t: FieldType) =
        match t with
        | SingleSelect _ -> "SINGLE_SELECT"
        | Text -> "TEXT"
        | Number -> "NUMBER"
        | Date -> "DATE"
        | Iteration -> "ITERATION"

    /// The board map as JSON. This is BOTH the `board` command's machine contract AND the on-disk cache
    /// format — one codec, so a `board` a human reads and a board `next` re-hydrates cannot drift. Written
    /// with a real JSON writer; a project title carrying a quote cannot forge the document. F# `Map`
    /// iterates in key order, so the output is deterministic.
    let boardToJson (board: BoardMap) : string =
        use stream = new MemoryStream()
        use w = new Utf8JsonWriter(stream, JsonWriterOptions(Indented = false, SkipValidation = false))

        w.WriteStartObject()
        w.WriteNumber("number", board.Number)
        w.WriteString("id", board.Id)
        w.WriteString("owner", board.Owner)
        w.WriteString("title", board.Title)
        w.WriteStartObject("fields")

        for KeyValue(name, field) in board.Fields do
            w.WriteStartObject(name)
            w.WriteString("id", field.Id)
            w.WriteString("dataType", dataTypeName field.Type)

            match field.Type with
            | SingleSelect options ->
                w.WriteStartObject("options")

                for KeyValue(optName, optId) in options do
                    w.WriteString(optName, optId)

                w.WriteEndObject()
            | _ -> ()

            w.WriteEndObject()

        w.WriteEndObject()
        w.WriteEndObject()
        w.Flush()
        Text.Encoding.UTF8.GetString(stream.ToArray())

    /// Re-hydrate a board map from its JSON. `None` on anything we cannot walk — a cache we cannot parse is
    /// a MISS, never a failure: the caller simply re-bootstraps.
    let private boardOfJson (json: string) : BoardMap option =
        try
            use doc = JsonDocument.Parse json
            let root = doc.RootElement

            let fields =
                root.GetProperty("fields").EnumerateObject()
                |> Seq.choose (fun f ->
                    let o = f.Value
                    let fid = o.GetProperty("id").GetString()

                    let ty =
                        match o.GetProperty("dataType").GetString() with
                        | "SINGLE_SELECT" ->
                            let options =
                                match o.TryGetProperty "options" with
                                | true, opts when opts.ValueKind = JsonValueKind.Object ->
                                    opts.EnumerateObject()
                                    |> Seq.map (fun p -> p.Name, p.Value.GetString())
                                    |> Map.ofSeq
                                | _ -> Map.empty

                            Some(SingleSelect options)
                        | "TEXT" -> Some Text
                        | "NUMBER" -> Some Number
                        | "DATE" -> Some Date
                        | "ITERATION" -> Some Iteration
                        | _ -> None

                    ty |> Option.map (fun t -> f.Name, { Id = fid; Type = t }))
                |> Map.ofSeq

            if Map.isEmpty fields then
                None
            else
                Some
                    { Number = root.GetProperty("number").GetInt32()
                      Id = root.GetProperty("id").GetString()
                      Owner = root.GetProperty("owner").GetString()
                      Title = root.GetProperty("title").GetString()
                      Fields = fields }
        with _ ->
            None

    /// `bootstrap`, served from the day-cache when it is warm (#418).
    ///
    /// This is the whole budget win: `bootstrap` is two GraphQL points, and it sits under EVERY worker
    /// command. Uncached, five workers looping `take` re-paid it every invocation — the exact drain #418
    /// is written about. A warm map costs zero, and the ids do not change under it.
    let bootstrapCached (transport: IGitHubTransport) (owner: string) (title: string) : IoResult<BoardMap> =
        let resolveAndStore () =
            match bootstrap transport owner title with
            | Ok board ->
                Cache.putBoardMap owner title (boardToJson board) |> ignore
                Ok board
            | Error e -> Error e

        match Cache.getBoardMap owner title with
        | Some cached ->
            match boardOfJson cached with
            | Some board -> Ok board
            | None -> resolveAndStore ()
        | None -> resolveAndStore ()

    // ---- the item id -------------------------------------------------------------------------------

    [<Literal>]
    let private ItemIdDoc =
        "query($owner: String!, $repo: String!, $number: Int!) { repository(owner: $owner, name: $repo) { issue(number: $number) { projectItems(first: 20) { totalCount nodes { id project { number } } } } } rateLimit { cost remaining } }"

    /// The window EVERY issue-side `projectItems` selection in this module asks for — `ItemIdDoc` here,
    /// `ItemStatusDoc` and `ItemBlockedByDoc` below — and therefore the one the guard must see. ONE literal
    /// for all three: three copies is three chances for a document and its guard to disagree, and
    /// `.github#2535 the connection windows in the documents agree with the guards` pins the whole set.
    [<Literal>]
    let private ProjectItemsWindow = 20

    let private repositoryItemId
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
        // become `Ok None` — because `Ok None` is what licenses `item-add`, and a failed lookup is not an
        // absence. Answering "not on the board" from a read that did not happen is a definite answer built
        // on no information, which is #266's class and the whole of #421.
        //
        // NOT because the add would duplicate the row (#871): `addProjectV2ItemById` is idempotent
        // server-side and resolves to the existing item — see `addItem` for the measurement. The guard is
        // about the ANSWER, not the row.
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

            let projectItems = issue.GetProperty "projectItems"

            let found =
                projectItems.GetProperty("nodes").EnumerateArray()
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

            match found with
            // FOUND IS FOUND. Truncation cannot unmake an answer we already have, so the completeness
            // question is only asked on the miss — the same rule `externalItemId`'s walk follows when it
            // returns on a hit without consulting `pageInfo`, and the reason this guard costs nothing on
            // the hot path.
            | Some _ -> Ok found

            | None ->
                // **`None` HERE IS A SUCCESSFUL READ, AND `.github#2535` IS WHAT MAKES THAT TRUE ON THE
                // SECOND AXIS.** We reached the board, we walked its items, and this issue is not among
                // them. That is the only path to `item-add`, and it cannot be reached from a failure —
                // which is #421 for a read that ERRORED. But `projectItems(first: 20)` had a second way to
                // manufacture the same definite "no": an issue sitting on more than twenty boards, ours
                // being the twenty-first, returns a full window that simply does not contain us, and
                // "walked its items" was a claim about a window rather than about the connection. Asking
                // `totalCount` is what turns the sentence above back into a measurement.
                match Reads.connectionComplete subject "this issue's project-item connection" ProjectItemsWindow projectItems with
                | Error e -> Error e
                | Ok() -> Ok None

        with :? KeyNotFoundException ->
            Error(Malformed(subject, "the item lookup response is missing `repository.issue.projectItems`"))

    [<Literal>]
    let private ExternalItemIdDoc =
        "query($projectId: ID!, $cursor: String) { node(id: $projectId) { ... on ProjectV2 { items(first: 100, after: $cursor) { pageInfo { hasNextPage endCursor } nodes { id content { ... on Issue { number repository { nameWithOwner } } } } } } } rateLimit { cost remaining } }"

    /// Resolve an issue owned outside the board owner from the board side of the relationship.
    ///
    /// GitHub can expose an external issue as a ProjectV2 item while omitting that placement from the
    /// issue-side `repository.issue.projectItems` connection.  The issue-side query therefore answers a
    /// false `None` for exactly the row that a fresh board scan can see (#2166).  Querying the project node
    /// preserves both identities at the lookup boundary: the item's `id` and its content's canonical
    /// `owner/repo#number`.  The full tuple is compared, so a default-owner twin cannot be selected merely
    /// because it has the same repository name and number.
    let private externalItemId
        (transport: IGitHubTransport)
        (board: BoardMap)
        (owner: string)
        (repo: string)
        (number: int)
        : IoResult<string option> =

        let subject = $"%s{owner}/%s{repo}#%d{number} on board %s{board.Owner}/%s{board.Title}"

        let same (left: string) (right: string) =
            String.Equals(left, right, StringComparison.OrdinalIgnoreCase)

        let rec page (cursor: string option) (guard: int) : IoResult<string option> =
            if guard <= 0 then
                Error(Malformed(subject, "the external-owner board item lookup did not terminate within 100 pages"))
            else
                let variables =
                    [ "projectId", VId board.Id ]
                    @ (match cursor with
                       | Some value -> [ "cursor", VString value ]
                       | None -> [])

                match transport.Send(query ExternalItemIdDoc variables subject) with
                | Error e -> Error e
                | Ok response ->
                    match graphQlData subject response.Body with
                    | Error e -> Error e
                    | Ok data ->
                        try
                            let project = data.GetProperty "node"

                            if project.ValueKind = JsonValueKind.Null then
                                Error(NotFound $"board node %s{board.Id}")
                            else
                                let items = project.GetProperty "items"

                                let found =
                                    items.GetProperty("nodes").EnumerateArray()
                                    |> Seq.tryPick (fun node ->
                                        match node.TryGetProperty "id", node.TryGetProperty "content" with
                                        | (true, id), (true, content)
                                            when id.ValueKind = JsonValueKind.String
                                                 && content.ValueKind = JsonValueKind.Object ->
                                            match content.TryGetProperty "number", content.TryGetProperty "repository" with
                                            | (true, issueNumber), (true, repository)
                                                when issueNumber.ValueKind = JsonValueKind.Number
                                                     && repository.ValueKind = JsonValueKind.Object ->
                                                match repository.TryGetProperty "nameWithOwner" with
                                                | true, nwo when nwo.ValueKind = JsonValueKind.String ->
                                                    let parts = nwo.GetString().Split('/', 2)

                                                    if
                                                        parts.Length = 2
                                                        && same parts.[0] owner
                                                        && same parts.[1] repo
                                                        && issueNumber.GetInt32() = number
                                                    then
                                                        Some(id.GetString())
                                                    else
                                                        None
                                                | _ -> None
                                            | _ -> None
                                        | _ -> None)

                                match found with
                                | Some id -> Ok(Some id)
                                | None ->
                                    // `.github#2535` hoisted this walk's `pageInfo` guard into `nextPage`
                                    // so `bootstrap`'s project list could share it. The refusal strings are
                                    // preserved byte-for-byte through `what`; the behaviour is unchanged.
                                    match nextPage subject "the external-owner board item lookup" items with
                                    | Error e -> Error e
                                    | Ok None -> Ok None
                                    | Ok(Some next) -> page (Some next) (guard - 1)
                        with :? KeyNotFoundException ->
                            Error(Malformed(subject, "the external-owner board item lookup response is missing `data.node.items`"))

        page None 100

    let itemId
        (transport: IGitHubTransport)
        (board: BoardMap)
        (owner: string)
        (repo: string)
        (number: int)
        : IoResult<string option> =

        if String.Equals(owner, board.Owner, StringComparison.OrdinalIgnoreCase) then
            repositoryItemId transport board owner repo number
        else
            externalItemId transport board owner repo number

    /// `itemId`, served from the forever-cache when it is warm.
    ///
    /// Item ids are stable, so a resolved id is kept forever — the second `item-id` for the same issue
    /// costs zero GraphQL. Only a FOUND id is cached: `Ok None` (#421 — genuinely not on the board) and
    /// `Error` are never memoised, because an item added later must still be found, and a failed read must
    /// never wear an absence's clothes.
    let itemIdCached
        (transport: IGitHubTransport)
        (board: BoardMap)
        (owner: string)
        (repo: string)
        (number: int)
        : IoResult<string option> =

        match Cache.getItemId owner repo number board.Number with
        | Some id -> Ok(Some id)
        | None ->
            match itemId transport board owner repo number with
            | Ok(Some id) ->
                Cache.putItemId owner repo number board.Number id
                Ok(Some id)
            | other -> other

    // ---- add: put an issue on the board (#861) -------------------------------------------------------

    [<Literal>]
    let private IssueNodeIdDoc =
        "query($owner: String!, $repo: String!, $number: Int!) { repository(owner: $owner, name: $repo) { issue(number: $number) { id } } rateLimit { cost remaining } }"

    [<Literal>]
    let private AddItemDoc =
        "mutation($projectId: ID!, $contentId: ID!) { addProjectV2ItemById(input: {projectId: $projectId, contentId: $contentId}) { item { id } } }"

    /// The issue's own node id — `addProjectV2ItemById`'s `contentId`, which is NOT the board item id.
    let private issueNodeId
        (transport: IGitHubTransport)
        (owner: string)
        (repo: string)
        (number: int)
        : IoResult<string> =

        let subject = $"%s{owner}/%s{repo}#%d{number}"

        let request =
            query
                IssueNodeIdDoc
                [ "owner", VString owner
                  "repo", VString repo
                  "number", VNumber(double number) ]
                subject

        match transport.Send request with
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

            match issue.TryGetProperty "id" with
            | true, id when id.ValueKind = JsonValueKind.String -> Ok(id.GetString())
            | _ -> Error(Malformed(subject, "the issue lookup response has no `id`"))

        with :? KeyNotFoundException ->
            Error(Malformed(subject, "the issue lookup response is missing `repository.issue`"))

    /// What `addItem` did. The caller needs to tell these apart: adding a second copy of an item is the
    /// failure this whole function is shaped around, so "it was already there" is a SUCCESS worth naming.
    type AddOutcome =
        /// The issue was already on this board. Nothing was written.
        | AlreadyOnBoard of itemId: string
        /// The issue was added. This is the only path that spends a mutation.
        | AddedToBoard of itemId: string

    /// Put an issue on the board, idempotently.
    ///
    /// #421 IS THE WHOLE FUNCTION, exactly as it is `itemId`'s: `Ok None` — a successful read that walked
    /// the board and did not find this issue — is the only thing that licenses the mutation, and an
    /// `Error` is a read that DID NOT HAPPEN. Unreachable is not absent. So the error propagates rather
    /// than being folded into "not there", which is #266's class and the defect #421 actually reported.
    ///
    /// WHAT THE GUARD IS *NOT* FOR, because the next reader will assume it and be afraid to touch it:
    /// it is not what stops a duplicate row. `addProjectV2ItemById` is idempotent SERVER-side — calling
    /// it for an issue already on the board returns that item's existing id and adds nothing (measured
    /// against the live board, #861). #421 said a duplicate "would have" been created had its remediation
    /// been followed; that was a reasonable inference, and it does not reproduce.
    ///
    /// The guard earns its place anyway, for reasons that do hold: adding on a failed read would spend a
    /// mutation on a budget that just refused a query, and it would report `AddedToBoard` for an issue
    /// whose presence we never established — a definite answer built on no information, which is the
    /// whole of #421.
    ///
    /// The lookup is deliberately the UNCACHED `itemId`: the forever-cache only ever memoises a FOUND id,
    /// so a cache hit is already an `AlreadyOnBoard` answer and a miss proves nothing.
    ///
    /// COST: 3 GraphQL on a real add (lookup, node id, mutation), 1 when it is already there. Two would
    /// do — `ItemIdDoc` already reads `repository.issue`, so `id` could ride along and retire
    /// `issueNodeId`. It does not, on purpose: `itemId` owns the ONE implementation of "narrow to OUR
    /// board", and an issue can sit on several. A second copy of that predicate to save a point on a
    /// once-per-filing verb trades a cross-board write — a no-op here and vandalism over there — against
    /// a rounding error on a 5,000/hr budget (#418).
    let addItem
        (transport: IGitHubTransport)
        (board: BoardMap)
        (owner: string)
        (repo: string)
        (number: int)
        : IoResult<AddOutcome> =

        // Resolve an external owner through the same owner-qualified positive cache as `item-id` and `addItem`.
        //
        // An external-owner issue can be added to an organization board and yield a stable project-item id,
        // while GitHub's subsequent `repository.issue.projectItems` read omits that cross-owner placement.
        // Re-asking turns a known canonical item into a false `NotOnBoard`; keeping the cache key as
        // owner/repo/number/board makes same-name repositories under different owners distinct (#2143).
        let resolved =
            if String.Equals(owner, board.Owner, StringComparison.OrdinalIgnoreCase) then
                itemId transport board owner repo number
            else
                itemIdCached transport board owner repo number

        match resolved with
        | Error e -> Error e
        | Ok(Some existing) ->
            // Idempotent, and it costs one read and no write. Re-running `add` is how a close-out pass or a
            // retried recipe behaves; it must not create a twin.
            Cache.putItemId owner repo number board.Number existing
            Ok(AlreadyOnBoard existing)

        | Ok None ->

        match issueNodeId transport owner repo number with
        | Error e -> Error e
        | Ok contentId ->

        let subject = $"%s{owner}/%s{repo}#%d{number}"

        let request =
            query AddItemDoc [ "projectId", VId board.Id; "contentId", VId contentId ] subject

        match transport.Send request with
        | Error e -> Error e
        | Ok response ->

        match graphQlData subject response.Body with
        | Error e -> Error e
        | Ok data ->

        try
            let item = data.GetProperty("addProjectV2ItemById").GetProperty("item")

            match item.TryGetProperty "id" with
            | true, id when id.ValueKind = JsonValueKind.String ->
                let newId = id.GetString()
                Cache.putItemId owner repo number board.Number newId
                Ok(AddedToBoard newId)
            | _ -> Error(Malformed(subject, "the add response has no `addProjectV2ItemById.item.id`"))

        with :? KeyNotFoundException ->
            Error(Malformed(subject, "the add response is missing `addProjectV2ItemById.item`"))

    // ---- the pre-claim column (#481) ----------------------------------------------------------------

    [<Literal>]
    let private ExternalItemFieldDoc =
        "query($itemId: ID!, $field: String!) { node(id: $itemId) { ... on ProjectV2Item { fieldValueByName(name: $field) { ... on ProjectV2ItemFieldSingleSelectValue { name } ... on ProjectV2ItemFieldTextValue { text } } } } rateLimit { cost remaining } }"

    /// Read ONE field of an issue owned OUTSIDE the board owner, from the BOARD side of the relationship.
    ///
    /// **THE ISSUE SIDE CANNOT ANSWER THIS QUESTION (#2166, #2204).** `repository.issue.projectItems` omits an
    /// organization project's placement of an issue whose repository has a different owner — measured on
    /// `EHotwagner/rogue3#96`, `EHotwagner/rogue3#75` and `EHotwagner/S.I.R.#138`, each of which returns only
    /// its own user project while the FS-GG Coordination row demonstrably exists. Narrowing that connection to
    /// `board.Number` therefore found nothing and reported `Ok None` — the DEFINITE "no column" — for a row
    /// that carries one. `#2172` gave `itemId` the board-side branch; this is the same branch for the two
    /// readers it did not reach, and both go through this ONE function so a future repair cannot land on
    /// `itemStatus` and miss `itemBlockedBy`.
    ///
    /// **IT IS STILL A RESOLVER READ.** `itemIdCached` resolves the row once and keeps it forever (ids are
    /// stable), so the field itself is one point on `node(id:)` — the #481/#418 thrift that keeps `take` →
    /// `claim` off a full-board scan. Only the FIRST external lookup pages the project, and `claim`'s own
    /// write resolves that id anyway.
    ///
    /// `Ok None` is a MEASURED absence with exactly two shapes: `itemId` walked the project to completion and
    /// this row is not on it, or the row is on it with the field genuinely unset. Every other outcome is
    /// `Error` — `itemId` already fails closed on an incomplete page (#2166), and a board item id that no
    /// longer resolves to a node is unresolvable, not empty.
    let private externalItemField
        (transport: IGitHubTransport)
        (board: BoardMap)
        (owner: string)
        (repo: string)
        (number: int)
        (field: string)
        (read: JsonElement -> 'a option)
        : IoResult<'a option> =

        let subject = $"%s{owner}/%s{repo}#%d{number} %s{field}"

        match itemIdCached transport board owner repo number with
        | Error e -> Error e
        // Not on this board at all — `itemId`'s external branch paged the project to completion to say so,
        // and it is the one absence that may be manufactured into "there is no column here".
        | Ok None -> Ok None
        | Ok(Some id) ->

        let request =
            query ExternalItemFieldDoc [ "itemId", VId id; "field", VString field ] subject

        match transport.Send request with
        | Error e -> Error e
        | Ok response ->

        match graphQlData subject response.Body with
        | Error e -> Error e
        | Ok data ->

        try
            let node = data.GetProperty "node"

            if node.ValueKind = JsonValueKind.Null then
                // A board item id that resolves to nothing is an UNRESOLVABLE read, not an empty field. It
                // may not wear an absence's clothes (#266): the id came from the board, so a null here means
                // the row moved or the read failed — either way we did not measure the column.
                Error(NotFound $"board item %s{id} for %s{subject}")
            else

            match node.TryGetProperty "fieldValueByName" with
            // Null when the row is on the board with the field genuinely unset — the same "nothing recorded"
            // answer the issue-side reader reaches the other way.
            | true, fv when fv.ValueKind = JsonValueKind.Object -> Ok(read fv)
            | true, fv when fv.ValueKind = JsonValueKind.Null -> Ok None
            | _ -> Error(Malformed(subject, "the external-owner field read response has no `node.fieldValueByName`"))

        with :? KeyNotFoundException ->
            Error(Malformed(subject, "the external-owner field read response is missing `data.node`"))

    /// The `Status` single-select value, read off a `fieldValueByName` node.
    let private statusOfFieldValue (fv: JsonElement) : BoardStatus option =
        match fv.TryGetProperty "name" with
        | true, nm when nm.ValueKind = JsonValueKind.String -> Reads.statusOfName (nm.GetString())
        | _ -> None

    /// The `Blocked by` text value, read off a `fieldValueByName` node.
    let private textOfFieldValue (fv: JsonElement) : string option =
        match fv.TryGetProperty "text" with
        | true, tx when tx.ValueKind = JsonValueKind.String -> Some(tx.GetString())
        | _ -> None

    let private stringOfFieldValue (fv: JsonElement) : string option =
        match fv.TryGetProperty "name", fv.TryGetProperty "text" with
        | (true, value), _ when value.ValueKind = JsonValueKind.String -> Some(value.GetString())
        | _, (true, value) when value.ValueKind = JsonValueKind.String -> Some(value.GetString())
        | _ -> None

    /// Freshly resolve any projected single-select/text field from the board item itself. Intake uses
    /// this after its batch so `projectionFresh` covers every field named in the receipt.
    let itemFieldValue transport board owner repo number field =
        externalItemField transport board owner repo number field stringOfFieldValue

    /// Is this issue's owner the board's own owner? Only then can the issue-side connection see the row.
    let private ownedByBoardOwner (board: BoardMap) (owner: string) =
        String.Equals(owner, board.Owner, StringComparison.OrdinalIgnoreCase)

    [<Literal>]
    let private ItemStatusDoc =
        "query($owner: String!, $repo: String!, $number: Int!) { repository(owner: $owner, name: $repo) { issue(number: $number) { projectItems(first: 20) { totalCount nodes { project { number } fieldValueByName(name: \"Status\") { ... on ProjectV2ItemFieldSingleSelectValue { name } } } } } } rateLimit { cost remaining } }"

    /// Read ONE item's `Status` column — the column a `claim` is about to overwrite, so `release` can put it
    /// back rather than guess `Ready` (#481).
    ///
    /// **THIS IS A RESOLVER READ, NOT A SCAN.** `fieldValueByName` returns one value per item with no node
    /// multiplication — the same thrifty selection `Scan` uses — so it costs one point for one item. Reaching
    /// for `Scan.board` here instead would put a seven-point full-board read on the hottest path in the org
    /// (`take` → `claim`, every worker, every round), against the one budget that dies first under fan-out.
    /// That regression is exactly what #481 (with #418) is written not to cause, so it is a resolver read.
    ///
    /// `Ok None` is a real answer with two shapes: the issue is not on THIS board, or it is but its `Status`
    /// is unset. Both mean "there is no column to record", and a claim that records none releases to `Ready`
    /// — the pre-#481 behaviour, now scoped to the one case where there is genuinely nothing to restore. A
    /// FAILED read is `Error`, never `Ok None`: the caller (`claim`) treats it as "recorded no column" too,
    /// but it may not be manufactured into the definite absence that `None` asserts.
    ///
    /// **THIS ARM IS FOR THE BOARD OWNER'S OWN ISSUES ONLY.** For an external owner the connection filters
    /// the row away and this reader would answer a false `Ok None` (#2204) — `itemStatus` routes those to
    /// `externalItemField` instead.
    let private repositoryItemStatus
        (transport: IGitHubTransport)
        (board: BoardMap)
        (owner: string)
        (repo: string)
        (number: int)
        : IoResult<BoardStatus option> =

        let subject = $"%s{owner}/%s{repo}#%d{number} Status"

        let request =
            query
                ItemStatusDoc
                [ "owner", VString owner
                  "repo", VString repo
                  "number", VNumber(double number) ]
                subject

        match transport.Send request with
        | Error e -> Error e
        | Ok response ->

        match graphQlData subject response.Body with
        | Error e -> Error e
        | Ok data ->

        try
            let issue = data.GetProperty("repository").GetProperty("issue")

            if issue.ValueKind = JsonValueKind.Null then
                // The issue itself does not exist — a real, definite answer, and there is no column to record.
                Ok None
            else

            let projectItems = issue.GetProperty "projectItems"

            // NARROW TO OUR BOARD, exactly as `itemId` does. An issue can sit on several boards, and the
            // Status on another one is not the column this claim overwrites here.
            let ourNode =
                projectItems.GetProperty("nodes").EnumerateArray()
                |> Seq.tryFind (fun n ->
                    match n.TryGetProperty "project" with
                    | true, p when p.ValueKind = JsonValueKind.Object ->
                        match p.TryGetProperty "number" with
                        | true, num when num.ValueKind = JsonValueKind.Number -> num.GetInt32() = board.Number
                        | _ -> false
                    | _ -> false)

            match ourNode with
            // NOT ON THIS BOARD. A successful read with a definite answer — nothing to restore — and
            // `.github#2535` is what keeps it one: on a miss, a window that hid the tail is a FAILED read,
            // never "not on this board". `claim` treats both as "recorded no column", but the two are not
            // the same fact and only one of them may be asserted.
            | None ->
                match Reads.connectionComplete subject "this issue's project-item connection" ProjectItemsWindow projectItems with
                | Error e -> Error e
                | Ok() -> Ok None
            | Some node ->
                match node.TryGetProperty "fieldValueByName" with
                // `fieldValueByName` is null when the item is on the board with NO Status set. On board, no
                // column — the same "nothing to restore" answer, reached the other way.
                | true, fv when fv.ValueKind = JsonValueKind.Object ->
                    match fv.TryGetProperty "name" with
                    | true, nm when nm.ValueKind = JsonValueKind.String -> Ok(Reads.statusOfName (nm.GetString()))
                    | _ -> Ok None
                | _ -> Ok None

        with :? KeyNotFoundException ->
            Error(Malformed(subject, "the item-status response is missing `repository.issue.projectItems`"))

    let itemStatus
        (transport: IGitHubTransport)
        (board: BoardMap)
        (owner: string)
        (repo: string)
        (number: int)
        : IoResult<BoardStatus option> =

        if ownedByBoardOwner board owner then
            repositoryItemStatus transport board owner repo number
        else
            externalItemField transport board owner repo number "Status" statusOfFieldValue

    let private ItemBlockedByDoc =
        "query($owner: String!, $repo: String!, $number: Int!) { repository(owner: $owner, name: $repo) { issue(number: $number) { projectItems(first: 20) { totalCount nodes { project { number } fieldValueByName(name: \"Blocked by\") { ... on ProjectV2ItemFieldTextValue { text } } } } } } rateLimit { cost remaining } }"

    /// Read ONE item's live `Blocked by` column. `itemStatus`'s twin over the TEXT fragment — see the
    /// `.fsi` for why this is a resolver read and what `Ok None` covers.
    ///
    /// **BOARD OWNER'S OWN ISSUES ONLY**, for `repositoryItemStatus`'s reason and by the same #2204
    /// measurement; `itemBlockedBy` routes an external owner to `externalItemField`.
    let private repositoryItemBlockedBy
        (transport: IGitHubTransport)
        (board: BoardMap)
        (owner: string)
        (repo: string)
        (number: int)
        : IoResult<string option> =

        let subject = $"%s{owner}/%s{repo}#%d{number} Blocked by"

        let request =
            query
                ItemBlockedByDoc
                [ "owner", VString owner
                  "repo", VString repo
                  "number", VNumber(double number) ]
                subject

        match transport.Send request with
        | Error e -> Error e
        | Ok response ->

        match graphQlData subject response.Body with
        | Error e -> Error e
        | Ok data ->

        try
            let issue = data.GetProperty("repository").GetProperty("issue")

            if issue.ValueKind = JsonValueKind.Null then
                Ok None
            else

            let projectItems = issue.GetProperty "projectItems"

            let ourNode =
                projectItems.GetProperty("nodes").EnumerateArray()
                |> Seq.tryFind (fun n ->
                    match n.TryGetProperty "project" with
                    | true, p when p.ValueKind = JsonValueKind.Object ->
                        match p.TryGetProperty "number" with
                        | true, num when num.ValueKind = JsonValueKind.Number -> num.GetInt32() = board.Number
                        | _ -> false
                    | _ -> false)

            match ourNode with
            // `itemStatus`'s rule, for `itemStatus`'s reason (`.github#2535`): a miss over a window that
            // hid the tail is a failed read, not "not on this board".
            | None ->
                match Reads.connectionComplete subject "this issue's project-item connection" ProjectItemsWindow projectItems with
                | Error e -> Error e
                | Ok() -> Ok None
            | Some node ->
                match node.TryGetProperty "fieldValueByName" with
                // `fieldValueByName` is null when the item is on the board with the field genuinely
                // unset — the same "nothing recorded" answer `itemStatus` reaches the other way.
                | true, fv when fv.ValueKind = JsonValueKind.Object ->
                    match fv.TryGetProperty "text" with
                    | true, tx when tx.ValueKind = JsonValueKind.String -> Ok(Some(tx.GetString()))
                    | _ -> Ok None
                | _ -> Ok None

        with :? KeyNotFoundException ->
            Error(Malformed(subject, "the item-blocked-by response is missing `repository.issue.projectItems`"))

    let itemBlockedBy
        (transport: IGitHubTransport)
        (board: BoardMap)
        (owner: string)
        (repo: string)
        (number: int)
        : IoResult<string option> =

        if ownedByBoardOwner board owner then
            repositoryItemBlockedBy transport board owner repo number
        else
            externalItemField transport board owner repo number "Blocked by" textOfFieldValue

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
                // `VString`, NOT `VId` — an option id LOOKS like an id, and the schema types it `String`.
                // GraphQL checks the DECLARATION against the argument, never against the value, so `VId`
                // here declares `$optionId: ID!` against a `String` argument and every single-select write
                // is refused before it is attempted (#848).
                | Some optionId -> Ok("value: {singleSelectOptionId: $optionId}", [ "optionId", VString optionId ])
                | None ->
                    let known = options |> Map.keys |> String.concat ", "

                    Error $"'%s{value}' is not an option on this field. Known options: %s{known}"

            | Text -> Ok("value: {text: $text}", [ "text", VString value ])

            | Number ->
                match Double.TryParse(value, Globalization.NumberStyles.Float, Globalization.CultureInfo.InvariantCulture) with
                | true, n -> Ok("value: {number: $number}", [ "number", VNumber n ])
                | _ -> Error $"'%s{value}' is not a number, and a NUMBER field cannot hold it."

            // `VDate`, NOT `VString` — the scalar is named `Date`, so `$date: String!` is refused against
            // it exactly as `$optionId: ID!` was against `String`. The board has two DATE fields (`Start`,
            // `Target`), so unlike the iteration leg below this one is reached today (#848).
            | Date -> Ok("value: {date: $date}", [ "date", VDate value ])
            // `iterationId` is `String` in the schema too, for the same reason and with the same fix. No
            // field on the board is an Iteration today, so this path is unreached — and it would have been
            // refused identically the day somebody added one.
            | Iteration -> Ok("value: {iterationId: $iterationId}", [ "iterationId", VString value ])

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
                            | VDate _ -> "Date!"

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
                                // A `Date` literal is a quoted string in GraphQL source, like the two above.
                                // The tag matters for the DECLARATION, which this path does not emit.
                                | VDate s -> gqlStr s
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
        //
        // PROPAGATE THE VALUE, do not rebuild it: re-tupling the fields here would silently drop any the
        // case grows later, and this arm has no opinion about which budget died.
        | Error(RateLimited _ as e) -> Error e

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

        // Keep batch writes on the identical item-resolution path as single writes.  A mixed policy here
        // would make `set-field` work for an external owner while `set-field --batch` falsely refused it.
        let resolved =
            if String.Equals(owner, board.Owner, StringComparison.OrdinalIgnoreCase) then
                itemId transport board owner repo number
            else
                itemIdCached transport board owner repo number

        match resolved with
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
                    owner
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
                      Worker = worker
                      // THE BOARD WE WERE WRITING TO, recorded at QUEUE time — the only moment it is known
                      // (#882). `flush` bootstraps from the environment, which may since have been repointed.
                      Board = Some(board.Owner, board.Title) }

                match Cache.defer e entry with
                | Ok() -> Ok Deferred
                | Error queueError -> Error queueError
            else
                // REPORTED, NEVER SWALLOWED. A refusal nobody can read is a refusal that did not happen.
                Error e

    /// The batch sibling of `boardWrite` (#448): resolve the item, emit N fields in ONE aliased document,
    /// and carry the SAME deferral policy the single write does.
    ///
    /// The policy differs from `boardWrite`'s in exactly one way, and it is forced by the transport: a batch
    /// can fail HALF-WAY. So there are three post-conditions, not two:
    ///   • an exhausted budget refused the WHOLE document — nothing landed — so EVERY pair is queued, the
    ///     batch replays intact, and nothing is lost;
    ///   • a `Partial` means some aliases DID land — it is NEVER queued (replaying rewrites the half that
    ///     took effect), and it surfaces unchanged for the caller to render field-by-field;
    ///   • any other permanent failure is reported, never swallowed.
    let boardWriteBatch
        (transport: IGitHubTransport)
        (board: BoardMap)
        (owner: string)
        (repo: string)
        (number: int)
        (writes: (string * FieldWrite) list)
        (worker: string)
        : IoResult<WriteOutcome> =

        // QUEUE EVERY PAIR. This is reached ONLY on `isQueueable e` (an exhausted budget), where the document
        // was refused outright — nothing landed — so the whole batch is deferrable and the queue replays it
        // pair by pair. `Cache.defer` re-checks the licence per entry; the loop stops on the first queue error.
        let queueAll (e: IoError) : IoResult<WriteOutcome> =
            let rec go remaining =
                match remaining with
                | [] -> Ok Deferred
                | (field, write) :: rest ->
                    let entry: Cache.Deferred =
                        { Ref = $"%s{owner}/%s{repo}#%d{number}"
                          Field = field
                          Value =
                            match write with
                            | Set v -> v
                            | Clear -> ""
                          At = DateTimeOffset.UtcNow.ToString("o")
                          Worker = worker
                          // The same record the single write makes (#882): every pair of this batch was
                          // refused against THIS board, so that is the board that can replay them.
                          Board = Some(board.Owner, board.Title) }

                    match Cache.defer e entry with
                    | Ok() -> go rest
                    | Error queueError -> Error queueError

            go writes

        let resolved =
            if String.Equals(owner, board.Owner, StringComparison.OrdinalIgnoreCase) then
                itemId transport board owner repo number
            else
                itemIdCached transport board owner repo number

        match resolved with
        | Error e -> if isQueueable e then queueAll e else Error e
        | Ok None -> Ok NotOnBoard
        | Ok(Some item) ->
            match setFieldBatch transport board item writes with
            | Ok() ->
                // FOLD OUR OWN WRITES INTO THE CACHED SCAN, one per field — the same reason the single write
                // does it (a claim is always followed by a take, and invalidating would send it to a full scan).
                for field, write in writes do
                    Cache.patchScan
                        board.Owner
                        board.Title
                        owner
                        repo
                        number
                        field
                        (match write with
                         | Set v -> v
                         | Clear -> "")

                Ok Written
            | Error e -> if isQueueable e then queueAll e else Error e

    type FlushOutcome =
        { Queued: int
          Written: int
          Dropped: int
          /// Entries queued against a DIFFERENT board, left in the queue untouched (#882). Distinct from
          /// `Dropped` in the one way that matters: these writes are still owed, and still landable.
          Skipped: int
          Stopped: IoError option }

    let flush (transport: IGitHubTransport) (board: BoardMap) : IoResult<FlushOutcome> =
        match Cache.pending () with
        | Error e -> Error e
        | Ok [] ->
            Ok
                { Queued = 0
                  Written = 0
                  Dropped = 0
                  Skipped = 0
                  Stopped = None }
        | Ok entries ->

        let mutable written = 0
        let mutable dropped = 0
        let mutable stopped = None

        // ANOTHER BOARD'S ENTRY IS NOT OURS TO REPLAY, AND EMPHATICALLY NOT OURS TO DROP (#882).
        //
        // Resolving it against THIS board is what the bug was: `itemId` answers `Ok None` — a successful read
        // that walked the whole board and found nothing — which is `NotOnBoard`, which is PERMANENT, so the
        // entry dropped and the CLI called it "permanently un-writable". Every step is locally correct; the
        // conclusion is false, because the question was asked of the wrong board.
        //
        // SKIP, NOT DROP, is the whole repair: the write is still owed and still landable, so it stays queued
        // where `flush --dry-run` can show it and a flush against its own board can replay it.
        //
        // A LEGACY ENTRY (`Board = None`) IS REPLAYED, NOT SKIPPED. It predates this field, so its board is
        // genuinely unknown — and "unknown" must not become "skip forever", which would strand it exactly as
        // #878 stranded the queue that had no verb. Replaying it against the current board is the behaviour it
        // was queued under: no worse than before, and correct in the single-board case that is every real one.
        let isOtherBoard (entry: Cache.Deferred) =
            match entry.Board with
            | Some queuedAgainst -> not (Cache.sameBoard queuedAgainst (board.Owner, board.Title))
            | None -> false

        // PARTITIONED BEFORE THE PASS, so a skip is decided by the entry alone and cannot be confused with a
        // stop: a rate limit halts the replay of OUR entries, and must not retroactively re-classify another
        // board's — `Skipped` is a property of the queue, `Stopped` a property of the budget.
        let replayable, otherBoards = entries |> List.partition (isOtherBoard >> not)
        let skipped = List.length otherBoards

        for entry in replayable do
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
                | None ->
                    Cache.dropPending entry
                    dropped <- dropped + 1
                | Some(owner, repo, number) ->

                    let write =
                        if entry.Value = "" then Clear else Set entry.Value

                    // REPLAY THROUGH `attempt`, NOT `boardWrite`. `boardWrite` carries the DEFER policy —
                    // it queues on an exhausted budget — and that is exactly right for a FIRST attempt and
                    // exactly wrong for a replay: this entry is ALREADY in the queue, so deferring it again
                    // appends a second copy. Every flush under a dead budget would double the queue,
                    // forever, while reporting that it had written nothing and backing off from nothing.
                    match attempt transport board owner repo number entry.Field write with
                    | Ok Written ->
                        Cache.dropPending entry
                        written <- written + 1

                    | Ok NotOnBoard ->
                        // `NotOnBoard` is DROPPED, not retried. It is permanent, and `boardWrite` would not
                        // have queued it in the first place — but an entry queued before the item was
                        // removed from the board can still reach here, and carrying it forever would mean
                        // the queue never drains.
                        //
                        // DROPPED IS NOT WRITTEN, AND `written` IS THE COUNT THIS FUNCTION RETURNS (#862).
                        // Counting it here would report a write that never happened, to a caller whose whole
                        // job is telling a worker whether their board writes landed — the same
                        // "reported success over a subject it never touched" that #266 names, inside the
                        // one verb that exists to repair exactly that. The entry still drops; it counts
                        // as DROPPED, which is a fact the caller renders separately.
                        Cache.dropPending entry
                        dropped <- dropped + 1

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
                        dropped <- dropped + 1

        // A STOP IS NOT AN ERROR HERE, AND THAT IS THE FIX (#862). Returning `Error e` discarded `written`
        // — the count the caller renders — so the caller re-read the queue to infer it, and a concurrent
        // `defer` into the shared queue file made that inference wrong. The rate limit and the work that
        // landed are DIFFERENT FACTS; a caller needs both, so both are returned. `Error` now means only
        // "the queue could not be read".
        Ok
            { Queued = List.length entries
              Written = written
              Dropped = dropped
              Skipped = skipped
              Stopped = stopped }
