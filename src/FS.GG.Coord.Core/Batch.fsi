namespace FS.GG.Coord

/// THE SCHEDULER: the maximal set of items that may run at once, given what is already in flight.
///
/// `Schedulability.schedulable` answers ONE question about ONE item. This answers the question a
/// fan-out actually asks — "given what is RUNNING, which items can I hand to N workers right now?" —
/// and it is a DIFFERENT function, because the answer for one candidate depends on the answers for
/// the candidates before it: an item chosen into the batch RESERVES its touch-set against every later
/// candidate. The scheduler is therefore a fold, not a map, and `batch`/`next`/`take` are all this
/// one fold with three different presentation layers (#485).
///
/// It is pure and total. It performs no IO, so — as with `schedulable` — it cannot mistake a failed
/// read for a legitimate "no". Where the caller could not learn a fact, it says so in the input.
module Batch =

    open Types
    open Schedulability

    /// WHO is holding a reserved touch-set.
    ///
    /// The verdict is the same either way — the item is not startable — but the REMEDY is not, and a
    /// scheduler that reports the collision without naming its owner withholds the one fact every
    /// remedy in the protocol needs (#428). Waiting, `say`, and "is this worth waiting for at all"
    /// are decisions about a worker and a lease; a pair of colliding paths cannot inform any of them.
    type Holder =
        /// A live claim. There is a worker to talk to and a lease to wait out — this is the only
        /// holder for which "wait ~Nm" is a truthful thing to tell an operator.
        ///
        /// `livePr` carries the #581 proof of life: `Some pr` when the holder's lease has lapsed but an
        /// open `item/<n>-*` PR keeps the claim alive — so it is NOT reapable and there is no lease
        /// window to wait out (talk to the worker instead). `None` is an ordinary claim within its
        /// lease. It must NEVER mean "we could not read the PR state": a liveness that could not be read
        /// never reaches here (it fails closed upstream), so `None` is always "no proof of life", which
        /// is what lets `KnownLiveWork` be derived from it (`Batch.fs`) rather than hardcoded (#712).
        | LiveClaim of worker: WorkerId * item: Ref * ageSeconds: int * livePr: int option

        /// An item already chosen into THIS batch. It frees at the end of this run, and no lease
        /// governs it.
        | BatchMember of item: Ref

        /// The board says the item is in progress, but NO claim marker backs it: someone is working
        /// outside the protocol. Deliberately distinct from `LiveClaim` — there is no lease to wait
        /// out and no worker to `say` to, so folding it in would advertise a wait that nothing will
        /// ever end (#428).
        | Unowned of item: Ref

        /// The token came from the reservation set, but we cannot name its owner — the two went out
        /// of step. Report the collision we can PROVE rather than inventing an owner for it.
        | UnknownHolder

    /// A touch-set that is already spoken for.
    ///
    /// `Owner`/`Repo` are not decoration. `Paths:` tokens are REPO-RELATIVE — `scripts/foo` in one
    /// repo and `scripts/foo` in another name two different files in two different worktrees — so a
    /// reservation compared without its repo invents a phantom collision and under-schedules a
    /// candidate that nothing is actually holding (#312, #353). Every comparison below projects the
    /// reservation set onto the candidate's own repo first.
    type Reservation =
        { Owner: string
          Repo: string
          Paths: TouchSet
          Holder: Holder }

    /// One candidate, and why.
    type Decision =
        { Item: Item
          Result: Schedulability
          /// When `Result` is `OverlapsInFlight`, who holds the tokens it collided with. `None` for
          /// every other verdict.
          CollidedWith: Holder option
          /// The derived priority the fold ORDERED this candidate by (.github#1598).
          ///
          /// Carried on the decision, never recomputed by a renderer. `--explain` exists to show the
          /// ordering the scheduler actually used, and a rank derived a second time downstream is a
          /// second answer with nothing asserting it matches the first — which is the drift this repo
          /// closed five times in one day.
          Rank: Rank.Rank }

    type BatchResult =
        { /// The items that may run in parallel, in the order they were ADMITTED — which is derived-rank
          /// order (.github#1598), not issue order.
          Chosen: Item list

          /// Every candidate the scheduler EVALUATED, with its verdict — including the ones it chose.
          ///
          /// A passed-over candidate is not dropped, it is REPORTED. "Nothing to do" and "everything
          /// is blocked" are the same empty list and two completely different operator instructions,
          /// and printing the first over the second is what sent a worker home from a full queue
          /// (#440, #488).
          Decisions: Decision list

          /// True iff `limit` stopped the fold early, leaving later candidates unevaluated.
          ///
          /// A cap that is not reported reads as "we looked at everything and this is all there was"
          /// — which is a lie the caller cannot detect. `Decisions` covers only what was evaluated,
          /// and this says so.
          Truncated: bool }

    /// The host's concurrency model, read from the machine-readable declaration in `host-loop.md`.
    /// The engine deliberately does not own these numbers: the document that governs dispatch does.
    type WaveModel =
        { Waves: int
          ImplementerSlotsPerWave: int
          ReviewSlots: int
          ConsolidationThreshold: int }

    /// Fleet occupancy at the instant `batch` made its scheduling decision.
    type WaveOccupancy =
        { ActiveItems: int
          WaveCapacity: int
          OpenSlots: int }

    /// Parse `<!-- fsgg:wave-model:v1 ... -->` from a host-loop document. Missing, duplicate,
    /// malformed, or non-positive declarations are refused rather than defaulted.
    val parseWaveModel: document: string -> Result<WaveModel, string>

    /// Derive capacity and deficit from the governing model and the distinct live item refs observed.
    val waveOccupancy: model: WaveModel -> activeItems: Ref list -> WaveOccupancy

    /// Stable typed projection emitted beside `batch`'s unchanged stdout answer.
    val renderWaveOccupancy: occupancy: WaveOccupancy -> string

    /// The explicit advisory emitted only when capacity and schedulable work coexist.
    val waveShortfallHeadline: schedulableItems: int -> occupancy: WaveOccupancy -> string option

    /// Which reservation — if any — holds `token` in the given repo.
    ///
    /// Repo-scoped by contract: a reservation in another repo names a different file (#312).
    val holderOf: reservations: Reservation list -> owner: string -> repo: string -> token: string -> Holder option

    /// A disjoint set of `candidates` that may run at once, given `inFlight` — packed PRIORITY-GREEDILY.
    ///
    /// **NOT AN ARBITRARY MAXIMAL SET** (.github#1598). Candidates are walked in derived-`Rank` order and
    /// each is admitted if it clears everything already reserved, so the highest-ranked schedulable item
    /// is ALWAYS in the batch. It previously walked in issue order and packed whatever maximal set fell
    /// out, which is how three `P0 Decisions` items lost their lanes to a hardening item with a smaller
    /// number, and why a driver's only remaining priority lever was to park nineteen rows in `Backlog`.
    ///
    /// TOTAL PARALLELISM MAY DROP, and that is the intended trade rather than a regression: one wide
    /// high-rank item can exclude several narrow ones a maximal pack would have run together. It is
    /// reported, not silent — see `displacedBy` and `explainRanking`.
    ///
    /// STILL DETERMINISTIC on identical input: every rank term is a board fact and the issue NUMBER is
    /// the final term, so two workers reading one cached window (#418) compute one batch. It is NOT
    /// stable across DIFFERENT windows — rank moves as the board moves — so no caller may assume two
    /// `batch` calls return the same set.
    ///
    /// `limit` caps the batch (`batch -n`). `None` is unlimited. Reaching the cap stops the fold, so
    /// the candidates after it are neither chosen nor explained — see `BatchResult.Truncated`.
    ///
    /// An item that is HELD — by a live lease, or by a lapsed lease whose PR proves the work is still
    /// alive (#581) — does not merely drop out of the batch: its touch-set is OCCUPIED, so it is
    /// added to the reservation set. Skipping without reserving would happily hand a later candidate
    /// the very files another worker is holding.
    ///
    /// RETURNS A VERDICT, NOT A BATCH, and the `Red` leg is not a formality — it is the fail-CLOSED
    /// direction, and it is load-bearing. A RESERVATION whose touch-set is unmatchable reserves
    /// NOTHING: every later candidate would clear it, and the scheduler would hand a second worker
    /// the very files its holder is standing in. We cannot see that holder's real surface, so we
    /// cannot safely schedule ANYTHING against it — so the whole batch is refused, rather than one
    /// item being quietly dropped. Unschedulable beats mis-scheduled.
    ///
    /// (A CANDIDATE with unmatchable tokens is a different matter: it is merely `UnusableTouchSet`,
    /// passed over with a reason. It reserves nothing, but it also occupies nothing.)
    /// **`boardCounts` IS THE WHOLE BOARD'S BLOCKING GRAPH, NOT THE CANDIDATES'** (.github#1628).
    ///
    /// It is a parameter for one reason: the blocking count — `Rank`'s primary term, and the one
    /// .github#1598 calls "the single best signal available" — is a WHOLE-BOARD fact, while `candidates`
    /// is a `--repo`-scoped projection of that board. Deriving the count from the candidates (which is
    /// what this did) meant an item three items in another repo were `Blocked by` counted 0 under
    /// `batch --repo <its own repo>` and 3 under a bare `batch`. Nothing errored: the batch stayed
    /// well-formed, disjoint and deterministic, and was ordered by a count that was wrong in the one
    /// direction that matters — a cross-repo hub, the thing most worth scheduling first, ranked as
    /// though nothing depended on it. And the scoped spelling is the one every worker runs.
    ///
    /// It costs no read. `Scan.blockerGraph` builds the whole-board `Blocked by` graph from the scan rows
    /// alone (#1090), and the worker paths already hold those rows unscoped — `Client.boardBlockingCounts`
    /// is that composition.
    ///
    /// Counts for refs that are not candidates are INERT (`Rank.ofItemsWith` reads by ref), so passing the
    /// whole board's map to a one-repo batch is safe by construction rather than by filtering.
    ///
    /// Everything above this note — disjointness, the priority-greedy pack, the `limit`, the reservations,
    /// the `Red` refusal — is unchanged by it, and is this function's contract.
    val scheduleWith:
        boardCounts: Map<Ref, int> ->
        allowBacklog: bool ->
        limit: int option ->
        inFlight: Reservation list ->
        candidates: Item list ->
            Verdict<BatchResult>

    /// `scheduleWith` with the counts derived from `candidates` — its full contract, plus the assumption
    /// that the candidate list IS the whole board.
    ///
    /// **NOT FOR THE WORKER LOOP** (.github#1628): `batch`/`next`/`take` scope their candidates, and a
    /// scoped list truncates the blocking graph silently. Its caller is `decide --snapshot`, which is
    /// handed a document and nothing wider — the same position it is already in for `Phase` and age
    /// (.github#1598), and the same benign direction: an undercount sorts an item later, never earlier.
    val schedule:
        allowBacklog: bool ->
        limit: int option ->
        inFlight: Reservation list ->
        candidates: Item list ->
            Verdict<BatchResult>

    /// THE OPERATOR-FACING RENDERER — the only place a passed-over item is put into English.
    ///
    /// `Schedulability.explain` cannot do this alone: it sees the ITEM and the VERDICT, but a collision's
    /// holder lives on the DECISION, because "who reserved this path" is a fact about the batch. Rendering
    /// without it yields "overlaps in-flight work: a ⇄ b" — true, useless, and a regression on bash, which
    /// has named the holder and its lease window since #428.
    ///
    /// It also keeps the distinction a holder-blind renderer destroys: a BATCH MEMBER frees at the end of
    /// this run and has no lease; a LIVE CLAIM means wait out a window or go and talk to a worker. Same
    /// verdict, opposite instructions.
    val explainDecision: leaseMinutes: int -> d: Decision -> string

    /// THE STARVED-QUEUE BANNER (#428) — the AGGREGATE `explainDecision` cannot give.
    ///
    /// `explainDecision` speaks per-item; this speaks about the QUEUE. When a batch hands out nothing over a
    /// board full of items queued behind live claims, "nothing schedulable" reads as an empty backlog and
    /// sends a worker home from a repo with work in it. The banner says the queue is BUSY, names every
    /// holder (who to talk to), gives the soonest lease (whether waiting is worth it), and — for a lease
    /// already expired — points at `reap`, the one blocker a worker clears alone.
    ///
    /// THE DEADLOCK SECTION (#1092). One starved-queue cause is not a wait at all: a `Blocked by` RING, where
    /// each item waits on the next around a circle. It drains never, no lease frees it, and a human must
    /// break an edge — so when `Blockers.cycles` finds one it LEADS, naming every member and its in-ring
    /// blockers. "A queue starved by blockers is #440's per-item business" holds for a blocker that will
    /// clear; it is false for a ring, which no per-item verdict can even see (every item on one is
    /// individually well-formed). This is precisely the AGGREGATE this function exists to give.
    ///
    /// Returns [] whenever the queue is not STARVED: work was handed out, or the queue is empty / starved by
    /// blockers that will clear / withheld at a column the caller did not ask for. A banner on a healthy
    /// queue is noise, so there is deliberately none. Each string is one stderr line.
    val starvedBanner: leaseMinutes: int -> result: BatchResult -> string list

    /// How many candidates a given batch member DISPLACED — passed over specifically because they
    /// collided with it (.github#1598).
    ///
    /// The cost side of priority-greedy packing, made countable. A trade nobody is told about is
    /// indistinguishable from a regression in parallelism.
    val displacedBy: result: BatchResult -> member': Ref -> int

    /// `batch --explain` — THE ORDERING, MADE INSPECTABLE (.github#1598 AC5).
    ///
    /// One line per candidate in DECISION order, which is rank order — so the list is itself the ranking.
    /// Each line carries the verdict, every rank input that produced the candidate's position, and, for an
    /// admitted item, how many lanes it displaced. An item with no priority evidence says so explicitly,
    /// because "sorts last" and "was penalised" are different facts and only one of them is true.
    ///
    /// Returns [] for an empty candidate set — a header over nothing is noise. Each string is one line.
    val explainRanking: result: BatchResult -> string list
