# Consumer documentation roadmap — READMEs and the org front door

- **Date:** 2026-07-21, hub at `.github@b078103` (main)
- **Owner:** `.github` (cross-repo coordination) — but most milestones land in the component repos.
- **Status:** Design + roadmap. No decision is taken here; §7 is a sequence of filable epics, each of which would be argued and tracked on the Coordination board like any other work.
- **Audience of *this* doc:** whoever drives the doc work (agent or human). **Audience of the docs it plans:** humans evaluating and *consuming* FS-GG — not the agents that build it.
- **Question:** FS-GG is an agent-developed platform whose consumer-facing docs have not kept pace with the code. The build/dev-process docs are dense and current; the *"what is this, what can it do, how do I get it, how do I use it"* docs are stale or missing. What is the smallest sequence of work that makes every repo's README, and the org front door, a good product page for a human consumer?
- **Method:** two parallel doc audits (org-level docs; all 7 component READMEs + their `docs/`), cross-checked against the one up-to-date narrative (`docs/architecture.md`) and the machine source of truth (`registry/dependencies.yml`, `renovate.json`, per-repo `nuget.config`). Ground-truth acquisition facts in §2 come from those files, not from the prose being audited.

---

## 1. TL;DR

- **The docs describe a platform that no longer exists.** Every consumer-facing narrative was written for a **four-component / five-repository** platform and was never updated when **Game, Audio, and Net** were added. The platform is now **seven framework components (eight repositories including `.github`)**. The phrase "five repositories" or "four components" is wrong in **the org profile page, the repo README, `docs/index.md`, four component READMEs (SDD, Rendering, Governance, Templates), and five of the nine consumer-guide files.**
- **Three published libraries are effectively unbuyable from their own docs.** `FS.GG.Game.*`, `FS.GG.Audio.*`, and `FS.GG.Net.*` are all **published to nuget.org** (public, anonymous) — but **none of their READMEs tell a consumer how to install or reference a package, and none show a first-usage snippet.** Game and Net don't even state that they *are* published; Net has no `docs/` directory at all.
- **The good news: the acquisition story is actually simple, and already true.** Since ADR-0039, **all 32 `FS.GG.*` packages are public on nuget.org and readable with no credential.** A consumer needs `dotnet tool install` or `dotnet add package` and nothing else. The docs make this sound harder than it is (some still point at the credentialed GitHub Packages feed).
- **This rotted for a structural reason, and will rot again unless we fix that too.** Component counts and version numbers are **hand-typed into prose in a dozen places.** That is the same hand-copied-derived-fact anti-pattern the [2026-07-20 coordination root-cause report](2026-07-20-cross-repo-coordination-overhead-root-cause.md) names as the org's central friction. The roadmap therefore ends by **generating** the count/version-bearing fragments from `registry/dependencies.yml` (per ADR-0058 "derive, don't restate" / ADR-0060), so the fix stays fixed.
- **The plan is six milestones (§7), each independently shippable**, ordered by *(consumer visibility × brokenness)*: fix the front door first, define the target shape, rescue the three orphaned libraries, correct the stale counts, reconcile the consumer guide, then make it un-rot-able.

---

## 2. Ground truth — what a consumer can actually acquire today

This is the factual baseline the docs must match. Sources are machine files, not prose.

| Component | Consumer artifact(s) | How you acquire it | Current version |
|---|---|---|---|
| **FS.GG.SDD** | `FS.GG.SDD.Cli` (dotnet tool, cmd `fsgg-sdd`); `FS.GG.Contracts` lib | `dotnet tool install --global FS.GG.SDD.Cli` | contracts `5.0.1` |
| **FS.GG.Rendering** | `FS.GG.UI.*` — 16 libs + `FS.GG.UI` BOM; `fs-gg-ui` `dotnet new` template | `dotnet add package FS.GG.UI` / `dotnet new fs-gg-ui` | framework `0.16.0` |
| **FS.GG.Governance** | `FS.GG.Governance.Cli` (tool, cmd `fsgg-governance`); `FS.GG.Governance.ReferenceGateSet` content pkg | `dotnet tool install --global FS.GG.Governance.Cli` | gate-set `1.3.0` |
| **FS.GG.Templates** | `FS.GG.Templates` (`dotnet new` pack); `FS.GG.NewSddWorkspace` tool | `dotnet new install FS.GG.Templates` | template `0.16.0` |
| **FS.GG.Game** | `FS.GG.Game.Core`, `FS.GG.Game.Render` libs | `dotnet add package FS.GG.Game.Core` | `0.7.1` |
| **FS.GG.Audio** | `FS.GG.Audio.Core/.Host/.Engine/.Elmish` libs | `dotnet add package FS.GG.Audio.Core` | `0.3.0` |
| **FS.GG.Net** | `FS.GG.Net.Core/.WebSocket/.WebSocket.Server/.Protobuf/.Grpc/.Elmish` libs | `dotnet add package FS.GG.Net.Core` | `0.1.0` |

