namespace FS.GG.Coord.GitHub

module Cache =

    open System
    open System.IO
    open System.Text.Json
    open System.Text.Json.Nodes
    open System.Text.RegularExpressions
    open Errors

    type ReadIntent =
        | Scheduling
        | Reconciling
        | Offering

    let root () =
        match Environment.GetEnvironmentVariable "FSGG_COORD_CACHE" with
        | null
        | "" ->
            let xdg =
                match Environment.GetEnvironmentVariable "XDG_CACHE_HOME" with
                | null
                | "" -> Path.Combine(Environment.GetFolderPath Environment.SpecialFolder.UserProfile, ".cache")
                | x -> x

            Path.Combine(xdg, "fsgg-coord")
        | c -> c

    [<Literal>]
    let private DefaultScanTtl = 90

    let scanTtlSeconds () =
        match Environment.GetEnvironmentVariable "FSGG_COORD_SCAN_TTL_SEC" with
        | null
        | "" -> DefaultScanTtl
        | v ->
            match Int32.TryParse v with
            | true, n when n >= 0 -> n
            // A TTL we cannot parse is not a TTL of zero and it is not the default either — but refusing
            // to run over a malformed env var would be a hard failure on a soft misconfiguration. Fall
            // back to NO CACHE: the safe direction is always to pay for the read.
            | _ -> 0

    /// A filename-safe slug. The cache key must not be able to escape the cache directory, and a repo or
    /// project title is arbitrary text.
    let private slug (s: string) =
        let cleaned = Regex.Replace(s, @"[^A-Za-z0-9._-]+", "-")
        cleaned.Trim('-').ToLowerInvariant()

    let private ensureRoot () =
        let r = root ()
        Directory.CreateDirectory r |> ignore
        r

    let private scanFile (owner: string) (title: string) =
        Path.Combine(ensureRoot (), $"scan-%s{slug owner}-%s{slug title}.json")

    let getScan (intent: ReadIntent) (owner: string) (title: string) =
        match intent with
        // THE RULE, AS CONTROL FLOW. A reconciler cannot reach the cache at all — not "should not", CANNOT.
        // For as long as this was a convention, it was a convention that got violated.
        //
        // AND NEITHER CAN THE CHORE OFFER (.github#1679). It is listed SEPARATELY rather than folded into
        // the line above, because these two are fresh for different reasons and the `.fsi` states both: a
        // reconciler's job is to say what is true right now, where an offer is an INSTRUCTION TO WRITE
        // issued at the instant a write just landed, with nothing behind it that re-decides whether it was
        // owed. Merging the cases would put the offer back on a warrant that was never checked against it,
        // which is the whole of the defect.
        | Reconciling
        | Offering -> None
        | Scheduling ->

        let ttl = scanTtlSeconds ()

        if ttl <= 0 then
            None
        else

        let file = scanFile owner title

        if not (File.Exists file) then
            None
        else

        let info = FileInfo file

        // A ZERO-BYTE cache file is not an empty board. It is a torn write, or a `putScan` that was killed
        // between `Create` and `Write`. Serving it would be the confident-empty-board again, arriving
        // through the file system instead of through the network.
        if info.Length = 0L then
            None
        else

        let age = DateTimeOffset.UtcNow - DateTimeOffset(info.LastWriteTimeUtc, TimeSpan.Zero)

        if age.TotalSeconds >= float ttl then
            None
        else
            try
                Some(File.ReadAllText file)
            with :? IOException ->
                // A cache we cannot read is a MISS, never a failure. The whole point of a cache is that it
                // is optional — failing the caller's read because our optimisation broke would make the
                // cache strictly worse than not having one.
                None

    /// Does this scan document actually carry items?
    ///
    /// This is the guard behind "a failed read is never rescued by the cache", and it is deliberately
    /// strict: a document that is not a non-empty JSON array is not a board scan we are willing to memoise.
    /// Bytes that do not parse are the #461 shape — a truncated page, a proxy's HTML — and an empty array
    /// is #344's confident empty board.
    let private isNonEmptyScan (scan: string) =
        if String.IsNullOrWhiteSpace scan then
            false
        else
            try
                use doc = JsonDocument.Parse scan

                doc.RootElement.ValueKind = JsonValueKind.Array
                && doc.RootElement.GetArrayLength() > 0
            with :? JsonException ->
                false

    let putScan (owner: string) (title: string) (scan: string) =
        // THE INVARIANT. An empty or unparseable scan is NEVER written. The caller that just failed to read
        // the board is holding an empty list either way — this is the last moment at which the difference
        // between "the board is empty" and "I could not read the board" is still knowable, and it is
        // therefore the only place the distinction can be enforced.
        if not (isNonEmptyScan scan) then
            false
        else
            try
                let file = scanFile owner title

                // Write-then-move, so a reader never sees a half-written scan. Two workers memoising the
                // same board concurrently is normal and benign; a reader seeing four kilobytes of one is
                // not.
                let temp = file + ".tmp." + string (Environment.ProcessId)
                File.WriteAllText(temp, scan)
                File.Move(temp, file, overwrite = true)
                true
            with :? IOException ->
                // Failing to CACHE is never failing to READ. The caller has its answer; we simply did not
                // get to keep it.
                false

    /// The three fields the board scan carries. Anything else is not foldable — and a field the scan has no
    /// slot for must leave the cache alone rather than inventing one.
    let private scanKey (field: string) =
        match field with
        | "Status" -> Some "status"
        | "Phase" -> Some "phase"
        | "Blocked by" -> Some "blockedBy"
        | _ -> None

    let patchScan
        (owner: string)
        (title: string)
        (issueOwner: string)
        (repo: string)
        (number: int)
        (field: string)
        (value: string)
        =
        match scanKey field with
        | None -> ()
        | Some key ->

        let file = scanFile owner title

        if not (File.Exists file) then
            ()
        else
            try
                let text = File.ReadAllText file

                // THE TTL IS MEASURED FROM THIS FILE'S MTIME (`getScan`), AND A PATCH IS NOT A READ. A patch
                // folds a board write we just made into a cache whose FRESHNESS is otherwise unchanged — the
                // board was last actually read at `putScan` time, not now. Rewriting the file stamps `now` and
                // restarts the TTL clock, so a worker in the take → claim → …writes… → done loop keeps
                // resetting its own cache to fresh and never sees another worker's board changes until it
                // idles out the window or runs `--fresh` (#1152). Capture the pre-fold mtime and restore it
                // after the rewrite: only a real read (`putScan`) may restart the clock.
                let preFoldWriteTimeUtc = File.GetLastWriteTimeUtc file
                let node = JsonNode.Parse text

                match node with
                | :? JsonArray as items ->
                    for item in items do
                        match item with
                        | :? JsonObject as o ->
                            let matches =
                                let o' = o.["owner"]
                                let r = o.["repo"]
                                let n = o.["number"]

                                // Older cache records predate the owner field. They are necessarily
                                // ambiguous, so retain their historic same-repository fold only when the
                                // write itself names the board owner; an explicit external owner must
                                // never cross-fold a default-owner same-name row (#2143).
                                not (isNull r)
                                && not (isNull n)
                                && ((not (isNull o') && o'.GetValue<string>() = issueOwner)
                                    || (isNull o' && issueOwner = owner))
                                && r.GetValue<string>() = repo
                                && n.GetValue<int>() = number

                            if matches then
                                // An empty value CLEARS the field. It does not write an empty string —
                                // those are different states on the board, and conflating them is how a
                                // cleared `Blocked by` came back as the literal text "".
                                o.[key] <- if value = "" then null else JsonValue.Create value

                        | _ -> ()

                    let temp = file + ".tmp." + string (Environment.ProcessId)
                    File.WriteAllText(temp, items.ToJsonString())
                    File.Move(temp, file, overwrite = true)
                    File.SetLastWriteTimeUtc(file, preFoldWriteTimeUtc)

                | _ -> ()

            with
            | :? JsonException
            | :? IOException ->
                // A cache we could not fold is a cache that is now STALE about our own write. That is a
                // miss on the next read, which is safe. It is not a reason to fail the write that
                // succeeded.
                ()

    let dropScan (owner: string) (title: string) =
        try
            File.Delete(scanFile owner title)
        with :? IOException ->
            ()

    // ---- the ETag store ---------------------------------------------------------------------------

    let private etagPaths (path: string) =
        let r = ensureRoot ()
        let key = slug path
        Path.Combine(r, $"issues-%s{key}.body"), Path.Combine(r, $"issues-%s{key}.etag")

    let getETag (path: string) =
        let _, etagFile = etagPaths path

        if not (File.Exists etagFile) then
            None
        else
            try
                match File.ReadAllText(etagFile).Trim() with
                | "" -> None
                | e -> Some e
            with :? IOException ->
                None

    let getBody (path: string) : IoResult<string> =
        let bodyFile, _ = etagPaths path

        if not (File.Exists bodyFile) then
            // WE SENT A VALIDATOR WE COULD NOT HONOUR. The server said "what you have is current" and we
            // do not have it. That is our bug, and it is an ERROR — serving an empty body here would turn
            // a protocol violation into an empty result set, which is the whole failure class this port
            // exists to end.
            Error(
                Malformed(
                    path,
                    "the server answered 304 Not Modified, but there is no cached body to serve — the ETag store and the body store are out of step. Re-run with --refresh."
                )
            )
        else
            try
                Ok(File.ReadAllText bodyFile)
            with :? IOException as e ->
                Error(Malformed(path, $"the cached body could not be read: %s{e.Message}"))

    let putBody (path: string) (etag: string option) (body: string) =
        try
            let bodyFile, etagFile = etagPaths path
            File.WriteAllText(bodyFile, body)

            match etag with
            | Some e -> File.WriteAllText(etagFile, e)
            // NO ETAG MEANS NO VALIDATOR. Drop any stale one rather than keeping it: revalidating a NEW
            // body against an OLD validator is how a 304 comes to serve the wrong bytes.
            | None ->
                if File.Exists etagFile then
                    File.Delete etagFile

        with :? IOException ->
            ()

    // ---- the board-map cache (#418) ---------------------------------------------------------------

    [<Literal>]
    let private DefaultBoardTtl = 86400

    let boardTtlSeconds () =
        match Environment.GetEnvironmentVariable "FSGG_COORD_BOARD_TTL_SEC" with
        | null
        | "" -> DefaultBoardTtl
        | v ->
            match Int32.TryParse v with
            | true, n when n >= 0 -> n
            // A TTL we cannot parse is not zero and not the default — but the safe direction for a cache is
            // always to pay for the read, so a malformed value disables it rather than failing the run.
            | _ -> 0

    let private boardMapFile (owner: string) (title: string) =
        Path.Combine(ensureRoot (), $"boardmap-%s{slug owner}-%s{slug title}.json")

    /// A board map worth memoising: a JSON object whose `fields` is a non-empty object. The mirror of
    /// `isNonEmptyScan` — an empty field map is #199's shape, a bootstrap that failed to walk the document,
    /// and caching it would make every write fail with "no field named Status" for a day.
    let private isUsableBoardMap (board: string) =
        if String.IsNullOrWhiteSpace board then
            false
        else
            try
                use doc = JsonDocument.Parse board

                doc.RootElement.ValueKind = JsonValueKind.Object
                && (match doc.RootElement.TryGetProperty "fields" with
                    | true, f -> f.ValueKind = JsonValueKind.Object && f.EnumerateObject() |> Seq.isEmpty |> not
                    | _ -> false)
            with :? JsonException ->
                false

    let getBoardMap (owner: string) (title: string) =
        let ttl = boardTtlSeconds ()

        if ttl <= 0 then
            None
        else

        let file = boardMapFile owner title

        if not (File.Exists file) then
            None
        else

        let info = FileInfo file

        // A zero-byte file is a torn write, not a cached board — the same discipline `getScan` keeps.
        if info.Length = 0L then
            None
        else

        let age = DateTimeOffset.UtcNow - DateTimeOffset(info.LastWriteTimeUtc, TimeSpan.Zero)

        if age.TotalSeconds >= float ttl then
            None
        else
            try
                Some(File.ReadAllText file)
            with :? IOException ->
                None

    let putBoardMap (owner: string) (title: string) (board: string) =
        if not (isUsableBoardMap board) then
            false
        else
            try
                let file = boardMapFile owner title
                let temp = file + ".tmp." + string (Environment.ProcessId)
                File.WriteAllText(temp, board)
                File.Move(temp, file, overwrite = true)
                true
            with :? IOException ->
                false

    let dropBoardMap (owner: string) (title: string) =
        try
            File.Delete(boardMapFile owner title)
        with :? IOException ->
            ()

    let private itemIdFile (owner: string) (repo: string) (number: int) (boardNumber: int) =
        // Keyed on the BOARD too: an issue can sit on several boards, and its item id differs per board.
        Path.Combine(ensureRoot (), $"itemid-%s{slug owner}-%s{slug repo}-%d{number}-b%d{boardNumber}.id")

    let getItemId (owner: string) (repo: string) (number: int) (boardNumber: int) =
        let file = itemIdFile owner repo number boardNumber

        if not (File.Exists file) then
            None
        else
            try
                match File.ReadAllText(file).Trim() with
                | "" -> None
                | id -> Some id
            with :? IOException ->
                None

    let putItemId (owner: string) (repo: string) (number: int) (boardNumber: int) (id: string) =
        // Never memoise an empty id — an absence must not be manufactured into a hit (#421).
        if String.IsNullOrWhiteSpace id then
            ()
        else
            try
                let file = itemIdFile owner repo number boardNumber
                let temp = file + ".tmp." + string (Environment.ProcessId)
                File.WriteAllText(temp, id)
                File.Move(temp, file, overwrite = true)
            with :? IOException ->
                ()

    // ---- the inbox cursor (per-worker high-water mark) --------------------------------------------

    let private inboxCursorFile (worker: string) =
        // `slug` matches the bash client's `inbox-$(slug "$w")`, so a worker's cursor is the same file on
        // both engines and a worker that switched mid-loop does not re-read mail it has already seen.
        Path.Combine(ensureRoot (), $"inbox-%s{slug worker}")

    /// The highest message id this worker has already seen. `0` — a fresh mailbox — when there is no cursor
    /// or it cannot be read.
    ///
    /// The failure direction is DELIBERATE and it is the opposite of the lock's. A lost cursor re-shows old
    /// mail (harmless noise); a cursor read as HIGHER than it is would hide new mail. So an unreadable or
    /// malformed cursor falls back to `0` — showing too much, never too little — where the lock, faced with
    /// the same ambiguity, fails the other way and blocks.
    let inboxCursor (worker: string) : int64 =
        let file = inboxCursorFile worker

        if not (File.Exists file) then
            0L
        else
            try
                match Int64.TryParse((File.ReadAllText file).Trim()) with
                | true, n when n >= 0L -> n
                | _ -> 0L
            with :? IOException ->
                0L

    /// Advance the cursor to the highest message id seen. `inbox --peek` never calls this — that is the
    /// whole of what `--peek` means.
    let putInboxCursor (worker: string) (id: int64) : unit =
        try
            let file = inboxCursorFile worker
            let temp = file + ".tmp." + string (Environment.ProcessId)
            File.WriteAllText(temp, string id)
            File.Move(temp, file, overwrite = true)
        with :? IOException ->
            ()

    // ---- the deferred board-write queue -----------------------------------------------------------

    type Deferred =
        { Ref: string
          Field: string
          Value: string
          At: string
          Worker: string
          /// The board this write was queued against, as `(owner, project title)`.
          ///
          /// THE FIELD #882 IS ABOUT. `Ref` is `owner/repo#n` — an ISSUE, which can sit on several boards —
          /// while the board itself came from `$FSGG_COORD_OWNER`/`$FSGG_COORD_PROJECT` at queue time and was
          /// recorded nowhere. `flush` then resolved every entry against whatever board it happened to
          /// bootstrap: repoint the config, and the entry's item is `NotOnBoard`, which is PERMANENT, so the
          /// write was dropped and reported as "permanently un-writable" — of a write that was perfectly
          /// writable, against the board nobody wrote down.
          ///
          /// `None` means the entry was queued by a build that predates #882 and recorded no board. It is not
          /// a licence to guess: see `flush`, which replays it against the current board exactly as it always
          /// did, because that is the behaviour it was queued under.
          Board: (string * string) option }

    /// Do two board identities name the same board?
    ///
    /// THE SAME EQUIVALENCE THE CACHE FILENAMES USE, and it lives here because `slug` does. Every other cache
    /// file is keyed on `slug owner`/`slug title`, so two identities that slug alike already share a scan
    /// cache and a board map — a comparison that disagreed with the filenames would let `flush` skip an entry
    /// whose board it is demonstrably already serving from cache.
    let sameBoard ((ao, at): string * string) ((bo, bt): string * string) =
        slug ao = slug bo && slug at = slug bt

    let private pendingFile () = Path.Combine(ensureRoot (), "pending.jsonl")

    // THE QUEUE IS ONE FILE, SHARED BY EVERY WORKER ON THE MACHINE. `root ()` is keyed on neither the
    // worktree, the worker, nor the board, so `pending.jsonl` is exactly the fan-out this protocol exists to
    // support — and `defer` (append) and `dropPending` (read-all, filter, write-all) did not compose:
    //
    //   A: flush -> dropPending -> pending () reads [e1]
    //   B:                         defer appends e2       <- file is now [e1; e2]
    //   A:                         WriteAllText []        <- e2 is GONE
    //
    // B was told "QUEUED; flush replays it" and it never would be: the exact promise-not-kept that #510 was
    // filed for, one layer down, landing on the worker who did nothing wrong (#881).
    //
    // THE LOCK IS ITS OWN FILE, and that is not incidental. `clearPending` UNLINKS `pending.jsonl` when the
    // last entry drains — so a lock taken on the queue itself would be a lock on an inode that no longer has
    // a name, and the next worker through `OpenOrCreate` would create a NEW file and take an uncontended
    // lock on it. Two workers, two inodes, one queue, no exclusion. The lock file is therefore never
    // unlinked; it is a mutex, not a queue.
    let private pendingLockFile () = Path.Combine(ensureRoot (), "pending.lock")

    [<Literal>]
    let private LockPollMs = 25

    [<Literal>]
    let private DefaultLockTimeoutMs = 5000

    let private lockTimeoutMs () =
        match Environment.GetEnvironmentVariable "FSGG_COORD_LOCK_TIMEOUT_MS" with
        | null
        | "" -> DefaultLockTimeoutMs
        | v ->
            match Int32.TryParse v with
            | true, n when n >= 0 -> n
            // The opposite fallback to `scanTtlSeconds`, and deliberately so. A TTL we cannot parse degrades
            // to "pay for the read", because the safe direction there is to spend. Here the safe direction is
            // to WAIT: a timeout of zero would make every contended queue operation refuse instantly, which
            // is a fail-open dressed as a config error.
            | _ -> DefaultLockTimeoutMs

    // ACQUISITION IS SEPARATE FROM THE BODY, so that an IOException thrown by `f` cannot be mistaken for
    // contention and retried — which would run `f` twice and rewrite the queue twice.
    let private tryAcquire (file: string) : FileStream option =
        try
            Some(new FileStream(file, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None))
        with :? IOException ->
            None

    /// Run `f` with exclusive access to the queue, or answer `None` if the lock did not free in time.
    ///
    /// NO STALE-LOCK POLICY, AND NONE IS NEEDED. The lock is the open handle, not a record written into the
    /// file, so the kernel drops it when the holder's last descriptor closes — including on SIGKILL, where no
    /// cleanup code of ours runs at all. A worker that dies mid-flush frees the queue by dying. This is the
    /// property that makes an advisory lock the cheap answer here rather than the one with a caretaker.
    let private withPendingLock (f: unit -> 'a) : 'a option =
        let file = pendingLockFile ()
        let deadline = DateTime.UtcNow.AddMilliseconds(float (lockTimeoutMs ()))

        let rec acquire () =
            match tryAcquire file with
            | Some fs -> Some fs
            | None ->
                if DateTime.UtcNow >= deadline then
                    None
                else
                    Threading.Thread.Sleep LockPollMs
                    acquire ()

        match acquire () with
        | None -> None
        | Some handle ->
            use _held = handle
            Some(f ())

    let private renderDeferred (e: Deferred) =
        let o = JsonObject()
        o.["ref"] <- JsonValue.Create e.Ref
        o.["field"] <- JsonValue.Create e.Field
        o.["value"] <- JsonValue.Create e.Value
        o.["at"] <- JsonValue.Create e.At
        o.["worker"] <- JsonValue.Create e.Worker

        // OMITTED, NOT NULLED, WHEN THERE IS NO BOARD. A `None` here is only ever reachable by re-rendering a
        // legacy entry during `dropPending`'s rewrite, and "the key is absent" is exactly how that entry
        // arrived. Writing `null` would invent a third state for `parseDeferred` to tell apart.
        match e.Board with
        | Some(owner, title) ->
            o.["boardOwner"] <- JsonValue.Create owner
            o.["boardTitle"] <- JsonValue.Create title
        | None -> ()

        o.ToJsonString()

    let defer (error: IoError) (entry: Deferred) : IoResult<unit> =
        // THE PRECONDITION IS THE ARGUMENT. A caller cannot queue a write without holding the failure that
        // licenses it, and only ONE failure does. This is #510 made unexpressible: the bug was that `claim`
        // tested for an exhausted budget before queuing and `set-field` did not, so `set-field` printed
        // "the write is QUEUED" over failures that could never be replayed — and `flush` then reported
        // success, confirming the lie.
        if not (isQueueable error) then
            Error error
        else
            // UNDER THE LOCK, even though the write is a single append. An append races a concurrent
            // `dropPending`'s rewrite, and it is the append that loses: see the lost-update sequence above.
            let append () =
                try
                    File.AppendAllText(pendingFile (), renderDeferred entry + "\n")
                    Ok()
                with :? IOException as e ->
                    Error(Transport $"the board write could not be queued: %s{e.Message}")

            match withPendingLock append with
            | Some r -> r
            // REFUSING TO QUEUE IS THE HONEST ANSWER. The caller is told the write did not land, which is
            // true. Appending without the lock to "not lose it" is what loses it.
            | None ->
                Error(
                    Transport
                        $"the board write could not be queued: the deferral queue stayed locked by another worker for %d{lockTimeoutMs ()}ms"
                )

    let private parseDeferred (line: string) =
        try
            use doc = JsonDocument.Parse line
            let r = doc.RootElement

            let get (name: string) =
                match r.TryGetProperty name with
                | true, v when v.ValueKind = JsonValueKind.String -> Some(v.GetString())
                | _ -> None

            // BOTH KEYS OR NEITHER (#882). Absent is a legacy entry, queued before the board was recorded —
            // real, and `flush` knows what to do with it. HALF a board is neither: it is a line we cannot
            // read, and this module's rule is that such a line refuses the drain rather than draining as if
            // the missing half had said something.
            let board =
                match get "boardOwner", get "boardTitle" with
                | Some owner, Some title -> Ok(Some(owner, title))
                | None, None -> Ok None
                | _ -> Error()

            match get "ref", get "field", get "value", get "at", get "worker", board with
            | Some rf, Some f, Some v, Some a, Some w, Ok b ->
                Some
                    { Ref = rf
                      Field = f
                      Value = v
                      At = a
                      Worker = w
                      Board = b }
            | _ -> None
        with :? JsonException ->
            None

    // THE UNLOCKED INNER READ. `dropPending` must read and rewrite as ONE critical section, so it holds the
    // lock across both and calls this; a `pending ()` that took the lock itself would deadlock against the
    // lock its own caller already holds. Nothing outside this module may call it: an unlocked read can see a
    // rewrite in progress, because `WriteAllText` is not atomic.
    let private pendingUnlocked () : IoResult<Deferred list> =
        let file = pendingFile ()

        if not (File.Exists file) then
            // AN ABSENT QUEUE IS AN EMPTY QUEUE, and that is a real, successful answer — this is the one
            // place an empty result is legitimate, because we can see that the file is not there. It is
            // distinct from a queue we could not READ, below.
            Ok []
        else
            try
                let lines =
                    File.ReadAllLines file
                    |> Array.filter (fun l -> not (String.IsNullOrWhiteSpace l))

                let parsed = lines |> Array.map parseDeferred

                // A LINE WE CANNOT PARSE IS NOT A LINE THAT ISN'T THERE. Skipping it would silently drop a
                // queued board write — the exact promise-not-kept that #510 was filed for — so a corrupt
                // queue refuses rather than draining itself quietly.
                if parsed |> Array.exists Option.isNone then
                    Error(
                        Malformed(
                            "pending.jsonl",
                            "the deferred-write queue has a line that is not a queue entry. Refusing to drain it: a line we cannot read is a board write we would silently drop."
                        )
                    )
                else
                    Ok(parsed |> Array.choose id |> List.ofArray)

            with :? IOException as e ->
                Error(Transport $"the deferred-write queue could not be read: %s{e.Message}")

    /// Everything currently queued, read under the lock so it cannot observe a partial rewrite.
    let pending () : IoResult<Deferred list> =
        match withPendingLock pendingUnlocked with
        | Some r -> r
        | None ->
            // A QUEUE WE COULD NOT READ IS NOT AN EMPTY QUEUE (#266). `Ok []` here would tell `flush` there
            // was nothing to replay and let it report success over a queue it never opened.
            Error(
                Transport
                    $"the deferred-write queue could not be read: it stayed locked by another worker for %d{lockTimeoutMs ()}ms"
            )

    let private clearPendingUnlocked () =
        try
            // UNLINK, never truncate. A zero-byte file is a claim — "there is a queue, and it is empty" —
            // and that is a statement about state that nobody made. When the last entry drains, the queue
            // ceases to exist.
            File.Delete(pendingFile ())
        with :? IOException ->
            ()

    let clearPending () = withPendingLock clearPendingUnlocked |> ignore

    let dropPending (entry: Deferred) =
        // READ AND REWRITE UNDER ONE LOCK. This is the whole fix: the window that destroyed a concurrent
        // `defer` was between the read and the write, so nothing that closes only one end of it is enough —
        // an atomic replace fixes the torn write and loses the append just the same (#881).
        withPendingLock (fun () ->
            match pendingUnlocked () with
            | Error _ -> ()
            | Ok entries ->
                // THE BOARD IS PART OF THE IDENTITY (#882). Without it, "set #882 Status = Done" queued
                // against board A and the same write queued against board B are the SAME entry to this
                // filter — so flushing B would drop A's write, unreplayed and unreported. That is the very
                // silent loss this field was added to end, re-entering through the drop path.
                let remaining =
                    entries
                    |> List.filter (fun e ->
                        not (
                            e.Ref = entry.Ref
                            && e.Field = entry.Field
                            && e.Value = entry.Value
                            && e.Board = entry.Board
                        ))

                if List.isEmpty remaining then
                    clearPendingUnlocked ()
                else
                    try
                        let text =
                            remaining |> List.map renderDeferred |> String.concat "\n"

                        File.WriteAllText(pendingFile (), text + "\n")
                    with :? IOException ->
                        ())
        // LOSING THE LOCK LEAVES THE ENTRY QUEUED, which is safe in the direction that matters: the entry has
        // already been written to the board, so the next `flush` replays a write that is idempotent. The
        // opposite default — dropping it unlocked — is the data loss.
        |> ignore
