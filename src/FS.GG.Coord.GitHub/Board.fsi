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
/// *"this issue is not on the board"* — then printed a remediation telling the worker to run `item-add`,
/// which CREATED A SECOND BOARD ITEM for an issue that already had one. A budget failure did not merely
/// report the wrong thing. It corrupted the board, while sounding helpful. So `Offboard` is a case here,
/// it is reached only from a SUCCESSFUL read, and `RateLimited` can never become it.
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

    /// Resolve the board and its field/option ids. Two GraphQL calls, then cached for a day.
    val bootstrap: transport: IGitHubTransport -> owner: string -> title: string -> IoResult<BoardMap>

    /// The board item id for an issue.
    ///
    /// `Ok None` means **the issue is genuinely not on this board** — a successful read with a definite
    /// answer, and the only thing that licenses an `item-add`. It is UNREACHABLE from a failed read: a
    /// rate-limited lookup returns `Error(RateLimited …)`, and #421 is the two of them being the same value.
    ///
    /// Item ids are stable, so this is cached forever once resolved.
    val itemId:
        transport: IGitHubTransport ->
        board: BoardMap ->
        owner: string ->
        repo: string ->
        number: int ->
            IoResult<string option>

    /// Read ONE item's `Status` column — the pre-claim column of #481, so `release`/`reap` can restore what
    /// a claim overwrote instead of guessing `Ready`.
    ///
    /// It is a `fieldValueByName` RESOLVER read — one point, one item, no node multiplication — and NOT a
    /// board scan, deliberately: this sits on `take` → `claim`, the hottest path on the budget that dies
    /// first (#418), so a full-board read here would be the regression #481 is written to avoid.
    ///
    /// `Ok None` is a definite answer with two shapes — the issue is not on THIS board, or it is but its
    /// `Status` is unset — both meaning "no column to restore", which a claim records as none and `release`
    /// then puts back as `Ready`. A failed read is `Error`, never `Ok None`: absence may not be manufactured.
    val itemStatus:
        transport: IGitHubTransport ->
        board: BoardMap ->
        owner: string ->
        repo: string ->
        number: int ->
            IoResult<BoardStatus option>

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
    val boardWriteBatch:
        transport: IGitHubTransport ->
        board: BoardMap ->
        owner: string ->
        repo: string ->
        number: int ->
        writes: (string * FieldWrite) list ->
        worker: string ->
            IoResult<WriteOutcome>

    /// Replay the deferred queue.
    ///
    /// An entry that succeeds is DROPPED. An exhausted budget STOPS the flush — the rest would fail
    /// identically, and spending REST calls to confirm that is exactly the back-off `EX_RATE` exists to
    /// prevent. An entry that is permanently un-writable is dropped LOUDLY: it will never land, and
    /// carrying it forever would mean the queue never drains and nobody is ever told why.
    val flush: transport: IGitHubTransport -> board: BoardMap -> IoResult<int>
