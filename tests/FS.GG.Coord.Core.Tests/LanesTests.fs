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
          Status = Ready
          State = Open
          TouchSet = Declared(paths |> List.map Matchable)
          Blockers = []
          Claim = None }

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
    let ``a PARTLY-unmatchable declaration still lanes on the tokens that DO match`` () =
        // It reserves something, so it is real. The unmatchable token is a separate finding (the
        // scheduler already reports it); it must not cost the item its lane.
        let mixed =
            item 1 [] |> withTouchSet (Declared [ Matchable "src/A/"; Unmatchable "**/x" ])

        let p = partition [ mixed; item 2 [ "src/A/thing.fs" ] ]

        Assert.Equal(1, List.length p.Lanes)
        Assert.Equal(2, List.length (List.head p.Lanes).Items)
        Assert.Empty p.Unlanable

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
