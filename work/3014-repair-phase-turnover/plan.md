---
schemaVersion: 1
workId: 3014-repair-phase-turnover
title: Repair Phase Turnover
stage: plan
changeTier: tier1
status: planned
sourceSpec: work/3014-repair-phase-turnover/spec.md
sourceClarifications: work/3014-repair-phase-turnover/clarifications.md
sourceChecklist: work/3014-repair-phase-turnover/checklist.md
publicOrToolFacingImpact: true
---

# Repair Phase Turnover Plan

Prose status: planned

## Source Snapshot
- spec: work/3014-repair-phase-turnover/spec.md sha256:00ff25bc22b8d8b7e375aed58a83adc1dfcf715fec546284add124becedd18fa schemaVersion:1
- clarifications: work/3014-repair-phase-turnover/clarifications.md sha256:cb8eec9c18d4e808dc871f6ab2d74ed1606a3170f99a7acf3bfa71ade5665996 schemaVersion:1
- checklist: work/3014-repair-phase-turnover/checklist.md sha256:23046b6a28464d0131cc5ae775d24d87c38b52417d69d6d26562bf29389b60a5 schemaVersion:1

## Plan Scope
- Work item 3014-repair-phase-turnover is planned from the current specification, clarification, and checklist facts.
- Requirement count: 5.
- Clarification decision count: 3.
- Checklist result count: 5.

## Plan Decisions
- PD-001 [AC-001] [FR-001] complete: Replace the fixed four-record destructure with a validated generation projection that selects confirmations 1-3 for compatibility and the last confirmation as terminal authority.
- PD-002 [AC-002] [FR-002] complete: Treat absence of `terminal-confirmation` as the historical exactly-three-round spelling; require the field only when the derived terminal record differs from confirmation 3.
- PD-003 [AC-003] [FR-003] complete: Preserve ledger validation, `Review.decideOrdinaryExhaustion`, exact live head, completed-wait evidence, legacy uniqueness, and fresh-claim checks as conjunctive pre-write guards.
- PD-004 [AC-004] [FR-004] complete: Leave repair-phase receipt authorization unchanged and exercise it after the generalized escalation is sealed.
- PD-005 [AC-005] [FR-005] complete: Extend documented legacy marker grammar with optional `terminal-confirmation` and cover both old and extended spellings in lifecycle and stateful E2E tests.

## Contract Impact
- PC-001 [PD-001] [PD-002] liveReviewWriter: `review record` accepts the same drafts and adds a compatible admitted-longer-chain turnover route.
- PC-002 [PD-002] [PD-005] exhaustionMarker: `fsgg:independent-review-escalation:v1` retains all required historical fields and adds one conditionally required terminal URL.
- PC-003 [PD-003] [PD-004] repairPhaseAuthority: structured escalation and the seven-field repair-phase receipt remain the only entry authority.

## Verification Obligations
- VO-001 [PD-001] [PD-003] [PC-001] semanticTest: A five-record contiguous ledger with exact completed round-4 wait and fresh claim records escalation; mutations of each bound field refuse without a write.
- VO-002 [PD-002] [PC-002] regressionTest: The existing exact three-round stateful turnover fixture remains green with no terminal field.
- VO-003 [PD-004] [PC-003] integrationTest: Close the predecessor unmerged, establish a newer claim and fresh PR, and record a receipt-bearing repair-phase entry naming the new escalation.
- VO-004 [PD-005] [PC-002] documentationTest: Marker grammar and repair-phase documentation state when `terminal-confirmation` is required.

## Performance Intent
No performance intent is declared for this work item.

## Migration Posture
- PM-001 [PC-001] [PC-002] backwardCompatible: Existing exactly-three-round markers and ledgers need no rewrite; only a newly authored longer admitted chain carries the terminal field.

## Generated View Impact
- GV-001 [PD-001] workModel: Regenerate the work model and every downstream readiness view from the source-bound package after implementation evidence; committed views must bind the final test-report digests and contain zero self-attested obligations.

## Accepted Deferrals
No accepted plan deferrals recorded.

## Planning Findings
No blocking planning findings recorded.

## Advisory Notes
- Optional Governance pointers remain compatibility facts only.

## Lifecycle Notes
- Next lifecycle action: `fsgg-sdd tasks --work 3014-repair-phase-turnover`.
