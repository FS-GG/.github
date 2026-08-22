---
schemaVersion: 1
workId: ci-runtime-optimization
title: CI Runtime Optimization Clarifications
stage: clarify
changeTier: tier1
status: clarified
sourceSpec: work/ci-runtime-optimization/spec.md
publicOrToolFacingImpact: true
---

# CI Runtime Optimization Clarifications Clarifications

## Source Specification
- work/ci-runtime-optimization/spec.md

## Clarification Questions
- CQ-001 [FR-001] [FR-002]: What happens when changed-file classification is incomplete or fails?
- CQ-002 [FR-003]: May mutation population or survivor accounting be reduced to meet a runtime target?
- CQ-003 [FR-004] [FR-005] [FR-006]: Which shell verdict owns the live repository and when may the synthetic fixture be omitted?
- CQ-004 [FR-007] [FR-008]: What is the authority for non-vacuity after duplicate test runs are removed?
- CQ-005 [FR-009]: Should this change introduce shared cross-workflow build artifacts?

## Answers
- CQ-001: Run the expensive gate. Unknown, missing base, diff failure, parse failure,
  deletion, or rename cannot authorize an omission.
- CQ-002: No. Scheduling and witness selection may change only when every enumerated
  mutant remains killed or explicitly justified and the unmutated control remains green.
- CQ-003: The `lint` job exclusively owns the live-tree verdict. The fixture owns
  synthetic positive/negative controls and runs when its implementation or contract
  changes; it must not rerun the live tree.
- CQ-004: The TRX emitted by the original successful test execution is authoritative.
  Missing, malformed, zero-test, or below-floor counters fail the same job.
- CQ-005: No. Shared artifacts are deferred because provenance, out-of-tree siting,
  upload/download cost, and exact-SHA binding need an independent experiment.

## Decisions
- DEC-001 [CQ-001] [FR-001] [FR-002] [FR-006]: All change classifiers are conservative
  and fail closed to the expensive path.
- DEC-002 [CQ-002] [FR-003]: Retain the complete signature-doc mutant census, one
  green control, explicit skip allowlist, and zero-survivor contract.
- DEC-003 [CQ-003] [FR-004] [FR-005]: Separate live shell lint from synthetic gate
  self-test and remove the fixture's duplicate live-tree invocation.
- DEC-004 [CQ-004] [FR-007] [FR-008]: Emit TRX once per suite and parse counters from
  that file with a dedicated fail-closed helper.
- DEC-005 [CQ-005] [FR-009]: Keep job names and unrelated workflows stable; record
  shared build artifacts and operational reconcile reuse as future work only.

## Accepted Deferrals
No accepted deferrals recorded.

## Remaining Ambiguity
No blocking ambiguity remains.

## Lifecycle Notes
- Next lifecycle action: `fsgg-sdd checklist --work ci-runtime-optimization`.
