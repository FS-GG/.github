namespace FS.GG.Coord.Cli

/// Pure JSON boundary for resumable roadmap/workspace cycle ledgers.
///
/// The ledger model, the transitions and every legality rule belong to `FS.GG.Coord.CycleLedger`.
/// This module owns the boundary around them: decoding one snapshot document, running the provider
/// validators that make a supplied artifact trustworthy, keeping the durable update journal, and
/// rendering one transition.
///
/// VALIDATOR AUTHORITY TRAVELS WITH THE ENGINE, NEVER WITH THE ARTIFACT (.github#2133 repair-phase
/// round 1). This is the module's central safety property and it is not visible from the types.
/// Artifact roots are CALLER DATA, and caller data must never select executable code. So the
/// `critique` and `feedback` adapters run validator scripts resolved beside the engine binary and
/// pinned by SHA-256: a validator that has been replaced, or is missing, fails closed with its
/// observed digest rather than running. A caller cannot point this command at a script of its own.
module CycleLedgerApplication =

    /// Run `cycle <inspect|register|advance|update|complete>` over a ledger snapshot supplied on
    /// `--snapshot FILE` or stdin, printing one `fsgg.coord.cycle-ledger/1` verdict.
    ///
    /// EXACTLY ONE ACTION ARGUMENT, from that closed set. Zero, two, or an unrecognised word is a
    /// refusal naming the five accepted actions rather than a default action being chosen for the
    /// caller.
    ///
    /// EVERY REFUSAL IS `ExitCode.Error` ON STDERR, prefixed `fsgg-coord-engine: cycle: `, and every
    /// success is `ExitCode.Green`. Decode failures, illegal transitions, validator refusals and
    /// path-escape refusals all arrive through that one channel: the command never partially
    /// succeeds, and never prints a transition it did not fully validate.
    ///
    /// AN ARTIFACT PATH MUST RESOLVE BENEATH `rootPath`. It is resolved absolutely and then made
    /// relative again; anything that escapes — `..`, or an absolute path elsewhere — is refused.
    /// Combined with the pinned-validator rule above, this is what stops a supplied ledger reaching
    /// outside the workspace it describes.
    ///
    /// `fsgg-sdd` ARTIFACTS ARE VETTED BY VERSION, AND AN UNLISTED VERSION FAILS CLOSED with the
    /// exact `toolVersion` it reported. The artifact must sit at `readiness/<workId>/verify.json`,
    /// the validator must confirm the `verify` command bound to that same work id, and the report
    /// must be both `coherent` and `noChange` — a verification that merely ran is not evidence that
    /// the provider view is byte-current. Advancing the accepted-version list is a deliberate,
    /// reviewed act; a validator bump that outruns it surfaces here as one specific, actionable
    /// refusal rather than as unrelated downstream failures.
    ///
    /// `complete` REQUIRES RECEIPTS THIS ENGINE ACTUALLY ISSUED. Each guarded update receipt must
    /// appear in the durable journal; a receipt that is merely well-formed is refused. Without that
    /// check a caller could author its own completion evidence, which would make the journal
    /// decorative. The journal lives at `$FSGG_CYCLE_JOURNAL`, or at git's own
    /// `fsgg-cycle-journal.json` path when that variable is unset.
    ///
    /// `update` MINTS THE NONCE ITSELF and appends before rendering, replacing any prior receipt for
    /// the same cycle. The write is atomic — a temporary file moved into place — so an interrupted
    /// run leaves either the old journal or the new one, never a truncated one that would strand
    /// every later `complete`.
    val run: Options.Options -> int
