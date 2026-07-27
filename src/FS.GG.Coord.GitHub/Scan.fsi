namespace FS.GG.Coord.GitHub

/// THE BOARD SCAN, AND THE SNAPSHOT THE ENGINE DECIDES FROM.
///
/// This is the piece ADR-0034 said the IO layer was FOR:
///
/// > `FS.GG.Coord.GitHub` … is required only for the Phase 3 flip, **when the engine must fetch its own
/// > state**.
///
/// Until now it could not. `scripts/fsgg-coord decide` reads a snapshot on stdin and the bash client is the
/// only thing that can produce one — so the typed engine has been a decision procedure with no way to
/// observe the thing it decides about. `scan` closes that loop: the engine reads the board itself and emits
/// the very document `decide` consumes, so `scripts/fsgg-coord scan | scripts/fsgg-coord decide` is a
/// complete, self-sufficient scheduling pass with no bash anywhere in it.
///
/// **THE COST MODEL IS THE DESIGN.** Projects v2 has no server-side item filter, so "list the board" is
/// inherently a full scan, and the only lever is what you select PER ITEM. `fieldValueByName` is a
/// RESOLVER field — one value, no node multiplication. `gh project item-list` instead nests
/// `fieldValues(first: 100)` inside `items(first: N)`, which is O(items × 100) nodes: **measured, a thrifty
/// full scan of the live 640-item board is 7 pages × 1 point = 7 points, while `gh project item-list` costs
/// 6 points to read FIVE items.** That is the whole difference between a fleet that can afford to look at
/// the board and one that cannot (#418).
///
/// **BLOCKERS ARE RESOLVED FROM THE SCAN ITSELF, FOR FREE.** The scan already carries every board item's
/// state, so a `Blocked by` edge pointing at another board item is answered from an in-memory index —
/// zero additional cost. Only OFF-BOARD refs need a read, and those go over REST (a PR is an issue in REST,
/// so one cheap call answers both kinds and distinguishes MERGED from CLOSED — #476).
module Scan =

    open FS.GG.Coord.Types
    open Errors
    open Transport

    /// One row of the board, as the scan sees it.
    type Row =
        { Ref: Ref
          Title: string
          Status: BoardStatus
          /// The `Blocked by` field's raw TEXT. Projects v2 has no list type, so this is free text and the
          /// structure has to be recovered from it — which is the parse family (#435, #497, #548) and the
          /// reason `Blockers.parse` lives in the core.
          BlockedByRaw: string
          /// The ISSUE's state, which is not the board column. When they disagree the issue wins (#520).
          State: IssueState
          /// A PR on the board is not an item of WORK. #641: they were listed as issues, so a duplicate
          /// check read a PR as "already filed" and suppressed a real finding.
          IsPullRequest: bool
          /// The `Class` column as OBSERVED — the PROJECTION `reconcile` writes, read back so that
          /// `CLASS-PROJECTION-LAG` can fire on disagreement and RETIRE on agreement (.github#1588).
          ///
          /// It costs nothing. `fieldValueByName` is a resolver field, so this is the same 7-point full
          /// scan the cost model above measures — one more resolved value per item, no node multiplication.
          ///
          /// `None` COLLAPSES THREE FACTS ON PURPOSE: the row is unclassed, the column holds a word this
          /// engine does not speak, or the project has no `Class` field at all. That collapse is safe here
          /// and nowhere else, because this value is never a verdict — it is one half of a comparison whose
          /// authority is the ITEM'S OWN TEXT (ADR-0066). All three mean "there is no projection here to
          /// trust", and the remedy is identical: write what the item declares. Contrast `TouchSet`, where
          /// exactly this collapse WAS the bug (#496) — there the board was the only source, so "absent"
          /// and "unreadable" had to be told apart or a gate would report an omission about an item nobody
          /// looked at. Here the source is the body, and `lint` reads it directly.
          BoardClass: ItemClass option

          /// The `Phase` column as OBSERVED (.github#1598) — the third rank input, and the column whose
          /// invisibility to the scheduler is the whole subject of that item.
          ///
          /// It costs nothing, on `BoardClass`'s terms exactly: another `fieldValueByName` resolver field
          /// on a node already selected, so the 7-point full scan is unchanged.
          ///
          /// `None` collapses the same three facts for the same reason — unset, an unspoken word, or no
          /// such field on this project. Here the collapse is safe because the consumer is an ORDERING,
          /// not a verdict: all three mean "no phase evidence", and `Rank` sorts such a row last. Nothing
          /// is refused, reported or written on the strength of this being `None`.
          Phase: Phase option

          /// When the ISSUE was created — the only age the board can supply (.github#1598).
          ///
          /// THE INSTANT, NOT A DAY COUNT, and the distinction is load-bearing because this record is
          /// CACHED. An `ageDays` written to disk is wrong by the cache's own lifetime the moment it is
          /// read back; an instant is not. The count is derived once, where the clock is actually read
          /// (`Client.enrichBoardFacts`), and only then does it reach `Item.AgeDays`.
          ///
          /// `None` when the field was absent or did not parse — never a zero age, which would be the
          /// YOUNGEST possible answer and the one that can never trigger starvation escalation.
          CreatedAt: System.DateTimeOffset option }

    /// Scan the whole board. Paginated, cursor-based, and CACHED (90s, both invariants — `Cache`).
    ///
    /// `intent` decides whether the cache may serve this read at all. A scheduler may be served a stale
    /// board (the worst it can do is offer an item somebody just claimed, and the claim CAS settles that);
    /// a RECONCILER never may, because its whole job is to say what is true right now.
    ///
    /// **A FAILED SCAN IS NEVER CACHED, AND NEVER RETURNS AN EMPTY BOARD.** That is #344, and it is enforced
    /// in `Cache.putScan` at the WRITE, because the caller that just failed to read the board is holding an
    /// empty list either way.
    val board:
        transport: IGitHubTransport ->
        cache: Cache.ReadIntent ->
        owner: string ->
        title: string ->
        projectNumber: int ->
            IoResult<Row list>

    /// How many off-board blocker refs we are willing to resolve over REST in one pass.
    ///
    /// The cap is ANNOUNCED, never silent. A silent cap would leave the overflow blocked-forever with no
    /// trace — an item reported blocked by something nobody ever looked up, which is indistinguishable from
    /// an item that is genuinely blocked.
    [<Literal>]
    val OffBoardCap: int = 60

    /// Board rows scoped to a `--repo`, and whether the request named a repo the board never heard of.
    ///
    /// THE `--repo` FILTER IS THIS FUNCTION'S MONOPOLY (#979). Every verb that takes `--repo` scopes
    /// through `scope`, and `scripts/check-repo-filter-monopoly.py` fails the build on a hand-rolled
    /// copy. The filter used to be written out per verb — five times, in `snapshot`, `ready`, `who`,
    /// `inbox` and `lint` — which is exactly the shape #978 had just fixed one layer down for repo
    /// RESOLUTION, and `Options.fs` names the reason in as many words:
    ///
    /// > IT LIVES IN THE PARSER BECAUSE THAT IS THE ONLY PLACE EVERY VERB REACHES (#962). It used to
    /// > live in `Client`, applied per-verb at a dispatch site, and **being left out of that list is a
    /// > silent fail-open**.
    ///
    /// The class had already produced four instances — #381, #446, #962 and #979 — and each repair
    /// added the missing verb rather than removing the list. A funnel plus a gate removes the list.
    type Scoped =
        { /// The rows in scope. Identical to the input when no `--repo` was given.
          Rows: Row list

          /// `Some msg` ⇒ `--repo` named a repo NO row carries: a typo, or a repo with no board items
          /// yet. The two are indistinguishable from here, so the message says both and the EXIT IS
          /// UNCHANGED (#979 decision (d)).
          ///
          /// REPORT, DO NOT GATE. `ready` is a TRUTH read — an empty board is a real answer, and
          /// erroring would break the reconciler that consumes it. #266 licenses exactly this: an
          /// out-of-scope subject must be REPORTED, not silently skipped. What a green exit MEANS is
          /// now stated rather than implied; that a fleet reading exit codes still cannot tell is the
          /// accepted residual, named on #979 rather than left as an omission nobody noticed.
          Advisory: string option }

    /// Scope rows to a `--repo`, and say so when the request names no row. See `Scoped`.
    ///
    /// `repo` is expected ALREADY RESOLVED — short-id → repo name is the parser's job (#962/#978), and
    /// re-resolving here would be the second copy of the bug this function exists to end.
    ///
    /// The known-repo set is derived from the ROWS IN HAND, never from a roster. That is #266's own
    /// corollary — *compare against reality, not against a record of reality* — and #933/#958 is the
    /// org's standing verdict on hand-maintained rosters. It costs nothing (the caller already has the
    /// rows), it cannot go stale, and it means the `kind: client` shim constraint that keeps
    /// `registry/repos.yml` out of the engine (case 13 §6c / #381) never binds.
    ///
    /// **NO ADVISORY OVER AN EMPTY BOARD**, and that is the fail-closed half. With zero rows there is
    /// no known-repo set to compare against, so "no row names `X`" would be a confident claim about a
    /// board nobody could see — #266's own defect, inside the fix for it. An empty result is then
    /// attributable to the board, not to the scope, and the caller's empty output already says so.
    val scope: repo: string option -> rows: Row list -> Scoped

    /// The board's `Blocked by` graph, ready for `Blockers.cycles` — resolved from the scanned ROWS ALONE,
    /// with no transport read (#1090). A ring runs only through on-board items, whose OPEN/CLOSED state is
    /// already in `rows`; an off-board blocker draws no ring edge, so it is a `BlockerUnknown` PLACEHOLDER,
    /// not a resolved verdict. Accurate for on-board refs only — the exact slice `Blockers.cycles` reads;
    /// not a substitute for `snapshot`'s fully-resolved blocker set. Total and pure.
    val blockerGraph: rows: Row list -> (Ref * Blocker list) list

    /// What the scan cost, and what it could not do — so a caller can say so rather than imply it.
    type Receipt =
        { Candidates: int
          /// `scope`'s advisory for this scan's `--repo`, carried out so `scan`'s caller can SAY the
          /// repo named nothing rather than print `0 candidate(s)` over a full board and imply it.
          RepoAdvisory: string option
          /// Off-board blocker refs resolved over REST.
          OffBoardResolved: int
          /// Off-board refs we did NOT resolve because the cap was hit. They stay `BlockerUnknown`, which
          /// BLOCKS — the safe direction — and they are COUNTED so the caller can say the cap was reached.
          OffBoardSkipped: int
          /// Candidates whose body could not be read. They are NOT dropped: they arrive as
          /// `TouchSet.Unreadable`, because an item that silently vanishes from the engine's world cannot
          /// be offered AND cannot be passed over with a reason.
          BodiesUnreadable: int }

    /// Assemble the snapshot `decide` consumes: `fsgg.coord.snapshot/1`.
    ///
    /// For every candidate this reads the issue BODY (the touch-set) and its claim MARKERS — both REST,
    /// both on the budget that survives, and the markers UNCONDITIONALLY, because a lock may never be read
    /// from a cache.
    ///
    /// A body it cannot read becomes `bodyUnreadable`, NOT an empty body. `TouchSet.parse ""` answers
    /// `Undeclared` — a confident OMISSION about an item nobody looked at — and the engine would then
    /// schedule every other item against a surface it cannot see.
    val snapshot:
        transport: IGitHubTransport ->
        rows: Row list ->
        repo: string option ->
        allowBacklog: bool ->
        limit: int option ->
        leaseMinutes: int ->
            IoResult<string * Receipt>
