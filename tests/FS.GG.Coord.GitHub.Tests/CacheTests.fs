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
      Worker = "vole-418" }

[<Fact>]
let ``#510 a board write may be deferred ONLY on an exhausted budget`` () =
    use _sandbox = new Sandbox()

    // THE PRECONDITION IS THE ARGUMENT, and that is the whole fix. A caller cannot queue a write without
    // holding the failure that licenses it, and only one failure does. In bash this test was written at the
    // `claim` call site and not at the `set-field` one — so `set-field` printed "the write is QUEUED" over
    // failures it could never replay, and `flush` then reported success, confirming the lie.
    match defer (RateLimited None) entry with
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

    defer (RateLimited None) entry |> ignore
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

    defer (RateLimited None) entry |> ignore
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
