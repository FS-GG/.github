---
schemaVersion: 1
workId: 2756-review-wait-continuity
title: Durable review wait and critic continuity
stage: specify
changeTier: tier1
status: specified
publicOrToolFacingImpact: true
---

# Durable review wait and critic continuity Specification

Prose status: specified

## User Value
Review and repair can pause safely across agent lifetimes without silently losing the touch-set reservation or treating an expired lease as authorization.

## Scope
- SB-001: Add a bounded WaitReceipt bound to item, claim generation, review generation, timestamps, and evidence; make successor critics the ordinary continuation model; update both skill projections and critic routes; add focused typed tests.

## Non-Goals
- SB-002: Do not infer runtime agent liveness, add a repository-global lock, extend an active mutation lease, or redesign delivery and landing.

## User Stories
- US-001 (P1): As a user, I can review and repair can pause safely across agent lifetimes without silently losing the touch-set reservation or treating an expired lease as authorization.

## Acceptance Scenarios
- AC-001 [US-001] [FR-001]: Given a live claim enters a protocol-created review wait, when the transition succeeds, then a bounded durable receipt binds the item, claim generation, review generation, timestamps, kind, and evidence reference.
- AC-002 [US-001] [FR-002]: Given a current wait receipt and open item PR outlive the active lease, when scheduling reads the item, then its paths remain reserved while mutation still requires the current claim generation or reacquisition.
- AC-003 [US-001] [FR-003]: Given completion, cancellation, or timeout races a wait, when the transition re-reads the authority, then exactly one terminal disposition consumes or expires the receipt and timeout leaves an explicit recoverable state.
- AC-004 [US-001] [FR-004]: Given the prior critic is no longer active, when review resumes, then a newly minted successor reads the durable packet and performs a full review of the exact current head without inheriting prior clearances.
- AC-005 [US-001] [FR-005]: Given the four named concurrency and lifetime cases, when the focused regression suite runs, then stale generations and losing race transitions fail closed.

## Functional Requirements
- FR-001: Entering a protocol-created review queue emits a bounded receipt with item, claim generation, review generation, enteredAt, expiresAt, and evidenceRef. (Stories: US-001; Acceptance: AC-001)
- FR-002: A current receipt plus the open PR preserves paths while resumption requires the current claim generation or an explicit reacquisition. (Stories: US-001; Acceptance: AC-002)
- FR-003: Completion, cancellation, and bounded timeout deterministically consume or expire the receipt. (Stories: US-001; Acceptance: AC-003)
- FR-004: A successor critic inherits only durable ledger context and performs a fresh full review of the current head. (Stories: US-001; Acceptance: AC-004)
- FR-005: Focused tests cover a wait beyond the active lease, dead critic, changed claim generation, and completion racing timeout. (Stories: US-001; Acceptance: AC-005)

## Ambiguities
No material ambiguities recorded.

## Public Or Tool-Facing Impact
- `fsgg-coord review` gains a durable bounded-wait projection and ordinary successor-review semantics.
- The `independent-review` skill projections and critic route bindings describe the same typed contract.

## Lifecycle Notes
- Next lifecycle action: `fsgg-sdd clarify --work 2756-review-wait-continuity`.
