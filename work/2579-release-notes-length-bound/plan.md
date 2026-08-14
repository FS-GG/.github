---
schemaVersion: 1
workId: 2579-release-notes-length-bound
title: Release Notes Length Bound
stage: plan
changeTier: tier1
status: planned
sourceSpec: work/2579-release-notes-length-bound/spec.md
sourceClarifications: work/2579-release-notes-length-bound/clarifications.md
sourceChecklist: work/2579-release-notes-length-bound/checklist.md
publicOrToolFacingImpact: true
---

# Release Notes Length Bound Plan

Prose status: planned

## Source Snapshot
- spec: work/2579-release-notes-length-bound/spec.md sha256:9628d7ab7aac6bd0f45dfe113647aea70d007b06b5847c0927d70277f4f42fe2 schemaVersion:1
- clarifications: work/2579-release-notes-length-bound/clarifications.md sha256:70fc9225351808eaf93d7f37064d95e76344083f1bc7026c8087f2bbca14d79e schemaVersion:1
- checklist: work/2579-release-notes-length-bound/checklist.md sha256:eda09f9605279a4369ff1f9805ba7a421c895881ef984e6e75bbea985f40d792 schemaVersion:1

## Plan Scope
- Work item 2579-release-notes-length-bound is planned from the current specification, clarification, and checklist facts.
- Requirement count: 7.
- Clarification decision count: 6.
- Checklist result count: 7.

## Plan Decisions
- PD-001 [AC-001] [FR-001] complete: Add a `length` arm to `scripts/check-engine-release-notes.py`. It folds `PROJECTS` out of `scripts/check-coherent-set-version.py` **by AST** (never by import, never restated — the discipline `tests/engine-release-notes/run.sh` already applies to `PATHS_SUBJECT`), evaluates `PackageReleaseNotes` for each member through one `dotnet msbuild -getProperty` call per project, and exits 1 naming any member over budget. An unfoldable/absent `PROJECTS` is exit 2 — "cannot tell what my subject is" is never "coherent".
- PD-002 [AC-002] [FR-002] complete: Express the limit once as `NUGET_ORG_RELEASE_NOTES_LIMIT = 35000`, with the observed nuget.org `400` text quoted in the comment above it as the external constraint's own words. Every run prints one line per member: evaluated characters used, characters remaining, and percent of budget consumed — on the green path as well as the red one.
- PD-003 [AC-003] [FR-003] complete: Restructure `<PackageReleaseNotes>` in `FS.GG.Coord.Cli.fsproj` into the `0.52.0` entry, then `$(FsggStandingAdvisories)`, then a history pointer. The advisories are hoisted verbatim in substance from the `0.50.2` and `0.50.6` entries — the `0.50.1` and `0.50.5` two-of-three warnings — and the accumulated per-version narrative below them is removed. `$(FsggCoherentSetVersion)` does not move.
- PD-004 [AC-004] [FR-004] complete: Add an `advisories` arm. STRUCTURAL: the authored `<PackageReleaseNotes>` element text, read from the project XML, must contain the literal `$(FsggStandingAdvisories)`. SEMANTIC: the evaluated `FsggStandingAdvisories` must be non-empty and its stripped text must occur inside the evaluated notes. The arm applies to the announcing project only, since only it carries notes.
- PD-005 [AC-005] [FR-005] complete: Leave `evaluated_properties`, the empty/coherent-scalar/first-token comparisons and the exit-code contract untouched; the new arms are added around them and the existing fixture legs for all four are retained unmodified as the regression proof.
- PD-006 [AC-006] [FR-006] complete: Extend `PATHS_SUBJECT` with the two sibling `.csproj` files and `scripts/check-coherent-set-version.py`, and add the same three entries to BOTH `paths:` blocks of `.github/workflows/engine-release-notes.yml`. The existing fixture leg that folds `PATHS_SUBJECT` and walks both trigger blocks already fails closed on drift and is the mechanism, not a new one.
- PD-007 [AC-007] [FR-007] complete: Extend `tests/engine-release-notes/run.sh` with one leg per new arm plus a REAL-TREE leg: it materializes `origin/main`'s `FS.GG.Coord.Cli.fsproj` (via `git show`) beside a props file carrying that tree's scalar, runs the length arm, and requires exit 1 with the 37,279-character measurement named. Every inversion is recorded in the item's own evidence with the exact mutation and observed red.

## Contract Impact
- PC-001 [PD-001] gate exit codes: `scripts/check-engine-release-notes.py` keeps its published contract — `0` coherent, `1` incoherent, `2` could not evaluate — and the new arms map onto it rather than introducing a fourth code.
- PC-002 [PD-003] published listing text: `PackageReleaseNotes` is rendered on both feeds' package listings. Removing the accumulated narrative changes what a consumer reads, so the standing advisories are the compatibility-preserving core and are carried forward unchanged in substance.
- PC-003 [PD-006] workflow trigger: `engine-release-notes.yml`'s `paths:` filters are part of this gate's reach; widening the subject without widening the trigger is the `.github#2512` defect and is refused by the fixture.

## Verification Obligations
- VO-001 [PD-001] [PC-001] semanticTest: `bash tests/engine-release-notes/run.sh` green on the repaired tree, with every pre-existing leg unmodified.
- VO-002 [PD-007] [PC-001] semanticTest: the real-`origin/main` length leg observed RED before the repair and GREEN after, executed rather than asserted.
- VO-003 [PD-004] [PC-002] semanticTest: the advisory arms observed red under two separate inversions — reference removed, property emptied — each with the length and first-token arms still satisfied, so the arm is shown to be what caught it.
- VO-004 [PD-006] [PC-003] semanticTest: a `PATHS_SUBJECT` entry removed from one trigger block reds the fixture's reachability leg.
- VO-005 [PD-002] [PC-001] semanticTest: `grep -rn "35000" scripts/ .github/workflows/ tests/` returns exactly one hit, the constant's own definition line.

## Performance Intent
No performance intent is declared for this work item.

## Migration Posture
- PM-001 [PC-002] diagnoseOnly: There is no migration to perform and one that must NOT be attempted. `FS.GG.Coord.Cli 0.52.0` is already published on the org feed carrying the OLD, over-limit notes, and that artifact is immutable — this change cannot and does not correct it. The repaired notes therefore first reach a feed at the next cut, which is the decided additive `0.52.1` re-cut owned by a separate item landing after this one. Consumers of the already-published `0.50.x`/`0.51.x` listings keep reading the accumulated narrative on those listings, which is precisely why removing it from the newest listing loses nothing: DEC-001's premise is that the registry is already serving it.

## Generated View Impact
- GV-001 [PD-003] workModel: `readiness/2579-release-notes-length-bound/work-model.json` refreshes from these plan sources. Beyond the SDD generated view, this change touches NO generated artifact in the repository: `registry/coordination-kit-skill-manifest.json` digests skill directories and none of this item's paths is a `kit:` source, so no kit manifest regeneration is implied and no kit release obligation arises from it.

## Accepted Deferrals
No accepted plan deferrals recorded.

## Planning Findings
No blocking planning findings recorded.

## Advisory Notes
- Optional Governance pointers remain compatibility facts only.

## Lifecycle Notes
- Next lifecycle action: `fsgg-sdd tasks --work 2579-release-notes-length-bound`.
