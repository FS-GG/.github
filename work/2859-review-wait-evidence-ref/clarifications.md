---
schemaVersion: 1
workId: 2859-review-wait-evidence-ref
title: Host-Owned Review Wait Boundary
stage: clarify
changeTier: tier1
status: clarified
sourceSpec: work/2859-review-wait-evidence-ref/spec.md
publicOrToolFacingImpact: true
---

# Host-Owned Review Wait Boundary Clarifications

## Source Specification
- work/2859-review-wait-evidence-ref/spec.md

## Clarification Questions
- CQ-001: Should the existing explicit event-file form be removed when host-owned entry is added?
- CQ-002: Should a completion that cites the critic's prose marker be normalized or refused?

## Answers
- CQ-001: No. Keep the explicit event-file form compatible so existing automation does not break while hosts move to the derived entry form.
- CQ-002: Normalize only when the marker resolves uniquely to the required structured decision record; otherwise refuse before append and name the required record.

## Decisions
- DEC-001 [CQ-001]: Add a dedicated `enter` form whose payload is derived from live authority; preserve explicit event files for compatibility.
- DEC-002 [CQ-002]: Validate terminal evidence against the immediately preceding structured record before append, with deterministic unique normalization permitted and ambiguity refused.
- DEC-003: Use the existing review-state and claim readers as the authority for head, kind, round, and claim generation; do not add a second state model.

## Accepted Deferrals
No accepted deferrals recorded.

## Remaining Ambiguity
- No blocking ambiguity remains.

## Lifecycle Notes
- Next lifecycle action: `fsgg-sdd checklist --work 2859-review-wait-evidence-ref`.
