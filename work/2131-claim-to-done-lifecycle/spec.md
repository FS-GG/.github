---
schemaVersion: 1
workId: 2131-claim-to-done-lifecycle
title: Claim-to-Done Lifecycle and Guarded Landing
stage: specify
changeTier: tier1
status: specified
publicOrToolFacingImpact: true
---

# Claim-to-Done Lifecycle and Guarded Landing Specification

Prose status: specified

## User Value
Workers and hosts receive one typed, fail-closed next action for each claim-to-done transition, eliminating manual correlation of delivery receipts.

## Scope
- SB-001: Lifecycle inspection, review handoff, review acceptance, guarded landing, post-merge obligations, completion, CLI contracts, GitHub reads and writes, regression tests, and pnext-item guidance.

## Non-Goals
- SB-002: Do not implement later lifecycle commands or Governance enforcement in this specification.

## User Stories
- US-001 (P1): As a user, I can workers and hosts receive one typed, fail-closed next action for each claim-to-done transition, eliminating manual correlation of delivery receipts.

## Acceptance Scenarios
- AC-001 [US-001] [FR-001]: Given Claim-to-Done Lifecycle and Guarded Landing is available, when the user exercises it, then they can workers and hosts receive one typed, fail-closed next action for each claim-to-done transition, eliminating manual correlation of delivery receipts.

## Functional Requirements
- FR-001: The lifecycle represents Claimed, Implementation, ReviewReady, ReviewActive, Accepted, Landable, MergedAwaitingObligations, Done, and terminal park states. (Stories: US-001; Acceptance: AC-001)
- FR-002: Each inspect or advance result binds item ref, live claim generation, executor identity, branch or worktree, PR, head SHA, declared paths, and board state, returning exactly one next action or a fail-closed no-verdict. (Stories: US-001; Acceptance: AC-001)
- FR-003: Review handoff verifies the canonical item branch, closing reference, declared paths, and current head before moving the board to In review. (Stories: US-001; Acceptance: AC-001)
- FR-004: Review acceptance accepts only a valid typed review chain for the current head SHA and rejects prose or stale evidence. (Stories: US-001; Acceptance: AC-001)
- FR-005: Guarded landing rechecks claim generation, head SHA, accepted review, checks, mergeability, and closing linkage before one idempotent REST merge. (Stories: US-001; Acceptance: AC-001)
- FR-006: Completion remains nonterminal until every declared release, publication, registry, dispatch, or deployment obligation has a verified receipt. (Stories: US-001; Acceptance: AC-001)
- FR-007: Completion verifies merge reachability, closed issue, Done projection, released claim, zero pending writes, and cleanup eligibility before emitting FSGG-DONE. (Stories: US-001; Acceptance: AC-001)
- FR-008: Mutating actions are idempotent and carry a freshness token so stale replay cannot merge twice or stamp the wrong lifecycle state. (Stories: US-001; Acceptance: AC-001)
- FR-009: Cleanup and follow-up routing are explicit terminal actions. (Stories: US-001; Acceptance: AC-001)

## Ambiguities
No material ambiguities recorded.

## Public Or Tool-Facing Impact
- The coordination CLI has an additive lifecycle surface and the pnext-item protocol delegates deterministic transitions to it.

## Lifecycle Notes
- Next lifecycle action: `fsgg-sdd clarify --work 2131-claim-to-done-lifecycle`.
