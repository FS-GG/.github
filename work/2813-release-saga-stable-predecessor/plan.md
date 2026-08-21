---
schemaVersion: 1
workId: 2813-release-saga-stable-predecessor
title: Release Saga Stable Predecessor
stage: plan
changeTier: tier1
status: planned
sourceSpec: work/2813-release-saga-stable-predecessor/spec.md
sourceClarifications: work/2813-release-saga-stable-predecessor/clarifications.md
sourceChecklist: work/2813-release-saga-stable-predecessor/checklist.md
publicOrToolFacingImpact: true
---

# Release Saga Stable Predecessor Plan

Prose status: planned

## Source Snapshot
- spec: work/2813-release-saga-stable-predecessor/spec.md sha256:f2ba19c6d3addb193d6d5c8ee0b9fd85102038c8e3d630093c0c4eb850f87537 schemaVersion:1
- clarifications: work/2813-release-saga-stable-predecessor/clarifications.md sha256:72268170d2e12918dc75feda0a9db9e8a37e12abedcb1f4941d4cb75bf718d37 schemaVersion:1
- checklist: work/2813-release-saga-stable-predecessor/checklist.md sha256:201dc12c3dbb9fcea3c31247070b0c423e32c2e2bcd5688b5ce3b34d2619fc6f schemaVersion:1

## Plan Scope
- Work item 2813-release-saga-stable-predecessor is planned from the current specification, clarification, and checklist facts.
- Requirement count: 4.
- Clarification decision count: 4.
- Checklist result count: 4.

## Plan Decisions
- PD-001 [AC-001] [AC-002] [FR-001] complete: Add a `predecessor` decision command to
  `scripts/release-saga.py` that validates one stable-channel receipt against its published coherent-set
  tag and peeled tag source, then emits its canonical identity. Preparation consumes the receipt itself
  and records `previousStableVersion` plus new `previousStableContentId`; the old caller-supplied
  `--previous-version` authority is removed.
- PD-002 [AC-001] [AC-002] [FR-001] complete: Reorder `release-saga-prepare.yml` so its first
  release-specific step identifies the latest non-draft `coherent-set/v*`, downloads
  `stable-channel.json`, resolves the tag source, and runs the new decision command before build/test/pack.
  Carry the validated file forward; do not read `registry/dependencies.yml` anywhere in preparation.
- PD-003 [AC-003] [FR-002] complete: Extend reusable descriptor comparison with
  `previousStableContentId`. Candidate creation and stored-draft replay therefore share one exact predecessor
  tuple, and any live-baseline movement makes the retry refuse rather than reuse stale bytes.
- PD-004 [AC-004] [AC-005] [FR-003] complete: Extend the hermetic saga suite with malformed, missing,
  prerelease, tag/version, tag/source, and stale-retry controls; reproduce registry `0.68.0` alongside
  receipt `0.69.0`; prove preparation binds the receipt; model `0.70.0` as poisoned; and promote only a
  distinct `0.71.0` manifest whose predecessor tuple is `0.69.0`.
- PD-005 [AC-004] [FR-003] [FR-004] complete: Advance the coherent source scalar and all three package
  histories/notes to unused `0.71.0`, prepend a release-owed/poisoned-version changelog entry, and document
  live-receipt authority plus forward recovery. Do not change registry published-version scalars.
- PD-006 [AC-005] [FR-003] complete: Ship an observed-red mutation that removes the predecessor content-ID
  bind (or restores registry selection) and prove the focused suite fails. Record the exact command/result.

## Contract Impact
- PC-001 [PD-001] command report: `release-saga.py predecessor` is an additive operator command; the prepare
  invocation replaces a caller-supplied scalar with a receipt path, deliberately failing old unsafe callers.
- PC-002 [PD-001] [PD-003] manifest: `previousStableContentId` is an additive descriptor member and becomes
  mandatory for newly prepared/reused manifests. Existing immutable manifests remain readable for observation
  and promotion under their stored schema, but `0.70.0` is deliberately never promoted.
- PC-003 [PD-002] workflow: `release-saga-prepare.yml` keeps both triggers and source-SHA semantics while adding
  a mandatory, pre-pack GitHub release read. Missing/unreadable authority fails the job before irreversible work.
- PC-004 [PD-005] package: `0.71.0` is a coherent MINOR on stable 0.x because preparation authority and the
  manifest identity observable by all three publisher workflows change; no package/member identity is removed.

## Verification Obligations
- VO-001 [PD-001] [PD-003] [PC-001] [PC-002] semanticTest: `bash tests/release-saga/run.sh` exercises
  every receipt refusal, exact predecessor binding, retry identity, forward promotion, and legacy saga behavior.
- VO-002 [PD-002] [PC-003] staticGate: The focused suite parses the production workflow and proves live receipt
  resolution/validation precedes every build and pack token and that registry predecessor selection is absent.
- VO-003 [PD-004] [PD-006] mutationTest: Run the focused suite against a temporary source mutation that drops
  predecessor content identity or substitutes the stale registry; expect non-zero and the named failed assertion.
- VO-004 [PD-005] [PC-004] package: Build and pack all three coherent projects at `0.71.0`, inspect nuspec versions
  and release notes, and run existing release/coherence, projection, registry, and workflow gates affected by paths.
- VO-005 [PD-005] remoteInvariant: Before and after implementation, read `coherent-set/v0.70.0` release metadata,
  asset names/digests, three tag SHAs, and published feed payloads; compare canonical snapshots byte-for-byte.

## Performance Intent
No performance intent is declared for this work item.

## Migration Posture
- PM-001 [PC-001] [PC-002] failClosed: No fallback from missing `previousStableContentId` exists on the new
  preparation/reuse path. Already-published manifests remain observation inputs; recovery never rewrites them.

## Generated View Impact
- GV-001 [PD-001] workModel: readiness/2813-release-saga-stable-predecessor/work-model.json and analysis.json
  are regenerated from authored lifecycle sources and committed as receipts; they are never hand-edited.

## Accepted Deferrals
No accepted plan deferrals recorded.

## Planning Findings
No blocking planning findings recorded.

## Advisory Notes
- Optional Governance pointers remain compatibility facts only.

## Lifecycle Notes
- Next lifecycle action: `fsgg-sdd tasks --work 2813-release-saga-stable-predecessor`.
