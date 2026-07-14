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

    /// Pull the reset instant out of a rate-limit response.
    ///
    /// GitHub sends `X-RateLimit-Reset` as a header, but a GraphQL 403 body can also carry `resetAt`. The
    /// caller passes whatever it has; a reset we cannot read yields `None`, and `Errors.explain` then says
    /// so rather than inventing a wait.
    let private readResetAt (body: string) =
        if String.IsNullOrWhiteSpace body then
            None
        else

        try
            use doc = JsonDocument.Parse body

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
                | _ -> None

            find doc.RootElement
        with :? JsonException ->
            None

    let classify (subject: string) (status: int) (body: string) =
        // ORDER IS THE CONTRACT. The rate-limit test runs FIRST, on every non-2xx, because a 403 is
        // ambiguous and the two readings have opposite remedies. Test permissions first and an exhausted
        // budget becomes "your token is wrong" — advice that is wrong, unactionable, and permanent.
        if isRateLimited body then
            RateLimited(readResetAt body)
        else
            match status with
            | 404 -> NotFound subject
            | 401
            | 403 -> Unauthorized subject
            | s -> Http(s, body)
