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
- SB-001: scripts/lib/gate.py; tests/gate-harness; .github/workflows/gate-harness.yml; scripts/check-repo-filter-monopoly.py; tests/repo-filter-monopoly; .github/workflows/repo-filter-monopoly.yml; scripts/check-harness-identity.py; scripts/check-ignored-author-coherence.py; scripts/check-preset-repo-scope-coherence.py; scripts/check-retirement-order-coherence.py; scripts/check-skillmirror-freshness.py; scripts/check-sparse-checkout-closure.py; scripts/skillmirror-redrive.py.

## Non-Goals
- SB-002: Do not implement later lifecycle commands or Governance enforcement in this specification.

## User Stories
- US-001 (P1): As a user, I can A shared gate can prove it examined every declared subject before reporting OK.

## Acceptance Scenarios
- AC-001 [US-001] [FR-001]: Given Fail-closed gate subject census is available, when the user exercises it, then they can A shared gate can prove it examined every declared subject before reporting OK.

## Functional Requirements
- FR-001: report_ok structurally requires a SubjectCensus and returns NO_VERDICT_PERMANENT for an empty census, an unresolved declared subject, an empty semantic examination, missing independent producer agreement, producer/examination disagreement, or absent authority provenance. (Stories: US-001; Acceptance: AC-001)
- FR-002: PATHS_SUBJECT remains the single machine-readable declaration convention and check-repo-filter-monopoly binds its census to the resolved files, the enumerated semantic comparison subjects, and a separately enumerated set of live comparison producers. (Stories: US-001; Acceptance: AC-001)
- FR-003: a project-present moved declared subject or removed monopoly HOME makes check-repo-filter-monopoly no-verdict while a project-absent fixture stays honestly silent. (Stories: US-001; Acceptance: AC-001)
- FR-004: the meta-gate refuses empty, partial, authority-unbound, zero-semantic, producerless, and producer-mismatched censuses, and every former compatibility caller that omits the required census. (Stories: US-001; Acceptance: AC-001)
- FR-005: every current report_ok caller supplies a census derived from its own resolved and examined subjects; no basename whitelist or default census remains. (Stories: US-001; Acceptance: AC-001)
- FR-006: the workflow description derives or omits its consumer count rather than claiming there are no consumers. (Stories: US-001; Acceptance: AC-001)

## Ambiguities
No material ambiguities recorded.

## Public Or Tool-Facing Impact
- This specification is an SDD lifecycle artifact and command-report contract input.

## Lifecycle Notes
- Next lifecycle action: `fsgg-sdd clarify --work 2760-gate-subject-census`.
