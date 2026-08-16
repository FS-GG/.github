namespace FS.GG.Coord

/// The `Kind:` body-line sentinel (.github#2712) — *whether this row has a lifecycle at all*, in the same
/// grammar as `Paths:`, `Class:` and `Blocked on:`.
///
/// **THE BODY IS THE AUTHORITY AND THE BOARD FIELD IS THE PROJECTION**, on `Class`'s exact terms and
/// ADR-0066's direction. This module reads; nothing here writes a body, and `reconcile` projects what it
/// reads onto the `Kind` board field.
///
/// **AND HERE THE DIRECTION IS LOAD-BEARING FOR SAFETY, NOT ONLY FOR DRIFT.** `Class`'s reason for
/// refusing a field-as-authority was board-schema churn. This axis adds a second: the value decides
/// whether the lifecycle reducer runs at all, so a field-as-authority would let one dropdown edit remove
/// a real work row from its own lifecycle — silently, with no body anybody could read to find out why.
/// The reducer and the scheduler therefore read `Item.Kind` and never `Item.BoardKind`.
module Kind =

    open Types

    /// **THE CLOSED VOCABULARY, AS A VALUE THE CHECKER READS — never a list the checker restates.**
    ///
    /// Derived by reflection over the `ItemKind` union, on `Class.legalClasses`' precedent and for its
    /// reason (.github#916: the copies AGREED with each other and were wrong the whole time, so agreement
    /// is not evidence). All four cases are nullary, so there is nothing to invent, and a list nobody
    /// writes cannot omit a case. Render each with `Types.itemKindWireName`.
    val legalKinds: ItemKind list

    /// The values of this body's `Kind:` lines that are NOT in the vocabulary — trimmed, de-duplicated,
    /// in body order. Empty when every `Kind:` line resolves, and empty when there is no `Kind:` line.
    ///
    /// **THE FACT `fromBody` STRUCTURALLY CANNOT CARRY**, and `.github#1651` measured what its absence
    /// costs on the `Class:` axis twice in one run: `fromBody` answers `None` for "no `Kind:` line" and
    /// `None` for "`Kind: registers`" alike, so a diagnostic derived from `fromBody` alone would tell an
    /// author who HAD written the line that they had not.
    ///
    /// A line with an EMPTY value is unrecognised, not absent: the key was declared and the value could
    /// not be read, and #266's rule is that a subject you could not evaluate is never one that passed.
    val unrecognised: body: string -> string list

    /// The kind this BODY declares, or `None` when it declares none.
    ///
    /// SAME GRAMMAR AS `Paths:`, `Class:` AND `Blocked on:` — a `Kind:` line at up to three leading
    /// spaces, OUTSIDE any fenced code block (a fenced line QUOTES the grammar, it does not use it —
    /// #277, and this module's own documentation quotes it), value normalised for case and surrounding
    /// space. Only `work`, `anchor`, `register` and `directive` are recognised; anything else yields
    /// `None`, on `Class.fromBody`'s terms exactly.
    ///
    /// **`work` DOMINATES when a body declares more than one — `Class.fromBody`'s ordered search with its
    /// dominance INVERTED, for the same underlying rule.** There, the strongest claim is "something is
    /// broken NOW" and must not be quietly downgraded to the reading that lets a burn-down stop. Here the
    /// strongest claim is "this row is ordinary work", because EXEMPTION is the powerful outcome: it
    /// removes the row from the lifecycle reducer entirely. An ambiguous declaration must resolve toward
    /// the reading that keeps the row UNDER the machinery, never toward the one that removes it. Order the
    /// search; never take the first line.
    ///
    /// There is NO title convention and none is invented — `Class.fromTitle` has one because the board
    /// already used `[decision]` prefixes; nothing on this board marks a register in its title, and
    /// inventing a convention here would be deriving an exemption from a naming habit.
    val fromBody: body: string -> ItemKind option

    /// The kind that GOVERNS, given what the body declared — `None` resolved to `Work`.
    ///
    /// **THE ONE PLACE THE `None`-MEANS-`Work` DEFAULT IS SPELLED**, so no caller re-decides it and no
    /// caller forgets it. Every row on this board declares no kind today, so this default is what makes
    /// the change a no-op for all of them: an undeclared row reaches an unchanged reducer with an
    /// unchanged answer.
    val govern: declared: ItemKind option -> ItemKind

    /// Does this kind have a lifecycle? `false` for every standing kind.
    ///
    /// Named once, and DERIVED as "not `Work`" rather than listed as three cases, so a fifth `ItemKind`
    /// is standing by default. That is the fail-closed direction: a kind nobody has taught this predicate
    /// about is one we cannot claim has a completion condition, and treating it as `Work` would hand the
    /// reducer a row it may mark `Done`.
    val isStanding: ItemKind -> bool
