---
schemaVersion: 1
workId: 2402-coherent-set-versioning
title: Coherent Set Versioning
stage: plan
changeTier: tier1
status: planned
sourceSpec: work/2402-coherent-set-versioning/spec.md
sourceClarifications: work/2402-coherent-set-versioning/clarifications.md
sourceChecklist: work/2402-coherent-set-versioning/checklist.md
publicOrToolFacingImpact: true
---

# Coherent Set Versioning Plan

Prose status: planned

## Source Snapshot
- spec: work/2402-coherent-set-versioning/spec.md sha256:ac69db5b7e169126290348ef659815b0d67d773f71ac1497af4ed52da7d58ddd schemaVersion:1
- clarifications: work/2402-coherent-set-versioning/clarifications.md sha256:a29ea750302b37735fbb3c0e4f8da929dafae0b38ed518b9f852da5ebb5481f7 schemaVersion:1
- checklist: work/2402-coherent-set-versioning/checklist.md sha256:df71f699fb74b9270722205caaae2a181105f1eee4f5132a8825ba09744746d3 schemaVersion:1

## Plan Scope
- Work item 2402-coherent-set-versioning is planned from the current specification, clarification, and checklist facts.
- Requirement count: 4.
- Clarification decision count: 3.
- Checklist result count: 4.

## Plan Decisions
- PD-001 [AC-001] [FR-001] complete: Add one MSBuild property `<FsggCoherentSetVersion>` to
  `Directory.Build.props`, set to `0.50.0` — one MINOR above `max(FS.GG.Kit 0.49.0, FS.GG.Drivers
  0.18.0, FS.GG.Coord.Cli 0.23.0)` = `0.49.0`, so no member appears to downgrade (spec SB-003/FR-003).
  Not `0.49.0` itself: live CI on this PR's first push proved `0.49.0` was already the newest
  FS.GG.Kit published on nuget.org, and `check-kit-published-coherence.py`'s PR arm refuses any PR
  that edits `FS.GG.Kit.csproj` — which adopting this property necessarily does — unless the
  declared version is STRICTLY GREATER than what is already published
  (`kit-published-coherence` / `pr-arm`, run 31523042887, job 93884745952). `0.50.0` satisfies both
  constraints. Replace the
  `<Version>` element in `src/FS.GG.Kit/FS.GG.Kit.csproj:481`, `src/FS.GG.Drivers/FS.GG.Drivers.csproj:70`
  and `src/FS.GG.Coord.Cli/FS.GG.Coord.Cli.fsproj:92` with `<Version>$(FsggCoherentSetVersion)</Version>`.
  `Directory.Build.props` already imports the org-shared `dist/dotnet/Directory.Build.props`
  (`Directory.Build.props:35`) and documents that repo-specific overrides belong below that import —
  the new property is added there, is a plain `.github`-local addition, and needs no change to the
  distributed org file (verified: the org file carries no `<Version>`/`FsggCoherentSetVersion` property
  today — `grep -n "Version" dist/dotnet/Directory.Build.props` — so there is nothing to collide with).
  `src/FS.GG.Coord.Cli/FS.GG.Coord.Cli.fsproj:110`'s own comment ("KEEP THIS PROPERTY IN STEP WITH
  Version ABOVE") names a second property that must track `<Version>` by hand today; this decision
  updates that property to reference `$(FsggCoherentSetVersion)` directly so the two can no longer
  drift from each other either.
