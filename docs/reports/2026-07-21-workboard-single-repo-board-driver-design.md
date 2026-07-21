# workBoard — a single-repo, board-driven driver skill for product workspaces

- **Date:** 2026-07-21, hub at `.github@264a67a` (main)
- **Owner:** `.github` (authors the skill + the driver package) with FS.GG.SDD (re-pins + republishes the CLI).
- **Status:** Design + roadmap + a proposed **ADR-0064**. No code lands here; §8 is a filable milestone sequence.
- **Question:** Make a board-driven, disposable-subagent work loop available *inside* a scaffolded product workspace, targeting that workspace's own project board — or failing gracefully when it has none. The org's existing board loop (`drive-board`) is org-operator-only and cannot run in a single-repo tree (ADR-0057). What is the right skill, and what does delivering it cost?
- **Method:** two parallel code audits — the `new-sdd-workspace` tool (`scripts/NewSddWorkspace/Program.fs`) and the SDD scaffold materializer (`FS.GG.SDD/.../CommandWorkflow/*`, `registry/skills.yml`, ADRs 0011/0014/0017/0053/0054/0057/0063). Findings are grounded in those files, cited inline.

---

## 1. TL;DR

- **Not `drive-board`.** ADR-0057 already decided `drive-board` is `scope: operator, materializes-when: "false"` — never delivered into a product tree — because it fans workers across *sibling repos* and a single product tree has no siblings. Materializing it would ship an inert, misleading skill. That decision stands.
- **A new skill fills the empty quadrant.** The loop family has four cells; three are filled. `workBoard` is the fourth: **single-repo, board-driven** — `workRoadmap`'s disposable-subagent loop with the ledger being the *board* instead of a markdown file; equivalently, `drive-board` minus the cross-repo fan-out.
- **The workspace already carries everything it needs.** `new-sdd-workspace`'s coordination wiring (default ON) records the board identity as `FSGG_COORD_OWNER`/`FSGG_COORD_PROJECT` env and materializes `check-board`, `pnext-item`, `intra-repo-parallel-work`, and the `fsgg-coord` shim. `workBoard` **composes what is already there**; it needs no new tooling.
- **"Fail gracefully" has a precise hook.** When a workspace was scaffolded `--no-coordination`, none of that lands. `workBoard` detects the missing board env / `fsgg-coord` / `pnext-item` and stops with a clear message pointing at `workRoadmap` (markdown) as the alternative.
- **Delivery is cheap and rides the existing driver fabric.** `workBoard` is a second `.github`-authored `scope: driver` skill (like `workRoadmap`), shipped via the embedded `FS.GG.Drivers` package. Adding a `driver`-scope row is **not schema growth** (scope validation is structural since ADR-0061) — so **no publish-before-flip validator dance**. The only versioned steps are: publish a new `FS.GG.Drivers` carrying the bytes, and re-pin/republish `FS.GG.SDD.Cli`.
- **The work spans two repos:** `.github` (author skill, add to `FS.GG.Drivers`, add registry row, publish) → FS.GG.SDD (bump the `FS.GG.Drivers` pin, republish the CLI). No `new-sdd-workspace` change is required (decided: target the wired board, don't add board creation).

---

## 2. Why a new skill, not a materialized `drive-board`

The loop skills form a 2×2 over *scope of the tree* and *kind of ledger*:

| | markdown ledger | board ledger |
|---|---|---|
| **single repo** | `workRoadmap` — `scope: driver`, `materializes-when: always` ([ADR-0053](../adr/0053-roadmap-driven-milestone-loop-disposable-sdd-subagents.md)/[0054](../adr/0054-workroadmap-delivery-fabric-a-github-authored-product-materialized-driver.md)) | **`workBoard` — this doc** |
| **cross-repo** | (n/a) | `drive-board` — `scope: operator`, `materializes-when: "false"` ([ADR-0057](../adr/0057-operator-scope-a-github-authored-never-materialized-skill-class.md)) |

ADR-0057's deciding fact: `drive-board` "runs **only** from an operator checkout where every rostered repo is present as a sibling … a **single product tree has no siblings**, so `drive-board` cannot run in one and must never be delivered into one." Its composed dependencies (`check-board`/`pnext-item`/`intra-repo-parallel-work`) are **kit** skills delivered to the 8 framework repos, not to product trees. So a materialized `drive-board` would be a skill whose fan-out has no targets and whose dependencies are absent. `workBoard` is the honest single-repo counterpart — and, unlike `drive-board`, its dependencies **are** present in a coordination-wired workspace (§3).

## 3. What a workspace already has (the enabling finding)

`new-sdd-workspace` (`scripts/NewSddWorkspace/Program.fs`) does **not create** a board — it **wires** the workspace to an existing one and records its identity:

- **Board identity → env** in `.claude/settings.json` (`writeCoordinationEnv`, merge-not-clobber): `FSGG_COORD_OWNER`, `FSGG_COORD_PROJECT` (default `FS-GG` / `Coordination`; override with `--board owner/title`), optional `FSGG_COORD_CHORE_LOCKS`. This is the file a workspace skill reads to discover its board.
- **Board tooling materialized** (coordination wiring, default ON): the `scripts/fsgg-coord` shim + `fs.gg.coord.cli` tool, and the skills `check-board`, `pnext-item`, `intra-repo-parallel-work`, `cross-repo-coordination`.
- **SDD skills always materialized**: the 16 `fs-gg-sdd-*` lifecycle skills.
- **`--no-coordination`** skips all board wiring — the graceful-fail case.

So a default workspace is already board-aware; the only missing piece is the *driver skill that consumes it the way `workRoadmap` consumes a markdown file.* The repo resolves from the git remote and the board from env, so `fsgg-coord next/batch/ready` inside a workspace already operate on **this repo's items on the wired board** with no extra configuration.

## 4. The design: `workBoard`

`workBoard` is the parent loop; each unit of work is a fresh disposable subagent; when its item is merged and done-stamped, the subagent dies and the parent re-plans against the board it just changed. It owns the single-repo scheduling loop and delegates everything else.

### 4.1 Board discovery and graceful failure (the load-bearing precondition)
Before the loop, `workBoard` checks, in order, and **stops cleanly** on the first miss — naming the fix, never crashing:

1. `FSGG_COORD_OWNER` and `FSGG_COORD_PROJECT` are set (board wired). Absent → *"this workspace has no coordination board (scaffolded `--no-coordination`?). Use `workRoadmap` for a markdown roadmap, or re-wire with `new-sdd-workspace --board owner/title`."*
2. `scripts/fsgg-coord` resolves and `check-board` + `pnext-item` are present. Absent → same class of message.
3. `fsgg-coord` can read the board (auth + reachability). A workspace pointed at its **own** (non-`FS-GG`) board needs the post-0.4.0 engine (#1140) for the offer/chore path; the default org board works on any engine. A version/permission failure stops with the reset/permission guidance, not a stack trace.

This is the whole of "or fail gracefully": the skill is always materialized, and it decides at runtime whether the workspace is board-capable.

### 4.2 The loop (single-repo `drive-board`)
Repeat until §4.5 says the board is genuinely done:

1. **Reconcile** — run `check-board` (already in the workspace). Clears stale claims, re-verifies `Blocked by` edges, surfaces rollup-ready epics and human-blocked items.
2. **Size the wave** — `fsgg-coord batch --repo <this-repo> -n <cap> --json` returns a maximal touch-set-disjoint set. **Touch-set disjointness is load-bearing here** — unlike `drive-board`, all workers share one working tree, so items must be file-disjoint (this is exactly what `take` + `intra-repo-parallel-work` enforce). Cap against the shared rate budget (§4.6).
3. **Spawn fresh subagents** (`isolation: "worktree"`), one per slot, each running the worker brief (§4.3). Concurrency is bounded — one repo, one shared account.
4. **Verify against ground truth** — never the subagent's word. `fsgg-coord ready --repo <r> --all --json`: the item it claimed is `Done` + issue closed (or `Blocked` with an edge). A "merged" claim that isn't → failed item; adopt a green orphan PR if the harness died between green and merge.
5. **Re-plan** — the board moved (new blockers, new follow-ups, finished items). Go to step 1.

The parent never implements an item itself. It schedules.

### 4.3 The worker: pnext-item envelope, SDD-lifecycle escalation *by complexity*
The invariant per-item harness is **`pnext-item`** (already materialized): mint a distinct worker id, `take` (gate on exit code 0), read the item's comments, worktree from `origin/main`, implement within the declared `Paths:`, open a PR, review, merge on green, `done --flip`. Inside that envelope, **the depth of the implementation scales with the item's complexity** (the decision you asked for):

- **Simple item** (Effort `S`/`M`, no `needs-sdd` marker): implement directly inside `pnext-item` — a focused change, PR, merge. No lifecycle overhead.
- **Complex item** (Effort `L`/`XL`, or a `needs-sdd` label/`Blocked by`-a-charter signal): the worker runs the full **`fs-gg-sdd-*` lifecycle** (charter/specify → plan → tasks → implement → verify → ship) for the implementation phase, *still inside* `pnext-item`'s one claim/merge envelope. Both skill sets are present in a wired workspace, so this needs no new machinery — only a documented branch in the worker brief keyed on the item's complexity signal.

This keeps a single claim/merge/done-stamp discipline while letting a heavyweight feature get the lifecycle it deserves and a one-line fix stay a one-line fix.

### 4.4 The landmine (inherited from `drive-board` §1)
Every subagent of one Claude Code session shares one `CLAUDE_CODE_SESSION_ID`, so N bare `whoami`s collapse to one worker id and the claim lock cannot separate them. The worker brief's **first act** must be `eval "$(scripts/fsgg-coord whoami --mint)"` inside its own worktree, and it must stop if `whoami` warns it inherited the session id. The host verifies the *outcome* (§4.4 verify), not the ids it cannot see.

### 4.5 Termination
Stop only when a **fresh** `check-board` shows: no schedulable item in the repo, no live claims, and no rollup-ready epic / cleared-but-`Blocked` item. A human-blocked item is surfaced and is **not** a reason to keep spinning. Then the parent writes `docs/reports/<date>-workboard.md` and lands it — the same close-out `workRoadmap` uses.

### 4.6 Concurrency
One repo, one account, one shared rate budget (GraphQL 5,000 pt/hr; REST carries the claim lock). Cap in-flight workers conservatively (`--workers N`, default low), share the 90s scan cache, and treat any worker's `EX_RATE` (75) as a fleet stop → drain, back off to the named reset, `flush --dry-run`, resume. Identical discipline to `drive-board` §3.

## 5. Delivery — the driver fabric, no schema growth

`workBoard` is a second `.github`-authored `scope: driver` skill. The mechanics (ADR-0054/[0063](../adr/0063-scaffold-materializer-sources-skills-from-the-owner-repo.md), #1300/#1304/#1306):

1. **Author** `workBoard/SKILL.md` in `.github`'s two skill roots (`.claude` + `.agents`), byte-identical (ADR-0011).
2. **Add its bytes to the `FS.GG.Drivers` package** (`src/FS.GG.Drivers`, `stage-drivers.py`) — today it carries only `workRoadmap`.
3. **Add the producer-manifest row** (`registry/driver-skill-manifest.json`) and the reconciled `registry/skills.yml` row: `{ id: workBoard, scope: driver, owner: .github, materializes-when: always, sha256: … }`. Because `driver` already exists and scope validation is structural (ADR-0061), **this is not schema growth** — no `schemaVersion` bump, no CLI-validator publish-before-flip.
4. **Publish** a new `FS.GG.Drivers` version; regenerate the projection (`docs/registry/compatibility.md`) and `registry/skills.CHANGELOG.md`.
5. **FS.GG.SDD**: bump the `FS.GG.Drivers` pin, rebuild so the bytes embed, and **republish `FS.GG.SDD.Cli`**. The next `new-sdd-workspace`/`fsgg-sdd scaffold` materializes `workBoard` into every workspace (`materializes-when: always`, delivered at scaffold time via the embedded driver bytes — offline, ADR-0063).

`materializes-when: always` is correct despite the runtime board dependency: the coordination kit is wired by `new-sdd-workspace` *after* `fsgg-sdd scaffold` runs the driver materializer, so a scaffold-time predicate could not see it. The skill therefore always lands and **checks for its board at runtime** (§4.1). This is why "fail gracefully" is a runtime property, not a materialization gate.

## 6. Decisions taken (for the ADR)

- **Board source:** target the already-wired board (`FSGG_COORD_OWNER/PROJECT`); **do not** add board creation to `new-sdd-workspace`. (A `--create-board` option is a possible future enhancement, out of scope here.)
- **Worker:** `pnext-item` envelope with **SDD-lifecycle escalation by item complexity** (§4.3).
- **Name:** `workBoard`.

## 7. Non-goals

- **`drive-board` is unchanged** — it stays `operator`/never-materialized. This adds a sibling; it does not reclassify the cross-repo skill.
- **No board creation** in `new-sdd-workspace` (this round).
- **No new coordination tooling** — `workBoard` composes the kit the workspace already has; if the kit is absent, it fails gracefully rather than shipping its own.
- **No change to the `skill-registry` schema** — a new `driver` row only.

## 8. Roadmap

Each milestone is independently shippable; the delivery ones are ordered (publish before re-pin).

- **W1 — author `workBoard/SKILL.md`** in both `.github` skill roots (the §4 protocol; reuse `drive-board`'s structure minus fan-out, `workRoadmap`'s close-out). *(repo: `.github`)*
- **W2 — file the ADR-0064** recording §2/§6 and the relation to ADR-0053/0054/0057/0063. *(repo: `.github`)*
- **W3 — add `workBoard` to `FS.GG.Drivers`** (`stage-drivers.py` + `driver-skill-manifest.json` + reconciled `skills.yml` row + projection + CHANGELOG); **publish** a new `FS.GG.Drivers`. *(repo: `.github`; depends on W1)*
- **W4 — re-pin + republish `FS.GG.SDD.Cli`** onto the new `FS.GG.Drivers`; verify a scaffold materializes `workBoard`. *(repo: FS.GG.SDD; depends on W3)*
- **W5 — graceful-fail + happy-path verification**: scaffold one wired workspace and one `--no-coordination` workspace; confirm `workBoard` drives the first and stops cleanly on the second. *(repo: FS.GG.SDD / `.github`; depends on W4)*

## 9. Appendix — the graceful-fail contract, in one paragraph

> `workBoard` is always materialized into a scaffolded workspace, but it **refuses to run unless the workspace is board-capable**: `FSGG_COORD_OWNER`/`FSGG_COORD_PROJECT` set, `scripts/fsgg-coord` present, and `check-board`+`pnext-item` materialized — i.e. the workspace was wired for coordination (not `--no-coordination`) and its `fsgg-coord` engine can read the board. On any miss it prints one clear line naming the cause and the alternative (`workRoadmap` for a markdown roadmap, or re-wiring via `new-sdd-workspace --board`), and exits non-zero without touching the board.
