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
          IsPullRequest: bool }

    [<Literal>]
    let OffBoardCap = 60

    type Scoped =
        { Rows: Row list
          Advisory: string option }

    // THE `--repo` FILTER, ONCE. Hand-rolled per verb it was a silent fail-open five times over (#979);
    // `scripts/check-repo-filter-monopoly.py` is what keeps it one. See `Scan.fsi` for the full argument.
    let scope (repo: string option) (rows: Row list) : Scoped =
        match repo with
        | None -> { Rows = rows; Advisory = None }
        | Some name ->
            let kept =
                rows
                |> List.filter (fun r -> String.Equals(r.Ref.Repo, name, StringComparison.OrdinalIgnoreCase))

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
                        |> List.map (fun r -> r.Ref.Repo)
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
                 content { \
                   __typename \
                   ... on Issue { number title state repository { nameWithOwner } } \
                   ... on PullRequest { number title state repository { nameWithOwner } } \
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

    let private parseRow (node: JsonElement) : Row option =
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

                Some
                    { Ref =
                        { Owner = parts.[0]
                          Repo = parts.[1]
                          Number = n }
                      Title = str content "title" |> Option.defaultValue ""
                      Status = nested node "status" "name" |> Option.map boardStatusOf |> Option.defaultValue NoStatus
                      BlockedByRaw = nested node "blockedBy" "text" |> Option.defaultValue ""
                      State = state
                      IsPullRequest = isPr }

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

            w.WriteString(
                "state",
                match r.State with
                | Open -> "OPEN"
                | Closed -> "CLOSED"
            )

            w.WriteBoolean("isPullRequest", r.IsPullRequest)
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
                                | _ -> false }
                    | _ -> None)
                |> List.ofSeq
                |> Some

        with :? JsonException ->
            None

    let private scanFresh
        (transport: IGitHubTransport)
        (owner: string)
        (title: string)
        (projectNumber: int)
        : IoResult<Row list> =

        let subject = $"the board '%s{title}' in %s{owner}"

        let rec page (cursor: string option) (acc: Row list) (guard: int) : IoResult<Row list> =
            if guard <= 0 then
                Error(Malformed(subject, "the board scan did not terminate within 100 pages — refusing to spin"))
            else

            let variables =
                [ "owner", VString owner
                  "number", VNumber(double projectNumber) ]
                @ (match cursor with
                   | Some c -> [ "cursor", VString c ]
                   | None -> [])

            let request =
                { Method = "POST"
                  Path = "graphql"
                  Query = []
                  Body = Query(BoardDoc, variables)
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

                    if messages |> List.exists Budget.isRateLimited then
                        // `GraphQlBudget` is asserted, not read, and here that is sound: this arm parses a
                        // GraphQL `errors` array, so the response IS a GraphQL response. The reset stays
                        // `None` — GitHub reports this shape as HTTP 200 with no rate-limit headers, and
                        // the body carries no `resetAt`.
                        Error(RateLimited(GraphQlBudget, None))
                    else
                        Error(GraphQlErrors messages)

                | _ ->

                let items =
                    doc.RootElement
                        .GetProperty("data")
                        .GetProperty("organization")
                        .GetProperty("projectV2")
                        .GetProperty("items")

                let rows =
                    items.GetProperty("nodes").EnumerateArray()
                    |> Seq.choose parseRow
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
                Error(Malformed(subject, "the board scan's response is missing `data.organization.projectV2.items`"))

        match page None [] 100 with
        | Error e -> Error e
        | Ok rows ->
            // A FAILED SCAN IS NEVER CACHED (#344), and `putScan` is what enforces it — an empty document is
            // refused there, at the write, because this is the last moment "the board is empty" and "I could
            // not read the board" are still distinguishable.
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
            | None -> scanFresh transport owner title projectNumber

        | None -> scanFresh transport owner title projectNumber

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
        let inFlight = ResizeArray<string * string * string list * Reserved>()

        // THE ARRAY IS CALLED `items` ON THE WIRE. Not `candidates` — that is what the parser reads, and a
        // writer that invented its own name would produce a document that refuses to parse on every single
        // run, forever, while looking (to a reader of either half alone) perfectly correct.
        w.WriteStartArray("items")

        let mutable failure: IoError option = None

        for row in candidates do
            if failure.IsNone then
                match row.State with
                // A CLOSED ISSUE IS SWEPT, NOT READ (#520, and case 51's "the issue is closed"). It stays a
                // candidate so `decide` can name it — `Schedulability` answers `Closed -> IssueClosed` as its
                // FIRST question, before column, blockers, touch-set or lock — but it needs none of those
                // reads to do so. Fetching its body/markers would pay the budget that dies first (#418) for a
                // verdict `state` alone already settles, and bash never fetches them either.
                | Closed ->
                    w.WriteStartObject()
                    w.WriteString("owner", row.Ref.Owner)
                    w.WriteString("repo", row.Ref.Repo)
                    w.WriteNumber("number", row.Ref.Number)
                    w.WriteString("status", statusWireName row.Status)
                    w.WriteString("state", "CLOSED")
                    w.WriteStartArray("blockers")
                    w.WriteEndArray()
                    w.WriteEndObject()
                | Open ->

                let blockers = blockersOf row

                match blockers with
                | Error e -> failure <- Some e
                | Ok blockers ->

                // THE BODY. An unreadable one is `bodyUnreadable`, NOT an empty body — `TouchSet.parse ""`
                // answers `Undeclared`, a confident OMISSION about an item nobody looked at, and the engine
                // would then schedule every other item against a surface it cannot see.
                let body = Reads.issueBody transport row.Ref.Owner row.Ref.Repo row.Ref.Number

                // THE MARKERS. Unconditional, uncached: a lock may never be read from a cache.
                let markers = Reads.markers transport row.Ref.Owner row.Ref.Repo row.Ref.Number

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
                // carries its own liveness above, and offering it is decided by that) and the column is one
                // a scheduler would OFFER; an In-progress/Done/etc. row is never offered, so the read would
                // be wasted budget. An unreadable probe writes nothing (fail open to the disjointness check,
                // exactly as a markerless row behaved before): #651 is a false NEGATIVE we are closing, not a
                // new fail-closed surface.
                match holder with
                | Some _ -> ()
                | None ->
                    match row.State, row.Status with
                    | Open, (Ready | Backlog) ->
                        match Reads.prAlive transport row.Ref.Owner row.Ref.Repo row.Ref.Number with
                        | Ok(LeaseExpiredPrOpen pr) -> w.WriteNumber("itemPr", pr)
                        | _ -> ()
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

                match reserveAs, body with
                | Some held, Ok text ->
                    match TouchSet.parse text with
                    | Declared tokens ->
                        let names =
                            tokens
                            |> List.map (fun t ->
                                match t with
                                | Matchable s -> s
                                | Unmatchable s -> s)

                        inFlight.Add(row.Ref.Owner, row.Ref.Repo, names, held)
                    | _ -> ()
                | _ -> ()

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
                    for (n, body) in issues do
                        // A BOARD ITEM IS NOT OFF THE BOARD: its marker was already read (and reserved, if
                        // held) by the candidate loop, so re-reading it here would pay the same budget twice
                        // and risk double-reserving. Only issues the board never listed reach the marker read.
                        if failure.IsNone && not (boardRefs.Contains(o, r, n)) then
                            match Reads.markers transport o r n with
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
                                    match TouchSet.parse body with
                                    | Declared tokens ->
                                        let names =
                                            tokens
                                            |> List.map (fun t ->
                                                match t with
                                                | Matchable s -> s
                                                | Unmatchable s -> s)

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
                                            names,
                                            RClaim(m.Worker, { Owner = o; Repo = r; Number = n }, m.AgeSeconds, livePr)
                                        )
                                    | _ -> ()

        w.WriteStartArray("inFlight")

        for (owner, repoName, paths, held) in inFlight do
            w.WriteStartObject()
            w.WriteString("owner", owner)
            w.WriteString("repo", repoName)
            w.WriteStartArray("paths")

            for p in paths do
                w.WriteStringValue p

            w.WriteEndArray()
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
