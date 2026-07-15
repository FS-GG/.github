namespace FS.GG.Coord

/// The `Paths:` touch-set: parsing, matchability, and disjointness.
///
/// ONE GRAMMAR, ONE PARSER. Today it has four — `paths_from_body`, `declares_no_touchset`, `lint`'s
/// jq `touchset_decl`, and the `check-board` skill's `test("(?m)^Paths:")` grep — and they disagree,
/// which is exactly the shape #485 named one level up ("is this item startable? computed in five
/// places, agrees in none"). The loosest of the four is not fence-aware, is case-sensitive, and
/// counts the `Paths: none` sentinel as a declaration; the strictest refuses tokens the others accept.
module TouchSet =

    open Types

    /// Parse the touch-set out of an issue body.
    ///
    /// FENCE-AWARE, and that is load-bearing (#277). A `Paths:` line inside a fenced code block is a
    /// QUOTATION, not a declaration — protocol docs are full of examples, and an item that "declares"
    /// its touch-set only inside a fence has declared nothing. The rule the org settled on is
    /// "unschedulable beats mis-scheduled": a quoted declaration reserves nothing, and a token that
    /// reserves nothing conflicts with nothing, which is how two workers end up in one file.
    ///
    /// Also: up to three leading spaces (a list-indented line is still a line), either case, backticks
    /// stripped (#435 — a backticked declaration was refused as unmatchable and the item silently
    /// never scheduled), commas or spaces as separators, and multiple bare declarations UNIONED.
    val parse: body: string -> TouchSet

    /// Is this token one the matcher can actually reserve?
    ///
    /// Not a glob language. Exact paths, directory prefixes, and a TRAILING `/**` or `/*`. A leading
    /// `**/` — or a `*` in the middle — matches nothing, and a token that matches nothing CONFLICTS
    /// WITH NOTHING: it would read as DISJOINT against every other worker, which is ADR-0021's own
    /// failure one level down (#273). So it is refused everywhere, never tolerated.
    val classify: token: string -> PathToken

    /// The tokens that can never match a file.
    val unmatchable: touchSet: TouchSet -> string list

    /// Do these two tokens overlap?
    ///
    /// Exact equality OR subtree containment, in EITHER direction — file-existence-independent, and
    /// deliberately CONSERVATIVE: it errs toward reporting overlap. `src/Scene` contains
    /// `src/Scene/Types.fs`, so declaring the parent reserves the child exactly as effectively as
    /// naming it. (Which is the trap behind #309: declaring a generated artifact's PARENT directory
    /// reserves the artifact against the whole board.)
    /// The token with its trailing `/**` or `/*` taken off — the SUBTREE it actually names. This is the
    /// form a collision is REPORTED in, not merely matched in: `src/Off/Sub/**` and `src/Off/Sub` are one
    /// subtree, and printing the raw suffix beside a reservation that has none reads as two different things.
    val stem: t: string -> string

    val tokensOverlap: a: string -> b: string -> bool

    /// Is `file` covered by `token`? THE verify-paths containment rule, matching the bash client byte for
    /// byte: strip the trailing `/**`, `/*`, or `/` (the grammar's only wildcard), then the file is covered
    /// iff it EQUALS that directory prefix or lies under it (`prefix/…`).
    ///
    /// An `Unmatchable` token reserves nothing, so it covers nothing (#273): a token that can match no file
    /// cannot vouch for one either. This is the same asymmetry the scheduler relies on, in one place.
    val covers: token: PathToken -> file: string -> bool

    /// The tokens two touch-sets share. Empty = they may run in parallel.
    ///
    /// CONTRACT: both token lists must come from the SAME repo. Tokens are repo-relative, so handing
    /// this cross-repo lists invents collisions that do not exist (#353). The caller owns that.
    val conflicts: a: TouchSet -> b: TouchSet -> (string * string) list
