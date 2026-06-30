---
title: Project-management topologies for an ADR + registry + Projects v2 system
category: FS.GG
categoryindex: 6
index: 20
description: Theoretical and practical comparison of two topologies — one repo with many Projects, vs many repos with a coordination layer (ours) — for an ADR/registry/GitHub Projects v2 project-management system, with emphasis on running many agent sessions concurrently.
date: 2026-06-30
---

# Project-management topologies for an ADR + registry + Projects v2 system

**Analysis date:** 2026-06-30
**Author:** automated analysis (Claude Code)
**Scope:** how FS-GG manages cross-cutting work with **ADRs** (decisions),
a **contract/compatibility registry** (durable facts), **cross-repo issues**
(requests), and a **GitHub Projects v2** board (sequencing) — and how that
machinery behaves under two repository topologies, with particular attention to
**multiple agent sessions running simultaneously**.

This is a design-rationale document, not a how-to. The operational protocol lives
in [`docs/coordination/README.md`](coordination/README.md); the decision that
established it is [ADR-0001](adr/0001-cross-repo-coordination-via-issues.md); the
durable facts live in [`registry/dependencies.yml`](../registry/dependencies.yml)
and its projection [`docs/registry/compatibility.md`](registry/compatibility.md).

---

## TL;DR

- The FS-GG project-management system is **four orthogonal artifacts**, each
  owning exactly one question: ADRs own *why*, the registry owns *what*
  (versioned contract surfaces + coherence), cross-repo issues own *requests*,
  and the Projects v2 board owns *when/order/who-is-blocked*. This separation is
  topology-independent — it works in one repo or many.
- **Topology A (one repo, many Projects)** centralizes everything into a single
  git history and CI domain. It gives *atomic cross-cutting changes* and
  *strong consistency* for free, but it serializes work: every agent contends on
  one main branch, one lockfile graph, one CI queue, and one blast radius.
- **Topology B (many repos, one Coordination board — ours)** trades atomicity
  for *isolation and parallel throughput*. Each repo is an independent unit of
  failure, release, and ownership; coordination becomes explicit, asynchronous
  message-passing (issues) over an explicitly-versioned interface (the registry).
- **For many concurrent agent sessions, Topology B is materially better**:
  agents partition by repo, write to disjoint git histories, and never block
  each other at the VCS layer. The only shared-write hotspots — the registry file
  and the board — are small, owner-partitioned, and off the critical build path.
  Topology A's single main branch is a global lock that every agent must acquire.
- The cost of B is **coordination latency** (async handoff instead of one commit)
  and the need to keep an explicit coherence invariant from silently drifting —
  which is exactly what the registry + the [contract-coherence gate](coordination/contract-coherence-gate.md)
  exist to make impossible.

---

## 1. Theoretical background

### 1.1 Coordination is layered — separate the questions

The core idea behind the FS-GG system is that "project management" is not one
thing; it is several questions that decay at different rates and have different
owners. Collapsing them into one tool (a wiki, a mega-issue, a shared planning
doc) is the classic failure mode: the *why* gets lost in comment threads, the
*what* drifts from reality, and the *when* is never authoritative.

The system keeps them as four artifacts, each the single source of truth for one
question (this is the layer table from the `cross-repo-coordination` skill):

| Layer | Question | Tool | Decay rate | Owner |
|---|---|---|---|---|
| Decisions | *why?* | **ADRs** (`docs/adr/`) | very slow (append-only history) | author of the decision |
| Contracts | *what is the versioned surface? is it coherent?* | **registry** (`registry/dependencies.yml`) | slow (changes per contract revision) | the producing repo, per row |
| Messages | *what do I need from you, right now?* | **cross-repo issues** | fast (transient; closed when resolved) | the target repo |
| Sequencing | *when / in what order / who is blocked?* | **Projects v2 board** | continuous (re-ordered constantly) | whoever plans the roadmap |

(There is a fifth, repo-local layer — `specs/<feature>/` — that owns the *detail*
of one unit of work inside a single repo; it is out of scope for cross-repo
topology.)

