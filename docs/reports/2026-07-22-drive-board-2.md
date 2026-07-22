# drive-board run — 2026-07-22 (second run)

Cross-repo Coordination board burn-down (ADR-0053 loop, `drive-board` skill). Engine: source build in
`.github` (`fsgg-coord-engine` 0.8.0). GraphQL budget stayed healthy throughout; **no rate-limit
back-offs**, no double-claims (every worker minted a distinct id), no orphaned claims.

## Scope

Drove the 7 schedulable items set up earlier in the session: the 4 decided-implementation items
(#462, #269→Rendering, #649, plus Templates#270→Rendering), the #645 follow-up (#648), and the two
newly-filed coordination features (#1343 retrofit board-wiring, #1344 user-owned boards). The
Governance#297 decision's implementation and the Game#462 decision's Templates half were re-homed to
Rendering mid-run (see *Routing corrections*).

## Shipped — merged + done-stamped (verified against ground truth)

| Item | Repo | PR | What landed |
|---|---|---|---|
| #649 | sdd | #651 | Collapse **pure plan-side `PD` deferral-mirror** into the keep-visible obligation (narrow #310 AC9); over-collapse guard added. |
| #646 | sdd | #652 | The **checklist-side (`CR`) residual** #649 didn't cover — folded structurally (a checklist review has no author body, so refs are dispositive). One obligation per deferral now. |
| #648 | sdd | #650 | `tasks`/`analyze` half of the prose-id-token family — scope reference-resolution to bracket-tag positions so a cited upstream id in prose is not a dangling reference. |
| #462 | game | #463 | Game.Core packed-api-surface **pack-time completeness gate** (a public module without a companion `.fsi` fails the release). Pathfinding source confirmed clean. |
| #464 | game | #465 | CI job wiring the packed-api-surface completeness test into `gate.yml`. |
| #981 | rendering | #983 | Scaffold governance test asserts host **behavior** (effects reach their sinks), not a launch-line substring; survival test across a launcher rename. (impl of Governance#297.) |
| #982 | rendering | #985 | Root-caused the `Pathfinding.fsi` **triplication**: three duplicate `+ type` lines in `api-surface-manifest.txt`, invisible because the coverage set collapses repeats and the drift check compared tripled-vs-tripled. Fixed + fail-closed duplicate-include guard. (Rendering half of Game#462.) |
| #1343 | .github | #1345 | `new-sdd-workspace retrofit` — idempotently wire the coordination kit + board onto a workspace scaffolded `--no-coordination` (inverse of #1142). |
| #1344 | .github | #1346 | Coord engine supports **user-owned** Projects v2 boards (`OwnerKind` Org\|User, `FSGG_COORD_OWNER_TYPE`); org behavior byte-identical. |

**9 merged.**

## Routing corrections (workers caught two mis-filings)

Both #269 (governance test) and #270 (api-surface vendoring) were filed against **FS.GG.Templates**,
but the scaffold template base — the emitted `GovernanceTests.fs` *and* the vendored
`docs/api-surface/**` — lives in **FS.GG.Rendering** `template/base/`. Templates only pins/consumes the
published `FS.GG.UI.Template` package. The workers root-caused and re-homed rather than forcing a fix:
- #269 → **Rendering#981** (shipped above); #269 closed as resolved-by-#981.
- #270 → **Rendering#982** (shipped above); #270 closed, re-homed.

Lesson for future filing: scaffold-emitted product artifacts belong to Rendering, not Templates.

## Follow-ups queued (Backlog)

- **Rendering#984** — vendor the **full** game-profile api-surface verbatim by **un-waiving** the
  modules currently dropped via `waive` lines in `scripts/api-surface-manifest.txt` (Effects, Ballistics,
  Dice, Los, Fov, Ai+Difficulty, Visibility, Scene.Animation/Transform, SkiaViewer vals). Split off #982
  because it is cross-cutting (skill pointers + Skill-parity harness + `scaffold-map.md`) beyond the
  `api-surface/` touch-set. This is the completeness remainder of the FS.GG.SDD#644 gap.
- **FS.GG.SDD#644** — the SDD surface issue for the incomplete api-surface — re-pointed `Blocked by`
  #984 (its Game half #462 + Rendering well-formedness half #982 both landed; the vendoring remainder is
  #984).

## Downstream lockstep (noted, not filed — contingent on a Rendering republish)

When Rendering ships a new `FS.GG.UI.Template` version carrying the #981/#982 changes, FS.GG.Templates
re-pins via `scripts/bump-rendering-pin.sh`, and its composition-layer launch-line grep
(`tests/composition/stages/05-build.sh` ~line 62) should relax from a substring pin to a behavior check.
Recorded in #981's body.

## Termination

Stopped on a genuine terminal state: `batch` empty for every rostered repo, 0 live claims, `lint` clean.
The two remaining non-`Done` items (#984 Backlog, #644 blocked by it) are real scheduled work, not
human-blocked — a Backlog park is a deliberate hold, and #644 is honestly blocked on #984. Not the
loop's to auto-expand.

## Tally

- **9 merged + done-stamped** (#462, #464, #646, #648, #649, #981, #982, #1343, #1344)
- **2 mis-routings corrected** (#269→#981, #270→#982) and their originals closed
- **2 follow-ups queued** (#984 un-waiving, and #644 re-pointed at it)
- **0 rate-limit back-offs**, 0 double-claims, 0 orphaned claims
