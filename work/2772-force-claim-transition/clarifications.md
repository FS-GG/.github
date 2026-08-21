---
schemaVersion: 1
workId: 2772-force-claim-transition
title: Atomic and recoverable forced-claim transition
stage: clarify
changeTier: tier1
status: clarified
sourceSpec: work/2772-force-claim-transition/spec.md
publicOrToolFacingImpact: true
---

# Atomic and recoverable forced-claim transition Clarifications

## Source Specification
- work/2772-force-claim-transition/spec.md

## Clarification Questions
- CQ-001 [AMB:AMB-001] blocking answered: Can the replacement be elected before deleting the
  lower-comment-id incumbent while preserving comment-order authority?
- CQ-002 [AMB:AMB-002] blocking answered: How must a cleanup failure be represented so the caller can
  determine the observed post-state and retry authority, including failure before replacement creation?
- CQ-003 [AMB:AMB-002] blocking answered: Does a replacement POST transport error prove no replacement
  exists, and at what point is a forced-transition post-census final?

## Answers
- CQ-001 [AMB:AMB-001] decision: No. Create the replacement capability first, then delete the foreign
  live markers, then evaluate the unchanged comment-order election from a fresh complete census. Until
  an older marker is removed, that older marker remains authoritative.
- CQ-002 [AMB:AMB-002] decision: Every forced transition returns a typed outcome governed by its complete
  pre-census and, when readable, complete post-census. Replacement POST failure is therefore a typed
  `ReplacementPostFailed` result proving the incumbent still wins, not a raw transport error. Cleanup
  failure carries the posted replacement and failed incumbent; unreadable post-state carries no invented
  census. Only the typed observed state authorizes any reconciliation.
- CQ-003 [AMB:AMB-002] decision: No. A transport error is ambiguous because the response may be lost after
  GitHub stores the replacement. Re-read authoritatively, identify a newly observed marker by its exact
  drafted identity, and reconcile it through the same cleanup and election path. Serialize `After` only
  after every cleanup, withdrawal, and reconciliation mutation has finished.

## Decisions
- DEC-001 [CQ-001] [AMB:AMB-001]: Preserve comment-order authority. The transition is
  `complete pre-census → admit → post replacement → delete foreign live markers → complete post-census
  → existing winner election`. A replacement marker is a recoverable capability before it is the winner;
  no new authority predicate is introduced.
- DEC-002 [CQ-002] [AMB:AMB-002]: Add closed forced-transition outcomes carrying `ForcedClaimCensuses`.
  Replacement-post failure performs an authoritative re-read and returns `ReplacementPostFailed` when
  the incumbent still wins. A delete failure after the post reports cleanup-required, old-holder-standing,
  no-holder, or unreadable post-state from the same census authority. Successful steal receipts serialize
  the pre/post censuses, so the machine result and diagnostic are grounded in the same observations.
- DEC-003 [CQ-001] [AMB:AMB-001]: If cleanup completes but the post-census is unreadable, withdraw the
  replacement only when doing so cannot create zero markers; otherwise report an unreadable post-state
  with the replacement retained. The implementation must make this branch explicit and test it or narrow
  the outcome contract to the states it can actually observe safely.
- DEC-004 [CQ-003] [AMB:AMB-002]: Treat replacement POST transport failure as ambiguous. A matching newly
  observed replacement is recoverable authority and continues through deterministic cleanup; if the old
  marker already disappeared and the replacement wins, return `ReplacementWon` without inventing theft.
  Every terminal forced outcome carries the final complete census taken after all mutations, or explicitly
  carries no `After` when that final read is unavailable.

## Accepted Deferrals
No accepted deferrals recorded.

## Remaining Ambiguity
No blocking ambiguity remains.

## Lifecycle Notes
- The chosen order follows the issue's binding: establish replacement capability before irreversible
  eviction and keep the existing comment-order winner as the sole authority.
- Round-2 repair incorporates independent review decision digest
  `387201c40307bdeefb15e8e00196a732df8d39a14045bc7d76096bb4dee9d3ed`.
