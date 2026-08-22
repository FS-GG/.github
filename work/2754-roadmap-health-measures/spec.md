---
schemaVersion: 1
workId: 2754-roadmap-health-measures
title: Derive and score roadmap health measures
stage: specify
changeTier: tier1
status: specified
publicOrToolFacingImpact: true
---

# Derive and score roadmap health measures Specification

Prose status: specified

## User Value
Honest roadmap status is derived from reproducible evidence.

## Scope
- SB-001: Derive all seven health measures, record explicit unverified values, and rescore M0–M6 in the roadmap.

## Non-Goals
- SB-002: Do not implement later lifecycle commands or Governance enforcement in this specification.

## User Stories
- US-001 (P1): As a user, I can honest roadmap status is derived from reproducible evidence.

## Acceptance Scenarios
- AC-001 [US-001] [FR-001]: Given Derive and score roadmap health measures is available, when the user exercises it, then they can honest roadmap status is derived from reproducible evidence.

## Functional Requirements
- FR-001: A deterministic command shall emit all seven named measures for a stated window, never omitting an unverified result. (Stories: US-001; Acceptance: AC-001)

## Ambiguities
No material ambiguities recorded.

## Public Or Tool-Facing Impact
- This specification is an SDD lifecycle artifact and command-report contract input.

## Lifecycle Notes
- Next lifecycle action: `fsgg-sdd clarify --work 2754-roadmap-health-measures`.
