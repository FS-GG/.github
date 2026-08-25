---
schemaVersion: 1
workId: 2941-coherent-release
title: Coherent Release
stage: plan
changeTier: tier1
status: planned
sourceSpec: work/2941-coherent-release/spec.md
sourceClarifications: work/2941-coherent-release/clarifications.md
sourceChecklist: work/2941-coherent-release/checklist.md
publicOrToolFacingImpact: true
---

# Coherent Release Plan

Prose status: planned

## Source Snapshot
- spec: work/2941-coherent-release/spec.md sha256:fe890de272e952eb62514e5ba219dccb6136dffe371893f30aff3092b6d9f7d4 schemaVersion:1
- clarifications: work/2941-coherent-release/clarifications.md sha256:e8185113bc3eb3b300583c97c9077ee3fcca75c66b501e2fcccf06f3af71986e schemaVersion:1
- checklist: work/2941-coherent-release/checklist.md sha256:1cb02f187d29e627549aaae984aa5c668a4eda6be48c9bd0b7c3553e3f7451cc schemaVersion:1

## Plan Scope
- Work item 2941-coherent-release is planned from the current specification, clarification, and checklist facts.
- Requirement count: 5.
- Clarification decision count: 5.
- Checklist result count: 5.

## Plan Decisions
- PD-001 [AC-001] [FR-001] complete: Change the single `FsggCoherentSetVersion` in
  `Directory.Build.props` from immutable published `0.75.3` to unused `0.75.4`, with a dated rationale
  that records why `0.75.3` cannot be repacked and names the nine engine commits, zero wire-surface
  commits, and one defect-class merge. Replace `FS.GG.Coord.Cli.fsproj`'s bounded
  `PackageReleaseNotes` head with `0.75.4` and accurately describe the compatible exact-merge completion
  and serialized `Blocked by` repairs. Update the distributed tool pin from stale `0.74.0` to
  observed-public `0.75.3`, not to unpublished `0.75.4`.
- PD-002 [AC-002] [FR-002] complete: Declare a `release-verification` post-merge obligation bound to the
  accepted PR head. Because `0.75.4` is the next same-line patch above both feed frontiers,
  `kit-auto-publish.yml` owns calling `release-saga-prepare.yml` against the exact merge SHA and then
  atomically pushing the three sibling component tags only after preparation succeeds. Verify its draft
  `coherent-set/v0.75.4` manifest names exactly `FS.GG.Coord.Cli`, `FS.GG.Kit`, and `FS.GG.Drivers`;
  do not duplicate the automated act or locally publish/repack.
- PD-003 [AC-003] [FR-003] complete: Observe all three package workflows consume the stored saga bytes,
  GitHub Packages complete first, and nuget.org complete second. Download every served archive from both
  feeds, compare all normalized non-signature entries to the prepared manifest, verify public nuspec
  source metadata, and use fresh isolated tool/package restores to execute Coord.Cli and reach Kit and
  Drivers content.
- PD-004 [AC-004] [FR-004] complete: Treat feed-autofix's publish-before-flip PR as a generated candidate,
  never as sufficient judgement. Reconcile `coord-engine.version` and `package-version`, prepend the
  dated registry changelog evidence, regenerate `docs/registry/compatibility.md`,
  `.agents/skills/publishing-and-deployment/SKILL.md`, and `docs/architecture.md`, then review and land
  that follow-up through the normal gated route. The architecture map's shape is expected unaffected;
  only its generated version projection moves.
- PD-005 [AC-005] [FR-005] complete: Wait for `release-saga-promote.yml` to publish the immutable
  `coherent-set/v0.75.4` release and stable-channel receipt; run release coherence and engine freshness
  against reconciled `origin/main`; then rerun/dispatch the exact-merge verification that exposed this
  blocker and retain the #2941 claim until all typed obligations and completion projections converge.

## Contract Impact
- PC-001 [PD-001] [PD-002] command report: the public package version advances to stable `0.75.4` while
  package ids, command names, workflow filenames, tag namespaces, stable-channel schema, and
  `Protocol.fs` remain unchanged. Receiver-visible lifecycle semantics are additive on the stable 0.x
  line. With `wireCount=0` and no removed member or identity, this is a compatible PATCH.

## Verification Obligations
- VO-001 [PD-001] [PC-001] semanticTest: Before review, run coherent-set-version,
  engine-release-notes, kit-published-coherence (including its automatic-patch obligation arm), release-saga,
  engine-freshness, source/registry/projection gates, Release build/test/pack, nuspec version checks, and
  the repository validation harness. The changed release declaration has no new gate implementation, so
  gate inversion is supplied by existing fixture legs that reject a stale version token, absent manual
  duplicate manual release obligation, mismatched sibling tags, and feed/manifest divergence.
- VO-002 [PD-002] [PD-003] releaseObservation: Post-merge evidence records prepare/publish/promotion run
  URLs, exact merge SHA, sibling tag resolutions, prepared manifest/content ids, both-feed downloads and
  per-entry comparisons, and isolated public install/restore output.
- VO-003 [PD-004] [PD-005] readiness: The feed-derived reconciliation PR and final `origin/main` must make
  the registry validator, generated projections, `check-release-coherence.py`, and
  `check-engine-freshness.py` green with `releaseOwed=false`; no pre-publication source diff may claim
  those external facts.

## Performance Intent
No performance intent is declared for this work item.

## Migration Posture
- PM-001 [PC-001] additive: Consumers may remain temporarily pinned at `0.75.3` while the stable release
  is promoted, then update through the existing dashboard/Renovate/receiver routes. No package identity,
  configuration migration, or breaking command change is introduced.

## Generated View Impact
- GV-001 [PD-001] workModel: `readiness/2941-coherent-release/work-model.json` and `analysis.json` are
  regenerated from the authored SDD sources by `tasks`/`analyze` and must reach `implementationReady`.
- GV-002 [PD-004] registryProjection: Feed-derived reconciliation regenerates the compatibility,
  architecture, and publishing-skill version tables from `registry/dependencies.yml`; hand-editing only
  one projection is not accepted.

## Accepted Deferrals
No accepted plan deferrals recorded.

## Planning Findings
No blocking planning findings recorded.

## Advisory Notes
- Optional Governance pointers remain compatibility facts only.

## Lifecycle Notes
- Next lifecycle action: `fsgg-sdd tasks --work 2941-coherent-release`.
