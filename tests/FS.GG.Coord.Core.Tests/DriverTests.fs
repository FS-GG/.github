namespace FS.GG.Coord.Tests

open Xunit
open FS.GG.Coord.Batch
open FS.GG.Coord.Driver

module DriverTests =
    let model = { Waves = 2; ImplementerSlotsPerWave = 3; ReviewSlots = 2; ConsolidationThreshold = 3 }
    let clean = { HasHostIdentity = true; StaleClaim = false; EngineCurrent = true; PendingWrites = 0; ReconcileDryRunFresh = true; ReconcileApplied = true; ReconcileFresh = true; TriageFresh = true; CurrencyScoped = true }
    let comment id url body : ReviewComment = { Id = id; Url = url; Body = body }

    [<Fact>]
    let ``#2127 receipts are source-bound complete and fresh`` () =
        let review = { MarkerValid = true; CriticIdentity = Some "shrike"; HeadSha = Some "abc"; Rounds = [1]; ChecksGreen = true; HostAccepted = true }
        Assert.True(receiptFresh 120L 30L { ObservedAt = 100L; SourceSha = "abc"; Complete = true; Review = Some review })
        Assert.False(receiptFresh 120L 30L { ObservedAt = 80L; SourceSha = "abc"; Complete = true; Review = Some review })
        Assert.False(receiptFresh 120L 30L { ObservedAt = 100L; SourceSha = "abc"; Complete = true; Review = None })
        let planning =
            { ObservedAt = 100L; SourceSha = "snapshot"; Complete = true; ConsolidationApproved = true
              Housekeeping = clean; WorkerReturns = [] }
        Assert.True(planningReceiptFresh 120L 30L "snapshot" planning)
        Assert.False(planningReceiptFresh 120L 30L "other" planning)
        Assert.False(planningReceiptFresh 140L 30L "snapshot" planning)

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
        let comments = [ comment 1L "https://reviews/1" "<!-- fsgg:independent-review:v1 -->\ncritic: shrike\nreviewed-head: abc\nverdict: pass"; comment 2L "https://reviews/2" "<!-- fsgg:review-accepted:v1 -->\naccepted-head: abc" ]
        match parseReviewComments comments with
        | Ok chain -> Assert.Equal(Some "shrike", chain.CriticIdentity)
        | Error errors -> failwithf "%A" errors
        Assert.True(Result.isError (parseReviewComments [ comment 1L "https://reviews/1" "<!-- fsgg:independent-review:v1 -->\ncritic: x\nreviewed-head: abc" ]))

    [<Fact>]
    let ``#2127 latest confirmation round binds the accepted sha`` () =
        let comments =
            [ comment 1L "https://reviews/1" "<!-- fsgg:independent-review:v1 -->\ncritic: shrike\nreviewed-head: old\nverdict: changes-required"
              comment 2L "https://reviews/2" "<!-- fsgg:independent-review-confirmation:v1 -->\ninitial-review: https://reviews/1\ncritic: shrike\nround: 1\npreceding-review: https://reviews/1\nreviewed-head: new\nverdict: pass"
              comment 3L "https://reviews/3" "<!-- fsgg:review-accepted:v1 -->\naccepted-head: new" ]
        match parseReviewComments comments with
        | Ok chain -> Assert.Equal(Some "new", chain.HeadSha)
        | Error errors -> failwithf "%A" errors

    [<Fact>]
    let ``#2127 repair rounds link the exact preceding comment URL in order`` () =
        let comments =
            [ comment 10L "https://reviews/initial" "<!-- fsgg:independent-review:v1 -->\ncritic: shrike\nreviewed-head: a\nverdict: changes-required"
              comment 20L "https://reviews/round-1" "<!-- fsgg:independent-review-confirmation:v1 -->\ninitial-review: https://reviews/initial\ncritic: shrike\nround: 1\npreceding-review: https://reviews/initial\nreviewed-head: b\nverdict: changes-required"
              comment 30L "https://reviews/round-2" "<!-- fsgg:independent-review-confirmation:v1 -->\ninitial-review: https://reviews/initial\ncritic: shrike\nround: 2\npreceding-review: https://reviews/round-1\nreviewed-head: c\nverdict: pass"
              comment 40L "https://reviews/accepted" "<!-- fsgg:review-accepted:v1 -->\naccepted-head: c" ]
        match parseReviewComments comments with
        | Ok chain ->
            Assert.Equal<int list>([ 1; 2 ], chain.Rounds)
            Assert.Equal(Some "c", chain.HeadSha)
        | Error errors -> failwithf "%A" errors

    [<Fact>]
    let ``#2127 review confirmations fail closed unless one critic advances every linked round`` () =
        let initial = comment 1L "https://reviews/1" "<!-- fsgg:independent-review:v1 -->\ncritic: shrike\nreviewed-head: first\nverdict: changes-required"
        let accepted head = comment 3L "https://reviews/3" $"<!-- fsgg:review-accepted:v1 -->\naccepted-head: %s{head}"
        let confirmation critic round initialUrl preceding head verdict =
            comment 2L "https://reviews/2" $"<!-- fsgg:independent-review-confirmation:v1 -->\ninitial-review: %s{initialUrl}\ncritic: %s{critic}\nround: %s{round}\npreceding-review: %s{preceding}\nreviewed-head: %s{head}\nverdict: %s{verdict}"
        let rejects comments = Assert.True(Result.isError(parseReviewComments comments))
        rejects [ initial; confirmation "other" "1" "https://reviews/1" "https://reviews/1" "second" "pass"; accepted "second" ]
        rejects [ initial; confirmation "shrike" "2" "https://reviews/1" "https://reviews/1" "second" "pass"; accepted "second" ]
        rejects [ initial; confirmation "shrike" "1" "wrong" "https://reviews/1" "second" "pass"; accepted "second" ]
        rejects [ initial; confirmation "shrike" "1" "https://reviews/1" "wrong" "second" "pass"; accepted "second" ]
        rejects [ initial; confirmation "shrike" "1" "https://reviews/1" "https://reviews/1" "second" "changes-required"; accepted "second" ]
        rejects [ initial; comment 2L "https://reviews/2" "<!-- fsgg:independent-review-confirmation:v1 -->\ncritic: shrike\nround: 1"; accepted "second" ]
        rejects [ initial; confirmation "shrike" "1" "https://reviews/1" "https://reviews/1" "second" "pass"; accepted "first" ]

    [<Fact>]
    let ``#2127 live worker returns are resumed and invalid review chains are typed`` () =
        Assert.Equal(ResumeSameWorker, nextAction model 2 true clean [ { ClaimLive = true; ReviewReady = false; ParkedOrDone = false } ])
        let errors = validateReviewChain 3 { MarkerValid = false; CriticIdentity = None; HeadSha = None; Rounds = [ 1; 3 ]; ChecksGreen = false; HostAccepted = false }
        Assert.Equal(6, List.length errors)
