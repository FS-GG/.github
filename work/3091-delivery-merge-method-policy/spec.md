---
schemaVersion: 1
workId: 3091-delivery-merge-method-policy
title: Typed delivery merge-method policy
stage: specify
changeTier: tier1
status: specified
publicOrToolFacingImpact: true
---

# Typed delivery merge-method policy Specification

Prose status: specified

## User Value
land an accepted exact-head pull request using one merge method the target repository actually permits

## Scope
- SB-001: typed repository merge capability observation; deterministic squash-then-rebase-then-merge selection; explicit merge_method request; zero-write refusal; CLI projection; tests

## Non-Goals
- SB-002: Do not implement later lifecycle commands or Governance enforcement in this specification.

## User Stories
- US-001 (P1): As the accountable delivery owner, I can land an accepted pull request without learning repository merge policy through a failed irreversible mutation.

## Acceptance Scenarios
- AC-001 [US-001] [FR-001]: Given squash-only policy, delivery selects squash and the exact-head request succeeds.
- AC-002 [US-001] [FR-001]: Given rebase-only or merge-only policy, delivery selects the sole allowed method.
- AC-003 [US-001] [FR-001]: Given multiple methods, selection is deterministic in squash, rebase, merge preference order.
- AC-004 [US-001] [FR-001]: Given missing, malformed, unreadable, or all-false policy, delivery refuses before any merge PUT.

## Functional Requirements
- FR-001: repository policy MUST expose three typed capability booleans and fail closed on incomplete responses. (Stories: US-001; Acceptance: AC-001)
- FR-002: selection MUST be a pure total decision over typed policy and return a typed refusal when none is allowed. (Stories: US-001; Acceptance: AC-001)
- FR-003: mergeAtHead MUST require a typed method and serialize the corresponding GitHub merge_method with the guarded head SHA. (Stories: US-001; Acceptance: AC-001)
- FR-004: the delivery handler MUST observe and select policy before calling guardedLanding and MUST preserve all existing authorization predicates. (Stories: US-001; Acceptance: AC-001)
- FR-005: the CLI repository-policy projection MUST expose the three observed merge facts. (Stories: US-001; Acceptance: AC-001)
- FR-006: tests MUST invert each capability and prove zero merge writes on refusal. (Stories: US-001; Acceptance: AC-001)

## Ambiguities
No material ambiguities recorded.

## Public Or Tool-Facing Impact
- This specification is an SDD lifecycle artifact and command-report contract input.

## Lifecycle Notes
- Next lifecycle action: `fsgg-sdd clarify --work 3091-delivery-merge-method-policy`.
