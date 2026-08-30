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
    type DuplicateCandidate = { Number: int; State: string; Title: string; Body: string; IsPullRequest: bool }
    /// Complete, uncached all-state inventory for intake. Unlike `issues`, pull requests remain
    /// candidates.
    ///
    /// **COMPLETE MEANS EVERY PAGE, AND IT IS `Transport.Send` THAT DELIVERS THAT** — it follows `Link:
    /// rel="next"` and concatenates, erroring on any continuation it cannot fetch or merge. What it does
    /// NOT do is clear `NextLink` on the merged response, so that flag reports *this collection
    /// paginated* and never *pages remain unread*. Reading it as the latter is `.github#2735`: in
    /// `FS-GG/.github`, whose issue listing always paginates, a fully merged inventory was refused on
    /// every call, and `intake apply` — the one filing path that validates before it creates — could not
    /// file there at all.
    ///
    /// The fail-closed refusal is preserved and asked of the SHAPE instead: a `rel="next"` link is only
    /// emitted over a FULL page, so a merged body carries strictly more elements than the `per_page` this
    /// read requested, and a body that does not is a set this cannot account for. That refusal names
    /// itself a FAILED READ, because the one answer this must never be confused with is "read it, found
    /// nothing" — the seam that decides whether a new row duplicates an existing one (#266).
    ///
    /// **THAT SHAPE RULE IS A HEURISTIC ABOUT THE SERVER, NOT A PROPERTY OF THE TYPE, AND IT IS KNOWN TO
    /// BREAK.** A complete inventory is REFUSED whenever its merged total is `<=` the requested `per_page`
    /// and page one advertised a continuation — demonstrated against the real transport, so do not read it
    /// as an invariant. It is kept because every break is in the FAIL-CLOSED direction (it refuses a good
    /// read; it never answers with a short one — no `Ok` over an incomplete inventory is reachable) and
    /// because GitHub does not emit that shape: it derives `Link` from a count and never advertises an
    /// empty successor. The residual window is a `<=`-one-page repository whose set shrinks mid-read, or a
    /// proxied endpoint. See `Reads.fs` for the measurements.
    val duplicateCandidates: transport: Transport.IGitHubTransport -> owner: string -> repo: string -> Errors.IoResult<DuplicateCandidate list>

    open FS.GG.Coord
    open FS.GG.Coord.Types
    open Errors
    open Transport

    /// **DID THIS `first: N` CONNECTION RETURN EVERYTHING IT HAS?** (`.github#2535`)
    ///
    /// `Ok ()` is the load-bearing value and it means *measured completeness*. Only after it may a caller
    /// read an absence out of `nodes` and mean it. It is reached two ways, and only two:
    ///
    /// - the connection reported a `totalCount` no larger than the number of `nodes` it returned; or
    /// - the connection came back SHORT of `window`, which is proof on its own — `first: N` returns
    ///   `min(N, available)`, so a page below N had nothing left for the window to hide.
    ///
    /// Everything else is `Error(Malformed …)`: a `totalCount` larger than what was returned (the defect
    /// itself), a FULL window with no readable `totalCount` (a complete set of exactly N is
    /// indistinguishable from a truncated one), a missing or non-array `nodes`, a connection that is not an
    /// object at all. Each leaves completeness UNKNOWN, and an unknown completeness may not be spent as a
    /// known one.
    ///
    /// `window` is the document's own `first: N` and MUST equal it — too low refuses pages it should
    /// accept, too high accepts pages it should refuse. `what` names the connection in the refusal,
    /// `subject` the thing being read.
    ///
    /// This is the `totalCount` half of `.github#2535`'s repair; the other half is a `pageInfo` cursor walk,
    /// used where the tail must actually be fetched rather than refused (`Board.bootstrap`'s project list,
    /// `Board.externalItemId`). It is exposed because
    /// `Board` compiles after this module and shares it, and because a second copy of a completeness check
    /// is precisely the drift `.github#2535` is about: five connections in two files, none of them asking.
    val connectionComplete:
        subject: string -> what: string -> window: int -> connection: System.Text.Json.JsonElement -> IoResult<unit>

    /// A live claim marker, as it sits on the issue.
    ///
    /// `Id` is the COMMENT ID, and it is the whole lock. GitHub issues comment ids from ONE server-side
    /// sequence, so "the lowest live marker id wins" is a total order that every racer observes
    /// identically — a real compare-and-swap with a real linearisation point. The CAS is sound and it is
    /// not being redesigned here; ADR-0040 C4 says so explicitly. This port changes the LANGUAGE, not the
    /// SUBSTRATE.
    type Marker =
        {
            Id: int64
            Worker: WorkerId
            Session: SessionId option
            /// Seconds since the marker was last heartbeated, by the SERVER's clock (`updated_at`).
            AgeSeconds: int
            /// The board column this claim overwrote, so releasing it can put that column back rather than
            /// guessing `Ready` (#481). A value nobody recorded cannot be restored.
            PreviousStatus: BoardStatus option
            PathRepo: string option
            /// The agent-contract digest captured when this work was dispatched. Renewals preserve the
            /// marker's value; `None` is a legacy claim that recorded no dispatch contract.
            AgentContract: string option
            /// The raw comment body — kept so that a heartbeat can rewrite the WHOLE marker without
            /// forgetting a field it never parsed (#550).
            Raw: string
        }

    /// THE MARKER READ, WITH ITS OWN COMPLETENESS ATTACHED (.github#1668).
    ///
    /// A `Marker list` on its own cannot tell *"this issue carries no claim"* apart from *"this issue carries
    /// comments I could not read, and any one of them may have been the claim"*. `who` rendered the second as
    /// the first — `UNCLAIMED — In progress with NO claim marker`, plus an accusation that somebody was
    /// working outside the protocol — which is the fail-open direction on the one read that separates
    /// workers. This type is what makes the difference sayable.
    type MarkerScan =
        {
            /// The claim markers, lowest comment id first — the CAS's total order, winner at the head.
            Markers: Marker list
            /// One entry per comment the scan could NOT classify, each naming which comment and why: a comment
            /// with no readable `body` (it may have been a marker), or a comment whose body IS a marker but
            /// which carries no numeric `id` to order it by.
            ///
            /// **EMPTY IS THE LOAD-BEARING VALUE.** Only an empty list licenses "there is no claim here".
            /// Non-empty means `Markers` is a LOWER BOUND, and a caller that reports absence off it is
            /// manufacturing an answer out of what it could not read (#266, .github#1794).
            Unreadable: string list
        }

    /// The `BoardStatus` for a Projects v2 Status option NAME (case-insensitive), or for a marker's decoded
    /// `prev=` value — the two callers that turn a column's human name back into the type. A name we do not
    /// recognise, or the empty string, is `None`: "this records no restorable column" (#481), never a guess.
    /// It is ONE parser so the claim that records a column and the read that restores it cannot drift.
    val statusOfName: name: string -> BoardStatus option

    /// The claim markers, plus every comment the scan could not classify.
    ///
    /// **NEVER CONDITIONAL, AND NEVER CACHED.** This is the one read that may not carry an `If-None-Match`
    /// and may not be served from the scan cache. A 304 serving a body captured before a marker was posted
    /// would report zero comments and hide a live lock — a failed read wearing an empty set's clothes,
    /// which is #461 one layer up, inside the mechanism built to prevent it.
    ///
    /// A malformed page is an ERROR, not an empty list. `gh` exits 0 on a truncated page and `jq` prints
    /// nothing, and the empty string that fell out of that pipeline is precisely how a live claim became
    /// invisible. Guessing the lock state from a failed read is the one thing a lock may never do.
    ///
    /// Reporting callers keep the whole scan and can render an honest `Undetermined`; decision and write
    /// callers must pass it through `requireCompleteMarkerScan`.
    val markerScan: transport: IGitHubTransport -> owner: string -> repo: string -> number: int -> IoResult<MarkerScan>

    /// Raw issue/PR comment bodies, in API order; malformed comments fail closed.
    val commentBodies:
        transport: IGitHubTransport -> owner: string -> repo: string -> number: int -> IoResult<string list>

    /// The most recent `limit` comment bodies of an issue, oldest-of-the-window first, in ONE bounded
    /// GraphQL call (.github#2300 repair 2) — unlike `commentBodies`, whose REST pagination is
    /// unbounded (a comment page carries no server-side end short of the whole thread, and the
    /// transport auto-follows every `Link: rel=next` page it is given).
    ///
    /// CHOOSING `limit`: pick the smallest bound the caller's search is expected to comfortably need,
    /// not the largest the caller can afford — a bigger window does not make a "latest match" search
    /// more correct, only slower. `Client.fs`'s `DeliveryRouteCommentWindow = 100` is the concrete
    /// precedent: 100 matches this codebase's own REST `per_page` convention (`commentBodies`,
    /// `markerScan`) and GitHub's usual Relay connection page ceiling, chosen for a search that only
    /// ever wants the LATEST matching marker and is self-correcting (a stale receipt is cleared by
    /// posting a fresh one, which becomes the newest comment and is found on the very next read,
    /// bounded or not) — see that binding's own doc comment for the measured evidence behind it.
    ///
    /// FAIL-CLOSED, NOT FAIL-EMPTY: a caller searching this for the latest matching marker and finding
    /// none must treat that as "not among the last `limit`", never as "does not exist" — those are
    /// different facts, and a route/lock caller that collapses them into "absent" converts a receipt
    /// that is merely OUT OF THE WINDOW into one this function never claimed to have seen. Every current
    /// caller (`Client.fs`'s `readDeliveryRouteVerdict`/`requireCurrentDeliveryRoute`/`deliveryRouteFact`
    /// — confirmed the only three, `grep -rn "recentCommentBodies" src/`) already reads an empty/no-match
    /// result as `Unreadable`/`Stale`, which REFUSES rather than guesses — the safe direction, at the
    /// cost of a real but recoverable stall for a row whose comment volume has outrun the window.
    val recentCommentBodies:
        transport: IGitHubTransport -> owner: string -> repo: string -> number: int -> limit: int -> IoResult<string list>

    /// Require `MarkerScan.Unreadable` to be empty, returning the complete marker list or a malformed-read
    /// error that names every unclassifiable comment.
    ///
    /// This is the gate for every caller that DECIDES or WRITES from the lock. There is deliberately no
    /// public projection that silently discards `Unreadable`: a new caller must choose this fail-closed gate
    /// or handle incompleteness itself.
    val requireCompleteMarkerScan: subject: string -> scan: MarkerScan -> IoResult<Marker list>

    /// Has this marker's lease lapsed?
    ///
    /// A NEGATIVE age means the marker's timestamp could not be read, and it is NOT stale. Reading an
    /// unreadable age as an expired lease would reap a live claim on the strength of a field we failed to
    /// parse.
    val isStale: leaseMinutes: int -> marker: Marker -> bool

    /// THE LEASE-FREE ORDERING RULE: the lowest-id marker, with no liveness judgement applied.
    ///
    /// "Lowest id wins" expressed ONCE, where callers can reach it. It filters nothing, decides no arm, and
    /// is not a lock: comment ids come from a single server-side sequence, so this is a total order every
    /// reader — including a CI job minutes later — computes identically.
    ///
    /// **IT IS NOT `reserver`, AND SUBSTITUTING ONE FOR THE OTHER IS A DEFECT.** `reserver` answers "who
    /// holds this lock", and returns the LIVE winner when there is one. This answers only "which marker is
    /// first". `reap` and `adopt` act exclusively when no live winner exists, so handing either of them a
    /// live holder would break a lock its owner is still standing in (design §4.2).
    ///
    /// It sorts rather than trusting its input's order, for the same reason `winner` does: a rule that
    /// depends on an unenforced invariant fails silently, and this one's failure is two racers computing two
    /// different winners.
    ///
    /// `winner` is this function composed with the staleness filter — one rule with one parameter, which is
    /// why re-deriving either from the other is never necessary.
    val lowestId: markers: Marker list -> Marker option

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

    /// THE MARKER THAT HOLDS THE LOCK, REGARDLESS OF LEASE — the live `winner` if there is one, else the
    /// lowest-id marker whose lease has lapsed.
    ///
    /// `winner` decides IDENTITY (only a live marker answers a heartbeat or loses a CAS); this decides
    /// RESERVATION. A lease is a clock, but a lock is broken only by `reap` (#461/#581): the scheduler must
    /// reserve a stale-but-unreaped claim's touch-set exactly as it reserves a live one, or hand a second
    /// worker the tree its holder is standing in. This is the choice `who` makes classifying a row Held vs
    /// Stale, expressed for the scheduler.
    val reserver: leaseMinutes: int -> markers: Marker list -> Marker option

    /// A worker-to-worker message parsed off an issue comment — the `say` / `inbox` channel.
    type Message =
        { Id: int64
          From: string
          To: string
          At: string
          Text: string }

    /// The `fsgg:msg` messages on an issue, in comment-id order (lowest first).
    ///
    /// **NEVER CONDITIONAL**, exactly like `markers`: a 304 could serve a comments page captured before a
    /// `say`, hiding a message. A message is not a lock, so the failure mode differs — a message with no
    /// orderable id, or with no parseable `from`/`to`, is DROPPED rather than (as a marker must) failing
    /// closed and blocking — but the read itself is as unconditional as the lock's, because a lost message
    /// is still a coordination failure. A malformed page is an error, never an empty mailbox (#461).
    val messages: transport: IGitHubTransport -> owner: string -> repo: string -> number: int -> IoResult<Message list>

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
    val blockerState:
        transport: IGitHubTransport -> owner: string -> repo: string -> number: int -> IoResult<BlockerState>

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
    ///
    /// When there is no open PR it asks one more thing (#1055): is a pushed `item/<n>-*` BRANCH on the
    /// remote? A branch with no PR is `LeaseExpiredBranchPushed` — proof of life during §3, before §5 opens
    /// the PR. That probe fails closed too: unreadable is `LivenessUnknown`, never `LeaseExpiredNoPr`.
    val prAlive: transport: IGitHubTransport -> owner: string -> repo: string -> number: int -> IoResult<Liveness>

    /// What the meter says right now.
    ///
    /// DELIBERATELY NOT `Budget.Meter`. That type's `Cost` is *what one query cost*, read off a GraphQL
    /// response — and the `/rate_limit` endpoint has no such notion. Reusing it here would mean publishing
    /// a field called `Cost` holding `limit - remaining`, which is a number that reads as one thing and
    /// means another. In a codebase whose entire thesis is that a value must not be able to masquerade as a
    /// different fact, that is not a shortcut worth taking.
    type RateLimitSnapshot = { Remaining: int; Limit: int }

    /// An issue's REST INTEGER ID — `.id`, not its number.
    ///
    /// `child` attaches by this id, never by the number: two repos can each have an issue #7, and posting a
    /// number where an id belongs attaches the wrong issue silently.
    val restId: transport: IGitHubTransport -> owner: string -> repo: string -> number: int -> IoResult<int64>

    /// The REST ids of an issue's EXISTING sub-issues (`issues/{n}/sub_issues`).
    ///
    /// FAILS CLOSED (#320): an unreadable list is an ERROR, never an empty one. `child` reads it to be
    /// idempotent, and folding a failed read into "the edge is absent" would make it POST, collect a 422,
    /// and blame the token — an unreachable subject reported as an absent one.
    val subIssueIds: transport: IGitHubTransport -> owner: string -> repo: string -> number: int -> IoResult<int64 list>

    /// One node of an epic's sub-issue GRAPH: its ref (`owner/repo#n`) and whether it is still open.
    type SubIssue = { Ref: string; Open: bool }

    /// An epic's sub-issue graph, with the TOTAL count kept apart from the visible nodes.
    ///
    /// `Total > Children.length` is a TRUNCATED graph, and the distinction is load-bearing: the rollup and
    /// EPIC-UNLINKED-CHILD may only reason about "all children" when they have all of them. Concluding
    /// "every child is done" — or "this declared child is unlinked" — over a list already known to be short
    /// is the #266 shape (a verdict on a subject not wholly seen).
    type SubIssueSet = { Total: int; Children: SubIssue list }

    /// An epic's sub-issue graph: the total count and each child's ref + open/closed state.
    ///
    /// FAILS CLOSED, like every read here: an unreadable graph is an ERROR, never an empty set — an epic
    /// whose children could not be read must not roll up as "no children" or "all done".
    val subIssues: transport: IGitHubTransport -> owner: string -> repo: string -> number: int -> IoResult<SubIssueSet>

    /// Does this ref name a PULL REQUEST rather than an issue? (`issues/{n}` carries `pull_request` iff so.)
    ///
    /// GitHub refuses to link a PR as a sub-issue, so a task-list line citing the PR that closed a checklist
    /// item declares a ref the graph can never hold. EPIC-UNLINKED-CHILD re-resolves its otherwise-unlinked
    /// refs through this and drops the PRs — else the gate wedges red forever on genuinely-complete work
    /// (#346). The CALLER owns the fail-closed policy (#266): a ref this cannot resolve is KEPT, because "I
    /// could not check" is not "it is a PR".
    val refIsPullRequest: transport: IGitHubTransport -> owner: string -> repo: string -> number: int -> IoResult<bool>

    /// The rate-limit meter.
    ///
    /// FREE — this read does not spend the budget it reports, which is what makes "back off until the
    /// reset" a strategy rather than a guess. It is billed to NEITHER counter, and the corpus depends on
    /// that.
    val rateLimit: transport: IGitHubTransport -> IoResult<RateLimitSnapshot>

    /// One recorded edit to an issue/PR's BODY, from GraphQL's `userContentEdits` connection.
    type ContentEdit = { EditedAt: System.DateTimeOffset; EditorLogin: string option }

    /// The provenance answer to "has this issue/PR body changed since X": the TOTAL edit count kept
    /// apart from the visible nodes, exactly like `SubIssueSet` — the connection is capped at 100, so a
    /// caller must be able to tell "5 edits, all listed" from "127 edits, 100 shown".
    type ContentEditProvenance = { Total: int; Edits: ContentEdit list }

    /// The GraphQL `userContentEdits` connection on an issue OR a pull request (`.github#2477`).
    ///
    /// `.github#2456`'s independent-review contract names this connection as the authoritative source
    /// for "has this body changed since X" — REST's timeline carries no body-edit event at all, only
    /// `renamed` for titles — and warns a critic off a hand-built `gh api graphql` call, which
    /// `graphql-monopoly` refuses as an unmetered principal on the shared budget. This is the sanctioned,
    /// metered way to ask the question the contract already tells every critic to ask.
    ///
    /// FAILS CLOSED, like every read here, and unusually LOUDLY for this one: a GraphQL response is
    /// reported as an HTTP 200 carrying `errors` even on rate-limit exhaustion, so `errors` is read
    /// BEFORE `data` is trusted — never as a fallback once extraction fails. A null
    /// `issueOrPullRequest`, a malformed body, or an `errors` array are each an ERROR here, never an
    /// empty connection. `.github#2456` exists because a REST-timeline "no edits found" is
    /// `NOT_MEASURED`, not a negative result; a GraphQL read that silently degraded a failure into
    /// `Ok { Total = 0; Edits = [] }` would manufacture exactly the false negative that contract was
    /// written to prevent.
    val contentEditProvenance:
        transport: IGitHubTransport -> owner: string -> repo: string -> number: int -> IoResult<ContentEditProvenance>

    /// A pull request's HEAD ref (`.head.ref`), e.g. `item/42-the-thing`.
    ///
    /// FAILS CLOSED (#322): an unreadable head ref is an ERROR, never an empty string. verify-paths uses
    /// it to decide WHICH issue a PR implements, and guessing that from a failed read would stamp a
    /// touch-set verdict on a subject nobody identified.
    val prHeadRef: transport: IGitHubTransport -> owner: string -> repo: string -> pr: int -> IoResult<string>
    /// Immutable head commit SHA for evidence binding.
    val prHeadSha: transport: IGitHubTransport -> owner: string -> repo: string -> pr: int -> IoResult<string>
    /// Immutable base commit SHA for evidence binding.
    val prBaseSha: transport: IGitHubTransport -> owner: string -> repo: string -> pr: int -> IoResult<string>
    /// Current tip SHA of the PR's declared base branch.
    ///
    /// This deliberately resolves `base.ref` through `git/ref/heads/<ref>` instead of trusting the PR
    /// object's potentially stale `base.sha` snapshot. It fails closed if either read is unavailable.
    val prBaseTipSha: transport: IGitHubTransport -> owner: string -> repo: string -> pr: int -> IoResult<string>

    /// UTF-8 file content at an immutable git ref.
    val fileAtRef:
        transport: IGitHubTransport ->
        owner: string ->
        repo: string ->
        path: string ->
        gitRef: string ->
            IoResult<string>

    /// Commit message at an immutable SHA, used as a trusted delivery declaration fact.
    val commitMessage: transport: IGitHubTransport -> owner: string -> repo: string -> sha: string -> IoResult<string>

    type CommentBody =
        { Id: int64; Url: string; Body: string }

    type AuthorityComment =
        { Id: int64
          Url: string
          Body: string
          Author: string
          CreatedAt: System.DateTimeOffset
          UpdatedAt: System.DateTimeOffset }

    val commentsWithIdentity:
        transport: IGitHubTransport -> owner: string -> repo: string -> number: int -> IoResult<CommentBody list>

    val commentsWithAuthority:
        transport: IGitHubTransport -> owner: string -> repo: string -> number: int -> IoResult<AuthorityComment list>

    val authenticatedLogin: transport: IGitHubTransport -> IoResult<string>

    /// Same-repository PR numbers durably cross-referenced from this item through its complete timeline.
    val crossReferencedPullRequests:
        transport: IGitHubTransport -> owner: string -> repo: string -> number: int -> IoResult<int list>

    /// A pull request's changed files (`pulls/{n}/files`), paginated.
    val prFiles: transport: IGitHubTransport -> owner: string -> repo: string -> pr: int -> IoResult<string list>

    /// A pull request's DESCRIPTION (`.body` on `pulls/{n}`) — the text GitHub's markdown parser reads to
    /// build `closingIssuesReferences` while the PR is open (.github#2107). `issueBody`'s exact sibling: a
    /// `null` body (nobody wrote one) reads as `Ok ""`, a real, successfully-observed fact, never an error.
    /// verify-paths uses this to catch a closing keyword written next to the board's OWN `<repo>#<n>`
    /// shorthand — a form GitHub's grammar does not parse — WHILE the PR is still open, which is the one
    /// moment fixing it is free.
    val prBody: transport: IGitHubTransport -> owner: string -> repo: string -> pr: int -> IoResult<string>

    /// IS THIS OPEN PR FINISHED WORK? — #697/#720, over REST.
    ///
    /// Reads the PR (for `mergeable` + head SHA), the head SHA's WORKFLOW RUNS, and that SHA's CHECK RUNS,
    /// and hands them to `Landable.score`. THREE reads, exactly as bash's `pr_landable`, and — like
    /// `prAlive` — only ever on a claim that is ALREADY stale and ALREADY has an open PR, which is rare, and
    /// on the ONE such item, never a scan.
    ///
    /// RETURNS A `PrState`, NOT AN `IoResult`, ON PURPOSE. This is the one read whose FAILURE IS ITS ANSWER:
    /// `PrUnknown` is not a masqueraded empty (the thing this module forbids everywhere else), it is the
    /// honest verdict "I could not tell", and its whole job is to make `reap`/`who`/`adopt` advise nothing
    /// on a guess. So every read error, and a `mergeable` that is still `null`, collapse to `PrUnknown` —
    /// the fail-closed direction — rather than propagating. The verdict is ADVISORY: it chooses which
    /// refusal `reap` speaks, never whether it refuses.
    ///
    /// A PR THAT IS NOT OPEN IS ANSWERED BEFORE ANY OF THAT (#1680). `merged` and `state` ride in the same
    /// `pulls/{n}` body as `mergeable`, so the question costs no extra request, and it is asked FIRST: a
    /// closed PR scores `PrMerged` or `PrClosed` and the runs/check-runs reads are never made. It has to be
    /// first, because GitHub reports `mergeable: null` for a merged PR — indistinguishable, on mergeability
    /// alone, from a PR whose background job has not finished — so the `Computing` arm used to claim it and
    /// return `PrPending`, the one verdict the exit contract defines as worth retrying, for a state that can
    /// never change. The bounded `mergeable` re-read is skipped for the same reason: GitHub stops computing
    /// mergeability once a PR leaves `open`, so those tries wait on a job that will never run again.
    ///
    /// NOTE (deferred, honest): the runs/check-runs reads are SINGLE PAGE here. GitHub paginates them with a
    /// `Link` header (bash passed `--paginate`, #547); the array-merging transport does not flatten the
    /// OBJECT bodies these endpoints return, so a multi-page runs list degrades to `PrUnknown` (fail closed),
    /// never a wrong verdict. The corpus's landable worlds are single-page; real multi-page runs pagination
    /// is a follow-up.
    val prLandable: transport: IGitHubTransport -> owner: string -> repo: string -> pr: int -> PrState

    /// Read the merged PR, repository default branch, and complete exact-merge Actions inventory.
    /// Only a non-empty all-success set of `push` runs for that SHA and branch verifies completion;
    /// absent/pending/red remain typed observations and every incomplete/malformed read fails closed.
    val postMergeVerification:
        transport: IGitHubTransport ->
        owner: string ->
        repo: string ->
        pr: int ->
            Errors.IoResult<FS.GG.Coord.Delivery.PostMergeVerification>

    /// `prLandable`, plus the NUMBER of subjects the verdict was scored over (`Landable.scoreN`) — the read
    /// `landable --wait` polls on (#724). The count distinguishes a `red` over zero subjects (the
    /// registration race — "CI has not started yet") from a `red` over real ones (a finding), and the
    /// `--wait` loop must not believe an early `green` until that count has stopped growing. It is 0 for
    /// every verdict reached before the runs are scored (conflicted, unknown). Same single-page caveat as
    /// `prLandable`.
    val prLandableN: transport: IGitHubTransport -> owner: string -> repo: string -> pr: int -> PrState * int

    /// Typed diagnostics returned beside the verdict. Pending assertions/refusals and settled red
    /// run/check identities remain distinct because their remedies are opposite; red subjects also carry
    /// the exact head SHA whose inventories were scored.
    type Unmet =
        /// An assertion the CALLER added (`--require NAME`, `--sha SHA`). The base branch policy has no
        /// opinion about it and nothing else will ever look at it.
        | Asserted of string
        /// GITHUB ITSELF will refuse this merge — its `mergeable_state` for the gated head, verbatim
        /// (`blocked`, `behind`, `draft`). Nobody asked for this one.
        | Refused of state: string * baseRef: string
        /// …and WHICH context the base branch requires that has no check run on this head. Diagnosis only.
        | NotReported of context: string * baseRef: string
        /// The policy could not be read, so the refusal cannot be attributed to a named context. A missing
        /// SENTENCE, never a missing verdict.
        | PolicyUnreadable of string
        /// A completed bad workflow run that participates in the red verdict, bound to the scored head.
        | RedWorkflowRun of headSha: string * path: string * runNumber: int * conclusion: string option
        /// A completed bad check-run that participates in the red verdict, bound to the scored head.
        | RedCheckRun of headSha: string * name: string * checkSuiteId: int64 option * conclusion: string option

    /// `prLandableN`, plus the two assertions a caller may add to it (#737). `prLandableN` is this with
    /// `required = []` and `expected = None`.
    ///
    /// `required` — check-run names that must have REPORTED (`Landable.scoreRequired`). For a check that is
    /// not REQUIRED by branch protection but IS the reason the PR exists; absent, it reads exactly like a
    /// passing one (#606).
    ///
    /// `expected` — the head SHA the caller believes it is gating. `pulls/{n}` is EVENTUALLY CONSISTENT
    /// after a force-push: for a moment it still names the previous commit, whose checks are green and are
    /// not about the code that would be merged. A caller that just pushed knows the SHA it pushed and can
    /// say so; a disagreement is `PrPending` (a read taken too early — never a verdict about the wrong
    /// commit), which `--wait` rides out. Callers that did not just push should omit it.
    ///
    /// IT ALSO ASKS WHETHER GITHUB WILL TAKE THE MERGE (#1575), and a `mergeable_state` of `blocked`,
    /// `behind` or `draft` is `PrPending`, never `PrGreen`. The rollup asks "is anything red?", which is
    /// blind to a subject that is ABSENT — so this command answered `green`, exit 0, for a PR GitHub then
    /// refused to merge ("the base branch policy prohibits the merge"), because a required context whose
    /// workflow was armed on `main` after the PR's head was pushed had never reported at all.
    ///
    /// IT ASKS THE PULL REQUEST, NOT THE BRANCH POLICY, and that is a correction to #1575's own prescribed
    /// remedy. Deriving the must-have-reported set from `branches/{b}/protection` needs
    /// `administration: read` — not a valid `permissions:` scope for a workflow's GITHUB_TOKEN at all —
    /// and `landable`'s unattended caller runs entirely under one. A verdict resting on that read would
    /// 403 forever there: #463, where a protection probe 403'd on every receiver and stopped the kit
    /// landing anywhere. #463's ratified repair was to ask the PR instead, recorded as better on its
    /// merits. `mergeable_state` rides in the PR object this function ALREADY reads, so the guard costs no
    /// request and no scope — and it is strictly wider than the derived set, covering a context satisfied
    /// by a legacy commit STATUS, an app-id mismatch, a strict base fallen behind, and required reviews.
    ///
    /// `unstable` is NOT among the refusing states: it means a non-required check failed, which GitHub
    /// merges. An ABSENT `mergeable_state` is NO OPINION, not a refusal — it is part of a body we already
    /// hold rather than a permission-gated read, so manufacturing a refusal from it would strand every
    /// caller against a payload that omits it.
    ///
    /// The base branch's required contexts are then read — from BOTH stores GitHub keeps them in, classic
    /// protection and rulesets (#574) — to say WHICH context has not reported. That read is DIAGNOSIS: it
    /// happens only on the refusing path, and a failure costs a sentence, never a verdict.
    ///
    /// The third element is every diagnostic reason, TYPED (`Unmet`) — so a `pending` can say what it is
    /// waiting for and a settled `red` can name every causal run/check without another API investigation.
    /// The verdict never depends on this projection. Same single-page caveat as `prLandable`.
    val prLandableRequire:
        transport: IGitHubTransport ->
        owner: string ->
        repo: string ->
        pr: int ->
        required: string list ->
        expected: string option ->
            PrState * int * Unmet list

    /// The FIRST issue a pull request declares it closes (`closingIssuesReferences`), if any.
    ///
    /// `Ok None` means it closes nothing by that record — a real answer (the PR may implement an item by
    /// its branch name instead). It is distinct from a failed read, which is an `Error`.
    ///
    /// **`Ok None` IS A MEASUREMENT, and until `.github#2534` the implementation did not honour this line.**
    /// It is now reachable from exactly one state: a `closingIssuesReferences.nodes` array successfully
    /// enumerated and found empty. A partial HTTP 200 (`data` present, `errors` populated — how GitHub
    /// reports an exhausted GraphQL budget), a null `repository`/`pullRequest`, an absent connection, and a
    /// node whose `number`/`nameWithOwner` cannot be read are each an `Error`. They used to be `Ok None`,
    /// which `verify-paths` reads as "this PR implements no tracked item" and reports as a GREEN skip.
    ///
    /// **AND `Ok (Some …)` IS A MEASUREMENT TOO** (`.github#2535`). The connection is selected `first: 5`
    /// and this returns the HEAD of whatever arrived, so a PR closing more issues than the window admits was
    /// answered from a set already known to be short — and this function's whole contract is *the ONE item
    /// this PR implements*, which a partly-seen closing graph does not have. It now carries `totalCount`,
    /// and a window that hid a tail is an `Error` rather than a head plucked from five of seven.
    val prClosingRef: transport: IGitHubTransport -> owner: string -> repo: string -> pr: int -> IoResult<Ref option>

    /// One list element's body — READ, or NOT READ. The `TouchSet.Unreadable` distinction (#1150), carried
    /// at the ELEMENT level because that is the granularity the anomaly has (.github#1794).
    ///
    /// `TouchSet.parse ""` answers `Undeclared`, and `TouchSet.conflicts` reads `Undeclared` as colliding
    /// with nothing. So `""` is not a neutral placeholder here: it is the ASSERTION *"this issue declares
    /// nothing"*. Asserting it about an element whose body was absent or ill-typed is the fail-open #1150
    /// closed one function away — the row collides with nobody, and there is no CAS on a file.
    type IssueBodyRead =
        /// The body as GitHub served it. `"body": null` on an issue nobody described IS this: a real,
        /// successfully-observed empty body, which `TouchSet.parse` correctly calls `Undeclared`. That
        /// reading is CORRECT and deliberately preserved — the defect was never `null`, it was that a null
        /// body and an unreadable one were the same value.
        | BodyRead of body: string
        /// No body we could read: the field was ABSENT, or present with a kind that is neither string nor
        /// null. Emphatically not an empty body. Callers that gate on it must fail closed.
        | BodyUnread of reason: string

    /// One element of the open-issue list: an issue we could IDENTIFY, and what we could read of its body.
    ///
    /// There is no `OpenIssue` for an element with no numeric `number` — an element nobody can name has no
    /// marker route (a marker scan needs the number) and no ref to report, so it cannot be carried as an
    /// unreadable entry. `openIssues` refuses the whole read instead. See its remarks.
    type OpenIssue = { Number: int; Body: IssueBodyRead }

    /// Every OPEN issue in a repo, with its body — the claim-scan candidate set, and (since .github#1779)
    /// the #353 collision gate's.
    ///
    /// PAGINATED, AND UNCONDITIONAL. A lock has no hundred-issue limit: a first page read as the whole set
    /// is a claim scan that cannot see the markers past it, and would report those items free.
    ///
    /// The bodies ride along because they are free here — one list read serves both the marker scan and the
    /// touch-set extraction, where two reads per item would double the REST cost of the scheduling loop.
    ///
    /// **IT MANUFACTURES NO ANSWER OUT OF WHAT IT COULD NOT READ (.github#1794).** Two silent fabrications
    /// used to live here, and both failed OPEN — the direction that costs a double-claim rather than a
    /// retry:
    ///
    /// | the element | before | now |
    /// |---|---|---|
    /// | no numeric `number` | **dropped silently** — the issue vanished from the claim scan and its lock reserved nothing | `Error(Malformed …)`, naming the element's index and the array's length |
    /// | `body` absent, or neither string nor null | `""` → `Undeclared` → *"declares nothing"* | `BodyUnread`, for the caller to fail closed on |
    /// | `"body": null` | `""` → `Undeclared` | **unchanged** — a real, empty body, and it must stay that way |
    ///
    /// A pull request is still dropped, and that is not a silent drop: `pull_request` is a POSITIVE
    /// identification of a thing that is not an item of work (#641), not a failure to identify anything.
    val openIssues: transport: IGitHubTransport -> owner: string -> repo: string -> IoResult<OpenIssue list>

    /// Read one queued ref's OPEN/CLOSED state. Missing, malformed, unreadable and pull-request refs are
    /// errors, never inferred as CLOSED.
    val issueState: transport: IGitHubTransport -> owner: string -> repo: string -> number: int -> IoResult<IssueState>

    /// `issues` — a repo's issue list over REST, ETag-revalidated (#446/#418). THE budget-free read: a 304
    /// serves the cached body for zero cost.
    ///
    /// CONDITIONAL, unlike `openIssues` — its subject is a listing, not the lock, so a 304 serving the
    /// cached body is exactly right (no marker can hide in a list nobody scans for markers). `fresh`
    /// (`--refresh`) drops the stored ETag and forces a full re-read. Returns the raw JSON body bash prints.
    val issues:
        transport: IGitHubTransport ->
        owner: string ->
        repo: string ->
        state: string ->
        label: string option ->
        fresh: bool ->
            IoResult<string>
