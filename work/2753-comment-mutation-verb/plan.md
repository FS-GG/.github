---
schemaVersion: 1
workId: 2753-comment-mutation-verb
title: Verified comment mutation verb
stage: plan
changeTier: tier1
status: planned
sourceSpec: work/2753-comment-mutation-verb/spec.md
sourceClarifications: work/2753-comment-mutation-verb/clarifications.md
sourceChecklist: work/2753-comment-mutation-verb/checklist.md
publicOrToolFacingImpact: true
---

# Verified comment mutation verb Plan

Prose status: planned

## Source Snapshot
- spec: work/2753-comment-mutation-verb/spec.md sha256:62be9c175b09c27d8e3ac1bc57d106038994fd6cea5f90824af11718a2e62ba6 schemaVersion:1
- clarifications: work/2753-comment-mutation-verb/clarifications.md sha256:13e6933ab2102c3e1a1773fa4e572d5eb6de8c70b4fd9c3cf5d60b8a6de066c1 schemaVersion:1
- checklist: work/2753-comment-mutation-verb/checklist.md sha256:2af01cff4e65fa71c4cfa66dabf0796f364ffc2f56551a72af2b0483a9b2d790 schemaVersion:1

## Plan Scope
- Work item 2753-comment-mutation-verb is planned from the current specification, clarification, and checklist facts.
- Requirement count: 5.
- Clarification decision count: 0.
- Checklist result count: 5.

## Plan Decisions
- PD-001 [AC-001] [FR-001] complete: Add one `comment` command family with `create <ref> <item> <source>` and `amend <ref> <item> <comment-id> <source>` forms; amendment authority is the explicit numeric comment id and no recency form is accepted.
- PD-002 [AC-001] [FR-002] complete: The handler copies the source into a new operation directory below an identity-scoped temporary root whose name binds worker, canonical item, and a cryptographically random operation id; it rejects absent or non-regular source files.
- PD-003 [AC-001] [FR-003] complete: Extend `FS.GG.Coord.GitHub.Writes` with create/amend primitives that always re-read the complete issue-comment collection and compare UTF-8 byte length and SHA-256 digest before returning a typed verified receipt.
- PD-004 [AC-001] [FR-004] complete: Delete only the allocated operation directory after a matching readback; preserve it and include its path in the failure diagnostic for transport, missing-readback, or digest-mismatch recovery.
- PD-005 [AC-001] [FR-005] complete: Add focused parser, handler, and transport tests covering nonexistent input, source isolation across operations, exact create/amend receipts, explicit-id routing, unreadable collection, and deliberately mismatched readback; mutation evidence must show the mismatch control red when inverted.

## Contract Impact
- PC-001 [PD-001] command report: `fsgg-coord comment create|amend` is an additive public CLI contract with JSON-default and text projections; the receipt names operation, canonical item, comment id, UTF-8 byte length, SHA-256 digest, and cleanup state.

## Verification Obligations
- VO-001 [PD-001] [PC-001] semanticTest: Run focused parser/handler/transport tests, the full CLI test project, build with signature checking, a local CLI missing-file smoke test, and a bounded inversion that forces stored bytes to differ and observes the focused suite fail.

## Performance Intent
No performance intent is declared for this work item.

## Migration Posture
- PM-001 [PC-001] additive: Preserve every existing command spelling and projection; migrate agent-authored comment guidance to the verified verb while leaving correct workflow `-F` file expansion untouched unless separately widened and migrated with its own token-safe integration design.

## Generated View Impact
- GV-001 [PD-001] workModel: Regenerate `readiness/2753-comment-mutation-verb/work-model.json` and later analysis/evidence/verification/ship receipts only through `fsgg-sdd`; no generated readiness file is hand-edited.

## Accepted Deferrals
No accepted plan deferrals recorded.

## Planning Findings
No blocking planning findings recorded.

## Advisory Notes
- Optional Governance pointers remain compatibility facts only.

## Lifecycle Notes
- Next lifecycle action: `fsgg-sdd tasks --work 2753-comment-mutation-verb`.
