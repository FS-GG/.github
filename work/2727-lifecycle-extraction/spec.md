---
schemaVersion: 1
workId: 2727-lifecycle-extraction
title: Lifecycle CLI extraction and typed completion dependency
stage: specify
changeTier: tier1
status: specified
publicOrToolFacingImpact: true
---

# Lifecycle CLI extraction and typed completion dependency Specification

Prose status: specified

## User Value
independently owned lifecycle CLI handlers with unchanged behavior

## Scope
- SB-001: done, landable, delivery, review, route, verify-paths, followup-audit, typed completion dependency, tests, and project wiring

## Non-Goals
- SB-002: Do not implement later lifecycle commands or Governance enforcement in this specification.

## User Stories
- US-001 (P1): As a user, I can independently owned lifecycle CLI handlers with unchanged behavior.

## Acceptance Scenarios
- AC-001 [US-001] [FR-001]: Given Lifecycle CLI extraction and typed completion dependency is available, when the user exercises it, then they can independently owned lifecycle CLI handlers with unchanged behavior.

## Functional Requirements
- FR-001: every lifecycle command preserves observable output, exit code, and side effects; every Options.Command remains exactly once registered; mutable completion backpatching and placeholder failwith are absent; focused coverage moves to the lifecycle test project; pack and release payload remain complete (Stories: US-001; Acceptance: AC-001)

## Ambiguities
No material ambiguities recorded.

## Public Or Tool-Facing Impact
- This specification is an SDD lifecycle artifact and command-report contract input.

## Lifecycle Notes
- Next lifecycle action: `fsgg-sdd clarify --work 2727-lifecycle-extraction`.
