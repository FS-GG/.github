---
schemaVersion: 1
workId: 2727-lifecycle-extraction
title: Lifecycle CLI extraction and typed completion dependency
stage: plan
changeTier: tier1
status: planned
sourceSpec: work/2727-lifecycle-extraction/spec.md
sourceClarifications: work/2727-lifecycle-extraction/clarifications.md
sourceChecklist: work/2727-lifecycle-extraction/checklist.md
publicOrToolFacingImpact: true
---

# Lifecycle CLI extraction and typed completion dependency Plan

Prose status: planned

## Source Snapshot
- spec: work/2727-lifecycle-extraction/spec.md sha256:84ee474b1c2e9f970ec9e6dc02d0e02f8e11abe2b735f0f3ba7a1f26beefb5f4 schemaVersion:1
- clarifications: work/2727-lifecycle-extraction/clarifications.md sha256:ae096e67e6ad9f57aaa43f97461dd4aeb6714afb4d4eba58ec4997d6a94ff3d1 schemaVersion:1
- checklist: work/2727-lifecycle-extraction/checklist.md sha256:95a511c008395d6c2407dab420abda41d9a0c9689de8fed2bd5ce043082939a8 schemaVersion:1

## Plan Scope
- Extract the seven issue-listed lifecycle command families from the existing CLI assembly into a
  new `FS.GG.Coord.Cli.Lifecycle` project, retaining Kernel/Options contracts and existing adapters.
- Define Lifecycle handler registration through the composition boundary established by #2726.
- Replace `completeDelivery`'s mutable forward reference with a typed completion-operation parameter
  passed when the Lifecycle handlers are constructed.
- Move focused lifecycle cases from `ApplicationServiceTests.fs` into
  `FS.GG.Coord.Cli.Lifecycle.Tests` and preserve black-box, pack, and release-payload coverage.

## Plan Decisions
- PD-001 [AC-001] [FR-001] complete: Lifecycle exports a handler-composition function with an
  explicit completion-operation parameter; `delivery` captures that value directly and cannot run
  through an uninitialized placeholder.
- PD-002 [AC-001] [FR-001] complete: Keep parsing and shared Context/Identity/Options contracts in
  the Kernel/current CLI boundary; Lifecycle owns the selected handlers and direct helpers.
- PD-003 [AC-001] [FR-001] complete: Preserve the existing e2e suites while moving focused unit tests
  into the new family test project and keeping every command registered exactly once.

## Contract Impact
- PC-001 [PD-001] internal dependency: completion becomes a typed function dependency instead of a
  mutable initialization cell; the transaction implementation and observable behavior are unchanged.
- PC-002 [PD-002] project boundary: Lifecycle references Kernel/Core/GitHub contracts already used by
  the CLI; no external package contract is added.
- PC-003 [PD-003] release payload: the new assembly must be present wherever existing CLI project
  references are packed or copied.

## Verification Obligations
- VO-001 [PD-001] [PC-001] semanticTest: prove source and built output contain neither the mutable
  completion cell nor its placeholder failure, and demonstrate a mutation restoring the forbidden
  shape makes the gate fail.
- VO-002 [PD-002] [PC-002] build: build Release and run the focused CLI and Lifecycle test projects.
- VO-003 [PD-003] [PC-003] packaging: pack the CLI and verify the Lifecycle assembly is in the payload.
- VO-004 [PD-003] [PC-001] compatibility: run existing coordination engine e2e suites covering moved
  commands and the command-case-to-handler exhaustiveness test.

## Performance Intent
No performance intent is declared for this work item.

## Migration Posture
- PM-001 [PC-001] atomic: land the typed dependency, Lifecycle project, handler composition, moved
  commands, tests, and payload wiring together; no intermediate state may omit or double-register a verb.

## Generated View Impact
- GV-001 [PD-001] workModel: refresh SDD readiness after implementation evidence is recorded;
  coordination projections remain unchanged because the CLI command contract is unchanged.

## Accepted Deferrals
No accepted plan deferrals recorded.

## Planning Findings
No blocking planning findings recorded.

## Advisory Notes
- Optional Governance pointers remain compatibility facts only.

## Lifecycle Notes
- Next lifecycle action: `fsgg-sdd tasks --work 2727-lifecycle-extraction`.
