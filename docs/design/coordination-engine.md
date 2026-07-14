# Design: the coordination engine

**Status:** proposal
**Author:** drafted 2026-07-12
**Supersedes in practice:** `scripts/fsgg-coord` (bash, 4,024 lines)
**Related:** ADR-0001, ADR-0015, ADR-0019, ADR-0021, ADR-0027; epics #266, #416, #417, #423

---

## 0. Summary

The coordination substrate works. It is also the org's largest and most persistent source
of defects, and the reason is not carelessness — it is that a **concurrent, transactional,
budget-constrained domain is being modelled in bash and jq regexes over prose**.

This document proposes moving the domain into a typed F# core, keeping every externally
observable contract byte-identical, and migrating behind a shadow-mode flag so the queue
never stops moving. It then proposes three structural changes the typed model *enables*
and which are worth more than the rewrite itself:

1. **The tool is the sole GraphQL principal.** Nothing else in the org may spend the budget.
2. **The docs and skills become generated projections of the model**, not hand-copied peers.
3. **The tool accumulates deferred maintenance and conscripts its next caller to drain it**
   — a helping mechanism, borrowed from lock-free algorithms, with the correctness
   conditions that come with it.

**Scope boundary.** This addresses the coordination/scheduling domain only. It does *not*
address the build/publish/pin/feed substrate (#504, #561, #574, #576, #519), which has a
separate root cause and will keep generating findings at its current rate.

---

## 1. The situation, measured

Over the 24 hours to 2026-07-12T12:00, in `FS-GG/.github`:

| | |
|---|---|
| Issues filed | **103** |
| Issues closed | **71** |
| Net | **+32** — the queue is losing |
| Open backlog | 40, of which **38 were created in the last two days** |
| Closed items whose *title cites a prior issue number* | **26 of 71 (37%)** |

Thirty-seven percent of a day's output is fixes about fixes. That is the definition of the
failure mode, and it concentrates in two families.

### 1.1 The scheduler family — 34 issues, one question

"Is this item startable?" has consumed **34 issues** (20 closed, 14 open): #431, #435, #437,
#440, #445, #454, #480, #488, #496, #516, #520, #523, #533, #534, #540, #581 and more.

One function has been through four rounds:

```
#440  take cannot reach a Backlog item, and reports a cause that is false
 └─ #452  fixed it
     └─ #481  the fix broke the invariant unclaim_status assumes
         └─ #488  "#440's fix reintroduced #440's defect in its own else-branch"
```

And #485 — **still open** — states the actual cause: *startability is computed in five
places and agrees in none*. Every one of those 34 issues is a patch to one of the five
copies.

> #485 is still open *even though its fix merged as `289d288`*. That is bug #558 — a closing
> keyword in the commit **subject** strands the item. **The board cannot currently be trusted
> to say what is done**, which corrupts every measurement in this section, including this one.

### 1.2 The projection family — the rule that lands in one tier

A coordination rule is currently stated in up to **six places**: the ADR, the canonical doc,
`scripts/fsgg-coord`, and four `SKILL.md` files × two skill roots — then content-addressed
into `registry/repos.lock` and pushed to six receivers. **Fifty-four vendored copies of the
protocol exist in the org.**

The propagation edge is *a second issue and a second PR, every time*:

```
#309  the touch-set rule       → landed in docs/coordination/parallel-work.md ONLY (d0bc333)
 └─ #502  "propagate #309's rule into the kit workers actually load"  (ea5ccb4, 4 SKILL.md)

#481  release restores the column it overwrote  → fixed the TOOL (db279ca)
 └─ #531  "three protocol docs still promise '-> Ready', and two are what workers load" (cb98ad3)
```

**There is a live, currently-unreported instance of this at HEAD.** `#527` (`3a9f32c`) moved
the kit digests out of the authored `repos.yml` into the generated `repos.lock`, precisely so
that #309's "do not reserve a generated artifact" rule could reach them and end the
three-worker deadlock of #428. It touched seven files. **None of them was a skill.** So
`.claude/skills/intra-repo-parallel-work/SKILL.md:122-129` — shipped to all six receivers,
right now — still instructs every worker to reserve `registry/repos.yml` in its touch-set
and to run `repos.sh digest`, a verb that no longer exists.

**The skill re-creates the exact deadlock the fix eliminated.** And the rule appears in *zero*
canonical docs, so there was no source-of-truth edit that could have carried it.

### 1.3 The org has already named the disease, and built a linter instead of a cure

`scripts/check-worker-id-attractor.py:11-18` describes `docs/coordination/parallel-work.md`
as **"the document those skills are a projection OF"** — and exists because the projection is
manual and the copy-paste keeps failing. The collision attractor — a literal worker id sitting
in a copyable position in the recipe — was removed **by hand twice** (#532, #551) before #570
gated it. Four workers sharing one id prefix were live on the board at once, every one of them
lifted from that example.

> **This document tripped that gate on its first CI run.** The draft quoted the offending
> literal verbatim, here, in a paragraph explaining that the literal spreads by being read.
> #570's gate caught it. That is the argument for §4.5 in miniature: a rule enforced by a
> *generated* projection cannot be re-introduced by the next author who quotes it, and a rule
> enforced by prose can — including by the author warning you about it.

That gate is a hand-written, per-rule assertion. **This design generalises it: if the
projection is generated, the rule cannot fail to arrive.**

---

## 2. Root cause: four substrate errors

The bugs are not random. Each family is what the current substrate makes *cheap*.

### 2.1 Bash's default is fail-open

In bash, an error, an empty result, and a legitimate "no" are the same value: the empty
string. Epic **#266 has 51 children** for this reason.

- **#461** — `active_claims` fails OPEN: a failed claim scan reads as *"nothing is claimed"*,
  so `take` hands a held item to a second worker. The file's own comment: *"one thing a lock
  may never do: an empty answer would read as 'nobody holds this'."*
- **#431** — `next` fails open, recommending items with no `Paths:`.
- **#436** — an assertion that **cannot fail** (green on a missing subject).
- **#503** — `repos-audit` *summed* its non-vacuity guard: one populated leg vouched for two
  empty ones.
- **#566** — pin-coherence *"proves `hostRules` is PRESENT, not that it WORKS."*

The countermeasure the org evolved — a **non-vacuity guard** plus `exit 3 = NO_VERDICT` — is
correct and is the single most transferable idea in the codebase. **It is a type, and it is
being enforced by convention.** In F# it is `Green | Red | NoVerdict`, and it is unforgettable.

### 2.2 State lives in prose, parsed by regex

Claims are HTML comments matched with
`capture("^<!--\\s*fsgg:claim\\s+worker=(?<w>[^\\s>]+)")`. Touch-sets are a bare `Paths:` line
in an issue body, read by an awk/grep/sed pipeline. Dependencies are free text — the tool
admits why: *"`Blocked by` is TEXT only because Projects v2 has no typed dependency field."*

Everything downstream is a parse bug wearing a different hat: **#435** (a backticked `Paths:`
line is refused as unmatchable), **#548** (the tool rejects the bare issue number its own
recipe tells you to type), **#558** (a closing keyword in the commit *subject* strands the
item), **#497/#507** (the claim scan dies at 128 KiB — **state is travelling through argv**).

