namespace FS.GG.Coord.GitHub

module Budget =

    open System
    open System.Text.Json
    open System.Text.RegularExpressions
    open Errors

    type Meter = { Cost: int; Remaining: int }

    let cost (nodes: int) = max 1 (nodes / 100)

    [<Literal>]
    let WarnBelow = 500

    /// The wordings GitHub actually uses. All of them, because it uses all of them.
    ///
    /// `rate limit exceeded` / `API rate limit` — the primary budget.
    /// `secondary rate limit` / `submitted too quickly` — the abuse detector, which is a DIFFERENT
    /// mechanism with a DIFFERENT remedy, and which `gh project` renders in its own words.
    ///
    /// "with the same remedy (wait)" is what this line used to say, and #1666 is the measured refutation.
    /// Waiting is the primary limit's remedy. A secondary limit is triggered by burst CONCURRENCY, and a
    /// fleet that waits out a nominal window and resumes at the same fan-out re-trips it immediately —
    /// which is the loop that halted one six-worker run three times in a day. `isSecondaryLimit` keeps the
    /// two apart; this predicate deliberately still answers only "is it a rate limit at all", because that
    /// is the question the fail-closed ordering in `classify` needs.
    ///
    /// The corpus injects two distinct wordings on purpose (`GH_RATELIMIT` 403s the GraphQL path and the
    /// `gh project` path differently) and asserts the client recognises BOTH. A classifier that matched
    /// only the first would leave every board write mis-classified as a permanent refusal — and a
    /// permanent refusal is never queued, so the write would be silently dropped. That is #510 arriving
    /// through the classifier instead of through the queue.
    /// `DateTimeOffset.FromUnixTimeSeconds`'s documented domain — 0001-01-01 to 9999-12-31. Outside it the
    /// call THROWS rather than returning a sentinel, so the bound is checked before the conversion.
    [<Literal>]
    let private UnixSecondsMin = -62135596800L

    [<Literal>]
    let private UnixSecondsMax = 253402300799L

    let private rateLimitPattern =
        Regex(
            @"rate limit (already )?exceeded|API rate limit|secondary rate limit|was submitted too quickly|abuse detection",
            RegexOptions.IgnoreCase ||| RegexOptions.Compiled
        )

    /// The SECONDARY (abuse-detection) wordings, split out of the pattern above.
    ///
    /// These three phrases were always inside `rateLimitPattern`, matched, and then thrown away: the result
    /// was a bool, so "which of the five wordings hit" was information the classifier computed and
    /// discarded. `#1666` is the cost of discarding it — a secondary limit rendered as "REST budget
    /// EXHAUSTED … resets in ~6m", with the primary reset attached to a limit that does not use it.
    ///
    /// TESTED BEFORE THE PRIMARY, and the order is the contract. "You have exceeded a secondary rate limit"
    /// contains the word "exceeded" next to "rate limit", so a primary-first test can swallow it; and of the
    /// two possible mistakes, calling a primary limit "secondary" merely over-advises (reduce concurrency
    /// AND wait), while calling a secondary limit "primary" prints a reset that does not apply and sends the
    /// fleet back in at full concurrency the moment it elapses. Ties go to `Secondary`.
    let private secondaryPattern =
        Regex(
            @"secondary rate limit|was submitted too quickly|abuse detection",
            RegexOptions.IgnoreCase ||| RegexOptions.Compiled
        )

    let isRateLimited (body: string) =
        not (String.IsNullOrEmpty body) && rateLimitPattern.IsMatch body

    let isSecondaryLimit (body: string) =
        not (String.IsNullOrEmpty body) && secondaryPattern.IsMatch body

    let readMeter (body: string) =
        if String.IsNullOrWhiteSpace body then
            None
        else

        try
            use doc = JsonDocument.Parse body

            // `rateLimit` sits under `data` on a query response. A mutation has no query root, so it is
            // simply absent — and absent is not an error, it is the documented shape of a mutation.
            let root = doc.RootElement

            let rateLimit =
                match root.TryGetProperty "data" with
                | true, data ->
                    match data.TryGetProperty "rateLimit" with
                    | true, rl when rl.ValueKind = JsonValueKind.Object -> Some rl
                    | _ -> None
                | _ -> None

            match rateLimit with
            | None -> None
            | Some rl ->
                let intOf (name: string) =
                    match rl.TryGetProperty name with
                    | true, v when v.ValueKind = JsonValueKind.Number -> Some(v.GetInt32())
                    | _ -> None

                match intOf "cost", intOf "remaining" with
                | Some c, Some r -> Some { Cost = c; Remaining = r }
                // A `rateLimit` object missing either half is a document we do not understand. Reporting
                // half a meter as a whole one would be a confident number with nothing behind it.
                | _ -> None

        with :? JsonException ->
            // A body that does not parse is not a meter reading of zero. It is no reading at all, and the
            // caller's own malformed-body handling owns it — the meter does not get to invent a number
            // from bytes it could not read.
            None

    /// Pull the reset instant out of a rate-limit response BODY (a GraphQL `resetAt`).
    ///
    /// This is the FALLBACK. The header is the primary source and `readReset` below prefers it — this
    /// arm only answers for a GraphQL 403 whose body carries `resetAt`.
    let private readResetAtFromBody (body: string) =
        if String.IsNullOrWhiteSpace body then
            None
        else

        try
            use doc = JsonDocument.Parse body

            // ARRAYS ARE SEARCHED TOO, and that is not hypothetical tidying. This search only ever runs on
            // a GraphQL body, and a rate-limited GraphQL response nulls `data` and reports the failure in
            // `errors[]` — an ARRAY. Descending objects alone meant the one shape this fallback exists to
            // read was the one shape it could not reach, so it always returned `None` and the caller always
            // said "the reset time could not be read". A search that cannot see its own subject is #266
            // again, three levels down.
            let rec find (e: JsonElement) =
                match e.ValueKind with
                | JsonValueKind.Object ->
                    e.EnumerateObject()
                    |> Seq.tryPick (fun p ->
                        if p.Name = "resetAt" && p.Value.ValueKind = JsonValueKind.String then
                            match DateTimeOffset.TryParse(p.Value.GetString()) with
                            | true, at -> Some at
                            | _ -> None
                        else
                            find p.Value)
                | JsonValueKind.Array -> e.EnumerateArray() |> Seq.tryPick find
                | _ -> None

            find doc.RootElement
        with :? JsonException ->
            None

    /// WHICH budget did GitHub say this was? Read, never inferred.
    ///
    /// `X-RateLimit-Resource` rides on the 403 itself, so it names the bucket that ACTUALLY refused the
    /// call. That matters more than it sounds: on this account the free `/rate_limit` endpoint reports a
    /// `core` counter that DISAGREES with the one real requests are billed against (measured 2026-07-16 —
    /// `/rate_limit` said 2431/5000 remaining while every real read 403'd with `remaining: 0`, and the two
    /// even named different reset instants). The failing response's own headers are the only reading that
    /// is definitionally about the request that failed.
    ///
    /// The RAW resource name GitHub sent, trimmed — `core`, `search`, `code_search`, `graphql`, …
    ///
    /// `None` when the header is absent or blank. A secondary/abuse-detector 403 frequently carries none.
    let private readResourceName (header: string -> string option) =
        match header "X-RateLimit-Resource" with
        | Some r when not (String.IsNullOrWhiteSpace r) -> Some(r.Trim())
        | _ -> None

    /// `Retry-After` — the ONLY reset that describes a secondary limit, and nothing in this codebase read
    /// it until #1666.
    ///
    /// GitHub sends delta-SECONDS here (RFC 9110 also permits an HTTP-date, so that is accepted too and
    /// converted to a delta). Guarded exactly like `X-RateLimit-Reset` below, and for the same reason: this
    /// runs on the FAILURE path inside a transport `try` that catches only `HttpRequestException` and
    /// `TaskCanceledException`, so an unguarded conversion would escape and kill the process in the code
    /// whose whole job is to explain why nothing can run.
    ///
    /// A NEGATIVE or absurd value is NO reading, never a clamp to zero: "retry now" is the one answer
    /// guaranteed to be wrong on a limit that just fired. The upper bound is a day — GitHub's secondary
    /// back-offs are seconds to minutes, and a value beyond that is not a figure to hand a worker as fact.
    let private readRetryAfter (header: string -> string option) =
        match header "Retry-After" with
        | Some v when not (String.IsNullOrWhiteSpace v) ->
            let raw = v.Trim()

            match Int64.TryParse raw with
            | true, secs when secs > 0L && secs <= 86400L -> Some(TimeSpan.FromSeconds(float secs))
            | true, _ -> None
            | _ ->
                // The HTTP-date form. Converted to a delta against now, and subject to the same bounds.
                match DateTimeOffset.TryParse raw with
                | true, at ->
                    let delta = at - DateTimeOffset.UtcNow

                    if delta > TimeSpan.Zero && delta <= TimeSpan.FromDays 1.0 then
                        Some delta
                    else
                        None
                | _ -> None
        | _ -> None

    /// WHICH limit refused this call — the MECHANISM as well as the bucket.
    ///
    /// Every REST resource used to collapse into a bare `RestBudget` with its name discarded, on the
    /// reasoning that "they share one remedy and one shape". #1666 measured the cost of that: they do not.
    /// A `search` 403 announced itself as "REST budget EXHAUSTED", the operator checked `core`, saw 87%
    /// free, and concluded the engine had an internal counter — a diagnosis filed, and withdrawn, three
    /// separate times. The bucket name is carried now, so the reading is checkable.
    ///
    /// And a SECONDARY limit is not a budget at all. It is tested FIRST (see `secondaryPattern`) and never
    /// receives a `resetAt`, because `X-RateLimit-Reset` describes the primary window and attaching it here
    /// is what produced "resets in ~6m" in front of a retry that succeeded seconds later.
    let private readResource (header: string -> string option) (body: string) =
        let name = readResourceName header

        if isSecondaryLimit body then
            SecondaryLimit(name, readRetryAfter header)
        else
            match name with
            | Some r when r.Equals("graphql", StringComparison.OrdinalIgnoreCase) -> GraphQlBudget
            | Some r -> RestBudget(Some r)
            // NO HEADER, NO GUESS. Naming a budget here would re-introduce the exact defect this function
            // replaces — a confident budget name with nothing behind it.
            | None -> UnknownBudget

    /// The reset instant — the HEADER first, then a GraphQL body's `resetAt`.
    ///
    /// `X-RateLimit-Reset` is epoch SECONDS. It was sitting on every REST 403 the whole time and nothing
    /// ever read it, so a REST rate limit could only ever say "the reset time could not be read" — while
    /// `/pnext-item` §1 told the worker to "back off until the reset it names". The tool was structurally
    /// unable to name one.
    let private readReset (header: string -> string option) (body: string) =
        let fromHeader =
            match header "X-RateLimit-Reset" with
            | Some v ->
                match Int64.TryParse(v.Trim()) with
                // RANGE-CHECKED, because `FromUnixTimeSeconds` THROWS on an epoch outside
                // `DateTimeOffset`'s range — and an `Int64` that parses is not an epoch that converts.
                // This runs on the FAILURE path, inside a `try` that catches only `HttpRequestException`
                // and `TaskCanceledException`, so the exception would escape the transport and take the
                // process down. A garbage header would turn "back off for 3 minutes" into a crash: the
                // tool falling over precisely when it is trying to tell you why it cannot work.
                | true, epoch when epoch >= UnixSecondsMin && epoch <= UnixSecondsMax ->
                    Some(DateTimeOffset.FromUnixTimeSeconds epoch)
                // A header we cannot READ is not a reset of zero — 1970 would render as "retry now",
                // which is the one answer guaranteed to be wrong on a limit that just fired. Falls
                // through to the body, then to an honest "could not be read".
                | _ -> None
            | None -> None

        match fromHeader with
        | Some at -> Some at
        | None -> readResetAtFromBody body

    let classify (subject: string) (status: int) (body: string) (header: string -> string option) =
        // ORDER IS THE CONTRACT. The rate-limit test runs FIRST, on every non-2xx, because a 403 is
        // ambiguous and the two readings have opposite remedies. Test permissions first and an exhausted
        // budget becomes "your token is wrong" — advice that is wrong, unactionable, and permanent.
        if isRateLimited body then
            let resource = readResource header body

            // THE PRIMARY RESET IS WITHHELD FROM A SECONDARY LIMIT, and this is the line that does it.
            // `X-RateLimit-Reset` rides on a secondary 403 just as it does on a primary one — it is simply
            // about a DIFFERENT bucket, the one that has not been exhausted. Reading it here is what put
            // "The budget resets in ~6m" in front of a retry that succeeded within seconds, three times in
            // one run (#1666). `Retry-After` is that limit's own signal and `SecondaryLimit` carries it.
            let resetAt =
                match resource with
                | SecondaryLimit _ -> None
                | _ -> readReset header body

            RateLimited(resource, resetAt)
        else
            match status with
            | 404 -> NotFound subject
            | 401
            | 403 -> Unauthorized subject
            | s -> Http(s, body)
