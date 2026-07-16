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
    /// mechanism with the same remedy (wait), and which `gh project` renders in its own words.
    ///
    /// The corpus injects two distinct wordings on purpose (`GH_RATELIMIT` 403s the GraphQL path and the
    /// `gh project` path differently) and asserts the client recognises BOTH. A classifier that matched
    /// only the first would leave every board write mis-classified as a permanent refusal — and a
    /// permanent refusal is never queued, so the write would be silently dropped. That is #510 arriving
    /// through the classifier instead of through the queue.
    let private rateLimitPattern =
        Regex(
            @"rate limit (already )?exceeded|API rate limit|secondary rate limit|was submitted too quickly|abuse detection",
            RegexOptions.IgnoreCase ||| RegexOptions.Compiled
        )

    let isRateLimited (body: string) =
        not (String.IsNullOrEmpty body) && rateLimitPattern.IsMatch body

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
    /// Every REST resource (`core`, `search`, `code_search`, …) is `RestBudget`: they are separate
    /// counters, but they share one remedy and one shape, and the distinction a worker needs is REST vs
    /// GraphQL — which of the two the protocol just lost.
    let private readResource (header: string -> string option) =
        match header "X-RateLimit-Resource" with
        | Some r when not (String.IsNullOrWhiteSpace r) ->
            if r.Trim().Equals("graphql", StringComparison.OrdinalIgnoreCase) then
                GraphQlBudget
            else
                RestBudget
        // NO HEADER, NO GUESS. A secondary/abuse-detector 403 carries no resource, and naming one here
        // would re-introduce the exact defect this function replaces — a confident budget name with
        // nothing behind it.
        | _ -> UnknownBudget

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
                | true, epoch -> Some(DateTimeOffset.FromUnixTimeSeconds epoch)
                // A header we cannot parse is not a reset of zero — 1970 would render as "retry now",
                // which is the one answer guaranteed to be wrong on a limit that just fired.
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
            RateLimited(readResource header, readReset header body)
        else
            match status with
            | 404 -> NotFound subject
            | 401
            | 403 -> Unauthorized subject
            | s -> Http(s, body)
