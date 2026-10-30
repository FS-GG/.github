namespace FS.GG.Coord

/// A pure, read-only SCOPED CONTEXT VIEW for one owning issue and one selected action (#4940, Design B of #4730).
///
/// It projects facts that already exist natively (the six-field issue, the native pull request and its
/// check evidence, the original operation custody, the workspace) into one typed, versioned, bounded JSON
/// document. It is NOT a packet, a required worker return, a status table or a ledger, and it grants no
/// authority: governing instructions stay complete and higher priority than this view.
///
/// ZERO IO. `build` and `render` take observations as values and return bytes, so a replay of the same
/// observations is byte-identical. The impure edge (`FS.GG.Coord.GitHub.ContextViewReads` and the CLI
/// application) only fetches the facts this module asks for.
///
/// THREE GUARANTEES THE TESTS ATTACK DIRECTLY:
///  1. Obligations are verbatim. The Done when text is carried as the author wrote it, never summarised.
///  2. Overflow is typed. If the rendered view would exceed the cap, whole sections are omitted from the
///     lowest priority up and each omission is listed with a retrieval pointer; the status becomes
///     `incomplete`. No section is ever cut mid-text and nothing disappears without an `omissions` entry.
///  3. A missing input is a typed `gap`, never an absent field that reads as "nothing to report".
module ContextView =

    /// The versioned wire schema this module emits.
    [<Literal>]
    val Schema: string = "fsgg.coord.context-view/1"

    /// Default summary cap in UTF-8 bytes. Chosen against measured real mandatory content (see the
    /// release notes and the PR body for #4940); callers may raise or lower it within the bounds below.
    val DefaultCapBytes: int

    /// A cap below this cannot even hold the header, gaps and omission records; it is refused, not honoured.
    val MinimumCapBytes: int

    /// Upper bound, aligned with the 256 KiB bound the original-operation observer accepts.
    val MaximumCapBytes: int

    /// The selected action. It chooses the applicable governing instructions and which sections are
    /// mandatory (the pull request and its checks are mandatory for every action except `implement`/`resume`).
    type Action =
        | Implement
        | Repair
        | Review
        | Merge
        | Accept
        | Resume

    val actionName: Action -> string
    val parseAction: string -> Result<Action, string>

    /// `OWNER/REPO#N`.
    type IssueRef = { Repository: string; Number: int }

    val parseIssueRef: string -> Result<IssueRef, string>

    /// An observation that was supplied, could not be read (with the reason), or was not requested.
    /// `Unavailable` and `NotRequested` become different gaps; neither is ever read as "none".
    type Read<'a> =
        | Supplied of 'a
        | Unavailable of reason: string
        | NotRequested

    type IssueFact = { Title: string; State: string; UpdatedAt: string; Body: string }

    type CheckFact = { Name: string; Status: string; Conclusion: string option }

    /// The check population at the pull request head. `Complete = false` when the native page said there
    /// were more checks than were read.
    type CheckPopulation = { Checks: CheckFact list; Complete: bool }

    type PullRequestFact =
        { Number: int
          Url: string
          State: string
          Draft: bool
          Merged: bool
          Head: string
          HeadRef: string
          BaseRef: string
          ChangedFiles: int
          Body: string
          Checks: Read<CheckPopulation> }

    /// The subset of the original-operation cursor (Design A custody) that carries ownership, attempt and the
    /// original Unknown effects/reservations. Read from the cursor file; never rewritten.
    type CustodyFact =
        { Issue: string
          Slice: string
          Attempt: string
          Owner: string
          Repository: string
          PullRequest: int
          Head: string
          Unknowns: string list
          Terminal: string option
          Gap: string option }

    type FileFact =
        | File of sha256: string
        | Directory
        | Absent
        | Unreadable of reason: string

    /// One curated Atlas record as read from the workspace archive.
    /// `Record` is the record's digest (its archive file name). `Supersedes` lists the digests this revision replaces
    /// and `ConflictsWith` its declared conflicts. One ID can have several revisions; only an unsuperseded one is a head.
    type AtlasEntry =
        { Id: string
          Kind: string
          Status: string
          Title: string
          Record: string
          Supersedes: string list
          ConflictsWith: string list }

    type NavigationFacts =
        { Files: Map<string, FileFact>
          Atlas: Read<AtlasEntry list> }

    type Observations =
        { Issue: IssueRef
          Action: Action
          RequestedPullRequest: int option
          CapBytes: int
          IssueRead: Read<IssueFact>
          PullRequestRead: Read<PullRequestFact>
          Custody: Read<CustodyFact>
          Navigation: Read<NavigationFacts> }

    /// Whether a gap weakens an OBLIGATION (Done when, owner, the issue itself) or only the surrounding context.
    type GapSeverity =
        | Obligation
        | Context

    type Gap = { Code: string; Severity: GapSeverity; Subject: string; Detail: string }

    type Omission = { Section: string; Mandatory: bool; Bytes: int; Sha256: string; Retrieval: string }

    type Status =
        | Complete
        | Incomplete

    type View =
        { Status: Status
          Json: string
          Bytes: int
          Sha256: string
          Gaps: Gap list
          Omissions: Omission list }

    type Refusal =
        /// The cap is outside the accepted bounds or too small for even the header, gaps and omission records.
        | CapOutOfBounds of cap: int * minimumNeeded: int

    /// Repository-relative files the view needs the workspace to verify: the action's governing instructions plus
    /// the declared non-pattern touch-set paths. The IO edge answers with `FileFact`s; the core never touches disk.
    val requestedPaths: action: Action -> issue: IssueFact -> string list

    /// The governing instruction files applicable to an action: a closed, ordered MINIMUM table. It is not every
    /// instruction that may apply, and the view says so (`governingInstructionsScope`).
    val instructionPaths: Action -> string list

    /// Build and render the view. Deterministic: equal observations give equal bytes.
    val build: Observations -> Result<View, Refusal>

    /// Parse the six-field issue sections. Exposed for tests and the IO edge. Headings are recognised only
    /// outside fenced code, matching the maintained six-field projection (a quoted copy never supplies a field).
    /// Returns (section name, verbatim body) for sections that occur exactly once, plus gap codes for the rest.
    val parseSections: body: string -> Map<string, string> * Gap list
