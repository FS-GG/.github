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
