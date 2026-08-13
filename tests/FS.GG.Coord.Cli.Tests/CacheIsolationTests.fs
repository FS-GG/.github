module FS.GG.Coord.Cli.Tests.CacheIsolationTests

open System
open Xunit
open FS.GG.Coord.GitHub

/// .github#2525 — THE ACCEPTANCE CHECK FOR "A TEST RUN LEAVES THE USER'S SCAN CACHE BYTE-UNCHANGED".
///
/// The incident this closes was not a partial read. `dotnet test` ran `Client.reconcile`/`Client.batch`
/// over four-row fixtures with no cache isolation, `Scan.scanFresh` → `Cache.putScan` wrote those rows to
/// `~/.cache/fsgg-coord/scan-fs-gg-coordination.json`, and every live board read on that machine then
/// served a fabricated board for the cache's TTL. No completeness guard can catch that: the poisoned board
/// is complete, well-formed and internally consistent. It is simply not the board.
///
/// So the guard is that the write has nowhere real to land, installed by `CacheSandbox` at assembly load.
/// These tests are what make deleting it LOUD rather than silent.
module Guard =

    [<Fact>]
    let ``the assembly redirected the cache fallback before any test ran`` () =
        match CacheSandbox.Root with
        | None -> failwith "the cache sandbox never installed — Cache.root() would fall back to the user's own ~/.cache"
        | Some root ->
            Assert.Equal(root, Environment.GetEnvironmentVariable "XDG_CACHE_HOME")
            Assert.True(IO.Directory.Exists root, "the sandbox root must exist before any test writes to it")

    [<Fact>]
    let ``Cache.root() resolves inside the sandbox, never under the user's home`` () =
        // Asserted against the REAL resolver rather than a restatement of it — `Cache.root()` is what
        // `putScan` calls. `FSGG_COORD_CACHE` must be clear for this to be measuring the FALLBACK, which is
        // the path an un-isolated test takes.
        let previous = Environment.GetEnvironmentVariable "FSGG_COORD_CACHE"

        try
            Environment.SetEnvironmentVariable("FSGG_COORD_CACHE", null)
            let resolved = Cache.root ()
            let sandbox = CacheSandbox.Root |> Option.defaultValue "<uninstalled>"

            Assert.StartsWith(sandbox, resolved)
            Assert.DoesNotContain(CacheSandbox.RealCacheRoot, resolved)
        finally
            Environment.SetEnvironmentVariable("FSGG_COORD_CACHE", previous)

    /// REPAIR 4 (.github#2525) — THE TEST THAT REPLACES ONE THAT COULD NOT FAIL.
    ///
    /// Its predecessor filtered leaked files for `CacheSandbox.Marker`. That string lives in the sandbox
    /// PATH; `Cache.putScan` writes the rendered rows and nothing else (`Cache.fs:194`), so no leaked file
    /// could ever contain it, the filter was empty by construction, and the assertion passed unconditionally.
    /// It was green through the whole of round 1 with a 204-byte poisoned fixture board sitting in the very
    /// directory it named. The tell was in my own inversion matrix and went unexamined: the M9 mutation
    /// leaked one file and turned two tests red, not three.
    ///
    /// It also early-returned on `if IO.Directory.Exists realCacheRoot`, with no `else` — so on CI, where
    /// that directory usually does not exist, it asserted nothing at all while its comment claimed "still
    /// absent" was being checked. Both defects are removed here: there is no early-out, and the predicate
    /// is a before/after comparison of CONTENT DIGESTS captured before any test ran.
    ///
    /// The positive half is what makes the negative half mean something. A test that only asserts "nothing
    /// appeared over there" passes just as happily when the write never happened at all, which is how the
    /// first version escaped. So this performs a REAL `Cache.putScan` first and proves it landed inside the
    /// sandbox — the detector is shown working on a write it made itself, in the same run, before it is
    /// trusted to report the absence of anyone else's.
    ///
    /// ONE LIMIT, STATED RATHER THAN DISCOVERED. The negative half can only see leaks that happened BEFORE
    /// this test ran, and xUnit does not guarantee an order even with parallelisation disabled. That bound
    /// is acceptable only because this is the BACKSTOP and not the guard: the thing that actually prevents
    /// leaks is the redirect, and if the redirect is broken then every test in the assembly leaks and the
    /// two tests above red deterministically, with no dependence on ordering at all. Measured across the
    /// mutation matrix, no configuration that leaks leaves all three of these green. Writing this down is
    /// the point — the defect being repaired here was a claim in a comment that nobody had checked.
    [<Fact>]
    let ``a real putScan lands in the sandbox, and the user's real cache gains no scan file`` () =
        let previousCache = Environment.GetEnvironmentVariable "FSGG_COORD_CACHE"

        // `putScan` refuses anything that is not a non-empty JSON array (`Cache.fs:168-178`), so the probe
        // has to be a plausible row set rather than a sentinel string.
        let payload =
            """[{"owner":"FS-GG","repo":".github","number":424242,"title":"cache-isolation round-trip probe","status":"Ready","blockedBy":"","severity":"Unset","state":"OPEN","isPullRequest":false,"pathRepo":".github"}]"""

        try
            // Clear FSGG_COORD_CACHE so this exercises the FALLBACK — the exact resolution path an
            // unisolated test takes, and therefore the one the sandbox has to be covering.
            Environment.SetEnvironmentVariable("FSGG_COORD_CACHE", null)

            let sandboxRoot =
                match CacheSandbox.Root with
                | Some root -> root
                | None -> failwith "the cache sandbox never installed — nothing below can be trusted"

            let scanFilesUnder (dir: string) =
                if IO.Directory.Exists dir then
                    IO.Directory.GetFiles(dir, "scan-*.json") |> Set.ofArray
                else
                    Set.empty

            let cacheDirUnderSandbox = IO.Path.Combine(sandboxRoot, "fsgg-coord")
            let before = scanFilesUnder cacheDirUnderSandbox

            Assert.True(
                Cache.putScan "FS-GG" "cache-isolation-probe" payload,
                "putScan refused the probe payload — the round-trip proves nothing if the write never happened"
            )

            // POSITIVE: the write landed, and it landed HERE.
            let appeared = Set.difference (scanFilesUnder cacheDirUnderSandbox) before
            let probe = Assert.Single appeared
            Assert.Equal(payload, IO.File.ReadAllText probe)

            // NEGATIVE: and the user's real cache is byte-unchanged since before any test ran. No
            // `Directory.Exists` early-out — an absent directory is the empty map on both sides, so
            // "still absent" is genuinely asserted rather than merely claimed.
            let now =
                if IO.Directory.Exists CacheSandbox.RealCacheRoot then
                    IO.Directory.GetFiles(CacheSandbox.RealCacheRoot, "scan-*.json")
                    |> Array.map (fun f ->
                        use sha = Security.Cryptography.SHA256.Create()
                        IO.Path.GetFileName f, (IO.File.ReadAllBytes f |> sha.ComputeHash |> Convert.ToHexString))
                    |> Map.ofArray
                else
                    Map.empty

            let added =
                now |> Map.filter (fun name _ -> not (CacheSandbox.ScanFilesAtStartup.ContainsKey name))

            let changed =
                now
                |> Map.filter (fun name digest ->
                    match Map.tryFind name CacheSandbox.ScanFilesAtStartup with
                    | Some before -> before <> digest
                    | None -> false)

            let describe (label: string) (m: Map<string, string>) =
                m |> Map.toList |> List.map fst |> String.concat ", " |> sprintf "%s: [%s]" label

            Assert.True(
                Map.isEmpty added && Map.isEmpty changed,
                sprintf
                    "this test run wrote to the user's own scan cache at %s — %s %s"
                    CacheSandbox.RealCacheRoot
                    (describe "added" added)
                    (describe "changed" changed)
            )
        finally
            Environment.SetEnvironmentVariable("FSGG_COORD_CACHE", previousCache)