**The one acquisition fact that governs all of them:** every `FS.GG.*` package is **public on nuget.org, anonymously readable** (ADR-0039 — "nuget.org is the read path; the org feed is the publish path"). Consumer docs must say **"restore from nuget.org, no credential"** and must **not** send a consumer to `https://nuget.pkg.github.com/FS-GG/…` (that feed needs auth and is the *publish* path). `.github` also ships `FS.GG.Coord.Cli` and `FS.GG.Drivers`, but those are **operator/agent** artifacts, not consumer packages — out of scope here (§8).

---

## 3. The problem, precisely

The audits found three distinct failure classes. They need different fixes, so the roadmap keeps them separate.

### 3a. Stale platform framing (wrong counts / missing components)
The whole doc surface still narrates the original four-component platform. Highest-impact instances:

| File | Line(s) | Stale claim |
|---|---|---|
| `profile/README.md` (org front door) | 12–16 | "the **five repositories** … (Rendering, SDD, Governance, Templates, and this coordination repo)" — wrong count, omits Game/Audio/Net |
| `profile/README.md` | 197–213 | "The components" table has **only 4 rows** — no Game/Audio/Net, no "pick your path" entry for them |
| `docs/consumer/versioning-and-updates.md` | 18–23 | "what ships" table has **no install rows** for `FS.GG.Game.*`, `FS.GG.Audio.*`, `FS.GG.Net.*` |
| `docs/consumer/which-products.md` | whole | "four common goals" — no game-sim / audio / networking acquisition path |
| `docs/consumer/index.md` | 22–33 | "made of **four components**" + 4-item list |
| `docs/consumer/faq.md` | 11–13 | "Do I have to use all **four** components?" |
| `README.md`, `docs/index.md` | 5–7 / 28,86 | "the **four-component split**" tagline (in *current* sections) |
| `FS.GG.SDD/README.md` | 10 | "a platform — **five repositories**" |
| `FS.GG.Rendering/README.md` | 12 | "a platform — **five repositories**" |
| `FS.GG.Governance/README.md` | 17, **41–46** | "**five repositories**" **and** a repo table listing only 4 repos |
| `FS.GG.Templates/README.md` | 8 | "a platform — **five repositories**" |

`docs/architecture.md` is **correct** (seven components, live version table) and is the source the rest lag behind. Game, Audio, and Net READMEs do **not** carry the wrong count — Net's framing ("bottom-layer sibling to `FS.GG.Game` and `FS.GG.Audio`") is the most current in the org.

### 3b. Published libraries with no acquisition path (the worst functional gap)
A consumer who lands on Game, Audio, or Net cannot get from the README to a working reference:

- **Game** — README lists packages as "packable lib" but has **zero** install/`PackageReference`/feed text and no consuming snippet. Never states it's published.
- **Audio** — documents *that* it publishes (Releases §), lists package IDs, has a good usage snippet — but **the acquire step between "here are the packages" and the snippet is missing.**
- **Net** — six documented packages, strong conceptual model, but **no install text, no quick-start exchange, and no `docs/` directory at all** — the only usage guidance is buried in two `samples/*/README.md`.

These are published NuGet libraries whose own front pages don't tell you how to use them. This is the highest-value fix after the front door.

### 3c. On-ramp buried under internals
- **Governance** — a 384-line README that is authoritative but internals-first (kernel theory, 166 projects, exit-code families). The actual consumer on-ramp (`docs/tutorials/adopter-onboarding.md`, a real "empty dir → governed workspace in 15 min") is **at the very bottom** and not linked from the top.

Everything else is in good shape: **SDD, Rendering, Templates READMEs are Good**, and SDD (`docs/quickstart.md`), Rendering (`docs/usage.md`), and Governance (`docs/tutorials/adopter-onboarding.md`) already have strong getting-started guides. This roadmap does **not** rewrite what works.

---

## 4. Principles for the docs (the design constraints)

