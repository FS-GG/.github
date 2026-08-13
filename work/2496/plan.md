---
schemaVersion: 1
workId: 2496
title: "pnext-item names the live delivery <ref> --pr N call point"
stage: plan
changeTier: tier1
status: planned
sourceSpec: work/2496/spec.md
sourceClarifications: work/2496/clarifications.md
sourceChecklist: work/2496/checklist.md
publicOrToolFacingImpact: true
---

# pnext-item names the live delivery <ref> --pr N call point Plan

Prose status: planned

## Source Snapshot
- spec: work/2496/spec.md sha256:55248e4f21f9ea83bbbde6e3d8405ebcf4d954f6cf3c1820c773362df40adad0 schemaVersion:1
- clarifications: work/2496/clarifications.md sha256:55f8a8168d96ff2c8bd0b2532f33e64ac9ae8c45b6727e4cca6bd683b542901b schemaVersion:1
- checklist: work/2496/checklist.md sha256:f33498c5fab91cc4ff45c692d1faa974c5568f2d42e30d06039d8521f215bda0 schemaVersion:1

## Plan Scope
- Edit exactly one documented surface, `.claude/skills/pnext-item/SKILL.md` §6 ("Merge and obligations") — no code, no schema, no CLI change. The `fsgg-coord` engine already does everything the new step needs (`Client.ensureAuthorization`/`rebindAuthorization`, live since `.github#2488`); the gap is purely that no documented step calls it.
- Requirement count: 5. Clarification decision count: 4. Checklist result count: 5.

