---
schemaVersion: 1
workId: 2858-verifypaths-cross-repo-subject
title: Verifypaths Cross Repo Subject
stage: plan
changeTier: tier1
status: planned
sourceSpec: work/2858-verifypaths-cross-repo-subject/spec.md
sourceClarifications: work/2858-verifypaths-cross-repo-subject/clarifications.md
sourceChecklist: work/2858-verifypaths-cross-repo-subject/checklist.md
publicOrToolFacingImpact: true
---

# Verifypaths Cross Repo Subject Plan

Prose status: planned

## Source Snapshot
- spec: work/2858-verifypaths-cross-repo-subject/spec.md sha256:65c0eb0a7c9801a0e7f50749bc1f6c94fcc885e00f24c611c33bf190dee86480 schemaVersion:1
- clarifications: work/2858-verifypaths-cross-repo-subject/clarifications.md sha256:ec259e4aa43af4d4e276ce361d2757e6cb4ff70658faa6772419ca5b6eebb870 schemaVersion:1
- checklist: work/2858-verifypaths-cross-repo-subject/checklist.md sha256:53832fdc7842bb9b26839d745046da9333d116c7863bbebd8c04449288a949b0 schemaVersion:1

## Plan Scope
- Work item 2858-verifypaths-cross-repo-subject is planned from the current specification, clarification, and checklist facts.
- Requirement count: 5.
- Clarification decision count: 1.
- Checklist result count: 5.

## Plan Decisions
- PD-001 [AC-001] [FR-001] complete: Parse at most one named issue from a positional ref or `--issue` before branch resolution; a named issue bypasses the branch and closing-ref lookup. Refuse two explicit subjects.
- PD-002 [AC-002] [AC-005] [FR-002] [DEC-001] complete: Permit cross-repository comparison only when the named issue is in the current owner's `.github` repository and the caller explicitly supplies the PR `--repo`. Keep an implicit cross-repository closing ref on the existing SKIP path and refuse other explicit straddles.
- PD-003 [AC-003] [FR-003] complete: Print the PR owner/repository and the full Coordination issue ref beside `issue-body Paths:` on OK and DRIFT. Retain the existing marker vocabulary consumed by the drift gate.
- PD-004 [AC-004] [AC-007] [FR-004] [DEC-001] complete: Preserve same-repo `--issue` inference when `--repo` is absent. Refuse an undeclared or partly unmatchable cross-repository `Paths:` declaration before OK or DRIFT, even under `--warn`. For a cross-repository comparison, classify files against the authored `Paths:` alone: the issue repository's generated roster and SDD package cannot exempt files in the target PR repository.
- PD-005 [AC-006] [AC-007] [FR-005] complete: Add HTTP parity cases for the branch/subject inversion, cross-repository OK and DRIFT, ambiguous and undeclared refusal, and unchanged conventional-branch and implicit-closing behavior. Add a command-boundary inversion with a known `.github` generated path that must DRIFT in the target repository.

## Contract Impact
- PC-001 [PD-001] [PD-002] [PD-003] command report: `verify-paths` accepts an optional positional issue ref alongside the existing `--issue` form; an explicit Coordination issue plus PR `--repo` now reaches OK or DRIFT, and verdict text identifies both repositories and issue-body `Paths:`. Existing `FSGG-PATHS` markers and exit meanings remain the gate contract. Package publication belongs to a later coherent-set release after the current Kit release source is settled.

## Verification Obligations
- VO-001 [PD-001] [PD-002] [PD-003] [PD-004] [PD-005] [PC-001] semanticTest: Build the Release engine with locked restore; run the compiled-engine parity suite and focused final-head probes for explicit Coordination OK/DRIFT, ambiguous refusal, and implicit cross-repository closing SKIP. Record actual run results and any remaining SDD or hosted gates separately.

## Performance Intent
No performance intent is declared for this work item.

## Migration Posture
- PM-001 [PC-001] compatible: No persistent command schema or cache migration is required. Callers that already use `--issue` in the same repository keep the same verdict behavior; only explicitly named Coordination subjects with explicit target repositories gain the cross-repository route.

## Generated View Impact
- GV-001 [PD-001] workModel: Generate and check this work item's SDD work model and analysis from the authored package; do not treat local source tests as a protected claim, review, or delivery receipt.

## Accepted Deferrals
No accepted plan deferrals recorded.

## Planning Findings
No blocking planning findings recorded.

## Advisory Notes
- Optional Governance pointers remain compatibility facts only.

## Lifecycle Notes
- Next lifecycle action: `fsgg-sdd tasks --work 2858-verifypaths-cross-repo-subject`.
