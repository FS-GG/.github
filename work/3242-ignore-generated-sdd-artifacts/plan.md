---
schemaVersion: 1
workId: 3242-ignore-generated-sdd-artifacts
title: Ignore Generated Sdd Artifacts
stage: plan
changeTier: tier1
status: planned
sourceSpec: work/3242-ignore-generated-sdd-artifacts/spec.md
sourceClarifications: work/3242-ignore-generated-sdd-artifacts/clarifications.md
sourceChecklist: work/3242-ignore-generated-sdd-artifacts/checklist.md
publicOrToolFacingImpact: true
---

# Accept Independently Regenerated Ignored SDD Artifacts Plan

Prose status: planned

## Source Snapshot
- spec: work/3242-ignore-generated-sdd-artifacts/spec.md sha256:847080e794e67c4df6fb02fd30165df8d73c685f7873fdefa40f935062523d6d schemaVersion:1
- clarifications: work/3242-ignore-generated-sdd-artifacts/clarifications.md sha256:95238983ee9e8c3e9ab281a7964d9c1eadb6d5cc13f8e14a7a3a7d00c600c136 schemaVersion:1
- checklist: work/3242-ignore-generated-sdd-artifacts/checklist.md sha256:c85bf800246df4eb5c3dd749d52327cbf2a663d0b7d4666adacdd45b2c008e27 schemaVersion:1

## Plan Scope
- Work item 3242-ignore-generated-sdd-artifacts is planned from the current specification, clarification, and checklist facts.
- Requirement count: 4.
- Clarification decision count: 0.
- Checklist result count: 4.

## Plan Decisions
- PD-001 [AC-001] [FR-001] complete: Delete the live observer's four remote generated-readiness reads; they conflict with the standard ignored-artifact policy and duplicate the later independent observer.
- PD-002 [AC-001] [FR-002] complete: Keep `independentlyObserveSdd` unchanged as the sole SDD-output authority: pinned 1.5.0, fresh exact-candidate checkout, canonical output comparison, complete tasks, exact HEAD, and clean status.
- PD-003 [AC-002] [FR-003] complete: Preserve qualification-to-observation digest binding and all live claim, lifecycle, review, PR, merge, preparation, and revision-binding checks.
- PD-004 [AC-001] [AC-002] [FR-004] complete: Add a production-observer regression that supplies no remote generated readiness files and proves the injected independent observer remains mandatory and decisive.

## Contract Impact
- PC-001 [PD-001] compatibility-preserving acceptance behavior: standard ignored SDD outputs are accepted only when independently regenerated and bound to the exact candidate; no command or result schema changes.

## Verification Obligations
- VO-001 [PD-001] [PD-002] [PD-003] [PC-001] semanticTest: Run the focused roadmap transaction tests, full BoardOps Release suite, production-composition regression, and exact GS2-07.3 acceptance replay with negative controls.

## Performance Intent
No performance intent is declared for this work item.

## Migration Posture
- PM-001 [PC-001] publishBeforeAdopt: Ship a patch coherent set, update the pilot's exact engine, and rerun the blocked GS2-07.3 acceptance without changing its immutable implementation identity.

## Generated View Impact
- GV-001 [PD-001] no registry schema impact: refresh ordinary SDD evidence and release projections only; acceptance input and receipt schemas stay unchanged.

## Accepted Deferrals
No accepted plan deferrals recorded.

## Planning Findings
No blocking planning findings recorded.

## Advisory Notes
- Optional Governance pointers remain compatibility facts only.

## Lifecycle Notes
- Next lifecycle action: `fsgg-sdd tasks --work 3242-ignore-generated-sdd-artifacts`.
