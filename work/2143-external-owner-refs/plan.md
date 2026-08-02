---
schemaVersion: 1
workId: 2143-external-owner-refs
title: Preserve External Owner References
stage: plan
changeTier: tier1
status: planned
sourceSpec: work/2143-external-owner-refs/spec.md
sourceClarifications: work/2143-external-owner-refs/clarifications.md
sourceChecklist: work/2143-external-owner-refs/checklist.md
publicOrToolFacingImpact: true
---

# Preserve External Owner References Plan

Prose status: planned

## Source Snapshot
- spec: work/2143-external-owner-refs/spec.md sha256:adc2108e992448b4664d1fde4f81569181451f19357419eef3b61d113216704a schemaVersion:1
- clarifications: work/2143-external-owner-refs/clarifications.md sha256:84183e237a7d33b78fe6696fb644fc320a80a8bebaac9784449ac8955b3078bb schemaVersion:1
- checklist: work/2143-external-owner-refs/checklist.md sha256:502317f25e515599cfc074cca3d28a6dbdd8c8752e7bd73a7bdf5bf3f25919d4 schemaVersion:1

## Plan Scope
- Work item 2143-external-owner-refs is planned from the current specification, clarification, and checklist facts.
- Requirement count: 1.
- Clarification decision count: 0.
- Checklist result count: 1.

## Plan Decisions
- PD-001 [AC-001] [FR-001] complete: Treat an owner-qualified project-item id obtained by intake as the canonical identity for subsequent external-owner field writes; retain the uncached lookup for same-owner writes so removal from the board remains observable.

## Contract Impact
- PC-001 [PD-001] command contract: `add`, `item-id`, `set-field`, and `set-field --batch` preserve `owner/repo/number/board` as one identity. A same-name repository under another owner cannot satisfy or receive a cached scan fold for this item.

## Verification Obligations
- VO-001 [PD-001] [PC-001] semanticTest: Exercise single and aliased batch writes from an `EHotwagner/rogue3#96`-shaped positive cache, prove the mutation targets that item id without an `itemId` re-query, and prove scan cache folding includes the issue owner.

## Performance Intent
No performance intent is declared for this work item.

## Migration Posture
- PM-001 [PC-001] migration: Existing cache entries without an `owner` retain the legacy same-repository fold only; current scans include an owner and therefore make cross-owner same-name folds exact. No board data migration is required.

## Generated View Impact
- GV-001 [PD-001] workModel: Refresh readiness from this authored contract and retain generated SDD views alongside the source changes so the external-owner decision is reviewable.

## Accepted Deferrals
No accepted plan deferrals recorded.

## Planning Findings
No blocking planning findings recorded.

## Advisory Notes
- Optional Governance pointers remain compatibility facts only.

## Lifecycle Notes
- Next lifecycle action: `fsgg-sdd tasks --work 2143-external-owner-refs`.
