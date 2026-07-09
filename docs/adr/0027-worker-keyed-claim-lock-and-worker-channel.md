# ADR-0027: The parallel-work lock is keyed on the worker, not the account — plus identity, visibility, scheduling, and a channel

- **Status:** Accepted
- **Date:** 2026-07-09
- **Affects:** all FS-GG repos (amends [ADR-0021](0021-parallel-intra-repo-work-claim-worktree-touchset.md) §1; `.github` owns the protocol)
- **Fixes:** [FS-GG/.github#255](https://github.com/FS-GG/.github/issues/255)

## Context

[ADR-0021](0021-parallel-intra-repo-work-claim-worktree-touchset.md) added an intra-repo
parallel-work protocol on top of the Coordination board: a **claim** (the issue assignee, as an
advisory lock), **isolation** (one git worktree per item), and a **touch-set** (a declared `Paths:`
line, compared pairwise by `fsgg-coord overlap`).

Running it in anger exposed that its central primitive does not hold.

### 1. The assignee cannot be the lock, because the assignee is not the worker

`fsgg-coord claim` refused an item only when it was assigned to *someone else*:

```sh
others="$(printf '%s\n' "$held" | grep -v -x "$me")"   # everyone holding it EXCEPT me
[ -n "$others" ] && die "already claimed"
```

Every agent in a fan-out authenticates as the **same GitHub account**. `@me` therefore resolves to
one principal for all of them, an item a sibling already holds reads back as "assigned to me",
`others` is empty, and the claim **succeeds**. Two workers proceed on one item. The post-assign
re-read ADR-0021 added to close the TOCTOU window compares the same way, so the honest back-off it
promised never fires.

This is not a bug in the check; it is a category error. The assignee names an **account**. The thing
that must be mutually excluded is a **worker**. Cross-repo, "a repo has one owner" made these
coincide, and ADR-0021 carried the assumption inward without noticing it had stopped being true.

Worse, ADR-0021's back-off is a **livelock** even when logins *do* differ: two racers that both
observe the other back off together and neither proceeds.

### 2. Nothing records which worker did what

An incident on this board ([#255](https://github.com/FS-GG/.github/issues/255), 2026-07-09) found an
item `In progress`, assigned, with files edited by a worker that had **never called `claim` at all**.
Attribution was attempted from file mtimes and the process list, and could not be completed: agents
write by absolute path, so no process's cwd proved anything. Nothing in the board, the worktree, or
the commit carried a worker id, so "who is editing this?" had no answer anywhere in the system.

### 3. The protocol detects overlap but gives workers no way to resolve it

`overlap` compares **two** items and says DISJOINT or OVERLAP. Three gaps follow:

- Scheduling N workers means running `overlap` over every candidate **pair** by hand, and it ignores
  what is already **in flight** — the only question a fan-out actually asks.
- ADR-0021 says a worker that widens its touch-set "re-declares and re-checks overlap before
  continuing", but supplies no mechanism, so nobody does.
- When two touch-sets do collide, the only remedy on offer is to **sequence**. Workers cannot talk,
  so "you take the interface, I'll take the impl" is unreachable — and the worker whose item was just
  invaded is never told.

### 4. A dead worker holds its item forever

Nothing expires a claim. A crashed agent's item stays `In progress` until a human notices.

## Decision

Key the lock, identity, attribution, and communication on a **worker id**, and make the scheduler
read the lock rather than the board.

1. **Every worker has a stable id.** Resolved in order: `--worker`, `$FSGG_WORKER`, the **git
   worktree's own name** (ADR-0021 already gives each item its own worktree, so it *is* an identity),
   the **agent harness's session id** hashed into a memorable name, else a generated name persisted
   per checkout. The last two rules can each hand one id to several workers, and **warn on every
   claim**, naming which reason applies — a shared id is the collapse this ADR exists to prevent.
   `fsgg-coord whoami` prints the id, the rule that produced it, and the harness/session behind it.

   A harness session id is deliberately **rule 4, not rule 1**: there is no cross-harness convention,
   and the same name has different cardinality on different harnesses. Claude Code shares one
   `CLAUDE_CODE_SESSION_ID` across every subagent it spawns
   ([#7881](https://github.com/anthropics/claude-code/issues/7881)), so keying the lock on it would
   rebuild this very bug one level down; OpenCode's subagents are child sessions and *are* per-worker;
   Codex exports no id at all. `fsgg-coord` therefore encodes the per-harness cardinality and warns
   when it is not per-worker. Evidence:
   [`docs/coordination/agent-session-identifiers.md`](../coordination/agent-session-identifiers.md).

2. **The lock is a claim marker comment, resolved by comment-order compare-and-swap.** To claim:
   read the live `fsgg:claim` markers, refuse if another worker holds one, post our own, **re-read**,
   and take the **lowest live marker id** as the winner; a loser deletes its own marker and says so.
   GitHub issues comment ids from a single server-side sequence, so "lowest id" is a **total order
   every racer observes identically**: a simultaneous claim has exactly one winner, and the losers
   know they lost. This is a real mutual exclusion, where the assignee protocol was either a no-op
   (same account) or a livelock (different accounts).

   The **assignee is still set** — it is what a human sees on the board — but it is no longer load-
   bearing. The board `Status` flip stays best-effort: the marker, not the column, is the lock.

   Two corollaries of "lowest live id wins", both of which the implementation must honour or the CAS
   fails open:

   - **An unobservable marker is a lost race.** If the re-read shows *no* live marker, our own marker
     is missing — a peer's `--force`/`reap` collected it, or the read lagged the write. We cannot
     *demonstrate* we hold the lock, so we do not hold it. "We cannot tell" resolves to **loss**, not
     to a win; the claimant removes any trace and retries. Treating the empty read as a win lets a
     worker announce a lock while holding nothing, leaving the item free for the next claimant.
   - **"Already gone" is success for a collector.** A `DELETE` that 404s means the marker is not
     there, which is exactly what the caller wanted. Two workers collecting the same expired marker
     must not turn the loser's benign 404 into a refusal to claim.

   The CAS assumes the re-read observes every concurrently-posted marker. GitHub offers no formal
   read-after-write guarantee across replicas, so a sufficiently unlucky pair of racers could each see
   only its own marker. The lease bounds the damage (the loser's marker expires and is collected), and
   `who` surfaces a double-hold, but this is the residual risk the git-ref CAS in *Alternatives* would
   have removed outright.

3. **Claims carry a lease, and an expired lease cannot be resurrected.** A marker's `updated_at` is
   its heartbeat; past `FSGG_CLAIM_LEASE_MIN` (default 120m) it is **stale**. Three rules keep a
   lease from becoming a second, silent lock:

   - **`claim` collects the stale markers it claims over** (and tells their workers) rather than
     merely ignoring them. A stale marker that is ignored *survives*, and can be renewed later.
   - **`heartbeat` renews only the current holder** — the lowest live marker. A worker whose lease
     expired is **refused and told to stop**; if another worker now holds the item, the refusal
     names them. Otherwise a worker that missed a heartbeat could renew its dead marker back to life
     *underneath* the worker that legitimately claimed next, and both would hold the item — the very
     failure this ADR exists to remove, reintroduced through the lease.
   - **`reap` re-verifies freshness immediately before deleting**, because a snapshot is not a lock:
     a holder that heartbeats between the scan and the delete keeps its claim.

   `reap` is a **dry run by default** and **tells the reaped worker** over the channel (§5) rather
   than silently stealing its item. `say`/`widen` renew a *live* claim implicitly, because a worker
   talking about its item is a worker still working it — they never revive a stale one.

4. **The worker id is stamped where attribution is later needed** — `git config fsgg.worker` in the
   worktree, an `FSGG-Worker:` commit trailer `claim` prints, and, on the claim marker itself,
   `harness=<name> session=<id>` provenance naming the agent transcript that took the lock. Provenance
   need not be unique to be useful: it turns "which agent claimed this?" into a lookup rather than the
   mtime-and-`ps` forensics §2's incident was reduced to. `fsgg-coord who` is the standing
   answer to "what is actually running": worker, age, lease health, declared paths, branch, and
   (`--local`) the worktree each item is checked out in. It flags two states the board cannot show:
   a **stale** claim, and an `In progress` item with **no marker at all** — the state §2's incident
   was in. Working outside the protocol is now loud instead of invisible.

5. **Workers have a channel.** `say` posts an `fsgg:msg` comment addressed to a worker id (or `*`);
   `inbox` delivers what is new for this worker across every live claim, behind a per-worker cursor.
   Messages ride the item they concern, so the conversation sits next to the work and GitHub notifies
   for free — the same reason [ADR-0001](0001-cross-repo-coordination-via-issues.md) chose issues
   over a file mailbox.

6. **`batch` is the scheduler; `take` is the worker loop.** `batch` returns a **maximal set of items
   whose touch-sets are disjoint from each other and from every in-flight claim** — the question a
   fan-out asks, answered once instead of O(n²) pairwise by hand. It reads the **marker**, not the
   board column, and a claimed item does not merely drop out: **its touch-set is reserved**, so a
   candidate overlapping held work is never scheduled. Items with no `Paths:` are unschedulable and
   are **reported**, never silently dropped. `take` picks and claims in one step and **re-schedules on
   a lost race**, where ADR-0021's `next || exit 0; claim || exit 0` sent a losing worker home.

7. **`widen` closes ADR-0021's loop.** It rewrites the `Paths:` line, re-checks against every live
   claim, and — the part a worker cannot do alone — **notifies the workers it now collides with**, on
   their own items, then exits non-zero.

8. **`overlap` gains `--active`** (one candidate against all in-flight claims), and
   **`verify-paths --pr N`** reports files a PR changed outside its issue's declared touch-set. CI
   runs it in `--warn` mode: the touch-set stays a **declaration, not an enforced boundary**
   (ADR-0021), but an undeclared drift is what turns two "disjoint" items into a merge conflict, and a
   declaration is worth only as much as the check that reads it back.

## Consequences

- **ADR-0021 §1 is superseded**; §2 (worktree isolation) and §3–4 (touch-set, disjointness) stand and
  are strengthened. The `Blocked by` sequencing, the earned done-stamp, the epic roll-up, and the
  board's role as the source of *order* are unchanged.
- **`fsgg-coord` gains** `whoami`, `heartbeat`, `who`, `reap`, `take`, `batch`, `widen`, `say`,
  `inbox`, `verify-paths`; `claim`/`release` are reimplemented on markers; `overlap` gains `--active`.
- **The lock is now honest under the conditions it is used in**, but it is still cooperative: nothing
  compels a worker to call `claim`. What changed is that skipping it is now **detectable** (`who`
  reports `UNCLAIMED`) rather than invisible. We say so rather than claim a guarantee we cannot keep.
- **Cost.** All new machinery is REST (5,000 *requests*/hr, a separate budget from GraphQL's 5,000
  *points*/hr). `who`/`reap`/`inbox` cost one board scan (~3 GraphQL points) plus 2 REST reads per
  in-flight item — bounded by the number of workers. `batch`/`take` add 2 REST per candidate they
  examine. No new board schema, no new durable store, no daemon.
- **A shared checkout defeats the id, and therefore the lock.** Rule 4 of §1 warns, loudly, every
  time. The fix is the one ADR-0021 already mandates: **one worktree per item**.
- **The lock fails closed, and never guesses.** A read that *fails* and an item with *no markers* must
  not look alike, or a rate-limited read would report "nobody holds this". `claims_of` therefore dies
  rather than returning an empty set. Every path that has already posted a marker must either keep it
  (won) or remove it (lost, or cannot tell) — a failed CAS re-read removes it, and a *failed removal*
  says so explicitly instead of reporting a clean back-off. A marker whose `worker=` cannot be parsed
  counts as a claim held by `unparsed-marker`, blocking the item rather than vanishing.
- **Markers are anchored to the start of a comment body.** Matched anywhere, any free-form `say`
  message quoting `<!-- fsgg:claim worker=… -->` would forge a lock on the item it rode in on.
- **The lease spans two clocks** — the client's `now` against GitHub's `updated_at`. The 120m default
  is orders of magnitude above realistic NTP skew, but a badly-skewed client can reap live claims.
  `who` prints the age it computed, so the skew is visible rather than silent.
- **Two latent `exit`-through-`if` bugs were fixed in passing**: `set_field`'s `die` is an `exit`, so
  the "best-effort" board flip inside `claim`/`release` aborted the command *after* the lock was
  taken; and `paths_of` propagated `grep`'s exit-1 ("no `Paths:` declared") through `set -o pipefail`,
  killing the very schedulers meant to report that item. Both are covered by the fixture, as are the
  adversarial interleavings above (stale-marker resurrection, CAS-read failure, reap TOCTOU, forgery).
- **This does not change the shape of the system** (no new repos, boundaries, coherent-set axes, or
  contracts), so `docs/architecture.md` needs no reconcile — the `architecture-map: unaffected`
  opt-out applies.

## Alternatives considered

- **Keep the assignee, compare on a worker tag.** Smallest diff, but inherits ADR-0021's TOCTOU
  window: a genuine simultaneous claim still ends with both racers backing off, and no winner.
- **A git-ref CAS** (`POST /git/refs` returns 422 if the ref exists) — a true single-call atomic
  compare-and-swap with no re-read. Rejected for costing a hidden ref namespace invisible in the
  GitHub UI, a `contents: write` permission, and a *second* primitive for worker identity (a ref holds
  only a sha), which lands the metadata back in a comment anyway. Comment-order CAS unifies the lock,
  the identity, the lease, and the channel in one primitive that humans can read in the issue timeline.
- **A same-machine `flock`.** Correct and trivial for one host, useless for the mixed agent/human,
  multi-machine case the protocol targets.
- **The agent harness's session id as the worker id.** Superficially the obvious answer — every
  harness has one, and it is stable for a worker's lifetime. Rejected as an *identity* because the
  cardinality is a property of the harness, not of the name: Claude Code shares one session id across
  all subagents of a fan-out (so N workers would share one id — the bug, reintroduced), OpenCode gives
  each subagent its own child session (so it would work), and Codex CLI exports none. It is also a
  UUID, unreadable in a `who` table or a `say --to`. Kept as a **fallback** (better than a per-checkout
  name) and as **provenance** on every claim. See
  [`docs/coordination/agent-session-identifiers.md`](../coordination/agent-session-identifiers.md).
- **A shared-file work queue** (`claims/`, lock files). Rejected for the reason ADR-0001 and ADR-0021
  already gave: git is append-only history, not a queue.
