---
title: Architecture
category: FS.GG
categoryindex: 6
index: 0
description: A newcomer's guide to the FS-GG architecture — the four-product split, the one-way dependency rule, the contract registry, and how the repositories compose into one runnable product.
---

# FS-GG architecture

> **Audience.** This document is for people who want to understand *how FS-GG is
> built* — its repositories, boundaries, contracts, and the decisions that shape
> them. If you only want to *use* FS-GG to build a product, start with the
> [consumer guide](consumer/index.md) instead.

FS-GG is **F# tooling for building desktop UI products on `net10.0`**: a
SkiaSharp/OpenGL UI framework, a spec-driven development (SDD) lifecycle CLI, and
optional governance tooling, that compose into one runnable product. It is the
split of the archived [`FS-Skia-UI`](https://github.com/EHotwagner/FS-Skia-UI)
monolith into four focused, independently shippable products plus an
organization-level coordination repository.

This page is a map. Authoritative detail lives in each product repository and in
the decision records linked throughout.

---

## 1. The shape of the system

```text
                         ┌───────────────────────────────────────────────┐
                         │  FS-GG/.github  (this repo — coordination)     │
                         │  • registry/dependencies.yml (contract truth)  │
                         │  • dist/dotnet/ (org-shared build config)      │
                         │  • docs/ (decision records + consumer guide)   │
                         └───────────────────────────────────────────────┘
                                   │ syncs build config to ▼ all repos
   ┌───────────────────────────────┼───────────────────────────────────────┐
   │                               │                                        │
┌──┴───────────────┐   ┌───────────┴──────────┐   ┌──────────────────┐   ┌──┴──────────────┐
│ FS.GG.Rendering  │   │ FS.GG.SDD            │   │ FS.GG.Governance │   │ FS.GG.Templates │
│ UI framework     │   │ lifecycle CLI +      │   │ optional rule /  │   │ scaffold-time   │
│ FS.GG.UI.* +     │   │ FS.GG.Contracts      │   │ evidence kernel  │   │ composition     │
│ fs-gg-ui template│   │ (fsgg-sdd)           │   │ (fsgg-governance)│   │ (overlay+provider)│
└──────────────────┘   └──────────────────────┘   └──────────────────┘   └─────────────────┘
        ▲                        ▲                         ▲                      │
        │                        │                         │                      │
        └── fs-gg-ui-template ───┴── scaffold-provider ────┴── governance-* ──────┘
              (consumed by Templates at scaffold time; see §6)
```

Five repositories under [github.com/FS-GG](https://github.com/FS-GG):

| Repository | Role | Ships |
|---|---|---|
| [**FS.GG.Rendering**](https://github.com/FS-GG/FS.GG.Rendering) | The UI framework — Scene, layout, input, viewer/host, controls, themes; Elmish/MVU over SkiaSharp/OpenGL. | `FS.GG.UI.*` packages + the `fs-gg-ui` `dotnet new` template |
| [**FS.GG.SDD**](https://github.com/FS-GG/FS.GG.SDD) | The lifecycle CLI + the typed cross-repo contract backbone. | `FS.GG.SDD.Cli` (`fsgg-sdd`) + `FS.GG.Contracts` |
| [**FS.GG.Governance**](https://github.com/FS-GG/FS.GG.Governance) | Optional rule / evidence / gate tooling — a pure inference kernel, advisory by default. | `FS.GG.Governance.Cli` (`fsgg-governance`) + the reference gate set |
| [**FS.GG.Templates**](https://github.com/FS-GG/FS.GG.Templates) | The composition — wires SDD + Rendering + Governance into one product at scaffold time. | the `rendering` scaffold provider + `fs-gg-governance` overlay |
| [**FS-GG/.github**](https://github.com/FS-GG/.github) (this repo) | Cross-repo contract registry, org-shared build config, consumer + decision docs. | — |

---

## 2. Why it is split this way

The earlier proposal was a single repo-native "SpecFlow graph operating system"
with a `ProjectGraph`/`ProductGraph`/`FeatureGraph`, an evidence ledger,
generated projections, route planning, product contracts, and platform policy —
all in one place. It was internally consistent but **monolithic**: it asked
maintainers to develop a changing UI framework on top of a changing governance
framework, creating a recursive maintenance cost (product changes became
governance-schema changes; governance changes could block rendering work; every
contributor had to learn a custom operating model before touching runtime code).

The current direction — recorded in
[`docs/project-split-decision.md`](project-split-decision.md) — is deliberately
simpler:

- keep the rendering framework buildable, testable, and releasable **without** an
  experimental governance platform;
- use **standard [Spec Kit](https://github.com/github/spec-kit)** for feature
  workflow in each repository;
- keep only narrow, deterministic, locally-worth-it checks in the rendering repo;
- make governance tooling **earn** adoption from the outside;
- keep SDD lifecycle tooling in its own repo so project workflow can evolve
  without becoming the governance rule engine.

The full index of split documents is
[`docs/index.md`](index.md). Background research that motivated the split is
preserved in [`docs/research-notes.md`](research-notes.md), and the design-system
layering rules live in [`docs/design-and-controls.md`](design-and-controls.md).

### The one rule that keeps it honest

> Governance may **inspect** your rendering or lifecycle artifacts; rendering and
> the lifecycle never **require** governance to build, test, document, package, or
> release.

The dependency direction is **one-way**. **FS.GG.Rendering depends on no other
FS-GG product** — never on Governance. SDD depends on Governance only through an
*optional* handoff document it can produce and ignore. Your inner development loop
is never blocked by governance, and if governance ever feels heavy you can drop it
and keep building. This rule is restated on the
[org landing page](../profile/README.md) and is the invariant every contract in
§5 is designed to preserve.

---

## 3. House style (shared across all four product repos)

Reading one repo teaches you all of them. The conventions are consistent:

- **F# with `.fsi` signature files as the sole public surface.** Every library
  ships a curated `.fsi`; the matching `.fs` carries no access modifiers. The
  public API is what the signature exposes — and it is drift-guarded by committed
  surface baselines (`surface/*.surface.txt` in Governance, `PublicSurface.baseline`
  + `SurfaceBaselineTests` in SDD, `readiness/surface-baselines/` in Rendering).
- **Pure cores, I/O at the edge.** Domain logic is pure and total; file/process/
  network effects are pushed to a thin interpreter at the boundary. Commands are
  modeled as **Elmish/MVU** loops (pure `init`/`update` over a deferred-effect
  union, interpreted to a fixpoint by the host).
- **`net10.0`, FSharp.Core `10.1.301`**, central package management with
  transitive pinning, deterministic builds, warnings-as-errors.
- **Spec Kit feature history.** Each repo keeps numbered `specs/NNN-*/` folders
  (`spec.md`/`plan.md`/`tasks.md`/`contracts/…`) as a design-history index, and a
  `.specify/` engine with an F# constitution.
- **Output rule: "JSON is the contract; Plain and Rich are projections."** CLIs
  emit deterministic JSON by default; `--text` and `--rich`
  ([Spectre.Console](https://spectreconsole.net/)) are *projections* of the same
  report, and Rich **degrades to zero-ANSI** when output is non-interactive,
  redirected, or `NO_COLOR`/`TERM=dumb` is set.
- **Org-shared build config.** `Directory.Build.props` / `Directory.Packages.props`
  are byte-identical copies distributed from this repo's
  [`dist/dotnet/`](../dist/dotnet/) by
  [`scripts/sync-build-config.sh`](../scripts/sync-build-config.sh); repo-specific
  settings live in `*.local.props`. A drift check fails the PR (see §7).

---

## 4. The product repositories in detail

### 4.1 FS.GG.Rendering — the UI framework

A SkiaSharp/OpenGL + Elmish UI framework. **18 `src/` projects, 17 test projects,
123 Spec Kit features**, on the new `.slnx` solution format
([`FS.GG.Rendering.slnx`](https://github.com/FS-GG/FS.GG.Rendering/blob/main/FS.GG.Rendering.slnx)).

**The Elmish-free core / optional adapter split is real in the project graph,**
not just an aspiration. The render core packages carry no Elmish dependency:

- `FS.GG.UI.Scene` — the dependency-light scene vocabulary every other package builds on.
- `FS.GG.UI.Layout` — pure layout + graph scene builders (uses `Yoga.Net`).
- `FS.GG.UI.DesignSystem` — token model, `Theme` record, `ResolvedStyle`, pure `Style.resolve`.
- `FS.GG.UI.Controls` — declarative Skia controls (Button, TextBox, DataGrid, charts…).
- `FS.GG.UI.Canvas`, `FS.GG.UI.Symbology` (+ `Symbology.Render`), `FS.GG.UI.KeyboardInput`, `FS.GG.UI.Diagnostics`.
- `FS.GG.UI.Themes.Default` / `FS.GG.UI.Themes.AntDesign` — concrete themes over the shared `DesignSystem` (no control fork; one semantic `Button`, many themes — see [`design-and-controls.md`](design-and-controls.md)).
- `FS.GG.UI.SkiaViewer` — the **only** project carrying native dependencies (SkiaSharp, HarfBuzz, Silk.NET windowing/GL).

The optional Elmish adapter layer is two separate opt-in packages:
`FS.GG.UI.Elmish` and `FS.GG.UI.Controls.Elmish` (over `Fable.Elmish`).

**Headless degrade-and-disclose.** Live windowed GL rendering needs an X11/GL
session, but the deterministic tiers run headless. The reference path
([`src/SkiaViewer/ReferenceRendering.fsi`](https://github.com/FS-GG/FS.GG.Rendering/blob/main/src/SkiaViewer/ReferenceRendering.fsi))
returns a three-way verdict — `ReferencePassed | ReferenceFailed |
ReferenceEnvironmentLimited` — so a capability-absent CI runner is *disclosed,
not failed*. The `tools/Rendering.Harness` CLI declares per run what each tier
proves and what it does not.

**The `fs-gg-ui` template** (manifest
[`.template.config/template.json`](https://github.com/FS-GG/FS.GG.Rendering/blob/main/.template.config/template.json),
packaged by `.template.package/` as `FS.GG.UI.Template`). Parameters: `profile`
(`app`/`game`/`headless-scene`/`governed`/`sample-pack` — `game` is a minimal
replaceable Pong-style starter and the intended game/rendering default, Feature
220), `designSystem` (`wcag`/`ant`),
`lifecycle` (`spec-kit`/`sdd`/`none`, ADR-0002), `productName` (the additive alias
of the canonical `--name`, ADR-0005), and `initGit` (the side-effect-free opt-in
from the Feature 205 behavior break — generation no longer auto-runs git/chmod).
The template generates a **root-buildable** product: `Product.slnx` + `global.json`
+ `build.sh`/`build.cmd` FAKE verb wrapper, so `dotnet restore|build|test|run`
works at the product root with zero FAKE knowledge.

An optional **BOM metapackage** `FS.GG.UI` (`src/Meta/`) pins all 16
`FS.GG.UI.*` members at one exact version so drift fails restore.

**CI.** [`gate.yml`](https://github.com/FS-GG/FS.GG.Rendering/tree/main/.github/workflows)
is the single required pre-merge check; `release.yml` does the heavy packaging and
consumption tests; `template-dispatch.yml` fires the cross-repo
`fs-gg-ui-template-released` event to Templates on a release tag.

### 4.2 FS.GG.SDD — lifecycle CLI + contract backbone

Two products in one repo (**11 projects: 5 src + 6 test**).

**`FS.GG.Contracts`** — the typed cross-repo contract backbone. A
**FSharp.Core-only BCL leaf** (no project references, no I/O), namespace `Fsgg`,
four modules:

- `Fsgg.ContractVersion` — a self-describing package SemVer (`val value = "1.2.0"`) so a consumer knows which surface it compiled against.
- [`Fsgg.Schemas`](https://github.com/FS-GG/FS.GG.SDD/blob/main/src/FS.GG.Contracts/Schemas.fsi) — one typed source of truth for every `.fsgg` schema shape and its version constant (SDD- and Governance-owned).
- [`Fsgg.Provider`](https://github.com/FS-GG/FS.GG.SDD/blob/main/src/FS.GG.Contracts/Provider.fsi) — the extended scaffold-provider descriptor (canonical `NameParameter`).
- [`Fsgg.Registry`](https://github.com/FS-GG/FS.GG.SDD/blob/main/src/FS.GG.Contracts/Registry.fsi) — **the validator for this repo's [`registry/dependencies.yml`](../registry/dependencies.yml)**. Its version grammar deliberately mirrors `scripts/validate-registry.py` byte-for-byte, including the 4-segment `major.minor.patch.revision` form (ADR-0007), so the typed validator and the (now retired) Python authority cannot disagree.

**The `fsgg-sdd` CLI** drives the lifecycle
`charter → specify → clarify → checklist → plan → tasks → analyze → evidence →
verify → ship`, modeled as an Elmish/MVU loop
([`CommandWorkflow.fs`](https://github.com/FS-GG/FS.GG.SDD/blob/main/src/FS.GG.SDD.Commands/CommandWorkflow.fs))
where pure planning emits a deferred `CommandEffect` union performed only at the
edge. Each command projects a `CommandReport` as JSON/text/rich. Authored output
lands in `work/<id>/*`; structured output (including `governance-handoff.json`)
in `readiness/<id>/*`.

> **Coming from Spec Kit? There is no `implement` command — by design.** SDD
> *brackets* implementation rather than owning it: it tracks the artifacts and
> evidence *around* your work, it does not produce your product code. The Spec Kit
> `/implement` step is the **unmanaged gap between `analyze` and `evidence`** — the
> [quickstart lifecycle table](https://github.com/FS-GG/FS.GG.SDD/blob/main/docs/quickstart.md)
> names the action after `analyze` literally as *"implement, then `evidence`."*
> `analyze` reports `analysis.json` as *implementation-ready* (the gate before you
> code); **you implement**; then `evidence` authors `work/<id>/evidence.yml`
> recording proof that it happened — the closest command equivalent. The
> [migration guide](https://github.com/FS-GG/FS.GG.SDD/blob/main/docs/migration-from-spec-kit.md)
> maps `spec`/`clarify`/`plan`/`tasks`/`evidence` but has no `implement` row, and
> there is no `Implement` case in the `SddCommand` union.
>
> | Spec Kit | SDD |
> |---|---|
> | `/specify` `/clarify` `/plan` `/tasks` `/analyze` | `specify` `clarify` (`checklist`) `plan` `tasks` `analyze` |
> | **`/implement`** | **(no command — the act between `analyze` and `evidence`); `evidence` records it** |
> | — | `verify` → `ship` (SDD-specific readiness / merge-boundary stages) |

**The scaffold provider mechanism** (`fsgg-sdd scaffold --provider rendering`)
reads `.fsgg/providers.yml`, validates the provider's contract major, then plans
`dotnet new install <source>` → SDD skeleton → `dotnet new <templateId>`. It
**guards SDD-owned trees** (`.fsgg/`, `work/`, `readiness/`, `AGENTS.md`,
`CLAUDE.md`): a provider that writes into them is reported `ProviderFailed`. SDD
embeds *no* rendering-specific identity — the runnable provider ships in Templates
(descriptor) and Rendering (template). The skeleton (`fsgg-sdd init`) writes the
`.fsgg/` slots SDD owns (`project.yml`/`sdd.yml`/`agents.yml`), per ADR-0005.

A separate `fsgg-sdd registry validate <path>` composes the YAML load edge with
`Fsgg.Registry.validateDocument` — this is the typed validator the contract
coherence gate runs (§5).

**The CLI is also the orchestrator** (ADR-0008 / ADR-0009). A scaffolded product is
`template@<pin>` **+ `fsgg-sdd`@`<installed>`**, and the CLI seeds artifacts that pin's
product is expected to contain (`fs-gg-sdd-*` process skills, `.fsgg/early-stage-guidance.md`),
so the CLI is a first-class member of the coherent set — the *orchestrator axis* of §5.
It is the single orchestration and enforcement surface but **not** the source of truth
(that stays declarative in the registry): on every command it **detects** drift
read-only — its own version vs. the pin's required minimum, and the seeded artifacts
present vs. those the pin expects — **warning when interactive and failing closed in
CI**. Remediation is never a side effect: a read-only `fsgg-sdd doctor` reports, and an
explicit `fsgg-sdd upgrade` (self-update + template re-pin + `refresh-agents` re-seed)
reconciles, **each as a confirmable diff**, touching only consumer-owned state. It
stamps the CLI version used + required minimum into `scaffold-provenance.json`.

### 4.3 FS.GG.Governance — the optional inference kernel

The largest repo by count (**166 `.fsproj`: 80 src + 84 Expecto/FsCheck test
projects, ~70 packable**), and entirely optional for its siblings.

**A pure, BCL-only kernel.** A fixed-point reasoner with full provenance, a
three-valued Kleene `Verdict` (`Pass`/`Fail`/`Uncertain`), and a reified `Check`
algebra folded **six ways from one source** (`eval`/`render`/`hash`/`explain`/
`reads`/`isReified`) so projections cannot drift apart.

**Two orthogonal axes** (the core model):

- `CheckTier = Deterministic | AgentReviewed | HumanOnly` — *who is competent to decide*.
- `Severity = Advisory | Blocking` — *whether failure stops you*.

These never collapse into each other; `Deterministic` is structurally refused for
opaque (non-reified) checks.

**The file-driven pipeline** — Config → Routing → Gates → Route → Enforcement →
Ship — is a real per-stage project chain. Enforcement runs on a six-value ordered
mode ladder `Sandbox < Inner < Focused < Verify < Gate < Release`, where only
**Verify / Gate / Ship** are blocking boundaries; `Sandbox`/`Inner` (the local
loop) are advisory-only. Profiles `Light | Standard | Strict | Release` tighten how
a maturity floor maps to a blocking boundary. The escape hatch is local-only and
loud — the merge boundary recomputes from scratch and ignores any local mode.

**The governance-handoff consumer** (`Adapters.SddHandoff`) reads SDD's optional
`readiness/<id>/governance-handoff.json`, importing no SDD code and mutating no
SDD-owned field. `fsgg-governance route --mode gate` returns exit `2`
(`GovernedBlocking`) when a consumed handoff blocks, and is **profile-aware**
(ADR-0005): strict tightens, light relaxes.

**The reference gate set** — `samples/sdd-reference-gate-set/.fsgg/`
(`governance.yml`/`policy.yml`/`capabilities.yml`/`tooling.yml`) — is packed *in
place* into a **content-only** NuGet package `FS.GG.Governance.ReferenceGateSet`
whose version (`1.2.1.1`) is *derived from* the four contained `schemaVersion`s
(ADR-0007). This is the one versioned source of truth the Templates overlay diffs
against.

### 4.4 FS.GG.Templates — the scaffold-time composition

Deliberately tiny: **no `src/`, no compiled code** — a `dotnet new` overlay, a
provider descriptor, helper scripts, and a shell composition harness.

**Composition by scaffold, not vendoring** (ADR-0002). `dotnet new` cannot depend
on another template, so rather than vendoring a rendering copy that goes stale,
the provider descriptor
[`providers/rendering.providers.yml`](https://github.com/FS-GG/FS.GG.Templates/blob/main/providers/rendering.providers.yml)
pins `FS.GG.UI.Template::<version>` and SDD installs the **live, pinned** upstream
package at scaffold time. `scripts/new-fullstack.sh` does the three steps: register
the provider → `fsgg-sdd scaffold` → apply the governance overlay *after* (so it
is not flagged writing into the SDD-owned `.fsgg/` tree).

**The `fs-gg-governance` overlay** ships a **populated** gate set (real
build/test/evidence checks wired to tooling commands), authored to Governance's
schemas. The composition harness
[`tests/composition/run.sh`](https://github.com/FS-GG/FS.GG.Templates/blob/main/tests/composition/run.sh)
is effectively the live integration test for the registry's dependency edges: it
packs, installs, instantiates, asserts pin coherence (provider, tag comment, and
README must name the same version), and proves the governance matrix end-to-end —
**strict + failing → exit 2, strict + satisfied → exit 0, light + failing → exit
0** — with independent SKIP probes so it never passes by omission.

---

## 5. The contract registry — the single source of truth

Because the system is split, cross-repo coherence is **explicit work**. The
machine-readable source of truth is
[`registry/dependencies.yml`](../registry/dependencies.yml) in this repo (human
projection: `docs/registry/compatibility.md`). It declares:

- **the four repos** and their roles;
- **versioned `contracts:`** — each with an owner, a surface (the on-disk file or
  package that *is* the contract), and its consumers;
- **hard dependency `dependencies:`** edges (downstream → upstream);
- a **`coherence:`** list, where `coherent: false` is a *standing cross-repo
  request* — a tracked promise not yet fully kept.

The protocol: **a `contract-change` issue MUST update this file as part of its
resolution.** The registry is validated in CI by the typed `Fsgg.Registry`
validator (`fsgg-sdd registry validate`), and a coherence gate asserts the
declared `fsgg-contracts` version equals the actual published package version.

The contracts that hold the system together:

| Contract | Owner | Surface | Consumed by |
|---|---|---|---|
| `scaffold-provider` | SDD | `.fsgg/providers.yml` + `dotnet new` wrapper protocol | Templates, Rendering |
| `fsgg-contracts` | SDD | the `FS.GG.Contracts` package (typed schemas + registry validator) | SDD, Governance, Templates |
| `scaffold-provenance` | SDD | `.fsgg/scaffold-provenance.json` | SDD |
| `governance-handoff` | SDD | `readiness/<id>/governance-handoff.json` (optional) | Governance |
| `governance-policy` / `-capabilities` / `-tooling` / `-descriptor` | Governance | the four `.fsgg/*.yml` slots | Templates |
| `governance-reference-gate-set` | Governance | the content-only `FS.GG.Governance.ReferenceGateSet` package | Templates |
| `fs-gg-ui-template` | Rendering | `dotnet new fs-gg-ui` + `FS.GG.UI.*` packages | Templates, SDD |
| `shared-build-config` | **.github** | `dist/dotnet/*` + `sync-build-config.sh` | all four |

Dependency edges (downstream → upstream): Templates → Rendering (template),
Templates → SDD (scaffold-provider), Templates → Governance (policy/overlay),
SDD → Governance (handoff, **optional**). Rendering points at nothing.

**The coherent set has three axes, not two.** A `fs-gg-ui-template@<V>` pin snapshots
the *template* and the *framework* — but a scaffolded product also carries the
`fsgg-sdd` CLI that generated it, and the CLI seeds artifacts the pin's product is
expected to contain (the `fs-gg-sdd-*` process skills, `.fsgg/early-stage-guidance.md`).
An old CLI on the newest pin silently omits them. **ADR-0008** closes that hole by
making the CLI a **first-class member of the coherent set** — the *orchestrator* axis
alongside template and framework — so the `fs-gg-ui-template` registry entry carries a
`minimum-fsgg-sdd` version (the oldest CLI that seeds those artifacts), validated by
`fsgg-sdd registry validate` and gated by `contract-coherence`. **ADR-0009** fixes the
*policy*: the CLI is the single orchestration and enforcement surface but **not** the
source of truth — it **detects** drift read-only on every command (interactive warns,
CI fails closed) and **remediates only through an explicit, diff-reviewed `fsgg-sdd
upgrade`** (self-update + re-pin + re-seed), never a silent auto-update. Truth stays
declarative in the registry so it can be diffed, gated, and flipped after publish
(`FR-007`). As of SDD v0.4.0 (2026-07-01) the registry pins `minimum-fsgg-sdd: 0.4.0`
(the oldest published CLI that seeds those artifacts — advanced `0.3.0→0.4.0` because
feature 056 made `fsgg-sdd` the sole skill-mirror authority, seeding the `fs-gg-sdd-*`
skills into a *third* root `.agents/skills/` and fanning the byte-identical union into
`.claude`/`.codex`/`.agents`, growing the seeded surface, FR-011). The
`fsgg-sdd-orchestrator-axis` coherence row is transiently `coherent: false` after that
publish-before-flip advance: the 0.4.0 CLI is LIVE on the org feed and the registry pins
0.4.0, but the Templates provider descriptor — what a scaffold actually reads — still
mirrors `0.3.0`; it re-coheres to `true` when Templates#47 re-mirrors `minimumFsggSdd
0.3.0→0.4.0` (alongside the `fs-gg-ui` template emitting UI skills into `.agents/skills/`
only). A behind-CLI scaffold is verified to warn (`scaffold.cliBehindMinimum`) and stamp
used+minimum into `scaffold-provenance` (original axis resolution closed epic #85).

The `coherence:` rows record verified, structurally-enforced invariants — for
example `lockfile-restore-enforcement` (a stale or silently-substituted dependency
fails restore in CI in every repo), `apicompat-publicapi-gate` (a public-API break
on a packable forces a SemVer major), `fs-gg-ui-version`/`-bom` (single-pin and
BOM coherence guarded on every Rendering PR), and
`governance-cli-handoff-consumer-published` (the full strict/light matrix proven
through the composed product). Cross-repo decisions are recorded as ADRs
(ADR-0002 composition-by-scaffold, ADR-0005 `.fsgg` slot ownership + canonical
`name`, ADR-0006 shared-build-config, ADR-0007 reference-gate-set version
derivation, ADR-0008 the CLI orchestrator axis, ADR-0009 detect-and-remediate
orchestration policy).

---

## 6. How it all composes (the end-to-end flow)

Composition happens **at scaffold time**, not by vendoring:

```text
 you ─▶ fsgg-sdd scaffold --provider rendering --param productName=MyApp
           │  (FS.GG.SDD)
           │      reads .fsgg/providers.yml ───────────────── scaffold-provider@1
           ├─ dotnet new install FS.GG.UI.Template::<pin> ──── fs-gg-ui-template   (FS.GG.Rendering)
           ├─ writes .fsgg/ + work/ + readiness/ skeleton
           └─ (optional) fs-gg-governance overlay ─────────── reference-gate-set   (FS.GG.Governance)
                                                               applied via         (FS.GG.Templates)

 FS.GG.Contracts ── validates ──▶ registry/dependencies.yml   (FS-GG/.github)
 dist/dotnet/*  ── sync-build-config.sh ──▶ Directory.Build.props in all four repos
```

The result is a real, windowed F# UI app plus a `.fsgg/` lifecycle skeleton you
can drive from `charter` to `ship`, with governance gates dropped in only if you
opt into them. There is no single all-in-one template, because that could only
exist by bundling a rendering copy that goes stale — the live, version-pinned
install is what keeps the composition honest. See the
[consumer guide](consumer/index.md) for the walkthrough.

---

## 7. Build, release, and CI conventions

- **One org-shared build config.** [`dist/dotnet/`](../dist/dotnet/) holds the
  canonical `Directory.Build.props`, `Directory.Packages.props`, and pinned
  `.config/dotnet-tools.json`, distributed verbatim by
  [`sync-build-config.sh`](../scripts/sync-build-config.sh). Each repo imports a
  `*.local.props` for repo-specific settings. A `--check` mode is the drift gate,
  run by the reusable `contract-coherence.yml` workflow (ADR-0006). See
  [`docs/build/README.md`](build/README.md).
- **Locked restore everywhere.** Every project commits `packages.lock.json`;
  CI restores `--locked-mode` (gated on `GITHUB_ACTIONS`), and `NU1603`/`NU1608`
  are promoted to errors so a silent version substitution fails the build.
- **Public-API breaking-change gate.** For F# packables the operative detector is
  the SDK's Package Validation / ApiCompat (PublicApiAnalyzers is C#-only); a real
  break fails the gate against the published feed baseline, forcing a SemVer major
  that the registry ranges enforce.
- **Cross-repo automation.** This repo hosts a reusable dispatch-sender workflow
  (App-token `repository_dispatch`, since `GITHUB_TOKEN` cannot dispatch
  cross-repo) and an org-shared Renovate preset
  ([`default.json`](../default.json), consumed via
  `extends: ["github>FS-GG/.github"]`) with custom managers for the embedded pins
  the standard NuGet manager misses. Producers push to the org GitHub Packages
  feed on release; consumers auto-PR the bump.

---

## 8. Where to start

- **Use FS-GG to build a product** → the [consumer guide](consumer/index.md)
  (install, scaffold, run, drive the lifecycle, optionally govern).
- **Develop FS-GG itself** → start at [`docs/index.md`](index.md) (the split
  decision record), then read the target product's repo `README.md`,
  `CONTRIBUTING.md`, and its `specs/` history.
- **Change a cross-repo contract** → read this page's §5, follow the
  `contract-change` protocol, and update
  [`registry/dependencies.yml`](../registry/dependencies.yml) as part of the
  resolution.

---

## Source index

**This repository**
- [`registry/dependencies.yml`](../registry/dependencies.yml) — the contract & dependency registry
- [`profile/README.md`](../profile/README.md) — the org landing page
- [`docs/index.md`](index.md) — the project-split decision record index
- [`docs/project-split-decision.md`](project-split-decision.md), [`docs/design-and-controls.md`](design-and-controls.md), [`docs/research-notes.md`](research-notes.md)
- [`docs/consumer/index.md`](consumer/index.md) — the consumer guide
- [`dist/dotnet/`](../dist/dotnet/), [`scripts/sync-build-config.sh`](../scripts/sync-build-config.sh), [`scripts/apply-labels.sh`](../scripts/apply-labels.sh)

**Product repositories**
- [FS.GG.Rendering](https://github.com/FS-GG/FS.GG.Rendering) — [solution](https://github.com/FS-GG/FS.GG.Rendering/blob/main/FS.GG.Rendering.slnx), [template manifest](https://github.com/FS-GG/FS.GG.Rendering/blob/main/.template.config/template.json), [reference rendering verdict](https://github.com/FS-GG/FS.GG.Rendering/blob/main/src/SkiaViewer/ReferenceRendering.fsi)
- [FS.GG.SDD](https://github.com/FS-GG/FS.GG.SDD) — [`Fsgg.Registry`](https://github.com/FS-GG/FS.GG.SDD/blob/main/src/FS.GG.Contracts/Registry.fsi), [`Fsgg.Schemas`](https://github.com/FS-GG/FS.GG.SDD/blob/main/src/FS.GG.Contracts/Schemas.fsi), [`CommandWorkflow.fs`](https://github.com/FS-GG/FS.GG.SDD/blob/main/src/FS.GG.SDD.Commands/CommandWorkflow.fs)
- [FS.GG.Governance](https://github.com/FS-GG/FS.GG.Governance) — [`README.md`](https://github.com/FS-GG/FS.GG.Governance/blob/main/README.md), [reference gate set](https://github.com/FS-GG/FS.GG.Governance/tree/main/samples/sdd-reference-gate-set/.fsgg)
- [FS.GG.Templates](https://github.com/FS-GG/FS.GG.Templates) — [provider descriptor](https://github.com/FS-GG/FS.GG.Templates/blob/main/providers/rendering.providers.yml), [composition harness](https://github.com/FS-GG/FS.GG.Templates/blob/main/tests/composition/run.sh)

> **Process status.** This page is the project's one **system-overview artifact** —
> the synthesis the point artifacts (ADRs, the registry) don't individually produce.
> It is **owned by `FS-GG/.github`** and non-authoritative (detail stays in the
> registry, the ADRs, and each product repo). Its maintenance is a process
> obligation, mirroring the "a `contract-change` must update the registry" rule:
> **any ADR that changes the shape of the system, and any `contract-change` that
> alters the §5 picture, MUST reconcile this page as part of its resolution** —
> update [`registry/dependencies.yml`](../registry/dependencies.yml) first (the
> protocol), then this page. See the
> [coordination protocol](coordination/README.md#system-overview--the-architecture-map).
