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
    let ``#2264 unresolved blocker wins over an implementation claim`` () =
        let blocker = { Ref = None; Raw = "human action"; State = BlockerUnparseable }
        let value = { observation with Blockers = fact [ blocker ] }
        Assert.Equal(LifecycleProjection.Project(Blocked, 1L), LifecycleProjection.project value)

    [<Fact>]
    let ``#2264 expired orphan claim no longer projects active work`` () =
        let value = { observation with Claim = fact (Some(claim, LeaseExpiredNoPr)) }
        Assert.Equal(LifecycleProjection.Project(Ready, 1L), LifecycleProjection.project value)
