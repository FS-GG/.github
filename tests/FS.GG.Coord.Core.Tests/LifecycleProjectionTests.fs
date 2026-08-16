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

    /// THE `work` WRAPPERS — and pinning `Work` here is not a convenience, it is the OVER-APPLICATION
    /// LEG of .github#2712 AC6, executed by every assertion already in this file. A `work` row must park
    /// and promote exactly as it did before the exemption existed; these wrappers make every pre-existing
    /// test in this module a statement of that, so an exemption that over-applied by one case would red
    /// the whole suite rather than pass quietly.
    let reduce intent value =
        LifecycleProjection.reduce LifecycleProjection.IntentStatusV1 Work intent value

    let advance intent watermark value =
        LifecycleProjection.advance LifecycleProjection.IntentStatusV1 Work intent watermark value

    /// The same reducer with the kind under test — used only by the exemption tests below.
    let reduceOf kind intent value =
        LifecycleProjection.reduce LifecycleProjection.IntentStatusV1 kind intent value

    let advanceOf kind intent watermark value =
        LifecycleProjection.advance LifecycleProjection.IntentStatusV1 kind intent watermark value

    /// Every STANDING kind, DERIVED from the union rather than listed, so a fifth `ItemKind` reaches
    /// every assertion below the day it is declared. A hand-written `[ Anchor; Register; Directive ]`
    /// would pass forever while silently not testing a case somebody added — which is the class
    /// `.github#266` owns and the one this row must not commit.
    let standingKinds = Kind.legalKinds |> List.filter Kind.isStanding

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
    let ``M6 typed human park authorizes Blocked without prose authority`` () =
        let human =
            LifecycleProjection.HumanPark(
                AwaitingHumanDecision,
                { Revision = 10L; Reason = "decision required" })
        Assert.True(LifecycleProjection.isHumanPark human)
        Assert.False(LifecycleProjection.isHumanPark LifecycleProjection.Auto)
        Assert.False(
            LifecycleProjection.isHumanPark(
                LifecycleProjection.Backlog { Revision = 10L; Reason = "policy backlog" }))

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

    // ---- .github#2690: the operator-writable intent channel, as a pure rule --------------------------

    [<Fact>]
    let ``2690 an explicit Ready or Backlog write mints the intent that reproduces it`` () =
        Assert.Equal(
            Some
                ({ ObservedAt = 500L
                   Status = Ready
                   Intent = LifecycleProjection.Auto }: LifecycleProjection.Watermark),
            LifecycleProjection.explicitStatusWatermark 500L "operator said so" Ready
        )

        Assert.Equal(
            Some
                ({ ObservedAt = 500L
                   Status = Backlog
                   Intent = LifecycleProjection.Backlog { Revision = 500L; Reason = "operator said so" } }
                : LifecycleProjection.Watermark),
            LifecycleProjection.explicitStatusWatermark 500L "operator said so" Backlog
        )

    [<Fact>]
    let ``2690 no other column records an intent, and each None has its own reason`` () =
        // `Blocked` is the one value that never had this defect: `requireCoherentParkIfBlocked` refuses the
        // write unless the row carries a durable `Blocked by` or a `Blocked on: human/...` sentinel, and
        // BOTH are re-derived every pass. Minting a `HumanPark` here would be strictly worse than the
        // defect — `projectWithIntent` tests `isHumanPark` ABOVE the blocker observation, so the park could
        // never again be lifted by closing the blocker that justified it. The two assertions below are that
        // argument, executed: the frozen park outranks a cleared blocker, and no intent is minted for it.
        let openBlocker =
            { Ref = Some { Owner = "FS-GG"; Repo = ".github"; Number = 1 }
              Raw = "FS-GG/.github#1"
              State = BlockerOpen }

        let idle = { observation with Claim = fact None }
        let blocked = { idle with Blockers = fact [ openBlocker ] }
        let cleared = { idle with Blockers = fact [ { openBlocker with State = BlockerClosed } ] }
        let frozenPark = LifecycleProjection.HumanPark(AwaitingHumanDecision, { Revision = 1L; Reason = "frozen" })

        Assert.Equal(LifecycleProjection.Project(Blocked, 1L), reduce LifecycleProjection.Auto blocked)
        // The blocker is gone and `Auto` lets the row go Ready; the same observation under a frozen park
        // stays Blocked forever. That is what minting one here would buy.
        Assert.Equal(LifecycleProjection.Project(Ready, 1L), reduce LifecycleProjection.Auto cleared)
        Assert.Equal(LifecycleProjection.Project(Blocked, 1L), reduce frozenPark cleared)
        Assert.Equal(None, LifecycleProjection.explicitStatusWatermark 500L "r" Blocked)

        // The three observation-projected columns. Intent does not decide any of them — every one is
        // reached ABOVE `projectWithIntent`'s `match intent` — so a watermark here would record something
        // the reducer never reads for that column while still suppressing policy re-derivation later.
        for status in [ InProgress; InReview; Done ] do
            Assert.Equal(None, LifecycleProjection.explicitStatusWatermark 500L "r" status)

        // Not a column anyone can choose: `Reads.statusOfName` never yields it.
        Assert.Equal(None, LifecycleProjection.explicitStatusWatermark 500L "r" NoStatus)

    [<Fact>]
    let ``2690 a fresh explicit intent outranks the frozen watermark that was reverting the row`` () =
        // THE #2695 SHAPE, AS THE PURE CORE SEES IT — the exact pair of markers measured on that row:
        // `observedAt` advanced from 1786843796660 to 1786875759540 while `revision` stayed frozen at
        // 1786843796660, replaying a nine-hour-old `decision-class work requires a human decision` against
        // a row whose Class had read `hardening` for ten minutes. `lifecycleSelection` consults policy ONLY
        // when there is no watermark at all, so the reclass could not be seen. What repairs it is not a new
        // policy read — it is a NEWER receipt, which is the whole reason `explicitStatusWatermark` takes the
        // reducer's own clock.
        let frozen: LifecycleProjection.Watermark =
            { ObservedAt = 1786875759540L
              Status = Blocked
              Intent =
                LifecycleProjection.HumanPark(
                    AwaitingHumanDecision,
                    { Revision = 1786843796660L
                      Reason = "decision-class work requires a human decision" }) }

        let operator =
            match LifecycleProjection.explicitStatusWatermark 1786875800000L "explicit set-field by rook-2cdb" Ready with
            | Some w -> w
            | None -> failwith "an explicit Ready write must record an intent"

        let comments =
            [ LifecycleProjection.watermarkMarker frozen
              LifecycleProjection.watermarkMarker operator ]

        // BEFORE the operator write, the frozen park is what the next pass reads back, and it re-parks a
        // row nobody asked to park. This half is the defect, asserted rather than described.
        Assert.Equal(Some frozen, LifecycleProjection.tryWatermark [ List.head comments ])

        let winner =
            match LifecycleProjection.tryWatermark comments with
            | Some w -> w
            | None -> failwith "two well-formed receipts must parse"

        Assert.Equal(operator, winner)

        // And the reducer honours it: an idle row under the recovered intent projects Ready, where the
        // frozen park projected Blocked against the identical observation.
        let idle = { observation with Claim = fact None }
        Assert.Equal(LifecycleProjection.Project(Ready, 1L), reduce winner.Intent idle)
        Assert.Equal(LifecycleProjection.Project(Blocked, 1L), reduce frozen.Intent idle)

    [<Fact>]
    let ``2690 the recorded intent survives its own wire round trip`` () =
        // The channel is only as good as the marker it writes: a receipt that does not parse back is an
        // intent that was never recorded. Both minted shapes go out and come back identical.
        for status in [ Ready; Backlog ] do
            let minted =
                match LifecycleProjection.explicitStatusWatermark 4242L "park: nothing is owed yet" status with
                | Some w -> w
                | None -> failwithf "%A must record an intent" status

            Assert.Equal(Some minted, LifecycleProjection.tryWatermark [ LifecycleProjection.watermarkMarker minted ])
