---
schemaVersion: 1
workId: 2772-force-claim-transition
title: Atomic and recoverable forced-claim transition
stage: specify
changeTier: tier1
status: specified
publicOrToolFacingImpact: true
---

# Atomic and recoverable forced-claim transition Specification

Prose status: specified

## User Value
An operator recovering an item with `claim --force` receives an interruption-safe transition and a
truthful result: accepted work remains represented by at least one marker, and the observed post-state
states whether retry is safe.

## Scope
- SB-001: Reorder forced claim from delete-then-create to create-elect-then-cleanup while retaining the
  existing comment-order election as the single ownership authority.
- SB-002: Represent the observed pre/post marker census and cleanup state sufficiently for the caller to
  distinguish an old holder that still stands, a replacement that won, a deterministic two-marker state,
  no-holder anomaly, and an unreadable post-state.
- SB-003: Render failure-specific, actionable diagnostics and retry authority from the observed state.
- SB-004: Fault-inject both multi-write boundaries and pin the authoritative marker census and outcome.

## Non-Goals
- SB-005: Do not add another lock predicate, phase store, board field, or lease rule.
- SB-006: Do not change ordinary claim, renewal, stale-marker collection, twin, impersonation, or
  comment-order winner semantics.
- SB-007: Do not make courtesy notices part of claim ownership or let a notice failure roll back a won
  replacement marker.

## User Stories
- US-001 (P1): As a recovery operator, I can force-claim a held item without a transport interruption
  leaving the row with zero claim markers.
- US-002 (P1): As an automation caller, I receive a typed post-state that determines whether retry is
  authorized instead of interpreting a generic non-zero exit.
- US-003 (P2): As a displaced worker, I receive an accurate notice only after the replacement capability
  has been established and the old marker is actually removed.

## Acceptance Scenarios
- AC-001 [US-001] [FR-001]: Given one live foreign holder, when replacement marker creation fails, then
  the old marker remains, no delete is attempted, and the result says the old holder still stands.
- AC-002 [US-001] [FR-002]: Given a posted replacement that wins comment-order election, when deletion of
  the old marker fails, then both markers remain, the replacement marker is retained, and the result
  identifies deterministic cleanup as the next action.
- AC-003 [US-002] [FR-003]: Given any forced-claim completion or interruption, when the command returns,
  then its outcome distinguishes replacement won, old holder stands, deterministic two-marker cleanup,
  no holder, and unreadable post-state; retry is authorized only by the corresponding observed state.
- AC-004 [US-003] [FR-004]: Given replacement election succeeds and old-marker deletion succeeds, when
  cleanup completes, then the displaced-worker notice names the old and new worker and the claim receipt
  reports a steal.
- AC-005 [US-001] [FR-005]: Given ordinary, free-item, twin, impersonation, or stale-marker inputs, when
  claim runs, then existing election and refusal behavior remains unchanged.

## Functional Requirements
- FR-001: Forced claim MUST post the replacement marker before attempting to delete any live foreign marker, then use the unchanged comment-order election after cleanup. (covers AC-001)
- FR-002: A cleanup failure MUST retain the posted replacement marker and MUST NOT report the operation as an ordinary loss or as though nothing happened. (covers AC-002)
- FR-003: The result MUST distinguish old holder standing, replacement won, deterministic cleanup required, no holder remaining, and unreadable post-state; retry authorization MUST be derived from that state. (covers AC-003)
- FR-004: Theft accounting MUST occur only for markers actually removed, and successful forced claim MUST still report the displaced worker and the surviving replacement marker. (covers AC-004)
- FR-005: The change MUST preserve the one comment-order winner, normal claim and renewal outcomes, identity refusals, admission check ordering, and stale-marker collection. (covers AC-005)
- FR-006: Focused tests MUST inject failure at replacement creation and old-marker cleanup, assert the complete marker census after each, and demonstrate observed red when safe ordering is inverted. (covers AC-001, AC-002)

## Ambiguities
- AMB-001: Whether a replacement that wins the existing comment-order election while the older marker is
  still present can be treated as authoritative before cleanup; clarification must reconcile this with
  the fact that the older comment id otherwise wins.
- AMB-002: Whether cleanup failure is returned as an `IoError` or a new typed `ClaimOutcome`; the chosen
  shape must preserve enough state for CLI rendering and retry authorization.

## Public Or Tool-Facing Impact
- `claim --force` failure wording and machine outcome semantics change.
- The `Writes.claimScoped` signature/outcome documentation may change to expose the interruption state.
- No new CLI flag or lock authority is introduced.

## Lifecycle Notes
- Governing issue: `FS-GG/.github#2772`; route work id and spec home match this artifact.
- Tier 1 because the forced-claim machine result and operator diagnostics are tool-facing contracts.
