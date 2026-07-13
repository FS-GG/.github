namespace FS.GG.Coord

module Divergence =

    open System
    open FS.GG.Coord.Types

    type Report =
        { Worker: WorkerId
          Day: DateOnly
          Engine: string
          Ran: int
          Skipped: int
          Compared: int
          OutcomeDivergences: int
          Unpaired: int
          EngineRefused: int
          ReasonDivergences: int }

    type Evidence =
        { Window: DateOnly list
          Engine: string
          Workers: WorkerId list
          Ran: int
          Skipped: int
          Compared: int
          ReasonDivergences: int
          Discarded: int }

    let private day (d: DateOnly) = d.ToString("yyyy-MM-dd")

    /// The `requiredDays` most recent COMPLETE UTC days, oldest first. Today is not in it — see the
    /// signature file: today is partial, and a criterion that re-opens every midnight is a criterion
    /// nobody can ever meet.
    let private coverageWindow (requiredDays: int) (today: DateOnly) =
        [ for back in requiredDays .. -1 .. 1 -> today.AddDays(-back) ]

    let evaluate
        (engine: string)
        (requiredDays: int)
        (minWorkers: int)
        (today: DateOnly)
        (reports: Report list)
        : Verdict<Evidence> =

        // A CRITERION SATISFIED BY NOTHING IS NOT A CRITERION. A zero-day window is covered vacuously and
        // a zero-worker quorum is met by the empty fleet — so both would report GREEN over an empty
        // ledger. That is the exact substitution (#266) this engine exists to make impossible, and it must
        // not be reachable by passing a `0`.
        if requiredDays < 1 then
            NoVerdict $"a %d{requiredDays}-day window is satisfied by no evidence at all — refusing to decide."
        elif minWorkers < 1 then
            NoVerdict $"a %d{minWorkers}-worker quorum is met by the empty fleet — refusing to decide."
        elif String.IsNullOrWhiteSpace engine then
            // Evidence is only ever evidence FOR a build. Without one named, every report matches and the
            // clock would be advanced by agreement that some other engine reached, about some other code.
            NoVerdict "no engine build was named, so no report can be known to be evidence FOR it."
        else

        let window = coverageWindow requiredDays today
        let oldest = List.head window

        // EVIDENCE IS NOT TRANSFERABLE ACROSS BUILDS. Discard — but COUNT what was discarded, so that a
        // clock which refuses to advance can say WHY, instead of looking like an empty ledger.
        let ofThisEngine, ofAnother = reports |> List.partition (fun r -> r.Engine = engine)

        let discarded = List.length ofAnother

        // In the window OR NEWER. Today is included, and so is anything dated ahead of it: a report from a
        // skewed clock is still a worker telling us the engines disagreed, and dropping it because its
        // timestamp is inconvenient would be the fail-open reading of the one signal that must never fail
        // open.
        let recent = ofThisEngine |> List.filter (fun r -> r.Day >= oldest)

        // EVERY CONDITION THE PER-WORKER CLIENT CALLS RED IS BLOCKING HERE TOO.
        //
        // An outcome divergence is the obvious one. But an item only ONE engine ruled on means the two
        // folds evaluated different candidate SETS, and a batch the engine REFUSED outright means they
        // disagree about whether the board is schedulable at all. The client already exits non-zero on
        // all three. Counting only the first here would let the fleet fold report GREEN over a fleet
        // every one of whose workers was printing RED — a false green assembled entirely out of true
        // negatives, which is the exact failure this module exists to refuse.
        let blocking (r: Report) =
            r.OutcomeDivergences + r.Unpaired + r.EngineRefused

        // RED FIRST, AND UNCONDITIONALLY. A disagreement is a FACT; coverage and quorum are questions
        // about how much we looked. Thin evidence cannot downgrade a divergence we did in fact observe,
        // so this may not wait behind the NoVerdict checks below.
        let diverged =
            recent
            |> List.filter (fun r -> blocking r > 0)
            |> List.sortBy (fun r -> (r.Day, r.Worker.Value))

        if not (List.isEmpty diverged) then
            let total = diverged |> List.sumBy blocking

            let describe (r: Report) =
                [ if r.OutcomeDivergences > 0 then
                      yield $"%d{r.OutcomeDivergences} outcome"
                  if r.Unpaired > 0 then
                      yield $"%d{r.Unpaired} ruled on by one engine only"
                  if r.EngineRefused > 0 then
                      yield $"%d{r.EngineRefused} batch(es) the engine refused outright" ]
                |> String.concat ", "

            Red
                [ yield
                      $"the engines disagreed about what may be scheduled: %d{total} blocking divergence(s) on engine %s{engine}."

                  for r in diverged do
                      yield
                          $"  %s{day r.Day}  %s{r.Worker.Value}  %s{describe r} (over %d{r.Compared} compared verdict(s))"

                  yield "the flip is BLOCKED. Reconcile the engines, then start the clock again." ]

        else

        // A DAY IS COVERED ONLY IF SOMETHING WAS ACTUALLY COMPARED ON IT. `Compared` is the unit, never
        // `Ran`: a `next` over an empty queue RAN and compared nothing, and an empty queue agrees with
        // everything. A fleet that scheduled no work all week would otherwise report a week of agreement.
        let covering (d: DateOnly) =
            ofThisEngine
            |> List.filter (fun r -> r.Day = d && r.Compared > 0)

        let uncovered = window |> List.filter (fun d -> List.isEmpty (covering d))

        if not (List.isEmpty uncovered) then
            let names = uncovered |> List.map day |> String.concat ", "

            let seen =
                if List.isEmpty ofThisEngine then
                    if discarded > 0 then
                        $" The ledger holds %d{discarded} report(s), but ALL of them are from another engine build."
                    else
                        " The ledger is empty for this engine — the shadow has never reported."
                else
                    ""

            NoVerdict
                $"the shadow compared nothing on %d{List.length uncovered} of the %d{requiredDays} day(s) in the window (%s{names}).%s{seen} That is not a clean day; it is a day nobody looked. The clock has NOT run %d{requiredDays} consecutive days."

        else

        let inWindow =
            ofThisEngine |> List.filter (fun r -> List.contains r.Day window)

        let workers =
            inWindow
            |> List.filter (fun r -> r.Compared > 0)
            |> List.map (fun r -> r.Worker)
            |> List.distinct
            |> List.sortBy (fun w -> w.Value)

        // ONE WORKER IS NOT A FLEET. The defects the shadow hunts — a claim handed to a second worker
        // (#461), a twin deleting a live twin's lock (#550), an id that cannot tell a worker from itself
        // (#419) — are CONCURRENCY defects, and they cannot appear in a log that only one worker wrote.
        // A single-worker log is precisely the log that cannot contain the bugs we are looking for, so
        // reading it as "the fleet agrees" is the failure this whole module exists to refuse.
        if List.length workers < minWorkers then
            let who =
                if List.isEmpty workers then
                    "no worker"
                else
                    workers |> List.map (fun w -> w.Value) |> String.concat ", "

            NoVerdict
                $"only %d{List.length workers} worker(s) contributed evidence (%s{who}); the criterion needs %d{minWorkers}. A single worker's log cannot contain a concurrency defect, so it cannot be evidence that there is none."

        else

        Green
            { Window = window
              Engine = engine
              Workers = workers
              Ran = inWindow |> List.sumBy (fun r -> r.Ran)
              Skipped = inWindow |> List.sumBy (fun r -> r.Skipped)
              Compared = inWindow |> List.sumBy (fun r -> r.Compared)
              ReasonDivergences = inWindow |> List.sumBy (fun r -> r.ReasonDivergences)
              Discarded = discarded }

    let explain (verdict: Verdict<Evidence>) : string list =
        match verdict with
        | Red reasons -> reasons

        | NoVerdict reason ->
            [ $"NO VERDICT — %s{reason}"
              "This is NOT zero divergence. It is zero evidence, and the flip stays gated." ]

        | Green e ->
            [ yield
                  $"GREEN — engine %s{e.Engine} agreed with bash on every one of %d{e.Compared} compared verdict(s)"

              yield
                  $"across %d{List.length e.Window} consecutive day(s) (%s{day (List.head e.Window)} .. %s{day (List.last e.Window)}) and %d{List.length e.Workers} worker(s):"

              yield "  " + (e.Workers |> List.map (fun w -> w.Value) |> String.concat ", ")

              if e.ReasonDivergences > 0 then
                  yield
                      $"note: %d{e.ReasonDivergences} REASON divergence(s) — the engines agreed on the outcome and named"

                  yield "      different causes. A decision to take, not a defect, and not a blocker."

              if e.Skipped > 0 then
                  yield
                      $"note: %d{e.Skipped} run(s) skipped the shadow entirely. They are not agreement, and are not counted."

              if e.Discarded > 0 then
                  yield
                      $"note: %d{e.Discarded} report(s) from another engine build were DISCARDED. Evidence does not"

                  yield "      transfer across builds — that is why republishing the engine restarts the clock."

              yield ""
              yield "ADR-0034 §5's cut-over criterion is MET for this build." ]
