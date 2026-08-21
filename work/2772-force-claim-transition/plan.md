---
schemaVersion: 1
workId: 2772-force-claim-transition
title: Atomic and recoverable forced-claim transition
stage: plan
changeTier: tier1
status: planned
sourceSpec: work/2772-force-claim-transition/spec.md
sourceClarifications: work/2772-force-claim-transition/clarifications.md
sourceChecklist: work/2772-force-claim-transition/checklist.md
publicOrToolFacingImpact: true
---

# Atomic and recoverable forced-claim transition Plan

Prose status: planned

## Source Snapshot
- spec: work/2772-force-claim-transition/spec.md sha256:722173e24b3e5a0da6b0166e61632e7a3a229eaa35a8b2510cada2f82890c117 schemaVersion:1
- clarifications: work/2772-force-claim-transition/clarifications.md sha256:1bcaf14ddfc8dfeb83101a6a92ed03f5ea6cfa0b88ebb8237b97faa2c974dfd8 schemaVersion:1
- checklist: work/2772-force-claim-transition/checklist.md sha256:a6ec061ec308af2e930deb552ea44f701212285780394b250281333e7c57585d schemaVersion:1

## Plan Scope
- Refactor only the forced-live-holder arm of `Writes.claimScoped`; keep ordinary claim and renewal
  paths byte-for-byte where practical.
- Extend the typed write outcome and CLI rendering only enough to preserve the posted replacement and
  report incomplete cleanup truthfully.
- Add fault injection to `WriteTests.fs`; do not enter #2753's broader CLI test directory.

## Plan Decisions
- PD-001 [AC-001] [FR-001] complete: Split fresh-marker posting from post-census resolution. In the force
  arm, run admission, post the replacement, then clean foreign live markers, then resolve the existing
  comment-order election from a complete re-read.
- PD-002 [AC-002] [FR-002] complete: When cleanup stops on a non-404 delete failure, retain the posted
  replacement and return `CleanupRequired` with its `Held` capability, removed worker ids, and the failed
  incumbent marker. Never withdraw the replacement in that arm.
- PD-003 [AC-003] [FR-003] complete: Capture complete typed pre/post censuses for every forced transition.
  Map replacement POST failure to `ReplacementPostFailed` only after the post-census proves the incumbent
  still wins; map cleanup interruptions to their distinct typed postconditions. Render each actionable
  state distinctly and add the governing censuses to successful `Stolen` machine receipts.
- PD-004 [AC-004] [FR-004] complete: Move theft callback invocation after each successful foreign-marker
  deletion. This makes every named victim an observed removal and keeps a partial cleanup accurately
  accounted.
- PD-005 [AC-005] [FR-005] complete: Preserve the twin/unparseable checks before mutation, the admission
  check before posting, stale collection after election, and ordinary `postAndResolve` behavior.
- PD-006 [AC-001] [AC-002] [FR-006] complete: Add scripted transport legs for replacement POST failure
  and incumbent DELETE failure; assert operation order and full marker ids. Invert the order in a bounded
  mutation and observe the POST-failure leg red because the incumbent vanishes.

## Contract Impact
- PC-001 [PD-001] [PD-002] additive union: `ClaimOutcome` gains census-backed forced-transition cases;
  successful `stolen` receipt JSON adds `forcedClaimCensuses`, while ordinary claim receipts remain
  byte-compatible.
- PC-002 [PD-003] command diagnostic: `claim --force` distinguishes failed replacement creation from
  incomplete cleanup; the latter explicitly reports that a replacement marker exists and retry is a
  reconciliation action.

## Verification Obligations
- VO-001 [PD-001] [PC-001] semanticTest: `WriteTests` injects failure on replacement POST and proves the
  incumbent marker remains and no delete occurred.
- VO-002 [PD-002] [PC-001] semanticTest: `WriteTests` injects failure on incumbent DELETE and proves both
  incumbent and replacement markers remain, with `CleanupRequired` carrying their identities.
- VO-003 [PD-003] [PC-002] semanticTest: the compiled engine e2e route proves replacement POST failure and
  cleanup-boundary `OldHolderStands` have distinct actionable text, the standing `TAKEN` notice remains,
  and a successful steal receipt carries its pre/post censuses.
- VO-004 [PD-005] regression: run `FS.GG.Coord.GitHub.Tests`, the relevant CLI test project, formatting,
  signature surface, and repository gate subset.
- VO-005 [PD-006] mutation: temporarily restore delete-before-post ordering, run the bounded focused test,
  record the observed failure, restore source, and re-run green.

## Performance Intent
No performance intent is declared for this work item.

## Migration Posture
- PM-001 [PC-001] additive: internal F# callers must exhaustively handle the forced-transition cases;
  no persisted marker grammar changes, and the successful stolen JSON receipt gains one additive census
  object.

## Generated View Impact
- GV-001 [PD-001] workModel: readiness artifacts record implementation and gate evidence; they do not
  participate in the live claim transition.

## Accepted Deferrals
No accepted plan deferrals recorded.

## Planning Findings
No blocking planning findings recorded.

## Advisory Notes
- The filed Client-only declaration was widened through the coordination engine because extraction moved
  the root-cause transition into `FS.GG.Coord.GitHub.Writes`.

## Lifecycle Notes
- Round-1 repair incorporates independent review decision digest
  `1251c73621e6d00d05cb75d38041a9b123d1ae43504ab9f1a787da69b7eaa8ee`.
- Next lifecycle action: `fsgg-sdd tasks --work 2772-force-claim-transition`.
