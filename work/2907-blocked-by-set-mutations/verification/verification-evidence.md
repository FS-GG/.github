# Verification evidence

## Positive controls

- `dotnet test tests/FS.GG.Coord.Cli.BoardOps.Tests/FS.GG.Coord.Cli.BoardOps.Tests.fsproj -c Release --no-restore`: 259 passed, 0 failed, 0 skipped.
- `dotnet test tests/FS.GG.Coord.GitHub.Tests/FS.GG.Coord.GitHub.Tests.fsproj --no-restore`: 671 passed, 0 failed, 0 skipped.
- `dotnet test tests/FS.GG.Coord.Cli.Kernel.Tests/FS.GG.Coord.Cli.Kernel.Tests.fsproj --no-restore`: 182 passed, 0 failed, 0 skipped.
- The Lifecycle, CLI, and Core test project commands each exited zero.
- The command-produced JUnit receipt at `readiness/2907-blocked-by-set-mutations/test-results/boardops.junit.xml`
  records one successful gate invocation and embeds the complete BoardOps transcript: 259 passed, 0 failed,
  0 skipped. `tests/observed-command-report/run.sh` proves the producer propagates a failing command,
  emits `failures=1`, and refuses a missing command.
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
- Hosted coord-engine run `32820976032` provided the red control for the new lease traffic: the Blocked
  intake route emitted issue-create, board-add, lease POST, atomic GraphQL projection, and lease DELETE,
  while lifecycle reconcile emitted lease POST, one GraphQL batch, lease DELETE, and its durable receipt.
  The prior exact-count assertions rejected both successful routes. The repaired checks now require those
  exact ordered method/path/kind shapes, retain the semantic status/field/fresh-observation assertions,
  and the complete write corpus passes 186/186 with the vetted `fsgg-sdd` 1.0.0 validator selected.
- Post-merge policy run `32826466267` exposed that the original observed receipt used a tracked historical
  `.trx`, which `scripts/m6-cutover-acceptance.py` forbids. Before repair,
  `tests/m6-cutover-acceptance/run.sh` exited 1 and named only
  `readiness/2907-blocked-by-set-mutations/test-results/boardops.trx`. The recovery removes that file,
  binds every evidence declaration to the exact bytes of the allowed command-produced JUnit receipt,
  and retains the 259-test result in its embedded transcript and evidence notes.
- Recovery PR #2944 then measured a separate append-only election collision on exact head
  `f002dc9f073576c79132cb6a06ead0d05dfb5713`. After independent review and host acceptance record
  `5407786870`, live delivery created authorization grant `5407800325`, but claim-fence run
  `32828366457` rejected it: the older election `5406718363`, bound to already-merged PR #2936, was the
  lowest marker for operation key `d163d676e274b8a021f2610d937859b5b2fe83c2f1f6754bef664b6bda9a8d05`.
  Both elections inherited claim generation `5406278215`, so retrying delivery or editing markers could
  not make PR #2944 authoritative. The PR was closed unmerged, the claim was released explicitly to
  `In review`, and an ordinary typed claim minted generation `5407856386`. Because host acceptance binds
  the claim generation as well as the head and effective base, the old acceptance is retained only as
  historical evidence; the successor PR requires a genuine evidence-bearing head move, fresh initial
  review, fresh host acceptance, and a new delivery election.

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
8. The intake lease-acquire method and reconcile lease-acquire method were each inverted from POST to
   DELETE in their production wire assertions. The full write corpus rejected exactly the two affected
   routes; restoring POST returned all 186 assertions green.
9. The observed-command wrapper was given `bash -c 'exit 7'`. It returned 7 and emitted a JUnit suite
   with `tests=1` and `failures=1`; its missing-command arm returned 2 without creating an output file.
   The pre-repair M6 policy subject independently returned 1 for the tracked historical TRX, while the
   repaired tree returns green after replacing it with the command-produced JUnit receipt.

These inversions discriminate union, subtraction, stale-write refusal, body-projection linting,
server-ordered mutation fencing, direct-writer lease bypass, deferred-flush lease bypass, and exact
production lease-wire ordering independently.

## Runtime controls

- Parser controls cover all four explicit intents, mutual exclusion, and rejection outside `set-field`.
- Legacy positional `Blocked by` replacement is refused before transport with the four explicit remedies; the parity fixture uses `--replace` and `--clear` and proves malformed explicit values spend zero GraphQL.
- Handler transport controls independently vary the first observation and guarded re-observation, assert the derived field mutation, and assert zero mutation on stale data.
- Batch, release, intake, reconcile, and explicit set-field controls provide a persistent issue-comment
  thread and therefore fail if any authoritative `Blocked by` route does not join the common lease.
- Lint controls distinguish absent/equal projection from empty, divergent, duplicate, and invalid body text; fenced examples remain ignored.
- The body lint is diagnostic-only and never feeds a board mutation route.
