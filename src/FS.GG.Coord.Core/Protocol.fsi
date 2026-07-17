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
    val landableExitCodes: ExitCodeDoc list

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
