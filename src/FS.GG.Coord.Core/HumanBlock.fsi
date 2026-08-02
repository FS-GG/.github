namespace FS.GG.Coord

/// The `Blocked on: human/...` body-line sentinel (#1103 leg 2), parsed off the same fence-skipped,
/// up-to-three-leading-spaces grammar as `Paths:`. See ADR-0045.
module HumanBlock =

    open Types

    /// The sentinel this body declares, or `None` when it declares none.
    ///
    /// SAME GRAMMAR AS `Paths:` — a `Blocked on:` line at up to three leading spaces, OUTSIDE any fenced
    /// code block (a fenced line is a quotation of the grammar, not a use of it — #277), value normalised
    /// for case and surrounding space. Only `human/decision` and `human/action` are recognised; anything
    /// else is not a sentinel and yields `None` (a real `Blocked by` ref is a board field, not this line).
    ///
    /// DECISION DOMINATES: a body carrying both lines resolves to `AwaitingHumanDecision`, the stronger
    /// "unstartable until a human chooses" — never weaken a decision block to a mere pending action.
    val parse: body: string -> HumanBlock option

    /// Every `Blocked by:` BODY-LINE declaration (.github#2079), raw and un-canonicalized, in body order.
    ///
    /// SAME GRAMMAR, DIFFERENT FACT. `Blocked on:` (above) is the human-block sentinel this module owns;
    /// `Blocked by:` is a DIFFERENT line — a dependency declaration written where `Blocked by` the board
    /// FIELD belongs (ADR-0045, `.github#1933`). A body line is inert by construction: nothing that
    /// resolves a blocker reads it, only the field does. This function does not judge that — it hands
    /// back exactly what the body said, so a caller with the field's own value can compare the two and
    /// decide whether the declaration is redundant or a silently-dropped edge.
    ///
    /// One raw string per matching line (the ORIGINAL value — comma lists, stray prose, and all), not
    /// canonicalized: canonicalizing needs the subject's own owner/repo default, which this module —
    /// pure over `body` alone — does not have. `Blockers.canonicalizeBlockedBy` is the caller's next step.
    val parseBlockedByLines: body: string -> string list
