# GraphQL budget & the `fsgg-coord` client

The [`Coordination` Projects v2 board](README.md) is the sequencing layer for all cross-repo work
(ADR-0001). **Projects v2 is GraphQL-only** — there is no REST surface for it — so every board read
and write spends from GitHub's GraphQL rate limit. Under sustained cross-repo coordination that
budget is the binding constraint, so this repo ships a thrifty client, [`scripts/fsgg-coord`](../../scripts/fsgg-coord),
and the skill routes board work through it.

## The client is the only GraphQL principal

**No skill, doc, recipe, or agent may call `gh api graphql`, `gh project`, `gh issue view/list/create/edit`,
or any `gh pr` subcommand.** Every board and issue operation goes through `fsgg-coord`, or over REST.
Gated by `graphql-monopoly` (`.github#587`).

This is not hygiene. The budget is **shared by the whole fleet** — N agents, one account — and the
client is the *only* thing that can meter it, cache against it, and **queue** behind it. A recipe
that reaches past the client is an unmetered principal on a budget whose exhaustion silently
corrupts the board, which is the failure this whole document is about.

It has bitten twice, both times in the command least able to afford it:

- [#528](https://github.com/FS-GG/.github/issues/528) — `pnext-item` §5 was GraphQL-only, so a worker
  who followed the recipe **could not land the work they had just finished**. Finished, green,
  reviewed, unmergeable.
- [#538](https://github.com/FS-GG/.github/issues/538) — `check-board` §3 resolved blockers over
  GraphQL: **the reconciler drained the very budget it needed to do its job.**

| instead of | use | cost |
|---|---|---|
| `gh project item-add` | **`fsgg-coord add <issue>`** | metered, cached, idempotent |
| `gh project item-list` | `fsgg-coord ready` / `next` | 6 pts → ~0 |
| `gh project item-edit` | `fsgg-coord set-field` | and it **queues** when the budget dies |
| `gh issue view` / `list` | `fsgg-coord issues <repo>` | 2 pts → **0** (REST + ETag) |
| `gh issue create`, any `gh pr …` | `gh api … repos/<o>/<r>/…` | GraphQL → **0** (REST) |

**`add` exists because of this rule.** Every recipe used to say `gh project item-add`, and the tool's
own "not on board" message said it too — so the monopoly was unenforceable until the client could put
an item on the board. A rule you cannot obey is not a rule, it is a reprimand.

Two things are deliberately *not* in scope. **Prose that warns you off a command** is the opposite of
a violation — the cost table below exists to say "never use `gh project item-list`" — so the gate
only reads *runnable lines inside a fence*. And **workflows** authenticate as `GITHUB_TOKEN` or an
App installation, which has its own rate limit and does not spend the workers' shared budget.

## The one fact that dictates the fix

GitHub's GraphQL **primary** limit is **5,000 points/hour**, it is **shared by every worker** (N agents
authenticate as ONE account), and a request costs

```
cost = max(1, nodes_requested / 100)
```

— node count, **but never less than 1 point per request**. That floor is the whole subtlety, and it
cuts in opposite directions for reads and writes:

- **A node-HEAVY read is billed for its nodes.** Aliasing five such queries into one request pays the
  same total node cost and saves nothing. This is why `gh project item-list` is a trap (below): its
  cost grows with the board, and no amount of batching helps.
- **A CHEAP write is billed the floor.** A Projects v2 field mutation returns ~1 node, so it costs
  **1 point — the minimum — no matter how many you alias into one document.** For writes, therefore,
  **cost tracks REQUEST COUNT**, and batching is a real primary-budget lever. Measured, five
  aliased no-op `Status` mutations cost the same **1 point** as one:

  ```
  1 mutation                ->  1 pt
  5 aliased in ONE document ->  1 pt
  ```

  A placement pass sets ~6 fields per item; sent one-per-request that is 6 points an item, aliased it
  is 1 ([#448](https://github.com/FS-GG/.github/issues/448)).

> **This section used to say "batching does nothing for the primary budget", flatly.** That is true
> only of node-heavy reads. It is false for mutations, and stating it unconditionally cost us a real
> decision: it was cited to talk a worker *out* of building `set-field --batch`, the one optimisation
> that would have cut the write path ~6× ([#447](https://github.com/FS-GG/.github/issues/447),
> [#421](https://github.com/FS-GG/.github/issues/421)).

- **The levers that lower primary consumption:**
  1. **Don't re-fetch static data.** Field ids, single-select option ids, and the project
     number/node-id are stable for the life of the field. Re-introspecting them every session is
     pure waste.
  2. **Ask for fewer nodes.** Resolve one issue's board item via `issue -> projectItems` (a handful
     of nodes), never by scanning the whole board's `items x fields` (cost grows with the board).
  3. **Move non-Projects reads onto REST.** Issues, PRs, comments, and labels are all REST-available.
     REST is a **separate** 5,000-**requests**/hr budget (1 point each regardless of payload) and
     honors ETags, so an unchanged list returns **304 at zero cost** and never touches GraphQL.
     **REST is not the safe budget, and moving a read here is not the end of the story** — measured
     2026-07-16, core sat at 0/5,000 and 403'd every read while GraphQL still had 3,639 points. This
     doctrine is what drains it. Which is why the next lever exists.
  3b. **Revalidate a REST read you make repeatedly.** A 304 is *free* and it is *not stale*: the server
     is asked every time and answers that what you hold is current, so it returns exactly what the
     unconditional read would have. That is why it may go where the 90s scan cache may not. It is worth
     the most in `landable --wait`, which polls three REST paths up to 30 times per wait, per worker, on
     every item — and a poll that finds no change is precisely the 304 case.
     **But an ETag only ever stands for PAGE ONE.** `Transport.Send` merges pages; the validator does
     not. A collection that grows a page while page one stays byte-identical would 304 over everything
     past it — a partial read wearing a complete one's clothes ([#461](https://github.com/FS-GG/.github/issues/461)),
     deciding a merge. So the engine memoises a page only with **headroom** (fewer items than
     `per_page`), which makes that unreachable: any growth must rewrite page one. See `Reads.memoisable`
     — and note the naive guard ("don't cache a response that paginated") does *not* work, because the
     unsound case is the one where the pagination never runs.
  4. **Spend a read once per *window*, not once per *worker*.** The budget is shared, so the same
     board scan performed by five workers costs five times as much and tells them the same thing.
     Cache it (`FSGG_COORD_SCAN_TTL_SEC`, 90s) and they pay for one between them ([#418](https://github.com/FS-GG/.github/issues/418)).
  5. **Queue a write the budget refused; never drop it.** Exhaustion takes the board writes with it,
     and a swallowed write is what makes the board lie (below). Persist it and replay it when the
     budget returns — `fsgg-coord flush` ([#418](https://github.com/FS-GG/.github/issues/418)).
  6. **Alias the WRITES.** Cheap mutations are billed the 1-point floor per *request*, so N field
     writes sent as N requests cost N points and the same N aliased into one document cost **1**.
     Reads cannot be batched away; writes can ([#448](https://github.com/FS-GG/.github/issues/448)).
- **When it runs out, it takes the WRITES with it — which is how the board starts lying.** This is not
  a hypothetical: `claim` holds its lock in REST *comments*, so under exhaustion the lock is taken and
  the `Status: In progress` write is refused. Swallow that and the board says `Backlog` while a worker
  holds the item, `next` hides it from everyone else, and `/check-board` reports it later as
  CLAIM-STATUS-LAG. **The protocol's failure mode is load-dependent: the more you fan out, the more the
  board lies** — and fanning out is the point of the protocol. So exhaustion is a *named condition*
  here, never a generic failure: see [Degrading, not dying](#degrading-not-dying).

`fsgg-coord` is those levers made concrete. It is a thin `gh` wrapper — no daemon, no state
beyond a JSON cache of **ids only** (never field *values*, and never a **lock**), so the worst a stale
cache can cause is "board schema changed", fixed by `fsgg-coord bootstrap --refresh`.

## What things actually cost

Measured against the live 640-item board, not estimated — and you should re-derive any row before you
trust it. **But measure it with the right instrument:**

```sh
FSGG_COORD_DEBUG=1 fsgg-coord batch --repo .github 2>&1 >/dev/null | grep 'graphql cost='
```

Every query `fsgg-coord` sends selects `rateLimit { cost }`, so `FSGG_COORD_DEBUG=1` reports what the
server charged **that query** — attributable, and unaffected by anyone else.

> ⚠️ **Do NOT measure by diffing `gh api rate_limit` before and after.** The budget is *shared*: every
> other worker's spend lands inside your measurement window and is billed to your command. That is not
> a small effect — it produced a "51-point `batch`" that is really **14**, and a claim that this
> document's read costs were wrong by 3–25× when they were fine
> ([#447](https://github.com/FS-GG/.github/issues/447)). A shared meter cannot attribute. If you have
> no per-query cost (mutations cannot select `rateLimit`), take the **minimum over several trials** —
> contention only ever adds.

| Call | GraphQL | Note |
|---|---|---|
| `fsgg-coord ready` / `next` (cold) — the whole 640-item board | **~7–13 pts** | 7 pages × 1 pt. The thrifty scan. |
| `fsgg-coord next` / `take` (warm, within the 90s TTL) | **0 pts** | Served from the shared scan cache. |
| `gh project item-list --limit 5` — **five** items | **6 pts** | Nearly the price of scanning all 640 the thrifty way. **Never use it.** |
| `gh issue list` / `gh issue view` | **2 pts each** | `gh` prefers GraphQL. Under a fan-out these are not rounding errors. |
| `fsgg-coord issues <repo>` (REST + ETag) | **0 pts** | Different budget; a 304 costs nothing at all. |
| `gh issue edit --add-assignee` | **4 pts** | Which is why `claim`/`release` use the REST assignees endpoint instead. |
| `gh api repos/…` (any REST read/write) | **0 pts** | Including `gh api` PR creation and issue comments. |
| `fsgg-coord budget` | **0 pts** | `rate_limit` does not meter itself. Free to check, so check. |

## What the client does

| Command | Lever | Effect |
|---|---|---|
| `bootstrap [--refresh]` | (1) | Introspect project id + field/option ids **once** into a user-level cache (`~/.cache/fsgg-coord`). Refresh is only needed when the board's *schema* changes. |
| `board` / `field-id` / `option-id` | (1) | Serve ids from cache — **zero** GraphQL calls. |
| `item-id <issue>` | (2) | Resolve an issue's board item via `issue -> projectItems`, pick the matching board, cache it. One narrow call, then free. |
| `set-field <issue> <Field> <Value>` | (1)+(2) | Resolve project/field/item/option ids from cache and run **one** mutation, auto-routing by the field's `dataType` (single-select / date / number / text / iteration). No per-write introspection. **One mutation per invocation, so N fields on one item cost N points today** — aliasing them into a single document (lever 6, worth ~6× on a placement pass) is **not yet implemented**: [#448](https://github.com/FS-GG/.github/issues/448). |
| `issues <repo> [--label L] [--jq E]` | (3) | List issues over **REST** with a stored **ETag**; an unchanged repeat 304s to cache. `--jq` projects the payload to trim what you read back. |
| `ready [--repo R] [--status S] [--phase P] [--all]` | (4) | List the **actionable** board items (not `Done` by default). Projects v2 has no server-side item filter, so this is a full scan — but it selects only three fields per item (`Status`, `Phase`, `Blocked by`) via `fieldValueByName` (a **resolver** field, no node multiplication), not the `fieldValues(first:100)` nested inside `items(first:N)` that `gh project item-list` pays (**O(items × 100) ≈ 2,500 pts**). A 100-item page costs ~1 point. `--repo` takes a registry short-id (`sdd`/`rendering`/`governance`/`templates`/`.github`), an `owner/repo`, or a literal repo name. |
| `next [--repo R] [--ignore-blocked] [--fresh]` | (4) | Print the one most-startable item — the first `Ready`, else the first `Backlog` — optionally scoped to a repo. Items whose `Blocked by` refs are still open (or cannot be verified) are **skipped**, with the reason on stderr; `--ignore-blocked` restores the unfiltered pick. Blocked-awareness is **free**: the blockers resolve against the same scan (which already carries every board item's `state`), so no per-blocker lookup is paid. A **scheduling** read, so it serves the shared 90s scan cache — the second worker to ask inside the window pays **0 pts**. `--fresh` forces a rescan. |
| `lint [--repo R] [--json] [--strict]` | (4) | Assert the board's **epic invariants**: no **open** `[epic]` with zero sub-issues (the orphaned epic — a closed childless epic is finished work, not an orphan), none the board calls `Done` over a still-open child, none with more children than the scan can see. Exits non-zero on any error; `Status: Done` on a still-open issue is a NOTE (fatal only under `--strict`). Same paginated full scan as `ready` — 1 point per 100-item page — but it also selects each epic's `subIssues`, which raises **nodeCount** (~200 → ~10,100 per page) while leaving cost at 1. That buys every child's state in the same pagination, where the alternative is one follow-up query per epic. |
| `who` / `reap` / `inbox` / `batch` / `take` | (3)+(4) | All read "what is in flight" from one place, and because **the marker is the lock**, that set is found by reading markers — not by trusting the board's `In progress` column, which `claim` writes best-effort. Cost: the same one board scan as `ready`, plus **one paginated REST issue-list per repo** (which carries each issue's body, so a touch-set is free), plus **one comments read per candidate**. Candidates are pruned soundly on `comments > 0` — a claim marker *is* a comment, so a zero-comment issue cannot hold a lock. This list deliberately does **not** reuse the ETag'd `issues` command: a 304 serving a pre-claim `comments: 0` would hide a live marker — **a lock may never be read from a cache**. (This clause used to add "that asks for one page of 100" as a second reason. It was true of the bash client and is **not** true of the engine: `Transport.Send` follows `Link: rel=next` and merges, so `issues` paginates. The rule stands on the lock alone, which is the reason that was always load-bearing.) Measured on `FS-GG/.github`: 4 GraphQL + 3 REST for one repo, 12 REST across all of them. Known bound: without `--repo`, the repos scanned come from the board, so a claim in a repo with zero board items needs an explicit `--repo`. |
| `overlap --active` / `widen` / `set-paths` | — | The **#353 collision scan**, and since [#1779](https://github.com/FS-GG/.github/issues/1779) it reads **no board at all** — so it costs **zero GraphQL**, warm cache or cold. The candidate set is `Reads.openIssues` for the item's own repo (one paginated REST call, bodies included and free), the `Paths:` tokens are compared **purely**, and a **comments read is paid only for a row whose tokens actually collide** — not per `In progress` row, which is what the row above pays. Measured on the live Coordination board, 2026-07-28, either side of one `overlap .github#1688 --active` (74 open issues, 5 rows `In progress`): **24/27/31 GraphQL points before, 0 after**; REST was 1 issue body + 1 issue list + 1 marker per colliding row. Why it reads no column: `claim` exits green with the column unwritten in four different ways (`statusWrite: written | deferred | failed | not-on-board`), and a false `DISJOINT` is final — **there is no CAS on a file**. |
| `flush [--dry-run]` | (5) | Replay board writes an exhausted budget refused. **Every** board write queues rather than losing it (#510) — but **nothing drains the queue on its own**: no board write flushes as a side effect, so `flush` is the only thing that replays one, and you must run it. `--dry-run` lists what is queued without replaying. Both check the queue before touching the board, so an empty queue and a dry run cost **zero** GraphQL. |
| `budget` | — | Print the **GraphQL** meter, the queued-write count, and a **warning while there is still budget left to act on**. **It does not report REST, and must not be read as evidence about it** (#907): REST is unmetered here, and the budget that died is invisible to a worker who checks this and sees green — [#266](https://github.com/FS-GG/.github/issues/266)'s signature. The obvious repair — read `/rate_limit` — is ruled OUT, and this cell used to recommend it: on this account that endpoint **disagrees with reality**, reporting `core: 2431/5000` (#894) and `core: 2320/5000` (#907) while every real request 403'd with `remaining: 0`, naming a *different reset instant*. It is free to call and wrong to believe, which is worse than silence. The honest readings of REST are the 403's own `X-RateLimit-*` headers and the engine's `EX_RATE` message, which names the budget that actually died (#897). |

## Scheduling reads vs. truth reads — where the 90s cache line is drawn

The scan cache is what makes a fan-out affordable, and it is only safe because of a distinction the
protocol already makes: **the board is a schedule; the claim marker is the lock.**

- **Scheduling reads — `next`, `take`, `batch`-via-`take`** — serve a scan up to `FSGG_COORD_SCAN_TTL_SEC`
  (90s) old, shared across every worker on the machine. Stale means at worst that two workers reach for
  the same item — and the claim CAS, which reads its markers over **REST, never from a cache**, decides
  who gets it. The loser retries with `--fresh`. **Staleness costs a retry; it cannot cost a double claim.**
- **Truth reads — `ready`, `lint`, `who`, `reap`, `overlap --active`, and the `/check-board` snapshot** —
  always scan fresh. Their job is to say what is true *now*, and a reconciler that reports drift which
  was already fixed is worse than no reconciler.

Two rules keep the cache from eating the [#344](https://github.com/FS-GG/.github/issues/344) fail-closed
guarantee: a **failed read is never rescued by the cache** (the read dies, the command dies, and an
empty scan is never written to the cache), and a **cache hit is not a read** — a `next` served from a
90s-old scan never touched the network, so there is no unreachable board for it to fail closed *on*.

## Degrading, not dying

An exhausted budget is predictable, it lasts a known length of time, and it is **not** a protocol error.
So the client treats it as its own condition:

- **`EX_RATE` (exit 75, EX_TEMPFAIL)** — every command exits 75 on exhaustion, with the reset time.
  A worker loop must read that as *back off until the reset*, never as "no work available", and never
  by retrying immediately.
- **The lock still works.** Claim markers, comments, issue reads, PR creation — all REST, all on the
  other budget. `claim` under exhaustion takes the lock and reports the board write as **DEFERRED**.
- **The board write is queued, not lost — and this is now true of EVERY board write.** There is one
  board write in the client (`board_write`), and it queues. **`flush` replays it, and NOTHING else
  does.** There is no autoflush on any code path: no board-writing command drains the queue as a side
  effect, so a deferral nobody flushes is a write that never lands. `EX_RATE` is therefore a back-off
  **and-come-back** instruction, and `flush` is the coming back:

  ```sh
  scripts/fsgg-coord flush --dry-run   # what is queued — replays NOTHING, costs zero GraphQL
  scripts/fsgg-coord flush             # replay it, once the budget is back
  ```

  **This text used to say the opposite** — *"the next board-writing command flushes automatically"* —
  and it was never true of any engine, bash or port (`.github#883`). It was false in the direction that
  produces **inaction**: the recipes pair the promise with *"do not fix the board by hand"*, so a worker
  whose `set-field` deferred read both instructions as **do nothing**, and the write sat in
  `pending.jsonl` forever.

  **Universal deferral was not always so either**, and the way it failed is the same shape: **only
  `claim` ever called `defer_write`**, while the exhaustion message above told *every* caller
  "Board WRITES are queued".
  So a `set-field` — which the recipes drive three times in a row when a worker files a finding —
  printed the promise and **dropped the write**; `flush` then found an empty queue and reported
  success, *confirming the lie*. The finding landed on the board with no Status, no Repo Scope and no
  Phase, and the worker who read the message did the correct thing and carried on (`.github#510`).
  A promise nothing keeps is worse than no promise: it is the one thing that stops the worker looking.

- **Three failures, three facts — and only one of them is queued.**
  - **EX_RATE — transient.** The budget returns and the same write then succeeds. **Queued.**
  - **Off-board — permanent.** There is no item to write to; a queued write here could never land, so
    queueing it would be a second unkeepable promise. **Dropped, loudly.**
  - **Refused — the value or the field is wrong.** An unknown field, an unknown option, a `Blocked by`
    that is not a ref (canonicalised *before* any GraphQL is spent, precisely so a bad value costs
    nothing). Replaying it could not succeed on the tenth attempt either, and queueing it would
    swallow the refusal that says why. **Never queued; the refusal reaches the worker.**
- **A failed lookup is never a confident absence** ([#421](https://github.com/FS-GG/.github/issues/421),
  the [#266](https://github.com/FS-GG/.github/issues/266) class). A rate-limited `item-id` used to print
  "issue … is not on board — add it first: `gh project item-add …`" for an issue that *was* on the board,
  and following that advice would have created a duplicate item. The read failing and the item being
  absent are different facts; only the second is ever reported.

Every GraphQL call also selects `rateLimit { cost remaining }`; run with `FSGG_COORD_DEBUG=1` to log
each call's cost, so you can **verify** the drop rather than guess. Start any investigation with
`fsgg-coord budget`.

## Example

```sh
fsgg-coord budget                                     # free. START here, especially before a fan-out
fsgg-coord bootstrap                                  # once per ~day (or after a schema change)
fsgg-coord next --repo .github                        # what should I pick up next? (0 pts on a warm cache)
fsgg-coord ready --all --json > /tmp/board.json       # ONE scan; answer every further question from the file
jq '[.[] | select(.blocked)] | length' /tmp/board.json # ...like this — a jq query costs 0 points
fsgg-coord set-field FS.GG.SDD#84 Phase  "P2 SDD"     # cache-resolved ids, one mutation
fsgg-coord set-field FS.GG.SDD#84 Status "In progress"
fsgg-coord issues rendering --label cross-repo \
  | jq -r '.[] | "\(.number)\t\(.title)"'             # REST + ETag; 304 on repeat
```

> **Don't reach for raw `gh project item-list` to find the next item.** It nests
> `fieldValues(first:100)` inside `items(first:N)`, so its cost grows as **O(items × 100)** — measured,
> it spends **6 points to read FIVE items**, about what the thrifty scan spends on all **640**. A few
> calls at board scale exhaust the 5,000-pt/hr primary budget. `ready`/`next` answer the same question
> at ~1 point per 100-item page by reading Status/Phase through the `fieldValueByName` **resolver**
> field instead of the `fieldValues` connection.

## Guardrail

[`tests/fsgg-coord/run-cases.sh`](../../tests/fsgg-coord/run-cases.sh) drives the client against a `gh` stub that
**counts calls** and asserts the levers actually fire (bootstrap-then-cache adds zero GraphQL calls;
item lookup is one narrow call then cached; `set-field` routes by `dataType`; `issues` 304s to
cache; `ready`/`next` paginate a two-page board in exactly two calls and filter client-side). It
runs in CI via `.github/workflows/fsgg-coord-selftest.yml` — the same "a fixture proves
it" discipline as the [skill-union assertion](skill-union-assertion.md).
