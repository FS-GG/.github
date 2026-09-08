namespace FS.GG.Coord

open System

module TelemetryBudget =
    type Usability =
        | Unknown of reason: string
        | NotApplicable of reason: string
        | Usable of numerator: int64 * denominator: int64

    type Verdict =
        | UnknownVerdict of reason: string
        | NotApplicableVerdict of reason: string
        | Pass of numerator: int64 * denominator: int64
        | Breach of numerator: int64 * denominator: int64 * severe: bool

    type Interval = { StartNanoseconds: int64; EndNanoseconds: int64 }

    // BigInteger makes the exact cross-products immune to Int64 overflow. There is no
    // percentage rounding at either boundary: 10*n>d and 4*n>d are the definitions.
    let assess usability =
        match usability with
        | Unknown reason -> UnknownVerdict reason
        | NotApplicable reason -> NotApplicableVerdict reason
        | Usable(numerator, denominator) when numerator < 0L || denominator < 0L -> UnknownVerdict "negative-count"
        | Usable(0L, 0L) -> NotApplicableVerdict "known-zero-activity"
        | Usable(_, 0L) -> UnknownVerdict "missing-denominator"
        | Usable(numerator, denominator) ->
            let n, d = bigint numerator, bigint denominator
            if 10I * n > d then Breach(numerator, denominator, 4I * n > d)
            else Pass(numerator, denominator)

    let private merge intervals =
        intervals
        |> List.filter (fun interval -> interval.StartNanoseconds >= 0L && interval.EndNanoseconds >= interval.StartNanoseconds)
        |> List.sortBy _.StartNanoseconds
        |> List.fold (fun state next ->
            match state with
            | current :: tail when next.StartNanoseconds <= current.EndNanoseconds ->
                { current with EndNanoseconds = max current.EndNanoseconds next.EndNanoseconds } :: tail
            | _ -> next :: state) []
        |> List.rev

    let unionNanoseconds intervals =
        if List.isEmpty intervals then None
        else
            try
                merge intervals
                |> List.fold (fun total interval -> Checked.(+) total (interval.EndNanoseconds - interval.StartNanoseconds)) 0L
                |> Some
            with :? OverflowException -> None

    let subtractNanoseconds source excluded =
        match unionNanoseconds source with
        | None -> None
        | Some sourceTotal ->
            let intersections =
                [ for included in merge source do
                    for removed in merge excluded do
                        let startAt = max included.StartNanoseconds removed.StartNanoseconds
                        let endAt = min included.EndNanoseconds removed.EndNanoseconds
                        if endAt > startAt then yield { StartNanoseconds = startAt; EndNanoseconds = endAt } ]
            match unionNanoseconds intersections with
            | None when not intersections.IsEmpty -> None
            | excludedTotal -> Some(sourceTotal - Option.defaultValue 0L excludedTotal)

    let interventionDue distinctBreachingItems hasSevereBreach =
        distinctBreachingItems >= 15 || hasSevereBreach
