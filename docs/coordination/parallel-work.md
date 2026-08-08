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
| 2 | `$FSGG_WORKER` | same, via the environment — and what §0's mint sets |
| 3 | the **agent harness's session id**, hashed to a memorable name | a fallback; deterministic, no state |
| — | **nothing else. It REFUSES.** | no `--worker`, no `$FSGG_WORKER`, no session: the engine errors rather than invent one |

Rule 3 **warns on every claim**, because it can hand one id to several workers: on Claude Code every
subagent of a session shares its `CLAUDE_CODE_SESSION_ID`, so a fan-out collapses onto one id — the
same-account bug, one level down. (On OpenCode subagents are *child* sessions with their own ids, so
rule 3 is genuinely per-worker there and does not warn. `fsgg-coord` knows the difference per harness;
unknown harnesses are assumed shared, which is the safe default.)

**THE LAST RULE IS A REFUSAL, AND THAT IS A DESIGN DECISION — not a gap.** This table used to carry
two more rules: *the git worktree's name* ("the normal case"), and *generated, persisted per
checkout* ("last resort"). **The engine implements neither** — `Identity.resolve` is the four legs
above and nothing else. Both were the **bash** client's, and ADR-0040's port dropped them
deliberately. The engine says why, in its own words:

> REFUSE rather than invent a shared id. The bash client persists a per-checkout id here; the engine
> does not, because a persisted-per-checkout id is itself a shared id under a fan-out sharing one
> checkout — the exact thing ADR-0027 forbids. Make the caller say who they are.

So a worker with no id gets a **loud error naming the mint**, not a quiet default that might collide.
That is the whole point: every remaining rule is one somebody had to *state*, so no id arrives by
accident.

