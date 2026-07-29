namespace FS.GG.Coord.Tests

open Xunit
open FS.GG.Coord
open FS.GG.Coord.Types

/// DERIVED PRIORITY (.github#1598) — the ordering `batch` and `take` pack lanes by.
///
/// Every assertion here is about a rank being COMPUTED. The item this suite exists for is not "the board
/// needs a priority field" — it is the opposite: a fifth hand-maintained field would drift from the thing
/// it describes, which is the defect class this repo closed five times in one day. So the properties worth
/// pinning are that each input actually MOVES the rank, that no input is stored, and that an absent input
/// never promotes.
module RankTests =

    let private ref n =
        { Owner = "FS-GG"
          Repo = ".github"
          Number = n }

    let private item n =
        { Ref = ref n
          Status = Ready
          State = Open
          TouchSet = Declared [ Matchable $"src/f%d{n}.fs" ]
          Blockers = []
          Claim = None
          ItemPr = None
          HumanBlock = None
          Predicate = None
          Class = None
          BoardClass = None
          Severity = Unset
          Phase = None
          AgeDays = None }

    let private blockedBy n state (it: Item) =
        { it with
            Blockers =
                it.Blockers
                @ [ { Ref = Some(ref n)
                      Raw = (ref n).Short
                      State = state } ] }

    /// The candidates in the order `Rank.key` puts them.
    let private order (items: Item list) =
        let counts = Rank.blockingCounts items

        items
        |> List.map (fun i -> i, Rank.ofItem counts i)
        |> List.sortBy (snd >> Rank.key)
        |> List.map (fun (i, _) -> i.Ref.Number)

    // ================================================================================================
    // AC1 — NO PRIORITY DATA, NO BEHAVIOUR CHANGE.
    // ================================================================================================

    [<Fact>]
    let ``#1598 a board with no priority data anywhere orders exactly as it did before — by issue number`` () =
        // THE SAFETY PROPERTY THE WHOLE ITEM RESTS ON. Every rank term above the issue number is `None`
        // or zero on a board nobody has classed, phased or blocked, so the number term alone survives and
        // the answer is the pre-#1598 answer. If this ever fails, landing the rewrite silently reordered
        // every repo that had not yet adopted `Class`.
        Assert.Equal<int list>([ 3; 7; 11 ], order [ item 11; item 3; item 7 ])

    [<Fact>]
    let ``#1598 an item with no rank inputs still schedules — it sorts LAST, it is not withheld`` () =
        let unranked = item 1
        let ranked = { item 99 with Class = Some Defect }

        // The unranked item has the SMALLER number and still loses — so it is genuinely sorted, not
        // merely left where it was. And it is present: `Rank` orders candidates, it never filters them.
        Assert.Equal<int list>([ 99; 1 ], order [ unranked; ranked ])

    [<Fact>]
    let ``#1598 isUnranked names exactly the no-evidence case`` () =
        let counts = Rank.blockingCounts [ item 1 ]
        Assert.True(Rank.isUnranked (Rank.ofItem counts (item 1)))

        for withEvidence in
            [ { item 1 with Class = Some Hardening }
              { item 1 with Phase = Some P8Net }
              { item 1 with AgeDays = Some 0 } ] do
            Assert.False(Rank.isUnranked (Rank.ofItem counts withEvidence))

    // ================================================================================================
    // AC3 — RANK IS COMPUTED, NEVER STORED.
    // ================================================================================================

    [<Fact>]
    let ``#1598 AC3 changing ONE Blocked by edge changes the rank, with no other edit anywhere`` () =
        // The acceptance criterion, literally. `#1` and `#2` are byte-identical apart from the edge that
        // `#3` draws at it — no field is set on either, and nothing is written anywhere. If rank were a
        // stored field this test could not be written at all.
        let before = [ item 1; item 2; item 3 ]
        Assert.Equal<int list>([ 1; 2; 3 ], order before)

        let after = [ item 1; item 2; item 3 |> blockedBy 2 BlockerOpen ]
        Assert.Equal<int list>([ 2; 1; 3 ], order after)

    [<Fact>]
    let ``#1598 blocking count is the number of OPEN items whose edge is still HOLDING`` () =
        let items =
            [ item 1
              item 2 |> blockedBy 1 BlockerOpen
              item 3 |> blockedBy 1 BlockerOpen
              // RESOLVED — a dependency that cleared is not a dependent, so it must not keep promoting
              // the thing it used to wait on.
              item 4 |> blockedBy 1 BlockerClosed
              // A CLOSED issue is not a dependent either: nobody is waiting on anything.
              { (item 5 |> blockedBy 1 BlockerOpen) with
                  State = Closed } ]

        Assert.Equal(2, Rank.blockingCounts items |> Map.find (ref 1))

    [<Fact>]
    let ``#1598 an UNPARSEABLE Blocked by edge is skipped, never guessed at`` () =
        // "Make the rank computation skip unparseable edges rather than guessing, and say so." Prose in a
        // dependency field has no ref by construction, so there is no node to credit — and inventing one
        // would distort every rank around it while looking like a measurement.
        let prose =
            { item 2 with
                Blockers =
                    [ { Ref = None
                        Raw = "RESOLVED: shipped last week"
                        State = BlockerUnparseable } ] }

        Assert.True(Rank.blockingCounts [ item 1; prose ] |> Map.isEmpty)

    [<Fact>]
    let ``#1598 one item naming the same blocker twice is ONE dependent`` () =
        let twice = item 2 |> blockedBy 1 BlockerOpen |> blockedBy 1 BlockerOpen
        Assert.Equal(1, Rank.blockingCounts [ item 1; twice ] |> Map.find (ref 1))

    // ================================================================================================
    // THE LEXICOGRAPHIC TIERS. Each test moves exactly ONE input.
    // ================================================================================================

    [<Fact>]
    let ``#1598 blocking count outranks Class — an item blocking two beats an unblocking defect`` () =
        let hub = item 50
        let defect = { item 1 with Class = Some Defect }

        let items =
            [ hub
              defect
              item 60 |> blockedBy 50 BlockerOpen
              item 61 |> blockedBy 50 BlockerOpen ]

        Assert.Equal(50, order items |> List.head)

    [<Fact>]
    let ``#1901 Severity ranks above Class and Unset ranks last`` () =
        let items =
            [ { item 1 with Severity = Low; Class = Some Defect }
              { item 2 with Severity = High; Class = Some Hardening }
              { item 3 with Severity = Critical; Class = None }
              { item 4 with Severity = Medium; Class = Some Defect }
              { item 5 with Severity = Unset; Class = Some Defect } ]

        Assert.Equal<int list>([ 3; 2; 4; 1; 5 ], order items)

    [<Fact>]
    let ``#1598 defect outranks hardening, and both outrank an unclassed row`` () =
        let items =
            [ { item 1 with Class = Some Hardening }
              { item 2 with Class = Some Defect }
              item 3 ]

        Assert.Equal<int list>([ 2; 1; 3 ], order items)

    [<Fact>]
    let ``#1598 a decision item ranks BELOW hardening — it must never be dispatched first`` () =
        // NOT `Class.fromBody`'s dominance order, deliberately. That order answers "what IS this item"
        // and puts `decision` above `hardening` because it is the stronger claim. THIS order answers
        // "what do we HAND a worker", and `decision` means a human must choose first — `Types.fsi` calls
        // it "surfaced, never dispatched".
        //
        // The scheduler cannot enforce that on its own: it refuses a decision item only through
        // ADR-0045's `Blocked on: human/decision` sentinel, so one classed by a `[decision]` TITLE alone,
        // with a real touch-set, is Startable. Ranking it second would have taken the one class that must
        // never be dispatched and dispatched it ahead of every hardening item on the board.
        let items =
            [ { item 1 with Class = Some Decision }
              { item 2 with Class = Some Hardening } ]

        Assert.Equal<int list>([ 2; 1 ], order items)

        // ...and still ahead of an unclassed row, which has no evidence at all.
        Assert.Equal<int list>([ 1; 3 ], order [ { item 1 with Class = Some Decision }; item 3 ])

    [<Fact>]
    let ``#1598 Class outranks Phase — a P8 defect beats a P0 hardening item`` () =
        let items =
            [ { item 1 with
                  Class = Some Hardening
                  Phase = Some P0Decisions }
              { item 2 with
                  Class = Some Defect
                  Phase = Some P8Net } ]

        Assert.Equal<int list>([ 2; 1 ], order items)

    [<Fact>]
    let ``#1598 Phase orders by PLAN ORDER, and an unphased row sorts after every phase`` () =
        let items =
            [ item 1
              { item 2 with Phase = Some P8Net }
              { item 3 with Phase = Some P0Decisions } ]

        Assert.Equal<int list>([ 3; 2; 1 ], order items)

    [<Fact>]
    let ``#1598 age breaks a tie oldest-first, and an unknown age is never the oldest`` () =
        let items =
            [ item 1
              { item 2 with AgeDays = Some 5 }
              { item 3 with AgeDays = Some 40 } ]

        // #3 (40d) then #2 (5d) then #1 (unknown). An unread age must not sort as ancient — that would
        // let a failed read outrank the whole board.
        Assert.Equal<int list>([ 3; 2; 1 ], order items)

    [<Fact>]
    let ``#1598 the item's own text beats the board column when they disagree about Class`` () =
        // ADR-0066's authority order, not a second one: the body is what the item SAYS it is and the
        // column is a projection of it, so a stale projection can never outrank the text.
        let counts = Rank.blockingCounts []

        let disagreeing =
            { item 1 with
                Class = Some Defect
                BoardClass = Some Hardening }

        Assert.Equal(Some Defect, (Rank.ofItem counts disagreeing).Class)

        // ...and the column IS read when the text is silent, which is the whole reason both fields exist.
        let projectedOnly = { item 1 with BoardClass = Some Hardening }
        Assert.Equal(Some Hardening, (Rank.ofItem counts projectedOnly).Class)

    // ================================================================================================
    // AC4 — STARVATION ESCALATION.
    // ================================================================================================

    [<Fact>]
    let ``#1598 AC4 a long-starved Ready item escalates above class and phase entirely`` () =
        let starved =
            { item 9 with
                AgeDays = Some(Rank.StarvationDays + 1) }

        let freshDefect =
            { item 1 with
                Class = Some Defect
                Phase = Some P0Decisions
                AgeDays = Some 0 }

        // The starved item is UNCLASSED, UNPHASED and has the larger number — it loses every single term
        // — and it still leads, because escalation is the tier above all of them. Without that, an item
        // whose touch-set always collides with something better-classed starves forever.
        Assert.Equal<int list>([ 9; 1 ], order [ freshDefect; starved ])

    [<Fact>]
    let ``#1598 AC4 escalation outranks the BLOCKING COUNT, or it is not a liveness guarantee`` () =
        // THE CASE ANTI-STARVATION ACTUALLY EXISTS FOR. An item starves when its touch-set permanently
        // collides with something better-ranked — and "better-ranked" is most often a HUB: something many
        // other items are blocked by. An escalation that beat `Class` and `Phase` but lost to the
        // blocking count would leave exactly that item starving forever, which is the whole failure.
        let hub = item 1

        let starved =
            { item 9 with
                AgeDays = Some(Rank.StarvationDays + 1) }

        let items =
            [ hub
              starved
              item 20 |> blockedBy 1 BlockerOpen
              item 21 |> blockedBy 1 BlockerOpen
              item 22 |> blockedBy 1 BlockerOpen ]

        Assert.Equal(9, order items |> List.head)

    [<Fact>]
    let ``#1598 escalation is a THRESHOLD, and one day under it does not fire`` () =
        Assert.False(Rank.isEscalated Ready (Some(Rank.StarvationDays - 1)))
        Assert.True(Rank.isEscalated Ready (Some Rank.StarvationDays))

    [<Fact>]
    let ``#1598 a parked Backlog row never escalates, however old it is`` () =
        // Somebody DECIDED to park it. Letting it age its way to the front would silently undo that
        // triage decision — the exact opposite of what this item is for, which is to stop `Backlog`
        // being used as a priority lever at all.
        Assert.False(Rank.isEscalated Backlog (Some 900))

    [<Fact>]
    let ``#1598 an item whose age we could not read never escalates`` () =
        // Escalating on an unknown age would promote every unreadable row above the whole board — a
        // failed read wearing a priority's clothes.
        Assert.False(Rank.isEscalated Ready None)

    // ================================================================================================
    // DETERMINISM — the property the batch has always depended on (#418).
    // ================================================================================================

    [<Fact>]
    let ``#1598 the order is total and input-order-independent`` () =
        let items =
            [ { item 4 with Class = Some Defect }
              { item 2 with Phase = Some P2Sdd }
              item 9
              { item 7 with AgeDays = Some 3 } ]

        let expected = order items
        Assert.Equal<int list>(expected, order (List.rev items))
        Assert.Equal<int list>(expected, order (List.sortBy (fun (i: Item) -> -i.Ref.Number) items))

    [<Fact>]
    let ``#1598 two items identical but for their number are separated by the number`` () =
        // The last term is what makes the order TOTAL. Without it two equally-ranked items would compare
        // equal, and `List.sortBy`'s stability would make the answer depend on the caller's input order —
        // which two workers reading one cached window do not share.
        let a = { item 5 with Class = Some Defect }
        let b = { item 6 with Class = Some Defect }
        Assert.Equal<int list>([ 5; 6 ], order [ b; a ])

    // ================================================================================================
    // AC5's INPUT — `explain` prints the inputs, never a score.
    // ================================================================================================

    [<Fact>]
    let ``#1598 explain names every input that produced the position`` () =
        let counts = Rank.blockingCounts []

        let r =
            Rank.ofItem
                counts
                { item 1 with
                    Class = Some Defect
                    Phase = Some P0Decisions
                    AgeDays = Some 4 }

        let text = Rank.explain r

        Assert.Contains("blocking 0", text)
        Assert.Contains("defect", text)
        Assert.Contains("P0 Decisions", text)
        Assert.Contains("4d old", text)
        // Not starved, so nothing may claim it is.
        Assert.DoesNotContain("STARVED", text)

    [<Fact>]
    let ``#1598 explain says so when an input is ABSENT, rather than printing a default`` () =
        let counts = Rank.blockingCounts []
        let text = Rank.explain (Rank.ofItem counts (item 1))

        // "unclassed" and "no phase" are the honest words. Printing `hardening` or `P0 Decisions` for a
        // row nobody set would be a guess with a renderer's authority behind it — and it would make the
        // explanation actively misleading about why the item lost its lane.
        Assert.Contains("unclassed", text)
        Assert.Contains("no phase", text)
        Assert.Contains("age unknown", text)

    [<Fact>]
    let ``#1598 explain announces starvation escalation, because it overrides everything below it`` () =
        let counts = Rank.blockingCounts []

        let text =
            Rank.explain (
                Rank.ofItem
                    counts
                    { item 1 with
                        AgeDays = Some(Rank.StarvationDays + 5) }
            )

        Assert.Contains("STARVED", text)
        // The sentence must not UNDERSTATE what escalation does. An earlier draft said "above class and
        // phase", which was true and incomplete — it also outranks the blocking count, and a driver
        // reading the shorter sentence would not understand why a hub lost its lane.
        Assert.Contains("above every other rank term", text)

    // ================================================================================================
    // .github#1628 — THE BLOCKING COUNT IS A WHOLE-BOARD FACT, NOT A CANDIDATE-SET ONE.
    //
    // `Rank`'s primary term used to be derived from the list it ranked, and `Scan.snapshot` scopes that
    // list with `--repo`. So the SAME item, on the SAME board, at the SAME instant, ranked as blocking
    // nothing under `take --repo <its own repo>` and as blocking three under a bare org-wide `batch` —
    // and the scoped spelling is the one every worker actually runs. Nothing errored: the batch stayed
    // well-formed, disjoint and deterministic, and was ordered by a count that was wrong in the one
    // direction that matters most, because an item with cross-repo dependents is by construction a hub.
    //
    // The fixture below is AC2's, at the Core layer: one item in repo A, three OPEN items in repo B
    // naming it in `Blocked by`, and a candidate list scoped to repo A. `CrossRepoRankTests` drives the
    // same shape through the real scan, the real snapshot codec and the real `--repo` scope.
    // ================================================================================================

    let private otherRepo n =
        { Owner = "FS-GG"
          Repo = "FS.GG.SDD"
          Number = n }

    /// An item in the OTHER repo, blocked by `.github#hub`. `Scan.snapshot --repo .github` never puts one
    /// of these on the candidate list, which is exactly what made the undercount invisible.
    let private dependent n hub =
        { item n with
            Ref = otherRepo n
            Blockers =
                [ { Ref = Some(ref hub)
                    Raw = (ref hub).Short
                    State = BlockerOpen } ] }

    /// The whole board: the hub and one ordinary item in `.github`, three dependents in `FS.GG.SDD`.
    let private wholeBoard =
        [ item 10
          item 11
          dependent 200 10
          dependent 201 10
          dependent 202 10 ]

    /// What `--repo .github` leaves on the candidate list.
    let private scopedToGithub =
        wholeBoard |> List.filter (fun i -> i.Ref.Repo = ".github")

    /// The whole board's counts, spelled the way `Client.boardBlockingCounts` spells them.
    let private wholeBoardCounts = Rank.blockingCounts wholeBoard

    [<Fact>]
    let ``#1628 THE DEFECT — candidate-derived counts rank a cross-repo hub as blocking NOTHING`` () =
        // THIS IS THE BUG, PINNED AS A FACT ABOUT THE OLD SPELLING, so the fixtures below cannot go
        // vacuous. A test that only asserted the fixed behaviour would keep passing if the scoping were
        // quietly removed from the scan — and then it would be asserting nothing at all.
        let scoped = Rank.blockingCounts scopedToGithub
        Assert.Equal(None, scoped |> Map.tryFind (ref 10))

        // The same board, unscoped, at the same instant: three.
        Assert.Equal(3, wholeBoardCounts |> Map.find (ref 10))

    [<Fact>]
    let ``#1628 AC1 an item's blocking count is the same scoped and unscoped`` () =
        // The acceptance criterion, stated as the equality it is. `ofItemsWith` is handed the WHOLE
        // board's counts and the SCOPED candidate list — the exact shape the offer path now uses.
        let scopedRanks = Rank.ofItemsWith wholeBoardCounts scopedToGithub
        let wholeRanks = Rank.ofItemsWith wholeBoardCounts wholeBoard

        let blockingOf ranks n =
            ranks
            |> List.find (fun ((i: Item), _) -> i.Ref.Number = n)
            |> snd
            |> fun (r: Rank.Rank) -> r.Blocking

        Assert.Equal(3, blockingOf scopedRanks 10)
        Assert.Equal(blockingOf wholeRanks 10, blockingOf scopedRanks 10)

    [<Fact>]
    let ``#1628 a count for a ref that is not a candidate is INERT`` () =
        // This is why handing a one-repo batch the whole ORG's counts is safe by construction rather than
        // by filtering: `ofItemsWith` reads the map BY REF, so the entries for `FS.GG.SDD` items that are
        // not on this candidate list are never looked at. If it merged, summed, or iterated the map
        // instead, a whole-board map would leak other repos' items into a scoped batch.
        let ranks = Rank.ofItemsWith wholeBoardCounts scopedToGithub

        Assert.Equal<int list>([ 10; 11 ], ranks |> List.map (fun (i, _) -> i.Ref.Number))

    [<Fact>]
    let ``#1628 AC4 off-board and unparseable edges keep #1598's treatment under the whole-board count`` () =
        // AC4. Widening the SOURCE set must not widen what counts as an EDGE — the two are independent,
        // and the easy mistake is to let "count more sources" become "count more kinds of edge".
        let prose =
            { item 300 with
                Ref = otherRepo 300
                Blockers =
                    [ { Ref = None
                        Raw = "waiting on the platform team"
                        State = BlockerUnparseable } ] }

        // An OFF-BOARD ref: parseable, so it is credited — but to a node no candidate can be, so no rank
        // ever reads it. It must not land on the hub.
        let offBoard =
            { item 301 with
                Ref = otherRepo 301
                Blockers =
                    [ { Ref = Some(otherRepo 9999)
                        Raw = "FS-GG/FS.GG.SDD#9999"
                        State = BlockerUnknown } ] }

        let counts = Rank.blockingCounts (wholeBoard @ [ prose; offBoard ])

        // Unchanged: three real dependents, and neither the prose nor the off-board edge joined them.
        Assert.Equal(3, counts |> Map.find (ref 10))
        Assert.Equal(1, counts |> Map.find (otherRepo 9999))

    [<Fact>]
    let ``#1628 blockingCountsOf counts every edge it is handed — the caller owns the source set`` () =
        // The contract that makes the fix possible, asserted directly. `blockingCountsOf` deliberately
        // does NOT filter by open-ness or scope: it cannot see rows, only edges. `blockingCounts` is this
        // function over the open items' edges, and `Client.boardBlockingCounts` is it over the whole
        // board's open non-PR rows — one counting rule, two source sets, no second implementation.
        let edges = wholeBoard |> List.map (fun i -> i.Ref, i.Blockers)

        Assert.Equal(3, Rank.blockingCountsOf edges |> Map.find (ref 10))

        // Hand it ONE source and it says one. Nothing about the list it did not see leaks in.
        Assert.Equal(
            1,
            Rank.blockingCountsOf [ (otherRepo 200), (dependent 200 10).Blockers ]
            |> Map.find (ref 10)
        )

    // ---- the fold, which is where the count actually decides something --------------------------------

    /// Every candidate declares `src/f<n>.fs`, so nothing collides and a batch here is a pure ORDERING
    /// test rather than a disjointness one.
    let private consideredOrder (result: Verdict<Batch.BatchResult>) =
        match result with
        | Green r -> r.Decisions |> List.map (fun d -> d.Item.Ref.Number)
        | other -> failwith $"the batch must be schedulable — got %A{other}"

    [<Fact>]
    let ``#1628 scheduleWith ranks by the counts it is GIVEN, not by the candidates' own`` () =
        // THE FIX, AT THE FOLD. Under candidate-derived counts the hub and the ordinary row both rank at
        // blocking 0 and the issue NUMBER decides — the pre-#1598 ordering wearing a rank's clothes.
        let scopedResult =
            Batch.scheduleWith wholeBoardCounts false None [] scopedToGithub

        match scopedResult with
        | Green r ->
            let hub = r.Decisions |> List.find (fun d -> d.Item.Ref.Number = 10)
            let ordinary = r.Decisions |> List.find (fun d -> d.Item.Ref.Number = 11)

            Assert.Equal(3, hub.Rank.Blocking)
            Assert.Equal(0, ordinary.Rank.Blocking)
        | other -> failwith $"the batch must be schedulable — got %A{other}"

        // The old spelling, on the same candidates, still says zero. That is the delta this item is.
        match Batch.schedule false None [] scopedToGithub with
        | Green r ->
            let hub = r.Decisions |> List.find (fun d -> d.Item.Ref.Number = 10)
            Assert.Equal(0, hub.Rank.Blocking)
        | other -> failwith $"the batch must be schedulable — got %A{other}"

    [<Fact>]
    let ``#1628 a whole-board count OUTRANKS a scoped defect, which a scoped count could not`` () =
        // The ordering consequence, made visible — and it needs a term the NUMBER cannot also explain, or
        // the assertion would pass either way. `blocking` sits ABOVE `Class` in the lexicographic key, so
        // a hub blocking three must lead a defect that blocks nothing. Under the scoped count the hub
        // read as blocking 0 and LOST to that defect: the wrong item handed out, silently, on every
        // `take --repo`.
        let defect = { item 11 with Class = Some Defect }
        let candidates = [ defect; item 10 ]

        Assert.Equal<int list>(
            [ 10; 11 ],
            consideredOrder (Batch.scheduleWith wholeBoardCounts false None [] candidates)
        )

        // Candidate-derived: the defect wins, because the hub's three dependents are invisible.
        Assert.Equal<int list>([ 11; 10 ], consideredOrder (Batch.schedule false None [] candidates))

    [<Fact>]
    let ``#1628 AC5 --explain prints the count the ordering actually used`` () =
        // An ordering nobody can inspect is one nobody trusts (#1598 AC5), and the failure mode this
        // guards is specific: a fix that changed the SORT but left `--explain` printing the old
        // candidate-derived number would leave every driver reading "blocking 0" beside an item that led
        // the batch, and concluding the scheduler was broken.
        match Batch.scheduleWith wholeBoardCounts false None [] scopedToGithub with
        | Green r ->
            let hubLine =
                Batch.explainRanking r |> List.find (fun l -> l.Contains ".github#10")

            Assert.Contains("blocking 3", hubLine)
        | other -> failwith $"the batch must be schedulable — got %A{other}"

    [<Fact>]
    let ``#1628 schedule is scheduleWith over the candidates' own counts — one fold, not two`` () =
        // `schedule` survives for `decide --snapshot`, which is handed a document and nothing wider. It
        // must be the SAME fold: a second copy would be free to drift, and #485 is this repo's standing
        // verdict on one question with more than one implementation.
        let viaWrapper = Batch.schedule false None [] wholeBoard

        let viaExplicit =
            Batch.scheduleWith (Rank.blockingCounts wholeBoard) false None [] wholeBoard

        Assert.Equal<int list>(consideredOrder viaWrapper, consideredOrder viaExplicit)
