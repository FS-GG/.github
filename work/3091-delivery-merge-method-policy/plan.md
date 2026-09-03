---
schemaVersion: 1
workId: 3091-delivery-merge-method-policy
title: Typed delivery merge-method policy
stage: plan
changeTier: tier1
status: planned
sourceSpec: work/3091-delivery-merge-method-policy/spec.md
sourceClarifications: work/3091-delivery-merge-method-policy/clarifications.md
sourceChecklist: work/3091-delivery-merge-method-policy/checklist.md
publicOrToolFacingImpact: true
---

# Typed delivery merge-method policy Plan

Prose status: planned

## Source Snapshot
- spec: work/3091-delivery-merge-method-policy/spec.md sha256:256be493a0a9c2dc05d88065321dccbe115bbbc954005f48686c8a0a14930b96 schemaVersion:1
- clarifications: work/3091-delivery-merge-method-policy/clarifications.md sha256:062bda409155de98724b82448bf5e6d7dea9975e333c29a2601b481563b5aad9 schemaVersion:1
- checklist: work/3091-delivery-merge-method-policy/checklist.md sha256:d054dbb9b332b475a7a8eb2f977015a5eab55ea606f0b92628d36ffde945773b schemaVersion:1

## Plan Scope
- Work item 3091-delivery-merge-method-policy is planned from the current specification, clarification, and checklist facts.
- Requirement count: 6.
- Clarification decision count: 0.
- Checklist result count: 6.

## Plan Decisions
- PD-001 [AC-001] [AC-002] [AC-003] [AC-004] [FR-001] complete: Observe all three merge capabilities as one complete typed repository-policy fact and fail closed when the response is incomplete.
- PD-002 [AC-001] [AC-002] [AC-003] [AC-004] [FR-002] complete: Derive the allowed method through the pure deterministic preference squash, then rebase, then merge, returning a typed no-method refusal instead of probing with writes.
- PD-003 [AC-001] [AC-002] [AC-003] [FR-003] complete: Require the selected typed method at the write boundary and serialize it with the exact guarded head SHA.
- PD-004 [AC-001] [AC-002] [AC-003] [AC-004] [FR-004] complete: Select policy before guardedLanding invokes its merge callback while preserving all existing freshness and authorization predicates.
- PD-005 [AC-001] [FR-005] complete: Extend the existing `graphql repository-policy` JSON projection additively with the three observed merge-capability booleans so operators and fixtures can inspect the same facts delivery consumes.
- PD-006 [AC-001] [AC-002] [AC-003] [AC-004] [FR-006] complete: Cover every capability combination at the pure selector, malformed/incomplete GraphQL inputs, explicit write payloads, squash-only live-handler composition, and all-false zero-PUT refusal.

## Contract Impact
- PC-001 [PD-001] command report: `graphql repository-policy` gains `mergeCommitAllowed`, `squashMergeAllowed`, and `rebaseMergeAllowed`; `Writes.mergeAtHead` becomes a typed internal signature change and emits GitHub's documented `merge_method` value.

## Verification Obligations
- VO-001 [PD-001] [PD-002] [PD-003] [PD-004] [PD-005] [PD-006] [PC-001] semanticTest: Require focused GraphQL/write/lifecycle tests plus complete engine qualification, with a recording transport proving an unreadable or no-method policy performs zero merge PUTs.

## Performance Intent
No performance intent is declared for this work item.

## Migration Posture
- PM-001 [PC-001] diagnoseOnly: The new policy fields are additive at the CLI boundary, but delivery treats an old or incomplete repository-policy response as unreadable and refuses; there is no write-side fallback or legacy implicit merge default.

## Generated View Impact
- GV-001 [PD-001] [PD-006] workModel: Refresh the SDD work model and retained evidence from the exact implementation/test sources; generated views must report stale rather than silently projecting pre-repair facts.

## Accepted Deferrals
No accepted plan deferrals recorded.

## Planning Findings
No blocking planning findings recorded.

## Advisory Notes
- Optional Governance pointers remain compatibility facts only.

## Lifecycle Notes
- Next lifecycle action: `fsgg-sdd tasks --work 3091-delivery-merge-method-policy`.
