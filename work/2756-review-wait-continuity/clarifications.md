---
schemaVersion: 1
workId: 2756-review-wait-continuity
title: Durable review wait and critic continuity
stage: clarify
changeTier: tier1
status: clarified
sourceSpec: work/2756-review-wait-continuity/spec.md
publicOrToolFacingImpact: true
---

# Durable review wait and critic continuity Clarifications

## Source Specification
- work/2756-review-wait-continuity/spec.md

## Clarification Questions
- CQ-001: Which durable authority stores a review wait?
- CQ-002: Does same-critic continuity remain the ordinary repair route?
- CQ-003: What authorizes mutation after a wait?

## Answers
- CQ-001: One machine-readable wait receipt is persisted with the item's review history; the typed parser is its sole authority.
- CQ-002: No. Measured host behavior despawned the critic before repair returned in five of five cited chains, so a freshly minted successor is the ordinary route.
- CQ-003: The actor proves the bound claim generation is still current or reacquires a new claim generation before mutation. Expiry never revives the old generation.

## Decisions
- DEC-001: `WaitReceipt` binds item, claim generation, review generation, kind, `enteredAt`, `expiresAt`, and `evidenceRef`; it does not extend a lease.
- DEC-002: The current receipt plus an open PR preserves the touch-set reservation during the bounded wait.
- DEC-003: Completion, cancellation, and timeout consume or expire the receipt idempotently; timeout projects an explicit recoverable state.
- DEC-004: A successor inherits only durable ledger context and re-derives a full review of the exact current head; it inherits no prior clearance.

## Accepted Deferrals
No accepted deferrals recorded.

## Remaining Ambiguity
No blocking ambiguity remains.

## Lifecycle Notes
- Next lifecycle action: `fsgg-sdd checklist --work 2756-review-wait-continuity`.
