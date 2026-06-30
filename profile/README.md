# FS-GG

**F# tooling for building desktop UI products** — a SkiaSharp/OpenGL UI
framework, a spec-driven development lifecycle CLI, and optional governance,
composed into one runnable product on `net10.0`.

You describe a Model-View-Update (MVU) app; FS-GG renders it, scaffolds it,
drives it through a structured lifecycle from charter to ship, and — only if you
opt in — checks it against rules you control. Each piece ships on its own and is
usable on its own; you adopt only what you need.

> **New here?**
> - **Building a product?** Start with the **[Consumer guide](https://github.com/FS-GG/.github/blob/main/docs/consumer/index.md)** —
>   install, scaffold, build, run, and ship your first product in one sitting.
> - **Want to understand how FS-GG is built?** Read the **[Architecture guide](https://github.com/FS-GG/.github/blob/main/docs/architecture.md)** —
>   the four-product split, the one-way dependency rule, the contract registry, and how it all composes.

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

The dependency direction is one-way and your inner loop is never blocked by a
platform. You can clone a product repo, read its [Spec Kit](https://github.com/github/spec-kit)
artifacts, run the documented commands, and ship — without learning a custom
operating model. If governance ever feels heavy, you drop it and keep building.

---

## Get started in three commands

```sh
# 1. Install the lifecycle CLI (a dotnet global tool).
dotnet tool install --global FS.GG.SDD.Cli

# 2. Scaffold a runnable Skia/Elmish app under an SDD-managed lifecycle.
fsgg-sdd scaffold --root ./MyApp --provider rendering --param productName=MyApp

# 3. Build and run it.
cd ./MyApp && dotnet build && dotnet run
```

That gives you a real, windowed F# UI app plus the `.fsgg/` lifecycle skeleton.
Continue with `fsgg-sdd charter` to drive the work lifecycle, and add governance
later if you want gates. Full walkthrough →
**[Getting started](https://github.com/FS-GG/.github/blob/main/docs/consumer/getting-started.md)**.

---

## The products

| Product | What it gives you | Ships |
|---|---|---|
| [**FS.GG.Rendering**](https://github.com/FS-GG/FS.GG.Rendering) | The UI framework: Scene, layout, input, viewer/host, controls, themes — Elmish/MVU over SkiaSharp/OpenGL. The render core is Elmish-free; idiomatic Elmish is an optional adapter. | `FS.GG.UI.*` packages + the `fs-gg-ui` `dotnet new` template |
| [**FS.GG.SDD**](https://github.com/FS-GG/FS.GG.SDD) | The lifecycle CLI: scaffold a product, then drive `charter → ship` with structured artifacts and JSON/text/rich reports. Also ships `FS.GG.Contracts`, the typed cross-repo contract backbone. | `FS.GG.SDD.Cli` (`fsgg-sdd`) + `FS.GG.Contracts` |
| [**FS.GG.Governance**](https://github.com/FS-GG/FS.GG.Governance) | Optional rule / evidence / route tooling — a pure inference kernel that checks your artifacts, advisory by default. | `FS.GG.Governance.Cli` (`fsgg-governance`) + the reference gate set |
| [**FS.GG.Templates**](https://github.com/FS-GG/FS.GG.Templates) | The composition: wires SDD + Rendering + Governance into one ready-to-run product at scaffold time. | the `rendering` scaffold provider + `fs-gg-governance` overlay |

### Pick your path

| You want to… | Use | Start at |
|---|---|---|
| Just render an F# UI | **FS.GG.Rendering** packages / `fs-gg-ui` template | [Rendering usage](https://github.com/FS-GG/FS.GG.Rendering/blob/main/docs/usage.md) |
| Run a managed dev lifecycle | **FS.GG.SDD** (`fsgg-sdd`) | [SDD quickstart](https://github.com/FS-GG/FS.GG.SDD/blob/main/docs/quickstart.md) |
| Scaffold a full-stack product | **FS.GG.Templates** (`rendering` provider) | [Templates](https://github.com/FS-GG/FS.GG.Templates#create-a-full-stack-product-composition-primary-path) |
| Add rules / gates to a project | **FS.GG.Governance** overlay | [Adopting governance](https://github.com/FS-GG/FS.GG.SDD/blob/main/docs/adopting-governance.md) |

Not sure? See **[Which products do I need?](https://github.com/FS-GG/.github/blob/main/docs/consumer/which-products.md)**

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

FS.GG.Rendering depends on no other FS-GG product — never on Governance.
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
> the four-product split, the one-way dependency rule, the contract registry, the
> shared F# house style, and how the repositories compose into one runnable
> product, with links to every source.

---

## Consumer documentation

The **[Consumer guide](https://github.com/FS-GG/.github/blob/main/docs/consumer/index.md)**
collects the cross-product processes in one place:

- [Getting started](https://github.com/FS-GG/.github/blob/main/docs/consumer/getting-started.md) — your first product, end to end.
- [Which products do I need?](https://github.com/FS-GG/.github/blob/main/docs/consumer/which-products.md) — a decision guide.
- [The development lifecycle](https://github.com/FS-GG/.github/blob/main/docs/consumer/lifecycle.md) — `charter → ship`, step by step.
- [Adopting governance](https://github.com/FS-GG/.github/blob/main/docs/consumer/governance.md) — profiles, gates, and the escape hatch.
- [Output, automation & CI](https://github.com/FS-GG/.github/blob/main/docs/consumer/automation.md) — the JSON contract and scripting.
- [Versions, feeds & updates](https://github.com/FS-GG/.github/blob/main/docs/consumer/versioning-and-updates.md) — installing, pinning, staying current.
- [FAQ & troubleshooting](https://github.com/FS-GG/.github/blob/main/docs/consumer/faq.md).

Authoritative per-product docs live in each repository; the consumer guide is the
map and the cross-product processes that connect them.

---

## Status

Active preview. Rendering ships `FS.GG.UI.*` preview packages and the `fs-gg-ui`
template; SDD and Governance are active and installable. APIs and package
versions may still move before a stable line — pin versions and read each
product's installation and versioning docs. FS-GG is the split of the archived
[`FS-Skia-UI`](https://github.com/EHotwagner/FS-Skia-UI) monolith into focused,
independently shippable products.

## License

MIT.
