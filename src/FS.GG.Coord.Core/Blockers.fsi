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
