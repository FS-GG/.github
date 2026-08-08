---
schemaVersion: 1
workId: 2137-delivery-route
title: Delivery Route
stage: plan
changeTier: tier1
status: planned
sourceSpec: work/2137-delivery-route/spec.md
sourceClarifications: work/2137-delivery-route/clarifications.md
sourceChecklist: work/2137-delivery-route/checklist.md
publicOrToolFacingImpact: true
---

# Delivery Route Plan

Prose status: planned

## Source Snapshot
- spec: work/2137-delivery-route/spec.md sha256:3db131124aef8ce0eee3ca78237f4c8715c5bab094061f68b7204edd55d05848 schemaVersion:1
- clarifications: work/2137-delivery-route/clarifications.md sha256:b5f694b07b287d21f9458e256989e2637169d0367fc0534739d926a242c85419 schemaVersion:1
- checklist: work/2137-delivery-route/checklist.md sha256:3ee1f53dff7c781822f33e75c61c9a907116eaa180e611b65062521823b3af26 schemaVersion:1

## Plan Scope
- Work item 2137-delivery-route is planned from the current specification, clarification, and checklist facts.
- Requirement count: 6.
- Clarification decision count: 0.
- Checklist result count: 6.

## Plan Decisions
- PD-001 [AC-001] [FR-001] complete: Model the fixed checklist and explicit two-route agent decision in Core; reject absent route, identity, rationale, reasons or evidence instead of scoring facts into a route.
- PD-002 [AC-001] [FR-002] complete: Add a versioned receipt codec bound to the live item revision and persist/read it through the coordination client.
- PD-003 [AC-001] [FR-003] complete: Enrich scheduling and intake facts with the route validation result and fail closed before a claim or dispatch state transition.
- PD-004 [AC-001] [FR-004] complete: Validate sdd-required receipts against fsgg-sdd work id, spec home and current pre-implementation readiness receipt without duplicating SDD lifecycle evaluation.
- PD-005 [AC-001] [FR-005] complete: Fingerprint declared paths, impacts, dependencies, evidence and phases into the receipt subject revision so a changed subject invalidates it.
- PD-006 [AC-001] [FR-006] complete: Project the receipt and reason codes through intake, inspection, delivery state and mirrored process skills; cover positive and fail-closed fixtures.

## Contract Impact
- PC-001 [PD-001] command report: fsgg-coord gains a versioned delivery-route receipt command/report surface; unknown additive receipt fields remain ignorable while malformed or incomplete required fields refuse.

## Verification Obligations
- VO-001 [PD-001] [PC-001] semanticTest: Run Core/CLI focused fixtures proving missing, unreadable, stale, malformed, automatic-choice and invalid-SDD-binding receipts refuse while a current explicit receipt permits the intended transition; invert each new gate.

## Performance Intent
No performance intent is declared for this work item.

## Migration Posture
- PM-001 [PC-001] additive: Existing items are deliberately parked at the route checkpoint until an agent records a current receipt; no implicit legacy lightweight migration is permitted.

## Generated View Impact
- GV-001 [PD-001] workModel: Refresh readiness and all three process-skill projections so dispatchers consume the receipt rather than prose heuristics.

## Accepted Deferrals
No accepted plan deferrals recorded.

## Planning Findings
No blocking planning findings recorded.

## Advisory Notes
- Optional Governance pointers remain compatibility facts only.

## Lifecycle Notes
- Next lifecycle action: `fsgg-sdd tasks --work 2137-delivery-route`.
