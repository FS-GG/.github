namespace FS.GG.Coord

/// Versioned, side-effect-free finding-packet validation (.github#2737).
///
/// Under .github#2695 the board analyst is the only actor that files, so the finding packet is the
/// SOLE INPUT to all board growth. `board-analyst/SKILL.md` prescribed eight colon-delimited fields
/// under a search anchor and said of that anchor: "nothing parses it." This module is what parses it.
///
/// PURE, like every module in this project: `parse` takes the packet's TEXT and returns typed
/// findings. It reads no file and no board, and it never throws — a malformed document is a value,
/// not an exception, because the caller is an agent that must be told what is wrong while it still
/// holds the context that produced the packet.
module FindingPacket =
    [<Literal>]
    val Schema: string = "fsgg.coord.finding-packet/v1"

    [<Literal>]
    val ResultSchema: string = "fsgg.coord.packet-result/v1"

    /// A field whose honest answer is a SEARCH RESULT, not a value.
    ///
    /// `red-today`, `derived-by` and `class-row` each ask the finder to go and look: for a command
    /// failing on `main`, for a `scripts/check-*.py` that already derives the condition, for an open
    /// row that already anchors the class. The prose form wrote `none` for all three, and a nullable
    /// — the shape .github#2737's body first proposed — collapses the same two states `none` did.
    ///
    /// It collapses the wrong two. `SearchedNotFound` and `NotSearched` are the difference between
    /// "I looked and there is none" and "I did not look", and tests 2 and 3 of the filing bar rest on
    /// exactly that distinction: an analyst that cannot tell them apart must redo every search, which
    /// is the whole cost the packet exists to avoid. Both cases therefore CARRY A STRING — the search
    /// performed, or the reason it was not — so the analyst can check the search rather than trust it.
    /// That is the property `none` and `null` both lack, and it is why neither was chosen.
    ///
    /// Real packets occupy all three states. `.github#2691`'s comment 5304198465 says an adjudicator
    /// "should check whether a gate already derives" its condition — `NotSearched`, stated by a finder
    /// who would have had to write a false `none`; 5308627798 names the generalising measurement and
    /// says "I did not run it".
    type Answer =
        /// The finder searched and found this. The string is the answer.
        | Found of string
        /// The finder searched and there is none. The string is the search performed.
        | SearchedNotFound of string
        /// The finder did not search. The string is why not.
        | NotSearched of string

    /// The root cause, or an explicit statement that it was not established.
    ///
    /// `NotEstablished` carries what was measured instead, which is .github#1858's rule for an issue
    /// body applied one step earlier — at the packet, where the finder still holds the measurement.
    type Cause =
        | Established of string
        | NotEstablished of string

    type Packet =
        { Schema: string
          /// Where it showed up — file:line, a command, or a run URL.
          Surface: string
          Cause: Cause
          RedToday: Answer
          DerivedBy: Answer
          ClassRow: Answer
          /// Why the fix could not ride the PR the finder was already pushing.
          WhyNotHere: string
          /// The narrow declaration the finder would propose, already in the shape an intake draft takes.
          Paths: string list
          /// The finder's minted worker id, ALONE — `scripts/fsgg-coord say <ref> --to <worker>` is the
          /// documented way an analyst replies, and it takes an id, not a sentence naming one.
          Finder: string }

    type Finding = { Field: string; Detail: string }

    /// The fields an `fsgg.coord.intake/v1` draft takes DIRECTLY from a validated packet, so the
    /// analyst that decides to file does not retype what the finder already established.
    type IntakeSeed =
        { Observed: string
          RootCause: string
          Paths: string list }

    /// Validate an already-decoded packet's intrinsic facts. Never throws.
    ///
    /// SHAPE ONLY. Whether the named command is genuinely red, whether the named gate genuinely
    /// derives the condition, and whether the named row genuinely anchors the class are the three
    /// tests of the filing bar, and all three are judgement the analyst owns. This module removes
    /// only the failure mode where a pass is spent discovering that a packet was never judgeable.
    val validate: Packet -> Result<Packet, Finding list>

    /// Decode and validate a packet document. Never throws: malformed JSON, a non-object document,
    /// an unknown field, and a sentinel written in a shape that cannot carry evidence are all
    /// returned as findings naming the field and the shape that was meant.
    val parse: string -> Result<Packet, Finding list>

    /// Lift a validated packet into the intake-draft fields it already carries (AC6).
    val toIntakeSeed: Packet -> IntakeSeed

    /// Render the `fsgg.coord.packet-result/v1` success document for a validated packet.
    val renderResult: Packet -> string