### 2.3 There is no module boundary, so predicates multiply

Five copies of schedulability (#485). Six statements of the claim protocol. Three
enumerations of the kit — one of which, `coordination-propagate.yml:24-37`, carries its own
epitaph: *"HAND-MAINTAINED, and it must match repos.yml `kit:` … A kit item missing here does
not fail: **it PROPAGATES NOTHING, silently**."*

### 2.4 The budget is a shared global with no owner

The GraphQL primary limit is **5,000 points/hour, shared across every worker** (N agents
authenticate as one account). `cost = max(1, nodes/100)`. Five workers looping `take`/`next`
drained it in **~15 minutes** (#418).

Exhaustion is where the board starts lying, and `docs/coordination/graphql-budget.md` says so
better than I can:

> *"When it runs out, it takes the WRITES with it. `claim` holds its lock in REST comments, so
> under exhaustion the lock is taken and the `Status: In progress` write is refused. Swallow
> that and the board says `Backlog` while a worker holds the item… **The protocol's failure
> mode is load-dependent: the more you fan out, the more the board lies** — and fanning out is
> the point of the protocol."*

**And the budget has no owner.** Three skills, two docs, and a workflow call `gh api graphql`
or `gh project` *directly, outside the tool* — verified at HEAD. #528 (`pnext-item` §5 was
GraphQL-only) and #538 (`check-board` §3 resolved blockers over GraphQL, *draining the budget
it needs*) are both direct consequences.

---

## 3. What must be preserved

The tool is not a standalone CLI. It is a **content-addressed row in a distributed kit**
(`kind: client`), byte-copied into six receiver repos, byte-compared there by CI, and **called
by a GitHub workflow that greps its stdout**. These constrain the rewrite more than the domain
does.

**Hard contracts — must survive byte-for-byte:**

| Contract | Consumer |
|---|---|
| Exit codes `0` ok / `1` error / `3` EX_OFFBOARD / `75` EX_RATE | every worker loop; `EX_RATE` must read as *back off*, never *no work* |
| `FSGG-PATHS OK\|DRIFT\|INVALID\|SKIP` on stdout | `touch-set-drift.yml` greps it |
| `FSGG-DONE` / `FSGG-NOT-DONE` / `FSGG-LINT <SEV> <CODE> <id> — <detail>` | CI + skills |
| `<!-- fsgg:claim worker= lease= harness= session= prev= -->` | the lock itself; any format change is a fleet-wide flag day |
| `<!-- fsgg:msg from= to= -->` | the inter-worker channel |
| `Paths:` grammar — fence-aware, not-a-glob, `Paths: none` sentinel, subtree-containment disjointness | `lint`, `batch`, `overlap`, `verify-paths`, `check-board` |
| `item/<n>-<slug>` branch convention, `FSGG-Worker:` commit trailer | the worktree protocol |
| `whoami --mint` prints exactly one eval-able line | the single sanctioned idiom; gated by #570 |

**Gates that must stay green:** `fsgg-coord-selftest` (46 negative assertions against a
call-counting `gh` stub), `coordination-coherence`, `touch-set-drift`, `worker-id-attractor`,
`recipe-pagination`, `repos-registry-selftest`.

**Budget levers that must be kept:** the 90 s shared scan cache; the scheduling-read vs
truth-read split (schedulers may serve stale, reconcilers never may); the two cache invariants
(*a failed read is never rescued by the cache*; *a cache hit is not a read*); and the house
rule that **every query selects `rateLimit { cost remaining }`**.

