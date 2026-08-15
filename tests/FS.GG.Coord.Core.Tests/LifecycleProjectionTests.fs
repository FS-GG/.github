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

    let reduce intent value =
        LifecycleProjection.reduce LifecycleProjection.IntentStatusV1 intent value

    let advance intent watermark value =
        LifecycleProjection.advance LifecycleProjection.IntentStatusV1 intent watermark value

    [<Fact>]
    let ``M6 Auto reducer covers claim PR delivery blocker and ready states`` () =
        Assert.Equal(LifecycleProjection.Project(InProgress, 1L), reduce LifecycleProjection.Auto observation)
        let pr : LifecycleProjection.PullRequest = { Number = 12; Open = true; ReviewOrCiActive = true }
        Assert.Equal(
            LifecycleProjection.Project(InReview, 1L),
            reduce LifecycleProjection.Auto { observation with PullRequest = fact (Some pr) })
        Assert.Equal(
            LifecycleProjection.Project(InReview, 1L),
            reduce LifecycleProjection.Auto
                { observation with Claim = fact None; Delivery = fact { Outstanding = true; DoneStamped = false } })
        Assert.Equal(
            LifecycleProjection.Project(Blocked, 1L),
            reduce LifecycleProjection.Auto
                { observation with Claim = fact None; Blockers = fact [ { Ref = Some { Owner = "FS-GG"; Repo = ".github"; Number = 1 }; Raw = "FS-GG/.github#1"; State = BlockerOpen } ] })
        Assert.Equal(
            LifecycleProjection.Project(Ready, 1L),
            reduce LifecycleProjection.Auto { observation with Claim = fact None })

    [<Fact>]
    let ``M6 explicit intents are authoritative after active facts settle`` () =
        let idle = { observation with Claim = fact None }
        let backlog = LifecycleProjection.Backlog { Revision = 7L; Reason = "operator park" }
        let deferred = LifecycleProjection.Deferred("window", Some 99L, 8L)
        let human =
            LifecycleProjection.HumanPark(
                AwaitingHumanAction,
                { Revision = 9L; Reason = "owner action" })
        Assert.Equal(LifecycleProjection.Project(Backlog, 1L), reduce backlog idle)
        Assert.Equal(LifecycleProjection.Project(Backlog, 1L), reduce deferred idle)
        Assert.Equal(LifecycleProjection.Project(Blocked, 1L), reduce human idle)

    [<Fact>]
    let ``M6 human intent cannot be reversed by active-looking observations`` () =
        let human =
            LifecycleProjection.HumanPark(
                AwaitingHumanDecision,
                { Revision = 10L; Reason = "decision required" })
        let pr : LifecycleProjection.PullRequest = { Number = 12; Open = true; ReviewOrCiActive = true }
        let active = { observation with PullRequest = fact (Some pr) }
        Assert.Equal(LifecycleProjection.Project(Blocked, 1L), reduce human active)

    [<Fact>]
    let ``M6 only closed plus verified receipt is Done`` () =
        let closed = { observation with Claim = fact None; Issue = fact Closed }
        match reduce LifecycleProjection.Auto closed with
        | LifecycleProjection.Withheld reason -> Assert.Contains("verified done", reason)
        | other -> failwithf "expected refusal, got %A" other
        let stamped = { closed with Delivery = fact { Outstanding = false; DoneStamped = true } }
        Assert.Equal(LifecycleProjection.Project(Done, 1L), reduce LifecycleProjection.Auto stamped)

    [<Fact>]
    let ``M6 incoherent observation timestamps fail closed`` () =
        let delayed = { observation with PullRequest = { ObservedAt = 2L; Value = None } }
        match reduce LifecycleProjection.Auto delayed with
        | LifecycleProjection.Withheld reason -> Assert.Contains("timestamps", reason)
        | other -> failwithf "expected refusal, got %A" other

    [<Fact>]
    let ``M6 watermark ordering rejects stale and contradictory observations`` () =
        let receipt : LifecycleProjection.Watermark =
            { ObservedAt = 3L; Status = InReview; Intent = LifecycleProjection.Auto }
        match advance LifecycleProjection.Auto (Some receipt) observation with
        | LifecycleProjection.Withheld reason -> Assert.Contains("predates", reason)
        | other -> failwithf "expected stale refusal, got %A" other
        let equal : LifecycleProjection.Watermark =
            { ObservedAt = 1L; Status = InReview; Intent = LifecycleProjection.Auto }
        match advance LifecycleProjection.Auto (Some equal) observation with
        | LifecycleProjection.Withheld reason -> Assert.Contains("conflicts", reason)
        | other -> failwithf "expected conflict refusal, got %A" other

    [<Fact>]
    let ``M6 watermark v2 round trips every intent kind`` () =
        let intents =
            [ LifecycleProjection.Auto
              LifecycleProjection.Backlog { Revision = 2L; Reason = "park reason" }
              LifecycleProjection.HumanPark(AwaitingHumanDecision, { Revision = 3L; Reason = "choose" })
              LifecycleProjection.HumanPark(AwaitingHumanAction, { Revision = 4L; Reason = "act" })
              LifecycleProjection.Deferred("later", Some 44L, 5L) ]
        for intent in intents do
            let value : LifecycleProjection.Watermark = { ObservedAt = 9L; Status = Backlog; Intent = intent }
            Assert.Equal(Some value, LifecycleProjection.tryWatermark [ LifecycleProjection.watermarkMarker value ])

    [<Fact>]
    let ``M6 legacy v1 and quoted or malformed v2 receipts are inert`` () =
        let current : LifecycleProjection.Watermark =
            { ObservedAt = 9L
              Status = Backlog
              Intent = LifecycleProjection.Backlog { Revision = 1L; Reason = "park" } }
        let v1 = "<!-- fsgg:lifecycle-watermark v=1 observedAt=99 status=Done -->"
        let quoted = "example: " + LifecycleProjection.watermarkMarker current
        let malformed = "<!-- fsgg:lifecycle-watermark v=2 observedAt=9 status=Backlog intent=backlog revision=1 until=none reason=%ZZ -->"
        Assert.Equal(None, LifecycleProjection.tryWatermark [ v1; quoted; malformed ])
        Assert.Equal(Some current, LifecycleProjection.tryWatermark [ v1; LifecycleProjection.watermarkMarker current ])

    [<Fact>]
    let ``M6 equal projection and watermark are idempotent`` () =
        let idle = { observation with Claim = fact None }
        let intent = LifecycleProjection.Backlog { Revision = 7L; Reason = "park" }
        let receipt : LifecycleProjection.Watermark = { ObservedAt = 1L; Status = Backlog; Intent = intent }
        Assert.Equal(LifecycleProjection.Project(Backlog, 1L), advance intent (Some receipt) idle)
