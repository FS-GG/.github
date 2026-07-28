namespace FS.GG.Coord.Cli

open FS.GG.Coord
open FS.GG.Coord.Types
open FS.GG.Coord.GitHub

/// Pure application service for lint verdicts.
///
/// Transport reads and CLI presentation stay at the composition edge; this module owns the
/// deterministic decisions between them.
module LintApplication =

    type EpicFinding =
        { Code: string
          Severity: string
          Detail: string }

    type Summary =
        { Errors: int
          Notes: int
          Fails: bool }

    val badTouchSetDetail: status: string -> usability: TouchSet.Usability -> string option

    /// Every legal `Class:` body line with its gloss, rendered from `Class.legalClasses` and
    /// `Types.itemClassWireName` — the vocabulary is READ from the typed core here, never restated
    /// (.github#1651 AC5, on #916's receipt that agreeing copies are still copies).
    val classMenu: string

    /// The refusal sentence for a body whose `Class:` line carries a word this engine does not speak —
    /// `None` when every line resolves, or when there is no line.
    ///
    /// ONE SENTENCE, TWO CALLERS. `lint` renders it as `CLASS-INVALID` and `add` renders it as the
    /// refusal that stops the row reaching the board misclassed. Two hand-written sentences for one
    /// fault would be the drift this repo keeps paying for, and the `add` one is the one a filer
    /// actually reads — it arrives while they are still standing there.
    val outOfVocabularyClass: body: string -> string option

    /// `lint`'s whole CLASS verdict for one `Ready`/`Backlog` open row: `Some(code, detail)` where the
    /// code is `CLASS-INVALID` (a `Class:` line whose value is out of vocabulary) or `CLASS-UNSET` (no
    /// class declared or derivable at all), and `None` when the row is classed.
    ///
    /// Both codes are severity `error`; the caller supplies that, on `badTouchSetDetail`'s terms.
    ///
    /// **ABSENT AND INVALID ARE DIFFERENT FAULTS AND MUST NEVER RENDER ALIKE** — a row carrying
    /// `Class: docs` described as recording no `Class:` is a diagnostic naming a fault the row does not
    /// have. An unrecognised value SUPPRESSES `CLASS-UNSET`: a body that wrote a line did not omit one.
    val classVerdict: status: string -> title: string -> body: string -> (string * string) option

    /// `lint`'s STATUS verdict for one row — `STATUS-UNSET`, `CLASS-UNSET`'s exact sibling
    /// (.github#1823 AC5). `Some detail` when an OPEN row sits on the board with no `Status` at all;
    /// `None` otherwise.
    ///
    /// **NOT `isSchedulableCandidate`'s population.** That predicate is `Ready || Backlog`, and the whole
    /// subject here is a row that is NEITHER: an unset column cannot be inside a set of columns.
    ///
    /// It REPORTS and never defaults — the default lives in `add`, at the moment the filer is still
    /// standing there. The two compose: `add` stops rows being filed invisible, this finds the ones that
    /// already are. Fourteen were, in one day, and every one was found by accident.
    val statusVerdict: state: IssueState -> status: BoardStatus -> string option

    val epicVerdict:
        state: IssueState ->
        status: BoardStatus ->
        body: string ->
        graph: Reads.SubIssueSet ->
        unlinked: string list ->
            EpicFinding list

    val summarize: strict: bool -> severities: string list -> Summary
