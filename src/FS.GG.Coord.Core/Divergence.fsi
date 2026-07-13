namespace FS.GG.Coord

/// The shadow's evidence, folded into the ONE verdict the cut-over is gated on (ADR-0034 §5).
///
/// ADR-0034 makes the flip conditional on a single sentence — *"zero divergence across the live fleet
/// for three consecutive days"* — and until #634 that sentence was not a function anywhere. The shadow
/// wrote its evidence to `$XDG_CACHE_HOME/fsgg-coord/divergence.jsonl`, a DISPOSABLE CACHE DIRECTORY on
/// whichever machine happened to run it; nothing collected it, the rows did not say which worker wrote
/// them, and no code computed the criterion. The flip would have been decided by a human reading one
/// laptop's counters.
///
/// The client already refuses to call an EMPTY log green — an empty log is zero evidence, not zero
/// divergence. This module is that same refusal carried one step further, to the three things a local
/// log cannot see: **a LOCAL log is not the FLEET, a chatty worker is not a QUORUM, and agreement by a
/// DIFFERENT ENGINE BUILD is not agreement by this one.**
module Divergence =

    open System
    open FS.GG.Coord.Types

    /// One worker's shadow evidence for one UTC day, as that worker published it to the ledger.
    type Report =
        { /// WHO produced this evidence. The local JSONL row did not record it (#634 leg 2), so the
          /// fold could not have counted fleet members even if the logs had been collected: one worker's
          /// 500 runs and 500 workers' one run each rendered identically.
          Worker: WorkerId

          /// The UTC calendar day the evidence is FOR — not the day it was published. A worker's summary
          /// for a day is rewritten in place as that day goes on.
          Day: DateOnly

          /// The engine build that produced this evidence.
          ///
          /// Evidence is NOT transferable across builds. Agreement proved by `0.1.0` says nothing about
          /// `0.2.0` — and the whole point of the shadow is to prove THE BUILD WE ARE ABOUT TO TRUST. So
          /// a report from any other build is not counted, and republishing the engine legitimately
          /// restarts the clock. That is not a tax; it is the criterion meaning what it says.
          Engine: string

          /// Shadowed invocations in which BOTH engines decided.
          Ran: int

          /// Invocations where the shadow could not run (no engine resolved, unreadable state). NOT
          /// agreement — a shadow that did not run compared nothing.
          Skipped: int

          /// ITEM-VERDICTS on which both engines actually ruled. **THE unit of evidence.**
          ///
          /// Not invocations: a `next` over an empty queue is an invocation that compared NOTHING, and an
          /// empty queue agrees with everything. Counting it as evidence is how a fleet that scheduled no
          /// work all week would have reported a week of clean agreement.
          Compared: int

          /// The engines disagreed about WHAT MAY BE SCHEDULED. This is the defect the shadow exists to
          /// catch, and any of it blocks the flip.
          OutcomeDivergences: int

          /// Item-verdicts that only ONE engine ruled on. The two folds evaluated different candidate
          /// SETS — a divergence in the fold itself, even when every shared verdict agreed.
          Unpaired: int

          /// Batches the engine REFUSED outright while bash proceeded. Not a disagreement about one item:
          /// a disagreement about whether the board is safe to schedule from at all.
          EngineRefused: int

          /// They agreed on the outcome and named different REASONS. A decision to take, not a defect —
          /// carried so it is visible, never fatal (this is the client's existing semantics, preserved).
          ReasonDivergences: int }

    /// What the fleet actually proved — the payload of a Green verdict, and the receipt for the flip.
    type Evidence =
        { /// The complete UTC days the criterion was evaluated over, oldest first.
          Window: DateOnly list

          /// The engine build the evidence is FOR.
          Engine: string

          /// Distinct workers that contributed evidence inside the window.
          Workers: WorkerId list

          Ran: int
          Skipped: int
          Compared: int

          /// Present, and green anyway. See `Report.ReasonDivergences`.
          ReasonDivergences: int

          /// Reports DISCARDED because they came from a different engine build. Surfaced so that a clock
          /// which appears not to advance explains itself, rather than looking like a broken ledger.
          Discarded: int }

    /// Fold the ledger into the cut-over verdict. Pure, total, and the ONLY place this rule exists.
    ///
    /// `today` is a PARAMETER, never `DateTime.UtcNow`: the engine reads nothing, and a rule that
    /// consults a clock cannot be tested against the day boundaries where it is most likely to be wrong.
    ///
    /// The coverage window is the `requiredDays` most recent **complete** UTC days — today is excluded
    /// because it is a PARTIAL day, and requiring it would drop the verdict to NoVerdict every midnight,
    /// on evidence that had not stopped arriving. But a divergence reported TODAY still turns the verdict
    /// Red: a fresh disagreement is a disagreement, and waiting for the day to close before believing it
    /// would be the fail-open reading (#266).
    ///
    /// Green requires ALL of:
    ///   - every day in the window carries evidence — at least one report with `Compared > 0` from the
    ///     engine under test. An uncovered day is NoVerdict, not a green day with a gap in it;
    ///   - at least `minWorkers` distinct workers contributed. One worker is not a fleet, and the
    ///     scheduler defects this shadow is hunting (#419, #461, #550) only appear under CONCURRENCY —
    ///     so a single-worker log is precisely the log that cannot contain them;
    ///   - zero BLOCKING divergences, in the window or newer.
    ///
    /// "Blocking" is every condition the per-worker client already calls RED — an outcome divergence, an
    /// item only one engine ruled on, and a batch the engine refused outright. All three of them must be
    /// counted here, or the fleet fold would report green over a fleet whose own workers were each
    /// printing RED: a false green assembled out of true negatives, which is the failure mode this
    /// module is for.
    ///
    /// Anything else is Red (they disagreed) or NoVerdict (we did not establish it). Never a bare `bool`,
    /// and NoVerdict is never zero at the exit.
    val evaluate:
        engine: string ->
        requiredDays: int ->
        minWorkers: int ->
        today: DateOnly ->
        reports: Report list ->
            Verdict<Evidence>

    /// The human projection of a verdict — a RENDERING of the same answer, which may not add a fact the
    /// verdict lacks, and which nothing may parse.
    val explain: verdict: Verdict<Evidence> -> string list
