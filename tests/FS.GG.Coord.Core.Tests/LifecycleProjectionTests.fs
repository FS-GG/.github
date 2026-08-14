namespace FS.GG.Coord.Tests

open Xunit
open FS.GG.Coord
open FS.GG.Coord.Types

module LifecycleProjectionTests =
    let worker = WorkerId "test-worker"
    let claim = { Worker = worker; Session = None; AgeSeconds = 0; PreviousStatus = Some Ready }
    let fact value : LifecycleProjection.Fact<_> = { ObservedAt = 1L; Value = value }
    let observation : LifecycleProjection.Observation =
        { Claim = fact (Some(claim, LeaseHeld))
          PullRequest = fact None
          Blockers = fact []
          Delivery = fact { Outstanding = false; DoneStamped = false }
          Issue = fact Open }

    [<Fact>]
    let ``#2264 a held implementation projects In progress`` () =
        Assert.Equal(LifecycleProjection.Project(InProgress, 1L), LifecycleProjection.project observation)

    [<Fact>]
    let ``#2264 open PR or CI projects In review`` () =
        let pr : LifecycleProjection.PullRequest = { Number = 12; Open = true; ReviewOrCiActive = true }
        let value = { observation with PullRequest = fact (Some pr) }
        Assert.Equal(LifecycleProjection.Project(InReview, 1L), LifecycleProjection.project value)

    [<Fact>]
    let ``#2264 active review or CI remains In review even after the PR closes`` () =
        let pr : LifecycleProjection.PullRequest = { Number = 12; Open = false; ReviewOrCiActive = true }
        let value = { observation with PullRequest = fact (Some pr) }
        Assert.Equal(LifecycleProjection.Project(InReview, 1L), LifecycleProjection.project value)

    [<Fact>]
    let ``#2264 delivery obligation remains In review after merge`` () =
        let delivery : LifecycleProjection.Delivery = { Outstanding = true; DoneStamped = false }
        let value = { observation with Claim = fact None; Delivery = fact delivery }
        Assert.Equal(LifecycleProjection.Project(InReview, 1L), LifecycleProjection.project value)

    [<Fact>]
    let ``#2264 only a closed verified done stamp projects Done`` () =
        let delivery : LifecycleProjection.Delivery = { Outstanding = false; DoneStamped = true }
        let value = { observation with Claim = fact None; Issue = fact Closed; Delivery = fact delivery }
        Assert.Equal(LifecycleProjection.Project(Done, 1L), LifecycleProjection.project value)

    [<Fact>]
    let ``#2264 a closed issue without a verified done receipt is withheld`` () =
        let value = { observation with Claim = fact None; Issue = fact Closed }
        match LifecycleProjection.project value with
        | LifecycleProjection.Withheld reason -> Assert.Contains("verified done", reason)
        | result -> failwithf "expected terminal evidence to be withheld, got %A" result

    [<Fact>]
    let ``#2264 delayed event is withheld rather than regressing status`` () =
        let value = { observation with PullRequest = { ObservedAt = 2L; Value = None } }
        match LifecycleProjection.project value with
        | LifecycleProjection.Withheld reason -> Assert.Contains("timestamps", reason)
        | result -> failwithf "expected withheld stale event, got %A" result

    [<Fact>]
    let ``#2264 persisted watermark makes an out-of-order event a no-op`` () =
        let pr : LifecycleProjection.PullRequest = { Number = 12; Open = true; ReviewOrCiActive = true }
        let newer = { observation with PullRequest = { ObservedAt = 3L; Value = Some pr }
                                       Claim = { ObservedAt = 3L; Value = observation.Claim.Value }
                                       Blockers = { ObservedAt = 3L; Value = observation.Blockers.Value }
                                       Delivery = { ObservedAt = 3L; Value = observation.Delivery.Value }
                                       Issue = { ObservedAt = 3L; Value = observation.Issue.Value } }
        let receipt : LifecycleProjection.Watermark = { ObservedAt = 3L; Status = InReview }
        match LifecycleProjection.advance (Some receipt) observation with
        | LifecycleProjection.Withheld reason -> Assert.Contains("predates", reason)
        | result -> failwithf "expected stale event to be withheld, got %A" result
        Assert.Equal(LifecycleProjection.Project(InReview, 3L), LifecycleProjection.advance None newer)

    [<Fact>]
    let ``#2264 equal-time contradictory events are withheld`` () =
        let receipt : LifecycleProjection.Watermark = { ObservedAt = 1L; Status = InReview }
        match LifecycleProjection.advance (Some receipt) observation with
        | LifecycleProjection.Withheld reason -> Assert.Contains("conflicts", reason)
        | result -> failwithf "expected contradictory event to be withheld, got %A" result

    [<Fact>]
    let ``#2264 lifecycle watermark is durable and selects the newest valid receipt`` () =
        let old : LifecycleProjection.Watermark = { ObservedAt = 4L; Status = InProgress }
        let latest : LifecycleProjection.Watermark = { ObservedAt = 9L; Status = InReview }
        let comments = [ "ordinary comment"; LifecycleProjection.watermarkMarker old; "<!-- fsgg:lifecycle-watermark v=1 observedAt=bad status=Done -->"; LifecycleProjection.watermarkMarker latest ]
        Assert.Equal(Some latest, LifecycleProjection.tryWatermark comments)

    [<Fact>]
    let ``#2264 round 1: a watermark quoted in prose cannot outrank the real persisted receipt`` () =
        // Round-1 review repair. The prior `body.IndexOf(marker)` found the sentinel wherever it sat in a
        // comment, including a documentation-style comment that merely QUOTES an illustrative marker —
        // exactly the org's normal writing style (disqualifications, acceptance markers, repair records
        // all quote prior markers in prose). A quoted marker carrying a large `observedAt` then outranked
        // the real receipt under `List.sortByDescending`, corrupting AC-4's guarantee that an older event
        // can never overwrite a newer observed state.
        let real : LifecycleProjection.Watermark = { ObservedAt = 9L; Status = InReview }
        let quotedMarker = LifecycleProjection.watermarkMarker { ObservedAt = 9999999999999L; Status = Done }
        let quoting =
            $"Repair note: for context, an earlier draft of this comment carried\n`{quotedMarker}`\nbefore it was corrected — ignore it, it was never real."
        let comments = [ "ordinary comment"; LifecycleProjection.watermarkMarker real; quoting ]
        Assert.Equal(Some real, LifecycleProjection.tryWatermark comments)

    [<Fact>]
    let ``#2264 unresolved blocker wins over an implementation claim`` () =
        let blocker = { Ref = None; Raw = "human action"; State = BlockerUnparseable }
        let value = { observation with Blockers = fact [ blocker ] }
        Assert.Equal(LifecycleProjection.Project(Blocked, 1L), LifecycleProjection.project value)

    [<Fact>]
    let ``#2264 expired orphan claim no longer projects active work`` () =
        let value = { observation with Claim = fact (Some(claim, LeaseExpiredNoPr)) }
        Assert.Equal(LifecycleProjection.Project(Ready, 1L), LifecycleProjection.project value)

    [<Fact>]
    let ``M1 explicit Backlog intent survives an otherwise-ready reconciliation`` () =
        let value = { observation with Claim = fact None }
        let intent =
            LifecycleProjection.Backlog
                { Revision = 7L
                  Reason = "operator parked for later" }
        Assert.Equal(
            LifecycleProjection.Project(Backlog, 1L),
            LifecycleProjection.reduce LifecycleProjection.IntentStatusV1 intent value
        )

    [<Theory>]
    [<InlineData(true)>]
    [<InlineData(false)>]
    let ``M1 both human park variants survive claim and review observations`` decision =
        let human = if decision then AwaitingHumanDecision else AwaitingHumanAction
        let pr : LifecycleProjection.PullRequest = { Number = 12; Open = true; ReviewOrCiActive = true }
        let value = { observation with PullRequest = fact (Some pr) }
        let intent =
            LifecycleProjection.HumanPark(
                human,
                { Revision = 8L
                  Reason = "explicit human park" }
            )
        Assert.Equal(
            LifecycleProjection.Project(Blocked, 1L),
            LifecycleProjection.reduce LifecycleProjection.IntentStatusV1 intent value
        )

    [<Fact>]
    let ``M1 active lifecycle facts dominate Backlog intent`` () =
        let intent =
            LifecycleProjection.Backlog
                { Revision = 9L
                  Reason = "not yet scheduled" }
        Assert.Equal(
            LifecycleProjection.Project(InProgress, 1L),
            LifecycleProjection.reduce LifecycleProjection.IntentStatusV1 intent observation
        )

    [<Fact>]
    let ``M1 migration turns only deliberate legacy parks into first-class intent`` () =
        match LifecycleProjection.migrateIntent 10L Backlog None with
        | LifecycleProjection.Backlog record -> Assert.Equal(10L, record.Revision)
        | other -> failwithf "expected Backlog migration, got %A" other
        match LifecycleProjection.migrateIntent 11L Blocked (Some AwaitingHumanAction) with
        | LifecycleProjection.HumanPark(AwaitingHumanAction, record) -> Assert.Equal(11L, record.Revision)
        | other -> failwithf "expected HumanPark migration, got %A" other
        Assert.Equal(LifecycleProjection.Auto, LifecycleProjection.migrateIntent 12L Ready None)

    [<Fact>]
    let ``M1 shadow classifies explained park differences and rollback is a projection switch`` () =
        let value = { observation with Claim = fact None }
        let intent = LifecycleProjection.migrateIntent 13L Backlog None
        let shadow = LifecycleProjection.shadow LifecycleProjection.IntentStatusV1 intent value
        Assert.Equal(
            LifecycleProjection.DeliberateParkPreserved(Ready, Backlog),
            shadow.Difference
        )
        Assert.Equal(
            LifecycleProjection.Project(Backlog, 1L),
            LifecycleProjection.select LifecycleProjection.Intent shadow
        )
        Assert.Equal(
            LifecycleProjection.Project(Ready, 1L),
            LifecycleProjection.select LifecycleProjection.Legacy shadow
        )

    [<Fact>]
    let ``M1 consecutive reconciliation is idempotent once intended status is projected`` () =
        let value = { observation with Claim = fact None }
        let intent = LifecycleProjection.migrateIntent 14L Backlog None
        let first = LifecycleProjection.shadowAdvance LifecycleProjection.IntentStatusV1 intent None value
        let projected = LifecycleProjection.select LifecycleProjection.Intent first
        let receipt =
            match projected with
            | LifecycleProjection.Project(status, observedAt) ->
                Some({ ObservedAt = observedAt; Status = status }: LifecycleProjection.Watermark)
            | other -> failwithf "expected projection, got %A" other
        let second = LifecycleProjection.shadowAdvance LifecycleProjection.IntentStatusV1 intent receipt value
        Assert.Equal(projected, LifecycleProjection.select LifecycleProjection.Intent second)

    [<Fact>]
    let ``M1 projection switch fails closed on misspelling`` () =
        Assert.Equal(Ok LifecycleProjection.Intent, LifecycleProjection.projectionMode None)
        Assert.Equal(Ok LifecycleProjection.Legacy, LifecycleProjection.projectionMode (Some "legacy"))
        match LifecycleProjection.projectionMode (Some "newest") with
        | Error reason -> Assert.Contains("unknown lifecycle projection mode", reason)
        | result -> failwithf "expected invalid switch to fail, got %A" result
