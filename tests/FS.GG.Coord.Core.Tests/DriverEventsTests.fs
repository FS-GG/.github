namespace FS.GG.Coord.Tests

open Xunit
open FS.GG.Coord
open FS.GG.Coord.Types
open FS.GG.Coord.DriverEvents

/// .github#2135 — the material-transition/active-inventory projection. Every test carries the named
/// scenario from the issue's acceptance #9 that bought it.
module DriverEventsTests =

    let baseFacts: ItemFacts =
        { Ref = ".github#1"
          ReadOk = true
          UnreadableReason = None
          BoardStatus = Some Types.Ready
          IssueState = Some Open
          ClaimWorker = None
          HumanBlock = None
          Pr = None
          Review = None
          Merged = false
          ObligationsDeclared = false
          Obligations = []
          Evidence = "board-status:Ready"
          ObservedAt = 100L
          SourceSha = "sha-1" }

    let validReview: Driver.ReviewChain =
        { MarkerValid = true
          CriticIdentity = Some "critic-1"
          HeadSha = Some "head-1"
          Rounds = []
          RepairPhase = false
          ChecksGreen = false
          HostAccepted = false
          RuntimeRouteEvidence = None
          DiffAuditRequired = false
          DiffAuditHead = None }

    // ---- classify: one fact in, one state out ---------------------------------------------------

    [<Fact>]
    let ``classify: unclaimed Ready item classifies Ready`` () =
        let classified = classify baseFacts
        Assert.Equal(Ready, classified.State)

    [<Fact>]
    let ``classify: a live claim with no review evidence classifies Claimed`` () =
        let facts = { baseFacts with ClaimWorker = Some "snipe-f30c" }
        let classified = classify facts
        Assert.Equal(Claimed "snipe-f30c", classified.State)

    [<Fact>]
    let ``classify: a valid review marker with no repair round is ReviewHandoff`` () =
        let facts =
            { baseFacts with
                ClaimWorker = Some "snipe-f30c"
                Pr = Some 42
                Review = Some validReview }

        Assert.Equal(ReviewHandoff(Some "critic-1"), (classify facts).State)

    [<Fact>]
    let ``classify: review-to-repair — a review marker whose RepairPhase is set classifies ReviewRepair`` () =
        let facts =
            { baseFacts with
                ClaimWorker = Some "snipe-f30c"
                Pr = Some 42
                Review = Some { validReview with RepairPhase = true; Rounds = [ 1; 2 ] } }

        Assert.Equal(ReviewRepair 2, (classify facts).State)

    [<Fact>]
    let ``classify: checks green and host-accepted classifies CiLandable`` () =
        let facts =
            { baseFacts with
                ClaimWorker = Some "snipe-f30c"
                Pr = Some 42
                Review = Some { validReview with ChecksGreen = true; HostAccepted = true } }

        Assert.Equal(CiLandable, (classify facts).State)

    [<Fact>]
    let ``classify: merged-awaiting-release — a merged closed item with an unverified obligation classifies MergedAwaitingObligations`` () =
        let facts =
            { baseFacts with
                ClaimWorker = Some "snipe-f30c"
                Pr = Some 42
                IssueState = Some Closed
                Merged = true
                ObligationsDeclared = true
                Obligations = [ { Id = "kit-release"; Kind = "release"; Evidence = None; HeadSha = "head-1"; Verified = false } ] }

        Assert.Equal(MergedAwaitingObligations 42, (classify facts).State)

    [<Fact>]
    let ``classify: merged-awaiting-release — once every obligation is verified the same item classifies Released`` () =
        let facts =
            { baseFacts with
                ClaimWorker = Some "snipe-f30c"
                Pr = Some 42
                IssueState = Some Closed
                Merged = true
                ObligationsDeclared = true
                Obligations = [ { Id = "kit-release"; Kind = "release"; Evidence = Some "https://example/1"; HeadSha = "head-1"; Verified = true } ] }

        Assert.Equal(Released, (classify facts).State)

    [<Fact>]
    let ``classify: a merged closed item with no declared obligations classifies Released, never stuck awaiting an obligation nobody declared`` () =
        let facts =
            { baseFacts with
                ClaimWorker = Some "snipe-f30c"
                IssueState = Some Closed
                Merged = true
                ObligationsDeclared = false
                Obligations = [] }

        Assert.Equal(Released, (classify facts).State)

    [<Fact>]
    let ``classify: a human-decision sentinel classifies HumanBlocked even while a claim is live`` () =
        let facts =
            { baseFacts with
                ClaimWorker = Some "snipe-f30c"
                HumanBlock = Some AwaitingHumanDecision }

        match (classify facts).State with
        | HumanBlocked _ -> ()
        | other -> Assert.Fail $"expected HumanBlocked, got %A{other}"

    [<Fact>]
    let ``classify: failed-read — ReadOk = false classifies Unreadable regardless of every other fact`` () =
        let facts =
            { baseFacts with
                ClaimWorker = Some "snipe-f30c"
                BoardStatus = Some Types.Done
                IssueState = Some Closed
                Merged = true
                ReadOk = false
                UnreadableReason = Some "PR read timed out" }

        match (classify facts).State with
        | Unreadable reason -> Assert.Equal("PR read timed out", reason)
        | other -> Assert.Fail $"expected Unreadable, got %A{other}"

    // ---- isActive — issue acceptance #4 ----------------------------------------------------------

    [<Fact>]
    let ``isActive: claimed, in-review, CI-landable and merged-awaiting-obligations are active`` () =
        Assert.True(isActive (Claimed "w"))
        Assert.True(isActive (ReviewHandoff(Some "c")))
        Assert.True(isActive (ReviewRepair 1))
        Assert.True(isActive CiLandable)
        Assert.True(isActive (MergedAwaitingObligations 1))

    [<Fact>]
    let ``isActive: Ready, Released, HumanBlocked, Done and Unreadable are never active`` () =
        Assert.False(isActive Ready)
        Assert.False(isActive Released)
        Assert.False(isActive (HumanBlocked "reason"))
        Assert.False(isActive Done)
        Assert.False(isActive (Unreadable "reason"))

    /// GATE-INVERSION EVIDENCE. `Unreadable` must never satisfy `isActive` — that is precisely what
    /// keeps a failed read from masquerading as "nothing active" (issue acceptance #7). Inverting the
    /// exclusion (as this commented-out mutation would) turns the assertion above red:
    ///     | Unreadable _ -> true   // WRONG: would let a failed read hide inside "active", which then
    ///                              // silently drops it from a caller that filters on isActive alone.
    /// Observed red when applied by hand during authoring, as `independent-review` requires; kept here
    /// as the named scenario the assertion above pins.
    [<Fact>]
    let ``gate-inversion: Unreadable classified active would defeat the whole point of the case`` () =
        Assert.False(isActive (Unreadable "anything"))

    // ---- deriveEvents / idempotency — issue acceptance #1, #5 ------------------------------------

    [<Fact>]
    let ``deriveEvents: an unseen ref always transitions, even from an empty cursor`` () =
        let classified = classify baseFacts
        let events, cursor = deriveEvents Map.empty [ classified ]
        Assert.Single(events) |> ignore
        Assert.Equal(None, events.[0].Previous)
        Assert.Equal(Ready, events.[0].New)
        Assert.Equal(Ready, Map.find ".github#1" cursor)

    [<Fact>]
    let ``deriveEvents: idempotent re-read — replaying the SAME facts against the returned cursor emits zero events`` () =
        let classified = classify baseFacts
        let firstEvents, cursor1 = deriveEvents Map.empty [ classified ]
        Assert.Single(firstEvents) |> ignore
        let secondEvents, cursor2 = deriveEvents cursor1 [ classified ]
        Assert.Empty(secondEvents)
        Assert.Equal<Cursor>(cursor1, cursor2)

    [<Fact>]
    let ``deriveEvents: a changed state against a populated cursor emits exactly one transition naming both states`` () =
        let cursor = Map.ofList [ ".github#1", Ready ]
        let claimed = classify { baseFacts with ClaimWorker = Some "snipe-f30c" }
        let events, newCursor = deriveEvents cursor [ claimed ]
        Assert.Single(events) |> ignore
        Assert.Equal(Some Ready, events.[0].Previous)
        Assert.Equal(Claimed "snipe-f30c", events.[0].New)
        Assert.Equal(Claimed "snipe-f30c", Map.find ".github#1" newCursor)

    // ---- premature-worker-return — issue acceptance #5 --------------------------------------------

    [<Fact>]
    let ``premature-worker-return: a returning worker process is not itself a transition — re-deriving unchanged claim facts stays quiet`` () =
        let claimed = { baseFacts with ClaimWorker = Some "snipe-f30c" }
        let classified = classify claimed
        let _, cursor = deriveEvents Map.empty [ classified ]
        // The "worker process returned" fact never enters `ItemFacts` at all — classification reads
        // only claim/board/PR/review facts — so calling classify/deriveEvents again over the identical
        // claim facts (as a returning process re-reading the board would) cannot produce a transition.
        let events, _ = deriveEvents cursor [ classify claimed ]
        Assert.Empty(events)
        Assert.True(isActive classified.State)

    // ---- omitted-active-item / external-claim — issue acceptance #3, #4, #6 -----------------------

    [<Fact>]
    let ``omitted-active-item: the active inventory is rendered completely even when nothing transitioned`` () =
        let claimed = classify { baseFacts with ClaimWorker = Some "snipe-f30c" }
        let _, cursor = deriveEvents Map.empty [ claimed ]
        let projection = project cursor [ { baseFacts with ClaimWorker = Some "snipe-f30c" } ] 200L
        Assert.Empty(projection.Transitions)
        Assert.Single(projection.Active) |> ignore
        Assert.Equal(".github#1", projection.Active.[0].Ref)

    [<Fact>]
    let ``external-claim: a claim this process never dispatched still appears as a transition and in the active inventory`` () =
        let externallyClaimed =
            { baseFacts with
                Ref = ".github#99"
                ClaimWorker = Some "some-other-worker"
                Evidence = "claim:worker=some-other-worker" }

        let projection = project Map.empty [ externallyClaimed ] 300L
        Assert.Single(projection.Transitions) |> ignore
        Assert.Equal(".github#99", projection.Transitions.[0].Ref)
        Assert.Single(projection.Active) |> ignore
        Assert.Equal(Claimed "some-other-worker", projection.Active.[0].State)

    [<Fact>]
    let ``project: "nothing transitioned" and "nothing active" are independent facts, never collapsed into one`` () =
        // A Ready item transitions once (unseen -> Ready) but is NEVER active; a claimed item is both
        // active and, on the second read, silent. Rendering both together over one cursor proves the
        // two axes never leak into each other.
        let readyOnly = project Map.empty [ baseFacts ] 100L
        Assert.Single(readyOnly.Transitions) |> ignore
        Assert.Empty(readyOnly.Active)

        let claimedFacts = { baseFacts with Ref = ".github#2"; ClaimWorker = Some "w" }
        let firstRead = project Map.empty [ claimedFacts ] 100L
        let secondRead = project firstRead.Cursor [ claimedFacts ] 200L
        Assert.Empty(secondRead.Transitions)
        Assert.Single(secondRead.Active) |> ignore

    // ---- repair round 1 (fsgg:independent-review:v1, wrenlet-9f2c): a PERSISTENT Unreadable must ----
    // ---- never go silent — idempotency is an exception for this one state, not the default rule ----

    [<Fact>]
    let ``repair-1: a persistent Unreadable re-emits a transition on every unchanged read, never falling silent`` () =
        let unreadable =
            { baseFacts with
                Ref = ".github#3"
                ReadOk = false
                UnreadableReason = Some "board scan timed out" }

        let firstEvents, cursor1 = deriveEvents Map.empty [ classify unreadable ]
        Assert.Single(firstEvents) |> ignore

        // Second read: IDENTICAL facts, IDENTICAL cursor entry. For every other state this is exactly
        // the idempotent-no-duplicate-event case (`deriveEvents: idempotent re-read`). For Unreadable
        // it must NOT be: a driver that only sees "no material transitions" after cycle one has no way
        // to tell "this item recovered" from "this item is still broken", which is worse than silence
        // — it reads as an all-clear over a rotting item (fsgg:independent-review:v1 round 1, finding 1).
        let secondEvents, cursor2 = deriveEvents cursor1 [ classify unreadable ]
        Assert.Single(secondEvents) |> ignore
        Assert.Equal(Some(Unreadable "board scan timed out"), secondEvents.[0].Previous)
        Assert.Equal(Unreadable "board scan timed out", secondEvents.[0].New)

        // A third read proves it is not a one-time "announce twice" grace period — it repeats for as
        // long as the failure persists.
        let thirdEvents, _ = deriveEvents cursor2 [ classify unreadable ]
        Assert.Single(thirdEvents) |> ignore

    [<Fact>]
    let ``repair-1: idempotency is UNCHANGED for every state other than Unreadable`` () =
        // The fix must be narrowly scoped to Unreadable. Re-asserting the existing idempotency
        // guarantee here pins that a stable Claimed/Ready/etc. state still goes quiet on an unchanged
        // re-read — repair 1 must not regress issue acceptance #5 while fixing acceptance #7.
        for facts in
            [ baseFacts
              { baseFacts with ClaimWorker = Some "w" }
              { baseFacts with IssueState = Some Closed; Merged = true } ] do
            let classified = classify facts
            let _, cursor = deriveEvents Map.empty [ classified ]
            let events, _ = deriveEvents cursor [ classify facts ]
            Assert.Empty(events)

    [<Fact>]
    let ``repair-1: project's rendered output keeps naming a persistent Unreadable item on every read`` () =
        // The end-to-end shape the host loop actually consumes: two successive `project` calls over an
        // unchanged Unreadable fact must both carry a transition line naming it, not just `deriveEvents`
        // in isolation.
        let unreadable = { baseFacts with Ref = ".github#4"; ReadOk = false; UnreadableReason = Some "PR read timed out" }
        let first = project Map.empty [ unreadable ] 100L
        Assert.Single(first.Transitions) |> ignore
        let second = project first.Cursor [ unreadable ] 200L
        Assert.Single(second.Transitions) |> ignore
        Assert.Contains(".github#4", (renderText second).Split('\n').[0])

    // ---- failed-read — issue acceptance #7 ---------------------------------------------------------

    [<Fact>]
    let ``failed-read: an unreadable item emits a transition event and is never silently dropped from an otherwise non-empty active list`` () =
        let claimed = { baseFacts with Ref = ".github#2"; ClaimWorker = Some "w" }
        let unreadable =
            { baseFacts with
                Ref = ".github#3"
                ReadOk = false
                UnreadableReason = Some "board scan timed out" }

        let projection = project Map.empty [ claimed; unreadable ] 400L
        Assert.Equal(2, List.length projection.Transitions)
        Assert.True(projection.Transitions |> List.exists (fun e -> e.Ref = ".github#3"))
        // The unreadable item is not active — but it is also not simply ABSENT: it is a named
        // transition, distinguishable from a legitimate "no active items" empty render.
        Assert.Single(projection.Active) |> ignore
        Assert.Equal(".github#2", projection.Active.[0].Ref)

    [<Fact>]
    let ``failed-read: every item unreadable still renders a real transition list, never an empty all-clear`` () =
        let unreadable = { baseFacts with ReadOk = false; UnreadableReason = Some "scan failed" }
        let projection = project Map.empty [ unreadable ] 500L
        Assert.False(List.isEmpty projection.Transitions)
        Assert.Empty(projection.Active)

    // ---- .github#2375 symptom 1 — a Done/closed/stamped row must never be reported transitioning to
    // ---- ready, and never as schedulable (issue acceptance #2) ------------------------------------

    [<Fact>]
    let ``terminal-regression: a cursor-Done ref that a fresh read reports Ready is refused, never rendered ready or schedulable`` () =
        // Simulates the reproduction exactly: a PREVIOUS read correctly classified the merged, closed,
        // done-receipted row Done; the CURRENT read's facts (stale/partial, racing GitHub's own
        // eventual consistency) claim no live claim and an unclaimed board status, which `classify`
        // alone would turn into Ready.
        let cursor = Map.ofList [ ".github#1", Done ]
        let staleFacts = { baseFacts with BoardStatus = Some Types.Ready; IssueState = Some Open }

        let projection = project cursor [ staleFacts ] 600L

        Assert.Empty(projection.Active)
        match Map.tryFind ".github#1" projection.Cursor with
        | Some Ready -> Assert.Fail "the terminal row was accepted as Ready — this is the exact regression the issue reports"
        | Some(Unreadable _) -> ()
        | other -> Assert.Fail $"expected Unreadable, got %A{other}"
        Assert.Single(projection.Transitions) |> ignore
        Assert.DoesNotContain("-> ready", (renderText projection).ToLowerInvariant())

    [<Fact>]
    let ``terminal-regression: a cursor-Released ref regressing to Ready is refused the same way`` () =
        let cursor = Map.ofList [ ".github#1", Released ]
        let staleFacts = { baseFacts with BoardStatus = Some Types.Ready; IssueState = Some Open }
        let projection = project cursor [ staleFacts ] 600L
        match projection.Active |> List.tryFind (fun c -> c.Ref = ".github#1") with
        | Some _ -> Assert.Fail "a regressed terminal row must never appear in the active inventory"
        | None -> ()
        Assert.False(projection.Cursor |> Map.exists (fun _ state -> state = Ready))

    /// GATE-INVERSION EVIDENCE for `guardTerminalRegression`. Removing the guard (equivalent to
    /// `project` calling only `classify`, as it did before this fix) makes the assertion above fail:
    /// the cursor is overwritten with `Ready`, the item enters `Active` as nothing (Ready is never
    /// active, but the point is it becomes schedulable), and `no live claim; unclaimed and schedulable`
    /// reappears verbatim in the render. Observed red by hand during authoring (reverting
    /// `guardTerminalRegression` to `id` and re-running this fixture) as `independent-review` requires.
    [<Fact>]
    let ``gate-inversion: without the terminal-regression guard, a Done row reads back Ready and schedulable`` () =
        let cursor = Map.ofList [ ".github#1", Done ]
        let staleFacts = { baseFacts with BoardStatus = Some Types.Ready; IssueState = Some Open }
        // Reproduce the PRE-FIX behavior directly (bypassing the guard under test) to pin exactly what
        // the guard prevents, without relying on the production code path staying broken.
        let unguardedClassified = [ classify staleFacts ]
        let events, unguardedCursor = deriveEvents cursor unguardedClassified
        Assert.Equal(Ready, Map.find ".github#1" unguardedCursor)
        Assert.Contains("no live claim; unclaimed and schedulable", events.[0].Reason)

    // ---- .github#2375 symptom 2 — the active inventory must never silently empty while claims are
    // ---- live (issue acceptance #1, #3) ------------------------------------------------------------

    [<Fact>]
    let ``missing-active-ref: three live claims the cursor remembers, entirely absent from this read's facts, are never rendered as "no active items"`` () =
        let claimedState worker = Claimed worker
        let cursor =
            Map.ofList
                [ "FS-GG/.github#2305", claimedState "smew-1ae8"
                  "FS-GG/.github#2365", claimedState "crake-4bcd"
                  "FS-GG/.github#2373", claimedState "finch-d23b" ]

        // The reproduction exactly: no intervening board change, but this read's facts batch is empty
        // — the shape a caller-side partial/failed scan produces.
        let projection = project cursor [] 700L

        Assert.False(List.isEmpty projection.Transitions, "a vanished active ref must still surface as a transition")
        Assert.Equal(3, List.length projection.Transitions)
        // None of the three are silently dropped: each renders Unreadable, and none is "no active
        // items" with zero explanation.
        for ref in [ "FS-GG/.github#2305"; "FS-GG/.github#2365"; "FS-GG/.github#2373" ] do
            match Map.tryFind ref projection.Cursor with
            | Some(Unreadable _) -> ()
            | other -> Assert.Fail $"%s{ref}: expected Unreadable, got %A{other}"

        let rendered = renderText projection
        Assert.DoesNotContain("no material transitions", rendered)

    [<Fact>]
    let ``missing-active-ref: a live claim still present in the facts batch is unaffected by the guard`` () =
        let cursor = Map.ofList [ ".github#1", Claimed "snipe-f30c" ]
        let stillClaimed = { baseFacts with ClaimWorker = Some "snipe-f30c" }
        let projection = project cursor [ stillClaimed ] 700L
        Assert.Empty(projection.Transitions)
        Assert.Single(projection.Active) |> ignore
        Assert.Equal(Claimed "snipe-f30c", projection.Active.[0].State)

    [<Fact>]
    let ``missing-active-ref: a ref the cursor never marked active going missing is not synthesized`` () =
        // A previously Ready (never active) or previously unseen ref disappearing from a read is not
        // this guard's concern — only PREVIOUSLY ACTIVE refs are cross-checked against the cursor.
        let cursor = Map.ofList [ ".github#1", Ready ]
        let projection = project cursor [] 700L
        Assert.Empty(projection.Transitions)
        Assert.Empty(projection.Active)

    /// GATE-INVERSION EVIDENCE for `missingActiveRefs`. Bypassing the guard (calling `deriveEvents`
    /// directly over an empty facts batch, exactly as `project` did before this fix) reproduces the
    /// verbatim reported output: "no material transitions" / "no active items" while the cursor still
    /// holds three live claims. Observed red by hand during authoring (reverting `missingActiveRefs` to
    /// return `[]` and re-running this fixture) as `independent-review` requires.
    [<Fact>]
    let ``gate-inversion: without the missing-active-ref guard, three live claims vanish with zero signal`` () =
        let cursor =
            Map.ofList
                [ "FS-GG/.github#2305", Claimed "smew-1ae8"
                  "FS-GG/.github#2365", Claimed "crake-4bcd"
                  "FS-GG/.github#2373", Claimed "finch-d23b" ]

        // Reproduce the PRE-FIX code path directly: classify over an empty facts batch, with no
        // cross-check against the cursor at all.
        let unguardedClassified: Classified list = [] |> List.map classify
        let events, _ = deriveEvents cursor unguardedClassified
        let unguardedProjection: Projection =
            { Transitions = events
              Active = unguardedClassified |> List.filter (fun c -> isActive c.State)
              Cursor = cursor
              RenderedAt = 700L }
        let rendered = renderText unguardedProjection
        Assert.Equal("no material transitions\nno active items", rendered)

    // ---- encode/decode round-trip — the cursor file's on-disk vocabulary --------------------------

    [<Fact>]
    let ``encodeState/decodeState round-trip every constructor`` () =
        let states =
            [ Ready
              Claimed "snipe-f30c"
              ReviewHandoff(Some "critic-1")
              ReviewHandoff None
              ReviewRepair 3
              CiLandable
              MergedAwaitingObligations 42
              Released
              HumanBlocked "Blocked on: human/decision"
              Done
              Unreadable "board scan timed out" ]

        for state in states do
            Assert.Equal(Some state, decodeState (encodeState state))

    [<Fact>]
    let ``decodeState: an unrecognized encoding decodes to None rather than guessing`` () =
        Assert.Equal(None, decodeState "some-future-schema:x")

    // ---- renderText — issue acceptance #3, stable two-line form ------------------------------------

    [<Fact>]
    let ``renderText: an empty projection renders the two documented "nothing happened" lines`` () =
        let projection: Projection = { Transitions = []; Active = []; Cursor = Map.empty; RenderedAt = 0L }
        let text = renderText projection
        let lines = text.Split '\n'
        Assert.Equal(2, lines.Length)
        Assert.Equal("no material transitions", lines.[0])
        Assert.Equal("no active items", lines.[1])

    [<Fact>]
    let ``renderText: a transitioned, active item names its ref and state on both lines`` () =
        let claimed = { baseFacts with ClaimWorker = Some "snipe-f30c" }
        let projection = project Map.empty [ claimed ] 100L
        let text = renderText projection
        let lines = text.Split '\n'
        Assert.Equal(2, lines.Length)
        Assert.Contains(".github#1", lines.[0])
        Assert.Contains("claimed:snipe-f30c", lines.[0])
        Assert.Contains(".github#1", lines.[1])
        Assert.Contains("claimed:snipe-f30c", lines.[1])
