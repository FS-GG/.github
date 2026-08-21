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
  determine the observed post-state and retry authority?

## Answers
- CQ-001 [AMB:AMB-001] decision: No. Create the replacement capability first, then delete the foreign
  live markers, then evaluate the unchanged comment-order election from a fresh complete census. Until
  an older marker is removed, that older marker remains authoritative.
- CQ-002 [AMB:AMB-002] decision: Return a distinct typed cleanup-required outcome carrying the posted
  replacement, the workers already removed, and the marker whose removal failed. The CLI renders this
  observed state separately from a pre-post transport failure and authorizes a re-run to reconcile it.

## Decisions
- DEC-001 [CQ-001] [AMB:AMB-001]: Preserve comment-order authority. The transition is
  `complete pre-census → admit → post replacement → delete foreign live markers → complete post-census
  → existing winner election`. A replacement marker is a recoverable capability before it is the winner;
  no new authority predicate is introduced.
- DEC-002 [CQ-002] [AMB:AMB-002]: Add a closed cleanup-required claim outcome. A replacement-post failure
  remains an `IoError` because no mutation landed and the incumbent stands. A delete failure after the
  post is not an `IoError` stripped of state: it reports the surviving replacement and failed cleanup.
- DEC-003 [CQ-001] [AMB:AMB-001]: If cleanup completes but the post-census is unreadable, withdraw the
  replacement only when doing so cannot create zero markers; otherwise report an unreadable post-state
  with the replacement retained. The implementation must make this branch explicit and test it or narrow
  the outcome contract to the states it can actually observe safely.

## Accepted Deferrals
No accepted deferrals recorded.

## Remaining Ambiguity
No blocking ambiguity remains.

## Lifecycle Notes
- The chosen order follows the issue's binding: establish replacement capability before irreversible
  eviction and keep the existing comment-order winner as the sole authority.
