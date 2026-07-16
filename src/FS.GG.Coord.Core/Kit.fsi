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

    /// The kit sources a touch-set NAMES — the obligation a worker is about to take on, read off the
    /// declaration rather than off the tree.
    ///
    /// **THIS IS NOT THE INFERENCE #563 REMOVED, AND THE DIFFERENCE IS THE WHOLE REASON IT MAY EXIST.** The
    /// old warning asked the declaration whether the obligation was MET (*"is `registry/repos.yml` in your
    /// touch-set?"*) and fell silent when it was named — so declaring it marked the obligation satisfied
    /// while `repos.lock` went stale, which is the fail-open #563 closed by recomputing the digest instead.
    /// That rule stands, and it is about SATISFACTION: a declaration can never prove the lock is current,
    /// and `staleSources` remains the only thing that says so.
    ///
    /// This asks a different question, and one only the declaration CAN answer: *which kit sources is this
    /// worker about to edit?* At claim time nothing is edited yet, so there is no digest to compare and
    /// `staleSources` is silent by construction — which is exactly why the obligation went unnamed until a
    /// red `main` (#469, #509). The answer here can only ADD a warning; it can never suppress one, so it
    /// cannot fail open the way its ancestor did.
    ///
    /// Overlap is `TouchSet.tokensOverlap` — exact equality or subtree containment in EITHER direction — so
    /// `.claude/skills/pnext-item/**` names the kit source `.claude/skills/pnext-item`, and so does a bare
    /// parent like `.claude/skills`. That is the same conservative rule the scheduler reserves by (#309), and
    /// erring toward naming an obligation the worker may not incur is the safe direction: the cost is a line
    /// of advice, and the cost of missing it is a red `main`. Order follows the lock.
    val declaredSources: lock: (string * string) list -> declared: string list -> string list

    /// A skill kit carries the BYTE-IDENTICAL union across its two roots (ADR-0011/0014); a divergence reds
    /// the `roots` gate. Given, per skill name, the two roots' `SKILL.md` bytes (`None` = that root is
    /// missing the file), the names whose roots are NOT byte-identical — a missing mirror counts as
    /// diverged. Order follows the input.
    val divergedRoots: roots: (string * byte[] option * byte[] option) list -> string list
