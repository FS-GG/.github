module FS.GG.Coord.GitHub.Tests.CacheTests

open System
open System.IO
open Xunit
open FS.GG.Coord.GitHub
open FS.GG.Coord.GitHub.Errors
open FS.GG.Coord.GitHub.Cache

/// Each test owns its own cache directory. The real cache is SHARED between every worker on the box — that
/// is the point of it, and it is also why a test that inherited another's cache would be testing the side
/// effects of whatever ran before it. The shell corpus learned this the hard way: its monolith shared one
/// cache across 847 assertions in file order, and a real defect (#344's empty-RC fail-open) was
/// UNREACHABLE because the state it needed was always already spent by the time its assertions ran.
type private Sandbox() =
    let dir =
        Path.Combine(Path.GetTempPath(), "fsgg-cache-test-" + Guid.NewGuid().ToString("N"))

    do
        Directory.CreateDirectory dir |> ignore
        Environment.SetEnvironmentVariable("FSGG_COORD_CACHE", dir)

    member _.Dir = dir

    interface IDisposable with
        member _.Dispose() =
            Environment.SetEnvironmentVariable("FSGG_COORD_CACHE", null)
            Environment.SetEnvironmentVariable("FSGG_COORD_SCAN_TTL_SEC", null)
            Environment.SetEnvironmentVariable("FSGG_COORD_BOARD_TTL_SEC", null)
            Environment.SetEnvironmentVariable("FSGG_COORD_LOCK_TIMEOUT_MS", null)

            try
                Directory.Delete(dir, true)
            with _ ->
                ()

let private aScan = """[{"repo":"FS.GG.SDD","number":42,"status":"Ready"}]"""

// ---- INVARIANT 1: a failed read is never rescued by the cache --------------------------------------

[<Fact>]
let ``#344 an EMPTY scan is never written to the cache`` () =
    use _sandbox = new Sandbox()

    // THE WHOLE INVARIANT, IN ONE ASSERTION. The caller that has just failed to read the board is holding
    // an empty list either way — this is the last moment at which "the board is empty" and "I could not
    // read the board" are still distinguishable, and so it is the only place the distinction can be
    // enforced.
    //
    // A failed scan that reached the cache would write *the board is empty* into it and serve that,
    // confidently, to the next ninety seconds of workers. One failed read, multiplied by the fleet, wearing
    // the clothes of a fact.
    Assert.False(putScan "FS-GG" "Coordination" "[]")
    Assert.True((getScan Scheduling "FS-GG" "Coordination").IsNone)

[<Fact>]
let ``#461 a scan that is not JSON is never written either`` () =
    use _sandbox = new Sandbox()

    // A truncated page, a proxy's HTML error body, a 5xx rendered as text. `gh` exits 0 on all of them.
    // Bytes we cannot parse are a FAILED READ, and a failed read may not become the fleet's cached truth.
    Assert.False(putScan "FS-GG" "Coordination" "<html>502 Bad Gateway</html>")
    Assert.False(putScan "FS-GG" "Coordination" "")
    Assert.True((getScan Scheduling "FS-GG" "Coordination").IsNone)

[<Fact>]
let ``a genuinely non-empty scan IS cached - the guard must not fire on a real answer`` () =
    use _sandbox = new Sandbox()

    // The counterweight, and it matters as much as the guard. A fail-closed rule that also refuses the good
    // path is not safe, it is broken — and it would send every worker back to a full-board scan, which is
    // the #418 budget exhaustion the cache exists to prevent.
    Assert.True(putScan "FS-GG" "Coordination" aScan)
    Assert.Equal(Some aScan, getScan Scheduling "FS-GG" "Coordination")

// ---- INVARIANT 2: a reconciler may never be served a cached board ----------------------------------

[<Fact>]
let ``a RECONCILING read never serves the cache, however fresh it is`` () =
    use _sandbox = new Sandbox()
    Assert.True(putScan "FS-GG" "Coordination" aScan)

    // `ready` / `lint` / `who` / `overlap --active` exist to say what is true RIGHT NOW. A cached "truth" is
    // how a reconciler comes to report drift that was already fixed — or, worse, to miss drift that is
    // still there.
    //
    // The scheduler may serve a stale board, because the worst a stale scan can do is offer an item
    // somebody just claimed, and the claim CAS — which reads markers over REST, never this cache — is what
    // actually decides who holds it. Staleness costs a retry. It cannot cost a double-claim.
    Assert.True((getScan Reconciling "FS-GG" "Coordination").IsNone)
    Assert.True((getScan Scheduling "FS-GG" "Coordination").IsSome)

