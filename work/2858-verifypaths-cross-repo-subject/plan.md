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
- spec: work/2858-verifypaths-cross-repo-subject/spec.md sha256:12c1d35fca9dfc590e4f1fd698b8bb87d4d4fb2a3f444772e86423dfb62a8f73 schemaVersion:1
- clarifications: work/2858-verifypaths-cross-repo-subject/clarifications.md sha256:ec259e4aa43af4d4e276ce361d2757e6cb4ff70658faa6772419ca5b6eebb870 schemaVersion:1
- checklist: work/2858-verifypaths-cross-repo-subject/checklist.md sha256:402b496eb03edf58701730013d52e54b880dd7d0bd0bf9f8625ce3da6bf3b445 schemaVersion:1

## Plan Scope
- Work item 2858-verifypaths-cross-repo-subject is planned from the current specification, clarification, and checklist facts.
- Requirement count: 5.
- Clarification decision count: 1.
- Checklist result count: 5.

## Plan Decisions
- PD-001 [AC-001] [FR-001] complete: Parse at most one named issue from a positional ref or `--issue` before branch resolution; a named issue bypasses the branch and closing-ref lookup. Refuse two explicit subjects.
- PD-002 [AC-002] [AC-005] [FR-002] [DEC-001] complete: Permit cross-repository comparison only when the named issue is in the current owner's `.github` repository and the caller explicitly supplies the PR `--repo`. Keep an implicit cross-repository closing ref on the existing SKIP path and refuse other explicit straddles.
- PD-003 [AC-003] [FR-003] complete: Print the PR owner/repository and the full Coordination issue ref beside `issue-body Paths:` on OK and DRIFT. Retain the existing marker vocabulary consumed by the drift gate.
- PD-004 [AC-004] [FR-004] [DEC-001] complete: Refuse a named Coordination subject without an explicit PR repository, and refuse an undeclared or partly unmatchable cross-repository `Paths:` declaration before OK or DRIFT. Do not let `--warn` turn a refusal green.
- PD-005 [AC-006] [FR-005] complete: Add HTTP parity cases for the branch/subject inversion, cross-repository OK and DRIFT, ambiguous and undeclared refusal, and unchanged conventional-branch and implicit-closing behavior.

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
