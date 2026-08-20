---
schemaVersion: 1
workId: 2758-agent-contract-version
title: Agent contract versioning
stage: plan
changeTier: tier1
status: planned
sourceSpec: work/2758-agent-contract-version/spec.md
sourceClarifications: work/2758-agent-contract-version/clarifications.md
sourceChecklist: work/2758-agent-contract-version/checklist.md
publicOrToolFacingImpact: true
---

# Agent contract versioning Plan

Prose status: planned

## Source Snapshot
- spec: work/2758-agent-contract-version/spec.md sha256:6cf6b4f77e809e2f8e9c6bf4bd7d2be09c8dddf8b729b6afe5683bc3179120c4 schemaVersion:1
- clarifications: work/2758-agent-contract-version/clarifications.md sha256:20eb643d1846d22a353c49009874842ad78ec8a37e64db72911df2ba15b1942d schemaVersion:1
- checklist: work/2758-agent-contract-version/checklist.md sha256:dbaebf950cd174c5d50d9e86af03d833f9ce11b9b15852fb8b14ffe8ff85116c schemaVersion:1

## Plan Scope
- Work item 2758-agent-contract-version is planned from the current specification, clarification, and checklist facts.
- Requirement count: 1.
- Clarification decision count: 0.
- Checklist result count: 1.

## Plan Decisions
- PD-001 [AC-001] [FR-001] complete: Compute the agent-contract version in `scripts/generate-projections` from the canonical byte stream of both skill roots, with stable path ordering and root-relative identities, then expose that one computation to the projection gate rather than reimplementing it.

## Contract Impact
- PC-001 [PD-001] command report: Extend the existing projection command/gate output with one durable `agentContractVersion` field; preserve existing command modes and derive the value on every merge with no hand-maintained version or release cadence.

## Verification Obligations
- VO-001 [PD-001] [PC-001] semanticTest: Run the projection suite; mutate a canonical skill byte and observe the version move; regenerate unchanged projections and observe the version remain stable; invert each new assertion once and record the named red witness.

## Performance Intent
No performance intent is declared for this work item.

## Migration Posture
- PM-001 [PC-001] diagnoseOnly: Existing projection consumers remain compatible because the durable field is additive; no migration or cohort freeze exists, and the digest rolls naturally whenever canonical skill bytes merge.

## Generated View Impact
- GV-001 [PD-001] workModel: `tests/projection` owns the executable contract-version fixtures and mutation controls; no second generated artifact class is introduced.

## Accepted Deferrals
No accepted plan deferrals recorded.

## Planning Findings
No blocking planning findings recorded.

## Advisory Notes
- Optional Governance pointers remain compatibility facts only.

## Lifecycle Notes
- Next lifecycle action: `fsgg-sdd tasks --work 2758-agent-contract-version`.
