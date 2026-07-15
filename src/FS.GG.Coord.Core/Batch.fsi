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
        | LiveClaim of worker: WorkerId * item: Ref * ageSeconds: int

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
          CollidedWith: Holder option }

    type BatchResult =
        { /// The items that may run in parallel, in issue order.
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

    /// Which reservation — if any — holds `token` in the given repo.
    ///
    /// Repo-scoped by contract: a reservation in another repo names a different file (#312).
    val holderOf: reservations: Reservation list -> owner: string -> repo: string -> token: string -> Holder option

    /// The maximal set of `candidates` that may run at once, given `inFlight`.
    ///
    /// Greedy by issue number, so it is DETERMINISTIC: two workers scheduling the same board in the
    /// same window compute the same batch, which is what makes the answer safe to cache and share.
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
    /// Returns [] whenever the queue is not STARVED-BY-CLAIMS: work was handed out, or the queue is empty /
    /// starved by blockers or columns (that is #440's per-item business). A banner on a healthy queue is
    /// noise, so there is deliberately none. Each string is one stderr line.
    val starvedBanner: leaseMinutes: int -> result: BatchResult -> string list
