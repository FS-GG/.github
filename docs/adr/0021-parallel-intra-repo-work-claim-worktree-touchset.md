# ADR-0021: Parallel intra-repo work via claim + worktree + declared touch-set

- **Status:** Accepted — **§1 (the assignee lock) is superseded by
  [ADR-0027](0027-worker-keyed-claim-lock-and-worker-channel.md)**; §2 (worktree isolation) and
  §3–4 (touch-set, disjointness) stand.
- **Date:** 2026-07-06
- **Affects:** all FS-GG repos (the protocol is org-wide; `.github` owns it)

> **Amendment (2026-07-09, [ADR-0027](0027-worker-keyed-claim-lock-and-worker-channel.md),
> [#255](https://github.com/FS-GG/.github/issues/255)).** §1's claim lock — "the issue assignee is the
> advisory lock (first assignee wins)" — does not hold for the case it was written for. When N workers
> fan out under **one GitHub account**, `@me` names the same principal for all of them: an item a
> sibling already holds reads back as "assigned to me", the "is anyone *else* holding this?" test
> passes, and **both workers proceed**. The assignee names an
> *account*; the thing that must be excluded is a *worker*. ADR-0027 keys the lock on a **worker id**
> and resolves races by comment-order compare-and-swap. Read §1 below as history.

## Context

[ADR-0001](0001-cross-repo-coordination-via-issues.md) coordinates work *between* the
FS-GG repos: requests are issues, sequencing is the org-level **Coordination** Projects v2
board, contracts live in the registry, decisions are ADRs. That fabric is repo-agnostic —
the board already carries a `Repo` field and `fsgg-coord next --repo <r>` already answers
"what do I pick up next" for a single repo.

What it does **not** solve is running **multiple workers (agents or people) in parallel on
different items inside one repo**. Cross-repo, three things come for free that intra-repo
parallelism has to earn:

1. **A repo has one owner**, so two actors never race for the same unit of work. With N
   parallel workers in one repo, two can grab the same issue.
2. **Separate repos are separate checkouts**, so parallel work can never stomp a shared
   working tree. Inside one repo, two agents editing one checkout collide.
3. **The shared surface between repos is a versioned *contract*** the registry tracks. The
   shared surface *within* a repo is **files/modules** — and there is no equivalent "can
   these two run at once?" check.

A shared-file "work queue" (a `claims/` folder, a lock file) was rejected for the same
reason ADR-0001 rejected a file mailbox: git is append-only history, not a queue —
concurrent writers conflict and there are no notifications. The GitHub-native primitives
that already back ADR-0001 cover all three gaps.

## Decision

Add a thin **intra-repo parallel-work protocol** on top of the existing board — a claim
lock, a worktree convention, and a declared touch-set — with no new durable store.

1. **Claim = assignee + `Status: In progress`, as one step.** A worker claims an item by
   assigning itself and flipping the board `Status` to `In progress`. **The issue assignee
   is the advisory lock** (first assignee wins); the tooling refuses an already-claimed item
   unless `--force`. This is the native, notified, `gh`-scriptable analogue of "a repo has
   one owner", made per-item.

2. **Isolation = one branch + one git worktree per item.** A claimed item is worked on
   branch `item/<issue>-<slug>` in its **own git worktree**, so parallel workers never share
   a working tree. Integration is by PR into a green `main`; disjoint items merge in any
   order, overlapping items merge in dependency order (see §4). This mirrors "separate repos
   are separate checkouts" without splitting the repo.

3. **Touch-set = a declared `Paths:` line on the item.** Each item declares the file
   globs it intends to touch as a `Paths:` line in the issue body (a subtree per token, e.g.
   `Paths: src/Scene/**, tests/Scene/**`). This is the intra-repo analogue of a contract: it
   makes the shared surface explicit and checkable **before** work starts, without a
   registry (the touch-set is transient, per-item — it does not outlive the merge).

4. **Schedulability = disjoint touch-sets.** Two items may run in parallel **iff their
   declared touch-sets are disjoint**. Overlap is detected up front
   (`fsgg-coord overlap <a> <b>`); overlapping items are **sequenced** with the board's
   existing `Blocked by` field (or a sub-issue chain) rather than run concurrently. Nothing
   new is invented for sequencing — it reuses ADR-0001's board.

Everything else is inherited verbatim: the board is still the source of *order*, the
registry of *contracts*, ADRs of *decisions*; `fsgg-coord done <issue> --flip` is still the
earned done-stamp and epic roll-up.

## Consequences

- **`.github` owns the protocol.** It is documented in the
  [`intra-repo-parallel-work`](../../.claude/skills/intra-repo-parallel-work/SKILL.md) skill
  and [`docs/coordination/parallel-work.md`](../coordination/parallel-work.md), siblings of
  the cross-repo protocol. To activate it in a product repo, copy the skill into that repo's
  `.claude/skills/` (same as ADR-0001's skill).
- **`fsgg-coord` gains three subcommands** — `claim <issue> [--force]`,
  `release <issue> [--status S]`, and `overlap <a> <b> [--repo r]` — thin `gh` wrappers in
  the existing house style, spending no new GraphQL budget (`claim`/`release` reuse the
  cached `set-field`; `overlap` reads issue bodies over REST).
- **No new board schema is required.** `Status: In progress` already exists and `Blocked by`
  already sequences. The touch-set lives in the issue body; a repo that wants it filterable
  MAY add an optional `Paths` text field, but the protocol does not need one.
- **The claim lock is advisory, and we say so.** Assignment is check-then-set, so a genuine
  simultaneous claim has a small TOCTOU window; `claim` re-reads after assigning and refuses
  (releasing its own assignment) if it lost the race. This is honest advisory locking, not a
  distributed mutex — sufficient because the board plus PR review catch any double-grab
  before it wastes real work.
- **The touch-set is a declaration, not an enforced boundary.** `overlap` compares declared
  globs as subtrees (file-existence-independent, conservative — it errs toward reporting
  overlap); it does not prevent a worker from editing outside its declared paths. A worker
  that must widen its touch-set re-declares and re-checks overlap before continuing.
- **A directory reservation includes future children.** A file-level declaration never escapes a
  sibling's parent-directory token merely because the file is not in the tree yet. That remains a
  conservative overlap and is sequenced; the filing path now warns when a proposed declaration
  strictly contains a sibling's, so the holding row can be narrowed or the work explicitly ordered
  before a worker spends a lease discovering the lane of one (#1843).
- **This does not change the shape of the system** (no new repos, boundaries, coherent-set
  axes, or contracts), so `docs/architecture.md` needs no reconcile — the
  `architecture-map: unaffected` opt-out applies.
