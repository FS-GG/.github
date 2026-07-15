namespace FS.GG.Coord.GitHub

/// THE READ PATH (ADR-0040 Phase A).
///
/// Every read the coordination client makes, as a function that CANNOT return an empty answer it did not
/// actually observe. That sentence is the whole design. In bash each of these was a `$( )` whose failure
/// mode was the empty string, and the incident record is the list of places where an empty string was
/// then read as a fact: an unreadable board became an empty board (#344), a rate-limited item lookup
/// became "not on board" (#421, with a remediation that CREATED A DUPLICATE), a truncated page of comments
/// became "nobody holds this lock" (#461).
///
/// None of those are expressible here. Every function returns `IoResult`, and the `Unknown` states the
/// core needs (`BlockerUnknown`, `LivenessUnknown`, `TouchSet.Unreadable`) are produced ONLY where we
/// genuinely looked and genuinely could not tell — never as a fallback for a read that failed.
///
/// THE REST/GRAPHQL LINE IS A CORRECTNESS BOUNDARY, NOT A PERFORMANCE ONE. GraphQL is metered by NODES
/// (5,000 pt/hr, shared by the whole fleet, first to die under fan-out — #418); REST is metered by
/// REQUESTS (5,000/hr, one point each, 304s free). So: Projects v2 goes on GraphQL because it has no REST
/// API, and EVERYTHING ELSE goes on REST — above all the lock. **A lock may never live on the budget that
/// dies first** (ADR-0034 §3, re-ratified by ADR-0040 C4).
module Reads =

    open FS.GG.Coord.Types
    open Errors
    open Transport

    /// A live claim marker, as it sits on the issue.
    ///
    /// `Id` is the COMMENT ID, and it is the whole lock. GitHub issues comment ids from ONE server-side
    /// sequence, so "the lowest live marker id wins" is a total order that every racer observes
    /// identically — a real compare-and-swap with a real linearisation point. The CAS is sound and it is
    /// not being redesigned here; ADR-0040 C4 says so explicitly. This port changes the LANGUAGE, not the
    /// SUBSTRATE.
    type Marker =
        { Id: int64
          Worker: WorkerId
          Session: SessionId option
          /// Seconds since the marker was last heartbeated, by the SERVER's clock (`updated_at`).
          AgeSeconds: int
          /// The board column this claim overwrote, so releasing it can put that column back rather than
          /// guessing `Ready` (#481). A value nobody recorded cannot be restored.
          PreviousStatus: BoardStatus option
          /// The raw comment body — kept so that a heartbeat can rewrite the WHOLE marker without
          /// forgetting a field it never parsed (#550).
          Raw: string }

    /// The claim markers on an issue, in comment-id order (lowest first — the winner is `List.head`).
    ///
    /// **NEVER CONDITIONAL, AND NEVER CACHED.** This is the one read that may not carry an `If-None-Match`
    /// and may not be served from the scan cache. A 304 serving a body captured before a marker was posted
    /// would report zero comments and hide a live lock — a failed read wearing an empty set's clothes,
    /// which is #461 one layer up, inside the mechanism built to prevent it.
    ///
    /// A malformed page is an ERROR, not an empty list. `gh` exits 0 on a truncated page and `jq` prints
    /// nothing, and the empty string that fell out of that pipeline is precisely how a live claim became
    /// invisible. Guessing the lock state from a failed read is the one thing a lock may never do.
    val markers: transport: IGitHubTransport -> owner: string -> repo: string -> number: int -> IoResult<Marker list>

    /// Has this marker's lease lapsed?
    ///
    /// A NEGATIVE age means the marker's timestamp could not be read, and it is NOT stale. Reading an
    /// unreadable age as an expired lease would reap a live claim on the strength of a field we failed to
    /// parse.
    val isStale: leaseMinutes: int -> marker: Marker -> bool

    /// THE CAS's WINNER: the lowest-id marker whose lease is still live.
    ///
    /// One function, in one place, because #485 is what happens otherwise — "who holds this?" computed in
    /// five places and agreeing in none. Comment ids come from a single server-side sequence, so this is a
    /// total order every racer observes identically.
    ///
    /// It SORTS its input rather than assuming it is ordered. A rule that depends on an invariant it does
    /// not enforce has a silent failure mode, and this one's is that two racers compute different winners
    /// and both believe they hold the lock — the exact outcome the CAS exists to prevent.
    val winner: leaseMinutes: int -> markers: Marker list -> Marker option

    /// An issue's body — the touch-set lives in it.
    ///
    /// Returns the raw body. The caller parses it with `TouchSet.parse`, and the distinction that matters
    /// is made THERE: a body we read that has no `Paths:` line yields `Undeclared` (an omission), a body we
    /// read that says `Paths: none` yields `DeclaredNone` (a decision), and a body we COULD NOT READ yields
    /// `Unreadable` — which is not the same fact as either, and is the reason this returns `IoResult`
    /// rather than a string with an empty fallback.
    val issueBody: transport: IGitHubTransport -> owner: string -> repo: string -> number: int -> IoResult<string>

    /// Resolve what a `Blocked by` ref actually points at.
    ///
    /// **OVER REST, DELIBERATELY.** GraphQL has `issueOrPullRequest`, which would answer this in one
    /// batched query — and that is the wrong budget. REST has its own, and `GET repos/{o}/{r}/issues/{n}`
    /// serves PULL REQUESTS TOO (a PR is an issue in REST), carrying `pull_request.merged_at`. So one cheap
    /// call per unresolved ref answers both kinds AND distinguishes MERGED from CLOSED.
    ///
    /// MERGED IS NOT PEDANTRY, IT IS THE BUG (#476). An issue's state is OPEN | CLOSED, but a PR's is
    /// OPEN | CLOSED | MERGED — so a rule that clears only on CLOSED unblocks when the blocking PR is
    /// ABANDONED and blocks forever once it is FINISHED. The gate opened exactly when the work was thrown
    /// away and shut exactly when it was done.
    ///
    /// A failed lookup yields `BlockerUnknown`, which BLOCKS. "I could not look" is not "I looked and it is
    /// fine" — and the safe direction on a lock is always to hold it.
    val blockerState: transport: IGitHubTransport -> owner: string -> repo: string -> number: int -> IoResult<BlockerState>

    /// Is there an OPEN pull request on this item's own `item/<n>-*` branch?
    ///
    /// This is #581, and it is the difference between a lease and a life. Lease expiry is EVIDENCE of
    /// abandonment, never PROOF, and its false positive is systematic: work that simply takes longer than
    /// the lease. An open PR on the item's branch is the worktree protocol's own artifact and is
    /// server-side proof of life — so a reaper that ignores it collects the claims of workers who are
    /// visibly, demonstrably still working.
    ///
    /// COST IS THE DESIGN QUESTION, and #581 says so. REST, never GraphQL — and only on the ONE item about
    /// to be offered or reaped, never on every candidate in a scan.
    ///
    /// A failed read yields `LivenessUnknown`, NOT "no PR". That distinction is what stops a transient 5xx
    /// from reaping live work.
    val prAlive: transport: IGitHubTransport -> owner: string -> repo: string -> number: int -> IoResult<Liveness>

    /// What the meter says right now.
    ///
    /// DELIBERATELY NOT `Budget.Meter`. That type's `Cost` is *what one query cost*, read off a GraphQL
    /// response — and the `/rate_limit` endpoint has no such notion. Reusing it here would mean publishing
    /// a field called `Cost` holding `limit - remaining`, which is a number that reads as one thing and
    /// means another. In a codebase whose entire thesis is that a value must not be able to masquerade as a
    /// different fact, that is not a shortcut worth taking.
    type RateLimitSnapshot =
        { Remaining: int
          Limit: int }

    /// An issue's REST INTEGER ID — `.id`, not its number.
    ///
    /// `child` attaches by this id, never by the number: two repos can each have an issue #7, and posting a
    /// number where an id belongs attaches the wrong issue silently.
    val restId: transport: IGitHubTransport -> owner: string -> repo: string -> number: int -> IoResult<int64>

    /// The rate-limit meter.
    ///
    /// FREE — this read does not spend the budget it reports, which is what makes "back off until the
    /// reset" a strategy rather than a guess. It is billed to NEITHER counter, and the corpus depends on
    /// that.
    val rateLimit: transport: IGitHubTransport -> IoResult<RateLimitSnapshot>

    /// A pull request's HEAD ref (`.head.ref`), e.g. `item/42-the-thing`.
    ///
    /// FAILS CLOSED (#322): an unreadable head ref is an ERROR, never an empty string. verify-paths uses
    /// it to decide WHICH issue a PR implements, and guessing that from a failed read would stamp a
    /// touch-set verdict on a subject nobody identified.
    val prHeadRef: transport: IGitHubTransport -> owner: string -> repo: string -> pr: int -> IoResult<string>

    /// A pull request's changed files (`pulls/{n}/files`), paginated.
    val prFiles: transport: IGitHubTransport -> owner: string -> repo: string -> pr: int -> IoResult<string list>

    /// The FIRST issue a pull request declares it closes (`closingIssuesReferences`), if any.
    ///
    /// `Ok None` means it closes nothing by that record — a real answer (the PR may implement an item by
    /// its branch name instead). It is distinct from a failed read, which is an `Error`.
    val prClosingRef: transport: IGitHubTransport -> owner: string -> repo: string -> pr: int -> IoResult<Ref option>

    /// Every OPEN issue in a repo, with its body — the claim-scan candidate set.
    ///
    /// PAGINATED, AND UNCONDITIONAL. A lock has no hundred-issue limit: a first page read as the whole set
    /// is a claim scan that cannot see the markers past it, and would report those items free.
    ///
    /// The bodies ride along because they are free here — one list read serves both the marker scan and the
    /// touch-set extraction, where two reads per item would double the REST cost of the scheduling loop.
    val openIssues: transport: IGitHubTransport -> owner: string -> repo: string -> IoResult<(int * string) list>
