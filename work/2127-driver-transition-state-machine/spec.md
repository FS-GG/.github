---
schemaVersion: 1
workId: 2127-driver-transition-state-machine
title: Driver Transition State Machine
stage: specify
changeTier: tier1
status: specified
publicOrToolFacingImpact: true
---

# Driver Transition State Machine Specification

Prose status: specified

## User Value
Drive-board hosts receive one typed next action that prevents missed wave rollovers and unsafe dispatches.

## Scope
- SB-001: Two-wave coordination planning, housekeeping receipts, review-chain/liveness validation, CLI contracts, tests, and mirrored drive-board guidance; no automatic judgement.

## Non-Goals
- SB-002: Do not implement later lifecycle commands or Governance enforcement in this specification.

## User Stories
- US-001 (P1): As a drive-board host, I receive a typed next action from current
  board facts instead of reconstructing wave transitions from memory.
- US-002 (P1): As a host, I can prove a dispatch is backed by a fresh, complete
  housekeeping chain.
- US-003 (P1): As a host, I can distinguish review-ready, resumable, and invalid
  worker/review states without parsing prose.

## Acceptance Scenarios
- AC-001 [US-001] [FR-001]: Given six active items drop to two, when the planner is
  advanced, then it requests consolidation and, after valid housekeeping, dispatches
  a three-slot second wave.
- AC-002 [US-002] [FR-002]: Given a stale or incomplete prerequisite receipt, when a
  dispatch is requested, then the planner refuses dispatch and names the required
  gate.
- AC-003 [US-003] [FR-003]: Given a review marker or worker return, when it lacks a
  valid critic/SHA/round/check state or the claim remains live without review-ready
  evidence, then the planner returns typed invalid or resume-same-worker data.
- AC-004 [US-002] [FR-004]: Given a stale claim, engine drift, pending write, or
  missing host identity, when planning advances, then it returns the corresponding
  first-class repair gate and does not present the pass as complete.

## Functional Requirements
- FR-001: The planner MUST return one typed two-wave action and make the 6-to-2 transition consolidate then dispatch a three-slot new wave after housekeeping. (covers AC-001)
- FR-002: Dispatch receipts MUST fail closed unless reconcile dry-run/apply, zero-pending flush, fresh reconcile, fresh lint/backlog, and scoped currency evidence form one fresh successful chain. (covers AC-002)
- FR-003: Review-chain and live-worker validation MUST return marker spelling, critic identity, exact SHA, ordered bounded rounds, check/acceptance readiness, and resume-same-worker states as typed data. (covers AC-003)
- FR-004: Housekeeping MUST request an identity before writes and surface stale claims, stale engine, or pending writes as explicit transition gates. (covers AC-004)

## Ambiguities
- AMB-001: The CLI command name and exact schema composition must preserve existing
  command contracts while exposing the new planner surface.
- AMB-002: Consolidation remains a human judgement; the planner needs an explicit
  input rather than an inferred objective.

## Public Or Tool-Facing Impact
- New typed CLI planning/advance data and receipt validation structures are expected.
- Drive-board guidance must remain equivalent in `.agents` and `.claude` roots.

## Lifecycle Notes
- Next lifecycle action: `fsgg-sdd clarify --work 2127-driver-transition-state-machine`.
