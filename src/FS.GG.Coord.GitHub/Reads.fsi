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

    open FS.GG.Coord
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

    /// THE MARKER READ, WITH ITS OWN COMPLETENESS ATTACHED (.github#1668).
    ///
    /// A `Marker list` on its own cannot tell *"this issue carries no claim"* apart from *"this issue carries
    /// comments I could not read, and any one of them may have been the claim"*. `who` rendered the second as
    /// the first — `UNCLAIMED — In progress with NO claim marker`, plus an accusation that somebody was
    /// working outside the protocol — which is the fail-open direction on the one read that separates
    /// workers. This type is what makes the difference sayable.
    type MarkerScan =
        { /// The claim markers, lowest comment id first — the CAS's total order, winner at the head.
          Markers: Marker list
          /// One entry per comment the scan could NOT classify, each naming which comment and why: a comment
          /// with no readable `body` (it may have been a marker), or a comment whose body IS a marker but
          /// which carries no numeric `id` to order it by.
          ///
          /// **EMPTY IS THE LOAD-BEARING VALUE.** Only an empty list licenses "there is no claim here".
          /// Non-empty means `Markers` is a LOWER BOUND, and a caller that reports absence off it is
          /// manufacturing an answer out of what it could not read (#266, .github#1794).
          Unreadable: string list }

    /// The `BoardStatus` for a Projects v2 Status option NAME (case-insensitive), or for a marker's decoded
    /// `prev=` value — the two callers that turn a column's human name back into the type. A name we do not
    /// recognise, or the empty string, is `None`: "this records no restorable column" (#481), never a guess.
    /// It is ONE parser so the claim that records a column and the read that restores it cannot drift.
    val statusOfName: name: string -> BoardStatus option

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
    ///
    /// **IT DISCARDS `MarkerScan.Unreadable`, AND THAT IS A KNOWN, UNCLOSED FAIL-OPEN — NOT A SAFE DEFAULT.**
    ///
    /// Say plainly what this costs, because the comment .github#1668 deleted from `parseMarker` was a
    /// confident safety argument that happened to be false, and replacing it with a second one would be the
    /// same mistake wearing the fix's clothes:
    ///
    /// * **A re-read does not recover a hidden marker.** `Unclassifiable` is a DETERMINISTIC property of a
    ///   fixed comment, not a timing race, so the CAS's post-and-re-read, `verifyHeld` and `reap`'s
    ///   re-confirmation all parse it the same way and are blind to it identically. A marker hidden here is
    ///   hidden from every read that follows — which is a DOUBLE-HOLD, not a lost race.
    /// * **Not every caller writes.** `Scan.snapshot` reads markers through here for the scheduler
    ///   snapshot that `next`, `batch`, `ready` and `reconcile` decide from, and nothing re-checks it.
    ///
    /// So this function is `markerScan` minus its safety, kept because changing every caller at once is a
    /// larger change than .github#1668 is, and its remaining callers were audited as no WORSE than they were
    /// before that item. `who` — the verb an operator reads before dispatching, reaping or adopting — was
    /// moved to `markerScan`. Routing the rest is tracked, and until it lands this doc is a warning rather
    /// than a licence: do not add a caller here without reading the two bullets above.
    val markers: transport: IGitHubTransport -> owner: string -> repo: string -> number: int -> IoResult<Marker list>

    /// `markers`, plus every comment the scan could not classify — the read for callers that REPORT.
    ///
    /// Same request, same guarantees (unconditional, uncached, paginated, malformed-is-an-error); the only
    /// difference is that this one does not throw away the evidence that its answer may be incomplete.
    val markerScan: transport: IGitHubTransport -> owner: string -> repo: string -> number: int -> IoResult<MarkerScan>

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
    type RateLimitSnapshot =
        { Remaining: int
          Limit: int }

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

    /// A pull request's HEAD ref (`.head.ref`), e.g. `item/42-the-thing`.
    ///
    /// FAILS CLOSED (#322): an unreadable head ref is an ERROR, never an empty string. verify-paths uses
    /// it to decide WHICH issue a PR implements, and guessing that from a failed read would stamp a
    /// touch-set verdict on a subject nobody identified.
    val prHeadRef: transport: IGitHubTransport -> owner: string -> repo: string -> pr: int -> IoResult<string>

    /// A pull request's changed files (`pulls/{n}/files`), paginated.
    val prFiles: transport: IGitHubTransport -> owner: string -> repo: string -> pr: int -> IoResult<string list>

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

    /// `prLandable`, plus the NUMBER of subjects the verdict was scored over (`Landable.scoreN`) — the read
    /// `landable --wait` polls on (#724). The count distinguishes a `red` over zero subjects (the
    /// registration race — "CI has not started yet") from a `red` over real ones (a finding), and the
    /// `--wait` loop must not believe an early `green` until that count has stopped growing. It is 0 for
    /// every verdict reached before the runs are scored (conflicted, unknown). Same single-page caveat as
    /// `prLandable`.
    val prLandableN: transport: IGitHubTransport -> owner: string -> repo: string -> pr: int -> PrState * int

    /// Why a verdict that is not `red` is nonetheless not `green` — the diagnostic channel `prLandableRequire`
    /// returns beside its verdict. The arms are held APART because their remedies are opposite: one is a
    /// preference the caller expressed, one is a fact about what GitHub will refuse, and one is the absence
    /// of any observation at all. One sentence for all three sends the operator to the wrong place (#1575).
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
    /// The third element is every unmet reason, TYPED (`Unmet`) — so a `pending` can say what it is waiting
    /// for instead of being one word with no thread to pull, and so an operator is not told that a refusal
    /// by the base branch is "an assertion you asked for". The verdict never depends on it. Same
    /// single-page caveat as `prLandable`.
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
    /// marker route (`Reads.markers` needs the number) and no ref to report, so it cannot be carried as an
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
