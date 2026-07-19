namespace FS.GG.Coord

/// The `Rooms:` body-line grammar (ADR-0051): the coordination rooms an item references, so its holder
/// joins their channel. Parsed off the same fence-skipped, up-to-three-leading-spaces rule as `Paths:`
/// (ADR-0045, ADR-0027 §7), in the same four ref spellings as `EpicBody.childRefs` / `Blockers` —
/// pinned against them in `ProtocolTests` so the three ref parsers cannot silently drift (#1153).
module Rooms =

    open Types

    /// Every room the body references, canonicalised to `Ref`s, de-duped and sorted.
    ///
    /// A `Rooms:` line at up to three leading spaces, OUTSIDE any fenced code block (a fenced line is a
    /// QUOTATION of the grammar, not a use of it — #277), exactly like `Paths:`/`Blocked on:`.
    ///
    /// A room reference is ADDITIVE, and that is the one place this grammar departs from `Paths:`. A
    /// touch-set is a single declaration (first `Paths:` line wins); a room membership is a UNION — a
    /// body may carry several `Rooms:` lines and each may list several refs, and the result is all of
    /// them, because a follow-up that inherits a contention adds its own `Rooms:` line and keeps the room
    /// alive (ADR-0051 §4). So this reads EVERY ref on EVERY `Rooms:` line, not the first of either.
    ///
    /// Every spelling reduces to one `owner/repo#n`: a bare `#n` adopts `selfOwner`/`selfRepo`, a
    /// `repo#n` adopts `selfOwner`, an `owner/repo#n` and an issue URL carry both. Non-ref prose on the
    /// line is ignored — a room line names references, and a word that is not one names no room.
    ///
    /// A `Rooms:` reference is NOT a blocker and does NOT gate scheduling; it establishes channel
    /// membership only. `Blocked by:` is a Projects v2 field, not a body line, so the two never interact
    /// — there is no precedence to resolve between them (ADR-0051 open-question 1).
    val parse: selfOwner: string -> selfRepo: string -> body: string -> Ref list
