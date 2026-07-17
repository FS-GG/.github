namespace FS.GG.Coord

/// The `Paths:` touch-set: parsing, matchability, and disjointness.
///
/// ONE GRAMMAR, ONE PARSER — and as of ADR-0040 Phase D.4 that is a FACT, not a goal. This module is
/// the only implementation left. The grammar once had four — `paths_from_body`, `declares_no_touchset`,
/// `lint`'s jq `touchset_decl`, and the `check-board` skill's `test("(?m)^Paths:")` grep — and they
/// disagreed, which is exactly the shape #485 named one level up ("is this item startable? computed in
/// five places, agrees in none"). The loosest was not fence-aware, was case-sensitive, and counted the
/// `Paths: none` sentinel as a declaration; the strictest refused tokens the others accepted. D.4 was
/// the one-way door: the bash monolith and the differential gates that shadowed it are gone.
///
/// So there is no second implementation to be caught disagreeing with, and the safety net that USED to
/// catch a parse error here went with it. That is not a reason for less rigour in this file; it is the
/// reason for more. #863 is what the gap looks like — a repeated `Paths: none` parsed as a path called
/// `none`, and nothing in the pipeline was left to notice.
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
    ///
    /// The `Paths: none` sentinel is decided over the UNIONED TOKEN SET, not the raw text: every token
    /// being the sentinel is `DeclaredNone`, whether it was declared once or five times. Testing the
    /// concatenation instead is #863 — two bare `Paths: none` lines make `"none none"`, which is not
    /// the sentinel, so `none` parsed as a PATH and the item became startable against a directory that
    /// does not exist. A `none` mixed WITH real paths is a contradiction and is `Unmatchable`: it is
    /// refused, never silently unioned in. This promise of a UNION is what made #863 reachable, so it
    /// is the promise that has to state the rule.
    val parse: body: string -> TouchSet

    /// Is this token the `none` SENTINEL — `Paths: none`, in any case, with any surrounding space?
    ///
    /// The sentinel is part of the grammar, so it is decided HERE, once. `Writes.validate` must tell a
    /// declaration that reserves nothing DELIBERATELY (the sentinel — an epic, a decision item) from one
    /// that reserves nothing BY MISTAKE (an unmatchable token, #273). Those are opposite verdicts on
    /// inputs one character apart, and a second implementation of the test is a second place for it to
    /// rot — #485's shape inside its own remedy.
    val isSentinel: token: string -> bool

    /// Is this token one the matcher can actually reserve?
    ///
    /// Not a glob language. Exact paths, directory prefixes, and a TRAILING `/**` or `/*`. A leading
    /// `**/` — or a `*` in the middle — matches nothing, and a token that matches nothing CONFLICTS
    /// WITH NOTHING: it would read as DISJOINT against every other worker, which is ADR-0021's own
    /// failure one level down (#273). So it is refused everywhere, never tolerated.
    ///
    /// The `none` SENTINEL is not a path either, and is `Unmatchable` here (#863). `parse` answers
    /// `DeclaredNone` before it asks, so a `none` that reaches this function stands beside real paths —
    /// a declaration that says both "I touch nothing" and "I touch src/A". It is path-shaped, so
    /// calling it `Matchable` would reserve a `none` directory that exists nowhere and therefore
    /// collides with no one: startable AND conflict-free with everything, which is #273's fail-open
    /// arriving through the very parser built to end it.
    val classify: token: string -> PathToken

    /// The tokens that can never match a file.
    val unmatchable: touchSet: TouchSet -> string list

    /// Is a DECLARED touch-set usable at all?
    ///
    /// THE `every`/`some` DISTINCTION IS PART OF THE RULE, NOT OF ITS RENDERING (#945). This type
    /// carried one `Unusable` case until `lint` — the third copy — was migrated onto it. `lint` needs
    /// the distinction (it renders a different sentence for each, and #646 exists because the PARTIAL
    /// case is the more urgent one), so a two-case type forced `lint` to keep its own `List.forall`
    /// beside the ask. That is the shape the whole exercise is about: a caller that has to re-derive
    /// half the answer has not stopped deciding, and its half is free to drift.
    ///
    /// So the type carries the distinction and callers COLLAPSE it where their verdict honestly does
    /// not differ. Collapsing at the call site is not the same as re-deriving at the call site: an
    /// added case breaks the match, where a missing threshold just goes quietly wrong.
    type Usability =
        /// Every token can match a file: the declaration reserves exactly what it names.
        | Usable
        /// NO token can match a file. `tokens` is those tokens, and it is never empty. The item is as
        /// dead as one with no touch-set at all — and it is the LESSER of the two failures, because
        /// nothing about it looks declared.
        | AllUnmatchable of tokens: string list
        /// SOME token can match a file and at least one cannot. `tokens` is only the unmatchable ones,
        /// and it is never empty. This is the WORSE case (#646): the item looks declared and its
        /// matchable tokens reserve work, while each unmatchable one silently reserves NOTHING — so the
        /// files it names are invisible to every other worker's overlap check.
        | SomeUnmatchable of tokens: string list

    /// THE USABILITY RULE. It lives here, and `Schedulability`, `Lanes` and `lint` ask it rather than
    /// deciding it (#864, #945).
    ///
    /// "ONCE" IS NOW A FACT, and it was a goal for exactly as long as `lint` kept its own copy. That
    /// copy AGREED — #646 had taught it the partial case — and agreeing is not asking: it is what
    /// `Schedulability` and `Lanes` were doing right up until they drifted into opposite verdicts on
    /// the same item, each pinned by a green test asserting the negation of the other's (#864).
    ///
    /// ANY unmatchable token disqualifies the whole touch-set. Not "every token" — ANY. A partly-dead
    /// declaration is the WORSE case, not the lesser one: the item LOOKS declared, and the dead tokens
    /// silently reserve nothing, so the files they name are invisible to every other worker's overlap
    /// check. Scheduling it is entering the collision the protocol exists to prevent, voluntarily.
    ///
    /// THE THRESHOLD IS THE WHOLE POINT OF THIS FUNCTION EXISTING. `unmatchable` hands back a list, and
    /// a list leaves every caller to decide how long it has to be — which is exactly how `Schedulability`
    /// (refuse on ≥1) and `Lanes` (lane on ≥1 MATCHABLE) came to answer the same question with opposite
    /// verdicts, each pinned by a green test asserting the negation of the other's (#864). That is #485's
    /// shape — "is this item startable?" computed in five places, agreeing in none — reproduced inside
    /// the typed core ADR-0034 built to end it. A caller that asks THIS cannot drift, because there is no
    /// threshold left for it to get wrong.
    ///
    /// `Undeclared`, `DeclaredNone` and `Unreadable` have no tokens, so they have no unusable ones and
    /// answer `Usable` — which is NOT a claim that they are schedulable or lanable. They are neither, for
    /// their own reasons, and every caller decides those three shapes BEFORE it asks this (#496: an
    /// omission, a decision, and an unread body are three different facts and must never be conflated).
    val usability: touchSet: TouchSet -> Usability

    /// The token with its trailing `/**` or `/*` taken off — the SUBTREE it actually names. This is the
    /// form a collision is REPORTED in, not merely matched in: `src/Off/Sub/**` and `src/Off/Sub` are one
    /// subtree, and printing the raw suffix beside a reservation that has none reads as two different things.
    val stem: t: string -> string

    /// Do these two tokens overlap?
    ///
    /// Exact equality OR subtree containment, in EITHER direction — file-existence-independent, and
    /// deliberately CONSERVATIVE: it errs toward reporting overlap. `src/Scene` contains
    /// `src/Scene/Types.fs`, so declaring the parent reserves the child exactly as effectively as
    /// naming it. (Which is the trap behind #309: declaring a generated artifact's PARENT directory
    /// reserves the artifact against the whole board.)
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