// ---- the TTL ---------------------------------------------------------------------------------------

[<Fact>]
let ``a TTL of zero disables the cache entirely`` () =
    use _sandbox = new Sandbox()
    Assert.True(putScan "FS-GG" "Coordination" aScan)

    Environment.SetEnvironmentVariable("FSGG_COORD_SCAN_TTL_SEC", "0")
    Assert.True((getScan Scheduling "FS-GG" "Coordination").IsNone)

[<Fact>]
let ``an UNPARSEABLE TTL falls back to no cache - the safe direction is to pay for the read`` () =
    use _sandbox = new Sandbox()
    Assert.True(putScan "FS-GG" "Coordination" aScan)

    // Refusing to run over a malformed env var would be a hard failure on a soft misconfiguration. Serving
    // an unbounded stale cache would be the opposite mistake, and a much more expensive one.
    Environment.SetEnvironmentVariable("FSGG_COORD_SCAN_TTL_SEC", "ninety")
    Assert.True((getScan Scheduling "FS-GG" "Coordination").IsNone)

[<Fact>]
let ``a scan older than the TTL is a MISS`` () =
    use sandbox = new Sandbox()
    Assert.True(putScan "FS-GG" "Coordination" aScan)

    // Age is the file's mtime, so backdate it past the window rather than sleeping through it.
    let file = Directory.GetFiles(sandbox.Dir, "scan-*.json") |> Array.exactlyOne
    File.SetLastWriteTimeUtc(file, DateTime.UtcNow.AddSeconds(-120.0))

    Environment.SetEnvironmentVariable("FSGG_COORD_SCAN_TTL_SEC", "90")
    Assert.True((getScan Scheduling "FS-GG" "Coordination").IsNone)

[<Fact>]
let ``#1152 a patchScan fold does not restart the TTL clock - only a real read does`` () =
    use sandbox = new Sandbox()
    Assert.True(putScan "FS-GG" "Coordination" aScan)

    // The board was actually READ at T0. Backdate the cache file's mtime to stand in for "putScan ran
    // 95s ago" without sleeping through the window.
    let file = Directory.GetFiles(sandbox.Dir, "scan-*.json") |> Array.exactlyOne
    let t0 = DateTime.UtcNow.AddSeconds(-95.0)
    File.SetLastWriteTimeUtc(file, t0)

    // A board write the worker made along the way folds into the cache. It is NOT a read, so it must not
    // make the cache look fresh again — otherwise a worker's own writes keep its cache eternally young and
    // it never sees another worker's board changes (#1152).
    patchScan "FS-GG" "Coordination" "FS.GG.SDD" 42 "Status" "Done"

    // The fold really happened — guards against a vacuous pass where patchScan no-op'd...
    Assert.Contains("Done", File.ReadAllText file)

    // ...and yet the TTL is still measured from T0, so at 95s > 90s the cache is a MISS. On the pre-fix
    // code the patch's rewrite stamped `now`, and this read served the stale scan as fresh.
    Environment.SetEnvironmentVariable("FSGG_COORD_SCAN_TTL_SEC", "90")
    Assert.True((getScan Scheduling "FS-GG" "Coordination").IsNone)

[<Fact>]
let ``a ZERO-BYTE cache file is a miss, not an empty board`` () =
    use sandbox = new Sandbox()
    Assert.True(putScan "FS-GG" "Coordination" aScan)

    // A torn write, or a `putScan` killed between create and write. Serving it would be the
    // confident-empty-board again, arriving through the filesystem instead of through the network.
    let file = Directory.GetFiles(sandbox.Dir, "scan-*.json") |> Array.exactlyOne
    File.WriteAllText(file, "")

    Assert.True((getScan Scheduling "FS-GG" "Coordination").IsNone)

[<Fact>]
let ``the cache is keyed on the BOARD - one board's items are never served for another`` () =
    use _sandbox = new Sandbox()

    // `FSGG_COORD_OWNER` / `PROJECT` can point this client at a different board. Serving one board's items
    // for another is not a STALE answer — it is a WRONG one, and nothing downstream would notice.
    Assert.True(putScan "FS-GG" "Coordination" aScan)
    Assert.True((getScan Scheduling "FS-GG" "Some Other Board").IsNone)
    Assert.True((getScan Scheduling "Other-Org" "Coordination").IsNone)

