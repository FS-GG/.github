namespace FS.GG.Coord.Tests

open Xunit
open FS.GG.Coord.Batch
open FS.GG.Coord.Driver

module DriverTests =
    let model = { Waves = 2; ImplementerSlotsPerWave = 3; ReviewSlots = 2; ConsolidationThreshold = 3 }
    let clean = { HasHostIdentity = true; StaleClaim = false; EngineCurrent = true; PendingWrites = 0; ReconcileDryRunFresh = true; ReconcileApplied = true; ReconcileFresh = true; TriageFresh = true; CurrencyScoped = true }

    [<Fact>]
    let ``#2127 receipts are source-bound complete and fresh`` () =
        let review = { MarkerValid = true; CriticIdentity = Some "shrike"; HeadSha = Some "abc"; Rounds = [1]; ChecksGreen = true; HostAccepted = true }
        Assert.True(receiptFresh 120L 30L { ObservedAt = 100L; SourceSha = "abc"; Complete = true; Review = Some review })
        Assert.False(receiptFresh 120L 30L { ObservedAt = 80L; SourceSha = "abc"; Complete = true; Review = Some review })
        Assert.False(receiptFresh 120L 30L { ObservedAt = 100L; SourceSha = "abc"; Complete = true; Review = None })

    [<Fact>]
    let ``#2127 6 to 2 consolidates then dispatches a fresh three-slot wave`` () =
        Assert.Equal(Consolidate, nextAction model 2 false clean [])
        Assert.Equal(DispatchWave 3, nextAction model 2 true clean [])

    [<Fact>]
    let ``#2127 housekeeping gates fail closed before dispatch`` () =
        Assert.Equal(RequestHostIdentity, nextAction model 2 true { clean with HasHostIdentity = false } [])
        Assert.Equal(ReapStaleClaims, nextAction model 2 true { clean with StaleClaim = true } [])
        Assert.Equal(RepairEngineCurrency, nextAction model 2 true { clean with EngineCurrent = false } [])
        Assert.Equal(FlushPendingWrites, nextAction model 2 true { clean with PendingWrites = 1 } [])
        Assert.Equal(RefreshTriage, nextAction model 2 true { clean with TriageFresh = false } [])

    [<Fact>]
    let ``#2127 stale reconciliation and scoped currency block dispatch`` () =
        Assert.Equal(ReconcileBoard, nextAction model 2 true { clean with ReconcileFresh = false } [])
        Assert.Equal(RepairEngineCurrency, nextAction model 2 true { clean with CurrencyScoped = false } [])

    [<Fact>]
    let ``#2127 no-pr and tests-running live claims resume their worker`` () =
        Assert.Equal(ResumeSameWorker, nextAction model 2 true clean [ { ClaimLive = true; ReviewReady = false; ParkedOrDone = false } ])

    [<Fact>]
    let ``#2127 review validation rejects marker sha rounds checks and acceptance defects`` () =
        let errors = validateReviewChain 3 { MarkerValid = false; CriticIdentity = None; HeadSha = None; Rounds = [ 2 ]; ChecksGreen = false; HostAccepted = false }
        Assert.True(List.length errors >= 5)

    [<Fact>]
    let ``#2127 review comment markers bind critic and identical reviewed accepted sha`` () =
        let comments = [ "<!-- fsgg:independent-review:v1 -->\ncritic: shrike\nreviewed-head: abc"; "<!-- fsgg:review-accepted:v1 -->\naccepted-head: abc" ]
        match parseReviewComments comments with
        | Ok chain -> Assert.Equal(Some "shrike", chain.CriticIdentity)
        | Error errors -> failwithf "%A" errors
        Assert.True(Result.isError (parseReviewComments [ "<!-- fsgg:independent-review:v1 -->\ncritic: x\nreviewed-head: abc" ]))

    [<Fact>]
    let ``#2127 latest confirmation round binds the accepted sha`` () =
        let comments = [ "<!-- fsgg:independent-review:v1 -->\ncritic: shrike\nreviewed-head: old"; "<!-- fsgg:independent-review-confirmation:v1 -->\ncritic: shrike\nround: 1\npreceding-review: old\nreviewed-head: new\nverdict: pass"; "<!-- fsgg:review-accepted:v1 -->\naccepted-head: new" ]
        match parseReviewComments comments with
        | Ok chain -> Assert.Equal(Some "new", chain.HeadSha)
        | Error errors -> failwithf "%A" errors

    [<Fact>]
    let ``#2127 live worker returns are resumed and invalid review chains are typed`` () =
        Assert.Equal(ResumeSameWorker, nextAction model 2 true clean [ { ClaimLive = true; ReviewReady = false; ParkedOrDone = false } ])
        let errors = validateReviewChain 3 { MarkerValid = false; CriticIdentity = None; HeadSha = None; Rounds = [ 1; 3 ]; ChecksGreen = false; HostAccepted = false }
        Assert.Equal(6, List.length errors)
