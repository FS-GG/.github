---
schemaVersion: 1
workId: 2834-review-contract-diagnostics
title: Actionable and honest review contract diagnostics
stage: specify
changeTier: tier1
status: specified
publicOrToolFacingImpact: true
---

# Actionable and honest review contract diagnostics Specification

Prose status: specified

## User Value
Make review refusals directly actionable and describe exactly what the validator enforces.

## Scope
- SB-001: Add review-wait then review-record remediation to the missing acceptance refusal; align meaningful and not-meaningful route evidence validation with named semantic parts; add focused regressions.

## Non-Goals
- SB-002: Do not change review marker schemas, round semantics, or unrelated Client.fs behavior.

## User Stories
- US-001 (P1): As a user, I can make review refusals directly actionable and describe exactly what the validator enforces.

## Acceptance Scenarios
- AC-001 [US-001] [FR-001]: A missing record refusal names review wait before review record; four entries remain structurally accepted; three or five entries are refused with an exact-count and ordered-role message; not-meaningful requires one explicit reason.

## Functional Requirements
- FR-001: A missing review record refusal names fsgg-coord review wait before fsgg-coord review record. (Stories: US-001; Acceptance: AC-001)
- FR-002: Meaningful route evidence validation and its refusal MUST agree that the wire contract is exactly four ordered entries representing built artifact, executed command, compared routes, and observed result; semantic truth remains critic-authored. (Stories: US-001; Acceptance: AC-001)
- FR-003: Not-meaningful route evidence is accepted only when it carries one explicit reason. (Stories: US-001; Acceptance: AC-001)
- FR-004: Focused regressions MUST prove the actionable diagnostic, preserve acceptance of a four-entry list under the documented shape-only contract, and make a non-four-entry refusal describe the actual cardinality check honestly. (Stories: US-001; Acceptance: AC-001)

## Ambiguities
No material ambiguities recorded.

## Public Or Tool-Facing Impact
- Public CLI diagnostics and typed review-ledger validation change.

## Lifecycle Notes
- Next lifecycle action: `fsgg-sdd clarify --work 2834-review-contract-diagnostics`.
