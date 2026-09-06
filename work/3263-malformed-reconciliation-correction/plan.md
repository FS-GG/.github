---
schemaVersion: 1
workId: 3263-malformed-reconciliation-correction
title: Human-authorized synthetic lifecycle checkpoint
stage: plan
changeTier: tier1
status: planned
sourceSpec: work/3263-malformed-reconciliation-correction/spec.md
sourceClarifications: work/3263-malformed-reconciliation-correction/clarifications.md
sourceChecklist: work/3263-malformed-reconciliation-correction/checklist.md
publicOrToolFacingImpact: true
---

# Human-authorized synthetic lifecycle checkpoint Plan

Prose status: planned

## Source Snapshot
- spec: work/3263-malformed-reconciliation-correction/spec.md sha256:cc6780f4cb608e1d425bd4dcd5dbf04ddd7c25c8aeebd77b10c916dcfc00dacf schemaVersion:1
- clarifications: work/3263-malformed-reconciliation-correction/clarifications.md sha256:cf542ca166c430b042e2b45ab854a3cdaa6b516f87c52b87caca6348a2df4043 schemaVersion:1
- checklist: work/3263-malformed-reconciliation-correction/checklist.md sha256:3edf7e9510396cb34ca26e54dedf490c5f963c16e551059f939f59c5b388321d schemaVersion:1

## Plan Scope
- Implement the proof parser/canonicalizer, checkpoint-aware validator, CLI plumbing, regression and mutation controls, projected guidance, ADR, and coherent release.

## Plan Decisions
- PD-001 [AC-001] [FR-001] complete: Add a closed versioned proof schema with exact scope/frontier flags and canonical SHA-256 digest.
- PD-002 [AC-001] [FR-002] complete: Require non-empty all-passed functional checks with immutable GitHub-comment or `sha256:<64hex>` evidence.
- PD-003 [AC-001] [FR-003] complete: Recognize one immediate `synthetic-evidence-checkpoint` completed event whose sole evidence is `synthetic-checkpoint:sha256:<proof-digest>`; its digest is the trusted anchor.
- PD-004 [AC-002] [FR-004] complete: Reject absent/wrong/reused/ambiguous/tampered proof and checkpoint combinations before applying any substitution.
- PD-005 [AC-002] [FR-005] complete: Split structural validation from evidence validation so canonical shape, identities, ordering, timestamps, transitions, and chain digests remain mandatory across the full history.
- PD-006 [AC-003] [FR-006] complete: Apply strict evidence and reconciliation validation to the suffix after the checkpoint; a malformed later event remains red.
- PD-007 [AC-004] [FR-007] complete: Add closed extraordinary reason values and ship as the next 0.x minor coherent set before registry and consumer adoption.

## Contract Impact
- PC-001 [PD-001] lifecycle telemetry contract: `Telemetry.validateLifecycle`, CLI `--synthetic-checkpoint`, output `syntheticCheckpoint`, proof schema, and worker skills are public/tool-facing and backward-compatible for ordinary histories.

## Verification Obligations
- VO-001 [PD-001] [PC-001] semanticTest: Positive current-shape fixture, named negative controls, observed-red mutations for authorization/scope/frontier/one-time/tamper/functional/strict-resumption gates, full Core and CLI suites, telemetry parity, skill quality, SDD verify/ship, and fresh package consumption.

## Performance Intent
No performance intent is declared for this work item.

## Migration Posture
- PM-001 [PC-001] compatibleAppendOnly: Histories without the proof are unchanged; older consumers reject checkpoint input; upgraded consumers require explicit exact authorization.

## Generated View Impact
- GV-001 [PD-001] skillProjection: Regenerate `.agents` and `.claude` pnext-item/work-roadmap mirrors and registry locks; parity gates reject stale output.

## Accepted Deferrals
No accepted deferrals recorded.

## Planning Findings
No blocking planning findings recorded.

## Advisory Notes
- Optional Governance pointers remain compatibility facts only.

## Lifecycle Notes
- Next lifecycle action: `fsgg-sdd tasks --work 3263-malformed-reconciliation-correction`.
