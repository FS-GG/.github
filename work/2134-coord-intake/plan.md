---
schemaVersion: 1
workId: 2134-coord-intake
title: Coord intake guarded transaction
stage: plan
changeTier: tier1
status: planned
sourceSpec: work/2134-coord-intake/spec.md
sourceClarifications: work/2134-coord-intake/clarifications.md
sourceChecklist: work/2134-coord-intake/checklist.md
publicOrToolFacingImpact: true
---

# Coord intake guarded transaction Plan

Prose status: planned

## Source Snapshot
- spec: work/2134-coord-intake/spec.md sha256:71da2b539103e8ea78b8ab310ece030e4e5f20e45d90f9b3491629b848f8921f schemaVersion:1
- clarifications: work/2134-coord-intake/clarifications.md sha256:4d628a13317ab6613cd87e34091bd83a76a886160909e15efd050ddbdbfbee7d schemaVersion:1
- checklist: work/2134-coord-intake/checklist.md sha256:081594cb67421840f1f0b552950ef6440d650f33f78a10821ca99f684b4ef83d schemaVersion:1

## Plan Scope
- Work item 2134-coord-intake is planned from the current specification, clarification, and checklist facts.
- Requirement count: 3.
- Clarification decision count: 0.
- Checklist result count: 3.

## Plan Decisions
- PD-001 [AC-001] [FR-001] complete: Model intake as a versioned Core draft and pure validation result; keep all parse, path, status, and disposition checks side-effect free so `intake validate` is a deterministic preflight.
- PD-002 [AC-001] [FR-002] complete: Make the GitHub application layer own complete duplicate reads and the idempotency key, then apply create/reuse plus board projection as one guarded sequence with a fresh readback receipt.
- PD-003 [AC-001] [FR-003] complete: Expose `intake validate` and `intake apply` through the CLI with explicit owner and create/reuse inputs; reject ambiguity and unreadable live facts rather than choosing on behalf of the caller.

## Contract Impact
- PC-001 [PD-001] command report: Add a versioned `fsgg.coord.intake/v1` draft and receipt contract. CLI JSON is the public machine surface; text output is derived from that receipt and does not invent an owner, duplicate disposition, or projection freshness.

## Verification Obligations
- VO-001 [PD-001] [PC-001] semanticTest: Add transport-backed tests proving validate performs zero writes, apply is idempotent across interrupted projection, duplicate reads fail closed, invalid Ready/Blocked states refuse, and the receipt is fresh. Invert the write guards and assert the focused suites go red.

## Performance Intent
No performance intent is declared for this work item.

## Migration Posture
- PM-001 [PC-001] diagnoseOnly: This is additive: existing `add`, `set-field`, and filing skills retain their behavior. Update p-add/padd-item to route new transaction-capable filing through intake without silently migrating older callers or accepting a missing draft version.

## Generated View Impact
- GV-001 [PD-001] workModel: Regenerate readiness only after the authored plan, tasks, and evidence are current; generated agent guidance must describe the intake transaction as the canonical filing route and must not duplicate the Core contract prose.

## Accepted Deferrals
No accepted plan deferrals recorded.

## Planning Findings
No blocking planning findings recorded.

## Advisory Notes
- Optional Governance pointers remain compatibility facts only.

## Lifecycle Notes
- Next lifecycle action: `fsgg-sdd tasks --work 2134-coord-intake`.
