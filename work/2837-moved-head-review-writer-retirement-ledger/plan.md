---
schemaVersion: 1
workId: 2837-moved-head-review-writer-retirement-ledger
title: Restart review-record sealing after retiring an accepted moved-head chain
stage: plan
changeTier: tier1
status: planned
sourceSpec: work/2837-moved-head-review-writer-retirement-ledger/spec.md
sourceClarifications: work/2837-moved-head-review-writer-retirement-ledger/clarifications.md
sourceChecklist: work/2837-moved-head-review-writer-retirement-ledger/checklist.md
publicOrToolFacingImpact: true
---

# Restart review-record sealing after retiring an accepted moved-head chain Plan

Prose status: planned

## Source Snapshot
- spec: work/2837-moved-head-review-writer-retirement-ledger/spec.md sha256:0ce541070a3f2bb5088dc8e6d78ba526a8817655e33cf80066195da2310f5d18 schemaVersion:1
- clarifications: work/2837-moved-head-review-writer-retirement-ledger/clarifications.md sha256:c1afb8ae6c156418c45388d9cfa8e7046095302fe6f2411717436b9dd87daebe schemaVersion:1
- checklist: work/2837-moved-head-review-writer-retirement-ledger/checklist.md sha256:2b39988fcc0d9545689efb6922e9463e1714c8f142f953b0d0040115e662b10a schemaVersion:1

## Plan Scope
- Work item 2837-moved-head-review-writer-retirement-ledger is planned from the current specification, clarification, and checklist facts.
- Requirement count: 2.
- Clarification decision count: 2.
- Checklist result count: 2.

## Plan Decisions
- PD-001 [AC-001] [FR-001] complete: Plan requirement FR-001 through the plan command contract.
- PD-002 [AC-001] [FR-002] complete: Plan requirement FR-002 through the plan command contract.

## Contract Impact
- PC-001 [PD-001] command report: fsgg-sdd plan, work/2837-moved-head-review-writer-retirement-ledger/plan.md, and command-report JSON are tool-facing and compatibility-preserving.

## Verification Obligations
- VO-001 [PD-001] [PC-001] semanticTest: Run focused command tests, FSI/prelude evidence, and CLI smoke evidence before task generation.

## Performance Intent
No performance intent is declared for this work item.

## Migration Posture
- PM-001 [PC-001] diagnoseOnly: Plan schemaVersion 1 is accepted; unsupported plan schemas diagnose before write.

## Generated View Impact
- GV-001 [PD-001] workModel: readiness/2837-moved-head-review-writer-retirement-ledger/work-model.json refreshes from current plan sources or reports staleGeneratedView.

## Accepted Deferrals
No accepted plan deferrals recorded.

## Planning Findings
No blocking planning findings recorded.

## Advisory Notes
- Optional Governance pointers remain compatibility facts only.

## Lifecycle Notes
- Next lifecycle action: `fsgg-sdd tasks --work 2837-moved-head-review-writer-retirement-ledger`.