The discipline is: **never answer a layer's question with another layer's tool.**
A reversed decision edits an ADR (or supersedes it); a changed surface edits the
registry; a re-prioritization moves a card on the board; a new ask opens an issue.
This is what lets the system stay coherent while many actors touch it — each write
has exactly one correct destination, so two actors rarely target the same bytes.

### 1.2 Git is a DAG, not a queue

[ADR-0001](adr/0001-cross-repo-coordination-via-issues.md) explicitly rejected a
file-based "mailbox" folder for cross-repo requests, and the reasoning is the
theoretical heart of the design. Git history is an **append-only directed acyclic
graph of immutable snapshots**. It is excellent at recording *what the tree was*;
it is poor at being a *communication channel between concurrent writers*:

- **No notification.** Writing a file notifies no one. An issue notifies the
  assignee, the watchers, and any `gh`-polling agent.
- **Concurrent writers conflict.** Two actors appending to `mailbox/requests.md`
  on the same base commit produce a merge conflict — a textual collision that has
  nothing to do with the semantics of their two messages. A queue should never
  make independent producers block each other.
- **No identity / addressing.** A message in a file has no stable handle. An issue
  is content-addressable (`FS-GG/<repo>#<n>`), so it can be referenced, threaded,
  reopened, and made idempotent — re-running an agent that already filed `#42`
  does not create a duplicate `#42`.
- **No status machine.** Open/closed, assigned, labelled, and searchable come for
  free with issues; a file would reinvent all of them, badly.

So the design uses **GitHub-native primitives as the coordination substrate** and
reserves git for what it is good at: durable, reviewable facts (ADRs, the registry
YAML, code). This distinction — *queue-shaped traffic → issues; ledger-shaped
facts → versioned files* — is the single most important architectural choice, and
it is what makes concurrency safe (see §4).

### 1.3 Contract-driven decoupling and the registry as a coherence oracle

The FS-GG repos are "deliberately decoupled but coupled at the edges through
versioned contracts" (`scaffold-provider`, `fs-gg-ui-template`, `governance-*`,
`fsgg-contracts`, …). This is the **interface-segregation** idea applied at the
repository scale: a repo depends not on another repo's *source* but on a named,
**SemVer-versioned surface** published as a package or file schema.

Two repos are *coherent* when the version a consumer pins actually satisfies the
surface the producer ships. Coherence is a **global invariant over a distributed
system** — exactly the kind of property that silently breaks because no single
actor can see all of it at once. The motivating incident was precisely this: the
`fs-gg-ui` template pinned `0.1.0-preview.1` while the framework HEAD had moved to
`0.1.46+` with a refactored Scene API, and *no release tag and no notification
path* connected the two. The build broke at the consumer, far from the cause.

The **registry is the explicit serialization of that invariant** — a single
machine-readable file that enumerates every edge (`from → to → via@version`),
every versioned contract, and every `coherence: true|false` row. It turns an
implicit, undiscoverable global property into a **checkable oracle**:

- `coherent: false` is, by convention, *a standing cross-repo request* — a TODO
  with an owner and a tracking issue.
- The reusable [contract-coherence gate](coordination/contract-coherence-gate.md)
  (`workflow_call`) makes every repo's CI go **red** when its real pins or
  build-config stop matching the registry's declared values. The invariant is no
  longer upheld by vigilance; it is upheld by a gate.
- The [auto-update fabric](coordination/auto-update-fabric.md) (dispatch-sender +
  Renovate preset) keeps the pins *fresh* so they rarely drift in the first place.

The registry is the durable-facts counterpart to the transient issue stream: the
issue says "please bump me"; the registry records "we are now coherent at
`0.1.53-preview.1`", with the ADR recording "and here is why we compose by
scaffold instead of vendoring."

### 1.4 Conway's law: topology mirrors ownership

> Organizations design systems that mirror their own communication structure.

Repository topology is an *encoding of the ownership and communication structure*
you want. The [project-split decision](project-split-decision.md) chose many repos
specifically to **lower cognitive load and shrink blast radius**: rendering
contributors should not need to understand the governance schema to ship a UI
change, and a governance experiment should not be able to block rendering work.

