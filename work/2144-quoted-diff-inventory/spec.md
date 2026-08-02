---
schemaVersion: 1
workId: 2144-quoted-diff-inventory
title: Quoted semantic-diff receipt
stage: specify
changeTier: tier1
status: specified
publicOrToolFacingImpact: true
---

# Quoted semantic-diff receipt Specification

Prose status: specified

## User Value
Bulk renames become reviewable, head-bound evidence instead of agent memory.

## Scope
- SB-001: Deterministic inventory, disposition receipt, host acceptance gate, and generated guidance.

## Non-Goals
- SB-002: Do not implement later lifecycle commands or Governance enforcement in this specification.

## User Stories
- US-001 (P1): As a user, I can bulk renames become reviewable, head-bound evidence instead of agent memory.

## Acceptance Scenarios
- AC-001 [US-001] [FR-001]: Given Quoted semantic-diff receipt is available, when the user exercises it, then they can bulk renames become reviewable, head-bound evidence instead of agent memory.

## Functional Requirements
- FR-001: Every classified changed literal is dispositioned and stale or unresolved receipts fail closed. (Stories: US-001; Acceptance: AC-001)

## Ambiguities
No material ambiguities recorded.

## Public Or Tool-Facing Impact
- This specification is an SDD lifecycle artifact and command-report contract input.

## Lifecycle Notes
- Next lifecycle action: `fsgg-sdd clarify --work 2144-quoted-diff-inventory`.
