---
schemaVersion: 1
workId: 2136-generated-facts
title: Generated Operational Process Facts
stage: plan
changeTier: tier1
status: planned
sourceSpec: work/2136-generated-facts/spec.md
sourceClarifications: work/2136-generated-facts/clarifications.md
sourceChecklist: work/2136-generated-facts/checklist.md
publicOrToolFacingImpact: true
---

# Generated Operational Process Facts Plan

Prose status: planned

## Source Snapshot
- spec: work/2136-generated-facts/spec.md sha256:0cca3f4208d06f74133f96a1479e527839962d334882ca4ac24b5977fbdc4130 schemaVersion:1
- clarifications: work/2136-generated-facts/clarifications.md sha256:fc624056f528c53fa8c0c78970f6402a3b5b87ea72b2aa5fb4938b0a764e7f70 schemaVersion:1
- checklist: work/2136-generated-facts/checklist.md sha256:fdafd3e35fb40852767ecc8212f9705649645c32b98524951d1f6af1def10448 schemaVersion:1

## Plan Scope
- Work item 2136-generated-facts is planned from the current specification, clarification, and checklist facts.
- Requirement count: 2.
- Clarification decision count: 0.
- Checklist result count: 2.

## Plan Decisions
- PD-001 [AC-001] [FR-001] complete: Put wave capacity, review markers and round ceilings, lifecycle gates, and ledger field names in `Protocol`; make the batch parser and review receipt validator consume that typed policy before projections render it.
- PD-002 [AC-001] [FR-002] complete: Render paired managed regions from `facts --json` and derive the `.github` release inventory from registry producer rows; keep rationale and judgement outside those regions.

## Contract Impact
- PC-001 [PD-001] command report: `fsgg-coord facts --json` advances from protocol schema 10 to 11 with additive `wavePolicy`, `reviewPolicy`, `lifecyclePolicy`, and `ledgerPolicy` objects; old readers can ignore the new keys.

## Verification Obligations
- VO-001 [PD-001] [PC-001] semanticTest: Build the Release CLI, assert the new facts JSON, run projection and skill-quality fixture suites, and prove stale/duplicate generated literals plus registry producer mutation are rejected.

## Performance Intent
No performance intent is declared for this work item.

## Migration Posture
- PM-001 [PC-001] additive: Existing generated regions remain valid until regenerated; a stale region now fails the projection gate, while unknown additive facts remain ignorable for existing facts readers.

## Generated View Impact
- GV-001 [PD-001] workModel: Refresh SDD readiness and both authored skill roots after the typed facts and registry-backed release inventory are generated.

## Accepted Deferrals
No accepted plan deferrals recorded.

## Planning Findings
No blocking planning findings recorded.

## Advisory Notes
- Optional Governance pointers remain compatibility facts only.

## Lifecycle Notes
- Next lifecycle action: `fsgg-sdd tasks --work 2136-generated-facts`.
