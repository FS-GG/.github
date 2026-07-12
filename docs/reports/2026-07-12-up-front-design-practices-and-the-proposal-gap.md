# Up-front design & architecture — how other people fill the gap ADRs don't

- **Date:** 2026-07-12, at commit `49d221d` (main)
- **Owner:** `.github` (cross-repo coordination)
- **Status:** Research report. **No decision is taken here.** §6 proposes candidate ADRs; each would
  have to be argued on its own merits.
- **Question:** ADRs record decisions and issues track work — both *downstream* of a design. What do
  mature engineering orgs use to do the design and architecture work **up front**, and how does that
  wire into an ADR + GitHub Projects v2 system like ours?
- **Method:** Four parallel web-research passes (written-proposal processes; modelling/documentation
  tooling; collaborative discovery techniques; GitHub-native project machinery), sources cited inline
  and marked where unverified. GitHub capability claims in §5 were then **verified directly against
  the live FS-GG org** via the GraphQL schema and `gh api`, not taken from the docs.
- **Companion:** [2026-06-30 — Project-management topologies](../2026-06-30-project-management-topologies-adr-registry-projects-v2-analysis.md)
  answers *how our PM system is shaped*. This report answers *what feeds it*.

---

## 1. TL;DR

- **The gap is real and it is structural, not a discipline problem.** ADRs record decisions; issues
  track work. Neither one **generates the option space**. By the time an ADR says "we chose X,
  accepting Y," the design already happened — somewhere, in some artifact, under some review. Most
  orgs name that artifact. We have not.
- Everyone who has solved this has added **two** things, and it is worth keeping them apart: a
  **discovery activity** that produces the options (a workshop, a spike, a map), and a **proposal
  artifact with a lifecycle** that carries an option from *idea* to *decided*.