1. **The README is the product page, not the contributor guide.** First screen answers, in order: *what is this · what can it do · how do I get it · show me the smallest thing that works · where do I go deeper.* Build/test/house-style/CI move below the fold or into `CONTRIBUTING`/`docs/`. This is why Governance rates "Partial" despite being the longest README.
2. **Consumer-first, because the *builder* is already served.** This is an agent-developed platform; the dense coordination/registry/lifecycle docs exist for the machine and are current. The gap is entirely on the human-consumer side. Invest there; don't add more process docs.
3. **State the acquisition path in full and make it trivial.** Every consumable component shows the exact `dotnet add package` / `dotnet tool install` line, names the package IDs, and says **"public on nuget.org, no credential."** Never point a consumer at the credentialed org feed.
4. **Derive, don't restate (ADR-0058).** A README says what a component *is* and *does*; it **links** to the platform vocabulary and the live version/registry rather than re-typing the component count or a version literal. Every hand-typed count and version is a future staleness bug — §3a is the proof. Milestone 6 makes the count/version fragments generated.
5. **One canonical narrative, many pointers.** `docs/architecture.md` already carries the correct seven-component story. The profile page and every README link to it for the "how it all fits" picture instead of each maintaining its own (drifting) copy.
6. **Meet the consumer where they land.** People arrive at an individual repo, not the org root. Each README must stand alone — acquire + first-use without leaving the page — *and* link up to the org front door for the whole-platform picture.

---

## 5. Target shape — the consumer README standard

Milestone 2 turns this into a checked-in template; here is the spec. A consumer README has these sections, in this order:

1. **Title + one-line "what it is, and who it's for."** No repo count in the first sentence.
2. **What it can do** — 3–7 concrete capability bullets a consumer recognizes (not internal module names).
3. **Acquire** — exact command(s), package IDs, "public on nuget.org — no credential." For multi-package sets, name the entry package and link the full map.
4. **Quick start** — the smallest runnable/usable snippet, copy-pasteable, that produces a visible result.
5. **Go deeper** — link to the repo's `docs/` usage/getting-started guide and to related components.
6. **Where this sits** — one line + a link to the [platform vocabulary (ADR-0020)] and `docs/architecture.md`. **Linked, not restated** — no local component count.
7. *(Below the fold)* build/test/contributing/licensing.

Acceptance test for any README: *a consumer who has never seen FS-GG can, from this page alone, install the thing and run one working example.* Today that passes for SDD/Rendering/Templates and fails for Game/Net.

---

## 6. Scope of the effort

**In scope:** the org profile page (`profile/README.md`), the repo-root `README.md`, `docs/index.md`, the nine `docs/consumer/*` files, and the seven component READMEs + a consumer getting-started/usage guide for each component that lacks one.

**Deliberately not in scope (§8 expands):** the agent/operator-facing skill docs (`.claude/skills/*` — `drive-board` et al.), the coordination/registry/ADR corpus, build-process docs, and `.github`'s own `FS.GG.Coord.*` / `FS.GG.Drivers` operator packages. Those are for the machine and its operators, not consumers; several are current already. The `drive-board` staleness that opened this thread is a real but *separate* track (agent-doc, not consumer-doc) — noted in §8, not scheduled here.

---

## 7. The roadmap

