namespace FS.GG.Coord.GitHub

module Reads =

    open System
    open System.Text.Json
    open System.Text.RegularExpressions
    open FS.GG.Coord
    open FS.GG.Coord.Types
    open Errors
    open Transport

    type Marker =
        { Id: int64
          Worker: WorkerId
          Session: SessionId option
          AgeSeconds: int
          PreviousStatus: BoardStatus option
          PathRepo: string option
          Raw: string }

    /// THE MARKER READ, WITH ITS OWN COMPLETENESS ATTACHED (.github#1668).
    ///
    /// `Markers` alone cannot distinguish *"this issue carries no claim"* from *"this issue carries comments
    /// I could not read, and any of them may have been the claim"*. Those are different facts, and the whole
    /// of `who`'s under-report is the second one being rendered as the first.
    type MarkerScan =
        { /// The claim markers, lowest comment id first — the CAS's total order, winner at the head.
          Markers: Marker list
          /// One entry per comment the scan could NOT classify, each saying which comment and why. EMPTY is
          /// the load-bearing value: empty means the marker list is COMPLETE, and only then may a caller
          /// say an item is unheld.
          Unreadable: string list }

    type RateLimitSnapshot = { Remaining: int; Limit: int }

    /// Parse a JSON document, or say we could not. There is no third option, and in particular there is no
    /// "return an empty document" — bytes we cannot read are a FAILED READ (#461).
    let private parse (subject: string) (body: string) : IoResult<JsonDocument> =
        if String.IsNullOrWhiteSpace body then
            Error(
                Malformed(
                    subject,
                    "the response body was empty. An empty body is not an empty result — it is a read that produced nothing, and it will not be read as 'there is nothing there'."
                )
            )
        else
            try
                Ok(JsonDocument.Parse body)
            with :? JsonException as e ->
                // HTTP 200 CARRYING BYTES THAT ARE NOT JSON. A truncated page, a proxy's HTML error body, a
                // 5xx rendered as text — `gh` exits 0 on all of them, `jq` prints nothing and exits 0, and
                // the empty string that falls out reads as "nothing here". That is #461 exactly, and it is
                // why this is an error and not an empty list.
                Error(Malformed(subject, $"the response is not JSON: %s{e.Message}"))

    let private str (e: JsonElement) (name: string) =
        match e.TryGetProperty name with
        | true, v when v.ValueKind = JsonValueKind.String -> Some(v.GetString())
        | _ -> None

    /// The `per_page` every collection read in this module asks for.
    ///
    /// IT IS A CONSTANT BECAUSE THE HEADROOM PROOF DEPENDS ON THE REQUEST AND THE PAGE SHAPE AGREEING. Ask
    /// for 100 and declare `Page(30, …)` and the guard would "prove" headroom over a page that is actually
    /// full — the one boundary it exists to refuse. One literal, used by both.
    [<Literal>]
    let private CollectionPageSize = 100

    /// What a response's ETag is entitled to stand for. DECLARED BY THE CALLER, never inferred — a PR object
    /// carries `labels`/`assignees` arrays and a check-runs page carries `check_runs`, and nothing in the
    /// bytes distinguishes "a resource that happens to contain a list" from "a page OF a list". Guessing that
    /// apart is how a validator comes to vouch for something it never saw.
    type private PageShape =
        /// ONE resource (`pulls/{n}`). It cannot paginate: there is no `Link`, no page two, and nothing for a
        /// validator to be partial about.
        | Single
        /// One PAGE of a collection: the `per_page` the request asked for, and where the items live — `None`
        /// when the root IS the array (`repos/…/issues`), `Some prop` for GitHub's wrapper objects
        /// (`{"total_count": …, "check_runs": [ … ]}`).
        | Page of perPage: int * property: string option

    /// How many items this page carries. `None` — a body we cannot count, or one shaped unlike the caller
    /// declared — and `memoisable` then refuses, because an uncountable page is one we cannot prove anything
    /// about.
    let private pageCount (property: string option) (body: string) : int option =
        try
            use doc = JsonDocument.Parse body

            let items =
                match property with
                | None ->
                    if doc.RootElement.ValueKind = JsonValueKind.Array then
                        Some doc.RootElement
                    else
                        None
                | Some p ->
                    match doc.RootElement.TryGetProperty p with
                    | true, v when v.ValueKind = JsonValueKind.Array -> Some v
                    | _ -> None

            items |> Option.map (fun a -> a.GetArrayLength())
        with :? JsonException ->
            None

    /// May this response's ETag be stored against this body?
    ///
    /// **THIS PREDICATE IS A PROOF, NOT A HEURISTIC**, and the thing it proves is that a future 304 against
    /// the stored validator can only mean the collection is genuinely unchanged.
    ///
    /// The hazard: `Transport.Send` follows `Link: rel=next` and MERGES the pages, but an ETag belongs to the
    /// FIRST request alone. Store it against a merge and a set that grows a page while page one stays
    /// byte-identical answers 304 — the merge never runs, and the caller is served a one-page body for a
    /// two-page set. A red run invisible on page two then scores GREEN: #461 (a partial read wearing a
    /// complete one's clothes) deciding whether to merge.
    ///
    /// `Transport.Send` now closes the FIRST half of that: a merged response carries no ETag at all, dropped
    /// at the only layer that knows a merge happened. So page one's validator can no longer escape onto a
    /// merged body, and the `NextLink` arm below is a backstop, not the guard.
    ///
    /// It is only half, and the second half is the one that bites. *Don't memoise a response that paginated*
    /// never runs on the case that matters: the unsound read is precisely the one where the collection has
    /// grown, page one 304s, and the pagination never happens at all — so there is no merge for anyone to
    /// notice, at this layer or any other.
    ///
    /// **HEADROOM CLOSES IT.** Memoise a page only when it carries FEWER items than the `per_page` it asked
    /// for. Then, writing `n` for the stored page's item count and `m` for the collection's true size later:
    ///
    ///   * `m <= perPage` — page one IS the whole collection. A 304 means page one's bytes still equal the
    ///     stored body, so the collection is unchanged and serving it is correct.
    ///   * `m > perPage` — page one now carries `perPage` items, and `n < perPage`, so page one's bytes
    ///     CANNOT equal the stored body. The server must answer 200, and that response carries a `Link` — so
    ///     we drop the validator rather than store it against a merge.
    ///
    /// A 304 is therefore only reachable in the first case. The `n < perPage` strictness is load-bearing:
    /// at `n = perPage` exactly (a full page, no `Link`) growth could leave page one untouched and 304 over
    /// the items that landed on page two — the one boundary this whole rule exists to refuse.
    let private memoisable (shape: PageShape) (response: Response) : bool =
        // BELT AND BRACES. `Transport.Send` already strips the ETag from a merged response, so there is
        // nothing here to store — but a merged body must not be memoisable on its own terms either, and a
        // guard that depends on another layer having done its job is one that fails silently when it stops.
        if response.NextLink.IsSome then
            false
        else
            match shape with
            | Single -> true
            | Page(perPage, property) ->
                // A page we cannot COUNT is a page we cannot prove has headroom. Refuse: the cost of not
                // memoising is a paid read, and the cost of being wrong is a merge over runs we never saw.
                match pageCount property response.Body with
                | Some n -> n < perPage
                | None -> false

    /// A REST GET that revalidates against a stored ETag: send the validator, serve the cached body on 304.
    ///
    /// **A 304 IS NOT A STALE READ, AND THAT IS THE WHOLE ARGUMENT.** The TTL caches (`Cache.getScan`) answer
    /// WITHOUT asking, so they trade freshness for cost and must be gated on `ReadIntent`. This does not: the
    /// server is asked every time, and a 304 is its assertion that the body we hold IS current. So this is
    /// transparent — it returns exactly what the unconditional read would have returned — and it is free
    /// (GitHub does not bill a 304 against the primary REST limit when the request carries an `Authorization`
    /// header). Transparent and free is why it may go where a TTL cache may NOT.
    ///
    /// **IT MAY NOT GO ON THE LOCK, AND CHEAPNESS IS NOT AN ARGUMENT AGAINST THAT.** `markers`, `messages`
    /// and `openIssues` stay unconditional under the rule they already carry — *a lock may never be read from
    /// a cache* (ADR-0034 §3). `memoisable`'s headroom proof would in fact admit them, and that is not a
    /// licence: it bounds what a VALIDATOR may vouch for, which is a strictly weaker question than what a LOCK
    /// may be answered from. Two rules, and the stricter one governs.
    ///
    /// THE CACHE KEY CARRIES THE QUERY, not just the path. `actions/runs?head_sha=…` is the SAME path for
    /// every commit, so keying on the path alone would serve one SHA's runs as another's — not a stale answer
    /// but a WRONG one, and the verdict it feeds is whether to merge.
    ///
    /// `shape` is what this response's ETag is entitled to stand for — see `memoisable`.
    let private conditionalGet
        (transport: IGitHubTransport)
        (subject: string)
        (path: string)
        (query: (string * string) list)
        (shape: PageShape)
        : IoResult<string> =

        let cacheKey =
            let qs = query |> List.map (fun (k, v) -> $"%s{k}=%s{v}") |> String.concat "&"
            if qs = "" then path else $"%s{path}?%s{qs}"

        let request =
            { Method = "GET"
              Path = path
              Query = query
              Body = NoBody
              Budget = Rest
              IfNoneMatch = Cache.getETag cacheKey
              Subject = subject }

        match transport.Send request with
        | Error e -> Error e
        | Ok response ->
            match response with
            // The server says what we hold is current. A missing body here is OUR protocol violation — a
            // validator we could not honour — and `getBody` reports it as an error, never an empty result.
            | NotModified -> Cache.getBody cacheKey
            | _ ->
                // Storing `None` is not a no-op: it DROPS any validator left from when this set still had
                // headroom, which is what stops a grown collection revalidating against the page it used to
                // fit on.
                let validator =
                    if memoisable shape response then
                        response.ETag
                    else
                        None

                Cache.putBody cacheKey validator response.Body
                Ok response.Body

    // ---- the claim marker -------------------------------------------------------------------------

    /// ANCHORED AT THE START OF THE BODY, and `worker=` must be the FIRST key.
    ///
    /// Both are security properties, not style. Un-anchor it and any free-form `say` message whose text
    /// merely QUOTES a marker (`<!-- fsgg:claim worker=ghost -->`) forges a lock on the item it is posted
    /// to.
    let private markerRe =
        Regex(@"^<!--\s*fsgg:claim\s", RegexOptions.Compiled)

    let private workerRe =
        Regex(@"^<!--\s*fsgg:claim\s+worker=(?<w>[^\s>]+)", RegexOptions.Compiled)

    let private prevRe = Regex(@"^<!--[^>]*\sprev=(?<p>[^\s>]*)", RegexOptions.Compiled)

    let private sessionRe = Regex(@"^<!--[^>]*\ssession=(?<s>[^\s>]+)", RegexOptions.Compiled)
    let private pathRepoRe = Regex(@"^<!--[^>]*\spathRepo=(?<r>[^\s>]+)", RegexOptions.Compiled)

    /// Undo `enc_status`. `%` was encoded FIRST, so it must be decoded LAST — otherwise a status
    /// containing a literal `%20` decodes into a space that was never there.
    let private decodeStatus (s: string) =
        s.Replace("%20", " ").Replace("%25", "%")

    /// The board column, from the marker's `prev=`.
    ///
    /// An unrecognised column yields `None` — "this claim recorded no restorable column" — rather than a
    /// guess. `release` then puts back `Ready`, which is what it did before anyone recorded anything, and
    /// says so. A value nobody recorded cannot be restored (#481), and inventing one would overwrite a
    /// column somebody chose deliberately.
    let statusOfName (name: string) =
        match name.Trim().ToLowerInvariant() with
        | "" -> None
        | "backlog" -> Some Backlog
        | "ready" -> Some Ready
        | "in progress" -> Some InProgress
        | "blocked" -> Some Blocked
        | "in review" -> Some InReview
        | "done" -> Some Done
        | _ -> None

    /// WHAT ONE COMMENT TURNED OUT TO BE — three answers, and the third one is .github#1668's whole subject.
    ///
    /// `NotAMarker` and `Unclassifiable` were the SAME answer (`None`) for this function's entire life, and
    /// collapsing them is a fail-OPEN on the lock itself: "I read this comment and it is not a claim" and "I
    /// could not read this comment at all" both left the marker list one element shorter and said nothing.
    /// An issue whose ONLY marker is unreadable then returns `Ok []` — a failed read wearing an empty set's
    /// clothes, which is #461/#1794 one layer down, inside the read those items were closed to protect.
    ///
    /// The old code KNEW this was the risk and argued it away in a comment that was simply false: it said an
    /// unorderable marker "still matches `markerRe` and still blocks below". Nothing blocked. `None` removed
    /// it from the list, and `who` printed `UNCLAIMED — In progress with NO claim marker` over it.
    type private CommentRead =
        /// A claim marker, read whole.
        | IsMarker of Marker
        /// POSITIVELY identified as something other than a claim — an ordinary comment, a `fsgg:msg`. We
        /// looked, and there is no lock here. This one is safe to drop, and it is the only one that is.
        | NotAMarker
        /// WE COULD NOT TELL. Either the comment carried no readable body — so it may have been a marker and
        /// we cannot say — or its body IS a marker and it carries no id to order it by. Never a silent drop.
        | Unclassifiable of reason: string

    let private readComment (now: DateTimeOffset) (index: int) (comment: JsonElement) : CommentRead =
        match str comment "body" with
        // NOT "not a marker". A comment whose `body` is absent or ill-typed is a comment we did not read,
        // and a claim marker is exactly the thing that could have been in it.
        | None ->
            Unclassifiable
                $"comment %d{index} carries no readable `body` field, so it could not be examined for a claim marker"
        | Some body when not (markerRe.IsMatch body) -> NotAMarker
        | Some body ->

        let id =
            match comment.TryGetProperty "id" with
            | true, v when v.ValueKind = JsonValueKind.Number -> Some(v.GetInt64())
            | _ -> None

        match id with
        // A COMMENT WITH NO ID IS NOT A MARKER WE CAN ORDER, and the id IS the lock — it is the total order
        // every racer observes. A marker we cannot place in that order cannot win or lose a race. But it is
        // a MARKER: `markerRe` matched, so we are looking at a claim. Reporting the item free on the
        // strength of it is the one thing a lock may never do, so it leaves as `Unclassifiable` and the
        // caller fails closed on it.
        | None ->
            Unclassifiable
                $"comment %d{index} IS a claim marker (its body matches the marker grammar) but carries no numeric `id`, so it cannot be placed in the CAS's total order"
        | Some id ->

        let m = workerRe.Match body

        let worker =
            if m.Success then
                WorkerId m.Groups.["w"].Value
            else
                // A MARKER WE CANNOT PARSE A WORKER OUT OF IS A CLAIM HELD BY NOBODY — and it BLOCKS the
                // item rather than vanishing. A half-written lock must fail closed. If this returned `None`
                // the item would read as free, and the whole point of a lock is that a lock you cannot
                // read is still a lock.
                WorkerId "unparsed-marker"

        let session =
            let s = sessionRe.Match body
            if s.Success then Some(SessionId s.Groups.["s"].Value) else None

        let previousStatus =
            let p = prevRe.Match body

            if p.Success then
                statusOfName (decodeStatus p.Groups.["p"].Value)
            else
                None

        let pathRepo =
            let p = pathRepoRe.Match body
            if p.Success then Some p.Groups.["r"].Value else None

        // AGE IS MEASURED AGAINST THE SERVER'S CLOCK (`updated_at`), not ours. A marker with no readable
        // timestamp gets a NEGATIVE age, which `Schedulability.leaseWindow` renders as "lease unknown" —
        // because inventing "frees in ~120m" out of a missing field is a confident sentence with nothing
        // behind it, and both #440 and #488 were closed for exactly that.
        let ageSeconds =
            match str comment "updated_at" with
            | Some ts ->
                match DateTimeOffset.TryParse ts with
                | true, at -> int (now - at).TotalSeconds
                | _ -> -1
            | None -> -1

        IsMarker
            { Id = id
              Worker = worker
              Session = session
              AgeSeconds = ageSeconds
              PreviousStatus = previousStatus
              PathRepo = pathRepo
              Raw = body }

    /// Has this marker's lease lapsed?
    ///
    /// A NEGATIVE age means we could not read the marker's timestamp, and it is NOT stale. Reading an
    /// unreadable age as an expired lease would reap a live claim on the strength of a field we failed to
    /// parse — a failed read deciding a lock, which is the exact substitution this whole layer exists to
    /// make impossible.
    let isStale (leaseMinutes: int) (marker: Marker) =
        marker.AgeSeconds >= 0 && marker.AgeSeconds > leaseMinutes * 60

    /// THE CAS's WINNER, IN ONE PLACE: the lowest-id marker whose lease is still live.
    ///
    /// GitHub issues comment ids from a single server-side sequence, so this is a total order that every
    /// racer observes identically — which is what makes the comment-order CAS a real compare-and-swap
    /// rather than a hopeful convention.
    ///
    /// It is a function, and there is one of it, because #485 is what happens otherwise: "who holds this?"
    /// computed in five places and agreeing in none.
    ///
    /// IT SORTS. It does not ASSUME its input is in id order, even though `markers` returns it that way.
    /// A rule that depends on an invariant it does not enforce is a rule with a silent failure mode — and
    /// this one's failure mode is that two racers each compute a different winner and both believe they
    /// hold the lock, which is the precise outcome the CAS exists to make impossible. Sorting here costs
    /// nothing on a list that is already sorted, and it closes the hole for every caller that is not
    /// `markers`.
    let winner (leaseMinutes: int) (markers: Marker list) =
        markers
        |> List.filter (isStale leaseMinutes >> not)
        |> List.sortBy (fun m -> m.Id)
        |> List.tryHead

    /// THE MARKER THAT HOLDS THE LOCK, REGARDLESS OF LEASE — the live CAS `winner` if there is one, else
    /// the lowest-id marker whose lease has lapsed.
    ///
    /// `winner` decides IDENTITY: only a live marker can answer a heartbeat or lose a CAS. This decides
    /// RESERVATION, and the two are not the same question. A lease is a clock; a lock is a lock, and it is
    /// broken only by `reap`, never by the clock running out (#461/#581, case 25). So the SCHEDULER must
    /// reserve a stale-but-unreaped claim's touch-set exactly as it reserves a live one — scheduling over
    /// it would hand a second worker the very tree its holder is standing in, the double-book this whole
    /// scheduler exists to prevent. This is the same choice `who` makes when it classifies a row Held (a
    /// live winner) or Stale (a lapsed marker still holding the lock).
    let reserver (leaseMinutes: int) (markers: Marker list) : Marker option =
        match winner leaseMinutes markers with
        | Some m -> Some m
        | None -> markers |> List.sortBy (fun m -> m.Id) |> List.tryHead

    let markerScan
        (transport: IGitHubTransport)
        (owner: string)
        (repo: string)
        (number: int)
        : IoResult<MarkerScan> =

        let subject = $"%s{owner}/%s{repo}#%d{number}"

        let request =
            { Method = "GET"
              Path = $"repos/%s{owner}/%s{repo}/issues/%d{number}/comments"
              Query = [ "per_page", "100" ]
              Body = NoBody
              Budget = Rest
              // NEVER CONDITIONAL. A 304 could serve a body captured before the marker was posted, and it
              // would report zero comments over a live lock. **A lock may never be read from a cache** —
              // going direct means there is no ETag to be stale.
              IfNoneMatch = None
              Subject = subject }

        match transport.Send request with
        | Error e -> Error e
        | Ok response ->
            match parse subject response.Body with
            | Error e -> Error e
            | Ok doc ->
                use doc = doc

                if doc.RootElement.ValueKind <> JsonValueKind.Array then
                    Error(Malformed(subject, "the comments response is not a JSON array"))
                else
                    let now = DateTimeOffset.UtcNow

                    let read =
                        doc.RootElement.EnumerateArray()
                        |> Seq.mapi (fun index comment -> readComment now index comment)
                        |> List.ofSeq

                    let found =
                        read
                        |> List.choose (function
                            | IsMarker m -> Some m
                            | NotAMarker
                            | Unclassifiable _ -> None)
                        // LOWEST COMMENT ID FIRST. This is the CAS's total order and the winner is the
                        // head. Sorting here — once, in the one place markers are read — is what stops a
                        // caller inventing its own idea of who won.
                        |> List.sortBy (fun m -> m.Id)

                    // EVERY COMMENT WE COULD NOT CLASSIFY, CARRIED OUT WITH THE ANSWER (.github#1668).
                    // This is the whole repair: the marker list is now accompanied by the reason it might
                    // be SHORT. A caller that reports "no claim here" while this list is non-empty is
                    // reporting a lower bound as a fact — which is what `who` did, and what it no longer
                    // does. Note the pairing: `[]` markers with `[]` unreadable is a real, complete
                    // observation of an unclaimed item, and it stays exactly that.
                    let unreadable =
                        read
                        |> List.choose (function
                            | Unclassifiable reason -> Some reason
                            | IsMarker _
                            | NotAMarker -> None)

                    Ok { Markers = found; Unreadable = unreadable }

    /// REQUIRE A COMPLETE LOCK READ before a caller decides or writes from it (.github#1896).
    ///
    /// `markerScan` deliberately preserves a lower bound for `who`, which can report the honest
    /// `Undetermined` verdict. The scheduler and the CAS have no safe partial answer: reserving only the
    /// markers they happened to parse can double-book a touch-set, and posting against that lower bound can
    /// double-hold the lock. They pass their scan through this gate and a single unclassifiable comment
    /// refuses the whole operation.
    ///
    /// This is separate from `markerScan`, rather than making that read itself fail, because the lower bound
    /// plus its provenance is useful to a reporting caller. There is deliberately no projection that returns
    /// `scan.Markers` alone: adding a new decision caller now requires choosing this gate or explicitly
    /// handling `Unreadable`, so the old fail-open cannot be reached by accident.
    let requireCompleteMarkerScan (subject: string) (scan: MarkerScan) : IoResult<Marker list> =
        match scan.Unreadable with
        | [] -> Ok scan.Markers
        | unreadable ->
            let reasons = String.concat "; " unreadable

            Error(
                Malformed(
                    subject,
                    $"the claim-marker scan is incomplete: %s{reasons}. Refusing to decide the lock from a lower bound."
                )
            )

    // ---- worker-to-worker messages (the `say` / `inbox` channel) ----------------------------------

    type Message =
        { Id: int64
          From: string
          To: string
          At: string
          Text: string }

    /// ANCHORED, exactly like `markerRe`. Un-anchor it and a `say` message whose TEXT merely quotes a
    /// message marker would be delivered as though it were one — the same forgery `markerRe` refuses.
    let private msgRe = Regex(@"^<!--\s*fsgg:msg\s", RegexOptions.Compiled)

    let private msgFromToRe =
        Regex(@"^<!--\s*fsgg:msg\s+from=(?<f>[^\s>]+)\s+to=(?<t>[^\s>]+)", RegexOptions.Compiled)

    // The rendered body is `<!-- fsgg:msg … -->\n**from → to**\n\n<text>`. Peel the marker comment and the
    // `**from → to**` header off the front, then trim the trailing newline, leaving the message the worker
    // wrote — the same two `sub`s bash's `parse_msgs` applies.
    let private msgCommentRe = Regex(@"^<!--[^>]*-->\s*", RegexOptions.Compiled)
    let private msgHeaderRe = Regex(@"^\*\*[^*]*\*\*\s*", RegexOptions.Compiled)

    let private parseMessage (comment: JsonElement) : Message option =
        match str comment "body" with
        | None -> None
        | Some body when not (msgRe.IsMatch body) -> None
        | Some body ->

        let id =
            match comment.TryGetProperty "id" with
            | true, v when v.ValueKind = JsonValueKind.Number -> Some(v.GetInt64())
            | _ -> None

        match id with
        // A message with no orderable id cannot be paged past — the inbox cursor is keyed on the id. Drop
        // it rather than let a null reset every reader's high-water mark. A message is NOT a lock, so the
        // safe failure here is to lose one message, never (as with a marker) to read an item as free.
        | None -> None
        | Some id ->

        let m = msgFromToRe.Match body

        if not m.Success then
            // A half-written `fsgg:msg` names no correspondent. Deliver it to nobody rather than guess a
            // recipient — again, a message is not a lock: dropping it is safe where broadcasting is not.
            None
        else
            let text =
                let noComment = msgCommentRe.Replace(body, "")
                let noHeader = msgHeaderRe.Replace(noComment, "")
                noHeader.TrimEnd()

            Some
                { Id = id
                  From = m.Groups.["f"].Value
                  To = m.Groups.["t"].Value
                  At = (str comment "created_at" |> Option.defaultValue "")
                  Text = text }

    /// The worker-to-worker messages on an issue, in comment-id order (lowest first).
    ///
    /// A message is an `fsgg:msg` comment `say` posts; `inbox` reads them across every in-flight claim. Read
    /// UNCONDITIONALLY, exactly like `markers`: a 304 could serve a comments page captured before a `say`,
    /// and a lost message is a coordination failure even where it is not a lost lock — the same reason the
    /// claim scan never goes conditional.
    let messages
        (transport: IGitHubTransport)
        (owner: string)
        (repo: string)
        (number: int)
        : IoResult<Message list> =

        let subject = $"%s{owner}/%s{repo}#%d{number} messages"

        let request =
            { Method = "GET"
              Path = $"repos/%s{owner}/%s{repo}/issues/%d{number}/comments"
              Query = [ "per_page", "100" ]
              Body = NoBody
              Budget = Rest
              IfNoneMatch = None
              Subject = subject }

        match transport.Send request with
        | Error e -> Error e
        | Ok response ->
            match parse subject response.Body with
            | Error e -> Error e
            | Ok doc ->
                use doc = doc

                if doc.RootElement.ValueKind <> JsonValueKind.Array then
                    Error(Malformed(subject, "the comments response is not a JSON array"))
                else
                    let found =
                        doc.RootElement.EnumerateArray()
                        |> Seq.choose parseMessage
                        |> Seq.sortBy (fun m -> m.Id)
                        |> List.ofSeq

                    Ok found

    let commentBodies (transport: IGitHubTransport) (owner: string) (repo: string) (number: int) =
        let subject = $"%s{owner}/%s{repo}#%d{number} comments"
        let request = { Method = "GET"; Path = $"repos/%s{owner}/%s{repo}/issues/%d{number}/comments"; Query = [ "per_page", "100" ]; Body = NoBody; Budget = Rest; IfNoneMatch = None; Subject = subject }
        match transport.Send request with
        | Error e -> Error e
        | Ok response ->
            match parse subject response.Body with
            | Error e -> Error e
            | Ok doc ->
                use doc = doc
                if doc.RootElement.ValueKind <> JsonValueKind.Array then Error(Malformed(subject, "the comments response is not a JSON array"))
                else
                    doc.RootElement.EnumerateArray()
                    |> Seq.map (fun c -> match str c "body" with Some body -> Ok body | None -> Error(Malformed(subject, "a comment has no readable body")))
                    |> Seq.fold (fun state next -> Result.bind (fun xs -> Result.map (fun x -> x :: xs) next) state) (Ok [])
                    |> Result.map List.rev

    // ---- the issue body ---------------------------------------------------------------------------

    let issueBody
        (transport: IGitHubTransport)
        (owner: string)
        (repo: string)
        (number: int)
        : IoResult<string> =

        let subject = $"%s{owner}/%s{repo}#%d{number}"

        let request =
            { Method = "GET"
              Path = $"repos/%s{owner}/%s{repo}/issues/%d{number}"
              Query = []
              Body = NoBody
              Budget = Rest
              IfNoneMatch = None
              Subject = subject }

        match transport.Send request with
        | Error e -> Error e
        | Ok response ->
            match parse subject response.Body with
            | Error e -> Error e
            | Ok doc ->
                use doc = doc

                // AN ABSENT BODY IS AN EMPTY BODY, AND THAT IS FINE. GitHub returns `"body": null` for an
                // issue nobody wrote a description for, and that is a real, successfully-read fact: the
                // issue exists and declares nothing. `TouchSet.parse` will call it `Undeclared`, which is
                // the correct verdict. This is NOT the failed-read case — that one came back as an `Error`
                // above and never reaches here.
                match doc.RootElement.TryGetProperty "body" with
                | true, v when v.ValueKind = JsonValueKind.String -> Ok(v.GetString())
                | true, v when v.ValueKind = JsonValueKind.Null -> Ok ""
                | _ -> Ok ""

    /// A per-ref state read for reconcilers. Unlike an open-list membership test, this distinguishes a
    /// valid CLOSED off-board issue from a missing, unreadable, or pull-request ref; those latter cases
    /// remain `Error` and callers must retain their local queue entry.
    let issueState
        (transport: IGitHubTransport)
        (owner: string)
        (repo: string)
        (number: int)
        : IoResult<IssueState> =

        let subject = $"%s{owner}/%s{repo}#%d{number} state"
        let request =
            { Method = "GET"
              Path = $"repos/%s{owner}/%s{repo}/issues/%d{number}"
              Query = []
              Body = NoBody
              Budget = Rest
              IfNoneMatch = None
              Subject = subject }

        match transport.Send request with
        | Error e -> Error e
        | Ok response ->
            match parse subject response.Body with
            | Error e -> Error e
            | Ok doc ->
                use doc = doc
                match doc.RootElement.TryGetProperty "pull_request", doc.RootElement.TryGetProperty "state" with
                | (true, _), _ -> Error(Malformed(subject, "the ref names a pull request, not a queued issue"))
                | _, (true, state) when state.ValueKind = JsonValueKind.String ->
                    match state.GetString().ToUpperInvariant() with
                    | "OPEN" -> Ok IssueState.Open
                    | "CLOSED" -> Ok IssueState.Closed
                    | other -> Error(Malformed(subject, $"unknown issue state '%s{other}'"))
                | _ -> Error(Malformed(subject, "the issue response has no readable state"))

    // ---- blockers -------------------------------------------------------------------------------

    let blockerState
        (transport: IGitHubTransport)
        (owner: string)
        (repo: string)
        (number: int)
        : IoResult<BlockerState> =

        let subject = $"%s{owner}/%s{repo}#%d{number}"

        let request =
            { Method = "GET"
              // A PR IS AN ISSUE IN REST. This one endpoint serves both kinds and carries
              // `pull_request.merged_at` — so one cheap call answers "is it closed?" AND "was it merged?",
              // and it does it on the budget that is still alive when GraphQL is not.
              Path = $"repos/%s{owner}/%s{repo}/issues/%d{number}"
              Query = []
              Body = NoBody
              Budget = Rest
              IfNoneMatch = None
              Subject = subject }

        match transport.Send request with
        | Error(NotFound _) ->
            // THE SERVER SAID IT IS NOT THERE. That is not "I could not look" — it is a successful read
            // with a definite answer, and the answer is that this ref points at nothing. It BLOCKS, because
            // a dependency on something that does not exist is not a dependency that has been satisfied.
            Ok BlockerUnknown

        | Error(RateLimited _ as e) ->
            // AN EXHAUSTED BUDGET IS NOT A FACT ABOUT THIS REF. It is a fact about the CLIENT, and the very
            // next resolution will fail identically — so degrading it to `BlockerUnknown` would mark EVERY
            // blocker unresolvable, report the whole board as blocked, and exit 0 with "nothing
            // schedulable". That is #534 (the budget-exhausted message swallowed, the worker told to
            // retry) wearing #421's clothes (a budget failure reported as a fact about an item), and it is
            // the exact failure this module exists to end — so it must be PROPAGATED, and the caller must
            // exit EX_RATE.
            Error e

        | Error _ ->
            // ANY OTHER FAILURE AND WE DID NOT LOOK AT *THIS ONE*. `BlockerUnknown` BLOCKS — "I could not
            // look" is not "I looked and it is fine" (#266, #421). The safe direction on a lock is always
            // to hold it.
            //
            // This one is deliberately NOT propagated, and the distinction from the arm above is the whole
            // point: a 502 on one issue is local to that issue, so the item it blocks stays blocked and
            // says so while every other item on the board is still schedulable. Failing the whole scan on
            // it would be fail-closed in the wrong place — one unreachable issue turning into a dead queue.
            Ok BlockerUnknown

        | Ok response ->
            match parse subject response.Body with
            | Error _ -> Ok BlockerUnknown
            | Ok doc ->
                use doc = doc
                let root = doc.RootElement

                let merged =
                    match root.TryGetProperty "pull_request" with
                    | true, pr when pr.ValueKind = JsonValueKind.Object ->
                        match pr.TryGetProperty "merged_at" with
                        | true, m -> m.ValueKind <> JsonValueKind.Null
                        | _ -> false
                    | _ -> false

                // MERGED FIRST. A merged PR is also a CLOSED one, so testing `state` first would collapse
                // the two — and that collapse IS #476: the gate opened when the blocking PR was abandoned
                // and shut forever once it was finished.
                if merged then
                    Ok BlockerMerged
                else
                    match str root "state" with
                    | Some "closed" -> Ok BlockerClosed
                    | Some "open" -> Ok BlockerOpen
                    | _ -> Ok BlockerUnknown

    // ---- proof of life ----------------------------------------------------------------------------

    /// Is there a pushed `item/<n>-*` branch on the remote? `prAlive` asks this only once it has found NO
    /// open PR, to tell a pushed-but-PR-less branch (proof of life, #1055) from a genuinely dead claim.
    ///
    /// It CANNOT reuse `branchTip`, which collapses "unreadable" and "absent" both to `None`: here that
    /// collapse is the #266 bug — "I could not ask" must NOT read as "no branch", or the same REST outage
    /// that expired the lease would license the reap. So this is three-valued: `Ok true`/`Ok false`, and an
    /// `Error` (rate-limited, propagated; anything else the caller maps to `LivenessUnknown`).
    let private itemBranchPushed
        (transport: IGitHubTransport)
        (owner: string)
        (repo: string)
        (number: int)
        : IoResult<bool> =

        let subject = $"%s{owner}/%s{repo} pushed item/%d{number}-* branches"

        let request =
            { Method = "GET"
              // matching-refs returns EVERY ref under the prefix (an empty array when none), so this asks
              // "does any `item/<n>-*` branch exist?" in one REST call, without guessing the slug. REST —
              // the budget the claim lock lives on — and paid only on the reap/who proof-of-life path.
              Path = $"repos/%s{owner}/%s{repo}/git/matching-refs/heads/item/%d{number}-"
              Query = []
              Body = NoBody
              Budget = Rest
              IfNoneMatch = None
              Subject = subject }

        match transport.Send request with
        | Error e -> Error e
        | Ok response ->
            match parse subject response.Body with
            | Error e -> Error e
            | Ok doc ->
                use doc = doc

                if doc.RootElement.ValueKind <> JsonValueKind.Array then
                    // A shape we did not expect is a read we could NOT make (#266), never "no branch".
                    Error(Malformed(subject, "the matching-refs response is not a JSON array"))
                else
                    Ok(doc.RootElement.GetArrayLength() > 0)

    let prAlive
        (transport: IGitHubTransport)
        (owner: string)
        (repo: string)
        (number: int)
        : IoResult<Liveness> =

        let subject = $"%s{owner}/%s{repo} open PRs for item #%d{number}"

        let request =
            { Method = "GET"
              Path = $"repos/%s{owner}/%s{repo}/pulls"
              Query = [ "state", "open"; "per_page", "100" ]
              Body = NoBody
              Budget = Rest
              IfNoneMatch = None
              Subject = subject }

        match transport.Send request with
        | Error(RateLimited _ as e) ->
            // PROPAGATED, for the same reason as `blockerState`: an exhausted budget is a fact about the
            // CLIENT, not about this item's PR. Swallowing it here would hide the one condition the caller
            // must back off on (EX_RATE), and `reap` would go on making liveness decisions from a read it
            // was never going to be able to make.
            Error e

        | Error _ ->
            // WE COULD NOT ASK ABOUT *THIS ITEM*. `LivenessUnknown` — NOT "no PR". This is the distinction
            // that stops a transient 5xx from reaping a worker who is visibly still working (#581).
            Ok LivenessUnknown

        | Ok response ->
            match parse subject response.Body with
            | Error _ -> Ok LivenessUnknown
            | Ok doc ->
                use doc = doc

                if doc.RootElement.ValueKind <> JsonValueKind.Array then
                    Ok LivenessUnknown
                else
                    let prefix = $"item/%d{number}-"

                    let alive =
                        doc.RootElement.EnumerateArray()
                        |> Seq.tryPick (fun pr ->
                            let headRef =
                                match pr.TryGetProperty "head" with
                                | true, h when h.ValueKind = JsonValueKind.Object -> str h "ref"
                                | _ -> None

                            match headRef with
                            | Some r when r.StartsWith prefix ->
                                match pr.TryGetProperty "number" with
                                | true, n when n.ValueKind = JsonValueKind.Number -> Some(n.GetInt32())
                                | _ -> None
                            | _ -> None)

                    match alive with
                    | Some pr -> Ok(LeaseExpiredPrOpen pr)
                    // NO OPEN PR — but §5 opens the PR only AFTER the work, so a worker in §3 has pushed a
                    // branch and no PR yet (#1055). Ask whether that branch exists before calling this dead:
                    // a pushed `item/<n>-*` branch is proof of life short of a PR. Fail closed — a branch
                    // probe we could not make is `LivenessUnknown`, never "no branch" (#266/#581), because
                    // the REST outage that expired the lease is the likely reason the probe fails too.
                    | None ->
                        match itemBranchPushed transport owner repo number with
                        | Error(RateLimited _ as e) -> Error e
                        | Error _ -> Ok LivenessUnknown
                        | Ok true -> Ok LeaseExpiredBranchPushed
                        | Ok false -> Ok LeaseExpiredNoPr

    let restId
        (transport: IGitHubTransport)
        (owner: string)
        (repo: string)
        (number: int)
        : IoResult<int64> =

        let subject = $"%s{owner}/%s{repo}#%d{number}"

        let request =
            { Method = "GET"
              Path = $"repos/%s{owner}/%s{repo}/issues/%d{number}"
              Query = []
              Body = NoBody
              Budget = Rest
              IfNoneMatch = None
              Subject = subject }

        match transport.Send request with
        | Error e -> Error e
        | Ok response ->
            match parse subject response.Body with
            | Error e -> Error e
            | Ok doc ->
                use doc = doc

                match doc.RootElement.TryGetProperty "id" with
                | true, v when v.ValueKind = JsonValueKind.Number -> Ok(v.GetInt64())
                | _ -> Error(Malformed(subject, "the issue response carried no numeric `id`"))

    /// The REST ids of an issue's EXISTING sub-issues (`issues/{n}/sub_issues`).
    ///
    /// FAILS CLOSED (#320): an unreadable list is an ERROR, never an empty one. `child` reads this to be
    /// idempotent — re-linking a child that is already attached is a no-op, not a 422 — and folding a
    /// failed read into "the edge is absent" would make it POST, collect a 422, and blame the token. An
    /// unreachable subject is not an absent one.
    let subIssueIds
        (transport: IGitHubTransport)
        (owner: string)
        (repo: string)
        (number: int)
        : IoResult<int64 list> =

        let subject = $"%s{owner}/%s{repo}#%d{number} sub-issues"

        let request =
            { Method = "GET"
              Path = $"repos/%s{owner}/%s{repo}/issues/%d{number}/sub_issues"
              Query = [ "per_page", "100" ]
              Body = NoBody
              Budget = Rest
              IfNoneMatch = None
              Subject = subject }

        match transport.Send request with
        | Error e -> Error e
        | Ok response ->
            match parse subject response.Body with
            | Error e -> Error e
            | Ok doc ->
                use doc = doc

                if doc.RootElement.ValueKind <> JsonValueKind.Array then
                    Error(Malformed(subject, "the sub-issues response is not a JSON array"))
                else
                    let ids =
                        doc.RootElement.EnumerateArray()
                        |> Seq.choose (fun i ->
                            match i.TryGetProperty "id" with
                            | true, v when v.ValueKind = JsonValueKind.Number -> Some(v.GetInt64())
                            | _ -> None)
                        |> List.ofSeq

                    Ok ids

    // ---- the sub-issue graph, with state + total (lint / rollup) -----------------------------------

    type SubIssue = { Ref: string; Open: bool }
    type SubIssueSet = { Total: int; Children: SubIssue list }

    [<Literal>]
    let private SubIssuesDoc =
        "query($owner: String!, $repo: String!, $number: Int!) { repository(owner: $owner, name: $repo) { issue(number: $number) { subIssues(first: 100) { totalCount nodes { number state repository { nameWithOwner } } } } } rateLimit { cost remaining } }"

    let subIssues
        (transport: IGitHubTransport)
        (owner: string)
        (repo: string)
        (number: int)
        : IoResult<SubIssueSet> =

        let subject = $"%s{owner}/%s{repo}#%d{number} sub-issue graph"

        let request =
            { Method = "POST"
              Path = "graphql"
              Query = []
              Body =
                Transport.Query(
                    SubIssuesDoc,
                    [ "owner", Transport.VString owner
                      "repo", Transport.VString repo
                      "number", Transport.VNumber(double number) ]
                )
              Budget = GraphQl
              IfNoneMatch = None
              Subject = subject }

        match transport.Send request with
        | Error e -> Error e
        | Ok response ->
            match parse subject response.Body with
            | Error e -> Error e
            | Ok doc ->
                use doc = doc

                try
                    let subIssuesNode =
                        doc.RootElement
                            .GetProperty("data")
                            .GetProperty("repository")
                            .GetProperty("issue")
                            .GetProperty("subIssues")

                    let total = subIssuesNode.GetProperty("totalCount").GetInt32()

                    let children =
                        subIssuesNode.GetProperty("nodes").EnumerateArray()
                        |> Seq.choose (fun n ->
                            let nwo =
                                match n.TryGetProperty "repository" with
                                | true, r when r.ValueKind = JsonValueKind.Object -> str r "nameWithOwner"
                                | _ -> None

                            let num =
                                match n.TryGetProperty "number" with
                                | true, v when v.ValueKind = JsonValueKind.Number -> Some(v.GetInt32())
                                | _ -> None

                            let isOpen =
                                match n.TryGetProperty "state" with
                                // GraphQL issue state is upper-case OPEN/CLOSED. Anything that is not
                                // exactly CLOSED is treated as still open — the conservative direction, so a
                                // rollup never flips over a child it could not read as closed.
                                | true, s when s.ValueKind = JsonValueKind.String -> s.GetString() <> "CLOSED"
                                | _ -> true

                            match nwo, num with
                            | Some nwo, Some num -> Some { Ref = $"%s{nwo}#%d{num}"; Open = isOpen }
                            | _ -> None)
                        |> List.ofSeq

                    Ok { Total = total; Children = children }
                with
                | :? System.Collections.Generic.KeyNotFoundException
                | :? System.NullReferenceException ->
                    Error(Malformed(subject, "the sub-issue graph response is missing `repository.issue.subIssues`"))

    let refIsPullRequest
        (transport: IGitHubTransport)
        (owner: string)
        (repo: string)
        (number: int)
        : IoResult<bool> =

        let subject = $"%s{owner}/%s{repo}#%d{number} (pull-request probe)"

        let request =
            { Method = "GET"
              Path = $"repos/%s{owner}/%s{repo}/issues/%d{number}"
              Query = []
              Body = NoBody
              Budget = Rest
              IfNoneMatch = None
              Subject = subject }

        match transport.Send request with
        | Error e -> Error e
        | Ok response ->
            match parse subject response.Body with
            | Error e -> Error e
            | Ok doc ->
                use doc = doc

                // The issues API returns a `pull_request` OBJECT iff the number is a PR. Its ABSENCE (or
                // null) is a plain issue.
                match doc.RootElement.TryGetProperty "pull_request" with
                | true, pr -> Ok(pr.ValueKind = JsonValueKind.Object)
                | _ -> Ok false

    // ---- the meter --------------------------------------------------------------------------------

    let rateLimit (transport: IGitHubTransport) : IoResult<RateLimitSnapshot> =
        let request =
            { Method = "GET"
              Path = "rate_limit"
              Query = []
              Body = NoBody
              // FREE. The meter read does not spend the meter, and it is billed to NEITHER counter. That is
              // what makes "back off until the reset" a strategy and not a guess — and the corpus depends
              // on it: bill this one and every GraphQL count delta it asserts shifts by one.
              Budget = Free
              IfNoneMatch = None
              Subject = "the rate limit" }

        match transport.Send request with
        | Error e -> Error e
        | Ok response ->
            match parse "the rate limit" response.Body with
            | Error e -> Error e
            | Ok doc ->
                use doc = doc

                let graphql =
                    match doc.RootElement.TryGetProperty "resources" with
                    | true, r ->
                        match r.TryGetProperty "graphql" with
                        | true, g when g.ValueKind = JsonValueKind.Object -> Some g
                        | _ -> None
                    | _ -> None

                match graphql with
                | None ->
                    Error(
                        Malformed(
                            "the rate limit",
                            "the meter response carries no `resources.graphql`. We cannot report a budget we did not read."
                        )
                    )
                | Some g ->
                    let intOf (name: string) =
                        match g.TryGetProperty name with
                        | true, v when v.ValueKind = JsonValueKind.Number -> Some(v.GetInt32())
                        | _ -> None

                    match intOf "remaining", intOf "limit" with
                    | Some remaining, Some limit -> Ok { Remaining = remaining; Limit = limit }
                    | _ ->
                        Error(
                            Malformed(
                                "the rate limit",
                                "the meter response is missing `remaining` or `limit`. Half a meter reported as a whole one is a confident number with nothing behind it."
                            )
                        )

    let prHeadRef
        (transport: IGitHubTransport)
        (owner: string)
        (repo: string)
        (pr: int)
        : IoResult<string> =

        let subject = $"%s{owner}/%s{repo} PR #%d{pr}"

        let request =
            { Method = "GET"
              Path = $"repos/%s{owner}/%s{repo}/pulls/%d{pr}"
              Query = []
              Body = NoBody
              Budget = Rest
              IfNoneMatch = None
              Subject = subject }

        match transport.Send request with
        | Error e -> Error e
        | Ok response ->
            match parse subject response.Body with
            | Error e -> Error e
            | Ok doc ->
                use doc = doc

                match doc.RootElement.TryGetProperty "head" with
                | true, head when head.ValueKind = JsonValueKind.Object ->
                    match str head "ref" with
                    | Some r -> Ok r
                    // #322: the API answered, but with no head ref we can name. That is a malformed answer,
                    // not "no branch" — refusing beats guessing which issue the PR implements.
                    | None -> Error(Malformed(subject, "the PR response carried no head.ref"))
                | _ -> Error(Malformed(subject, "the PR response carried no `head`"))

    /// The immutable commit identity for evidence that must bind to the reviewed PR revision.
    let prHeadSha
        (transport: IGitHubTransport)
        (owner: string)
        (repo: string)
        (pr: int)
        : IoResult<string> =

        let subject = $"%s{owner}/%s{repo} PR #%d{pr}"
        let request =
            { Method = "GET"; Path = $"repos/%s{owner}/%s{repo}/pulls/%d{pr}"; Query = []; Body = NoBody
              Budget = Rest; IfNoneMatch = None; Subject = subject }
        match transport.Send request with
        | Error e -> Error e
        | Ok response ->
            match parse subject response.Body with
            | Error e -> Error e
            | Ok doc ->
                use doc = doc
                match doc.RootElement.TryGetProperty "head" with
                | true, head when head.ValueKind = JsonValueKind.Object ->
                    match str head "sha" with
                    | Some sha when not (String.IsNullOrWhiteSpace sha) -> Ok sha
                    | _ -> Error(Malformed(subject, "the PR response carried no head.sha"))
                | _ -> Error(Malformed(subject, "the PR response carried no `head`"))

    let prFiles
        (transport: IGitHubTransport)
        (owner: string)
        (repo: string)
        (pr: int)
        : IoResult<string list> =

        let subject = $"%s{owner}/%s{repo} PR #%d{pr} files"

        let request =
            { Method = "GET"
              Path = $"repos/%s{owner}/%s{repo}/pulls/%d{pr}/files"
              Query = [ "per_page", "100" ]
              Body = NoBody
              Budget = Rest
              IfNoneMatch = None
              Subject = subject }

        match transport.Send request with
        | Error e -> Error e
        | Ok response ->
            match parse subject response.Body with
            | Error e -> Error e
            | Ok doc ->
                use doc = doc

                if doc.RootElement.ValueKind <> JsonValueKind.Array then
                    Error(Malformed(subject, "the PR files response is not a JSON array"))
                else
                    Ok(
                        doc.RootElement.EnumerateArray()
                        |> Seq.choose (fun f -> str f "filename")
                        |> List.ofSeq
                    )

    let prBody
        (transport: IGitHubTransport)
        (owner: string)
        (repo: string)
        (pr: int)
        : IoResult<string> =

        let subject = $"%s{owner}/%s{repo} PR #%d{pr} body"

        let request =
            { Method = "GET"
              Path = $"repos/%s{owner}/%s{repo}/pulls/%d{pr}"
              Query = []
              Body = NoBody
              Budget = Rest
              IfNoneMatch = None
              Subject = subject }

        match transport.Send request with
        | Error e -> Error e
        | Ok response ->
            match parse subject response.Body with
            | Error e -> Error e
            | Ok doc ->
                use doc = doc

                // `issueBody`'s exact rule: `"body": null` is a real, successfully-read fact (a PR nobody
                // described), not a failure — the failed-read case already returned `Error` above.
                match doc.RootElement.TryGetProperty "body" with
                | true, v when v.ValueKind = JsonValueKind.String -> Ok(v.GetString())
                | true, v when v.ValueKind = JsonValueKind.Null -> Ok ""
                | _ -> Ok ""

    /// A JSON element's `int64` property, or `None` — for `check_suite.id` / `check_suite_id`.
    let private int64Of (e: JsonElement) (name: string) =
        match e.TryGetProperty name with
        | true, v when v.ValueKind = JsonValueKind.Number -> Some(v.GetInt64())
        | _ -> None

    /// A JSON element's `int` property, or `None`.
    let private intOf (e: JsonElement) (name: string) =
        match e.TryGetProperty name with
        | true, v when v.ValueKind = JsonValueKind.Number -> Some(v.GetInt32())
        | _ -> None

    /// The `workflow_runs[]` on a head SHA, as `Landable.RunRow`s — or `None` if the read failed. `None` is
    /// distinct from `Some []` (no runs registered yet, a real observation the scorer reads as #606's empty
    /// set), so a failed read collapses to `PrUnknown` while an honest empty stays a finding.
    let private workflowRuns
        (transport: IGitHubTransport)
        (owner: string)
        (repo: string)
        (sha: string)
        : Landable.RunRow list option =

        let subject = $"%s{owner}/%s{repo} runs @ %s{sha}"

        // CONDITIONAL, AND THE HOTTEST READ IN THE TOOL: `landable --wait` polls this 30 times per wait, per
        // worker, on every item. A poll that finds no change is EXACTLY the 304 case, because "nothing has
        // changed yet" is what waiting MEANS. Keyed on the head SHA (it rides in the query), so one commit's
        // runs can never be served as another's.
        //
        // It is a PAGE of a collection, so it is memoised only with headroom — see `memoisable`. A commit's
        // runs are far short of 100 in practice, so this revalidates; if one ever crosses the page boundary
        // it silently reverts to paying in full, which is the safe direction.
        match
            conditionalGet
                transport
                subject
                $"repos/%s{owner}/%s{repo}/actions/runs"
                [ "head_sha", sha; "per_page", string CollectionPageSize ]
                (Page(CollectionPageSize, Some "workflow_runs"))
        with
        | Error _ -> None
        | Ok body ->
            match parse subject body with
            | Error _ -> None
            | Ok doc ->
                use doc = doc

                match doc.RootElement.TryGetProperty "workflow_runs" with
                | true, arr when arr.ValueKind = JsonValueKind.Array ->
                    arr.EnumerateArray()
                    |> Seq.map (fun r ->
                        let prNumbers =
                            match r.TryGetProperty "pull_requests" with
                            | true, prs when prs.ValueKind = JsonValueKind.Array ->
                                prs.EnumerateArray() |> Seq.choose (fun p -> intOf p "number") |> List.ofSeq
                            | _ -> []

                        ({ Path = str r "path" |> Option.defaultValue ""
                           Event = str r "event" |> Option.defaultValue ""
                           HeadBranch = str r "head_branch" |> Option.defaultValue ""
                           PrNumbers = prNumbers
                           RunNumber = intOf r "run_number" |> Option.defaultValue 0
                           Status = str r "status" |> Option.defaultValue ""
                           Conclusion = str r "conclusion"
                           CheckSuiteId = int64Of r "check_suite_id" }
                        : Landable.RunRow))
                    |> List.ofSeq
                    |> Some
                | _ -> None

    /// The `check_runs[]` on a head SHA, as `Landable.CheckRow`s — or `None` if the read failed.
    let private checkRuns
        (transport: IGitHubTransport)
        (owner: string)
        (repo: string)
        (sha: string)
        : Landable.CheckRow list option =

        let subject = $"%s{owner}/%s{repo} check-runs @ %s{sha}"

        // CONDITIONAL, for the same reason as `workflowRuns` — the other half of the same poll. The SHA is in
        // the PATH here rather than the query, which keys it just as soundly.
        match
            conditionalGet
                transport
                subject
                $"repos/%s{owner}/%s{repo}/commits/%s{sha}/check-runs"
                [ "per_page", string CollectionPageSize ]
                (Page(CollectionPageSize, Some "check_runs"))
        with
        | Error _ -> None
        | Ok body ->
            match parse subject body with
            | Error _ -> None
            | Ok doc ->
                use doc = doc

                match doc.RootElement.TryGetProperty "check_runs" with
                | true, arr when arr.ValueKind = JsonValueKind.Array ->
                    arr.EnumerateArray()
                    |> Seq.map (fun c ->
                        let suiteId =
                            match c.TryGetProperty "check_suite" with
                            | true, s when s.ValueKind = JsonValueKind.Object -> int64Of s "id"
                            | _ -> None

                        ({ Name = str c "name" |> Option.defaultValue ""
                           CheckSuiteId = suiteId
                           Status = str c "status" |> Option.defaultValue ""
                           Conclusion = str c "conclusion" }
                        : Landable.CheckRow))
                    |> List.ofSeq
                    |> Some
                | _ -> None

    /// `mergeable` as GitHub returns it: `true`/`false`, `null` while it COMPUTES lazily in a background
    /// job, or absent (a malformed/minimal PR response). `Computing` and `Absent` are held apart because
    /// only `Computing` is worth a re-read — an absent field will not appear on a second look.
    type private MergeState =
        | Mergeable of bool
        | Computing
        | Absent

    /// The delay between mergeability re-reads. GitHub computes `mergeable` in a BACKGROUND job and returns
    /// `null` until it lands, so a retry fired microseconds after the first read cannot have observed a job
    /// that had not finished — a zero-delay retry is a no-op dressed as diligence. Default ~1s (bash's), env
    /// so the test harness can drive the fixture's read-count flip without paying the wall-clock.
    let private mergeableRetryMs () =
        match Environment.GetEnvironmentVariable "FSGG_COORD_MERGEABLE_RETRY_MS" with
        | null
        | "" -> 1000
        | v ->
            match Int32.TryParse v with
            | true, n when n >= 0 -> n
            | _ -> 1000

    /// Everything ONE read of `pulls/{n}` tells the landable verdict, bound together so no two facts here
    /// can describe different PRs. `None` on any field is "unreadable or absent", never a guess.
    type private PrFacts =
        { Merge: MergeState
          /// The head commit the PR OBJECT names — eventually consistent, so not necessarily the branch's
          /// real tip (#955/#989/#995).
          HeadSha: string option
          /// The head BRANCH, which is what lets a stale `head.sha` be measured against that tip.
          HeadRef: string option
          /// The BASE branch — the branch whose policy decides whether GitHub will take this merge (#1575).
          /// Read from the same object as the rest, so a verdict can never be scored against one PR's head
          /// and another PR's base.
          BaseRef: string option
          /// `mergeable_state` — GITHUB'S OWN ANSWER to "will I take this merge?" (#1575), and the field
          /// that makes the whole guard below free. `mergeable` says only "does it apply cleanly"; this
          /// says whether the BASE BRANCH POLICY is satisfied — `clean`, `blocked` (a required context has
          /// not passed, or has not reported at all), `behind` (a strict base moved), `draft`, `unstable`
          /// (a NON-required check failed — GitHub still merges it), `dirty`, `unknown`.
          ///
          /// It is computed by the same lazy background job as `mergeable` and rides in the same object,
          /// so it costs NO extra request and needs NO extra token scope. Both matter: see the guard.
          MergeableState: string option
          /// IS THE PR STILL OPEN, AND DID IT MERGE? (#1680) — `state` and `merged`, from the same object
          /// as everything else here, so a verdict can never be scored against one PR's openness and
          /// another PR's checks.
          ///
          /// Free, which is the point: both ride in the `pulls/{n}` body this function already reads, so
          /// asking "is this PR merged?" costs no request, no pagination and no extra scope. That matters
          /// because the alternative — leaving merged-ness out of the domain — is what made the answer
          /// `pending`. GitHub reports `mergeable: null` and `mergeable_state: "unknown"` for a merged PR
          /// (measured on #1675: `state=closed merged=true mergeable=null`), so a reader that knows only
          /// about mergeability sees the SAME shape as a PR whose background job has not finished, and
          /// #950's arm maps that to `PrPending` — correctly, for the case it was written for, and
          /// catastrophically for this one. The distinction is not inferable from `mergeable`; it has to
          /// be READ. So it is read.
          ///
          /// `Merged` is NOT derived from `State = "closed"`: a closed PR may or may not have merged, and
          /// conflating them is exactly the collapse #1680 AC4 refuses. Both are carried.
          State: string option
          Merged: bool }

    /// Read a PR once: its mergeability state, head SHA, head branch ref, and base branch ref.
    let private prFacts
        (transport: IGitHubTransport)
        (owner: string)
        (repo: string)
        (pr: int)
        : PrFacts option =

        let subject = $"%s{owner}/%s{repo} PR #%d{pr} landable"

        // CONDITIONAL — the third read of the poll, and the one that 304s most: a PR object barely changes
        // while its CI runs, which is the whole duration of the wait. `pulls/{n}` is a single object, so it
        // never paginates and the validator always stands for the whole body.
        //
        // The lazy-`mergeable` re-read above still works: GitHub computes it in a background job, and when it
        // lands the PR object CHANGES — so the validator changes and we get the new body. A 304 means it has
        // NOT landed yet, which is precisely the `Computing` the retry is for.
        match conditionalGet transport subject $"repos/%s{owner}/%s{repo}/pulls/%d{pr}" [] Single with
        | Error _ -> None
        | Ok body ->
            match parse subject body with
            | Error _ -> None
            | Ok doc ->
                use doc = doc
                let root = doc.RootElement

                // `.mergeable` is `true` / `false` / `null` / absent. Read them all APART: `false` is
                // CONFLICTED, the one state we most need to name, and folding it into a fallback (the jq `//`
                // trap the corpus warns about) would report a conflict as `unknown`.
                let merge =
                    match root.TryGetProperty "mergeable" with
                    | true, v when v.ValueKind = JsonValueKind.True -> Mergeable true
                    | true, v when v.ValueKind = JsonValueKind.False -> Mergeable false
                    | true, v when v.ValueKind = JsonValueKind.Null -> Computing
                    | _ -> Absent

                // The head SHA and the BRANCH it names, read from the same object so they cannot disagree
                // about which PR they describe. The ref is what lets a `false` be checked against the branch's
                // real tip when the caller asserted no `--sha` of their own (#989).
                let sha, headRef =
                    match root.TryGetProperty "head" with
                    | true, h when h.ValueKind = JsonValueKind.Object -> str h "sha", str h "ref"
                    | _ -> None, None

                // The BASE branch, from the SAME object (#1575). Which branch a PR merges INTO is what
                // decides which required contexts govern it, and reading it here — rather than assuming
                // `main` — is what keeps the guard honest on a stacked or release-branch PR.
                let baseRef =
                    match root.TryGetProperty "base" with
                    | true, b when b.ValueKind = JsonValueKind.Object -> str b "ref"
                    | _ -> None

                // `merged` is a plain boolean on the PR object, and ABSENT/non-boolean reads as `false`
                // (#1680). Fail-closed in the direction that matters: a body we cannot read `merged` from
                // is treated as NOT merged, so the verdict falls through to the ordinary open-PR scoring
                // rather than claiming a merge that may not have happened. The reverse default would let a
                // malformed response manufacture "already landed", which is the one answer that tells a
                // recovery path to stamp an item nothing landed for.
                let merged =
                    match root.TryGetProperty "merged" with
                    | true, v when v.ValueKind = JsonValueKind.True -> true
                    | _ -> false

                Some
                    { Merge = merge
                      HeadSha = sha
                      HeadRef = headRef
                      BaseRef = baseRef
                      MergeableState = str root "mergeable_state"
                      State = str root "state"
                      Merged = merged }

    /// The BRANCH's real tip — the commit `refs/heads/{branch}` names right now, or `None` if it cannot be
    /// read. `None` is never "the branch is empty": an unreadable ref proves nothing, and the one caller
    /// (#989) keeps its existing verdict rather than acting on a fact it does not have.
    ///
    /// UNCONDITIONAL, deliberately, where every neighbouring read is conditional. The whole question this
    /// answers is "has the PR object caught up with the push YET?", so a 304 served from a validator stored
    /// microseconds ago would answer with the state the caller is trying to see PAST. Memoising the read that
    /// exists to detect staleness is how the detector inherits the staleness.
    let private branchTip
        (transport: IGitHubTransport)
        (owner: string)
        (repo: string)
        (branch: string)
        : string option =

        let subject = $"%s{owner}/%s{repo} branch %s{branch} tip"
        let path = $"repos/%s{owner}/%s{repo}/git/ref/heads/%s{Uri.EscapeDataString branch}"

        let request =
            { Method = "GET"
              Path = path
              Query = []
              Body = NoBody
              // REST — the budget the claim lock lives on (ADR-0034 §3). Stated here, at the call, because
              // this read is the whole COST of #989 and it must be visible where it is spent. It is paid
              // only on the `false`-with-no-`--sha` path: a conflicted verdict, never the green hot path.
              Budget = Rest
              IfNoneMatch = None
              Subject = subject }

        match transport.Send request with
        | Error _ -> None
        | Ok response ->
            match parse subject response.Body with
            | Error _ -> None
            | Ok doc ->
                use doc = doc

                match doc.RootElement.TryGetProperty "object" with
                | true, o when o.ValueKind = JsonValueKind.Object -> str o "sha"
                | _ -> None

    // ---- WHY the base branch refuses, when it does (#1575) -----------------------------------------
    //
    // THIS IS DIAGNOSIS, NOT VERDICT, AND THAT DISTINCTION IS THE WHOLE DESIGN. The verdict comes from
    // `mergeable_state` on the PR object we already read — GitHub's own answer, free, and needing no
    // token scope. What that field cannot do is say WHICH requirement is unmet, and "blocked" with no
    // thread to pull is the same dead end a bare `pending` was. So these reads NAME the contexts that
    // have not reported, and they are allowed to FAIL: an unreadable policy costs the operator a
    // sentence, never a verdict.
    //
    // IT MUST BE THAT WAY ROUND, AND #463 IS WHY. Reading `branches/{b}/protection` needs
    // `administration: read`, which **is not a valid `permissions:` scope for a workflow's
    // GITHUB_TOKEN** — declaring it is a validation error that kills the run at startup
    // (docs/coordination/reusable-workflow-contract.md). And `landable`'s own unattended caller,
    // `skill-registry-autofix.yml`, "runs entirely under GITHUB_TOKEN" by that file's own words. Make
    // the verdict depend on this read and that gate returns exit 4 forever: #463 exactly, where a
    // protection probe 403'd on every receiver, fell through to the fail-closed arm, and stopped the kit
    // landing anywhere. #463's ratified repair was to ask the PULL REQUEST instead — and that repair is
    // recorded as better on its merits, not merely cheaper, because the PR's own state accounts for
    // required reviews, a strict base, and unresolved conversations too. This follows it.
    //
    // So fail-closed is satisfied where it belongs — on the VERDICT, which is never green while GitHub
    // says it will refuse — and it is not smuggled onto a read the fleet's token cannot make. A gate
    // that fails closed on a question nobody can answer does not fail closed; it fails ALWAYS.

    /// The contexts a branch REQUIRES — or why we could not name them (#1575). `RequiredUnreadable` is a
    /// missing SENTENCE here, not a missing verdict.
    type private RequiredSet =
        | RequiredContexts of string list
        | RequiredUnreadable of string

    let private requiredCheckError (subject: string) (error: IoError) =
        $"could not read %s{subject} — %s{Errors.explain error}"

    /// Required contexts from CLASSIC branch protection.
    ///
    /// A 404 is an answer about THIS store — no classic protection on this branch — and says nothing
    /// about rulesets, which are read separately below. A 403 is not: it means "I may not look".
    let private classicRequired
        (transport: IGitHubTransport)
        (owner: string)
        (repo: string)
        (branch: string)
        : Result<string list, string> =

        let subject = $"%s{owner}/%s{repo} branch %s{branch} protection"

        let path =
            $"repos/%s{owner}/%s{repo}/branches/%s{Uri.EscapeDataString branch}/protection"

        // CONDITIONAL. Branch protection is the least volatile thing this tool reads, and it is read only
        // while a PR is blocked — a state `--wait` may poll for its whole budget. It is still ASKED every
        // poll (this is revalidation, not a TTL cache), so a policy change made mid-wait is seen at once;
        // a poll that finds no change costs a 304, which GitHub does not bill against the REST limit.
        match conditionalGet transport subject path [] Single with
        | Error(NotFound _) -> Ok []
        | Error(Unauthorized _) ->
            Error
                $"could not read %s{subject} — the token may not see it. Naming the unmet requirement needs `administration: read`, which a workflow's GITHUB_TOKEN cannot hold at all"
        | Error e -> Error(requiredCheckError subject e)
        | Ok body ->
            match parse subject body with
            | Error e -> Error(requiredCheckError subject e)
            | Ok doc ->
                use doc = doc

                // GUARDED, because `TryGetProperty` THROWS on a non-object. A 200 that parses but is not
                // an object is the proxy-error-page shape this module already has a test for, and a
                // diagnostic that crashes the process is worse than one that says nothing.
                if doc.RootElement.ValueKind <> JsonValueKind.Object then
                    Error $"could not read %s{subject} — the payload is not an object"
                else

                match doc.RootElement.TryGetProperty "required_status_checks" with
                | true, rsc when rsc.ValueKind = JsonValueKind.Object ->
                    // `checks` is the current shape (context + app_id); `contexts` is the legacy one. Read
                    // `checks` when it is there and fall back only when it is ABSENT — an empty `checks` is
                    // "requires nothing", not "look somewhere else".
                    let entries =
                        match rsc.TryGetProperty "checks" with
                        | true, checks when checks.ValueKind = JsonValueKind.Array ->
                            checks.EnumerateArray()
                            |> Seq.map (fun c ->
                                if c.ValueKind = JsonValueKind.Object then
                                    str c "context"
                                else
                                    None)
                            |> List.ofSeq
                            |> Some
                        | _ ->
                            match rsc.TryGetProperty "contexts" with
                            | true, arr when arr.ValueKind = JsonValueKind.Array ->
                                arr.EnumerateArray()
                                |> Seq.map (fun c ->
                                    if c.ValueKind = JsonValueKind.String then
                                        Some(c.GetString())
                                    else
                                        None)
                                |> List.ofSeq
                                |> Some
                            | _ -> None

                    match entries with
                    | None -> Ok []
                    // A required check we cannot NAME is one we decline to name. Dropping it silently
                    // would shorten the list from a payload we did not understand.
                    | Some list when list |> List.exists Option.isNone ->
                        Error
                            $"could not read %s{subject} — a required status check has no readable `context`"
                    | Some list -> list |> List.choose id |> List.filter (fun c -> c <> "") |> Ok
                // Protected, but not on status checks. A real answer.
                | _ -> Ok []

    /// Required contexts from RULESETS — the OTHER, entirely separate store. `branches/<b>/protection`
    /// does not report ruleset rules and `rules/branches/<b>` does not report classic protection; a branch
    /// may be governed by either, both, or neither, and GitHub enforces both (#574).
    ///
    /// A 404 here is NOT "no rules": this endpoint answers `[]` for a branch with no rules, so a 404 means
    /// "no such repo or branch".
    let private rulesetRequired
        (transport: IGitHubTransport)
        (owner: string)
        (repo: string)
        (branch: string)
        : Result<string list, string> =

        let subject = $"%s{owner}/%s{repo} branch %s{branch} rulesets"

        let path =
            $"repos/%s{owner}/%s{repo}/rules/branches/%s{Uri.EscapeDataString branch}"

        match
            conditionalGet transport subject path [ "per_page", string CollectionPageSize ] (Page(CollectionPageSize, None))
        with
        | Error(NotFound _) ->
            Error
                $"could not read %s{subject} — a branch with no rules answers `[]`, not 404, so this is 'no such repo or branch'"
        | Error e -> Error(requiredCheckError subject e)
        | Ok body ->
            match parse subject body with
            | Error e -> Error(requiredCheckError subject e)
            | Ok doc ->
                use doc = doc

                if doc.RootElement.ValueKind <> JsonValueKind.Array then
                    Error $"could not read %s{subject} — expected a list of rules"
                else
                    let contexts =
                        doc.RootElement.EnumerateArray()
                        |> Seq.filter (fun rule ->
                            rule.ValueKind = JsonValueKind.Object
                            && str rule "type" = Some "required_status_checks")
                        |> Seq.collect (fun rule ->
                            match rule.TryGetProperty "parameters" with
                            | true, p when p.ValueKind = JsonValueKind.Object ->
                                match p.TryGetProperty "required_status_checks" with
                                | true, arr when arr.ValueKind = JsonValueKind.Array ->
                                    arr.EnumerateArray()
                                    |> Seq.map (fun c ->
                                        if c.ValueKind = JsonValueKind.Object then
                                            str c "context"
                                        else
                                            None)
                                | _ -> Seq.empty
                            | _ -> Seq.empty)
                        |> List.ofSeq

                    if contexts |> List.exists Option.isNone then
                        Error
                            $"could not read %s{subject} — a `required_status_checks` rule names a check with no readable `context`"
                    else
                        contexts |> List.choose id |> List.filter (fun c -> c <> "") |> Ok

    /// Does GITHUB ITSELF say it will refuse this merge? `Some state` names the `mergeable_state` it said
    /// so with; `None` means it did not say so — including the case where it said NOTHING (see below).
    ///
    /// AN ALLOW-LIST OF REFUSALS, NOT A DENY-LIST OF PERMISSIONS, and that is the fail-safe direction here.
    /// GitHub may add a state tomorrow; an unknown state must not silently start refusing every merge in
    /// the fleet. The three listed are the states it documents as blocking:
    ///
    ///   * `blocked` — the base branch policy is not satisfied. THE #1575 STATE: a required context that
    ///     has not passed, or has not reported at all, or a required review, or an unresolved conversation.
    ///   * `behind`  — the base moved and the branch is required to be up to date (`strict`).
    ///   * `draft`   — a draft PR is not mergeable, whatever its checks say.
    ///
    /// NOT `unstable`: that is a NON-required check failing, which GitHub merges. (Our own rollup reds it
    /// first, which is a house rule stricter than GitHub's — deliberately, and unchanged here.)
    /// NOT `clean`, the merge path. NOT `dirty`/`unknown`, which never reach this arm — `mergeable` has
    /// already answered for them.
    let private refusedState (facts: PrFacts) : string option =
        match facts.MergeableState with
        | Some("blocked" | "behind" | "draft" as s) -> Some s
        | _ -> None

    /// The UNION of both stores. Short-circuits on the classic store's failure: a list we already cannot
    /// complete is not worth a second request.
    let private requiredContexts
        (transport: IGitHubTransport)
        (owner: string)
        (repo: string)
        (branch: string)
        : RequiredSet =

        match classicRequired transport owner repo branch with
        | Error why -> RequiredUnreadable why
        | Ok classic ->
            match rulesetRequired transport owner repo branch with
            | Error why -> RequiredUnreadable why
            | Ok rules -> RequiredContexts(List.distinct (classic @ rules))

    /// The landable verdict AND the number of subjects it was scored over (`Landable.scoreN`). The count is
    /// what `landable --wait` polls on: a `red` over ZERO subjects is "CI has not started yet", not "CI
    /// failed" (#606/#724), and it is 0 for every verdict reached before the runs are scored (conflicted,
    /// unknown). `prLandable` is this, with the count dropped.
    /// `prLandableN`, plus the two assertions a caller can add to it (#737):
    ///
    /// `required` — check-run names that must have REPORTED, threaded to `Landable.scoreRequired`.
    ///
    /// `expected` — the head SHA the caller believes it is gating. GitHub's PR object is EVENTUALLY
    /// CONSISTENT after a force-push: for a second or so `pulls/{n}` still names the PREVIOUS commit, whose
    /// checks are green and are not about the code that would be merged. A caller that has just pushed KNOWS
    /// the SHA it pushed (`git rev-parse HEAD`), so it can say so, and a disagreement scores `PrPending` —
    /// never green, never a verdict about the wrong commit. `pending` does not settle, so `--wait` simply
    /// waits for GitHub to catch up. Omit it and the PR's own head SHA is taken on trust, which is the right
    /// default for every caller that did not just push.
    ///
    /// Why a verdict that is not `red` is nonetheless not `green`. DIAGNOSTICS — the verdict is decided
    /// above, and nothing here changes it — but a bare `pending` is one honest word and no thread to pull,
    /// which is fine while the state is transient and useless on the case that never resolves.
    ///
    /// The three arms are held APART because their REMEDIES are opposite, and an operator handed one
    /// sentence for all three is sent to look at the wrong thing (#1575 AC2).
    type Unmet =
        /// An assertion the CALLER added (`--require NAME`, `--sha SHA`). Nobody but this caller is looking
        /// at it, and the base branch policy has no opinion about it.
        | Asserted of string
        /// GITHUB ITSELF WILL REFUSE THIS MERGE — its `mergeable_state` for the gated head, verbatim
        /// (`blocked`, `behind`, `draft`). Nobody asked for this one; it is a fact about the PR (#1575).
        | Refused of state: string * baseRef: string
        /// …and WHICH context the base branch requires that has no check run on this head. Diagnosis only:
        /// the verdict above already stands without it.
        | NotReported of context: string * baseRef: string
        /// The policy could not be read, so the refusal cannot be attributed to a named context. A missing
        /// SENTENCE, never a missing verdict — reading it needs a scope a workflow token cannot hold, and
        /// making the verdict depend on it is #463 (a probe that 403'd everywhere and stopped the kit
        /// landing at all).
        | PolicyUnreadable of string

    /// Returns the verdict, the subject count `--wait` settles on, and — for diagnostics only — every
    /// unmet reason, typed (`Unmet`). The verdict never depends on that list.
    let prLandableRequire
        (transport: IGitHubTransport)
        (owner: string)
        (repo: string)
        (pr: int)
        (required: string list)
        (expected: string option)
        : PrState * int * Unmet list =

        // THE LAZY RE-READ (#697). A null `mergeable` is neither "mergeable" nor "conflicted" — and it is the
        // NORMAL first answer for a PR GitHub has not yet tested. So a present `null` is re-read a bounded
        // number of times (3, ~1s apart, bash's budget); a `false` seen on the SECOND look is the conflict the
        // first read could not yet see, and adopting on the first `null` would land it. `Absent` is not
        // re-read — the field is not there to change — and a read that FAILED is `PrUnknown`.
        //
        // A `null` that OUTLIVES the budget is `PrPending`, not `PrUnknown` (#950): the budget is ~2s and a
        // background job is not, so exhausting it says "not yet", never "unreadable". The verdict below draws
        // that line; this re-read only decides how long to hold the question open before deferring it there.
        let rec readMerge (triesLeft: int) : PrFacts =
            match prFacts transport owner repo pr with
            | None ->
                { Merge = Absent
                  HeadSha = None
                  HeadRef = None
                  BaseRef = None
                  MergeableState = None
                  State = None
                  Merged = false }
            // A CLOSED PR IS NEVER RE-READ (#1680). Its `mergeable` is `null` and will stay `null` forever
            // — GitHub stops computing mergeability once a PR leaves `open` — so every try of this budget
            // is spent waiting for a background job that will never run again. That is the FIRST of the two
            // waits #1680 measured, and the quieter one: three PR reads and ~2s per `landable` call, paid
            // even by the single-shot form the issue timed at "instantly". The `--wait` loop's 600s is the
            // second, and `Landable.settled` closes that one; this closes this one. Both had to go, or
            // "`--wait` never polls a merged PR" would still have cost a caller two seconds per invocation.
            | Some facts when facts.State = Some "closed" -> facts
            | Some facts when facts.Merge = Computing && triesLeft > 1 ->
                let ms = mergeableRetryMs ()

                if ms > 0 then
                    System.Threading.Thread.Sleep ms

                readMerge (triesLeft - 1)
            | Some facts -> facts

        // THE HEAD-SHA RECONCILIATION, ONCE, AHEAD OF EVERY MERGEABLE VERDICT (#955).
        //
        // `mergeable` is a fact about the commit GitHub LAST EVALUATED, not about the commit you pushed. So
        // the question "is this verdict even about my PR?" is prior to the verdict itself, and it is the same
        // question whichever way `mergeable` came back. It must therefore be asked ONCE, before the arms —
        // not inside one of them.
        //
        // It used to live inside the `true` arm, and the `false` arm was ordered FIRST and bound the SHA to
        // `_`. That made the guard UNREACHABLE for a `false`, and `--sha` — the flag whose whole purpose is
        // "gate on THIS commit" — could not reach it either. The asymmetry was the bug in one line: a stale
        // `true` was SHA-checked and demoted to `pending`, while a stale `false` was trusted and PROMOTED to
        // a terminal verdict. It contradicted this function's own documented contract ("a disagreement scores
        // `PrPending` — never green, NEVER A VERDICT ABOUT THE WRONG COMMIT"), because `conflicted` is
        // precisely a verdict about the wrong commit.
        //
        // It fails closed, so nothing unsafe merged; the cost was to the worker. §2 branches from
        // `origin/main` and N workers merge while you work, so conflict-then-rebase IS §5's happy path — and
        // the recipe defines exit 3 as terminal, prescribing "a conflicted PR needs a rebase". Follow it
        // literally against a stale `false` and you rebase, push, poll, get `conflicted`, and rebase the
        // commit you just made: a loop whose exit condition is the thing it keeps destroying. The only escape
        // was to disbelieve the tool and read the API by hand — the exact lesson #698 says a gate that cries
        // wolf on the happy path teaches, and the one §5 cannot afford it to teach.
        //
        // Only a PROVEN disagreement demotes. `expected = None` (§5's own `landable <pr> --wait`) asserts
        // nothing, and a `Mergeable false` carrying no head SHA gives nothing to compare — both leave the
        // verdict exactly as it was, so this widens the guard's REACH without widening its CLAIM.

        // ONE read, bound once. `readMerge` spends the re-read budget (3 PR reads, ~1s apart), so asking it
        // twice would double every caller's reads to answer one question.
        let facts = readMerge 3

        // IS THERE ANYTHING TO GATE AT ALL? (#1680) — asked FIRST, ahead of every other question, because
        // it is prior to all of them. `landable` documents itself as "is this OPEN PR finished work?", and
        // every guard below — the head-SHA reconciliation, the stale-tip measurement, the mergeability
        // arms, the rollup — is a refinement of that question. None of them is meaningful for a PR that is
        // not open: there is no merge to be clean, no branch tip to have caught up with, and no check set
        // whose growth could settle. Asking them anyway is how the old code got here, and what it produced
        // was `pending` — "come back later" — for a state that cannot change.
        //
        // ORDER IS THE WHOLE FIX. Put this arm anywhere below and the `Computing` arm reaches a merged PR
        // first (GitHub nulls `mergeable` on merge), which is #1680 exactly. Put it here and the terminal
        // fact wins, as terminal facts must.
        //
        // MERGED AND CLOSED-UNMERGED ARE HELD APART (AC4), for the same reason `Computing` and `Absent` are
        // held apart above: they share a shape and differ in the act they call for. `merged` means the work
        // LANDED and, on the recovery path this command's caller is usually walking, must now be STAMPED.
        // `closed` means nothing landed and stamping it would record a lie. One word each, decided here.
        //
        // No `Unmet` reason for either, consistent with `PrConflicted`/`PrUnknown` below: that list carries
        // assertions the CALLER added (`--require`, `--sha`) and GitHub's own refusal, and its stderr banner
        // says so. Nobody asserted this. The verdict word carries it, and `Client.landable` speaks the
        // sentence.
        // ...WITH ONE THING STILL SAID. A caller who passed `--sha` asserted which commit they meant, and
        // this arm returns before the reconciliation below would have checked it. Dropping that silently
        // would be this issue's own defect in miniature — a verdict that is true while hiding the fact the
        // caller actually asked about — so a DISAGREEING `--sha` is reported beside the verdict. It does not
        // change the verdict: the PR really is merged, and that is the answer. It changes what the caller is
        // told about the commit they named, which on the merged arm is the difference between "your work
        // landed" and "something landed here, and it was not what you asked about".
        let assertedSha =
            match facts.HeadSha, expected with
            | Some sha, Some want when sha <> want ->
                [ Asserted
                      $"you asked about %s{want}, but this PR's head is %s{sha} — the merge that landed is not the commit you named" ]
            | _ -> []

        match facts.State, facts.Merged with
        | Some "closed", true -> PrMerged, 0, assertedSha
        | Some "closed", false -> PrClosed, 0, assertedSha
        | _ ->

        let staleHead =
            match facts.Merge, facts.HeadSha with
            | Mergeable _, Some sha when expected |> Option.exists (fun e -> e <> sha) -> Some sha
            | _ -> None

        match staleHead with
        | Some sha ->
            let want = defaultArg expected ""

            PrPending,
            0,
            [ Asserted
                  $"the PR still names head %s{sha}, not the %s{want} you asked to gate — GitHub has not caught up with the push (or --sha named a commit that is not this PR's head)" ]
        | None ->

        // Bound HERE, past the early return above, and matched on `expected = None` — so the read is spent on
        // exactly one path and no other. A caller who passed `--sha` has already been answered (agreeing, or
        // returned as stale above) and must not pay for a second opinion; `Computing`/`Absent`/`true` never
        // reach it at all. `Option.exists` is the fail-closed half: an unreadable tip is `None`, which is
        // `false`, which leaves the conflict standing.
        let staleAgainstBranch =
            match facts.Merge, facts.HeadSha, facts.HeadRef, expected with
            | Mergeable false, Some sha, Some headRef, None ->
                branchTip transport owner repo headRef |> Option.exists (fun tip -> tip <> sha)
            | _ -> false

        match facts.Merge, facts.HeadSha, facts.HeadRef with
        // A `false` NOBODY ASSERTED A SHA FOR — §5's own `landable <pr> --wait`, and the form every worker
        // runs (#989). #955 made the guard above reachable for a `false`; it is reachable only by a caller
        // that passes `--sha`, and the recipe passes none, so the repair could not fire on the path it was
        // for.
        //
        // Asking the RECIPE to pass `--sha "$(git rev-parse HEAD)"` was #955's own recommendation, and it
        // has a race #955 did not see: a bot pushes to your item branch (`lockfile-sync.yml` is a
        // `workflow_call` reusable ending in a bare push; `feed-autofix.yml` triggers `on: pull_request` and
        // force-pushes), your worktree HEAD is then NOT the PR's head, and the assertion fails through no
        // fault of yours — turning #955's TRANSIENT false `conflicted` into a PERMANENT false `pending`.
        // That is strictly worse: waiting is exactly what the recipe tells you to do about a 7.
        //
        // So ask GIT, not the caller. The push updated `refs/heads/{branch}` synchronously — that IS the
        // push — while the PR object is re-pointed by a background job, and this function's own contract has
        // recorded the consequence since `--sha` was built: "for a second or so `pulls/{n}` still names the
        // PREVIOUS commit". A tip that disagrees with `head.sha` is therefore not an opinion about what the
        // caller believes; it is GitHub disagreeing with itself, and it means precisely "has not caught up".
        // That is the fact #955 is about, MEASURED rather than asserted — and it is immune to the bot-push
        // race, because it never asks what the caller believes. It is also why #955's t1 read `conflicted`
        // at all: `--wait` returns a conflicted TERMINALLY on the first read, and the first read is exactly
        // the one inside that window.
        //
        // Only on this path. A caller who passed `--sha` was answered above at no cost, and the green path
        // never reaches here — so the extra REST read is paid on a conflicted verdict only, never on the
        // merge that follows a green one.
        //
        // FAIL-CLOSED both ways (#266). A tip we cannot READ proves nothing, so the conflict stands: an
        // unreadable ref must not manufacture a `pending` any more than it may manufacture a green. And an
        // AGREEING tip means the PR really does name the branch's real head, so its `false` is about the
        // code that would be merged — a real conflict, still terminal, still exit 3 on the first read.
        | Mergeable false, _, _ when staleAgainstBranch -> PrPending, 0, []
        | Mergeable false, _, _ -> PrConflicted, 0, []
        // `Computing` and `Absent` MUST NOT share a body (#950). Both mean "no mergeability here", but only
        // one of them can change: a `null` outliving the bounded re-read above is a background job that has
        // not landed yet — GUARANTEED transient — while an absent field will never appear. Collapsing them
        // made the transient one terminal, and `Landable.settled` settles `PrUnknown` at once, so `--wait`
        // returned exit 4 on a seconds-old PR without waiting at all — the one form §5 tells workers to run.
        // `PrPending` never settles, so `--wait` polls until GitHub answers; the 3-try budget is unchanged
        // for single-shot callers, who now get a retryable 7 instead of a fail-closed 4.
        //
        // No unmet REASON, deliberately, though there is an obvious one to give. That list is the channel for
        // assertions the CALLER added (`--require`, `--sha`) — its two producers are exactly those, and the
        // stderr it feeds says so in as many words ("These are assertions you asked for"). Nobody asked for
        // this one, so a reason here would print a true sentence under a false one. `PrConflicted`/`PrUnknown`
        // give none for the same reason.
        | Computing, _, _ -> PrPending, 0, []
        | Absent, _, _ -> PrUnknown, 0, []
        | Mergeable true, None, _ -> PrUnknown, 0, []
        // The head SHA is reconciled above for a caller that PASSED `--sha`. One that did not has asserted
        // nothing, so `sha` here is whatever the PR names — which, inside the force-push window, is the
        // commit they REPLACED. See the green guard below (#995).
        | Mergeable true, Some sha, headRef ->
            match workflowRuns transport owner repo sha, checkRuns transport owner repo sha with
            | Some runs, Some checks ->
                let state, n = Landable.scoreRequired required (Some true) runs checks

                let unmet =
                    Landable.missing required runs checks
                    |> List.map (fun name -> Asserted $"required check `%s{name}` has not reported")

                // DO NOT CALL THE REPLACED COMMIT'S GREEN OURS (#995, epic #266).
                //
                // #955 and #989 repaired this same stale head on the `false` arm, where it failed CLOSED —
                // a false `conflicted` that cost the worker an hour. HERE IT FAILS OPEN, and the cost is
                // untested code on `main`: `pulls/{n}` names the pre-rebase commit for a beat after a
                // force-push, its runs are COMPLETE and GREEN, and they are not about the code that would be
                // merged. This function's contract has said exactly that since `--sha` was built (#737) —
                // "whose checks are green and are not about the code that would be merged" — and named
                // `--sha` as the guard. §5 runs `landable <pr> --wait` and passes none, so the guard was
                // unreachable on the one path every worker runs. That is #989's gap, on the arm where being
                // wrong merges rather than stalls.
                //
                // Measured on PR #993 (#989's own evidence, re-read for what it says about `true`): the
                // instant the push returned, `ref-tip=NEW  pr.head=OLD  mergeable=true`. Score that and
                // `--wait` settles GREEN on its FIRST read, over the dead commit's checks.
                //
                // ONLY WHEN THE SCORE IS GREEN, and that is what makes this affordable rather than a tax on
                // every poll. `--wait` polls many times and stops at the first settling verdict, so the green
                // arm is reached ONCE per invocation — one REST request to know the checks we scored belong
                // to the commit that will merge. A red or pending never reads the ref: it is already not
                // merging. `expected = Some` never reaches it either — that caller was answered above, for
                // free, and asserted the SHA itself.
                //
                // FAIL-CLOSED (#266): a tip we cannot READ leaves the green alone. An unreadable ref proves
                // nothing, and manufacturing a `pending` from it would strand every caller whose ref read
                // 404s — the fix, failing open in the other direction.
                match state, headRef with
                | PrGreen, Some r when
                    expected.IsNone
                    && branchTip transport owner repo r |> Option.exists (fun tip -> tip <> sha)
                    ->
                    PrPending, 0, []
                // A REQUIRED CONTEXT THAT NEVER REPORTED IS NOT A PASSING ONE (#1575, epic #266).
                //
                // The rollup above asks "is anything red?", and that question is structurally blind to a
                // subject that is ABSENT — #606's lesson, which `--require` already closes for checks the
                // CALLER names. What it did not close is the set GitHub itself will hold the merge on. So
                // this command answered `green`, exit 0, for a PR GitHub then refused:
                //
                //     $ landable 1027 --repo FS.GG.Rendering --wait --sha 69367d92
                //     green
                //     $ gh pr merge 1027 --squash
                //     X ... the base branch policy prohibits the merge.
                //
                // `mergeable=MERGEABLE`, `mergeStateStatus=BLOCKED`, all 18 reporting check runs SUCCESS —
                // and the required context `skill-union / skill-union` had NO CHECK RUN AT ALL, because the
                // workflow that produces it was added to `main` AFTER this PR's head was pushed. A context
                // that never reports is not a context that fails, and nothing in an "is anything red?"
                // rollup can see the difference.
                //
                // THE FAILURE DIRECTION WAS THE SAFE ONE — GitHub still refused — so this never merged
                // anything bad. It is still a false verdict, and `landable` is the gate the whole worker
                // protocol keys on (`pnext-item` §5 says merge only on this word). A verdict of `green`
                // that GitHub refuses answers a different question from the one its caller asked.
                //
                // ASK THE PULL REQUEST, NOT THE BRANCH POLICY. #1575 proposed deriving the must-have-
                // reported set from `branches/{b}/protection` and comparing it to the head's check runs.
                // That read needs `administration: read`, which is NOT A VALID `permissions:` SCOPE for a
                // workflow's GITHUB_TOKEN at all — and `landable`'s own unattended caller,
                // `skill-registry-autofix.yml`, "runs entirely under GITHUB_TOKEN" in that file's own
                // words. A verdict resting on it would 403 there forever, which is #463 restored: a
                // protection probe that 403'd on every receiver, fell through to the fail-closed arm, and
                // stopped the kit landing anywhere. #463's ratified repair was to ask the PR instead, and
                // that is recorded as the better design on its merits.
                //
                // `mergeable_state` IS THAT ANSWER, and it is already in the object above — same lazy
                // background job as `mergeable`, same request, no extra scope, no new failure mode. It is
                // also strictly WIDER than the derived set: it accounts for a required context that never
                // reported, for one satisfied by a legacy commit STATUS rather than a check run, for
                // app-id mismatch, for a strict base the branch has fallen behind, for required reviews
                // and unresolved conversations. Every one of those refuses a merge this command was about
                // to call finished.
                //
                // ONLY THE STATES GITHUB ACTUALLY REFUSES. `unstable` is NOT among them — it means a
                // NON-required check failed, which GitHub merges (our own rollup reds it first anyway, and
                // that is a deliberate house rule, not GitHub's). `clean` is the merge path and must stay
                // exactly as fast as it was. An ABSENT field is NO OPINION, not a refusal: it is not a
                // permission-gated read but part of a body we already hold, so its absence means the
                // payload is not what github.com serves — and manufacturing a refusal from it would
                // strand every caller against a fixture or a GHES that omits it.
                //
                // `PrPending`, NOT `PrRed` — the same call `scoreRequired` makes for a missing required
                // check, for the same reason. "It has not reported" is literally the pending sentence, and
                // it is usually transient. `pending` never settles, so `--wait` rides out the transient
                // case and refuses the permanent one when its tries run out — the same no-merge, reached
                // honestly, and never a green.
                //
                // THE POLICY READ BELOW IS DIAGNOSIS AND MAY FAIL. It names WHICH context has not
                // reported, which is the difference between a diagnosis and a mystery — but the verdict is
                // already decided, so a 403 or a rate limit costs a sentence, not a merge. That is what
                // keeps the fail-closed rule on the question the fleet can answer.
                | PrGreen, _ when refusedState facts |> Option.isSome ->
                    let refusal = (refusedState facts).Value
                    let baseRef = defaultArg facts.BaseRef "the base branch"

                    let named =
                        match facts.BaseRef with
                        | None ->
                            [ PolicyUnreadable
                                  "the PR object named no base branch, so its policy could not be read to say which requirement is unmet" ]
                        | Some b ->
                            match requiredContexts transport owner repo b with
                            | RequiredUnreadable why -> [ PolicyUnreadable why ]
                            | RequiredContexts contexts ->
                                Landable.missing contexts runs checks
                                |> List.map (fun c -> NotReported(c, b))

                    PrPending, n, (Refused(refusal, baseRef) :: named) @ unmet
                | _ -> state, n, unmet
            | _ -> PrUnknown, 0, []

    let prLandableN
        (transport: IGitHubTransport)
        (owner: string)
        (repo: string)
        (pr: int)
        : PrState * int =
        let state, n, _ = prLandableRequire transport owner repo pr [] None
        state, n

    let prLandable
        (transport: IGitHubTransport)
        (owner: string)
        (repo: string)
        (pr: int)
        : PrState =
        prLandableN transport owner repo pr |> fst

    [<Literal>]
    let private ClosingRefDoc =
        "query($owner: String!, $repo: String!, $pr: Int!) { repository(owner: $owner, name: $repo) { pullRequest(number: $pr) { closingIssuesReferences(first: 5) { nodes { number repository { nameWithOwner } } } } } rateLimit { cost remaining } }"

    let prClosingRef
        (transport: IGitHubTransport)
        (owner: string)
        (repo: string)
        (pr: int)
        : IoResult<Ref option> =

        let subject = $"%s{owner}/%s{repo} PR #%d{pr} closing refs"

        let request =
            { Method = "POST"
              Path = "graphql"
              Query = []
              Body =
                Transport.Query(
                    ClosingRefDoc,
                    [ "owner", Transport.VString owner
                      "repo", Transport.VString repo
                      "pr", Transport.VNumber(double pr) ]
                )
              Budget = GraphQl
              IfNoneMatch = None
              Subject = subject }

        match transport.Send request with
        | Error e -> Error e
        | Ok response ->
            match parse subject response.Body with
            | Error e -> Error e
            | Ok doc ->
                use doc = doc

                try
                    let nodes =
                        doc.RootElement
                            .GetProperty("data")
                            .GetProperty("repository")
                            .GetProperty("pullRequest")
                            .GetProperty("closingIssuesReferences")
                            .GetProperty("nodes")

                    match nodes.EnumerateArray() |> Seq.tryHead with
                    | None -> Ok None
                    | Some n ->
                        let number =
                            match n.TryGetProperty "number" with
                            | true, v when v.ValueKind = JsonValueKind.Number -> Some(v.GetInt32())
                            | _ -> None

                        let nwo =
                            match n.TryGetProperty "repository" with
                            | true, r when r.ValueKind = JsonValueKind.Object -> str r "nameWithOwner"
                            | _ -> None

                        match number, nwo with
                        | Some num, Some nwo when nwo.Contains "/" ->
                            let parts = nwo.Split('/')

                            Ok(Some { Owner = parts.[0]; Repo = parts.[1]; Number = num })
                        | _ -> Ok None
                with
                | :? System.Collections.Generic.KeyNotFoundException
                | :? System.NullReferenceException -> Ok None

    // ---- the claim-scan candidate set --------------------------------------------------------------

    /// See Reads.fsi. `BodyRead ""` is a real empty body; `BodyUnread` is one nobody read.
    type IssueBodyRead =
        | BodyRead of body: string
        | BodyUnread of reason: string

    type OpenIssue = { Number: int; Body: IssueBodyRead }

    let openIssues
        (transport: IGitHubTransport)
        (owner: string)
        (repo: string)
        : IoResult<OpenIssue list> =

        let subject = $"%s{owner}/%s{repo} open issues"

        let request =
            { Method = "GET"
              Path = $"repos/%s{owner}/%s{repo}/issues"
              Query = [ "state", "open"; "per_page", "100" ]
              Body = NoBody
              Budget = Rest
              // UNCONDITIONAL, AND IT MATTERS. This is the set the claim scan runs over. A 304 serving a
              // body captured before a marker was posted would hide a live lock. The corpus asserts
              // `inm=none` on exactly this request.
              IfNoneMatch = None
              Subject = subject }

        match transport.Send request with
        | Error e -> Error e
        | Ok response ->
            match parse subject response.Body with
            | Error e -> Error e
            | Ok doc ->
                use doc = doc

                if doc.RootElement.ValueKind <> JsonValueKind.Array then
                    Error(Malformed(subject, "the issue-list response is not a JSON array"))
                else
                    // THE ARRAY'S OWN LENGTH, BEFORE ANY FILTERING. It is the denominator the refusal
                    // below quotes: "element 7 of 74" locates the anomaly in a payload an operator can go
                    // and look at, where "an element" alone does not (.github#1794 AC2).
                    let total = doc.RootElement.GetArrayLength()

                    // ONE ELEMENT, READ OR REFUSED. Returns `Ok None` for a pull request — a POSITIVE
                    // identification of a thing that is not an item of work (#641), which is why it is the
                    // one drop this read still makes silently.
                    let readOne (index: int) (i: JsonElement) : IoResult<OpenIssue option> =
                        // A PULL REQUEST IS AN ISSUE IN REST, and it is not an item of work. #641 is
                        // exactly this: `fsgg-coord issues` listed PRs as issues, so the duplicate-check
                        // read a PR as "already filed" and silently suppressed a real finding.
                        match i.TryGetProperty "pull_request" with
                        | true, _ -> Ok None
                        | _ ->

                        match i.TryGetProperty "number" with
                        // AN ELEMENT NOBODY CAN NAME REFUSES THE WHOLE READ (.github#1794). It cannot be
                        // carried as an unreadable ENTRY the way a marker scan is keyed
                        // on the number, so there is no lock to look up, no ref to report, and nothing a
                        // caller could fail closed *about*. Dropping it was the fail-open — the issue
                        // vanished from the claim scan, its lock reserved nothing, and nothing anywhere
                        // said an element had been discarded. #266: never "I looked and it was fine".
                        | true, n when n.ValueKind <> JsonValueKind.Number ->
                            Error(
                                Malformed(
                                    subject,
                                    $"element %d{index} of %d{total} has a `number` that is not a number (%A{n.ValueKind}) — an issue that cannot be identified cannot be scanned for a lock, and dropping it would report every claim on it as free"
                                )
                            )
                        | false, _ ->
                            Error(
                                Malformed(
                                    subject,
                                    $"element %d{index} of %d{total} carries no `number` — an issue that cannot be identified cannot be scanned for a lock, and dropping it would report every claim on it as free"
                                )
                            )
                        | true, n ->
                            let body =
                                match i.TryGetProperty "body" with
                                | true, b when b.ValueKind = JsonValueKind.String -> BodyRead(b.GetString())
                                // `"body": null` IS A SUCCESSFUL READ, AND IT STAYS ONE. GitHub serves null
                                // for an issue nobody wrote a description for; the issue exists and declares
                                // nothing, which is exactly what `TouchSet.parse ""` answers. The defect
                                // .github#1794 names is not this line — it is that the two lines below used
                                // to be this line.
                                | true, b when b.ValueKind = JsonValueKind.Null -> BodyRead ""
                                | true, b ->
                                    BodyUnread $"the `body` field is a %A{b.ValueKind}, not a string or null"
                                | false, _ -> BodyUnread "the element carries no `body` field"

                            Ok(Some { Number = n.GetInt32(); Body = body })

                    // Short-circuit on the first refusal. `List.fold` would read the rest of the array to
                    // no purpose, and an `Error` is an answer about the WHOLE read, not about one element.
                    let rec walk index (acc: OpenIssue list) (rest: JsonElement list) =
                        match rest with
                        | [] -> Ok(List.rev acc)
                        | i :: tail ->
                            match readOne index i with
                            | Error e -> Error e
                            | Ok None -> walk (index + 1) acc tail
                            | Ok(Some issue) -> walk (index + 1) (issue :: acc) tail

                    walk 0 [] (doc.RootElement.EnumerateArray() |> List.ofSeq)

    /// `issues` — a repo's issue list over REST, ETag-revalidated (#446/#418). THE budget-free read: a 304
    /// serves the cached body for zero cost, which is the whole reason the command exists — a worker reads
    /// issues WITHOUT spending GraphQL, so it never has to fall back to `gh issue list` (2 points a call).
    ///
    /// UNLIKE `openIssues`, this read IS conditional, and that is correct: its subject is the issue list a
    /// human/consumer asked for, not the lock. `openIssues` must never be served a 304 because a stale body
    /// could hide a claim marker (#461); here there is no marker to hide — a listing is a listing — so the
    /// ETag is pure budget savings. The cache key is the request AS A STRING (path + the query that shapes
    /// the result), so a different state or label is a different cache entry, exactly as bash slugs its path.
    ///
    /// **IT IS A PAGE OF A COLLECTION, AND IT DOES PAGINATE.** `docs/coordination/graphql-budget.md` says this
    /// command "asks for one page of 100", and that was true of the bash client — it is NOT true here:
    /// `Transport.Send` follows `Link: rel=next` and merges. So the ETag it stores is PAGE ONE'S, over a body
    /// that may be a merge, and it is memoised only under `memoisable`'s headroom rule. Without that, a repo
    /// whose open issues cross 100 would revalidate the whole list against its first page and 304 over
    /// everything past it — silently, and only once the repo got big enough. `FS-GG/.github` sits under 100
    /// today, which is why this has never bitten and why it would have bitten later.
    ///
    /// Returns the issue array as a JSON string — pull requests dropped (#641) — for the caller to jq.
    let issues
        (transport: IGitHubTransport)
        (owner: string)
        (repo: string)
        (state: string)
        (label: string option)
        (fresh: bool)
        : IoResult<string> =

        let query =
            [ "state", state; "per_page", string CollectionPageSize ]
            @ (match label with
               | Some l when l <> "" -> [ "labels", l ]
               | _ -> [])

        // The cache key is the full request string — the same fact bash slugs to name its body/etag files.
        let cacheKey =
            let qs = query |> List.map (fun (k, v) -> $"%s{k}=%s{v}") |> String.concat "&"
            $"repos/%s{owner}/%s{repo}/issues?%s{qs}"

        let subject = $"%s{owner}/%s{repo} issues"

        let request =
            { Method = "GET"
              Path = $"repos/%s{owner}/%s{repo}/issues"
              Query = query
              Body = NoBody
              Budget = Rest
              // CONDITIONAL BY DESIGN — the ETag is what makes the 304 free. `--refresh` (fresh) drops it,
              // forcing a full re-read when the caller wants to bypass the cache.
              IfNoneMatch = (if fresh then None else Cache.getETag cacheKey)
              Subject = subject }

        match transport.Send request with
        | Error e -> Error e
        | Ok response ->
            match response with
            // A 304 says "what you have is current" — serve it from the cache. A missing body here is OUR
            // protocol violation (a validator we could not honour), and `getBody` reports it as an error,
            // never an empty result. The cached body was validated on the 200 that stored it (below), so a
            // 304 does not re-parse it.
            | NotModified -> Cache.getBody cacheKey
            | _ ->
                // 200: a body we could not parse is a FAILED READ, never "this repo has no issues" — the same
                // #461 fail-closed rule the rest of this layer holds (a 200 carrying a proxy's HTML error or a
                // truncated page is not an empty listing). We validate it is a JSON ARRAY, drop pull requests
                // (#641), then cache and emit that filtered array. An empty-but-present `[]` is a real answer
                // and passes; garbage does not.
                match parse subject response.Body with
                | Error e -> Error e
                | Ok doc ->
                    use doc = doc

                    if doc.RootElement.ValueKind <> JsonValueKind.Array then
                        Error(Malformed(subject, "the issue-list response is not a JSON array"))
                    else
                        // #641 — A PULL REQUEST IS AN ISSUE IN REST, and `issues` must not list it: the §4
                        // duplicate-check reads a PR as "already filed" and silently suppresses a real finding.
                        // Drop every element that carries a `pull_request` key (the same predicate `openIssues`
                        // applies to the claim scan; here it guards the human/consumer listing), keeping each
                        // genuine issue's FULL object and the array shape the caller's jq expects. We cache and
                        // emit the FILTERED body, not the raw one, so a later 304 re-serves the same filtered
                        // array — the ETag is GitHub's (sent as If-None-Match); the body is only ever re-served
                        // to us, never back to GitHub, so storing the projection is correct.
                        let kept = Nodes.JsonArray()

                        for el in doc.RootElement.EnumerateArray() do
                            match el.TryGetProperty "pull_request" with
                            | true, _ -> ()
                            | _ -> kept.Add(Nodes.JsonNode.Parse(el.GetRawText()))

                        let filtered = kept.ToJsonString()

                        // THE VALIDATOR IS JUDGED ON THE RAW PAGE, THE BODY IS THE FILTERED ONE, AND THAT
                        // DISTINCTION IS THE WHOLE OF THIS. `memoisable` asks whether the PAGE had headroom —
                        // a question about what the server sent and what its ETag stands for — so it must see
                        // `response`, never the projection. Count the filtered array instead and a page of
                        // exactly 100 raw items that filters to 60 issues (40 PRs, #641) would "prove"
                        // headroom it does not have, and a later 304 would serve a one-page body for a
                        // two-page set. That is the #461 shape, laundered through our own filter.
                        let validator =
                            if memoisable (Page(CollectionPageSize, None)) response then
                                response.ETag
                            else
                                None

                        Cache.putBody cacheKey validator filtered
                        Ok filtered
