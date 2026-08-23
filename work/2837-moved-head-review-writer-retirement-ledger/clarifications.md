---
schemaVersion: 1
workId: 2837-moved-head-review-writer-retirement-ledger
title: Restart review-record sealing after retiring an accepted moved-head chain
stage: clarify
changeTier: tier1
status: clarified
sourceSpec: work/2837-moved-head-review-writer-retirement-ledger/spec.md
publicOrToolFacingImpact: true
---

# Restart review-record sealing after retiring an accepted moved-head chain Clarifications

## Source Specification
- work/2837-moved-head-review-writer-retirement-ledger/spec.md

## Clarification Questions
- CQ-001: Which structured records determine the next revision and `previousDigest` after a previously accepted head moves?

## Answers
- CA-001 [CQ-001]: Only records in the same live-generation partition the reader validates. An accepted non-current-head chain is retired before the next initial record is sealed.

## Decisions
- DEC-001 [FR-001] [CQ:CQ-001]: Reuse the core live/retired partition through the Driver boundary so `Client.fs` seals against only live records while old structured comments remain byte-for-byte append-only.
- DEC-002 [FR-001] [FR-002]: Cover the real review-record writer followed by live projection, a hosted wire route, and a mutation that restores all-comment sealing and must turn the focused regression red.

## Accepted Deferrals
No accepted deferrals recorded.

## Remaining Ambiguity
No blocking ambiguity remains.

## Lifecycle Notes
- Next lifecycle action: `fsgg-sdd checklist --work 2837-moved-head-review-writer-retirement-ledger`.
