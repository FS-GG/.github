---
schemaVersion: 1
workId: 2871-closed-delivery-paths
title: Preserve declared paths across closed-item delivery
stage: clarify
changeTier: tier1
status: clarified
sourceSpec: work/2871-closed-delivery-paths/spec.md
publicOrToolFacingImpact: true
---

# Preserve declared paths across closed-item delivery Clarifications

## Source Specification
- work/2871-closed-delivery-paths/spec.md

## Clarification Questions
- CQ-001: Which source owns `DeclaredPaths` at the delivery transition?
- CQ-002: How must a failed authoritative body read be represented?
- CQ-003: Does this item also repair completion merge-SHA normalization?

## Answers
- CQ-001: The live issue body owns the declaration. The board scan continues
  to own status and scheduling facts, but its closure-sensitive touch-set
  projection must not replace the body at delivery.
- CQ-002: Convert the failed read to `Delivery.Unread` with the transport
  diagnostic. Do not parse an empty fallback and do not map failure to
  `Delivery.Undeclared`.
- CQ-003: No. That mismatch is independently reproducible after completion and
  has a distinct comparison/normalization cause.

## Decisions
- **DEC-001** [CQ-001] [FR-001] [FR-003]: Read and parse the issue body once in
  the live delivery handler, and use that authoritative `TouchSet` for both
  snapshot declaration facts and PR-path classification.
- **DEC-002** [CQ-002] [FR-002] [FR-004]: Preserve the four-way typed result;
  an IO failure becomes `Unread`, while a successfully read body may become
  `Declared`, `DeclaredNone`, or `Undeclared`.
- **DEC-003** [CQ-003]: Defer merge-SHA normalization to its own tracked item so
  this repair remains bounded to #2871's root cause.

## Accepted Deferrals
No accepted deferrals recorded.

## Remaining Ambiguity
- None.

## Lifecycle Notes
- Next lifecycle action: `fsgg-sdd checklist --work 2871-closed-delivery-paths`.
