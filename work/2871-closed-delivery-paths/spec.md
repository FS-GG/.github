---
schemaVersion: 1
workId: 2871-closed-delivery-paths
title: Preserve declared paths across closed-item delivery
stage: specify
changeTier: tier1
status: specified
publicOrToolFacingImpact: true
---

# Preserve declared paths across closed-item delivery Specification

Prose status: specified

## User Value
Operators can complete an already-merged item without reopening it merely to
make a valid `Paths:` declaration visible to delivery.

## Scope
- SB-001: src/FS.GG.Coord.Cli.Lifecycle/LiveHandlers.fs and tests/coord-engine-e2e/writes.sh.

## Non-Goals
- SB-002: Do not change scheduling, board-scan touch-set semantics, or the
  `Paths:` grammar.
- SB-003: Do not relax fail-closed behavior for unread issue bodies, claims,
  pull requests, or post-merge verification.
- SB-004: Do not repair the separate abbreviated-versus-full merge SHA receipt
  verifier mismatch.

## User Stories
- US-001 (P1): As a release operator, I can complete delivery after the owning
  issue closes and retain the exact touch-set authority that was present while
  it was open.
- US-002 (P1): As a caller, I receive an explicit unread-fact failure when the
  issue body cannot be read, rather than a false claim that no declaration
  exists.

## Acceptance Scenarios
- AC-001 [US-001] [FR-001]: Given one unchanged issue body containing a
  matchable `Paths:` line, when delivery reads it before and after closure,
  then the same declared tokens reach the delivery snapshot in both states.
- AC-002 [US-002] [FR-002]: Given the authoritative body read fails, when
  delivery constructs its snapshot, then it returns an unread/no-verdict
  diagnosis and does not emit the no-`Paths:` diagnosis.
- AC-003 [US-002] [FR-003]: Given the authoritative body is read successfully
  and contains no declaration, when delivery constructs its snapshot, then it
  retains the existing undeclared/no-`Paths:` diagnosis.
- AC-004 [US-001] [FR-004]: Given an open claimed item, when delivery executes
  its existing guarded route, then its authorization and fail-closed outcomes
  remain unchanged.

## Functional Requirements
- FR-001: Given one issue body with a matchable Paths declaration, delivery projects the identical declared path tokens while the issue is open and after it is closed. (Stories: US-001; Acceptance: AC-001)
- FR-002: An unread issue-body or projection read produces an unread no-verdict reason and never the definite no-Paths diagnosis. (Stories: US-001; Acceptance: AC-001)
- FR-003: A genuinely absent Paths declaration still produces the existing undeclared no-verdict diagnosis. (Stories: US-001; Acceptance: AC-001)
- FR-004: Existing open-item delivery, claim authorization, and fail-closed behavior remain unchanged. (Stories: US-001; Acceptance: AC-001)

## Ambiguities
No material ambiguities recorded.

## Public Or Tool-Facing Impact
- This repairs the live `fsgg-coord delivery` contract. It does not add or
  rename commands, flags, JSON fields, or package APIs.

## Lifecycle Notes
- Next lifecycle action: `fsgg-sdd clarify --work 2871-closed-delivery-paths`.
