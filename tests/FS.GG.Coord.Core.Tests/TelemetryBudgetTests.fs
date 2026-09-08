namespace FS.GG.Coord.Tests

open System
open System.Text
open Xunit
open FS.GG.Coord

module TelemetryBudgetTests =
    [<Theory>]
    [<InlineData(10L,100L,"pass",false)>]
    [<InlineData(11L,100L,"breach",false)>]
    [<InlineData(25L,100L,"breach",false)>]
    [<InlineData(26L,100L,"breach",true)>]
    let ``thresholds are exact and strict`` numerator denominator expected severe =
        match TelemetryBudget.assess (TelemetryBudget.Usable(numerator,denominator)) with
        | TelemetryBudget.Pass _ -> Assert.Equal("pass", expected)
        | TelemetryBudget.Breach(_,_,actual) -> Assert.Equal("breach", expected); Assert.Equal(severe,actual)
        | value -> failwithf "unexpected verdict %A" value

    [<Fact>]
    let ``threshold multiplication cannot overflow`` () =
        match TelemetryBudget.assess (TelemetryBudget.Usable(Int64.MaxValue,Int64.MaxValue)) with
        | TelemetryBudget.Breach(_,_,true) -> ()
        | value -> failwithf "expected severe breach, got %A" value

    [<Fact>]
    let ``known zero activity is not applicable and a missing denominator is unknown`` () =
        Assert.Equal(TelemetryBudget.NotApplicableVerdict "known-zero-activity", TelemetryBudget.assess (TelemetryBudget.Usable(0L,0L)))
        Assert.Equal(TelemetryBudget.UnknownVerdict "missing-denominator", TelemetryBudget.assess (TelemetryBudget.Usable(1L,0L)))

    [<Fact>]
    let ``parallel waits are unioned and witnessed work is excluded at nanosecond precision`` () =
        let interval startAt endAt : TelemetryBudget.Interval = { StartNanoseconds = startAt; EndNanoseconds = endAt }
        let waits = [ interval 0L 120_000_000_000L; interval 30_000_000_000L 90_000_000_000L ]
        let useful = [ interval 20_000_000_000L 100_000_000_000L ]
        Assert.Equal(Some 40_000_000_000L, TelemetryBudget.subtractNanoseconds waits useful)
        Assert.Equal(Some 1L, TelemetryBudget.unionNanoseconds [ interval 0L 1L ])

    [<Fact>]
    let ``one severe item or fifteen distinct items opens intervention`` () =
        Assert.False(TelemetryBudget.interventionDue 14 false)
        Assert.True(TelemetryBudget.interventionDue 15 false)
        Assert.True(TelemetryBudget.interventionDue 1 true)

    [<Fact>]
    let ``caller-authored budget verdicts are not facts`` () =
        let bytes = Encoding.UTF8.GetBytes $"""{{"schema":"{TelemetryStore.BatchSchema}","ingestId":"bad-budget","sourceIdentity":"worker","generation":"g","cursor":"1","eventCount":1,"events":[{{"kind":"budget-assessment","identity":"bad","itemId":"item","revision":0,"verdict":"pass","reset":true}}]}}"""
        match TelemetryStore.parseBatch bytes with
        | Error errors -> Assert.Contains("unsupported", String.concat ";" errors)
        | Ok _ -> failwith "caller-authored assessment was accepted"

    [<Fact>]
    let ``completion must come from the native item source`` () =
        let bytes = Encoding.UTF8.GetBytes $"""{{"schema":"{TelemetryStore.BatchSchema}","ingestId":"bad-population","sourceIdentity":"worker","generation":"g","cursor":"1","eventCount":1,"events":[{{"kind":"budget-population","identity":"population","itemId":"item","revision":0,"originalItemId":"item","state":"completed","sourceKind":"caller","sourceRef":"fake"}}]}}"""
        Assert.True(TelemetryStore.parseBatch bytes |> Result.isError)
