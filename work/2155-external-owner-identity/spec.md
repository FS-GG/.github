---
schemaVersion: 1
workId: 2155-external-owner-identity
title: Preserve canonical external-owner identity across scheduler and claim paths
stage: specify
changeTier: tier1
status: specified
publicOrToolFacingImpact: true
---

# Preserve canonical external-owner identity across scheduler and claim paths Specification

Prose status: specified

## User Value
Cross-repository board lanes can be claimed by their canonical owner without fallback to the default owner.

## Scope
- SB-001: Preserve owner/repo identity in the scheduler decision, CLI batch next and take handoff, GitHub mutation target, JSON receipt, twin-owner fixtures, and accepted live recovery.

## Non-Goals
- SB-002: Do not implement later lifecycle commands or Governance enforcement in this specification.

## User Stories
- US-001 (P1): As a user, I can cross-repository board lanes can be claimed by their canonical owner without fallback to the default owner.

## Acceptance Scenarios
- AC-001 [US-001] [FR-001]: Given Preserve canonical external-owner identity across scheduler and claim paths is available, when the user exercises it, then they can cross-repository board lanes can be claimed by their canonical owner without fallback to the default owner.

## Functional Requirements
- FR-001: Every offered external row carries the canonical owner/repo identity into the claim target and receipt. (Stories: US-001; Acceptance: AC-001)
- FR-002: Batch followed by bare take selects and claims the same external row while leaving a default-owner twin untouched. (Stories: US-001; Acceptance: AC-001)
- FR-003: JSON output exposes owner/repo for both offered and claimed rows. (Stories: US-001; Acceptance: AC-001)

## Ambiguities
No material ambiguities recorded.

## Public Or Tool-Facing Impact
- This specification is an SDD lifecycle artifact and command-report contract input.

## Lifecycle Notes
- Next lifecycle action: `fsgg-sdd clarify --work 2155-external-owner-identity`.