That is a Conway's-law argument: the team wants *independent* evolution of
rendering, lifecycle, governance, and templates, so the system is split along
those exact seams, and the seams are made explicit as versioned contracts. The
alternative — one repo — encodes the opposite: *everything evolves together,
atomically, under one review and release process.* Neither is universally right;
each is right for a particular communication structure. The split doc's own
con-list ("cross-repo coordination becomes explicit work") is the price of
choosing decoupling, and the ADR/registry/board machinery is the payment.

### 1.5 A distributed-systems lens

It is useful to read the two topologies as two points on a consistency/throughput
trade-off, analogous to (not literally) CAP:

- **One repo = a single strongly-consistent store.** There is one main branch =
  one linearizable log. Any cross-cutting change is *atomic* (one commit touches
  rendering + governance + the registry together) and *immediately consistent*.
  The price is **contention**: every writer competes for that one log, and the
  system's write throughput is bounded by how fast you can serialize merges into
  one main.
- **Many repos = a partitioned, eventually-consistent system.** Each repo is its
  own consistency domain; cross-repo state converges *asynchronously* through
  issues (messages) and the registry (a shared, owner-partitioned ledger). The
  price is **coordination latency** and the possibility of *transient
  incoherence* (a window where consumer and producer disagree) — which the
  registry surfaces and the coherence gate refuses to let merge.

The whole point of the registry + gate + auto-update fabric is to **buy back the
consistency** that partitioning gives up, *without* giving up the parallel
throughput that partitioning provides. That is the deal the rest of this document
evaluates — especially under many concurrent agents, which is a write-throughput
problem.

---

## 2. Topology A — one repo, many Projects v2

**Shape.** A single repository (a monorepo) holds rendering, SDD, governance, and
templates as directories. Project management uses **many Projects v2 boards** —
e.g. one per product area or workstream — all backed by issues in the one repo.
ADRs live in one `docs/adr/`; a registry, if it exists at all, is one file with
no cross-repo edges to reconcile (the "edges" are now internal module boundaries
enforced by the build).

**Mechanics.**

- Coordination *within* the repo needs no issue protocol: a cross-cutting change
  is one branch, one PR, one CI run, one merge. The "registry" degenerates into
  ordinary build-time dependency resolution (project references), so a large part
  of the coherence problem simply does not exist — the compiler is the oracle.
- Many Projects boards are used to *slice* the single issue pool by lens
  (per-area Kanban, a release roadmap, a bug triage view). They are *views*, not
  *coordination boundaries*.
- ADRs are still valuable but are repo-local decisions, not cross-repo treaties.

**Pros.**

- **Atomic cross-cutting change.** Rename a contract and update every consumer in
  one commit. No registry, no `contract-change` issue, no version dance, no
  transient incoherence window. This is the single biggest advantage.
- **Strong consistency by construction.** `main` is always internally coherent or
  it does not build. The compiler/test suite *is* the coherence gate.
- **One source of truth for everything.** One clone, one CI config, one docs site,
  one search domain. Onboarding is "clone this."
- **Refactoring is cheap.** Module boundaries can move freely because they are not
  frozen as published, versioned, externally-consumed surfaces.
- **No release-lag class of bug.** The `fs-gg-ui-template@0.1.0` vs framework
  `0.1.46` drift is *impossible* — there is only HEAD.

**Cons.**

- **One blast radius.** A bad merge reds the build for *everyone*, in every area,
  immediately. There is no failure isolation between rendering and governance.
- **One CI queue / one main = a global write lock.** Throughput is bounded by the
  rate at which changes can be serialized into a single main branch (see §4).
