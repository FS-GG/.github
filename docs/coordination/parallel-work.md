# Intra-repo parallel-work protocol

How multiple workers (agents or people) run **in parallel on different items inside one
FS-GG repo** without grabbing the same item, stomping a shared working tree, or colliding on
the same files. This is the inner-repo sibling of the
[cross-repo coordination protocol](README.md) — it **reuses** that fabric (the Coordination
board, `Blocked by` sequencing, the `fsgg-coord` client, the earned done-stamp) and adds only
the three primitives intra-repo parallelism needs. Decision:
[ADR-0021](../adr/0021-parallel-intra-repo-work-claim-worktree-touchset.md).

## Why a separate protocol

[ADR-0001](../adr/0001-cross-repo-coordination-via-issues.md) coordinates work *between*
repos. Three properties it relies on are free across repos but **not** free inside one:

1. **One owner per repo** → across repos two actors never race for the same unit of work;
   inside one repo, N workers can grab the same issue.
2. **Separate checkouts** → across repos parallel work can't share a working tree; inside one
   repo, two agents editing one checkout collide.
3. **The shared surface is a versioned contract** the registry tracks → inside one repo the
   shared surface is **files**, with no "can these two run at once?" check.

A shared-file work queue (a `claims/` folder, lock files) is rejected for the same reason
ADR-0001 rejected a file mailbox: git is append-only history, not a queue. The GitHub-native
primitives already backing ADR-0001 cover all three gaps.

## The three primitives

### 1. Claim → assignee + `Status: In progress`

A worker claims an item by **assigning itself and flipping the board `Status` to
`In progress`** in one step. The **issue assignee is the advisory lock** (first assignee
wins).

```sh
scripts/fsgg-coord claim <issue>          # assign @me + Status=In progress; prints the branch
scripts/fsgg-coord claim <issue> --force  # steal an item already held by someone else
scripts/fsgg-coord release <issue>        # unassign @me + Status=Ready (back into the pool)
```

The lock is honest advisory locking, not a distributed mutex: `claim` re-reads the assignees
after assigning and, on a genuine simultaneous claim, **backs off** (removes its own
assignment) rather than let both racers proceed. The board plus PR review catch any residual
double-grab before it wastes real work.

### 2. Isolate → one branch + one git worktree per item

A claimed item is worked on `item/<n>-<slug>` in its **own git worktree**, so parallel
workers never share a working tree. `claim` prints the exact command.

```sh
git worktree add ../<repo>-<n> -b item/<n>-<slug>
# ...work, commit, push, PR into main...
git worktree remove ../<repo>-<n>
```

Integration is by PR into a green `main`. Agents should prefer the harness's built-in
worktree isolation (`isolation: "worktree"`), which is the same discipline managed for them.

### 3. Touch-set → a declared `Paths:` line + an overlap check

Each item declares the file subtrees it will touch as a **`Paths:`** line in its issue body:

```
Paths: src/Scene/**, tests/Scene/**
```

Two items may run in parallel **iff their touch-sets are disjoint**:

```sh
scripts/fsgg-coord overlap <a> <b>    # DISJOINT -> exit 0 (parallel); OVERLAP -> exit 1 (sequence)
```

- **DISJOINT** → own worktree each, run concurrently.
- **OVERLAP** → **sequence** with the board's existing `Blocked by` field (or a sub-issue
  chain). Overlapping items merge in dependency order and rebase; disjoint items merge in any
  order.
- `overlap` compares declared globs as **subtrees** — conservative (errs toward reporting
  overlap) and file-existence-independent, so a new feature that adds files still has a
  checkable touch-set. Exit 2 means an item declared no `Paths:` — add one.

The touch-set is **transient** (per-item, gone at merge), so it lives in the issue body, not
the registry — the registry is for *durable* cross-repo contracts only.

## What is inherited unchanged

- **Sequencing** — the Coordination board's `Blocked by` and sub-issues (ADR-0001). Overlap
  detection just decides *which* items get a dependency edge.
- **Picking work** — `fsgg-coord next --repo <r>` / `ready --repo <r>`.
- **Finishing** — `fsgg-coord done <issue> --flip`: the green `FSGG-DONE` stamp earned only
  after the PR is merged **and** the board is `Done`, with automatic epic roll-up.
- **The GraphQL budget discipline** — `claim`/`release` reuse the cached `set-field`;
  `overlap` reads issue bodies over REST. No new budget cost, no new board schema.

## Setup

- `Status: In progress` and `Blocked by` already exist on the board — **no schema change is
  required**. A repo may add an optional `Paths` text field if it wants touch-sets filterable
  on the board, but the protocol reads the `Paths:` line from the issue body.
- To activate the protocol in a product repo, copy the
  [`intra-repo-parallel-work`](../../.claude/skills/intra-repo-parallel-work/SKILL.md) skill
  into that repo's `.claude/skills/` (same as the cross-repo skill).
