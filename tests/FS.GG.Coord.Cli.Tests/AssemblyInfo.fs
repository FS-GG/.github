module FS.GG.Coord.Cli.Tests.AssemblyInfo

// THESE TESTS MAY NOT RUN IN PARALLEL, AND THE REASON IS THE PRODUCTION DESIGN, NOT THE TEST DESIGN.
//
// This is `FS.GG.Coord.GitHub.Tests/AssemblyInfo.fs`'s argument, arriving here the moment it became true
// of this assembly too (#1063). `Followups.path` resolves through `Cache.root()`, which reads
// `FSGG_COORD_CACHE` from the ENVIRONMENT — deliberately, because that is how the bash client behaves and
// how the shell corpus isolates itself. The env var name is part of the contract, and reusing it is what
// lets a fixture that already redirects the cache get the follow-up queue for free.
//
// An environment variable is process-global. xUnit parallelises across CLASSES by default, so two test
// classes that each stand up their own throwaway cache directory overwrite each other's — and the symptom
// is not a clean failure. It is a test reading another test's queue, which for THIS suite means reading
// another worker's follow-ups: the exact cross-contamination the queue's per-worker path exists to
// prevent, rebuilt inside the harness that checks for it.
//
// A LOCK IN `FollowupsTests` IS NOT THIS. It serialises one class against itself, which is a class-scoped
// answer to a process-scoped problem — and `Followups.fsi` spends a paragraph on why a component must not
// take "nobody else is looking" as its only defence. The guard belongs where the scope of the hazard is.
// Nothing else in this assembly touches the cache TODAY; the next class that does would race silently,
// and would pass in CI until it didn't.
//
// The suite is ~240 tests and runs in well under a second. Serial is not a cost worth optimising, and a
// suite that races itself is worth nothing at all.
[<assembly: Xunit.CollectionBehavior(DisableTestParallelization = true)>]
do ()

/// …and `ChoresTests` became exactly that class (.github#2525). Three of its legs ran the REAL
/// `Client.reconcile`/`Client.batch` — so a real `Scan.scanFresh` → `Cache.putScan` — outside the
/// `withCache` helper sitting ten lines away from them.
///
/// The consequence was not a racing test. `Cache.root()` falls back to `$XDG_CACHE_HOME/fsgg-coord`, and
/// then to `~/.cache/fsgg-coord`, so `dotnet test` wrote this suite's four-row FIXTURE BOARD into the
/// developer's own scan cache as `scan-fs-gg-coordination.json`. Every live `batch`/`take`/`next`/`driver`
/// read on that machine then served those rows as the board for the cache's whole TTL — a board that is
/// complete, well-formed and internally consistent, and entirely fabricated.
///
/// That is why the remedy below is a REDIRECTION rather than an assertion, and why it is installed by the
/// test FRAMEWORK rather than by a fixture. A leaked fixture board defeats every completeness check by
/// construction — there is nothing partial about it to detect — so the only thing that helps is for the
/// write to have nowhere real to land, before any test can run. `withCache` stays and is still the right
/// per-leg tool; this only guarantees that the root a leg WITHOUT it falls back to is never the user's.
[<assembly: Xunit.TestFramework("FS.GG.Coord.Cli.Tests.CacheSandboxFramework", "FS.GG.Coord.Cli.Tests")>]
do ()
