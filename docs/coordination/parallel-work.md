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

**If `whoami` warns, mint an id — and mint it with the tool, never by hand:**

```sh
eval "$(scripts/fsgg-coord whoami --mint)"    # sets FSGG_WORKER in THIS shell; run it per worker
```

This is the **one** mint idiom. It is what `whoami`'s own warning prints, it needs no shell trivia,
and it is the only one that can change its scheme without a migration across every doc that quotes it.

**Do not invent an id, and do not copy one from a document — including this one.** That is why no
literal id appears anywhere in these protocol docs as something you could paste. Agents asked to name
themselves *converge*: [#419](https://github.com/FS-GG/.github/issues/419) found **four `finch-*`
workers claiming at once**, every one of them pattern-matched off the single `finch-…` example that
used to sit on this page, while the tool's own minted ids spread cleanly across the word list. The
attractor is the **word**, not the suffix — re-rolling the hex does not help if you reach for the bird
you just read. An id two workers share is an id the lock cannot separate: `release` drops the other's
claim mid-flight, `heartbeat` renews a marker that is not yours, and `say`/`inbox` cross-deliver.

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

The id is stamped where attribution is later needed — `claim` prints the exact commit trailer to use,
with your id already filled in. **Use the line it printed**; do not retype one, and do not derive one:

```sh
git commit --trailer "FSGG-Worker: <the id `claim` printed>"
```

There is no id written here to copy, and no expression to substitute for one, because **both of the
obvious shortcuts are wrong**. `$FSGG_WORKER` is empty for a worker whose id came from the worktree
name (rule 3 — the normal case), so it yields a blank trailer. And `$(git config fsgg.worker)` reads
the id of *whoever claimed most recently*: `claim` stamps `fsgg.worker` per-worktree only when
`extensions.worktreeConfig` is set, which is **not** git's default, so it falls back to the shared
repo config that every linked worktree reads (`fsgg-coord` says so when it happens). A blank trailer
loses the attribution; a borrowed one asserts a false one, which is worse.

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
scripts/fsgg-coord release <issue>          # drop the lease; Status RESTORED to what the claim overwrote
scripts/fsgg-coord release <issue> --status Blocked   # ...drop it, but say where it lands
scripts/fsgg-coord heartbeat <issue>        # renew the lease on a long-running claim
```

`release` drops the **lease**, which is not the same claim as *"this item is startable"*. It undoes
the `In progress` that `claim` set, and only that — but note *how*, because the two cases are
different mechanisms (#481):

- **Restored.** `claim` **overwrites** the column, so it *records* what it overwrote. `release` puts
  that back: a `Backlog` item returns to `Backlog`. It is not preserved — it is remembered. `Ready`
  is only the fallback for a claim that recorded nothing (a pre-#481 marker, or a column that could
  not be read). Guessing `Ready` was the bug: since #440 made `claim` reachable from `Backlog`, every
  undo path quietly **promoted** triaged work into the queue humans read as ready.
- **Kept.** A `Status` you set *deliberately during* the lease — `Blocked`, `Done` — is left alone
  (#331). A column it cannot read is left alone too, rather than guessed.

`reap` collects an expired lease under the same rule, so a claim that dies on a `Blocked` item is not
resurrected as `Ready`. So handing back an item you cannot finish keeps its column honest.

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
scripts/fsgg-coord adopt <issue>            # a DEAD claim whose PR is GREEN: land it, don't bin it
```

`reap` re-checks each claim's freshness immediately before releasing it, so a holder that heartbeats
mid-reap keeps its lock. `say` and `widen` renew a *live* lease implicitly — a worker talking about its
item is a worker still working it — but they never revive a stale one.

**An orphan is not garbage** ([#697](https://github.com/FS-GG/.github/issues/697)). `reap` refuses a
stale claim whose `item/<n>-*` PR is open — the lease lapsed, the *work* did not (#581) — but for a
long time its only offered exit was *"close it, then reap"*, and that sentence was a loaded gun
pointed at the best work on the board. There are **three** states here, not two, and the tool now
tells them apart by reading what the PR **says** rather than merely that it exists:

| the PR on a stale claim | what it means | what to do |
|---|---|---|
| open, still being worked | proof of life (#581) | leave it |
| open, abandoned mid-flight | genuinely dead | close it, then `reap` |
| **open, green and mergeable** | **FINISHED work whose worker died between "green" and "merge"** | **`adopt` it** |

That third row is not a corner case: it is the **success path** of a worker whose harness died in a
window that is minutes long on every single item this protocol produces, and the better the work, the
more expensive the loss. `adopt` verifies the PR is green **and** mergeable, transfers the claim marker
to you (so the lock stays a total order and two workers never both drive one PR), and hands you the
merge. The original author's commits keep their author and their `FSGG-Worker:` trailer — **you are the
lander, not the author.**

`adopt` refuses everything that is not finished work, because each of those is a different act wearing
the same word: a **live** claim (that is a steal — talk to its worker, or `claim --force` and own it),
an item with **no PR** (nothing to land — `reap` it and claim normally), and any PR that is **not green
and mergeable** (rebasing a conflicted PR or fixing a red one is *authoring*, not landing). It refuses
on an **unreadable** PR too: adopting on a guess is how a verified command launders an unverified one.

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

#### `Paths: none` — the touch-set-less item, declared (`.github#496`)

Some items genuinely have no touch-set: an epic, a decision item, an investigation whose scope *is*
the question. They say so **in the declaration**, not in prose:

```
Paths: none
```

This does not make the item schedulable — nothing does, without files. What it buys is **intent, in
a form a machine can read**. Before the sentinel, an epic and a forgotten touch-set rendered
identically —

```
.github#416 — no 'Paths:' declared (cannot schedule)     <- an epic. correct.
.github#419 — no 'Paths:' declared (cannot schedule)     <- somebody forgot. a bug.
```

— so the distinction the protocol depends on existed only in the filer's head. The recipe *asked* for
it as prose ("no touch-set: declare at claim time"), and **nothing read prose**. The result: **nine
items of real work sat on the board looking like work, invisible to every worker who asked for work**,
while `fsgg-coord lint` reported `0 error(s)`. A gate green on a missing subject — the #266 family, in
the one surface whose whole job is board health.

So the sentinel exists, and `lint` enforces it: **`NO-TOUCH-SET`** is an **error** for any
`Ready`/`Backlog` item that declares neither paths nor `Paths: none`. There are exactly two honest
states, and the tool now knows which one it is looking at.

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

**Do not reserve generated artifacts** (FS-GG/.github#309). A `Paths:` token names a file two workers
might *author* into conflict. A file produced by a checked-in generator and guarded by a **regeneration
gate** — a CI check that re-runs the generator and fails on any diff — is neither authored nor
semantically conflicting: **a collision in it is a rebase, not a decision.** Declaring it reserves a
file nobody owns, and serialises every item that regenerates it.

Two conditions, and the second is not optional:

1. **Nobody authors it.** Ask whether a human makes a *merge decision* in it. If two workers' edits can
   only be reconciled by re-running a script, neither has an intent to preserve. The test is
   **authorship, not `.gitignore`** — a generated file that is committed and reviewed is still not
   authored.
2. **A regeneration gate guards it.** Excluding the file moves the guarantee from the *scheduler* to
   *CI* — so CI must actually have it. **If nothing fails on a stale copy, do not exclude it.** You
   would be trading a loud false `OVERLAP` for a silent unguarded staleness, which is strictly worse.
   Add the gate first, or keep declaring the file.

Note this is **not** the `verify-paths` touch-set drift of the **Drift** section below, which is
advisory and blocks nothing. The regeneration gate is the repo's own check on the artifact, and it is
the thing that makes exclusion safe.

**Beware the subtree.** `overlap` matches **directory prefixes**, so declaring the artifact's *parent*
reserves the artifact exactly as effectively as naming it. Declare your sources, not the directory the
generated file happens to sit in:

| | `Paths:` |
|---|---|
| ❌ reserves the baseline against the whole board | `src/Core/**, readiness/**` |
| ❌ same lock, one level down | `src/Core/**, readiness/surface-baselines/**` |
| ✅ | `src/Core/Pathfinding.fs, tests/Core/PathfindingTests.fs` |

The instance that produced this rule: `FS.GG.Game` has one generated
`readiness/surface-baselines/<pkg>.txt` per package — a sorted list of exported type names, reflected
out of the built assembly and gated by a CI check that regenerates it and fails on any diff. Every
`[core]` item adds a module, so every `[core]` item appends to that one file. Declared honestly, every
`[core]` item was pairwise-overlapping with every other, and the whole `P6 Game` phase collapsed to
**one worker** — in the phase the protocol exists to fan out. Excluding it from FS-GG/FS.GG.Game#34's
touch-set let that item run alongside `#32` and `#33`; on rebase onto a `main` that had meanwhile
landed `#32` and `#41` (all `FS.GG.Game`), the baseline three-way-merged with **zero manual fixup** and
the gate passed.

**In this repo, the artifact is `registry/repos.lock`** (FS-GG/.github#527). The coordination kit is
content-addressed: `repos.lock` pins a `sha256` of every kit source — `scripts/fsgg-coord` and each
`.claude/skills/<kit>/`. Editing any kit source invalidates its digest, and `repos-registry-selftest`
reds `main` until it is regenerated:

```sh
scripts/repos.sh relock          # regenerates registry/repos.lock
```

`repos.lock` is generated and CI-gated, so **the rule above applies to it: do not reserve it.**
Regenerate it, commit it, and name it as **expected drift** in the PR.

The digest used to be a `sha256:` field on each `kit:` row *inside the authored* `registry/repos.yml`.
Because a kit source is content-addressed, every kit edit therefore had to reserve the authored roster
— and so serialised against every other kit edit *and* against anyone genuinely authoring a roster row.
**Three workers deadlocked on it in one afternoon** (FS-GG/.github#428). The rule above could not reach
it, because **the rule classifies a FILE and the generated thing was a FIELD inside an authored one**.
Splitting the field out into `repos.lock` (#527) is what lets the existing rule apply.

Note where that fix did *not* land. #527 touched seven files and **none of them was a skill**, so
`intra-repo-parallel-work` went on telling every worker to reserve `registry/repos.yml` and to run
`scripts/repos.sh digest` — a command that still exists and now writes nothing — thereby re-creating
the very deadlock the fix removed (FS-GG/.github#588). And this rule appeared in **no canonical doc at
all**, so there was no source-of-truth statement a docs edit could have carried. It exists here now.
That is the projection defect stated as plainly as it can be stated, and it is the argument for
ADR-0034's decision to *generate* the skills from the model rather than copy them.

And **declare against what the generator emits, not against the issue's prose** — the corollary that
bit hardest. FS-GG/FS.GG.Game#31's acceptance said "surface baseline". It adds a *function* to an
existing module, and the generator emits one exported **type** per line, so it never touched the
baseline at all. A `Paths:` line asserted from an issue body rather than from the generator's output is
how a *false global lock* gets created.

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

**A regenerated artifact will report as touch-set drift, and that is expected** — you excluded it
deliberately (see *Do not reserve generated artifacts* above), and then you regenerated it.
`verify-paths` cannot today distinguish *"you touched a file you never declared"* (a real finding — you
should have `widen`ed) from *"you regenerated the artifact the protocol told you not to declare"*
(correct behaviour). Until it can, **say which one it is** in the PR, so the advisory does not decay
into a line workers skip past. Closing that gap wants a second declaration surface that `verify-paths`
subtracts before reporting — tracked in FS-GG/.github#498, with the ADR that owes ADR-0021 an
explanation of why there are two.

### 4. See and say → visibility, and a channel

```sh
scripts/fsgg-coord who --repo <r>            # who holds what, right now
scripts/fsgg-coord who --repo <r> --local    # ...joined to the local git worktrees
```

`who` is the answer to "what is actually going on" — worker, age, lease health, declared paths,
branch, worktree. It reports every item holding a live marker, **including one the board never heard
of**, and flags two states the board cannot show:

- **`STALE`** — a claim past its lease. Its worker probably died; `reap` collects it.
- **`STALE (#<pr> OPEN — GREEN: LAND IT)`** — the claim is dead and the work is **finished**. Do not
  reap it and do not close the PR: `adopt` it (#697). `who` reads the PR's state, not just its
  existence, precisely because `who` is what a human reads *immediately before deciding to reap*.
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

### 5. Finish — the earned done-stamp

```sh
scripts/fsgg-coord done <issue> --flip     # green FSGG-DONE only after PR merged AND Status=Done
scripts/fsgg-coord child <parent> <issue>  # link a filed issue as a sub-issue of its epic
```

Same stamp and epic roll-up as cross-repo.

**The claim's lifetime is the WORK's lifetime.** `done --flip` drops the marker
([#533](https://github.com/FS-GG/.github/issues/533)); an expired lease whose `item/<n>-*` PR is
still open does **not** release the item ([#581](https://github.com/FS-GG/.github/issues/581)); and a
worker holds **at most one item** ([#516](https://github.com/FS-GG/.github/issues/516)).

Three symptoms of one missing idea. A claim **reserves a touch-set**, so a claim that outlives its
work is a live lock on files nobody is editing — and the items most likely to collide with a
just-finished one are its own **follow-up findings**, the ones §4 tells you to file *because* you were
standing in those files. A claim that dies before its work is worse: `take` handed out
`FS.GG.Rendering#429` with its PR open, because a loaded box stretched one build past the lease, and
the same thing later reaped the claim on `#485` **while that worker was fixing #485**. Lease expiry is
*evidence* of abandonment; an **open PR on the item's own branch is proof of life**, and the branch
name is this protocol's own artifact (§2), not a heuristic.

"Workers should heartbeat" is true and insufficient. ADR-0027's argument is that **the lock**, not
worker diligence, prevents collisions — so a lock that releases itself while its holder is
demonstrably still working is a lock with a liveness bug, and the fix belongs in the scheduler.

**And proof of life is only half of it** ([#697](https://github.com/FS-GG/.github/issues/697)). #581
taught the tools to read *whether* a PR exists and stop there; #651 taught `take` not to hand out an
item whose PR is already open. Neither ever reads **what the PR says** — so the protocol could see
that work was in flight and never that it was **done**. The blind spot is one idea, and its worst face
is the friendliest-looking one: a stale claim whose PR is green, reviewed and mergeable was protected
from `reap` (#581, correctly) and thereby *guaranteed to rot* — the claim reserves its touch-set for
the rest of the lease, `main` moves underneath it, and the PR eventually conflicts and dies. Nothing
ever landed it. The only documented exit was to **close it**, which threw the work away. `adopt` is
that exit, and reading the PR's state — `mergeable`, plus its head's check runs — is what makes it
safe to take.

The CAS is keyed on the **item**, which is why it guarantees at most one worker per item and nothing
guaranteed the converse. That asymmetry is #419/ADR-0027 turned inside out: that family is N workers
colliding on one id; this is one id holding N items, and *"give every worker its own id"* does nothing
for it. `--force` is the way to say you mean it.

**An item is not Done while one of its own sub-issues is open**
([#583](https://github.com/FS-GG/.github/issues/583)). `done --flip` refuses, and names the child.

This is the rule §4 makes you need. §4 tells you to **split off what you cannot land and `child`-link
it** — so the more faithfully a worker split their work, the more reliably they closed the parent
over the piece they had just split out of it. The roll-up read the sub-issue graph of the item's
**parent** and never asked the same question of the item **in hand**: an item was stamped `Done` and
closed over an open child carrying one of its own acceptance criteria, and the green ✓✓ actively said
otherwise. A red stamp is a finding; **a green stamp over unfinished work is a board that lies**,
which is the one thing worse than an unstamped item.

If the split-out work is genuinely *separate* rather than *part of* this item, it should not be a
sub-issue at all — sequence it with `Blocked by`. A sub-issue means **part of**; `Blocked by` means
**after**.

**Put `Closes #<n>` in the commit BODY, not the subject**
([#558](https://github.com/FS-GG/.github/issues/558)). `gh pr create --fill` maps the commit
**subject → PR title** and the **body → PR body**, and GitHub builds `closingIssuesReferences` from
the **PR body only, and only while the PR is open**. So `fix: the thing (closes #165)` — the
near-universal convention — puts the keyword exactly where that field never looks. Everything still
works (the squash commit closes the issue, because GitHub honours the keyword there too), so the
stamp went **red on correct, merged, green work** — permanently, because editing the merged PR's body
does not backfill the link. `done` now also reads GitHub's own `CLOSED_EVENT` closer, so the stamp is
earned either way; but a red that fires reproducibly on correct work is how a red stamp becomes
noise, and this stamp's credibility is the entire point of it.

**Never write a closing keyword next to an issue number you do not mean to close — GitHub does not
read the word "not"** ([#643](https://github.com/FS-GG/.github/issues/643)). GitHub scans the body
for `close|closes|closed|fix|fixes|fixed|resolve|resolves|resolved` followed by an issue ref and
links the two. It **does not parse the sentence**. A PR that said, in as many words, `It does not
close #422`, **closed #422** on merge — that string contains `close #422`, and the negation is
invisible to the parser. The Projects auto-workflow then stamped the item **Done**, so an open,
unfinished, explicitly-not-done item was closed and stamped with its acceptance criteria unmet, and
the only thing that caught it was a worker disbelieving the `release` output. There is no second line
of defence here: `done --flip` refuses to stamp work that is not *merged*, and this work **was**
merged — it just did not *finish the item*.

**And negation is not the only way to fire a keyword you did not mean.** It needs no negation at all —
only adjacency to an issue number. Narrative past tense (`On merge, GitHub closed #422`), a quoted
example, a `fixes #N` copied out of a log, a deferral (`a follow-up will resolve #N`) — none of them
carries a negator, and every one of them closes an issue. **There is no such thing as a harmless
closing keyword in a PR body.**

So the rule is not "avoid the word not". It is:

> **Say what you close, on a line that says nothing else. Everywhere else in the body, GitHub must
> not be able to bind a keyword to a number.**

```
Closes #643.                     ← a declaration: the whole line, nothing else on it
Closes #1, closes #2.            ← REPEAT the keyword. `Closes #1, #2` closes only #1 —
                                   the bare `#2` is bound to nothing and is silently dropped.
```

Everywhere else, deny GitHub the binding. There are exactly two remedies that work, and a third for
when you must quote a body verbatim:

- **Reword the verb** — *"does NOT complete"*, *"addresses"*, *"supersedes"*, *"closed that issue"*.
  GitHub scans a fixed keyword list; a verb outside it binds nothing.
- **Drop the verb** — `Refs #422.` A bare reference is a link, not a close.
- **Break the adjacency** — quote the number without its `#`: *"and on merge, GitHub closed 422
  anyway"*. What binds is a keyword followed by whitespace and then a *ref*; with no `#` there is no
  ref, so there is nothing to bind.
  **A newline does not break it.** Whitespace is whitespace: `closes` at the end of one line and
  `#123` at the start of the next binds exactly as if they were adjacent, both for GitHub and for the
  gate. Do not reach for a line break to escape a keyword.

> **AND CODE IS NOT A REMEDY. This section used to say it was, and that advice closed an issue**
> ([#683](https://github.com/FS-GG/.github/issues/683)). It read: *"deny GitHub the binding: write it
> as code (`closed #422`) …  Writing the offending string as code, exactly as this paragraph does, is
> not a typographic nicety — it is the rule applying to itself."* It was self-consistent, it was
> confident, and it was **wrong**, because it modelled the wrong parser.
>
> **Two parsers read a PR, and they disagree about code.** The **markdown** parser builds
> `closingIssuesReferences` — the link shown on the PR — and it really does skip code. But the thing
> that **closes the issue** on a squash merge is the **commit message**, and a commit message is
> **plain text**: backticks, fences and indentation are ordinary characters in it. Every reference
> markdown skipped, the commit parser binds.
>
> PR [#681](https://github.com/FS-GG/.github/pull/681) — the PR that *shipped the gate against this
> bug* — followed this advice, wrote its examples in backticks, and **closed #422 for the second
> time**. Its `closingIssuesReferences` correctly said `#643` and only `#643`; #422's `CLOSED_EVENT`
> names the **commit** as the closer. Both records are accurate. They describe different parsers, and
> the destructive one is the one nobody had modelled.
>
> A markdown file **in the tree** — this one — is never parsed for closing keywords, so it may still
> quote the bug in backticks. **A PR body may not.** That distinction is the whole of it.

The `closing-keywords` gate enforces exactly this on every PR, and it is not advisory — it fails the
PR. It now scans the **raw** body, with no exemption for code, because that is what the commit parser
does. It was written against the body of the very change that introduced it, which is how we learned
that the negation-only version of the rule was too weak — and then #681 taught us the same lesson one
level down, which is why the gate models both parsers and the code exemption is gone.

This is the fourth face of one coin, and the org has now hit all four: a keyword in the **title**,
where GitHub never looks, so the link is silently *missing*
([#558](https://github.com/FS-GG/.github/issues/558)); an unclosed code fence that silently *voids* a
real `Closes #N` ([#616](https://github.com/FS-GG/.github/issues/616)); a keyword that *fires when it
was never meant to* ([#643](https://github.com/FS-GG/.github/issues/643)); and a keyword written **as
code** that fires anyway, because the squash commit message is not markdown
([#683](https://github.com/FS-GG/.github/issues/683)). In each, the author's intent and GitHub's parse
disagree, and nothing tells the author. Three of the four were found only *after* the merge that
caused them, and the fourth was found by the PR that shipped the fix for the third.

**`--pr <n>` overrides WHICH pull request, never WHETHER it closed the issue**
([#543](https://github.com/FS-GG/.github/issues/543)). It used to select by number alone, so pointing
it at any merged PR that merely *mentioned* the issue turned the stamp green — reintroducing
[#342](https://github.com/FS-GG/.github/issues/342) through the escape hatch documented for the bug
above. It now applies the same provenance test as the automatic path.

**An epic rolls up from its sub-issue graph, and from nothing else.** A `(j) child of #266` title, a
`Child of #266` comment, a checklist line in the epic's body — all of them look like a link, and
none of them is one. A child that was merely *mentioned* is invisible to `done --flip`, which will
then stamp the epic `Done` over it. That is not hypothetical: an epic completed thirty minutes after
an open child of it was filed ([#325](https://github.com/FS-GG/.github/issues/325)).

So `child` is run **when the issue is filed**, not at close-out — the failure mode is precisely a
worker who moved on. It is idempotent. Two API traps live inside it rather than in your fingers: the
endpoint keys on the child's REST **id** (not its issue number), and `-f sub_issue_id=…` sends that
id as a JSON string, which the API rejects with a 422. It must be `-F`.

As a backstop, `done --flip` refuses to roll up an epic whose **body** declares a child the graph
does not contain, and `fsgg-coord lint` reports the same condition as `EPIC-UNLINKED-CHILD`. The
body's task-list is the epic's second, human-legible record of its children; the two records must
agree, and "all children are Done" must never be a statement about a set already known to be short.

#### What the body counts as a declaration

Only a **task-list line** — `- [ ]` / `- [x]` (`*` and `+` bullets count; `[X]` counts), indented
**at most three spaces** — declares a child, and only its **first** issue ref does. The matcher reads
raw text, not rendered markdown. Three shapes were found live on five `.github` epics
([#345](https://github.com/FS-GG/.github/issues/345)):

- **A bare `#n` resolves against the epic's own repo**, exactly as GitHub renders it. `SDD#109` and
  `Templates#49` have no slash, so they are read as `.github#109` and `.github#49` — a merged PR and
  an unrelated issue. Write cross-repo children as `FS-GG/FS.GG.SDD#109`.
- **The first ref wins, so an aside outranks the child.** `— simulation patterns (ties to ADR-0017 /
  #163) → **FS-GG/FS.GG.Rendering#73**` declares `#163`, not the child that follows it. Put the
  child ref first and let the aside trail. "First" is positional in the raw line, so a `#n` inside a
  code span or a link's *text* still wins — `[PR #243](…/pull/243)` declares `.github#243`.
- **A pull request is not a child.** A sub-issue graph holds issues, never PRs, so a line delivered
  by a PR has nothing to link. Cite it as a **bare** `/pull/` URL —
  `https://github.com/FS-GG/.github/pull/239` — which carries no `#n` token and so declares nothing.
  A bare `(PR #243)`, or that URL wrapped in `[PR #243](…)`, reads as an ordinary `#n`. Since
  [#346](https://github.com/FS-GG/.github/issues/346) both `done --flip` and `EPIC-UNLINKED-CHILD`
  re-resolve an otherwise-unlinked ref and **drop** it once GitHub confirms it is a pull request, a
  genuine same-repo `(PR #243)` no longer wedges the gate. It lingers only in the residual cases the
  probe cannot clear: a number that resolves to an *issue* in the epic's own repo, or one that will
  not resolve at all — 404, network, rate-limit — which is kept fail-closed
  ([#266](https://github.com/FS-GG/.github/issues/266)). The bare `/pull/` URL stays the cleanest
  form anyway: it declares nothing and spends no REST probe.

For the same reason, a spun-off finding that is *not* epic scope belongs in prose beneath the
checklist: any issue ref on a task-list line — bare, qualified, backticked, or an `/issues/` URL — is
a claim that the epic has that child.

> **The one place this gate fails open.** A task-list line indented **four or more spaces** — a
> nested sub-checklist — matches nothing, so it declares nothing. Its child is invisible to both
> `lint` and `done --flip`'s cross-check, and the epic will roll up `Done` straight over it. Keep
> epic children at the top level of the checklist. Tracked as
> [#346](https://github.com/FS-GG/.github/issues/346).

## The fan-out loop

```sh
# scheduler: how many items can run at once, given what is already in flight?
scripts/fsgg-coord batch --repo <r> -n 4

# each worker, independently — named, isolated, and safe against a lost race:
eval "$(scripts/fsgg-coord whoami --mint)"    # MINT one; never invent or copy one (§0). Or let the
                                              # worktree name it (§0 rule 3).
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
  PR is merged **and** the board is `Done`, with automatic epic roll-up — which refuses to fire over
  an epic whose body declares a child the sub-issue graph lacks. `fsgg-coord child <parent> <issue>`
  is what puts a child *in* that graph; nothing else does.
- **The GraphQL budget discipline.** Everything in this protocol is **REST** (a separate 5,000
  *requests*/hr budget) except the board scan `who`/`batch`/`reap` share (~3 GraphQL points). `who`,
  `reap`, and `inbox` add 2 REST reads per in-flight item — bounded by the number of workers.
  `batch`/`take` add 2 REST per candidate examined. No new board schema, no new durable store.

## Setup

- **Install the coordination engine — and KEEP IT CURRENT:**

  ```sh
  dotnet tool install -g FS.GG.Coord.Cli     # provides `fsgg-coord-engine`
  dotnet tool update  -g FS.GG.Coord.Cli     # ...and run this too. A global tool does NOT self-update.
  ```

  **A stale engine is worse than no engine** (#655). A worker without one contributes no evidence and
  says so. A worker with a *superseded* one contributes divergences from a build nobody should trust —
  noise that buries the real findings. `fsgg-coord` carries a floor and simply **refuses** to shadow with
  an engine below it: the run is recorded as a skip, naming the version and how to fix it. Nothing
  breaks, and nothing is quietly wrong.

  This is not hypothetical. Engines before `0.1.1` strip the leading dot from every dotfile path, so
  `.github/workflows/gate.yml` becomes a token that matches no file — it conflicts with nothing, and the
  engine reports an item as **startable while a live claim is holding it** (#649). The shadow caught that
  seven times in one day. Bash was authoritative, so nobody was mis-scheduled; that is exactly what
  shadow mode is for.

  This is **optional and safe to skip** — `fsgg-coord` works exactly as before without it. What it
  buys is the SHADOW (ADR-0034): with an engine present, every `batch`/`next`/`take` is decided by
  **both** the bash client and the typed F# engine, **bash's answer is the one you get**, and any
  disagreement is logged (`fsgg-coord divergence`). Nothing about your run changes — not the answer,
  not the exit code, not the timing you would notice.

  It is worth the one command because the shadow is how the port earns its cutover. Bash stays
  authoritative until the divergence log has been clean across the live fleet for three consecutive
  days, and that log only fills where an engine exists. A worker without one contributes no evidence,
  and the clock does not move.

  A global tool lands in `~/.dotnet/tools`, which is already on `PATH`, so it is found in **every**
  repo with no per-repo setup. (A local `dotnet tool restore` also works — but note a local tool is
  *not* on `PATH`, and `fsgg-coord` reaches it via `dotnet tool run`.)

  **In a receiver you no longer have to do either.** Every receiver already declares the engine in
  `.config/dotnet-tools.json` — Renovate keeps its version current and the kit distributes the file — and
  `fsgg-coord` now **restores it for you** the first time it needs it.

  It did not, and that cost the fleet a day. A manifest is a *declaration*, not an *installation*: until
  something runs `dotnet tool restore`, `dotnet tool run fsgg-coord-engine` exits 1 with *Run "dotnet tool
  restore"…* on **stderr**, so the version came back empty, became `unknown`, and `unknown` is stale by
  design — every scheduling call in all six receivers skipped, blaming a stale engine and telling the
  worker to *update* a tool they had never installed. On 2026-07-14 that was **139 of 147 shadow runs**.
  The kit's answer had been a sentence asking the worker to run the restore, and that sentence had the
  hit rate every other request in this fabric has: zero.

- **Your shadow's evidence is published for you.** The shadow pushes it to the fleet ledger itself, from
  the scheduling call that produced it — and `done --flip` publishes immediately as well. No extra step,
  nothing to remember (#656).

  This used to be a request: *"when your loop is done, run `fsgg-coord divergence --publish`."* Measured
  against a live fleet of 28 workers and 597 compared item-verdicts, it was run **zero times**, by
  anybody, including by the worker who wrote it. **Asking is not a mechanism.**

  The first fix hung the publish on `done` — *"the one command every worker runs when it finishes an
  item."* It is not. An item closed by a **squash-message closing keyword** (#681, #685, #693) is merged,
  closed and board-Done without `done` ever being called, and on 2026-07-14 that was most of them: seven
  issues closed, 218 item-verdicts compared, zero divergence — and **not one row reached the ledger**. The
  fleet gate read the day as *"a day nobody looked."* **A hook on one path is a request that the path be
  taken.** So the publish now hangs on the *shadow*, which is the only path that has, by construction,
  just produced evidence.

  It is **throttled** — at most one REST write per 30 minutes per machine, shared across every worker on
  it, because the hot scheduling loop may not pay the network on every call. Missing a window loses
  nothing permanently: the publish folds your **whole** log, keyed on `(worker, day, engine)` and
  rewritten in place, so the next one carries the missed day up with it. Late is a property of this
  ledger; lost is not.

  It is idempotent, it costs one REST call, and **it can never cost you your done-stamp or your item** —
  it runs after the stamp is earned, and a publish that fails is bookkeeping that failed.

  You can still run it by hand, and should if you are stopping without finishing an item:

  ```sh
  fsgg-coord divergence --publish            # your local log is not evidence until it is published
  fsgg-coord divergence --fleet              # where the FLEET stands: 0 green · 1 red · 3 no verdict
  ```

  The local log lives in `~/.cache/fsgg-coord/`, a cache directory that dies with your container. The
  cut-over criterion is *"zero divergence across the **live fleet** for three consecutive days"*, and a
  worker who shadows and never publishes has moved that clock exactly as far as one who never shadowed
  at all.

- `Status: In progress` and `Blocked by` already exist on the board — **no schema change is
  required**. A repo may add an optional `Paths` text field if it wants touch-sets filterable
  on the board, but the protocol reads the `Paths:` line from the issue body.
- `claim`/`release`/`say` need `issues: write`; the board writes need
  `gh auth refresh -s project,read:project`, as the rest of `fsgg-coord` does.
- To activate the protocol in a product repo, copy the
  [`intra-repo-parallel-work`](../../.claude/skills/intra-repo-parallel-work/SKILL.md) skill
  into that repo's `.claude/skills/` (same as the cross-repo skill). To get the advisory drift
  check, copy `.github/workflows/touch-set-drift.yml` too.