// ---- the deferred board-write queue (#510) ---------------------------------------------------------

let private entry =
    { Ref = "FS.GG.SDD#810"
      Field = "Status"
      Value = "In progress"
      At = "2026-07-14T12:00:00Z"
      Worker = "vole-418"
      Board = Some("FS-GG", "Coordination") }

[<Fact>]
let ``#510 a board write may be deferred ONLY on an exhausted budget`` () =
    use _sandbox = new Sandbox()

    // THE PRECONDITION IS THE ARGUMENT, and that is the whole fix. A caller cannot queue a write without
    // holding the failure that licenses it, and only one failure does. In bash this test was written at the
    // `claim` call site and not at the `set-field` one — so `set-field` printed "the write is QUEUED" over
    // failures it could never replay, and `flush` then reported success, confirming the lie.
    match defer (RateLimited(UnknownBudget, None)) entry with
    | Ok() -> ()
    | Error e -> failwith $"an exhausted budget must be queueable — got %A{e}"

    match pending () with
    | Ok [ one ] -> Assert.Equal("FS.GG.SDD#810", one.Ref)
    | other -> failwith $"the queued write must actually be there — got %A{other}"

[<Fact>]
let ``#510 a PERMANENT failure is refused, and nothing is queued`` () =
    use _sandbox = new Sandbox()

    // A bad field, a bad option, a non-ref `Blocked by`. Replaying these forever would mean the queue never
    // drains and the refusal never reaches the worker who could fix it.
    match defer (Http(422, "No such field")) entry with
    | Error(Http(422, _)) -> ()
    | other -> failwith $"a permanent failure must be refused, not queued — got %A{other}"

    match pending () with
    | Ok [] -> ()
    | other -> failwith $"nothing may be queued on a permanent failure — got %A{other}"

[<Fact>]
let ``the queue is UNLINKED when it drains, not truncated`` () =
    use sandbox = new Sandbox()

    defer (RateLimited(UnknownBudget, None)) entry |> ignore
    dropPending entry

    // An empty file is a CLAIM — "there is a queue, and it is empty" — and that is a statement about state
    // nobody made. The corpus asserts the difference (`ABSENT` vs a count), because conflating them is how
    // "no writes are pending" and "I never looked" came to print the same sentence.
    Assert.False(File.Exists(Path.Combine(sandbox.Dir, "pending.jsonl")))

    match pending () with
    | Ok [] -> ()
    | other -> failwith $"an absent queue is an empty queue — got %A{other}"

[<Fact>]
let ``a CORRUPT queue refuses to drain rather than silently dropping a board write`` () =
    use sandbox = new Sandbox()

    defer (RateLimited(UnknownBudget, None)) entry |> ignore
    File.AppendAllText(Path.Combine(sandbox.Dir, "pending.jsonl"), "this is not a queue entry\n")

    // Skipping the unreadable line would silently drop a queued board write — the exact promise-not-kept
    // that #510 was filed for, arriving through the READER this time instead of the writer. A line we
    // cannot read is a write we would lose, so the queue refuses as a whole.
    match pending () with
    | Error(Malformed _) -> ()
    | other -> failwith $"a corrupt queue must refuse, not drain quietly — got %A{other}"

// ---- the ETag store --------------------------------------------------------------------------------

[<Fact>]
let ``a 304 with NO cached body is an ERROR, never an empty result`` () =
    use _sandbox = new Sandbox()

    // We sent a validator we could not honour: the server said "what you have is current" and we do not
    // have it. That is OUR protocol violation, and serving an empty body here would turn it into an empty
    // result set — which is the whole failure class this port exists to end.
    match getBody "repos/FS-GG/FS.GG.SDD/issues" with
    | Error(Malformed _) -> ()
    | other -> failwith $"a 304 we cannot answer must be an error — got %A{other}"

[<Fact>]
let ``a body and its ETag are stored together, and a body with no ETag drops the stale one`` () =
    use _sandbox = new Sandbox()
    let path = "repos/FS-GG/FS.GG.SDD/issues"

    putBody path (Some "\"etag-v1\"") """[{"number":42}]"""
    Assert.Equal(Some "\"etag-v1\"", getETag path)

    match getBody path with
    | Ok body -> Assert.Contains("42", body)
    | Error e -> failwith $"the cached body must be readable — got %A{e}"

    // A NEW body revalidated against an OLD validator is how a 304 comes to serve the wrong bytes. If the
    // server sent no ETag, we have no validator, and we must not keep pretending we do.
    putBody path None """[{"number":43}]"""
    Assert.True((getETag path).IsNone)

