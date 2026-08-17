namespace FS.GG.Coord.Cli

/// The `diff-audit` command's PROCESS BOUNDARY: the only place the engine shells out to `git` to
/// read source bytes, and therefore the seam whose failure modes have to be stated rather than
/// discovered.
///
/// The pure inventory, classification, threshold and validation logic all belong to
/// `FS.GG.Coord.SemanticDiff`. This module owns exactly three impure things — running `git show`
/// against two immutable commits, reading the optional receipt and item-body files from disk, and
/// choosing the exit code — and exports only the command entry point, so no caller can reach the
/// process seam directly or grow a second spelling of it.
module SemanticDiffApplication =

    /// Run `diff-audit <base> <head> <old-token> <new-token> [receipt.json|-] [item-body.md]`
    /// with a non-empty `--paths P...`, printing the typed receipt as JSON on stdout.
    ///
    /// READS TWO IMMUTABLE COMMITS, NOT THE WORKING TREE. Every declared path is fetched with
    /// `git show <sha>:<path>` from both `base` and `head`, so the audit describes what those
    /// commits contain and is unaffected by uncommitted local edits.
    ///
    /// A FAILED GIT READ IS AN ERROR, NEVER AN EMPTY AUDIT. If either `git show` fails for any
    /// declared path — bad sha, path absent at that commit, not a repository — the whole call
    /// refuses with that path named. It never degrades to "no occurrences found", which would be
    /// indistinguishable from a clean audit and is the fail-open shape this receipt exists to
    /// prevent.
    ///
    /// THE ITEM BODY IS CONSUMED AS BYTES FROM A FILE, never from caller process memory or
    /// environment. Requiredness is derived from that captured body, the immutable head commit's
    /// own message, and the occurrence count against the threshold; a caller cannot assert it.
    /// Fetching the live body from the named item is the driver's job, not this command's.
    ///
    /// THE THRESHOLD comes from `FSGG_DIFF_AUDIT_THRESHOLD` and defaults to 5. It must parse as a
    /// non-negative integer: any other value is refused rather than silently falling back to the
    /// default, because a typo that disabled the audit would leave a green exit behind it.
    ///
    /// A SUPPLIED RECEIPT SUPPLIES DISPOSITIONS AND NOTHING ELSE. The occurrence inventory is
    /// always re-derived here from the two commits; the receipt's rows are matched by id and only
    /// their `Disposition` is adopted. A receipt whose base, head or declared paths disagree with
    /// this invocation is refused as stale, and one whose rows are missing, duplicated, or not in
    /// one-to-one correspondence with the live inventory is refused as incomplete — so a receipt
    /// can never shrink, extend or restate the set of occurrences that must be accounted for. The
    /// literal `-` in the receipt position means "inventory only", with no dispositions applied.
    ///
    /// EXIT CODES. `0` when the audit is not required, or is required and every occurrence
    /// validates. `3` when the audit is required and validation leaves findings — the distinct code
    /// that lets a caller tell an unresolved audit from a broken invocation. `1` for a usage error,
    /// an unreadable input, a stale or incomplete receipt, or a failed git read.
    val run: opts: Options.Options -> int
