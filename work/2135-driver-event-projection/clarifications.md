---
schemaVersion: 1
workId: 2135-driver-event-projection
title: Driver Event Projection
stage: clarify
changeTier: tier1
status: needsAnswers
sourceSpec: work/2135-driver-event-projection/spec.md
publicOrToolFacingImpact: true
---

# Driver Event Projection Clarifications

## Source Specification
- work/2135-driver-event-projection/spec.md

## Clarification Questions
- CQ-001 [AMB:AMB-001]: Which review-state vocabulary does classification reuse
  for "review handoff" vs "review/repair"?
- CQ-002 [AMB:AMB-002]: What backs "release/deployment/downstream adoption"
  material state?

## Answers
- CQ-001 → Reuse `Driver.ReviewChain`/`Driver.parseReviewComments` from
  `.github#2127` unchanged: a valid marker with no repair round is a review
  handoff; a valid marker whose round count places it in the repair phase is
  review/repair. No second review vocabulary is introduced (resolves AMB-001).
- CQ-002 → Reuse the existing `Delivery.Obligation` receipt vocabulary: a merged
  PR with any undischarged obligation is `MergedAwaitingObligations`; a merged PR
  whose obligations are all receipted (or explicitly declared none) is
  `Released` (resolves AMB-002).

## Decisions
- **DEC-001** [CQ-001] [AMB:AMB-001] [FR-004]: Review-state classification reuses
  `Driver.ReviewChain`'s existing marker/round vocabulary verbatim; no second
  review-state parser is introduced.
- **DEC-002** [CQ-002] [AMB:AMB-002] [FR-004]: Post-merge material state is
  derived from `Delivery.Obligation` receipts already defined by the delivery
  lifecycle; no new release-tracking surface is introduced.

## Accepted Deferrals
No accepted deferrals recorded.

## Remaining Ambiguity
No material ambiguities recorded. AMB-001 and AMB-002 resolved above.

## Lifecycle Notes
- Next lifecycle action: `fsgg-sdd checklist --work 2135-driver-event-projection`.
