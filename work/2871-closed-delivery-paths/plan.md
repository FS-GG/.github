---
schemaVersion: 1
workId: 2871-closed-delivery-paths
title: Preserve declared paths across closed-item delivery
stage: plan
changeTier: tier1
status: planned
sourceSpec: work/2871-closed-delivery-paths/spec.md
sourceClarifications: work/2871-closed-delivery-paths/clarifications.md
sourceChecklist: work/2871-closed-delivery-paths/checklist.md
publicOrToolFacingImpact: true
---

# Preserve declared paths across closed-item delivery Plan

Prose status: planned

## Source Snapshot
- spec: work/2871-closed-delivery-paths/spec.md sha256:17d446247f6a0f38b9bff29790975e3679910778831a7198932bf872c26307d6 schemaVersion:1
- clarifications: work/2871-closed-delivery-paths/clarifications.md sha256:1394fd166a483968a50bddb78d5ceba323ec8a7f853782cc2bfcea4dcdfb289c schemaVersion:1
- checklist: work/2871-closed-delivery-paths/checklist.md sha256:66c37dd400f3bbb3fc6cb386f677d8af470aada9220fdeffd425521fe1a03e0f schemaVersion:1

## Plan Scope
- Replace delivery's closure-sensitive touch-set projection with one
  authoritative issue-body read while preserving the board scan for status and
  scheduling facts.
- Exercise the production CLI transport route in the existing e2e harness.

## Plan Decisions
- PD-001 [DEC-001] [AC-001] [FR-001] complete: Call `Reads.issueBody`
  after the fresh board candidate is established and parse it with the single
  `TouchSet.parse` grammar.
- PD-002 [DEC-002] [AC-002] [FR-002] complete: Carry a body read error as
  `Delivery.Unread (Errors.explain error)`; never synthesize an empty body.
- PD-003 [DEC-001] [AC-003] [FR-003] complete: Use the parsed authoritative
  touch set for both `DeclaredPaths` and `deliveryPathClassifier`, keeping a
  truly absent declaration typed as `Undeclared`.
- PD-004 [DEC-002] [AC-004] [FR-004] complete: Leave claim, review,
  authorization, PR, and completion logic unchanged.

## Contract Impact
- PC-001 [PD-001] [PD-002] liveCommand: `fsgg-coord delivery` becomes
  closure-stable while retaining its existing typed result vocabulary.

## Verification Obligations
- VO-001 [PD-001] [PD-003] [PC-001] productionRoute: In the e2e HTTP fixture,
  hold issue body bytes constant, vary open/closed state, and observe identical
  declared paths through live `delivery`.
- VO-002 [PD-002] [PC-001] negativeControl: Make the issue-body endpoint
  unreadable and prove delivery refuses with an unread diagnosis; restore a
  readable body without `Paths:` and prove the distinct undeclared diagnosis.
- VO-003 [PD-004] regression: Run the lifecycle unit suites, full coordination
  engine e2e harness, build, formatting, and SDD analyze gates.
- VO-004 [PD-001] [PD-002] gateInversion: Temporarily restore the board
  projection as the delivery source and show the new closed-item control red;
  temporarily collapse the failed read to `Undeclared` and show the unread
  control red.

## Performance Intent
No performance intent is declared for this work item.

## Migration Posture
- PM-001 [PC-001] inPlace: No migration or compatibility shim is required;
  existing callers receive the same result schema with corrected authoritative
  facts.

## Generated View Impact
- GV-001 [PD-001] workModel: Regenerate the SDD work model and analysis from
  the authored plan before implementation; generated readiness artifacts are
  never hand-edited.

## Accepted Deferrals
No accepted plan deferrals recorded.

## Planning Findings
No blocking planning findings recorded.

## Advisory Notes
- Optional Governance pointers remain compatibility facts only.

## Lifecycle Notes
- Next lifecycle action: `fsgg-sdd tasks --work 2871-closed-delivery-paths`.
