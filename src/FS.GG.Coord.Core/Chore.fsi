namespace FS.GG.Coord

/// THE CHORE QUEUE — the tool has no thread, so it conscripts the next caller (ADR-0034 §4.6, Phase 4.3).
///
/// The tool cannot reconcile the board, retire a stale claim, or re-verify a blocker on its own. So nobody
/// does, until a human runs `/check-board` or a cron fires 25 minutes late, and the whole
/// `CLAIM-STATUS-LAG` / `STALE-CLAIM` / `BLOCKER-CLEARED` family lives in that gap. The fix is the
/// **helping mechanism** from lock-free algorithms — a thread that encounters another thread's incomplete
/// operation completes it before proceeding. The agent is the thread the tool doesn't have.
///
/// **ADOPTING THE NAME MEANS ADOPTING THE CORRECTNESS CONDITIONS.** All four, or this is a machine for
/// manufacturing duplicate work and false green. Each one is discharged by a TYPE here, not by an `if`
/// somewhere downstream, because a missing `if` looks exactly like code that was never needed (Writes.fsi):
///
/// 1. **CLAIMED, NOT BROADCAST.** If N workers each call `next` and each is handed the same chore, N of them
///    do it — that is #464 (*N workers file the same finding N times*) and #463 (*two workers hand-synced
///    the same kit twice in one day*), rediscovered inside the mechanism meant to help.
///
///    **THIS CONDITION IS NOT BUILT YET, AND THAT IS WHY NOTHING CALLS `offer`.** The lock is IO — it cannot
///    live in a pure core — and which substrate it takes is an open DECISION, not an omission: the item CAS
///    (`Writes.claim`) is 145 lines of claim-specific policy (stale collection, twin detection #419,
///    `prev=` #481, renew-in-place #550) that a chore lock wants none of, so reusing it means factoring the
///    org's most safety-critical function, and NOT reusing it means a second compare-and-swap beside the
///    first — which is #485, the defect this whole core exists to retire, re-committed inside its own fix.
///
///    So this module ships as Phase 1 shipped: **deliberately dead code with a live test suite.** `derive`
///    is reachable from `lint`-shaped reporting and from tests; `offer` is reachable from NOTHING, and it
///    must stay that way until the lock exists. A chore queue that offers without a lock is not a smaller
///    version of this feature — the design doc says so in as many words: *"without those four, it is a
///    machine for manufacturing duplicate work and false green."* The subset is the failure mode, so the
///    unwired state is the honest one. See .github#733.
///
/// 2. **VERIFIABLE, NOT MERELY REPORTED.** "The agent said it did it" is a promise, and a promise that
///    nothing re-checks is exactly the #510 shape — *the fix for fail-open must not itself fail open*. So a
///    chore is never retired on a report. It is retired when `derive` no longer produces it from a FRESH
///    read, which is the same check that produced it. This is why `derive` is a pure total function of
///    observed board state and why it is the ONLY door to a `Chore`: re-running the check IS the
///    verification, so the two cannot disagree, and every chore is idempotent BY CONSTRUCTION rather than by
///    a promise in a doc.
///
/// 3. **OFFERED AT A SAFE POINT, AND BOUNDED.** Never mid-claim: a worker holding a lease with a live
///    touch-set must not be handed an unbounded side-quest that blows its lease or its context. `SafePoint`
///    is a capability with no public constructor, minted only where the caller is OBSERVABLY idle, so
///    "never mid-claim" is an argument rather than a check somebody can forget. And `offer` hands back **at
///    most one** chore, carrying its `Size`, so the unlucky caller does not pay for everybody's garbage
///    collection.
///
/// 4. **CHORES DO NOT GENERATE CHORES.** Strict depth-0, or the drain never converges. Enforced the same
///    way: `offer` demands a `SafePoint`, and the chore-execution path never mints one. A chore that offered
///    a chore cannot be written down.
///
/// **WHAT IS A CHORE, AND WHAT IS NOT.** A chore is a `/check-board` finding whose remedy is a BOARD WRITE
/// — the projection changing to match the truth. `/check-board`'s one rule holds here unchanged: *fixes only
/// ever write to the board.* A finding whose remedy needs judgement (`UNCLAIMED-IN-PROGRESS` — someone is
/// working outside the protocol; `BLOCKER-UNPARSEABLE` — prose in a dependency field; `DONE-STATUS-OPEN-ISSUE`
/// — was the flip premature?; `EPIC-*`; `UNDECLARED-PATHS` — the fix is an *issue* edit) is NOT a chore and
/// must never become one. Handing an agent a judgement call and re-checking whether it "did it" would
/// automate exactly the decisions this org files issues to have humans make. Those stay report-only, in
/// `lint`.
module Chore =

    open Types

    /// WHAT THE CALLER IS BEING ASKED TO PAY — condition 3's "explicit size, so the worker can decline".
    ///
    /// NOT a duration and not a point count. It is the shape of the remedy, because that is what a caller
    /// actually decides on: whether this fits in the gap before it picks up real work.
    type ChoreSize =
        /// One board write, decided entirely from the scan the offer already paid for. No further reads.
        | Quick

        /// A write plus a fresh per-subject read — the remedy must re-verify against the server before it
        /// acts, because the scan is a snapshot and this subject can change under it.
        | Involved

        member Label: string

    /// WHICH `/check-board` RULE GENERATED THIS CHORE.
    ///
    /// Each case carries the fact that JUSTIFIES it, not merely a code — so the statement a worker reads and
    /// the condition `derive` tested are the same value, and a chore cannot describe a subject it did not
    /// observe. The rule ids match `/check-board`'s table exactly; they are the anchor a report cites.
    type ChoreKind =
        /// `STALE-CLAIM` — the lease lapsed AND we looked for the item's own `item/<n>-*` PR and found none.
        /// Remedy: collect the marker, restoring the column it overwrote.
        ///
        /// ONLY from `LeaseExpiredNoPr`. Lease expiry is EVIDENCE of abandonment, never PROOF, and its false
        /// positive is systematic — work that outlasts its lease (#581). A lapsed lease whose PR is open is
        /// a worker demonstrably still working, and a lapsed lease we could not probe is one we cannot rule
        /// dead. Neither is a chore; `Writes.reapable` refuses both, and this refuses to OFFER either, so
        /// the reaper is never handed a chore it must then decline.
        | StaleClaim of holder: WorkerId

        /// `CLAIM-STATUS-LAG` — a LIVE claim holds the item, but the board column does not say so.
        /// Remedy: `Status = In progress`.
        ///
        /// Carries the column actually observed. Only the columns a claim SHOULD have overwritten qualify
        /// (`Ready`/`Backlog`/`NoStatus`): a holder who deliberately set `Blocked` or `In review` during the
        /// lease made a decision, and #331 is the rule that a column set deliberately during a lease still
        /// wins. Reconciling those would overwrite the holder's own judgement with a default — the drift
        /// this closes, running backwards.
        | ClaimStatusLag of column: BoardStatus

        /// `CLOSED-ISSUE-NOT-DONE` — the ISSUE is closed but the column is not `Done`. Remedy: `Status = Done`.
        ///
        /// The column is a projection of the work; the issue IS the work, and when they disagree the issue
        /// wins (#520). This does NOT fake a done-stamp: the stamp is `done --flip`'s, it is earned against a
        /// merged PR, and it is not what this writes. This only stops a closed issue sitting on the board
        /// wearing a column that says it is still live.
        | ClosedIssueNotDone of column: BoardStatus

        /// `BLOCKER-CLEARED` — every blocker resolved, but the column still says `Blocked`.
        /// Remedy: `Status = Ready`.
        ///
        /// Resolved means CLOSED **or MERGED** (#476): a PR's state is OPEN | CLOSED | MERGED, so a rule that
        /// clears only on CLOSED unblocks when the blocking work is ABANDONED and blocks forever once it is
        /// FINISHED. Requires EVERY blocker to be resolved — one `BlockerUnknown` or `BlockerUnparseable` and
        /// this is not offered at all, because "I could not look" is not "I looked and it is fine" (#266,
        /// #421) and the safe direction on a block is to hold it. Carries what it saw resolve.
        | BlockerCleared of resolved: string list

        /// `STATUS-NOT-BLOCKED` — an OPEN blocker, but the column is `Ready`/`Backlog`, so the scheduler is
        /// advertising work that is not startable. Remedy: `Status = Blocked`.
        ///
        /// Requires a blocker observed OPEN. An unresolvable blocker also blocks — but writing `Blocked` off
        /// a read we failed to make would stamp a column from a failure, so that stays report-only.
        | StatusNotBlocked of blockers: string list

        /// The `/check-board` rule id — the anchor a report cites and a reader greps back to this code.
        member RuleId: string

    /// ONE UNIT OF DEFERRED MAINTENANCE.
    ///
    /// There is no public constructor and no `create`: the ONLY door is `derive`. That is condition 2 made
    /// structural — a `Chore` that exists is a condition somebody OBSERVED, so "re-run the check that
    /// generated it" is always possible, and a chore cannot be minted from a report, a queue entry that
    /// outlived its condition, or an agent's say-so.
    [<Sealed>]
    type Chore =
        /// The item the condition was observed on.
        member Subject: Ref

        member Kind: ChoreKind

        member Size: ChoreSize

        /// STABLE ACROSS RE-DERIVATION: `<rule-id>:<owner>/<repo>#<n>`. The same condition on the same
        /// subject yields the same id on every pass, which is what lets the lock name a chore and what lets
        /// `isRetired` ask "is THIS one gone?" of a fresh derivation.
        member Id: string

        /// One line: what is wrong, and what the remedy writes. This is the text the offer prints.
        member Statement: string

    /// WHERE A CHORE MAY BE OFFERED — the natural boundaries of condition 3.
    type Boundary =
        /// At `next`: the worker is idle and about to pick up work anyway.
        | AtNext
        /// After `done --flip`: the item is stamped and the claim is already dropped (#533).
        | AfterDone

        member Label: string

    /// PROOF THAT THE CALLER IS SOMEWHERE IT IS SAFE TO BE HANDED A CHORE — condition 3, and condition 4.
    ///
    /// ABSTRACT, with no public constructor. The only door is `safePoint`, which mints one ONLY when the
    /// caller is observably idle. So "never offer a chore to a worker holding a live lease" is not an `if`
    /// in the offer path that a later refactor can drop — it is the argument `offer` cannot be called
    /// without.
    ///
    /// It carries condition 4 as well, and this is the whole of the depth-0 enforcement: the chore-execution
    /// path never mints one of these, so a chore cannot offer a chore. The drain converges because the
    /// recursion is unwritable, not because a counter stops it.
    [<Sealed>]
    type SafePoint =
        member Boundary: Boundary
        member Worker: WorkerId

        /// The board the idleness was OBSERVED on, carried so that `offer` cannot be asked about a
        /// different one. The evidence and the subject are the same value — a `SafePoint` minted from
        /// board A and spent on board B would be a capability proving nothing about what it authorised.
        member internal Items: Item list

    /// MINT A `SafePoint` — the only door, and it looks rather than trusts.
    ///
    /// `None` when this worker holds a live claim anywhere in `items`: that worker is mid-lease with a live
    /// touch-set, and an unbounded side-quest is exactly what must not be handed to it. The evidence is
    /// DERIVED from the board we just read, never asserted by the caller — a caller that could pass
    /// `iAmIdle = true` is a caller that can be wrong, and this is the argument that stops the offer.
    ///
    /// A stale-but-unreaped claim of our own counts as held: the lock is broken by `reap`, not by the clock
    /// (#461/#581), and its touch-set is still reserved.
    val safePoint: boundary: Boundary -> worker: WorkerId -> items: Item list -> SafePoint option

    /// DERIVE EVERY CHORE THIS BOARD STATE IMPLIES. Pure, total, and the ONLY constructor of a `Chore`.
    ///
    /// This is condition 2. Because the queue is DERIVED rather than STORED, there is nothing to keep in
    /// step with reality: a chore whose condition somebody fixed simply stops being produced, by anybody,
    /// and a chore two workers both perform converges instead of duplicating. There is no queue file to
    /// go stale, no entry to leak, and no "mark it done" for an agent to lie about.
    ///
    /// It cannot read anything, so it cannot mistake a failed read for a condition. Where the caller could
    /// not learn a fact — `BlockerUnknown`, `LivenessUnknown`, `TouchSet.Unreadable` — no chore is produced:
    /// every rule here fails CLOSED, because the cost of a missed chore is that the next caller does it, and
    /// the cost of a wrong one is a board write nobody wanted.
    val derive: items: Item list -> Chore list

    /// OFFER **AT MOST ONE** CHORE — condition 3's bound, and it is a hard one.
    ///
    /// Not a list. "The unlucky caller must not pay for everybody's garbage collection" is a real constraint
    /// and a list is how it gets violated: a caller handed twelve chores does twelve, or does one and drops
    /// eleven on the floor having taken the lock on all of them. One caller, one chore, and the fleet's
    /// throughput does the rest — the whole premise is a fleet *already calling the tool constantly*, so the
    /// queue drains at the rate the fleet calls, not at the rate any one caller sacrifices itself.
    ///
    /// Ordering is by how much the chore unwedges the QUEUE, not by age: a `STALE-CLAIM` reserves a
    /// touch-set that holds live work out of the board (#601), so it goes first; then the rules that make
    /// unstartable work startable or stop startable work being advertised falsely; then the cosmetic lag.
    /// Ties break on the chore's `Id`, so the order is TOTAL — two callers deriving the same board agree
    /// about what is next, which under a fan-out is what stops them contending on different chores every
    /// pass.
    ///
    /// It takes the board from the `SafePoint` rather than as a second argument, so the idleness evidence
    /// and the board it is spent on cannot come apart.
    val offer: at: SafePoint -> Chore option

    /// HAS THIS CHORE'S CONDITION ACTUALLY GONE? — condition 2's re-check, and the ONLY way to retire one.
    ///
    /// `items` must come from a FRESH read, never the scan cache: this is a reconciler's question, and its
    /// whole job is to say what is true right now. Answering it from the 90s cache would retire a chore
    /// against the very snapshot that produced it, which would confirm every remedy including the ones that
    /// silently failed — a check that reports success over a subject it cannot see (#266), sitting inside
    /// the mechanism built to stop exactly that.
    ///
    /// `true` iff re-deriving no longer yields this chore's `Id`. Note what this does NOT ask: whether the
    /// agent says it did the work, whether a write returned 200, or whether a queue entry was popped. The
    /// condition is gone or it is not.
    val isRetired: chore: Chore -> items: Item list -> bool
