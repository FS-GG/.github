---
schemaVersion: 1
workId: 2754-roadmap-health-measures
title: Derive and score roadmap health measures
stage: plan
changeTier: tier1
status: planned
sourceSpec: work/2754-roadmap-health-measures/spec.md
sourceClarifications: work/2754-roadmap-health-measures/clarifications.md
sourceChecklist: work/2754-roadmap-health-measures/checklist.md
publicOrToolFacingImpact: true
---

# Derive and score roadmap health measures Plan

Prose status: planned

## Source Snapshot
- spec: work/2754-roadmap-health-measures/spec.md sha256:f27f5767ba4782cb660dba250017be2e23c445f1dccfdbcd3f3ba93d87f1e858 schemaVersion:1
- clarifications: work/2754-roadmap-health-measures/clarifications.md sha256:80e63e71e184eeada45e5398839a4c8e20a2a369e0ea3df3ab48c8721872bf20 schemaVersion:1
- checklist: work/2754-roadmap-health-measures/checklist.md sha256:f7b3f0284ef25dd08c63eb29702998b788d893720f60d070840034142b8d1ce2 schemaVersion:1

## Plan Scope
- Work item 2754-roadmap-health-measures is planned from the current specification, clarification, and checklist facts.
- Requirement count: 5.
- Clarification decision count: 0.
- Checklist result count: 1.

## Plan Decisions
- PD-001 [AC-001] [FR-001] [PC-001] [PM-001] [GV-001] [VO-001] complete: Implement a repository-local Python reporter that validates a canonical-digest-bound raw issue snapshot, derives weekly flow from raw timestamps, derives artifact and line trends from exact Git objects, and emits each of the seven measures with explicit `unverified`/`retired` values.
- PD-002 [AC-002] [FR-002] [VO-002] complete: Make the M0–M6 exit-predicate table the sole checkbox authority and name each gap.
- PD-003 [AC-003] [FR-003] [VO-003] complete: Evaluate M6 against both three health cycles and the bounded `.github#266`/`.github#2752`/`.github#2691` successor census.
- PD-004 [AC-004] [FR-004] [VO-004] complete: Preserve the operator's approved freeze as an attributable dated decision with scope and lift condition.
- PD-005 [AC-005] [FR-005] [VO-005] complete: Record measure 2's retirement in the roadmap with actor, date, state, and reason.

## Contract Impact
- PC-001 [PD-001] command report: `scripts/report-roadmap-health.py --format json` is a deterministic, machine-readable report containing seven named measures, their evidence window, and a legal explicit `unverified` verdict.

## Verification Obligations
- VO-001 [PD-001] [PC-001] semanticTest: Prove all seven measures and exact historical counts are derived through both direct and CLI routes.
- VO-002 [PD-002] semanticTest: Mutate asserted summaries and invalid Git boundaries and prove they fail closed.
- VO-003 [PD-003] semanticTest: Mutate raw census digest, identity, and timestamp semantics and prove they fail closed.
- VO-004 [PD-004] semanticTest: Mutate incident/census shapes, refs, dates, negative fields, and completeness types and prove they fail closed.
- VO-005 [PD-005] semanticTest: Prove `met` requires an explicitly complete typed census while incomplete absence remains `unverified`.

## Performance Intent
No performance intent is declared for this work item.

## Migration Posture
- PM-001 [PC-001] additive: The reporter is additive; the roadmap is rescored from its reported facts and preserves historical baseline prose as the comparison authority.

## Generated View Impact
- GV-001 [PD-001] workModel: Refresh the SDD readiness views after the authored plan and task evidence are current; no other generated repository projection is changed.

## Accepted Deferrals
No accepted plan deferrals recorded.

## Planning Findings
No blocking planning findings recorded.

## Advisory Notes
- Optional Governance pointers remain compatibility facts only.

## Lifecycle Notes
- Next lifecycle action: `fsgg-sdd tasks --work 2754-roadmap-health-measures`.
