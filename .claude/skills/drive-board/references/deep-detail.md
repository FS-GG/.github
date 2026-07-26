
# drive-board (FS-GG)

One command's worth of intent: **"take the whole Coordination board and burn it down — across every
repo, as parallel as it is safe to be — and don't make me drive."** This skill is the **cross-repo
sibling of [work-roadmap](../../work-roadmap/SKILL.md)**: same parent-loop-of-disposable-subagents shape,
but the ledger is the **board**, not a markdown file, and the workers fan out **across repos** instead
of running one milestone at a time in one repo.

It owns exactly one thing neither of its parts does: **the cross-repo scheduling loop** — read the
board, decide how many workers each repo can absorb *right now*, spawn that many fresh subagents,
verify what they claim, despawn them, re-plan against the board they just changed. Everything else it
delegates and does not re-teach:

- **Analysis** is [check-board](../../check-board/SKILL.md)'s. The host does not hand-roll board truth —
  it runs the reconcile pass and reads its findings. That is also where **blockers that emerge mid-work**
  get caught: nothing re-checks a `Blocked by` when its blocker clears, so each planning pass re-runs it.
- **The worker** is [pnext-item](../../pnext-item/SKILL.md). Each subagent runs it start to done-stamp.
  The host never implements an item itself — the discipline is that every item goes through a worker
  that runs the full loop, claims with the lock, isolates in a worktree, opens a PR, and merges on green.
- **The claim/worktree/touch-set protocol** is
  [intra-repo-parallel-work](../../intra-repo-parallel-work/SKILL.md)'s. The host relies on it; it does
  not restate it.

## Where this runs, and the one precondition that is not optional

