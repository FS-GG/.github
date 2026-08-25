# Verification evidence

## Positive controls

- `dotnet test tests/FS.GG.Coord.Cli.BoardOps.Tests/FS.GG.Coord.Cli.BoardOps.Tests.fsproj -c Release --no-restore`: 259 passed, 0 failed, 0 skipped.
- `dotnet test tests/FS.GG.Coord.GitHub.Tests/FS.GG.Coord.GitHub.Tests.fsproj --no-restore`: 671 passed, 0 failed, 0 skipped.
- `dotnet test tests/FS.GG.Coord.Cli.Kernel.Tests/FS.GG.Coord.Cli.Kernel.Tests.fsproj --no-restore`: 182 passed, 0 failed, 0 skipped.
- The Lifecycle, CLI, and Core test project commands each exited zero.
- The durable TRX at `readiness/2907-blocked-by-set-mutations/test-results/boardops.trx` records all 259 BoardOps tests passing.
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
- The superseding PR's hosted parity-fixture gate then caught `_send(204, {})` writing a forbidden body
  on its kept-alive HTTP/1.1 connection. Guarding 204 before serialization made
  `scripts/check-parity-fixtures.py` green across all 46 fixtures and the full parity corpus green at
  616/616 with zero not-measured results. The hosted red is the negative control for the framing fix.
- The round-1 successor critic found that the lease covered derived and explicit single-field intents but
  could still be bypassed by other authoritative writers. The repair routes `set-field --batch`, intake,
  reconcile lifecycle repairs, and `release --blocked-by` through the same server-ordered issue-comment
  lease. A discriminating batch interleaving fixture installs a lower-ID contender and proves the losing
  command emits zero board mutations; the release fixture proves its route posts the same lease marker.
  The expanded Release BoardOps suite passed 258/258.
- The round-2 successor critic executed the deferred queue route and proved `Board.flush` could replay an
  unconditional `Blocked by` replacement beneath an active lower-ID contender. The repair reacquires the
  same issue-comment lease during replay, retains the current entry and stops without duplication when the
  action did not provably land, and removes a fulfilled entry if only ticket cleanup is uncertain. The
  focused lower-ID control records zero field mutations and one still-pending entry; the expanded Release
  BoardOps suite passed 259/259.
- The same critic identified a premature delivery authorization while successor review was pending. The
  PR marker was removed through a body edit at `2026-08-25T07:08:21Z`; the append-only election comment
  `5406718363` remains as audit evidence but no current PR marker references its grant. Running
  `scripts/check-claim-generation.py` against exact head `6cd87934b3dfc239da337e5b6f469b377323fc86`
  and the re-read marker-free body returned 1 with `[missing]`, proving the old election cannot authorize
  the head. No authorization is recreated before the post-acceptance delivery boundary.

## Gate inversions

Each bounded mutation was applied alone, the focused test was observed red, and the production implementation was restored before the positive controls:

1. Add-set derivation was changed from union with the observed set to requested-only. The add-preservation control failed: expected `#290, #299`, actual `#299`.
2. Remove-set derivation was forced to clear. The remove-preservation control failed: expected `#299`, actual `<cleared>`.
3. The guarded-write revision/value match was forced false. The stale-observation control failed because the command returned zero and the transport recorded a mutation.
4. The inert-body verdict was suppressed with `Ok None`. The lint theory failed because the divergent body case expected a finding and observed none.
5. The production mutation-lease election was reversed from lowest to highest comment id. The lower-id
   contender control failed because the command returned zero instead of fencing our higher-id writer.
6. The batch writer's shared-lease predicate was disabled. The new interleaving control failed because
   the batch command returned zero and mutated the field despite the active lower-ID contender; restoring
   the shared predicate returned the focused control green.
7. The deferred replay's shared-lease predicate was disabled. The flush-vs-derived interleaving control
   failed because flush reported one write, emitted the replacement beneath the active lower-ID contender,
   and removed the pending entry; restoring the lease returned zero mutations and preserved exactly one
   queued entry.

These inversions discriminate union, subtraction, stale-write refusal, body-projection linting,
server-ordered mutation fencing, direct-writer lease bypass, and deferred-flush lease bypass independently.

## Runtime controls

- Parser controls cover all four explicit intents, mutual exclusion, and rejection outside `set-field`.
- Legacy positional `Blocked by` replacement is refused before transport with the four explicit remedies; the parity fixture uses `--replace` and `--clear` and proves malformed explicit values spend zero GraphQL.
- Handler transport controls independently vary the first observation and guarded re-observation, assert the derived field mutation, and assert zero mutation on stale data.
- Batch, release, intake, reconcile, and explicit set-field controls provide a persistent issue-comment
  thread and therefore fail if any authoritative `Blocked by` route does not join the common lease.
- Lint controls distinguish absent/equal projection from empty, divergent, duplicate, and invalid body text; fenced examples remain ignored.
- The body lint is diagnostic-only and never feeds a board mutation route.
