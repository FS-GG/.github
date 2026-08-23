---
schemaVersion: 1
workId: 2835-intake-apply-partial-transaction
title: Intake Apply Partial Transaction
stage: specify
changeTier: tier1
status: specified
publicOrToolFacingImpact: true
---

# Intake Apply Partial Transaction Specification

Prose status: specified

## User Value
An intake caller can validate and retry one stable filing draft without hidden partial state or duplicate issues.

## Scope
- SB-001: Intake validation, receipt binding, issue creation, label and board writes, typed resume semantics, documentation, and focused tests.

## Non-Goals
- SB-002: Do not implement later lifecycle commands or Governance enforcement in this specification.

## User Stories
- US-001 (P1): As a user, I can an intake caller can validate and retry one stable filing draft without hidden partial state or duplicate issues.

## Acceptance Scenarios
- AC-001 [US-001] [FR-001]: Given Intake Apply Partial Transaction is available, when the user exercises it, then they can an intake caller can validate and retry one stable filing draft without hidden partial state or duplicate issues.

## Functional Requirements
- FR-001: Validation rejects severity values the target board would reject before any external write. (Stories: US-001; Acceptance: AC-001)
- FR-002: Apply reports every visible partial effect after a downstream write failure. (Stories: US-001; Acceptance: AC-001)
- FR-003: A corrected draft with the same stable id resumes the existing filing and completes labels and board fields without creating a duplicate. (Stories: US-001; Acceptance: AC-001)
- FR-004: Focused tests execute rejected-value, partial-write, corrected-retry, and duplicate-prevention routes with subject-breaking mutation evidence. (Stories: US-001; Acceptance: AC-001)

## Ambiguities
No material ambiguities recorded.

## Public Or Tool-Facing Impact
- This specification is an SDD lifecycle artifact and command-report contract input.

## Lifecycle Notes
- Next lifecycle action: `fsgg-sdd clarify --work 2835-intake-apply-partial-transaction`.
