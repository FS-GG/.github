namespace FS.GG.Coord.GitHub

module Reads =

    open System
    open System.Text.Json
    open System.Text.RegularExpressions
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
    let private boardStatusOf (s: string) =
        match s.Trim().ToLowerInvariant() with
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
                boardStatusOf (decodeStatus p.Groups.["p"].Value)
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
