---
schemaVersion: 1
workId: 2864-engine-currency-repo-shape
title: Repository-shape-aware engine currency verification
stage: plan
changeTier: tier1
status: planned
sourceSpec: work/2864-engine-currency-repo-shape/spec.md
sourceClarifications: work/2864-engine-currency-repo-shape/clarifications.md
sourceChecklist: work/2864-engine-currency-repo-shape/checklist.md
publicOrToolFacingImpact: true
---

# Repository-shape-aware engine currency verification Plan

Prose status: planned

## Source Snapshot
- spec: work/2864-engine-currency-repo-shape/spec.md sha256:77a8c56ddc54a891c4a9a6c6696e9674a498f1bbcc82bad936e9b9806ac208da schemaVersion:1
- clarifications: work/2864-engine-currency-repo-shape/clarifications.md sha256:96f307ae6e449ca1c707e4cd411b6f5d0802d4e3becf7a1f225a408efa654b2b schemaVersion:1
- checklist: work/2864-engine-currency-repo-shape/checklist.md sha256:c5a7e148e59b241ebaf2b59b53fac7e638cf966ce8e5f513fe71178abd257f5e schemaVersion:1

## Plan Scope
- Work item 2864-engine-currency-repo-shape is planned from the current specification, clarification, and checklist facts.
- Requirement count: 5.
- Clarification decision count: 0.
- Checklist result count: 5.

## Plan Decisions
- PD-001 [AC-001] [FR-001] complete: Probe `origin/main` for the coordination source roots. When present, retain the shared-checkout HEAD and scoped `rev-list` drift calculation as the authoring path.
- PD-002 [AC-002] [FR-002] complete: When those roots are absent, parse the `fs.gg.coord.cli` pin from `origin/main:.config/dotnet-tools.json` and compare it to the output of the wrapper that will perform the board write.
- PD-003 [AC-002] [FR-003] complete: Normalize only one trailing assembly-version `.0`; compare every remaining byte exactly so prerelease or other version drift remains visible.
- PD-004 [AC-003] [FR-004] complete: Implement each probe as an explicit assignment plus non-empty/cardinality guard; a failed `git show`, JSON parse, or engine invocation terminates the recipe.
- PD-005 [AC-004] [FR-005] complete: Author the `.agents` sources, regenerate `.claude` projections and both manifests, and take the next FS.GG.Kit minor because receivers execute a changed required protocol step.

## Contract Impact
- PC-001 [PD-001] [PD-002] protocol guidance: `pnext-item` and `drive-board` engine-currency instructions change for receivers while preserving the authoring path.

## Verification Obligations
- VO-001 [PD-001] [PC-001] semanticTest: Execute the authoring branch against this checkout and observe a current or positive source-drift count.
- VO-002 [PD-002] [PD-003] [PD-004] mutationTest: Execute the receiver branch against a real receiver checkout, then substitute a mismatched pin and show the comparison turns red; separately remove or corrupt the manifest subject and show refusal rather than current.
- VO-003 [PD-005] projectionTest: Run projection generation/checks, skill-quality checks, manifest checks, and pack the FS.GG.Kit artifact.

## Performance Intent
No performance intent is declared for this work item.

## Migration Posture
- PM-001 [PC-001] additiveProtocol: Existing authoring-repository behavior is retained; receiver workers immediately adopt the pin-based check when the next kit is materialized.

## Generated View Impact
- GV-001 [PD-005] skillProjections: `.claude` skill mirrors and the coordination/driver manifests are regenerated from the canonical `.agents` guidance.
- GV-002 [PD-001] workModel: Preserve the source-bound analysis receipt for this work id so implementation and verification are traceable to the reviewed repo-shape decisions.

## Accepted Deferrals
No accepted plan deferrals recorded.

## Planning Findings
No blocking planning findings recorded.

## Advisory Notes
- Optional Governance pointers remain compatibility facts only.

## Lifecycle Notes
- Next lifecycle action: `fsgg-sdd tasks --work 2864-engine-currency-repo-shape`.
