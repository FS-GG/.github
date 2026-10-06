namespace FS.GG.Coord.Tests

open System
open Xunit
open FS.GG.Coord
open FS.GG.Coord.ProcessEfficiency

module ProcessEfficiencyTests =
    let private value = function Ok result -> result | Error reason -> failwith reason
    let private ratio n d = fraction n d |> value
    let private share item purpose n d : Share = { ItemId = item; Purpose = purpose; Fraction = ratio n d }
    let private resource identity amount shares : Resource =
        { Key = { Identity = identity; Dimension = "tokens-total"; Provider = "fixture"; AccountingScope = "native" }
          Amount = amount; Shares = shares }
    let private interval first last : TelemetryBudget.Interval = { StartNanoseconds = first; EndNanoseconds = last }
    let private attempt identity ordinal inputs usage : Attempt = { Identity = identity; Ordinal = ordinal; InputDigest = inputs; Usage = usage }
    let private operation identity complete attempts : Operation = { Identity = identity; PopulationComplete = complete; Attempts = attempts }
    let private source identity revision digest item : SourceRevision = { Identity = identity; Revision = revision; ContentDigest = digest; EffectiveItemId = item }

    [<Fact>]
    let ``shared native resource is counted once and conserved before rounding`` () =
        let r = resource "shared" 1I [ share "A" DirectProduct 1I 3I; share "B" DirectProduct 1I 3I ]
        let result = allocate [r; r] |> value
        Assert.Equal(1I, result.Total)
        Assert.Equal(ratio 2I 3I, result.Allocated)
        Assert.Equal(ratio 1I 3I, result.Unallocated)
        Assert.Equal(ratio 1I 3I, result.ByItem.["A"])
        Assert.Equal(ratio 2I 3I, result.ByPurpose.[DirectProduct])

    [<Fact>]
    let ``allocation permutations retain exact totals`` () =
        let a = resource "a" 60I [share "A" UnknownPurpose 1I 1I]
        let b = resource "b" 40I [share "B" UsefulAssurance 1I 2I]
        Assert.Equal(allocate [a; b; a], allocate [b; a; b])
        let result = allocate [a; b] |> value
        Assert.Equal(ratio 60I 1I, result.ByPurpose.[UnknownPurpose])
        Assert.Equal(ratio 20I 1I, result.Unallocated)

    [<Fact>]
    let ``overallocation and malformed shares fail closed`` () =
        Assert.Equal(Error "allocation-exceeds-source", allocate [resource "u" 1I [share "A" DirectProduct 2I 3I; share "B" UsefulAssurance 2I 3I]])
        let bad = { ItemId = "A"; Purpose = UnknownPurpose; Fraction = { Numerator = 1I; Denominator = 0I } }
        Assert.Equal(Error "invalid-resource", allocate [resource "u" 1I [bad]])
        Assert.Equal(Error "invalid-fraction", fraction -1I 1I)

    [<Fact>]
    let ``resource conflicts and incompatible units cannot silently aggregate`` () =
        let a = resource "u" 10I []
        Assert.Equal(Error "resource-replay-conflict", allocate [a; {a with Amount = 20I}])
        Assert.Equal(Error "incompatible-resource-scope", allocate [a; {a with Key = {a.Key with Dimension = "runner-seconds"}}])

    [<Fact>]
    let ``arbitrary size amounts retain exact conservation`` () =
        let huge = bigint Int64.MaxValue * bigint Int64.MaxValue
        let result = allocate [resource "u" huge [share "A" DirectProduct 2I 3I]] |> value
        Assert.Equal(huge, result.Total)
        Assert.Equal(ratio (huge * 2I) 3I, result.Allocated)
        Assert.Equal(ratio huge 3I, result.Unallocated)

    [<Fact>]
    let ``late predecessor and correction replay preserve one current effective contribution`` () =
        let old = source "outcome" 1L "digest1" "wrong"
        let current = source "outcome" 2L "digest2" "correct"
        Assert.Equal(Ok [current], selectCurrent [current; old; current; old])
        Assert.Equal(Ok [current], selectCurrent [old; current])

    [<Fact>]
    let ``same source revision conflict stays unknown even after a later revision`` () =
        let old = source "u" 2L "digest-a" "A"
        let conflict = source "u" 2L "digest-b" "B"
        let newer = source "u" 3L "digest-c" "C"
        Assert.Equal(Error "source-revision-conflict", selectCurrent [old; conflict; newer])

    [<Fact>]
    let ``effective attribution participates in conflict detection`` () =
        let row = source "u" 2L "same-raw-digest" "A"
        Assert.Equal(Error "source-revision-conflict", selectCurrent [row; {row with EffectiveItemId = "B"}])

    [<Fact>]
    let ``overlapping observed spans are separate from clipped elapsed unions`` () =
        let result = timeSummary (interval 0L 20L) [interval 0L 10L; interval 5L 15L] [interval 8L 18L; interval 12L 20L] |> value
        Assert.Equal(20I, result.ObservedSpanNanoseconds)
        Assert.Equal(Some 15L, result.TouchUnionNanoseconds)
        Assert.Equal(Some 12L, result.WaitUnionNanoseconds)
        Assert.Equal(Some 7L, result.TouchWaitOverlapNanoseconds)
        Assert.Equal(Some(ratio 3I 4I), result.FlowRatio)

    [<Fact>]
    let ``empty wait is unknown but observed outside window is known zero`` () =
        let result = timeSummary (interval 5L 15L) [interval 0L 8L; interval 12L 20L] [] |> value
        Assert.Equal(16I, result.ObservedSpanNanoseconds)
        Assert.Equal(Some 6L, result.TouchUnionNanoseconds)
        Assert.Equal(None, result.WaitUnionNanoseconds)
        Assert.Equal(None, result.TouchWaitOverlapNanoseconds)
        let outside = timeSummary (interval 5L 15L) [interval 20L 30L] [interval 0L 1L] |> value
        Assert.Equal(Some 0L, outside.TouchUnionNanoseconds)
        Assert.Equal(Some 0L, outside.WaitUnionNanoseconds)

    [<Fact>]
    let ``invalid intervals do not get silently filtered as zero`` () =
        Assert.Equal(Error "invalid-interval", timeSummary (interval 0L 20L) [interval 10L 5L] [])
        let result = timeSummary (interval 10L 10L) [interval 10L 10L] [] |> value
        Assert.Equal(None, result.FlowRatio)

    [<Fact>]
    let ``retry replay is excluded and same versus changed inputs are separated`` () =
        let second = attempt "x2" 2 "h1" 10I
        let result = retries [operation "x" true [second; attempt "x1" 1 "h1" 40I; second]
                              operation "y" true [attempt "y1" 1 "h1" 20I; attempt "y2" 2 "h2" 30I]
                              operation "z" true [attempt "z1" 1 "h1" 50I]] |> value
        Assert.Equal(150I, result.ObservedUsage)
        Assert.Equal(10I, result.SameInputUsage)
        Assert.Equal(30I, result.ChangedInputUsage)
        Assert.Equal(Some(ratio 2I 3I), result.Incidence)
        Assert.Equal(Some(ratio 4I 15I), result.Burden)

    [<Fact>]
    let ``cancelled cost and incomplete exposure remain but full burden unknown`` () =
        let result = retries [operation "c" true [attempt "c1" 1 "h1" 15I; attempt "c2" 2 "h1" 35I]
                              operation "missing" false [attempt "m1" 1 "h1" 10I]] |> value
        Assert.Equal(60I, result.ObservedUsage)
        Assert.Equal(35I, result.AdditionalUsage)
        Assert.Equal(Some(ratio 1I 1I), result.Incidence)
        Assert.Equal(None, result.Burden)

    [<Fact>]
    let ``known populations require contiguous unique attempt ordinals`` () =
        Assert.Equal(Error "complete-attempt-population-has-gaps", retries [operation "x" true [attempt "x2" 2 "h1" 10I]])
        Assert.Equal(Error "attempt-replay-conflict", retries [operation "x" true [attempt "x1" 1 "h1" 10I; attempt "x2" 1 "h1" 10I]])
        Assert.Equal(Error "attempt-replay-conflict", retries [operation "x" true [attempt "x1" 1 "h1" 10I]; operation "y" true [attempt "x1" 1 "h1" 10I]])

    [<Fact>]
    let ``source merged remains visible without minting native completion`` () =
        let incomplete = { ExpectedInvocations = Some(set ["a"; "b"]); TerminalInvocations = set ["a"]; UsageInvocations = set ["a"]; NativeEligible = true }
        let result = population (set ["merge"]) 40I incomplete |> value
        Assert.Equal(1, result.SourceDeliveries)
        Assert.Equal(0, result.NativeCompletions)
        Assert.Equal(None, result.WholeItemUsage)
        Assert.Equal("provisional-delivery", result.AssessmentScope)
        let complete = {incomplete with TerminalInvocations = set ["a"; "b"]; UsageInvocations = set ["a"; "b"]}
        let final = population (set ["merge"]) 70I complete |> value
        Assert.Equal(1, final.SourceDeliveries)
        Assert.Equal(1, final.NativeCompletions)
        Assert.Equal(Some 70I, final.WholeItemUsage)
        Assert.Equal(0, (population (set ["merge"]) 70I {complete with NativeEligible = false} |> value).NativeCompletions)

    [<Fact>]
    let ``zero accepted denominator preserves exposure and is not applicable`` () =
        Assert.Equal(Ok None, costPerAccepted 100I 0)
        Assert.Equal(Ok(Some(ratio 105I 2I)), costPerAccepted 105I 2)
        Assert.Equal(Error "invalid-cohort", costPerAccepted -1I 2)

    [<Fact>]
    let ``fingerprint tracks content effective item and calculator rather than fact count`` () =
        let a = source "a" 1L "digest1" "old"
        let b = source "b" 2L "digest2" "old"
        let hash rows = sourceFingerprint "efficiency-calculation/1" rows |> value
        Assert.Equal(hash [a; b], hash [b; a; a])
        Assert.NotEqual<string>(hash [a; b], hash [{a with EffectiveItemId = "correct"}; b])
        Assert.NotEqual<string>(hash [a; b], hash [{a with ContentDigest = "new"}; b])
        Assert.NotEqual<string>(hash [a; b], sourceFingerprint "efficiency-calculation/2" [a; b] |> value)

    [<Fact>]
    let ``first pass excludes missing and open populations while retaining abandoned exposure`` () =
        let item identity complete state accepted : FirstPassItem = { Identity = identity; PopulationComplete = complete; State = state; AcceptedAttempt = accepted }
        let a = item "A" true Accepted (Some 1)
        let result = firstPass [a; a; item "B" true Accepted (Some 2); item "C" true Abandoned None; item "D" false Accepted (Some 1); item "E" false Open None] |> value
        Assert.Equal(3, result.EligibleItems)
        Assert.Equal(1, result.FirstPassItems)
        Assert.Equal(Some(ratio 1I 3I), result.Rate)
        Assert.Equal(2, result.ExcludedItems)
        Assert.Equal(1, result.OpenItems)
        Assert.Equal(1, result.AbandonedItems)
        Assert.Equal(Error "first-pass-replay-conflict", firstPass [a; {a with AcceptedAttempt = Some 2}])

    [<Fact>]
    let ``fresh public projection cannot erase missing source or ingestion coverage`` () =
        let epoch = DateTimeOffset.Parse("2026-10-06T00:00:00Z", Globalization.CultureInfo.InvariantCulture)
        let result = freshness (epoch.AddSeconds 120.) (Some(epoch.AddSeconds 20.)) (Some(epoch.AddSeconds 110.)) (Some(epoch.AddSeconds 118.)) (Some(set ["a"; "b"; "c"; "d"])) (set ["a"; "b"; "c"]) |> value
        Assert.Equal(Some 100M, result.SourceAgeSeconds)
        Assert.Equal(Some 10M, result.IngestionAgeSeconds)
        Assert.Equal(Some 2M, result.PublicationAgeSeconds)
        Assert.Equal(Some(ratio 3I 4I), result.ProducerCoverage)
        let unknown = freshness epoch None None (Some epoch) None Set.empty |> value
        Assert.Equal(None, unknown.SourceAgeSeconds)
        Assert.Equal(None, unknown.IngestionAgeSeconds)
        Assert.Equal(None, unknown.ProducerCoverage)

    [<Fact>]
    let ``freshness preserves fractional offset timestamps and refuses future witnesses`` () =
        let parse (text: string) = DateTimeOffset.Parse(text, Globalization.CultureInfo.InvariantCulture)
        let cutoff = parse "2026-10-06T00:00:01.125Z"
        let earlier = parse "2026-10-06T02:00:00.625+02:00"
        let result = freshness cutoff (Some earlier) None None None Set.empty |> value
        Assert.Equal(Some 0.5M, result.SourceAgeSeconds)
        Assert.Equal(Error "future-freshness-time", freshness cutoff (Some(cutoff.AddTicks 1L)) None None None Set.empty)

    [<Fact>]
    let ``A to B to B counts changed repair then same input retry`` () =
        let result = retries [operation "x" true [attempt "a" 1 "A" 10I; attempt "b" 2 "B" 20I; attempt "c" 3 "B" 30I]] |> value
        Assert.Equal(60I, result.ObservedUsage)
        Assert.Equal(50I, result.AdditionalUsage)
        Assert.Equal(20I, result.ChangedInputUsage)
        Assert.Equal(30I, result.SameInputUsage)
        Assert.Equal(0I, result.UnknownInputUsage)
        let missing = retries [operation "x" false [attempt "c" 3 "B" 30I]] |> value
        Assert.Equal(30I, missing.AdditionalUsage)
        Assert.Equal(30I, missing.UnknownInputUsage)
        Assert.Equal(0I, missing.ChangedInputUsage)
        Assert.Equal(None, missing.Burden)
