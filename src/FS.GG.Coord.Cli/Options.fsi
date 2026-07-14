namespace FS.GG.Coord.Cli

/// Argument parsing, and the residue rule.
///
/// EVERY CONSUMED OPTION IS DECLARED HERE, so that anything else can be REJECTED. The org learned this
/// the expensive way in SDD, where `init --project-root /tmp/b` silently seeded the current directory
/// and then reported success: an argument that is ignored is indistinguishable, from the caller's side,
/// from an argument that was honoured. A parser that shrugs at an unknown token is the same fail-open
/// shape as a gate that reports green over a subject it never read (#266) — the caller asked for
/// something, got a confident answer, and the answer was about something else.
///
/// So: an unknown token is NAMED and refused. A flag given without its value is refused rather than
/// swallowing the NEXT flag as its argument.
module Options =

    /// What the engine was asked to do.
    type Command =
        /// Decide a batch from a board-state snapshot on stdin. The command the shadow uses — the engine
        /// reads NOTHING for itself (see `Snapshot`).
        | Decide

        /// Fold the fleet divergence ledger into ADR-0034 §5's cut-over verdict (#634). Reads a ledger
        /// document on stdin; like `Decide`, it fetches nothing — not the board, not the ledger, and not
        /// even the clock (see `Fleet`).
        | FleetVerdict

        /// Partition the board into lanes — sets of work that can never contend (#428, #485). DERIVED
        /// from the touch-sets, never asserted: safety is computed by the same `TouchSet.conflicts` the
        /// scheduler reserves against, so a lane cannot disagree with the batch about what collides.
        | LanesView
        | Facts

        | Help
        | Version

    /// How to render the answer.
    ///
    /// `Json` is the CONTRACT and always wins: it is what the bash client parses, and it is
    /// byte-stable. `Text` is a projection of the same answer for a human at a terminal, and it
    /// carries no contract — it may add or drop nothing, but nothing may parse it.
    type Render =
        | Json
        | Text

    type Options =
        { Command: Command
          Render: Render

          /// Read the snapshot (or the ledger) from this file instead of stdin. Testing and debugging
          /// only; the shadow always pipes, because a temp file is one more thing that can be stale.
          SnapshotFile: string option }

    /// Parse argv. `Error` carries a message already fit to print.
    val parse: args: string list -> Result<Options, string>

    val usage: string
