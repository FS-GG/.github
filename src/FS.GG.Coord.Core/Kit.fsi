namespace FS.GG.Coord

/// The kit-digest obligation, as PURE LOGIC (#469/#563/#588).
///
/// `registry/repos.lock` pins a content digest of every kit source (ADR-0019, #527) — editing one and
/// not relocking reds `main`. The warning that names that obligation used to INFER it from what a worker
/// declared ("is `registry/repos.yml` in your touch-set?"), which FAILED OPEN after #527 moved the
/// digests out of the authored `repos.yml` into the generated `repos.lock`: declaring `repos.yml`
/// silenced the warning while the lock was still stale. A DECLARATION is not the obligation; a MATCHING
/// DIGEST is. So the tool no longer infers it — it recomputes the digest and looks. This module is the
/// looking, factored from the file IO so the comparison itself is unit-tested and has one home (#485).
module Kit =

    /// Parse `registry/repos.lock`: each non-comment line is `<sha256>  <src>`. Blank lines, `#`-comment
    /// lines, and a line with no source field are skipped (bash's `case "$want" in ''|'#'*) continue` and
    /// `[ -n "$src" ] || continue`). Returns `(expected-digest, source-path)` in file order.
    val parseLock: content: string -> (string * string) list

    /// The kit sources whose ACTUAL content digest differs from the lock. `resolve src` returns the digest
    /// computed off the tree, or `None` when the entry names a file this tree does not carry — which is not
    /// a staleness but simply "not here" (bash's `[ -f "$path" ] || continue`), so a receiver that mirrors
    /// the kit but not a given source is never nagged about it. Order follows the lock.
    val staleSources: resolve: (string -> string option) -> lock: (string * string) list -> string list

    /// A skill kit carries the BYTE-IDENTICAL union across its two roots (ADR-0011/0014); a divergence reds
    /// the `roots` gate. Given, per skill name, the two roots' `SKILL.md` bytes (`None` = that root is
    /// missing the file), the names whose roots are NOT byte-identical — a missing mirror counts as
    /// diverged. Order follows the input.
    val divergedRoots: roots: (string * byte[] option * byte[] option) list -> string list
