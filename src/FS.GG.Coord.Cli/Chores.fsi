namespace FS.GG.Coord.Cli

/// THE CHORE QUEUE'S IO EDGE — the lock the pure core could not take, and the only door to an offer.
///
/// `Chore` (Core) is pure and total: it DERIVES what a board state implies, ranks it, and hands back at
/// most one. What it cannot do is take a lock — a lock is IO. ADR-0041 is the record of which lock it
/// takes: `Writes.claim`, UNCHANGED, on a dedicated CLOSED per-repo chore-lock issue, whose number
/// `Options.choreLockRef` carries (ADR-0042). This module is that composition and nothing else.
///
/// **This is the wiring `.github#733` is for, and its absence is why `Chore` was dead code.** Conditions
/// 2, 3 and 4 have been discharged as types in `Chore.fs` since PR #875 and could not fire, because
/// condition 1's lock had no home: it cannot live in a pure core, and which substrate it took was an open
/// decision until ADR-0041 measured that `Writes.claim` was ALREADY a general comment-order CAS over an
/// arbitrary ref — item-configured by its caller, not item-specific. No new function, no new marker
/// prefix, no edit to the org's most safety-critical write. The refactor the queue seemed to need was the
/// reason it never shipped.
///
/// WHY THE Cli LAYER. It is the only layer that can see all three pieces at once: `Chore` is Core,
/// `Writes.claim` is GitHub, and `choreLockRef` is Cli (ADR-0042 put it there because the engine has no
/// YAML reader — the shim ships to receivers WITHOUT `registry/repos.yml`, so a roster-reading lock lookup
/// would refuse exactly where the queue has to work). Cli is where the dependency edges meet, so Cli is
/// where the composition goes.
///
/// WHY NOT IN `Client.fs`. The offer path is the one part of the queue carrying a correctness argument of
/// its own — four conditions, each of which fails open if it is quietly dropped. `Client.fs` is 3,700
/// lines and the org's most contended file (#979 calls it "a collision magnet"); an argument put there is
/// an argument the next editor re-litigates by accident. A module with an `.fsi` is the argument written
/// where it can be read.
///
/// **EVERY REFUSAL IS SILENT, AND EVERY ONE FAILS CLOSED.** There is deliberately no error channel: an
/// offer is a COURTESY to a worker who asked for something else, so a chore queue that cannot take its
/// lock must offer NOTHING rather than broadcast (condition 1). A missing lock ref, a lost race, a
/// transport error, an exhausted budget — each is `None`, and the caller's real answer is untouched. The
/// cost of a missed chore is that the next caller does it; the cost of a broadcast one is
/// [#464](https://github.com/FS-GG/.github/issues/464) — N workers doing one chore N times — which is the
/// failure the queue exists to avoid, rediscovered inside the mechanism meant to help.
module Chores =

    open FS.GG.Coord
    open FS.GG.Coord.GitHub

    /// THE CHORE LOCK'S LEASE, IN MINUTES — SHORT, for a reason the item lease does not share.
    ///
    /// An item lease is 120m because an item is an hour of work. A chore is ONE board write (`Quick`), or a
    /// write plus one fresh per-subject read (`Involved`) — that is condition 3's bound, and this number is
    /// the same bound expressed in time. A longer lease buys the queue nothing and costs it the only thing
    /// that matters here: the window in which a worker that died mid-chore keeps its repo's whole drain to
    /// itself.
    ///
    /// It is not a deadline for the WORKER. Nothing measures how long the agent takes, and nothing has to:
    /// the queue is DERIVED, never stored (condition 2), so a chore whose condition is still there is
    /// simply re-derived on the next pass, and one somebody fixed stops being produced by anybody. This
    /// lease governs the LOCK only — how long before the next caller may collect a marker whose worker
    /// never came back. `Writes.claim`'s stale-marker collection is what makes that self-healing, which is
    /// ADR-0041's "beneficially so": the debris a chore lock has is exactly the debris every lock has.
    ///
    /// So: how long a dead worker stalls one repo's drain, and nothing else.
    val LeaseMinutes: int

    /// OFFER AT MOST ONE CHORE, HOLDING THE LOCK — the whole of the queue's IO.
    ///
    /// `Some(chore, lockRef)` means, and ONLY means: this worker is observably idle, the board implies this
    /// chore, and **we hold the per-repo chore lock** — which is `lockRef`. It is a capability, not a
    /// suggestion: the caller may act on it without asking anything else, which is the point of returning it
    /// rather than a list of candidates and a promise.
    ///
    /// It hands BACK the ref it locked rather than leaving the caller to look it up again. A second
    /// `choreLockRef` call is a second chance to answer differently — and the caller would have to make it
    /// on the same `repo` string, which is the one input here that is not already canonical.
    ///
    /// **`items` MAY SPAN REPOS, AND THE SCOPING IS THIS FUNCTION'S JOB — not the caller's.** The FS-GG
    /// board is org-wide (1,170 rows across 7 repos when measured), and a bare `next` with no `--repo` asks
    /// `Scan.scope None`, which returns every one of them. A lock is PER-REPO (ADR-0041), so a chore derived
    /// from another repo's row and serialised on this repo's lock is not locked at all: two workers holding
    /// two different repos' locks could be handed the SAME chore, which is condition 1 defeated by the
    /// mechanism meant to enforce it. So `offer` keeps only the rows belonging to the LOCK's repo.
    ///
    /// **Idleness is still asked of the WHOLE board, and that asymmetry is deliberate.** Idleness is a fact
    /// about the WORKER; a chore is a fact about the REPO. A worker holding a live claim in FS.GG.SDD is
    /// mid-item with a live touch-set — precisely who condition 3 protects — and a scoped board cannot see
    /// that claim, so scoping the idleness question would answer "idle" about a worker who is not.
    ///
    /// The order is what makes condition 1 true, and it is not an optimisation:
    ///
    /// 1. **`Chore.safePoint`** — mint the idleness evidence, or stop. `None` when this worker holds a live
    ///    claim anywhere on this board: a worker mid-lease with a live touch-set must not be handed a
    ///    side-quest (condition 3). The evidence is DERIVED from the board we already read, never asserted
    ///    by us — a caller that could pass `iAmIdle = true` is a caller that can be wrong.
    /// 2. **`Chore.offer`** — pure, free, and reads the board FROM the `SafePoint`, so the idleness evidence
    ///    and the board it is spent on cannot come apart. At most one, ranked by how much it unwedges the
    ///    queue.
    /// 3. **The lock, LAST.** Nothing is written until there is something to write it for. Deriving first
    ///    costs one pure pass over a list we already have; taking the lock first would spend a REST request
    ///    — the budget the item lock itself lives on (ADR-0034 §3) — on every idle `next` in the fleet, to
    ///    discover that the board is clean. On a healthy board that is the common case, so the cheap order
    ///    is also the correct one.
    ///
    /// **Only `Won` and `Renewed` are a lock.** `Lost` is another worker draining this repo — exactly what
    /// the lock is for, and the caller is told nothing, because there is nothing for it to do. `Twin`
    /// (#419) is two workers sharing an id, and a lock that cannot separate them is not a lock:
    /// `Writes.claim`'s refusal is inherited here rather than re-decided. `Impersonates` (#1646) is a caller
    /// acting as a worker it is not, and the chore lock is not exempt: the drain this serialises would
    /// otherwise be taken out in the name of a worker who never asked to drain anything. `Undecided`,
    /// `BlockedByUnparseableMarker`, and every transport error are "I could not take it" — never "I took
    /// it", which is the fail-open this whole codebase exists to make unwritable (#266).
    ///
    /// `repo` is resolved through `Options.choreLockRef`, which is `None` for every repo without a lock
    /// issue — the six receivers today. Absent ⇒ no offer, and that is ADR-0041's contract verbatim: a
    /// chore queue that cannot find its lock must offer nothing. It is the honest state rather than a gap.
    ///
    /// It does NOT execute the chore, and there is no verb here that could. The tool has no thread — that
    /// is the premise of the whole design (§4.6) — so the AGENT is the thread, and this hands it the work.
    /// Nothing re-checks that the agent complied, because nothing needs to: condition 2 is discharged by
    /// derivation, not by a report. `Chore.isRetired` is the re-check, and it is the NEXT caller's
    /// question, asked of a fresh board.
    val offer:
        transport: Transport.IGitHubTransport ->
        boundary: Chore.Boundary ->
        worker: Types.WorkerId ->
        self: Writes.SelfIdentity ->
        session: Types.SessionId option ->
        extra: Types.Ref list ->
        owner: string ->
        repo: string ->
        observed: Chore.Board ->
            (Chore.Chore * Types.Ref) option

    /// RENDER AN OFFER for a human worker — the `Statement` the chore already carries, plus what holding
    /// the lock obliges.
    ///
    /// It prints `chore.Statement` rather than re-describing the condition, so the sentence the worker
    /// reads and the fact `derive` tested are the SAME VALUE. A second spelling of "what is wrong here"
    /// is #485's shape — one rule computed in two places, agreeing at first and drifting later — and it
    /// would drift toward whatever the renderer's author assumed, which is the direction that invents
    /// findings.
    val render: chore: Chore.Chore -> lockRef: Types.Ref -> string
