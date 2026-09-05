---
schemaVersion: 1
workId: 3210-roadmap-work-unit-compiler
title: Roadmap Work Unit Compiler
stage: plan
changeTier: tier1
status: planned
sourceSpec: work/3210-roadmap-work-unit-compiler/spec.md
sourceClarifications: work/3210-roadmap-work-unit-compiler/clarifications.md
sourceChecklist: work/3210-roadmap-work-unit-compiler/checklist.md
publicOrToolFacingImpact: true
---

# Roadmap Work Unit Compiler Plan

Prose status: planned

## Source Snapshot
- spec: work/3210-roadmap-work-unit-compiler/spec.md sha256:3512f8212ddfbdd8e48d12d6939b291a118bfd55ee0f53dd95c9fe5aecf50680 schemaVersion:1
- clarifications: work/3210-roadmap-work-unit-compiler/clarifications.md sha256:29975389cf7929ec79699578b03ddadb0d771973c7ad4384fb32a0ca0650a5ff schemaVersion:1
- checklist: work/3210-roadmap-work-unit-compiler/checklist.md sha256:64b28b6193a1711ed8c59ee35cf6c24001c1f4cb7031ca91e1f3f0b7e175ab39 schemaVersion:1

## Plan Scope
- Work item 3210-roadmap-work-unit-compiler is planned from the current specification, clarification, and checklist facts.
- Requirement count: 10.
- Clarification decision count: 0.
- Checklist result count: 10.

## Plan Decisions
- PD-001 [AC-001] [FR-001] complete: Add `RoadmapWorkUnit` closed types and a pure selector over ordered catalog rows and accepted-unit identities, returning precise selection refusals.
- PD-002 [AC-002] [FR-002] complete: Model authority pin, unit registration, gate registration, and evidence obligations as a canonical preparation plan with bounded text patches and stable content identities.
- PD-003 [AC-003] [FR-003] complete: Compose existing lifecycle/review and qualification validators at the typed core boundary; parse their canonical JSON in the CLI without weakening either contract.
- PD-004 [AC-004] [FR-004] complete: Represent SDD obligations as expected command/stage/artifact triples and admit only observed successful execution receipts bound to current source digests.
- PD-005 [AC-005] [FR-005] complete: Add an explicit five-revision identity set and ancestry facts; validate phase roles before acceptance or roadmap-close rendering.
- PD-006 [AC-006] [FR-006] complete: Canonicalize unsigned receipt and index payloads, compute one transaction digest over both, then render/verify the two sealed documents as an inseparable set.
- PD-007 [AC-007] [FR-007] complete: Expose pure `roadmap unit prepare inspect|render|verify` and `roadmap unit accept inspect|render|verify`; board-ops applies generated intake drafts exclusively through existing `IntakeApplication`/receipt/cache locking.
- PD-008 [AC-008] [FR-008] complete: Adapt the sealed acceptance to `RoadmapClosure.Inputs`/canonical evidence instead of adding another close implementation.
- PD-009 [AC-009] [FR-009] complete: Add core, CLI, and board-ops positive/inverted matrices including deterministic replay, partial-state recovery, duplicate refusal/reuse, and close handoff.
- PD-010 [AC-010] [FR-010] complete: Bump and pack the coherent coordination set, verify package bytes at both feeds/receiver, update `work-roadmap` and registry digests/changelog, then execute a clean later-GS2 fixture and record comparison evidence.

## Contract Impact
- PC-001 [PD-001] publicApi: `FS.GG.Coord.Core` gains additive typed roadmap work-unit preparation and acceptance contracts.
- PC-002 [PD-003] wireSchema: canonical preparation input/plan and acceptance input/receipt/index schemas are closed, versioned, digest-bound JSON.
- PC-003 [PD-007] commandReport: `roadmap unit prepare|accept inspect|render|verify` is an additive CLI family with deterministic stdout/file behavior and typed nonzero refusals.
- PC-004 [PD-007] mutationBoundary: board-ops accepts only compiler-produced staged-intake drafts and delegates to #3105's existing transaction, cache, lock, and authoritative readback.
- PC-005 [PD-008] internalComposition: accepted evidence maps into the existing `RoadmapClosure` contract without a second closure schema.
- PC-006 [PD-010] driverSkill: `work-roadmap` and its manifest/registry rows advance as one coherent migration after package publication.

## Verification Obligations
- VO-001 [PD-001] [PC-001] unitTest: Positive selection plus zero/multiple/unknown/duplicate/skipped/already-accepted inversions.
- VO-002 [PD-002] [PC-002] unitTest: Canonical preparation byte replay, bounded patch validation, catalog-row identity mismatch, and obligation completeness.
- VO-003 [PD-003] [PC-002] integrationTest: Valid #3208/#3209 fixtures and stale/digest/subject/role substitutions produce exact typed refusals.
- VO-004 [PD-004] [PC-002] mutationTest: Authored or synthetic SDD evidence, stale source snapshots, and missing observed execution each fail independently.
- VO-005 [PD-005] [PC-002] mutationTest: Every forbidden identity collapse and ancestry substitution is inverted independently.
- VO-006 [PD-006] [PC-002] unitTest: Receipt/index bytes replay, transaction cross-digest verification, partial/missing/extra/substituted document refusal.
- VO-007 [PD-007] [PC-003] cliTest: Inspect/render/verify round trips and malformed inputs for both command families.
- VO-008 [PD-007] [PC-004] integrationTest: Existing, interrupted, conflicting, and duplicate staged-intake state converges through the single #3105 mutation boundary.
- VO-009 [PD-008] [PC-005] integrationTest: Accepted compiler output is consumed by `roadmap close inspect|render|verify` without translation loss.
- VO-010 [PD-010] [PC-006] releaseTest: Full suites, black-box parity, SDD analyze/verify/ship, coherent pack/publication byte equality, receiver verification, skill-quality, and a clean later-GS2 end-to-end pilot.

## Performance Intent
No performance intent is declared for this work item.

## Migration Posture
- PM-001 [PC-003] additive: Ship compiler APIs and CLI before any skill references them; old commands remain unchanged.
- PM-002 [PC-004] reuse: Staged intake is composed, not forked; no data migration or second cache format is introduced.
- PM-003 [PC-006] publishBeforeFlip: Publish byte-identical coherent packages to both feeds and verify a receiver before changing `work-roadmap` or registry digests.

## Generated View Impact
- GV-001 [PD-001] workModel: Regenerate `readiness/3210-roadmap-work-unit-compiler/work-model.json` after every authored lifecycle change and require its exact source digests at analyze, verify, and ship gates.

## Accepted Deferrals
No accepted plan deferrals recorded.

## Planning Findings
No blocking planning findings recorded.

## Advisory Notes
- Optional Governance pointers remain compatibility facts only.

## Lifecycle Notes
- Next lifecycle action: `fsgg-sdd tasks --work 3210-roadmap-work-unit-compiler`.
