---
schemaVersion: 1
workId: 2305-generated-artifact-lanes
title: Generated skill manifests should not serialize disjoint skill-editing lanes
stage: plan
changeTier: tier1
status: planned
sourceSpec: work/2305-generated-artifact-lanes/spec.md
sourceClarifications: work/2305-generated-artifact-lanes/clarifications.md
sourceChecklist: work/2305-generated-artifact-lanes/checklist.md
publicOrToolFacingImpact: true
---

# Generated skill manifests should not serialize disjoint skill-editing lanes Plan

Prose status: planned

## Source Snapshot
- spec: work/2305-generated-artifact-lanes/spec.md sha256:74a3425d0a9b4ffc5a163e0f854c0978c07b810688e4f98d3973d5b0c4f0893b schemaVersion:1
- clarifications: work/2305-generated-artifact-lanes/clarifications.md sha256:a526372c9044a65ec0b81edcde1819ef9c48eea71f8e32707718b13d426a3d20 schemaVersion:1
- checklist: work/2305-generated-artifact-lanes/checklist.md sha256:f3467bd12a46a51b4ebfd6c1e91826821e9386de721c519d4e5fc6cf685dd4cb schemaVersion:1

## Plan Scope
- Work item 2305-generated-artifact-lanes is planned from the current specification, clarification, and checklist facts.
- Requirement count: 5.
- Clarification decision count: 0.
- Checklist result count: 5.

## Plan Decisions
- PD-001 [AC-001] [FR-001] complete: Add `TouchSet.generatedTokens (generated: Set<string>) (tokens: string list) : string list` to `FS.GG.Coord.Core/TouchSet.fs`/`.fsi` — pure, no IO. It stems each requested token (reusing the existing `stem` function) and returns the subset whose stem is an EXACT member of `generated`. A directory-prefix token (e.g. `registry/**`, stem `registry`) never matches a file-shaped generated entry (e.g. `registry/driver-skill-manifest.json`), which is what keeps the ADR-0044 #309 parent-directory trap (documented on `tokensOverlap`) from reopening: only an exact-name declaration of the generated artifact itself is caught.
- PD-002 [AC-002] [FR-002] complete: In `Client.fs`'s `updateTouchSet` (backs both `widen` and `set-paths`), after `Writes.validate` accepts the requested tokens and before the proposed declaration or collision scan is computed, resolve the generated set via the existing `generatedPathCollector`/`KitDigest.kitRoot()` seam (already wired for `deliveryPathsVerified`) and call `TouchSet.generatedTokens`. A non-empty result refuses the whole call — no PATCH is issued, `Paths:` stays byte-identical, and the message names ADR-0044 and every offending token, mirroring the existing fail-loud style of the flag-shaped/sentinel-mixed refusals in `Writes.validate`. This is an ALL-OR-NOTHING input refusal (same shape as `Writes.validate`'s own refusals), not a per-token silent drop, because a silent drop would leave a worker believing they declared something they did not (clarify decision, `work/2305-generated-artifact-lanes/clarifications.md`).
- PD-003 [AC-001] [FR-003] complete: Add `TouchSet.excludeGenerated (generated: Set<string>) (pairs: (string * string) list) : (string * string) list` to `TouchSet.fs`/`.fsi` — pure. It drops a `(x, y)` conflict pair only when `stem x = stem y` AND that shared stem is a member of `generated`; an asymmetric pair (one side a directory prefix, the other an exact generated file) is NOT dropped, for the same #309 reason as PD-001. Wire it into `Client.fs` at the two places `TouchSet.conflicts`/`TouchSet.scopedConflicts` currently feed a verdict straight through: the per-candidate scan inside `activeCollisions` (used by `widen`/`set-paths`'s pre-PATCH recheck) and `overlapCmd`'s two-ref comparison. `Lanes.fs` and `Schedulability.fs` are deliberately NOT touched (SB-005): once PD-002 stops a generated token from ever entering a declaration, no live `Paths:` carries one going forward, so the scheduler's own lane partitioning never sees the collision to begin with — filtering it a second time there would be dead code for a case PD-002 already prevents.
- PD-004 [AC-001] [FR-004] complete: No new code — this is the negative-case proof that PD-001/PD-003's exact-stem-match rule does not accidentally clear a real collision. Covered by `TouchSetTests.fs` cases: (a) two DIFFERENT real (non-generated) files sharing no token still overlap normally; (b) a generated exact-match token on one side against a directory-prefix declaration on the other (e.g. `registry/**`) still collides, proving the parent-directory case stays caught.
- PD-005 [AC-001] [FR-005] complete: No code change — `src/FS.GG.Kit/FS.GG.Kit.csproj` is verified absent from `scripts/generated-paths`' output (measured: `scripts/generated-paths` lists `dist/skill-union-assert.sh`, `registry/coordination-kit-skill-manifest.json`, `registry/driver-skill-manifest.json`, `registry/repos.lock` — no kit csproj), so `TouchSet.generatedTokens`/`excludeGenerated` never match it and `check-kit-published-coherence`'s single-writer field keeps colliding exactly as today. Proven by a `TouchSetTests.fs` case using the real generated set shape.
- PD-006 [AC-003] [FR-006] complete: No code change to `scripts/generate-driver-manifest` — ADR-0044's `--list` contract is already implemented correctly (verified: both manifest paths are already emitted, whole-file/empty-marker). Proof is by EXECUTION per the item's own gate-inversion requirement: dirty a committed manifest (or edit a skill body without regenerating) and confirm `scripts/generate-driver-manifest --check` exits non-zero, run manually in the worktree and recorded under `Verification:` in the delivery report — the item's declared `Paths:` includes this script defensively but its ADR-0044 compliance predates this change and needs no edit.