An **operator checkout where every rostered repo is present as a sibling** — `../FS.GG.Rendering`,
`../FS.GG.Game`, and so on, next to this `.github` checkout. A worker the host spawns for repo `r`
`cd`s into that repo's checkout and worktrees from it (pnext-item §2); if the checkout is not there,
there is nothing to spawn *into*. Check it once, before the loop, and **fail loudly** if a rostered
repo is missing — a silent skip is invisible work, which is the failure this whole fabric is built
against (#442). `scripts/repos.sh list --all` enumerates the roster; each row must resolve to a
checkout on disk.

Preconditions, checked once:

- Every repo in `scripts/repos.sh list --all` has a sibling checkout, each on a clean tree.
- `scripts/fsgg-coord whoami` resolves — and the **host's** own id does not matter, but see §1: the
  *workers'* ids are where this breaks.
- Board writes are authorised: `gh auth refresh -s project,read:project`, `issues: write`.
- You are NOT going to push to `main` in any repo. Every change is a PR; the merge guard blocks direct
  pushes, and agent PR *merges* are allowed only on a green, reviewed PR — the same rule work-roadmap
  and pnext-item hold.

## 1. The landmine: N subagents collapse onto ONE worker id

Read this before anything else, because a `drive-board` that skips it *looks* like it works and
quietly double-works items.

`fsgg-coord whoami` resolves a worker id in this order (pnext-item §0, and it is the **engine's**, not
this doc's): `--worker <id>` → `$FSGG_WORKER` → the harness **session id** → *refuse*. On Claude Code
**every subagent of a session shares one `CLAUDE_CODE_SESSION_ID`** — so if the host spawns five
subagents and each runs a bare `whoami`, all five resolve to the **same** id. The claim lock is a
comment-order CAS over `fsgg:claim` markers, and it separates workers **only while their ids are
distinct** (ADR-0027, #419): an id two workers share is an id the lock cannot separate, and `release`,
`heartbeat`, `say` and `inbox` then act on one another's claims. The lock you are fanning out *behind*
is defeated at the root.

**There is no worktree-name derivation to save you** — pnext-item §3 is explicit that the current
engine dropped it, so "each worker gets its own worktree" does **not** by itself give it its own id.
The id must be made distinct deliberately. Two ways, and this skill uses the first:

- **Each worker mints its own id, inside its own isolated worktree.** The host spawns each subagent
  with the host's isolated-worktree option when supported (a fresh tree, so the per-checkout mint persists to *that* tree, not
  the shared one) and the worker's very first act is the one mint idiom:

  ```sh
  eval "$(scripts/fsgg-coord whoami --mint)"   # never invent one, never copy one (#419, #551)
  ```

- **The host assigns `FSGG_WORKER` per dispatch.** Also valid, but the host is then a source of ids,
  and an id is *minted, never chosen* (#419) — so if you go this way, mint each one with the tool and
  hand it to exactly one worker. Prefer the first: it keeps the "no literal id in any document" rule
  (#551) intact and puts the mint where the lock is taken.

**The worker's brief (§4) MUST make the mint its first step, and MUST stop if `whoami` warns.**
`whoami` warns precisely when the id came from the shared session — that warning is the tripwire for
this bug, and a worker that works through it holds no lock. The host cannot verify the workers' ids
for them (it cannot see inside their trees), so it verifies the *outcome* instead: §5.

## 2. The scheduling model: repo-directed `take`, and why not item-directed

The host decides **which repos have schedulable work** and **how many workers each can absorb**, then
each worker **self-claims via `take`**. The host does **not** pick specific items and hand them out.

That is deliberate. `take` is the only path that checks a candidate's touch-set against every live
claim and **re-schedules on a lost race** (intra-repo §2, pnext-item §1). `claim <item>` — the
specific-item path — does **not** check disjointness (pnext-item §1: "`claim` does not check your
touch-set against live claims — only `take` does"). So an item-directed host would have to re-implement
the scheduler's collision check itself, and get it right across a moving board. Let `take` do it.

Cross-repo, this is even cleaner than intra-repo: two workers in **different** repos are in different
checkouts, so they cannot collide on files at all. **Touch-set disjointness is a within-repo concern**;
across repos it is free. Which means the host's real scheduling problem is not "which items" — it is
**concurrency against the one shared resource**, the account's rate budget (§3).

**How many workers a repo can absorb right now** is a question the engine already answers. `batch`
returns a maximal set of items that are disjoint from each other and from everything in flight:

```sh
scripts/fsgg-coord batch --repo <r> -n <cap> --json   # up to <cap> items that can run at once, here, now
```

The **size of that set** is how many parallel `take`-workers repo `r` can usefully hold this instant.
Spawn `min(len(batch), remaining budget slot)` workers for `r`; each runs `take` and self-selects. Do
not pass the item numbers to the workers — `batch` is the host's *sizing* read, and `take` is the
worker's *claiming* read, and keeping them separate is what preserves the lost-race guarantee.

## 3. Concurrency and the one budget the whole fleet shares

Every worker authenticates as the **same account**, so they share its two budgets: GraphQL (5,000
pt/hr, the board reads) and **REST (5,000 req/hr, where the claim lock lives** — ADR-0034 §3, #895).
REST is the scarcer one under fan-out: it is metered per request and cannot be batched, and **five
workers looping `take` drained GraphQL in ~15 minutes** once (#418) — REST has no such lever at all.
So concurrency is not "as many as there are items." It is a cap, and the cap is a rate-limit decision.

Three rules the host holds so a fan-out scales instead of taking the board down:

- **Cap total in-flight workers across all repos** — start conservative (a handful), not one-per-item.
  The board having 200 schedulable items does not mean 200 workers; it means the cap's worth, then the
  next wave. Make the cap a knob (`$drive-board --workers N`), default it low.
- **Let workers share the 90s scan cache.** The host's own planning reads (`check-board`, `batch`)
  scan **fresh** — a reconciler on a stale board invents drift (check-board §1) — but the *workers'*
  `take`/`next` reads must **not** add `--fresh`, or N workers cost N board reads instead of one.
- **Treat a worker's `EX_RATE` (exit 75) as a fleet-wide stop signal, not that worker's problem.**
  One worker hitting the REST cap means the *account* hit it, so every other worker is about to too.
  When a returning worker reports 75, **stop spawning, let the in-flight ones drain, and back off until
  the reset it names** — then `scripts/fsgg-coord flush --dry-run`, because a board write made on an
  exhausted budget is *queued and nothing replays it* (pnext-item §1). Do not loop into the limit.

`scripts/fsgg-coord budget` shows GraphQL only — REST's remaining requests are not queryable (pnext-item
§5), which is *why* the cap is conservative and the 75-back-off is reactive rather than predictive.

## 4. The loop (what the HOST does)

The host is the agent that invoked `$drive-board`. It schedules; it never implements. Repeat until §6
says the board is genuinely done:

1. **Reconcile first.** Run [check-board](../../check-board/SKILL.md) (dry run, then `--apply` for the
   mechanical fixes if you are driving unattended and trust the pass). This clears stale claims, flips
   `BLOCKER-CLEARED` items back to `Ready`, and — critically for your "blocking appears after work"
   case — **re-verifies every `Blocked by` edge**, so work a previous wave unblocked becomes visible.
   Read its four-part summary; its *asked-and-unanswered* list is work that is stuck on a human, not
   on you.
2. **Size the wave.** For each repo with `Ready` work, `batch --repo <r> -n <cap> --json` to learn how
   many workers it can absorb. Sum against the concurrency cap (§3) to get a per-repo worker count for
   this wave.
3. **Spawn a fresh subagent per slot** (using the host's available worker/subagent mechanism; request an isolated worktree when supported), handing each the
   per-worker brief below with `<REPO>` substituted. One subagent, one `take`-loop-of-one — it takes an
   item, works it to done-stamp, and returns. Spawn the wave **concurrently** across repos (separate
   checkouts cannot collide) up to the cap.
4. **Collect each worker as it returns, and verify — do not trust (§5).** A worker reports the item it
   took, the PR it merged, and anything it filed or found. The subagent is now dead; its context is
   gone, which is deliberate.
5. **Re-plan.** New blockers a worker discovered are now `Blocked by` edges on the board; items it
   finished are `Done`; follow-ups it filed are new `Backlog` rows. The board has moved, so go back to
   step 1. The re-reconcile is not optional — it is how an edge that "only appears after some work"
   enters the schedule.

Never let the host "just finish one quickly" itself — the whole value is that every item goes through a
worker that runs the full pnext-item loop, and a host that starts editing has no worktree, no claim,
and no touch-set reservation.

### The per-worker subagent brief

The host hands each subagent essentially this, with `<REPO>` (a registry short-id) substituted:

> You are one worker in a fan-out. **Claim ONE schedulable item in `<REPO>`, take it to merged and
> done-stamped, report, and exit.** You run in your own isolated worktree; you may not push to `main`
> (every change is a PR; a green, reviewed PR may be merged).
>
> 1. **Become someone first.** Your very first command, before anything else:
>    `eval "$(scripts/fsgg-coord whoami --mint)"`. If `whoami` warns that your id came from the session,
>    **stop and report it** — you hold no lock, and working anyway puts two workers on one item. Do not
>    invent or copy an id.
> 2. **Run [pnext-item](../../pnext-item/SKILL.md) for `<REPO>`**, exactly as written: `take --json` (gate on
>    the exit code AND require its fresh `.converged == true` receipt), report that receipt to the host,
>    and do not announce or implement before marker + `Status=In progress` are both observed; read the item's **comments**
>    before you start (a prior worker's "do not do this" is the highest-signal thing on the board),
>    `git fetch` then worktree from `origin/main` by name (#622), implement inside your declared
>    `Paths:`, open a PR, review it, merge on green, and `done --flip` to earn the stamp.
>    Before merge, name any post-merge release/publication/dispatch/deployment obligations. If any
>    remain after merge, immediately reopen the auto-closed issue, set and freshly verify `In review`,
>    keep the claim live, finish and verify those obligations, then close and earn `FSGG-DONE`.
> 3. **A blocker you discover is the point, not a failure.** If the item cannot proceed until other
>    work lands, file that work at its **root cause** (pnext-item §4), set `Blocked by` on this item to
>    the blocker, and `release --status Blocked` so the board tells the truth. Report the blocker you
>    filed — the host schedules around it next wave.
> 4. **Findings you make, you FIX in the same PR when that keeps it reviewable**, or file at the root
>    cause and (for your own case-2 follow-ups) queue them — pnext-item §4 is the authority. Do **not**
>    recurse into a second item yourself: you are one worker in a wave, and the host owns what comes
>    next. Report what you filed; the host pops it.
> 5. **If `take` exits 5 (nothing schedulable) or 75 (rate budget exhausted)**, do not spin. Report the
>    exit code and stop — 5 means this repo is dry for now, 75 means the shared budget is gone and the
>    host must back the whole fleet off (§3).
> 6. **Report back**: the item number, the merged PR, the exact `FSGG-DONE` line, the post-merge
>    obligation list and verification evidence (or explicitly `none`), any blocker/finding you filed
>    (with issue numbers), and the `take` exit code if you got no item. Then exit.
>
> If the item cannot land from here and it is not a clean blocker to file — it needs a human decision,
> or another repo owns the actual fix — do **not** fake a merge. Report it and stop; the host surfaces
> it.

## 5. Verify against ground truth — never the subagent's word

This is work-roadmap step 5, and it is the failure mode most worth catching: **a subagent that reports
"merged" on a PR that did not merge.** The host cannot see inside a dead subagent's context, so it
checks the world the subagent claims to have changed. For each returned worker:

```sh
scripts/fsgg-coord ready --repo <r> --all --json   # the always-fresh TRUTH read of the column
```

Run the same fresh read **immediately after each worker reports a claim**, before describing that worker
as active: its row must be `In progress`, and the typed receipt must have `markerObserved=true` and
`converged=true`. Repeat this for every reported transition (`Blocked`, `In review`, `Done`): a worker
message is intent; the fresh board row is the ledger. If they disagree, report and reconcile the lag
instead of narrating the intended state as current.

- The item it claimed to finish is **`Done`** on the board and its issue is **closed** (or the worker
  reported a blocker, in which case it is **`Blocked`** with a `Blocked by` edge). If it says "merged"
  and the item is still `In progress` or `Ready`, **treat it as a failed item, not a passed one** —
  and check for a **stale claim with a green open PR**, which is the real success path of a worker
  whose harness died between green and merge: `adopt` it rather than binning it (#697, intra-repo §3).
- **Projects auto-`Done` is not completion evidence.** Require the worker's exact `FSGG-DONE` line,
  then independently run `scripts/fsgg-coord done <issue>` and require exit 0 plus `FSGG-DONE` before
  counting the item terminal. A `Done` row with a live claim, or a worker-reported outstanding
  release/publication/dispatch/deployment obligation, is non-terminal: the issue must be open and the
  fresh row `In review` while that work continues. If merge auto-closed/projected it, have the worker
  restore that active state immediately and verify it; never narrate the auto-projection as earned.
- No **orphaned claim** is left holding the item. If the worker died mid-flight, `reap` a lapsed lease
  — unless its `item/<n>-*` PR is open, which is proof the work is alive and outranks the timer (#581).
- The **rate budget** did not silently strand a write. If any worker returned 75, run
  `scripts/fsgg-coord flush --dry-run` before the next wave — a queued board write that nothing
  replays reads later as drift you will "find" and duplicate.

Do not take a merge you can check on the worker's say-so. Pull and look.

## 6. Termination — via check-board, not via an empty `take`

The loop ends when the board is **genuinely** done, and the trap is that a **drifted board looks done
when it is not** — that is the entire reason [check-board](../../check-board/SKILL.md) exists. So do not
stop because a `take` returned 5, or because one `batch` came back empty. Stop only when a **fresh**
reconcile pass shows all three of:

- **No schedulable item in any repo** — `batch --repo <r>` empty for every `r`, and `next` explains
  *why* each remaining candidate is skipped (blocked, backlog, no touch-set) rather than startable.
- **No live claims in flight** — `who --all-repos` shows nothing held (or only stale ones you
  have reaped/adopted).
- **No `EPIC-ROLLUP-READY` epics and no cleared-but-still-`Blocked` items** left in check-board's
  findings. An item `Blocked` behind an issue that shipped is work advertised as unstartable; a
  rollup-ready epic is a `yes`/`no` a human owes. These are *not* an empty board — they are the board
  lying, and check-board §3/§4 is what distinguishes them.

A remaining candidate that is blocked on a **human** (`awaiting-human`, or check-board's *asked-and-
unanswered* list) is **not** yours to unblock and **not** a reason to keep spinning. Surface it and
stop — driving it yourself would make the machine answer the decision the item exists to escalate.

## 7. The completion report

When §6 says done, the host — **itself, not a subagent** — writes a report and lands it, the same way
work-roadmap does. `docs/reports/<YYYY-MM-DD>-drive-board.md`: per repo, what shipped this run (items,
merged PRs, done-stamps), the blockers workers discovered and where they were filed, the follow-ups
they queued, every rate-limit back-off, and the outstanding human-blocked items check-board named.
Close with a roll-up and land it as its own reviewed PR — the last thing to merge. Then report to the
operator: board burned down, report PR number, and the list of items still parked on a human.

## Failure handling

- **A worker that reports a merge that did not happen** is caught by §5 (verify against `ready`, not
  the report). Failed item, not passed — re-plan will re-offer it, or `adopt` its green orphan PR.
- **Two workers on one item** means §1 was skipped — the ids collapsed onto the session. Stop the fan-
  out, `who`/`reap` the tangle, and fix the brief so the mint is the first step. This is the bug the
  whole skill is arranged to prevent; if you see it, the arrangement failed, not the protocol.
- **`EX_RATE` (75) from any worker** is a fleet stop (§3): drain, back off to the named reset, `flush`,
  resume. Never loop into the limit — REST carries the lock, and a lock you cannot verify is not one.
- **A repo checkout missing at start** halts the precondition check (§0). A silent skip would leave
  that repo's board permanently unserved while the run reports success — the #442 shape.
- **Never bypass the merge guard.** No direct push to `main` in any repo, no local merge into `main`.
  Every change is a PR; the only allowance is that a worker may *merge* a green, reviewed one.

## See also

- **ADR-0053** — the loop work-roadmap canonizes; this skill is its cross-repo generalization, and the
  *why* of "fresh disposable subagent per unit, verify against ground truth, despawn, re-plan" lives
  there.
- [check-board](../../check-board/SKILL.md) — the analysis pass the host runs every wave; the authority on
  board truth, stale blockers, and rollup-ready epics.
- [pnext-item](../../pnext-item/SKILL.md) — the worker loop each subagent runs, and the "fix the cause,
  then take it" discipline for what a worker finds mid-item.
- [intra-repo-parallel-work](../../intra-repo-parallel-work/SKILL.md) — the claim/worktree/touch-set
  protocol underneath, and the identity rule §1 turns on. ADR-0021, ADR-0027.
- ADR-0001 (the board), ADR-0034/ADR-0040 (the engine and its budgets).