## Technical Context
Pure documentation change to a kit-mirrored `SKILL.md` (Markdown, no code). The behavior being documented already exists and is exercised only by hand today (`.github#2491`'s manual demonstration). No new F# code, no new CLI flag, no schema change — the fix is entirely "name the call, the point, the frequency, and the failure handling in the worker's own instructions."

## Constitution Check
- I (Specify before implementing): this plan follows a specified, clarified, checklist-covered spec — the SDD front-half this very item is producing.
- III (Public surface declared): the "surface" here is the documented worker protocol itself (`pnext-item` §6), which is exactly what SB-001 declares and DEC-001 through DEC-004 pin down; there is no code signature to baseline.
- IV (Idiomatic simplicity): one step, one call, one place — not a new script, not a new flag, not a CI job (SB-006/SB-007 rule those out explicitly).
- VI (Test evidence is mandatory): FR-005/AC-005 requires a real demonstration (this item's own PR, dogfeeding the new step) — see Verification Obligations below.

## Design
- Insert one new paragraph into `.claude/skills/pnext-item/SKILL.md` §6 ("Merge and obligations"), between the existing "Observe the host-acceptance marker … wait on the typed `landable` verdict for the exact head SHA" sentence and "Merge only green and verify the merge on the default branch."
- The new paragraph:
  1. Names the command literally: `scripts/fsgg-coord delivery <ref> --pr <pr> --json` (the live form — no `--snapshot`), run from the worker's own credentialed shell.
  2. States the point: immediately after `landable` reports green for the exact head SHA that will be merged, immediately before the merge REST call — once per item, not once per push (DEC-001, DEC-002).
  3. States failure handling: report the failure; it does not block the merge, because `claim-generation` remains advisory-only (`Landable.fs` `advisoryCheckNames`) until armed into branch protection, which this item does not do (DEC-003).
  4. States the credential/authority boundary: only the worker holding the item's live claim marker may call it, and only before releasing that claim — the live form itself refuses otherwise ("no live claim marker can authorize delivery") — and it must never run from CI, because its first action is `Board.bootstrapCached`, a Projects-v2 GraphQL read no CI credential in this org carries (ADR-0019 §1, `.github#2332`) (DEC-004).
  5. States the cost rationale in place: bounded to one call per item, riding the warm `Cache.Scheduling` 90-second scan cache `take`/`done` already pay for in the common case; never a per-push write, because `rebindAuthorization` makes a repeat call against an already-current marker a zero-cost no-op (DEC-002).
- No change to `src/FS.GG.Coord.*` — the mechanism (`ensureAuthorization`, `rebindAuthorization`, the live `delivery` command) already exists and is unconditional on `--apply` since `.github#2488`. This item closes the reachability gap in the documented flow, not the mechanism.
- Demonstration (FR-005): this very item's own PR is carried through the newly-documented step at merge time — a live, non-manual invocation of the new instruction — and `gh pr checks` on that PR is read for `claim-generation`'s conclusion as the evidence.

## Plan Decisions
- PD-001 [FR-001] [AC-001] [DEC-001] complete: Insert the new documented step into `pnext-item` §6, at the point fixed by DEC-001 — after `landable` reports green, before the merge REST call.
- PD-002 [FR-002] [AC-002] [DEC-002] complete: Word the step so it is invoked exactly once per item (not per push), citing `rebindAuthorization`'s no-op-on-current behavior as the reason a second call is unneeded and a repeat call is cheap if one nonetheless happens.
- PD-003 [FR-003] [AC-003] [DEC-003] [DEC-004] complete: Word the step's failure-handling and credential-boundary sentences per DEC-003 (report, non-blocking, advisory) and DEC-004 (live-claim-holder only, never CI).
- PD-004 [FR-004] [AC-004] complete: Fold the write/scan-cost and ADR-0019/`.github#2332` credential-boundary rationale into the same paragraph (not a separate doc) so a reader never has to leave `pnext-item` to find out why.
- PD-005 [FR-005] [AC-005] complete: Carry this item's own PR through the newly-documented step at its own merge, and capture `gh pr checks`'s `claim-generation` conclusion as the demonstration evidence (analogous to `.github#2488`'s AC4).

## Contract Impact
- PC-001 [PD-001] docs: `.claude/skills/pnext-item/SKILL.md` is the tool-facing worker-protocol contract every FS-GG repo's dispatched worker follows; it is kit-mirrored (ADR-0019), so this edit is compatibility-preserving (additive: one new paragraph in an existing numbered step, no removed or renumbered step) but fleet-wide in reach on the next kit release.

## Verification Obligations
- VO-001 [PD-001] [PD-002] [PD-003] [PD-004] [PC-001] structuralTest: A repository-wide grep for the live `delivery <ref> --pr N` form in the documented flow (`grep -rn "fsgg-coord delivery\b" .claude/skills/ docs/ .github/workflows/ | grep -v "delivery-route\|delivery --snapshot"`) finds a hit inside `.claude/skills/pnext-item/SKILL.md` §6 — the exact check `.github#2496`'s own body ran and reported zero hits on, before this change.
- VO-002 [PD-005] liveDemonstration: This item's own PR, at its own merge, is carried through the new step — a live, non-`--snapshot`, non-manual `delivery <ref> --pr N` call at the documented point — and `gh pr checks <this-pr>` is read afterward for `claim-generation`'s conclusion.

## Performance Intent
No performance intent is declared for this work item.

## Migration Posture
- PM-001 [PC-001] kitRepublish: `.claude/skills/pnext-item/SKILL.md` is a digest-tracked kit source (`registry/repos.lock`); merging this item requires `scripts/repos.sh relock` before opening the PR and naming `registry/repos.lock` as EXPECTED DRIFT in the PR body per the kit-digest notice `take` printed on claim, and the edit becomes live for other repos only once the coordination kit is next republished/pinned there (a post-merge obligation, not part of this item's own gates).

## Generated View Impact
- GV-001 [PD-001] workModel: This item's own `readiness/2496/work-model.json` is the only generated view the change touches — refreshed by `verify`/`ship`, not authored by hand; the delivered change itself (`.claude/skills/pnext-item/SKILL.md`) is documentation, not a generated view.

## Accepted Deferrals
No accepted plan deferrals recorded.

## Planning Findings
No blocking planning findings recorded.

## Advisory Notes
- Optional Governance pointers remain compatibility facts only.
- This plan deliberately does not touch `src/FS.GG.Coord.*` — the write-side mechanism already exists and is unconditional on `--apply` (`.github#2488`); only the documented worker instruction is missing.

## Lifecycle Notes
- Next lifecycle action: `fsgg-sdd tasks --work 2496`.
