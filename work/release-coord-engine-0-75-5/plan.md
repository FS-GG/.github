---
schemaVersion: 1
workId: release-coord-engine-0-75-5
title: Coherent coordination release 0.75.5
stage: plan
changeTier: tier1
status: planned
sourceSpec: work/release-coord-engine-0-75-5/spec.md
sourceClarifications: work/release-coord-engine-0-75-5/clarifications.md
sourceChecklist: work/release-coord-engine-0-75-5/checklist.md
publicOrToolFacingImpact: true
---

# Coherent coordination release 0.75.5 Plan

Prose status: planned

## Source Snapshot
- spec: work/release-coord-engine-0-75-5/spec.md sha256:e498163a3e805d806073f52ed8ba9430566eb372fb718836ca33503fa516eacb schemaVersion:1
- clarifications: work/release-coord-engine-0-75-5/clarifications.md sha256:5416785afedc037f1971c11ef56d5b22d4618b6320442dcfdcc668c9c9faad06 schemaVersion:1
- checklist: work/release-coord-engine-0-75-5/checklist.md sha256:63588e54fb6afac34efe5faab3ea1e242d8869db461901018d49b5c3dd2a250c schemaVersion:1

## Plan Scope
- Work item release-coord-engine-0-75-5 is planned from the current specification, clarification, and checklist facts.
- Requirement count: 5.
- Clarification decision count: 6.
- Checklist result count: 5.

## Plan Decisions
- PD-001 [AC-001] [FR-001] complete: Advance `FsggCoherentSetVersion` from `0.75.4` to `0.75.5`
  with a dated source comment naming the two compatible non-wire commits. Replace Coord.Cli's bounded
  release-note head with `0.75.5`, describing post-merge classification and full merge-OID completion.
  Leave feed-facing registry and public pins at observed `0.75.4` before publication.
- PD-002 [AC-002] [FR-002] complete: Declare mapped release obligations on the exact reviewed head.
  After guarded merge, let `kit-auto-publish.yml` call the existing prepare-once saga and atomically
  create the three sibling tags only after manifest preparation; never duplicate that act locally.
- PD-003 [AC-003] [FR-003] complete: Observe the three publishers consuming stored saga bytes, with
  GitHub Packages complete before nuget.org. Download all six served archives, compare normalized
  non-signature entries and nuspec source metadata, and perform fresh public installs/restores.
- PD-004 [AC-004] [FR-004] complete: Review and land the feed-derived reconciliation candidate through
  the normal route. Advance registry source/package facts, prepend changelog evidence, regenerate
  compatibility and publishing projections, record architecture shape as unaffected unless derivation
  proves otherwise, and advance declared tool/profile pins to verified public `0.75.5`.
- PD-005 [AC-005] [FR-005] complete: Wait for immutable promotion, then run release coherence and engine
  freshness against reconciled `origin/main`, verify the historical full-SHA receipts remain terminal,
  and retain the #2983 claim until every mapped obligation and completion projection converges.

## Contract Impact
- PC-001 [PD-001] [PD-002] package contract: the public coherent-set version advances compatibly from
  `0.75.4` to stable `0.75.5`; package ids, command names, workflow filenames, tag namespaces, stable
  channel schema, and `Protocol.fs` remain unchanged. This is a PATCH release.

## Verification Obligations
- VO-001 [PD-001] [PC-001] semanticTest: Before review, run coherent-set-version,
  engine-release-notes, kit/package tests, release-saga, feed-coherence, release-coherence,
  engine-freshness, registry/projection gates, Release build/test/pack, nuspec checks, and the repository
  validation harness. Preserve existing discriminating fixture inversions for version/tag/feed drift.
- VO-002 [PD-002] [PD-003] releaseObservation: Post-merge evidence records preparation/publication/
  promotion run URLs, exact merge SHA, sibling tag resolutions, manifest and package content ids,
  both-feed comparisons, nuspec provenance, and isolated public install output.
- VO-003 [PD-004] [PD-005] readiness: Feed-derived reconciliation and final `origin/main` must make
  registry/projection checks, release coherence, and engine freshness green with `releaseOwed=false`;
  no preparation diff may claim external publication facts early.

## Performance Intent
No performance intent is declared for this work item.

## Migration Posture
- PM-001 [PC-001] additive: Consumers may remain temporarily pinned at `0.75.4` during promotion, then
  update through existing dashboard/Renovate routes. No identity, configuration, or command migration
  is introduced.

## Generated View Impact
- GV-001 [PD-001] workModel: `readiness/release-coord-engine-0-75-5/work-model.json` and lifecycle
  readiness artifacts regenerate from authored SDD sources and must reach `implementationReady`.
- GV-002 [PD-004] registryProjection: Feed-derived reconciliation regenerates compatibility,
  architecture, and publishing-skill version tables from `registry/dependencies.yml`; a hand-edited
  partial projection is not accepted.

## Accepted Deferrals
No accepted plan deferrals recorded.

## Planning Findings
No blocking planning findings recorded.

## Advisory Notes
- Optional Governance pointers remain compatibility facts only.

## Lifecycle Notes
- Next lifecycle action: `fsgg-sdd tasks --work release-coord-engine-0-75-5`.
