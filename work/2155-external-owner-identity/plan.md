---
schemaVersion: 1
workId: 2155-external-owner-identity
title: Preserve canonical external-owner identity across scheduler and claim paths
stage: plan
changeTier: tier1
status: planned
sourceSpec: work/2155-external-owner-identity/spec.md
sourceClarifications: work/2155-external-owner-identity/clarifications.md
sourceChecklist: work/2155-external-owner-identity/checklist.md
publicOrToolFacingImpact: true
---

# Preserve canonical external-owner identity across scheduler and claim paths Plan

Prose status: planned

## Source Snapshot
- spec: work/2155-external-owner-identity/spec.md sha256:5fc315b65d8a712f1584e21456228bf5faeb0f5d145091d6c18ca87eca453d50 schemaVersion:1
- clarifications: work/2155-external-owner-identity/clarifications.md sha256:252385ab9f3c71ac9e851a47d9349075050543ce0078534e6c0277e022862f9d schemaVersion:1
- checklist: work/2155-external-owner-identity/checklist.md sha256:a3c0e45baa3b8fa3a1ec3ddfee5b9d06d59b3fced9ba91a324db278a556459ec schemaVersion:1

## Plan Scope
- Work item 2155-external-owner-identity is planned from the current specification, clarification, and checklist facts.
- Requirement count: 3.
- Clarification decision count: 0.
- Checklist result count: 3.

## Plan Decisions
- PD-001 [AC-001] [FR-001] complete: Make `Owner`, `Repo`, and `Number` the canonical identity carried by the scheduler candidate and claim command. `Ref` remains a compact display projection only; no mutation may reconstruct identity by applying the default owner to that projection.
- PD-002 [AC-001] [FR-002] complete: Thread the selected typed candidate directly from the global scheduler through `take` into the GitHub claim target. Cover default and external rows with the same repo name and number, then prove the offered external row is the sole row mutated.
- PD-003 [AC-001] [FR-003] complete: Extend machine receipts to expose canonical `owner`, `repo`, and `number`, including batch/next selection and claim output. Keep human bare-ref output compatible only where it is explicitly a display contract.

## Contract Impact
- PC-001 [PD-001] command receipt: `FS.GG.Coord.Core` selection and `FS.GG.Coord.GitHub` claim transport share a typed `IssueRef`; `FS.GG.Coord.Cli` JSON receipts serialize that identity. The external owner is a required mutation input, never inferred from a compact string.

## Verification Obligations
- VO-001 [PD-001] [PC-001] semanticTest: Run Core, CLI, GitHub, and end-to-end tests that prove (a) twin-owner selection claims only `EHotwagner/rogue3#96`, (b) `batch --repo cross-repo` followed by bare `take` preserves that exact target, (c) JSON receipts expose the owner, and (d) the live external row recovery posts its claim marker and updates its project Status without touching an `FS-GG/rogue3#96` twin.

## Performance Intent
No performance intent is declared for this work item.

## Migration Posture
- PM-001 [PC-001] compatibility: Existing ownerless human display strings remain accepted where their owner is unambiguous by command scope. New cross-owner mutations use the typed candidate, so no persisted board-data migration is needed and ambiguous reparsing is removed from the mutating path.

## Generated View Impact
- GV-001 [PD-001] workModel: `readiness/2155-external-owner-identity/work-model.json` and generated Codex/Claude guidance must refresh after plan, task, and evidence changes; stale generated guidance is a diagnostic, not authority over the authored SDD artifacts.

## Accepted Deferrals
No accepted plan deferrals recorded.

## Planning Findings
No blocking planning findings recorded.

## Advisory Notes
- Optional Governance pointers remain compatibility facts only.

## Lifecycle Notes
- Next lifecycle action: `fsgg-sdd tasks --work 2155-external-owner-identity`.
