# FS-GG

**F# tooling for building desktop UI apps** — a SkiaSharp/OpenGL UI
framework, a spec-driven development lifecycle CLI, optional governance, and
libraries for game-simulation, audio, and networking, composed into one runnable
workspace on `net10.0`.

You describe a Model-View-Update (MVU) app; FS-GG renders it, scaffolds it,
drives it through a structured lifecycle from charter to ship, and — only if you
opt in — checks it against rules you control. Each piece ships on its own and is
usable on its own; you adopt only what you need.

> **📖 Two things, named precisely.** FS-GG is the **platform** — the framework
> components we build and publish, each in its own repository alongside this
> coordination repo (the full list is in [The components](#the-components) below).
> What you scaffold *with* the platform is a **workspace**: a generated repo carrying
> a runnable **app**, the `.fsgg/` lifecycle, agent skills, and optional governance.
> Within the platform, each repository is a **component**. *The platform is what we
> maintain; a workspace is what you build with it.* →
> [vocabulary (ADR-0020)](https://github.com/FS-GG/.github/blob/main/docs/adr/0020-platform-workspace-component-vocabulary.md)

> **New here?**
> - **Building an app?** Start with the **[Consumer guide](https://github.com/FS-GG/.github/blob/main/docs/consumer/index.md)** —
>   install, scaffold, build, run, and ship your first workspace in one sitting.
> - **Want to understand how FS-GG is built?** Read the **[Architecture guide](https://github.com/FS-GG/.github/blob/main/docs/architecture.md)** —
>   the component split, the one-way dependency rule, the contract registry, and how it all composes.

---

## The idea: three actors, and a bias toward the cheapest competent one

Human–AI software development is work done by three kinds of actor, with
overlapping capabilities but very different cost and reliability:

| Actor | Cost | Reliability | Good at |
|---|---|---|---|
| **(H)uman** | expensive | fallible even within their competence | judgment, taste, novel decisions, sign-off |
| **(A)gent** | cheaper | non-deterministic | producing and reviewing within known patterns |
| **(M)achine** | very cheap | deterministic — *for tested workloads* | replaying anything already captured as code |

A requirement is a structure of workloads. The question FS-GG is built around is
**how to partition that structure across H, A, and M** — and the bias is
deliberate: *push each piece of work down to the cheapest actor that is provably
competent for it.* **Maximize M. Hand the rest to A. Reserve H for what only H can
do.**

Two things make that bias pay off, and FS-GG is the machinery for both:

1. **Stop reinventing solved work.** A large share of agent effort is re-deriving
   standard workflows and re-making known mistakes — input routing, the MVU loop,
   layout, theming, terminal output. FS-GG **freezes that into deterministic
   framework code** (`FS.GG.UI.*`) and delivers it to agents through a scaffold
   template, curated **skills**, and **examples anchored to deterministic tests**.
   A's production surface shrinks to the part that is genuinely novel; the rest is
   M it rides for free.
2. **Make "who decides" an explicit, honest choice.** When work *can't* be reduced
   to deterministic code, FS-GG's governance kernel decides — from a given set of
   facts — **which actor is competent to rule**, and refuses to let an actor claim
   more authority than it has earned.

### Radically opinionated on purpose

The narrower the option space, the more of a standard workflow can be frozen into
M and the better examples and skills can cover it. FS-GG chooses strong opinions —
F#, SkiaSharp, [Spectre.Console](https://spectreconsole.net/), one MVU model, one
semantic control set with many themes — so that the common path is pre-solved and
the tooling is deep rather than wide.

### Why F#

The house style is not incidental — it is what makes work *capturable as M*:

- **`.fsi` signature files as the sole public surface** enable a
  `task → .fsi (contract) → tests → implementation → test` workflow, and the
  signature is drift-guarded by committed surface baselines — so "did the API
  change" becomes a machine check, not a human judgment.
- **Algebraic data types, exhaustive matching, and total functions** let a single
  source be folded many ways *without drifting apart* — the basis for
  deterministic, explainable verdicts.
- **Pure cores with I/O at the edge** keep logic reproducible and testable; effects
  live in a thin interpreter at the boundary.

---

## The one rule that keeps it honest

> Governance may **inspect** your rendering or lifecycle artifacts; rendering and
> the lifecycle never **require** governance to build, test, document, package, or
> release.

The dependency direction is one-way and your inner loop is never blocked by
governance. You can clone a component repo, read its [Spec Kit](https://github.com/github/spec-kit)
artifacts, run the documented commands, and ship — without learning a custom
operating model. If governance ever feels heavy, you drop it and keep building.

---

## Get started

The fastest path is the one-command workspace scaffolder,
**[`new-sdd-workspace`](https://github.com/FS-GG/.github/blob/main/scripts/NewSddWorkspace/README.md)**
([ADR-0016](https://github.com/FS-GG/.github/blob/main/docs/adr/0016-retire-templates-local-new-fullstack-single-scaffolder.md)) —
it fetches the provider descriptor, scaffolds a runnable app, and lays down the
`.fsgg/` lifecycle skeleton in a single step.

```sh
# 1. Install the scaffolder + the lifecycle CLI (dotnet global tools, on public nuget.org).
dotnet tool install --global FS.GG.SDD.Cli
dotnet tool install --global FS.GG.NewSddWorkspace --prerelease

# 2. Scaffold a runnable workspace in ONE command (default profile = a minimal
#    Pong-style game; pass --profile app|headless-scene|governed|sample-pack for another shape).
new-sdd-workspace ./Pong Pong              # <target-dir> <product-name>
#    …or run it with NO arguments on a terminal for an interactive wizard
#    (product → target → profile → governance) with a live scaffold preview
#    and a final go/no-go confirmation.

# 3. Build and run.
cd ./Pong && dotnet build && dotnet run
```

That gives you a real, windowed F# UI app plus the `.fsgg/` lifecycle skeleton
(`FS.GG.UI.*` are preview packages on public nuget.org — restore needs no extra feed).
Continue with `fsgg-sdd charter` to drive the work lifecycle, and add governance
later if you want gates.

Full walkthrough →
**[Getting started](https://github.com/FS-GG/.github/blob/main/docs/consumer/getting-started.md)**.
Or create a ready specced sample game: https://github.com/FS-GG/FS.GG.Game/blob/main/docs/TestSpecTutorial.md

---

## The `fsgg-sdd` CLI

`fsgg-sdd` (the `FS.GG.SDD.Cli` global tool) is the **single surface** you drive an
FS-GG workspace with. It does three jobs: it **scaffolds** a runnable workspace, it
**drives** the `charter → ship` lifecycle, and it **orchestrates coherence** —
keeping a workspace aligned with its pinned *coherent set* (the
[template + framework **+ the CLI itself**](https://github.com/FS-GG/.github/blob/main/docs/architecture.md#5-the-contract-registry--the-single-source-of-truth)).
As the orchestrator it never silently self-updates or rewrites your files: it
**detects** drift read-only on every command, and **remediates only through an
explicit, diff-reviewed command**.

```sh
dotnet tool install --global FS.GG.SDD.Cli   # exposes `fsgg-sdd`
fsgg-sdd --version                            # the CLI's OWN version — not your fs-gg-ui version
```

### Commands

| Group | Command | What it does |
|---|---|---|
| **Scaffold** | `fsgg-sdd scaffold` | Materialize a runnable app **and** the `.fsgg/` lifecycle skeleton from a provider. |
| | `fsgg-sdd init` | Write the `.fsgg/` skeleton **only** — no runtime app, no provider. |
| **Lifecycle** | `charter` · `specify` · `clarify` · `checklist` · `plan` · `tasks` · `analyze` · `evidence` · `verify` · `ship` | Drive a unit of work through the fixed, ordered lifecycle (each emits a report). |
| **Orchestrate** | `fsgg-sdd doctor` | **Read-only:** report whether the project (CLI + template pin + seeded artifacts) is coherent with its set. |
| | `fsgg-sdd upgrade` | Reconcile to the coherent set — self-update + template re-pin + artifact re-seed — **each shown as a confirmable diff**. |
| | `fsgg-sdd refresh-agents` | Re-seed the CLI-owned agent artifacts (`fs-gg-sdd-*` process skills, `.fsgg/early-stage-guidance.md`). |
| | `fsgg-sdd refresh` | Bring a work item's generated views back to currency. |
| **Utility** | `fsgg-sdd agents` | Generate per-target Claude/Codex command + skill guidance. |
| | `fsgg-sdd registry validate <path>` | Validate a cross-repo `dependencies.yml` with the typed validator. |
| | `fsgg-sdd validate` | Self-test: exhaustively exercise the command × projection × state matrices. |

### Parameters

**`scaffold` / `init`:**

| Flag | Meaning |
|---|---|
| `--root <dir>` | Target project directory. |
| `--provider <id>` | Template provider to invoke (e.g. `rendering`); resolved from `.fsgg/providers.yml`. |
| `--param name=<Product>` | The **canonical** product-name parameter (ADR-0005). `--param productName=<Product>` is an accepted alias. |
| `--dry-run` | Plan the steps without executing. |
| `--no-update` | Skip refreshing the template before scaffolding. |
| `--force` | Materialize into a non-empty directory. |

**Every command — output projection** (precedence `--rich` > `--text` > `--json` > default):

| Flag | Output |
|---|---|
| *(default)* / `--json` | Deterministic JSON — **the automation contract**. |
| `--text` | Portable plain-text summary. |
| `--rich` | [Spectre.Console](https://spectreconsole.net/) rendering; degrades to zero-ANSI when non-interactive or `NO_COLOR`/`TERM=dumb`. |

**Exit codes** (`scaffold`): `0` success · `1` malformed input (unknown provider, missing parameter, target collision) · `2` provider defect. When the installed CLI is **behind the coherent set's minimum**, an *interactive* run warns and points at `fsgg-sdd upgrade`; a *CI / non-interactive* run **fails closed** (non-zero).

> The orchestration verbs (`doctor` / `upgrade` / `refresh-agents`) and the
> behind-the-pin drift check are the CLI's orchestrator role, decided in
> [ADR-0008](https://github.com/FS-GG/.github/blob/main/docs/adr/0008-fsgg-sdd-cli-first-class-member-of-coherent-set.md) /
> [ADR-0009](https://github.com/FS-GG/.github/blob/main/docs/adr/0009-cli-single-orchestrator-detect-and-remediate.md)
> and rolling out with the SDD orchestrator work — run `fsgg-sdd --help` to see what
> your installed version exposes.

---

## The components

<!-- BEGIN GENERATED: fsgg-component-count -->
<!--
  DO NOT EDIT THIS REGION. It is emitted from registry/repos.yml by
  scripts/generate-projections, and `projections` in CI fails on any diff.

  The component/repository COUNT was hand-typed into a dozen consumer-facing places and rotted
  the instant Game/Audio/Net were added — "five repositories" / "four components" were wrong in
  the org profile, four component READMEs and five consumer-guide files (roadmap §3a, #1313). It
  is now a pure count of registry/repos.yml's roster rows, whose SET is held closed by
  check-roster-closure.py; add or retire a repo THERE and every doc that states the count follows.
-->

**Seven framework components** ship independently — each public on [nuget.org](https://www.nuget.org) and restoring with no credential (ADR-0039) — across **eight** repositories in the org (those seven plus this `.github` coordination repo).

<!-- END GENERATED: fsgg-component-count -->

You adopt only what you need — each component stands alone. The table below is
generated from the org registry, so it never falls out of step with what actually
ships; the exact acquire command and package IDs are in
[Pick your path](#pick-your-path) below and in each component's README.

<!-- BEGIN GENERATED: fsgg-component-inventory -->
<!--
  DO NOT EDIT THIS REGION. It is emitted from registry/repos.yml + registry/dependencies.yml by
  scripts/generate-projections, and `projections` in CI fails on any diff.

  The component inventory was a hand-maintained table in the profile page and the consumer
  'what ships' guide, and it rotted: Game/Audio/Net shipped without ever being added (roadmap
  §3a, #1313). The ROW SET is now the framework rows of registry/repos.yml's roster; each row's
  description is that component's `role` in registry/dependencies.yml, and the version is its
  package-bearing contracts' live `package-version` (held to the feed by check-feed-coherence.py).
  Add a framework repo to the roster and a row appears; bump a package and the version follows —
  with no hand edit to any consumer doc. If a description reads too technically, fix the `role`
  in registry/dependencies.yml (the one home), not a copy here.
-->

*Generated from `registry/repos.yml` (the org repo roster, ADR-0019) joined with
`registry/dependencies.yml` (each component's `role` and its contracts' live `package-version`).
`Current version` is `—` for a component whose packages are not (yet) tracked as a
package-bearing contract owned by that component. The exact acquire command and package IDs are
authored beside this table — package IDs are stable identity, versions are not (readme-standard).*

| Component | What it does | Current version |
|---|---|---|
| [**FS.GG.SDD**](https://github.com/FS-GG/FS.GG.SDD) | Lifecycle CLI to scaffold a workspace and drive it from charter to ship; ships the typed cross-repo contracts | `7.4.0` |
| [**FS.GG.Rendering**](https://github.com/FS-GG/FS.GG.Rendering) | The UI framework — MVU over SkiaSharp/OpenGL with layout, input, controls and themes, plus the fs-gg-ui template | `0.24.0` |
| [**FS.GG.Governance**](https://github.com/FS-GG/FS.GG.Governance) | Optional tooling that checks your artifacts against rules you control — advisory by default | `1.5.0` |
| [**FS.GG.Templates**](https://github.com/FS-GG/FS.GG.Templates) | Wires SDD, Rendering and Governance into one ready-to-run workspace at scaffold time | — |
| [**FS.GG.Game**](https://github.com/FS-GG/FS.GG.Game) | Game-simulation libraries — a render-independent simulation core with a companion renderer, usable as plain F# libraries | `0.13.0` |
| [**FS.GG.Audio**](https://github.com/FS-GG/FS.GG.Audio) | Audio-engine libraries — synthesis, playback and mixing (buses, fades, ducking, 3D), with an optional Elmish adapter | `0.5.0` |
| [**FS.GG.Net**](https://github.com/FS-GG/FS.GG.Net) | Networking/transport libraries — protobuf messaging over WebSocket or gRPC, render-independent, with an optional Elmish adapter | `0.5.0` |

<!-- END GENERATED: fsgg-component-inventory -->

The [**FS-GG/.github**](https://github.com/FS-GG/.github) coordination repo — the
org's shared docs, registry, and cross-repo fabric — is not a consumer component.

### Pick your path

| You want to… | Use | Start at |
|---|---|---|
| Just render an F# UI | **FS.GG.Rendering** packages / `fs-gg-ui` template | [Rendering usage](https://github.com/FS-GG/FS.GG.Rendering/blob/main/docs/usage.md) |
| Run a managed dev lifecycle | **FS.GG.SDD** (`fsgg-sdd`) | [SDD quickstart](https://github.com/FS-GG/FS.GG.SDD/blob/main/docs/quickstart.md) |
| Scaffold a full-stack workspace (one command) | **`new-sdd-workspace`** (wraps the FS.GG.Templates `rendering` provider) | [Scaffolder README](https://github.com/FS-GG/.github/blob/main/scripts/NewSddWorkspace/README.md) |
| Add rules / gates to a workspace | **FS.GG.Governance** overlay | [Adopting governance](https://github.com/FS-GG/FS.GG.SDD/blob/main/docs/adopting-governance.md) |
| Build a game / simulation as a library | **FS.GG.Game** (`dotnet add package FS.GG.Game.Core`) | [FS.GG.Game](https://github.com/FS-GG/FS.GG.Game) |
| Add audio (synthesis / playback) | **FS.GG.Audio** (`dotnet add package FS.GG.Audio.Core`) | [FS.GG.Audio](https://github.com/FS-GG/FS.GG.Audio) |
| Wire networking / transport (WebSocket / gRPC) | **FS.GG.Net** (`dotnet add package FS.GG.Net.Core`) | [FS.GG.Net](https://github.com/FS-GG/FS.GG.Net) |

Not sure? See **[Which components do I need?](https://github.com/FS-GG/.github/blob/main/docs/consumer/which-products.md)**

---

## How governance decides *who decides*

When work cannot be reduced to deterministic code, the question is which actor is
competent to rule on it. The governance kernel makes that a typed, explicit
decision on two orthogonal axes:

- **`CheckTier` — who is competent to decide:** `Deterministic` (a **machine**
  re-evaluates a reproducible check every run), `AgentReviewed` (an **agent**
  decides; the verdict is cached against a content hash and frozen as evidence),
  or `HumanOnly` (a **human** decides; the kernel escalates and never rules).
- **`Severity` — whether failure stops you:** `Advisory` or `Blocking`.

Two guardrails make the actor partition trustworthy rather than aspirational:

- **A machine can't claim authority it can't back.** The `Deterministic` tier is
  *structurally refused* for a check that hides opaque logic — the rule is
  unconstructable, not merely discouraged. Verdicts are three-valued
  (`Pass`/`Fail`/`Uncertain`), so an actor can honestly say "undecided" and
  escalate instead of fabricating an answer.
- **An agent can't promote itself.** An agent finding becomes eligible to *block*
  only via deterministic backing evidence, reproduction across independent
  reviews, or explicit human sign-off — **never the model's own self-reported
  confidence.** Agent judges are calibrated against human ground truth, per model
  identity.

Adopt it when you want gates; ignore it and keep building when you don't. See
[Adopting governance](https://github.com/FS-GG/.github/blob/main/docs/consumer/governance.md).

---

## How it composes

Composition happens **at scaffold time**, not by vendoring: `fsgg-sdd scaffold`
installs the live, version-pinned rendering template, and the Governance overlay
drops a reference gate set into the project. There is no single all-in-one
template, because that could only exist by bundling a rendering copy that goes
stale.

```text
       you ──run──▶ fsgg-sdd scaffold ──installs──▶ FS.GG.Rendering app (live, pinned)
                          │
                          ├──skeleton──▶ .fsgg/ lifecycle (charter … ship)
                          └──overlay (optional)──▶ FS.GG.Governance reference gate set

FS.GG.Rendering depends on no other FS-GG component — never on Governance.
```

Every feature moves through
`charter → specify → clarify → checklist → plan → tasks → analyze → evidence →
verify → ship`, where each step reads and writes structured artifacts and emits a
deterministic report you (and your agents and CI) can consume. The output rule
throughout: **JSON is the contract; plain text and rich
([Spectre.Console](https://spectreconsole.net/)) are projections of it**, and rich
degrades to zero-ANSI when output is redirected or `NO_COLOR` is set — so every
surface stays machine-checkable.

> **📐 Want the full picture?** Read the
> **[Architecture guide](https://github.com/FS-GG/.github/blob/main/docs/architecture.md)** —
> the component split, the one-way dependency rule, the contract registry, the
> shared F# house style, and how the repositories compose into one runnable
> workspace, with links to every source.

---

## Consumer documentation

The **[Consumer guide](https://github.com/FS-GG/.github/blob/main/docs/consumer/index.md)**
collects the cross-component processes in one place:

- [Getting started](https://github.com/FS-GG/.github/blob/main/docs/consumer/getting-started.md) — your first workspace, end to end.
- [Which components do I need?](https://github.com/FS-GG/.github/blob/main/docs/consumer/which-products.md) — a decision guide.
- [The development lifecycle](https://github.com/FS-GG/.github/blob/main/docs/consumer/lifecycle.md) — `charter → ship`, step by step.
- [Adopting governance](https://github.com/FS-GG/.github/blob/main/docs/consumer/governance.md) — profiles, gates, and the escape hatch.
- [Output, automation & CI](https://github.com/FS-GG/.github/blob/main/docs/consumer/automation.md) — the JSON contract and scripting.
- [Versions, feeds & updates](https://github.com/FS-GG/.github/blob/main/docs/consumer/versioning-and-updates.md) — installing, pinning, staying current.
- [FAQ & troubleshooting](https://github.com/FS-GG/.github/blob/main/docs/consumer/faq.md).

Authoritative per-component docs live in each repository; the consumer guide is the
map and the cross-component processes that connect them.

---

## Status

Active preview. Rendering ships `FS.GG.UI.*` preview packages and the `fs-gg-ui`
template; SDD and Governance are active and installable. APIs and package
versions may still move before a stable line — pin versions and read each
component's installation and versioning docs. FS-GG is the split of the archived
[`FS-Skia-UI`](https://github.com/EHotwagner/FS-Skia-UI) monolith into focused,
independently shippable components.

## License

MIT.
