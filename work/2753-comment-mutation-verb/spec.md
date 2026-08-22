---
schemaVersion: 1
workId: 2753-comment-mutation-verb
title: Verified comment mutation verb
stage: specify
changeTier: tier1
status: specified
publicOrToolFacingImpact: true
---

# Verified comment mutation verb Specification

Prose status: specified

## User Value
Coordination callers create or amend an issue or pull-request comment from an owned file capability and receive proof that GitHub stored the intended bytes.

## Scope
- SB-001: Add one coordination-engine comment mutation verb; kernel options; focused CLI tests; select and widen to the narrow handler and GitHub adapter paths only after inspection.

## Non-Goals
- SB-002: Recover destroyed historical packet bodies or rewrite correct -F/--field, --input, and --body-file uses solely because they accept files.

## User Stories
- US-001 (P1): As a coordination caller, I can create or amend an exact comment by explicit id from an owned file and receive a verified receipt.

## Acceptance Scenarios
- AC-001 [US-001] [FR-001]: Missing input and mismatched readback each refuse; exact create and amend return matching id and SHA-256 digest receipts; no command contract exposes recency-based amendment; temporary resources are unique per worker, item, and operation and survive failed verification.

## Functional Requirements
- FR-001: Expose one engine verb that creates or amends by explicit comment id from an existing file path and never offers recency-based amendment. (Stories: US-001; Acceptance: AC-001)
- FR-002: Allocate or consume a unique per-operation capability carrying worker, item, and unguessable operation identity; refuse session-global conventional filenames and missing paths. (Stories: US-001; Acceptance: AC-001)
- FR-003: Read the written comment back from the authoritative issue-comment collection, compare byte length and SHA-256 digest with source bytes, and return a receipt naming comment id and digest. (Stories: US-001; Acceptance: AC-001)
- FR-004: Preserve the recovery capability after failed write or readback and clean it only after successful matching readback. (Stories: US-001; Acceptance: AC-001)
- FR-005: Add negative controls for missing path, mismatched readback, sibling scratch collision, explicit amendment, and positive create/amend round trips. (Stories: US-001; Acceptance: AC-001)

## Ambiguities
No material ambiguities recorded.

## Public Or Tool-Facing Impact
- This specification is an SDD lifecycle artifact and command-report contract input.

## Lifecycle Notes
- Next lifecycle action: `fsgg-sdd clarify --work 2753-comment-mutation-verb`.
