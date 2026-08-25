---
schemaVersion: 1
workId: 2941-coherent-release
title: Coherent Release
stage: clarify
changeTier: tier1
status: clarified
sourceSpec: work/2941-coherent-release/spec.md
publicOrToolFacingImpact: true
---

# Coherent Release Clarifications

## Source Specification
- work/2941-coherent-release/spec.md

## Clarification Questions
No clarification questions recorded.

## Answers
No clarification answers recorded.

## Decisions
- DEC-001: Select stable `0.75.4`. Live checks on 2026-08-25 found coherent `0.75.3` packages on both
  feeds, sibling tags at exact source `264725f374e3f05da46d7c3089462076a1f9bf7a`, and promoted
  `coherent-set/v0.75.3`, while finding no `0.75.4` package or sibling tag. The `0.75.3` bytes are
  immutable and valid, but their embedded release notes materially misdescribe the accumulated engine
  commits; repair therefore moves forward and never repacks `0.75.3`.
- DEC-002: Classify the forward repair as PATCH on the stable `0.x` line. The nine-commit engine frontier has
  `wireCount=0`, preserves every public command/member/package identity, and corrects existing
  completion, landability, and dependency-set behavior; `defectCount=1` makes release latency zero.
- DEC-003: Keep the three existing release workflow filenames and tag namespaces. Their NuGet Trusted
  Publishing policies and receiver tag resolution are external bindings, and the workflows already
  enforce the exact coherent membership, shared source SHA, prepare-once saga, and dual-feed order.
- DEC-004: Preserve publish-before-flip ordering. The preparation PR advances the shared source version
  and release notes while the canonical distributed pin is reconciled only to current public `0.75.3`;
  feed-facing registry/package projections advance to `0.75.4` only after both feeds serve it.
- DEC-005: Treat source preparation, saga publication, feed verification, feed-derived reconciliation,
  promoted immutable release, and freshness/receiver rerun as one item's explicit obligations. The
  worker keeps the claim until those facts converge; a successful upload alone is not completion.

## Accepted Deferrals
No accepted deferrals recorded.

## Remaining Ambiguity
No blocking ambiguity remains.

## Lifecycle Notes
- Next lifecycle action: `fsgg-sdd checklist --work 2941-coherent-release`.
