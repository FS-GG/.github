namespace FS.GG.Coord.GitHub

module Budget =

    open System
    open System.Collections.Generic
    open System.IO
    open System.Security.Cryptography
    open System.Text
    open System.Text.Json
    open System.Text.RegularExpressions
    open Errors

    type Meter = { Cost: int; Remaining: int }

    [<Literal>]
    let private UnixSecondsMin = -62135596800L

    [<Literal>]
    let private UnixSecondsMax = 253402300799L

    /// A reading from the response that actually served (or refused) a resource.  The ledger retains no
    /// credential: its filename is keyed by a one-way digest of the credential.
    type RestObservation =
        { Resource: string
          Limit: int option
          Remaining: int option
          Used: int option
          ResetAt: DateTimeOffset option
          ObservedAt: DateTimeOffset
          Source: string }

    let private observationRoot () =
        match Environment.GetEnvironmentVariable "FSGG_COORD_CACHE" with
        | null
        | "" ->
            match Environment.GetEnvironmentVariable "XDG_CACHE_HOME" with
            | null
            | "" -> Path.Combine(Environment.GetFolderPath Environment.SpecialFolder.UserProfile, ".cache", "fsgg-coord")
            | path -> Path.Combine(path, "fsgg-coord")
        | path -> path

    let private observationFile (token: string) =
        use sha = SHA256.Create()
        let key = sha.ComputeHash(Encoding.UTF8.GetBytes token) |> Convert.ToHexString |> fun x -> x.ToLowerInvariant()
        Path.Combine(observationRoot (), $"budget-%s{key}.json")

    let private intHeader (header: string -> string option) name =
        header name |> Option.bind (fun raw -> match Int32.TryParse(raw.Trim()) with | true, value when value >= 0 -> Some value | _ -> None)

    let private resetHeader (header: string -> string option) =
        header "X-RateLimit-Reset"
        |> Option.bind (fun raw -> match Int64.TryParse(raw.Trim()) with | true, epoch when epoch >= UnixSecondsMin && epoch <= UnixSecondsMax -> Some(DateTimeOffset.FromUnixTimeSeconds epoch) | _ -> None)

    /// Record rate-limit headers from a real REST resource.  The latest live observation wins; callers
    /// never manufacture a resource from a missing header.
    let observeRestHeaders (token: string) (header: string -> string option) =
        match header "X-RateLimit-Resource" with
        | None -> ()
        | Some resource when String.IsNullOrWhiteSpace resource -> ()
        | Some resource ->
            let observation =
                { Resource = resource.Trim()
                  Limit = intHeader header "X-RateLimit-Limit"
                  Remaining = intHeader header "X-RateLimit-Remaining"
                  Used = intHeader header "X-RateLimit-Used"
                  ResetAt = resetHeader header
                  ObservedAt = DateTimeOffset.UtcNow
                  Source = "response-header" }

            try
                Directory.CreateDirectory(observationRoot ()) |> ignore
                let file = observationFile token
                // A named mutex serialises independent CLI processes sharing this credential. Without it
                // two simultaneous resource responses both read the old ledger then each replace it,
                // silently dropping one resource — precisely the multi-worker information this ledger
                // exists to preserve.
                use gate = new Threading.Mutex(false, "fsgg-coord-budget-" + Path.GetFileNameWithoutExtension file)
                let held =
                    try gate.WaitOne(TimeSpan.FromSeconds 2.0)
                    with :? Threading.AbandonedMutexException -> true

                if held then
                    try
                        let temp = file + ".tmp." + string Environment.ProcessId + "." + Guid.NewGuid().ToString "N"
                        let existing =
                            if File.Exists file then
                                try JsonSerializer.Deserialize<RestObservation list>(File.ReadAllText file)
                                with :? JsonException -> []
                            else []

                        let next =
                            let prior = existing |> List.tryFind (fun value -> value.Resource.Equals(observation.Resource, StringComparison.OrdinalIgnoreCase))
                            // A late success from the same rate-limit window cannot erase a refusal that
                            // already named zero.  GitHub advances the reset window on genuine recovery;
                            // that later successful response is the only fact allowed to restore dispatch.
                            match prior with
                            | Some exhausted when exhausted.Remaining = Some 0 && observation.Remaining <> Some 0
                                                   && observation.ResetAt <= exhausted.ResetAt -> existing
                            | _ -> observation :: (existing |> List.filter (fun value -> not (value.Resource.Equals(observation.Resource, StringComparison.OrdinalIgnoreCase))))

                        File.WriteAllText(temp, JsonSerializer.Serialize next)
                        File.Move(temp, file, true)
                    finally
                        gate.ReleaseMutex()
            with :? IOException -> ()

    /// Read the one credential-scoped authoritative resource observation, if any. A torn or unreadable
    /// ledger is unknown rather than zero; dispatch must not turn a failed cache read into capacity.
    let readRestObservations (token: string) : RestObservation list =
        try
            let file = observationFile token
            if File.Exists file then
                JsonSerializer.Deserialize<RestObservation list>(File.ReadAllText file) |> Option.ofObj |> Option.defaultValue []
            else []
        with
        | :? IOException
        | :? JsonException -> []

    /// Compatibility projection for consumers that need a single most-conservative fact. New admission
    /// gates inspect `readRestObservations` so one healthy bucket cannot hide another exhausted one.
    let readRestObservation (token: string) : RestObservation option =
        readRestObservations token
        |> List.sortBy (fun observation -> observation.Remaining |> Option.defaultValue Int32.MaxValue)
        |> List.tryHead

    /// The floor kept for work that finishes an already accepted item.  This is intentionally
    /// configurable for a smaller installation, but never accepts zero or a malformed value: a
    /// misspelled environment setting must not silently remove the fleet's escape hatch.
    let dispatchReserve () =
        match Environment.GetEnvironmentVariable "FSGG_COORD_REST_DISPATCH_RESERVE" with
        | null
        | "" -> 100
        | raw ->
            match Int32.TryParse(raw.Trim()) with
            | true, value when value > 0 -> value
            | _ -> 100

    type FleetState =
        | Healthy
        | Constrained
        | Exhausted
        | Unknown

    /// The pessimistic fleet projection.  An exhausted resource always wins over a healthy
    /// sibling; capacity is permission to add load, not an average of unrelated buckets.
    let fleetState (observations: RestObservation list) =
        let remaining = observations |> List.choose _.Remaining

        if observations.IsEmpty || remaining.IsEmpty || remaining.Length <> observations.Length then
            Unknown
        elif remaining |> List.exists ((=) 0) then
            Exhausted
        elif remaining |> List.exists (fun value -> value < dispatchReserve ()) then
            Constrained
        else
            Healthy

    let fleetStateText state =
        match state with
        | Healthy -> "healthy"
        | Constrained -> "constrained"
        | Exhausted -> "exhausted"
        | Unknown -> "unknown"

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

    let ofGraphQlErrors (messages: string list) =
        if messages |> List.exists isSecondaryLimit then
            // NO RESOURCE IS NAMED, and that is honest rather than lossy: this arm has no headers to read,
            // and a secondary limit is account-wide anyway — it is not a property of the `graphql` bucket.
            Some(RateLimited(SecondaryLimit(None, None), None))
        elif messages |> List.exists isRateLimited then
            // `GraphQlBudget` is asserted, not read, and here that is sound: this arm parses a GraphQL
            // `errors` array, so the response IS a GraphQL response. The reset stays `None` — GitHub
            // reports this shape as HTTP 200 with no rate-limit headers, and the body carries no `resetAt`.
            Some(RateLimited(GraphQlBudget, None))
        else
            None

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

    // ---- what THIS invocation spent -------------------------------------------------------------------

    /// GraphQL points this process has spent, and how many billed calls it took.
    ///
    /// `readMeter` parsed `rateLimit { cost }` correctly, was unit-tested, and — until #2418 — had NO
    /// PRODUCTION CALLER. Fourteen query documents across four files select `rateLimit { cost remaining }`,
    /// so the fleet PAID to transmit a number that was then dropped on the floor. The visible consequence
    /// was not a wrong answer, it was the absence of one: when the budget died twice in a two-hour board
    /// run, nobody could say what had spent it, and reading the source could not answer either — two
    /// separate source-level hypotheses about the drain were wrong by 30x before the missing wiring was
    /// found. An unattributable budget is diagnosed by guessing.
    type Spend =
        { Points: int
          Calls: int
          /// The meter's own `remaining` from the LAST billed call — GitHub's number, never our arithmetic.
          LastRemaining: int option }

    let private spendGate = obj ()
    let mutable private spentPoints = 0
    let mutable private spentCalls = 0
    let mutable private lastRemaining: int option = None

    let private debugEnabled () =
        match Environment.GetEnvironmentVariable "FSGG_COORD_DEBUG" with
        | null
        | "" -> false
        | value -> not (value.Equals("0", StringComparison.Ordinal))

    /// Record one GraphQL response's meter.
    ///
    /// Called for every 2xx GraphQL body. A body with no `rateLimit` (every MUTATION — `rateLimit` is a
    /// field of the query root) reads as `None` and is NOT counted: a mutation's cost is real but the
    /// server does not report it here, and inventing the 1-point floor would make this ledger a mix of
    /// measured and assumed numbers. `Calls` therefore means BILLED CALLS THE METER SPOKE FOR, and the
    /// mutation gap is stated in `docs/coordination/graphql-budget.md` rather than papered over.
    let observeGraphQlBody (body: string) =
        match readMeter body with
        | None -> ()
        | Some meter ->
            lock spendGate (fun () ->
                spentPoints <- spentPoints + meter.Cost
                spentCalls <- spentCalls + 1
                lastRemaining <- Some meter.Remaining)

            // The recipe `docs/coordination/graphql-budget.md` has documented since it was written —
            // `FSGG_COORD_DEBUG=1 … | grep 'graphql cost='`. It named an environment variable that
            // existed in NO engine (the same shape as #883's autoflush myth: a documented affordance
            // nobody had implemented). This line is what makes that recipe true.
            if debugEnabled () then
                eprintfn "fsgg-coord-engine: graphql cost=%d remaining=%d" meter.Cost meter.Remaining

    /// What this process has spent so far.
    let graphQlSpend () : Spend =
        lock spendGate (fun () ->
            { Points = spentPoints
              Calls = spentCalls
              LastRemaining = lastRemaining })

    /// Reset the counter. Tests only — a process spends once and exits.
    let resetGraphQlSpend () =
        lock spendGate (fun () ->
            spentPoints <- 0
            spentCalls <- 0
            lastRemaining <- None)

    // ---- the cross-process ledger ---------------------------------------------------------------------

    /// One invocation's spend, appended when the process exits.
    ///
    /// A per-process counter alone cannot answer the question that matters, because the fleet is N SHORT-LIVED
    /// PROCESSES sharing one budget: `take`, `claim`, `done` and the host's own `reconcile` each run, spend,
    /// print, and die. "What drained the 5,000?" is a question ABOUT THE WINDOW, not about any one process, so
    /// the number has to outlive the process that measured it.
    type SpendRecord =
        { Command: string
          Points: int
          Calls: int
          Worker: string option
          ObservedAt: DateTimeOffset }

    let private spendLedgerFile () = Path.Combine(observationRoot (), "graphql-spend.jsonl")

    /// Append this invocation's spend. NEVER throws and never fails a command: telemetry that can break the
    /// tool it measures is worse than no telemetry. A zero-call invocation writes nothing — the overwhelming
    /// majority of commands touch no GraphQL at all, and a ledger padded with zeroes buries the rows that
    /// spent.
    let recordSpend (command: string) =
        try
            let spend = graphQlSpend ()

            if spend.Calls > 0 then
                let worker =
                    match Environment.GetEnvironmentVariable "FSGG_WORKER" with
                    | null
                    | "" -> None
                    | value -> Some value

                let line =
                    JsonSerializer.Serialize
                        {| command = command
                           points = spend.Points
                           calls = spend.Calls
                           worker = worker |> Option.toObj
                           observedAt = DateTimeOffset.UtcNow.ToString("o") |}

                let file = spendLedgerFile ()
                Directory.CreateDirectory(Path.GetDirectoryName file) |> ignore

                // O_APPEND on a line-sized write is atomic enough for concurrent workers; `FileShare.ReadWrite`
                // keeps a reader (`budget`) from refusing a writer mid-wave.
                use stream =
                    new FileStream(file, FileMode.Append, FileAccess.Write, FileShare.ReadWrite)

                use writer = new StreamWriter(stream, Encoding.UTF8)
                writer.WriteLine line
        with _ ->
            ()

    /// Read the ledger back, newest first, keeping only records inside `window`.
    ///
    /// Returns `[]` on any unreadable ledger rather than throwing — but note this is NOT the #266 shape it
    /// resembles. An empty ledger is not being reported as a clean board; it is reported by `budget` as
    /// "no attribution recorded", which is the honest reading of a file nobody has written yet.
    let recentSpend (window: TimeSpan) : SpendRecord list =
        try
            let file = spendLedgerFile ()

            if not (File.Exists file) then
                []
            else

            let cutoff = DateTimeOffset.UtcNow - window

            File.ReadAllLines file
            |> Array.toList
            |> List.choose (fun line ->
                if String.IsNullOrWhiteSpace line then
                    None
                else
                    try
                        use doc = JsonDocument.Parse line
                        let root = doc.RootElement

                        let str (name: string) =
                            match root.TryGetProperty name with
                            | true, v when v.ValueKind = JsonValueKind.String -> Some(v.GetString())
                            | _ -> None

                        let num (name: string) =
                            match root.TryGetProperty name with
                            | true, v when v.ValueKind = JsonValueKind.Number -> Some(v.GetInt32())
                            | _ -> None

                        match str "command", num "points", num "calls", str "observedAt" with
                        | Some command, Some points, Some calls, Some observedAt ->
                            match DateTimeOffset.TryParse(observedAt: string) with
                            | true, at when at >= cutoff ->
                                Some
                                    { Command = command
                                      Points = points
                                      Calls = calls
                                      Worker = str "worker"
                                      ObservedAt = at }
                            | _ -> None
                        | _ -> None
                    with :? JsonException ->
                        None)
            |> List.sortByDescending (fun r -> r.ObservedAt)
        with _ ->
            []

    /// Aggregate a window's records by command, dearest first. This is the attribution answer.
    let spendByCommand (records: SpendRecord list) : (string * int * int) list =
        records
        |> List.groupBy (fun r -> r.Command)
        |> List.map (fun (command, rows) ->
            command, rows |> List.sumBy (fun r -> r.Points), rows |> List.sumBy (fun r -> r.Calls))
        |> List.sortByDescending (fun (_, points, _) -> points)

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
                // The HTTP-date form — and EXACT formats only, never a loose `TryParse`.
                //
                // `DateTimeOffset.TryParse` accepts far more than an HTTP-date: `"23:59"` parses as *today
                // at 23:59* and would be handed to the worker as a ~20-hour delay attributed to GitHub —
                // fabricating precisely the confident wrong number this whole change exists to stop, in the
                // new code, one branch after the numeric arm rejects `999999999` for being unbelievable.
                // These are the three forms RFC 9110 §5.6.7 permits.
                let httpDateFormats =
                    [| "r"; "dddd, dd-MMM-yy HH:mm:ss 'GMT'"; "ddd MMM d HH:mm:ss yyyy" |]

                match
                    DateTimeOffset.TryParseExact(
                        raw,
                        httpDateFormats,
                        Globalization.CultureInfo.InvariantCulture,
                        Globalization.DateTimeStyles.AssumeUniversal
                        ||| Globalization.DateTimeStyles.AdjustToUniversal
                    )
                with
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
