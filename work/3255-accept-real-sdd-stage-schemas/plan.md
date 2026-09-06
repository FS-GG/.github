---
schemaVersion: 1
workId: 3255-accept-real-sdd-stage-schemas
title: Accept Real Sdd Stage Schemas
stage: plan
changeTier: tier1
status: planned
sourceSpec: work/3255-accept-real-sdd-stage-schemas/spec.md
sourceClarifications: work/3255-accept-real-sdd-stage-schemas/clarifications.md
sourceChecklist: work/3255-accept-real-sdd-stage-schemas/checklist.md
publicOrToolFacingImpact: true
---

# Accept Real Sdd Stage Schemas Plan

Prose status: planned

## Source Snapshot
- spec: work/3255-accept-real-sdd-stage-schemas/spec.md sha256:c1bac88feb003a4574353bbaf9996b78530d7a8aa9c1ab35a2a0b92d2f5ee528 schemaVersion:1
- clarifications: work/3255-accept-real-sdd-stage-schemas/clarifications.md sha256:1d52fe2642564cad43731bfb950e020aaa253582a9bdccf9398685007de39e18 schemaVersion:1
- checklist: work/3255-accept-real-sdd-stage-schemas/checklist.md sha256:533b5b84fb25dbab296897e0faa6bd2615ba85b51981477751ea7e2790b7b055 schemaVersion:1

## Plan Scope
- Work item 3255-accept-real-sdd-stage-schemas is planned from the current specification, clarification, and checklist facts.
- Requirement count: 4.
- Clarification decision count: 0.
- Checklist result count: 4.

## Plan Decisions
- PD-001 [AC-001] [FR-001] complete: Split acceptance observation validation by SDD stage: analyze and verify retain their source-backed artifact contract and reject only blocking findings, while preserving exact ready-state checks.
- PD-002 [AC-001] [FR-002] complete: Validate ship through its native sourcesDigest, verificationReadiness, and disposition objects, including an empty blockingFindingIds set, rather than assuming analyze-only arrays.
- PD-003 [AC-001] [FR-003] complete: Clone the already authenticated candidate checkout into a disposable local checkout before qualification execution, then rebind execution paths into that clone and require the original candidate HEAD, tree, and status to remain unchanged.
- PD-004 [AC-001] [FR-004] complete: Add focused native-shape, blocking-finding, and qualification-mutation regressions; retain canonical artifact equality and every live authority check.

## Contract Impact
- PC-001 [PD-001] compatibility repair: the public acceptance input schema is unchanged; the engine now reads the already published SDD 1.5.0 stage contracts correctly and qualification gains internal execution isolation.

## Verification Obligations
- VO-001 [PD-001] [PD-002] [PD-003] [PD-004] [PC-001] semanticTest: Run focused Core and CLI qualification suites, the full Release test set, mutation inversions, and the preserved GS2-07.3 production seal before acceptance.

## Performance Intent
No performance intent is declared for this work item.

## Migration Posture
- PM-001 [PC-001] publishBeforeAdopt: Publish a coherent patch set, adopt the exact CLI and kit pins in SDD, then rerun the immutable GS2-07.3 candidate without editing its source facts.

## Generated View Impact
- GV-001 [PD-001] no schema projection change: refresh the work-model and readiness evidence only; registry reconciliation records the patch package identities after promotion.

## Accepted Deferrals
No accepted plan deferrals recorded.

## Planning Findings
No blocking planning findings recorded.

## Advisory Notes
- Optional Governance pointers remain compatibility facts only.

## Lifecycle Notes
- Next lifecycle action: `fsgg-sdd tasks --work 3255-accept-real-sdd-stage-schemas`.
