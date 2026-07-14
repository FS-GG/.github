namespace FS.GG.Coord.GitHub

/// THE BOARD SCAN, AND THE SNAPSHOT THE ENGINE DECIDES FROM.
///
/// This is the piece ADR-0034 said the IO layer was FOR:
///
/// > `FS.GG.Coord.GitHub` … is required only for the Phase 3 flip, **when the engine must fetch its own
/// > state**.
///
/// Until now it could not. `fsgg-coord-engine decide` reads a snapshot on stdin and the bash client is the
/// only thing that can produce one — so the typed engine has been a decision procedure with no way to
/// observe the thing it decides about. `scan` closes that loop: the engine reads the board itself and emits
/// the very document `decide` consumes, so `fsgg-coord-engine scan | fsgg-coord-engine decide` is a
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
          IsPullRequest: bool }

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

    /// What the scan cost, and what it could not do — so a caller can say so rather than imply it.
    type Receipt =
        { Candidates: int
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
