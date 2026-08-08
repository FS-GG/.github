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
    let ``#2264 unresolved blocker wins over an implementation claim`` () =
        let blocker = { Ref = None; Raw = "human action"; State = BlockerUnparseable }
        let value = { observation with Blockers = fact [ blocker ] }
        Assert.Equal(LifecycleProjection.Project(Blocked, 1L), LifecycleProjection.project value)