// ---- the board-map cache (#418) --------------------------------------------------------------------

let private aBoard =
    """{"number":12,"id":"PVT_coord","owner":"FS-GG","title":"Coordination","fields":{"Phase":{"id":"PVTSSF_phase","dataType":"SINGLE_SELECT","options":{"P2 SDD":"opt_p2"}}}}"""

[<Fact>]
let ``a usable board map round-trips through the day-cache`` () =
    use _sandbox = new Sandbox()

    // The counterweight to every fail-closed rule below: a real board map IS cached and served back
    // verbatim. Without this the #418 win never happens — every worker re-bootstraps.
    Assert.True(putBoardMap "FS-GG" "Coordination" aBoard)
    Assert.Equal(Some aBoard, getBoardMap "FS-GG" "Coordination")

[<Fact>]
let ``a board map with NO fields is never cached - it is a bootstrap that went wrong`` () =
    use _sandbox = new Sandbox()

    // An empty field map is #199's shape — a document we failed to walk. Caching it would make every write
    // fail with "no field named Status" for a day, so it is refused at the write, like an empty scan.
    Assert.False(putBoardMap "FS-GG" "Coordination" """{"number":12,"id":"PVT_coord","owner":"FS-GG","title":"Coordination","fields":{}}""")
    Assert.False(putBoardMap "FS-GG" "Coordination" "<html>502</html>")
    Assert.True((getBoardMap "FS-GG" "Coordination").IsNone)

[<Fact>]
let ``a zero board TTL disables the board cache - the safe direction is to pay for the read`` () =
    use _sandbox = new Sandbox()
    Environment.SetEnvironmentVariable("FSGG_COORD_BOARD_TTL_SEC", "0")

    // The store still writes (the file is there for a later run), but a zero TTL never serves it — exactly
    // as FSGG_COORD_SCAN_TTL_SEC=0 disables the scan cache.
    Assert.True(putBoardMap "FS-GG" "Coordination" aBoard)
    Assert.True((getBoardMap "FS-GG" "Coordination").IsNone)

[<Fact>]
let ``dropBoardMap forgets the cached map - the --refresh path`` () =
    use _sandbox = new Sandbox()
    Assert.True(putBoardMap "FS-GG" "Coordination" aBoard)
    dropBoardMap "FS-GG" "Coordination"
    Assert.True((getBoardMap "FS-GG" "Coordination").IsNone)

// ---- the item-id cache (forever, positives only) ---------------------------------------------------

[<Fact>]
let ``a resolved item id is cached forever, keyed on the board`` () =
    use _sandbox = new Sandbox()

    putItemId "FS-GG" "FS.GG.SDD" 42 12 "PVTI_coord123"
    Assert.Equal(Some "PVTI_coord123", getItemId "FS-GG" "FS.GG.SDD" 42 12)

    // Keyed on the BOARD too: the same issue on a different board is a different item, a distinct miss.
    Assert.True((getItemId "FS-GG" "FS.GG.SDD" 42 7).IsNone)
    // And a different issue is its own miss.
    Assert.True((getItemId "FS-GG" "FS.GG.SDD" 43 12).IsNone)