**What the claim lock gets right, and must not be "improved":** the comment-order CAS is
*sound*. GitHub issues comment ids from a single server-side sequence, so "lowest live marker
id wins" is a total order every racer observes identically. The bugs around it (#419, #461,
#550) were in **worker identity** and **fail-open scanning**, not in the CAS. It also lives on
REST *deliberately* — because the GraphQL budget is the first thing to die under fan-out, and
**a lock may never live on the budget that dies first**.

> **Alternative considered and rejected: a git-ref CAS.** A `coord/claims` branch updated with
> `push --force-with-lease` is a true compare-and-swap with a real linearization point and free
> history. It is tempting. It is also a *third* state substrate to keep coherent, it puts the
> lock behind git auth in CI, and it replaces a mechanism that is *already correct* with one
> that is merely *more elegant*. **The comment-order CAS stays.** Rewrite the identity and the
> error handling around it, not the lock.

---

## 4. The design

### 4.1 Shape

```
FS.GG.Coord.Core          pure F#, zero IO.  The model, and the only place a rule exists.
  ├── Identity            WorkerId, SessionId, Harness — minting, the same-id/different-session refusal
  ├── Lock                ClaimMarker, Lease, the comment-order CAS as a pure decision function
  ├── TouchSet            PathToken (not a glob), fence-aware parse, subtree-containment disjointness
  ├── Board               Status, Phase, RepoScope, BlockedBy — typed, with canonicalisation on write
  ├── Budget              Cost, Remaining, the max(1, nodes/100) model, the 1-pt floor
  └── Schedulability      ONE total function.  Board -> Claims -> Item -> Schedulability
                          (a DU, never a bool — see 4.2)

FS.GG.Coord.GitHub        the impure edge.  Result-typed IO, retries, ETag, the scan cache.
                          THE SOLE GRAPHQL PRINCIPAL IN THE ORG (see 4.3).

FS.GG.Coord.Projections   emits docs/coordination/*.md and the four SKILL.md FROM the model.
                          (see 4.5 — this is where the leverage is)

FS.GG.Coord.Cli           argument parsing, the three output projections, exit codes.
                          Follows FS.GG.SDD.Cli exactly: hand-rolled parser + Options.fsi,
                          one CommandReport, --json is the contract, --text/--rich are projections.
```

The org's established F# CLI pattern is already proven and should be copied without
deviation: hand-rolled parsing with a companion `.fsi` declaring every consumed option (so the
residue can be *rejected* — SDD learned this when `init --project-root /tmp/b` silently seeded
the current directory and reported success); one `CommandReport` with typed diagnostics;
`0` ok / `1` user error / `2` tool defect; Spectre.Console 0.57.2; xUnit; FsCheck for
round-trip properties.

### 4.2 Make the fail-open class unrepresentable

Every check returns three values, and `NoVerdict` is non-zero:

```fsharp
type Verdict<'a> =
    | Green of 'a
    | Red of Diagnostic list
    | NoVerdict of reason: string      // "I could not reach an answer." NEVER green.
```

Schedulability is not a bool. The reason #485 exists — five predicates disagreeing — is that
each one collapsed a rich answer into a yes/no and then disagreed about *which* no:

```fsharp
type Schedulability =
    | Startable
    | NotOnBoard                       // #421: a rate-limited lookup is NOT "not on board"
    | NoTouchSet                       // #496: distinct from `Paths: none`
    | DeliberatelyNoTouchSet           // `Paths: none`
    | BlockedBy   of Ref list          // #476: a blocker is cleared by CLOSED *or* MERGED
    | BlockerUnknown of Ref list       // #485(e): not on the board != closed. Never "blocked forever".
    | Held        of WorkerId * Lease
    | IssueClosed                      // #520: take/batch currently schedule CLOSED issues
    | WrongStatus of Status
    | Undetermined of reason: string   // the NoVerdict leg. Exit 3 or 75, never 0.
```

Fourteen of the open scheduler issues are a missing case in this DU.

### 4.3 The tool is the sole GraphQL principal — and it is gated

**Rule.** No skill, doc, recipe, workflow, or agent may invoke `gh api graphql`, `gh project`,
`gh issue view`, or `gh issue list`. Every board and issue read goes through the tool, which is
the only thing that can see, meter, cache, and queue against the shared 5,000 pt/hr budget.

This is not currently true. It must become a **gate** — `check-graphql-monopoly.py`, a grep
over the skill roots, `docs/`, and `.github/workflows/`, with a non-vacuity guard and `exit 3`
on zero subjects, modelled directly on `check-worker-id-attractor.py`. Six known violators at
HEAD.

What the monopoly buys, none of which is achievable without it:

- **One budget accountant.** A real token bucket shared across the fleet via the cache dir,
  rather than each caller independently discovering exhaustion.
- **One cache.** The 90 s scan already exists; a direct caller bypasses it and pays 6 pts to
  list five items (`gh project item-list --limit 5` — *"Never use it."*).
- **Aliased writes.** A Projects v2 field mutation returns ~1 node, so it costs the **floor —
  1 pt — no matter how many you alias into one document.** A placement pass sets ~6 fields per
  item: 6 pts sent one-per-request, **1 pt aliased**. #448 is open and this is a free ~6×. It
  is only implementable if writes are funnelled through one place.
