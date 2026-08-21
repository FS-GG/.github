---
schemaVersion: 1
workId: 2797-review-escalation-claim-turnover
title: Review Escalation Claim Turnover
stage: specify
changeTier: tier1
status: specified
publicOrToolFacingImpact: true
---

# Review Escalation Claim Turnover Specification

Prose status: specified

## User Value
An exhausted ordinary review chain can enter its one bounded repair phase after the original claim is released and a fresh claimant takes ownership.

## Scope
- SB-001: Structured escalation authority across claim turnover only; no round four, no confirmation/pass/acceptance authority transfer, and no consumer-history rewrite.

## Non-Goals
- SB-002: Do not implement later lifecycle commands or Governance enforcement in this specification.

## User Stories
- US-001 (P1): As a board driver, I can append the missing structured escalation after valid ordinary exhaustion even though the original claim was released, so the item can enter its one bounded repair phase without rewriting history.

## Acceptance Scenarios
- AC-001 [US-001] [FR-001]: Given a valid initial decision and confirmation rounds 1, 2, and 3 whose exact round-3 wait completed under the released claim, when a fresh repair-phase claimant records an escalation for the same item, PR, head, review generation, and digest, then exactly one structured escalation revision is appended.
- AC-002 [US-001] [FR-002]: Given the replacement claim is current, when it attempts a confirmation, pass, acceptance, round four, or any non-escalation decision on the exhausted PR, then the writer refuses before any mutation.
- AC-003 [US-001] [FR-003]: Given a wrong item, PR, head, round, digest, incomplete or malformed wait chain, missing ordinary-exhaustion evidence, duplicate escalation, or non-current/non-fresh claimant, when the production writer validates the request, then it refuses before any GitHub write.
- AC-004 [US-001] [FR-004]: Given the valid changed-claim escalation has been appended, when review state is projected, then ordinary exhaustion and repair-phase entry are explicit and no round-4 repair action is proposed.
- AC-005 [US-001] [FR-005]: Given the focused pure and production-writer suites, when the valid changed-claim route and each required refusal are mutated, then the exact mutant fails while the unmodified route passes.
- AC-006 [US-001] [FR-006]: Given the producer source change is merged, when release freshness is evaluated, then a coherent engine release/install obligation is filed or discharged so the S.I.R. consumer can reconcile its existing ledger mechanically.

## Functional Requirements
- FR-001: The review writer MUST authorize exactly one escalation across claim turnover only after a valid initial plus confirmation rounds 1, 2, and 3 chain, completed exact round-3 wait, ordinary-exhaustion evidence, and fresh current repair-phase claim all validate. (Stories: US-001; Acceptance: AC-001)
- FR-002: Replacement-claim authority MUST be escalation-only for the exhausted PR and MUST NOT authorize another confirmation, pass, acceptance, or round four. (Stories: US-001; Acceptance: AC-002)
- FR-003: The writer MUST refuse wrong item, PR, head, round, digest, malformed or incomplete wait chain, absent exhaustion evidence, duplicate escalation, and stale, non-current, or non-fresh claimant before writes. (Stories: US-001; Acceptance: AC-003)
- FR-004: Review projection MUST represent ordinary exhaustion and repair-phase entry without proposing a fourth ordinary repair round. (Stories: US-001; Acceptance: AC-004)
- FR-005: Focused pure and production-writer tests MUST reproduce the changed-claim route and demonstrate fail-before/pass-after mutations for every acceptance boundary. (Stories: US-001; Acceptance: AC-005)
- FR-006: A coherent released engine MUST make the repaired producer behavior installable by the blocked S.I.R. consumer without manual history edits. (Stories: US-001; Acceptance: AC-006)

## Ambiguities
- AMB-001: Which existing durable facts are sufficient to distinguish a valid post-turnover escalation from unauthorized claim replacement?
- AMB-002: Where should the narrow authority decision live so pure projection and the production writer cannot drift?
- AMB-003: How does the projection expose exhaustion without manufacturing round four?

## Public Or Tool-Facing Impact
- `fsgg-coord review record` gains one narrow changed-claim escalation route derived from existing durable review-wait and decision ledgers.
- The review-state projection gains explicit ordinary-exhaustion/repair-phase semantics and retains both established repair ceilings.

## Lifecycle Notes
- Next lifecycle action: `fsgg-sdd clarify --work 2797-review-escalation-claim-turnover`.
