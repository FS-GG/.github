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
- spec: work/2772-force-claim-transition/spec.md sha256:7540aa88e08e86980b56e5c50f3839c344b0a852ab9e7c9b42864d0d156be672 schemaVersion:1
- clarifications: work/2772-force-claim-transition/clarifications.md sha256:6d11b156370c143e89f12221eeacbf5c44cadde8517c95293a0ca025d175ce45 schemaVersion:1
- checklist: work/2772-force-claim-transition/checklist.md sha256:c06f974ea2dfe00cfe6dc9186f608b6d6ed0e4e3d4540a259a27e4bf5d30b17f schemaVersion:1

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
- PD-003 [AC-003] [FR-003] [DEC-004] complete: Capture complete typed pre/post censuses for every forced transition.
  Treat replacement POST transport failure as ambiguous: retain the exact generated body and adopt only a
  byte-identical newly observed replacement, including lease and renewal token. Reconcile that exact marker
  through cleanup/election, or map to `ReplacementPostFailed` without deleting the incumbent. Capture
  terminal `After` only after stale cleanup and any replacement withdrawal complete. Render each actionable
  state distinctly and serialize its governing censuses for green and non-green `--json` outcomes.
- PD-004 [AC-004] [FR-004] complete: Move theft callback invocation after each successful foreign-marker
  deletion. This makes every named victim an observed removal and keeps a partial cleanup accurately
  accounted.
- PD-005 [AC-005] [FR-005] complete: Preserve the twin/unparseable checks before mutation, the admission
  check before posting, stale collection after election, and ordinary `postAndResolve` behavior.
- PD-006 [AC-001] [AC-002] [FR-006] complete: Add scripted transport legs for replacement POST failure
  and incumbent DELETE failure, plus response-lost POST legs where old+replacement coexist and where the
  replacement already wins. Assert operation order and final marker ids. Invert ambiguous-POST recognition
  and final-census ordering independently and observe the focused tests red. Add production legs for all
  six non-green terminal receipt classes and a same-fields/different-body response-lost negative control;
  invert receipt emission and exact-body equality independently and observe red.

## Contract Impact
- PC-001 [PD-001] [PD-002] additive union: `ClaimOutcome` gains census-backed forced-transition cases;
  successful `stolen` receipt JSON adds `forcedClaimCensuses`, while ordinary claim receipts remain
  byte-compatible.
- PC-002 [PD-003] command diagnostic and JSON receipt: `claim --force` distinguishes failed replacement
  creation from incomplete cleanup; every terminal non-green `--json` result emits a typed
  `ForcedClaimOutcomeReceipt` carrying its governing censuses while stderr retains actionable prose.

## Verification Obligations
- VO-001 [PD-001] [PC-001] semanticTest: `WriteTests` injects failure on replacement POST and proves the
  incumbent marker remains and no delete occurred.
- VO-002 [PD-002] [PC-001] semanticTest: `WriteTests` injects failure on incumbent DELETE and proves both
  incumbent and replacement markers remain, with `CleanupRequired` carrying their identities.
- VO-003 [PD-003] [PC-002] semanticTest: the compiled engine e2e route proves all six non-green terminal
  outcomes emit distinct typed final-census receipts plus actionable stderr, successful forced outcomes
  retain their censuses, and the standing `TAKEN` notice remains.
- VO-004 [PD-005] regression: run `FS.GG.Coord.GitHub.Tests`, the relevant CLI test project, formatting,
  signature surface, and repository gate subset.
- VO-005 [PD-006] mutation: temporarily restore delete-before-post ordering, then independently disable
  response-lost replacement recognition and substitute the pre-cleanup census; run each bounded focused
  test. Also replace exact-body equality with parsed-field equality and suppress terminal receipt stdout;
  record the observed failures, restore source, and re-run green.

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
- Round-2 repair incorporates independent review decision digest
  `387201c40307bdeefb15e8e00196a732df8d39a14045bc7d76096bb4dee9d3ed`.
- Round-3 repair incorporates independent review decision digest
  `487e53f3b6ec50213b0f6695f83ed3fe015006a2096a767e2cac7c6075a14352`.
- Next lifecycle action: `fsgg-sdd tasks --work 2772-force-claim-transition`.
