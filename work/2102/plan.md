---
schemaVersion: 1
workId: 2102
title: Converge receiver-proj migration shape across SDD, Audio, and Governance
stage: plan
changeTier: tier1
status: planned
sourceSpec: work/2102/spec.md
sourceClarifications: work/2102/clarifications.md
sourceChecklist: work/2102/checklist.md
publicOrToolFacingImpact: true
---

# Converge receiver-proj migration shape across SDD, Audio, and Governance Plan

Prose status: planned

## Source Snapshot
- spec: work/2102/spec.md sha256:5cb515c0f484da841a7795235cff7a5ba8f6172eb6c24bcb8db633b4c35a9411 schemaVersion:1
- clarifications: work/2102/clarifications.md sha256:192d0e3ea17243269fdad47e890491af72cf62beb2d5f4d394593bf6ca07d5e3 schemaVersion:1
- checklist: work/2102/checklist.md sha256:c0b337d549efa4d473089edab5c9d9c5ea4cfb6d78160170e5a2a4e46f258f61 schemaVersion:1

## Plan Scope
- Work item 2102 is planned from the current specification, clarification, and checklist facts.
- Requirement count: 1.
- Clarification decision count: 0.
- Checklist result count: 1.

## Plan Decisions
- PD-001 [AC-001] [FR-001] complete: Standardize on an inline exact receiver-proj pin for every migrated workflow call site; retain the tool-owned `skill-view selftest` as the only swapped-root resolver fixture, and remove SDD's duplicate receiver fixture.

## Contract Impact
- PC-001 [PD-001] receiver-workflow-guidance: The receiver contract is a two-control shape: an inline call-site pin rejects malformed invocation text, while `skill-view selftest` proves declaration resolution. The durable guidance is `docs/coordination/receiver-proj-migration-shape.md` and the cross-repo coordination skill links to it.

## Verification Obligations
- VO-001 [PD-001] [PC-001] mutationTest: Each SDD receiver call-site pin must fail for the retired source/roots form, a wrong project path, and an added flag, then pass after restoration. `skill-view selftest` must exercise its swapped-root lane; Audio and Governance remain verified against their merged inline-pin shape.

## Performance Intent
No performance intent is declared for this work item.

## Migration Posture
- PM-001 [PC-001] crossRepo: `.github#2102` owns the decision and stays blocked by `FS.GG.SDD#840`; the SDD child owns source edits and evidence. No sibling repository is edited from this worktree.

## Generated View Impact
- GV-001 [PD-001] skillGuidance: Update the canonical Claude skill source and regenerate the agent skill view so future agents receive the same migration decision rather than a prose-only document.

## Accepted Deferrals
No accepted plan deferrals recorded.

## Planning Findings
No blocking planning findings recorded.

## Advisory Notes
- Optional Governance pointers remain compatibility facts only.

## Lifecycle Notes
- Next lifecycle action: `fsgg-sdd tasks --work 2102`.
