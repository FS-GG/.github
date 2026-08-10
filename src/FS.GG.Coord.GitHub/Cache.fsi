namespace FS.GG.Coord.GitHub

/// The 90-second shared scan cache, and the ETag store — with the two invariants that make them safe.
///
/// The cache exists because of #418: the GraphQL budget is 5,000 pt/hr for the WHOLE FLEET, and five
/// workers looping `take` drained it in fifteen minutes. It is shared at the USER level, not per-process,
/// so N workers on one box pay for one board scan between them. That is the point.
///
/// It is also, structurally, the most dangerous thing in this codebase — because a cache is a machine for
/// turning one answer into many, and if the answer it memoises is a FAILED READ, it hands that failure to
/// every worker for the next ninety seconds, wearing the clothes of a fact. Both invariants below exist
/// to stop exactly that.
module Cache =
    val getIntakeReceipt: draftId: string -> Result<FS.GG.Coord.IntakeReceipt.Receipt option, string>
    val putIntakeReceipt: receipt: FS.GG.Coord.IntakeReceipt.Receipt -> Result<unit, string>
    type IntakeIntent = { DraftId: string; Owner: string; Repository: string; DraftDigest: string }
    val getIntakeIntent: draftId: string -> Result<IntakeIntent option, string>
    val putIntakeIntent: intent: IntakeIntent -> Result<unit, string>
    /// Serialize one draft's receipt/create boundary. The OS releases the lock on process death.
    val withIntakeLock: draftId: string -> action: (unit -> 'a) -> Result<'a, string>

    open Errors

    /// WHY the caller is reading. This is a TYPE because it is a RULE, and the rule was violated by
    /// convention for as long as it was a convention.
    ///
    /// A scheduler may serve a stale board: the worst a stale scan can do is offer an item somebody just
    /// claimed, and the claim CAS — which reads MARKERS over REST, never this cache — is what actually
    /// decides who holds it. The loser retries. **Staleness costs a retry; it cannot cost a double-claim.**
    ///
    /// A reconciler may NEVER serve a stale board. Its entire job is to say what is true *right now*, and a
    /// cached "truth" is how a reconciler reports drift that was already fixed — or misses drift that
    /// isn't.
    ///
    /// **AND THE CHORE OFFER IS ON THE RECONCILER'S SIDE, NOT THE SCHEDULER'S** (.github#1679). It rode
    /// `Scheduling` for its whole life because it is reached from `next` — but the warrant above is about
    /// CLAIMING AN ITEM, and it is right about claiming an item. None of it transfers to a chore:
    ///
    /// - **There is no CAS behind a chore's PREMISE.** The chore lock is a CAS over *who performs* the
    ///   chore; nothing re-decides whether it was owed. `Chore.isRetired` works by RE-DERIVING, and a
    ///   re-derivation over the same cached board answers "still owed" against a write that has already
    ///   landed — so a stale offer survives its own discharge for the whole TTL rather than being corrected
    ///   by the next reader.
    /// - **The cost is not a retry.** It is a worker handed a written instruction to perform a board write
    ///   that is already done, and a per-repo lock taken to serialise it — measured as eight needless
    ///   offers and eight needless lock acquisitions across three repos (.github#1649, .github#1679).
    /// - **The offer fires at the WORST instant for a stale column.** `done --flip` is precisely the moment
    ///   a board column was most recently written, and `offerChoreAfterDone` buys a dedicated scan on the
    ///   stated grounds that `done` "holds no snapshot ... so the offer costs a scan". Serving that
    ///   paid-for scan out of the cache gives back the freshness it was bought for.
    ///
    /// **THIS DOES NOT SHRINK THE CACHE'S JOB, AND MUST NOT BE READ AS DOING SO.** #418's subject is the
    /// SCHEDULING POLL — five workers looping `take` drained the fleet's 5,000 pt/hr in fifteen minutes —
    /// and `Scheduling` is untouched: `next`/`take`/`batch` still serve their decision from one shared scan.
    /// The offer is not in that loop. It is gated on a repo having a chore lock at all, it fires once per
    /// `done --flip` and only on a `next` that already found nothing, and the REST half of its read
    /// (`Scan.snapshot`'s per-open-row body and marker reads — the expensive half, ~85 requests unscoped)
    /// was never cached and is paid either way. What changes hands is the GraphQL board query alone.
    ///
    /// **AND THAT QUERY WAS MEASURED RATHER THAN ESTIMATED**, because the estimate is how #1086 got the
    /// same trade wrong by an order of magnitude and called it a saving. On the live Coordination board,
    /// 2026-07-28: one `scan --fresh` moved the GraphQL budget 3516 → 3465 — **51 points of 5,000/hr**,
    /// brackets included, so the scan itself is at most that. Roughly one percent of the fleet's hourly
    /// budget per offer, on a path that fires once per completed item. #418's failure was five workers
    /// looping a board read; this is not that shape, and the number says so.
    ///
    /// **And it is paid back in kind**: a fresh scan WRITES the cache (`Scan.scanFresh` → `putScan`), so an
    /// offering read leaves a fresh board behind it for the next ninety seconds of `take`s. The offer path
    /// stops being a consumer of the shared scan and becomes a producer of it.
    /// **THE #353 COLLISION SCAN IS NO LONGER A CONSUMER OF THIS CACHE AT ALL** (.github#1740 → #1779), and
    /// the history is worth keeping because it is the same mistake twice. `Client.activeCollisions` — behind
    /// `overlap --active`, `widen` and `set-paths` — rode `Scheduling` while `Reconciling` below had listed
    /// `overlap --active` all along; the doc and the code had simply never been read against each other.
    /// #1740 moved it to `Reconciling`. The warrant at the top of this file is about CLAIMING AN ITEM and is
    /// right about it; it never transferred, for the same reason the chore offer's did not:
    ///
    /// - **There is no CAS behind a touch-set.** The item CAS decides who holds an ITEM. Nothing whatsoever
    ///   re-decides who may edit a FILE — the touch-set is the only mechanism, and a false `DISJOINT` is
    ///   final. Staleness on that consumer costs a double-EDIT, not a retry.
    /// - **It was observed, not theorised.** 2026-07-28: two workers claimed items declaring
    ///   `src/FS.GG.Coord.Cli/Client.fs` 41 seconds apart; the `widen` 53 seconds later printed `DISJOINT`
    ///   off this cache. The same check two hours later printed `OVERLAP`. Both edited the file.
    ///
    /// **BUT A FRESHER BOARD WAS NEVER THE ANSWER, AND THAT IS #1779'S FINDING.** Freshness only fixes a
    /// STALE READ of the column. `claim` exits GREEN with the column unwritten in three other ways —
    /// `statusWrite: deferred | failed | not-on-board` — and the freshest possible board read still shows
    /// the pre-claim column in every one of them. #1740 patched the deferred case off the deferral queue and
    /// declined the other two. #1779 removed the column from the decision instead: the candidate set is now
    /// `Reads.openIssues` (REST, repo-scoped) filtered on TOKENS, with a marker read only for a row that
    /// actually collides. No board query, no cache tier, no queue read.
    ///
    /// **SO THE COST WENT DOWN RATHER THAN UP**, measured on the live board, 2026-07-28, either side of one
    /// `overlap --active`: `Scheduling` (warm) **0–4** points → `Reconciling` (fresh) **19–25** → marker-keyed
    /// **0**, on a cold cache as well as a warm one, because nothing on this path asks GraphQL anything. See
    /// `Client.activeCollisions` for the full table and the REST side.
    ///
    /// The `Scheduling`/`Reconciling` distinction below still matters for every OTHER consumer, and the rule
    /// this scan's history teaches is unchanged: a consumer that has not been named in a warrant is a
    /// consumer that warrant was never checked against.
    type ReadIntent =
        /// `next` / `take` / `batch` — the DECISION half. May serve a cached scan.
        | Scheduling
        /// `ready` / `lint` / `who`. Always scans fresh. (The #353 collision scan was listed here by #1740
        /// and reads no board at all since .github#1779 — see above.)
        | Reconciling
        /// The deferred chore queue's board (`Client.wholeBoard`, at `next` and at `done --flip`). Always
        /// scans fresh, for the reasons above — kept DISTINCT from `Reconciling` because the two are fresh
        /// for different reasons, and a case that merely borrowed `Reconciling`'s warrant would be the same
        /// un-rechecked reuse that put the offer on `Scheduling` in the first place.
        | Offering

    /// Where the cache lives. `FSGG_COORD_CACHE`, else `$XDG_CACHE_HOME/fsgg-coord`, else
    /// `$HOME/.cache/fsgg-coord`.
    ///
    /// THE ENV VAR NAME IS PART OF THE CONTRACT. Every cache assertion in the corpus isolates itself by
    /// setting `FSGG_COORD_CACHE` to a throwaway directory; rename it and the whole cache half of the
    /// fixture silently stops testing anything — it would be measuring a cache nobody is using.
    val root: unit -> string

    /// The scan TTL in seconds. `FSGG_COORD_SCAN_TTL_SEC`, default 90. **Zero disables the cache.**
    val scanTtlSeconds: unit -> int

    /// Serve the cached board scan, if the intent permits it and it is fresh.
    ///
    /// `None` means MISS — go and read. It never means "the board is empty": a miss is the absence of a
    /// cached answer, not the presence of an empty one, and those two have been the same value in this
    /// domain once too often.
    ///
    /// Keyed on the BOARD (owner + project title), not the worker: `FSGG_COORD_OWNER`/`PROJECT` can point
    /// this client at a different board, and serving one board's items for another is not a stale answer,
    /// it is a WRONG one — and nothing downstream would notice.
    val getScan: intent: ReadIntent -> owner: string -> title: string -> string option

    /// Store a board scan.
    ///
    /// **INVARIANT: A FAILED READ IS NEVER RESCUED BY THE CACHE.** An EMPTY scan is never written. This is
    /// enforced HERE, at the write, and not at the read — because the caller that has just failed to read
    /// the board is holding an empty list either way, and the only place the difference is still knowable
    /// is the moment it tries to memoise it.
    ///
    /// A failed scan that reached the cache would write *"the board is empty"* into it and hand that,
    /// confidently, to the next ninety seconds of workers — #344's confident-empty-board, laundered
    /// through the cache and multiplied by the fleet. A genuinely empty board simply re-scans. That is the
    /// right price, and it is a low one.
    ///
    /// Returns whether it stored anything, so a caller can say so rather than assume.
    val putScan: owner: string -> title: string -> scan: string -> bool

    /// Fold OUR OWN write into the cached scan, rather than invalidating it.
    ///
    /// Invalidating would send the very next `take` back to a full-board scan — and a claim is ALWAYS
    /// followed by a take — so the cache would never survive the loop it exists for. Only the three fields
    /// the scan actually carries can be folded; anything else leaves the cache untouched rather than
    /// writing a field the scan has no slot for.
    val patchScan:
        owner: string -> title: string -> issueOwner: string -> repo: string -> number: int -> field: string -> value: string -> unit

    /// Drop the cached scan for a board. Used by `--fresh`.
    val dropScan: owner: string -> title: string -> unit

    /// The stored ETag for a REST path, if we have one.
    ///
    /// **THE LOCK IS NEVER READ FROM A CACHE, AND THEREFORE NEVER CONDITIONALLY.** A 304 serving a body
    /// captured before a claim marker was posted would report `comments: 0` and hide a live lock — a
    /// failed read wearing an empty set's clothes, one layer beneath #461. The claim scan must not call
    /// this, and the corpus asserts that its request carries no `If-None-Match` at all.
    val getETag: path: string -> string option

    /// The cached body for a REST path — what a 304 entitles us to serve.
    ///
    /// A 304 with NO cached body is a protocol violation by us, not by the server: we sent a validator we
    /// could not honour. It is an error, never an empty result.
    val getBody: path: string -> IoResult<string>

    /// Store a body and its ETag together. They are ONE fact — a body without its validator cannot be
    /// revalidated, and a validator without its body is what makes a 304 unanswerable.
    val putBody: path: string -> etag: string option -> body: string -> unit

    // ---- the board-map cache (#418) ----------------------------------------------------------------

    /// The board TTL in seconds. `FSGG_COORD_BOARD_TTL_SEC`, default 86400 (a day). **Zero disables it.**
    ///
    /// The field/option ID map is cached for a DAY, not the 90 seconds the scan is, because these are IDs
    /// and ids do not change. Conflating the two would re-resolve the whole field map on every `next`/`take`
    /// — two GraphQL points per worker per invocation, on the budget that dies first (#418) — which is the
    /// exact cost `bootstrap` exists to pay once.
    val boardTtlSeconds: unit -> int

    /// The cached board map (the `bootstrap` JSON), if we have one and it is fresh.
    ///
    /// `None` is a MISS — go and bootstrap — never "the board has no fields". Not gated on `ReadIntent`: a
    /// reconciler needs the field ids too, and the ids are stable; it is the item STATE (the scan) a
    /// reconciler may never serve stale, not the schema.
    val getBoardMap: owner: string -> title: string -> string option

    /// Store a board map. A document that is not a JSON object carrying a non-empty `fields` map is NEVER
    /// written — an empty field map is a bootstrap that went wrong (#199-shape), and caching it would make
    /// every write for a day fail with "no field named Status". Returns whether it stored anything.
    val putBoardMap: owner: string -> title: string -> board: string -> bool

    /// Drop the cached board map for a board. Used by `bootstrap --refresh`.
    val dropBoardMap: owner: string -> title: string -> unit

    /// The cached board item id for an issue on a board, if we have resolved it before.
    ///
    /// Item ids are STABLE, so this has no TTL — once resolved, forever. Only a FOUND id is ever cached: a
    /// "not on this board" answer (#421's `Ok None`) is NOT memoised, because an item added later would
    /// then be invisible for the life of the cache.
    val getItemId: owner: string -> repo: string -> number: int -> boardNumber: int -> string option

    /// Store a resolved board item id.
    val putItemId: owner: string -> repo: string -> number: int -> boardNumber: int -> id: string -> unit

    /// The highest message id this worker has already seen (the `inbox` cursor). `0` for a fresh mailbox,
    /// and `0` for an unreadable or malformed cursor too.
    ///
    /// The fallback direction is the opposite of the lock's, on purpose: a lost cursor re-shows old mail
    /// (noise), where a cursor read too HIGH would hide new mail. So it fails toward showing too much. Keyed
    /// on the (slugged) worker id, matching the bash client's `inbox-<slug>` file, so the cursor survives a
    /// worker switching engines mid-loop.
    val inboxCursor: worker: string -> int64

    /// Advance the `inbox` cursor to the highest message id seen. `inbox --peek` does NOT call this — leaving
    /// the cursor un-advanced is the entire meaning of `--peek`.
    val putInboxCursor: worker: string -> id: int64 -> unit

    /// The deferred board-write queue (`pending.jsonl`).
    ///
    /// ONLY an exhausted budget may be queued (`Errors.isQueueable`). Every other failure is permanent, and
    /// queuing a permanent failure is a promise that can never be kept — `flush` would replay it forever,
    /// the refusal would never reach the operator, and the tool would report success over a write it had
    /// dropped. That is #510, and the type is what stops it being rewritten.
    type Deferred =
        { Ref: string
          Field: string
          Value: string
          At: string
          Worker: string
          /// The board this write was queued against, as `(owner, project title)` (#882).
          ///
          /// `Ref` names an ISSUE, and an issue can sit on several boards — so without this, `flush` resolved
          /// every entry against whatever board the environment happened to point at, found the item missing,
          /// and dropped the write as "permanently un-writable". It was writable; the board was simply not
          /// the one it was queued for.
          ///
          /// `None` is a pre-#882 entry that recorded no board — replayed against the current board, which is
          /// the behaviour it was queued under. It is not an invitation to guess.
          Board: (string * string) option }

    /// Do two board identities name the same board?
    ///
    /// The equivalence the cache FILENAMES use: every other cache file is keyed on a slug of owner/title, so
    /// two identities that slug alike already share a scan cache and a board map.
    val sameBoard: string * string -> string * string -> bool

    /// Queue a board write. The error is taken, not a bool, so that a caller CANNOT queue a write without
    /// having in hand the failure that licenses it.
    val defer: error: IoError -> entry: Deferred -> IoResult<unit>

    /// Everything currently queued.
    val pending: unit -> IoResult<Deferred list>

    /// Drop one entry — it has been replayed successfully, or it is permanently un-writable.
    val dropPending: entry: Deferred -> unit

    /// UNLINK the queue, rather than truncating it.
    ///
    /// An empty file and an absent file are different facts, and the corpus asserts the difference: a
    /// zero-byte `pending.jsonl` reads as "there is a queue and it is empty", which is a claim about state
    /// nobody made. When the last entry drains, the queue ceases to exist.
    val clearPending: unit -> unit
