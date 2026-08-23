---
schemaVersion: 1
workId: 2837-moved-head-review-writer-retirement-ledger
title: Restart review-record sealing after retiring an accepted moved-head chain
stage: specify
changeTier: tier1
status: specified
publicOrToolFacingImpact: true
---

# Restart review-record sealing after retiring an accepted moved-head chain Specification

Prose status: specified

## User Value
Keep moved-head review recovery writable and readable after an accepted old chain retires.

## Scope
- SB-001: Derive the live/retired partition before recordReview seals a new current-head initial record; preserve retired bytes; add pure, production-writer, hosted, and mutation regressions.

## Non-Goals
- SB-002: Do not change marker schemas, ordinary same-head revision continuity, review round limits, or unrelated delivery behavior.

## User Stories
- US-001 (P1): As a user, I can keep moved-head review recovery writable and readable after an accepted old chain retires.

## Acceptance Scenarios
- AC-001 [US-001] [FR-001]: Given an accepted old-head chain and a fresh authorized initial-review wait at the current head, when the real record writer seals the critic decision and the wait completes, then the new record is revision 1 with `previousDigest = null`, live review passes, and the unchanged old chain is reported only under `retiredChains`.

## Functional Requirements
- FR-001: Given an accepted review chain on an old head and an authorized initial-review wait on the new head, the real writer must emit revision 1 with previousDigest null; after wait completion the live read must pass and report the old chain only under retiredChains. (Stories: US-001; Acceptance: AC-001)
- FR-002: Ordinary same-head revision continuity, marker schemas, review round limits, and unrelated delivery behavior must remain unchanged. (Stories: US-001; Acceptance: AC-001)

## Ambiguities
No material ambiguities recorded.

## Public Or Tool-Facing Impact
- The typed review-record writer changes its revision/digest derivation for the accepted-then-moved recovery route; the serialized contract is unchanged.

## Lifecycle Notes
- Next lifecycle action: `fsgg-sdd clarify --work 2837-moved-head-review-writer-retirement-ledger`.
