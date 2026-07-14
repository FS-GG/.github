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

        /// **THE ONE COMMAND THAT PERFORMS IO.** Read the board, and emit the snapshot `decide` consumes.
        ///
        /// ADR-0034 deferred the IO adapter and said what it was for: *"it is required only for the Phase 3
        /// flip, when the engine must fetch its own state."* Until this command existed the typed engine was
        /// a decision procedure with no way to observe the thing it decides about — bash was the only thing
        /// that could produce a snapshot for it, which is precisely why bash could not be deleted.
        ///
        /// `fsgg-coord-engine scan | fsgg-coord-engine decide` is a complete scheduling pass with no bash
        /// anywhere in it.
        | Scan

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
          SnapshotFile: string option

          /// `scan --repo NAME`: restrict the candidates to one repository.
          ///
          /// Touch-set tokens are REPO-RELATIVE, so mixing repos in one batch invents collisions between
          /// files that are not the same file (#353). The scheduler's caller owns that boundary, and this
          /// is where it is drawn.
          Repo: string option

          /// `scan --fresh`: bypass the 90-second scan cache.
          Fresh: bool

          /// `scan --include-backlog`: let the batch fall back to Backlog when no Ready item is startable.
          ///
          /// Pretending otherwise is how a full queue read as an empty one (#440).
          AllowBacklog: bool

          /// `scan -n N`: cap the batch.
          Limit: int option

          /// `scan --lease MINUTES`: the claim lease. Travels with the SNAPSHOT because it is configurable
          /// in the client — an engine that hard-coded 120 would tell every worker to wait out a window that
          /// has already closed.
          LeaseMinutes: int }

    /// The documented default (`FSGG_CLAIM_LEASE_MIN`).
    [<Literal>]
    val DefaultLeaseMinutes: int = 120

    /// Parse argv. `Error` carries a message already fit to print.
    val parse: args: string list -> Result<Options, string>

    val usage: string
