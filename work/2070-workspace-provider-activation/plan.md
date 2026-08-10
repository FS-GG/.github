---
schemaVersion: 1
workId: 2070-workspace-provider-activation
title: Workspace Provider Activation
stage: plan
changeTier: tier1
status: planned
sourceSpec: work/2070-workspace-provider-activation/spec.md
sourceClarifications: work/2070-workspace-provider-activation/clarifications.md
sourceChecklist: work/2070-workspace-provider-activation/checklist.md
publicOrToolFacingImpact: true
---

# Workspace Provider Activation Plan

Prose status: planned

## Source Snapshot
- spec: work/2070-workspace-provider-activation/spec.md sha256:e4b7fbd4c083b91d2b645e9f7757624e4d0f7bc2867306c232c4a036472b138e schemaVersion:1
- clarifications: work/2070-workspace-provider-activation/clarifications.md sha256:fdd8ee77284bc40bf4bdb0f1b68ca6bdc5e41fd8eede2c387e4976ecb78e59fd schemaVersion:1
- checklist: work/2070-workspace-provider-activation/checklist.md sha256:98cf65d772005e0c47ae8d48ec946bc1d36ea442c5c758b550a6d0998ae9b03f schemaVersion:1

## Plan Scope
- Work item 2070-workspace-provider-activation is planned from the current specification, clarification, and checklist facts.
- Requirement count: 5.
- Clarification decision count: 1.
- Checklist result count: 5.

## Plan Decisions
- PD-001 [AC-001] [FR-001] complete: Re-derive Game Skills/SDD materializer evidence directly (`curl` nuget.org flatcontainer index for `fs.gg.game.skills`; `gh pr view 554/819`; download `fs.gg.game.skills.0.7.0.nupkg` and `sha256sum` the extracted `SKILL.md` against `registry/skills.yml:207`'s digest; `gh api .../compare` to confirm SDD PR#819's merge commit ancestry). Already performed in research; recorded verbatim in spec.md DEC-002.
- PD-002 [AC-002] [FR-002] complete: Re-derive FS.GG.Workspace.Template evidence directly (`curl` nuget.org flatcontainer index for `fs.gg.workspace.template`; download the 0.8.0 nupkg; `sha256sum`; `unzip` and list template identities; compare against FS.GG.Templates#349's own reported hash). Already performed in research; recorded verbatim in spec.md DEC-001.
- PD-003 [AC-003] [FR-003] complete: Re-derive S.I.R./Babylon-bindings evidence directly (`gh pr view 140 --repo EHotwagner/S.I.R.` file diff; grep `Directory.Packages.props`/`.fsproj`/`package.json` for FS-GG references; confirm no `../../FS.GG.*` `ProjectReference`; check `EHotwagner/babylonjsBindings` for nuget/npm-only dependencies). Already performed in research; recorded verbatim in spec.md DEC-005.
- PD-004 [AC-004] [FR-004] complete: Hand-edit `registry/dependencies.yml` (add `fs-gg-workspace-template` contract row with four identities and `minimum-fsgg-sdd`; add `game-skills` contract row; add `templates` to `game-sim-core.consumers`; add `templates->game` and `sdd->game` dependency edges) and `registry/skills.yml` (confirm `fs-gg-game-fable` row unchanged), then prepend dated entries to `registry/CHANGELOG.md` and `registry/skills.CHANGELOG.md`, then edit `docs/architecture.md` lines 100/388/542 to drop "not-yet-published"/"planned" language for the four identities.
- PD-005 [AC-005] [FR-005] complete: Run `scripts/generate-projections` to rewrite `docs/registry/compatibility.md`'s generated regions, then `scripts/generate-projections --check` to confirm no drift, then the registry validator (`fsgg-sdd registry --file registry/dependencies.yml` or the repo's `scripts/check-projection.py`) to confirm schema-clean.

## Contract Impact
- PC-001 [PD-001] research evidence: FR-001..FR-003's independent re-derivations (nuget.org reads, downloaded nupkg hashes, `gh api compare` ancestry checks, S.I.R./Templates diff inspection) are the evidentiary basis every registry row in PD-004 cites; they are not committed as files, only cited by commit/tag/hash in `registry/dependencies.yml` comments and this plan.
- PC-002 [PD-004] registry contract: `registry/dependencies.yml` and `registry/skills.yml` are the org's typed cross-repo contract registry (ADR-0015, `contract-coherence` gate); every added row must independently re-verify against a public feed read, not merely restate a closing issue's claim, and must not name a version newer than what any consuming descriptor (FS.GG.Templates `providers/*.providers.yml`, S.I.R.'s scaffold provenance) has actually adopted.

## Verification Obligations
- VO-001 [PD-001] [PC-001] semanticTest: Each of FR-001/FR-002/FR-003's independent re-derivations (nuget.org version/hash checks, PR merge-commit ancestry via `gh api .../compare`, S.I.R./Templates dependency-file inspection) was run and its output recorded verbatim in spec.md's Coherent-Set Decisions before any registry row was written — evidence precedes assertion, never the reverse.
- VO-002 [PD-004] [PC-002] semanticTest: `scripts/generate-projections --check` exits clean and the registry's shipped validator (`scripts/check-projection.py` / `fsgg-sdd registry`) reports no schema violation over the edited `registry/dependencies.yml`/`registry/skills.yml`/`docs/registry/compatibility.md`.

## Performance Intent
No performance intent is declared for this work item.

## Migration Posture
- PM-001 [PC-002] diagnoseOnly: `registry/dependencies.yml` `schemaVersion: 2` is unchanged by this item (no schema growth, only new rows within the existing schema); no migration is owed and none is performed.

## Generated View Impact
- GV-001 [PD-005] generatedProjection: `docs/registry/compatibility.md`'s `BEGIN GENERATED: fsgg-contract-versions` / `fsgg-skill-registry-counts` regions are re-emitted by `scripts/generate-projections` after the registry edit lands, and `readiness/2070-workspace-provider-activation/work-model.json` refreshes from the current plan sources.

## Accepted Deferrals
No accepted plan deferrals recorded.

## Planning Findings
No blocking planning findings recorded.

## Advisory Notes
- Optional Governance pointers remain compatibility facts only.

## Lifecycle Notes
- Next lifecycle action: `fsgg-sdd tasks --work 2070-workspace-provider-activation`.
