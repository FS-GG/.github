module FS.GG.Coord.Cli.Kernel.Tests.AssemblyInfo

// THESE TESTS MAY NOT RUN IN PARALLEL, AND THE REASON ARRIVED WITH THE TESTS (.github#2725).
//
// This is the sibling `FS.GG.Coord.Cli.Tests/AssemblyInfo.fs` argument, restated for the subject this
// assembly actually has. `IdentityTests` resolves worker identity, and identity resolution reads the
// PROCESS ENVIRONMENT — `FSGG_WORKER`, the harness session variables, and the shared-session provenance
// derived from them. Three of its legs therefore SET an environment variable, exercise the resolver, and
// restore it in a `finally`.
//
// An environment variable is process-global. xUnit parallelises across CLASSES by default, so a second
// class running while one of those legs holds a temporary value observes it — and the symptom is not a
// clean failure but a resolver answering with another test's identity, which is precisely the
// one-id-two-workers confusion (#419) this suite exists to pin.
//
// TODAY `IdentityTests` IS THE ONLY CLASS HERE THAT TOUCHES THE ENVIRONMENT, AND THE GUARD IS STILL
// ASSEMBLY-SCOPED RATHER THAN CLASS-SCOPED. That is the sibling file's argument verbatim and it is the
// right one: a lock inside one class is a class-scoped answer to a process-scoped problem, and the next
// class that reads the environment would race silently and pass in CI until it didn't. The guard belongs
// where the scope of the hazard is.
//
// WHAT IS DELIBERATELY ABSENT. The sibling assembly also installs a `TestFramework` that redirects
// `Cache.root()`, because `Client.reconcile`/`Client.batch` legs there once wrote a fixture board into
// the developer's real scan cache (.github#2525). Nothing in this assembly can reach the cache: it
// references `FS.GG.Coord.Cli.Kernel` alone, no module in that project reads or writes the scan cache,
// and no test here constructs a transport or runs a command. Copying the sandbox across would be
// duplicating a fixture for a hazard this assembly does not have; if a later extraction moves
// cache-touching code under this project, the sandbox comes with it.
[<assembly: Xunit.CollectionBehavior(DisableTestParallelization = true)>]
do ()
