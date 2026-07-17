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
    type ReadIntent =
        /// `next` / `take` / `batch`. May serve a cached scan.
        | Scheduling
        /// `ready` / `lint` / `who` / `overlap --active`. Always scans fresh.
        | Reconciling

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
    val patchScan: owner: string -> title: string -> repo: string -> number: int -> field: string -> value: string -> unit

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
