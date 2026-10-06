namespace FS.GG.Coord

module ProcessEfficiency =
    type Fraction = { Numerator: bigint; Denominator: bigint }
    type Purpose =
        | DirectProduct | UsefulAssurance | NecessaryCoordination
        | ProcessImprovement | AvoidableProcess | UnknownPurpose
    type ResourceKey = { Identity: string; Dimension: string; Provider: string; AccountingScope: string }
    type Share = { ItemId: string; Purpose: Purpose; Fraction: Fraction }
    type Resource = { Key: ResourceKey; Amount: bigint; Shares: Share list }
    type Allocation =
        { Total: bigint; Allocated: Fraction; Unallocated: Fraction
          ByItem: Map<string, Fraction>; ByPurpose: Map<Purpose, Fraction> }
    type SourceRevision =
        { Identity: string; Revision: int64; ContentDigest: string; EffectiveItemId: string }
    type TimeSummary =
        { ElapsedNanoseconds: bigint; ObservedSpanNanoseconds: bigint
          TouchUnionNanoseconds: int64 option; WaitUnionNanoseconds: int64 option
          TouchWaitOverlapNanoseconds: int64 option; FlowRatio: Fraction option }
    type Attempt = { Identity: string; Ordinal: int; InputDigest: string; Usage: bigint }
    type Operation = { Identity: string; PopulationComplete: bool; Attempts: Attempt list }
    type RetrySummary =
        { ObservedUsage: bigint; KnownOperations: int; RetriedOperations: int
          AdditionalUsage: bigint; SameInputUsage: bigint; ChangedInputUsage: bigint; UnknownInputUsage: bigint
          Incidence: Fraction option; Burden: Fraction option }
    type NativePopulation =
        { ExpectedInvocations: Set<string> option; TerminalInvocations: Set<string>
          UsageInvocations: Set<string>; NativeEligible: bool }
    type PopulationSummary =
        { SourceDeliveries: int; NativeCompletions: int; ObservedUsage: bigint
          WholeItemUsage: bigint option; AssessmentScope: string }


    type ItemState = Accepted | Abandoned | Open | Unsuccessful
    type FirstPassItem = { Identity: string; PopulationComplete: bool; State: ItemState; AcceptedAttempt: int option }
    type FirstPassSummary =
        { EligibleItems: int; FirstPassItems: int; Rate: Fraction option
          ExcludedItems: int; OpenItems: int; AbandonedItems: int }
    type Freshness =
        { SourceAgeSeconds: decimal option; IngestionAgeSeconds: decimal option
          PublicationAgeSeconds: decimal option; ProducerCoverage: Fraction option }

    let private normalized numerator denominator =
        let divisor = System.Numerics.BigInteger.GreatestCommonDivisor(numerator, denominator)
        { Numerator = numerator / divisor; Denominator = denominator / divisor }

    let fraction numerator denominator =
        if numerator < 0I || denominator <= 0I then Error "invalid-fraction"
        else Ok(normalized numerator denominator)

    let private zero = { Numerator = 0I; Denominator = 1I }
    let private add (left: Fraction) (right: Fraction) =
        normalized (left.Numerator * right.Denominator + right.Numerator * left.Denominator) (left.Denominator * right.Denominator)
    let private increment key value state =
        Map.add key (add (Map.tryFind key state |> Option.defaultValue zero) value) state
    let private bounded (value: string) =
        not (System.String.IsNullOrWhiteSpace value) && value.Length <= 256

    let allocate (resources: Resource list) =
        let groups = resources |> List.groupBy _.Key
        let scope = resources |> List.map (fun r -> r.Key.Dimension, r.Key.Provider, r.Key.AccountingScope) |> List.distinct
        let invalid (r: Resource) =
            r.Amount < 0I || not (bounded r.Key.Identity) || not (bounded r.Key.Dimension)
            || not (bounded r.Key.Provider) || not (bounded r.Key.AccountingScope)
            || (r.Shares |> List.exists (fun s -> not (bounded s.ItemId) || s.Fraction.Numerator < 0I || s.Fraction.Denominator <= 0I))
        if scope.Length > 1 then Error "incompatible-resource-scope"
        elif resources |> List.exists invalid then Error "invalid-resource"
        elif groups |> List.exists (fun (_, rows) -> rows |> List.distinct |> List.length > 1) then Error "resource-replay-conflict"
        else
            let unique = groups |> List.map (fun (_, rows) -> List.head rows)
            let excessive (r: Resource) =
                let total = r.Shares |> List.fold (fun sum s -> add sum s.Fraction) zero
                total.Numerator > total.Denominator
            if unique |> List.exists excessive then Error "allocation-exceeds-source"
            else
                let total = unique |> List.sumBy _.Amount
                let allocations =
                    [ for r in unique do
                        for share in r.Shares do
                            yield share, normalized (r.Amount * share.Fraction.Numerator) share.Fraction.Denominator ]
                let allocated = allocations |> List.fold (fun sum (_, amount) -> add sum amount) zero
                Ok { Total = total; Allocated = allocated
                     Unallocated = normalized (total * allocated.Denominator - allocated.Numerator) allocated.Denominator
                     ByItem = allocations |> List.fold (fun state (s, a) -> increment s.ItemId a state) Map.empty
                     ByPurpose = allocations |> List.fold (fun state (s, a) -> increment s.Purpose a state) Map.empty }

    let selectCurrent (sources: SourceRevision list) =
        let invalid = sources |> List.exists (fun s -> not (bounded s.Identity) || s.Revision < 0L || not (bounded s.ContentDigest) || not (bounded s.EffectiveItemId))
        let revisions = sources |> List.groupBy (fun s -> s.Identity, s.Revision)
        // A conflicting historical revision is not erased by arrival of a later one.
        if invalid then Error "invalid-source-revision"
        elif revisions |> List.exists (fun (_, rows) -> rows |> List.distinct |> List.length > 1) then Error "source-revision-conflict"
        else
            sources |> List.groupBy _.Identity
            |> List.map (fun (_, rows) -> rows |> List.maxBy _.Revision)
            |> List.sortBy _.Identity |> Ok

    let timeSummary (window: TelemetryBudget.Interval) (touch: TelemetryBudget.Interval list) (wait: TelemetryBudget.Interval list) =
        let valid (i: TelemetryBudget.Interval) = i.StartNanoseconds >= 0L && i.EndNanoseconds >= i.StartNanoseconds
        if not (valid window) || not (List.forall valid (touch @ wait)) then Error "invalid-interval"
        else
            let clip intervals =
                intervals |> List.choose (fun (i: TelemetryBudget.Interval) ->
                    let first = max window.StartNanoseconds i.StartNanoseconds
                    let last = min window.EndNanoseconds i.EndNanoseconds
                    if last > first then Some { TelemetryBudget.StartNanoseconds = first; TelemetryBudget.EndNanoseconds = last } else None)
            let union original clipped =
                if List.isEmpty original then None
                elif List.isEmpty clipped then Some 0L
                else TelemetryBudget.unionNanoseconds clipped
            let t, w = clip touch, clip wait
            let touchUnion, waitUnion = union touch t, union wait w
            let intersections =
                [ for a in t do
                    for b in w do
                        let first = max a.StartNanoseconds b.StartNanoseconds
                        let last = min a.EndNanoseconds b.EndNanoseconds
                        if last > first then yield { TelemetryBudget.StartNanoseconds = first; TelemetryBudget.EndNanoseconds = last } ]
            let overlap =
                if List.isEmpty touch || List.isEmpty wait then None
                elif List.isEmpty intersections then Some 0L
                else TelemetryBudget.unionNanoseconds intersections
            let elapsed = bigint window.EndNanoseconds - bigint window.StartNanoseconds
            Ok { ElapsedNanoseconds = elapsed
                 ObservedSpanNanoseconds = touch |> List.sumBy (fun i -> bigint i.EndNanoseconds - bigint i.StartNanoseconds)
                 TouchUnionNanoseconds = touchUnion; WaitUnionNanoseconds = waitUnion
                 TouchWaitOverlapNanoseconds = overlap
                 FlowRatio = touchUnion |> Option.bind (fun amount -> if elapsed = 0I then None else Some(normalized (bigint amount) elapsed)) }

    let retries (operations: Operation list) =
        let invalid =
            operations |> List.exists (fun o ->
                not (bounded o.Identity) || (o.Attempts |> List.exists (fun a -> not (bounded a.Identity) || not (bounded a.InputDigest) || a.Ordinal < 1 || a.Usage < 0I)))
        let groups = operations |> List.groupBy _.Identity
        if invalid then Error "invalid-attempt"
        elif groups |> List.exists (fun (_, rows) -> rows |> List.distinct |> List.length > 1) then Error "operation-replay-conflict"
        else
            let unique = groups |> List.map (fun (_, rows) -> List.head rows)
            let attemptConflict =
                unique |> List.exists (fun o ->
                    (o.Attempts |> List.groupBy _.Identity |> List.exists (fun (_, rows) -> rows |> List.distinct |> List.length > 1))
                    || (o.Attempts |> List.distinct |> List.groupBy _.Ordinal |> List.exists (fun (_, rows) -> rows.Length > 1)))
            let identityReuse =
                unique |> List.collect (fun o -> o.Attempts |> List.map (fun a -> a.Identity, o.Identity))
                |> List.groupBy fst |> List.exists (fun (_, rows) -> rows |> List.map snd |> List.distinct |> List.length > 1)
            let prepared = unique |> List.map (fun o -> o, o.Attempts |> List.distinct |> List.sortBy _.Ordinal)
            let incompleteClaim =
                prepared |> List.exists (fun (o, attempts) -> o.PopulationComplete && (List.isEmpty attempts || (attempts |> List.map _.Ordinal) <> [1 .. attempts.Length]))
            if attemptConflict || identityReuse then Error "attempt-replay-conflict"
            elif incompleteClaim then Error "complete-attempt-population-has-gaps"
            else
                let total = prepared |> List.sumBy (fun (_, rows) -> rows |> List.sumBy _.Usage)
                let known = prepared |> List.filter (fun (o, _) -> o.PopulationComplete)
                let retried = known |> List.filter (fun (_, rows) -> rows.Length > 1) |> List.length
                // Retry input changes are relative to the preceding executed attempt.
                // Missing predecessor observations leave input classification unknown, while
                // every witnessed additional ordinal still contributes resource exposure.
                let additions =
                    [ for _, rows in prepared do
                        let byOrdinal = rows |> List.map (fun a -> a.Ordinal, a) |> Map.ofList
                        for a in rows do
                            if a.Ordinal > 1 then
                                yield a, Map.tryFind (a.Ordinal - 1) byOrdinal |> Option.map (fun previous -> a.InputDigest = previous.InputDigest) ]
                let additional = additions |> List.sumBy (fun (a, _) -> a.Usage)
                Ok { ObservedUsage = total; KnownOperations = known.Length; RetriedOperations = retried
                     AdditionalUsage = additional
                     SameInputUsage = additions |> List.sumBy (fun (a, same) -> if same = Some true then a.Usage else 0I)
                     ChangedInputUsage = additions |> List.sumBy (fun (a, same) -> if same = Some false then a.Usage else 0I)
                     UnknownInputUsage = additions |> List.sumBy (fun (a, same) -> if same.IsNone then a.Usage else 0I)
                     Incidence = if List.isEmpty known then None else Some(normalized (bigint retried) (bigint known.Length))
                     Burden = if total = 0I || (unique |> List.exists (fun o -> not o.PopulationComplete)) then None else Some(normalized additional total) }

    let population (sourceOutcomeIds: Set<string>) observedUsage (native: NativePopulation) =
        if observedUsage < 0I || (sourceOutcomeIds |> Set.exists (bounded >> not)) then Error "invalid-population"
        else
            let complete =
                match native.ExpectedInvocations with
                | Some expected when not (Set.isEmpty expected) ->
                    expected = native.TerminalInvocations && expected = native.UsageInvocations
                | _ -> false
            let eligible = native.NativeEligible && complete
            Ok { SourceDeliveries = sourceOutcomeIds.Count
                 NativeCompletions = if eligible then 1 else 0
                 ObservedUsage = observedUsage; WholeItemUsage = if eligible then Some observedUsage else None
                 AssessmentScope = if eligible then "native-item" else "provisional-delivery" }

    let costPerAccepted observedCost acceptedItems =
        if observedCost < 0I || acceptedItems < 0 then Error "invalid-cohort"
        elif acceptedItems = 0 then Ok None
        else Ok(Some(normalized observedCost (bigint acceptedItems)))

    let sourceFingerprint calculationVersion sources =
        if not (bounded calculationVersion) then Error "invalid-calculation-version"
        else
            selectCurrent sources |> Result.map (fun selected ->
                let rows =
                    [| yield [| "fsgg.telemetry.efficiency-source/1"; calculationVersion |]
                       for row in selected do
                           yield [| row.Identity; row.Revision.ToString(System.Globalization.CultureInfo.InvariantCulture); row.ContentDigest; row.EffectiveItemId |] |]
                let bytes = System.Text.Json.JsonSerializer.SerializeToUtf8Bytes rows
                System.Security.Cryptography.SHA256.HashData bytes |> System.Convert.ToHexStringLower)

    let firstPass (items: FirstPassItem list) =
        let invalid =
            items |> List.exists (fun item ->
                not (bounded item.Identity)
                || (match item.State, item.AcceptedAttempt with
                    | Accepted, Some ordinal when ordinal > 0 -> false
                    | Accepted, _ -> true
                    | _, None -> false
                    | _ -> true))
        let grouped = items |> List.groupBy _.Identity
        if invalid then Error "invalid-first-pass-item"
        elif grouped |> List.exists (fun (_, rows) -> rows |> List.distinct |> List.length > 1) then Error "first-pass-replay-conflict"
        else
            let unique = grouped |> List.map (fun (_, rows) -> List.head rows)
            let eligible = unique |> List.filter (fun item -> item.PopulationComplete && item.State <> Open)
            let first = eligible |> List.filter (fun item -> item.State = Accepted && item.AcceptedAttempt = Some 1) |> List.length
            Ok { EligibleItems = eligible.Length; FirstPassItems = first
                 Rate = if List.isEmpty eligible then None else Some(normalized (bigint first) (bigint eligible.Length))
                 ExcludedItems = unique.Length - eligible.Length
                 OpenItems = unique |> List.filter (fun item -> item.State = Open) |> List.length
                 AbandonedItems = unique |> List.filter (fun item -> item.State = Abandoned) |> List.length }

    let freshness (cutoff: System.DateTimeOffset) (producerTime: System.DateTimeOffset option) (ingestionTime: System.DateTimeOffset option) (publicationTime: System.DateTimeOffset option) (expectedProducers: Set<string> option) (observedProducers: Set<string>) =
        let times = [producerTime; ingestionTime; publicationTime]
        let badIdentity =
            (observedProducers |> Set.exists (bounded >> not))
            || (expectedProducers |> Option.exists (Set.exists (bounded >> not)))
        if times |> List.exists (Option.exists (fun instant -> instant > cutoff)) then Error "future-freshness-time"
        elif badIdentity then Error "invalid-producer-identity"
        elif expectedProducers |> Option.exists (fun expected -> not (Set.isSubset observedProducers expected)) then Error "producer-population-conflict"
        else
            let age = Option.map (fun (instant: System.DateTimeOffset) -> decimal (cutoff.UtcTicks - instant.UtcTicks) / decimal System.TimeSpan.TicksPerSecond)
            Ok { SourceAgeSeconds = age producerTime; IngestionAgeSeconds = age ingestionTime
                 PublicationAgeSeconds = age publicationTime
                 ProducerCoverage = expectedProducers |> Option.bind (fun expected ->
                     if Set.isEmpty expected then None else Some(normalized (bigint observedProducers.Count) (bigint expected.Count))) }
