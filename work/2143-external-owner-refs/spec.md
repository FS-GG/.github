---
schemaVersion: 1
workId: 2143-external-owner-refs
title: Preserve External Owner References
stage: specify
changeTier: tier1
status: specified
publicOrToolFacingImpact: true
---

# Preserve External Owner References Specification

Prose status: specified

## User Value
External-owner Coordination board rows can be updated safely without silently targeting a same-name repository under the default owner.

## Scope
- SB-001: Preserve owner/repository/number canonical identity through add, item-id, single and batch field writes, reconciliation, receipts, fake contracts, and targeted live recovery.

## Non-Goals
- SB-002: Do not implement later lifecycle commands or Governance enforcement in this specification.

## User Stories
- US-001 (P1): As a user, I can external-owner Coordination board rows can be updated safely without silently targeting a same-name repository under the default owner.

## Acceptance Scenarios
- AC-001 [US-001] [FR-001]: Given Preserve External Owner References is available, when the user exercises it, then they can external-owner Coordination board rows can be updated safely without silently targeting a same-name repository under the default owner.

## Functional Requirements
- FR-001: Explicit owner/repo issue refs and issue URLs must select the identical external board item on all read and write paths, and receipts must expose that canonical identity. (Stories: US-001; Acceptance: AC-001)

## Ambiguities
No material ambiguities recorded.

## Public Or Tool-Facing Impact
- This specification is an SDD lifecycle artifact and command-report contract input.

## Lifecycle Notes
- Next lifecycle action: `fsgg-sdd clarify --work 2143-external-owner-refs`.
