namespace FS.GG.Coord.GitHub

/// THE DONE-STAMP — a precondition engine with a write at the end of it.
///
/// This is not "another board write". `done --flip` is the one surface that says *this work is finished*,
/// and every defect in its history is a case where it said that about work which was not — or refused to
/// say it about work which was. Both directions are expensive, and the second is worse than it looks:
///
/// > A red that fires **reproducibly, on correct work** is the fastest way to teach every worker that red
/// > stamps are noise — which is precisely the credibility this stamp exists to have. (#596, quoted by #600)
///
/// THE DESIGN, AND IT IS THE WHOLE MODULE: **the preconditions are PURE.**
///
/// `verify` is a total function from FACTS to a verdict. It performs no IO, so it cannot mistake a failed
/// read for a satisfied precondition — which is exactly how this family of bugs was born. The IO is one
/// query (`facts`), and everything that DECIDES is downstream of it and testable without a network.
///
/// FOUR INCIDENTS, AND EACH ONE IS A CASE IN A TYPE HERE:
///
/// **#600 — the stamp had no green path for work resolved WITHOUT a PR.** An item legitimately closed with
/// no code change in this repo at all — obsolete, duplicate, resolved-elsewhere, a decision item whose
/// deliverable is an ADR somewhere else — stamped **RED**, reproducibly, on correct work. `pnext-item` §4
/// *instructs* workers to transplant a duplicate's detail into the surviving issue, and then the tool called
/// them liars for doing it. `ResolvedWithoutPr` is the green path — and it takes EVIDENCE, because a free
/// pass would be no stamp at all.
///
/// **#583 — an OPEN sub-issue does not stop a parent being stamped Done.**
///
/// **#613 — the roll-up stamped a parent `Done` on the BOARD and never CLOSED THE ISSUE.** So the upward
/// climb died after one hop, and the board and the issue disagreed about the same work — live at
/// FS.GG.Rendering#361: board `Done`, issue OPEN, all four children closed.
///
/// **#614 — the roll-up CLOSED an open parent because its only child was a PARTIAL fix.** This is the
/// sharpest one, and it is why `Discharge` exists. FS.GG.SDD#350 needed an ADR *and* a code change; a
/// worker split out the disclosure-only half as #398, whose body said **in bold** that it *does NOT complete
/// FS.GG.SDD#350*, and linked it as a sub-issue exactly as the recipe instructs. When #398 merged, the roll-up saw
/// "all children complete", CLOSED THE PARENT, and climbed a hop further to stamp an epic Done over it.
/// **None of the parent's actual work existed.** The roll-up assumed children PARTITION their parent. They do not, and
/// whether they do is a fact only the child's author knows — so it is an ARGUMENT, not an inference.
module Done =

    open FS.GG.Coord.Types
    open Errors
    open Transport

    /// What GitHub's OWN RECORD says closed this issue — never what the prose claims.
    type Closure =
        /// A merged pull request closed it, and GitHub recorded the closing act.
        ///
        /// TWO SOURCES, BECAUSE ONE IS NOT ENOUGH. `closingIssuesReferences` sees the `Closes #N` in the PR
        /// **body** — but `gh pr create --fill` maps the COMMIT SUBJECT to the PR TITLE, so a worker whose
        /// commit reads `fix: the thing (closes #NNN)` puts the keyword where that field never looks. The
        /// squash commit still closes the issue, so a correct, merged, green PR stamped RED — permanently,
        /// because editing a merged PR's body does not backfill the reference. The `CLOSED_EVENT`'s
        /// `closer` is what actually records the act, and it is checked too.
        | ClosedByPullRequest of pr: int

        /// Resolved with no PR, and with EVIDENCE (#600). Obsolete, duplicate, resolved-elsewhere, a
        /// decision item whose deliverable lives in another repo.
        ///
        /// The evidence is REQUIRED. A green path that took no argument would not be a stamp — it would be
        /// a way of turning the stamp off, and every worker would learn to reach for it.
        | ResolvedWithoutPr of evidence: string

        /// The issue is CLOSED and nothing records why. Not a PR, not a commit, and no evidence offered.
        | ClosedByNobody

        /// The issue is still open. There is nothing to stamp.
        | StillOpen

    /// The sub-issues, as the stamp must see them.
    type Children =
        | NoChildren
        | AllResolved of count: int
        /// #583. A parent with open children is not done, whatever its board column says.
        | SomeOpen of numbers: int list
        /// **WE COULD NOT SEE THEM ALL.** `totalCount` and the nodes we were handed disagree, so the page
        /// was truncated. An unverifiable subject must NOT report green — this is checked BEFORE anything
        /// else, because a truncated read that happened to show only closed children would otherwise sail
        /// straight through.
        | Unverifiable of totalCount: int * seen: int

    /// **DOES THIS CHILD DISCHARGE ITS PARENT?** (#614)
    ///
    /// It is an ARGUMENT because it is a fact only the child's author knows, and no amount of reading the
    /// board can recover it. FS.GG.SDD#398's body said, in bold, that it did NOT complete FS.GG.SDD#350 — and the
    /// roll-up closed the parent anyway, because it inferred completion from "all children are done".
    ///
    /// There is no default. A caller must say which it is, and saying `Partial` is what keeps a parent open.
    type Discharge =
        /// This child completes its parent's scope. The parent may be rolled up.
        | Completes
        /// This child is a PARTIAL fix. **The parent stays open even if this is its only child.**
        | Partial of why: string

    /// Everything the stamp needs, read in ONE query.
    ///
    /// One query, because eight separate reads is eight chances for one of them to fail and be mistaken for
    /// a satisfied precondition — and because it is metered on the budget the whole fleet shares.
    type Facts =
        { Ref: Ref
          State: IssueState
          /// PRs whose body carries a closing keyword for this issue (`closingIssuesReferences`).
          ClosingPrs: int list
          /// The PR named by the issue's own `CLOSED_EVENT` — the record of the closing ACT.
          ClosedByEvent: int option
          Children: Children
          BoardStatus: BoardStatus
          Parent: Ref option }

    /// Read the facts. The only IO in the decision.
    val facts: transport: IGitHubTransport -> board: Board.BoardMap -> ref: Ref -> IoResult<Facts>

    /// **THE PRECONDITIONS, AS A TOTAL FUNCTION.** No IO. It cannot fail open, because it cannot fail.
    ///
    /// `resolvedWithoutPr` is the operator's `--evidence` — `Some reason` asserts *"there is no PR, and here
    /// is why this is finished anyway"* (#600). It is not consulted unless the record genuinely shows no
    /// closing PR, so a worker who supplies it on an item that DID have one still gets the PR named in the
    /// stamp rather than their own prose.
    ///
    /// ORDER IS LOAD-BEARING. Truncation is checked first: an unverifiable subject must not report green,
    /// and a truncated page that happened to show only closed children would otherwise pass every test
    /// below it.
    val verify: resolvedWithoutPr: string option -> facts: Facts -> Verdict<Closure>

    /// The one-line stamp a worker reads. Green stamps and red stamps do not look alike, on purpose.
    val render: ref: Ref -> verdict: Verdict<Closure> -> string

    /// The children an epic's BODY declares that its sub-issue GRAPH does not contain — the
    /// EPIC-UNLINKED-CHILD set, shared by `lint` and the `done --flip` rollup so the rule is defined ONCE
    /// (#485). Takes the already-read body and the graph's refs, so a caller that has them pays no re-read.
    /// A body-cited ref that resolves to a PULL REQUEST is dropped (a PR can never be a sub-issue, #346); a
    /// ref the probe cannot resolve is KEPT (fail closed, #266 — "I could not check" is not "it is a PR").
    /// Returns the canonical `owner/repo#n` refs, sorted.
    val bodyUnlinkedChildren:
        transport: IGitHubTransport ->
        owner: string ->
        repo: string ->
        body: string ->
        graphRefs: string list ->
            IoResult<string list>

    /// What happened to a parent when we climbed to it.
    type RollUp =
        /// Stamped Done AND CLOSED (#613 — the board and the issue must not disagree about the same work).
        | ParentClosed of Ref
        /// Left open, and here is why. A `Partial` child, an open sibling, an unverifiable child set.
        | ParentLeftOpen of Ref * reasons: string list
        /// There is no parent. The climb is over.
        | NoParent

    /// Climb the parent chain, stamping and CLOSING what is genuinely finished.
    ///
    /// `discharge` is the CHILD's declaration about the parent immediately above it (#614). It is required,
    /// it has no default, and `Partial` stops the climb dead — which is the whole point: a parent whose only
    /// child was a partial fix stays open, and the epic above it is never stamped over.
    ///
    /// Each hop it closes, it closes fully — board column AND issue state (#613). A parent stamped `Done` on
    /// a board while its issue stays OPEN is two systems disagreeing about the same work, and the climb dies
    /// there anyway because the next hop reads an open child.
    ///
    /// Bounded to ten hops. A parent chain longer than that is a cycle, and a cycle is a bug — one that
    /// would otherwise climb forever.
    val rollUp:
        transport: IGitHubTransport ->
        board: Board.BoardMap ->
        worker: string ->
        parent: Ref ->
        discharge: Discharge ->
            IoResult<RollUp list>
