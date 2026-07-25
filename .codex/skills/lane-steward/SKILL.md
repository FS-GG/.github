---
name: lane-steward
description: Use when a full FS-GG board exposes too few parallel lanes or workers report nothing schedulable. Diagnose over-broad touch-sets and propose narrower declarations without scheduling work.
---

# lane-steward

Your board says forty items. It can absorb two.

```
$ fsgg-coord scan --repo .github | fsgg-coord lanes --text
3 lane(s) — 2 free, 1 occupied or with no startable work.

lane .github#422  (40 item(s), 3 startable)
  → .github#422
    .github#431
    …
  the declarations gluing this lane together:
    …

CEILING: 2 worker(s) can start right now, provably without colliding.
```

Forty items of work, transitively glued into **one lane**, because a handful of `Paths:` declarations
are broader than the work they describe. Fan out five workers and three of them are handed nothing —
and `take` reports an empty queue over a board that is full (#440's shape). That is what you are here
to fix.

**`scan` reads the board; `lanes` decides. The pipe is the whole invocation.** `lanes` is a DECISION
command — it partitions a snapshot on stdin and touches no network, which is what lets it reserve
against exactly what the scheduler does. It takes **no `--repo`**: that flag parses and is silently
ignored, so `fsgg-coord lanes --repo <r>` reads an empty stdin and refuses with *"the snapshot is
empty … a failed read"* — a read it never made. Feed it `scan`, or `--snapshot <file>` (#975).

## The one rule, and it is the whole job

**You may not decide that two items are safe to run together. Ever.**

Safety is *computed* — `fsgg-coord lanes` derives it in the typed core from the same
`TouchSet.conflicts` the scheduler reserves against. If an agent could assert it and got it wrong, two
workers would land in one file, which is the failure this entire protocol exists to prevent (#273,
#461). A wrong lane is not a bad suggestion; it is a corrupted lock.

So your job is the **input**, never the output:

> **You propose a `Paths:` declaration. The tool proves what it implies.**

Every narrowing you apply goes through `fsgg-coord set-paths`, which replaces the old declaration
with the exact narrower set and re-checks it against every live claim. `widen` is only for additive
expansion when work grows into a path the current declaration does not reserve. If a proposed set is
unsafe, the command refuses it. If it is safe-but-wrong, the section below explains why that is the
dangerous case.

## What you are actually looking for

Run this first. It is the whole input:

```sh
fsgg-coord scan --repo <r> | fsgg-coord lanes --json
```

Three things come back, and they are three different jobs. `glue` is nested per-lane inside
`partition`; `unlanable` and `ceiling` are top-level.

### 1. `glue` — the over-broad declarations. **This is the main event.**

For every lane, the tool ranks each token by what removing it would *buy*:

```json
{ "token": "scripts/fsgg-coord", "declaredBy": 31, "splitsInto": 7 }
```

Read that as: **thirty-one items declare `scripts/fsgg-coord`, and if they did not, this one lane
would become seven.** That is five extra workers, and it is one token.

`splitsInto` is the only number that matters. A token declared by twenty items whose removal splits the
lane into **1** is *load-bearing* — the work really is coupled, and narrowing it would be a lie. The
tool only ever shows you tokens where `splitsInto > 1`, so everything you are shown is worth something.

**Now go and read the issues.** For each item declaring the glue token, ask the only question that
matters:

> **Does this item actually touch that path — or did somebody declare the whole file because the work
> lives *near* it?**

An item that adds a new subcommand to `scripts/fsgg-coord` genuinely touches it, and no narrowing is
honest. An item that only edits `docs/` but declared `scripts/fsgg-coord` "because it is about the
tool" is costing the board a lane for nothing. **That distinction is a judgement about the work, which
is why this is an agent's chore and not a script's.**

### 2. `unlanable` with `"chore": true` — items nobody can pick up

- **`no-touch-set`** — no `Paths:` line at all. The item is real work and is **invisible to every
  worker who asks for work**. Propose a touch-set.
- **`unusable-tokens`** — it declares tokens that match nothing (a leading `**/`, a `*` in the middle).
  **Worse than undeclared**, because it *looks* declared: it reserves nothing, so it reads as disjoint
  from everybody (#273). Fix these first.

### 3. `unlanable` with `"chore": false` — **do not touch these**

`declared-none` is `Paths: none`: an epic, a decision item, an investigation whose scope *is* the
question. It is unschedulable **by design** and it is **correct**. An agent that helpfully proposes a
touch-set for an epic has made the board worse and reported that it improved it. The tool tells them
apart so that you cannot confuse them (#496); do not undo that.

## Proposing a change

**Narrow, honestly, and never below what the work touches.**

A touch-set that reserves *less* than the work actually edits is the worst outcome available to you —
worse than the over-broad one you are replacing. `batch` will then hand those files to a second worker
while the first is editing them. The over-broad declaration merely costs parallelism; an under-broad
one costs correctness.

When in doubt, **leave it alone and say so.** A lane you did not split is a cost. A collision you
caused is a defect.

```sh
# The item is yours: replace the broad declaration with the exact narrower set.
scripts/fsgg-coord set-paths <issue> --paths "docs/coordination/parallel-work.md, .claude/skills/pnext-item/, .agents/skills/pnext-item/"

# The work later grows into another path: additive expansion only.
scripts/fsgg-coord widen <issue> --paths "tests/new-surface/"
```

If the item is unclaimed, post the proposal as a normal issue comment so its eventual claimant can
act on it without re-deriving anything. If somebody holds it, address the proposal to that holder
with `fsgg-coord say <issue> --to <worker> '…'`. Use this shape:

> **lane-steward:** this item declares `scripts/fsgg-coord`, which 31 items declare and which is gluing
> this lane into one. Reading the issue, the work is in `docs/coordination/parallel-work.md` and the two
> skill roots — it does not appear to touch the tool at all.
>
> Proposed: `Paths: docs/coordination/parallel-work.md, .claude/skills/pnext-item/, .agents/skills/pnext-item/`
>
> If that is right, the holder should run
> `scripts/fsgg-coord set-paths <issue> --paths "<the above>"`. This replaces the broad declaration;
> `widen` would preserve the glue token and cannot perform this narrowing. If the work *does* touch
> the tool, say so and leave it.

**Do not change an item you do not hold.** The claim marker is the lock; the issue body is not.
Editing a declaration you have not claimed races the worker who has, and last-write-wins silently
clobbers theirs (this is the rule `pnext-item` §1 already states).

For an item **you** hold, or one nobody holds and you are about to work: claim it first, then
`set-paths` to the exact replacement. Use `widen` later only when the implementation expands beyond
that declared set.

## Splitting an item

Sometimes the honest touch-set really is broad, because the item really is two pieces of work. A single
issue that edits the tool *and* the docs *and* the registry cannot be narrowed — it can only be **split**.

That is a proposal about scope, not paths, and it belongs to whoever owns the item. File it as a comment,
name the pieces and the touch-set each would carry, and let them decide. Do not split somebody's issue
for them.

## When you are done

```sh
fsgg-coord scan --repo <r> | fsgg-coord lanes --text      # did the ceiling actually move?
```

**That number is your whole score.** Not the count of comments you posted, not the tokens you narrowed —
the number of workers the board can absorb. If it did not move, the tokens you touched were not the glue,
and the `glue` ranking will tell you which ones are.

## What this chore may never do

- **Never assert that two items are safe together.** That is computed, and it is not yours.
- **Never narrow a touch-set below what the work touches.** An over-broad declaration costs a lane. An
  under-broad one hands two workers the same file.
- **Never propose a touch-set for a `Paths: none` item.** It is an epic. It is correct.
- **Never edit the `Paths:` of an item you do not hold.** The claim is the lock.
