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

    /// The blockers still holding this item. Empty = not blocked.
    val unresolved: blockers: Blocker list -> Blocker list

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
