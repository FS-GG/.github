---
schemaVersion: 1
workId: 2771-live-path-coherence
title: Live board path and maintaining-lane coherence
stage: plan
changeTier: tier1
status: planned
sourceSpec: work/2771-live-path-coherence/spec.md
sourceClarifications: work/2771-live-path-coherence/clarifications.md
sourceChecklist: work/2771-live-path-coherence/checklist.md
publicOrToolFacingImpact: true
---

# Live board path and maintaining-lane coherence Plan

Prose status: planned

## Source Snapshot
- spec: work/2771-live-path-coherence/spec.md sha256:4781c55f6aeab32228fd14ac0e226b1c91550bd4ea55299a2fa1962f814e650b schemaVersion:1
- clarifications: work/2771-live-path-coherence/clarifications.md sha256:9c1862ea2d31ad209c9e4ab4b0a0094700142d6e845759b493e4f066f444e079 schemaVersion:1
- checklist: work/2771-live-path-coherence/checklist.md sha256:3aa6ce5c3120edd69ed86a1ab5bdc5f3fac10eb43b4240303760576d15aff418 schemaVersion:1

## Plan Scope
- Work item 2771-live-path-coherence is planned from the current specification, clarification, and checklist facts.
- Requirement count: 5.
- Clarification decision count: 0.
- Checklist result count: 5.

## Plan Decisions
- PD-001 [AC-001] [FR-001] complete: For every declared token absent from the current tree, query Git rename history for that exact source path. Report only a proven rename whose destination still exists; an absent token without rename evidence remains a valid planned path.
- PD-002 [AC-001] [FR-002] complete: Keep exact-count-baseline ownership in a machine-readable JSON registry beside this check's fixtures. Each entry names the baseline and either a maintaining issue or an explicit exemption; maintaining issues are resolved from the same board-item inventory and must declare the baseline in `Paths:`.
- PD-003 [AC-001] [FR-003] complete: Consume the complete supplied board inventory and evaluate every issue whose live issue state is `OPEN`, independent of project Status, so Backlog is included. Invalid item shapes, missing bodies, unreadable registry data, and failed Git queries produce exit 2 rather than an empty result.
- PD-004 [AC-001] [FR-004] complete: Extend the shell fixture with two mutations that must make the command nonzero: an historical source token moved to a live destination and a baseline registry entry whose maintaining issue omits that baseline. Retain planned-path and malformed-input controls.
- PD-005 [AC-001] [FR-005] complete: Register `tests/pipefail-assertions/baseline.txt` as explicitly exempt because its opportunistic decrements have no pre-assigned issue lane; register signature-doc-siting against its named maintaining extraction lane and verify its declared coverage.

## Contract Impact
- PC-001 [PD-001] [PD-002] command report: `scripts/check-board-paths-live.py --items <json>` remains the entry point and preserves warning-only success for coherent inventories. New `--root` and `--baseline-registry` options make history and ownership inputs explicit; evidence errors return 2 and coherence violations return 1.

## Verification Obligations
- VO-001 [PD-001] [PD-002] [PD-003] [PD-004] [PD-005] [PC-001] semanticTest: Run `bash tests/board-paths-live/run.sh`; then invert each new gate independently and observe exit 1, and corrupt the item/registry inputs to observe exit 2. Run repository policy wiring checks after the focused fixture.

## Performance Intent
No performance intent is declared for this work item.

## Migration Posture
- PM-001 [PC-001] diagnoseOnly: Existing callers that supply only `--items` continue to work using the repository root and checked-in registry defaults. No issue body or baseline is rewritten; diagnostics tell operators which row or metadata entry to repair.

## Generated View Impact
- GV-001 [PD-001] [PD-002] workModel: `readiness/2771-live-path-coherence/work-model.json` and `analysis.json` are regenerated from the authored lifecycle sources by `fsgg-sdd analyze`; the baseline registry is authored test input, not a generated view.

## Accepted Deferrals
No accepted plan deferrals recorded.

## Planning Findings
No blocking planning findings recorded.

## Advisory Notes
- Optional Governance pointers remain compatibility facts only.

## Lifecycle Notes
- Next lifecycle action: `fsgg-sdd tasks --work 2771-live-path-coherence`.
