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

    // ================================================================================================
    // .github#2712 — THE STANDING-ITEM EXEMPTION, PROVEN TO **BIND**
    //
    // The row this covers is named after `.github#266`'s own defect class: a gate that reports pass while
    // its subject is absent, wrong, or unexamined. So an exemption that merely EXISTS is not what is being
    // asserted here. Each test below drives inputs that WOULD have produced a transition and asserts that
    // none happened — and the `work` wrappers at the top of this module make every other assertion in this
    // file the matching over-application leg, so the pair fails in both directions.
    // ================================================================================================

    /// Facts that are ALL individually sufficient to move a `work` row, so an exemption that leaked at any
    /// one of them would be caught by the corresponding pair below rather than by a single happy case.
    let private drivingObservations : (string * LifecycleProjection.Observation * BoardStatus) list =
        let pr : LifecycleProjection.PullRequest = { Number = 12; Open = true; ReviewOrCiActive = true }
        let blocker = { Ref = Some { Owner = "FS-GG"; Repo = ".github"; Number = 1 }; Raw = "FS-GG/.github#1"; State = BlockerOpen }
        [ "a done receipt on a closed issue projects Done",
          { observation with
              Claim = fact None
              Delivery = fact { Outstanding = false; DoneStamped = true }
              Issue = fact Closed },
          Done

          "a live claim projects In progress", observation, InProgress

          "an open item PR projects In review",
          { observation with Claim = fact None; PullRequest = fact (Some pr) }, InReview

          "an outstanding delivery obligation projects In review",
          { observation with Claim = fact None; Delivery = fact { Outstanding = true; DoneStamped = false } }, InReview

          "an unresolved blocker projects Blocked",
          { observation with Claim = fact None; Blockers = fact [ blocker ] }, Blocked

          "an idle row under Auto projects Ready",
          { observation with Claim = fact None }, Ready ]

    [<Fact>]
    let ``2712 the exemption BINDS — every standing kind is untouched by inputs that WOULD have moved a work row`` () =
        // THE NEGATIVE LEG. Each observation below is independently sufficient to move a `work` row, and
        // the `work` assertion is made in the SAME loop rather than in a sibling test: a fixture that
        // quietly stopped driving a transition would then fail on the `work` arm instead of passing
        // vacuously on the standing one. "Found nothing" and "looked at nothing" do not share an outcome.
        for label, obs, expected in drivingObservations do
            Assert.Equal(LifecycleProjection.Project(expected, 1L), reduceOf Work LifecycleProjection.Auto obs)

            for kind in standingKinds do
                Assert.Equal(LifecycleProjection.Exempt kind, reduceOf kind LifecycleProjection.Auto obs)

                // `advance` too, and not because it delegates: it is asserted separately precisely so a
                // later change that stops delegating cannot silently drop the exemption from the entry
                // point every live caller actually uses. See `%s{label}`.
                Assert.Equal(LifecycleProjection.Exempt kind, advanceOf kind LifecycleProjection.Auto None obs)

    [<Fact>]
    let ``2712 the exemption is not suppressible by a persisted watermark — the freeze cannot reach it`` () =
        // THE TEST THIS DESIGN EXISTS FOR. Independent critic `avocet-e644`'s probe 3 on PR #2718 measured
        // that a `Class`-derived policy arm IS defeated by a pre-existing receipt: a `Class: decision` row
        // carrying the `Backlog` watermark `add` now mints settles at `Backlog` and never reaches
        // `Blocked`, while the identical row with NO receipt reaches `Blocked`. Since .github#2690 every
        // `add`-filed row carries such a receipt, so an exemption expressed as policy intent — or as a
        // `SchedulingIntent` case — would be inert on the whole live board.
        //
        // Every watermark shape is driven, INCLUDING ones whose own `Status` and `Intent` disagree with
        // the exemption and would otherwise win the ordering comparison.
        let watermarks : (string * LifecycleProjection.Watermark) list =
            [ "a frozen human park", { ObservedAt = 5L; Status = Blocked; Intent = LifecycleProjection.HumanPark(AwaitingHumanDecision, { Revision = 5L; Reason = "frozen" }) }
              "a frozen backlog park", { ObservedAt = 5L; Status = BoardStatus.Backlog; Intent = LifecycleProjection.Backlog { Revision = 5L; Reason = "frozen" } }
              "an auto receipt every add-filed row now carries", { ObservedAt = 5L; Status = Ready; Intent = LifecycleProjection.Auto }
              "a receipt from the FUTURE, which would otherwise withhold on ordering", { ObservedAt = 9999L; Status = Done; Intent = LifecycleProjection.Auto }
              "a receipt at the SAME instant claiming a different column, which would otherwise withhold", { ObservedAt = 1L; Status = Done; Intent = LifecycleProjection.Auto } ]

        for label, watermark in watermarks do
            for _, obs, _ in drivingObservations do
                for kind in standingKinds do
                    Assert.Equal(LifecycleProjection.Exempt kind, advanceOf kind LifecycleProjection.Auto (Some watermark) obs)

                    // The watermark's OWN intent, replayed exactly as `Client.lifecycleSelection` replays
                    // it — this is the precise shape the freeze takes at `Client.fs:2492`.
                    Assert.Equal(LifecycleProjection.Exempt kind, advanceOf kind watermark.Intent (Some watermark) obs)

            // NON-VACUITY, and it is the leg that makes the loop above evidence rather than decoration: the
            // SAME watermark against a `work` row must NOT answer `Exempt`. Without this, a fixture whose
            // observations had stopped driving anything would pass every assertion above. See `%s{label}`.
            let workAnswer = advanceOf Work watermark.Intent (Some watermark) observation
            Assert.False(
                (match workAnswer with LifecycleProjection.Exempt _ -> true | _ -> false),
                $"a `work` row must never be exempt — the exemption over-applied under: %s{label}")

    [<Fact>]
    let ``2712 an unexempted row is exactly what it was — Kind.govern reads no declaration as work`` () =
        // THE OVER-APPLICATION LEG, stated once as a property rather than only implied by the wrappers:
        // every row on this board declares no `Kind:` line, so `govern None` must be `Work` and the reducer
        // must give the identical answer it gave before the exemption existed.
        Assert.Equal(Work, Kind.govern None)
        Assert.Equal(Work, Kind.govern (Some Work))

        for _, obs, expected in drivingObservations do
            Assert.Equal(
                LifecycleProjection.Project(expected, 1L),
                reduceOf (Kind.govern None) LifecycleProjection.Auto obs)

    [<Fact>]
    let ``2712 no watermark is derivable from an exempt result — the receipt half of the exemption`` () =
        // AC2 says "no park, no promote, no `Done`, no watermark". The first three are the `Project`
        // absence above; this is the fourth. A `Watermark` is built from a `Project`'s status and
        // timestamp, so an `Exempt` result carries no material from which one could be constructed — and
        // that is asserted here rather than left to the reader of `Client.fs`, because the reconcile arm
        // that must skip the watermark map is the one place a later edit could re-introduce it.
        for kind in standingKinds do
            match advanceOf kind LifecycleProjection.Auto None observation with
            | LifecycleProjection.Exempt k -> Assert.Equal(kind, k)
            | other -> failwithf "a %A row must be Exempt, not %A" kind other

    [<Fact>]
    let ``2712 the recorded residual bound is the one the reducer actually has`` () =
        // THE PROSE IS THE SUBJECT HERE, NOT THE CODE. `clarifications.md` DEC-003 accepts a bounded
        // residual: a row whose BODY could not be read is not known to be standing, so it reaches the
        // reducer as `Work` and is treated exactly as today. The code is right and unchanged. What was
        // wrong was the recorded SIZE of that residual — an earlier draft wrote it as
        // `TouchSet.Unreadable -> Deferred -> Backlog`, and independent critic `tern-ca7d` refuted the
        // parenthetical by execution in round 1.
        //
        // `Deferred -> Backlog` is only `projectWithIntent`'s LAST-RESORT arm. FOUR observation-driven
        // arms sit ABOVE the intent dispatch, and the first of them is the one that matters: a CLOSED
        // issue carrying a done receipt projects `Done` whatever the intent is. So the true bound is
        // `Done` — which is exactly the state this row was filed to stop (`.github#2106` sitting `Done`
        // while its issue must stay `OPEN`).
        //
        // THIS TEST EXISTS SO THE RECORDED BOUND CANNOT DRIFT FROM THE CODE AGAIN. A prose bound nothing
        // executes is a claim about behaviour with no gate behind it, which is `.github#266`'s class
        // landing on the declaration layer instead of on a gate. If a later change moves this arm, this
        // fails and DEC-003 must be re-stated rather than quietly becoming false a second time.
        let deferred = LifecycleProjection.Deferred("touch-set unreadable: the body could not be read", None, 1L)

        let closedWithReceipt =
            { observation with
                Claim = fact None
                Delivery = fact { Outstanding = false; DoneStamped = true }
                Issue = fact Closed }

        // The unreadable-body reading, spelled the way the engine spells it.
        Assert.Equal(Work, Kind.govern None)

        // THE BOUND, at its true size — and asserted as `Done` rather than merely "not Backlog", so the
        // test names the outcome an operator would have to see rather than a category it avoids.
        Assert.Equal(LifecycleProjection.Project(Done, 1L), reduceOf (Kind.govern None) deferred closedWithReceipt)
        Assert.Equal(LifecycleProjection.Project(Done, 1L), advanceOf (Kind.govern None) deferred None closedWithReceipt)

        // A WATERMARK DOES NOT CHANGE IT — the observation arm is above the intent dispatch, so the
        // receipt is irrelevant here for the same structural reason the exemption is immune to it.
        //
        // STRICTLY OLDER (`0L` against the observation's `1L`), and that is not incidental: this
        // fixture first used the SAME instant, and `advance`'s ordering rule withheld on the
        // same-timestamp/different-status conflict BEFORE the residual could be observed at all. That is
        // the ordering guarantee working, not the residual — and a fixture that withheld for an
        // unrelated reason would have pinned nothing while looking green. `standingKinds` below uses the
        // same watermark, where the exemption outranks the ordering rule regardless.
        let stale: LifecycleProjection.Watermark =
            { ObservedAt = 0L; Status = BoardStatus.Backlog; Intent = deferred }

        Assert.Equal(LifecycleProjection.Project(Done, 1L), advanceOf (Kind.govern None) deferred (Some stale) closedWithReceipt)

        // AND THE REFUTED FORM IS PINNED AS REFUTED. Asserting the positive alone would still pass if a
        // later change made `Backlog` reachable here too; this says the old recorded bound is wrong.
        Assert.NotEqual<LifecycleProjection.Result>(
            LifecycleProjection.Project(BoardStatus.Backlog, 1L),
            reduceOf (Kind.govern None) deferred closedWithReceipt)

        // THE CONTRAST THAT MAKES THE RESIDUAL A RESIDUAL RATHER THAN A HOLE: the identical row whose
        // body WAS read and DECLARES its kind is exempt. The residual is the unreadable body, nothing
        // wider — which is what keeps DEC-003's acceptance honest.
        for kind in standingKinds do
            Assert.Equal(LifecycleProjection.Exempt kind, advanceOf kind deferred (Some stale) closedWithReceipt)
