namespace FS.GG.Coord

/// THE PROTOCOL, AS DATA — the source every projection is emitted FROM (ADR-0034 §4.5).
///
/// A coordination rule was stated in up to six places — the ADR, the canonical doc, the tool, and four
/// `SKILL.md` bodies across two skill roots — then content-addressed into `repos.lock` and byte-copied
/// into six receivers: 54 vendored copies. The rules live HERE now, once, in the typed core that already
/// enforces them, and the prose is GENERATED. A rule cannot land in one tier and not the others, because
/// there are no tiers.
module Protocol =

    open Types

    /// One rule. `Id` is the anchor a projection references, so a doc can cite a rule without restating
    /// it and a reader can grep the id back to the code that enforces it.
    type Rule =
        { Id: string
          Title: string

          /// The rule itself, in one paragraph. This is the text that lands in every projection.
          Statement: string

          /// Why it is this way — the incident that bought it. Emitted into the canonical doc, and
          /// omitted from the terse skill projections.
          Because: string }

    /// A schedulability verdict, as the worker meets it.
    type VerdictDoc = { Kind: string; Meaning: string }

    /// One board `Status` option, as a filer meets it: the option name the board stores, and whether the
    /// scheduler hands the item out while it sits there.
    /// One TOP-LEVEL key of the snapshot document (`scan --json`), as the `jq` filters in `check-board`
    /// meet it. See `snapshotKeys`.
    type SnapshotKeyDoc =
        { /// The key as it appears on the wire — the string `Scan.snapshot` writes.
          Key: string

          /// Whether a RECONCILER acts on this key, or merely carries it.
          ///
          /// The bit a key NAME cannot carry, and the one a reader needs: `limit` and `leaseMinutes`
          /// look like board facts and are the scan's own PARAMETERS echoed back, so a pass that
          /// selects on them is reconciling against its own request rather than against the board.
          Reconciled: bool

          /// What the key carries, and why a reconciler does or does not act on it.
          Meaning: string }

    type BoardStatusDoc =
        { /// The Projects v2 option name — `Types.statusWireName`'s answer, never a second spelling. This
          /// is the string `set-field` accepts and a `jq` `.status` selector matches.
          Wire: string

          /// Whether a scheduler offers an item in this column — `Schedulability.columnStartability`'s
          /// answer, spelled by `Schedulability.columnStartabilityWireName`, never a second spelling.
          ///
          /// A STRING, on `VerdictDoc.Kind`'s terms and for its reason: the vocabulary belongs to `Core`,
          /// beside the union it names. Carrying the union here instead would push the spelling out into
          /// whichever projection renders it — which is what the first draft of #1057 did, in `Snapshot.fs`,
          /// where renaming a case compiled with zero F# errors.
          ///
          /// NOT a bool: the truth has three states. `Backlog` is startable only under `--include-backlog`,
          /// and that is the state a filer most often asks about.
          Startable: string

          /// What the column asserts about the item, and why a scheduler does or does not offer it.
          Meaning: string }

    /// One `BlockerState`, as a reader of the scan's JSON meets it: the wire string, and what it says
    /// about the blocker.
    type BlockerStateDoc =
        { /// The string `scan` emits — `Types.blockerStateWireName`'s answer, never a second spelling.
          Wire: string

          /// Whether the blocker HOLDS. The one bit a reconciler acts on, and the one the union's case
          /// name does not carry: `unknown` and `unparseable` read like non-answers and BLOCK.
          Holds: bool

          /// What the state says about the blocker, and why it holds or does not.
          Meaning: string }

    /// The blocker wire vocabulary, as prose — the five cases of `Types.BlockerState`, each with the
    /// string `scan` actually writes and what it means (#889).
    ///
    /// `check-board` §1 restated this list by hand: *"`open | closed | merged | unknown | unparseable`,
    /// the five cases of the engine's `BlockerState`"*. That sentence names its own source and was still
    /// a copy — the sharpest form of the problem, because a RECONCILER reads these strings with `jq` and
    /// a `.state` selector that matches nothing reports a **clean board**. Its worst output, by its own
    /// account (#476).
    ///
    /// Generatable only since #1012 gave the vocabulary an owner in `Core`. Before that it was two
    /// `private` INVERSE copies outside `Core`, unreachable from here, and hand-typing the five cases
    /// into this file would have been a THIRD — #865's defect, and #916's trap 1: a generator whose
    /// source is hand-maintained just moves the drift upstream and makes it authoritative.
    ///
    /// `Wire` is `blockerStateWireName`; `Holds` is a total match. Neither is written twice.
    val blockerStates: BlockerStateDoc list

    /// The board's `Status` vocabulary, as prose — the six options of `Types.BoardStatus` that a board
    /// column can actually hold, each with the option name the board stores and whether the scheduler
    /// offers it (#889).
    ///
    /// `cross-repo-coordination` stated these six strings by hand, in a field table, and that table is
    /// what a filer reads before setting a column. The stakes are not a misprinted doc: `set-field`
    /// REFUSES an unknown option, so a filer who copies a drifted spelling gets a loud failure — but a
    /// RECONCILER reads these strings with `jq` and a `.status` selector that matches nothing reports a
    /// CLEAN BOARD. That is `check-board`'s worst output by its own account (#476), and it is the shape
    /// #1012 measured in `BlockerState`: two copies, 775 tests green, the vocabulary broken.
    ///
    /// SIX, NOT SEVEN. `BoardStatus` has a seventh case — `NoStatus`, whose wire form is `""` — and it is
    /// deliberately not here: it is the ABSENCE of a column, not an option the board offers, and a filer
    /// cannot select it. `everyBoardStatus` drops it in a TOTAL match, so a new case cannot join the
    /// union without this file being asked which side of that line it falls on (#437 is what happens when
    /// `NoStatus` is allowed to read as `Backlog`).
    ///
    /// `Wire` is `statusWireName`; `Startable` is `Schedulability.columnStartability`. Neither is written
    /// twice — and `Startable` could not be published at all until #1057 gave that fact a name, since it
    /// lived as three legs of a `match` inside `schedulable` where no document could read it.
    val boardStatuses: BoardStatusDoc list

    /// The snapshot document's schema string — the `schema` member `Scan.snapshot` writes.
    ///
    /// THE OWNERSHIP CALL, STATED SO IT IS ARGUABLE (#1058). This module is `Core`; the document is
    /// rendered by `Scan` and parsed by `Snapshot`, both outside it. So `Protocol` states the shape of a
    /// document it does not itself render, and this string is a THIRD copy of one that already lives in
    /// both. That is a real cost, taken deliberately: the rejected alternative — own the shape where it
    /// is rendered and teach `generate-projections` a second source — is the stricter reading of
    /// #865/#916 trap 1, and it was declined on cost, not on principle.
    ///
    /// What makes the cost payable is a TEST, not a convention: `ProtocolTests` pins this against the
    /// strings `Scan` writes and `Snapshot` accepts. A doc that drifts from its subject is the failure
    /// this module exists to end, and an ownership call that leaves drift unpinned would have re-created
    /// it one file over.
    val snapshotSchema: string

    /// The snapshot document's top-level keys, in the order the WRITER emits them — not the order a
    /// prose author found readable. The literal this replaced had `leaseMinutes` and `limit` swapped and
    /// nothing noticed, because nothing compared them.
    ///
    /// `Reconciled` is the column a reader acts on: it says which keys a `jq` filter may select on.
    /// Only `items` carries board facts. The rest are the scan's own parameters echoed back, and a pass
    /// that reconciles against those is reconciling against its own request.
    val snapshotKeys: SnapshotKeyDoc list

    /// The verdict union, as prose. Emitted from the same cases the scheduler returns, so the list a
    /// worker reads cannot omit one — which is what fourteen of the scheduler family's issues were.
    ///
    /// The `Kind`s are `Schedulability.kind`'s, not a second spelling: the kinds match the wire
    /// vocabulary the divergence log speaks, so a reader of that log can grep a verdict straight into
    /// the doc that explains it. That sentence stood here while BOTH halves were false (#865) — this
    /// list was hand-typed, `item-pr-open` was on the wire and in no doc, and `held` was documented for
    /// a wire that says `held-by`. It is true now because one exhaustive match emits both, and the
    /// compiler refuses a case that has no entry.
    val verdicts: VerdictDoc list

    /// One exit code, as the CALLER's contract — the fact a shell script reads without parsing prose.
    type ExitCodeDoc =
        { Code: int

          /// The `EX_*` spelling, where the code has one a worker would recognise; `""` where it does
          /// not. The name is a label on the number, never a second source for it.
          Name: string

          /// What the code means the engine OBSERVED.
          Meaning: string

          /// What the caller should DO about it. A code whose remedy is unstated is a code a worker
          /// invents a remedy for.
          Action: string }

    /// One row of `release`/`reap`'s column precedence — the mapping from an item's state at unclaim
    /// time to the column it ends in. NOT an `ExitCodeDoc`: there is no exit code and no `EX_*` name.
    /// The subject is `(explicit --status, live column, recorded column)` → end state, so the row
    /// carries the CONDITION and the resulting column, plus the two things a worker actually reads back.
    type ReleaseColumnDoc =
        { /// The input, stated in PRECEDENCE ORDER — what is true of the explicit `--status`, the live
          /// column, and the marker's recorded column. The first row whose condition holds wins, exactly
          /// as `release` evaluates them.
          Condition: string

          /// The column the item ends in.
          EndState: string

          /// Whether `release` WRITES the column, or leaves it exactly as it is. The absence of a write
          /// is not an optimisation: it IS the observable that says the column was nobody's to change
          /// (#331), and "preserved" and "restored" are indistinguishable to the board's history without
          /// it.
          Writes: bool

          /// The line stdout prints — the TELL. `release` exits 0 even when the column write does not
          /// land, so the exit code cannot confirm a park; the stdout line can, and a worker keys on it
          /// to tell a preserve from a set from a bare "no column was set" without re-reading the board.
          Stdout: string }

    type WavePolicyDoc =
        { Waves: int
          ImplementerSlotsPerWave: int
          ReviewSlots: int
          ConsolidationThreshold: int }

    // ---- MARKER GRAMMAR TYPES (.github#2399) ----------------------------------------------------------
    //
    // Before this, the review-marker family was six independent `string` fields plus a rule stated as
    // PROSE INSIDE A STRING (`QuotedMarkerRule`) — not a function the compiler can check, so every
    // reader that needed "is this occurrence live, quoted, or competing" re-derived it by hand
    // (`Driver.fs`'s own scanner, and the hand-projected copy `d58577ec` had to write beside it). The
    // types below give the question a total answer instead: WHERE a marker is permitted to count
    // (`MarkerAnchor`), and WHAT one physical occurrence of it means (`MarkerOccurrence`), with
    // `occurrences` as the one function that decides both from a `Marker` and a body.

    /// A marker's stable name — e.g. `"independent-review"` — NOT its rendered wire text. Use
    /// `markerWireText` to get the `<!-- fsgg:%s{id}:v1 -->` form `occurrences` actually scans for.
    type MarkerId = string

    /// Where in a comment body a canonical marker occurrence is permitted to count as LIVE. Every
    /// marker in the family declares exactly one.
    type MarkerAnchor =
        /// The marker must BE the comment's own first trimmed line (.github#83758ec3, #d86ded2a): the
        /// delivery-obligation/receipt/none and delivery-route markers anchor here in production, in
        /// `src/FS.GG.Coord.Cli/DeliveryApplication.fs` and `Client.fs` (out of this item's `Paths:`,
        /// unchanged) — the marker family declared HERE does not currently use this anchor, but
        /// `occurrences` implements and tests it so the type is total over the real anchor vocabulary
        /// rather than only the cases this item happens to touch.
        | LeadingLine
        /// The marker may appear anywhere within the comment's LEADING marker block — the run of lines
        /// from byte 0 that are each exactly one KNOWN marker's canonical text (.github#2221, #2248).
        /// All five `ReviewPolicyDoc` markers anchor here today.
        | LeadingBlock
        /// Legacy/explicitly-unanchored: every canonical whole-line occurrence anywhere in the body
        /// counts. This is exactly the defect class #83758ec3/#d86ded2a fixed for the delivery markers
        /// (a marker checked against a whole body rather than a specific line) — a new marker choosing
        /// this arm is a deliberate, reviewed exception, and its declaration site must carry a comment
        /// saying why. No marker in this file declares it.
        | AnywhereInBody

    /// The disposition of one canonical whole-line occurrence of a marker's wire text within a body.
    /// No "maybe" arm: `occurrences` classifies every such line as exactly one of these three. A line
    /// that does not match the marker's wire text at all produces no `MarkerOccurrence` — it is not an
    /// occurrence of anything.
    type MarkerOccurrence =
        /// A canonical occurrence inside the marker's anchor region, and the only one there — the
        /// occurrence a reader acts on.
        | Live of raw: string
        /// A canonical occurrence OUTSIDE the marker's anchor region that markdown ITSELF marks as
        /// quotation — inside a fence (`` ``` ``/`~~~`) or a genuinely 4-space/tab-indented block,
        /// mirroring `Driver.fs`'s `annotateQuotedRegions` (Driver.fs:272-286) exactly, so this means
        /// what `Driver.fs` means by it. Inert: never evidence, never an error by itself. (Revised
        /// .github#2399 repair round 1: previously this case covered EVERY out-of-region occurrence,
        /// which disagreed with `Driver.fs` for the unfenced/unindented case — see `Misplaced`.)
        | Quoted
        /// A canonical occurrence OUTSIDE the marker's anchor region that markdown does NOT mark as
        /// quotation — sitting on its own line, unfenced and unindented. `Driver.fs`'s
        /// `misplacedMarkerKinds` (Driver.fs:288-311) refuses exactly this shape by name (`.github#2221`,
        /// pinned at `DriverTests.fs`'s `` #2221 a misplaced marker is refused by name, not silently
        /// dropped `` test) rather than treating it as an inert quotation: a critic or contributor who
        /// wrote it clearly meant to post the marker, just not where the protocol requires it, and "I
        /// found bytes I could not interpret" must not silently become "I read nothing wrong". Only
        /// meaningfully produced for `LeadingBlock` today — `LeadingLine`/`AnywhereInBody` markers have
        /// no `Misplaced` concept in their production code (`DeliveryApplication.fs`/`Client.fs`, out of
        /// this item's `Paths:`).
        ///
        /// `Driver.fs` narrows this ONE STEP further for a marker carrying field grammar
        /// (`Protocol.markerFieldGrammar`'s `RequiredFields` non-empty): it treats a structurally
        /// misplaced occurrence as harmless when the comment ALSO writes no OTHER protocol field
        /// anywhere in the body (`writesProtocolFields`, Driver.fs:290-295) — a whole-body,
        /// cross-marker heuristic over the full protocol field vocabulary (route-evidence and
        /// diff-audit fields included), not a per-marker anchor fact. `occurrences` does not model that
        /// second gate, so for a field-grammar-bearing marker this case is a conservative
        /// OVER-approximation of `Driver.fs`'s true refusal set: every occurrence `Driver.fs` actually
        /// refuses is `Misplaced` here too (the structural condition is a prerequisite `Driver.fs`
        /// checks first), but not every `Misplaced` here is one `Driver.fs` refuses. The asymmetry is
        /// safe in the direction that matters — this function never reports `Quoted`/inert for an
        /// occurrence `Driver.fs` would refuse — and is exact (no approximation at all) for a marker
        /// with EMPTY field grammar (escalation, repair-phase), where `Driver.fs`'s own gate is
        /// unconditional.
        | Misplaced of raw: string
        /// A second (or later) canonical occurrence of the SAME marker INSIDE its own anchor region. A
        /// marker kind has exactly one meaning per comment, so every occurrence in the region is
        /// `Competing` when more than one is present — refused rather than resolved by "first wins".
        | Competing of raw: string

    /// One marker's identity and the anchor `occurrences` checks its occurrences against.
    type Marker = { Id: MarkerId; Anchor: MarkerAnchor }

    /// A marker's rendered wire text — the SAME `<!-- fsgg:%s{id}:v1 -->` wrapping
    /// `Driver.fs`'s (private) `markerText` has always used. `occurrences` scans a body for this, not
    /// for the bare `MarkerId`.
    val markerWireText: MarkerId -> string

    /// Classify every physical line of `body` against `marker`. `knownMarkerTexts` is the closed family
    /// of wire texts `LeadingBlock` needs to know the leading block's extent — a leading run mixing two
    /// different known markers is still one leading block (.github#2221) — and is ignored by
    /// `LeadingLine`/`AnywhereInBody`, which need no family context. Total: this is the
    /// quoted-versus-misplaced-versus-competing rule of #2221/#2248 and the leading-line rule of
    /// #83758ec3/#d86ded2a, stated once as a function instead of re-derived prose per call site. See
    /// `MarkerOccurrence.Misplaced` for the one documented, safe-direction approximation this makes
    /// relative to `Driver.fs` for a field-grammar-bearing marker.
    val occurrences: knownMarkerTexts: string list -> marker: Marker -> body: string -> MarkerOccurrence list

    /// Renders the prose equivalent of `marker`'s anchor rule, dispatched over `MarkerAnchor` rather
    /// than stored as a free string. For `LeadingBlock` this is byte-identical to the string
    /// `d58577ec` projected as `ReviewPolicyDoc.QuotedMarkerRule` — the removed field's replacement.
    val renderMarkerAnchorRule: MarkerAnchor -> string

    type ReviewPolicyDoc =
        { InitialMarker: string
          ConfirmationMarker: string
          AcceptanceMarker: string
          EscalationMarker: string
          RepairPhaseMarker: string
          MaxAutomatedRepairRounds: int
          RepairPhaseMaxRounds: int
          /// Every marker name field above, paired with the `MarkerAnchor` `occurrences` checks it
          /// against. Replaces `QuotedMarkerRule`'s hand-projected prose: the rule is `occurrences`,
          /// and this is its input for the review-protocol family.
          MarkerAnchors: Marker list }

    /// The plain column-0 fields a marker's own comment must additionally carry (.github#2369) — e.g.
    /// `fsgg:independent-review:v1` requires `critic`, `reviewed-head`, `verdict`. Read off this value
    /// as data instead of re-deriving `Driver.fs`'s private `markerFieldGrammar` function by hand. An
    /// empty list is a marker with NO field grammar (escalation, repair-phase) — not an omission; see
    /// `Driver.fs`'s own comment on why that omission is deliberate.
    type MarkerFieldGrammarDoc = { MarkerId: MarkerId; RequiredFields: string list }

    /// The review-protocol family's field grammar, one entry per `ReviewPolicyDoc` marker name field,
    /// in the same order. `ProtocolTests` pins this against `Driver.parseReviewComments`'s own observed
    /// enforcement (the source of truth `Driver.fs`'s private function encodes), so it cannot drift
    /// from what the engine actually requires without a red test.
    val markerFieldGrammar: MarkerFieldGrammarDoc list

    type LifecyclePolicyDoc =
        { RequiredHousekeeping: string list
          TerminalActions: string list
          HostAcceptanceFields: string list }

    type LedgerPolicyDoc =
        { Schema: string
          ObservationFields: string list
          ContentIntakeFields: string list
          ContentDispositionFields: string list
          ReceiptFields: string list
          RequiredObservations: (string * string) list }

    /// `take`'s exit contract (#585) — the one command in the worker loop, so the code that tells "you
    /// hold it" from the ways it can hand you nothing is the difference between a fan-out and a
    /// double-claim.
    ///
    /// Generated, because the hand-written copy was WRONG: `/pnext-item` §1 documented `EX_PARTIAL` as
    /// `take` "could not read the board — a no-verdict", where `Errors.ExPartial` is a WRITE that
    /// half-landed and `take` never returns it. `Cli.Tests` pins every `Code` here against the literal
    /// the engine actually returns, so this list cannot drift from `Client.take` again.
    val takeExitCodes: ExitCodeDoc list

    /// `landable`'s exit contract (#900) — the POLL-LOOP half of the same defect. The code is the
    /// machine-readable verdict, so a loop tells "keep waiting" from "stop" without parsing the word.
    ///
    /// Generated, because the hand-written copy documented BASH's numbers: `/pnext-item` §5 called `3`
    /// "pending" where the engine returns it for RED, so a loop built from the table waits forever on a
    /// PR that can never go green — and had no row for `7`, the one code that does mean wait. `7` is
    /// the ONLY retryable code here, and there is no EX_RATE: `landable` has no error channel, so an
    /// exhausted budget arrives as `unknown` (4). `Cli.Tests` pins every `Code` here against the
    /// literal the engine returns; `Core.Tests` pins what the rows SAY.
    ///
    /// `NotOpen` (10) since #1680, and it is the same defect a third time, in the other direction: the
    /// table was complete over the OPEN-PR vocabulary and had no row for a PR that is not open, so a
    /// MERGED PR — the most terminal state there is — was answered with `7`, the one code documented as
    /// worth retrying, and `--wait` burned its full 600s budget on it every time. The row is DERIVED
    /// from the verdict vocabulary (`Types.PrState` gained `PrMerged`/`PrClosed`, `ExitCode.landableCodes`
    /// gained `NotOpen`, and the completeness test checks this list against that set), which is #1680
    /// AC6: a projection must follow the vocabulary rather than restate it alongside.
    val landableExitCodes: ExitCodeDoc list

    /// `release`/`reap`'s column precedence (#1099) — the third table in the class #889/#900 proved:
    /// `takeExitCodes` and `landableExitCodes` were the two facts that were GENERATED, and neither has
    /// drifted since. This one is `release`'s column semantics, the single most-repaired behaviour in
    /// the org — SEVEN issues (#331/#354/#531/#867/#911/#914/#921) corrected a hand copy while the
    /// engine's `unclaimColumn` was, eventually, right — and it was the one restatement #1059 could not
    /// move into a region, because it is a PRECEDENCE with an end state, not a one-paragraph `Rule`.
    ///
    /// Generated, so the `pnext-item` "Abandoning an item" prose that stated this precedence by hand
    /// cannot drift from `Client.release`'s arms again. The rows are ordered as `release` EVALUATES
    /// them: explicit `--status` first (it beats everything), then the read-failed fail-closed case,
    /// then the `unclaimColumn` arms. `ProtocolTests` pins what the rows SAY — that the `--status` row
    /// leads, and that a preserve writes nothing.
    val releaseColumns: ReleaseColumnDoc list
    val wavePolicy: WavePolicyDoc
    val reviewPolicy: ReviewPolicyDoc
    val lifecyclePolicy: LifecyclePolicyDoc
    val ledgerPolicy: LedgerPolicyDoc

    val touchSetGrammar: Rule
    val touchSetDeclaration: Rule
    val blockerResolution: Rule
    val checkOrder: Rule
    val claimLock: Rule
    val leaseRule: Rule
    val failClosed: Rule

    /// Every rule, in the order a projection presents them.
    val rules: Rule list

    /// The rules a worker FILING an item must satisfy — the subset `cross-repo-coordination` restates
    /// (#889). A SUBSET of `rules`, holding the same values, never a second list: the containment is
    /// pinned, so a rule cannot reach a projection without the canonical doc stating it too.
    ///
    /// It is a subset rather than the whole block because a filer does not schedule, claim, or hold a
    /// lease — they link to `intra-repo-parallel-work` for that. A region carries what its document is
    /// FOR (#916).
    val filingRules: Rule list

    /// The rules a RECONCILER must satisfy — the subset `check-board` restates (#889). A SUBSET of
    /// `rules` on the same terms as `filingRules`, and pinned by the same containment invariant.
    ///
    /// `check-board`'s own finding codes are PROCEDURE and stay authored; what it may not restate is the
    /// protocol they read — that a blocker clears on CLOSED **or MERGED** (`blocker-resolution`), that a
    /// read which did not happen may never render as a confident answer (`fail-closed` — a reconciler's
    /// false clean is its worst output), and what a `Paths:` line actually IS (`touch-set-declaration`).
    val reconcileRules: Rule list

    /// The rules a worker DRIVING an item must satisfy — the subset `pnext-item` restates (#889/#1059). A
    /// SUBSET of `rules` on the same terms as `filingRules`, and pinned by the same containment invariant.
    ///
    /// A driver touches more of the protocol than any other role, so the cut is what they DECIDE rather
    /// than what they meet: they mint an id and take a lock with it (`claim-lock`, whose total order
    /// separates workers only while their ids are distinct), they hold a lease that cannot be renewed once
    /// it expires (`claim-lease`), and they author a `Paths:` line mid-flight against a grammar that
    /// refuses them (`touch-set-declaration`, `touch-set-grammar`). The scheduler's own order, blocker
    /// resolution, and fail-closed reads are the ENGINE's — a driver reads their verdict.
    val driverRules: Rule list

    /// One section of the facts document: a key, and the facts stated under it.
    ///
    /// ONE CASE PER JSON SHAPE, NOT PER KEY — `rules`, `filingRules` and `reconcileRules` are three keys
    /// of one shape, and the writer cannot tell them apart. So a new Core-owned fact KEY is an edit to
    /// `factsDocument` alone, and only a new fact SHAPE reaches the Cli (#1027).
    ///
    /// Every case carries its key, including those with one member today: a case without one puts its
    /// key string back in the writer, which is half the inventory back in the Cli.
    type FactSection =
        | Rules of key: string * Rule list
        | Verdicts of key: string * VerdictDoc list
        | BlockerStates of key: string * BlockerStateDoc list
        | BoardStatuses of key: string * BoardStatusDoc list
        | ExitCodes of key: string * ExitCodeDoc list
        | ReleaseColumns of key: string * ReleaseColumnDoc list
        | WavePolicy of key: string * WavePolicyDoc
        | ReviewPolicy of key: string * ReviewPolicyDoc
        | LifecyclePolicy of key: string * LifecyclePolicyDoc
        | LedgerPolicy of key: string * LedgerPolicyDoc
        /// The snapshot document's SHAPE — a schema string and its top-level keys. The one case carrying
        /// a scalar beside its list: the shape IS schema-plus-keys, and folding the schema in as a
        /// one-member `keys` entry would misdescribe it to every reader of the emitted JSON.
        | SnapshotShape of key: string * schema: string * keys: SnapshotKeyDoc list

    /// The facts document's schema version — a fact about the document's shape, so it lives with the
    /// document and not with the writer that renders it (#1027).
    ///
    /// `ProtocolTests` pins it against `factsDocument`'s key list, so a key added without a bump reds a
    /// test rather than relying on the adder to remember. The pin forces the decision; it does not make
    /// it — a schema derived from its own content would bump on a rename and sit still on a semantic
    /// change.
    ///
    /// NOT `[<Literal>]`, deliberately: F# requires a literal's VALUE in the signature (FS0034), so the
    /// string would be spelled here as well as in `Protocol.fs` — a second copy, in the change whose
    /// whole subject is second copies. The compiler does check the two match, so it could not drift
    /// silently; it would simply make a schema bump a two-file edit for a constant nothing consumes at
    /// compile time. That is the ceremony this item exists to delete, so it is not re-introduced for a
    /// property nothing here uses.
    val factsSchema: string

    /// THE INVENTORY — every fact the document states, under its key, in document order.
    ///
    /// This was `Snapshot.renderFacts`'s positional parameter list: the inventory of facts, hand-kept in
    /// the Cli across three files, for facts this module owns (#1027). The writer folds this list, so the
    /// order here IS the JSON's key order and there is no second list to keep in step.
    val factsDocument: FactSection list
