---
schemaVersion: 1
workId: 2760-gate-subject-census
title: Fail-closed gate subject census
stage: specify
changeTier: tier1
status: specified
publicOrToolFacingImpact: true
---

# Fail-closed gate subject census Specification

Prose status: specified

## User Value
A shared gate can prove it examined every declared subject before reporting OK.

## Scope
- SB-001: scripts/lib/gate.py; tests/gate-harness; .github/workflows/gate-harness.yml; scripts/check-repo-filter-monopoly.py; tests/repo-filter-monopoly; .github/workflows/repo-filter-monopoly.yml.

## Non-Goals
- SB-002: Do not implement later lifecycle commands or Governance enforcement in this specification.

## User Stories
- US-001 (P1): As a user, I can A shared gate can prove it examined every declared subject before reporting OK.

## Acceptance Scenarios
- AC-001 [US-001] [FR-001]: Given Fail-closed gate subject census is available, when the user exercises it, then they can A shared gate can prove it examined every declared subject before reporting OK.

## Functional Requirements
- FR-001: report_ok consumes a structured census and returns NO_VERDICT_PERMANENT for an empty census, an unresolved declared subject, or a census whose authority revision or digest is absent. (Stories: US-001; Acceptance: AC-001)
- FR-002: PATHS_SUBJECT remains the single machine-readable declaration convention and the migrated monopoly gate resolves it without a Boolean reduction. (Stories: US-001; Acceptance: AC-001)
- FR-003: a project-present moved declared subject makes check-repo-filter-monopoly red while a project-absent fixture stays honestly silent. (Stories: US-001; Acceptance: AC-001)
- FR-004: the meta-gate reds when a consumer reports OK with no examined subjects and admits a known complete census. (Stories: US-001; Acceptance: AC-001)
- FR-005: the workflow description derives or omits its consumer count rather than claiming there are no consumers. (Stories: US-001; Acceptance: AC-001)

## Ambiguities
No material ambiguities recorded.

## Public Or Tool-Facing Impact
- This specification is an SDD lifecycle artifact and command-report contract input.

## Lifecycle Notes
- Next lifecycle action: `fsgg-sdd clarify --work 2760-gate-subject-census`.
