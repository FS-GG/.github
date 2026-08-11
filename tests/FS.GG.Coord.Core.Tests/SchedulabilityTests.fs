namespace FS.GG.Coord.Tests

open Xunit
open FS.GG.Coord
open FS.GG.Coord.Types
open FS.GG.Coord.Schedulability

/// "Is this item startable?" — the 34-issue family, as tests.
///
/// In the bash client this question was computed in FIVE places and agreed in NONE (#485). Every test
/// below is one of the ways they disagreed, or one of the ways the survivor was still wrong.
module SchedulabilityTests =

    let private ref n =
        { Owner = "FS-GG"
          Repo = "FS.GG.SDD"
          Number = n }

    let private item n =
        { Ref = ref n
          PathRepo = (ref n).Repo
          Status = Ready
          State = Open
          TouchSet = Declared [ Matchable "src/Scene/**" ]
          Blockers = []
          Claim = None
          ItemPr = None
          ItemPrUnreadable = false
          HumanBlock = None
          Predicate = None
          Class = None
          BoardClass = None
          DeliveryRoute = DeliveryRoute.Current { Schema = DeliveryRoute.Schema; Subject = "test"; SubjectRevision = "test"; Route = Some DeliveryRoute.Lightweight; Agent = "test"; Timestamp = "2026-01-01T00:00:00Z"; ReasonCodes = [ "test" ]; Rationale = "test"; DeclaredImpacts = [ "test" ]; ObservedFacts = [ "test" ]; SddWorkId = None; SpecHome = None; RequiredGates = [] }
          Severity = Unset
          Phase = None
          AgeDays = None }

    let private claim w =
        { Worker = WorkerId w
          Session = None
          AgeSeconds = 60
          PreviousStatus = Some Backlog }

    /// The default question: no backlog fallback, nothing in flight.
    let private ask it = schedulable Set.empty false [] it

    // ================================================================================================
    // THE HAPPY PATH — and it must be the ONLY green.
    // ================================================================================================

    [<Fact>]
    let ``a Ready, open, declared, unblocked, unheld item is Startable`` () =
        Assert.Equal(Startable, ask (item 1))

    [<Fact>]
    let ``a missing delivery route is never inferred as lightweight`` () =
        let actual = ask { item 1 with DeliveryRoute = DeliveryRoute.Stale [ "delivery-route receipt is missing" ] }
        Assert.Equal(AwaitingDeliveryRouteDecision [ "delivery-route receipt is missing" ], actual)

    [<Fact>]
    let ``an unreadable delivery route fails closed`` () =
        let actual = ask { item 1 with DeliveryRoute = DeliveryRoute.Unreadable [ "comments unavailable" ] }
        Assert.Equal(AwaitingDeliveryRouteDecision [ "comments unavailable" ], actual)

    // ================================================================================================
    // #520 — THE SIXTH DISAGREEMENT. A CLOSED ISSUE IS NOT SCHEDULABLE.
    // ================================================================================================
    // Candidate selection read the board `Status` column and NOTHING ELSE. The scan carried the issue's
    // state all along; nothing ever read it. So an issue whose column was never flipped to Done stayed
    // a candidate FOREVER — FS.GG.Rendering#502 was handed to a worker TWO HOURS after it was closed as
    // completed, and #481's restore-the-column logic then promoted it Backlog -> Ready on release,
    // re-arming it for the next one.
    //
    // The board column is a PROJECTION of the work. The issue IS the work. When they disagree, the
    // issue wins. In the type, they are different fields, and the question is asked in that order.

    [<Fact>]
    let ``#520 a CLOSED issue is not schedulable, however the board column reads`` () =
        Assert.Equal(IssueClosed, ask { item 502 with State = Closed; Status = Ready })

    [<Fact>]
    let ``#520 ...and the issue is asked FIRST — a closed item is never merely 'wrong status'`` () =
        // Order matters: reporting `WrongStatus` for a closed issue sends the worker to fix the column,
        // when the column is the only part that is right.
        Assert.Equal(IssueClosed, ask { item 502 with State = Closed; Status = Backlog })

    // ================================================================================================
    // #437 / #485(c) — A NULL STATUS IS ITS OWN BUG, NOT A BACKLOG.
    // ================================================================================================
    // An item on the board with no Status was INVISIBLE to next/take/batch — the scheduler reported
    // "nothing to do" while real work sat on the board. It is not Backlog, it is not an error, it is
    // its own state, and modelling a three-valued thing as two values is how it stayed invisible.

    [<Fact>]
    let ``#437 an item with NO Status is not startable — and says so as its own case`` () =
        Assert.Equal(WrongStatus NoStatus, ask { item 3 with Status = NoStatus })

    [<Fact>]
    let ``#437 ...and NoStatus is NOT Backlog, even with the backlog fallback on`` () =
        // If `NoStatus` silently read as `Backlog`, `--include-backlog` would quietly start scheduling
        // items nobody had triaged. It must stay unstartable, and it must stay NAMED.
        Assert.Equal(WrongStatus NoStatus, schedulable Set.empty true [] { item 3 with Status = NoStatus })

    // ================================================================================================
    // #440 / #488 — A FULL QUEUE MUST NOT READ AS AN EMPTY ONE.
    // ================================================================================================
    // `next` picked "the first Ready, else the first Backlog"; `take` stopped at Ready. So on a board
    // with zero Ready items and startable Backlog ones — which is what `.github` actually looked like —
    // `take` reported "no schedulable item" and the worker went home. Then #440's own fix reintroduced
    // #440's defect in its own else-branch (#488).

    [<Fact>]
    let ``#440 a Backlog item is startable when the backlog fallback is on`` () =
        Assert.Equal(Startable, schedulable Set.empty true [] { item 4 with Status = Backlog })

    [<Fact>]
    let ``#440 ...and is NOT startable when it is off — the fallback is a decision, not a default`` () =
        Assert.Equal(WrongStatus Backlog, schedulable Set.empty false [] { item 4 with Status = Backlog })

    // ================================================================================================
    // #496 — TWO DEATHS, TWO DIAGNOSES.
    // ================================================================================================
    // An epic and a forgotten touch-set rendered identically, so the distinction existed only in the
    // filer's head and NO GATE COULD BE WRITTEN AT ALL. Nine items of real work went invisible to every
    // worker who asked for work, while `lint` — the one surface whose job is board health — reported
    // `0 error(s)` over a dead queue.

    [<Fact>]
    let ``#496 no Paths: at all is an OMISSION — somebody should fix it`` () =
        Assert.Equal(NoTouchSet, ask { item 5 with TouchSet = Undeclared })

    [<Fact>]
    let ``#496 'Paths: none' is a DECISION — unschedulable by design, and not a bug`` () =
        Assert.Equal(DeliberatelyNoTouchSet, ask { item 6 with TouchSet = DeclaredNone })

    [<Fact>]
    let ``#496 ...and the two verdicts are different values — the whole point of the issue`` () =
        Assert.NotEqual<Schedulability>(NoTouchSet, DeliberatelyNoTouchSet)

    [<Fact>]
    let ``#496 a declared-but-UNUSABLE touch-set is a THIRD death, and lint was green over it`` () =
        // `**/packages.lock.json`: a leading `**/` matches nothing, and a token that matches nothing
        // conflicts with nothing. `batch` skipped the item forever; `lint` reported 0 errors.
        let dead = Declared [ Unmatchable "**/packages.lock.json" ]
        Assert.Equal(UnusableTouchSet [ "**/packages.lock.json" ], ask { item 31 with TouchSet = dead })

    [<Fact>]
    let ``#273 a PARTIALLY unmatchable touch-set is refused, never partially scheduled`` () =
        // This is the worse case: the item LOOKS declared, and the unmatchable tokens reserve nothing —
        // so the files they name are invisible to every other worker's overlap check. Scheduling it
        // would be entering the collision the protocol exists to prevent, voluntarily.
        let half =
            Declared [ Matchable "src/Ok/**"; Unmatchable "**/bad.json" ]

        Assert.Equal(UnusableTouchSet [ "**/bad.json" ], ask { item 7 with TouchSet = half })

    // ================================================================================================
    // #476 — A MERGED BLOCKER IS RESOLVED.
    // ================================================================================================

    [<Fact>]
    let ``#476 an item blocked only by a MERGED PR is startable — the work FINISHED`` () =
        let blockers =
            [ { Ref = Some { Owner = "FS-GG"; Repo = ".github"; Number = 449 }
                Raw = ".github#449"
                State = BlockerMerged } ]

        Assert.Equal(Startable, ask { item 350 with Blockers = blockers })

    [<Fact>]
    let ``#476 an OPEN blocker still holds, and the verdict NAMES it`` () =
        let b =
            { Ref = Some { Owner = "FS-GG"; Repo = ".github"; Number = 500 }
              Raw = ".github#500"
              State = BlockerOpen }

        match ask { item 8 with Blockers = [ b ] } with
        | BlockedBy [ holding ] -> Assert.Equal(500, holding.Ref.Value.Number)
        | other -> failwith $"expected BlockedBy, got %A{other}"

    [<Fact>]
    let ``#266 an UNKNOWN blocker holds — a failed lookup is not a cleared blocker`` () =
        let b =
            { Ref = Some { Owner = "FS-GG"; Repo = ".github"; Number = 999 }
              Raw = ".github#999"
              State = BlockerUnknown }

        match ask { item 9 with Blockers = [ b ] } with
        | BlockedBy _ -> ()
        | other -> failwith $"an unresolvable blocker must BLOCK; got %A{other}"

    // ================================================================================================
    // #581 — THE WORK OUTLIVES THE CLAIM. Lease expiry is EVIDENCE, never PROOF.
    // ================================================================================================
    // `take` handed out FS.GG.Rendering#429 with PR #433 OPEN, because a loaded box stretched one build
    // past the 120m lease. Then it happened AGAIN, to the worker fixing #485: the claim lapsed
    // mid-test-cycle and was reaped with the work uncommitted. It survived only because the reaping
    // worker CHOSE to preserve the worktree as a WIP commit. That is generosity, not a lock.
    //
    // "Workers should heartbeat" is true and insufficient. ADR-0027's argument is that THE LOCK, not
    // worker diligence, prevents collisions.

    [<Fact>]
    let ``a live claim holds the item`` () =
        Assert.Equal(HeldBy(WorkerId "heron-b71"), ask { item 10 with Claim = Some(claim "heron-b71", LeaseHeld) })

    [<Fact>]
    let ``#581 an EXPIRED lease with an OPEN item PR still holds it — the lease lapsed, the WORK did not`` () =
        let held = Some(claim "shrike-a91", LeaseExpiredPrOpen 433)
        Assert.Equal(HeldByLiveWork(WorkerId "shrike-a91", 433), ask { item 429 with Claim = held })

    [<Fact>]
    let ``#581 an EXPIRED lease with NO open PR releases the item — the negative control`` () =
        // Without this, the assertion above is satisfied by a scheduler that simply stopped scheduling.
        let dead = Some(claim "ghost-000", LeaseExpiredNoPr)
        Assert.Equal(Startable, ask { item 11 with Claim = dead })

    [<Fact>]
    let ``#651 a MARKERLESS item with an open item PR is NOT offered — an implementation is already in flight`` () =
        // No claim marker (Claim = None), but the scan found an open `item/<n>-*` PR. #581 read that
        // proof-of-life only through a marker, so this item used to fall straight through to Startable and
        // get handed out a second time. It must now be ItemPrOpen — not offered.
        match ask { item 651 with Claim = None; ItemPr = Some 812 } with
        | ItemPrOpen 812 -> ()
        | other -> failwith $"a markerless item with an open PR must not be offered; got %A{other}"

    [<Fact>]
    let ``#651 negative control — a markerless item with NO item PR is still Startable`` () =
        // Without this, the assertion above is satisfied by a scheduler that stopped offering markerless
        // items altogether. The common case (no PR) must stay startable.
        Assert.Equal(Startable, ask { item 652 with Claim = None; ItemPr = None })

    [<Fact>]
    let ``#651 a LIVE claim outranks the item PR — a claimed item is never double-counted`` () =
        // ItemPr is the MARKERLESS signal only; when a marker holds the lock, its own liveness governs and
        // the item PR (if any) is the claim's LeaseExpiredPrOpen, checked above. A live claim wins here.
        Assert.Equal(
            HeldBy(WorkerId "heron-b71"),
            ask { item 653 with Claim = Some(claim "heron-b71", LeaseHeld); ItemPr = Some 999 }
        )

    [<Fact>]
    let ``#1055 an EXPIRED lease with a pushed branch but NO PR is WITHHELD, not offered`` () =
        // §5 opens the PR only after the work, so a REST outage can expire the lease while a pushed branch is
        // the only artifact. The item must NOT be offered — `take` re-offering it would hand a second worker
        // the files the first is standing in. Undetermined (a branch is weaker evidence than a PR), never
        // Startable.
        let branch = Some(claim "curlew-ab", LeaseExpiredBranchPushed)

        match ask { item 1055 with Claim = branch } with
        | Undetermined _ -> ()
        | other -> failwith $"a pushed branch is proof of life; the item must not be offered; got %A{other}"

    [<Fact>]
    let ``#581 we could not ask about the PR -> UNDETERMINED, never 'free'`` () =
        // THE CASE THAT DESTROYED WORK. "We could not check" is not "there is no PR". A type that
        // cannot express the difference is a type that will be asked to guess, and it will guess wrong
        // in the direction that loses somebody's uncommitted afternoon.
        let unknown = Some(claim "shrike-a91", LivenessUnknown)

        match ask { item 12 with Claim = unknown } with
        | Undetermined _ -> ()
        | other -> failwith $"an unverifiable claim is not an abandoned one; got %A{other}"

    // ================================================================================================
    // DISJOINTNESS — the only check that depends on other items, so it goes last.
    // ================================================================================================

    [<Fact>]
    let ``an item overlapping in-flight work is not startable, and the verdict names the collision`` () =
        let inFlight = [ Declared [ Matchable "src/Scene/**" ] ]

        match schedulable Set.empty false inFlight (item 13) with
        | OverlapsInFlight hits -> Assert.NotEmpty hits
        | other -> failwith $"expected OverlapsInFlight, got %A{other}"

    [<Fact>]
    let ``an item disjoint from everything in flight is startable`` () =
        let inFlight = [ Declared [ Matchable "src/Audio/**" ] ]
        Assert.Equal(Startable, schedulable Set.empty false inFlight (item 14))

    // ---- .github#2305 / ADR-0044: generated, CI-gated artifacts do not block `take` ------------------
    //
    // The critic's repair-1 finding: `overlap`/`activeCollisions` (`Client.fs`) excluded a shared
    // generated-artifact token from a collision verdict, but the live `take`/`batch` scheduling path
    // (`Schedulability.schedulable`, reached via `Batch.scheduleWith`) did not — so a real `take` could
    // still refuse two items whose actual subjects never collided, only a file neither of them authors.
    // Reconstructs the row's own measured instance: `.github#2254` in flight holding
    // `registry/driver-skill-manifest.json` (among its real files), `.github#2248` asking to start with
    // the SAME generated token declared, real subjects otherwise disjoint.

    [<Fact>]
    let ``#2305 an item overlapping in-flight work SOLELY on a generated artifact token is Startable, given the roster`` () =
        let generated = Set.ofList [ "registry/driver-skill-manifest.json" ]

        let inFlight =
            [ Declared
                  [ Matchable ".claude/skills/drive-board/SKILL.md"
                    Matchable "registry/driver-skill-manifest.json" ] ]

        let candidate =
            { item 2248 with
                TouchSet =
                    Declared
                        [ Matchable ".claude/skills/pnext-item/references/independent-review.md"
                          Matchable "registry/driver-skill-manifest.json" ] }

        // WITHOUT the roster: today's pre-repair-1 defect (the negation this test would have asserted
        // before the fix — the gate-inversion mutation reproduces this exact result).
        match schedulable Set.empty false inFlight candidate with
        | OverlapsInFlight hits -> Assert.NotEmpty hits
        | other -> failwith $"expected the unaware call to still collide (this is the PRE-repair baseline); got %A{other}"

        // WITH the roster: the repair. The real subjects never collided.
        Assert.Equal(Startable, schedulable generated false inFlight candidate)

    [<Fact>]
    let ``#2305 negative control — a directory-prefix claim over the generated artifact's parent still overlaps (#309 trap)`` () =
        let generated = Set.ofList [ "registry/driver-skill-manifest.json" ]
        let inFlight = [ Declared [ Matchable "registry/**" ] ]
        let candidate = { item 15 with TouchSet = Declared [ Matchable "registry/driver-skill-manifest.json" ] }

        match schedulable generated false inFlight candidate with
        | OverlapsInFlight hits -> Assert.NotEmpty hits
        | other -> failwith $"a directory-prefix claim over the parent must still block; got %A{other}"

    [<Fact>]
    let ``#2305 negative control — a genuinely shared non-generated path still overlaps (FS.GG.Kit Version shape)`` () =
        let generated = Set.ofList [ "registry/driver-skill-manifest.json" ]
        let inFlight = [ Declared [ Matchable "src/FS.GG.Kit/FS.GG.Kit.csproj" ] ]
        let candidate = { item 16 with TouchSet = Declared [ Matchable "src/FS.GG.Kit/FS.GG.Kit.csproj" ] }

        match schedulable generated false inFlight candidate with
        | OverlapsInFlight hits -> Assert.NotEmpty hits
        | other -> failwith $"a genuine single-writer field absent from the generated roster must still block; got %A{other}"

    // ================================================================================================
    // ORDER IS PART OF THE SPEC.
    // ================================================================================================

    [<Fact>]
    let ``#516 order: an unusable touch-set outranks a live claim`` () =
        // "Nobody can claim this item" is a stronger and cheaper statement than "somebody already has",
        // and a worker told the second when the first is also true fixes the wrong thing. This is the
        // ordering the bash fix got wrong first, and the fixture caught.
        let it =
            { item 15 with
                TouchSet = Declared [ Unmatchable "**/bad.json" ]
                Claim = Some(claim "heron-b71", LeaseHeld) }

        match ask it with
        | UnusableTouchSet _ -> ()
        | other -> failwith $"the item-level defect must be reported first; got %A{other}"

    [<Fact>]
    let ``order: a CLOSED issue outranks everything — it is not blocked, held, or overlapping, it is DONE`` () =
        let it =
            { item 16 with
                State = Closed
                Claim = Some(claim "heron-b71", LeaseHeld)
                Blockers =
                    [ { Ref = Some(ref 99); Raw = (ref 99).Short; State = BlockerOpen } ] }

        Assert.Equal(IssueClosed, ask it)

    // ================================================================================================
    // THE REASON IS NOT OPTIONAL (#440).
    // ================================================================================================
    // A queue that shrinks without explanation is #440: `take` reported "no schedulable item" over a
    // board full of work, and the worker went home. Every non-green verdict must be able to say why.

    [<Fact>]
    let ``every verdict can explain itself — a silent skip is how a full queue reads as an empty one`` () =
        let verdicts =
            [ ask { item 20 with State = Closed }
              ask { item 21 with Status = NoStatus }
              ask { item 22 with TouchSet = Undeclared }
              ask { item 23 with TouchSet = DeclaredNone }
              ask { item 24 with TouchSet = Declared [ Unmatchable "**/x" ] }
              ask { item 25 with Blockers = [ { Ref = Some(ref 99); Raw = (ref 99).Short; State = BlockerOpen } ] }
              ask { item 26 with Claim = Some(claim "w", LeaseHeld) }
              ask { item 27 with Claim = Some(claim "w", LeaseExpiredPrOpen 1) }
              ask { item 28 with Claim = Some(claim "w", LivenessUnknown) }
              ask { item 29 with HumanBlock = Some AwaitingHumanDecision }
              ask { item 30 with HumanBlock = Some AwaitingHumanAction }
              schedulable Set.empty false [ Declared [ Matchable "src/Scene/**" ] ] (item 31) ]

        for v in verdicts do
            let reason = explain 120 (item 1) v
            Assert.False(System.String.IsNullOrWhiteSpace reason, $"%A{v} produced no reason")

    // ================================================================================================
    // #1103/#1887 — human decisions/actions refuse REGARDLESS of the touch-set.
    // ================================================================================================

    [<Fact>]
    let ``#1887 Class decision refuses a Ready item with no human sentinel`` () =
        // The live defect's exact disagreement: the item's own body class says decision, the sentinel is
        // absent, and the board advertises Ready. Before #1887 this was Startable and blind `take` claimed
        // it. BoardClass is deliberately stale in the opposite direction: the body-derived Class wins.
        Assert.Equal(
            AwaitingHuman AwaitingHumanDecision,
            ask
                { item 1887 with
                    Class = Some Decision
                    BoardClass = Some Hardening
                    HumanBlock = None })

    [<Fact>]
    let ``#1887 Class decision refuses Backlog even when the caller opts into Backlog`` () =
        Assert.Equal(
            AwaitingHuman AwaitingHumanDecision,
            schedulable Set.empty
                true
                []
                { item 1887 with
                    Status = Backlog
                    Class = Some Decision
                    HumanBlock = None })

    [<Fact>]
    let ``#1887 negative control — a non-decision class with no sentinel remains Startable`` () =
        // This is the mutation discriminator: deleting the Decision guard makes the two tests above match
        // this control and red. The remedy narrows one class; it does not park every classed item.
        Assert.Equal(
            Startable,
            ask
                { item 1887 with
                    Class = Some Hardening
                    BoardClass = Some Decision
                    HumanBlock = None })

    [<Fact>]
    let ``a human/decision sentinel refuses even a Ready item that carries a real touch-set (#918)`` () =
        // #918's exact shape: a decision item declaring a real, wide touch-set (the fix-scope). The
        // column cannot withhold it and neither can `Paths:` — only the sentinel can.
        Assert.Equal(
            AwaitingHuman AwaitingHumanDecision,
            ask { item 918 with HumanBlock = Some AwaitingHumanDecision })

    [<Fact>]
    let ``a human/decision sentinel refuses a Backlog item even with --include-backlog (the #1081 door)`` () =
        // `take --include-backlog` walked through the closed door on #918/#1081 because `Backlog` is a
        // "not yet", not a "not for you". The sentinel is checked AFTER the column, so opting the column
        // in does not defeat it.
        Assert.Equal(
            AwaitingHuman AwaitingHumanDecision,
            schedulable Set.empty true [] { item 918 with Status = Backlog; HumanBlock = Some AwaitingHumanDecision })

    [<Fact>]
    let ``a human/action sentinel refuses too, carrying its own case`` () =
        Assert.Equal(
            AwaitingHuman AwaitingHumanAction,
            ask { item 574 with HumanBlock = Some AwaitingHumanAction })

    [<Fact>]
    let ``a concrete open blocker outranks the human-block sentinel — it is more actionable`` () =
        // Both hold the item; `Blocked by #99` is the sentence a worker can act on, so it wins. The
        // sentinel is checked only once the concrete blockers are clear.
        let b = { Ref = Some(ref 99); Raw = (ref 99).Short; State = BlockerOpen }

        match ask { item 8 with Blockers = [ b ]; HumanBlock = Some AwaitingHumanDecision } with
        | BlockedBy _ -> ()
        | other -> failwith $"expected BlockedBy to outrank the sentinel, got %A{other}"

    // ================================================================================================
    // #1103 leg 8 — `Paths: any` is a schedulable file-less chore, the counterpart to `Paths: none`.
    // ================================================================================================

    [<Fact>]
    let ``Paths any is Startable — a chore reserves nothing and is schedulable`` () =
        Assert.Equal(Startable, ask { item 40 with TouchSet = DeclaredChore })

    [<Fact>]
    let ``Paths none and Paths any are NOT the same verdict — that is the whole of leg 8`` () =
        Assert.Equal(DeliberatelyNoTouchSet, ask { item 41 with TouchSet = DeclaredNone })
        Assert.Equal(Startable, ask { item 42 with TouchSet = DeclaredChore })

    [<Fact>]
    let ``a chore conflicts with nothing — startable even beside in-flight work on any files`` () =
        Assert.Equal(
            Startable,
            schedulable Set.empty false [ Declared [ Matchable "src/Scene/**" ] ] { item 43 with TouchSet = DeclaredChore })