- **An honest deferred-write queue.** Today `die_rate()` tells *every* caller "Board WRITES are
  queued: see `fsgg-coord flush`" — but `defer_write` is **only ever called from `claim`**. A
  bare `set-field` or `done --flip` that hits `EX_RATE` prints that promise and **drops the
  write**; `flush` then finds an empty queue and reports success, *confirming the lie* (#510).
  The queue must cover every board write.

> The `graphql-budget.md` doc carries its own errata: it used to state flatly that *"batching
> does nothing for the primary budget"* — true of node-heavy reads, false for mutations — and
> *"stating it unconditionally cost us a real decision: it was cited to talk a worker out of
> building `set-field --batch`, the one optimisation that would have cut the write path ~6×"*
> (#447). Build it on day one.

### 4.4 Distribution — the central decision

`scripts/fsgg-coord` is a `kit:` row that is sha256'd into `repos.lock`, byte-copied with its
exec bit into six repos, byte-compared by `coordination-coherence`, and shelled out to by
`touch-set-drift.yml` on a runner with no .NET SDK step. A compiled binary breaks all four.

| Option | Verdict |
|---|---|
| **A. Keep bash, refactor** | Rejected. The substrate *is* the cause. |
| **B. `dotnet tool` in the kit, receivers install from the org feed** | Creates a cycle: to fix coord you must publish; to publish you need coord to schedule the fix. |
| **C. Self-contained single-file binary as the `kind: client` row** | The kit already carries a digest + exec bit, so mechanically it fits — but a ~20–70 MB binary × 6 receivers × every version is git bloat, needs a per-RID matrix, and **is invisible to Renovate** (`datasource=nuget`). No org precedent. |
| **D. `dotnet tool` + a thin shim as the kit row** | **Recommended.** |

**Option D in detail.** The kit's `fsgg-coord` row becomes a small bash shim that resolves the
tool from `.config/dotnet-tools.json` and `exec`s it. That file **is already distributed to
every repo by `sync-build-config.sh`**, and Renovate already watches it. Therefore:

- The kit row, its digest, its exec bit, and the `scripts/fsgg-coord` path that every doc,
  workflow, and skill references are all **unchanged**. `coordination-coherence`,
  `repos-registry-selftest`, and `touch-set-drift` keep working with no edit.
- The shim is a few lines of bash and stops churning, so the kit stops re-drifting on every
  protocol edit — which is itself a live cost (`coordination-coherence` reds every receiver's
  main between the push and the sync PR landing).
- Renovate bumps the tool in receivers automatically.
- **The publish cycle is broken by asymmetry:** `.github` builds coord *from source* and never
  depends on the feed. Only *receivers* consume the package, and they only need it for
  `verify-paths` in CI and for workers. So a broken feed cannot prevent coord from being fixed.

Costs, stated honestly: receivers' CI gains an `actions/setup-dotnet` step (`touch-set-drift`
runs on bare `ubuntu-latest` today); every worker's environment needs a .NET runtime (all six
receivers are .NET repos, so this is close to free in practice, but it is a new dependency for
the coordination path specifically); and the `kit:` schema gains a shim/tool distinction, which
is a `contract-change` under ADR-0015 and a `schemaVersion` bump on `repos.yml`.

### 4.5 Generate the projections — the biggest single win

`fsgg-coord` is **already the model**; it just isn't the *source*. In every drift that can be
dated, **the tool was right and the prose was wrong** — `db279ca` fixed the tool one PR before
`cb98ad3` fixed three docs.

So invert the dependency. `FS.GG.Coord.Projections` emits `docs/coordination/parallel-work.md`
and the four `SKILL.md` bodies **from the typed model**, gated by a regeneration check exactly
like `repos.lock`. A rule then *cannot* land in one tier and not the others, because there are
no longer tiers — there is a model and its renderings.

This retires, by construction:

- the entire #502 / #531 / #551 / #555 / #540 / #548 family;
- the 54 vendored protocol copies (they become generated output, and a collision in them is a
  rebase, not a decision — #309's rule now applies to them);
- `check-worker-id-attractor.py`, which exists *only* because the projection is manual;
- the live `repos.yml`-vs-`repos.lock` drift in §1.2, which no gate can currently see.

### 4.6 Deferred maintenance, drained by the next caller

*(This is the "the tool can't call an agent, so it conscripts one" idea. It is a good idea, and
it has a name.)*

The tool has no thread. It cannot reconcile the board, retire a stale claim, or re-verify a
blocker on its own — so today nobody does, until a human runs `/check-board` or a cron fires 25
minutes late. Drift accumulates between calls, and the whole `CLAIM-STATUS-LAG` /
`STALE-CLAIM` / `OFF-BOARD-ISSUE` family lives in that gap.

The proposal: **the tool maintains a chore queue, and hands work to the next agent that calls
it.** The agent is the thread the tool doesn't have.

This is the **helping mechanism** from lock-free algorithms — a thread that encounters another
thread's incomplete operation completes it before proceeding — and adopting the name means
adopting its correctness conditions, which are exactly the ones that bite here:

**1. A chore must be claimed, not broadcast.** If N workers each call `next` and each is handed
the same chore, N of them do it. That is **#464** (*N parallel workers file the same finding N
times*) and **#463** (*two workers hand-synced the same kit twice in one day*), rediscovered.
Chores take a lock — the same comment-order CAS, or a marker on a chore issue. The primitive
already exists; use it.

