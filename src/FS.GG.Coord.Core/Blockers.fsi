namespace FS.GG.Coord

/// The `Blocked by` rule. ONE definition.
///
/// It was written out by hand in FOUR places in the bash client, and after #476 fixed two of them,
/// TWO STILL CARRIED THE PRE-#476 FORM — so `.blocked` computed false while the display and the
/// "nothing is startable" diagnostic both named a MERGED pull request as the reason, sending workers
/// to go and look at finished work (#520). A rule spelled out in N places agrees in N-1 at best.
module Blockers =

    open Types

    /// Is this blocker RESOLVED — i.e. does it no longer hold the item?
    ///
    /// Resolved iff CLOSED or MERGED.
    ///
    /// `Merged` is the case that matters. `Blocked by` may name a PULL REQUEST, whose state is
    /// OPEN | CLOSED | MERGED — so a rule that clears only on CLOSED unblocks when the PR is
    /// ABANDONED and blocks forever once it is FINISHED. The gate opened precisely when the blocking
    /// work was thrown away, and shut precisely when it was done (#476).
    ///
    /// `Unknown` and `Unparseable` BLOCK. "I could not look" is not "I looked and it is fine" (#266):
    /// an unresolvable blocker is the safe direction, and it is the only safe direction.
    val isResolved: blocker: Blocker -> bool

    /// The same rule, asked of a bare `BlockerState` — for callers that hold a STATE rather than a
    /// `Blocker`, and must not answer it themselves.
    ///
    /// `isResolved` is this function applied to `blocker.State`, not a second decision: the resolution
    /// rule is spelled ONCE and everything else asks. The pair exists because `Protocol.blockerStates`
    /// documents whether each state HOLDS, and it has no `Blocker` to hand — only a case. Without this
    /// it would have hand-typed the five answers into the DOC, which is the copy the doc exists to end
    /// (#865, and #916's trap 1: generation makes copies agree, it does not make them true).
    val isResolvedState: state: BlockerState -> bool

    /// The blockers still holding this item. Empty = not blocked.
    val unresolved: blockers: Blocker list -> Blocker list

    /// **DID EVERY RECORDED BLOCKER CLEAR — the `BLOCKER-CLEARED` precondition (.github#1738).**
    ///
    /// `true` iff there is at least ONE blocker and every one of them `isResolved`. The emptiness guard is
    /// the content: `List.forall` over `[]` answers TRUE, so without it a row that never recorded a blocker
    /// would read as one whose blockers have all CLEARED — and #620's remedy is about a `Blocked` row whose
    /// dependencies finished, not about one that never had any. (`.github#1689` and `#1737` are both
    /// `Blocked` with an empty `Blocked by`; #602 is the incident that shape already caused.)
    ///
    /// **IT IS A `val` BECAUSE TWO PROJECTS MUST AGREE ABOUT IT EXACTLY, NOT BECAUSE IT IS LONG.**
    /// `Chore.choresFor` FIRES `BLOCKER-CLEARED` on this condition; `Scan` must PROBE the same population
    /// for `Item.ItemPr`, the fact that rule's #1738 gate reads. If the two ever disagree — Scan narrower
    /// than Chore — the gate silently stops seeing its subject and goes green over the promotion it exists
    /// to refuse, which is #266's shape re-armed inside the fix for it. A copy in each project is what
    /// #1012 measured: two spellings pointing opposite ways, with 775 tests passing over the disagreement.
    val cleared: blockers: Blocker list -> bool

    /// EVERY SET OF ITEMS MUTUALLY DEADLOCKED BY `Blocked by` — the question no per-item rule can ask.
    ///
    /// Each returned group is a set in which every member sits on a ring, so **no member can EVER become
    /// startable**: each waits on the next around a circle, and no lease, no merge and no amount of waiting
    /// will free any of them. A human must break an edge. `[]` when the graph is acyclic.
    ///
    /// THIS IS NOT A PER-ITEM FACT, AND THAT IS THE WHOLE POINT. The blocker graph has been repaired four
    /// times — #343 (a blocker naming an OPEN issue, handed out anyway), #476 (a ref naming a PR never
    /// clears), #602 (`Blocked` with an EMPTY blocker list), #620 (blockers ALL CLOSED, invisible to
    /// everything) — and every one of those rules inspects ONE item's blockers in isolation. A cycle passes
    /// all four, because **every item in a ring is individually, locally, perfectly well-formed**: non-empty
    /// blocker list, every blocker OPEN, every ref a real issue, correctly never handed out. The defect
    /// exists only in the GRAPH, and no per-item rule has a graph to look at. It is also why no WORKER can
    /// see one: each edge is drawn by a different worker from locally correct information, and the ring is
    /// visible only from above (#1092).
    ///
    /// The org already ratified the rule for the other graph: `Done.fs` refuses to climb a parent chain past
    /// ten hops because "a cycle is a bug — this is a cycle, not a hierarchy". The graph the engine BUILDS is
    /// guarded; the graph we ask humans to maintain BY HAND, and which gates scheduling, was not.
    ///
    /// AN EDGE IS AN UNRESOLVED BLOCKER POINTING AT A NODE IN `nodes`. Both halves fail CLOSED (#266):
    ///
    /// - **Resolution is `isResolved`'s call, never re-answered here** — a resolved blocker no longer holds,
    ///   so it cannot be part of a LIVE ring. A rule spelled in two places agrees in one at best (#520).
    /// - **A blocker naming an item OUTSIDE `nodes` draws no edge.** We cannot see whether that item is on a
    ///   ring, and "I could not look" is not "I looked and it is fine". No edge, no claimed cycle — this
    ///   under-reports rather than inventing a deadlock out of a board we only half hold.
    ///
    /// TOTAL and PURE: it reads nothing, terminates on any graph (including one that is entirely one ring),
    /// and cannot mistake a failed read for an acyclic board. Duplicate nodes collapse (first wins); each
    /// group is sorted, as is the result, so the output is deterministic and testable.
    ///
    /// A group may be larger than one simple ring — two rings sharing an item are ONE mutually-deadlocked
    /// set, and reporting them together is the honest answer: every member is still stuck, and breaking one
    /// edge may not free all of them. A group is never a singleton unless that item blocks ITSELF.
    val cycles: nodes: (Ref * Blocker list) list -> Ref list list

    /// Why a `Blocked by` WRITE was refused. The field is a TYPED dependency edge (Projects v2 has no
    /// dependency field, so it is TEXT and nothing but this gate stops it drifting back into a resolution
    /// log), so `set-field <issue> 'Blocked by' <value>` accepts only issue refs — and the two ways to get
    /// it wrong want two different corrections.
    type BlockedByRefusal =
        /// A placeholder (a run of hyphens, an em/en dash, or `none` / `n/a` / `tbd` / `todo`,
        /// case-insensitive — bash's `canon_blocked_by` set) — the caller is trying to say "no blocker"
        /// with a token, not by clearing the field. The correction is to clear it (`'Blocked by' ''`).
        | Placeholder
        /// Prose, not issue refs (a delivery log, an inverted `blocks X` edge, or a ref trailed by prose).
        /// If the caller means the item ITSELF is blocked, that is a `Status`, not a dependency edge.
        | NotIssueRefs

    /// Canonicalize a `Blocked by` field value to `owner/repo#n[, owner/repo#n …]`.
    ///
    /// `defaultOwner`/`defaultRepo` are the BLOCKED item's own owner/repo, so a bare `#n` adopts BOTH and a
    /// `repo#n` adopts the owner — every accepted form (`owner/repo#n`, `repo#n`, `#n`, an issue URL)
    /// reduces to one canonical `owner/repo#n`, and refs that canonicalize alike are de-duped (first
    /// occurrence wins, order preserved). Every token must be a ref: prose in a dependency field is not a
    /// dependency (`Error NotIssueRefs`), and the `-`/`none` placeholder is refused toward clearing
    /// (`Error Placeholder`). An empty / whitespace value is `Ok None` — the caller clears the field.
    ///
    /// PURE, so the write can be validated BEFORE any board read — a refused value spends no GraphQL, the
    /// budget that dies first.
    val canonicalizeBlockedBy:
        defaultOwner: string -> defaultRepo: string -> raw: string -> Result<string option, BlockedByRefusal>
