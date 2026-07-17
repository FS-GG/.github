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
