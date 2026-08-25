# Verification evidence

## Positive controls

- `dotnet test tests/FS.GG.Coord.Cli.BoardOps.Tests/FS.GG.Coord.Cli.BoardOps.Tests.fsproj --no-restore`: 257 passed, 0 failed, 0 skipped.
- `dotnet test tests/FS.GG.Coord.GitHub.Tests/FS.GG.Coord.GitHub.Tests.fsproj --no-restore`: 671 passed, 0 failed, 0 skipped.
- `dotnet test tests/FS.GG.Coord.Cli.Kernel.Tests/FS.GG.Coord.Cli.Kernel.Tests.fsproj --no-restore`: 182 passed, 0 failed, 0 skipped.
- The Lifecycle, CLI, and Core test project commands each exited zero.
- The durable TRX at `readiness/2907-blocked-by-set-mutations/test-results/boardops.trx` records all 257 BoardOps tests passing.
- `tests/coord-engine-parity/run.sh` passed the explicit replace/clear, ref-first, zero-GraphQL refusal, canonicalization, de-duplication, and scoped-field controls after its legacy positional calls were migrated.
- After the initial critic identified a missing hosted body for open/In-progress item #423, the repaired
  serialized parity run passed 616/616 assertions with zero failures and zero not-measured results. Its
  negative control removes only #423's body, proves production requested that body, and proves lint
  aborts rather than returning a partial JSON findings array.
- After the successor pass, hosted `harness-identity / ladder-decided` exposed an ambient-session leak in
  `BlockedBySetMutationFixture.run`. The round-2 repair clears all four variables currently read by
  `Identity.resolve`, uses an explicit `--worker`, and exercises each poisoned source independently.
  `python3 scripts/check-harness-identity.py --root .` passed its 111-shell/104-F# census, the focused
  BlockerLint selection passed 36/36, and `tests/harness-identity/run.sh` passed 14/14 mutation controls.
- The round-3 repair serializes the complete Blocked-by observation/derive/guarded-write transaction
  behind GitHub's issue-comment ticket order. A two-task control forces both contenders through their
  empty pre-censuses, posts both tickets, and holds the winner until both election reads complete. One
  first attempt executes, the loser executes no action, and retry leaves both requested edges present;
  maximum concurrent actions remains one. The focused `#2907` selection passed 11/11.
- A clean serialized parity run passed 616/616 with zero not-measured, and the production write harness
  passed 180/186. Its six failures are the pre-existing #2133 validator-version mismatch: installed
  `fsgg-sdd` reports 1.1.0 while those controls accept only 1.0.0; all other write assertions passed.
- `scripts/generate-projections --check` reported every projection current; the signature-doc mutation sweep killed 435/435 mutants.
- The Release engine build and the 12-entry deterministic package check exited zero.
- Recovery validation after rebasing onto `origin/main` at `712f0257c15b8027432bbf7d4c1ea3df9b643105`
  rebuilt the Release CLI with zero warnings/errors, passed BoardOps 257/257 and GitHub 671/671,
  and confirmed `scripts/generate-projections --check` current. Regenerating the lifecycle views with
  `fsgg-sdd` 1.2.5 required a second `analyze` pass after the first refreshed `work-model.json`; the
  second pass returned `implementationReady`, `verify` returned `verificationReady` with all 14
  obligations backed by observed evidence, and `ship` returned `shipReady`.

## Gate inversions

Each bounded mutation was applied alone, the focused test was observed red, and the production implementation was restored before the positive controls:

1. Add-set derivation was changed from union with the observed set to requested-only. The add-preservation control failed: expected `#290, #299`, actual `#299`.
2. Remove-set derivation was forced to clear. The remove-preservation control failed: expected `#299`, actual `<cleared>`.
3. The guarded-write revision/value match was forced false. The stale-observation control failed because the command returned zero and the transport recorded a mutation.
4. The inert-body verdict was suppressed with `Ok None`. The lint theory failed because the divergent body case expected a finding and observed none.
5. The production mutation-lease election was reversed from lowest to highest comment id. The lower-id
   contender control failed because the command returned zero instead of fencing our higher-id writer.

These inversions discriminate union, subtraction, stale-write refusal, body-projection linting, and
server-ordered mutation fencing independently.

## Runtime controls

- Parser controls cover all four explicit intents, mutual exclusion, and rejection outside `set-field`.
- Legacy positional `Blocked by` replacement is refused before transport with the four explicit remedies; the parity fixture uses `--replace` and `--clear` and proves malformed explicit values spend zero GraphQL.
- Handler transport controls independently vary the first observation and guarded re-observation, assert the derived field mutation, and assert zero mutation on stale data.
- Lint controls distinguish absent/equal projection from empty, divergent, duplicate, and invalid body text; fenced examples remain ignored.
- The body lint is diagnostic-only and never feeds a board mutation route.
