module FS.GG.Coord.Cli.Tests.CacheIsolationTests

open System
open Xunit
open FS.GG.Coord.GitHub

/// .github#2525 repair 1 — THE ACCEPTANCE CHECK FOR "A TEST RUN LEAVES THE USER'S SCAN CACHE UNTOUCHED".
///
/// The incident this closes was not a partial read. `dotnet test` ran `Client.reconcile`/`Client.batch`
/// over a four-row fixture with no cache isolation, `Scan.scanFresh` → `Cache.putScan` wrote those rows to
/// `~/.cache/fsgg-coord/scan-fs-gg-coordination.json`, and every live board read on that machine then
/// served a fabricated board for the cache's TTL. No completeness guard can catch that: the poisoned board
/// is complete, well-formed and internally consistent. It is simply not the board.
///
/// So the guard is that the write has nowhere real to land, installed by `AssemblyInfo.CacheSandbox` at
/// assembly load. These tests are what make deleting it LOUD rather than silent.
module Guard =

    /// The user's real cache root — the location the leak actually reached — computed WITHOUT consulting
    /// the redirected environment, so it stays a fixed point to compare against.
    let private realCacheRoot =
        IO.Path.Combine(Environment.GetFolderPath Environment.SpecialFolder.UserProfile, ".cache", "fsgg-coord")

    [<Fact>]
    let ``the assembly redirected the cache fallback before any test ran`` () =
        // Deleting the `[<ModuleInitializer>]` reds exactly here, which is the point: the sandbox is
        // otherwise invisible, and an invisible guard is one a later edit removes without noticing.
        match CacheSandbox.Root with
        | None -> failwith "the cache sandbox never installed — Cache.root() would fall back to the user's own ~/.cache"
        | Some root ->
            Assert.Equal(root, Environment.GetEnvironmentVariable "XDG_CACHE_HOME")
            Assert.True(IO.Directory.Exists root, "the sandbox root must exist before any test writes to it")

    [<Fact>]
    let ``Cache.root() resolves inside the sandbox, never under the user's home`` () =
        // The property that actually matters, asserted against the REAL resolver rather than a restatement
        // of it — `Cache.root()` is what `putScan` calls. `FSGG_COORD_CACHE` must be clear for this to be
        // measuring the FALLBACK, which is the path an un-isolated test takes.
        let previous = Environment.GetEnvironmentVariable "FSGG_COORD_CACHE"

        try
            Environment.SetEnvironmentVariable("FSGG_COORD_CACHE", null)
            let resolved = Cache.root ()
            let sandbox = CacheSandbox.Root |> Option.defaultValue "<uninstalled>"

            Assert.StartsWith(sandbox, resolved)
            Assert.DoesNotContain(realCacheRoot, resolved)
        finally
            Environment.SetEnvironmentVariable("FSGG_COORD_CACHE", previous)

    [<Fact>]
    let ``no scan file from this run reached the user's real cache directory`` () =
        // AC6-as-narrowed, stated literally. This is deliberately an assertion about the USER'S directory
        // rather than about the sandbox: it is the sentence the acceptance criterion is written in, and it
        // stays true and meaningful on a machine where that directory does not exist at all (CI), where
        // "unchanged" means "still absent".
        //
        // It cannot false-red from a concurrent worker's live `fsgg-coord` run on the same machine, because
        // it does not compare a before/after snapshot — it asks only whether any file there was written by
        // THIS process, via the sandbox marker every write from this assembly would have to carry.
        if IO.Directory.Exists realCacheRoot then
            let sandboxed =
                IO.Directory.GetFiles(realCacheRoot, "scan-*.json")
                |> Array.filter (fun f ->
                    try
                        IO.File.ReadAllText f |> fun t -> t.Contains CacheSandbox.Marker
                    with _ ->
                        false)

            Assert.Empty sandboxed
