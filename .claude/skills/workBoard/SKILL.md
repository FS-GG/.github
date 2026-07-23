---
name: workBoard
description: Drive a single product workspace's own wired project board to completion, fanning work out to fresh disposable subagents and re-planning after each wave — the board-ledger sibling of workRoadmap and the single-repo sibling of drive-board. Use inside a coordination-wired scaffolded workspace when you want its board burned down without babysitting it: the host reconciles the board (check-board), sizes a touch-set-disjoint wave against this one repo, spawns one fresh subagent per slot to run pnext-item, checkpoints and finalizes schema-v2 development feedback per item, verifies each claimed result against ground truth (never the subagent's word), despawns it, and loops until the board is genuinely empty. A simple item is implemented directly; a complex one (Effort L/XL or a needs-sdd signal) runs the full fs-gg-sdd-* lifecycle inside the same claim/merge/done-stamp envelope. Refuses to run — cleanly, naming the fix — when the workspace has no board (scaffolded --no-coordination), no fsgg-coord engine, or no coordination kit. Composes check-board (analyze), pnext-item (the worker), fs-gg-feedback-report (checkpoint/synthesis), and intra-repo-parallel-work (the claim/worktree/touch-set protocol) the workspace already carries. Canonized by ADR-0064; see also ADR-0053, ADR-0054, ADR-0057, ADR-0063.
---

# workBoard

One command's worth of intent: **"take this workspace's own project board and burn it down, as
parallel as it is safe to be inside one tree — and don't make me drive."** The board is the plan and
the ledger both: the host reads it to find schedulable work, and each item's worker writes back to it
when the work lands and is done-stamped. Every item gets a **fresh subagent with fresh context**; when
its item is merged and stamped, that subagent **dies**, and the host re-plans against the board it just
changed. The loop ends when a fresh reconcile shows the board is genuinely done, and then the host
writes the report and lands it.

This skill fills the last cell of a 2×2 the org's loop family had left empty (ADR-0064) — **single
repo × board ledger**:

| | markdown ledger | board ledger |
|---|---|---|
| **single repo** | [workRoadmap](../workRoadmap/SKILL.md) — `driver`, always materialized | **`workBoard` — this file** |
| **cross-repo** | (n/a) | [drive-board](../drive-board/SKILL.md) — `operator`, never materialized |

So it is the **board-ledger sibling of [workRoadmap](../workRoadmap/SKILL.md)** (same parent-loop-of-
disposable-subagents shape, but the ledger is the wired board rather than a markdown file) and the
**single-repo sibling of [drive-board](../drive-board/SKILL.md)** (same reconcile → size-wave → spawn →
verify-against-ground-truth → re-plan shape, but the fan-out is *one repo* instead of every repo).

It owns exactly one thing neither of its parts does: **the single-repo, board-driven scheduling loop**.
Everything else it delegates and does not re-teach:

- **Analysis** is [check-board](../check-board/SKILL.md)'s. The host does not hand-roll board truth — it
  runs the reconcile pass and reads its findings. That is also where **blockers that emerge mid-work**
  are caught: nothing re-checks a `Blocked by` when its blocker clears, so each planning pass re-runs it.
- **The worker** is [pnext-item](../pnext-item/SKILL.md). Each subagent runs it start to done-stamp; the
  host never implements an item itself. Every item goes through a worker that claims with the lock,
  isolates in a worktree, opens a PR, and merges on green.
- **The claim/worktree/touch-set protocol** is
  [intra-repo-parallel-work](../intra-repo-parallel-work/SKILL.md)'s. Here it is **load-bearing in a way
  it is not for drive-board**: all of workBoard's workers share **one working tree**, so the `Paths:`
  disjointness `take` already enforces is what keeps a wave from colliding. The host leans on it; it
  does not re-implement it.

## Where this runs, and the precondition that decides everything

A **coordination-wired scaffolded product workspace** — one that `new-sdd-workspace` (or `fsgg-sdd
scaffold`) built with coordination wiring ON (the default). That wiring, and nothing this skill ships,
is what makes the loop possible: it records the board identity as `FSGG_COORD_OWNER`/`FSGG_COORD_PROJECT`
env in `.claude/settings.json`, vendors the `scripts/fsgg-coord` shim + engine, and materializes
`check-board`, `pnext-item`, and `intra-repo-parallel-work`. workBoard **composes what is already
there**; it needs no new tooling. Its absence — a workspace scaffolded `--no-coordination` — is exactly
the "fail gracefully" boundary below.

