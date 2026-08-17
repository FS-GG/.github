namespace FS.GG.Coord

open System

/// The checkable SKELETON beside the churn reading, never a replacement for it (.github#2739).
///
/// `board-analyst/SKILL.md` makes the churn reading a required output of every pass and names six
/// elements it must carry. All six were emitted as free prose, so **nothing could confirm a pass
/// produced them**: a reading that silently omitted element 4 or element 6 was indistinguishable from
/// one that considered them and found nothing — which is precisely the distinction the skill text says
/// matters ("a pass that finds no pathology says so explicitly, with the measurements that support it.
/// Silence is not a clean reading, and neither is a bare number").
///
/// THE ROW'S HARDEST CONSTRAINT IS "KEEP THE PROSE", so read the shape below as a skeleton and not as
/// a form. Nothing here constrains the ANALYSIS: `cause`, `gate`, `remedy` and `prose` are free text
/// this module never inspects for content. What it does constrain is whether each of the six questions
/// was ANSWERED, and — where the answer is "none" — whether the answer carries the search that
/// produced it. The judgement stays with the analyst; only its completeness becomes checkable.
///
/// PURE, like every module in this project. `parse` takes the document's TEXT, `validate` takes an
/// already-decoded reading and the instant to judge its window against. Neither reads a file, a board
/// or a clock, and neither throws — a malformed document is a value, because the caller is an analyst
/// who must be told what is missing while it still holds the pass that produced it.
module ChurnReading =
    [<Literal>]
    val Schema: string = "fsgg.coord.churn-reading/v1"

    [<Literal>]
    val ResultSchema: string = "fsgg.coord.churn-reading-result/v1"

    /// The answer to one of the six questions the reading must ask.
    ///
    /// .github#2739 first proposed a bare array, with "empty means considered and none found". That is
    /// strictly better than the prose it replaces — absent stops looking like empty — but it collapses
    /// the pair .github#2760 names as the live defect one layer up: `gate.report_ok` takes a prose
    /// summary rather than a subject census, "so the harness built to make fail-open impossible cannot
    /// tell 'looked and found nothing' from 'did not look'." A bare `[]` cannot tell them apart either.
    ///
    /// So this is the tagged union .github#2737's `FindingPacket.Answer` established for the packet
    /// that FEEDS this reading, and it is deliberately the same vocabulary: the two documents express
    /// the same idea the same way. `SearchedNotFound` CARRIES THE SEARCH, so a reader can check the
    /// claim rather than trust it — the property `[]`, `"none"` and `null` all lack.
    ///
    /// `NotSearched` decodes and then FAILS `validate`. That is not a contradiction: the skill requires
    /// all six elements, so a pass that skipped one is incomplete and must be told which one and why it
    /// matters. The alternative — refusing the case at the parser — would leave an analyst who genuinely
    /// did not look with no honest shape to write, and the shape they would reach for instead is a false
    /// `SearchedNotFound`. Naming the state is what makes it visible.
    type Census<'a> =
        /// The pass looked and these are the rows. Must be non-empty.
        | Found of 'a list
        /// The pass looked and there are none. The string is the search performed.
        | SearchedNotFound of string
        /// The pass did not look. The string is why not. Decodes, but never validates.
        | NotSearched of string

    /// Elements 2 and 3: a cause named in the analyst's own words, and the rows that instance it.
    type CauseInstance =
        { /// Free prose. Never inspected for content — "name the cause in your own words" is judgement.
          Cause: string
          Rows: string list }

    /// Element 4: a row restating a condition a `scripts/check-*.py` already derives.
    type DerivedRow = { Row: string; Gate: string }

    /// Element 6: rows individually green and jointly red through a gate corpus neither author controls.
    /// The coupling needs at least two rows to exist at all, which `validate` enforces.
    type JointlyRed = { Rows: string list; Gate: string }

    /// Element 1's window, as a closed interval with explicit UTC endpoints.
    ///
    /// `references/churn-reading.md` § *Two windows, one board* is the whole reason this is a pair of
    /// instants rather than a string: two readers of one board on one day, both with correct
    /// arithmetic, reported net −7 and net −1, and both were right — the windows differed by seventeen
    /// hours and both were labelled "today".
    type Window = { Start: DateTimeOffset; End: DateTimeOffset }

    /// Element 1's second half. THE UNIT IS REQUIRED, and that is measured rather than fastidious: the
    /// reading that motivated the analyst role said "while 25 items landed", and
    /// `references/churn-reading.md` records that at that row's own endpoint NO candidate unit produces
    /// 25 — issues closed, pull requests merged and pull requests closed were all measured, and none of
    /// them is that number. A count whose unit is unrecoverable is not a measurement.
    type Landed = { Count: int; Unit: string }

    type Reading =
        { Schema: string
          Window: Window
          /// Element 1's components. `netRowDelta` must equal `rowsOpened - rowsClosed`; that is the
          /// only arithmetic invariant this document has, and transcription slips in published counts
          /// are a measured failure of this repository rather than a hypothetical one.
          RowsOpened: int
          RowsClosed: int
          NetRowDelta: int
          ItemsLanded: Landed
          CauseInstances: Census<CauseInstance>
          SuccessorGeneratingCauses: Census<CauseInstance>
          AlreadyDerived: Census<DerivedRow>
          JointlyRedCandidates: Census<JointlyRed>
          /// Element 5. Explicit `null` when no pathology is present — never the string "none", which
          /// `validate` refuses by name. Required to be non-null whenever any census reports `Found`.
          Remedy: string option
          /// The prose reading this skeleton accompanies, or an unambiguous pointer to where it was
          /// posted. Free-form, never inspected — but REQUIRED, because a skeleton that can be emitted
          /// without the reading would destroy the thing it was built to protect.
          Prose: string }

    type Finding = { Field: string; Detail: string }

    /// Validate an already-decoded reading against the instant it is being judged at. Never throws.
    ///
    /// `now` is a parameter rather than a clock read because this module is pure and because the
    /// window rule needs it: an interval whose end has not happened yet cannot be re-derived by
    /// anybody, including its author five minutes later.
    ///
    /// SHAPE AND COMPLETENESS ONLY. Whether a named cause really is one cause, whether a gate really
    /// derives the condition, and whether a remedy really names a mechanism are the analyst's
    /// judgement and stay there. This removes only the failure mode where a pass is reported without
    /// anyone being able to tell which of the six questions it asked.
    val validate: DateTimeOffset -> Reading -> Result<Reading, Finding list>

    /// Decode and validate a reading document's SHAPE. Never throws: malformed JSON, a non-object
    /// document, an unknown field at any level, an absent required field, and a census written as a
    /// bare array, a bare string or `null` are all returned as findings naming the field and the shape
    /// that was meant.
    val parse: string -> Result<Reading, Finding list>

    /// Render the `fsgg.coord.churn-reading-result/v1` success document.
    ///
    /// It reports the CASE of each of the four censuses, not merely that validation passed, so the
    /// receipt itself distinguishes "looked and found none" from "found these" — the distinction the
    /// whole schema exists for, carried forward into the thing an analyst pastes into a report.
    val renderResult: Reading -> string
