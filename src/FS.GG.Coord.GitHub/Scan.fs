namespace FS.GG.Coord.GitHub

module Scan =

    open System
    open System.Text
    open System.Text.Json
    open System.Text.RegularExpressions
    open FS.GG.Coord
    open FS.GG.Coord.Types
    open Errors
    open Transport

    type Row =
        { Ref: Ref
          Title: string
          Status: BoardStatus
          BlockedByRaw: string
          State: IssueState
          IsPullRequest: bool
          /// The repository whose tree this item's `Paths:` tokens name.  This normally equals
          /// `Ref.Repo`; a cross-repository coordination item may instead select `Repo Scope` (#1732).
          /// It is deliberately separate from `Ref`, which continues to identify the issue to read,
          /// claim, and close.
          PathRepo: string
          /// The `Class` column as OBSERVED (.github#1588). `None` covers three facts the board itself
          /// does not tell apart — the row is unclassed, the value is a word this engine does not speak,
          /// or the project has no `Class` field at all — and all three mean the same thing to the only
          /// consumer: there is no projection here to trust. `lint` reports the gap from the ITEM's text,
          /// which is the authority, so nothing downstream has to guess which of the three this was.
          BoardClass: ItemClass option

          /// The `Severity` column as observed. Missing or unrecognised values are explicitly `Unset`,
          /// which ranks last and remains visible to lint.
          Severity: Severity

          /// The `Phase` column as OBSERVED (.github#1598). `None` covers the same three facts
          /// `BoardClass` does — unset, a word this engine does not speak, or no such field on the
          /// project — and they collapse for the same reason: to the one consumer (`Rank`) all three mean
          /// "no phase evidence", and an item with none sorts last.
          Phase: Phase option

          /// When the ISSUE was created — the board's only usable age timestamp (.github#1598).
          ///
          /// Carried as the INSTANT, not as a day count, precisely because it is cached: a `Row` written
          /// to disk today and read tomorrow must not report yesterday's age. The count is derived where
          /// the clock is read (`Client.enrichBoardFacts`), so the cached fact never goes stale.
          CreatedAt: DateTimeOffset option

          /// .github#2254 REPAIR 1 (`heron-fef6`). The row's own body TEXT — read ONLY for a
          /// closed-and-`Done` candidate whose `BoardClass` was EMPTY at the moment of a `scanFresh` call
          /// made with `Cache.Reconciling` (see `scanFresh`) — never for `Scheduling`/`Offering`, and
          /// never merely to double-check a column that already carries a value.
          ///
          /// `None` is "not applicable, or this scan's intent never asked" — the overwhelming majority of
          /// rows, on every scan. `Some(Ok text)` is the body; `Some(Error e)` mirrors `Scan.snapshot`'s
          /// own `bodyUnreadable` naming, so a failed census read is COUNTED there, never silently dropped
          /// (#266) — `snapshot`'s swept branch reads THIS rather than calling `Reads.issueBody` itself,
          /// which is what keeps the extra read off every caller but `reconcile`: `Client.fs`'s
          /// `scanAndDecide` already forwards its own `Cache.ReadIntent` into `Scan.board` UNCHANGED
          /// (`Scan.board ctx.Transport intent ...`), so gating the read HERE, inside `scanFresh`, needs no
          /// new parameter on `snapshot` and no edit to `Client.fs` at all — the two calls already agree
          /// on intent, they simply never shared this one narrow fact before.
          ///
          /// DELIBERATELY UNCACHED. `renderRows`/`parseRows` never round-trip it: `Cache.getScan` already
          /// refuses to serve a cache hit for `Reconciling`/`Offering` (`Cache.fs`'s own `| Reconciling |
          /// Offering -> None`), so every `Reconciling` scan reaches `scanFresh` fresh regardless — nothing
          /// is lost by leaving this out of the cache file, and leaving it OUT is what stops a `Scheduling`
          /// read that happens to share a cache file from ever being able to inherit a census read it never
          /// asked for and never paid for.
          SweptBody: IoResult<string> option }

    [<Literal>]
    let OffBoardCap = 60

    type Scoped =
        { Rows: Row list
          Advisory: string option }

    // Repo Scope is a board vocabulary (`audio`); command scope is canonical (`FS.GG.Audio`).
    // Keep the normalization at the board boundary, before filtering, so a row cannot disappear before
    // the Client has an opportunity to enrich its path scope (#1732).
    // THE `--repo` FILTER, ONCE. Hand-rolled per verb it was a silent fail-open five times over (#979);
    // `scripts/check-repo-filter-monopoly.py` is what keeps it one. See `Scan.fsi` for the full argument.
    let scope (repo: string option) (rows: Row list) : Scoped =
        match repo with
        | None -> { Rows = rows; Advisory = None }
        | Some name ->
            let kept =
                rows
                |> List.filter (fun r -> String.Equals(RepoScope.resolve r.PathRepo, name, StringComparison.OrdinalIgnoreCase))

            let advisory =
                // A row matched, so the request named something real: nothing to say.
                if not (List.isEmpty kept) then
                    None
                // NO ROWS AT ALL — so there is no known-repo set to compare against, and "no row names
                // `X`" would be a confident claim about a board nobody could see (#266, inside the fix
                // for #266). A failed scan is never an empty one (`Cache.putScan`, #344), so this is a
                // genuinely empty board: the emptiness is the BOARD's, not the scope's, and the caller's
                // empty output already says it.
                elif List.isEmpty rows then
                    None
                else
                    let known =
                        rows
                        |> List.map (fun r -> RepoScope.resolve r.PathRepo)
                        |> List.distinctBy (fun r -> r.ToLowerInvariant())
                        |> List.sort

                    let knownList = String.Join(", ", known)

                    // BOTH readings, because from here they are the same fact: the board carries no row
                    // for this name. Naming only the typo would be a verdict the rows cannot support.
                    Some(
                        $"fsgg-coord-engine: WARNING — no board row names repo `%s{name}`.\n"
                        + $"  The board knows: %s{knownList}\n"
                        + "  Check the spelling, or this repo has no items on the board yet."
                    )

            { Rows = kept; Advisory = advisory }

    type Receipt =
        { Candidates: int
          RepoAdvisory: string option
          OffBoardResolved: int
          OffBoardSkipped: int
          BodiesUnreadable: int }

    // ---- the thrifty board query --------------------------------------------------------------------

    /// THE COST LEVER, AND IT IS THE WHOLE QUERY.
    ///
    /// `fieldValueByName` is a RESOLVER field: one value per item, no node multiplication. The alternative
    /// — what `gh project item-list` does — nests `fieldValues(first: 100)` inside `items(first: N)`, which
    /// is O(items × 100) NODES, and GraphQL's primary limit is metered by nodes REQUESTED.
    ///
    /// Measured on the live 640-item board: this document is 7 pages × 1 point = **7 points**.
    /// `gh project item-list` costs **6 points to read five items**.
    ///
    /// `class` and `phase` are both `fieldValueByName` RESOLVER fields and `createdAt` is a SCALAR on a
    /// node already selected, so none of the three multiplies nodes and the 7 points above is unchanged
    /// by .github#1588 or .github#1598. A `fieldValues(first: N)` connection would not have been free,
    /// which is the whole reason this query does not use one.
    [<Literal>]
    let private BoardDoc =
        "query($owner: String!, $number: Int!, $cursor: String) { \
         organization(login: $owner) { \
           projectV2(number: $number) { \
             items(first: 100, after: $cursor) { \
               pageInfo { hasNextPage endCursor } \
               nodes { \
                 status: fieldValueByName(name: \"Status\") { ... on ProjectV2ItemFieldSingleSelectValue { name } } \
                 blockedBy: fieldValueByName(name: \"Blocked by\") { ... on ProjectV2ItemFieldTextValue { text } } \
                 class: fieldValueByName(name: \"Class\") { ... on ProjectV2ItemFieldSingleSelectValue { name } } \
                 severity: fieldValueByName(name: \"Severity\") { ... on ProjectV2ItemFieldSingleSelectValue { name } } \
                 phase: fieldValueByName(name: \"Phase\") { ... on ProjectV2ItemFieldSingleSelectValue { name } } \
                 repoScope: fieldValueByName(name: \"Repo Scope\") { ... on ProjectV2ItemFieldSingleSelectValue { name } } \
                 content { \
                   __typename \
                   ... on Issue { number title state createdAt repository { nameWithOwner } } \
                   ... on PullRequest { number title state createdAt repository { nameWithOwner } } \
                 } \
               } \
             } \
           } \
         } rateLimit { cost remaining } }"

    // Reconciliation alone needs a closed-and-Done item's declaration to project its `Class` column.
    // Ask for that scalar in the same board page instead of turning the swept census into one REST issue
    // read per row. Scheduling and offering deliberately retain `BoardDoc`: an issue body is neither an
    // input to their board-row scan nor a free thing to carry around.
    [<Literal>]
    let private ReconcilingBoardDoc =
        "query($owner: String!, $number: Int!, $cursor: String) { \
         organization(login: $owner) { \
           projectV2(number: $number) { \
             items(first: 100, after: $cursor) { \
               pageInfo { hasNextPage endCursor } \
               nodes { \
                 status: fieldValueByName(name: \"Status\") { ... on ProjectV2ItemFieldSingleSelectValue { name } } \
                 blockedBy: fieldValueByName(name: \"Blocked by\") { ... on ProjectV2ItemFieldTextValue { text } } \
                 class: fieldValueByName(name: \"Class\") { ... on ProjectV2ItemFieldSingleSelectValue { name } } \
                 severity: fieldValueByName(name: \"Severity\") { ... on ProjectV2ItemFieldSingleSelectValue { name } } \
                 phase: fieldValueByName(name: \"Phase\") { ... on ProjectV2ItemFieldSingleSelectValue { name } } \
                 repoScope: fieldValueByName(name: \"Repo Scope\") { ... on ProjectV2ItemFieldSingleSelectValue { name } } \
                 content { \
                   __typename \
                   ... on Issue { number title state createdAt body repository { nameWithOwner } } \
                   ... on PullRequest { number title state createdAt body repository { nameWithOwner } } \
                 } \
               } \
             } \
           } \
         } rateLimit { cost remaining } }"

    let private boardStatusOf (s: string) =
        match s.Trim().ToLowerInvariant() with
        | "" -> NoStatus
        | "backlog" -> Backlog
        | "ready" -> Ready
        | "in progress" -> InProgress
        | "blocked" -> Blocked
        | "in review" -> InReview
        | "done" -> Done
        | _ -> NoStatus

    let private str (e: JsonElement) (name: string) =
        match e.TryGetProperty name with
        | true, v when v.ValueKind = JsonValueKind.String -> Some(v.GetString())
        | _ -> None

    let private nested (e: JsonElement) (name: string) (inner: string) =
        match e.TryGetProperty name with
        | true, o when o.ValueKind = JsonValueKind.Object -> str o inner
        | _ -> None

    /// An ISO-8601 instant, or `None`.
    ///
    /// `RoundtripKind` so a `Z` suffix stays UTC instead of being reinterpreted as local time — an age in
    /// DAYS would survive that, but a rank input that silently shifts by a timezone on one machine and
    /// not another is exactly the kind of non-determinism the batch may not have. `None` on anything
    /// unparseable: an age we could not read is unknown, never zero (`Item.AgeDays`).
    let private instant (s: string option) : DateTimeOffset option =
        match s with
        | None -> None
        | Some raw ->
            match
                DateTimeOffset.TryParse(
                    raw,
                    Globalization.CultureInfo.InvariantCulture,
                    Globalization.DateTimeStyles.RoundtripKind
                )
            with
            | true, v -> Some v
            | _ -> None

    let private parseRow (carrySweptBody: bool) (node: JsonElement) : Row option =
        match node.TryGetProperty "content" with
        | true, content when content.ValueKind = JsonValueKind.Object ->
            let number =
                match content.TryGetProperty "number" with
                | true, n when n.ValueKind = JsonValueKind.Number -> Some(n.GetInt32())
                | _ -> None

            let nwo =
                match content.TryGetProperty "repository" with
                | true, r when r.ValueKind = JsonValueKind.Object -> str r "nameWithOwner"
                | _ -> None

            match number, nwo with
            | Some n, Some nwo when nwo.Contains "/" ->
                let parts = nwo.Split('/')

                let isPr =
                    match str content "__typename" with
                    | Some "PullRequest" -> true
                    | _ -> false

                let state =
                    match str content "state" with
                    // A PR's state is OPEN | CLOSED | MERGED. `IssueState` has two cases, and MERGED is a
                    // CLOSED thing — a merged PR is not open. The MERGED/CLOSED distinction matters for a
                    // BLOCKER (#476), and that is resolved elsewhere, against the ref, not here.
                    | Some s when s.ToUpperInvariant() <> "OPEN" -> Closed
                    | _ -> Open

                let row =
                    { Ref =
                        { Owner = parts.[0]
                          Repo = parts.[1]
                          Number = n }
                      Title = str content "title" |> Option.defaultValue ""
                      Status = nested node "status" "name" |> Option.map boardStatusOf |> Option.defaultValue NoStatus
                      BlockedByRaw = nested node "blockedBy" "text" |> Option.defaultValue ""
                      State = state
                      IsPullRequest = isPr
                      // A missing field preserves the historic meaning: paths belong to the issue's
                      // repository.  The client resolves a present roster short-id before it becomes a
                      // scheduling scope; retaining the raw field here keeps this GraphQL reader free of
                      // the CLI's resolver table.
                      PathRepo = nested node "repoScope" "name" |> Option.defaultValue parts.[1]
                      // COSTS NOTHING TO ADD. `fieldValueByName` is a RESOLVER field — one value per
                      // item, no node multiplication — so this is the same 7 points over the live board
                      // that the query's own comment measures. `Option.bind` on the resolved name, so a
                      // project with no `Class` field (every board before .github#1588, and every parity
                      // fixture) reads `None` rather than failing the scan.
                      BoardClass = nested node "class" "name" |> Option.bind itemClassOfWireName
                      Severity =
                        nested node "severity" "name"
                        |> Option.bind severityOfWireName
                        |> Option.defaultValue Unset
                      // Same shape, same cost, same fail-soft as `class` above: a project with no `Phase`
                      // field (every parity fixture, and any board but the live one) reads `None` rather
                      // than failing the scan.
                      Phase = nested node "phase" "name" |> Option.bind phaseOfWireName
                      CreatedAt = str content "createdAt" |> instant
                      // Filled only by the reconciling variant of the board document below.
                      SweptBody = None }

                if carrySweptBody && row.State = Closed && row.Status = Done && row.BoardClass.IsNone then
                    // Match `Reads.issueBody`: null is a successfully observed empty description. A
                    // missing or malformed scalar is different: it is an unreadable declaration and must
                    // reach the snapshot as such rather than suppressing the class projection.
                    let swept =
                        match content.TryGetProperty "body" with
                        | true, body when body.ValueKind = JsonValueKind.String -> Ok(body.GetString())
                        | true, body when body.ValueKind = JsonValueKind.Null -> Ok ""
                        | _ -> Error(Malformed(row.Ref.Short, "the reconciling board response has no readable issue body"))

                    Some { row with SweptBody = Some swept }
                else
                    Some row

            | _ -> None

        // A DRAFT ITEM — a board card with no issue behind it. It is not work anybody can claim, and it has
        // no ref, so it cannot be reserved, blocked, or done. Skipping it is correct; inventing a ref for it
        // would put a phantom on the queue.
        | _ -> None

    /// The scan, as the JSON we cache. It is the ROWS, not the raw GraphQL — so a cache hit does not have to
    /// re-walk a document, and the shape on disk is the shape the reader wants.
    let private renderRows (rows: Row list) =
        let sb = StringBuilder()
        use stream = new IO.MemoryStream()
        use w = new Utf8JsonWriter(stream)

        w.WriteStartArray()

        for r in rows do
            w.WriteStartObject()
            w.WriteString("owner", r.Ref.Owner)
            w.WriteString("repo", r.Ref.Repo)
            w.WriteNumber("number", r.Ref.Number)
            w.WriteString("title", r.Title)

            // The FIFTH copy, and the one #983's measurement could not see: it had no name, so a grep for
            // `let private statusName` — which is how the other four were found and counted — walked
            // straight past it. This is the scan's own JSON `status` field, read by every `jq` in the
            // recipe and by the parity corpus, so it is the wire by any definition.
            w.WriteString("status", statusWireName r.Status)

            w.WriteString("blockedBy", r.BlockedByRaw)

            // OMITTED when unclassed, rather than written as "". An empty string would round-trip through
            // `itemClassOfWireName` to `None` and so happen to be correct — but it would also be a cache
            // entry ASSERTING a value for a column nobody read, and `statusWireName`'s empty case is
            // exactly that assertion made deliberately for a column that really is unset. Here it is not:
            // `None` covers "no such field on this project", which is not a value at all.
            match r.BoardClass with
            | Some c -> w.WriteString("class", itemClassWireName c)
            | None -> ()

            w.WriteString("severity", severityWireName r.Severity)

            // OMITTED when absent, on `class`'s terms and for its reason: an empty string would be a
            // cache entry asserting a value for a column nobody read.
            match r.Phase with
            | Some p -> w.WriteString("phase", phaseWireName p)
            | None -> ()

            // THE INSTANT, ROUND-TRIPPED — never a precomputed age. `"o"` is the round-trip format, so
            // this is the one field on the entry whose meaning does not decay while the entry sits on
            // disk; a cached `ageDays` would be wrong by exactly the cache's own lifetime.
            match r.CreatedAt with
            | Some t -> w.WriteString("createdAt", t.ToString("o", Globalization.CultureInfo.InvariantCulture))
            | None -> ()

            w.WriteString(
                "state",
                match r.State with
                | Open -> "OPEN"
                | Closed -> "CLOSED"
            )

            w.WriteBoolean("isPullRequest", r.IsPullRequest)
            w.WriteString("pathRepo", r.PathRepo)
            w.WriteEndObject()

        w.WriteEndArray()
        w.Flush()
        ignore sb
        Encoding.UTF8.GetString(stream.ToArray())

    let private parseRows (json: string) : Row list option =
        try
            use doc = JsonDocument.Parse json

            if doc.RootElement.ValueKind <> JsonValueKind.Array then
                None
            else
                doc.RootElement.EnumerateArray()
                |> Seq.choose (fun e ->
                    let s (n: string) = str e n

                    let num =
                        match e.TryGetProperty "number" with
                        | true, v when v.ValueKind = JsonValueKind.Number -> Some(v.GetInt32())
                        | _ -> None

                    match s "owner", s "repo", num with
                    | Some o, Some rp, Some n ->
                        Some
                            { Ref = { Owner = o; Repo = rp; Number = n }
                              Title = s "title" |> Option.defaultValue ""
                              Status = s "status" |> Option.map boardStatusOf |> Option.defaultValue NoStatus
                              BlockedByRaw = s "blockedBy" |> Option.defaultValue ""
                              State =
                                match s "state" with
                                | Some "CLOSED" -> Closed
                                | _ -> Open
                              IsPullRequest =
                                match e.TryGetProperty "isPullRequest" with
                                | true, v -> v.ValueKind = JsonValueKind.True
                                | _ -> false
                              // Older cache rows predate #1732's independent path scope.  Their only
                              // truthful interpretation is the historic one: the issue repository.
                              PathRepo = s "pathRepo" |> Option.defaultValue rp
                              // Absent on every cache entry written before .github#1588, and that reads
                              // as `None` — the fail-closed direction. A stale entry then derives a
                              // projection chore that rewrites the column it already holds, which costs
                              // one idempotent board write; the opposite default would suppress a real
                              // projection because an old cache said nothing.
                              BoardClass = s "class" |> Option.bind itemClassOfWireName
                              Severity =
                                s "severity"
                                |> Option.bind severityOfWireName
                                |> Option.defaultValue Unset
                              // Absent on every cache entry written before .github#1598, and that reads
                              // as `None` — which ranks the row LAST rather than promoting it. A stale
                              // cache therefore under-prioritises for at most one cache lifetime; the
                              // opposite default would let an unread entry outrank the whole board.
                              Phase = s "phase" |> Option.bind phaseOfWireName
                              CreatedAt = s "createdAt" |> instant
                              // NEVER ROUND-TRIPPED (.github#2254 repair 1) — `renderRows` above never
                              // writes it, and `Cache.getScan` already refuses to SERVE a hit for
                              // `Reconciling`/`Offering`, so every scan that could use it reaches
                              // `scanFresh` fresh regardless. Carrying a cached census read forward here
                              // would be the one way a `Scheduling` read could inherit a fact it never
                              // paid for.
                              SweptBody = None }
                    | _ -> None)
                |> List.ofSeq
                |> Some

        with :? JsonException ->
            None

    let private scanFresh
        (transport: IGitHubTransport)
        (intent: Cache.ReadIntent)
        (owner: string)
        (title: string)
        (projectNumber: int)
        : IoResult<Row list> =

        let subject = $"the board '%s{title}' in %s{owner}"

        // OWNER-KIND AWARE (#1344, #1349). Resolved once for the whole paginated scan: an org-owned board
        // answers to `organization(login:)`, a user-owned one to `user(login:)`, and a token's OWN board to
        // `viewer` (no login). `Org` is the default and keeps both the document and the parse path below
        // byte-identical to what preceded this. `ownerVars` binds `$owner` for Org/User and NOTHING for
        // Viewer, in lockstep with the document `forOwner` produces.
        let kind = OwnerKind.fromEnv ()
        let ownerField = OwnerKind.ownerField kind
        let carrySweptBody = intent = Cache.Reconciling
        let boardDocument = if carrySweptBody then ReconcilingBoardDoc else BoardDoc
        let boardDoc = OwnerKind.forOwner kind boardDocument
        let ownerVars = OwnerKind.ownerVars kind owner

        let rec page (cursor: string option) (acc: Row list) (guard: int) : IoResult<Row list> =
            if guard <= 0 then
                Error(Malformed(subject, "the board scan did not terminate within 100 pages — refusing to spin"))
            else

            let variables =
                ownerVars
                @ [ "number", VNumber(double projectNumber) ]
                @ (match cursor with
                   | Some c -> [ "cursor", VString c ]
                   | None -> [])

            let request =
                { Method = "POST"
                  Path = "graphql"
                  Query = []
                  Body = Query(boardDoc, variables)
                  Budget = GraphQl
                  IfNoneMatch = None
                  Subject = subject }

            match transport.Send request with
            | Error e -> Error e
            | Ok response ->

            try
                use doc = JsonDocument.Parse response.Body

                match doc.RootElement.TryGetProperty "errors" with
                | true, errs when errs.ValueKind = JsonValueKind.Array && errs.GetArrayLength() > 0 ->
                    let messages =
                        errs.EnumerateArray()
                        |> Seq.map (fun e -> str e "message" |> Option.defaultValue "(no message)")
                        |> List.ofSeq

                    // Tells a SECONDARY limit from the primary budget (#1666). This site used to call
                    // `isRateLimited`, which matches both wordings, and named the GraphQL budget for either.
                    match Budget.ofGraphQlErrors messages with
                    | Some limited -> Error limited
                    | None -> Error(GraphQlErrors messages)

                | _ ->

                let items =
                    doc.RootElement
                        .GetProperty("data")
                        .GetProperty(ownerField)
                        .GetProperty("projectV2")
                        .GetProperty("items")

                let rows =
                    items.GetProperty("nodes").EnumerateArray()
                    |> Seq.choose (parseRow carrySweptBody)
                    |> List.ofSeq

                let pageInfo = items.GetProperty("pageInfo")

                let hasNext =
                    match pageInfo.TryGetProperty "hasNextPage" with
                    | true, v -> v.ValueKind = JsonValueKind.True
                    | _ -> false

                let next = str pageInfo "endCursor"

                match hasNext, next with
                | true, Some c -> page (Some c) (acc @ rows) (guard - 1)
                | _ -> Ok(acc @ rows)

            with
            | :? JsonException as e -> Error(Malformed(subject, $"the board scan's response is not JSON: %s{e.Message}"))
            | :? Collections.Generic.KeyNotFoundException ->
                Error(Malformed(subject, $"the board scan's response is missing `data.%s{ownerField}.projectV2.items`"))

        match page None [] 100 with
        | Error e -> Error e
        | Ok rows ->
            // A server that accepted the reconciling document but omitted its selected `body` scalar is
            // malformed. Keep the old REST read as a narrow compatibility/fail-closed fallback for that
            // impossible-to-trust response shape; ordinary rows (including null bodies) never take it.
            // If the fallback fails, its error remains `SweptBody` and the snapshot refuses to pretend the
            // declaration was empty.
            let rows =
                if carrySweptBody then
                    rows
                    |> List.map (fun row ->
                        match row.SweptBody with
                        | Some(Error _) ->
                            { row with SweptBody = Some(Reads.issueBody transport row.Ref.Owner row.Ref.Repo row.Ref.Number) }
                        | _ -> row)
                else
                    rows

            // A FAILED SCAN IS NEVER CACHED (#344), and `putScan` is what enforces it — an empty document is
            // refused there, at the write, because this is the last moment "the board is empty" and "I could
            // not read the board" are still distinguishable. `renderRows` never serialises `SweptBody`
            // (see its own field doc), so this cache entry carries none of the read above regardless of
            // which branch produced `rows`.
            Cache.putScan owner title (renderRows rows) |> ignore
            Ok rows

    let board
        (transport: IGitHubTransport)
        (cache: Cache.ReadIntent)
        (owner: string)
        (title: string)
        (projectNumber: int)
        : IoResult<Row list> =

        // A CACHE HIT IS NOT A READ. It returns before a single GraphQL point is spent — and it is served
        // only when the INTENT permits it, which a reconciler's never does.
        match Cache.getScan cache owner title with
        | Some hit ->
            match parseRows hit with
            | Some rows -> Ok rows
            // A CACHE WE CANNOT PARSE IS A MISS, NEVER A FAILURE. Falling through to a real scan is always
            // safe; failing the caller because our own optimisation rotted would make the cache strictly
            // worse than not having one.
            | None -> scanFresh transport cache owner title projectNumber

        | None -> scanFresh transport cache owner title projectNumber

    // ---- blockers ------------------------------------------------------------------------------------

    let private refRe =
        Regex(@"^(?:(?<owner>[\w.-]+)/)?(?<repo>[\w.-]+)?#(?<num>\d+)$", RegexOptions.Compiled)

    let private urlRe =
        Regex(@"github\.com/(?<owner>[\w.-]+)/(?<repo>[\w.-]+)/issues/(?<num>\d+)", RegexOptions.Compiled)

    /// Parse one `Blocked by` token into a ref — or say that it is not one.
    ///
    /// `None` is `BlockerUnparseable`, and it BLOCKS. Prose in a dependency field is not a cleared
    /// dependency: *"Blocked by RESOLVED: shipped last week"* has no owner, no repo and no number, and the
    /// bash client used to drop such blockers entirely — so an item it called BLOCKED arrived at the engine
    /// UNBLOCKED, and the engine's answer is the one that reaches a worker: blocked work, handed out.
    let private parseBlockerRef (defaultOwner: string) (defaultRepo: string) (token: string) : Ref option =
        let t = token.Trim()

        let m = urlRe.Match t

        if m.Success then
            Some
                { Owner = m.Groups.["owner"].Value
                  Repo = m.Groups.["repo"].Value
                  Number = int m.Groups.["num"].Value }
        else
            let m = refRe.Match t

            if not m.Success then
                None
            else
                let repo =
                    if m.Groups.["repo"].Success && m.Groups.["repo"].Value <> "" then
                        m.Groups.["repo"].Value
                    else
                        defaultRepo

                let owner =
                    if m.Groups.["owner"].Success && m.Groups.["owner"].Value <> "" then
                        m.Groups.["owner"].Value
                    else
                        defaultOwner

                Some
                    { Owner = owner
                      Repo = repo
                      Number = int m.Groups.["num"].Value }

    /// The board's `Blocked by` graph, for `Blockers.cycles` — resolved from the SCANNED ROWS ALONE, with
    /// NO transport read (#1090).
    ///
    /// A ring can only run through ON-BOARD items, and a board item's OPEN/CLOSED state is already in
    /// `rows` — so an on-board blocker's resolution is FREE, the same free case `resolveBlocker` takes when
    /// the scan already saw the target. An OFF-BOARD blocker draws no ring edge whatever its state, because
    /// `Blockers.cycles` keeps only edges whose target is a node in the graph. So this does not resolve one
    /// — it marks it `BlockerUnknown` and spends no read. A lint that pays the REST lock budget (#418) to
    /// distinguish a MERGED off-board blocker from a CLOSED one, for a blocker no ring can pass through,
    /// would be resolving a fact its one consumer discards.
    ///
    /// **THE OFF-BOARD STATE IS A PLACEHOLDER, NOT A VERDICT** — this graph's resolution is accurate ONLY
    /// for on-board refs, which is precisely what `Blockers.cycles` reads. It is not `snapshot`'s
    /// fully-resolved blocker set, and no consumer that cares about an off-board blocker's real state may
    /// use it. TOTAL and PURE: it reads nothing and terminates on any board.
    let blockerGraph (rows: Row list) : (Ref * Blocker list) list =
        let onBoard =
            rows
            |> List.map (fun r -> (r.Ref.Owner, r.Ref.Repo, r.Ref.Number), r.State)
            |> Map.ofList

        rows
        |> List.map (fun row ->
            let blockers =
                if String.IsNullOrWhiteSpace row.BlockedByRaw then
                    []
                else
                    row.BlockedByRaw.Split(',')
                    |> Array.toList
                    |> List.map (fun t -> t.Trim())
                    |> List.filter (fun t -> t <> "")
                    |> List.map (fun token ->
                        match parseBlockerRef row.Ref.Owner row.Ref.Repo token with
                        // Prose in a dependency field is not a ref: it draws no edge (no `Ref`), and it
                        // BLOCKS every other reader — but a ring cannot run through a node it cannot name.
                        | None ->
                            { Ref = None
                              Raw = token
                              State = BlockerUnparseable }
                        | Some r ->
                            match Map.tryFind (r.Owner, r.Repo, r.Number) onBoard with
                            // FREE — the scan saw the target. OPEN blocks (a live ring edge); CLOSED is
                            // resolved and `Blockers.cycles` drops it, so a closed blocker breaks a ring.
                            | Some Open ->
                                { Ref = Some r
                                  Raw = r.Short
                                  State = BlockerOpen }
                            | Some Closed ->
                                { Ref = Some r
                                  Raw = r.Short
                                  State = BlockerClosed }
                            // OFF THE BOARD — not a node, so no ring edge whatever its state. Placeholder
                            // only; see the note above.
                            | None ->
                                { Ref = Some r
                                  Raw = r.Short
                                  State = BlockerUnknown })

            row.Ref, blockers)

    // ---- the snapshot --------------------------------------------------------------------------------

    // ONE owner, in `Core`, beside `statusWireName` — this was a private copy of the vocabulary, and
    // `Snapshot` held the other half facing the other way (#1012).
    let private blockerStateName = Types.blockerStateWireName

    /// WHO A RESERVATION IS HELD BY, as the assembler knows it. A marker-backed claim (live OR stale — a
    /// lock is a lock, #461) names its worker and item; a MARKERLESS In-progress board row (arm A of
    /// bash's `active_claims`) reserves too — something is evidently editing those files — but has no
    /// worker to name and no lease to wait out, so it is `Unowned`. It is written to the wire as the
    /// codec's `live-claim` / `unowned` holder, which `Snapshot.parse` already reads.
    type private Reserved =
        /// `livePr` carries the #581 proof of life onto the RESERVATION, so the cache does not
        /// reconstruct a liveness-less claim and re-open the bug one layer down (#712). `Some pr` when
        /// the lease has LAPSED but an open `item/<n>-*` PR keeps the claim alive (NOT reapable — talk to
        /// the worker, there is no lease window to wait out); `None` is an ordinary within-lease claim, a
        /// lapsed claim with no PR, OR a liveness that could not be read. It must never distinguish that
        /// last case — `None` always means "no proof of life", which is what lets `Batch` derive
        /// `KnownLiveWork` from it rather than hardcode it.
        | RClaim of worker: WorkerId * holder: Ref * ageSeconds: int * livePr: int option
        | RUnowned of holder: Ref

    /// The surface a reservation holds, on its way to the wire. `RvNames` is the ordinary case — the
    /// path tokens lifted off the body we read. `RvUnreadable` is #1150: a live-held item whose BODY
    /// READ FAILED reserves an UNKNOWN surface, not an empty one. We cannot prove any candidate disjoint
    /// from a touch-set we never saw, so we carry `Unreadable` (with the read's reason) to the wire; the
    /// codec reads it back into `TouchSet.Unreadable`, which `Batch.schedule` reds the batch on. Dropping
    /// it — the pre-#1150 `| _ -> ()` on a failed body — was the fail-open: the claim reserved nothing and
    /// a candidate overlapping its real files was handed the tree its holder is standing in.
    type private ReservedPaths =
        | RvNames of string list
        | RvUnreadable of reason: string

    /// Assemble the snapshot `decide` consumes: `fsgg.coord.snapshot/1`. Every caller's cost is IDENTICAL
    /// regardless of intent (.github#2254 repair 1, `heron-fef6`): the one extra body read a closed-and-
    /// `Done` row with an empty `Class` column needs for `reconcile`'s census is paid, if at all, inside
    /// `scanFresh` — gated on `Cache.Reconciling`, which `board` already receives from an UNCHANGED
    /// `Client.fs` call site — and simply carried here on `Row.SweptBody`. This function reads that field;
    /// it never calls `Reads.issueBody` itself and never sees `Cache.ReadIntent` at all, which is what
    /// keeps `next`/`batch`/`take`/`scan` byte-identical to their pre-#2254 cost with no signature change
    /// of their own to make.
    let snapshot
        (transport: IGitHubTransport)
        (rows: Row list)
        (repo: string option)
        (allowBacklog: bool)
        (limit: int option)
        (leaseMinutes: int)
        : IoResult<string * Receipt> =

        // THE CANDIDATES. Pull requests are excluded (#641 — a PR is an issue in REST and it is not work).
        // CLOSED issues are NOT excluded here, and that is deliberate: #520 handed a closed issue to a
        // worker because candidate selection read the board COLUMN and nothing else, so the fix is to make
        // `decide` answer "is the issue closed?" FIRST (Schedulability check #1) — which it can only do if
        // the closed item reaches it. Excluding it here would get the right answer (never scheduled) with no
        // WORDS: the worker asking "why isn't #502 offered?" would get nothing for it, when bash names it
        // "the issue is closed". A closed candidate is SWEPT below with no body/marker/blocker read, exactly
        // as bash sweeps it — so the reason survives at zero extra cost.
        //
        // PRs are dropped BEFORE the scope, so the known-repo set `scope` reports names the repos with
        // items of WORK on the board — a repo carrying only PRs is not one a worker can be handed an
        // item in, and offering it as a spelling suggestion would be a lie in the shape of help.
        let scoped =
            rows |> List.filter (fun r -> not r.IsPullRequest) |> scope repo

        let candidates = scoped.Rows

        // THE BOARD IS ITS OWN BLOCKER INDEX, AND IT IS FREE. The scan already carries every board item's
        // state, so a `Blocked by` edge pointing at another board item costs ZERO additional reads.
        let onBoard =
            rows
            |> List.map (fun r -> (r.Ref.Owner, r.Ref.Repo, r.Ref.Number), r)
            |> Map.ofList

        let mutable offBoardResolved = 0
        let mutable offBoardSkipped = 0
        let mutable bodiesUnreadable = 0

        /// Resolve one `Blocked by` token.
        let resolveBlocker (owner: string) (repoName: string) (token: string) : IoResult<Blocker> =
            match parseBlockerRef owner repoName token with
            | None ->
                // PROSE IN A DEPENDENCY FIELD BLOCKS. It is not a ref, we cannot look it up, and "I could
                // not read this" is emphatically not "nothing is blocking".
                Ok
                    { Ref = None
                      Raw = token.Trim()
                      State = BlockerUnparseable }

            | Some r ->
                match Map.tryFind (r.Owner, r.Repo, r.Number) onBoard with
                | Some row ->
                    // FREE. The scan saw it.
                    //
                    // NOTE this yields OPEN or CLOSED, never MERGED — the board's `content.state` for a PR
                    // is already collapsed. A blocker that is a MERGED PR reads as CLOSED here, which is the
                    // SAME verdict (`Blockers.isResolved` clears on both), so #476's bug does not return: it
                    // was clearing on CLOSED *only* and treating MERGED as still-blocking.
                    Ok
                        { Ref = Some r
                          Raw = r.Short
                          State =
                            match row.State with
                            | Open -> BlockerOpen
                            | Closed -> BlockerClosed }

                | None ->
                    // OFF THE BOARD. One REST read — a PR is an issue in REST, so this answers both kinds
                    // and distinguishes MERGED from CLOSED (#476).
                    if offBoardResolved >= OffBoardCap then
                        // THE CAP IS ANNOUNCED, NEVER SILENT. The overflow stays `BlockerUnknown`, which
                        // BLOCKS — the safe direction — and it is COUNTED, so the caller can say the cap was
                        // reached rather than reporting a confident "blocked" about something nobody looked
                        // up.
                        offBoardSkipped <- offBoardSkipped + 1

                        Ok
                            { Ref = Some r
                              Raw = r.Short
                              State = BlockerUnknown }
                    else
                        match Reads.blockerState transport r.Owner r.Repo r.Number with
                        | Error e -> Error e
                        | Ok state ->
                            offBoardResolved <- offBoardResolved + 1

                            Ok
                                { Ref = Some r
                                  Raw = r.Short
                                  State = state }

        let blockersOf (row: Row) : IoResult<Blocker list> =
            if String.IsNullOrWhiteSpace row.BlockedByRaw then
                Ok []
            else
                row.BlockedByRaw.Split(',')
                |> Array.map (fun t -> t.Trim())
                |> Array.filter (fun t -> t <> "")
                |> Array.fold
                    (fun acc token ->
                        match acc with
                        | Error e -> Error e
                        | Ok blockers ->
                            match resolveBlocker row.Ref.Owner row.Ref.Repo token with
                            | Error e -> Error e
                            | Ok b -> Ok(blockers @ [ b ]))
                    (Ok [])

        use stream = new IO.MemoryStream()
        use w = new Utf8JsonWriter(stream)

        w.WriteStartObject()
        w.WriteString("schema", "fsgg.coord.snapshot/1")
        w.WriteBoolean("allowBacklog", allowBacklog)

        match limit with
        | Some n -> w.WriteNumber("limit", n)
        | None -> w.WriteNull("limit")

        w.WriteNumber("leaseMinutes", leaseMinutes)

        // `inFlight` is what live claims already reserve. It is assembled below, from the SAME marker reads
        // the candidates need — so a claimed item costs one marker read, not two.
        //
        // The HOLDER travels with it. A reservation that does not name who holds it can tell a worker that
        // their files are taken but not by whom — and "queued behind a claim held by W, lease frees in ~96m"
        // and "nothing schedulable" are the same fact and two completely different instructions (#428). The
        // first sends the worker to talk to W; the second sends them home. A markerless In-progress row has
        // no W to name (`RUnowned`), which is a different instruction again: there is no lease to wait out.
        let inFlight = ResizeArray<string * string * ReservedPaths * Reserved>()

        // THE ARRAY IS CALLED `items` ON THE WIRE. Not `candidates` — that is what the parser reads, and a
        // writer that invented its own name would produce a document that refuses to parse on every single
        // run, forever, while looking (to a reader of either half alone) perfectly correct.
        w.WriteStartArray("items")

        let mutable failure: IoError option = None

        for row in candidates do
            if failure.IsNone then
                match row.State with
                // A CLOSED AND STAMPED ITEM IS SWEPT, NOT READ (#520, and case 51's "the issue is closed").
                // It stays a candidate so `decide` can name it — `Schedulability` answers
                // `Closed -> IssueClosed` as its FIRST question, before column, blockers, touch-set or lock
                // — but it needs none of those reads to do so. Fetching its markers would pay the budget
                // that dies first (#418) for a verdict `state` alone already settles, and bash never
                // fetches them either. The body is likewise never fetched EXCEPT for the narrow,
                // already-free-to-detect population .github#2254 names — see the arm below.
                //
                // THE GUARD IS THE DONE STAMP, NOT `Closed` (.github#2225). This arm swept on `Closed`
                // alone, and the sentence licensing that — "its touch-set is never consulted" — was TRUE
                // when it was written. `delivery` (#2131) began consulting it without touching this file,
                // and neither site could see the other, so the licence expired silently while both halves
                // still read as correct locally.
                //
                // In this protocol CLOSING IS THE MIDDLE OF AN ITEM, not its end: `merge-and-release` owes
                // publication, byte-identity verification, receipts and registry records AFTER the merge
                // that closes the issue, and only then a done stamp. Sweeping that whole window dropped the
                // item's body AND its markers, and one sweep therefore failed in three registers — `who`
                // reported EMPTY for a live claim, `batch` reserved NOTHING and could hand a second worker
                // the tree its holder was standing in (#1858's class), and `delivery` blamed the ITEM's own
                // `Paths:` line for the reader's blindness. The terminal fact is the STAMP; a
                // closed-but-unstamped row is a first-class in-flight state.
                | Closed when row.Status = Done ->
                    w.WriteStartObject()
                    w.WriteString("owner", row.Ref.Owner)
                    w.WriteString("repo", row.Ref.Repo)
                    w.WriteNumber("number", row.Ref.Number)
                    w.WriteString("status", statusWireName row.Status)
                    w.WriteString("state", "CLOSED")
                    w.WriteStartArray("blockers")
                    w.WriteEndArray()

                    // .github#2254: `CLASS-PROJECTION-LAG`'s own gate is `Open` (`Chore.fs`), so a row that
                    // reaches Done/CLOSED before any reconcile pass ever observes it while still Open keeps
                    // its disagreement — an EMPTY board `Class` column beside a body that DOES declare one
                    // — examined by nothing, forever. This sweep is exactly why: it never sends the body
                    // that disagreement would be read from.
                    //
                    // REPAIR 1 (`heron-fef6`): this function no longer decides whether to pay that read.
                    // `row.SweptBody` already carries the answer — `Some` only when `scanFresh` populated it
                    // for a `Cache.Reconciling` scan of exactly this row (empty `BoardClass`), `None` on
                    // every `Scheduling`/`Offering` scan and on every row that already carried a `Class`
                    // value. A naive gate HERE on `BoardClass` alone once paid this read for `next`, `batch`
                    // and `take` too — measured, `+1 GET .../issues/398` on `batch --json` — even though
                    // `Schedulability.schedulable` decides a closed row on `state` alone and never reaches
                    // `Item.Class`. Reading `SweptBody` instead of `Reads.issueBody` directly is what keeps
                    // this function itself blind to `Cache.ReadIntent`, so it cannot re-introduce that
                    // regression by acquiring a new caller that forgets to gate.
                    match row.SweptBody with
                    | None -> ()
                    | Some(Ok text) -> w.WriteString("body", text)
                    | Some(Error e) ->
                        bodiesUnreadable <- bodiesUnreadable + 1
                        w.WriteString("bodyUnreadable", explain e)

                    w.WriteEndObject()
                // A CLOSED, UNSTAMPED ROW READS EXACTLY LIKE AN OPEN ONE, and deliberately shares this path
                // rather than getting a third arm of its own: it is the post-merge window, where the claim is
                // live, the touch-set still reserves, and the item's declared facts are still consulted. The
                // shared writer below already emits `state: CLOSED` for it (the `| Closed -> "CLOSED"` case),
                // so the only thing that changes is that its body and markers are now READ.
                | Closed
                | Open ->

                let blockers = blockersOf row

                match blockers with
                | Error e -> failure <- Some e
                | Ok blockers ->

                // THE BODY. An unreadable one is `bodyUnreadable`, NOT an empty body — `TouchSet.parse ""`
                // answers `Undeclared`, a confident OMISSION about an item nobody looked at, and the engine
                // would then schedule every other item against a surface it cannot see.
                let body = Reads.issueBody transport row.Ref.Owner row.Ref.Repo row.Ref.Number

                // THE MARKERS. Unconditional, uncached, and COMPLETE: a lock may never be read from a cache
                // or decided from a lower bound. One unclassifiable comment may be the real winner, so the
                // scheduler refuses the scan rather than reserving only the markers it happened to parse.
                let markers =
                    Reads.markerScan transport row.Ref.Owner row.Ref.Repo row.Ref.Number
                    |> Result.bind (Reads.requireCompleteMarkerScan row.Ref.Short)

                match markers with
                // A FAILED MARKER READ IS FATAL, and it is the one read that must be. Guessing the lock
                // state from a failed read is the one thing a lock may never do — an empty answer would
                // read as "nobody holds this" (#461), and the item would be handed to a second worker.
                | Error e -> failure <- Some e
                | Ok markers ->

                // THE LOCK, NOT JUST THE LIVE WINNER. A stale-but-unreaped marker still holds the item (a
                // lease is a clock; a lock is broken only by `reap`), so the RESERVATION reads `reserver`.
                // The candidate's own `claim` block below is written from the same marker — its liveness
                // then decides whether the item is offered (#581), while the reservation stands regardless.
                let holder = Reads.reserver leaseMinutes markers

                // #712/#581: read the proof of life ONCE. The claim block below RENDERS it and the
                // reservation CARRIES its PR (arm A) — probing again for the reservation would pay the
                // budget that dies first (#418) twice for one fact. Only a STALE marker is probed: a
                // within-lease claim needs no proof of life, and a markerless row has no claim to be
                // alive. A read we could not make is `LivenessUnknown`, never "no PR" (Reads.prAlive's
                // #581 contract) — and it collapses to `livePr = None`, so `None` never means "unread".
                let liveness: Liveness option =
                    match holder with
                    | Some m when Reads.isStale leaseMinutes m ->
                        match Reads.prAlive transport row.Ref.Owner row.Ref.Repo row.Ref.Number with
                        | Ok l -> Some l
                        | Error _ -> Some LivenessUnknown
                    | Some _ -> Some LeaseHeld
                    | None -> None

                w.WriteStartObject()

                // A REF IS THREE FIELDS ON THE WIRE, not one string. `FS.GG.SDD#42` is the DISPLAY form; the
                // codec carries owner, repo and number apart, because a ref that has to be re-parsed on the
                // far side is a ref that can be re-parsed WRONG — and the `Blocked by` free-text field is
                // already the cautionary tale for exactly that (#435, #497, #548).
                w.WriteString("owner", row.Ref.Owner)
                w.WriteString("repo", row.Ref.Repo)
                w.WriteNumber("number", row.Ref.Number)
                w.WriteString("status", statusWireName row.Status)

                w.WriteString(
                    "state",
                    match row.State with
                    | Open -> "OPEN"
                    | Closed -> "CLOSED"
                )

                match body with
                | Ok text -> w.WriteString("body", text)
                | Error e ->
                    bodiesUnreadable <- bodiesUnreadable + 1
                    w.WriteString("bodyUnreadable", explain e)

                w.WriteStartArray("blockers")

                for b in blockers do
                    w.WriteStartObject()

                    match b.Ref with
                    | Some r ->
                        w.WriteString("owner", r.Owner)
                        w.WriteString("repo", r.Repo)
                        w.WriteNumber("number", r.Number)
                    // AN UNPARSEABLE BLOCKER HAS NO REF, AND THAT IS ITS WHOLE POINT. Prose in a dependency
                    // field is not a ref — it has no owner, no repo and no number — and it STILL BLOCKS.
                    | None -> ()

                    w.WriteString("raw", b.Raw)
                    w.WriteString("state", blockerStateName b.State)
                    w.WriteEndObject()

                w.WriteEndArray()

                match holder with
                | None -> ()
                | Some m ->
                    w.WriteStartObject "claim"
                    w.WriteString("worker", m.Worker.Value)
                    w.WriteNumber("ageSeconds", m.AgeSeconds)

                    match m.Session with
                    | Some(SessionId s) -> w.WriteString("session", s)
                    | None -> ()

                    match m.PreviousStatus with
                    | Some s -> w.WriteString("prevStatus", statusWireName s)
                    | None -> ()

                    // LIVENESS. The lease alone may not decide abandonment (#581), so an EXPIRED lease sends
                    // us to look for the item's own `item/<n>-*` PR — server-side proof of life. A read we
                    // could not make is `unknown`, never "no PR". Rendered from the single read above (#712).
                    w.WriteStartObject "liveness"

                    match liveness with
                    | Some(LeaseExpiredPrOpen pr) ->
                        w.WriteString("kind", "lease-expired-pr-open")
                        w.WriteNumber("pr", pr)
                    | Some LeaseExpiredNoPr -> w.WriteString("kind", "lease-expired-no-pr")
                    | Some LeaseExpiredBranchPushed -> w.WriteString("kind", "lease-expired-branch-pushed")
                    | Some LeaseHeld -> w.WriteString("kind", "lease-held")
                    | Some LivenessUnknown -> w.WriteString("kind", "unknown")
                    // Unreachable: this block is entered only under `Some m`, where `liveness` is `Some _`.
                    | None -> w.WriteString("kind", "lease-held")

                    w.WriteEndObject()
                    w.WriteEndObject()

                // #651 — a MARKERLESS item with an open `item/<n>-*` PR is a duplicate implementation
                // already in flight. #581's proof-of-life read the PR only THROUGH a claim marker, so a
                // Ready/Backlog row whose marker never existed (or was cleaned) fell through to `Startable`
                // and got handed out a second time. Probe it here — only when there is NO marker (a marker
                // carries its own liveness above, and offering it is decided by that). An unreadable probe
                // writes nothing: #651 is a false NEGATIVE we are closing, not a new fail-closed surface.
                //
                // #651's OWN JUSTIFICATION FOR THAT — *"fail open to the disjointness check, exactly as a
                // markerless row behaved before"* — IS TRUE ONLY OF ITS OWN POPULATION, AND IS DELETED HERE
                // RATHER THAN LEFT TO COVER THE NEW ONE (.github#1738). It holds for a `Ready`/`Backlog` row:
                // step 5b failing open lands on step 6, disjointness, and offering a row is read-only and
                // re-decided next scan. It does NOT hold for the `Blocked` rows probed below — step 2 answers
                // `WrongStatus Blocked` and step 6 is never reached, so there is no disjointness check to
                // fall open to. What that arm falls open into now is a BOARD WRITE, and the paragraph at the
                // end of this block is where that is stated. A justification that outlives the population it
                // was measured on is how a fail-open keeps its cover.
                //
                // WHICH COLUMNS ARE PROBED IS THE WHOLE QUESTION, AND `Ready`/`Backlog` ALONE WAS THE WRONG
                // ANSWER (.github#1738). That set is "the columns a scheduler would OFFER" — the right
                // subject while `Item.ItemPr`'s only consumer was `Schedulability` step 5b, which is asked
                // about a row's column AS IT STANDS. It is the wrong subject for the OTHER consumer:
                // `Chore`'s `BLOCKER-CLEARED` reads the same field to decide whether to WRITE `Ready` onto a
                // `Blocked` row — the column that makes the row offerable NEXT pass. A `Blocked` row was
                // never probed, so `ItemPr` was `None` for every single one of them, and the gate that reads
                // it could never see its subject: green, and blind (#266). Measured on `.github` on
                // 2026-07-29 — `#1689` is `Blocked` with PR #1911 open on `item/1689-*`, and the snapshot
                // reported `itemPr: null` for it.
                //
                // SO THE PROBE ALSO COVERS THE `BLOCKER-CLEARED` CANDIDATE SET — bounded by the blocker
                // precondition, and asking `Blockers.cleared` rather than spelling it. Not every `Blocked`
                // row: only one with at least one blocker and EVERY blocker resolved. `blockers` is already
                // resolved above, so deciding this costs no read.
                //
                // IT IS A SUPERSET OF THE FIRING SET, NOT AN EQUAL — and that direction is the safe one.
                // `BLOCKER-CLEARED` also requires `humanBlockAllowsFlip` and `predicateAllowsFlip`, so a
                // human-parked row is probed and will never fire. Narrowing to match all three would make
                // this population depend on THREE of `Chore`'s gates and drift three ways — and a probe
                // NARROWER than the rule is the failure this change exists to end (a gate that cannot see
                // its subject), while a probe wider than the rule costs only requests. One gate, the cheap
                // and stable one, shared as a `val`.
                //
                // THE BOUND IS THE BUDGET ARGUMENT, NOT TIDINESS. This is a REST request per row, on the
                // budget the claim lock lives on (ADR-0034 §3, #418), and a blanket `| Open, _ ->` would
                // spend one on every parked, in-progress and blocked row on the board, every scan. Bounded
                // this way the extra cost is at most one request per row whose blockers have just cleared —
                // measured at ZERO additional requests on `.github`'s live board, whose one
                // blocker-carrying `Blocked` row still has an open blocker.
                //
                // AND THE `| _ -> ()` BELOW IS A KNOWN FAIL-OPEN WITH A NEW CONSUMER — .github#1924.
                // `Reads.prAlive : IoResult<Liveness>` has FIVE outcomes and this field carries ONE, so
                // THREE collapse to "no PR" — count them, because the third is the expensive one:
                //   * `Ok LeaseExpiredBranchPushed` — #1055's pushed branch, work in flight before its PR;
                //   * `Ok LivenessUnknown` — the read failed;
                //   * `Error _`, INCLUDING `RateLimited`, which `Reads.prAlive` propagates DELIBERATELY
                //     ("an exhausted budget is a fact about the CLIENT, not about this item's PR") and this
                //     arm then swallows. Rate limiting is SYSTEMIC, not per-row, so one exhausted scan
                //     answers "no PR" for every row it probes and promotes all of them in one pass — which
                //     is the shape of the multi-row event #1738 was filed off.
                // That collapse was #651's deliberate choice while the only consumer was step 5b, which
                // fails open into OFFERING — read-only, and corrected by the next scan. `BLOCKER-CLEARED`
                // fails open into a board WRITE, which `choresFor`'s header calls the asymmetry that makes
                // the mechanism safe to run unattended. Closing it needs a receipt this wire fact cannot
                // carry, so it is filed rather than bodged: see .github#1924.
                match holder with
                | Some _ -> ()
                | None ->
                    let probe () =
                        match Reads.prAlive transport row.Ref.Owner row.Ref.Repo row.Ref.Number with
                        | Ok(LeaseExpiredPrOpen pr) -> w.WriteNumber("itemPr", pr)
                        | Ok LivenessUnknown
                        | Error _ -> w.WriteBoolean("itemPrUnreadable", true)
                        | _ -> ()

                    match row.State, row.Status with
                    | Open, (Ready | Backlog) -> probe ()
                    | Open, Blocked when Blockers.cleared blockers -> probe ()
                    | _ -> ()

                w.WriteEndObject()

                // THE RESERVATION (arm A of bash's `active_claims`, plus the lock #461). It comes from the
                // body we ALREADY read — one read, two uses — and it is what stops a second worker being
                // handed the same files. Two kinds hold a touch-set here:
                //   • a MARKER (live or stale): named by its worker/item — a lock is a lock (#461/#581).
                //   • a MARKERLESS `In progress` row: something is evidently editing those files, so it
                //     reserves too — but there is no worker to name and no lease to wait out, so it is
                //     `Unowned`. Dressing it up as a holder would send a worker to wait for a marker that is
                //     never coming (#428). Only the `In progress` COLUMN licenses this: a Ready/Backlog row
                //     with no marker reserves nothing, because nobody is working it.
                //
                // WHERE THIS AGREES WITH THE #353 COLLISION SCAN, AND THE ONE PLACE IT DOES NOT (.github#1792).
                // `Client.activeCollisions` — behind `overlap --active`, `widen`, `set-paths` — answers the
                // same question ("who has reserved these files") for the OTHER half of the protocol, and the
                // two used to disagree about a LAPSED lease: this arm read `Reads.reserver`, that one read
                // `Reads.winner`, so the scheduler could hold an item reserved while the collision gate called
                // its files free. #1792 settled that in `reserver`'s favour AT BOTH SITES — a lease is a clock,
                // a lock is broken only by `reap` (#461/#581) — so MARKER-BACKED reservations now agree
                // exactly, live or lapsed.
                //
                // THE SECOND BULLET ABOVE IS THE DELIBERATE REMAINDER. `RUnowned` is derived from the COLUMN,
                // and #1779 keyed `activeCollisions` on the marker instead — its candidate set is
                // `Reads.openIssues`, which has no board state in it — so this reservation is unreachable
                // there by construction, not by omission. Closing that would cost the collision gate a board
                // read per call (the GraphQL half #1779 drove to zero, on a verb workers loop, #418/#1666),
                // and would buy a stop with no protocol exit: a markerless row has nobody to `say` to and no
                // marker to `reap`, which the scheduler can absorb and a gate a worker is told to believe
                // cannot. So the rule is: THE TWO SURFACES AGREE ON EVERY MARKER, LIVE OR LAPSED, AND DIVERGE
                // ONLY WHERE THERE IS NO MARKER. `activeCollisions` carries the same sentence, and
                // `ApplicationServiceTests` pins both halves so the divergence stays a decision rather than
                // an accident.
                // #712: carry the #581 proof of life onto the reservation. `Some pr` ONLY for a lapsed
                // lease held open by a PR — every other liveness (within lease, no PR, unread) is `None`,
                // "no proof of life", so the reservation never claims a liveness it does not have.
                let livePr =
                    match liveness with
                    | Some(LeaseExpiredPrOpen pr) -> Some pr
                    | _ -> None

                let reserveAs =
                    match holder with
                    | Some m -> Some(RClaim(m.Worker, row.Ref, m.AgeSeconds, livePr))
                    | None when row.Status = InProgress -> Some(RUnowned row.Ref)
                    | None -> None

                match reserveAs with
                | None -> ()
                | Some held ->
                    match body with
                    | Ok text ->
                        match TouchSet.parse text with
                        | Declared tokens ->
                            let names =
                                tokens
                                |> List.map (fun t ->
                                    match t with
                                    | Matchable s -> s
                                    | Unmatchable s -> s)

                            inFlight.Add(row.Ref.Owner, row.Ref.Repo, RvNames names, held)
                        | _ -> ()
                    // #1150: THE BODY READ FAILED on an item we hold a lock over. Core has a fail-closed
                    // guard for exactly this (`Batch.unusableReservation`, the `Unreadable` branch) — but it
                    // only fires if it RECEIVES an `Unreadable` reservation, and the old `| _ -> ()` dropped
                    // the claim instead, so the guard was dead end-to-end (`BodiesUnreadable` only drove an
                    // advisory warning). Reserve an UNKNOWN surface: we cannot prove any candidate disjoint
                    // from files we never saw, so red the batch rather than hand a second worker its tree.
                    | Error e -> inFlight.Add(row.Ref.Owner, row.Ref.Repo, RvUnreadable(explain e), held)

        w.WriteEndArray()

        // OFF-BOARD CLAIMS RESERVE TOO (#461/#581, case 25). The loop above read every BOARD candidate's
        // marker — but a lock lives OFF the board: a claim on an issue whose column flip failed (the board
        // says Ready, the lock says held), or on one that never reached the board at all. The board scan is
        // blind to it, so a candidate declaring the same files would be handed a tree another worker is
        // standing in — the exact double-claim this scheduler exists to prevent.
        //
        // So scan the in-scope repos' OPEN ISSUES — the SAME paginated, unconditional read `who`/`reap`
        // run (a lock has no hundred-issue limit, and a 304 could serve a `comments: 0` captured before a
        // marker was posted) — and reserve every LIVE claim on an issue the board did NOT already list.
        // This is bash's `active_claims` arm B; arm A (the board's In-progress rows) is the candidate loop
        // above, whose claims are already in `inFlight`.
        let boardRefs =
            rows |> List.map (fun r -> r.Ref.Owner, r.Ref.Repo, r.Ref.Number) |> Set.ofList

        // The repos an off-board claim can live in are the in-scope board's repos (bash derives them the
        // same way). No candidate names a repo → no repo to scan, and no board item means nothing off the
        // board could contradict — so a fixture with no schedulable candidates pays no issue-list read.
        let repos =
            candidates |> List.map (fun r -> r.Ref.Owner, r.Ref.Repo) |> List.distinct

        for (o, r) in repos do
            if failure.IsNone then
                // FAILS CLOSED (#461): an unreadable scan is never an empty one — batch would schedule over
                // a lock it could not see, the precise fail-open the whole scheduler prevents.
                match Reads.openIssues transport o r with
                | Error e -> failure <- Some e
                | Ok issues ->
                    for issue in issues do
                        let n = issue.Number

                        // A BOARD ITEM IS NOT OFF THE BOARD: its marker was already read (and reserved, if
                        // held) by the candidate loop, so re-reading it here would pay the same budget twice
                        // and risk double-reserving. Only issues the board never listed reach the marker read.
                        if failure.IsNone && not (boardRefs.Contains(o, r, n)) then
                            let markerSubject = $"%s{o}/%s{r}#%d{n}"

                            match
                                Reads.markerScan transport o r n
                                |> Result.bind (Reads.requireCompleteMarkerScan markerSubject)
                            with
                            | Error e -> failure <- Some e
                            | Ok markers ->
                                match Reads.reserver leaseMinutes markers with
                                // No claim at all — a chatty issue somebody merely commented on. A comment is
                                // not a lock, so it reserves nothing. A STALE marker, on the other hand, DOES
                                // reach `Some m` (via `reserver`, not `winner`): a lapsed lease is still a
                                // lock, broken only by `reap`, so its touch-set is reserved with its true
                                // (expired) age — the starved-queue slice's whole point (#428, case 25).
                                | None -> ()
                                | Some m ->
                                    // The touch-set rides in on the SAME list read (one read, two uses),
                                    // exactly as the board loop reuses the candidate body. A claim declaring
                                    // no touch-set reserves no files — the board loop's own rule (line above).
                                    //
                                    // AND A BODY WE COULD NOT READ IS `RvUnreadable`, NOT NOTHING
                                    // (.github#1794). The board loop one screen up has said this since #1150
                                    // — *"an unreadable one is `bodyUnreadable`, NOT an empty body"* — and
                                    // then this sweep, reading the SAME kind of body off a different route,
                                    // let an unreadable one fall through the `| _ -> ()` below and reserve
                                    // nothing. That is #1150's own fail-open, on the arm it did not reach:
                                    // the claim reserved nothing and a candidate overlapping its real files
                                    // was handed the tree its holder is standing in. `RvUnreadable` writes
                                    // `pathsUnreadable` to the wire, `decide` reconstructs
                                    // `TouchSet.Unreadable`, and `Batch.schedule` reds the batch on it.
                                    let reservation =
                                        match issue.Body with
                                        | Reads.BodyUnread reason -> Some(RvUnreadable reason)
                                        | Reads.BodyRead body ->
                                            match TouchSet.parse body with
                                            | Declared tokens ->
                                                tokens
                                                |> List.map (fun t ->
                                                    match t with
                                                    | Matchable s -> s
                                                    | Unmatchable s -> s)
                                                |> RvNames
                                                |> Some
                                            | _ -> None

                                    match reservation with
                                    | None -> ()
                                    | Some rv ->
                                        // #712/#581: an off-board claim reserves too, and the tools must not
                                        // call it "reapable" if a PR is keeping it alive. Probed HERE — after
                                        // the touch-set is known to reserve something — so a stale claim that
                                        // declares no files never pays a proof-of-life read it would discard
                                        // (the budget that dies first, #418). Probe ONLY a STALE claim: a
                                        // within-lease one is not reapable anyway, so its `livePr` is `None`.
                                        // A read we could not make collapses to `None`, never distinguished
                                        // from "no PR", so the reservation never asserts a liveness it lacks.
                                        let livePr =
                                            if Reads.isStale leaseMinutes m then
                                                match Reads.prAlive transport o r n with
                                                | Ok(LeaseExpiredPrOpen pr) -> Some pr
                                                | _ -> None
                                            else
                                                None

                                        inFlight.Add(
                                            o,
                                            r,
                                            rv,
                                            RClaim(m.Worker, { Owner = o; Repo = r; Number = n }, m.AgeSeconds, livePr)
                                        )

        w.WriteStartArray("inFlight")

        for (owner, repoName, paths, held) in inFlight do
            w.WriteStartObject()
            w.WriteString("owner", owner)
            w.WriteString("repo", repoName)

            // #1150: mirror the CANDIDATE convention (`body` vs `bodyUnreadable`, #496) — write EITHER a
            // `paths` array OR a `pathsUnreadable` reason, never both. The reader keys on which is present:
            // `pathsUnreadable` reconstructs `TouchSet.Unreadable`, which reds the batch.
            match paths with
            | RvNames names ->
                w.WriteStartArray("paths")

                for p in names do
                    w.WriteStringValue p

                w.WriteEndArray()
            | RvUnreadable reason -> w.WriteString("pathsUnreadable", reason)

            w.WriteStartObject "holder"

            match held with
            | RClaim(worker, holderRef, age, livePr) ->
                w.WriteString("kind", "live-claim")
                w.WriteString("worker", worker.Value)
                w.WriteString("owner", holderRef.Owner)
                w.WriteString("repo", holderRef.Repo)
                w.WriteNumber("number", holderRef.Number)
                w.WriteNumber("ageSeconds", age)
                // #712/#581: written ONLY when present, so a within-lease claim round-trips
                // byte-identically and only a PR-kept-alive lapsed claim carries the extra field. The
                // codec's `live-claim` reader (`Snapshot.parse`) reads it back into `LiveClaim`'s `livePr`.
                match livePr with
                | Some pr -> w.WriteNumber("livePr", pr)
                | None -> ()
            | RUnowned holderRef ->
                // A MARKERLESS In-progress reserver — the codec's `unowned` holder, which carries only the
                // ref (there is no worker, and no age, because there is no lease).
                w.WriteString("kind", "unowned")
                w.WriteString("owner", holderRef.Owner)
                w.WriteString("repo", holderRef.Repo)
                w.WriteNumber("number", holderRef.Number)

            w.WriteEndObject()
            w.WriteEndObject()

        w.WriteEndArray()
        w.WriteEndObject()
        w.Flush()

        match failure with
        | Some e -> Error e
        | None ->
            Ok(
                Encoding.UTF8.GetString(stream.ToArray()),
                { Candidates = List.length candidates
                  RepoAdvisory = scoped.Advisory
                  OffBoardResolved = offBoardResolved
                  OffBoardSkipped = offBoardSkipped
                  BodiesUnreadable = bodiesUnreadable }
            )