- **Coupled release cadence.** You cannot ship a rendering fix without dragging
  along whatever else is on main. Independent versioning is hard; the project-split
  doc rejected exactly this ("independent release cadence for runtime, lifecycle,
  and rule tooling" was a goal).
- **Cognitive load.** Every contributor (and every agent) sees the whole system.
  The split doc's primary motivation — "lower cognitive load for runtime
  contributors" — is given up.
- **Boards-as-views invite drift between boards.** Many Projects over one issue
  pool means an issue can be on three boards with three different `Status` values;
  without discipline the boards disagree about reality.
- **Governance becomes load-bearing too early.** The original FS-GG concern: a
  monolith makes you "develop a changing UI framework on top of a changing
  governance framework" — a recursive maintenance cost.

---

## 3. Topology B — many repos, one Coordination board (ours)

**Shape.** Five repos — `FS.GG.SDD`, `FS.GG.Rendering`, `FS.GG.Governance`,
`FS.GG.Templates`, and `.github` (org-level home of the protocol, registry, ADRs,
shared build config, and reusable workflows). One **org-level Projects v2 board
named `Coordination`** spans all five and is the single sequencing layer; per-repo
milestones are kept only for repo-local release cuts.

**Mechanics** (the four layers in practice):

- **Decisions** → ADRs in `.github/docs/adr/`. ADR-0001 mandates the board;
  ADR-0002 retires vendoring in favour of scaffold composition; ADR-0005 fixes
  `.fsgg/` slot ownership; etc. These are *treaties* between repos.
- **Contracts** → `registry/dependencies.yml` + `docs/registry/compatibility.md`.
  Each row has an **`owner`** (the producing repo), a version, and a coherence
  state. A `contract-change` issue *must* update the registry as part of its
  resolution.
- **Messages** → cross-repo issues filed *in the target repo* with `cross-repo` +
  `cross-repo:request` labels, responded to with a `## Response` comment, resolved
  by closing (ideally via a linked PR).
- **Sequencing** → the `Coordination` board, with custom fields `Status`, `Phase`
  (P0…P5), `Repo`, `Workstream`, `Start`/`Target`, `Effort`, `Contract`, and
  `Blocked by`. Epics are Phase parents; sub-issues roll up.

**Pros.**

- **Failure isolation.** A red build in Governance does not block Rendering. Each
  repo is an independent unit of failure, release, and review. This is the central
  win and the explicit goal of the split.
- **Independent cadence + parallel throughput.** Each repo has its own main, its
  own CI, its own release. N repos ≈ N independent serialization domains, so N
  streams of work (or N agents) proceed without contending at the VCS layer (§4).
- **Bounded cognitive load.** A rendering contributor (or agent) loads the
  rendering repo and its published upstream surfaces — not the governance schema.
- **Explicit, auditable coupling.** Every cross-repo dependency is a named,
  versioned, registered edge with an owner and a tracking issue. "Who depends on
  whom, and is it coherent?" is answerable from one file instead of by reading
  five build graphs.
- **The coherence invariant is enforced, not hoped.** The contract-coherence gate
  reds CI on drift; the auto-update fabric keeps pins fresh; `coherent: false` is a
  first-class, owned, tracked request. The very bug class that motivated the system
  (`fs-gg-ui` drift) is now *structurally* caught (`lockfile-restore-enforcement`,
  `apicompat-publicapi-gate`, the version-coherence guard).
- **GitHub-native coordination.** Requests are notified, threaded, assignable,
  searchable, and `gh`-scriptable — ideal for autonomous agents (§4).

**Cons.**

- **Cross-cutting change is multi-step and asynchronous.** Renaming
  `productName → name` (ADR-0005) or retiring the vendored monolith (ADR-0002) is
  a *campaign*: an ADR, registry edits, a `contract-change` issue per consumer, a
  release, pin bumps, and a coherence flip — sequenced as P0…P5 on the board. The
  registry's own history shows how much prose this generates.
- **Transient incoherence is possible.** Between a producer release and a consumer
  pin-bump there is a window where the registry says `coherent: false`. The system
  *manages* this window (gate + auto-update) rather than eliminating it.
- **Coordination is real, ongoing work.** Someone (or some agent) must keep the
  registry, board, and ADRs honest. Drift between the board and the issues, or
  between the registry annotation and the contract block, is a recurring chore
  (the 2026-06-30 `templates→rendering` edge reconciliation is an example of exactly
  this projection-drift being fixed).
- **Higher infrastructure surface.** Reusable workflows, an org GitHub App for
  cross-repo dispatch, a Packages feed, a Renovate preset, label sync — all must
  exist and be provisioned (the H4 `#21` admin step gated several of these).

---

## 4. The concurrency question — many simultaneous agent sessions

This is the decisive axis for an agent-driven workflow, and it is fundamentally a
**write-throughput and contention** question. Treat each agent session as an
independent writer that wants to (a) read state, (b) make changes, (c) commit and
push, and (d) coordinate hand-offs. The two topologies behave very differently.

### 4.1 Inventory of shared mutable state

| State | Topology A (one repo) | Topology B (many repos) |
|---|---|---|
| `main` branch / git history | **One** — every agent's merges serialize here | **N** — one per repo, disjoint |
| CI queue | One | N (per repo) |
| Lockfile / build-config graph | One shared graph; any edit touches everyone | Per-repo; only the owner's repo is touched |
| Registry file | (often absent / trivial) | **One** `dependencies.yml` — the shared hotspot |
| Projects v2 board(s) | Many boards, one issue pool | One `Coordination` board |
| Issue namespace | One repo's issues | Per-repo issues (content-addressed `repo#n`) |

The key observation: in Topology A the **biggest, hottest shared resource is
`main` itself** — a resource every agent must write to. In Topology B `main` is
partitioned N ways, and the only genuinely shared writable artifacts are the
registry file and the board, both of which are *small, owner-partitioned, and off
the build's critical path.*

### 4.2 Contention analysis

**Topology A — agents contend on one main.**

- Two agents editing overlapping files produce **merge conflicts** that a human or
  a retry loop must resolve. As agent count rises, the probability that any two
  touch the same file (or the shared lockfile / `Directory.Packages.props`)
  approaches 1 — and a CPM lockfile is a *global* file, so almost any dependency
  change collides.
- CI is a **single queue**; agents wait behind each other. A red main from one
  agent **blocks every other agent's merge** until it is green again — a global
  stall, not a local one.
- Worktrees (`git worktree`, the harness's per-agent isolation) help agents *work*
  in parallel, but they all still **converge on one main at push time**. Isolation
  during editing does not remove the serialization point at integration.
- Net: throughput is bounded by *serialized integration into one main*, and the
  failure of any one agent has a *global* blast radius.

**Topology B — agents partition by repo.**

- Assign each agent (or agent fleet) a repo. Their writes go to **disjoint git
  histories**; they never conflict at the VCS layer, never wait in each other's CI
  queue, and a red build in one repo never blocks a merge in another. This is the
  **lock-free, partitioned** ideal for parallel writers.
- Cross-repo hand-offs are **messages, not shared writes**. Agent-in-Templates
  needs a bump from Rendering → it *files an issue* in Rendering and continues or
  yields. Agent-in-Rendering picks it up, responds, releases, closes. No file is
  co-written; the issue is the lock-free, idempotent, content-addressed channel
  (§1.2). This is message-passing concurrency (CSP-style) rather than shared-memory
  concurrency — the model that scales without locks.
- **The two real hotspots and why they stay cool:**
  - *The registry file.* It is the one file multiple repos' agents may edit. But
    every row has an **`owner`**, so the convention is *single-writer-per-row*:
    the Rendering agent edits the `fs-gg-ui-template` row, the SDD agent edits the
    `fsgg-contracts` row. Conflicts are then confined to the YAML's shared spine
    (the top-of-file `updated:` annotation, list ordering), which is small and
    mergeable. The registry is a **serialization point by design** — it is where
    the global coherence invariant is made consistent — but it is a *narrow* one,
    written rarely (only on `contract-change`), not on every code edit.
  - *The Projects v2 board.* Field edits are **item-scoped**; two agents editing
    two different cards do not collide. Two agents editing the *same* field of the
    *same* card get GitHub's API last-write-wins — a lost update, not a corruption
    or a merge conflict. The board is a coordination *convenience*, never on the
    code critical path, so a lost field update is cheap to detect and redo.

### 4.3 Blast radius and failure isolation

- **A:** one bad agent commit → global red → all agents blocked. Failure is *not*
  isolated; the system's reliability is the *minimum* over all agents.
- **B:** one bad agent commit → that repo red → only that repo's agents blocked;
  cross-repo consumers keep working against the *last good released pin* (not HEAD),
  because they depend on **published versions, not source**. Decoupling at the
  version edge is also *fault* decoupling. The registry's `coherent: false` is how a
  failure is *announced* across the partition without coupling the builds.

### 4.4 Consistency vs latency for agents

The trade reappears at the agent level:

- **A** gives an agent a *consistent global read*: HEAD of one repo is the whole
  truth. An agent never reasons about "is the thing I depend on released yet?"
- **B** gives an agent a *fast, isolated write* but an *eventually-consistent
  read*: an agent in Templates may see `coherent: false` and must decide to wait,
  file a request, or proceed against the last good pin. The registry exists so this
  decision is made against an explicit, machine-readable state instead of guesswork
  — and the coherence gate guarantees the agent cannot *merge* an incoherent pin
  even if it tries.

### 4.5 Practical guidance for an agent fleet on Topology B

1. **Partition by repo, one owning agent (or fleet) per repo per campaign.** This
   keeps VCS writes disjoint and is the single most important rule.
2. **Hand off across repos with issues, never by co-editing a file.** Use the
   `Cross-repo request` template; reference `repo#n`, shas, and the contract id.
   Issues are idempotent — a retried agent that re-files is deduplicable.
3. **Treat the registry as a guarded, single-writer-per-row resource.** Only the
   row's `owner` agent edits that row; batch the edit into the same PR that lands
   the contract change; rebase rather than co-write the shared `updated:` spine.
4. **Treat the board as advisory state.** Reconcile it *after* the authoritative
   issue/registry write lands, accept last-write-wins on fields, and never block a
   code merge on a board update.
5. **Let the gate be the backstop.** Agents will occasionally race; the
   contract-coherence gate, lockfile-locked restore, and apicompat gate are what
   make a racing agent's incoherent state *fail to merge* rather than *ship*.
6. **Use the `coherent: false` convention as a work queue.** An agent can scan the
   registry for `false` rows + their tracking issues to find the next piece of
   cross-repo work without human dispatch.

### 4.6 Verdict on concurrency

For *many simultaneous agent sessions*, **Topology B (ours) is the better fit by a
wide margin**, for the same reason sharded/partitioned systems beat a single-writer
log under write load: it removes the global serialization point (one `main`) and
replaces shared-memory coordination (co-writing files) with message-passing (issues)
over an explicitly-versioned interface (the registry). Topology A's atomic-change
advantage is real, but it is an advantage for *one* coordinated writer making a
sweeping change — not for *N* independent writers, where its single main becomes the
bottleneck and its single blast radius becomes the reliability ceiling.

The residual risk in B — transient incoherence while agents race across partitions —
is precisely the risk the registry makes visible and the coherence gate makes
unmergeable. In other words, the FS-GG design already pays the concurrency tax in the
right currency.

---

## 5. Decision matrix

| Dimension | One repo, many Projects (A) | Many repos, one board (B) |
|---|---|---|
| Atomic cross-cutting change | ✅ one commit | ❌ multi-step campaign |
| Cross-cutting read consistency | ✅ strong (HEAD = truth) | ⚠️ eventual (registry-mediated) |
| Failure isolation / blast radius | ❌ global | ✅ per-repo |
| Independent release cadence | ❌ coupled | ✅ independent |
| Cognitive load per contributor/agent | ❌ whole system | ✅ one repo + upstream surfaces |
| Refactoring freedom of internal boundaries | ✅ cheap | ❌ frozen as versioned surfaces |
| **Parallel write throughput (many agents)** | ❌ bounded by one main | ✅ N independent mains |
| **Agent hand-off model** | shared-memory (co-edit) | ✅ message-passing (issues) |
| **Contention hotspots** | one main + one lockfile | registry row + board field (narrow) |
| Coordination overhead | ✅ minimal | ❌ explicit, ongoing |
| Risk of silent drift | ✅ low (compiler is oracle) | ⚠️ managed by registry + gate |
| Infra surface (workflows, App, feed) | ✅ small | ❌ large |

**Rule of thumb.** Choose **A** when the system is small, evolves as one unit,
ships on one cadence, and is worked by a *small, coordinated* set of writers who
benefit from atomic sweeping changes. Choose **B** when you want independent
evolution, failure isolation, and — decisively for this project — **high parallel
throughput from many independent (agent) writers**, and you are willing to invest
in an explicit coherence mechanism (registry + gate + auto-update) to buy back the
consistency that partitioning costs.

---

## 6. How FS-GG maps, and recommendations

FS-GG is squarely **Topology B**, and the choice is well-matched to its goals
(independent cadence, bounded cognitive load, governance that earns adoption from
outside) and especially to an agent-driven workflow with many concurrent sessions.
The machinery that makes B safe is already built and largely proven:

- the four-layer separation is documented and enforced by convention;
- the registry + the contract-coherence gate make the global invariant checkable
  and unmergeable-when-broken;
- `lockfile-restore-enforcement`, `apicompat-publicapi-gate`, and the
  version-coherence guard make the original drift bug class *structurally*
  impossible, not merely discouraged;
- the auto-update fabric keeps pins fresh so the incoherence window stays small.

Recommendations to strengthen B specifically for concurrent agents:

1. **Codify single-writer-per-row for the registry** in the coordination README
   (it is currently implied by `owner`, not stated as a concurrency rule). Consider
   splitting `dependencies.yml`'s top-of-file `updated:` annotation history into a
   separate append-only changelog so the one truly shared line stops being a
   merge hotspot.
2. **Make `coherent: false` a machine-readable agent work queue.** A tiny script
   (`gh`-scriptable) that lists `false` rows + their tracking issues + board cards
   would let an idle agent self-dispatch the next cross-repo task.
3. **Add a board↔issue↔registry reconciliation check** to CI (the 2026-06-30 edge
   drift and the epic-#16 Start-date drift were both manual catches). A periodic
   job that flags board cards whose `Status`/`Blocked` disagree with their issue
   state, or registry rows whose annotation disagrees with the contract block,
   would turn a recurring chore into a gate.
4. **Document the agent-fleet partitioning rule** (§4.5) in the
   `cross-repo-coordination` skill so multi-session runs default to repo-partitioned
   ownership and issue-based hand-off rather than racing on shared files.
5. **Keep flipping the last `coherent: false` rows** (`reference-gate-set-published`,
   `cross-repo-auto-update`) — they are the remaining proofs that the partitioned
   model converges, and closing them retires the last manual steps in the fabric.

---

## Appendix: glossary

- **ADR** — Architecture Decision Record; the *why* layer. Append-only,
  numbered, in `docs/adr/`.
- **Registry** — `registry/dependencies.yml` (+ `docs/registry/compatibility.md`
  projection); the *what/coherent?* layer. Owner-partitioned rows.
- **Contract** — a named, SemVer-versioned cross-repo surface (a package or a
  file schema) that decouples a consumer from a producer's source.
- **Coherence** — the global invariant that every consumer's pinned version
  satisfies the producer's shipped surface. `coherent: false` = a standing,
  owned, tracked cross-repo request.
- **Coherence gate** — a reusable `workflow_call` CI job that reds a repo's build
  when its real pins/config diverge from the registry.
- **Cross-repo issue** — the *request* layer; a GitHub issue filed in the target
  repo, the lock-free message-passing channel between repos.
- **Coordination board** — the org-level Projects v2 board; the *when/order/blocked*
  layer.
- **Blast radius** — the set of work that a single failure (a red build) blocks.
  Global in A; per-repo in B.
- **Serialization point** — a resource all writers must funnel through. `main` in
  A; the registry file (narrowly, per-row) in B.
</content>
</invoke>
