# Verification evidence

## Positive controls

- `dotnet test tests/FS.GG.Coord.Cli.BoardOps.Tests/FS.GG.Coord.Cli.BoardOps.Tests.fsproj --no-restore`: 254 passed, 0 failed, 0 skipped.
- `dotnet test tests/FS.GG.Coord.GitHub.Tests/FS.GG.Coord.GitHub.Tests.fsproj --no-restore`: 671 passed, 0 failed, 0 skipped.
- `dotnet test tests/FS.GG.Coord.Cli.Kernel.Tests/FS.GG.Coord.Cli.Kernel.Tests.fsproj --no-restore`: 182 passed, 0 failed, 0 skipped.
- The Lifecycle, CLI, and Core test project commands each exited zero.
- The durable TRX at `readiness/2907-blocked-by-set-mutations/test-results/boardops.trx` records all 254 BoardOps tests passing.
- `tests/coord-engine-parity/run.sh` passed the explicit replace/clear, ref-first, zero-GraphQL refusal, canonicalization, de-duplication, and scoped-field controls after its legacy positional calls were migrated.
- After the initial critic identified a missing hosted body for open/In-progress item #423, the repaired
  serialized parity run passed 616/616 assertions with zero failures and zero not-measured results. Its
  negative control removes only #423's body, proves production requested that body, and proves lint
  aborts rather than returning a partial JSON findings array.
- `scripts/generate-projections --check` reported every projection current; the signature-doc mutation sweep killed 435/435 mutants.
- The Release engine build and the 12-entry deterministic package check exited zero.

## Gate inversions

Each bounded mutation was applied alone, the focused test was observed red, and the production implementation was restored before the positive controls:

1. Add-set derivation was changed from union with the observed set to requested-only. The add-preservation control failed: expected `#290, #299`, actual `#299`.
2. Remove-set derivation was forced to clear. The remove-preservation control failed: expected `#299`, actual `<cleared>`.
3. The guarded-write revision/value match was forced false. The stale-observation control failed because the command returned zero and the transport recorded a mutation.
4. The inert-body verdict was suppressed with `Ok None`. The lint theory failed because the divergent body case expected a finding and observed none.

These inversions discriminate union, subtraction, stale-write refusal, and body-projection linting independently.

## Runtime controls

- Parser controls cover all four explicit intents, mutual exclusion, and rejection outside `set-field`.
- Legacy positional `Blocked by` replacement is refused before transport with the four explicit remedies; the parity fixture uses `--replace` and `--clear` and proves malformed explicit values spend zero GraphQL.
- Handler transport controls independently vary the first observation and guarded re-observation, assert the derived field mutation, and assert zero mutation on stale data.
- Lint controls distinguish absent/equal projection from empty, divergent, duplicate, and invalid body text; fenced examples remain ignored.
- The body lint is diagnostic-only and never feeds a board mutation route.
