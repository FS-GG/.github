# Author verification

Candidate source commit: `a9088ef6` (the final evidence-only successor retains identical production
and test blobs).

## Baseline

- `dotnet build src/FS.GG.Coord.Cli/FS.GG.Coord.Cli.fsproj -c Release`: passed with 0 warnings and
  0 errors.
- `dotnet test tests/FS.GG.Coord.Cli.Tests/FS.GG.Coord.Cli.Tests.fsproj -c Release`: 496 passed,
  0 failed, 0 skipped.
- `PATH=/tmp/fsgg-sdd-1.0.0:$PATH FSGG_COORD_OWNER_TYPE=organization bash
  tests/coord-engine-e2e/writes.sh`: 175 passed, 0 failed. The valid H0/H1/H2/H3 route wrote exactly
  once; the readable noncontiguous historical chain and malformed legacy backlink each returned
  nonzero with unchanged comment count.
- `scripts/check-coherent-set-version.py`: all three package projects evaluate to 0.69.0.
- `scripts/check-release-coherence.py`: current immutable feed frontier is coherent 0.68.0 and the
  0.69.0 source cut introduces no completion gap.
- `scripts/check-engine-release-notes.py`, `scripts/check-ship-verdict-provenance.py`,
  `scripts/generate-driver-manifest --check`, `scripts/generate-projections --check`, and
  `git diff --check`: passed.
- Release packs produced `FS.GG.Coord.Cli.0.69.0.nupkg`, `FS.GG.Kit.0.69.0.nupkg`, and
  `FS.GG.Drivers.0.69.0.nupkg`. An isolated local tool install reported `0.69.0.0` and loaded the
  command contract.

## Gate inversions

Both inversions ran from detached copies of candidate `a9088ef6`; neither mutation touched the
candidate branch.

1. Noncontiguous-history fence inversion: disabled all three production structured-ledger validation
   calls involved in `review record` plus the exact initial/confirmation 1/2/3 kind-round predicate.
   The writer suite went red at the targeted `.github#2797` assertion: 174 passed, 1 failed, exit 1.
2. Legacy-backlink fence inversion: changed only the `not legacyMatches` refusal guard so a malformed
   confirmation backlink could pass that production boundary. The same targeted assertion went red:
   174 passed, 1 failed, exit 1.

These are author-time mutation results, not hosted publication evidence. Dual-feed identity, clean
public installation, registry reconciliation, and the S.I.R. handoff remain post-merge obligations.
