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
