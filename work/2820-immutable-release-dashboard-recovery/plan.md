---
schemaVersion: 1
workId: 2820-immutable-release-dashboard-recovery
title: Immutable Release Dashboard Recovery
stage: plan
changeTier: tier1
status: planned
sourceSpec: work/2820-immutable-release-dashboard-recovery/spec.md
sourceClarifications: work/2820-immutable-release-dashboard-recovery/clarifications.md
sourceChecklist: work/2820-immutable-release-dashboard-recovery/checklist.md
publicOrToolFacingImpact: true
---

# Immutable Release Dashboard Recovery Plan

Prose status: planned

## Source Snapshot
- spec: work/2820-immutable-release-dashboard-recovery/spec.md sha256:7604f0eb811bbbac8190e44f46c72f9897d0ce1bdf1e8f23fa688949b91fc423 schemaVersion:1
- clarifications: work/2820-immutable-release-dashboard-recovery/clarifications.md sha256:eae894e4ce2eaff625e2834ced6259d166c45ef9b6f7a82bc675fe1f666e4c54 schemaVersion:1
- checklist: work/2820-immutable-release-dashboard-recovery/checklist.md sha256:9dd0d2a934ed8b7977f89221b68a95fbef8edcf1188ca6236eefce4da20d93c5 schemaVersion:1

## Plan Scope
- Work item 2820-immutable-release-dashboard-recovery is planned from the current specification, clarification, and checklist facts.
- Requirement count: 6.
- Clarification decision count: 4.
- Checklist result count: 6.

## Plan Decisions
- PD-001 [AC-001] [FR-001] complete: Refactor `scripts/release-saga-ci.sh`'s single `upload_journal` boundary to query `gh release view` for immutable state. On mutable drafts it retains the current `release upload --clobber`; on immutable releases it downloads the exact named remote journal into a temporary directory, validates it against the current release/package identity and prepared artifacts, then returns without any asset mutation.
- PD-002 [AC-002] [FR-002] complete: Keep release-kit and release-coord-engine on the shared adapter and explicitly document/bind their journal operations before the existing token mint and dashboard write/read-back steps. Do not duplicate the recovery branch in workflow YAML.
- PD-003 [AC-003] [FR-003] complete: Preserve draft journal uploads and the promotion observer's exact-three-journal barrier. Add a topology assertion that promotion remains the only workflow making the coherent-set release non-draft/immutable.
- PD-004 [AC-004] [FR-004] complete: Validate immutable remote journals with the production `assert-identity` and `assert-artifacts` operations, additionally require the exact package entry, and fail closed when view/download/parse/identity/package state is absent or unreadable.
- PD-005 [AC-005] [FR-005] complete: Reuse the existing production dashboard-tick command and its executed self-test for roster non-vacuity, refused writes, unreadable read-back, and idempotency; the release-saga fixture proves both workflows can reach that unchanged gate after immutable journal recovery.
- PD-006 [AC-006] [FR-006] complete: Extend `tests/release-saga/run.sh` with a fake `gh` immutable release and exact remote journal. Prove no upload/edit/delete occurs, valid replay succeeds twice, missing/mismatched journals red, and both workflow topologies sequence journal recovery before dashboard delivery. Invert the immutable branch back to unconditional clobber and require the focused suite to fail on the HTTP-422 fake.

## Contract Impact
- PC-001 [PD-001] [PD-002] shellWorkflow: `scripts/release-saga-ci.sh` keeps its command/argument surface and package workflows keep their workflow_dispatch inputs; only immutable journal persistence changes from mutation to validated read-back.

## Verification Obligations
- VO-001 [PD-001] [PD-004] [PC-001] semanticTest: Run `bash tests/release-saga/run.sh` against the production adapter with immutable/mutable fake GitHub release states and exact/missing/mismatched journal subjects.
- VO-002 [PD-002] [PD-003] [PC-001] topologyTest: Parse both package workflows and promotion to prove the shared adapter remains before dashboard delivery and promotion remains after the complete three-journal barrier.
- VO-003 [PD-005] regressionTest: Run `bash tests/dashboard-tick/run.sh` to retain executed roster, write refusal, read-back, and idempotency evidence for the unchanged delivery route.
- VO-004 [PD-006] mutationTest: Subject-mutate immutable recovery to attempt `release upload --clobber`; the fake returns HTTP 422 and `bash tests/release-saga/run.sh` must red. Also invert remote journal identity and topology tokens one at a time and observe red before restoration.

## Performance Intent
No performance intent is declared for this work item.

## Migration Posture
- PM-001 [PC-001] compatible: No migration is required. Existing mutable drafts use the same journal asset, schema, and clobber path; already-promoted releases gain a read-only exact-source replay.

## Generated View Impact
- GV-001 [PD-001] [PD-006] workModel: `readiness/2820-immutable-release-dashboard-recovery/work-model.json` and `analysis.json` are regenerated from these authored sources through `fsgg-sdd tasks` and `analyze`; they are never hand-edited to claim implementation readiness.

## Accepted Deferrals
No accepted plan deferrals recorded.

## Planning Findings
No blocking planning findings recorded.

## Advisory Notes
- Optional Governance pointers remain compatibility facts only.

## Lifecycle Notes
- Next lifecycle action: `fsgg-sdd tasks --work 2820-immutable-release-dashboard-recovery`.
