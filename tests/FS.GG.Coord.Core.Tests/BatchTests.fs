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
          Claim = None }

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

        Assert.Equal(Some(LiveClaim(WorkerId "w-alice", ref 1, 900)), d.CollidedWith)

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
              Holder = LiveClaim(WorkerId "w-bob", refIn "FS-GG" "FS.GG.Rendering" 9, 60) }

        let r = run [ elsewhere ] [ item 1 [ "scripts/build.sh" ] ]

        Assert.Equal<int list>([ 1 ], chosenNumbers r)

    [<Fact>]
    let ``an in-flight reservation in the SAME repo does hold them`` () =
        let here =
            { Owner = "FS-GG"
              Repo = "FS.GG.SDD"
              Paths = Declared [ Matchable "scripts/build.sh" ]
              Holder = LiveClaim(WorkerId "w-bob", ref 9, 60) }

        let r = run [ here ] [ item 1 [ "scripts/build.sh" ] ]

        Assert.Empty(r.Chosen)
        Assert.Equal(Some(LiveClaim(WorkerId "w-bob", ref 9, 60)), (List.head r.Decisions).CollidedWith)

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
              Holder = LiveClaim(WorkerId "w-bob", ref 9, 60) }

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
