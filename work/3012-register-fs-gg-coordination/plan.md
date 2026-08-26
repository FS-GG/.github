---
schemaVersion: 1
workId: 3012-register-fs-gg-coordination
title: Register FS.GG.Coordination as a governed component
stage: plan
changeTier: tier1
status: planned
sourceSpec: work/3012-register-fs-gg-coordination/spec.md
sourceClarifications: work/3012-register-fs-gg-coordination/clarifications.md
sourceChecklist: work/3012-register-fs-gg-coordination/checklist.md
publicOrToolFacingImpact: true
---

# Register FS.GG.Coordination as a governed component Plan

Prose status: planned

## Source Snapshot
- spec: work/3012-register-fs-gg-coordination/spec.md sha256:774c9e46b19972d9fdb219a0a1473f50aef717e2afb1e09fbae7d15068feaa7d schemaVersion:1
- clarifications: work/3012-register-fs-gg-coordination/clarifications.md sha256:49d7b753afac7a78289fc0fc0f927d97fae2f76bd57ac9fa58b444454d941d02 schemaVersion:1
- checklist: work/3012-register-fs-gg-coordination/checklist.md sha256:3d093897a956fc3ef6613b87cd52cc3a20084b5658076a13e6c21d7ce53543a4 schemaVersion:1

## Plan Scope
- Work item 3012-register-fs-gg-coordination is planned from the current specification, clarification, and checklist facts.
- Requirement count: 5.
- Clarification decision count: 0.
- Checklist result count: 5.

## Plan Decisions
- PD-001 [AC-001] [FR-001] complete: Add one `coordination` non-participant row, retain the closed
  receives vocabulary, update the roster changelog, and mutation-prove that the row receives no v1 fabric.
- PD-002 [AC-001] [FR-002] complete: Extend `RepoScope.resolve`, its focused tests, and the guarded
  board-schema table; add the live Project option only through `project-field-options` snapshot/restore.
- PD-003 [AC-001] [FR-003] complete: Reconcile architecture counts and append exact live settings,
  App-selection, producer-release, and Project-migration evidence to the timestamped administrator report.
- PD-004 [AC-001] [FR-004] complete: Run roster validation/selftests, Project field tests/live check,
  focused F# resolver tests, projection currency, and the complete SDD analyze/verify/ship chain.
- PD-005 [AC-001] [FR-005] complete: Require a fresh independent cross-repository critic on the exact
  PR head and use the guarded coordination landing protocol.

## Contract Impact
- PC-001 [PD-001] additive registry contract: `registry/repos.yml` gains one non-participant identity;
  the existing schema remains version 11 because no field or vocabulary changes.
- PC-002 [PD-002] additive board/runtime contract: `coordination` becomes a valid `Repo Scope` and maps
  to `FS.GG.Coordination`; all prior options and assignments remain byte-for-byte represented in receipts.

## Verification Obligations
- VO-001 [PD-001] [PC-001] semanticTest: Prove the real roster validates and a mutation cannot grant
  the coordination row any legacy capability.
- VO-002 [PD-002] [PC-002] integrationTest: Prove source/schema/resolver/live-Project agreement and
  complete preservation of every pre-migration assignment.
- VO-003 [PD-003] acceptanceTest: Re-read repository settings and App installations; represent
  unsupported or over-broad controls as findings, never successful writes.
- VO-004 [PD-004] readinessEvidence: Produce current SDD analysis, observed test evidence, verification,
  and ship receipts over the exact implementation.
- VO-005 [PD-005] independentReview: Bind critic acceptance and hosted checks to the exact merge head.

## Performance Intent
No performance intent is declared for this work item.

## Migration Posture
- PM-001 [PC-001] additive: Keep roster schema version 11 because the existing non-participant shape
  expresses the new row exactly; older readers see one additional valid identity and no new vocabulary.

## Generated View Impact
- GV-001 [PD-001] workModel: Refresh the SDD work model from exact lifecycle sources and require
  `scripts/generate-projections --check` to prove the repository's architecture/protocol projections remain current.

## Accepted Deferrals
No accepted plan deferrals recorded.

## Planning Findings
No blocking planning findings recorded.

## Advisory Notes
- Optional Governance pointers remain compatibility facts only.

## Lifecycle Notes
- Next lifecycle action: `fsgg-sdd tasks --work 3012-register-fs-gg-coordination`.