[<Fact>]
let ``an empty item id is never memoised - an absence must not become a hit (#421)`` () =
    use _sandbox = new Sandbox()
    putItemId "FS-GG" "FS.GG.SDD" 42 12 ""
    Assert.True((getItemId "FS-GG" "FS.GG.SDD" 42 12).IsNone)

// ---- the inbox cursor: a per-worker high-water mark -------------------------------------------------

[<Fact>]
let ``inboxCursor is 0 for a mailbox never read`` () =
    use _sandbox = new Sandbox()
    // A fresh worker has seen no mail — 0, so its first read delivers everything above 0.
    Assert.Equal(0L, inboxCursor "smew-f31")

[<Fact>]
let ``putInboxCursor then inboxCursor round-trips the high-water mark`` () =
    use _sandbox = new Sandbox()
    putInboxCursor "smew-f31" 4210L
    Assert.Equal(4210L, inboxCursor "smew-f31")

[<Fact>]
let ``the cursor is per-worker - one worker's read does not consume another's mail`` () =
    use _sandbox = new Sandbox()
    putInboxCursor "smew-f31" 900L
    // finch never read, so its cursor is untouched — the mailbox is a per-worker fact.
    Assert.Equal(0L, inboxCursor "finch-a3f")
    Assert.Equal(900L, inboxCursor "smew-f31")

[<Fact>]
let ``an unreadable cursor falls back to 0 - it shows too much, never too little`` () =
    use sandbox = new Sandbox()
    // Garbage in the cursor file (a truncated write, a half-flushed disk). The fallback direction is the
    // OPPOSITE of the lock's: a cursor read too HIGH would hide new mail, so a bad one degrades to 0 and
    // re-shows old mail instead — noise, never a silently swallowed message.
    File.WriteAllText(Path.Combine(sandbox.Dir, "inbox-smew-f31"), "not-a-number")
    Assert.Equal(0L, inboxCursor "smew-f31")

[<Fact>]
let ``the cursor file is keyed on the slugged worker id (matches the bash client)`` () =
    use sandbox = new Sandbox()
    putInboxCursor "smew-f31" 12L
    // `inbox-<slug>` is the shared contract with the bash client — a worker that switches engines mid-loop
    // must land on the SAME file, or it re-reads mail it already saw.
    Assert.True(File.Exists(Path.Combine(sandbox.Dir, "inbox-smew-f31")))

// ---- #881: the deferral queue is one file, shared by every worker on the box ------------------------

/// Take the queue lock the way another worker's PROCESS would, so the test contends for real rather than
/// asserting against a mock of contention.
let private holdQueueLock (sandbox: Sandbox) =
    new FileStream(
        Path.Combine(sandbox.Dir, "pending.lock"),
        FileMode.OpenOrCreate,
        FileAccess.ReadWrite,
        FileShare.None
    )

[<Fact>]
let ``#881 a concurrent defer is NOT destroyed by a flush's dropPending`` () =
    use _sandbox = new Sandbox()

    // A DEEP QUEUE, because the window IS the read-modify-write. `dropPending` reads every line, parses each
    // as JSON, filters, re-renders and writes the lot back; over a one-entry queue that is microseconds long
    // and no concurrent `defer` ever lands inside it. The first draft of this test seeded ONE entry, passed
    // against the unfixed code, and proved nothing — a green test that could not see its subject (#266).
    for i in 1..400 do
        defer (RateLimited(UnknownBudget, None)) { entry with Ref = $"FS.GG.SDD#%d{i}" } |> ignore

    let mutable lost = 0

    for i in 1..40 do
        // A distinct victim per round, so a survivor of an earlier round can never be read as this one's.
        let victim =
            { entry with
                Ref = $"FS.GG.Game#%d{i}"
                Value = "Ready" }

        // The dropper drops one real entry, which is exactly what `flush` does per replayed write.
        let dropper =
            Threading.Tasks.Task.Run(fun () -> dropPending { entry with Ref = $"FS.GG.SDD#%d{i}" })

        // Land INSIDE the dropper's read-modify-write rather than on either side of it.
        Threading.Thread.Sleep 1
        defer (RateLimited(UnknownBudget, None)) victim |> ignore
        dropper.Wait()

        match pending () with
        | Ok entries when entries |> List.exists (fun e -> e.Ref = victim.Ref) -> ()
        | _ -> lost <- lost + 1

    // The deferrer was told "QUEUED; flush replays it", and unless the read and the rewrite are ONE critical
    // section that promise is a lie: the dropper's `WriteAllText` lands on a snapshot that predates the
    // append, so the board write of the worker who did nothing wrong is silently gone.
    Assert.Equal(0, lost)

[<Fact>]
let ``#881 defer REFUSES while another worker holds the queue, rather than racing it`` () =
    use sandbox = new Sandbox()
    Environment.SetEnvironmentVariable("FSGG_COORD_LOCK_TIMEOUT_MS", "150")

    use _held = holdQueueLock sandbox

    // REFUSING IS THE HONEST ANSWER. The caller is told the write did not land, which is true. Appending
    // anyway "so as not to lose it" is precisely what loses it.
    match defer (RateLimited(UnknownBudget, None)) entry with
    | Error(Transport m) -> Assert.Contains("locked", m)
    | other -> failwith $"defer must refuse while the queue is held — got %A{other}"

// ---- #882: the board is part of the entry, and of its identity ---------------------------------------

[<Fact>]
let ``#882 the board survives the queue round-trip`` () =
    use _sandbox = new Sandbox()

    // A FIELD THAT DOES NOT ROUND-TRIP IS A FIELD THAT IS NOT THERE. The queue is a JSONL file, so the board
    // is only recorded if `renderDeferred` writes it AND `parseDeferred` reads it back.
    defer (RateLimited(UnknownBudget, None)) entry |> ignore

    match pending () with
    | Ok [ one ] -> Assert.Equal(Some("FS-GG", "Coordination"), one.Board)
    | other -> failwith $"the board must survive render -> parse — got %A{other}"

[<Fact>]
let ``#882 an entry with NO board parses as a legacy entry, rather than refusing the drain`` () =
    use sandbox = new Sandbox()

    // A QUEUE WRITTEN BY THE PREVIOUS BUILD. Making the board REQUIRED would fail `parseDeferred` on every
    // one of these lines — and an unparseable line refuses the whole drain, by design (#510). So the upgrade
    // that added this field would have bricked every non-empty queue in existence: the queued writes would
    // be unreplayable by any verb, which is #878's stranding with an extra step.
    File.WriteAllText(
        Path.Combine(sandbox.Dir, "pending.jsonl"),
        """{"ref":"FS.GG.SDD#810","field":"Status","value":"Ready","at":"2026-07-14T12:00:00Z","worker":"vole-418"}"""
        + "\n"
    )

    match pending () with
    | Ok [ one ] ->
        Assert.Equal(None, one.Board)
        Assert.Equal("FS.GG.SDD#810", one.Ref)
    | other -> failwith $"a pre-#882 entry must still parse — got %A{other}"

[<Fact>]
let ``#882 HALF a board is a line we cannot read, and refuses the drain`` () =
    use sandbox = new Sandbox()

    // ABSENT IS A LEGACY ENTRY; HALF IS CORRUPTION. Reading a half-recorded board as "no board" would replay
    // it against the current board on the strength of the half that is missing — a guess, dressed as the
    // legacy case. This module's rule is that a line it cannot read refuses the drain rather than draining
    // as if it had said something.
    File.WriteAllText(
        Path.Combine(sandbox.Dir, "pending.jsonl"),
        """{"ref":"FS.GG.SDD#810","field":"Status","value":"Ready","at":"2026-07-14T12:00:00Z","worker":"vole-418","boardOwner":"FS-GG"}"""
        + "\n"
    )

    match pending () with
    | Error(Malformed("pending.jsonl", _)) -> ()
    | other -> failwith $"half a board must refuse the drain — got %A{other}"

[<Fact>]
let ``#882 dropPending does not drop the SAME write queued against another board`` () =
    use _sandbox = new Sandbox()

    let mine = entry
    let theirs = { entry with Board = Some("FS-GG", "Some Other Board") }

    defer (RateLimited(UnknownBudget, None)) mine |> ignore
    defer (RateLimited(UnknownBudget, None)) theirs |> ignore

    // THE BOARD IS PART OF THE IDENTITY. Ref, field and value are identical here — the same item, the same
    // write, owed to two different boards — so a filter that ignores the board treats them as one entry and
    // drops both. That is #882's silent loss re-entering through the drop path, and it needs no repointing
    // to reach: one flush, two boards' queues, one of them gone.
    dropPending mine

    match pending () with
    | Ok [ one ] -> Assert.Equal(Some("FS-GG", "Some Other Board"), one.Board)
    | other -> failwith $"another board's identical write must survive the drop — got %A{other}"

[<Fact>]
let ``#881 a queue we could not read is not an EMPTY queue`` () =
    use sandbox = new Sandbox()
    defer (RateLimited(UnknownBudget, None)) entry |> ignore

    Environment.SetEnvironmentVariable("FSGG_COORD_LOCK_TIMEOUT_MS", "150")
    use _held = holdQueueLock sandbox

    // #266's signature, in the queue: `Ok []` here would tell `flush` there was nothing to replay and let it
    // report success over a queue it never managed to open. An absent queue is empty; a LOCKED one is unread.
    match pending () with
    | Ok [] -> failwith "a locked queue must never read as empty — that is a flush reporting success over unread work"
    | Error(Transport _) -> ()
    | other -> failwith $"a locked queue must refuse — got %A{other}"
