---
schemaVersion: 1
workId: 2758-agent-contract-version
title: Agent contract versioning
stage: specify
changeTier: tier1
status: specified
publicOrToolFacingImpact: true
---

# Agent contract versioning Specification

Prose status: specified

## User Value
Later readers can attribute every dispatched work record to the exact canonical skill prose the agent used.

## Scope
- SB-001: scripts/generate-projections, scripts/check-projection.py, and tests/projection only.

## Non-Goals
- SB-002: Do not implement later lifecycle commands or Governance enforcement in this specification.

## User Stories
- US-001 (P1): As a user, I can later readers can attribute every dispatched work record to the exact canonical skill prose the agent used.

## Acceptance Scenarios
- AC-001 [US-001] [FR-001]: Given Agent contract versioning is available, when the user exercises it, then they can later readers can attribute every dispatched work record to the exact canonical skill prose the agent used.

## Functional Requirements
- FR-001: Derive one deterministic SHA-256 digest from the canonical bytes of both skill roots, reuse the generator computation, record it in an existing durable work field, roll it on every merge, and prove with executable controls that a canonical skill change moves it while unchanged-source regeneration does not. (Stories: US-001; Acceptance: AC-001)

## Ambiguities
No material ambiguities recorded.

## Public Or Tool-Facing Impact
- This specification is an SDD lifecycle artifact and command-report contract input.

## Lifecycle Notes
- Next lifecycle action: `fsgg-sdd clarify --work 2758-agent-contract-version`.
