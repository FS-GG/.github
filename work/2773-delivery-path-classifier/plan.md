---
schemaVersion: 1
workId: 2773-delivery-path-classifier
title: Single authoritative delivery path classifier
stage: plan
changeTier: tier1
status: planned
sourceSpec: work/2773-delivery-path-classifier/spec.md
sourceClarifications: work/2773-delivery-path-classifier/clarifications.md
sourceChecklist: work/2773-delivery-path-classifier/checklist.md
publicOrToolFacingImpact: true
---

# Single authoritative delivery path classifier Plan

Prose status: planned

## Source Snapshot
- spec: work/2773-delivery-path-classifier/spec.md sha256:9c500b20152bb501322a2963af83f5c2083e3b740883bdfc36efcaf7f8596577 schemaVersion:1
- clarifications: work/2773-delivery-path-classifier/clarifications.md sha256:5f62f5c1b45065ef658d4f89bc20bba49d7d3546798a5bacbecd91b13e2add74 schemaVersion:1
- checklist: work/2773-delivery-path-classifier/checklist.md sha256:a0286291454cb124fbc9cee58d4f5d32c891fccedbf5048e8ed198bc65fa8ed7 schemaVersion:1

## Plan Scope
- Work item 2773-delivery-path-classifier is planned from the current specification, clarification, and checklist facts.
- Requirement count: 4.
- Clarification decision count: 0.
- Checklist result count: 4.

## Plan Decisions
- PD-001 [AC-001] [FR-001] complete: Define one discriminated union in FS.GG.Coord.Core whose cases distinguish declared, generated, SDD-package, undeclared, and unknown-authority paths and carry a stable diagnostic reason.
- PD-002 [AC-001] [FR-002] complete: Supply route-bound SDD package prefixes from the CLI adapter and admit only the selected work id's mandatory `work/` and `readiness/` trees without adding them to the authored touch-set.
- PD-003 [AC-001] [FR-003] complete: Make unreadable generated or SDD authority inputs explicit `Unknown` inputs; the classifier and both callers must reject them rather than substituting an empty set.
- PD-004 [AC-001] [FR-004] complete: Preserve the structured delivery problem through `RepairReviewHandoff` and render that problem in the operator-facing delivery response.

## Contract Impact
- PC-001 [PD-001] command report: `Delivery` exposes the typed classifier through its `.fsi`; `Client` projects it into `verify-paths` and delivery snapshots, and `DeliveryApplication` adds the existing problem text to `repairReviewHandoff` output without changing stage/action names.

## Verification Obligations
- VO-001 [PD-001] [PC-001] semanticTest: Add core exhaustive classification tests, CLI parity tests covering every union case, SDD-package acceptance, undeclared-file rejection, unreadable-authority rejection, and delivery rendering tests; invert each new gate and observe the focused suite fail before restoring it.

## Performance Intent
No performance intent is declared for this work item.

## Migration Posture
- PM-001 [PC-001] diagnoseOnly: Keep the existing JSON stage/action contract compatible; consumers gain an explanatory problem field/text on the repair action and require no data migration.

## Generated View Impact
- GV-001 [PD-001] workModel: Regenerate `readiness/2773-delivery-path-classifier/work-model.json` and `analysis.json` from the authored plan; these SDD receipts are mandatory route outputs and are not authored touch-set declarations.

## Accepted Deferrals
No accepted plan deferrals recorded.

## Planning Findings
No blocking planning findings recorded.

## Advisory Notes
- Optional Governance pointers remain compatibility facts only.

## Lifecycle Notes
- Next lifecycle action: `fsgg-sdd tasks --work 2773-delivery-path-classifier`.
