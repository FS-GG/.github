---
schemaVersion: 1
workId: 3308-routine-development-route
title: R0 — Adopt the smaller contract
stage: plan
changeTier: tier1
status: planned
sourceSpec: work/3308-routine-development-route/spec.md
sourceClarifications: work/3308-routine-development-route/clarifications.md
sourceChecklist: work/3308-routine-development-route/checklist.md
publicOrToolFacingImpact: true
---

# R0 — Adopt the smaller contract Plan

Prose status: planned

## Source Snapshot
- spec: work/3308-routine-development-route/spec.md sha256:97e2f8b236bcc2dd427fce6c72485b5843f286f5c772c194d15915403ee039fd schemaVersion:1
- clarifications: work/3308-routine-development-route/clarifications.md sha256:3510d1d8b3904a5f3f605029c97aadc92d0cbdee69de9fcfe1b35dcc6830a784 schemaVersion:1
- checklist: work/3308-routine-development-route/checklist.md sha256:c58f0a0c1f673a12078cc48b3838cfe820dc1d0f7a7cb308302f0bc06a540cbd schemaVersion:1

## Plan Scope
- Work item 3308-routine-development-route is planned from the current specification, clarification, and checklist facts.
- Requirement count: 2.
- Clarification decision count: 0.
- Checklist result count: 2.

## Plan Decisions
- PD-001 [AC-001] [FR-001] complete: Add an explicit `routine` delivery-route contract whose eligibility is validated from machine-readable policy and whose native PR boundary requires selected technical checks plus exact-head freshness, but none of the strict route's process-only evidence.
- PD-002 [AC-001] [FR-002] complete: Keep `strict` as the only route for publication, deployment, credentials, destructive effects, migrations or cutovers, externally enforced contracts, and all work already operating under strict authority; never infer routine eligibility from size.

## Contract Impact
- PC-001 [PD-001] command report: Version the delivery-route policy and CLI result so callers can select `routine` prospectively while existing `lightweight` and `sdd-required` records retain strict legacy meaning. Update shared skill sources and regenerate Claude/Codex twins from the same source.

## Verification Obligations
- VO-001 [PD-001] [PC-001] semanticTest: Prove routine admission succeeds without issue, claim, SDD, lifecycle, critique, feedback/receipt-cycle, or metadata-Done inputs; invert every protected-operation and changed-head predicate and observe refusal; run focused unit/CLI and skill-projection parity suites.

## Performance Intent
No performance intent is declared for this work item.

## Migration Posture
- PM-001 [PC-001] prospective: Existing route records and in-flight strict work keep their original authority. Only new explicitly admitted pilot work may use `routine`; downstream accepted-unit/release consumers remain strict until their own public contracts adopt it.

## Generated View Impact
- GV-001 [PD-001] agentGuidance: Regenerate `AGENTS.md`, `CLAUDE.md`, and both runtime `work-roadmap` skill twins from canonical `.fsgg`/skill sources, then require projection tests to show equivalent routine/strict behavior.

## Accepted Deferrals
No accepted plan deferrals recorded.

## Planning Findings
No blocking planning findings recorded.

## Advisory Notes
- Optional Governance pointers remain compatibility facts only.

## Lifecycle Notes
- Next lifecycle action: `fsgg-sdd tasks --work 3308-routine-development-route`.