## Contract Impact
- PC-001 [PD-001] [PD-003] public F# surface: `FS.GG.Coord.Core/TouchSet.fsi` gains two new values, `generatedTokens` and `excludeGenerated`. Additive only — no existing signature in `TouchSet.fsi` changes, so `TouchSet.conflicts`/`scopedConflicts` keep their exact current signature and behavior for `Lanes.fs`/`Schedulability.fs` and every other caller (`docs/api-surface` baseline gains two lines; `surface --check` must be re-run and, if it gates this repo, refreshed).
- PC-002 [PD-002] CLI-observable behavior change: `widen`/`set-paths` on a request naming a generated, CI-gated artifact now exits non-zero (refused) where it previously exited 0 (silently granted a real reservation on a file nobody authors). `overlap`/`activeCollisions` now answer DISJOINT for a collision attributable solely to a shared generated-artifact token where they previously answered OVERLAP. Both are the `.github#2305` row's own acceptance criteria, not an incidental break; no caller outside this repo's own coordination tooling depends on the old (defective) behavior, and no receiver-facing package ships `Client.fs`.

## Verification Obligations
- VO-001 [PD-001] [PD-003] [PC-001] semanticTest: `TouchSetTests.fs` gains unit coverage for `generatedTokens` (empty input, no match, exact match, directory-prefix non-match) and `excludeGenerated` (drops an exact-exact generated pair, keeps a genuinely disjoint pair, keeps an asymmetric exact-vs-directory-prefix pair, keeps the kit-Version-shaped non-generated pair) — run via `dotnet test tests/FS.GG.Coord.Core.Tests`.
- VO-002 [PD-002] [PC-002] manualCliEvidence: Build the CLI (`dotnet build src/FS.GG.Coord.Cli -c Release`) and exercise `widen`/`overlap` against constructed fixtures in the worktree to confirm the refusal message and the DISJOINT verdict, recorded under `Verification:` in the delivery report — the item's declared `Paths:` intentionally excludes `FS.GG.Coord.Cli.Tests`/`coord-engine-e2e`, so this obligation is discharged by direct execution rather than a new committed test file (consistent with the item's own narrow `Paths:` declaration).
- VO-003 [PD-006] [AC-003] gateInversion: Invert `generate-driver-manifest --check`'s own subject per the `pnext-item` gate-inversion requirement — dirty a manifest / edit a skill body without regenerating, run `--check`, record the exact mutation and the observed red under `Verification:`.

## Performance Intent
No performance intent is declared for this work item — `generatedTokens`/`excludeGenerated` are O(n) set lookups over already-in-memory token lists, no new IO, no change to `activeCollisions`' REST/GraphQL cost profile (the same `generatedPathCollector` process invocation `deliveryPathsVerified` already pays for is reused, not duplicated, per PD-002's design).

## Migration Posture
- PM-001 [PC-001] [PC-002] compatibility: No persisted board data or issue body needs migration. A `Paths:` declaration that ALREADY names a generated artifact (written before this change, e.g. a historical `widen`) is not rewritten by this change — PD-003's `excludeGenerated` reads such a legacy declaration and stops it from counting as a collision going forward, so old declarations self-heal at the next `widen`/`overlap`/`activeCollisions` read rather than requiring a repo-wide rewrite.

## Generated View Impact
- GV-001 [PD-001] [PD-003] workModel: `readiness/2305-generated-artifact-lanes/work-model.json` and generated Codex/Claude guidance refresh after this plan, `tasks`, and `evidence` change, via `fsgg-sdd`'s own generated-view refresh — stale generated guidance is a diagnostic, not authority over these authored artifacts.

## Accepted Deferrals
No accepted plan deferrals recorded.

## Planning Findings
No blocking planning findings recorded.

## Advisory Notes
- Optional Governance pointers remain compatibility facts only.

## Lifecycle Notes
- Next lifecycle action: `fsgg-sdd tasks --work 2305-generated-artifact-lanes`.
