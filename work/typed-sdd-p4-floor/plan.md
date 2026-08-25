---
schemaVersion: 1
workId: typed-sdd-p4-floor
title: P4 Typed SDD registry floor
stage: plan
changeTier: tier1
status: planned
sourceSpec: work/typed-sdd-p4-floor/spec.md
sourceClarifications: work/typed-sdd-p4-floor/clarifications.md
sourceChecklist: work/typed-sdd-p4-floor/checklist.md
publicOrToolFacingImpact: true
---

# P4 Typed SDD registry floor Plan

Prose status: planned

## Source Snapshot
- spec: work/typed-sdd-p4-floor/spec.md sha256:aa5761716839581bec012bfa418ada0b6372ee84b6e1cd1dfd00b25e02ce6eed schemaVersion:1
- clarifications: work/typed-sdd-p4-floor/clarifications.md sha256:6554a35b720fa67aecd2bb06997f1bc2a3af62004f9ba0fcc17b0ef2a0c610cd schemaVersion:1
- checklist: work/typed-sdd-p4-floor/checklist.md sha256:9f3ab77066fad099991b7923acc79c8a60a0020153361bdc4fb0ba8129f99344 schemaVersion:1

## Plan Scope
- Work item typed-sdd-p4-floor is planned from the current specification, clarification, and checklist facts.
- Requirement count: 1.
- Clarification decision count: 0.
- Checklist result count: 1.

## Plan Decisions
- PD-001 [AC-001] [FR-001] complete: Advance both provider-family `minimum-fsgg-sdd.version` mirrors to the already dual-published `1.4.0-preview.1`; retain every lifecycle default unchanged.

## Contract Impact
- PC-001 [PD-001] coherent registry floor: `fs-gg-ui-template` and `fs-gg-workspace-template` declare the first CLI release supporting the Typed Protocol Kernel lifecycle.

## Verification Obligations
- VO-001 [PD-001] [PC-001] semanticTest: Run registry validation and FS.GG.Templates' five-descriptor equality checker against the changed authority.

## Performance Intent
No performance intent is declared for this work item.

## Migration Posture
- PM-001 [PC-001] minimumAdvance: Existing Standard SDD consumers remain valid; Typed SDD consumers must install the declared preview compiler floor.

## Generated View Impact
- GV-001 [PD-001] workModel: Regenerate the work model and agent guidance from the current registry-floor decision before review.

## Accepted Deferrals
No accepted plan deferrals recorded.

## Planning Findings
No blocking planning findings recorded.

## Advisory Notes
- Optional Governance pointers remain compatibility facts only.

## Lifecycle Notes
- Next lifecycle action: `fsgg-sdd tasks --work typed-sdd-p4-floor`.
