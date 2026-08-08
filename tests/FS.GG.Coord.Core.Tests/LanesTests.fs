namespace FS.GG.Coord.Tests

open Xunit
open FS.GG.Coord
open FS.GG.Coord.Types
open FS.GG.Coord.Lanes

/// LANES, as tests — and every one of them is a way two workers end up in one file.
///
/// A lane is a claim about SAFETY: "these items cannot contend, ever." If that claim is wrong the
/// scheduler hands two workers the same file, which is the failure the entire protocol exists to
/// prevent. So the properties below are not conveniences — they are the whole contract:
///
///   * TRANSITIVITY. A—B and B—C means A and C share a lane even though they do not touch, because B
///     serialises them. Getting this wrong is the classic union-find bug and it reports MORE
///     parallelism than exists, which is the direction that hurts.
///   * REPO SCOPING. `scripts/foo` in two different repos is two different files (#312). Joining them
///     invents a collision and HALVES the board's real parallelism.
///   * THE THREE UNLANABLE STATES. An epic (`Paths: none`), a forgotten declaration, and a declaration
///     whose tokens match nothing are three different facts (#496, #273). The middle one is a chore.
///     The last one is worse than the middle one. The first one must never be "fixed".
module LanesTests =

    let private refIn owner repo n : Ref =
        { Owner = owner; Repo = repo; Number = n }

    let private ref n = refIn "FS-GG" "FS.GG.SDD" n

    let private itemAt r paths : Item =
        { Ref = r
          PathRepo = r.Repo
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
          DeliveryRoute = DeliveryRoute.Current { Schema = DeliveryRoute.Schema; Subject = "test"; SubjectRevision = "test"; Route = Some DeliveryRoute.Lightweight; Agent = "test"; Timestamp = "2026-01-01T00:00:00Z"; ReasonCodes = [ "test" ]; Rationale = "test"; DeclaredImpacts = [ "test" ]; ObservedFacts = [ "test" ]; SddWorkId = None; SpecHome = None; RequiredGates = [] }
          Severity = Unset
          Phase = None
          AgeDays = None }

    let private item n paths = itemAt (ref n) paths

    let private withTouchSet ts (it: Item) = { it with TouchSet = ts }

    let private heldBy w (it: Item) =
        { it with
            Claim =
                Some(
                    { Worker = WorkerId w
                      Session = None
                      AgeSeconds = 10
                      PreviousStatus = Some Ready },
                    LeaseHeld
                ) }

    let private always (_: Item) = true

    let private laneOf (p: Partition) (n: int) =
        p.Lanes |> List.find (fun l -> l.Items |> List.exists (fun i -> i.Ref.Number = n))

    // ---- the core property ---------------------------------------------------------------------

    [<Fact>]
    let ``disjoint items are separate lanes — that is the parallelism`` () =
        let p =
            partition [ item 1 [ "src/A/" ]; item 2 [ "src/B/" ]; item 3 [ "docs/" ] ]

        Assert.Equal(3, List.length p.Lanes)
        Assert.Equal(3, List.length (free always p))

    [<Fact>]
    let ``overlapping items share a lane — they serialise`` () =
        let p = partition [ item 1 [ "src/A/" ]; item 2 [ "src/A/thing.fs" ] ]

        Assert.Equal(1, List.length p.Lanes)
        Assert.Equal(2, List.length (List.head p.Lanes).Items)

    [<Fact>]
    let ``TRANSITIVITY — A touches B, B touches C, so A and C share a lane though they never touch`` () =
        // The union-find bug that reports more parallelism than exists. A and C are disjoint; B glues
        // them. Scheduling A and C concurrently is safe ONLY if B is not in play — but B is on the
        // board, so all three contend and the lane is one.
        let a = item 1 [ "src/A/" ]
        let b = item 2 [ "src/A/"; "src/C/" ]
        let c = item 3 [ "src/C/" ]

        let p = partition [ a; b; c ]

        Assert.Equal(1, List.length p.Lanes)
        Assert.Equal(3, List.length (List.head p.Lanes).Items)
        Assert.Equal(1, List.length (free always p)) // ONE worker, not three

    [<Fact>]
    let ``REPO SCOPING — the same token in two repos is two different files, so two lanes (#312)`` () =
        // Joining them would invent a collision and halve the board's real parallelism.
        let a = itemAt (refIn "FS-GG" "FS.GG.SDD" 1) [ "scripts/foo" ]
        let b = itemAt (refIn "FS-GG" "FS.GG.Game" 2) [ "scripts/foo" ]

        let p = partition [ a; b ]

        Assert.Equal(2, List.length p.Lanes)
        Assert.Equal(2, List.length (free always p))

    [<Fact>]
    let ``a lane never spans repos`` () =
        let p =
            partition
                [ itemAt (refIn "FS-GG" "FS.GG.SDD" 1) [ "src/" ]
                  itemAt (refIn "FS-GG" "FS.GG.Game" 2) [ "src/" ] ]

        for lane in p.Lanes do
            let repos = lane.Items |> List.map (fun i -> (i.Ref.Owner, i.Ref.Repo)) |> List.distinct
            Assert.Equal(1, List.length repos)

    [<Fact>]
    let ``#1732 paths use their declared repo scope, not the issue host repo`` () =
        let host = refIn "FS-GG" ".github" 0
        let audio = { itemAt { host with Number = 1 } [ "scripts/skill-view" ] with PathRepo = "FS.GG.Audio" }
        let github = { itemAt { host with Number = 2 } [ "scripts/skill-view" ] with PathRepo = ".github" }

        let p = partition [ audio; github ]

        Assert.Equal(2, List.length p.Lanes)

    // ---- occupancy and the ceiling ---------------------------------------------------------------

    [<Fact>]
    let ``a lane holding a live claim is OCCUPIED and is not free capacity`` () =
        let p =
            partition [ item 1 [ "src/A/" ] |> heldBy "wren-1"; item 2 [ "src/B/" ] ]

        Assert.Equal(2, List.length p.Lanes)

        let held = laneOf p 1
        Assert.Equal<WorkerId list>([ WorkerId "wren-1" ], held.HeldBy)

        // The ceiling is ONE, not two: somebody is standing in the other lane.
        Assert.Equal(1, List.length (free always p))

    [<Fact>]
    let ``a lane with no STARTABLE item is not capacity either`` () =
        // Counting it would overstate the ceiling and send a worker to an empty lane — #440 ("no
        // schedulable item" over a board full of work) wearing a lane's hat.
        let p = partition [ item 1 [ "src/A/" ]; item 2 [ "src/B/" ] ]

        let onlyOne (it: Item) = it.Ref.Number = 1

        Assert.Equal(2, List.length p.Lanes)
        Assert.Equal(1, List.length (free onlyOne p))

    [<Fact>]
    let ``THE CHOKEPOINT — one over-broad touch-set glues the whole board into a single lane (#428)`` () =
        // This is `scripts/fsgg-coord` in real life: four open items, one lane, one worker. The
        // partition makes it visible as a STRUCTURE rather than as a series of surprises.
        let chokepoint = item 1 [ "scripts/" ]

        let p =
            partition
                [ chokepoint
                  item 2 [ "scripts/a" ]
                  item 3 [ "scripts/b" ]
                  item 4 [ "scripts/c" ] ]

        Assert.Equal(1, List.length p.Lanes)
        Assert.Equal(1, List.length (free always p)) // four items of work, ONE worker

    // ---- the three unlanable states, kept apart (#496, #273) --------------------------------------

    [<Fact>]
    let ``an UNDECLARED item is a chore — real work nobody can pick up`` () =
        let p = partition [ item 1 [ "src/A/" ] |> withTouchSet Undeclared ]

        Assert.Empty p.Lanes

        match List.exactlyOne p.Unlanable with
        | NoTouchSet _ -> ()
        | other -> failwith $"expected NoTouchSet, got %A{other}"

    [<Fact>]
    let ```Paths: none` is DELIBERATE — an epic, and never a chore`` () =
        let p = partition [ item 1 [] |> withTouchSet DeclaredNone ]

        Assert.Empty p.Lanes

        match List.exactlyOne p.Unlanable with
        | DeliberatelyNone _ -> ()
        | other -> failwith $"expected DeliberatelyNone, got %A{other}"

    [<Fact>]
    let ``a declaration whose tokens match NOTHING is not a lane of one — it reserves nothing (#273)`` () =
        // The dangerous one. It LOOKS declared, so it passes a glance — but it reserves nothing, so it
        // would read as disjoint from every other worker: the lock succeeding under exactly the
        // conditions it exists to prevent. A lane of one would advertise it as safe, startable work.
        let broken =
            item 1 [] |> withTouchSet (Declared [ Unmatchable "**/foo" ])

        let p = partition [ broken; item 2 [ "src/B/" ] ]

        Assert.Equal(1, List.length p.Lanes) // ONLY the good item
        Assert.Equal(2, (List.head p.Lanes).Id.Number)

        match List.exactlyOne p.Unlanable with
        | UnusableTokens(_, tokens) -> Assert.Equal<string list>([ "**/foo" ], tokens)
        | other -> failwith $"expected UnusableTokens, got %A{other}"

    [<Fact>]
    let ``a PARTLY-unmatchable declaration is NOT laned — the scheduler refuses it, so it is a chore (#864)`` () =
        // THIS TEST USED TO ASSERT THE OPPOSITE, and it was green the whole time — beside a green
        // `SchedulabilityTests` asserting that this very item is NEVER startable. Two modules, one
        // question, opposite answers, a passing test pinning each. Its argument was "it reserves
        // something, so it is real; the unmatchable token is a separate finding the scheduler already
        // reports" — which sounds right and is wrong twice:
        //
        //   * the scheduler does not merely REPORT it, it REFUSES the item forever. An item nobody can
        //     start cannot contend with anybody, so it was never a lane.
        //   * "it must not cost the item its lane" had it backwards. The lane cost the BOARD: the dead
        //     token still glues real lanes together through `TouchSet.conflicts`, understating the
        //     ceiling on a token that reserves NOTHING — and `explain`'s chore list never named it, so
        //     the one surface whose job is board health was silent about the broken declaration.
        let mixed =
            item 1 [] |> withTouchSet (Declared [ Matchable "src/A/"; Unmatchable "**/x" ])

        let p = partition [ mixed; item 2 [ "src/A/thing.fs" ] ]

        // ONLY the good item lanes. #1 is a chore, not a lane-mate — even though `src/A/` genuinely
        // overlaps #2's file.
        Assert.Equal(1, List.length p.Lanes)
        Assert.Equal(2, (List.head p.Lanes).Id.Number)
        Assert.Equal(1, List.length (List.head p.Lanes).Items)

        match List.exactlyOne p.Unlanable with
        | UnusableTokens(it, tokens) ->
            Assert.Equal(1, it.Ref.Number)
            // ONLY the dead token is named — the live `src/A/` is not the worker's problem to fix.
            Assert.Equal<string list>([ "**/x" ], tokens)
        | other -> failwith $"expected UnusableTokens, got %A{other}"

    [<Fact>]
    let ``LANES AND THE SCHEDULER AGREE ABOUT EVERY TOUCH-SET SHAPE — the invariant #864 broke`` () =
        // THE REGRESSION GUARD, and the reason the two green tests above could not catch each other:
        // nothing ever asserted the two modules against ONE another. ADR-0034's central promise is ONE
        // schedulability function, and `Lanes.free`/`explain` keep it by taking `startable` as a
        // parameter — but `partition` re-decided USABILITY on its own, before that parameter was ever
        // consulted. This pins the two together over every shape a touch-set can take, so a future
        // divergence fails HERE rather than on a board.
        //
        // The contract: an item `partition` puts in a lane must be one the scheduler could start, and an
        // item the scheduler refuses for its TOUCH-SET must be reported as unlanable. (Refusals for a
        // reason that is not the touch-set — a lock, a blocker, a column — are not `partition`'s
        // business: those items are laned and simply not startable yet.)
        let shapes =
            [ "every token live", Declared [ Matchable "src/A/" ], true
              "every token dead", Declared [ Unmatchable "**/x" ], false
              "SOME tokens dead — the #864 case", Declared [ Matchable "src/A/"; Unmatchable "**/x" ], false
              "some tokens dead, dead one first", Declared [ Unmatchable "**/x"; Matchable "src/A/" ], false
              "no declaration", Undeclared, false
              "the `none` sentinel", DeclaredNone, false
              "body never read", Unreadable "boom", false ]

        for name, ts, expectedLanable in shapes do
            let it = item 1 [] |> withTouchSet ts
            let p = partition [ it ]

            let laned = p.Lanes |> List.collect (fun l -> l.Items) |> List.isEmpty |> not
            let startable = Schedulability.schedulable false [] it = Schedulability.Startable

            // THE RULE ITSELF is the third party to the agreement (#945). `lint` was a THIRD copy of
            // this question for as long as `Usability` lacked the every/some distinction it renders —
            // it agreed, by hand, which is what these two did until they didn't. Pinning both consumers
            // to `usability` here means a future caller cannot quietly pick a different threshold: the
            // only thing left to get wrong is which CASE, and that is a match the compiler checks.
            //
            // Note what this asserts and what it does not: `Usable` is not "startable". The three
            // no-token shapes below are `Usable` and correctly unlanable, for reasons decided BEFORE
            // usability is asked (#496). So the agreement is one-directional, and stating it that way
            // is the honest form — an `=` here would be a green test asserting something false.
            let usable = TouchSet.usability ts = TouchSet.Usable

            if not usable then
                Assert.True(
                    (not laned && not startable),
                    $"%s{name}: TouchSet.usability says UNUSABLE, but Lanes said lanable=%b{laned} and Schedulability said startable=%b{startable} — a caller is deciding usability for itself again (#864, #945)"
                )

            Assert.True(
                (laned = expectedLanable),
                $"%s{name}: expected lanable=%b{expectedLanable}, partition said %b{laned}"
            )

            // THE AGREEMENT ITSELF: laned and startable are the same answer for every shape. If this
            // ever splits again, the two modules are deciding the question twice (#485, #864).
            Assert.True(
                (laned = startable),
                $"%s{name}: Lanes says lanable=%b{laned} but Schedulability says startable=%b{startable} — the two modules disagree about the same item (#864)"
            )

            // And an unlanable item is never silently dropped: it is on the chore list, by name.
            if not expectedLanable then
                Assert.Equal(1, List.length p.Unlanable)
                Assert.Equal(1, (List.exactlyOne p.Unlanable).Item.Ref.Number)

    // ---- determinism ------------------------------------------------------------------------------

    [<Fact>]
    let ``lane ids are DETERMINISTIC — two workers partitioning the same board agree`` () =
        // A lane you can NAME ("take lane #3") only works if everyone computes the same name. The id is
        // the lowest-numbered item in the component, and input order may not change it.
        let items = [ item 5 [ "src/A/" ]; item 2 [ "src/A/x" ]; item 9 [ "src/B/" ] ]

        let a = partition items
        let b = partition (List.rev items)

        let ids p = p.Lanes |> List.map (fun l -> l.Id.Number)

        Assert.Equal<int list>(ids a, ids b)
        Assert.Equal<int list>([ 2; 9 ], ids a) // the lane containing 2 and 5 is named 2

    [<Fact>]
    let ``an empty board has no lanes and no ceiling — not an error`` () =
        let p = partition []

        Assert.Empty p.Lanes
        Assert.Empty p.Unlanable
        Assert.Empty(free always p)

    // ---- the glue: WHICH declaration is costing the parallelism, and how much ----------------------

    [<Fact>]
    let ``the GLUE names the token holding a lane together, and what removing it would buy`` () =
        // Four items, all glued by one over-broad token. Drop it and they fly apart into four.
        // This is `scripts/fsgg-coord` in real life (#428) — and until now the only way to find out
        // WHICH token was doing it was to read forty issue bodies and guess.
        let items =
            [ item 1 [ "scripts/coord"; "src/A/" ]
              item 2 [ "scripts/coord"; "src/B/" ]
              item 3 [ "scripts/coord"; "src/C/" ]
              item 4 [ "scripts/coord"; "src/D/" ] ]

        let p = partition items
        Assert.Equal(1, List.length p.Lanes)

        let g = glue (List.head p.Lanes) |> List.head

        Assert.Equal("scripts/coord", g.Token)
        Assert.Equal(4, g.SplitsInto) // remove it and one lane becomes four
        Assert.Equal(4, List.length g.DeclaredBy)

    [<Fact>]
    let ``a LOAD-BEARING token splits nothing — the work really is coupled, and narrowing it would lie`` () =
        // Both items touch src/A. Removing `src/A/` from the analysis leaves them still joined by
        // nothing, so they DO fly apart — but the point of the ranking is that a token whose removal
        // buys 1 is worthless. Here `docs/` is that: it is declared by one item and glues nobody.
        let items = [ item 1 [ "src/A/"; "docs/" ]; item 2 [ "src/A/" ] ]

        let p = partition items
        let ranked = glue (List.head p.Lanes)

        let docs = ranked |> List.find (fun g -> g.Token = "docs/")
        Assert.Equal(1, docs.SplitsInto) // removing `docs/` buys NOTHING — they still share src/A
        Assert.Equal(1, List.length docs.DeclaredBy)

        // ...while the real coupling is src/A, and dropping it WOULD separate them.
        let srcA = ranked |> List.find (fun g -> g.Token = "src/A/")
        Assert.Equal(2, srcA.SplitsInto)

        // The ranking puts the payoff first.
        Assert.Equal("src/A/", (List.head ranked).Token)

    [<Fact>]
    let ``the ranking is DETERMINISTIC — two workers agree on which token to attack`` () =
        let items =
            [ item 1 [ "a/"; "z/" ]; item 2 [ "a/"; "y/" ]; item 3 [ "a/" ] ]

        let tokensOf its =
            (glue (List.head (partition its).Lanes)) |> List.map (fun g -> g.Token)

        Assert.Equal<string list>(tokensOf items, tokensOf (List.rev items))

    [<Fact>]
    let ``a lane of ONE has no glue — nothing is holding it to anything`` () =
        let p = partition [ item 1 [ "src/A/" ] ]
        Assert.Empty(glue (List.head p.Lanes))
