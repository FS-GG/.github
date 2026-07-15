module FS.GG.Coord.GitHub.Tests.AssemblyInfo

// THESE TESTS MAY NOT RUN IN PARALLEL, AND THE REASON IS THE PRODUCTION DESIGN, NOT THE TEST DESIGN.
//
// `Cache.root()` reads `FSGG_COORD_CACHE` from the ENVIRONMENT — deliberately, because that is exactly how
// the bash client behaves and how the shell corpus isolates itself. The env var name is part of the
// contract: rename it and the whole cache half of the fixture silently stops testing anything.
//
// An environment variable is process-global. So two test classes that each stand up their own throwaway
// cache directory are, under xUnit's default per-class parallelism, overwriting each other's — and the
// symptom is not a clean failure. It is a test reading another test's cache, which is precisely the
// cross-contamination the shell corpus's own runner was rewritten to eliminate (its monolith shared one
// cache across 847 assertions in file order, and a real defect was UNREACHABLE because the state it needed
// was always already spent by the time its assertions ran).
//
// The suite is ~110 tests and runs in well under a second. Serial is not a cost worth optimising, and a
// suite that races itself is worth nothing at all.
[<assembly: Xunit.CollectionBehavior(DisableTestParallelization = true)>]
do ()
