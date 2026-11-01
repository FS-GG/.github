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

    /// The sandbox `XDG_CONFIG_HOME`, once installed (.github#5007). `None` means the framework hook never ran.
    let mutable ConfigRoot: string option = None

    /// The operator's REAL telemetry configuration, resolved from the home directory as the engine's own
    /// final fallback does, so the guard and the thing it guards cannot disagree about which file is meant.
    let RealTelemetryConfig =
        IO.Path.Combine(Environment.GetFolderPath Environment.SpecialFolder.UserProfile, ".config", "fs-gg", "telemetry.json")

    /// The user's REAL cache root, resolved from the home directory exactly as `Cache.root()`'s final
    /// fallback does. Held here rather than recomputed per test so the guard and the thing it guards can
    /// never disagree about which directory is under discussion.
    let RealCacheRoot =
        IO.Path.Combine(Environment.GetFolderPath Environment.SpecialFolder.UserProfile, ".cache", "fsgg-coord")

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

            // The same defect, for the OPERATOR'S TELEMETRY CONFIG (.github#5007). Telemetry routes that take
            // `--store-root` ask `WorkspaceTelemetryApplication.configuredPath` whether a workspace config is
            // selected, and with no `--config`, no `FSGG_TELEMETRY_CONFIG` and no `XDG_CONFIG_HOME` that is
            // `~/.config/fs-gg/telemetry.json`. On any machine that has an operator workspace configured
            // the file exists, the route answers "workspace config and legacy store root cannot both be
            // selected", and the legacy-store tests that never mention a config fail (UTEL-06D, UTEL-06.8).
            // CI has no such file, so the failure is invisible there: it depends on the machine, not the date.
            // Redirect the fallback and clear the two selectors, so no test can see the real operator
            // configuration. Fixtures that need a config still set these themselves and restore THESE values.
            let config = IO.Path.Combine(dir, "config")
            IO.Directory.CreateDirectory config |> ignore
            Environment.SetEnvironmentVariable("XDG_CONFIG_HOME", config)
            Environment.SetEnvironmentVariable("FSGG_TELEMETRY_CONFIG", null)
            Environment.SetEnvironmentVariable("FSGG_TELEMETRY_STORE", null)
            ConfigRoot <- Some config
            Root <- Some dir

/// xUnit constructs the test framework before discovery and execution, which is the earliest hook this
/// runner offers and the only one that needs no per-class opt-in. An `IClassFixture`/`ICollectionFixture`
/// would have to be remembered by every future class — the same "remember to call `withCache`" failure
/// mode, one level up.
type CacheSandboxFramework(messageSink: IMessageSink) =
    inherit XunitTestFramework(messageSink)
    do CacheSandbox.install ()
