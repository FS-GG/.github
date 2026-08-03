---
schemaVersion: 1
workId: 2178-workspace-collaborator-only-issues
title: Workspace collaborator-only issues and Project access security
stage: plan
changeTier: tier1
status: planned
sourceSpec: work/2178-workspace-collaborator-only-issues/spec.md
sourceClarifications: work/2178-workspace-collaborator-only-issues/clarifications.md
sourceChecklist: work/2178-workspace-collaborator-only-issues/checklist.md
publicOrToolFacingImpact: true
---

# Workspace collaborator-only issues and Project access security Plan

Prose status: planned

## Source Snapshot
- spec: work/2178-workspace-collaborator-only-issues/spec.md sha256:95e7fa69f196e983af4c353c8b48b17a3baf06d16340fd3ae1d5502e3f45d0e6 schemaVersion:1
- clarifications: work/2178-workspace-collaborator-only-issues/clarifications.md sha256:741cf0b83c566e68e3b714094fc480dc467e9b640bd97a2db4fe2bbf7320376d schemaVersion:1
- checklist: work/2178-workspace-collaborator-only-issues/checklist.md sha256:bd858d20c2f0f85a55ef673f9df5781692ae4b94ac694a46b7be7395ab137f1f schemaVersion:1

## Plan Scope
- Work item 2178-workspace-collaborator-only-issues is planned from the current specification, clarification, and checklist facts.
- Requirement count: 6.
- Clarification decision count: 0.
- Checklist result count: 6.

## Plan Decisions
- PD-001 [AC-001] [FR-001] complete: Read the typed repository policy before mutation, re-read after mutation, and persist prior/final/actor/source; grade collaborator policy only when `hasIssuesEnabled` is true.
- PD-002 [AC-001] [FR-002] complete: Resolve trusted users/teams to node IDs and send them only as structured GraphQL variables; validate requested-grant payload identity/cardinality without representing it as an effective-access read.
- PD-003 [AC-001] [FR-003] complete: Persist visibility and requested-grant facts as a partial receipt, plus one deduplicated obligation for the unobservable organization base permission and effective/exclusive writer set. Clear it only when both human assertions match the requested state.
- PD-004 [AC-001] [FR-004] complete: Preserve repository and Project obligations independently when either resource is absent or unreadable, and make every resume transition target-specific and idempotent.
- PD-005 [AC-001] [FR-005] complete: Exercise the built CLI through a GraphQL parser/structured-variable seam and independently model API grant payloads, human effective writers, visibility mutation/reread state, persistence, failure, and redaction.
- PD-006 [AC-001] [FR-006] complete: Publish and verify the byte-identical 0.9.0 package on both feeds before a separate registry-adoption receipt advances dependency truth.

## Contract Impact
- PC-001 [PD-002] [PD-003] command report: `new-sdd-workspace secure` adds typed Project intent and a two-fact human completion spelling; scaffold provenance adds version-tolerant receipt/obligation objects while preserving unrelated rows.

## Verification Obligations
- VO-001 [PD-005] [PC-001] semanticTest: Run `tests/new-sdd-workspace/run.sh`, `tests/projects-audit/run.sh`, `tests/repos-audit/run.sh`, the workflow-derived suite selector, an exact live schema coercion probe with fake node IDs, and package inspection.

## Performance Intent
No performance intent is declared for this work item.

## Migration Posture
- PM-001 [PC-001] additive: Existing repository-only secure invocations remain valid; Project completion is intentionally stricter because it must bind both unobservable access facts.

## Generated View Impact
- GV-001 [PD-003] [PD-005] workModel: readiness views trace the partial-receipt/human-completion state machine and its observed verification sources.

## Accepted Deferrals
No accepted plan deferrals recorded.

## Planning Findings
No blocking planning findings recorded.

## Advisory Notes
- Optional Governance pointers remain compatibility facts only.

## Lifecycle Notes
- Next lifecycle action: `fsgg-sdd tasks --work 2178-workspace-collaborator-only-issues`.
