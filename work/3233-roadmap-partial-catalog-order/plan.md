---
schemaVersion: 1
workId: 3233-roadmap-partial-catalog-order
title: Accept ordered partial roadmap catalogs
stage: plan
changeTier: tier1
status: planned
sourceSpec: work/3233-roadmap-partial-catalog-order/spec.md
sourceClarifications: work/3233-roadmap-partial-catalog-order/clarifications.md
sourceChecklist: work/3233-roadmap-partial-catalog-order/checklist.md
publicOrToolFacingImpact: true
---

# Accept ordered partial roadmap catalogs Plan

Prose status: planned

## Source Snapshot
- spec: work/3233-roadmap-partial-catalog-order/spec.md sha256:73a42865bf610bcd345b28288ac43a9537780bf3df27adc1859d06d068ea3c28 schemaVersion:1
- clarifications: work/3233-roadmap-partial-catalog-order/clarifications.md sha256:4fc2233cc879f6a5ba0c640f95de1cba555b16017ffc00b236999f90831f3913 schemaVersion:1
- checklist: work/3233-roadmap-partial-catalog-order/checklist.md sha256:1549ab98ca61837193886e80440ae84d7dfb9da050a724236aaf61747d304688 schemaVersion:1

## Plan Scope
- Work item 3233-roadmap-partial-catalog-order is planned from the current specification, clarification, and checklist facts.
- Requirement count: 4.
- Clarification decision count: 0.
- Checklist result count: 4.

## Plan Decisions
- PD-001 [AC-001] [FR-001] complete: Reproduce with the immutable production roadmap, catalog, and request, then require the repaired compiler to select GS2-07.3 and its accepted GS2-07.2 prerequisite.
- PD-002 [AC-001] [FR-002] complete: Compare the catalog prefix with canonical roadmap order filtered by the catalog prefix's own ID set, so omitted historical rows are irrelevant but relative order remains authoritative.
- PD-003 [AC-001] [FR-003] complete: Preserve the separate exact first-unchecked identity check and existing immediate accepted-prerequisite validation; retain the B,A,C reorder negative control.
- PD-004 [AC-001] [FR-004] complete: Keep Protocol.fs unchanged and classify the coherent-set move as a 0.83.1 patch because no command, option, result, or exit-code vocabulary changes.

## Contract Impact
- PC-001 [PD-001] compatibility-preserving behavior repair: RoadmapWorkUnit preparation accepts the documented partial-catalog topology without changing the CLI or Protocol.fs wire surface.

## Verification Obligations
- VO-001 [PD-001] [PC-001] semanticTest: Run the 10 focused RoadmapWorkUnit core tests, the 4 CLI round-trip tests, and the exact production preparation command; retain order, frontier, and prerequisite negative controls.

## Performance Intent
No performance intent is declared for this work item.

## Migration Posture
- PM-001 [PC-001] publishBeforeAdopt: Prepare the 0.83.1 source frontier while holding package-version at 0.83.0, and declare exact-head post-merge obligations for automatic-release verification, SDD adoption, the GS2-07.3 pilot, and registry reconciliation.

## Generated View Impact
- GV-001 [PD-001] registryProjections: Regenerate compatibility, architecture, publishing inventory, and driver-manifest projections from the 0.83.1 source frontier; keep package-version at 0.83.0 until publication.

## Accepted Deferrals
No accepted plan deferrals recorded.

## Planning Findings
No blocking planning findings recorded.

## Advisory Notes
- Optional Governance pointers remain compatibility facts only.

## Lifecycle Notes
- Next lifecycle action: `fsgg-sdd tasks --work 3233-roadmap-partial-catalog-order`.
