namespace FS.GG.Coord.GitHub

/// THE BOARD-SIDE WRITES — Projects v2, over GraphQL, because there is no REST for it.
///
/// This is the other half of the write path. `Writes` owns the ISSUE — comments, bodies, the claim CAS,
/// all REST, all on the budget that survives. This module owns the BOARD, and it is on the budget that
/// dies first (#418: five workers looping `take` drained 5,000 pt/hr in fifteen minutes). Every design
/// decision below follows from that one fact.
///
/// THREE INCIDENTS SHAPE IT, AND THEY ARE NOT INDEPENDENT.
///
/// **#421 — an exhausted budget is not an absent item.** `item_id` resolved an issue's board item. Under a
/// dead budget the lookup failed, the failure came back as the empty string, and the caller read that as
/// *"this issue is not on the board"* — then printed a remediation telling the worker to run `item-add`
/// for an issue that already had a board item. A budget failure did not merely fail; it produced a
/// DEFINITE ANSWER out of a read that never happened, and sounded helpful doing it. So `Offboard` is a
/// case here, it is reached only from a SUCCESSFUL read, and `RateLimited` can never become it.
///
/// **What #421 did NOT do is corrupt the board, and the correction matters** (#871). This header, and
/// eight other comments across five more files, asserted that it did — three of them in these very
/// words: the remediation *"CREATED A SECOND BOARD ITEM"*. It did not:
/// `addProjectV2ItemById` is idempotent server-side — for an issue already on the board it resolves to
/// that item's existing id and adds nothing (measured against the live Coordination board, #861/#871;
/// the board carries 1,124 items and 1,124 distinct content ids). #421's own text was careful and
/// counterfactual — a duplicate *"would have"* been created *"had I followed it"* — and the hedge
/// hardened into an assertion as it was copied inward. The rule it drew is untouched and right; only
/// this stated mechanism was wrong. Do not weaken the guard: it stops a definite answer built on no
/// information, which is the defect. It was never what stopped a duplicate row — GitHub is.
///
/// **#510 — the tool promised a write it dropped.** Only `claim` deferred its board write on an exhausted
/// budget; `set-field` and `done --flip` printed the same *"the write is QUEUED"* sentence and threw the
/// write away — and `flush` then reported success, confirming the lie. Here there is ONE board write
/// (`boardWrite`), every caller goes through it, and the queue is `Cache.defer`, which takes the failure
/// that licenses it as an argument. **Only an exhausted budget may be queued.** Everything else is
/// permanent, and replaying a permanent failure forever is how the queue came to lie.
///
/// **#448 — six field writes cost six points, and they should cost one.** GitHub bills
/// `cost = max(1, nodes/100)`: a Projects v2 field mutation returns ~1 node, so it hits the ONE-POINT
/// FLOOR, and the cost of a placement pass therefore tracks the REQUEST COUNT and nothing else. Aliased
/// into a single document, six requests become one request and one point. (The org's budget doc claimed the
/// opposite. That is true of QUERIES, whose cost scales with the nodes they return, and false of these
/// mutations, which are pinned to the floor.)
module Board =

    open FS.GG.Coord.Types
    open Errors
    open Transport

    /// What kind of field it is — which decides which mutation shape the value goes into.
    ///
    /// `SingleSelect` carries its options by NAME, because that is what a caller has ("Ready") and the API
    /// wants an option id ("opt_ready"). Resolving that mapping is the whole reason `bootstrap` exists.
    type FieldType =
        | SingleSelect of options: Map<string, string>
        | Text
        | Number
        | Date
        | Iteration

    type Field = { Id: string; Type: FieldType }

    /// The board's identity and its field/option id map.
    ///
    /// Cached for a day (`BOARD_TTL_MIN` = 1440). These are IDs, and ids do not change — what changes is the
    /// board's CONTENT, which is what the 90-second scan cache is for. Conflating the two would either
    /// re-resolve the whole field map on every call (two GraphQL points, per worker, per invocation) or
    /// serve day-old item state. They are different facts with different lifetimes.
    type BoardMap =
        { Number: int
          Id: string

          /// The owner and title this board was resolved FROM.
          ///
          /// They are carried because the scan cache is keyed on them, and a write that folds itself into
          /// the cache must fold into the RIGHT one. `FSGG_COORD_OWNER` / `FSGG_COORD_PROJECT` can point
          /// this client at a different board — so a hardcoded title is not a shortcut, it is a write
          /// landing in another board's cache file, or in none. Neither is visible to anybody.
          Owner: string
          Title: string

          Fields: Map<string, Field> }

    /// A dependency-edge value bound to the Projects-v2 item revision that produced it.
    type BlockedByObservation =
        { Value: string option
          Revision: string option }

    /// A write to one field.
    ///
    /// **`Clear` IS A DIFFERENT MUTATION, NOT AN EMPTY `Set`.** This is a type because it was a trap:
    /// `gh project item-edit --text ''` is a NO-OP — the API answers *"no changes to make"* — so an empty
    /// write silently left the old value in place, and the board went on displaying a `Blocked by` that had
    /// been cleared. The clear is `clearProjectV2ItemFieldValue`, a different call entirely, and a caller
    /// that means it has to say so.
    ///
    /// `Set ""` is REFUSED, loudly, rather than quietly reinterpreted as a clear — because a caller who
    /// wrote it meant one of the two things and we cannot know which.
    type FieldWrite =
        | Set of value: string
        | Clear

    /// Resolve the board and its field/option ids. Two GraphQL calls in the ordinary case — one project
    /// page, one field map.
    ///
    /// **THE PROJECT LIST IS WALKED, NOT SAMPLED** (`.github#2535`). `projectsV2(first: 50)` with no cursor
    /// answers the same shape whether the owner has 50 projects or 500, so a board that happened to be the
    /// 51st came back as `NotFound` — a definite CONFIGURATION verdict ("`FSGG_COORD_PROJECT` names a board
    /// that is not here") manufactured from a window nobody announced, whose remedy is wrong for the actual
    /// condition. The walk returns on the HIT, so the ordinary two-call cost is unchanged and only an owner
    /// whose board really is past the first page pays a third call.
    ///
    /// `NotFound` therefore now means *no board by that title among ALL projects*, and a walk that could
    /// not be shown to finish — a missing or non-Boolean `pageInfo.hasNextPage`, a next page with no usable
    /// cursor — is `Error(Malformed …)` rather than that verdict.
    ///
    /// **A TRUNCATED FIELD MAP IS REFUSED RATHER THAN CACHED** (same item). `fields(first: 50)` dropped the
    /// tail of a board with more than 50 fields, and the all-empty guard cannot see a PARTIAL map — so the
    /// truncated map was cached for a day and every later write to a missing field failed with `no field
    /// named 'X' on this board. Known fields: …`, reciting the truncated list as if it were the board's own.
    val bootstrap: transport: IGitHubTransport -> owner: string -> title: string -> IoResult<BoardMap>

    /// The board map as JSON — the `board` command's machine contract, and the on-disk cache format. One
    /// codec serves both, so a board a human reads and a board `next` re-hydrates cannot drift.
    val boardToJson: board: BoardMap -> string

    /// `bootstrap`, served from the day-cache (`Cache.getBoardMap`) when it is warm; resolves and stores it
    /// on a miss. The budget win of #418 — two GraphQL points under every worker command, paid once a day
    /// instead of once an invocation. A cached document we cannot parse is a miss, never a failure.
    val bootstrapCached: transport: IGitHubTransport -> owner: string -> title: string -> IoResult<BoardMap>

    /// The board item id for an issue. Issues owned outside the board owner are resolved from the
    /// ProjectV2 item connection because GitHub may omit their placement from the issue-side
    /// `repository.issue.projectItems` connection. The lookup compares canonical owner, repository, and
    /// number, so an external row cannot be confused with a default-owner twin.
    ///
    /// `Ok None` means **the issue is genuinely not on this board** — a successful read with a definite
    /// answer, and the only thing that licenses an `item-add`. It is UNREACHABLE from a failed read: a
    /// rate-limited lookup returns `Error(RateLimited …)`, and #421 is the two of them being the same value.
    ///
    /// **AND UNREACHABLE FROM A TRUNCATED ONE** (`.github#2535`). #421 closed the axis where the read
    /// ERRORED; the issue-side connection is selected `first: 20`, and an issue sitting on more boards than
    /// that — ours being the twenty-first — returned a full window that simply did not contain us. "We
    /// walked its items" was then a claim about a WINDOW rather than about the connection, and the
    /// non-answer it produced licensed adding a row that already existed. The connection now carries
    /// `totalCount`, and a window that hid a tail is `Error(Malformed …)`. The question is asked only on the
    /// MISS: a hit cannot be unmade by a tail, so the hot path costs nothing.
    ///
    /// Item ids are stable, so this is cached forever once resolved.
    val itemId:
        transport: IGitHubTransport ->
        board: BoardMap ->
        owner: string ->
        repo: string ->
        number: int ->
            IoResult<string option>

    /// `itemId`, served from the forever-cache (`Cache.getItemId`) when it is warm. A resolved id is stable,
    /// so the second lookup for the same issue costs zero GraphQL. Only a FOUND id is cached — `Ok None`
    /// (#421) and `Error` are never memoised.
    val itemIdCached:
        transport: IGitHubTransport ->
        board: BoardMap ->
        owner: string ->
        repo: string ->
        number: int ->
            IoResult<string option>

    /// What `addItem` did.
    type AddOutcome =
        /// The issue was already on this board. No mutation was spent, and re-running `add` is safe.
        | AlreadyOnBoard of itemId: string
        /// The issue was added — the only outcome that writes.
        | AddedToBoard of itemId: string

    /// Put an issue on the board, idempotently — the metered verb the GraphQL monopoly rule (#586) names,
    /// and the one the port dropped (#861). It is what makes `gh project item-add` refusable: that call
    /// spends the shared fleet budget with nothing to meter or cache it.
    ///
    /// **#421 is the whole function.** Only `Ok None` from the item lookup — a successful read that walked
    /// the board and did not find this issue — licenses the mutation. An `Error` is a read that did not
    /// happen; adding on one puts a SECOND item on the board for an issue that was already there, and the
    /// board then has two rows nobody can reconcile. A failed lookup is not an absence, so it propagates.
    val addItem:
        transport: IGitHubTransport ->
        board: BoardMap ->
        owner: string ->
        repo: string ->
        number: int ->
            IoResult<AddOutcome>

    /// Read ONE item's `Status` column — the pre-claim column of #481, so `release`/`reap` can restore what
    /// a claim overwrote instead of guessing `Ready`.
    ///
    /// It is a `fieldValueByName` RESOLVER read — one point, one item, no node multiplication — and NOT a
    /// board scan, deliberately: this sits on `take` → `claim`, the hottest path on the budget that dies
    /// first (#418), so a full-board read here would be the regression #481 is written to avoid.
    ///
    /// An issue owned OUTSIDE the board owner is read from the BOARD side instead, exactly as `itemId` is
    /// (#2166 remainder, #2204): `repository.issue.projectItems` omits an organization project's placement of
    /// an externally owned issue, so the issue-side narrowing answered a false `Ok None` for a row that
    /// carries a column — and `claim`/`take` could then never report `converged`, `release` never restored
    /// the pre-claim column, and `add` applied its `Backlog` default over a live one. The board-side route
    /// resolves the row through `itemIdCached` (forever-stable, so the field itself stays a one-point
    /// resolver read) and fails closed on an unresolvable lookup.
    ///
    /// `Ok None` is a definite answer with two shapes — the issue is not on THIS board, or it is but its
    /// `Status` is unset — both meaning "no column to restore", which a claim records as none and `release`
    /// then puts back as `Ready`. A failed read is `Error`, never `Ok None`: absence may not be manufactured.
    ///
    /// **INCLUDING A READ THAT TRUNCATED** (`.github#2535`): the issue-side `projectItems` window is 20, and
    /// a miss over a window that hid a tail is `Error`, not "not on this board". `claim` treats both as
    /// "recorded no column", but they are not the same fact and only one of them may be ASSERTED — a
    /// definite absence manufactured from a window is #266's class whatever the caller then does with it.
    val itemStatus:
        transport: IGitHubTransport ->
        board: BoardMap ->
        owner: string ->
        repo: string ->
        number: int ->
            IoResult<BoardStatus option>

    /// Read ONE item's live `Blocked by` column — `itemStatus`'s twin, over the TEXT fragment instead of
    /// the single-select one (.github#2079).
    ///
    /// `release --status Blocked` must refuse to PARK before the lock drops when the row will end with
    /// neither a non-empty `Blocked by` field nor a `Blocked on: human/...` sentinel — ADR-0045's park
    /// invariant, made a write-time gate rather than a read-time report. That refusal needs the LIVE
    /// value, not the scan's cache and not the marker, because the defect this closes is precisely a
    /// field that already reads as fine while the edge that would make it so landed somewhere else.
    ///
    /// A RESOLVER READ, on `itemStatus`'s own terms and for its own reason: one point, one item, never
    /// `Scan.board`'s full-board cost on a per-call gate.
    ///
    /// It shares `itemStatus`'s external-owner route through the one `externalItemField` mechanism, so the
    /// #2204 repair cannot land on one reader and miss the other; the two differ only in which value they
    /// pull off the `fieldValueByName` node.
    ///
    /// `Ok None` covers "not on this board" and "on the board with the field genuinely empty" alike, both
    /// meaning there is no edge here today. A failed read is `Error`, never a manufactured absence — and by
    /// `.github#2535` that includes a `projectItems` window that hid a tail, on `itemStatus`'s rule and for
    /// `itemStatus`'s reason.
    val itemBlockedBy:
        transport: IGitHubTransport ->
        board: BoardMap ->
        owner: string ->
        repo: string ->
        number: int ->
            IoResult<string option>

    /// Fresh dependency-edge observation including the Projects-v2 item revision.
    val itemBlockedByObservation:
        transport: IGitHubTransport ->
        board: BoardMap ->
        owner: string ->
        repo: string ->
        number: int ->
            IoResult<BlockedByObservation>

    /// Fresh resolver read for one projected single-select or text field.
    val itemFieldValue:
        transport: Transport.IGitHubTransport ->
        board: BoardMap ->
        owner: string ->
        repo: string ->
        number: int ->
        field: string ->
            IoResult<string option>

    /// Write ONE field. Routes by the field's declared type; an empty `Set` is refused.
    ///
    /// A value that does not fit its field — an unknown single-select option, a NUMBER that is not a number
    /// — is refused BEFORE the mutation is sent, and it costs zero GraphQL. A rejected value must not spend
    /// the budget that dies first.
    val setField:
        transport: IGitHubTransport ->
        board: BoardMap ->
        itemId: string ->
        fieldName: string ->
        write: FieldWrite ->
            IoResult<unit>

    /// Write N fields in ONE aliased document (#448).
    ///
    /// EVERYTHING IS RESOLVED AND VALIDATED BEFORE A SINGLE MUTATION IS EMITTED. A bad pair caught late
    /// would not merely waste a point — it would fail the document AFTER its earlier aliases had already
    /// been written to the board, which is a half-written board nobody asked for.
    ///
    /// THE PARTIAL-APPLY ARM IS THE HARD PART, AND IT IS REAL. A GraphQL error arrives as **HTTP 200**
    /// carrying BOTH `data` and `errors`, with `errors[].path[0]` naming the failing ALIAS — and because
    /// **mutations execute serially**, the aliases before the failure DID land. So the response body says
    /// exactly which writes took effect. That is `Errors.Partial` (EX_PARTIAL, 4), and it is the one
    /// failure that must NEVER be queued: replaying the document would rewrite the half that already
    /// landed.
    val setFieldBatch:
        transport: IGitHubTransport ->
        board: BoardMap ->
        itemId: string ->
        writes: (string * FieldWrite) list ->
            IoResult<unit>

    /// What happened to a board write. Not a bool — the three outcomes need three different sentences.
    type WriteOutcome =
        /// It landed.
        | Written
        /// The budget was exhausted, so it is QUEUED and will be replayed by `flush`. This is the only
        /// failure that gets to make that promise (#510).
        | Deferred
        /// The issue is not an item on this board. A PERMANENT fact, and NOT queued — `flush` would drop it
        /// too, which would be a second promise nobody could keep.
        | NotOnBoard

    /// **THE ONE BOARD WRITE. Nothing else may call `setField` directly.**
    ///
    /// #510 is what happens when this is a convention rather than a chokepoint: `claim` deferred its write
    /// on an exhausted budget and `set-field` did not, so the two paths disagreed about a promise the tool
    /// was making in both.
    ///
    /// A refusal is REPORTED, never swallowed — a refusal nobody can read is a refusal that did not happen.
    val boardWrite:
        transport: IGitHubTransport ->
        board: BoardMap ->
        owner: string ->
        repo: string ->
        number: int ->
        field: string ->
        write: FieldWrite ->
        worker: string ->
            IoResult<WriteOutcome>

    /// **THE ONE BATCH BOARD WRITE (#448).** `boardWrite` for N fields in one aliased document.
    ///
    /// Carries the SAME deferral policy — an exhausted budget QUEUES every pair, so the whole batch replays
    /// intact — with one addition the transport forces: a batch can land HALF-WAY. A `Partial` (some aliases
    /// took effect) is NEVER queued, because replaying the document would rewrite what already landed; it
    /// surfaces as `Error(Partial …)` for the caller to render field-by-field.
    ///
    /// `expectedBlockedBy` requests a revision-conditional dependency write. The boundary re-reads both
    /// Projects item revision and field value; a mismatch fails stale with zero writes. Even when they
    /// match, GitHub Projects v2 supplies no compare-and-set input, so the batch fails closed with an
    /// unsupported mutation-authority diagnostic and zero writes. Conditional batches are never deferred.
    /// A Ready transition therefore needs an explicit board-write authority or a CAS-capable broker.
    val boardWriteBatch:
        transport: IGitHubTransport ->
        board: BoardMap ->
        owner: string ->
        repo: string ->
        number: int ->
        expectedBlockedBy: BlockedByObservation option ->
        writes: (string * FieldWrite) list ->
        worker: string ->
            IoResult<WriteOutcome>

    /// What one `flush` pass did. Every count is measured against the queue THIS pass read, so a caller
    /// never has to reconstruct one by re-reading the file (#862).
    ///
    /// That reconstruction is not a hypothetical: `flush` used to return `IoResult<int>`, so a stop
    /// DISCARDED the count it had already written and the caller re-read the queue to infer it. The
    /// deferral queue is a single shared file (`~/.cache/fsgg-coord`, every worker on the machine), so a
    /// concurrent `defer` in that window made the inference WRONG — reporting "nothing replayed" over
    /// writes that had landed. The counts belong to the pass that made them.
    type FlushOutcome =
        { /// What the queue held when this pass started reading it.
          Queued: int

          /// Replayed, and landed.
          Written: int

          /// Permanently un-writable — an unparseable ref, or an item no longer on this board. Dropped,
          /// never retried, and NEVER counted as written: `Written` is what a caller renders as "replayed
          /// N of M", and counting a drop there reports a write that never happened (#266).
          Dropped: int

          /// Queued against a DIFFERENT board, and therefore left alone (#882).
          ///
          /// THE OPPOSITE OF `Dropped`, and the distinction is the whole of #882: a dropped write will never
          /// land, a skipped one is still owed and still landable — just not by this pass, against this
          /// board. They are counted apart because "we discarded your write" and "your write is waiting for
          /// its own board" are opposite things to tell a worker.
          Skipped: int

          /// A fresh rate limit STOPPED the pass. The remainder — `Queued - Written - Dropped - Skipped` —
          /// is still queued, untouched and not re-appended, and the caller must back off (`EX_RATE`).
          Stopped: IoError option }

    /// Replay the deferred queue.
    ///
    /// An entry that succeeds is DROPPED. An exhausted budget STOPS the flush — the rest would fail
    /// identically, and spending REST calls to confirm that is exactly the back-off `EX_RATE` exists to
    /// prevent. An entry that is permanently un-writable is dropped LOUDLY: it will never land, and
    /// carrying it forever would mean the queue never drains and nobody is ever told why.
    ///
    /// An entry queued against ANOTHER board is SKIPPED, never dropped (#882): this pass cannot resolve it —
    /// asking THIS board about it yields `NotOnBoard`, which is permanent and false — so it stays queued for
    /// the flush that can. An entry that recorded no board at all (queued before #882) is replayed here,
    /// exactly as it always was.
    ///
    /// `Error` is reserved for a queue that could not be READ. A rate-limit that stops the pass is NOT an
    /// error here — it is `Stopped`, carried alongside the work that did land, because those are different
    /// facts and a caller needs both.
    val flush: transport: IGitHubTransport -> board: BoardMap -> IoResult<FlushOutcome>
