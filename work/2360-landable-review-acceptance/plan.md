---
schemaVersion: 1
workId: 2360-landable-review-acceptance
title: Landable Review Acceptance
stage: plan
changeTier: tier1
status: planned
sourceSpec: work/2360-landable-review-acceptance/spec.md
sourceClarifications: work/2360-landable-review-acceptance/clarifications.md
sourceChecklist: work/2360-landable-review-acceptance/checklist.md
publicOrToolFacingImpact: true
---

# Landable Review Acceptance Plan

Prose status: planned

## Source Snapshot
- spec: work/2360-landable-review-acceptance/spec.md sha256:4f8eaccfaef25efd1bdaf4c029dfe81e47042a07507b7280f4c100327f1375f9 schemaVersion:1
- clarifications: work/2360-landable-review-acceptance/clarifications.md sha256:6eaf2393b3b05f08c5244f0c48dac7965143f47f1fac13b4eda16b2d49c1f368 schemaVersion:1
- checklist: work/2360-landable-review-acceptance/checklist.md sha256:8fd6014a94f7c43b32a3a3b063a9de4a83cc0aaf164a01afb914b1cdf95e65d4 schemaVersion:1

## Plan Scope
- Work item 2360-landable-review-acceptance is planned from the current specification, clarification, and checklist facts.
- Requirement count: 4.
- Clarification decision count: 2.
- Checklist result count: 4.

## Plan Decisions
- PD-001 [AC-001] [AC-002] [AC-006] [FR-001] [FR-002] complete: Derive
  `requireReviewAccepted` as true by default and retain the existing explicit marker token as an
  idempotent request. Keep the review read after the CI verdict settles green, so pending/red/not-open
  paths retain their request cost and verdict semantics.
- PD-002 [AC-004] [AC-005] [AC-006] [FR-003] complete: Add one private exemption token equal to the
  existing `registry-coherence` assertion. The exemption applies only when that token is present and
  `fsgg:review-decision/v2` is absent; explicitly requesting review therefore always wins. Strip only
  the review marker from the check-run requirements, leaving `registry-coherence` to be evaluated by
  `Reads.prLandableRequire` exactly as today.
- PD-003 [AC-006] [FR-003] complete: Preserve stdout's one-word verdict and every exit code. Emit a
  stderr provenance line when an otherwise-green candidate evaluates review acceptance, and a
  different stderr line when the narrow registry exemption is used. Existing unmet diagnostics remain
  the reason for a pending verdict.
- PD-004 [AC-003] [AC-005] [FR-004] complete: Reuse the structured review parser for absent,
  malformed, unreadable, exact-head, and stale-head classification.
- PD-005 [AC-001] [AC-002] [AC-004] [AC-005] complete: Rewrite the old compatibility fixture that
  asserted plain landable was green into the default-refusal regression, add a default exact-head pass,
  and add registry-exemption and explicit-review-wins fixtures. Retain the existing explicit-token and
  stale-head cases as compatibility controls.
- PD-006 [AC-007] [FR-005] complete: Add optional, appended digest inputs for claim generation and
  base SHA so historical non-acceptance records remain byte-compatible; require both on acceptance and
  have live `review record` derive them from the complete marker and the current tip resolved through
  the PR's `base.ref` (never its cached `base.sha`).
- PD-007 [AC-008] [AC-010] [FR-006] [FR-008] complete: Parse the acceptance subject back to its item,
  re-read the winning claim and live base-branch ref in `landable`, and add a fixture whose PR head and
  cached `base.sha` stay unchanged while `refs/heads/main` moves from accepted base B to base C.
- PD-008 [AC-009] [FR-007] complete: Extend guarded landing with final live head/base observations,
  refuse before the merge callback on divergence, and return a receipt containing both revisions.

## Contract Impact
- PC-001 [PD-001] [PD-002] [PD-003] CLI verdict contract: stdout and exit codes remain compatible,
  while plain otherwise-green `landable` becomes fail-closed on review acceptance and stderr discloses
  whether that assertion was evaluated or deliberately exempted.

## Verification Obligations
- VO-001 [PD-001] [PD-004] semanticTest: Run the focused `LandableNotOpenTests` class and demonstrate
  plain absent review -> pending, plain exact-head review -> green, and stale-head review -> pending.
- VO-002 [PD-002] [PD-003] semanticTest: Demonstrate `registry-coherence` alone remains green with an
  exemption diagnostic and adding the review token to the same call makes absent review pending.
- VO-003 [PD-005] gateInversion: Temporarily invert the new default-review predicate in the bounded
  source hunk, run the focused suite, observe the default-refusal fixture red, restore the production
  predicate, and rerun green.
- VO-004 [PD-001] [PD-002] build: Build `src/FS.GG.Coord.Cli` in Release and run the complete
  `FS.GG.Coord.Cli.Tests` project.
- VO-005 [PD-006] [PD-007] semanticTest: Run Core and CLI suites, the 648-assertion production parity
  corpus, and the focused base-movement fixture.
- VO-006 [PD-008] gateInversion: Mutate the final base equality so moved-base authorization reaches
  the merge callback, observe the guarded-landing fixture red, restore, and rerun green.
- VO-007 producerVerifier: After `fsgg-sdd ship`, run the repository-owned ship-verdict provenance
  fixer and verifier; run signature-doc-siting against its independently maintained exact-count baseline.

## Performance Intent
No performance intent is declared for this work item.

## Migration Posture
- PM-001 [PC-001] compatibleVerdictVocabulary: no option, stdout word, or exit code is added. The
  behavioral tightening affects only otherwise-green plain calls lacking current review evidence; the
  one known critic-free automated merge caller is preserved by its already-present assertion token.

## Generated View Impact
- GV-001 [PD-001] [PD-005] workModel: `readiness/2360-landable-review-acceptance/work-model.json`
  and `analysis.json` are SDD projections regenerated from the authored lifecycle package. The source
  and test change introduces no repository generator, registry, or projected skill surface, so these
  readiness receipts are the only generated views affected.

## Accepted Deferrals
No accepted plan deferrals recorded.

## Planning Findings
No blocking planning findings recorded.

## Advisory Notes
- Optional Governance pointers remain compatibility facts only.

## Lifecycle Notes
- Next lifecycle action: `fsgg-sdd tasks --work 2360-landable-review-acceptance`.
