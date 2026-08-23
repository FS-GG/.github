---
schemaVersion: 1
workId: 2738-one-dependency-edge
title: One typed dependency edge authority
stage: plan
changeTier: tier1
status: planned
sourceSpec: work/2738-one-dependency-edge/spec.md
sourceClarifications: work/2738-one-dependency-edge/clarifications.md
sourceChecklist: work/2738-one-dependency-edge/checklist.md
publicOrToolFacingImpact: true
---

# One typed dependency edge authority Plan

Prose status: planned

## Source Snapshot
- spec: work/2738-one-dependency-edge/spec.md sha256:dd53e45a04e12fa97622aa8410c45e0d748ac6050d3e467b9475f2c42b71a048 schemaVersion:1
- clarifications: work/2738-one-dependency-edge/clarifications.md sha256:b4897f5f7b564a781516861329a865ed3899e11b980ac3fa413f684f3168fe93 schemaVersion:1
- checklist: work/2738-one-dependency-edge/checklist.md sha256:06edf38359ed8dd7528fca06e5862646e08014b8f55c3d09dc5b29388d16ecfa schemaVersion:1

## Plan Scope
- Work item 2738-one-dependency-edge is planned from the current specification, clarification, and checklist facts.
- Requirement count: 6.
- Clarification decision count: 0.
- Checklist result count: 6.

## Plan Decisions
- PD-001 [AC-001] [FR-001] complete: Treat the Projects-v2 `Blocked by` field as the sole typed dependency-edge authority; body prose never grants or withholds `Ready`.
- PD-002 [AC-001] [FR-002] complete: Introduce one revision-bound dependency-edge observation consumed by intake, scheduling, coherent blocked writes, reconciliation, and lint projections.
- PD-003 [AC-001] [FR-003] complete: Thread the observation revision into the intake Ready mutation so a changed board revision produces `Stale` and requires a fresh read.
- PD-004 [AC-001] [FR-004] complete: Keep `Blocked on: human/decision` and `Blocked on: human/action` parsing in `HumanBlock`; these sentinels are distinct from machine dependency edges.
- PD-005 [AC-001] [FR-005] complete: Continue generating `Blocked by:` prose from typed intake data for human readability, but remove all dependency decisions based on parsing that prose.
- PD-006 [AC-001] [FR-006] complete: Retire `blockedByBodyDivergence` after tests enumerate empty, live, unreadable, stale, and legacy-divergent states and prove only the column affects dependency meaning.

## Contract Impact
- PC-001 [PD-001] internal board contract: dependency observations expose value plus board revision; intake Ready application accepts only an observation current for the mutation snapshot. Existing intake JSON and generated body prose remain compatible.

## Verification Obligations
- VO-001 [PD-001] [PC-001] semanticTest: Add focused Core and CLI tests for all edge states, mutate the live-column Ready guard to ignore the dependency and observe the focused suite red, then run the affected project suites and build.

## Performance Intent
No performance intent is declared for this work item.

## Migration Posture
- PM-001 [PC-001] compatibilityProjection: Existing `Blocked by:` body lines remain readable prose and may be regenerated from typed fields; they never become an edge source. Existing human-block sentinels remain unchanged.

## Generated View Impact
- GV-001 [PD-001] workModel: Regenerate the SDD work model and analysis after authoring these decisions; readiness must report `implementationReady` before code changes begin.

## Accepted Deferrals
No accepted plan deferrals recorded.

## Planning Findings
No blocking planning findings recorded.

## Advisory Notes
- Optional Governance pointers remain compatibility facts only.

## Lifecycle Notes
- Next lifecycle action: `fsgg-sdd tasks --work 2738-one-dependency-edge`.
