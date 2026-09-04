---
schemaVersion: 1
workId: 3201-coherent-release
title: Coherent Release 0.80.1
stage: plan
changeTier: tier1
status: planned
sourceSpec: work/3201-coherent-release/spec.md
sourceClarifications: work/3201-coherent-release/clarifications.md
sourceChecklist: work/3201-coherent-release/checklist.md
publicOrToolFacingImpact: true
---

# Coherent Release 0.80.1 Plan

Prose status: planned

## Source Snapshot
- spec: work/3201-coherent-release/spec.md sha256:40097c1f87aafafe96501ed035ec989bf5ca7525eaa01968c1c134e408926ba0 schemaVersion:1
- clarifications: work/3201-coherent-release/clarifications.md sha256:9e6818849383bc196f4f9f071d3bdbe881d93efcb3583676502dbaea6ab58e4c schemaVersion:1
- checklist: work/3201-coherent-release/checklist.md sha256:9a38b2b0038ec218614b8aca19fd7d8fe136bb3b154039c528edffed40b80928 schemaVersion:1

## Plan Scope
- Work item 3201-coherent-release is planned from the current specification, clarification, and checklist facts.
- Requirement count: 3.
- Clarification decision count: 0.
- Checklist result count: 3.

## Plan Decisions
- PD-001 [AC-001] [FR-001] complete: Create one reviewed release-source change on top of
  `eb26e156827671382805500c71e4b4d04bbc6977` that bumps the coherent set and governed projections to
  0.80.1 while retaining the exact supervisor telemetry skill and semantic-test bytes merged for
  `.github#3199`. The guarded merge commit, rather than the candidate head, becomes the immutable release
  source consumed by the automatic prepare-once saga; no component tag is pushed directly.
- PD-002 [AC-001] [FR-002] complete: After the automatic saga promotes 0.80.1, require all three sibling
  tags to resolve to its stored source, download FS.GG.Coord.Cli, FS.GG.Kit, and FS.GG.Drivers from
  GitHub Packages and NuGet.org, compare normalized archive entries excluding only NuGet.org's
  `.signature.p7s`, and exercise clean public tool/package installs including packaged skill-byte checks.
- PD-003 [AC-001] [FR-003] complete: Retain the item claim through publication and reconciliation; record
  protected-main checks, immutable release evidence, stable-channel and manifest digests, receiver
  adoption outcomes, registry/pin/documentation coherence, and append-only phase telemetry before Done.

## Contract Impact
- PC-001 [PD-001] packageRelease: Stable version 0.80.1 changes package and embedded skill content while
  preserving all three package identities, public CLI contracts, sibling tag namespaces, and the
  stable-channel schema. It is a compatible patch on the existing 0.x line sourced from guarded main.

## Verification Obligations
- VO-001 [PD-001] [PC-001] releaseObservation: Require a promoted release and sibling-tag equality,
  dual-feed package availability and normalized entry equality, isolated restores, semantic supervisor
  enforcement in source and packaged skills, package/release coherence, receiver delivery receipts,
  registry/pin/documentation projections, and green protected-main checks at the exact release source.

## Performance Intent
No performance intent is declared for this work item.

## Migration Posture
- PM-001 [PC-001] additive: Existing consumers remain valid on 0.80.0 and may adopt 0.80.1 through the
  normal receiver dispatch or Renovate paths. No package rename, configuration migration, or breaking
  command change is introduced.

## Generated View Impact
- GV-001 [PD-001] workModel: `readiness/3201-coherent-release/work-model.json` and `analysis.json` are
  regenerated from this authored package and must reach `implementationReady`; version, registry,
  compatibility, architecture, distributed-pin, profile, and consumer-versioning projections are
  regenerated only from the chosen coherent version and later observed public state.

## Accepted Deferrals
No accepted plan deferrals recorded.

## Planning Findings
No blocking planning findings recorded.

## Advisory Notes
- Optional Governance pointers remain compatibility facts only.

## Lifecycle Notes
- Next lifecycle action: `fsgg-sdd tasks --work 3201-coherent-release`.
