# Intra-repo parallel-work protocol

How multiple workers (agents or people) run **in parallel on different items inside one
FS-GG repo** without grabbing the same item, stomping a shared working tree, or colliding on
the same files. This is the inner-repo sibling of the
[cross-repo coordination protocol](README.md) — it **reuses** that fabric (the Coordination
board, `Blocked by` sequencing, the `fsgg-coord` client, the earned done-stamp) and adds only
the primitives intra-repo parallelism needs. Decisions:
[ADR-0021](../adr/0021-parallel-intra-repo-work-claim-worktree-touchset.md) (worktrees,
touch-sets) and [ADR-0027](../adr/0027-worker-keyed-claim-lock-and-worker-channel.md) (worker
identity, the lock, the channel, the scheduler).

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

## 0. Every worker has a name

**This is the load-bearing part.** N agents in a fan-out authenticate as the **same GitHub
account**, so `@me` is one principal for all of them. Anything keyed on the account — the issue
assignee, most obviously — cannot tell two workers apart. ADR-0021 keyed its lock on the assignee,
and the lock was consequently a **no-op** under exactly the conditions it existed for: a second
worker's claim on a held item sailed straight through, and both worked it.

Everything below is therefore keyed on a **worker id**.

```sh
scripts/fsgg-coord whoami          # this worker's id, and which rule produced it
```

Resolution order:

| | Rule | When |
|---|---|---|
| 1 | `--worker <id>` (global flag) | an orchestrator naming an agent it spawns |
| 2 | `$FSGG_WORKER` | same, via the environment |
| 3 | the **git worktree's name** | the normal case — §2 gives each item its own worktree, so the worktree *is* an identity |
| 4 | the **agent harness's session id**, hashed to a memorable name | a fallback; deterministic, no state |
| 5 | generated, persisted per checkout | last resort — a single worker in the primary checkout |

Rules 4 and 5 **warn on every claim**, because each can hand one id to several workers — and they say
which reason applies. Rule 5: a checkout shared by several workers gives them one id. Rule 4: on
Claude Code every subagent of a session shares its `CLAUDE_CODE_SESSION_ID`, so a fan-out collapses
onto one id — the same-account bug, one level down. (On OpenCode subagents are *child* sessions with
their own ids, so rule 4 is genuinely per-worker there and does not warn. `fsgg-coord` knows the
difference per harness; unknown harnesses are assumed shared, which is the safe default.)

**A session id is not an identity, and it was never going to be.** It is unique per *session*, not per
*worker*, and the mapping between the two is a property of the harness. See
[agent-session-identifiers.md](agent-session-identifiers.md) for the survey (Claude Code, OpenCode,
Codex CLI, Gemini CLI) and the evidence. If you are fanning out, fan out with worktrees (§2), or set
`FSGG_WORKER` per worker.

