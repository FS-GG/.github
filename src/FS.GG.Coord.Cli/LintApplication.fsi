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
    /// (.github#1823 AC5). `Some detail` when an OPEN row sits on the board with no `Status` **this
    /// engine can read**; `None` otherwise.
    ///
    /// That wording is not hedging. `Scan.boardStatusOf` coerces an UNTAUGHT column name to `NoStatus`,
    /// so this rule cannot distinguish "empty" from "a column added to the board since the engine was
    /// written" — and asserting the first at `error` severity would state a falsehood about a row that
    /// has a column. The finding names both readings and carries both remedies; the consequence it is
    /// actually about (`Schedulability` refuses the row) is identical either way.
    ///
    /// **NOT `isSchedulableCandidate`'s population.** That predicate is `Ready || Backlog`, and the whole
    /// subject here is a row that is NEITHER: an unset column cannot be inside a set of columns.
    ///
    /// It REPORTS and never defaults — the default lives in `add`, at the moment the filer is still
    /// standing there. The two compose: `add` stops rows being filed invisible, this finds the ones that
    /// already are. Fourteen were, in one day, and every one was found by accident.
    val statusVerdict: state: IssueState -> status: BoardStatus -> string option

    /// `Some detail` for every open, non-`Done` row whose Severity is `Unset`; `None` once triaged.
    val severityVerdict: state: IssueState -> status: BoardStatus -> severity: Severity -> string option

    val epicVerdict:
        state: IssueState ->
        status: BoardStatus ->
        body: string ->
        graph: Reads.SubIssueSet ->
        unlinked: string list ->
            EpicFinding list

    /// One board row as the consolidation rule sees it.
    type ConsolidationRow =
        { /// The row's display ref, as the finding will name it.
          Ref: string
          /// The repo its tokens are relative to. Rows from different repos are NEVER compared (#353).
          Repo: string
          TouchSet: TouchSet }

    /// A candidate set of rows that MAY be one piece of work — never a claim that they are.
    type ConsolidationGroup =
        { /// Every member's ref, sorted. Never fewer than two.
          Members: string list
          /// The subtrees EVERY member declares. Never empty — a set with no common token is not a
          /// candidate operation, and this is the evidence the runner judges on.
          Shared: string list
          /// The WEAKEST member pair's coverage (plain Jaccard) — a floor over the group, not a mean.
          Coverage: float
          /// The WEAKEST member pair's hub-discounted shared-token evidence.
          Evidence: float }

    /// What the consolidation rule saw, INCLUDING what it could not.
    type ConsolidationVerdict =
        { Groups: ConsolidationGroup list
          /// Rows whose `Paths:` could not be read, with the reason. While this is non-empty the verdict
          /// is a NO-VERDICT and not an absence of clusters (#266).
          Unreadable: (string * string) list
          /// How many rows carried a declaration the rule could compare.
          Compared: int
          /// The whole population it was handed.
          Population: int }

    /// Coverage floor — plain Jaccard over declared tokens, at the reference measurement's own
    /// threshold, deliberately unchanged. Every difference from the reference lives in the evidence
    /// floor, so the effect of the hub discount can be stated rather than asserted.
    [<Literal>]
    val consolidationCoverageFloor: float = 0.5

    /// Evidence floor, `1 / sqrt 2`. The reading that matters: ONE shared token is enough on its own
    /// only when at most THREE rows in the population declare it; four or more, and it needs company.
    val consolidationEvidenceFloor: float

    /// What one shared token is worth when `rowsDeclaring` rows declare it — `1 / sqrt(n - 1)`.
    ///
    /// 1.00 when exactly two rows declare it (the token is that pair's own), 0.71 at three, 0.35 at
    /// nine. A token shared by `n` rows is evidence for `n - 1` partners at once, so no pair may claim
    /// all of it; the square root DAMPS that rather than crushing it, and both halves are measured.
    /// Undamped (`1/(n-1)`) loses four of the six real groups on the live board — the repos-audit group's
    /// two shared tokens are declared by nine and six rows. Flat keeps every false pair. This is where
    /// both hold.
    val consolidationTokenWeight: rowsDeclaring: int -> float

    /// Which open rows MAY be the same piece of work.
    ///
    /// **PAIRWISE, NEVER TRANSITIVE.** A group is a set in which EVERY pair independently clears both
    /// floors AND which retains a token every member declares. Nothing is admitted through a third row.
    /// Connected components over "shares at least one token" puts **74 of 78** `Ready` rows in a single
    /// cluster (measured 2026-07-29), because a few files are hubs — that is #1732/#1843's directory-token
    /// problem resurfacing inside the analysis.
    ///
    /// **TWO FACTORS, AND THE SECOND IS WHY THE FIRST IS NOT ENOUGH.** Coverage is plain Jaccard;
    /// evidence is the hub-discounted weight of the shared tokens, summed and NOT divided. Down-weighting
    /// hubs *inside* the ratio achieves nothing — the weight cancels between numerator and denominator,
    /// and #1869/#1875 score 1.00 under every weighting because they declare the identical single hub
    /// token. The discount only bites when spent absolutely.
    ///
    /// **FAILS CLOSED (#266).** A row whose `Paths:` could not be read is reported, never dropped, and
    /// the verdict over such a board is a no-verdict rather than "no clusters".
    ///
    /// **REPO-PARTITIONED (#353).** Tokens are repo-relative, so rows from different repos are never
    /// compared and hub frequencies are counted per repo.
    val consolidationVerdict: rows: ConsolidationRow list -> ConsolidationVerdict

    /// The `CONSOLIDATION-CANDIDATE` sentence: the members, the SHARED TOKENS the runner judges on, both
    /// scores, what the rule cannot see, and whose decision it is. All four are in the finding rather
    /// than in a docstring, because the finding is where the reader is standing.
    val consolidationDetail: group: ConsolidationGroup -> string

    /// The `CONSOLIDATION-UNREADABLE` sentence — a row this rule could not read, and the statement that
    /// the board's verdict is therefore incomplete rather than clean.
    val consolidationUnreadableDetail: reason: string -> string

    val summarize: strict: bool -> severities: string list -> Summary