- PD-002 [AC-001] [FR-002] complete: Add `scripts/check-coherent-set-version.py` — a static, no-network
  gate (same shape as `check-lock-ranges.py`: git-tracked files only) asserting `Directory.Build.props`
  declares exactly one `FsggCoherentSetVersion` property and that FS.GG.Kit.csproj, FS.GG.Drivers.csproj
  and FS.GG.Coord.Cli.fsproj each resolve `<Version>` to `$(FsggCoherentSetVersion)` (via
  `dotnet msbuid -getProperty:Version`, the same evaluated-property technique
  `release-coord-engine.yml`'s own header requires — "never a grep"). Wire it into a new
  `coherent-set-version.yml` workflow (PR + push + schedule triggers, mirroring
  `lock-range-coherence.yml`'s shape) and a hermetic `tests/coherent-set-version/run.sh` fixture with a
  MUTATION leg: temporarily rewrite one project file back to an independent literal `<Version>` (e.g.
  `0.18.1` in FS.GG.Drivers.csproj) in a scratch copy, assert the gate reds, restore, assert the gate is
  green. This is new evidence, not reused from an existing suite — no existing test exercises this
  mechanism.
- PD-003 [AC-001] [FR-003] complete: Add a migration note to `docs/registry/compatibility.md` recording:
  the set's starting version (`0.50.0`), the `max()` derivation, and the DEC-001 reconciliation with
  `.github#2396` verbatim (receiver-pin lag vs. within-set lag), so a future reader does not have to
  re-derive either fact.
- PD-004 [AC-001] [FR-004] complete: Per DEC-002, the PR body states the evaluated subject of each of the
  seven named gates (`check-source-coherence.py`, `check-feed-coherence.py`, `check-pin-coherence.py`,
  `check-engine-pin.py`, `check-kit-published-coherence.py`, `check-lock-ranges.py`,
  `contract-coherence.yml`) and the one-line justification for keeping each, with the evidence recorded
  in DEC-002. No workflow or script is deleted by this plan — DEC-002 established that none of the seven
  asserts drift *between* Kit, Drivers and coord-engine, so none is made unreachable by this change.
  This is the plan's answer to FR-004, not a gap: the requirement is "evaluated and stated", not "found
  and deleted", and the record shows the evaluation happened.
- PD-005 [AC-001] deferred (SB-005 / DEC-003): Release-workflow consolidation (one workflow cutting all
  three packages together) and the real dual-feed publish + receiver-restore verification (AC2, AC7 of
  the parent issue) are explicitly OUT of this plan, filed as follow-up FS-GG/.github#2409 per DEC-003.
  This plan's `Paths:` therefore touches none of `release-kit.yml`, `release-drivers.yml`,
  `release-coord-engine.yml`, or the feed.

## Contract Impact
- PC-001 [PD-001] [PD-002] production code: `Directory.Build.props` gains one new MSBuild property;
  three project files change how `<Version>` resolves (same evaluated value, different source) — no
  package's NEXT published version is held back, and Kit/Drivers/CoordCli's *source* `<Version>` all
  become `0.50.0` the moment this merges (a real, visible jump for Drivers 0.18.0→0.50.0 and
  Coord.Cli 0.23.0→0.50.0; Kit 0.49.0→0.50.0, one MINOR, because `0.49.0` was already the newest
  FS.GG.Kit published on nuget.org and this PR edits FS.GG.Kit.csproj). This is source-tree-only: no
  release workflow runs as part of this change, so no package is actually published at `0.50.0` by
  this PR — that remains PD-005's deferred follow-up. A new CI workflow (`coherent-set-version.yml`)
  and script are added; no existing workflow or script is modified or deleted.
- PC-002 [PD-001] known consequence: Once this merges, `coord-engine`'s SOURCE `<Version>` (0.50.0) will
  disagree with its registry `package-version`/feed (still 0.23.0, unpublished) until the deferred
  follow-up cuts a real release. `scripts/check-engine-freshness.py` will correctly begin reporting a
  release owed (1+ commit — this very bump — since the `coord-engine/v0.23.0` tag), which is accurate,
  not a regression: this is the same "release owed, and that is the correct signal" state the registry's
  own `coord-engine` row documents as the expected shape of that gate (`registry/dependencies.yml:930`).
  This consequence is named explicitly in the PR body and the follow-up item rather than left implicit.

## Verification Obligations
- VO-001 [PD-001] [PD-002] semanticTest: `dotnet msbuild src/FS.GG.Kit/FS.GG.Kit.csproj -getProperty:Version`,
  same for FS.GG.Drivers.csproj and FS.GG.Coord.Cli.fsproj, each evaluates to `0.50.0`.
- VO-002 [PD-002] semanticTest: `bash tests/coherent-set-version/run.sh` green on the unmodified tree;
  gate-inversion mutation leg reds when a project file is mutated back to an independent `<Version>`,
  green again once restored (MUTATION-PROVEN evidence, recorded with command + observed output in the
  PR body).
- VO-003 [PD-001] [PD-003] semanticTest: `git grep -n "<Version>" src/FS.GG.Kit/FS.GG.Kit.csproj
  src/FS.GG.Drivers/FS.GG.Drivers.csproj src/FS.GG.Coord.Cli/FS.GG.Coord.Cli.fsproj` shows only
  `$(FsggCoherentSetVersion)` — no independent literal remains.
- VO-004 [PD-001] build: `dotnet build src/FS.GG.Kit src/FS.GG.Drivers src/FS.GG.Coord.Cli -c Release`
  succeeds unchanged (the property substitution does not alter any other build behavior).

## Performance Intent
No performance intent is declared for this work item.

## Migration Posture
- PM-001 [PC-001] [PC-002] real migration: the version-scalar migration is recorded in the
  `docs/registry/compatibility.md` note this plan adds — starting version `0.50.0`, the
  `max(0.49.0, 0.18.0, 0.23.0)` + one-MINOR derivation, and the explicit statement that no member's
  *published* version, registry row, or feed moves as part of this migration: only the three projects' SOURCE
  `<Version>` moves, together, in one commit. Cutting an actual coherent-set release is the deferred
  follow-up's scope, not this plan's.

## Generated View Impact
- GV-001 [PD-001] [PD-002] workModel: `readiness/2402-coherent-set-versioning/work-model.json`
  refreshes from this plan's sources on the next `fsgg-sdd` stage command; no other generated view
  (driver manifests, dashboards, skill-union bundle) is affected, because no source file under any
  other generator's inputs changes — `Directory.Build.props`, the three project files, the new gate
  script/workflow/test, and the migration note are not generator inputs for anything but this work
  item's own SDD work-model.

## Accepted Deferrals
No accepted plan deferrals recorded.

## Planning Findings
No blocking planning findings recorded.

## Advisory Notes
- Optional Governance pointers remain compatibility facts only.

## Lifecycle Notes
- Next lifecycle action: `fsgg-sdd tasks --work 2402-coherent-set-versioning`.