What the session id *is* good for is **provenance**: every claim marker records
`harness=<name> session=<id>`, so "which agent transcript took this lock?" is a lookup. That is the
question the incident behind [#255](https://github.com/FS-GG/.github/issues/255) could only answer
with file mtimes and the process list.

The id is stamped where attribution is later needed — `claim` writes `git config fsgg.worker` into
the worktree and prints the commit trailer to use:

```sh
git commit --trailer "FSGG-Worker: finch-a3f"
```

Without that, "who edited these files?" is answerable only by mtime forensics, which is where this
protocol's first real incident ended up.

## The primitives

### 1. Claim → a marker comment, won by comment-order CAS

A worker claims an item by posting an `fsgg:claim` marker comment, re-reading, and taking the
**lowest live marker id** as the winner.

```sh
scripts/fsgg-coord take --repo <r>          # pick + claim the next schedulable item (the usual entry point)
scripts/fsgg-coord claim <issue>            # claim a specific item; prints the worktree to isolate in
scripts/fsgg-coord claim <issue> --force    # steal an item another worker holds
scripts/fsgg-coord release <issue>          # drop the lock; the item returns to the pool (Status=Ready)
scripts/fsgg-coord heartbeat <issue>        # renew the lease on a long-running claim
```

GitHub issues comment ids from **one server-side sequence**, so "lowest live id" is a total order
that every racer observes identically: a simultaneous claim has exactly **one winner**, and the
losers know they lost and delete their own marker. (ADR-0021's assignee check-then-set could only
make *both* racers back off — a livelock — or, same-account, let both through.)

The **assignee is still set**, because it is what a human sees on the board. It is not the lock. The
board `Status: In progress` flip is **best-effort** for the same reason: an item not yet on the board
still claims. **The marker is the lock.** Anything asking "is this taken?" reads the marker, never
the column.

That rule binds the **readers** too, and for a while it did not. `who`, `reap`, `inbox`, `batch` and
`overlap --active` all take their view of in-flight work from one place, and that place used to *ask
the column* which items to look at — so a claim whose `Status` flip failed, or a claim on an item
that is not on the board at all, was invisible to every one of them (FS-GG/.github#257). `reap` could
not collect a dead worker off such an item, so its lease stopped self-healing; and because `batch`
reserves the touch-set of everything in flight, an off-board claim's `Paths:` were never reserved —
the scheduler would hand a second worker an item overlapping the subtree the first was editing.

So the in-flight set is now the **union** of two things: the board's `In progress` column, and every
open issue that **carries a live marker**, wherever the board thinks it sits. Only the column can
report `UNCLAIMED` (a markerless item is otherwise just an issue); only the marker can report a lock.

**Leases.** A marker's `updated_at` is its heartbeat. Past `FSGG_CLAIM_LEASE_MIN` (default 120m) the
claim is *stale*: ignored by the lock, collected by the next claimant (who tells you), and reapable.

**An expired lease cannot be renewed.** `heartbeat` renews only the *current holder*; if your lease
lapsed it refuses and tells you to **stop working the item** — naming the worker that holds it now, if
any. Re-take it with `claim` (which re-runs the CAS) or walk away. Renewing a dead marker would
resurrect it underneath whoever claimed next, and two workers would hold one item.

```sh
scripts/fsgg-coord reap --repo <r>          # dry run: which claims outlived their lease?
scripts/fsgg-coord reap --repo <r> --apply  # release them — and TELL the reaped worker (§4)
```

`reap` re-checks each claim's freshness immediately before releasing it, so a holder that heartbeats
mid-reap keeps its lock. `say` and `widen` renew a *live* lease implicitly — a worker talking about its
item is a worker still working it — but they never revive a stale one.

### 2. Isolate → one branch + one git worktree per item

A claimed item is worked on `item/<n>-<slug>` in its **own git worktree**, so parallel workers never
share a working tree. `claim` prints the exact command.

```sh
git worktree add ../<repo>-<n> -b item/<n>-<slug> origin/main
# ...work, commit, push, PR into main...
git worktree remove ../<repo>-<n>
```

The base ref is **not optional**. `git worktree add -b <new>` with no commit-ish branches from the
shared checkout's `HEAD` — and the premise of this protocol is that N workers pass through that
checkout, so its `HEAD` is routinely whatever unmerged branch the last one left. The item's PR then
carries that branch's commits as well as its own, and nothing on the path warns: the touch-set
declaration was honest, the *branch base* was not. At best `verify-paths` reports the resulting drift
as an advisory — and only while the sibling branch is still unmerged. Once it lands on `main` first,
GitHub computes the PR's diff against the new base, the foreign commits disappear from it, and nothing
sees them at all (.github#319).

Integration is by PR into a green `main`. Agents should prefer the harness's built-in worktree
isolation (`isolation: "worktree"`), which is the same discipline managed for them. The worktree also
supplies the worker id (§0 rule 3), so this is not merely hygiene.

`claim` stamps the worker into the worktree (`git config fsgg.worker`), which is what `who --local`
reads back. Git only scopes those keys per-worktree when the repo enables the worktree-config
extension — otherwise they land in the **shared** config and the last claim overwrites every earlier
one, so `who --local` names one worker for every worktree. Enable it once per checkout:

```sh
git config extensions.worktreeConfig true
```

`claim` says so when it has to fall back; it does not enable the extension for you.

### 3. Touch-set → a declared `Paths:` line, and a scheduler that reads it

Each item declares the file subtrees it will touch as a **`Paths:`** line in its issue body — a
comma- or space-separated list of **exact paths and directory prefixes**:

```
Paths: src/Scene/**, tests/Scene/**, Directory.Packages.props
```

**This is not a glob language** (FS-GG/.github#273). Each token is matched by *exact equality* or
*subtree containment*; the only wildcard recognised is a **trailing** `/**` or `/*`, which is
stripped to leave the directory prefix. A leading `**/`, or a `*` anywhere in the middle, matches
**nothing** — and a token that matches nothing would *conflict with nothing*, so an unmatchable
declaration would read as `DISJOINT` against every item on the board. Rather than silently clear,
the tool **refuses**: `claim`, `widen`, `batch`, `overlap`, and `verify-paths` all reject an
unmatchable token and name it. Spell such paths out:

| want | write |
|---|---|
| every lockfile | each one, exactly: `src/A/packages.lock.json, src/B/packages.lock.json` |
| a subtree | `src/Scene/**` (or `src/Scene`) |
| one file | `Directory.Packages.props` |

**And a declaration is a line the author wrote as one** (FS-GG/.github#277). The reader skips fenced
(` ``` `, `~~~`) and indented code blocks. Otherwise an issue that merely *quotes* a `Paths:` line —
in a reproduction, in a suggested `widen` — adopts it as its own touch-set, reserving the wrong files
while every token still looks well-formed, so the #273 guard clears it. A real declaration is indented
**at most 3 spaces, never a tab**: markdown reads 4 spaces (or a tab) as a code block, and so does the
reader, so a tab-indented `Paths:` line declares nothing and the item is refused as undeclared. An
issue whose only `Paths:` line is fenced therefore declares **nothing**: unschedulable beats
mis-scheduled.

What survives the strip is **unioned**. A bare `Paths:` line at column 0 is a declaration wherever it
sits, so a body carrying two of them is ambiguous — and the reader **over**-reserves rather than
guess. Taking only the first would reserve a bare *quotation* and silently drop the real declaration
beneath it: under-reserving, so two workers are told `DISJOINT` on a file they both edit, which is
ADR-0021's own failure mode. Over-reserving costs a false `OVERLAP` — loud, investigable, and it
spends only parallelism. `widen` rewrites the same lines the reader reads, collapsing duplicates to
one, so it can never patch a quotation and leave the real declaration standing.

Two items may run in parallel **iff their touch-sets are disjoint**. Do not check this by hand,
pairwise — ask the scheduler, which also accounts for what is already **in flight**:

```sh
scripts/fsgg-coord batch --repo <r>          # a maximal set of items that may run at once
scripts/fsgg-coord batch --repo <r> -n 4     # ...at most 4, one per worker
scripts/fsgg-coord overlap <a> --active      # one candidate vs every live claim
scripts/fsgg-coord overlap <a> <b>           # the pairwise check (DISJOINT=0, OVERLAP=1, undeclared=2)
```

`batch` picks items whose touch-sets are disjoint **from each other and from every claimed item**.
A claimed item does not merely drop out of the batch — **its touch-set is reserved**, so a candidate
overlapping held work is never scheduled. Items with no `Paths:` are unschedulable and are
**reported**, never silently dropped (an undeclared touch-set that vanished would read as "no work").

- **DISJOINT** → own worktree each, run concurrently.
- **OVERLAP** → **sequence** with the board's existing `Blocked by` field (or a sub-issue chain), or
  **talk** (§4) and split the touch-set. Overlapping items merge in dependency order and rebase;
  disjoint items merge in any order.
- `overlap` compares declared tokens as **subtrees** — conservative (errs toward reporting overlap)
  and file-existence-independent, so a new feature that adds files still has a checkable touch-set.
- An **unmatchable** token (see above) is treated exactly as an *unknown* touch-set: `overlap` exits
  2, `batch` passes the candidate over, and a **held** item that declares one reserves nothing, so
  `batch` refuses to schedule anything against it rather than hand its files to a second worker.

The touch-set is **transient** (per-item, gone at merge), so it lives in the issue body, not the
registry — the registry is for *durable* cross-repo contracts only.

**Widening mid-flight.** ADR-0021 required a worker that widens its touch-set to re-declare and
re-check. `widen` does all of it, including the part a worker cannot do alone — telling the workers it
now collides with, on their own items:

```sh
scripts/fsgg-coord widen <issue> --paths "src/Scene/**, src/Audio/**"
```

It exits non-zero on a collision. Stop editing the shared paths until it is resolved.

**Drift.** The touch-set is a *declaration*, not an enforced boundary. `verify-paths` reads it back
against what a PR actually changed:

```sh
scripts/fsgg-coord verify-paths --pr <n> [--warn]
```

CI runs it in `--warn` mode (see `.github/workflows/touch-set-drift.yml`): it reports, it does not
block. An undetected drift is what turns two "disjoint" items into a merge conflict.

### 4. See and say → visibility, and a channel

```sh
scripts/fsgg-coord who --repo <r>            # who holds what, right now
scripts/fsgg-coord who --repo <r> --local    # ...joined to the local git worktrees
```

`who` is the answer to "what is actually going on" — worker, age, lease health, declared paths,
branch, worktree. It reports every item holding a live marker, **including one the board never heard
of**, and flags two states the board cannot show:

- **`STALE`** — a claim past its lease. Its worker probably died; `reap` collects it.
- **`UNCLAIMED`** — an item the board calls `In progress` that carries **no claim marker**. Someone is
  working outside the protocol, and nothing records who. This is detection, not prevention: nothing
  compels a worker to claim. What changed is that skipping it is now loud.

When two workers' work touches, they can talk. Messages are issue comments addressed to a worker id
(or `*`), riding the item they concern — so the conversation sits next to the work and GitHub
notifies for free:

```sh
scripts/fsgg-coord say <issue> --to <worker> 'I own src/Audio until this lands.'
scripts/fsgg-coord say <issue> 'Anyone else in here?'      # broadcast (to=*)
scripts/fsgg-coord inbox --repo <r>                        # what is new for me, across every live claim
scripts/fsgg-coord inbox --repo <r> --peek                 # ...without advancing the cursor
```

`reap --apply` and `widen` both post to this channel rather than acting silently: a reaped worker is
told its claim was collected, and a worker whose touch-set was invaded is told by whom.

### 5. Finish — the earned done-stamp (unchanged)

```sh
scripts/fsgg-coord done <issue> --flip     # green FSGG-DONE only after PR merged AND Status=Done
```

Same stamp and epic roll-up as cross-repo.

## The fan-out loop

```sh
# scheduler: how many items can run at once, given what is already in flight?
scripts/fsgg-coord batch --repo <r> -n 4

# each worker, independently — named, isolated, and safe against a lost race:
export FSGG_WORKER=finch-a3f                  # or let the worktree name it (§0 rule 3)
scripts/fsgg-coord take --repo <r>            # pick + claim + print the worktree, retrying on a lost race
git worktree add ../<repo>-<n> -b item/<n>-<slug> origin/main   # name the base: HEAD is not `main` (§2)
# ...implement; `heartbeat` if it runs long; `say`/`inbox` if work touches...
scripts/fsgg-coord done <issue> --flip        # earn the stamp
scripts/fsgg-coord release <issue>            # ...or hand it back
```

## What is inherited unchanged

- **Sequencing** — the Coordination board's `Blocked by` and sub-issues (ADR-0001). Overlap
  detection just decides *which* items get a dependency edge.
- **Finishing** — `fsgg-coord done <issue> --flip`: the green `FSGG-DONE` stamp earned only after the
  PR is merged **and** the board is `Done`, with automatic epic roll-up.
- **The GraphQL budget discipline.** Everything in this protocol is **REST** (a separate 5,000
  *requests*/hr budget) except the board scan `who`/`batch`/`reap` share (~3 GraphQL points). `who`,
  `reap`, and `inbox` add 2 REST reads per in-flight item — bounded by the number of workers.
  `batch`/`take` add 2 REST per candidate examined. No new board schema, no new durable store.

## Setup

- `Status: In progress` and `Blocked by` already exist on the board — **no schema change is
  required**. A repo may add an optional `Paths` text field if it wants touch-sets filterable
  on the board, but the protocol reads the `Paths:` line from the issue body.
- `claim`/`release`/`say` need `issues: write`; the board writes need
  `gh auth refresh -s project,read:project`, as the rest of `fsgg-coord` does.
- To activate the protocol in a product repo, copy the
  [`intra-repo-parallel-work`](../../.claude/skills/intra-repo-parallel-work/SKILL.md) skill
  into that repo's `.claude/skills/` (same as the cross-repo skill). To get the advisory drift
  check, copy `.github/workflows/touch-set-drift.yml` too.