- The industry has split into **two schools** on the proposal artifact, and it is a real fork:
  **(A)** a separate RFC/design doc that the ADR later records (Google, Uber, Rust, Kubernetes), or
  **(B)** **one document whose state machine spans proposal → decision → implemented** (Oxide's RFDs;
  Harmel-Law's advice-process ADRs). School B is the one worth our attention, because in it *the ADR
  in `Proposed` state **is** the up-front design artifact* — writing it is how the decision gets made,
  not a receipt issued afterwards.
- **We are already doing school B accidentally, and school A accidentally, and neither on purpose.**
  ADR-0034 has a `**Design doc:**` link to `docs/design/coordination-engine.md` — that is school A,
  invented once, ad hoc, for one ADR. Our three `Proposed` ADRs are school B, undeclared. Both are
  fine; having neither written down is what costs us.
- **The GitHub substrate changed under us in the last 12 months and we have not noticed.** Typed
  **issue dependencies** (GA Aug 2025), **sub-issues** and **issue types** (GA Apr 2025), and
  **org-level issue fields** (GA ~May 2026) are all live in FS-GG *right now* — verified against our
  own GraphQL schema. **ADR-0034's Context contains a premise that is now false:** *"`Blocked by` is
  TEXT only because Projects v2 has no typed dependency field."* It has one. This is the single most
  actionable finding in this report (§5.2).
- **Honest negative result:** there is **no credible published account of anyone running an
  architecture process on a Projects v2 board.** ADR practice is well documented but repo-centric.
  If we build this, we are assembling from primitives — newly *feasible* (the enabling pieces are
  ~1 year old), not well-trodden.

---

## 2. Why ADRs cannot do this job alone

Nygard's 2011 ADR ([original post](https://cognitect.com/blog/2011/11/15/documenting-architecture-decisions))
is an **append-only decision log**. Its defining property is immutability: *"If a decision is
reversed, we will keep the old one around, but mark it as superseded… It's still relevant to know
that it **was** the decision, but is **no longer** the decision."*

That property is exactly what makes it a poor container for *deliberation*. A record is written by
someone who already knows the answer. The template has nowhere to put the roads not taken — which is
why the **MADR** template ([adr.github.io/madr](https://adr.github.io/madr/)) exists at all: it adds
**Considered Options** and **Pros and Cons of the Options**, and those two sections are precisely
what turn an ADR from a receipt into a proposal.

The generic failure this produces is well attested and worth naming because it is the one we are
closest to: **retroactive ADRs are near-worthless.** Context gets reconstructed, alternatives are
forgotten, and Consequences get written as *outcomes* rather than *forecasts* — which destroys the
record's whole epistemic value. An ADR that could not have been wrong when it was written is not
telling you anything.

---

## 3. The proposal artifact — the two schools

### 3.1 School A — a separate proposal doc, which the ADR later records

You write an RFC or design doc, argue it out, and the ADR is the receipt. This is what most large
orgs do. The exemplars, with publicly stealable templates:

| Process | The artifact | The mechanism worth stealing |
|---|---|---|
| **[Google design docs](https://www.industrialempathy.com/posts/design-docs-at-google/)** (Malte Ubl) | Google Doc, 10–20pp (1–3 for small work) | **Goals *and non-goals***; "the actual design" is about **trade-offs, not spec**; **Alternatives considered**. Plus an explicit test for *whether to write one at all*. |
| **[Rust RFCs](https://github.com/rust-lang/rfcs)** | `text/NNNN-*.md` via PR | **`@rfcbot concern X`** — a *blocking* concern only its raiser can resolve; resolving it **restarts the 10-day clock**. Makes "blocking objection vs. nitpick" machine-checkable instead of a social guess. |
| **[Kubernetes KEPs](https://github.com/kubernetes/enhancements)** | dir + `README.md` + **`kep.yaml`** | A **machine-readable sidecar** (`status:`, `stage:`) that CI gates on — the design doc becomes typed data. Plus an independent **Production Readiness Review** as a hard gate. |
| **[Squarespace RFCs](https://engineering.squarespace.com/blog/2019/the-power-of-yes-if)** (Tanya Reilly) | doc + review meeting | The best public writeup on *reviewing* proposals. An explicit **Approvers** field, a **Status** field, and **"yes, if" / "not yet"** conditional approval. |
| **[Amazon PR-FAQ](https://workingbackwards.com/concepts/working-backwards-pr-faq-process/)** | press release written *first* | The artifact's primary job is to **kill bad ideas cheaply**. Prose, not bullets — because prose exposes fuzzy thinking that bullets hide. |

Squarespace's list of *problems they had before* is the most useful paragraph in this whole section,
because it is a catalogue of what goes wrong when the proposal step is unnamed: designs got shallow
review; authors made *locally* optimal choices that produced incompatible systems; reviewers were shy
about asking fundamental questions; authors drowned in feedback with **no distinction between
blocking and optional**; reviewers approved **by silence**; and **nobody knew when review was over**
and implementation could start.

**Known failure modes of school A** (sourced, and they are not minor):

- **Promo-driven and retroactive docs** — written *during* implementation because it reads better
  than a day of coding ([HN on Ubl's essay](https://news.ycombinator.com/item?id=40273534)).
- **Staleness as documentation substitute** — out of date the day they're finished, but teams point
  at them instead of writing docs.
- **No follow-up mechanism.** The sharpest critique is Nick Cameron's
  [We need to talk about RFCs](https://www.ncameron.org/blog/the-problem-with-rfcs/): from a merged
  Rust RFC you **cannot tell whether it was ever implemented**. Also: stakeholders showing up only at
  the final comment period → late rework; stale RFCs never closed; real design migrating into private
  channels.
- The counterweight worth reading: Lucas Costa,
  [Design docs considered harmful](https://www.lucasfcosta.com/blog/design-docs) — *you have the
  least information at the beginning of a project, which is exactly when a design doc asks you to
  make the most decisions.* His alternative is **one-way-door analysis**: spend the rigour only on
  irreversible decisions (API contracts, schemas, service boundaries) and move fast on the rest.

### 3.2 School B — one document, whose state machine spans the whole lifecycle

**[Oxide's RFDs](https://rfd.shared.oxide.computer/rfd/0001)** ("Requests for Discussion") collapse
the split. One document; six states:

```
prediscussion → ideation → discussion → published → committed → abandoned
```

`published` = org direction. **`committed` = implemented; it now describes how the system actually
works.** There is no separate ADR, because the RFD *becomes* the record. ~500 of them in five years,
covering software, hardware, and company process. Note that `committed` is a direct answer to
Cameron's "was this ever implemented?" gap — the *frozen* record is what creates that gap, and a
state machine is what closes it.

**Andrew Harmel-Law's Architecture Advice Process** brings the same idea into ADRs proper, and is the
most relevant single body of work for us. Canon:
[Scaling the Practice of Architecture, Conversationally](https://martinfowler.com/articles/scaling-architecture-conversationally.html)
(martinfowler.com, 2021) and *Facilitating Software Architecture* (O'Reilly, Nov 2024), with a
[companion repo of the actual artifacts](https://github.com/andrewharmellaw/facilitating-software-architecture).

The rule is one sentence:

> *"Anyone can make any decision, as long as they seek advice, but not **permission**, from all those
> affected and those with expertise."*

Advice must be **heard and recorded, not obeyed**. Deciders are told to actively hunt disagreement.
No consensus, no veto, no hierarchy — but consultation is *mandatory* and the record is *public*.

Two things in [his ADR template](https://github.com/andrewharmellaw/facilitating-software-architecture/blob/main/adr/adr-template.md)
*are* the mechanism:

1. **Six states, not three:** `DRAFT → PROPOSED → ACCEPTED → ADOPTED → (SUPERSEDED | EXPIRED)`, where
   **`ADOPTED` means actually in production.** No transition is an approval gate — the decider moves
   the ADR; nobody signs off.
2. **A first-class `## Advice` section**, recorded as `[Advice offered] — [Name, Role, Date]`.
   Attributed and dated, so *"did you consult?"* and *"did you engage with what you heard?"* are both
   auditable. **Note the absence that defines the design: there is no approver field.**

The weekly **Architecture Advice Forum** is not a review board — its agenda is generated by *a
standing query over ADRs in `PROPOSED` status* (the tooling makes the agenda, not a chairperson), and
**decisions are not taken in the meeting**. The Fowler article names what it replaces — the
Architecture Review Board — and then removes its power: *"the only thing other attendees can do is
offer advice, or suggest additional people to seek advice from."*

**Evidence, in both directions.** Thoughtworks moved the architecture advice process to **Trial**
(Radar Vol. 34, Apr 2025), noting the State of DevOps finding that traditional Architecture Review
Boards are *counterproductive and correlate with low organizational performance*. The flagship case is
[Xapo Bank](https://martinfowler.com/articles/xapo-architecture-experience.html) — a Gibraltar-regulated
bank, fully remote across 40+ countries — where decisions that took *"weeks (or months!) now happen in
days."*

But read Xapo's **failure modes**, because they are the ones we would hit:

- **Consensus bias.** Despite knowing no approval was required, people kept asking *"so, do we all
  agree?"* — de-facto-approval-board drift, observed in the wild.
- **It needs a caretaker.** *"Creating a forum or structure alone is not enough."* It took **one
  full-time person**.
- **Confound:** they ran it alongside a Team Topologies restructure, CD investment and DORA metrics —
  *"it did not exist in isolation."* Anyone citing Xapo as proof must cite that caveat.

And the deepest structural critique, from Chris Richardson: the advice process only scales if each
team's consultation set is small, which requires **loose design-time coupling** — so **it presupposes
the architecture it is supposed to produce.** (This one cuts *for* us, not against: our repos are
already partitioned, which is precisely the precondition. See the 2026-06-30 topologies report.)

---

## 4. Where the options actually come from

The proposal artifact is the **container**. It does not generate content. The generator is a workshop,
a map, or a spike. Ranked by likelihood of earning their cost in a repo like ours:

**Risk-storming** (Simon Brown, [riskstorming.com](https://riskstorming.com/)) — the cheapest real
win. Draw the architecture diagram; everyone writes risks on stickies **individually and in silence**
(this is the point — it prevents anchoring); then converge, sticking each risk onto the element it
threatens, so clustering reveals the hot spots. Then review the risks **only one person saw** and the
ones people **scored differently**. Two hours, no facilitator training, and it emits exactly the input
an ADR pipeline wants: a ranked list of things worth a spike or a decision. Its honest weakness: it is
a *workshop*, not a *format* — nothing it produces is machine-readable, so the git binding is ours to
invent.

**[ddd-crew's Starter Modelling Process](https://github.com/ddd-crew/ddd-starter-modelling-process)**
(5.9k★, CC-BY) — the most complete free package anyone has published: Understand → Discover
(EventStorming) → Decompose → Strategize (Core Domain Charts) → Connect → Organise (Team Topologies) →
Define (Bounded Context Canvas) → Code. The canvases are the valuable part because they are **designed
to fail loudly**: the [Bounded Context Canvas](https://github.com/ddd-crew/bounded-context-canvas) has
an **Assumptions** section (*"you will never make design decisions having full knowledge… make them
explicit"*) and an **Open Questions** section, and the instruction is that if you cannot fill a
section you **go back to EventStorming rather than guess**. The tell for box-filling is a canvas with
no open questions.

Its cost is real and disqualifying for casual use: a Bounded Context Canvas workshop is *a full day
minimum, two preferred*, needs domain experts in the room, and **steps 1 and 4 cannot be completed by
an engineering team alone** by construction (Core Domain Charts have a business-differentiation axis
only product can supply). A team that tries produces a chart with a fabricated axis, which is worse
than no chart.

**Shape Up's "shaping"** (Basecamp, [free book](https://basecamp.com/shapeup)) — the product-side
answer, and its core insight transfers even if we never adopt the six-week cycle:

> *"Estimates start with a design and end with a number. **Appetite** starts with a number and ends
> with a design."*

Shaped work is **rough** (*"work that's too fine, too early commits everyone to the wrong details"*),
**solved** (all macro elements present and connected), and **bounded** (*it tells the team where to
stop*). The fidelity argument is the design claim: wireframes are too concrete, prose is too abstract,
so you **breadboard**. The relevance to us is direct — this is a precise description of the altitude
at which an agent-executable work item should be specified.

**The honest cross-cutting finding:** almost **none** of these methods specify their own handoff into
ADRs. The exceptions are the advice process (where the ADR *is* the unit of work) and, partially,
Architecture Haiku. Everything else has an obvious but **unspecified** bridge — and the objects that
bridge it are all the same object under different names: EventStorming's **hotspots**, the Bounded
Context Canvas's **Open Questions**, Example Mapping's **red cards**, and risk-storming's **risk
register** are each *a discovery backlog with a built-in uncertainty signal*. That is the thing that
should become an issue or a spike. **If we want "session → ADR", that is a bridge we are building,
not one we are adopting.**

---

## 5. The GitHub substrate — verified, not quoted

### 5.1 What is actually live in FS-GG today

Every row below was **verified against our own org** on 2026-07-12 (GraphQL `Issue` type fields and
`organization.issueTypes`), not read off a marketing page.

| Capability | Status | Verified how |
|---|---|---|
| **Issue dependencies** — typed `blocked by` / `blocking`, "Blocked" badge on project boards | **GA Aug 2025**; 50 links per relationship type; full API + webhooks | `Issue.blockedBy`, `Issue.blocking`, `Issue.issueDependenciesSummary` all present in our schema |
| **Sub-issues** — 100 per parent, 8 levels deep, cross-repo *and* cross-org | GA Apr 2025 | `Issue.subIssues`, `Issue.subIssuesSummary` present |
| **Issue types** — org-level, max 25, filterable with `type:` | GA Apr 2025 | `Issue.issueType` present; **FS-GG already has `Task`, `Bug`, `Feature`, `Epic` enabled** |
| **Issue fields** — typed, **org-level**, live *on the issue*, travel with it across every project and repo | GA ~May 2026; max 25 per org | the whole `IssueField*` type family is in our schema |
| `gh` CLI support for all of the above (`--blocked-by`, `--parent`, `--type`) | v2.94.0, Jun 2026 | [changelog](https://github.blog/changelog/2026-06-10-manage-sub-issues-types-and-dependencies-from-github-cli/) |

**Issue fields are the unlock.** Before them, a "Decision status" field belonged to *a board*. Now it
belongs to *the issue*, org-wide — which means an ADR issue can carry `Decision status: Proposed`
across every repo and every project view we have, without the board being the source of truth.

### 5.2 A stale premise in ADR-0034

ADR-0034's Context quotes the coordination tool against itself:

> *"`Blocked by` is TEXT only because Projects v2 has no typed dependency field."*

**That is no longer true, and has not been since August 2025.** GitHub shipped typed issue
dependencies GA; `blockedBy` / `blocking` / `issueDependenciesSummary` are in FS-GG's live schema
today. The typed coordination engine landed in #612 modelling dependency edges as free text *on a
premise that the platform had already invalidated* — and free-text `Blocked by` is exactly the class
of "prose parsed by regex" that ADR-0034 exists to eliminate.

This does not invalidate ADR-0034 — its argument (a typed core beats 4,024 lines of bash) stands
entirely, and the engine is the right place to fix this. It means **one input to it was wrong**, and
the fix is now cheap and typed. This deserves its own ADR (or an amendment), not a footnote.

The same re-check applies to `Epic` — we already have the issue type enabled, so epic decomposition
does not need labels or naming conventions.

### 5.3 Two patterns worth copying

- **[Astro's staged funnel](https://github.com/withastro/roadmap)** — **Stage 1 = a GitHub
  Discussion** (cheap, no ceremony, no tracking), **Stage 2 = an Issue** (accepted, has a champion),
  **Stage 3 = an RFC as a PR** against `proposals/NNNN-*.md`. Discussions are for the messy front of
  the funnel and get **promoted to an issue the moment they need tracking**. This is forced by a
  platform limit, not taste: **Discussions have no status field and cannot go on a Projects board.**
  Angular works around it by encoding state in the *title* (`[Watch This Space]` → `[Complete]`),
  which tells you the limitation is real.
- **[Ember's RFC stages](https://rfcs.emberjs.com/id/0617-rfc-stages/)** — each stage transition is
  **its own PR** with its own comment period, and the stage lives in the document's frontmatter. The
  record itself carries lifecycle state. This is the closest published thing to what our ADR
  `Status:` line already gestures at.

### 5.4 The limits that will bite

- **There are no field-level permissions.** Anyone with write access can flip a "Decision status"
  field to Accepted. **Governance must live in PR review + `CODEOWNERS` on `docs/adr/`, with the
  board as a *view* of that truth — never the truth itself.** (This is already our instinct; it is
  now also a documented constraint.)
- **No rollups.** No automatic sum or percentage of child estimates into a parent issue.
- **Built-in project automation is weak** — one auto-add workflow per repository. Anything
  cross-repo needs an Action.
- **The "architecture board" pattern is essentially unwritten.** I searched for it specifically. ADR
  practice is well documented but repo-centric; GitHub's own dogfooding post is about release
  management. The [GDS guidance](https://gds-way.digital.cabinet-office.gov.uk/standards/architecture-decisions.html)
  actively argues *against* a separate status field, on the grounds that **the PR's state *is* the
  decision status**. Reasonable people disagree with the thing §6 proposes.

---

## 6. What this means for FS-GG

We already have both ends — ADRs (`docs/adr/`, 34 of them) and a Coordination board. What we do not
have is a **named, declared front half**. Observably:

- **`docs/design/` contains exactly one file** (`coordination-engine.md`), created for ADR-0034, which
  links to it as a `**Design doc:**`. That is school A — invented once, for one ADR, undeclared as a
  convention. Nothing says when a design doc is required, what is in one, or what state it is in.
- **Our ADR status vocabulary is `Proposed | Accepted | Superseded`.** Three of 34 are `Proposed`.
  There is **no state meaning "in production"** — so the `ADOPTED` gap Oxide and Harmel-Law both close
  is open in our log, and "was this ever actually implemented?" is answerable only by reading the repo.
- **There is no `## Advice` or `## Considered Options` section in our template.** Alternatives are
  argued in PR threads, which are not part of the record and are not attributable after the fact.

Candidate ADRs this suggests — **each to be argued separately, none decided here**:

1. **Adopt a six-state ADR with an `## Advice` section.** `DRAFT → PROPOSED → ACCEPTED → ADOPTED →
   (SUPERSEDED | EXPIRED)`, plus `## Considered Options` (MADR) and attributed, dated advice entries.
   Smallest change, biggest payoff: it makes the ADR the *design artifact* rather than the receipt, it
   closes the "was it implemented" gap with `ADOPTED`, and in a multi-repo org where the affected
   party is usually *another repo's owner*, it makes **"did you consult the affected repos?"
   auditable** — which is precisely the question our coordination protocol exists to answer and
   currently answers only in prose.
2. **Retire free-text `Blocked by` in favour of typed GitHub issue dependencies** (§5.2). This is a
   correction to an ADR-0034 input, it removes a regex-over-prose surface from the engine that ADR-0034
   was written to detypify, and the platform support is live and CLI-addressable today.
3. **Declare the design-doc tier** — say when `docs/design/` is required (my read of the evidence:
   when the decision is a **one-way door** — contract, boundary, or schema), what shape it takes, and
   that its terminal state is an ADR. Or explicitly decide *not* to have the tier and fold it into a
   longer ADR. Either is defensible; the current state — one file, no rule — is not.

**What I would not do:** adopt DDD strategic-design workshops (they need product stakeholders we
cannot convene, and steps 1 and 4 are uncompletable without them), or add an approval board (the
evidence runs actively against it). Start with **risk-storming**, which costs two hours and produces
the discovery backlog the ADR pipeline is currently starved of.

---

## 7. Landmines — tooling that looks alive and isn't

Flagged because each would waste a day if adopted from a stale tutorial:

- **Structurizr was restructured out from under everyone (Jan–Feb 2026).** `structurizr/cli`,
  `structurizr/lite` and `structurizr/java` are **archived**; the cloud service went **read-only
  2026-07-01**. Everything consolidated into
  [structurizr/structurizr](https://github.com/structurizr/structurizr). Any C4-as-code tutorial you
  find predates this. [LikeC4](https://github.com/likec4/likec4) is the moving alternative — with the
  same bus-factor-of-one problem.
- **`npryce/adr-tools` (5.5k★) is effectively unmaintained** — last release **3.0.0, Jul 2018** — but
  it is **not archived and carries no deprecation notice**, so it looks alive. **MADR** is the
  maintained option.
- **Mermaid's C4 support is still officially experimental** and has been for years (*"the syntax and
  properties can change in future releases"*). Fine for a sketch in a PR; wrong for a durable model.
- **D2's maintenance has stalled** — the maintainer joined OpenAI (Apr 2026) and is seeking a
  non-profit home for it.

---

## 8. Sources

The load-bearing ones, in the order they matter:

- Andrew Harmel-Law, [Scaling the Practice of Architecture, Conversationally](https://martinfowler.com/articles/scaling-architecture-conversationally.html)
  · [artifacts repo](https://github.com/andrewharmellaw/facilitating-software-architecture)
  · [Xapo Bank case study](https://martinfowler.com/articles/xapo-architecture-experience.html)
  · [Thoughtworks Radar — Architecture advice process (Trial, Apr 2025)](https://www.thoughtworks.com/radar/techniques/architecture-advice-process)
- [Oxide RFD 1 — Requests for Discussion](https://rfd.shared.oxide.computer/rfd/0001)
- Malte Ubl, [Design Docs at Google](https://www.industrialempathy.com/posts/design-docs-at-google/)
  · Tanya Reilly, [The Power of "Yes, if"](https://engineering.squarespace.com/blog/2019/the-power-of-yes-if)
  · Nick Cameron, [We need to talk about RFCs](https://www.ncameron.org/blog/the-problem-with-rfcs/)
  · Lucas Costa, [Design docs considered harmful](https://www.lucasfcosta.com/blog/design-docs)
- [MADR](https://adr.github.io/madr/) · Nygard, [Documenting Architecture Decisions](https://cognitect.com/blog/2011/11/15/documenting-architecture-decisions)
- [Simon Brown — Risk-storming](https://riskstorming.com/) · [ddd-crew Starter Modelling Process](https://github.com/ddd-crew/ddd-starter-modelling-process)
  · [Shape Up](https://basecamp.com/shapeup)
- GitHub: [Dependencies on issues (GA, Aug 2025)](https://github.blog/changelog/2025-08-21-dependencies-on-issues/)
  · [Evolving GitHub Issues and Projects (GA, Apr 2025)](https://github.blog/changelog/2025-04-09-evolving-github-issues-and-projects/)
  · [Sub-issues, types and dependencies from the CLI (Jun 2026)](https://github.blog/changelog/2026-06-10-manage-sub-issues-types-and-dependencies-from-github-cli/)
  · [Astro roadmap (staged funnel)](https://github.com/withastro/roadmap)
  · [Ember RFC stages](https://rfcs.emberjs.com/id/0617-rfc-stages/)
