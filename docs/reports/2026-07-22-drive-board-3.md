# drive-board run — 2026-07-22 (third run)

Cross-repo Coordination board burn-down (ADR-0053 loop, `drive-board` skill). Engine: source build in
`.github` (`fsgg-coord-engine` 0.8.0). GraphQL budget healthy throughout; **no rate-limit back-offs**,
no double-claims (every worker minted a distinct id).

## Scope

Filed three researched findings from the MiniTank1 M11 / TowerDefense1 M9+M10 capstone feedback first
so drive-board could work them, then burned down the promoted set (those + the two parked items from
the prior run, #984 and #1349).

## Shipped — merged + done-stamped (verified against ground truth)

| Item | Repo | PR | What landed |
|---|---|---|---|
| #653 | sdd | #655 | The **"stale" substring bug**: `Plan.planDecisionStatus` matched the word "stale" anywhere in a decision's prose via a `containsWord` scan. Rewritten to match the declaration-position status marker **exactly** — the #541/#645/#648 declaration-vs-prose pattern, now for the status keyword. (Routes TowerDefense1#22.) |
| #1349 | .github | #1350 | **User-owned boards need no login in config**: new `OwnerKind.Viewer` resolves a user board from the token's own GraphQL `viewer` identity. `FSGG_COORD_OWNER_TYPE=user` with no `FSGG_COORD_OWNER` → viewer-scoped. Org path byte-identical. (Follow-up to #1344.) |
| #984 | rendering | #987 | **Un-waived** the full game-profile api-surface into `template/base/docs/api-surface/**` (8 new `.fsi` mirrors + SkiaViewer persistence vals) + a module-completeness gate so a future re-waive reds. Closed the cross-product api-surface gap. |
| #986 | rendering | #988 | Documented the 12 Scene.Animation vals in `fs-gg-scene`; reconciled the surface-doc-ledger; **routed the 40 Game.Core vals to Game#466** (fs-gg-game-core is Game's). |
| #654 | sdd | #656 | **Issue-vs-test cross-check** (`BugGuardCheck`): a `pins-bug #n` / `guards #n` test-marker grammar + a pure classifier that warns when a marked test's issue is still open — the TowerDefense1#14 hazard (a green test pinning a filed bug). Live-issue-state + CI-scan left as a documented seam. |

**5 merged.** On #984 landing, **SDD#644** (the SDD surface issue for the incomplete api-surface) was
confirmed resolved by its three root-cause halves (Game#462 + Rendering#982 + Rendering#984) and closed.

## Recurrences already fixed this session (will clear once a new fsgg-sdd publishes)

The capstone reports re-hit themes fixed earlier today, on the still-pinned 0.22.0:
- **Deferral fan-out** (#7) → fixed by SDD#646 (both PD and CR mirrors fold).
- **Prose-id-token parsing** → fixed by SDD#645 + #648.
- **plan→analyze scaffold-prose gap** (#2/#530) → escalated on #530; structural fix still pending.

## Follow-ups queued (Backlog)

- **Game#466** — the fs-gg-game-core half of the doc cascade: document the 40 un-waived Game.Core sim
  vals (Ai/Difficulty, Ballistics, Dice, Effects, Fov, Los) in their owner-sourced skill, and reconcile
  the surface-doc-ledger rows the #986 worker left `tracked`. Clean, workable. (Touch-set added
  post-filing — the #986 worker filed it without one.)
- **Rendering#989** — operator request: a visual-representation **coverage check**. Every gameplay
  element (doors, bombs, explosions, projectiles) must map to a visible token OR an explicit
  hidden-by-mechanic opt-out — the visual analog of match exhaustiveness. Design-needing (Effort L).
- **Rendering#990** — operator request, sibling of #989: improve **fs-gg-symbol-design** to enumerate
  the FULL gameplay-element set (not just the unit roster) and produce/maintain the comprehensive
  element↔visual **catalog** that #989 checks. Design-needing (Effort L).

## Routing note

The MiniTank1 M11 finding that `FS.GG.Game.Harness` (the fs-gg-playtest package) is unavailable to a
game-profile scaffold was recorded as a **comment on the closed Rendering#927** (its exact prior fix) —
either a stale scaffold pin or a regression, for triage — cross-referenced #984.

## Termination

Stopped on a genuine terminal state: `batch` empty for every rostered repo, 0 live claims, `lint` clean.
The three remaining non-`Done` items are Backlog parks — #466 clean-workable, #989/#990 design-needing
(awaiting operator design steer on how a game declares its renderable-element set and the catalog
format). Not the loop's to auto-expand.

## Tally

- **5 merged + done-stamped** (#653, #654, #984, #986, #1349); **#644 closed** (resolved by its halves)
- **3 findings filed from capstone feedback** (#653, #654, + the #927 comment)
- **2 operator feature requests boarded** (#989, #990) + **2 doc follow-ups** (#466, and #654's seam)
- **0 rate-limit back-offs**, 0 double-claims, 0 orphaned claims
