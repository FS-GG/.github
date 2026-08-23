# Gate mutation evidence

All mutations below were applied to the authoring tree based on `0c533d0760d25999b6477ddcfafcc6a28c2be731`, run once, and restored before the final green run.

## M1 — bypass the dependency-column verdict

- Mutation: replace `readyDependencyVerdict observation.Value` with `readyDependencyVerdict None` in `Handlers.fs`.
- Command: `dotnet test tests/FS.GG.Coord.Cli.BoardOps.Tests/FS.GG.Coord.Cli.BoardOps.Tests.fsproj --no-restore --filter 'FullyQualifiedName~intake apply refuses Ready from a live column edge'`
- Observed: exit 1; both theory cases failed because the required `Ready is refused while the live Blocked by column carries a dependency` diagnostic was absent.
- Subject: the production `intake apply` decision boundary, with and without legacy body prose.

## M2 — disable the revision-staleness refusal

- Mutation: replace `if readyDependencyStale initialDependencyObservation current then` with `if false && readyDependencyStale initialDependencyObservation current then` in `Handlers.fs`.
- Command: `dotnet test tests/FS.GG.Coord.Cli.BoardOps.Tests/FS.GG.Coord.Cli.BoardOps.Tests.fsproj --no-restore --filter 'FullyQualifiedName~Projects revision'`
- Observed: exit 1; the fixture recorded the second Projects observation and then reached `updateProjectV2ItemFieldValue`, failing with `stale dependency decision reached the board mutation` instead of the stale-observation refusal.
- Subject: the production re-read immediately before `Board.boardWriteBatch`, not the pure helper alone.

## M3 — diverge the observation-query window

- Mutation: change `ItemBlockedByObservationDoc` from `projectItems(first: 20)` to `projectItems(first: 21)` in `Board.fs`.
- Command: `dotnet test tests/FS.GG.Coord.GitHub.Tests/FS.GG.Coord.GitHub.Tests.fsproj --no-restore --filter 'FullyQualifiedName~2535 the connection windows'`
- Observed: exit 1; the pagination contract reported expected `[20, 20, 20, 20]`, actual `[20, 20, 20, 21]`.
- Subject: agreement between every issue-side Projects connection document and the shared completeness guard.

## M4 — discard the board-write boundary condition

- Mutation: replace the exact `expectedBlockedBy` observation passed from `Handlers.fs` to `Board.boardWriteBatch` with `None`.
- Command: `dotnet test tests/FS.GG.Coord.Cli.BoardOps.Tests/FS.GG.Coord.Cli.BoardOps.Tests.fsproj -c Release --no-restore --filter 'FullyQualifiedName~2738'`
- Observed: exit 1; 9 tests passed and the new interleaving case failed after the fake installed a `Blocked by` edge between the handler's second observation and the board batch, because the mutated route emitted the forbidden batch mutation.
- Subject: the production condition carried across the handler/board boundary; the fixture proves zero `Status=Ready` mutation when the exact Projects revision or dependency edge changes.

## Restoration control

The final verification reruns the focused tests and all four affected suites from restored source. `git diff --check` and the PR diff provide the committed-source control: none of the four mutant spellings is retained.
