
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
   Consume all four result parts: mechanical changes, queued/failed writes, judgement findings, and
   the fresh post-apply result. Its *asked-and-unanswered* list is work that is stuck on a human, not
   on you. An unreadable or unflushed result stops planning; it never becomes an empty board.
2. **Triage Backlog.** Follow [backlog-triage](backlog-triage.md) against a fresh Backlog inventory.
   Classify every relevant row as promote to `Ready`, retain with an evidenced reason, set `Blocked`
   behind a verified issue, or awaiting human judgement. Do not infer missing paths, blocker meaning,
   epic discharge, or priority. Apply supported status writes and re-read them before sizing.
3. **Size the wave.** For each repo with `Ready` work, `batch --repo <r> -n <cap> --json` to learn how
   many workers it can absorb. Sum against the concurrency cap (§3) to get a per-repo worker count for
   this wave.
4. **Spawn a fresh subagent per slot** (using the host's available worker/subagent mechanism; request an isolated worktree when supported), handing each the
   per-worker brief below with `<REPO>` substituted. One subagent, one `take`-loop-of-one — it takes an
   item, works it to done-stamp, and returns. Spawn the wave **concurrently** across repos (separate
   checkouts cannot collide) up to the cap.
5. **Collect each worker as it returns, and verify — do not trust (§5).** A worker reports the item it
   took, the PR it merged, and anything it filed or found. The subagent is now dead; its context is
   gone, which is deliberate.
6. **Bring the shared engine current** — after the merges of this wave are verified, before the next
   wave is spawned. Every worker execs the engine built in the **shared** `.github` checkout, and the
   merges you just verified are what leave it behind. Run the check below; rebuild only if it answers
   non-zero. Skip it and `.github#1549` refuses the next wave's board writes — including your own.
   See [engine currency](#engine-currency).
7. **Re-plan.** New blockers a worker discovered are now `Blocked by` edges on the board; items it
   finished are `Done`; follow-ups it filed are new `Backlog` rows. Discard the prior inventory and go
   back to step 1. Reconcile and Backlog triage are both mandatory before another spawn: that is how an
   edge or follow-up that only appears after work enters the very next wave.

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
> 6. **If `pnext-item` §1's engine check says the shared checkout is behind, do not `take` — report
>    "the shared engine is N commits behind" and stop.** The guard will refuse the write anyway, so
>    taking first only spends the lease learning that. Report it whether or not you *could* reach that
>    checkout: the repair mutates a tree every other live worker is reading, and serialising it is the
>    host's job, not a favour a worker does on its way past. You are owed the repair, not blamed for it.
> 7. **Report back**: the item number, the merged PR, the exact `FSGG-DONE` line, the post-merge
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

### Engine currency

**You are the actor that creates this drift, and you are the one that owns the repair**
(`.github#1663`). In `.github` the coordination engine is a *source build*: `scripts/fsgg-coord`
resolves `src/FS.GG.Coord.Cli/bin/Release/net10.0/` under the **shared** checkout for every caller
standing in a worktree, so the binary the whole fleet execs — workers and host alike — comes from one
tree that nothing in this loop used to move. Merging a wave's PRs is exactly what leaves it behind
`origin/main`, which is why the step belongs *after* §5's verification and *before* the next spawn.

Since `.github#1549` that drift is not silent, it is a **refusal**: `stale_guard` compares the shared
checkout's `HEAD` to the default-branch ref it resolves (`refs/remotes/origin/HEAD`, falling back to
`origin/main`, then `origin/master` — `origin/main` in this repo, and the recipe below names it
directly) under the engine's own source trees, and refuses every board write while it is behind. Measured on this host, twice in one run (2026-07-27), both times in this shape — a
worker's PR merged into `.github` main, the shared checkout fell behind, board writes refused. **The
first occurrence cost a `set-field` that silently did not land**, caught only because a later `lint`
contradicted it. The tax scales with how fast the fleet lands work, which is what a good run of this
skill maximises.

Run the check once per wave. Past the `git fetch` — which §5's verification already makes you run, and
which does double duty here, because a linked worktree shares the common dir's refs — it is four local
`git` calls, ~5 ms, and no network of its own:

```sh
git fetch origin
SHARED="$(git worktree list --porcelain | head -1 | cut -d' ' -f2-)"       # the path, for the repair
SHARED_HEAD="$(git worktree list --porcelain | sed -n '2s/^HEAD //p')"     # the commit it is sitting on
[ -n "$SHARED_HEAD" ] || { echo "cannot read the shared checkout's HEAD — that is not freshness"; exit 1; }
git rev-list --count "$SHARED_HEAD..origin/main" -- \
  src/FS.GG.Coord.Cli src/FS.GG.Coord.Core src/FS.GG.Coord.GitHub
```

Non-zero, and **only** then — this half is a Release build, so it is gated on the answer, never run per
wave on principle:

```sh
git -C "$SHARED" merge --ff-only origin/main
dotnet build "$SHARED/src/FS.GG.Coord.Cli" -c Release
```

This is the same recipe `pnext-item` §1 gives the workers, deliberately — the check and the refusal must
have one subject, and so must the check and the repair. Three clauses are worth keeping intact:

- **`merge --ff-only origin/main`, NOT `pull --ff-only`.** The shared checkout is routinely on a
  **detached HEAD** — measured mid-run — where `git pull --ff-only` exits 1 with *"You are not currently
  on a branch"* and moves nothing, leaving the refusal exactly where it was while looking like a repair.
  `merge --ff-only` fast-forwards a detached HEAD and a branch alike, and still refuses loudly if the
  tree has diverged, which is the case you want escalated rather than merged. `stale_guard`'s own
  printed remedy still says `pull --ff-only`; that is `.github#1664`, and it is why this does not.
- **The rebuild names `$SHARED` explicitly.** A bare `dotnet build src/FS.GG.Coord.Cli -c Release`
  rebuilds whatever tree you are standing in — never the stale one — and changes nothing.
- **Scoped to the engine's three source trees, not `main` as a whole.** A docs commit, a workflow edit
  or a registry row must not send the fleet into a Release rebuild; "halting the fleet whenever `main`
  moves" is the outcome #1549 explicitly refused.

**This is a protocol step, not a new gate.** `#1549`'s guard is already the enforcement, and it fails
closed; what was missing was an actor that owned the repair before the refusal landed. `pnext-item` §1
makes each worker *check* — a floor, because whether anyone else did it is not observable from inside a
worktree — and escalates the repair here (`.github#1594`). That escalation's landing place is named in
[host-loop](host-loop.md): a worker reporting "I am refused the shared checkout, and the engine is N
behind" has done the right thing and is owed this repair, not a re-dispatch.

Two notes on reach. In a checkout with **no source build at all** the count is `0` and the whole block is
a no-op, correctly: a receiver resolves a packaged engine at tiers 3/4, never reaches `stale_guard`, and
has nothing beside it to be stale against — so this step costs a workspace driver nothing and needs no
repo special-case. Do not read that backwards *inside* `.github`, where the guard does run: a pathspec
that matches nothing also counts `0`, so if the engine's projects were ever renamed or moved, this check
would answer "fresh" forever while `stale_guard` — which probes that the trees exist and returns *no
verdict* when they do not — refuses every write. A host repairing nothing while blocked is the same
fail-open one level up; if the count is `0` and the refusal persists, suspect the pathspec, not the
board. And an **empty
`SHARED_HEAD` refuses** rather than reporting fresh: `--porcelain`'s second line is `bare` for a bare
main working tree, and `rev-list --count "..origin/main"` is valid git meaning `HEAD..origin/main` —
i.e. it would measure *your* tree, which is current by construction, and answer `0`. Cannot look ≠
nothing to find (`.github#266`).

## 6. Termination — via check-board, not via an empty `take`

The loop ends when the board is **genuinely** done, and the trap is that a **drifted board looks done
when it is not** — that is the entire reason [check-board](../../check-board/SKILL.md) exists. So do not
stop because a `take` returned 5, or because one `batch` came back empty. Stop only when a **fresh**
reconcile and [backlog-triage](backlog-triage.md) pass show all four of:

- **No startable `defect` in any repo** — not an empty board. See "why the test is `defect`" below.
- **No actionable or untriaged Backlog remains.** Every Backlog row was classified from the current
  wave: actionable rows were promoted before `batch`; retained rows have a concrete evidenced reason;
  human judgement rows are surfaced in the completion report.
- **No live claims in flight** — `who --all-repos` shows nothing held (or only stale ones you
  have reaped/adopted).
- **No `EPIC-ROLLUP-READY` epics and no cleared-but-still-`Blocked` items** left in check-board's
  findings. An item `Blocked` behind an issue that shipped is work advertised as unstartable; a
  rollup-ready epic is a `yes`/`no` a human owes. These are *not* an empty board — they are the board
  lying, and check-board §3/§4 is what distinguishes them.

A remaining candidate that is blocked on a **human** (`awaiting-human`, or check-board's *asked-and-
unanswered* list) is **not** yours to unblock and **not** a reason to keep spinning. Surface it and
stop — driving it yourself would make the machine answer the decision the item exists to escalate.

### Why the test is `defect`, and not "no schedulable item" (.github#1588, ADR-0066)

The old first condition was **no schedulable item in any repo**, and it is why this loop could not
terminate on its own terms. Measured on 2026-07-27: the board went from 5 non-Done rows to **34** during a
single burn-down in which 35+ items merged. That growth was *healthy* — every new row was a real, evidenced
finding produced by fixing the previous one (#1538 spawned #1568; #721 spawned #726 and #727; #1525 spawned
#1562). But under a rule that says "stop when nothing is schedulable", a run in which fixing one thing files
two **never stops**, and that run ended only because a human intervened.

The board carried no way to tell these apart, because it had words for *when* (`Status`), *where* (`Repo
Scope`), and *how big* (`Effort`), and none for **how bad**:

- **defect** — `coordination-coherence` RED on `main` (#722); a reusable gate that dies at load for every
  caller (#1510); a summary reporting a byte-identity it never computed (#1506).
- **hardening** — `sparse.py` accepting `"1"` where `core.getBooleanInput` wants `true` (#1554); a test
  file's running commentary skipping two version entries (#724).
- **decision** — three digest implementations disagreeing on CRLF, and somebody must pick (#1547).

A human sorted those by reading the titles in seconds. The fact was knowable and simply nowhere in the
data, so `batch`, `ready` and this stopping rule saw one undifferentiated row for all three.

**The unclassed row is the part to get right, and it is the reason this contract is not simply "stop when
no row is classed `defect`".** A driver keying on `defect` over rows that carry no class reads every one of
them as *not-a-defect* and terminates immediately — stopping early and leaving live defects, which is the
exact failure this change exists to prevent, arriving through the change itself. So an unclassed row counts
as a **possible defect**. That is #266's rule on a new axis — a subject you could not evaluate is never a
subject that passed — and it makes this contract safe at any level of population rather than only after
somebody has classed every row.

**It does not mean the run may never stop.** Termination and certainty are two different claims, and
conflating them would replace one unterminating rule with another. You may stop with unclassed rows
outstanding; what you may not do is *report the board defect-free*. Name them by number as unresolved and
say what the run did not establish.

**How to read a class.** `reconcile --apply` first, then `ready --json`'s `class` field: that column is the
projection, so it is current exactly as of the last reconcile, and reading it before reconciling
under-reports rows whose body is classed and whose column is not. `lint`'s `CLASS-UNSET` names every row the
column cannot speak for. Together the two are complete; neither is alone.

The authority behind that column is the item's `Class:` body line (ADR-0066, preserving ADR-0045's decision
to carry this kind of fact in the body rather than a Projects v2 field). Do not hand-edit the column — the
next `reconcile` overwrites it from the body, so an edit there is lost and, worse, believed until it is.

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