**2. A chore must be verifiable, not merely reported.** The tool cannot enforce compliance and
must never assume it. "The agent said it did it" is a promise, and **a promise that nothing
re-checks is exactly the #510 shape** — the fix for fail-open must not be fail-open. So: the
tool re-runs the check that generated the chore. A chore is retired when *the condition is
observably gone*, never when an agent claims it is. Chores must therefore be **idempotent** —
which is the same condition helping imposes.

**3. A chore must be offered at a safe point, and bounded.** Never mid-claim: a worker holding a
lease with a live touch-set must not be handed an unbounded side-quest that blows its lease or
its context. Offer at natural boundaries — after `done`, or at `next` when the worker is idle
and about to pick up work anyway — and carry an explicit size so the worker can decline. The
unlucky caller must not pay for everybody's garbage collection.

**4. Chores do not generate chores.** Strict depth-0. Otherwise the drain never converges.

Given those four, this is genuinely good: it costs no new infrastructure, no bot account, no
cron; it reuses the deferred-write queue pattern the tool already has (`flush`); it amortises
maintenance across a fleet that is *already calling the tool constantly*; and it closes the
"nobody owns reconciliation" hole that produces the drift `check-board` currently finds by
hand.

Without those four, it is a machine for manufacturing duplicate work and false green.

---

## 5. Roadmap

Each phase is independently valuable and independently abandonable. Nothing after Phase 0
blocks the queue from moving, because bash stays authoritative until Phase 3.

### Phase 0 — stop the bleeding *(days; no F# involved)*

Cheap, independent, and worth doing even if the rest is rejected.

