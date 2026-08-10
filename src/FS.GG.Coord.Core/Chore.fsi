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
///    **THE SUBSTRATE IS DECIDED — ADR-0041, .github#873 — AND THE WIRING IS NOT DONE, WHICH IS WHY NOTHING
///    CALLS `offer` YET.** A chore takes `Writes.claim`, UNCHANGED, on a dedicated per-repo chore-lock issue
///    (closed, so it is never mistaken for work; never LOCKED, because the marker is a comment), with a
///    short lease:
///
///        Writes.claim transport choreLeaseMinutes worker session choreLockRef (fun () -> None)
///
///    **`choreLockRef` COMES FROM `Options.choreLockRef owner repo` — NOT from `registry/repos.yml`**
///    (ADR-0042, .github#1026). ADR-0041 recorded the number in the roster file, and this text cited it for
///    its whole life; the engine has no YAML reader, deliberately (the shim ships as a `kind: client` kit
///    item WITHOUT the roster — case 13 §6c / #381), so the source named here did not exist and #733 had no
///    legal first step rather than merely a hard one. The ref is embedded beside the roster and pays the
///    roster's price: adding a repo's lock is a code edit here, not a data change.
///
///    `None` ⇒ `offer` REFUSES — unchanged from ADR-0041, and the reason the reader returns an option at all.
///    Today only `.github` has a lock (#1033); the six receivers are `None` until .github#733 creates theirs,
///    so `offer` refuses there, which is the honest state and not a gap.
///
///    This text used to say the substrate was an open DECISION, and that the item CAS was "145 lines of
///    claim-specific policy a chore lock wants none of" — so reusing it meant factoring the org's most
///    safety-critical function, and not reusing it meant a second CAS (#485). **That premise was wrong, and
///    it is what kept this module dead:** `claim` touches only comments, its lease is already a PARAMETER,
///    and its one board coupling is the caller-supplied `readPreviousStatus` callback. It is already a
///    general comment-order CAS over an arbitrary issue ref — `WriteTests` had been driving it as
///    `claim … aRef (fun () -> None)` all along, which IS this configuration. The lock was built the whole
///    time; only the reading of it was missing.
///
///    So this module still ships as Phase 1 shipped: **deliberately dead code with a live test suite.**
///    `derive` is reachable from `ChoreTests` and from `offer`/`isRetired` beside it; `offer` is reachable
///    from NOTHING — so **no shipped code path derives a chore, and not one of the five rules fires.** A
///    condition they name is a condition nothing observes. That must stay true until .github#733 wires it at
///    the safe point and adds the `perform` path that re-verifies with `isRetired` against a FRESH read.
///
///    This line used to say `derive` was "reachable from `lint`-shaped reporting and from tests", and the
///    CONTRAST — two callers against `offer`'s none — read as a module that was half live. It never was.
///    `lint` is `NO-TOUCH-SET`/`BAD-TOUCH-SET` and nothing else (#496/#945/#1013); its own comment defers the
///    other rule families to a later slice, and `Client.fs`, where it lives, does not contain the string
///    `Chore` at all. **The cost was not cosmetic** (.github#1047): #733 — the item that wires THIS module —
///    was parked `Blocked by #1026` under the note *"self-healing: BLOCKER-CLEARED flips it back the moment
///    #1026 resolves"*. #1026 resolved; nothing flipped it; #733 sat blocked on a condition only #733 could
///    clear, invisible to every `take`, until it was reconciled by hand. **The rule that would have
///    unwedged it is a rule this module does not run** — and a reader who believed this line had no way to
///    know that. An `.fsi` that overstates its own reachability does not merely mislead: it gets items parked
///    behind promises nothing can keep.
///
///    A chore queue that offers without a lock is not a smaller version of this feature — the design doc says
///    so in as many words: *"without those four, it is a machine for manufacturing duplicate work and false
///    green."* The subset is the failure mode, so the unwired state remains the honest one until then. See
///    .github#733 and ADR-0041.
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

    /// WHERE A CLEARED ROW SHOULD LAND — `BLOCKER-CLEARED`'s remedy, chosen from the row's own touch-set
    /// (.github#2220).
    ///
    /// `BLOCKER-CLEARED` used to answer one question — *"do the recorded blockers still hold?"* — and emit
    /// `Status = Ready` unconditionally. That is a correct answer to the question it asks, applied to rows
    /// for which `Ready` is not a REACHABLE state. `Ready` is the column `Schedulability.columnStartability`
    /// calls `AlwaysStartable`, so it asserts a startability the scheduler will then refuse on every pass:
    /// `batch`/`take` list the row as a candidate and decline it forever. A permanently unfillable lane at
    /// the head of the queue — MEASURED on `.github#1858`, the board's only `Severity: Critical` row, which
    /// spent six days simultaneously the most important defect on the board and impossible to schedule.
    ///
    /// **THE DISTINCTION IS ALREADY TYPED, AND THE RULE SIMPLY DID NOT READ IT.** `Types.fsi` separates
    /// `Undeclared` (an OMISSION) from `DeclaredNone` (a DECISION) from `DeclaredChore` (reserves nothing
    /// but IS schedulable). This type is that partition projected onto the one axis the remedy needs — the
    /// column to write — while keeping the two unschedulable cases APART in the prose, because collapsing
    /// them re-introduces exactly the ambiguity `Types.fsi:127-140` exists to remove: one is a bug to
    /// repair, the other a decision to respect, and a worker reading the offer acts differently on each.
    ///
    /// This is a REMEDY, not a second startability opinion. It never promotes a row the old rule held, and
    /// it never holds a row the old rule promoted — it only redirects the write for the two populations
    /// whose destination was unreachable.
    type ClearedDestination =
        /// A real declaration (`Declared`) or `Paths: any` (`DeclaredChore`). `Ready` is reachable, so the
        /// remedy is #620's original flip, unchanged. THE COMMON PATH, and the one the fixture pair pins.
        | ToReady

        /// `Paths: none` — unschedulable BY DESIGN. Clear the stale `Blocked`, which is genuinely wrong
        /// once the blockers have resolved, WITHOUT asserting a startability the row cannot have.
        /// `Backlog` says exactly that and nothing more: not blocked, not startable, parked by decision.
        | ToBacklogDeclaredNone

        /// No `Paths:` line at all — unschedulable by OMISSION. Same column as above, because the board
        /// facts are the same; a different sentence, because the repair is not. `lint`'s `UNDECLARED-PATHS`
        /// owns the repair itself, which is an ISSUE edit and never a board write.
        | ToBacklogUndeclared

        /// The column this destination writes.
        member Status: BoardStatus

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

        /// `CLAIM-REVIEW-LAG` — a live claim has a freshly observed implementation PR while its board
        /// status has not advanced to `In review`.
        | ClaimReviewLag
        /// A fresh lifecycle fact-set disagrees with the mutable Project Status column.
        | LifecycleProjectionLag of destination: BoardStatus

        /// `CLOSED-ISSUE-NOT-DONE` — the ISSUE is closed but the column is not `Done`. Remedy: `Status = Done`.
        ///
        /// The column is a projection of the work; the issue IS the work, and when they disagree the issue
        /// wins (#520). This does NOT fake a done-stamp: the stamp is `done --flip`'s, it is earned against a
        /// merged PR, and it is not what this writes. This only stops a closed issue sitting on the board
        /// wearing a column that says it is still live.
        | ClosedIssueNotDone of column: BoardStatus

        /// `BLOCKER-CLEARED` — every blocker resolved, but the column still says `Blocked`.
        /// Remedy: `Status = <the destination's column>` — `Ready` for a row that can hold it, `Backlog`
        /// for one whose touch-set makes `Ready` unreachable (.github#2220, and `ClearedDestination`).
        ///
        /// Resolved means CLOSED **or MERGED** (#476): a PR's state is OPEN | CLOSED | MERGED, so a rule that
        /// clears only on CLOSED unblocks when the blocking work is ABANDONED and blocks forever once it is
        /// FINISHED. Requires EVERY blocker to be resolved — one `BlockerUnknown` or `BlockerUnparseable` and
        /// this is not offered at all, because "I could not look" is not "I looked and it is fine" (#266,
        /// #421) and the safe direction on a block is to hold it. Carries what it saw resolve.
        ///
        /// ...AND, for an item that DECLARES a machine-checkable registry predicate, that predicate's
        /// resolved verdict must `Agrees` — ADR-0050 call-site B (.github#1203). A recorded blocker can be
        /// a PROXY for the item's real acceptance (FS.GG.Rendering#923's "WI-2 (Game publishes the skill)"),
        /// so blockers-cleared alone can fake readiness; the flip-time predicate re-verify closes that. A
        /// `Contradicts` or an `Unknown` HOLDS the item on exactly the terms a `BlockerUnknown` does — fail
        /// closed (#266). An item declaring no predicate (`Item.Predicate = None`) is ungated and flips as
        /// today (ADR-0050 decision 5 — a general prose predicate is not mechanically evaluable).
        ///
        /// ...AND NEVER ON AN ITEM PARKED ON A HUMAN (.github#1644). ADR-0045's `Blocked on: human/decision`
        /// / `human/action` body sentinel says a PERSON must act before the item is startable, and
        /// `Schedulability` step 3b refuses such an item outright. This is the one mechanical rule that
        /// could overwrite that judgement, and the overwrite does not heal: `Ready` is the column that
        /// advertises the row, `lint`'s `BLOCKED-NO-REASON` only watches a `Blocked` one, and
        /// `STATUS-NOT-BLOCKED` cannot push it back with every blocker resolved. So an automated promotion
        /// would silently convert *"a human must decide this"* into *"a worker may pick this up"* — the
        /// exact conversion ADR-0045's sentinel exists to prevent.
        ///
        /// It FAILS CLOSED on a body nobody read, and that clause is load-bearing rather than defensive.
        /// `Item.HumanBlock = None` means BOTH "declares no sentinel" and "we did not look" — the option has
        /// nowhere to put the second — so gating on `IsSome` alone would promote a parked row whose body
        /// read failed. The fact IS recorded, one field over: `TouchSet.Unreadable` is "we did not read the
        /// body", off the same body, and the flip is held on it. See `derive`'s note below, which this
        /// makes the ONE exception to.
        ///
        /// ...AND NEVER ON AN ITEM WHOSE IMPLEMENTATION IS ALREADY IN FLIGHT (.github#1738). An open
        /// `item/<n>-*` PR on a markerless row is what `Schedulability` step 5b refuses on — *"an
        /// implementation is already in flight … claiming it now would duplicate work that is already
        /// written"* (#651) — and this remedy writes `Ready`, the one column `columnStartability` calls
        /// `AlwaysStartable` and therefore the one that ADVERTISES the row. Two mechanisms disagreeing about
        /// one item, with the write winning: the human-park shape above, one field over, and MEASURED three
        /// times in a single board event (`FS.GG.Rendering#1086`/`#1089`/`#1092` when `#1094` merged, each
        /// with a complete open PR).
        ///
        /// It reads `Item.ItemPr` — the SAME field step 5b refuses on, asked the same way. That is the
        /// mechanism, not a convenience: a gate consulting a second source, or reading this one more
        /// strictly, would be a third opinion about one row and could disagree with the scheduler in the
        /// other direction.
        ///
        /// **IT IS NOT FAIL-CLOSED ON A FAILED PROBE, AND THAT RESIDUAL IS FILED — .github#1924.** Unlike
        /// the human-park gate above, this one has no receipt to fail closed on: `Reads.prAlive` has FIVE
        /// outcomes and `int option` carries one, so THREE arrive as `None` = "no PR" —
        /// `LeaseExpiredBranchPushed` (#1055), `LivenessUnknown` (we could not ask), and `Error _`
        /// including the `RateLimited` that `prAlive` propagates on purpose. #651 chose that collapse
        /// while step 5b was the only consumer — and step 5b fails open into OFFERING, which is read-only
        /// and re-decided next scan. This rule fails open into a board WRITE, the asymmetry `derive`'s note
        /// below says the mechanism rests on. Fixing it changes a shared wire fact and its readers, so it
        /// is a filed item rather than a clause here; until it lands, a rate-limited scan can still promote
        /// a cleared row.
        ///
        /// **THE PROBE'S POPULATION IS PART OF THIS RULE.** `Scan` used to probe only the columns a scheduler
        /// offers TODAY (`Ready`/`Backlog`); this rule writes the column that makes a row offerable TOMORROW,
        /// so a `Blocked` row — the ONLY population this rule acts on — was never probed and `ItemPr` was
        /// `None` for every one of them. A gate that could never see its subject is the fail-open wearing the
        /// fix's clothes (#266), so #1738 widened the probe to cover the `BLOCKER-CLEARED` candidate set. Both
        /// sides key on `Blockers.cleared`, spelled once in `Core`, because a probe that drifts NARROWER than
        /// the rule blinds the gate again — and the probed set is deliberately a SUPERSET (it does not consult
        /// the park or the predicate), because wider costs requests where narrower costs correctness.
        ///
        /// Never on a RESERVED item: that `Blocked` is most likely the holder's own, and their column wins
        /// (#331). See "the reserver owns the column" below.
        ///
        /// **AND IT IS THE ONLY KIND THAT NEEDS ANY OF THIS** — the general statement of .github#1644 (*"no
        /// mechanical remedy may overwrite a fact the scheduler refuses on"*), applied once rather than
        /// restated per rule, and pinned by `ChoreTests` rather than asserted here. `Write` above is most of
        /// what settles it: `BLOCKER-CLEARED` is the only kind whose remedy writes a column
        /// `Schedulability.columnStartability` calls startable. All FIVE others, none omitted:
        ///
        /// - `CLAIM-STATUS-LAG` writes `In progress`, and fires only on a RESERVED item — where `ItemPr` is
        ///   `None` by construction, since `Scan` probes only markerless rows.
        /// - `STATUS-NOT-BLOCKED` writes `Blocked` and requires an OPEN blocker, on which `Schedulability`
        ///   step 3 already refuses — so its write AGREES with the scheduler and moves the row FURTHER from
        ///   startable.
        /// - `CLOSED-ISSUE-NOT-DONE` writes `Done` on a row step 1 refuses as `IssueClosed`.
        /// - `CLASS-PROJECTION-LAG` writes no scheduling column at all.
        /// - `STALE-CLAIM` has `Write = None` and is the case a `Write`-shaped argument alone would MISS: its
        ///   remedy is delegated to `reap`, which restores `PreviousStatus` — and that CAN be `Ready`. It
        ///   still cannot contradict step 5b, for a reason of its own rather than of its write: it fires only
        ///   on `LeaseExpiredNoPr`, a probe that SUCCEEDED and found no PR, so the fact it would overwrite is
        ///   the fact it was derived from.
        ///
        /// None of the five can contradict a refusal, in either direction.
        ///
        /// ...AND THE REMEDY IS CHOSEN FROM THE ROW'S TOUCH-SET, NOT FIXED AT `Ready` — .github#2220. See
        /// `ClearedDestination` for the incident. Every gate above HOLDS the row; this one is the first
        /// that REDIRECTS it, and the difference matters: the populations it redirects (`Paths: none`,
        /// and a missing `Paths:` line) have a genuinely stale `Blocked` that something must still clear.
        /// Holding them would leave the lie in place; promoting them to `Ready` advertises a row no
        /// scheduler can admit. `Backlog` is the only column that is true of both.
        ///
        /// The destination is carried, not re-derived, on the same terms every other case carries what it
        /// saw: `Write` and `Statement` both read THIS value, so the column the receipt names and the
        /// column the sentence promises cannot come apart.
        | BlockerCleared of resolved: string list * destination: ClearedDestination

        /// `STATUS-NOT-BLOCKED` — an OPEN blocker, but the column is `Ready`/`Backlog`, so the scheduler is
        /// advertising work that is not startable. Remedy: `Status = Blocked`.
        ///
        /// Requires a blocker observed OPEN. An unresolvable blocker also blocks — but writing `Blocked` off
        /// a read we failed to make would stamp a column from a failure, so that stays report-only.
        ///
        /// Never on a RESERVED item, and here the rule's own premise is what fails: a claim reserves the
        /// item's touch-set, so the scheduler will not hand it to anyone whatever the column says. It is not
        /// being advertised, so there is nothing to correct.
        | StatusNotBlocked of blockers: string list

        /// `CLASS-PROJECTION-LAG` (.github#1588) — the item's own text declares a `Class` and the board's
        /// `Class` column does not agree. Remedy: `Class = <the declared class>`.
        ///
        /// **THE ONLY KIND THAT WRITES A FIELD OTHER THAN `Status`**, and the reason `Write` below exists.
        ///
        /// The direction is an ADR, not a preference. #1588's prose proposed making the board field the
        /// AUTHORITY and deriving ADR-0045's `Blocked on: human/decision` sentinel from it; its own
        /// acceptance criteria say the reverse, and the criteria are right — ADR-0045 decided this exact
        /// axis, rejecting a Projects v2 field in favour of a body line. Field-as-authority would reverse
        /// an Accepted ADR by rewriting ~50 issue bodies. So the body declares and this projects, which is
        /// also the only reading under which the column is not the fourth hand-maintained copy AC5 forbids.
        ///
        /// DERIVES NOTHING FROM AN ITEM THAT DECLARES NOTHING. `Item.Class = None` produces no chore —
        /// never a default class. Untriaged severity is reported by `lint`'s `CLASS-UNSET` and settled by
        /// a human; a default here would be #266's fail-open one axis over.
        ///
        /// Fires only on DISAGREEMENT, which is what lets it RETIRE. An unconditional write would leave
        /// `isRetired` answering "still owed" forever against a write that landed.
        ///
        /// Never on a RESERVED item, on the shared rule below — deference DEFERS, and the projection costs
        /// nothing to derive one pass later.
        ///
        /// OPEN **OR** CLOSED (.github#2254) — no longer OPEN-only. A row that reaches Done/CLOSED between
        /// two reconcile passes was never OPEN at a moment this rule looked at it, so the old OPEN-only
        /// gate left such a row's disagreement unexamined forever, invisible to both `reconcile` and
        /// `lint`. The population this actually reaches for a closed row is narrower than "every closed
        /// row" in practice, because `item.Class` for one is `None` unless `Scan.snapshot` paid the extra
        /// body read — itself gated on an EMPTY `BoardClass`, the population #2254's AC1 names. A closed
        /// row already carrying some `Class` value is never read and never reaches this rule at all.
        | ClassProjectionLag of declared: ItemClass

        /// The `/check-board` rule id — the anchor a report cites and a reader greps back to this code.
        member RuleId: string

        /// **THE BOARD WRITE THIS KIND'S REMEDY PERFORMS — the field and the value, spelled ONCE.**
        ///
        /// `None` is `STALE-CLAIM`, whose remedy is a MARKER COLLECTION rather than a field write and is
        /// delegated to `reap`. It means "there is no field write", never "we could not work one out".
        ///
        /// **IT LIVES HERE, IN THE CORE, BECAUSE THE INVARIANT THAT GUARDS IT LIVES HERE.** This was a
        /// private `write` in `Client.fs`, correctly documented there as the single source for the write,
        /// the receipt's `field`/`value` pair, and the human table's remedy column. That was true while
        /// every kind wrote `Status` — and `ChoreTests`' "an item derives AT MOST ONE chore" rests on
        /// exactly that coincidence, its own comment saying "every one of the five kinds has a remedy that
        /// writes `Status`, so 'at most one chore that writes the column' and 'at most one chore' are the
        /// same sentence". `CLASS-PROJECTION-LAG` breaks the coincidence: a `Status` repair and a `Class`
        /// projection on one item are two independent repairs, not a contradiction.
        ///
        /// So the invariant has to be restated as what it always MEANT — at most one chore per FIELD — and
        /// a Core test cannot state that while the field mapping is in the Cli. Copying the mapping into
        /// the test would be the second hand-maintained `match` over `ChoreKind` that `Client.fs`'s own
        /// comment forbids, and a test asserting a partition it defines itself asserts nothing. Moving it
        /// keeps one source and puts it where it can be gated.
        member Write: (string * string) option

    // THE RESERVER OWNS THE SCHEDULING COLUMN.
    //
    // These are `//`, not `///`, and that is deliberate: this documents a RULE, not the declaration below it.
    // As a `///` block separated by a blank line it was not orphaned the way it looked — F# attached it to the
    // NEXT declaration, so the rule silently became the opening paragraph of the `Chore` TYPE's XML summary.
    // It builds clean under TreatWarningsAsErrors, so nothing catches it, and the one rule it described is the
    // one `CLOSED-ISSUE-NOT-DONE` got wrong.
    //
    // While a claim marker reserves an item, its column belongs to whoever holds it. A worker who hits a
    // blocker and sets `Blocked` made a DECISION, and a column set deliberately during a lease still wins
    // (#331), so a chore that "reconciles" it overwrites somebody's judgement with a default. It is the
    // RESERVER, not the live winner (`Reads.reserver`, not `Reads.winner`): a lease is a clock but a lock is
    // broken only by `reap` (#461/#581), so a stale-but-uncollected marker still owns its column.
    //
    // Deferring costs nothing, which is why it is safe to make absolute: `STALE-CLAIM` collects an abandoned
    // marker and `reap` restores the column it overwrote (#481), so the deferred rules fire on the next pass.
    // Deference DEFERS; it does not suppress.
    //
    // ONE exception: `STALE-CLAIM`, which is the rule that ENDS the reservation. It must fire on a reserved
    // item — that is its whole purpose — and it cannot contradict the others, because `Chore.choresFor` derives
    // it and them from opposite branches of one `match` on the claim.
    //
    // `CLOSED-ISSUE-NOT-DONE` was a second exception and is no longer one. Its justification — "a closed issue
    // is ground truth about the WORK (#520) and no lease outranks it" — answers a LIVE lease and was never true
    // of a STALE one, where `STALE-CLAIM`'s own remedy writes `PreviousStatus` straight back: 42 combinations
    // derived that pair. It costs nothing to defer in either case. `rank` already orders `STALE-CLAIM` (0)
    // ahead of it (3), so no observable order changed; and against a live lease the holder who just closed the
    // issue is about to `done --flip` it, which is a race #331 already forbids.
    //
    // Without this rule, an item that was `Ready`, claimed, and blocked derived BOTH `CLAIM-STATUS-LAG` ("set
    // In progress") and `STATUS-NOT-BLOCKED` ("set Blocked"): two chores writing opposite columns to one item,
    // with the winner decided by whichever caller drained the queue first. The invariant is asserted over every
    // (status × claim × blocker × issue-state) combination in `ChoreTests`, with NO kind excluded from the
    // count: **an item derives at most ONE chore.** Every kind's remedy writes the column, so that is the same
    // sentence as "at most one chore may want to write its column" — and it is the form that cannot be quietly
    // narrowed. The longer one was: it excluded `STALE-CLAIM` as "a restore, not a write", and that exclusion
    // is what hid the pair above.

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
        /// After `done`: the item is stamped and the claim is already dropped (#533) — the two facts that
        /// make it a safe point, and `done` establishes both with or without `--flip` (the column write is
        /// unconditional; `--flip` only rolls the parent up). Said as `done --flip` while nothing minted this
        /// case, which was harmless until #733 wired it and the narrower label stopped being true.
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

    /// THE BOARD AN IDLENESS QUESTION MAY BE PUT TO — and, more to the point, whether it can answer.
    ///
    /// **`Item list` could not say, and that is what let condition 3 fail open** (#1086). "Is this worker
    /// idle?" is a question about the WHOLE board: a worker mid-item in FS.GG.SDD is not idle while asking
    /// about `.github`. `safePoint` answered it honestly about whatever list it was handed — and `next
    /// --repo <r>` hands it a list `Scan.scope` has already filtered, in which that SDD claim does not
    /// appear. Invisible read as absent, the worker read as idle, and the guard handed them the side-quest
    /// it exists to withhold.
    ///
    /// So the scope rides IN THE TYPE. A bare list cannot reach `safePoint` any more; the caller has to say
    /// which it has, and the only place `Whole` is constructible is where the unfiltered read happens.
    type Board =
        /// Every row the scan returned, UNFILTERED by repo — `Scan.scope None`. A live claim of ours in any
        /// repo is visible here, so idleness derived from it is honest.
        | Whole of Item list

        /// A slice: `--repo` filtered it, or a caller narrowed it. It CANNOT answer the idleness question —
        /// a claim outside the filter is invisible to it, and invisible is not absent (#266). Carried as a
        /// case rather than forbidden outright so that "I have only a slice" is a thing the type can SAY,
        /// and `safePoint` can refuse it, instead of a mistake it cannot see.
        | Filtered of Item list

    /// MINT A `SafePoint` — the only door, and it looks rather than trusts.
    ///
    /// `None` when this worker holds a live claim anywhere in `observed`: that worker is mid-lease with a
    /// live touch-set, and an unbounded side-quest is exactly what must not be handed to it. The evidence is
    /// DERIVED from the board we just read, never asserted by the caller — a caller that could pass
    /// `iAmIdle = true` is a caller that can be wrong, and this is the argument that stops the offer.
    ///
    /// `None` ALSO when `observed` is `Filtered`, and that is #1086's fix: a board that cannot see our other
    /// claims cannot report us idle. "I could not tell" is not a yes (#266), and here it never reaches the
    /// call sites as a yes because it is not expressible.
    ///
    /// **EVIDENCE AND SUBJECT ARE TWO ARGUMENTS, because they are two sets and always were.** Idleness is a
    /// fact about the WORKER, so it is asked of the whole board; a chore is a fact about a REPO, so it is
    /// derived over that repo's rows (ADR-0041's lock is per-repo — deriving over more would hand a worker
    /// one repo's chore under another's lock). `Chores.offer` used to fake this by minting TWICE — once over
    /// the board for idleness, once over the scoped subject to spend — and leaning on the second being a
    /// subset of the first. One mint, two arguments, no subset reasoning to get wrong.
    ///
    /// `subject` is what the resulting `SafePoint` carries, so `offer` still reads its board FROM the
    /// capability and cannot be asked about a different one.
    ///
    /// A stale-but-unreaped claim of our own counts as held: the lock is broken by `reap`, not by the clock
    /// (#461/#581), and its touch-set is still reserved.
    val safePoint: boundary: Boundary -> worker: WorkerId -> observed: Board -> subject: Item list -> SafePoint option

    /// DERIVE EVERY CHORE THIS BOARD STATE IMPLIES. Pure, total, and the ONLY constructor of a `Chore`.
    ///
    /// This is condition 2. Because the queue is DERIVED rather than STORED, there is nothing to keep in
    /// step with reality: a chore whose condition somebody fixed simply stops being produced, by anybody,
    /// and a chore two workers both perform converges instead of duplicating. There is no queue file to
    /// go stale, no entry to leak, and no "mark it done" for an agent to lie about.
    ///
    /// It cannot read anything, so it cannot mistake a failed read for a condition. Every rule fails CLOSED
    /// over the facts IT reads: the cost of a missed chore is that the next caller does it, and the cost of a
    /// wrong one is a board write nobody wanted. So `BlockerUnknown` and `BlockerUnparseable` stop
    /// `BLOCKER-CLEARED` (a blocker we could not resolve is not one we cleared, #266/#421), a resolved
    /// registry `Predicate` that `Contradicts` or is `Unknown` stops it too (ADR-0050 call-site B — the
    /// verdict is resolved at the impure edge and read here as a fact, so `derive` stays pure while the flip
    /// still fails closed on a predicate it could not verify, .github#1203), and `LivenessUnknown` stops
    /// `STALE-CLAIM` (a liveness probe that failed is not an abandoned lease, #581).
    ///
    /// It is PER-RULE, and deliberately not the blanket "an unknown fact anywhere produces no chore" this
    /// sentence used to claim. That was false and could not have been otherwise: a blocker we failed to
    /// resolve does not make the ISSUE's closedness unknown, so `CLOSED-ISSUE-NOT-DONE` still fires next to a
    /// `BlockerUnknown` — correctly. Suppressing a rule over a fact it never reads is not caution, it is a
    /// second way to be wrong.
    ///
    /// `TouchSet` HAS EXACTLY ONE READER, AND THIS SENTENCE USED TO SAY IT HAD NONE. Until .github#1644 it
    /// read *"no rule reads it at all, so `Unreadable` cannot suppress anything"* — true when written, and
    /// false the moment `BLOCKER-CLEARED` began consulting `Unreadable` as its BODY-READ RECEIPT. It is
    /// still the only reader and it still reads no path token: the parked-item gate needs the one fact
    /// `HumanBlock option` cannot carry — *did anybody read the body the sentinel would live on?* — and
    /// `TouchSet.Unreadable` is that fact, lifted off the same body by the same parse. Every OTHER rule
    /// ignores the field entirely, and `ChoreTests` pins both halves: invariance across every touch-set for
    /// the rules that do not read it, and the fail-closed hold for the one that does.
    val derive: items: Item list -> Chore list

    /// Convert a verified lifecycle projection into its one Status repair, if the board is stale.
    val lifecycleProjection: item: Item -> destination: BoardStatus -> Chore option

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
