---
schemaVersion: 1
workId: 2738-one-dependency-edge
title: One typed dependency edge authority
stage: specify
changeTier: tier1
status: specified
publicOrToolFacingImpact: true
---

# One typed dependency edge authority Specification

Prose status: specified

## User Value
A single revision-bound typed dependency-edge fact governs every consumer while body prose remains projection-only, and conditional Ready projection fails closed when the mutation authority cannot compare that revision atomically.

## Scope
- SB-001: Intake Ready gating, scheduler/coherent write consumers, reconciliation, lint retirement, projection writes, legacy divergent prose, and human-block sentinel preservation within the declared paths.

## Non-Goals
- SB-002: Do not implement later lifecycle commands or Governance enforcement in this specification.

## User Stories
- US-001 (P1): As a user, I can A single revision-bound typed dependency-edge fact governs every consumer while body prose remains projection-only.

## Acceptance Scenarios
- AC-001 [US-001] [FR-001]: Given One typed dependency edge authority is available, when the user exercises it, then they can A single revision-bound typed dependency-edge fact governs every consumer while body prose remains projection-only.
- AC-002 [US-001] [FR-003]: Given an edge-free revision-bound observation, when intake reaches the Ready write on GitHub Projects v2, then it reports unsupported atomic mutation authority and sends no `Status=Ready` mutation; a revision mismatch reports Stale, and the transition requires an explicit manual authority or a CAS-capable broker.

## Functional Requirements
- FR-001: A reused issue with a live Projects-v2 Blocked by column dependency is refused Ready regardless of whether its body contains, omits, or contradicts Blocked by prose. (Stories: US-001; Acceptance: AC-001)
- FR-002: Empty, live, unreadable, stale-revision, and divergent legacy body states are enumerated across every dependency-edge consumer. (Stories: US-001; Acceptance: AC-001)
- FR-003: A board revision change observed before mutation returns Stale and forces re-derivation. When the Projects backend cannot atomically compare the expected revision at its field-mutation boundary, conditional intake Ready fails closed with an explicit unsupported-authority diagnostic and sends no `Status=Ready` mutation; Ready then requires a separate explicit manual authority or CAS-capable broker. (Stories: US-001; Acceptance: AC-002)
- FR-004: Blocked on: human/decision and Blocked on: human/action remain distinct scheduling sentinels. (Stories: US-001; Acceptance: AC-001)
- FR-005: The Blocked by body line is generated for readability and never parsed as edge authority. (Stories: US-001; Acceptance: AC-001)
- FR-006: The blockedByBodyDivergence lint retires only after no consumer can assign dependency meaning to body prose. (Stories: US-001; Acceptance: AC-001)

## Ambiguities
No material ambiguities recorded.

## Public Or Tool-Facing Impact
- This specification is an SDD lifecycle artifact and command-report contract input.

## Lifecycle Notes
- Next lifecycle action: `fsgg-sdd clarify --work 2738-one-dependency-edge`.
