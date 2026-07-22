# drive-board run — 2026-07-22

Cross-repo Coordination board burn-down (ADR-0053 loop, `drive-board` skill). Host reconciled the
board each wave, fanned fresh disposable subagents across repos, verified every claim against ground
truth, and re-planned. Engine: source build in `.github` (`fsgg-coord-engine` 0.8.0). GraphQL budget
stayed healthy throughout; **no rate-limit back-offs**.

## Scope

The board was already burned down to `Done` except for **6 items filed earlier this session** from the
Rougue1 / MiniTank1 / TowerDefense1 SDD feedback reports (M3–M10). All 6 were parked in `Backlog`. The
operator promoted the **5 implementation items** to `Ready`; the 6th — `FS.GG.Governance#297`, a
doctrine *question* with `Paths: none` — was deliberately left parked (a coding worker cannot resolve
it). This run drove the 5.

## What shipped (merged + done-stamped, verified against ground truth)

| Item | Repo | PR | Merge | What landed |
|---|---|---|---|---|
| #460 | game | #461 | `62ca780` | Generalized the `fs-gg-game-core` look-alike warning from record-field to **DU case-name** collisions (`EnemyKind.Boss` vs `RoomType.Boss`, consumer-vs-framework). Skill-doc + regenerated manifest. |
| #979 | rendering | #980 | `83086cc` | Added `runAppWithWindowBehaviorAndAudioAndPersistence` — the missing **audio×window×persistence** corner of the SkiaViewer launcher matrix (purely additive; the interpreter already composed all four). Surface `.fsi` + tests. |
| #645 | sdd | #647 | `6cc9e87` | Fixed the confirmed **`clarify` `DEC-###`-in-prose** false duplicate: a cited upstream decision id is now a reference, not a second declaration. Extracted #541's declaration-position predicate into a shared helper. |

Each verified: issue `closed`, PR `merged=true`, board `Status=Done`.

## Blocked at root cause (not forced — filed and wired)

Two items turned out to need a decision or change a worker seat cannot make. Both were correctly
root-caused rather than half-fixed:

| Item | Repo | Blocked by | Why |
|---|---|---|---|
| #644 | sdd | **Game#462** (filed) | The incomplete vendored api-surface is **frozen at scaffold time by the provider template**; the game modules + malformed `Pathfinding.fsi` live in FS.GG.Game.Core's packed surface and the FS.GG.Templates scaffold wiring. SDD is forbidden (ADR-0004 FR-009/FR-014) from embedding a package literal, so no honest SDD-side fix exists. |
| #646 | sdd | **SDD#649** (filed) | Collapsing a deferral's obligations contradicts a **shipped, tested design (#310 AC9)** that deliberately keeps a `PD-###` deferral-mirror's own task. Reducing the count is a genuine obligation-model design decision. |

## Follow-ups queued (Backlog, not in the approved batch)

- **SDD#648** — the `tasks`/`analyze` half of the prose-id-token family (#645). A separate primitive
  (`Plan.planSourceIdsInLine` scans every prose id as a `SourceId`), scoped out of #645's PR to keep
  it reviewable. A real, workable implementation item awaiting triage.

## Outstanding — awaiting a human decision (not the loop's to unblock)

Three doctrine/design questions now gate all further progress on this cluster:

- **Governance#297** — should the model-agnostic governance test assert launch *behavior*, not a
  literal launch-line substring? (`Paths: none` — doctrine.)
- **Game#462** — must FS.GG.Game.Core's packed api-surface be authoritative-and-complete for the game
  profile (and fix the `Pathfinding.fsi` triplication)? Gates #644.
- **SDD#649** — is a scaffold-generated pure deferral-mirror `PD-###` a real design decision or a
  redundant mirror? Gates #646.

## Termination

Stopped on a genuine terminal state: `batch` empty for every rostered repo, 0 live claims, `lint`
clean, and every remaining non-`Done` item either blocked on one of the three decisions above or parked
in `Backlog`. Per the skill, an item blocked on a human is not the loop's to unblock.

## Tally

- **3 merged + done-stamped** (#460, #979, #645)
- **2 blocked at root cause** (#644→#462, #646→#649)
- **4 new issues surfaced by the work** (#462, #648, #649, plus the pre-existing parked #297)
- **0 rate-limit back-offs**, 0 orphaned claims, 0 workers double-claimed (each minted a distinct id)
