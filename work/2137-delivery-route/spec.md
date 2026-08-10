---
schemaVersion: 1
workId: 2137-delivery-route
title: Coordination Delivery Route Decision
stage: specify
changeTier: tier1
status: specified
publicOrToolFacingImpact: true
---

# Coordination Delivery Route Decision Specification

Prose status: specified

## User Value
Coordinators receive an explicit durable delivery route decision before implementation dispatch.

## Scope
- SB-001: versioned route receipt, fixed impact checklist, SDD work/spec binding, scheduler and intake enforcement, stale receipt handling, and skill projections.

## Non-Goals
- SB-002: fsgg-coord does not author SDD specifications or infer an agent decision.

## User Stories
- US-001 (P1): As a user, I can coordinators receive an explicit durable delivery route decision before implementation dispatch.

## Acceptance Scenarios
- AC-001 [US-001] [FR-001]: Missing, stale, unreadable, or incomplete route facts prevent dispatch; SDD-required work binds current SDD identities and gates.

## Functional Requirements
- FR-001: An agent explicitly chooses lightweight or sdd-required with reason codes and rationale; checklist facts inform but never infer or default the choice. (Stories: US-001; Acceptance: AC-001)
- FR-002: The engine validates and persists a versioned receipt containing subject ref and revision, route, identity, timestamp, reason codes, rationale, declared impacts and observed live facts. (Stories: US-001; Acceptance: AC-001)
- FR-003: Intake, Ready-to-In-progress, claim and dispatch refuse a missing, stale, unreadable or incomplete receipt. (Stories: US-001; Acceptance: AC-001)
- FR-004: For sdd-required, the receipt binds current SDD work id, canonical spec home and required pre-implementation gates; fsgg-coord consumes rather than reimplements SDD lifecycle semantics. (Stories: US-001; Acceptance: AC-001)
- FR-005: Material path, public-contract, dependency, acceptance-evidence or phase changes invalidate the receipt. (Stories: US-001; Acceptance: AC-001)
- FR-006: Intake, inspection, claim state and terminal evidence project the decision and reason codes, and the affected process skills consume the typed receipt. (Stories: US-001; Acceptance: AC-001)

## Ambiguities
No material ambiguities recorded.

## Public Or Tool-Facing Impact
- This specification is an SDD lifecycle artifact and command-report contract input.

## Lifecycle Notes
- Next lifecycle action: `fsgg-sdd clarify --work 2137-delivery-route`.
