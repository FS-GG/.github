---
schemaVersion: 1
workId: 2127-driver-transition-state-machine
title: Driver Transition State Machine
stage: plan
changeTier: tier1
status: planned
sourceSpec: work/2127-driver-transition-state-machine/spec.md
sourceClarifications: work/2127-driver-transition-state-machine/clarifications.md
sourceChecklist: work/2127-driver-transition-state-machine/checklist.md
publicOrToolFacingImpact: true
---

# Driver Transition State Machine Plan

Prose status: planned

## Source Snapshot
- spec: work/2127-driver-transition-state-machine/spec.md sha256:464df339e80b5fe8500a353eccb69e38d03c6420576e074754b671a20f97291b schemaVersion:1
- clarifications: work/2127-driver-transition-state-machine/clarifications.md sha256:ad9c5d3dbd4e19246833bdb370752e443384a645fd293fdf9162c30a984c011e schemaVersion:1
- checklist: work/2127-driver-transition-state-machine/checklist.md sha256:cb1b5f79eff7b8b66537e43354399073e9bbd9e0c79209faac5ef879b58407b4 schemaVersion:1

## Plan Scope
- Add a pure Core planner that maps a typed snapshot plus explicit consolidation
  judgement to exactly one action: continue, consolidate, housekeeping gate, engine
  repair, resume worker, or sized dispatch.
- Add typed receipt/review-chain validators and a CLI projection while retaining
  existing commands.
- Update equivalent Codex/Claude drive-board guidance and regression tests.

## Plan Decisions
- PD-001 [AC-001] [FR-001] complete: Model active claims across waves, reserve two
  critic slots, and emit a consolidate action at activeItems <= 3; only a fresh
  successful chain may turn that into a sized (three-slot) next-wave dispatch.
- PD-002 [AC-002] [FR-002] complete: Bind dispatch eligibility to one timestamped
  receipt chain covering reconcile dry-run/apply, zero pending writes, fresh
  reconcile, lint/backlog inventory, and scoped engine currency; reject stale or
  incomplete chains with the first unmet gate.
- PD-003 [AC-003] [FR-003] complete: Represent review marker spelling, critic,
  SHA, ordered capped rounds, checks, host acceptance, claim liveness, and
  review-ready evidence in typed validation results.
- PD-004 [AC-004] [FR-004] complete: Make missing identity, stale claims, stale
  engine, and queued writes explicit housekeeping transitions before any write or
  dispatch is presented as complete.

## Contract Impact
- PC-001 [PD-001] command report: Add a compatibility-preserving `driver` planning
  command and typed JSON action/receipt/review validation payloads.

## Verification Obligations
- VO-001 [PD-001] [PC-001] semanticTest: Cover 6-to-2 rollover, stale claim,
  missing identity, invalid marker, queued write, stale engine, fresh-triage gate,
  and live-claim no-PR/tests-running resume cases in Core and CLI tests.

## Performance Intent
No performance intent is declared for this work item.

## Migration Posture
- PM-001 [PC-001] additive: Preserve existing command behavior; the new planner is
  opt-in and its missing/freshness data fails closed rather than guessing legacy state.

## Generated View Impact
- GV-001 [PD-001] workModel: Refresh SDD readiness and mirror equivalent
  drive-board instructions into both supported agent roots.

## Accepted Deferrals
No accepted plan deferrals recorded.

## Planning Findings
No blocking planning findings recorded.

## Advisory Notes
- Optional Governance pointers remain compatibility facts only.

## Lifecycle Notes
- Next lifecycle action: `fsgg-sdd tasks --work 2127-driver-transition-state-machine`.