| Work | Closes / prevents |
|---|---|
| **Freeze feature work on bash `fsgg-coord`.** CRITICAL fixes only. Every further patch to one of the five schedulability copies makes the port harder. | — |
| **Fix the done-stamp** (#558, #543, #583). | Until this works, the board cannot say what is done, and *no phase below can be measured*. Do it first. |
| **Land the GraphQL-monopoly gate** (§4.3). Six violators at HEAD. | #528, #538, and the whole class |
| **Fix #510** — the deferred-write queue must cover every board write, not just `claim`. | The tool currently promises a write it drops |
| **Propagate the #527 rule into the skills** (§1.2 — live at HEAD, re-creating #428's deadlock). | #428 |

**Exit:** the board is trustworthy, the budget has one owner, and the tool no longer lies about
queued writes.

### Phase 1 — extract the model *(≈1–2 weeks; ships nothing)*

Build `FS.GG.Coord.Core`: pure, zero IO, no behaviour change, not wired to anything.

- The types in §4.1 and the `Schedulability` DU in §4.2.
- **One** `schedulability` function. Read all five bash copies; where they disagree, the
  disagreement is a decision to be made and recorded, not merged silently.
- **Port the incident history into tests.** The 4,024 lines of bash are an incident log —
  *"one thing a lock may never do: an empty answer would read as 'nobody holds this'"*. Every
  invariant those comments assert becomes a named regression test; every closed issue (#419,
  #440, #461, #481, #488, #496, #520, #547…) becomes a test that carries its number.
  **Target: ~60 tests before a single line of production code is trusted.** This directly
  retires the systemic cause behind #488 — a fix that reintroduced its own bug because nothing
  proved it couldn't.
- FsCheck properties for the parsers (`Paths:` round-trip, `Blocked by` canonicalisation,
  marker encode/decode) — the classes behind #435, #497, #548, #558.

**Exit:** `dotnet test` is green, and every historical defect has a test that fails without its
fix.

### Phase 2 — the adapter, and shadow mode *(≈2 weeks; still ships nothing user-visible)*

- `FS.GG.Coord.GitHub`: `Result`-typed IO, the ETag/REST reads, the 90 s scan cache with both
  invariants, `rateLimit { cost remaining }` on every query, the retry/`EX_RATE` contract.
- Ship `fsgg-coord --engine=fs` **behind a flag, defaulting off**.
- **Shadow mode:** on every invocation, run both engines, return bash's answer, and log any
  divergence. Zero risk — bash remains authoritative and the fleet is unaffected.
- Divergence is the acceptance test. It will find real bugs *in both*.

**Exit (SUPERSEDED by [ADR-0038](../adr/0038-the-corpus-is-the-cut-over-gate.md)):** ~~zero divergence
across the live fleet for **three consecutive days** under normal fan-out.~~ That clock could not tick
— a worker in a per-item worktree resolved no engine and so banked no evidence (#728) — and its own
taxonomy classified as *"not a bug"* three real defects the flip would otherwise have shipped. **Phase 2
exits on the defect corpus**, like Phase 3b: `tests/fsgg-coord/cases/`, green against **both** engines.
The shadow keeps running as **telemetry** — it is how a live fleet is watched, not what a cut-over waits
on.

#### Phase 2 as built — what shipped, and the two decisions it forced

The shadow harness landed; `FS.GG.Coord.GitHub` (the `Result`-typed IO adapter) did **not**, and is
deliberately deferred. Nothing in shadow mode needs it: the engine reads *nothing*. It is required
only for the Phase 3 flip, when the engine must fetch its own state, and building it early would have
put an unused adapter on the live path for no gain.

**What shipped:** `FS.GG.Coord.Cli` (`fsgg-coord-engine decide` — a board-state snapshot on stdin, a
typed verdict per candidate on stdout); `Batch` in the core — the greedy fold `next`/`take`/`batch`
all delegate to, which `schedulable` alone cannot express because a chosen item *reserves* against the
candidates after it; `fsgg-coord --engine shadow`; and `fsgg-coord divergence`.

**Deviation from the plan above, stated plainly:** this phase was specified as shipping
`--engine=fs` "behind a flag, defaulting off". It does not. An `--engine=fs` that did not actually
make the engine authoritative would be an option that is accepted and ignored — indistinguishable,
from the caller's side, from one that was honoured, which is the exact fail-open this project exists
to end. Making it *genuinely* authoritative is the flip. So `--engine fs` is **refused**, with a
message naming what it is waiting for. It lands in Phase 3, where it belongs.

**Decision 1 — the two engines check in a different ORDER, and both orders are defensible.**

Bash filters blocked candidates in bulk *before* it ever reads a lock, and reads the lock *before* the
touch-set. The typed core does the reverse on both counts, deliberately: `Schedulability.fs` argues
that "nobody can *claim* this item" (no touch-set) is a stronger and cheaper statement than "somebody
already *has*", and that a worker told the second when the first is also true fixes the wrong thing.

So for an item that is **both blocked and held**, bash says *"blocked by X"* and the core says
*"held by W"*. Both are true. Neither engine is wrong. This is why `divergence` classifies on two
axes and reports them apart:

| Class | Meaning | Blocks the flip? |
|---|---|---|
| **OUTCOME** | The engines disagree about whether an item may be **handed out**. | **Yes.** This is how two workers end up in one file. |
| **REASON** | They agree it is unschedulable and name a **different fact**. | No. A decision to record. |

A single counter would have buried the first in the second, and "zero divergence for three
consecutive days" could never have gone green — for something that was never a bug. **The choice of
which order survives is a Phase 3 decision, taken with the live frequency data the shadow is now
collecting.** It is recorded here rather than merged silently, because #485 exists precisely because
five predicates were merged silently.

> **TAKEN — [ADR-0038](../adr/0038-the-corpus-is-the-cut-over-gate.md): blockers are checked BEFORE the
> touch-set.** Bash's order wins. *Semantics:* a blocked item cannot be started whatever its touch-set
> says, so *"no `Paths:` declared"* sends a worker to fix something that leaves them exactly where they
> were. *Cost, which settles it:* blockers are **board** facts, already in the scan and free; a touch-set
> lives in the issue **body**, one REST read per item. Touch-set-first would oblige a body fetch for every
> blocked item on the board — paying the budget that dies first (#418) to answer a question the board had
> already answered. It is also why bash never fetched those bodies, why they were never fixtures, and how
> a swept item with an unreadable body could silently cease to exist for as long as it did.

**Decision 2 — the core's order is not free, and the shadow now measures what it costs.**

Bash's order is *cheaper*, and that is not an accident. Blocker state arrives free in the board scan,
so short-circuiting on it means a blocked candidate never costs an issue-body read. The core's order
reaches the touch-set first, so it *needs* that body. A shadowed run therefore pays one body read and
one marker read per blocked candidate — REST only, never the 5,000-pt/hr GraphQL budget that dies
first under fan-out, and only when the shadow is switched on.

Handing the engine an empty body instead would have fabricated a `NoTouchSet` verdict that neither
engine holds — a manufactured divergence, which is worse than no shadow at all. So the shadow pays,
and **counts** (`divergence` reports `extraReads`). If that number turns out to be large in practice,
it is a real argument for bash's order, and it will be made with evidence rather than taste.

**A modelling wart found in passing.** `Types.Blocker` requires a `Ref` even when its state is
`BlockerUnparseable` — but the whole meaning of that case is that the text *is not a ref*. It is
cosmetic today (an unparseable blocker still blocks, in both engines, which is the only property that
decides anything) and it is not worth a Phase 1 type change mid-shadow. It should become
`Ref option` at the flip.

#### What the shadow's own review caught — the five ways it could still have lied

Every one of these was found *after* the harness was green, and every one of them is the failure this
project exists to end, reproduced inside its remedy. They are recorded because the next person to
touch this will be tempted by the same shortcuts.

1. **The observer could kill the caller.** The shadow reads markers for the candidates bash
   *short-circuits*, and those reads go through `claims_of` — *"or DIE"*, and it means it. But `die`
   is `kill -s TERM $$`: it takes down the **top-level shell**, and no `|| true` catches a signal. One
   transient 5xx on a blocked candidate would have aborted the tool, and a worker running
   `--engine shadow take` would have got a hard failure and no item — on a run bash alone completes.
   Contained with `soft_run`. **The shadow's first rule is that it may not change the answer, and it
   could.**

2. **...and containing it re-created the original sin.** A contained `die` returns the empty string,
   and reading *that* as "nobody holds this item" is exactly #461 — an empty answer wearing a failed
   read's clothes. A failed marker read now makes a candidate **unobservable**, not unheld: counted,
   withheld from the comparison, never guessed at.

3. **`--ignore-blocked` manufactured OUTCOME divergences.** The flag is a diagnostic that relaxes
   bash's blocker filter and nothing else. The snapshot still carried the blockers, so the engine
   dutifully returned `blocked-by` for every candidate bash had deliberately let through — and each
   was logged as an **OUTCOME** divergence, the release-blocking class, while both engines behaved
   exactly as designed. The engine must be told the rule bash **enforced**, not the rule bash knows.

4. **`compared` counted the union, not the pairs.** An engine that decided *nothing* — a `Red` verdict
   refusing the batch, or an empty `decisions` array — still produced `compared: 28`, every item
   classed `bash-only`, `outcome: 0`. Green, over a run in which the engine had agreed to nothing.

5. **A `Red` engine verdict was recorded and read by nothing.** The engine refusing the batch outright
   while bash proceeds is the sharpest disagreement available. It scored as agreement.

`divergence` now gates on `engineRed` and `unpaired` as well as `outcome`, and the report states what
was **not** compared, not only what was. The pattern across all five is the same one the ADR opens
with: *a number that only ever reports what it looked at is how "we agreed" and "we never checked"
come to print the same sentence.*

#### The phase boundary was circular, and the roadmap above still says so

**Phase 2 as specified could never exit.** Its gate is *"zero divergence across the live fleet for
three consecutive days"* — and at the moment the harness landed, **nothing anywhere ran it**:

- The shadow defaulted **off** (`--engine=bash`), faithfully to *"behind a flag, defaulting off"*, and
  nothing in the fleet sets the flag.
- The engine only exists where somebody built it — which is `.github`, and nowhere else, because
  distribution is Phase 3's job.
- **No workflow scans the live board at all.** `GITHUB_TOKEN` is repo-scoped and cannot read an
  *org* Projects v2 board, so a scheduled shadow would need an App or PAT grant that does not exist.

So: Phase 2 cannot exit without fleet-wide evidence → fleet-wide evidence needs the engine distributed
→ distribution is Phase 3 → Phase 3 is gated on Phase 2 exiting. **The clock could never start.** An
observer nobody switches on is not a cautious observer; it is a decoration, and it would have sat there
producing a reassuring empty log — which is, precisely, this project's own thesis about what an empty
result means.

**Half of it is fixed here.** The shadow now defaults to `auto`: it runs **wherever an engine
resolves**, and does nothing where one does not. The presence of the engine *is* the switch, which is
the honest gate — a repo without one is byte-for-byte unaffected and cannot be broken by a thing that
is not there. No env var, no opt-in, no ceremony. It is safe to make this the default only because the
shadow has been proven unable to change bash's answer, its exit code, or its life (709 assertions,
including a mutation test that removes the containment and watches the tool die).

**The other half is a real change to the roadmap, and it belongs to whoever schedules Phase 3:**

> **Phase 3 must split into 3a (publish) and 3b (flip).** Shipping the engine as a `dotnet tool` plus
> the kit shim is what lets the shadow run in the six receivers *at all* — and it is **not a flip**:
> bash stays authoritative throughout, and `--engine=fs` stays refused. Only once 3a has been live long
> enough for the divergence log to be both **non-empty and clean** does 3b become answerable.
>
> Publishing before flipping is not a compromise. It is the only order in which the flip's evidence can
> exist.

#### 3a, as built — the evidence had nowhere to go

Publishing the engine made the shadow *able* to run everywhere. It did not make the evidence *count*,
and [#634](https://github.com/FS-GG/.github/issues/634) is what was left:

> The shadow appended to `$XDG_CACHE_HOME/fsgg-coord/divergence.jsonl` — **a disposable cache directory
> on one machine**. Nothing collected it. The rows did not say **which worker** wrote them, so a fold
> could not have counted fleet members even if the logs had been gathered: one worker's 500 runs and 500
> workers' one run each rendered identically. And **nothing computed the criterion at all** — the clause
> the whole cut-over is conditional on was a sentence in an ADR, not a function in the codebase.
>
> CI was the sharpest case: `touch-set-drift` shells out to the tool on a bare runner whose cache dies
> with the job, so **100% of everything it ever observed was discarded**, on every run, forever.
>
> The client was already scrupulous that an **empty** log is not agreement (`divergence` exits 3 — *"zero
> EVIDENCE, not zero divergence"*). It had no way to say that a **non-empty LOCAL** log is not the
> **FLEET**. That is the same substitution one layer up, inside the tool built to end it.

So 3a also ships **the fleet ledger**: workers publish a per-`(worker, day, engine)` summary as a marker
comment on a well-known issue (REST — *a ledger may never live on the budget that dies first*), and
`Divergence.evaluate` folds it, **in the typed core**, into the one verdict the flip is gated on. It fails
closed on evidence that is absent, thin, single-worker, uncovered on a day, or from another engine build.

**The clock cannot start until the evidence has somewhere to accumulate. Now it does.**

### Phase 3 — flip *(days)*

- **Entry (SUPERSEDED by [ADR-0038](../adr/0038-the-corpus-is-the-cut-over-gate.md)):** ~~`fsgg-coord
  divergence --fleet` is GREEN — three consecutive covered days, ≥2 distinct workers, zero blocking
  divergences, on the build being flipped.~~
  **That clock could not tick.** Workers run in per-item worktrees; a worktree worker resolved no engine
  (#728); a worker who banks no evidence can never be one of the "≥2 distinct workers". And because
  `Divergence.evaluate` partitions by exact engine build, any republish restarts the window — so the
  engine could not be improved while waiting for the clock that was waiting for the engine.
- **Entry (actual):** the **defect corpus** — `tests/fsgg-coord/cases/`, one case per historical defect —
  is green against **both** `--engine=bash` and `--engine=fs`. It covers every path that has actually
  broken, rather than whatever floated past a live fleet for three days; it needs only a checkout; and it
  survives an engine rebuild. Sweeping it under `fs` found three real defects that this clock's own
  taxonomy classifies as REASON divergences — *"not a bug"* — and would have waved through. The shadow is
  now **telemetry**, not a gate.
- **What actually landed:** `--engine=fs` is **open** — the engine's answer is the answer, and every
  failure in that mode is fatal (no fallback: falling back to bash after the caller asked for the typed
  core is a silent engine substitution). **The default did NOT move.** With no flag and no
  `FSGG_COORD_ENGINE`, the mode is `auto`, and `auto` still returns **bash's** answer — the engine
  shadows where one resolves, and its disagreement is logged. `--engine=bash` remains the escape hatch.
  Making `fs` the default is a **separate** decision on the corpus's evidence; ADR-0038 does not take it.
- *(future)* `--engine=fs` becomes the default.
- *(future)* One week later, delete the bash implementation. The kit row becomes the shim (§4.4).
- `fsgg-coord-selftest`'s 46 negative assertions run unchanged against the new engine — they
  are the contract, and they were written against a call-counting `gh` stub, so they port.

**Exit *(FUTURE — this is the criterion, not the present tense)*:** F# is authoritative — `fs` is the
**default** and the bash implementation is gone; every gate in §3 is green; the stdout tokens are
byte-identical. **What has landed is the OPENING of `fs`, not its imposition:** today the default is
`auto` and bash is still the answer a caller gets.

### Phase 4 — take the wins the model enables *(the actual payoff)*

Ordered by value:

1. **Generated projections** (§4.5). Retires the #502/#531/#551/#555 family and 54 vendored
   copies. *This is the largest single win in the document.*
2. **Aliased batch writes** (#448). ~6× on the write path, free, measured.
3. **The chore queue** (§4.6), with all four conditions. Closes the reconciliation gap.
4. Fold the hand-written per-rule gates (`check-worker-id-attractor.py`, and the projection
   half of `coordination-coherence`) into the regeneration gate.

### What it retires

**22 of the 40 open `.github` issues (55%)** live in the domain this restructures — the whole
scheduler family, the claim/lease family, the parse family, and the fail-open family, plus the
projection family that Phase 4 closes by construction.

**It does not touch** the build/publish/pin/feed substrate — #504, #561, #574, #576, #519, and
epic #423. That is a separate root cause with its own design, and it will keep producing
findings at the current rate. **Do not let this document be read as a fix for that.**

---

## 6. Risks

| Risk | Mitigation |
|---|---|
| **Rewriting the runway while flying.** The tool schedules the work that rewrites the tool. | Shadow mode (Phase 2). Bash stays authoritative until divergence is zero. Nothing is cut over on faith. |
| **The 4,024 lines encode ~200 issues of hard-won knowledge; a clean-room rewrite loses it.** | Phase 1 is *explicitly* the port of that knowledge into tests, and it happens **before** any production code is trusted. The comments are the spec. |
| **A flag day on the `fsgg:claim` marker format** would strand every live claim. | The marker format is frozen (§3). If it must ever change, it changes with a version prefix and a two-format reader. |
| **The publish cycle** (to fix coord you must publish coord). | Broken by asymmetry: `.github` builds from source; only receivers consume the package (§4.4). |
| **The chore queue manufactures duplicate work.** | The four conditions in §4.6 are non-negotiable. Chores are claimed, verified, bounded, and depth-0. |
| **Scope creep into the publishing substrate.** | Stated boundary, twice. This design owns coordination only. |
| **Nobody is watching the arrival rate.** 103 findings/day is *supply*, not demand — the fleet finds faster than it fixes. | Out of scope here, but it is the other half of the problem, and no rewrite fixes it. |

---

## 7. Open questions

1. **Where does the F# project live?** `.github/scripts/FsggCoord/` alongside the existing
   `NewSddWorkspace` tool is the path of least resistance and has precedent — but `.github`'s
   own F# tool does *not* consume the shared `Directory.Build.props`, has *no* lockfile, and is
   tested by a bash fixture. **A new tool should opt in to SDD's stricter standard**, not
   inherit `.github`'s looser one. Decide explicitly.
2. **Do the five schedulability copies disagree anywhere that a decision is actually owed** —
   i.e. is any of the five *right* and the others wrong, or is a sixth answer needed? Phase 1
   must surface this rather than paper over it.
3. **Does the chore queue need its own board item type**, or is a marker on a well-known chore
   issue sufficient? Prefer the latter — it reuses the CAS.
4. **Should `Paths:` migrate from the issue body to a typed board field?** It would kill the
   parse family outright. Against: Projects v2 has no list type, and `Blocked by` shows what
   happens when a structured thing is stuffed into a TEXT field. Probably no — but it should be
   argued, not assumed.

---

## 8. Recommendation

Do Phase 0 **this week**, regardless of what is decided about the rest. It is cheap, it is
independent, and until the done-stamp works you cannot measure anything — including whether
any of this is helping.

Then commit to Phases 1–3. The typed model is worth it not because F# is better than bash, but
because **the domain is concurrent and transactional and the current substrate cannot represent
that**, so the same four defect classes will keep regenerating no matter how many individual
moles are whacked. #485 and #570 are already groping toward this by hand.

Phase 4 is where the money is. The rewrite is the enabling condition; **the generated
projections are the payoff.**
