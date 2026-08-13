---
schemaVersion: 1
workId: 2324-mandatory-sdd-output-enforcement
title: Mandatory Sdd Output Enforcement
stage: plan
changeTier: tier1
status: planned
sourceSpec: work/2324-mandatory-sdd-output-enforcement/spec.md
sourceClarifications: work/2324-mandatory-sdd-output-enforcement/clarifications.md
sourceChecklist: work/2324-mandatory-sdd-output-enforcement/checklist.md
publicOrToolFacingImpact: true
---

# Mandatory Sdd Output Enforcement Plan

Prose status: planned

## Source Snapshot
- spec: work/2324-mandatory-sdd-output-enforcement/spec.md sha256:d0ca312ff0847867c7956675068e945ed5c988632040d2a02864a7a736e17dc2 schemaVersion:1
- clarifications: work/2324-mandatory-sdd-output-enforcement/clarifications.md sha256:88ab3ccc8d1248fa0478633f5dfca895af7cf25be81b534eb79fa12bc74a891a schemaVersion:1
- checklist: work/2324-mandatory-sdd-output-enforcement/checklist.md sha256:4798fb7eab54c3eb4c0de1f304cd5d2e33496059ae00707acda52e267b1d4362 schemaVersion:1

## Plan Scope
- Work item 2324-mandatory-sdd-output-enforcement is planned from the current specification, clarification, and checklist facts.
- Requirement count: 7.
- Clarification decision count: 3.
- Checklist result count: 7.

## Plan Decisions
- PD-001 [AC-001] [FR-001] complete: Add `DeliveryRoute.mandatorySddPaths: Receipt -> string list` to `src/FS.GG.Coord.Core/DeliveryRoute.fs`/`.fsi`, returning `work/<sddWorkId>` and `readiness/<sddWorkId>` for a receipt whose SDD binding is valid. `verifyPaths` maps them through `TouchSet.classify`/`TouchSet.covers` so the containment rule is asked, never restated (#485).
- PD-002 [AC-002] [FR-002] complete: In `Client.verifyPaths`, partition the existing `drift` list into three buckets — `sdd package (expected)`, then ADR-0044's existing `regenerated (expected)`, then `undeclared` — and keep the verdict decided by `undeclared` alone, so removing the SDD bucket cannot mask any other undeclared file.
- PD-003 [AC-003] [FR-003] complete: `mandatorySddPaths` matches on `Route = Some SddRequired`; a `Lightweight` receipt returns the empty list, so a lightweight item's `work/<n>/` change stays drift with no new code path.
- PD-004 [AC-004] [FR-004] complete: A failed comment read, or a `Stale`/`Unreadable` verdict, yields the empty list plus one stderr line stating that nothing was subtracted — deliberately the same sentence shape `generatedPaths` already uses, so a reader meets one fail-closed idiom rather than two.
- PD-005 [AC-005] [FR-005] complete: The exemption tokens are built from the receipt's own `sddWorkId` only; `work/` and `readiness/` are never roots, so another work id's package is untouched by construction rather than by a filter.
- PD-006 [AC-006] [FR-006] complete: `mandatorySddPaths` reuses `validateSddBinding` (which already pins `specHome = work/<workId>/spec.md` and restricts `sddWorkId` to machine tokens) and adds one leading-alphanumeric guard, closing the `.`/`..`/`.hidden` traversal shapes that `tokens` alone admits.
- PD-007 [AC-007] [FR-007] complete: The receipt read sits inside the existing `if List.isEmpty drift then ... else` guard that already defers `generatedPaths`, so a green PR issues no extra call and prints no expected-bucket heading.

## Contract Impact
- PC-001 [PD-001] public surface: `src/FS.GG.Coord.Core/DeliveryRoute.fsi` gains one `val`. Additive only; no existing signature, type, or verdict changes.
- PC-002 [PD-002] tool-facing: `verify-paths` gains one reported bucket. The four `FSGG-PATHS` verdict tokens `.github/workflows/touch-set-drift.yml` greps for are unchanged, so the workflow's classifier keeps working byte-identically.

## Verification Obligations
- VO-001 [PD-001] [PC-001] semanticTest: `tests/FS.GG.Coord.Core.Tests/DeliveryRouteTests.fs` pins the derivation — valid sdd-required receipt, lightweight receipt, missing bindings, and each fail-closed shape of FR-006.
- VO-002 [PD-002] [PC-002] semanticTest: `tests/FS.GG.Coord.Cli.Tests` drives the real `Client.verifyPaths` against a stubbed transport serving the PR, its files, the issue body, and the GraphQL receipt comment, asserting AC-001 through AC-005 and AC-007 at the command boundary.
- VO-003 [PD-002] [PC-002] gateInversion: Each new assertion ships with recorded evidence that inverting the gate reddens it — the mutation applied and the observed failure — per the independent-review gate-inversion rule.

## Performance Intent
No performance intent is declared for this work item.

## Migration Posture
- PM-001 [PC-001] diagnoseOnly: No migration is required and none is performed. The change is purely additive on both surfaces — one new `val` in `DeliveryRoute.fsi`, one new reported bucket in `verify-paths` — so every already-recorded receipt, every existing `Paths:` declaration, and the four items that already declare their own package directories (`.github#2306`, `#2305`, `#2366`, `#2324`) keep their exact current behaviour. The two still-exposed rows (`.github#2249`, `#2343`) are healed by the new read, not by a body edit, so no board data is migrated.

## Generated View Impact
- GV-001 [PD-001] workModel: This item's own `readiness/2324-mandatory-sdd-output-enforcement/` package is the ONLY generated view it touches, and it is the very artifact the change is about — so it is also the item's live self-test: the PR carries `work/2324-…/` and `readiness/2324-…/` files, and the fixed `verify-paths` must classify them under `sdd package (expected)` rather than `undeclared`. No repo-level generated artifact (`registry/**`, `scripts/generated-paths`' roster) is regenerated by this change, so ADR-0044's existing subtraction is neither extended nor relied upon.

## Accepted Deferrals
No accepted plan deferrals recorded.

## Planning Findings
No blocking planning findings recorded.

## Advisory Notes
- Optional Governance pointers remain compatibility facts only.

## Lifecycle Notes
- Next lifecycle action: `fsgg-sdd tasks --work 2324-mandatory-sdd-output-enforcement`.