**Do not reason from the deleted rules.** They generated advice all over this protocol that is still
being followed and is no longer true — [#629](https://github.com/FS-GG/.github/issues/629) is a
worker following rule 3's consequence faithfully and reporting a bug the engine cannot have. If you
are about to write "the worktree names your worker", measure it first: `whoami` from a worktree
returns the **same** id as the checkout it was cut from, because neither `$FSGG_WORKER` nor a session
id is a function of the path.

**A session id is not an identity, and it was never going to be.** It is unique per *session*, not per
*worker*, and the mapping between the two is a property of the harness. See
[agent-session-identifiers.md](agent-session-identifiers.md) for the survey (Claude Code, OpenCode,
Codex CLI, Gemini CLI) and the evidence. If you are fanning out, fan out with worktrees (§2), or set
`FSGG_WORKER` per worker.

What the session id *is* good for is **provenance**: every claim marker records
`harness=<name> session=<id>`, so "which agent transcript took this lock?" is a lookup. That is the
question the incident behind [#255](https://github.com/FS-GG/.github/issues/255) could only answer
with file mtimes and the process list.

The id is stamped where attribution is later needed — the commit trailer:

```sh
git commit --trailer "FSGG-Worker: $FSGG_WORKER"
```

**`claim` does NOT print this line, and this page used to say it did** — *"`claim` prints the exact
commit trailer to use, with your id already filled in. **Use the line it printed**"*. It prints one
line and always has, since the port:

```
claimed <repo>#<n> by worker <id>
```

`grep -rn 'FSGG-Worker' src/` matches nothing. The **bash** client printed the trailer; ADR-0040's
port dropped that output and this instruction outlived it — so a worker was told to copy a line that
does not exist, *and* forbidden to reconstruct one, which leaves no legal move at all.

**`$FSGG_WORKER` is the right answer, and the old reason for forbidding it is gone.** This page used
to reject it because *"`$FSGG_WORKER` is empty for a worker whose id came from the worktree name (rule
3 — the normal case)"*. There is no rule 3: worktree-name derivation does not exist, so that is not a
way `$FSGG_WORKER` can be empty. And §0 **mandates the mint**, which sets it — so for anyone who
followed §0, `$FSGG_WORKER` **is** the id holding the lock, and expanding it is not a shortcut but the
direct read.

If it is empty, you skipped §0 and your id came from the session (rule 3). Do not paper over that with
a trailer: mint one, because a session-derived id is the one every subagent of your session shares.

**`$(git config fsgg.worker)` is still wrong**, and for a reason nothing retired: it reads the id of
*whoever claimed most recently*. `claim` stamps `fsgg.worker` per-worktree only when
`extensions.worktreeConfig` is set, which is **not** git's default, so it falls back to the shared
repo config every linked worktree reads (`fsgg-coord` says so when it happens). A blank trailer loses
the attribution; a borrowed one asserts a false one, which is worse.

Without that, "who edited these files?" is answerable only by mtime forensics, which is where this
protocol's first real incident ended up.

## The rules

<!-- BEGIN GENERATED: fsgg-protocol -->
<!--
  DO NOT EDIT THIS REGION. It is emitted from src/FS.GG.Coord.Core/Protocol.fs by
  scripts/generate-projections, and `projections` in CI fails on any diff.

  This region exists because a rule stated in six documents is a rule that will disagree with
  itself — #485 (startability computed in five places, agreeing in none) and the #502/#531/#551
  family. Edit Protocol.fs and regenerate; a collision here is a rebase, not a decision (#309).
-->

### The rules the scheduler actually enforces

*Generated from the typed core. The engine that decides your item is the engine that wrote this.*

#### `Paths:` is a declaration, and a fenced one is a QUOTATION

Declare the touch-set as a `Paths:` line at up to three leading spaces. A `Paths:` line INSIDE a fenced code block is a quotation of the grammar, not a use of it — the protocol docs quote it constantly. `Paths: none` is a SENTINEL meaning "this item deliberately has no touch-set", and it is not the same fact as having forgotten one.

> **Why:** #277 (a fenced line read as a declaration would let a doc reserve files) and #496 (an epic and a forgotten touch-set rendered identically, so no gate could be written at all — nine items of real work went invisible, and the surface whose job is board health reported `0 error(s)` over a dead queue).

#### The touch-set grammar — it is NOT a glob language

supported: an exact path ('src/Foo.fs'), or a directory prefix ('src/Foo', 'src/Foo/*', 'src/Foo/**'). There is no glob matcher: a leading '**/' or an interior '*' matches nothing — spell the paths out.

> **Why:** #273. Four hand-copied forms of the unmatchable-token predicate existed across two engines. A token that matches no file conflicts with nothing — so an item declaring only such tokens reserves NOTHING, clears every overlap check, and the lock succeeds under exactly the conditions it exists to prevent.

#### Blockers are checked BEFORE the touch-set

The scheduler asks, in order: is the issue closed? is its Status one we hand out? is it BLOCKED? is its touch-set usable? is it HELD? does it overlap work in flight? The first answer that is not "no" is the verdict, and it is the one sentence the worker reads.

> **Why:** ADR-0038. A blocked item cannot be started whatever its touch-set says, so reporting "no `Paths:` declared" sends a worker to fix something that leaves them exactly where they were. And blockers are FREE — they are board facts already in the scan — where a touch-set costs a body READ per item, on the budget that dies first (#418). That is why bash never fetched a blocked item's body, and how an unreadable one could silently cease to exist.

#### A MERGED blocker is RESOLVED; an unreadable one BLOCKS

`Blocked by` is a Projects v2 board FIELD, not a body line — the same medium split as `Paths:` and its own fence rule, in reverse: `Paths:` lives in the body and a `Blocked by` FIELD is the only place this dependency is recorded. A `Blocked by:` line written into the issue BODY is inert: nothing that clears a blocker reads the body, so it looks like a declaration and does nothing. Write the edge with `set-field <ref> "Blocked by" <ref>`. Once the edge is on the field: `Blocked by` clears on CLOSED **or MERGED**. It does not clear on OPEN, on a blocker whose state could not be read (unverifiable), or on prose that is not an issue ref at all (unparseable) — all three BLOCK.

> **Why:** #476: `Blocked by` may name a PULL REQUEST, whose state is OPEN | CLOSED | MERGED. A rule clearing only on CLOSED unblocks when the blocking work is ABANDONED and blocks forever once it is FINISHED — the gate opened precisely when the work was thrown away and shut precisely when it was done. And #266/#421: "I could not look" is not "I looked and it is fine"; prose in a dependency field is not permission. And .github#1933: two agents independently read a `Blocked by:` BODY line, found no FIELD edge, and concluded there was none — one filed a false defect (.github#1931) and withheld from promoting a row only because a third worker caught the contradiction by hand. Nothing in the operator-facing docs said which medium held the fact; only the `.fsi` comments did, and filers do not open those.

#### The claim lock is a comment-order CAS, and the ASSIGNEE cannot hold it

A claim is an `fsgg:claim` marker COMMENT, and the lowest live marker id wins. GitHub issues comment ids from one server-side sequence, so "lowest live marker" is a total order every racer observes identically. The GitHub ASSIGNEE cannot be the lock, because N agents share one account. That total order is over MARKERS, and it separates WORKERS only while their ids are DISTINCT: an id two workers share is an id this lock cannot separate, and `release`, `heartbeat`, `say` and `inbox` then act on one another's claims. So a worker id is MINTED, never chosen — a worker asked to pick one is not a random source.

> **Why:** ADR-0027, and #419 for the distinctness half: agents asked to invent an id converge on the same corner of the name space, and this board carried FOUR `finch-*` workers at once — every one of them lifted from the single example id that then sat in the recipe. The attractor is the WORD, not the suffix, which is why the remedy is a mint rather than a reminder to be careful, and why #532/#551/#570 had to remove the pasteable id from the docs twice by hand before a gate asserted it. The lock lives on REST, and the invariant it serves — a lock may never live on the budget that dies first — is unamended. What inverted is WHICH budget that is, so this rule no longer asserts a standing answer. #418 measured GraphQL dying first (five workers looping `take` drained 5,000 pt/hr in ~15 minutes), and REST was chosen as the survivor. #895 measured the reverse, twice on 2026-07-16: REST core hit 0/5,000 and took `claim`/`take`/`who` down with it, while GraphQL stayed healthy through both — 3,639/5,000 at the first of them. This rule used to state "GraphQL is the first budget to die" as standing fact, and that premise is what kept regenerating the doctrine that caused the inversion — a recipe steering every worker's reads onto REST to save GraphQL points, on one shared account, spending the lock's own budget to save 7 points of 5,000. #895 decided (2026-07-17) that the lock STAYS and the DOCTRINE moves (#968): REST is metered per request and cannot be batched, so under fan-out it is structurally the scarcer budget with no lever to pull, where GraphQL batches 100 nodes to a query. Discretionary reads belong on GraphQL; REST carries the lock, which has no alternative.

#### The lease is a WINDOW, and an unknown age says so

A claim's lease is 120 minutes by default (`FSGG_CLAIM_LEASE_MIN`), and `heartbeat` renews it only while it is LIVE. Past it the claim is REAPABLE — not free: only `reap` may break a lock, and an item's touch-set stays reserved until it does. An EXPIRED lease cannot be renewed in place; the holder must re-claim. Evidence that the work is alive — an open `item/<n>-*` PR — withholds the item from `take` and REFUSES a `reap`, but it does not revive the lease. A claim whose age cannot be read reports `lease unknown`, never a window.

> **Why:** #428 ("nothing schedulable" and "queued behind a claim held by <w>, lease frees in ~96m" are the same fact and two completely different operator instructions — the first reads as an empty queue and sends a worker home) and #440/#488 (inventing "frees in ~120m" from a missing timestamp is a confident-but-unfounded sentence, which is the class both were closed for). And the lease is a TIMER, which is why it never decides alone: it cannot see a REST outage, and `heartbeat` is REST, so an outage on the lock's budget spends a lease nobody can renew and silently reads as abandonment (#976, ratifying that the fleet stops there rather than making the clock outage-aware). What answers instead is evidence — an open `item/<n>-*` PR (#581), or a liveness probe that failed and therefore fails closed (#266). Expiry is EVIDENCE of abandonment, never proof.

#### A read that did not happen may never render as a confident answer

An error, an empty result, and a legitimate "no" are three different facts. A failed board scan is not an empty board; a failed marker read is not an unheld item; an unread issue body is not an undeclared touch-set. Every one of them fails CLOSED and says which it was.

> **Why:** Epic #266, which has 51 children. #461: a failed claim scan read as "nothing is claimed", so `take` handed a held item to a second worker. #344: a rate-limited scan exited 0 with no verdict, and a worker read "nothing to do" off a board it never managed to read.

### What the scheduler can tell you, and nothing else

One total function returns one of these. There is no other answer, and there is no silent no —
an unreachable answer is not a negative one.

- **`startable`** — Nothing holds it. It can be claimed now.
- **`issue-closed`** — The issue is CLOSED while the board still shows it open. The issue's state is the WORK; the board column is a PROJECTION of it. When they disagree, the issue wins — run /check-board.
- **`wrong-status`** — Its board Status is not one a scheduler hands out (or it has none at all, which makes it invisible to every scheduler and is a bug, not a decision).
- **`blocked-by`** — A `Blocked by` entry is unresolved. CLOSED and MERGED resolve; OPEN, unverifiable and unparseable all BLOCK.
- **`awaiting-human`** — `Blocked on: human/...` — a HUMAN must act first, whatever the `Paths:` line records, so an agent cannot make the call the item exists to escalate (#918). `human/decision` is unschedulable until a human CHOOSES; `human/action` becomes startable the moment a human action (e.g. a scope grant) lands, but not before. Which one rides on the verdict's `humanBlock` detail.
- **`awaiting-delivery-route-decision`** — The mandatory agent-authored delivery-route receipt is missing, stale, malformed, or unreadable. Re-evaluate the item; the engine never infers a route from checklist facts.
- **`no-touch-set`** — No `Paths:` line at all — an OMISSION. The item is real work and it is invisible to every worker who asks for work. Declare one, or `Paths: none` if it truly has no touch-set.
- **`deliberately-no-touch-set`** — `Paths: none` — a decision somebody made. An epic, a decision item, an investigation whose scope IS the question. Unschedulable BY DESIGN, and correct.
- **`unusable-touch-set`** — The declaration contains token(s) that can match no file, so they reserve NOTHING — and files nobody reserved are invisible to every other worker's overlap check.
- **`held-by`** — A live claim marker holds it. Wait out the lease, or talk to the worker.
- **`held-by-live-work`** — The lease EXPIRED but the work did not: an open `item/<n>-*` PR is the worktree protocol's own artifact, and it outranks a timer. Not offered; its touch-set stays reserved.
- **`item-pr-open`** — No claim marker governs it, but an `item/<n>-*` PR is already OPEN on its branch — an implementation is in flight whether or not anyone claimed it. Not offered: claiming it would duplicate work that is already written (#651).
- **`overlaps-in-flight`** — Its files collide with work already in flight. The holder and its lease window are named, because "nothing schedulable" and "queued behind a claim that frees in ~96m" are the same fact and two completely different instructions.
- **`undetermined`** — WE COULD NOT DECIDE — and that is never a silent no. An unreachable answer is not a negative one. This is the case whose absence made every other case a lie waiting to happen.

<!-- END GENERATED: fsgg-protocol -->

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

That rule binds the **readers** too, and for a while it did not. `who`, `reap`, `inbox` and `batch`
all take their view of in-flight work from one place, and that place used to *ask the column* which
items to look at — so a claim whose `Status` flip failed, or a claim on an item that is not on the
board at all, was invisible to every one of them (FS-GG/.github#257). `reap` could not collect a dead
worker off such an item, so its lease stopped self-healing; and because `batch` reserves the touch-set
of everything in flight, an off-board claim's `Paths:` were never reserved — the scheduler would hand a
second worker an item overlapping the subtree the first was editing.

So the scheduler's in-flight set is the **union** of two things: the board's `In progress` column, and
every open issue that **carries a live marker**, wherever the board thinks it sits. Only the column can
report `UNCLAIMED` (a markerless item is otherwise just an issue); only the marker can report a lock.

**`overlap --active` / `widen` / `set-paths` do not read the column at all**, and that is a stronger
rule rather than an exception to this one (FS-GG/.github#1779). Those three answer *"may I edit this
file?"*, and a wrong `DISJOINT` there is **final** — nothing downstream re-decides it, because **there
is no CAS on a file**. The column can disagree with a live marker in four ways, and `claim` exits green
on all of them: the write **landed** (and a cached read shows the old value), was **deferred** on an
exhausted budget, **failed permanently** (never queued, by #510 — so nothing will *ever* write it), or
reported **not-on-board** (there is no row to write). A candidate set derived from rows misses the last
two by construction. So the scan lists the repo's **open issues**, compares `Paths:` tokens first, and
reads a marker only for a row that actually collides — no board query, no cache tier, no deferral
queue. It is also **cheaper**: measured live 2026-07-28, 24–31 GraphQL points a call → **0**.

Two consequences worth knowing, both deliberate. A claim on a **closed** issue reserves nothing here
(the scheduler agrees — it sweeps a closed candidate without reading its marker). And the scheduler
still reserves a *markerless* `In progress` row, which this scan cannot see because it never reads the
column — so `take` can refuse a surface `widen` calls disjoint (FS-GG/.github#1792).

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
share a working tree. Construct the command yourself — `claim` prints only the claim line, not this
(the bash engine printed it; ADR-0040's port dropped that output).

```sh
git fetch origin                                                # NOTHING else does — see below
git worktree add ../<repo>-<n> -b item/<n>-<slug> origin/main
# ...work, commit, push, PR into main...
git worktree remove ../<repo>-<n>
```

**The fetch is not optional either, and it is the half that survives naming the base.** `git worktree
add` does not fetch: it resolves `origin/main` against the *local* remote-tracking ref, which advances
only when something in this checkout fetches. The same premise that makes the base ref necessary makes
the fetch necessary — N workers are merging into `main`, so **the better this protocol works, the
staler your `origin/main` is when you start** (three commits behind after 15 minutes on a two-worker
board; five during a single item on a busy one). And a stale base is worse than an old one: you build
and test from a tree that is internally consistent and simply *old*, so it reproduces already-fixed
bugs faithfully while every gate goes green — none of them has an opinion about whether the tree is
current (.github#622).

The base ref is **not optional**. `git worktree add -b <new>` with no commit-ish branches from the
shared checkout's `HEAD` — and the premise of this protocol is that N workers pass through that
checkout, so its `HEAD` is routinely whatever unmerged branch the last one left. The item's PR then
carries that branch's commits as well as its own, and nothing on the path warns: the touch-set
declaration was honest, the *branch base* was not. At best `verify-paths` reports the resulting drift
as an advisory — and only while the sibling branch is still unmerged. Once it lands on `main` first,
GitHub computes the PR's diff against the new base, the foreign commits disappear from it, and nothing
sees them at all (.github#319).

Integration is by PR into a green `main`. Agents should prefer the harness's built-in worktree
isolation (`isolation: "worktree"`), which is the same discipline managed for them. The worktree
isolates the *tree*, and nothing else — it does **not** supply the worker id. This line used to say it
did ("§0 rule 3"), and that rule does not exist: your id comes from §0's mint, and a worktree cannot
change it.

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
spends only parallelism. `widen` and `set-paths` rewrite the same lines the reader reads, collapsing
duplicates to one, so neither can patch a quotation and leave the real declaration standing.

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
Regenerate it and commit it — `verify-paths` asks `repos.sh relock --list` what it emits and reports the
lock under `regenerated (expected):`, apart from the drift you are being asked to act on, so there is
nothing to explain in the PR (ADR-0044, FS-GG/.github#498).

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
re-check. `widen` adds the requested tokens to the existing normalized union, preserving every earlier
declaration and making repeated calls idempotent. It also does the part a worker cannot do alone —
telling the workers it now collides with, on their own items:

```sh
scripts/fsgg-coord widen <issue> --paths "src/Scene/**, src/Audio/**"
```

It exits non-zero on a collision. Stop editing the shared paths until it is resolved.

Replacement is a different operation because it can hand paths away. Use the explicit command when
the complete declaration is known — most notably to narrow an over-reservation:

```sh
scripts/fsgg-coord set-paths <issue> --paths "src/Scene/**"
```

It carries the same held-lock, validation, collision re-check, and notification gates. Keeping the
operations separate prevents a late additive `widen` from silently deleting paths declared earlier
(FS-GG/.github#1377).

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
eval "$(scripts/fsgg-coord whoami --mint)"    # MINT one; never invent or copy one (§0). There is no
                                              # second way: no mint, no id (the engine REFUSES).
scripts/fsgg-coord take --repo <r>            # pick + claim the next SCHEDULABLE item, retrying on a lost race
git fetch origin                              # NOTHING else does — the base is otherwise the PAST (§2)
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

- **The coordination client IS the typed engine now (ADR-0040 Phase D).** `scripts/fsgg-coord` is a
  ~40-line shim (ADR-0034 §4.4) that resolves the compiled `fsgg-coord-engine` and `exec`s it — there is
  no bash implementation left and no shadow, so the engine must be present to coordinate at all. The shim
  resolves ONE engine, in this order, and fails loudly if it finds none (never a silent no-op, #266):

  1. `FSGG_COORD_ENGINE_BIN` — an explicit path, honoured or refused, never fallen back from.
  2. a **repo-local source build** — `.github` alone builds the engine from source, and never from the
     feed (ADR-0034 decision 2), so fixing coord never requires publishing coord. Where a source build
     exists it is the **authority**, and it outranks both packaged forms below.
  3. a global tool on `PATH` — `dotnet tool install -g FS.GG.Coord.Cli`.
  4. a **local manifest** — `.config/dotnet-tools.json` + `dotnet tool run`; the shim runs
     `dotnet tool restore` for you the first time (a manifest is a *declaration*, not an installation, #655).

  **The source build used to be listed LAST — beneath both packaged forms — and the shim resolved it that
  way** ([#1018](https://github.com/FS-GG/.github/issues/1018)). This list stated decision 2's invariant in
  item 4 while item 2 contradicted it: in `.github`, a **feed** build beat the authoritative source, so the
  one repo that never depends on the feed silently did, and a repair sitting in `src/` never ran. It falsely
  closed epic 889 with the guard that refuses exactly that built and present
  ([#1005](https://github.com/FS-GG/.github/issues/1005)), and it made the shim's own fixtures post 2 real
  claims to this board ([#1008](https://github.com/FS-GG/.github/issues/1008)).

  **In a receiver the order below item 1 is unchanged**, and structurally so: the condition is the source
  build's *existence*, not a repo name — only the repo that owns coord's source can have one — so a receiver
  resolves exactly as it always did. If you deliberately want the packaged engine inside `.github`, item 1
  outranks the source build and is the way to say so:
  `FSGG_COORD_ENGINE_BIN="$(command -v fsgg-coord-engine)"`.

- **In a receiver you install nothing.** Every receiver already declares the engine in
  `.config/dotnet-tools.json` — Renovate keeps its version current, the kit distributes the file — and the
  shim restores it the first time it needs it. The kit used to ask the worker to run the restore; asking
  is not a mechanism (measured hit rate: zero), so the shim does it for you (#655).

- **Outside a receiver, install it globally — and KEEP IT CURRENT:**

  ```sh
  dotnet tool install -g FS.GG.Coord.Cli     # provides `fsgg-coord-engine`
  dotnet tool update  -g FS.GG.Coord.Cli     # ...and run this too — a global tool does NOT self-update.
  ```

  A global tool lands in `~/.dotnet/tools`, already on `PATH`, so it is found in **every** repo with no
  per-repo setup. Keeping it current matters because the engine **is** the scheduler now: engines before
  `0.1.1` strip the leading dot from every dotfile path, so `.github/workflows/gate.yml` matches no file,
  conflicts with nothing, and the scheduler reports an item **startable while a live claim is holding it**
  (#649). There is no longer a bash answer behind it to stay authoritative — a stale engine simply
  mis-schedules.

- `Status: In progress` and `Blocked by` already exist on the board — **no schema change is
  required**. A repo may add an optional `Paths` text field if it wants touch-sets filterable
  on the board, but the protocol reads the `Paths:` line from the issue body.
- `claim`/`release`/`say` need `issues: write`; the board writes need
  `gh auth refresh -s project,read:project`, as the rest of `fsgg-coord` does.
- To activate the protocol in a product repo, copy the
  [`intra-repo-parallel-work`](../../.claude/skills/intra-repo-parallel-work/SKILL.md) skill
  into that repo's `.claude/skills/` (same as the cross-repo skill). To get the advisory drift
  check, copy `.github/workflows/touch-set-drift.yml` too.
