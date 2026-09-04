---
schemaVersion: 1
workId: 3194-coherent-release
title: Coherent Release
stage: plan
changeTier: tier1
status: planned
sourceSpec: work/3194-coherent-release/spec.md
sourceClarifications: work/3194-coherent-release/clarifications.md
sourceChecklist: work/3194-coherent-release/checklist.md
publicOrToolFacingImpact: true
---

# Coherent Release Plan

Prose status: planned

## Source Snapshot
- spec: work/3194-coherent-release/spec.md sha256:f9b654d4c9947a3cc4351ad022ae359556f0013822aa0d760cb8ba6d63bdd18d schemaVersion:1
- clarifications: work/3194-coherent-release/clarifications.md sha256:ab60473af9b4d2f95b3068d053e51fc2053a5bdd2a31e9d174863a97e6ed0f56 schemaVersion:1
- checklist: work/3194-coherent-release/checklist.md sha256:51a251925087e7b3dbcb0dcb9a1349563adefa3be7abd8effbfa2305835c3625 schemaVersion:1

## Plan Scope
- Work item 3194-coherent-release is planned from the current specification, clarification, and checklist facts.
- Requirement count: 4.
- Clarification decision count: 0.
- Checklist result count: 4.

## Plan Decisions
- PD-001 [AC-001] [FR-001] complete: Treat `4b93b10d8baf67c283aeed2f71b955cc7e721872`
  as the immutable source of coherent set 0.80.0. Resume the existing prepare-once saga and component
  workflow attempts; never push tags directly or repack the stored artifacts.
- PD-002 [AC-001] [FR-002] complete: Verify all three sibling tags resolve to the exact source, then
  download FS.GG.Coord.Cli, FS.GG.Kit, and FS.GG.Drivers 0.80.0 independently from GitHub Packages and
  NuGet.org. Compare normalized archive entries excluding only NuGet.org's `.signature.p7s`, and run
  clean public tool/package restores.
- PD-003 [AC-001] [FR-003] complete: Capture the promoted coherent release URL, stable-channel and release
  manifest digests, package content digests, publisher attempts, receiver dispatch outcomes, and exact
  protected-main checks. Any independently owned receiver repair is filed and boarded before completion.
- PD-004 [AC-001] [FR-004] complete: Append claim, SDD, publication, verification, reconciliation,
  review, merge, and done phases to the item lifecycle ledger with whole-minute durations, OpenAI
  `gpt-5.6-sol` medium, Codex 0.153.0, coord 0.80.0.0, SDD 1.5.0, contracts 7.5.2, and measured or
  explicitly unavailable token provenance.

## Contract Impact
- PC-001 [PD-001] packageRelease: Stable version 0.80.0 changes package content while preserving all
  three package identities, the public CLI verb contract, sibling tag namespaces, and stable-channel
  schema. The release is compatible on the existing 0.x line and is sourced from canonical main.

## Verification Obligations
- VO-001 [PD-001] [PC-001] releaseObservation: Require promoted release and sibling-tag equality,
  dual-feed availability and entry equality, isolated restores, package/release coherence, engine
  freshness through the release source, receiver dispatch receipts, and green generated projections.

## Performance Intent
No performance intent is declared for this work item.

## Migration Posture
- PM-001 [PC-001] additive: Existing consumers remain valid on 0.79.0 and may adopt 0.80.0 through the
  normal receiver dispatch or Renovate paths. No package rename, configuration migration, or breaking
  command change is introduced.

## Generated View Impact
- GV-001 [PD-001] workModel: `readiness/3194-coherent-release/work-model.json` and `analysis.json` are
  regenerated from the authored package and must reach `implementationReady`; registry, compatibility,
  architecture, and distributed-pin projections must be regenerated only from observed public state.

## Accepted Deferrals
No accepted plan deferrals recorded.

## Planning Findings
No blocking planning findings recorded.

## Advisory Notes
- Optional Governance pointers remain compatibility facts only.

## Lifecycle Notes
- Next lifecycle action: `fsgg-sdd tasks --work 3194-coherent-release`.
