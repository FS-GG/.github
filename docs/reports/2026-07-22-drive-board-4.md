# drive-board run — 2026-07-22 (fourth run)

Cross-repo Coordination board burn-down (ADR-0053 loop). Engine: source build in `.github`
(`fsgg-coord-engine` 0.8.0). GraphQL budget healthy throughout; **no rate-limit back-offs**, no
double-claims. This run closed the game-shell decomposition and the last two board decisions, leaving
the board **genuinely empty**.

## Shipped — merged + done-stamped (verified against ground truth)

| Item | PR | What landed |
|---|---|---|
| #1001 | #1005 | Game shell **persists display settings** (resolution/mode) across restart — a JSON codec beside `KeymapCodec`, total decode. |
| #1002 | #1006 | Release-lane **`Product.Tests` behaviour coverage** for the shell (router/Esc/menu/rebind/display), pure + additive, durable spine untouched. |
| #994 | #1004 | Scaffold-emitted **visual-representation Coverage gate** consuming #990's catalog — a scaffolded product reds when a gameplay element has no token and no reasoned Hidden opt-out. Completes the symbology-coverage feature (#989 check + #990 catalog + #994 gate). |
| #998 | #1007 | Permanent **`owner-documented` ledger category** — the 37 Game.Core Ai/Ballistics/Dice/Effects vals (vendored by #984, documented in FS.GG.Game's fs-gg-game-core per ADR-0063) are recorded as terminally-satisfied cross-repo, not pending `tracked`. |
| **#1000** | **#1008** | **The keystone: the generic game shell is now the turnkey DEFAULT launch.** A fresh game scaffold boots into the shell menu (title + Start/Config/Exit, Esc pause, Settings incl. resolution/fullscreen + key rebinding + persisted display) on the pointer host. Moved the game family onto the interactive host and revised the durable `GovernanceTests`/`BehaviorTests` pins to **model-agnostic behaviour assertions** (per the #981 governance-behaviour precedent) + the #139 keyboard-only boundary. |

## Decisions adjudicated (by @ehotwagner)

- **#1003 → (A)** — make the shell the turnkey default; move the game family to the pointer host and revise
  the durable test spine atomically in #1000. Closed; #1000 unblocked, widened, and landed (#1008).
- **#998 → (3)** — permanent `owner-documented` ledger category; no tension with #984 (surface stays
  vendored) or ADR-0063 (docs stay owner-sourced). Implemented (#1007).

## Parallelism note

Two mid-run parallelism ceilings were diagnosed and fixed rather than accepted: (1) follow-ups filed
**without `Paths:`** (#993/#994) were unschedulable — added touch-sets; (2) an **over-broad `template/base/`**
touch-set on #994 glued the Rendering lane — narrowed it. Across repos, waves ran up to 7 workers
concurrently (the testspec pass); within Rendering the ceiling is genuine touch-set overlap on the
scaffold-emission area.

## Termination

Board **genuinely empty**: no non-`Done` items, 0 live claims, `lint` clean, every repo's `batch` empty.
No human-blocked items remain — both outstanding decisions were adjudicated and their work landed.

## Tally

- **5 merged + done-stamped** (#994, #998, #1000, #1001, #1002)
- **2 decisions adjudicated** (#1003, #998) and their implementations landed
- **0 rate-limit back-offs, 0 double-claims, 0 orphaned claims**
