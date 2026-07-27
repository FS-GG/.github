namespace FS.GG.Coord

/// **PRIORITY, DERIVED — never a fifth hand-maintained board field** (.github#1598).
///
/// The board has carried `Phase` since it existed and no scheduler read it. `batch` and `take` ordered
/// candidates by ISSUE NUMBER, so the only priority lever a driver actually had was `Status` — and on
/// 2026-07-27 that meant parking NINETEEN hardening items in `Backlog` to move five structural items to
/// the front of a maximal-disjoint lane pack. It worked, and it overloaded `Backlog`: the triage contract
/// says that column means "deliberately parked with a concrete evidenced reason", and nineteen rows sat
/// there with their reason recorded only in a session transcript.
///
/// **EVERY INPUT IS ALREADY ON THE BOARD.** That is the design constraint, not a convenience. A `Priority`
/// field would be a fifth fact a human maintains, and hand-maintained facts drifting from the thing they
/// describe is the defect class this repo closed five times in one day (#1507, #1510/#1515, #1528, #1538).
/// So rank is COMPUTED, from four things the board already knows:
///
///   1. **Blocking count** — how many open items name this one in a still-holding `Blocked by` edge. The
///      single best signal available, and free: the graph is on the candidate list already.
///   2. **`Class`** (.github#1588) — `defect` before `decision` before `hardening`, which is
///      `Class.fromBody`'s own dominance order rather than a second one.
///   3. **`Phase`** — how early in the plan, the column that started all this.
///   4. **Age** — oldest first, a stable tie-break, and the input starvation escalation reads.
///
/// Lexicographic, in that order, with the ISSUE NUMBER underneath as the final term. **The number is what
/// keeps the scheduler DETERMINISTIC**, which the batch has always depended on: the board scan is cached
/// and shared across the fleet (#418), so two workers reading one window must compute one batch. Every
/// term above the number is a fact about the board, so this is still a total order on identical input.
///
/// **NO PRIORITY DATA ⇒ NO BEHAVIOUR CHANGE.** An item with no dependents, no class, no phase and no
/// readable age sorts LAST — but among a whole board of such items the number term alone survives, which
/// is precisely the pre-#1598 ordering. That is what made this safe to land in one step.
///
/// **AN UNREAD INPUT NEVER PROMOTES.** `None` is the lowest tier of every term, and `AgeDays = None` never
/// escalates. The fail-open would be the other direction: a column the scan could not read becoming the
/// highest-priority work on the board.
///
/// Pure and total. It performs no IO and owns no clock — `AgeDays` is measured at the impure edge, on
/// `Claim.AgeSeconds`'s terms — so a rank is reproducible from its inputs and a fixture states an age
/// rather than mocking time.
module Rank =

    open Types

    /// How many days a `Ready` item may sit before it escalates above the class and phase terms.
    ///
    /// THREE WEEKS, and the number is a judgement rather than a measurement — the board carries no record
    /// of when a row was last OFFERED, so "never offered for N days" is approximated by the item's own
    /// age, which is the only board timestamp there is. It is deliberately long: escalation exists so that
    /// an item whose touch-set always collides with something higher-ranked cannot starve forever, not so
    /// that age competes with severity in the ordinary case.
    [<Literal>]
    val StarvationDays: int = 21

    /// One candidate's derived priority. Every field is an OBSERVATION, so the record can be printed as
    /// the reason for its own position (`explain`) rather than as an opaque score.
    type Rank =
        { /// `Ready`, and old enough that the queue is starving it. Sorts above every other term.
          Escalated: bool
          /// Open items whose still-holding `Blocked by` names this one. More is earlier.
          Blocking: int
          /// The item's severity — its own text first, the board column as fallback. `None` sorts last.
          Class: ItemClass option
          /// The board's `Phase` column. `None` sorts after every phase.
          Phase: Phase option
          /// Whole days since the issue was created. `None` sorts as if brand new, never as starved.
          AgeDays: int option
          /// The issue number — the determinism term, and the entire pre-#1598 ordering.
          Number: int }

    /// The lexicographic sort key. LOWER IS EARLIER.
    ///
    /// Returned as a tuple rather than a single score on purpose: a score would have to weight terms
    /// against each other, and there is no exchange rate between "blocks two items" and "is a defect".
    /// Lexicographic tiers need none — each term only breaks the ties the term above it left.
    val key: r: Rank -> int * int * int * int * int * int

    /// How many OPEN items name each ref in a still-holding `Blocked by` edge.
    ///
    /// UNPARSEABLE EDGES ARE SKIPPED, SAID SO HERE, AND THAT IS THE CORRECT FAILURE. Prose in a dependency
    /// field has no ref by construction, so there is no node to credit; inventing one would distort every
    /// rank around it. `lint`'s `BLOCKED-NO-REASON` already reports such an edge to a human — this
    /// deliberately does not guess on its behalf. RESOLVED edges are skipped too: a dependency that
    /// cleared is not a dependent.
    ///
    /// Deduplicated per source item, so one item naming the same blocker twice counts once. Pure: the
    /// entire graph is on the candidate list.
    val blockingCounts: items: Item list -> Map<Ref, int>

    /// Whether an item is being STARVED: `Ready` (never `Backlog` — see the note below) and at least
    /// `StarvationDays` old.
    ///
    /// A `Backlog` row reached by `--include-backlog` never escalates. Somebody DECIDED to park it, and
    /// letting it age its way to the front would silently undo that triage decision — the opposite of
    /// what .github#1598 is for, which is to stop `Backlog` being used as a priority lever at all.
    val isEscalated: status: BoardStatus -> ageDays: int option -> bool

    /// One item's rank, given `blockingCounts` over the whole candidate set.
    ///
    /// `Class` prefers the item's OWN TEXT to the board column (.github#1588's authority order), so a
    /// stale projection can never outrank what the item says about itself.
    val ofItem: counts: Map<Ref, int> -> item: Item -> Rank

    /// Every candidate's rank in one pass — the blocking graph is walked once per batch, not once per item.
    val ofItems: items: Item list -> (Item * Rank) list

    /// TRUE when the rank carries no priority evidence at all. Such an item still schedules; it sorts last.
    val isUnranked: r: Rank -> bool

    /// The rank's INPUTS in English, for `batch --explain` — what each term CONTRIBUTED, never a score.
    /// "rank 7" answers no question a driver has; "blocking 3, defect, P0 Decisions, 41d old" answers all
    /// of them, and is checkable against the board by eye.
    val explain: r: Rank -> string