Six milestones, ordered by *(consumer visibility × brokenness)*. Each is independently shippable and mergeable, sized to be taken end-to-end by one worker (the org's `pnext-item` / `workRoadmap` shape). Milestones 1 and 2 are the first wave (independent, parallelizable); 3–5 depend on the standard from M2; 6 is the durable fix and can start in parallel but lands last.

### M1 — Fix the org front door *(repo: `.github`; highest visibility)*
The org profile page is the single most consumer-visible surface and is the most wrong.
- Rewrite `profile/README.md` to **seven framework components (eight repos incl. `.github`)**; replace the 4-row "components" table with all 7, each with a one-line "what it does" and a "pick this if…" entry (add Game/Audio/Net paths).
- Fix the "four-component split" tagline in `README.md` and `docs/index.md` (current sections only; the dated 2026-06 planning corpus in `docs/index.md` stays as history).
- **Acceptance:** no "five repositories" / "four component" string in any *current* org-level section; profile table matches §2; every component links to its repo.

### M2 — The consumer README standard *(repo: `.github`; unblocks M3–M5)*
Turn §5 into a checked-in, linkable artifact so all repo work converges on one shape.
- Add `docs/consumer/readme-standard.md` (the §5 spec + a fill-in-the-blanks skeleton) and a short "acquisition snippet" every repo pastes (the nuget.org / no-credential line + `dotnet add package` pattern).
- **Acceptance:** a worker can scaffold a compliant consumer README from this doc without re-deriving the shape; the standard explicitly forbids hardcoded counts/versions (points to M6).

### M3 — Rescue the three orphaned libraries *(repos: Game, Audio, Net; the worst functional gap)*
Bring Game, Audio, and Net READMEs to the M2 standard — the highest-value fix after the front door.
- **Each:** add an **Acquire** section (package IDs from §2, `dotnet add package …`, "public on nuget.org — no credential") and a **Quick start** snippet that compiles and produces a visible result.
- **Net additionally:** create a `docs/` directory with a getting-started/usage guide (promote the two `samples/*/README.md` exchanges into a first-class walkthrough).
- **Game additionally:** state that the packages are published; add a "consume `FS.GG.Game.Core` as a library" quick start distinct from the SDD-lifecycle TestSpec tutorial.
- **Acceptance:** the §5 acceptance test (install + run one example from the README alone) passes for all three.

### M4 — Correct the stale counts in the component READMEs *(repos: SDD, Rendering, Governance, Templates)*
- Replace "five repositories" with the M2 "Where this sits" pointer (linked, not restated) in all four.
- **Governance additionally:** fix the 4-row repo table (lines 41–46) → all 7, or delete it in favor of the architecture-doc link; and **lift the `docs/tutorials/adopter-onboarding.md` on-ramp to the top of the README** (the §3c fix — a "New here? 15-minute adopter onboarding →" banner) and push kernel internals below the fold.
- **Rendering/Templates:** replace the hardcoded framework version in prose with a pointer to the live version (pre-work for M6).
- **Acceptance:** no stale count in any component README; Governance's consumer on-ramp is reachable in the first screen.

### M5 — Reconcile the consumer guide to seven components *(repo: `.github`)*
- `docs/consumer/which-products.md` — add game-sim / audio / networking acquisition paths to the decision table and add a section each.
- `docs/consumer/versioning-and-updates.md` — add the missing `FS.GG.Game.*` / `FS.GG.Audio.*` / `FS.GG.Net.*` install rows to the "what ships" table.
- `docs/consumer/index.md` + `faq.md` — update "four components" to the correct set.
- Fix the `who-drives-the-lifecycle.md:47` anchor and the `architecture.md` heading slug that both still say "all four component repos."
- **Acceptance:** a consumer can learn how to acquire **any** of the seven components from the consumer guide; zero "four components" strings remain.

### M6 — Make it un-rot-able: generate the count/version fragments *(repo: `.github`; durable fix)*
The root cause of §3a is hand-typed derived facts. Close it the way the org closed its other drift classes (ADR-0058/0060).
- Emit the component-inventory table and the per-package version rows **from `registry/dependencies.yml`** into the profile page, the consumer "what ships" table, and the architecture version table (marked generated regions, like the existing generated registry projections).
- Where a README must show a version, render it from the registry at doc-build/CI time rather than typing it in prose.
- **Acceptance:** adding the *eighth* component, or bumping a package, updates every consumer table with **no hand edit** — the next component onboarding does not reopen §3a.

---

## 8. Non-goals and adjacent tracks

- **Agent/operator skill docs are a separate track.** The `drive-board` skill (and its siblings) that opened this investigation still say "five repositories" too — but those are **agent-facing operational docs**, not consumer docs. They rot on the same root cause and would benefit from the same M6 generation, but rewriting them is a different audience and a different roadmap. Flagged, not scheduled here.
- **No rewrite of what works.** SDD/Rendering/Templates READMEs and the three strong getting-started guides stay; M4 touches only their stale lines.
- **No new process/build docs.** The builder side is current and dense enough.
- **`.github`'s own packages** (`FS.GG.Coord.Cli`, `FS.GG.Drivers`) are operator/agent artifacts — documented for operators, not listed as consumer packages.

## 9. Sequencing summary

```text
Wave 1 (parallel):   M1 (front door)      M2 (README standard)
                          │                     │
Wave 2 (after M2):        ├── M3 (Game/Audio/Net acquire + quick start)  ← highest functional value
                          ├── M4 (SDD/Rendering/Governance/Templates counts + Governance on-ramp)
                          └── M5 (consumer guide → 7 components)
                          │
Wave 3 (durable):     M6 (generate count/version fragments from the registry) — lands last, ends the rot
```

Every milestone is a filable epic on the Coordination board; M3 fans out to one item per repo (Game, Audio, Net) that can run concurrently (different repos never collide on files). The whole roadmap is worker-shaped: a fresh subagent can take any single milestone from claim to merged-and-done without cross-milestone context.

---

## 10. Appendix — the one-paragraph acquisition truth every doc should echo

> **Getting FS-GG.** Every `FS.GG.*` package is public on [nuget.org](https://www.nuget.org) and restores with **no credential** (ADR-0039). Install the lifecycle CLI with `dotnet tool install --global FS.GG.SDD.Cli`; reference a library with `dotnet add package <id>` (e.g. `FS.GG.UI`, `FS.GG.Game.Core`, `FS.GG.Audio.Core`, `FS.GG.Net.Core`); scaffold a full workspace with `dotnet new install FS.GG.Templates`. The `nuget.pkg.github.com/FS-GG` feed is the org's **publish** path and needs auth — consumers never use it.
