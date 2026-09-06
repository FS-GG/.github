---
schemaVersion: 1
workId: 3263-malformed-reconciliation-correction
title: Human-authorized synthetic lifecycle checkpoint
stage: clarify
changeTier: tier1
status: clarified
sourceSpec: work/3263-malformed-reconciliation-correction/spec.md
publicOrToolFacingImpact: true
---

# Human-authorized synthetic lifecycle checkpoint Clarifications

## Source Specification
- work/3263-malformed-reconciliation-correction/spec.md

## Clarification Questions
- Q-001: What authority and exact scope can authorize a synthetic checkpoint?
- Q-002: Which facts remain strict across the extraordinary boundary?
- Q-003: What makes functional verification sufficient and checkable?
- Q-004: How does ordinary processing resume after the checkpoint?
- Q-005: How are reuse and ambiguity prevented?

## Answers
- A-001 [Q-001]: One immutable human-authored GitHub issue comment authorizes one canonical item/run/unit and exact frontier revision/digest.
- A-002 [Q-002]: Canonical JSON, event shape, item identity, sequence, previous digest, frontier digest, and checkpoint proof digest remain strict; missing evidence provenance alone is explicitly not required.
- A-003 [Q-003]: The proof carries a non-empty list of named checks, all with `passed` status and immutable GitHub-comment or `sha256:<64hex>` evidence.
- A-004 [Q-004]: The immediate checkpoint event becomes the trusted anchor; every event after it is processed by the unchanged strict validator.
- A-005 [Q-005]: Exactly one proof and one checkpoint are admitted, the checkpoint must immediately follow its frontier, and its sole evidence token consumes the proof digest.

## Decisions
- D-001 [Q-001]: Authorization is an immutable GitHub issue-comment URL and the proof digest binds it with the complete scope, frontier, reason, flags, and functional results.
- D-002 [Q-002]: The checkpoint event is the immediate successor to the authorized frontier and is appended through the ordinary canonical digest chain.
- D-003 [Q-003]: Missing receipt/evidence provenance is explicitly not required at the checkpoint; the implementation never invents counts or recreates bytes.
- D-004 [Q-004]: Canonical JSON, chain digests, item identity, and event structure remain mandatory. The checkpoint substitutes only pre-frontier evidence/reconciliation validation.
- D-005 [Q-005]: More than one proof, more than one checkpoint, reuse, or any scope/frontier disagreement is ambiguity and fails closed.

## Accepted Deferrals
No accepted deferrals recorded.

## Remaining Ambiguity
No blocking ambiguity remains.

## Lifecycle Notes
- Next lifecycle action: `fsgg-sdd checklist --work 3263-malformed-reconciliation-correction`.
