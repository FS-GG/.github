---
schemaVersion: 1
workId: 3247-roadmap-acceptance-authoring
title: Roadmap Acceptance Authoring
stage: plan
changeTier: tier1
status: planned
sourceSpec: work/3247-roadmap-acceptance-authoring/spec.md
sourceClarifications: work/3247-roadmap-acceptance-authoring/clarifications.md
sourceChecklist: work/3247-roadmap-acceptance-authoring/checklist.md
publicOrToolFacingImpact: true
---

# Roadmap Acceptance Authoring Plan

Prose status: planned

## Source Snapshot
- spec: work/3247-roadmap-acceptance-authoring/spec.md sha256:e6a8a15480cbcd4572d3d92969ab83476a0cdedc4fc032841bc07c188b1820be schemaVersion:1
- clarifications: work/3247-roadmap-acceptance-authoring/clarifications.md sha256:55ab01edaaaf4f9e90d48031e0db32d1e555ef5c2cab7ddd70eddc357d4f0bd1 schemaVersion:1
- checklist: work/3247-roadmap-acceptance-authoring/checklist.md sha256:5970b167c037780525d58385a7415dff5a28af571de1819bda38da9bd652e9b1 schemaVersion:1

## Plan Scope
- Work item 3247-roadmap-acceptance-authoring is planned from the current specification, clarification, and checklist facts.
- Requirement count: 3.
- Clarification decision count: 0.
- Checklist result count: 3.

## Plan Decisions
- PD-001 [AC-001] [FR-001] complete: Treat `ReviewEvidence` as the immutable acceptance-envelope comment created after qualification, while qualification semantic evidence must equal the pre-existing `StructuredReviewEvidence` comment.
- PD-002 [AC-001] [FR-002] complete: Reuse the complete structured route-ledger parser at the live acceptance boundary and require its current record to be `sdd-required` with the exact submitted `SddWorkId`.
- PD-003 [AC-001] [FR-003] complete: Preserve all candidate, merge, lifecycle, review, and SDD observation identities; the repair changes validation joins only and requires no rewrite of existing unit authority.

## Contract Impact
- PC-001 [PD-001] command report: `roadmap unit accept seal` keeps its existing input schema and command surface; only the interpretation of existing review and SDD identity fields is corrected, so 0.83.3 is a compatibility-preserving patch.

## Verification Obligations
- VO-001 [PD-001] [PC-001] semanticTest: Run the Core and BoardOps suites, including positive and inverted tests for distinct review/envelope identities, edited envelopes, missing route ledgers, and mismatched route work IDs.

## Performance Intent
No performance intent is declared for this work item.

## Migration Posture
- PM-001 [PC-001] diagnoseOnly: No schema migration is required; existing 0.83.x acceptance inputs become authorable, and stale or mismatched route/evidence identities continue to fail closed with diagnostics.

## Generated View Impact
- GV-001 [PD-001] workModel: Refresh SDD readiness artifacts plus the coherent-set compatibility and packed-skill manifest projections after the source version advances to 0.83.3.

## Accepted Deferrals
No accepted plan deferrals recorded.

## Planning Findings
No blocking planning findings recorded.

## Advisory Notes
- Optional Governance pointers remain compatibility facts only.

## Lifecycle Notes
- Next lifecycle action: `fsgg-sdd tasks --work 3247-roadmap-acceptance-authoring`.
