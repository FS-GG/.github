namespace FS.GG.Coord.Tests

open Xunit
open FS.GG.Coord
open FS.GG.Coord.Types
open FS.GG.Coord.Schedulability
open FS.GG.Coord.Batch

/// THE SCHEDULER, as tests — the part `schedulable` cannot express on its own.
///
/// `schedulable` is a question about ONE item. `schedule` is a FOLD, and every bug in this file lives
/// in the fold: what an item RESERVES when it is passed over, which repo a reserved token belongs to,
/// and what a cap does to the candidates it never reached. Those are the defects a per-item predicate
/// is structurally incapable of catching, and they are the ones that put two workers in one file.
module BatchTests =

    let private refIn owner repo n =
        { Owner = owner
          Repo = repo
          Number = n }

    let private ref n = refIn "FS-GG" "FS.GG.SDD" n

    let private item n paths =
        { Ref = ref n
          Status = Ready
          State = Open
          TouchSet = Declared(paths |> List.map Matchable)
          Blockers = []
          Claim = None
          ItemPr = None
          ItemPrUnreadable = false
          HumanBlock = None
          Predicate = None
          Class = None
          BoardClass = None
          Severity = Unset
          Phase = None
          AgeDays = None }

    let private held w ageSeconds it =
        { it with
            Claim =
                Some(
                    { Worker = WorkerId w
                      Session = None
                      AgeSeconds = ageSeconds
                      PreviousStatus = Some Backlog },
                    LeaseHeld
                ) }

    let private ok =
        function
        | Green r -> r
        | Red reasons -> failwithf "expected a batch, got Red: %A" reasons
        | NoVerdict reason -> failwithf "expected a batch, got NoVerdict: %s" reason

    let private chosenNumbers r = r.Chosen |> List.map (fun i -> i.Ref.Number)

    let private verdictOf r n =
        r.Decisions
        |> List.tryFind (fun d -> d.Item.Ref.Number = n)
        |> Option.map (fun d -> d.Result)

    let private run inFlight candidates = schedule false None inFlight candidates |> ok

    // ================================================================================================
    // THE FOLD. An item chosen into the batch RESERVES its touch-set against every later candidate.
    // ================================================================================================
    // This is the whole reason `schedule` is not `List.map schedulable`. Both items below are Startable
    // in isolation, and handing out both is exactly the collision ADR-0021 exists to prevent.

    [<Fact>]
    let ``two candidates over the same file: the first is chosen, the second is passed over`` () =
        let r = run [] [ item 1 [ "src/Scene/Types.fs" ]; item 2 [ "src/Scene/Types.fs" ] ]

        Assert.Equal<int list>([ 1 ], chosenNumbers r)

        match verdictOf r 2 with
        | Some(OverlapsInFlight _) -> ()
        | other -> failwithf "expected #2 to overlap the batch member, got %A" other

    [<Fact>]
    let ``...and the batch member is NAMED as the holder — not merely 'something overlaps'`` () =
        // #428. Reporting the colliding PATHS and stopping tells a worker they are blocked and
        // withholds the one fact every remedy needs: WHO. A batch member is a different answer from a
        // live claim — it frees at the end of this run, and there is no lease to wait out.
        let r = run [] [ item 1 [ "src/Scene" ]; item 2 [ "src/Scene/Types.fs" ] ]

        let d = r.Decisions |> List.find (fun d -> d.Item.Ref.Number = 2)

        Assert.Equal(Some(BatchMember(ref 1)), d.CollidedWith)

    [<Fact>]
    let ``disjoint candidates all schedule — the fold must not invent a collision`` () =
        let r = run [] [ item 1 [ "src/A.fs" ]; item 2 [ "src/B.fs" ]; item 3 [ "src/C.fs" ] ]

        Assert.Equal<int list>([ 1; 2; 3 ], chosenNumbers r)

    [<Fact>]
    let ``greedy by ISSUE NUMBER, whatever order the board scan returned them in`` () =
        // Determinism is not a nicety here. The scan is cached and SHARED across the fleet (#418), so
        // two workers reading the same 90s window must compute the same batch — an order-dependent
        // scheduler hands them different answers from byte-identical input, and they collide.
        let a = run [] [ item 7 [ "src/X.fs" ]; item 3 [ "src/X.fs" ] ]
        let b = run [] [ item 3 [ "src/X.fs" ]; item 7 [ "src/X.fs" ] ]

        Assert.Equal<int list>([ 3 ], chosenNumbers a)
        Assert.Equal<int list>(chosenNumbers a, chosenNumbers b)

    // ================================================================================================
    // A HELD ITEM RESERVES. Skipping without reserving is how a second worker got the same files.
    // ================================================================================================
    // The lock, not the board column, is the truth: a claim whose `Status` flip failed still owns the
    // item. If a held candidate merely dropped out of the batch, the next candidate over the same files
    // would sail through the disjointness check — because nothing would be holding them.

    [<Fact>]
    let ``a HELD candidate reserves its touch-set against the candidates after it`` () =
        let r =
            run
                []
                [ item 1 [ "scripts/fsgg-coord" ] |> held "w-alice" 60
                  item 2 [ "scripts/fsgg-coord" ] ]

        Assert.Empty(r.Chosen)
        Assert.Equal(Some(HeldBy(WorkerId "w-alice")), verdictOf r 1)

        match verdictOf r 2 with
        | Some(OverlapsInFlight _) -> ()
        | other -> failwithf "expected #2 to overlap the held item's reservation, got %A" other

    [<Fact>]
    let ``...and the collision names the WORKER and the lease age, so 'wait ~Nm' can be truthful`` () =
        let r =
            run
                []
                [ item 1 [ "scripts/fsgg-coord" ] |> held "w-alice" 900
                  item 2 [ "scripts/fsgg-coord" ] ]

        let d = r.Decisions |> List.find (fun d -> d.Item.Ref.Number = 2)

        Assert.Equal(Some(LiveClaim(WorkerId "w-alice", ref 1, 900, None)), d.CollidedWith)

    [<Fact>]
    let ``#581 an EXPIRED lease whose PR is open still reserves — the work is alive, so are its files`` () =
        // Lease expiry is EVIDENCE of abandonment, never proof, and its false positive is systematic:
        // work that takes longer than the lease. An open `item/<n>-*` PR is the worktree protocol's own
        // artifact and outranks a timer. This item is NOT free, and neither are its files.
        let expired =
            { item 1 [ "scripts/fsgg-coord" ] with
                Claim =
                    Some(
                        { Worker = WorkerId "w-alice"
                          Session = None
                          AgeSeconds = 99999
                          PreviousStatus = None },
                        LeaseExpiredPrOpen 4242
                    ) }

        let r = run [] [ expired; item 2 [ "scripts/fsgg-coord" ] ]

        Assert.Empty(r.Chosen)
        Assert.Equal(Some(HeldByLiveWork(WorkerId "w-alice", 4242)), verdictOf r 1)

        match verdictOf r 2 with
        | Some(OverlapsInFlight _) -> ()
        | other -> failwithf "expected #2 to be held off by the live PR's reservation, got %A" other

    [<Fact>]
    let ``an UNSCHEDULABLE-but-unheld candidate reserves NOTHING — nobody is working it`` () =
        // The counterweight to the two tests above, and the line between them: a blocked item is not
        // being worked, so its files are free. Reserving them would serialise the board behind work
        // that has not started and may never start.
        let blocked =
            { item 1 [ "src/Scene/Types.fs" ] with
                Blockers =
                    [ { Ref = Some(ref 999)
                        Raw = (ref 999).Short
                        State = BlockerOpen } ] }

        let r = run [] [ blocked; item 2 [ "src/Scene/Types.fs" ] ]

        Assert.Equal<int list>([ 2 ], chosenNumbers r)

    // ================================================================================================
    // #312 / #353 — TOKENS ARE REPO-RELATIVE. A cross-repo comparison invents a collision.
    // ================================================================================================
    // `batch` is legitimately MULTI-repo: with no --repo it schedules the whole board. But `scripts/foo`
    // in one repo and `scripts/foo` in another name two different files in two different worktrees, so
    // comparing them bare under-schedules a candidate that NOTHING is actually holding.

    [<Fact>]
    let ``#312 the same token in two DIFFERENT repos is not a collision`` () =
        let sdd = item 1 [ "scripts/build.sh" ]

        let rendering =
            { item 2 [ "scripts/build.sh" ] with
                Ref = refIn "FS-GG" "FS.GG.Rendering" 2 }

        let r = run [] [ sdd; rendering ]

        Assert.Equal<int list>([ 1; 2 ], chosenNumbers r)

    [<Fact>]
    let ``#353 an in-flight reservation in ANOTHER repo does not hold this repo's files`` () =
        let elsewhere =
            { Owner = "FS-GG"
              Repo = "FS.GG.Rendering"
              Paths = Declared [ Matchable "scripts/build.sh" ]
              Holder = LiveClaim(WorkerId "w-bob", refIn "FS-GG" "FS.GG.Rendering" 9, 60, None) }

        let r = run [ elsewhere ] [ item 1 [ "scripts/build.sh" ] ]

        Assert.Equal<int list>([ 1 ], chosenNumbers r)

    [<Fact>]
    let ``an in-flight reservation in the SAME repo does hold them`` () =
        let here =
            { Owner = "FS-GG"
              Repo = "FS.GG.SDD"
              Paths = Declared [ Matchable "scripts/build.sh" ]
              Holder = LiveClaim(WorkerId "w-bob", ref 9, 60, None) }

        let r = run [ here ] [ item 1 [ "scripts/build.sh" ] ]

        Assert.Empty(r.Chosen)
        Assert.Equal(Some(LiveClaim(WorkerId "w-bob", ref 9, 60, None)), (List.head r.Decisions).CollidedWith)

    // ================================================================================================
    // FAIL CLOSED: a reservation that reserves NOTHING refuses the whole batch.
    // ================================================================================================
    // A held item whose touch-set is unmatchable is the worst state in the domain: it OCCUPIES files
    // while RESERVING none, so every candidate clears it and the scheduler hands a second worker the
    // very files its holder is standing in. We cannot see that surface, so we cannot schedule against
    // it — and "cannot schedule" must mean REFUSE, not "schedule anyway". Unschedulable beats
    // mis-scheduled (#273, and ADR-0021's own failure one level down).

    [<Fact>]
    let ``#273 an in-flight claim with an UNMATCHABLE touch-set refuses the batch — it reserves nothing`` () =
        let blind =
            { Owner = "FS-GG"
              Repo = "FS.GG.SDD"
              Paths = Declared [ Unmatchable "**/*.fs" ]
              Holder = LiveClaim(WorkerId "w-bob", ref 9, 60, None) }

        match schedule false None [ blind ] [ item 1 [ "src/A.fs" ] ] with
        | Red reasons ->
            Assert.NotEmpty(reasons)
            Assert.Contains("reserves NOTHING", String.concat " " reasons)
        | other -> failwithf "expected the batch to be REFUSED, got %A" other

    [<Fact>]
    let ``...but an unmatchable CANDIDATE is merely passed over — it occupies nothing`` () =
        // The distinction the `Red` leg turns on. A candidate reserving nothing harms nobody; a HOLDER
        // reserving nothing harms everybody. Conflating them either kills the batch over a typo, or
        // schedules straight into occupied files.
        let bad =
            { item 1 [] with
                TouchSet = Declared [ Unmatchable "**/*.fs" ] }

        let r = run [] [ bad; item 2 [ "src/A.fs" ] ]

        Assert.Equal<int list>([ 2 ], chosenNumbers r)

        match verdictOf r 1 with
        | Some(UnusableTouchSet _) -> ()
        | other -> failwithf "expected #1 to be UnusableTouchSet, got %A" other

    // ================================================================================================
    // THE CAP. `-n` stops the fold — so the candidates after it are UNEVALUATED, not unschedulable.
    // ================================================================================================
    // A silent cap reads as "we looked at everything, and this is all there was". That is a lie the
    // caller cannot detect, and it is the same shape as every other fail-open in this domain: an
    // absence of findings presented as a finding of absence.

    [<Fact>]
    let ``-n caps the batch and REPORTS that it truncated`` () =
        let r =
            schedule false (Some 2) [] [ item 1 [ "src/A.fs" ]; item 2 [ "src/B.fs" ]; item 3 [ "src/C.fs" ] ]
            |> ok

        Assert.Equal<int list>([ 1; 2 ], chosenNumbers r)
        Assert.True(r.Truncated, "a cap that stopped the fold must say so")

    [<Fact>]
    let ``a cap that did not bite does NOT report truncation`` () =
        // Reporting a cap that never fired would be its own small lie — and it is the signal a caller
        // uses to decide whether "nothing else is startable" is a fact or an artefact.
        let r = schedule false (Some 2) [] [ item 1 [ "src/A.fs" ]; item 2 [ "src/B.fs" ] ] |> ok

        Assert.Equal<int list>([ 1; 2 ], chosenNumbers r)
        Assert.False(r.Truncated, "the cap was reached on the LAST candidate — nothing was left unseen")

    [<Fact>]
    let ``the candidates a cap never reached get NO verdict — silence is not a skip`` () =
        let r = schedule false (Some 1) [] [ item 1 [ "src/A.fs" ]; item 2 [ "src/B.fs" ] ] |> ok

        Assert.Equal<int list>([ 1 ], chosenNumbers r)
        Assert.True(r.Truncated)
        Assert.Equal(None, verdictOf r 2)

    // ================================================================================================
    // #440 / #488 — AN EMPTY QUEUE AND A BLOCKED QUEUE ARE DIFFERENT ANSWERS.
    // ================================================================================================
    // `take` reported "no schedulable item" over a board full of work and the worker went home. The
    // blocked candidates never reached the skip-reason loop, so they contributed nothing to the
    // passed-over list — and `take` concluded from THAT emptiness that the queue was empty. The one
    // state most likely to starve a queue was the one state that left no trace.

    [<Fact>]
    let ``#488 a fully-blocked queue chooses nothing but EXPLAINS every candidate`` () =
        let blocked n =
            { item n [ $"src/%d{n}.fs" ] with
                Blockers =
                    [ { Ref = Some(ref 999)
                        Raw = (ref 999).Short
                        State = BlockerOpen } ] }

        let r = run [] [ blocked 1; blocked 2; blocked 3 ]

        Assert.Empty(r.Chosen)

        // THE POINT: three decisions, not an empty list. "Nothing to do" and "everything is blocked"
        // are the same empty `Chosen` and two completely different operator instructions.
        Assert.Equal(3, List.length r.Decisions)

        Assert.All(
            r.Decisions,
            fun d ->
                match d.Result with
                | BlockedBy _ -> ()
                | other -> failwithf "expected BlockedBy, got %A" other
        )

    [<Fact>]
    let ``an EMPTY queue is distinguishable from a blocked one — no candidates, no decisions`` () =
        let r = run [] []

        Assert.Empty(r.Chosen)
        Assert.Empty(r.Decisions)

    // ================================================================================================
    // #428 — THE STARVED-QUEUE BANNER. A busy queue that hands out nothing is not an empty one.
    // ================================================================================================
    // The chokepoint: in a repo where one file is nearly every item's touch-set, ONE claim serialises the
    // whole queue. `batch` correctly schedules nothing — and "nothing schedulable" reads exactly like an
    // empty backlog, so a worker goes home from a repo with work in it. The banner is the difference: it
    // names the holders (who to talk to) and the soonest lease (whether to wait), and an EXPIRED lease is
    // a reap, not a wait. A markerless reserver reserves, but it is NOT a holder — no worker, no lease.

    let private resv repo paths holder : Reservation =
        { Owner = "FS-GG"
          Repo = repo
          Paths = Declared(paths |> List.map Matchable)
          Holder = holder }

    let private anyLine (needle: string) (lines: string list) =
        lines |> List.exists (fun (l: string) -> l.Contains needle)

    /// The corpus's starved world, one layer down: #221 queued behind a fresh off-board claim (tern-y99),
    /// #222 held by its own fresh marker (kite-z01), #224 queued behind an EXPIRED claim (ghost-222), and
    /// #225 overlapping a MARKERLESS In-progress reserver (#226) — reserved, but no holder to name.
    let private starvedResult () =
        let inFlight =
            [ resv "FS.GG.SDD" [ "src/Starve" ] (LiveClaim(WorkerId "tern-y99", ref 223, 0, None))
              resv "FS.GG.SDD" [ "src/Dead" ] (LiveClaim(WorkerId "ghost-222", ref 216, 99999, None))
              resv "FS.GG.SDD" [ "src/Ghostly" ] (Unowned(ref 226)) ]

        let candidates =
            [ item 221 [ "src/Starve/Sub" ]
              item 222 [ "src/Solo" ] |> held "kite-z01" 0
              item 224 [ "src/Dead/Sub" ]
              item 225 [ "src/Ghostly/Sub" ] ]

        run inFlight candidates

    [<Fact>]
    let ``#428 a starved queue is BUSY, names every holder, and gives the soonest lease`` () =
        let r = starvedResult ()
        Assert.Empty(r.Chosen)

        let banner = starvedBanner 120 r

        // THREE items queued behind live claims — the markerless #225 is NOT one of them.
        Assert.True(
            anyLine "3 item(s) are QUEUED BEHIND LIVE CLAIMS held by: ghost-222, kite-z01, tern-y99" banner,
            $"expected the queued-behind-claims line naming the three holders, got %A{banner}"
        )

        Assert.True(anyLine "this queue is BUSY, not empty" banner, $"expected the BUSY line, got %A{banner}")

    [<Fact>]
    let ``#428 an EXPIRED lease is the soonest to free, and it is a REAP not a wait`` () =
        let banner = starvedBanner 120 (starvedResult ())

        // ghost-222's lease has lapsed, so it frees NOW — the soonest of all — and the advice points at
        // `reap`, the one blocker a worker clears themselves. Exactly one lease has expired.
        Assert.True(anyLine "soonest: lease EXPIRED — reapable" banner, $"expected the EXPIRED soonest line, got %A{banner}")

        Assert.True(
            anyLine "1 of those lease(s) have EXPIRED — collect them: scripts/fsgg-coord reap --repo FS.GG.SDD --apply" banner,
            $"expected the reap advice for the one expired lease, got %A{banner}"
        )

    [<Fact>]
    let ``#428 a markerless In-progress reserver is never dressed up as a holder named '—'`` () =
        // It reserves (something is editing those files), so #225 is not scheduled over it — but it has no
        // worker and no lease, so it must NOT inflate the queued-behind-claims count nor appear as "held by —".
        let banner = starvedBanner 120 (starvedResult ())

        Assert.False(anyLine "held by —" banner, $"a markerless reserver must never be a holder named '—', got %A{banner}")
        Assert.False(anyLine "4 item(s)" banner, $"the markerless #225 must not be counted as queued behind a claim, got %A{banner}")

    [<Fact>]
    let ``#428 a queue that HANDED OUT WORK prints no starved banner`` () =
        // A BUSY banner on a schedule that worked is noise that trains workers to skip stderr (#440). The
        // banner is silent whenever anything was chosen.
        let r = run [] [ item 1 [ "src/A.fs" ]; item 2 [ "src/B.fs" ] ]
        Assert.NotEmpty(r.Chosen)
        Assert.Empty(starvedBanner 120 r)

    [<Fact>]
    let ``#428 a queue starved by BLOCKERS alone gets no banner — that is #440's per-item business`` () =
        // The banner is for queues starved BY CLAIMS — a worker waiting on a lease. A blocked or wrong-column
        // queue has no holder to name and no lease to wait out; its emptiness is explained per item (#440),
        // and a BUSY banner over it would be a lie.
        let blocked n =
            { item n [ $"src/%d{n}.fs" ] with
                Blockers =
                    [ { Ref = Some(ref 999)
                        Raw = (ref 999).Short
                        State = BlockerOpen } ] }

        let r = run [] [ blocked 1; blocked 2 ]
        Assert.Empty(r.Chosen)
        Assert.Empty(starvedBanner 120 r)

    [<Fact>]
    let ``#428 when every lease is FRESH the soonest is a countdown, and no reap advice fires`` () =
        // No expired lease means nothing to reap — the advice must not appear (there is nothing to collect),
        // and the soonest lease is a real countdown a worker can decide against.
        let inFlight = [ resv "FS.GG.SDD" [ "src/Starve" ] (LiveClaim(WorkerId "tern-y99", ref 223, 60, None)) ]
        let r = run inFlight [ item 221 [ "src/Starve/Sub" ] ]
        Assert.Empty(r.Chosen)

        let banner = starvedBanner 120 r
        Assert.True(anyLine "soonest: lease frees in ~" banner, $"expected a countdown, got %A{banner}")
        Assert.False(anyLine "EXPIRED" banner, $"no lease has expired, so no EXPIRED line, got %A{banner}")
        Assert.False(anyLine "collect them: fsgg-coord reap" banner, $"no reap advice when nothing expired, got %A{banner}")

    // ================================================================================================
    // #636 — THE UNTRIAGED QUEUE. A queue starved at the COLUMN is not busy, and it is not empty either.
    // ================================================================================================
    // The live board that prompted this: 7 items in Backlog, 2 behind live claims. The banner named only
    // the claims — "soonest: lease frees in ~120m" — so `take` told a worker to wait two hours over seven
    // items that were one flag away. `take --include-backlog` has always worked; nothing said so.

    let private backlogItem n paths = { item n paths with Status = Backlog }

    [<Fact>]
    let ``#636 a queue withheld at the COLUMN says so, and names the flag that schedules it`` () =
        let r = run [] [ backlogItem 1 [ "src/A.fs" ]; backlogItem 2 [ "src/B.fs" ] ]
        Assert.Empty(r.Chosen)

        let banner = starvedBanner 120 r

        Assert.True(
            anyLine "2 item(s) are in BACKLOG, and were passed over AT THE COLUMN" banner,
            $"expected the withheld-at-the-column line, got %A{banner}"
        )

        Assert.True(
            anyLine "fsgg-coord take --repo FS.GG.SDD --include-backlog" banner,
            $"expected the remedy to name the flag AND the repo, got %A{banner}"
        )

    [<Fact>]
    let ``#636 an untriaged queue is UNTRIAGED, not BUSY — there is no lease to wait out`` () =
        // The #428 judgement, applied to the column: "BUSY" over a queue nobody holds would send a worker
        // to wait out a lease that does not exist. No holder is named, no lease window is offered, and no
        // reap is advertised — none of the three exists.
        let r = run [] [ backlogItem 1 [ "src/A.fs" ] ]
        let banner = starvedBanner 120 r

        Assert.True(anyLine "this queue is UNTRIAGED, not empty" banner, $"expected the UNTRIAGED headline, got %A{banner}")
        Assert.False(anyLine "BUSY" banner, $"nobody holds this queue, so it is not BUSY, got %A{banner}")
        Assert.False(anyLine "QUEUED BEHIND LIVE CLAIMS" banner, $"no claim is queued behind, got %A{banner}")
        Assert.False(anyLine "soonest" banner, $"no lease exists to name a window for, got %A{banner}")

    [<Fact>]
    let ``#636 the banner may NOT promise the withheld items are startable — the scheduler never asked`` () =
        // THE HONESTY CONSTRAINT. Both items are Backlog; #2 is ALSO blocked, so it would not be startable
        // even with the flag. The scheduler stops at the column and never evaluates the blocker, so the
        // banner counts BOTH — and must therefore describe them as UNASKED, never as startable. Promising
        // "2 items are startable" here would be #440's defect: asserting a cause we did not observe.
        let blockedToo =
            { backlogItem 2 [ "src/B.fs" ] with
                Blockers =
                    [ { Ref = Some(ref 999)
                        Raw = (ref 999).Short
                        State = BlockerOpen } ] }

        let r = run [] [ backlogItem 1 [ "src/A.fs" ]; blockedToo ]
        let banner = starvedBanner 120 r

        Assert.True(anyLine "2 item(s) are in BACKLOG" banner, $"the column is checked first, so BOTH are withheld there, got %A{banner}")
        Assert.True(anyLine "UNASKED, not no" banner, $"expected the honesty hedge, got %A{banner}")

        // The defect would be a PROMISED COUNT — "2 item(s) are startable" / "would be startable" — which is
        // a claim about #2 the scheduler never evaluated (it stopped at #2's column, before its blocker).
        // The hedge above legitimately contains the words "are startable"; what must never appear is the
        // assertion.
        Assert.False(anyLine "item(s) are startable" banner, $"the banner may not promise a startable count it never computed, got %A{banner}")
        Assert.False(anyLine "would be startable" banner, $"the banner may not promise startability it never computed, got %A{banner}")

    [<Fact>]
    let ``#636 a BUSY and UNTRIAGED queue reports BOTH — the claim must not hide the flag`` () =
        // The live board's exact shape, and the whole defect: reporting only the claims left the worker
        // waiting ~120m over items that needed a flag, not a lease.
        let inFlight = [ resv "FS.GG.SDD" [ "src/Starve" ] (LiveClaim(WorkerId "tern-y99", ref 223, 60, None)) ]
        let r = run inFlight [ item 221 [ "src/Starve/Sub" ]; backlogItem 300 [ "src/Untriaged.fs" ] ]
        Assert.Empty(r.Chosen)

        let banner = starvedBanner 120 r

        Assert.True(anyLine "1 item(s) are QUEUED BEHIND LIVE CLAIMS" banner, $"the claim fact survives, got %A{banner}")
        Assert.True(anyLine "soonest: lease frees in ~" banner, $"the lease window survives, got %A{banner}")
        Assert.True(anyLine "1 item(s) are in BACKLOG" banner, $"the column fact must NOT be hidden by the claim, got %A{banner}")
        Assert.True(anyLine "--include-backlog" banner, $"the remedy must survive alongside the wait, got %A{banner}")

    [<Fact>]
    let ``#636 WITH --include-backlog a Backlog row is scheduled, and no banner claims it was withheld`` () =
        // The invariant the whole banner rests on: `WrongStatus Backlog` is reachable ONLY when the flag is
        // absent. With it, the row falls through to the ordinary checks and is handed out — so there is
        // nothing withheld to report, and no banner at all.
        let r = schedule true None [] [ backlogItem 1 [ "src/A.fs" ] ] |> ok

        Assert.Equal<int list>([ 1 ], chosenNumbers r)
        Assert.Empty(starvedBanner 120 r)

    [<Fact>]
    let ``#636 an ORG-WIDE batch names EVERY repo it withheld from, not just the first`` () =
        // A batch is NOT repo-scoped: `--repo` is optional, and without it the scan keeps every row on the
        // org board. A remedy built from `List.head` would print one `take --repo` under a count spanning
        // several — the worker runs it, addresses a subset, and the rest stay invisible with the line still
        // claiming they were covered.
        let other n paths =
            { item n paths with
                Ref = refIn "FS-GG" "FS.GG.Game" n
                Status = Backlog }

        let r = run [] [ backlogItem 1 [ "src/A.fs" ]; other 2 [ "src/B.fs" ] ]
        let banner = starvedBanner 120 r

        Assert.True(anyLine "2 item(s) are in BACKLOG" banner, $"both repos' items are counted, got %A{banner}")
        Assert.True(anyLine "fsgg-coord take --repo FS.GG.SDD --include-backlog" banner, $"expected the SDD remedy, got %A{banner}")
        Assert.True(anyLine "fsgg-coord take --repo FS.GG.Game --include-backlog" banner, $"expected the Game remedy, got %A{banner}")

    [<Fact>]
    let ``#636 an ORG-WIDE reap advice names EVERY repo with a dead lease`` () =
        // The same defect, in the line six above the one #636 added — and the false premise ("repo-scoped by
        // construction") that generated both. Two expired leases in two repos must yield two reap commands;
        // naming only the first leaves the other repo's lease uncollected under a line that counted it.
        let deadIn repo n =
            resv repo [ $"src/Dead%d{n}" ] (LiveClaim(WorkerId $"ghost-%d{n}", refIn "FS-GG" repo (900 + n), 99999, None))

        let cand repo n =
            { item n [ $"src/Dead%d{n}/Sub" ] with
                Ref = refIn "FS-GG" repo n }

        let r = run [ deadIn "FS.GG.SDD" 1; deadIn "FS.GG.Game" 2 ] [ cand "FS.GG.SDD" 1; cand "FS.GG.Game" 2 ]
        Assert.Empty(r.Chosen)

        let banner = starvedBanner 120 r

        Assert.True(anyLine "2 of those lease(s) have EXPIRED" banner, $"both dead leases are counted, got %A{banner}")
        Assert.True(anyLine "fsgg-coord reap --repo FS.GG.SDD --apply" banner, $"expected the SDD reap, got %A{banner}")
        Assert.True(anyLine "fsgg-coord reap --repo FS.GG.Game --apply" banner, $"expected the Game reap, got %A{banner}")

    [<Fact>]
    let ``#636 a Ready-starved queue gains no untriaged noise`` () =
        // Silence when nothing was withheld at the column: a queue starved by blockers alone is still
        // #440's per-item business, and an UNTRIAGED banner over it would be the noise #428 refuses.
        let blocked =
            { item 1 [ "src/A.fs" ] with
                Blockers =
                    [ { Ref = Some(ref 999)
                        Raw = (ref 999).Short
                        State = BlockerOpen } ] }

        let r = run [] [ blocked ]
        Assert.Empty(r.Chosen)
        Assert.Empty(starvedBanner 120 r)

    [<Fact>]
    let ``#428 a lease over its clock but with a live PR is NOT reapable — no phantom EXPIRED, no reap advice`` () =
        // #581. A `HeldByLiveWork` claim is past its lease by the clock, yet its open PR proves the work is
        // alive — so it is not a reap. The banner must NOT print "lease EXPIRED — reapable" (there is no
        // reap advice to match it) nor advertise a collection that would break a lock over live work.
        let liveWork =
            { item 1 [ "scripts/fsgg-coord" ] with
                Claim =
                    Some(
                        { Worker = WorkerId "w-alice"
                          Session = None
                          AgeSeconds = 99999
                          PreviousStatus = None },
                        LeaseExpiredPrOpen 4242
                    ) }

        // The item is HELD by its own live work — the ONLY thing queued. (A separate candidate OVERLAPPING
        // it would see a plain `LiveClaim` reservation, which carries no PR-liveness; that case is the
        // accepted disposition where the banner advises `reap`, and `reap` re-probes and refuses the live
        // one. This test isolates the verdict the engine CAN know is live-work: the item's own.)
        let r = run [] [ liveWork ]
        Assert.Empty(r.Chosen)

        let banner = starvedBanner 120 r
        Assert.True(anyLine "this queue is BUSY, not empty" banner, $"still BUSY, got %A{banner}")
        Assert.False(anyLine "EXPIRED" banner, $"a live-work over-run is not reapable, so no EXPIRED line, got %A{banner}")
        Assert.False(anyLine "collect them: fsgg-coord reap" banner, $"no reap advice for live work, got %A{banner}")
        Assert.True(anyLine "soonest: lease unknown" banner, $"no live lease to wait on, so 'lease unknown', got %A{banner}")

    // ================================================================================================
    // #1092 — THE DEADLOCK BANNER. A ring is the one starved cause that waiting cannot fix.
    // ================================================================================================
    // Every other starved-queue line describes a queue that DRAINS: a lease frees, a column is triaged.
    // A `Blocked by` ring drains never, and per-item it printed as "Status is Blocked" — the same words
    // a ten-minute block gets — under "this queue is BUSY", which implies it drains. It does not. This
    // is the aggregate the per-item explanation cannot give.

    /// The live incident, on the board: .github#1059 -> #1063 -> #1073 -> #1059, every edge open.
    let private ringItem n blockedBy =
        { item n [ $"src/%d{n}.fs" ] with
            Ref = refIn "FS-GG" ".github" n
            Blockers =
                blockedBy
                |> List.map (fun b ->
                    { Ref = Some(refIn "FS-GG" ".github" b)
                      Raw = (refIn "FS-GG" ".github" b).Short
                      State = BlockerOpen }) }

    [<Fact>]
    let ``#1092 a `Blocked by` ring is named as a DEADLOCK, not swallowed by 'BUSY'`` () =
        let r = run [] [ ringItem 1059 [ 1063 ]; ringItem 1063 [ 1073 ]; ringItem 1073 [ 1059 ] ]
        Assert.Empty(r.Chosen)

        let banner = starvedBanner 120 r

        Assert.True(
            anyLine "DEADLOCKED: 3 item(s) form a `Blocked by` CYCLE" banner,
            $"expected the deadlock line naming all three, got %A{banner}")

        // Every member and its in-ring edge is named — the worker has to pick an edge to cut.
        Assert.True(anyLine ".github#1059 — Blocked by .github#1063" banner, $"%A{banner}")
        Assert.True(anyLine ".github#1063 — Blocked by .github#1073" banner, $"%A{banner}")
        Assert.True(anyLine ".github#1073 — Blocked by .github#1059" banner, $"%A{banner}")

        // A ring is NOT a wait: the word BUSY must not appear over a queue that never drains.
        Assert.False(anyLine "this queue is BUSY" banner, $"a ring is not BUSY — it never drains, got %A{banner}")

    [<Fact>]
    let ``#1092 the deadlock banner LEADS, and composes with a BUSY section`` () =
        // A board with BOTH a ring and an item queued behind a live claim: the ring leads (it is the
        // cause nothing else clears), and the BUSY line still appears for the drainable part. #636's
        // composition rule, extended.
        let held' = item 900 [ "src/held.fs" ] |> held "otter-1" 0
        let r =
            run
                [ resv "FS.GG.SDD" [ "src/held.fs" ] (LiveClaim(WorkerId "otter-1", ref 900, 0, None)) ]
                [ ringItem 1 [ 2 ]; ringItem 2 [ 1 ]; held'; item 901 [ "src/held.fs" ] ]

        let banner = starvedBanner 120 r
        Assert.True(anyLine "DEADLOCKED: 2 item(s)" banner, $"expected the ring, got %A{banner}")
        Assert.True(anyLine "this queue is BUSY, not empty" banner, $"expected the BUSY section too, got %A{banner}")
        // The ring comes before the BUSY line.
        let idxRing = banner |> List.findIndex (fun l -> l.Contains "DEADLOCKED")
        let idxBusy = banner |> List.findIndex (fun l -> l.Contains "BUSY")
        Assert.True(idxRing < idxBusy, $"the ring must lead, got %A{banner}")

    [<Fact>]
    let ``#1092 a queue blocked by OFF-BOARD issues is still no ring — #428's invariant holds`` () =
        // The existing "#428 starved by BLOCKERS alone gets no banner" case must keep passing: those
        // blockers name ref 999, not a candidate, so no edge is drawn and there is no ring. This asserts
        // the deadlock section adds nothing there.
        let blocked n =
            { item n [ $"src/%d{n}.fs" ] with
                Blockers = [ { Ref = Some(ref 999); Raw = (ref 999).Short; State = BlockerOpen } ] }

        let banner = starvedBanner 120 (run [] [ blocked 1; blocked 2 ])
        Assert.False(anyLine "DEADLOCKED" banner, $"off-board blockers draw no edge, so no ring, got %A{banner}")
        Assert.Empty(banner)

    // ================================================================================================
    // #669 — THE PARTITION. Every open item is STARTABLE, or NAMED WITH A REASON. Never absent from both.
    // ================================================================================================
    // This is #669's headline rule, and #266's shape at the scheduler layer: "nothing schedulable" and
    // "everything is accounted for" must not be the same answer. The individual-case tests above each pin
    // ONE verdict; none of them asserts the WHOLE partition — that over a heterogeneous board, the batch
    // leaves no candidate in the gap between the startable list and the skip-reason list. #669 sat open
    // for exactly that: the rule HOLDS, and nothing said so, so four workers re-measured it.
    //
    // The partition it asserts, over an uncapped run (a cap is the ONE licensed way to leave a candidate
    // unseen, and it must flag `Truncated` when it does — pinned separately above):
    //   * every candidate gets a decision                        (no open item vanishes from the accounting)
    //   * Chosen == exactly the decisions whose verdict is Startable
    //   * Chosen and the reason-bearing decisions are DISJOINT and their union is every candidate
    //   * every non-startable decision renders a NON-EMPTY reason that NAMES the item
    //     — the "NAMED WITH A REASON" half, in the operator's own words (`Batch.explainDecision`).

    /// A board that exercises the spread of schedulability verdicts a real queue produces — one item per
    /// reason, plus two disjoint startables and one that overlaps a batch member. Numbered so the greedy
    /// fold reaches #14 (chosen) before #15 (which then overlaps it).
    let private mixedBoard =
        let blockerOpen = { Ref = Some(ref 999); Raw = (ref 999).Short; State = BlockerOpen }

        let withClaim liveness it =
            { it with
                Claim =
                    Some(
                        { Worker = WorkerId "wren-1"
                          Session = None
                          AgeSeconds = 60
                          PreviousStatus = None },
                        liveness
                    ) }

        [ item 1 [ "src/a1.fs" ] // Startable
          { item 2 [ "src/a2.fs" ] with Status = Backlog } // WrongStatus Backlog (allowBacklog=false)
          { item 3 [ "src/a3.fs" ] with Status = NoStatus } // WrongStatus NoStatus — #669's named dead-code arm
          { item 4 [ "src/a4.fs" ] with Status = InProgress } // WrongStatus InProgress
          { item 5 [ "src/a5.fs" ] with State = Closed } // IssueClosed
          { item 6 [] with TouchSet = Undeclared } // NoTouchSet
          { item 7 [] with TouchSet = DeclaredNone } // DeliberatelyNoTouchSet
          { item 8 [] with TouchSet = Declared [ Unmatchable "**/*.fs" ] } // UnusableTouchSet
          { item 9 [ "src/a9.fs" ] with Blockers = [ blockerOpen ] } // BlockedBy
          item 10 [ "src/a10.fs" ] |> held "otter-2" 60 // HeldBy
          item 11 [ "src/a11.fs" ] |> withClaim (LeaseExpiredPrOpen 4242) // HeldByLiveWork
          { item 12 [ "src/a12.fs" ] with ItemPr = Some 777 } // ItemPrOpen
          { item 13 [] with TouchSet = Unreadable "the issue body returned HTTP 500" } // Undetermined
          item 14 [ "src/shared.fs" ] // Startable
          item 15 [ "src/shared.fs" ] ] // OverlapsInFlight — collides with #14, chosen first

    [<Fact>]
    let ``#669 every candidate is chosen or explained — the partition has no gap`` () =
        let r = run [] mixedBoard

        // Nothing was left unseen: an uncapped run decides EVERY candidate. This is the invariant #669
        // states — a candidate absent from the decisions is a row that vanished with no verdict at all.
        Assert.False(r.Truncated, "an uncapped run leaves nothing unseen")
        Assert.Equal(List.length mixedBoard, List.length r.Decisions)

        let allNumbers = mixedBoard |> List.map (fun i -> i.Ref.Number) |> Set.ofList
        let chosen = r.Chosen |> List.map (fun i -> i.Ref.Number) |> Set.ofList

        let explained =
            r.Decisions
            |> List.choose (fun d ->
                match d.Result with
                | Startable -> None
                | _ -> Some d.Item.Ref.Number)
            |> Set.ofList

        // Chosen is EXACTLY the startable decisions — the two lists are two views of one fold, not two
        // independent computations that could disagree.
        let startableDecisions =
            r.Decisions
            |> List.choose (fun d ->
                match d.Result with
                | Startable -> Some d.Item.Ref.Number
                | _ -> None)
            |> Set.ofList

        Assert.Equal<Set<int>>(chosen, startableDecisions)

        // THE PARTITION: disjoint, and together they are every candidate. No item in both; none in neither.
        Assert.True(Set.isEmpty (Set.intersect chosen explained), "no item is both startable and explained")
        Assert.Equal<Set<int>>(allNumbers, Set.union chosen explained)

    [<Fact>]
    let ``#669 every passed-over candidate is NAMED WITH A REASON, in the operator's words`` () =
        let r = run [] mixedBoard

        // The rule is not "every item has a verdict" — a verdict a worker cannot read is the #266 fail
        // whose remedy this issue is. Each non-startable decision must render an English reason that names
        // the item, via the same edge `take` prints (`Batch.explainDecision`, which folds in the holder).
        for d in r.Decisions do
            match d.Result with
            | Startable -> ()
            | _ ->
                let sentence = explainDecision 120 d
                Assert.False(System.String.IsNullOrWhiteSpace sentence, $"%A{d.Result} rendered an empty reason")

                Assert.True(
                    sentence.Contains d.Item.Ref.Short,
                    $"the reason for %A{d.Result} does not name the item it is about: %s{sentence}")

    // ================================================================================================
    // PRIORITY-GREEDY LANE PACKING (.github#1598)
    // ================================================================================================
    // The fold used to walk candidates in ISSUE-NUMBER order and admit whatever maximal disjoint set fell
    // out. That is not a priority, and on 2026-07-27 it cost the board its whole afternoon: three `P0
    // Decisions` items were refused as "overlaps batch member .github#1560" while #1560 was hardening, and
    // the only lever left to the driver was to park nineteen rows in `Backlog` to force the queue.
    //
    // The disjointness guarantee is UNCHANGED here and that is the point — what changed is the ORDER the
    // fold walks, so the highest-ranked schedulable item is always the one that gets the contested lane.

    let private classed c it = { it with Class = Some c }
    let private phased p it = { it with Phase = Some p }
    let private aged d it = { it with AgeDays = Some d }

    [<Fact>]
    let ``#1887 batch never admits a decision-class row, even when its touch-set is disjoint`` () =
        let decision =
            { item 1887 [ "src/Decision" ] with
                Class = Some Decision
                BoardClass = Some Hardening
                HumanBlock = None }

        let ordinary = item 1888 [ "src/Ordinary" ] |> classed Hardening
        let r = run [] [ decision; ordinary ]

        Assert.Equal<int list>([ 1888 ], chosenNumbers r)
        Assert.Equal(Some(AwaitingHuman AwaitingHumanDecision), verdictOf r 1887)

    [<Fact>]
    let ``#1887 batch negative control still admits the same row when its body class is hardening`` () =
        let ordinary =
            { item 1887 [ "src/Decision" ] with
                Class = Some Hardening
                BoardClass = Some Decision
                HumanBlock = None }

        Assert.Equal<int list>([ 1887 ], chosenNumbers (run [] [ ordinary ]))

    [<Fact>]
    let ``#1598 AC2 THE 2026-07-27 CASE — a P0 defect and a hardening item on one path: the P0 wins`` () =
        // The fixture the acceptance criterion asks for, reproduced exactly: one contended path, a
        // structural P0 item and a lower-value one, and the P0 carries the LARGER issue number — which is
        // precisely why it used to lose. Under the old ordering this returned [ 1 ]; there is no board
        // state that made it return [ 2 ].
        let hardening =
            item 1 [ "scripts/coordination-sync" ] |> classed Hardening |> phased P0Decisions

        let p0 =
            item 2 [ "scripts/coordination-sync" ] |> classed Defect |> phased P0Decisions

        let r = run [] [ hardening; p0 ]

        Assert.Equal<int list>([ 2 ], chosenNumbers r)

        match verdictOf r 1 with
        | Some(OverlapsInFlight _) -> ()
        | other -> failwithf "expected the hardening item to be the one displaced, got %A" other

    [<Fact>]
    let ``#1598 the highest-ranked schedulable item is ALWAYS admitted, never merely usually`` () =
        // The property, over a board where the top-ranked item collides with several others. Whatever the
        // pack does with the rest, the head of the ranking must be in it.
        let hub =
            item 90 [ "src/Shared" ] |> classed Defect |> phased P0Decisions

        let r =
            run
                []
                [ item 1 [ "src/Shared/A.fs" ]
                  item 2 [ "src/Shared/B.fs" ]
                  item 3 [ "src/Shared/C.fs" ]
                  hub ]

        Assert.Contains(90, chosenNumbers r)

    [<Fact>]
    let ``#1598 disjointness is UNCHANGED — priority reorders lanes, it never overlaps them`` () =
        // The one guarantee that may not be traded for priority. A high-rank item does not get to run
        // alongside something it collides with; it gets to be the one that wins the collision.
        let r =
            run
                []
                [ item 1 [ "src/A.fs" ] |> classed Hardening
                  item 2 [ "src/A.fs" ] |> classed Defect
                  item 3 [ "src/B.fs" ] ]

        Assert.Equal<int list>([ 2; 3 ], List.sort (chosenNumbers r))

    [<Fact>]
    let ``#1598 lanes narrow items would have shared are still BOTH admitted — parallelism is not sacrificed`` () =
        // Priority-greedy is not "one item at a time". Where nothing collides, everything still runs, and
        // the only observable difference from the old scheduler is the order of `Chosen`.
        let r =
            run
                []
                [ item 1 [ "src/A.fs" ] |> classed Hardening
                  item 2 [ "src/B.fs" ] |> classed Defect ]

        Assert.Equal<int list>([ 2; 1 ], chosenNumbers r)

    [<Fact>]
    let ``#1598 a cap hands the ONE lane to the top-ranked item, not to the lowest number`` () =
        // `next` and `take` are this fold capped at one, so this is the assertion that actually decides
        // what a worker is handed. Every candidate here is disjoint — nothing collides — so under the old
        // ordering the cap always yielded #1.
        let r =
            schedule
                false
                (Some 1)
                []
                [ item 1 [ "src/A.fs" ]
                  item 2 [ "src/B.fs" ] |> classed Defect
                  item 3 [ "src/C.fs" ] ]
            |> ok

        Assert.Equal<int list>([ 2 ], chosenNumbers r)

    [<Fact>]
    let ``#1598 AC4 an item displaced by better-ranked work eventually wins a lane by starving`` () =
        // THE STARVATION CRITERION, as a before/after over ONE board. The only thing that changes between
        // the two runs is the passage of time on the displaced item — no field is set, no edge is drawn,
        // nothing is triaged. If escalation did not exist, an item whose touch-set permanently collides
        // with a better-classed one would never run again.
        let contendedPath = [ "src/Contended.fs" ]

        let winner =
            item 1 contendedPath |> classed Defect |> phased P0Decisions |> aged 0

        let displaced fresh =
            item 2 contendedPath |> aged (if fresh then 1 else Rank.StarvationDays + 1)

        // Day one: the defect takes the lane, exactly as it should.
        Assert.Equal<int list>([ 1 ], chosenNumbers (run [] [ winner; displaced true ]))

        // Weeks later, nothing else having changed: the starved item takes it.
        Assert.Equal<int list>([ 2 ], chosenNumbers (run [] [ winner; displaced false ]))

    // ================================================================================================
    // `batch --explain` (.github#1598 AC5)
    // ================================================================================================

    [<Fact>]
    let ``#1598 AC5 explainRanking prints every candidate, its verdict, and its rank inputs`` () =
        let r =
            run
                []
                [ item 1 [ "src/A.fs" ] |> classed Hardening
                  item 2 [ "src/A.fs" ] |> classed Defect |> phased P0Decisions ]

        let lines = explainRanking r
        let text = String.concat "\n" lines

        // The ADMITTED item leads the list, because the list IS the ranking.
        Assert.Contains("ADMITTED", List.item 2 lines)
        Assert.Contains("#2", List.item 2 lines)

        // Both candidates appear, with the inputs that produced their positions.
        Assert.Contains("defect", text)
        Assert.Contains("P0 Decisions", text)
        Assert.Contains("hardening", text)
        Assert.Contains("refused", text)

    [<Fact>]
    let ``#1598 AC5 an admitted item reports how many lanes it DISPLACED`` () =
        // The stated risk, made countable: one wide high-rank item can exclude several narrow ones a
        // maximal pack would have run together. That trade must be reported, or a drop in parallelism is
        // indistinguishable from a regression.
        let wide = item 9 [ "src" ] |> classed Defect

        let r =
            run
                []
                [ item 1 [ "src/A.fs" ]
                  item 2 [ "src/B.fs" ]
                  item 3 [ "src/C.fs" ]
                  wide ]

        Assert.Equal(3, displacedBy r wide.Ref)

        let admitted =
            explainRanking r |> List.find (fun l -> l.Contains "ADMITTED")

        Assert.Contains("displaced 3", admitted)

    [<Fact>]
    let ``#1598 AC5 an item with no priority evidence is told it SORTS LAST, not that it was penalised`` () =
        let r =
            run [] [ item 1 [ "src/A.fs" ] |> classed Defect; item 2 [ "src/A.fs" ] ]

        let refused =
            explainRanking r |> List.find (fun l -> l.Contains "refused")

        Assert.Contains("no priority evidence: sorts last", refused)

    [<Fact>]
    let ``#1598 explainRanking is SILENT over an empty candidate set`` () =
        Assert.Empty(explainRanking (run [] []))

    [<Fact>]
    let ``#1598 every decision carries the rank the FOLD used, not one a renderer re-derived`` () =
        let r =
            run [] [ item 1 [ "src/A.fs" ] |> classed Defect |> phased P3Governance |> aged 12 ]

        let d = List.exactlyOne r.Decisions

        Assert.Equal(Some Defect, d.Rank.Class)
        Assert.Equal(Some P3Governance, d.Rank.Phase)
        Assert.Equal(Some 12, d.Rank.AgeDays)
        Assert.Equal(1, d.Rank.Number)
