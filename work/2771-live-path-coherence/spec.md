---
schemaVersion: 1
workId: 2771-live-path-coherence
title: Live board path and maintaining-lane coherence
stage: specify
changeTier: tier1
status: specified
publicOrToolFacingImpact: true
---

# Live board path and maintaining-lane coherence Specification

Prose status: specified

## User Value
prevent workers from discovering unusable or incomplete Paths declarations only after claim

## Scope
- SB-001: one check over open Coordination rows, repository history, and exact-count baseline declarations

## Non-Goals
- SB-002: Do not implement later lifecycle commands or Governance enforcement in this specification.

## User Stories
- US-001 (P1): As a user, I can prevent workers from discovering unusable or incomplete Paths declarations only after claim.

## Acceptance Scenarios
- AC-001 [US-001] [FR-001]: Given Live board path and maintaining-lane coherence is available, when the user exercises it, then they can prevent workers from discovering unusable or incomplete Paths declarations only after claim.

## Functional Requirements
- FR-001: report every open row token proven moved away while allowing planned new files (Stories: US-001; Acceptance: AC-001)
- FR-002: require every exact-count baseline to name its maintaining issue in machine-readable form and require that issue Paths cover the baseline (Stories: US-001; Acceptance: AC-001)
- FR-003: include Backlog rows and fail closed on unreadable board or history data (Stories: US-001; Acceptance: AC-001)
- FR-004: negative fixtures for a moved-away token and an undeclared baseline lane must exit nonzero (Stories: US-001; Acceptance: AC-001)
- FR-005: tests/pipefail-assertions/baseline.txt is declared or explicitly exempted in its own machine-readable metadata (Stories: US-001; Acceptance: AC-001)

## Ambiguities
No material ambiguities recorded.

## Public Or Tool-Facing Impact
- This specification is an SDD lifecycle artifact and command-report contract input.

## Lifecycle Notes
- Next lifecycle action: `fsgg-sdd clarify --work 2771-live-path-coherence`.
