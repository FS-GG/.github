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
          Worker: string }

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