### The graceful-fail preconditions (ADR-0064 §4.1)

workBoard is **always materialized** into a scaffold — the coordination kit is wired *after* the scaffold
materializer runs, so no scaffold-time predicate could see it (ADR-0063), which is why "fail gracefully"
is a **runtime** property, not a materialization gate. So before the loop, check these **in order** and
**stop cleanly on the first miss** — print one clear line that names the cause *and* the alternative, and
**exit non-zero without touching the board**. Never crash, and never degrade into a plain edit loop:

1. **The board is wired.** `FSGG_COORD_OWNER` and `FSGG_COORD_PROJECT` are set (read them from the
   environment / `.claude/settings.json`). Absent → *"this workspace has no coordination board (scaffolded
   `--no-coordination`?). Use [workRoadmap](../workRoadmap/SKILL.md) for a markdown roadmap, or **retrofit**
   the coordination kit + board onto it with `new-sdd-workspace retrofit <workspace>` (add `--board
   owner/title` for a non-default board) — the idempotent inverse of the scaffold-time wiring, which is
   exactly what `--no-coordination` left off (#1343)."*
2. **The kit is present.** `scripts/fsgg-coord` resolves, and `check-board` + `pnext-item` are
   materialized in this tree. Absent → the same class of message: the workspace was not wired for
   coordination, so this is not the skill for it.
3. **The engine can read the board.** `fsgg-coord` can reach and read the board (auth + reachability).
   The default org board (`FS-GG` / `Coordination`) works on any engine; a workspace pointed at its
   **own** (non-`FS-GG`) board needs the post-0.4.0 engine for the offer/chore path (#1140) — a
   version/permission failure stops with the reset/permission guidance it prints, **not** a stack trace.
   That own board can be **user-owned**, not only org-owned (#1344, #1349): set `FSGG_COORD_OWNER_TYPE=user`
   with an explicit `FSGG_COORD_OWNER` for a named account, or `FSGG_COORD_OWNER_TYPE=user` with **no**
   `FSGG_COORD_OWNER` to drive the token's **own** `viewer` board with no login in config (the CLI labels
   it `@me`). Unset / `org` keeps the org default, byte-identical.

That is the whole of "or fail gracefully": the skill lands everywhere, and decides at runtime whether the
workspace is board-capable. A miss is a clean stop with a pointer, not an error.

Preconditions, once you know you *are* board-capable:

- The working tree is **clean** and `main` is up to date (`git fetch && git status`). The workers branch
  from `origin/main`, and a stale base manufactures fresh evidence for bugs already fixed (pnext-item §2).
- Board writes are authorised: `gh auth refresh -s project,read:project`, `issues: write`.
- You are **not** going to push to `main`. Every change is a PR; the merge guard blocks direct pushes,
  and an agent may *merge* only a green, reviewed PR — the same rule workRoadmap, drive-board and
  pnext-item hold.

## 1. The landmine: N subagents collapse onto ONE worker id (ADR-0064 §4.4)

Read this before anything else, because a workBoard that skips it *looks* like it works and quietly
double-works items — and here, where every worker is in the **same tree**, that is worse than cross-repo.

`fsgg-coord whoami` resolves a worker id in this order (pnext-item §0, and it is the **engine's**, not
this doc's): `--worker <id>` → `$FSGG_WORKER` → the harness **session id** → *refuse*. On Claude Code
**every subagent of a session shares one `CLAUDE_CODE_SESSION_ID`** — so if the host spawns four
subagents and each runs a bare `whoami`, all four resolve to the **same** id. The claim lock is a
comment-order CAS over `fsgg:claim` markers, and it separates workers **only while their ids are
distinct** (ADR-0027, #419): an id two workers share is an id the lock cannot separate, and `release`,
`heartbeat`, `say` and `inbox` then act on one another's claims. The lock you are fanning out *behind* is
defeated at the root — and in one shared tree, two workers who both "hold" the same item edit the same
files.

**There is no worktree-name derivation to save you** — pnext-item §3 is explicit that the current engine
dropped it, so "each worker gets its own worktree" does **not** by itself give it its own id. The id must
be made distinct deliberately. This skill uses the mint-per-worker path:

- **Each worker mints its own id, inside its own isolated worktree.** The host spawns each subagent with
  `isolation: "worktree"` (a fresh tree, so the per-checkout mint persists to *that* tree, not the shared
  one) and the worker's very first act is the one mint idiom:

  ```sh
  eval "$(scripts/fsgg-coord whoami --mint)"   # never invent one, never copy one (#419, #551)
  ```

**The worker's brief (§3) MUST make the mint its first step, and MUST stop if `whoami` warns.** `whoami`
warns precisely when the id came from the shared session — that warning is the tripwire for this bug, and
a worker that works through it holds no lock. The host cannot verify the workers' ids for them (it cannot
see inside their trees), so it verifies the **outcome** instead (§4).

## 2. The loop (what the HOST does)

The host is the agent that invoked `/workBoard`. It **schedules; it never implements.** `<this-repo>` is
this one workspace's repo, resolved from the git remote — every `fsgg-coord` read below is scoped to it.
Repeat until §5 says the board is genuinely done:

1. **Reconcile first.** Run [check-board](../check-board/SKILL.md) (dry run, then `--apply` for the
   mechanical fixes if you are driving unattended and trust the pass). This clears stale claims, flips
   `BLOCKER-CLEARED` items back to `Ready`, and — critically for blockers that appear *after* some work —
   **re-verifies every `Blocked by` edge**, so work a previous wave unblocked becomes visible. Read its
   summary; its *asked-and-unanswered* list is work stuck on a **human**, not on you.
2. **Size the wave.** Ask the engine how many workers this repo can absorb *right now*:

   ```sh
   scripts/fsgg-coord batch --repo <this-repo> -n <cap> --json   # a maximal touch-set-disjoint set
   ```

   `batch` returns items that are `Ready`, unblocked, and **disjoint from each other and from everything
   in flight**. **Touch-set disjointness is load-bearing here** in a way it never is for drive-board: all
   workers share one working tree, so items must be file-disjoint or a wave stomps itself — which is
   exactly what `take` + [intra-repo-parallel-work](../intra-repo-parallel-work/SKILL.md) enforce. The
   **size** of that set is how many parallel `take`-workers to spawn this instant; cap it against the
   shared rate budget (§6). Do **not** pass the item numbers to the workers — `batch` is the host's
   *sizing* read and `take` is the worker's *claiming* read, and keeping them separate is what preserves
   `take`'s lost-race guarantee (drive-board §2).
3. **Spawn a fresh subagent per slot** (Agent tool, `isolation: "worktree"`), one per slot, each running
   the worker brief (§3). One subagent, one `take`-loop-of-one: it takes an item, works it to done-stamp,
   and returns. Concurrency is bounded — one repo, one shared account (§6).
4. **Collect each worker as it returns, and verify — do not trust (§4).** A worker reports the item it
   took, the PR it merged, and anything it filed or found. The subagent is now dead; its context is gone,
   which is deliberate.
5. **Re-plan.** The board has moved — new `Blocked by` edges a worker discovered, items now `Done`,
   follow-ups filed as new `Backlog` rows. Go back to step 1. The re-reconcile is **not** optional: it is
   how an edge that "only appears after some work" enters the schedule.

Never let the host "just finish one quickly" itself — the whole value is that every item goes through a
worker that runs the full pnext-item loop, and a host that starts editing has no worktree, no claim, and
no touch-set reservation, in the one tree every worker shares.

## 3. The worker: a pnext-item envelope, SDD-lifecycle escalation *by complexity* (ADR-0064 §4.3)

The invariant per-item harness is **[pnext-item](../pnext-item/SKILL.md)** (already materialized): mint a
distinct worker id, `take` (gate on exit code 0), read the item's comments, worktree from `origin/main`,
implement within the declared `Paths:`, open a PR, review, merge on green, `done --flip`. Inside that one
claim/merge/done-stamp envelope, **the depth of the implementation scales with the item's complexity** —
this is the decision ADR-0064 records:

- **Simple item** (Effort `S`/`M`, no `needs-sdd` signal): implement directly inside pnext-item — a
  focused change, PR, merge. No lifecycle overhead.
- **Complex item** (Effort `L`/`XL`, or a `needs-sdd` label / a `Blocked by`-a-charter signal): the worker
  runs the full **`fs-gg-sdd-*` lifecycle** (charter/specify → clarify → plan → tasks → implement →
  verify/validate → ship) for the implementation phase, **still inside** pnext-item's one claim/merge
  envelope. Both skill sets are present in a wired workspace, so this is a **documented branch, not new
  machinery** — one claim/merge/done-stamp discipline whether the item is a one-line fix or a heavyweight
  feature that deserves the lifecycle.

### The per-worker subagent brief

The host hands each subagent essentially this, with `<REPO>` (this workspace's repo) substituted:

> You are one worker in a fan-out **inside one shared working tree**. **Claim ONE schedulable item in
> `<REPO>`, take it to merged and done-stamped, report, and exit.** You run in your own isolated worktree;
> you may not push to `main` (every change is a PR; a green, reviewed PR may be merged).
>
> 1. **Become someone first.** Your very first command, before anything else:
>    `eval "$(scripts/fsgg-coord whoami --mint)"`. If `whoami` warns that your id came from the session,
>    **stop and report it** — you hold no lock, and working anyway puts two workers on one item in the one
>    tree we all share. Do not invent or copy an id.
> 2. **Run [pnext-item](../pnext-item/SKILL.md) for `<REPO>`**: `take` (gate on the
>    exit code — 0 is the *only* code that means you hold an item, #585), read the item's **comments**
>    before you start (a prior worker's "do not do this" is the highest-signal thing on the board),
>    `git fetch` then worktree from `origin/main` by name, implement **inside your declared `Paths:`** (in
>    a shared tree, a path you did not declare is one another worker may be editing). Pause before
>    opening the PR for steps 3–4, then resume pnext-item: open, review, merge on green, and
>    `done --flip` to earn the stamp.
> 3. **Scale the implementation to the item.** A simple item (Effort `S`/`M`) you implement directly. A
>    **complex** one (Effort `L`/`XL`, or a `needs-sdd` signal) you take through the full `fs-gg-sdd-*`
>    lifecycle — still inside this one claim → PR → merge → done-stamp envelope. Both skill sets are here.
> 4. **Checkpoint and finalize development feedback before opening the PR.** Use one stable cycle id
>    based on the item number and slug. Checkpoint only material observations at: onboarding/first
>    build; lifecycle authoring when used; first implementation/test/evidence loop; and
>    verify/ship/PR orchestration. Capture misleading guidance, avoidable retries, workarounds,
>    capability gaps, and unexpectedly effective composition when they occur. Finalize one schema-v2
>    report, search prior reports and open/closed issues for recurrence, run the bundled validator,
>    and include the checkpoint JSONL plus report in this item's PR.
> 5. **A blocker you discover is the point, not a failure.** If the item cannot proceed until other work
>    lands, file that work at its **root cause** (pnext-item §4), set `Blocked by` on this item to the
>    blocker, and `release --status Blocked` so the board tells the truth. Report the blocker you filed —
>    the host schedules around it next wave.
> 6. **Findings you make, you FIX in the same PR when that keeps it reviewable**, or file at the root cause
>    — pnext-item §4 is the authority. Do **not** recurse into a second item yourself: you are one worker
>    in a wave, and the host owns what comes next. Report what you filed; the host pops it.
> 7. **If `take` exits 5 (nothing schedulable) or 75 (rate budget exhausted)**, do not spin. Report the
>    exit code and stop — 5 means the repo is dry for now, 75 means the shared budget is gone and the host
>    must back the whole fleet off (§6).
> 8. **Report back**: the item number, the merged PR, the done-stamp, feedback report path, any
>    blocker/finding you filed (with
>    issue numbers), and the `take` exit code if you got no item. Then exit.
>
> If the item cannot land from here and it is not a clean blocker to file — it needs a human decision, or
> another repo owns the actual fix — do **not** fake a merge. Report it and stop; the host surfaces it.

## 4. Verify against ground truth — never the subagent's word

This is drive-board §5 and workRoadmap step 5, and it is the failure mode most worth catching: **a
subagent that reports "merged" on a PR that did not merge.** The host cannot see inside a dead subagent's
context, so it checks the world the subagent claims to have changed. For each returned worker:

```sh
scripts/fsgg-coord ready --repo <this-repo> --all --json   # the always-fresh TRUTH read of the column
```

- The item it claimed to finish is **`Done`** on the board and its issue is **closed** — or the worker
  reported a blocker, in which case it is **`Blocked`** with a `Blocked by` edge. If it says "merged" and
  the item is still `In progress` or `Ready`, **treat it as a failed item, not a passed one** — and check
  for a **stale claim with a green open PR**, the real success path of a worker whose harness died between
  green and merge: `adopt` it rather than binning it (#697, intra-repo §3).
- No **orphaned claim** is left holding the item — and holding, in one shared tree, a touch-set the next
  wave needs. If the worker died mid-flight, `reap` a lapsed lease — **unless** its `item/<n>-*` PR is
  open, which is proof the work is alive and outranks the timer (#581).
- The **rate budget** did not silently strand a write. If any worker returned 75, run
  `scripts/fsgg-coord flush --dry-run` before the next wave — a queued board write that nothing replays
  reads later as drift you will "find" and duplicate.

Do not take a merge you can check on the worker's say-so. Read `ready`, not the report.

## 5. Termination — via check-board, not via an empty `take`

The loop ends when the board is **genuinely** done, and the trap is that a **drifted board looks done
when it is not** — the entire reason [check-board](../check-board/SKILL.md) exists. So do not stop because
a `take` returned 5, or because one `batch` came back empty. Stop only when a **fresh** reconcile pass
shows all of:

- **No schedulable item** — `batch --repo <this-repo>` empty, and `next` explains *why* each remaining
  candidate is skipped (blocked, backlog, no touch-set) rather than startable.
- **No live claims in flight** — `who --repo <this-repo>` shows nothing held (or only stale ones you have
  reaped/adopted).
- **No `EPIC-ROLLUP-READY` epics and no cleared-but-still-`Blocked` items** left in check-board's
  findings. An item `Blocked` behind an issue that shipped is work advertised as unstartable; a
  rollup-ready epic is a `yes`/`no` a human owes. These are **not** an empty board — they are the board
  lying, and check-board is what distinguishes them.

A remaining candidate blocked on a **human** (`awaiting-human`, or check-board's *asked-and-unanswered*
list) is **not** yours to unblock and **not** a reason to keep spinning. Surface it and stop — driving it
yourself would make the machine answer the decision the item exists to escalate.

## 6. Concurrency and the one budget the whole fleet shares

One repo, one account, one shared rate budget: GraphQL (5,000 pt/hr, the board reads) and **REST (5,000
req/hr, where the claim lock lives** — ADR-0034 §3, #895). REST is the scarcer one under fan-out — metered
per request, un-batchable — and **five workers looping `take` drained GraphQL in ~15 minutes** once
(#418). So concurrency is a cap, and the cap is a rate-limit decision. Three rules keep the fan-out from
taking the board down (identical discipline to drive-board §3):

- **Cap in-flight workers conservatively** (`/workBoard --workers N`, default low). A board with 40
  schedulable items does not mean 40 workers; it means the cap's worth, then the next wave.
- **Let the workers share the 90s scan cache.** The host's own planning reads (`check-board`, `batch`)
  scan **fresh** — a reconciler on a stale board invents drift — but the *workers'* `take`/`next` reads
  must **not** add `--fresh`, or N workers cost N board reads instead of one.
- **Treat a worker's `EX_RATE` (exit 75) as a fleet-wide stop, not that worker's problem.** One worker
  hitting the REST cap means the *account* hit it, so every other worker is about to. **Stop spawning, let
  the in-flight ones drain, back off until the reset it names**, then `scripts/fsgg-coord flush --dry-run`
  — a board write made on an exhausted budget is *queued and nothing replays it* (pnext-item §1). Do not
  loop into the limit; REST carries the lock, and a lock you cannot verify is not one.

`scripts/fsgg-coord budget` shows GraphQL only — REST's remaining requests are not queryable (pnext-item
§5), which is *why* the cap is conservative and the 75-back-off is reactive, not predictive.

## 7. The completion report

When §5 says done, the host — **itself, not a subagent** — writes a report and lands it, the same close-out
[workRoadmap](../workRoadmap/SKILL.md) and [drive-board](../drive-board/SKILL.md) use. Write
`docs/reports/<YYYY-MM-DD>-workboard.md`, timestamped for today: what shipped this run (items, merged PRs,
done-stamps), the blockers workers discovered and where they were filed, the follow-ups they queued, every
rate-limit back-off, and the outstanding human-blocked items check-board named. Follow the house report
style already in `docs/reports/`. Aggregate the item feedback reports by recurring root cause and owner,
avoidable cost, positive patterns worth promoting, and development-surface coverage gaps; do not
concatenate them. **Land it as its own reviewed PR** — feature branch, open, review, merge
on green — the last thing to merge. Then report to the operator: board burned down, report PR number, and
the list of items still parked on a human.

## Failure handling

- **A worker that reports a merge that did not happen** is caught by §4 (verify against `ready`, not the
  report). Failed item, not passed — re-plan will re-offer it, or `adopt` its green orphan PR.
- **Two workers on one item** means §1 was skipped — the ids collapsed onto the session, and in one shared
  tree they are now editing the same files. Stop the fan-out, `who`/`reap` the tangle, and fix the brief so
  the mint is the first step. This is the bug the whole skill is arranged to prevent; if you see it, the
  arrangement failed, not the protocol.
- **A wave that stomps its own tree** means the disjointness §2 relies on was not respected — a worker
  edited outside its declared `Paths:`. That is why the worker brief pins the item *inside* its declaration
  and the host sizes waves off `batch` (disjoint by construction) rather than picking items by hand.
- **`EX_RATE` (75) from any worker** is a fleet stop (§6): drain, back off to the named reset, `flush`,
  resume. Never loop into the limit.
- **The workspace is not board-capable** is not a failure — it is the graceful-fail path (§4.1 above under
  *Where this runs*). Print the one line naming `workRoadmap`/`new-sdd-workspace --board` and stop; do not
  degrade into a plain edit loop.
- **Never bypass the merge guard.** No direct push to `main`, no local `git merge` into `main`. Every
  change is a PR; the only allowance is that a worker may *merge* a green, reviewed one.

## See also

- **ADR-0064** — the org record that canonizes this skill, fills the 2×2's fourth cell, and decides the
  graceful-fail-as-runtime-precondition and SDD-escalation-by-complexity questions. This file is the
  protocol; that record is the *why*.
- **ADR-0053** — the disposable-subagent loop this reuses (fresh subagent per unit, verify against ground
  truth, despawn, re-plan); the *why* of the shape lives there.
- [drive-board](../drive-board/SKILL.md) — the cross-repo sibling. Same loop; it fans across sibling repos
  where this fans inside one tree, so its workers cannot collide on files and this skill's must not.
  ADR-0057 (why drive-board is `operator` and never materialized — the reason this skill exists).
- [workRoadmap](../workRoadmap/SKILL.md) — the markdown-ledger sibling; the close-out (§7) is the same.
- [check-board](../check-board/SKILL.md) — the analysis pass the host runs every wave; the authority on
  board truth, stale blockers, and rollup-ready epics.
- [pnext-item](../pnext-item/SKILL.md) — the worker loop each subagent runs, the SDD-escalation branch
  (§3), and the "fix the cause, then take it" discipline for what a worker finds mid-item.
- [intra-repo-parallel-work](../intra-repo-parallel-work/SKILL.md) — the claim/worktree/touch-set protocol
  underneath, load-bearing here because the wave shares one tree. ADR-0021, ADR-0027.
- **ADR-0054 / ADR-0063** — the `FS.GG.Drivers` delivery fabric this skill rides into scaffolded
  workspaces, and the owner-sourced driver bytes that make it a row-plus-bytes addition. ADR-0056 — SDD as
  the default lifecycle the complex-item worker escalates into.
- ADR-0034 (the engine and its budgets), ADR-0001 (the board), ADR-0011 (the byte-identical skill roots
  this file is authored in).
