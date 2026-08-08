---
schemaVersion: 1
workId: 2134-coord-intake
title: Coord intake guarded transaction
stage: specify
changeTier: tier1
status: specified
publicOrToolFacingImpact: true
---

# Coord intake guarded transaction Specification

Prose status: specified

## User Value
Agents can safely create or reuse a schedulable issue through one idempotent intake transaction.

## Scope
- SB-001: Versioned draft validation, explicit ownership, duplicate candidates, guarded apply, coherent status projection, and a verified receipt.

## Non-Goals
- SB-002: Do not implement later lifecycle commands or Governance enforcement in this specification.

## User Stories
- US-001 (P1): As a user, I can agents can safely create or reuse a schedulable issue through one idempotent intake transaction.

## Acceptance Scenarios
- AC-001 [US-001] [FR-001]: Given Coord intake guarded transaction is available, when the user exercises it, then they can agents can safely create or reuse a schedulable issue through one idempotent intake transaction.

## Functional Requirements
- FR-001: The CLI validates a draft without writes and applies it at most once per draft id while returning a fresh receipt of issue and board state. (Stories: US-001; Acceptance: AC-001)
- FR-002: The CLI refuses ambiguous ownership, unusable paths, unreadable duplicate search, invalid blocked dependencies, and Ready results with live blockers. (Stories: US-001; Acceptance: AC-001)
- FR-003: The CLI reports open and closed duplicate candidates while requiring an explicit reuse or create disposition. (Stories: US-001; Acceptance: AC-001)

## Ambiguities
No material ambiguities recorded.

## Public Or Tool-Facing Impact
- This specification is an SDD lifecycle artifact and command-report contract input.

## Lifecycle Notes
- Next lifecycle action: `fsgg-sdd clarify --work 2134-coord-intake`.
