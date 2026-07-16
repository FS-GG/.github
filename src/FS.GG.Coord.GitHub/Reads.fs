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
          Raw: string }

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

    let private parseMarker (now: DateTimeOffset) (comment: JsonElement) : Marker option =
        match str comment "body" with
        | None -> None
        | Some body when not (markerRe.IsMatch body) -> None
        | Some body ->

        let id =
            match comment.TryGetProperty "id" with
            | true, v when v.ValueKind = JsonValueKind.Number -> Some(v.GetInt64())
            | _ -> None

        match id with
        // A COMMENT WITH NO ID IS NOT A MARKER WE CAN ORDER, and the id IS the lock — it is the total order
        // every racer observes. A marker we cannot place in that order cannot win or lose a race, so it
        // cannot be treated as a marker at all. Dropping it is safe only because the claim CAS re-reads and
        // re-checks; it can never be the thing that makes an item look FREE, because an unparseable marker
        // still matches `markerRe` and still blocks below.
        | None -> None
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

        Some
            { Id = id
              Worker = worker
              Session = session
              AgeSeconds = ageSeconds
              PreviousStatus = previousStatus
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

    let markers
        (transport: IGitHubTransport)
        (owner: string)
        (repo: string)
        (number: int)
        : IoResult<Marker list> =

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

                    let found =
                        doc.RootElement.EnumerateArray()
                        |> Seq.choose (parseMarker now)
                        // LOWEST COMMENT ID FIRST. This is the CAS's total order and the winner is the
                        // head. Sorting here — once, in the one place markers are read — is what stops a
                        // caller inventing its own idea of who won.
                        |> Seq.sortBy (fun m -> m.Id)
                        |> List.ofSeq

                    Ok found

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
                    // WE LOOKED, AND THERE IS NO OPEN PR ON THIS ITEM'S BRANCH. Now — and only now — is
                    // abandonment a reasonable reading. Note this says nothing about the LEASE: the caller
                    // owns that, and it must have found the lease lapsed before it asked this question at
                    // all.
                    | None -> Ok LeaseExpiredNoPr

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

        let request =
            { Method = "GET"
              Path = $"repos/%s{owner}/%s{repo}/actions/runs"
              Query = [ "head_sha", sha; "per_page", "100" ]
              Body = NoBody
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

        let request =
            { Method = "GET"
              Path = $"repos/%s{owner}/%s{repo}/commits/%s{sha}/check-runs"
              Query = [ "per_page", "100" ]
              Body = NoBody
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

                match doc.RootElement.TryGetProperty "check_runs" with
                | true, arr when arr.ValueKind = JsonValueKind.Array ->
                    arr.EnumerateArray()
                    |> Seq.map (fun c ->
                        let suiteId =
                            match c.TryGetProperty "check_suite" with
                            | true, s when s.ValueKind = JsonValueKind.Object -> int64Of s "id"
                            | _ -> None

                        ({ CheckSuiteId = suiteId
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

    /// Read a PR once: its mergeability state and head SHA (`None` if unreadable/absent).
    let private prMergeAndSha
        (transport: IGitHubTransport)
        (owner: string)
        (repo: string)
        (pr: int)
        : (MergeState * string option) option =

        let subject = $"%s{owner}/%s{repo} PR #%d{pr} landable"

        let request =
            { Method = "GET"
              Path = $"repos/%s{owner}/%s{repo}/pulls/%d{pr}"
              Query = []
              Body = NoBody
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

                let sha =
                    match root.TryGetProperty "head" with
                    | true, h when h.ValueKind = JsonValueKind.Object -> str h "sha"
                    | _ -> None

                Some(merge, sha)

    /// The landable verdict AND the number of subjects it was scored over (`Landable.scoreN`). The count is
    /// what `landable --wait` polls on: a `red` over ZERO subjects is "CI has not started yet", not "CI
    /// failed" (#606/#724), and it is 0 for every verdict reached before the runs are scored (conflicted,
    /// unknown). `prLandable` is this, with the count dropped.
    let prLandableN
        (transport: IGitHubTransport)
        (owner: string)
        (repo: string)
        (pr: int)
        : PrState * int =

        // THE LAZY RE-READ (#697). A null `mergeable` is UNKNOWN, not "mergeable" and not "conflicted" — and
        // it is the NORMAL first answer for a PR GitHub has not yet tested. So a present `null` is re-read a
        // bounded number of times (3, ~1s apart, bash's budget); a `false` seen on the SECOND look is the
        // conflict the first read could not yet see, and adopting on the first `null` would land it. `Absent`
        // is not re-read — the field is not there to change — and a read that FAILED is `PrUnknown`.
        let rec readMerge (triesLeft: int) : MergeState * string option =
            match prMergeAndSha transport owner repo pr with
            | None -> Absent, None
            | Some(Computing, sha) when triesLeft > 1 ->
                let ms = mergeableRetryMs ()

                if ms > 0 then
                    System.Threading.Thread.Sleep ms

                readMerge (triesLeft - 1)
            | Some result -> result

        match readMerge 3 with
        | Mergeable false, _ -> PrConflicted, 0
        | Computing, _
        | Absent, _ -> PrUnknown, 0
        | Mergeable true, None -> PrUnknown, 0
        | Mergeable true, Some sha ->
            match workflowRuns transport owner repo sha, checkRuns transport owner repo sha with
            | Some runs, Some checks -> Landable.scoreN (Some true) runs checks
            | _ -> PrUnknown, 0

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

    let openIssues
        (transport: IGitHubTransport)
        (owner: string)
        (repo: string)
        : IoResult<(int * string) list> =

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
                    let issues =
                        doc.RootElement.EnumerateArray()
                        // A PULL REQUEST IS AN ISSUE IN REST, and it is not an item of work. #641 is
                        // exactly this: `fsgg-coord issues` listed PRs as issues, so the duplicate-check
                        // read a PR as "already filed" and silently suppressed a real finding.
                        |> Seq.filter (fun i ->
                            match i.TryGetProperty "pull_request" with
                            | true, _ -> false
                            | _ -> true)
                        |> Seq.choose (fun i ->
                            match i.TryGetProperty "number" with
                            | true, n when n.ValueKind = JsonValueKind.Number ->
                                let body =
                                    match i.TryGetProperty "body" with
                                    | true, b when b.ValueKind = JsonValueKind.String -> b.GetString()
                                    | _ -> ""

                                Some(n.GetInt32(), body)
                            | _ -> None)
                        |> List.ofSeq

                    Ok issues

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
    /// Returns the raw JSON body — the array bash's `issues` prints. The caller projects it with jq.
    let issues
        (transport: IGitHubTransport)
        (owner: string)
        (repo: string)
        (state: string)
        (label: string option)
        (fresh: bool)
        : IoResult<string> =

        let query =
            [ "state", state; "per_page", "100" ]
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
                // truncated page is not an empty listing). We validate it is a JSON ARRAY, then emit the RAW
                // bytes bash's `issues` prints (so the caller's jq sees exactly the REST body), and cache the
                // body + its validator TOGETHER so the next read can revalidate. An empty-but-present `[]` is
                // a real answer and passes; garbage does not.
                match parse subject response.Body with
                | Error e -> Error e
                | Ok doc ->
                    use doc = doc

                    if doc.RootElement.ValueKind <> JsonValueKind.Array then
                        Error(Malformed(subject, "the issue-list response is not a JSON array"))
                    else
                        Cache.putBody cacheKey response.ETag response.Body
                        Ok response.Body
