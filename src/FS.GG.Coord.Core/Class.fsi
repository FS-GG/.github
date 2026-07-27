namespace FS.GG.Coord

/// The `Class:` body-line sentinel (.github#1588) — *how bad*, in the same grammar as `Paths:` and
/// `Blocked on:`. See ADR-0066.
///
/// **THE BODY IS THE AUTHORITY AND THE BOARD FIELD IS THE PROJECTION, AND THAT DIRECTION IS AN ADR, NOT
/// A PREFERENCE.** #1588's own prose says the `Blocked on: human/decision` sentinel "becomes derivable
/// FROM the field"; its acceptance criteria say the opposite — the sentinel and a `[decision]` title
/// prefix are *evidence*, and "if a row's class is inferable from a label, a title convention or a
/// sentinel, derive it there". The criteria win, and not merely because criteria win: ADR-0045 already
/// decided this exact axis for `HumanBlock`, REJECTING a Projects v2 field in favour of a body line on
/// the grounds of board-schema churn and `lint`'s ability to enforce a body line cheaply. Making the
/// field the authority would silently reverse an Accepted ADR — and would do it by rewriting ~50 issue
/// bodies, which is not a change anybody could review.
///
/// So this module reads. Nothing here writes a body, and `reconcile` projects what it reads onto the
/// board field. A board field nobody derives is the fourth hand-maintained copy AC5 forbids.
module Class =

    open Types

    /// **THE CLOSED VOCABULARY, AS A VALUE THE CHECKER READS — never a list the checker restates.**
    ///
    /// Derived by reflection over the `ItemClass` union, on `Protocol.everyBlockerState`'s precedent: the
    /// three cases are nullary, so there is nothing to invent, and a list nobody writes cannot omit a
    /// case. Render each with `Types.itemClassWireName` — that is the one function that spells the words,
    /// and reading it here is what makes `lint`'s "the legal values are…" sentence and the value
    /// `reconcile` writes onto the board the same vocabulary by construction.
    ///
    /// .github#916 is why this is not a `[ Defect; Hardening; Decision ]` literal in the checker: the
    /// copies AGREED with each other and were wrong the whole time, so agreement is not evidence. A
    /// fourth case reaches every diagnostic the day it is declared, with no edit anywhere.
    val legalClasses: ItemClass list

    /// The values of this body's `Class:` lines that are NOT in the vocabulary — trimmed, de-duplicated,
    /// in body order. Empty when every `Class:` line resolves, and empty when there is no `Class:` line
    /// at all.
    ///
    /// **THIS IS THE FACT `fromBody` STRUCTURALLY CANNOT CARRY, and its absence was a live defect.**
    /// `fromBody` answers `None` for "no `Class:` line" and `None` for "`Class: docs`" alike, so `lint`
    /// reported a row that HAD written a class as one that *"records no `Class:`"* — the diagnostic named
    /// a fault the row did not have, and a reader who went looking for a missing line found a present one
    /// (.github#1651). Measured twice in one run, in two repos, by two workers, with two different
    /// invented words (`docs`, `enhancement`): both wrote the line, neither forgot it.
    ///
    /// A line with an EMPTY value is unrecognised, not absent. The key was declared and the value could
    /// not be read, and #266's rule is that a subject you could not evaluate is never a subject that
    /// passed.
    ///
    /// It is DELIBERATELY independent of `derive`: a body that says both `Class: docs` and
    /// `Blocked on: human/decision` resolves to `decision` AND still carries a word this engine does not
    /// speak. Suppressing the report because some other evidence rescued the row would leave the wrong
    /// line in the body for the next reader to trust.
    val unrecognised: body: string -> string list

    /// The class this BODY declares, or `None` when it declares none.
    ///
    /// SAME GRAMMAR AS `Paths:` AND `Blocked on:` — a `Class:` line at up to three leading spaces,
    /// OUTSIDE any fenced code block (a fenced line quotes the grammar, it does not use it — #277),
    /// value normalised for case and surrounding space. Only `defect`, `hardening` and `decision` are
    /// recognised; anything else yields `None`, on `HumanBlock.parse`'s terms exactly — a word we do not
    /// know is not a class we can act on, and guessing which of three it meant is the one thing AC3
    /// forbids.
    ///
    /// A `Blocked on: human/decision` sentinel ALSO yields `decision`, with no `Class:` line required.
    /// That is the zero-cost derivation AC5 demands rather than a convenience: an item already carrying
    /// ADR-0045's strongest "a human must choose" sentinel would otherwise have to say so twice, and two
    /// lines meaning one fact is precisely the drift this repo keeps paying for (#983, #1012).
    ///
    /// DEFECT DOMINATES over the other two when a body declares more than one, for `HumanBlock.parse`'s
    /// reason inverted. There the strongest claim is the most RESTRICTIVE ("a human must choose") and it
    /// must not be weakened; here the strongest claim is "something is broken NOW", and a body that says
    /// both `defect` and `hardening` must not be quietly downgraded to the one that lets a burn-down
    /// stop. Order the search; never take the first line.
    val fromBody: body: string -> ItemClass option

    /// The class this TITLE declares, or `None`.
    ///
    /// Only the `[decision]` prefix convention, which the board already uses (#1547, #1589, #1611) and
    /// which AC3 names as evidence. It is deliberately a PREFIX and not a substring: `lint`'s epic rule
    /// scans `[epic]` anywhere in a title and that is a known wart, but a title MENTIONING a decision is
    /// not a decision item, and this vocabulary decides whether a driver may stop.
    ///
    /// There is no title convention for `defect` or `hardening`, so there is nothing to derive for them
    /// and nothing is invented. `lint` reports the gap instead — see `CLASS-UNSET`.
    val fromTitle: title: string -> ItemClass option

    /// The class an item's own text declares, over both sources: the BODY first, then the TITLE.
    ///
    /// The body wins because it is the authority — an explicit `Class:` line is somebody stating the
    /// answer, where a title prefix is a convention being read. The title is consulted only when the
    /// body declares nothing, so adopting the body line can never be a downgrade for an item that
    /// already had a `[decision]` prefix.
    ///
    /// `None` is a REAL and common answer, and the whole reason `lint` has a rule: it means the item's
    /// text carries no evidence of its severity. It is never a default class, because a default here is
    /// exactly #266's fail-open one axis over — a row nobody triaged reading as a row that is fine.
    val derive: title: string -> body: string -> ItemClass option
