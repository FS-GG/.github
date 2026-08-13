namespace FS.GG.Coord.Cli.Tests

open System
open Xunit.Abstractions
open Xunit.Sdk

/// The sandbox `AssemblyInfo.fs` explains: this assembly's `Cache.root()` FALLBACK is moved off the
/// developer's home before any test runs (.github#2525).
module CacheSandbox =

    /// Stamped into the sandbox path so a file written from this assembly is identifiable wherever it
    /// lands — which is what lets `CacheIsolationTests` assert about the user's real directory without
    /// comparing a before/after snapshot that a concurrent live worker could perturb.
    [<Literal>]
    let Marker = "fsgg-cli-tests-cache-sandbox"

    /// The sandbox root, once installed. `None` means the framework hook never ran, which is a defect and
    /// is asserted as one rather than silently tolerated.
    let mutable Root: string option = None

    /// The user's REAL cache root, resolved from the home directory exactly as `Cache.root()`'s final
    /// fallback does. Held here rather than recomputed per test so the guard and the thing it guards can
    /// never disagree about which directory is under discussion.
    let RealCacheRoot =
        IO.Path.Combine(
            Environment.GetFolderPath Environment.SpecialFolder.UserProfile,
            ".cache",
            "fsgg-coord"
        )

    let private digestOf (file: string) =
        use sha = Security.Cryptography.SHA256.Create()
        IO.File.ReadAllBytes file |> sha.ComputeHash |> Convert.ToHexString

    /// Every `scan-*.json` in `RealCacheRoot`, by name and content digest.
    ///
    /// CONTENT, NOT A MARKER (.github#2525 repair 4). The first version of this guard filtered leaked files
    /// for a marker string, and the marker only ever existed in the sandbox PATH — `Cache.putScan` writes
    /// the rendered rows and nothing else (`Cache.fs:194`), so no leaked file could ever contain it and the
    /// filter was empty by construction. The guard was green for the whole of round 1 with a poisoned file
    /// sitting in the directory it claimed to inspect. A before/after digest comparison has no such escape:
    /// it asks whether the bytes changed, which is the question AC6 is actually written in.
    let ScanFilesAtStartup: Map<string, string> =
        // A module-level binding, so it is captured when this module initialises — which the framework
        // constructor below triggers, BEFORE xUnit discovers or runs anything. Putting it inside `install`
        // would tie the snapshot's existence to the redirect's, and the mutation that neuters the redirect
        // would then also blind the detector, which is precisely the failure being repaired.
        try
            if IO.Directory.Exists RealCacheRoot then
                IO.Directory.GetFiles(RealCacheRoot, "scan-*.json")
                |> Array.map (fun f -> IO.Path.GetFileName f, digestOf f)
                |> Map.ofArray
            else
                Map.empty
        with _ ->
            Map.empty

    let install () =
        match Root with
        | Some _ -> () // the framework is constructed once, but installing twice must not orphan a root
        | None ->
            let dir =
                IO.Path.Combine(IO.Path.GetTempPath(), Marker + "-" + Guid.NewGuid().ToString "n")

            IO.Directory.CreateDirectory dir |> ignore

            // XDG_CACHE_HOME, NOT FSGG_COORD_CACHE, and the choice is load-bearing. `withCache` and every
            // other fixture in this suite isolate by setting `FSGG_COORD_CACHE`; writing that variable here
            // would either be overwritten by them or overwrite them. Redirecting the FALLBACK leaves every
            // existing isolation exactly as it was and catches only the legs that have none.
            Environment.SetEnvironmentVariable("XDG_CACHE_HOME", dir)
            Root <- Some dir

/// xUnit constructs the test framework before discovery and execution, which is the earliest hook this
/// runner offers and the only one that needs no per-class opt-in. An `IClassFixture`/`ICollectionFixture`
/// would have to be remembered by every future class — the same "remember to call `withCache`" failure
/// mode, one level up.
type CacheSandboxFramework(messageSink: IMessageSink) =
    inherit XunitTestFramework(messageSink)
    do CacheSandbox.install ()
