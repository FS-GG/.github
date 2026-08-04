---
schemaVersion: 1
workId: 2144-quoted-diff-inventory
title: Quoted semantic-diff receipt
stage: plan
changeTier: tier1
status: planned
sourceSpec: work/2144-quoted-diff-inventory/spec.md
sourceClarifications: work/2144-quoted-diff-inventory/clarifications.md
sourceChecklist: work/2144-quoted-diff-inventory/checklist.md
publicOrToolFacingImpact: true
---

# Quoted semantic-diff receipt Plan

Prose status: planned

## Source Snapshot
- spec: work/2144-quoted-diff-inventory/spec.md sha256:60d0d40808c48cc896f21ae124a428efa454543ca41045844ab3c07344aed498 schemaVersion:1
- clarifications: work/2144-quoted-diff-inventory/clarifications.md sha256:892407dae0ff3c3a8528583e086ce4ffb5fee9468ec39b52a124876db7e580fb schemaVersion:1
- checklist: work/2144-quoted-diff-inventory/checklist.md sha256:12fad0ee57036c1120efffb32bd35b206826a70851e1b7b8f8c5a5597c2c5332 schemaVersion:1

## Plan Scope
- Work item 2144-quoted-diff-inventory is planned from the current specification, clarification, and checklist facts.
- Requirement count: 1.
- Clarification decision count: 0.
- Checklist result count: 1.

## Plan Decisions
- PD-001 [AC-001] [FR-001] complete: Add a pure, typed semantic-diff model that classifies changed source spans and validates a versioned, SHA-bound receipt; keep Git invocation and file reads at the CLI edge.
- PD-002 [AC-001] [FR-001] complete: Measure threshold activation in semantic occurrences on the no-receipt path too, by recovering the rename tokens from the live PR base/head blobs (`SemanticDiff.discoverRenames`), rather than substituting the changed-file count. Evidence that cannot be read requires the receipt; a missing fact is never converted into a negative one. Authorized as one repair phase by https://github.com/FS-GG/.github/issues/2144#issuecomment-5170895957 after the ordinary three-round ceiling on closed PR #2149.

## Contract Impact
- PC-001 [PD-001] command report: expose `diff-audit` as a JSON-first local CLI command accepting repository root, base SHA, head SHA, declared paths, and a receipt; require its current, complete result from the review-acceptance parser when the audit is required.

## Verification Obligations
- VO-001 [PD-001] [PC-001] semanticTest: prove word-boundary F# identifier renames inside ordinary, escaped, and interpolated strings are inventoried; prove comments, generated files, and identifier-only controls; prove missing, stale, or unresolved receipt fields reject acceptance.

## Performance Intent
No performance intent is declared for this work item.

## Migration Posture
- PM-001 [PC-001] diagnoseOnly: receipt schema version 1 is the only accepted contract; an unknown version, duplicate occurrence, unrecognized disposition, or SHA/path mismatch is a diagnostic and cannot be accepted.

## Generated View Impact
- GV-001 [PD-001] workModel: generate the operational worker/critic rule from the typed receipt contract, then refresh the work model and fail the projection gate on drift.

## Accepted Deferrals
No accepted plan deferrals recorded.

## Planning Findings
No blocking planning findings recorded.

## Advisory Notes
- Optional Governance pointers remain compatibility facts only.

## Lifecycle Notes
- Next lifecycle action: `fsgg-sdd tasks --work 2144-quoted-diff-inventory`.
