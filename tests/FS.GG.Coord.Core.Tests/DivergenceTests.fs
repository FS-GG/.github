namespace FS.GG.Coord.Tests

open System
open Xunit
open FS.GG.Coord
open FS.GG.Coord.Types
open FS.GG.Coord.Divergence

/// THE CUT-OVER CRITERION, as tests (#634).
///
/// ADR-0034 §5 gates the flip on one sentence — *"zero divergence across the live fleet for three
/// consecutive days"* — and every test below is a way that sentence could be reported as TRUE by a fold
/// that had established nothing of the kind. That is not a hypothetical class of bug in this repo; it
/// is epic #266, and this module is on the critical path of the ADR written to end it.
///
/// The four substitutions a naive fold makes, each of which has a test here:
///   a LOCAL log for the FLEET · a CHATTY WORKER for a QUORUM · a day NOBODY LOOKED AT for a CLEAN one ·
///   agreement by ANOTHER BUILD for agreement by THIS one.
module DivergenceTests =

    let private d (s: string) = DateOnly.Parse(s, Globalization.CultureInfo.InvariantCulture)

    let private today = d "2026-07-13"

    /// The coverage window for requiredDays=3 and today=07-13 is [07-10, 07-11, 07-12]. Today is
    /// PARTIAL and is deliberately not in it.
    let private report worker day compared outcome =
        { Worker = WorkerId worker
          Day = d day
          Engine = "0.1.0"
          Ran = 4
          Skipped = 0
          Compared = compared
          OutcomeDivergences = outcome
          Unpaired = 0
          EngineRefused = 0
          ReasonDivergences = 0 }

    let private evaluate3 reports = evaluate "0.1.0" 3 2 today reports

    let private cleanWindow =
        [ report "w-a" "2026-07-10" 12 0
          report "w-b" "2026-07-11" 9 0
          report "w-a" "2026-07-12" 7 0 ]

    let private isGreen =
        function
        | Green _ -> true
        | _ -> false

    let private isRed =
        function
        | Red _ -> true
        | _ -> false

    let private isNoVerdict =
        function
        | NoVerdict _ -> true
        | _ -> false

    let private reasonOf =
        function
        | NoVerdict r -> r
        | Red rs -> String.concat " " rs
        | Green _ -> ""

    // ---- the one case that is allowed to be green ------------------------------------------------

    [<Fact>]
    let ``three covered days, two workers, zero divergence — the criterion is met`` () =
        match evaluate3 cleanWindow with
        | Green e ->
            Assert.Equal(3, List.length e.Window)
            Assert.Equal(2, List.length e.Workers)
            Assert.Equal(28, e.Compared)
        | other -> failwith $"expected Green, got %A{other}"

    // ---- a day nobody looked at is not a clean day ------------------------------------------------

    [<Fact>]
    let ``a GAP in the window is no-verdict, and the uncovered day is named`` () =
        let verdict =
            evaluate3 [ report "w-a" "2026-07-10" 12 0; report "w-b" "2026-07-12" 9 0 ]

        Assert.True(isNoVerdict verdict)
        Assert.Contains("2026-07-11", reasonOf verdict)

    [<Fact>]
    let ``a day on which the shadow RAN but compared NOTHING is not covered`` () =
        // An empty queue agrees with everything, because it decided nothing. This is the client's own
        // hard-won rule (its first live invocation walked straight through the earlier version of it),
        // and the fleet fold has to carry it or three days of an idle board reads as three days of
        // agreement.
        let verdict =
            evaluate3
                [ report "w-a" "2026-07-10" 12 0
                  { report "w-b" "2026-07-11" 0 0 with Ran = 9 } // ran nine times, compared nothing
                  report "w-a" "2026-07-12" 7 0 ]

        Assert.True(isNoVerdict verdict)
        Assert.Contains("2026-07-11", reasonOf verdict)

    // ---- a quorum of one is not a fleet -----------------------------------------------------------

    [<Fact>]
    let ``three PERFECT days from ONE worker is no-verdict`` () =
        // The defects the shadow hunts (#419, #461, #550) are CONCURRENCY defects. They cannot appear
        // in a log that one worker wrote alone — so a single-worker log is precisely the log that
        // cannot contain them, and reading it as "the fleet agrees" is the whole bug.
        let verdict =
            evaluate3
                [ report "w-solo" "2026-07-10" 99 0
                  report "w-solo" "2026-07-11" 99 0
                  report "w-solo" "2026-07-12" 99 0 ]

        Assert.True(isNoVerdict verdict)
        Assert.Contains("concurrency defect", reasonOf verdict)

    // ---- evidence does not transfer across builds --------------------------------------------------

    [<Fact>]
    let ``a ledger full of ANOTHER build proves nothing about this one`` () =
        let verdict =
            evaluate3 (cleanWindow |> List.map (fun r -> { r with Engine = "0.0.9" }))

        Assert.True(isNoVerdict verdict)
        Assert.Contains("another engine build", reasonOf verdict)

    [<Fact>]
    let ``a stale build's reports are DISCARDED, not counted, and the count is surfaced`` () =
        let mixed =
            cleanWindow @ [ { report "w-z" "2026-07-11" 50 0 with Engine = "0.0.9" } ]

        match evaluate3 mixed with
        | Green e ->
            Assert.Equal(1, e.Discarded)
            Assert.Equal(28, e.Compared) // the 50 from 0.0.9 is NOT in it
        | other -> failwith $"expected Green, got %A{other}"

    // ---- red, and red beats everything -------------------------------------------------------------

    [<Fact>]
    let ``an outcome divergence in the window is RED`` () =
        let verdict =
            evaluate3
                [ report "w-a" "2026-07-10" 12 0
                  report "w-b" "2026-07-11" 9 2
                  report "w-a" "2026-07-12" 7 0 ]

        Assert.True(isRed verdict)
        Assert.Contains("w-b", reasonOf verdict)

    [<Fact>]
    let ``a divergence TODAY blocks a window that is otherwise three clean days`` () =
        // Today is outside the COVERAGE window (it is partial), but a fresh disagreement is still a
        // disagreement. Waiting for the day to close before believing it would be the fail-open reading
        // of the one signal that may never fail open.
        let verdict = evaluate3 (cleanWindow @ [ report "w-c" "2026-07-13" 3 1 ])

        Assert.True(isRed verdict)

    [<Fact>]
    let ``a FUTURE-dated report still blocks — a skewed clock does not launder a divergence`` () =
        let verdict = evaluate3 (cleanWindow @ [ report "w-c" "2026-07-20" 3 1 ])

        Assert.True(isRed verdict)

    [<Fact>]
    let ``thin evidence may not DOWNGRADE a divergence we actually observed`` () =
        // One worker, one day — nowhere near the criterion. But we DID see them disagree, and that is a
        // fact, not a question about how hard we looked. Red, not no-verdict.
        let verdict = evaluate3 [ report "w-a" "2026-07-11" 4 1 ]

        Assert.True(isRed verdict)

    [<Fact>]
    let ``an item only ONE engine ruled on is blocking — the folds evaluated different sets`` () =
        // `unpaired` and `engineRed` are RED in the per-worker client. If the fleet fold counted only
        // `outcome`, it would report GREEN over a fleet every one of whose workers was printing RED —
        // a false green assembled entirely out of true negatives.
        let verdict =
            evaluate3
                [ report "w-a" "2026-07-10" 12 0
                  { report "w-b" "2026-07-11" 9 0 with Unpaired = 2 }
                  report "w-a" "2026-07-12" 7 0 ]

        Assert.True(isRed verdict)

    [<Fact>]
    let ``a batch the engine REFUSED outright is blocking`` () =
        let verdict =
            evaluate3
                [ report "w-a" "2026-07-10" 12 0
                  { report "w-b" "2026-07-11" 9 0 with EngineRefused = 1 }
                  report "w-a" "2026-07-12" 7 0 ]

        Assert.True(isRed verdict)

    // ---- reason divergences are a decision, not a defect --------------------------------------------

    [<Fact>]
    let ``REASON divergences are carried and stay green — they are a decision to take`` () =
        let verdict =
            evaluate3 (cleanWindow |> List.map (fun r -> { r with ReasonDivergences = 3 }))

        match verdict with
        | Green e -> Assert.Equal(9, e.ReasonDivergences)
        | other -> failwith $"expected Green, got %A{other}"

    // ---- the vacuity guards: a criterion satisfied by nothing is not a criterion ---------------------

    [<Fact>]
    let ``a ZERO-day window is refused, not vacuously green`` () =
        // Every day in an empty window is trivially covered, so this would be GREEN over an EMPTY
        // ledger. A `0` reaching here must never be the thing that opens the gate.
        Assert.True(isNoVerdict (evaluate "0.1.0" 0 2 today []))

    [<Fact>]
    let ``a ZERO-worker quorum is refused — it is met by the empty fleet`` () =
        Assert.True(isNoVerdict (evaluate "0.1.0" 3 0 today cleanWindow))

    [<Fact>]
    let ``an UNNAMED engine is refused — evidence is only ever evidence FOR a build`` () =
        Assert.True(isNoVerdict (evaluate "" 3 2 today cleanWindow))

    [<Fact>]
    let ``an EMPTY ledger is no-verdict — the absence of evidence is not evidence of absence`` () =
        let verdict = evaluate3 []

        Assert.True(isNoVerdict verdict)
        Assert.Contains("never reported", reasonOf verdict)

    // ---- the window is the RECENT days, not any three -------------------------------------------------

    [<Fact>]
    let ``three clean days from LAST MONTH do not meet a criterion about the last three`` () =
        // Agreement in June says nothing about the engine at HEAD today. The window is anchored to
        // `today`, so old evidence cannot be re-counted forever.
        let verdict =
            evaluate3
                [ report "w-a" "2026-06-10" 12 0
                  report "w-b" "2026-06-11" 9 0
                  report "w-a" "2026-06-12" 7 0 ]

        Assert.True(isNoVerdict verdict)
