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
- AC-001 [US-001] [FR-001]: Given the stated window and exact Git boundary, when the reporter runs, then it derives all seven measures from digest-bound raw issue records, typed incident evidence, and repository commands; it explicitly retires measure 2 and never accepts asserted period, artifact, or line-count summaries.
- AC-002 [US-001] [FR-002]: Given the historical reading, when the roadmap is scored, then every M0–M6 checkbox is controlled by its exact exit predicate and every unmet or unverified gap is named.
- AC-003 [US-001] [FR-003]: Given M6's successor clause, when it is evaluated, then the census explicitly covers `.github#266`, `.github#2752`, and `.github#2691` together with the three-cycle health result.
- AC-004 [US-001] [FR-004]: Given immediate-action 1, when the roadmap is read, then the freeze decision records state, date, actor, scope, and lift condition.
- AC-005 [US-001] [FR-005]: Given an undefined measure, when it is retired, then the retirement actor, date, state, and reason appear in the roadmap document rather than being inferred from omission.

## Functional Requirements
- FR-001: A deterministic command shall emit all seven named measures for a stated window, derive issue flow from a canonical-digest-bound raw issue census, derive repository trends from resolvable exact Git objects, validate typed incidents/censuses, and never omit an unverified result. (Stories: US-001; Acceptance: AC-001)
- FR-002: The roadmap shall score M0–M6 from each milestone's exit predicate rather than deliverable completion, naming every violated or unverified predicate. (Stories: US-001; Acceptance: AC-002)
- FR-003: M6 shall combine three consecutive health cycles with an explicit successor census covering `.github#266`, `.github#2752`, and `.github#2691`. (Stories: US-001; Acceptance: AC-003)
- FR-004: The immediate-action freeze decision shall record its actor, date, approved/refused/superseded state, scope, and lift condition. (Stories: US-001; Acceptance: AC-004)
- FR-005: Measure retirement shall be explicit in the document and record actor, date, retired state, and reason. (Stories: US-001; Acceptance: AC-005)

## Ambiguities
No material ambiguities recorded.

## Public Or Tool-Facing Impact
- This specification is an SDD lifecycle artifact and command-report contract input.

## Lifecycle Notes
- Next lifecycle action: `fsgg-sdd clarify --work 2754-roadmap-health-measures`.
