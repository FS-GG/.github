namespace FS.GG.Coord

/// Pure normalization for repository scope tokens shared by command input and board rows.
module RepoScope =

    /// The one thing a normalized `Repo Scope` token can be, published so the compiler — not a
    /// consumer's memory — enumerates every place that reads it.
    ///
    /// `cross-repo` (`docs/coordination/board-schema.md`: "the one deliberate non-roster value") is the
    /// board's only `Repo Scope` value that never names a repository. Every other spelling — a roster
    /// short-id, an `owner/repo`, or a literal name — resolves to a genuine repository name and is safe
    /// to compare against one, reserve a touch-set path under, or select a claim lock with.
    ///
    /// Deliberately has NO passthrough/`Unknown` arm a consumer can treat as a repository by accident —
    /// the same property `DeliveryRoute.Verdict` relies on. A caller that wants the raw resolved string
    /// regardless of which arm applies states that explicitly via `orFallback`, never through a wildcard.
    type Scope =
        /// A rostered repository name. The only arm that may be compared against a repository name,
        /// reserved in a touch-set, or used to select a claim lock.
        | Repository of name: string
        /// A deliberate non-repository board value (`cross-repo`). Never a repository — a `Paths:`
        /// reservation, claim lock, or lane test that substitutes this for one has already reintroduced
        /// the erasure this type exists to make unrepresentable.
        | NonRepository of token: string

    /// Map roster short ids and owner/repo spellings to the canonical repository name, or classify a
    /// raw board token as non-repository. `raw`'s casing is ignored; the returned `Repository`/
    /// `NonRepository` payload preserves the caller's original spelling (so a `NonRepository` token
    /// round-trips byte-identical to what the board carried).
    val resolve: raw: string -> Scope

    /// The established fallback POLICY (`.github#2351`'s `pathRepoOrFallback`, promoted here so both
    /// `Core` and `Cli` reach it): a scope that does not name a repository behaves exactly like an
    /// absent one and yields `fallback` instead — normally the item's own hosting repository. States
    /// which arm applies explicitly (an exhaustive two-arm match, not `Option.defaultValue` over a
    /// bare string, which is what let `cross-repo` substitute for a repository in the first place).
    val orFallback: fallback: string -> scope: Scope -> string
