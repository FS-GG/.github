---
schemaVersion: 1
workId: 2136-generated-facts
title: Generated Operational Process Facts
stage: specify
changeTier: tier1
status: specified
publicOrToolFacingImpact: true
---

# Generated Operational Process Facts Specification

Prose status: specified

## User Value
Maintainers and agents consume one executable, versioned source for wave, review, lifecycle, ledger, and release operations.

## Scope
- SB-001: Typed facts, deterministic projections, registry-derived release inventory, mirrored skills, and regression fixtures.

## Non-Goals
- SB-002: Do not replace qualitative judgement or policy rationale with generated prose.

## User Stories
- US-001 (P1): As a user, I can maintainers and agents consume one executable, versioned source for wave, review, lifecycle, ledger, and release operations.

## Acceptance Scenarios
- AC-001 [US-001] [FR-001]: Given Generated Operational Process Facts is available, when the user exercises it, then they can maintainers and agents consume one executable, versioned source for wave, review, lifecycle, ledger, and release operations.

## Functional Requirements
- FR-001: The generator must emit every machine-readable operational literal from facts and reject duplicate or stale generated output. (Stories: US-001; Acceptance: AC-001)
- FR-002: Release inventory must change when the registry producer set changes without hand-editing skill prose. (Stories: US-001; Acceptance: AC-001)

## Ambiguities
No material ambiguities recorded.

## Public Or Tool-Facing Impact
- This specification is an SDD lifecycle artifact and command-report contract input.

## Lifecycle Notes
- Next lifecycle action: `fsgg-sdd clarify --work 2136-generated-facts`.
