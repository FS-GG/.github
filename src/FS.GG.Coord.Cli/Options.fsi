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
        /// Decide a batch from a board-state snapshot on stdin. The only command the shadow uses, and
        /// the only one that exists at Phase 2 — the engine reads NOTHING for itself (see `Snapshot`).
        | Decide

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

          /// Read the snapshot from this file instead of stdin. Testing and debugging only; the shadow
          /// always pipes, because a temp file is one more thing that can be stale.
          SnapshotFile: string option }

    /// Parse argv. `Error` carries a message already fit to print.
    val parse: args: string list -> Result<Options, string>

    val usage: string
