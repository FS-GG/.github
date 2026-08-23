---
schemaVersion: 1
workId: 2834-review-contract-diagnostics
title: Actionable and honest review contract diagnostics
stage: plan
changeTier: tier1
status: planned
sourceSpec: work/2834-review-contract-diagnostics/spec.md
sourceClarifications: work/2834-review-contract-diagnostics/clarifications.md
sourceChecklist: work/2834-review-contract-diagnostics/checklist.md
publicOrToolFacingImpact: true
---

# Actionable and honest review contract diagnostics Plan

Prose status: planned

## Source Snapshot
- spec: work/2834-review-contract-diagnostics/spec.md sha256:0a61a9d58ae4f4e6c58db4d9381998ae80f05584928b547dbaf83742651d0cd7 schemaVersion:1
- clarifications: work/2834-review-contract-diagnostics/clarifications.md sha256:6be3dc88e527fa6003d3aad7cdf0020c6d7ba06160b9aed05d7c7f1c829cb8f4 schemaVersion:1
- checklist: work/2834-review-contract-diagnostics/checklist.md sha256:8d09ea5547ff1ab8c4bb2fb192f53886e560d051ae06dd063a3d2971740a11eb schemaVersion:1

## Plan Scope
- Change only the narrowed declared paths: typed review-ledger validation, the lifecycle landable refusal, and their focused Core/Lifecycle tests.
- Preserve review marker schemas, review round semantics, landability verdict codes, and unrelated coordination behavior.
- Keep route evidence as the established ordered list shape and make the refusal explicit that the validator enforces cardinality and order, not semantic truth.

## Plan Decisions
- PD-001 [AC-001] [FR-001] complete: Extend the exact missing-host-acceptance refusal with the producing sequence `fsgg-coord review wait` then `fsgg-coord review record`, without changing its verdict or token.
- PD-002 [AC-001] [FR-002] complete: Preserve the documented shape-only contract and rewrite the meaningful-route refusal to say it requires exactly four ordered entries representing built artifact, executed command, compared routes, and observed result.
- PD-003 [AC-001] [FR-003] complete: Preserve the existing not-meaningful exactly-one-reason rule and its independent non-blank list validation.
- PD-004 [AC-001] [FR-004] complete: Add focused positive and negative regressions for both diagnostics and mutation-test the exact-count gate.

## Contract Impact
- PC-001 [PD-001] [PD-002] review contract: The CLI refusal gains actionable remediation and the routeEvidence refusal accurately documents its existing ordered string-list cardinality; no serialized field, marker schema, or accepted shape changes.

## Verification Obligations
- VO-001 [PD-001] [PD-002] [PD-003] [PD-004] [PC-001] semanticTest: Run the focused Core and Lifecycle tests, build both affected test projects, and temporarily invert the new semantic predicate to prove the focused Core regression turns red before restoring green.

## Performance Intent
No performance intent is declared for this work item.

## Migration Posture
- PM-001 [PC-001] compatibleDiagnostic: Existing records remain valid under the same shape-only rule; only refusal prose changes to distinguish structural validation from critic-authored semantic judgement.

## Generated View Impact
- GV-001 [PD-001] workModel: Regenerate tasks and analysis after authoring this plan so readiness binds the exact implementation and verification decisions.

## Accepted Deferrals
No accepted plan deferrals recorded.

## Planning Findings
No blocking planning findings recorded.

## Advisory Notes
- Optional Governance pointers remain compatibility facts only.

## Lifecycle Notes
- Next lifecycle action: `fsgg-sdd tasks --work 2834-review-contract-diagnostics`.
