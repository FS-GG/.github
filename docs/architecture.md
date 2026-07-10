---
title: Architecture
category: FS.GG
categoryindex: 6
index: 0
description: A newcomer's guide to the FS-GG architecture — the component split, the one-way dependency rule, the contract registry, and how the repositories compose into one runnable workspace.
---

# FS-GG architecture

> **Audience.** This document is for people who want to understand *how FS-GG is
> built* — its repositories, boundaries, contracts, and the decisions that shape
> them. If you only want to *use* FS-GG to build an app, start with the
> [consumer guide](consumer/index.md) instead.

FS-GG is **F# tooling for building desktop UI apps on `net10.0`**: a
SkiaSharp/OpenGL UI framework, a spec-driven development (SDD) lifecycle CLI, and
optional governance tooling, that compose into one runnable workspace. It began as the
split of the archived [`FS-Skia-UI`](https://github.com/EHotwagner/FS-Skia-UI)
monolith into four focused, independently shippable components plus an
organization-level coordination repository, and is now growing a fifth framework
component — **FS.GG.Game**, the render-independent simulation core — extracted from
Rendering under [ADR-0022](adr/0022-extract-fs-gg-game-as-an-sdd-driven-component.md).

This page is a map. Authoritative detail lives in each component repository and in
the decision records linked throughout.

> **Terms (ADR-0020).** The **platform** is FS-GG as a whole — the seven
> repositories below (six framework components + `.github`; the sixth,
> **FS.GG.Game**, was extracted under ADR-0022 and published at P5; the seventh,
> **FS.GG.Audio**, was onboarded as a standalone component under ADR-0023). Each repository is a
> **component**. What a consumer *scaffolds* with the platform is a **workspace** —
> the generated repo with a runnable **app**, the `.fsgg/` lifecycle, skills, and
> optional governance. This page uses those words precisely; see
> [ADR-0020](adr/0020-platform-workspace-component-vocabulary.md).

---

## 1. The shape of the system

```text
                         ┌───────────────────────────────────────────────┐
                         │  FS-GG/.github  (this repo — coordination)     │
                         │  • registry/dependencies.yml (contract truth)  │
                         │  • registry/repos.yml (repo roster; ADR-0019)  │
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
└───┬─────────▲────┘   └──────────┬───────────┘   └────────┬─────────┘   └────────┬────────┘
    │         │                   │                        │                      │
    │         │ Game.Render▲      └── scaffold-provider ────┴── governance-* ──────┤
    │         └──────────┐ │  (Templates consumes fs-gg-ui-template + the two above)│
┌───┴──────────────────┐│ │                                                        │
│ FS.GG.Game  (NEW —   ││ └────── fs-gg-ui-template ───────────────────────────────┘
│ mid-extraction,      ││          (all three consumed by Templates at scaffold time; see §6)
│ ADR-0022)            │└─ Game.Render ─▶ FS.GG.UI.Scene  (adapter reaches UP — allowed)
│ Game.Core (BCL sim)  │
│ + Game.Render        │   Game.Core → nothing (BCL-only BOTTOM layer, sibling to Rendering).
└──────────────────────┘   FS.GG.Audio.* → nothing either (the second such bottom layer, ADR-0023).
                           Rendering's template reaches UP to both bottom layers (game-sim-core,
                           fs-gg-audio) and to nothing else; neither reaches back. One-way preserved.
```

Seven repositories under [github.com/FS-GG](https://github.com/FS-GG) (six framework
components + `.github`; **FS.GG.Game** was extracted under ADR-0022 — its packages
`FS.GG.Game.Core` + `.Render` 0.1.0-preview.1 are **published** to the org feed + nuget.org
and the Canvas+Scene majors shipped as `FS.GG.UI` 0.2.0-preview.1; `game-extraction`
**`coherent: true`** since P5, 2026-07-06; and **FS.GG.Audio** was onboarded as a standalone
render-independent component under ADR-0023, its `FS.GG.Audio.*` set published at
0.1.0 — promoted off the `-preview` channel on 2026-07-09 (FS.GG.Audio#4), which made it the
**last `FS.GG.*` producer to go stable**, so every producer in the org now ships on a stable
channel):

| Repository | Role | Ships |
|---|---|---|
| [**FS.GG.Rendering**](https://github.com/FS-GG/FS.GG.Rendering) | The UI framework — Scene, layout, input, viewer/host, controls, themes; Elmish/MVU over SkiaSharp/OpenGL. | `FS.GG.UI.*` packages + the `fs-gg-ui` `dotnet new` template |
| [**FS.GG.SDD**](https://github.com/FS-GG/FS.GG.SDD) | The lifecycle CLI + the typed cross-repo contract backbone. | `FS.GG.SDD.Cli` (`fsgg-sdd`) + `FS.GG.Contracts` |
| [**FS.GG.Governance**](https://github.com/FS-GG/FS.GG.Governance) | Optional rule / evidence / gate tooling — a pure inference kernel, advisory by default. | `FS.GG.Governance.Cli` (`fsgg-governance`) + the reference gate set |
| [**FS.GG.Templates**](https://github.com/FS-GG/FS.GG.Templates) | The composition — wires SDD + Rendering + Governance into one workspace at scaffold time. | the `rendering` scaffold provider + `fs-gg-governance` overlay |
| [**FS.GG.Game**](https://github.com/FS-GG/FS.GG.Game) *(extracted, ADR-0022; published P5)* | The render-independent simulation core + a thin Scene adapter — the new BCL-only bottom layer, extracted from Rendering. Developed with `fsgg-sdd` as its lifecycle. | `FS.GG.Game.Core` (BCL-only sim) + `FS.GG.Game.Render` (Scene adapter), 0.1.0-preview.1 on the org feed + nuget.org |
| [**FS.GG.Audio**](https://github.com/FS-GG/FS.GG.Audio) *(onboarded, ADR-0023)* | The render-independent game-audio component — pure `AudioEffect` vocabulary, an `IAudioBackend` device seam, a mixing Engine (buses / fades / ducking / 3D), and an Elmish `Cmd` bridge. Depends on no FS-GG component — a BCL-only bottom layer, sibling to Rendering and `FS.GG.Game.Core`. First consumed cross-repo by Rendering's template `game`/`sample-pack` profiles ([ADR-0024](adr/0024-wire-fs-gg-audio-into-the-game-scaffold-profile.md) step 3, [.github#238](https://github.com/FS-GG/.github/issues/238)), shipped in `fs-gg-ui-template` 0.3.1-preview.1. Developed with `fsgg-sdd` as its lifecycle. | `FS.GG.Audio.Core` / `.Host` / `.Engine` / `.Elmish`, 0.1.0-preview.1 on the org feed |
| [**FS-GG/.github**](https://github.com/FS-GG/.github) (this repo) | Cross-repo contract registry, the org repo roster + coordination-kit authority (ADR-0019), org-shared build config, consumer + decision docs. | — |

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
FS-GG component** — never on Governance. SDD depends on Governance only through an
*optional* handoff document it can produce and ignore. Your inner development loop
is never blocked by governance, and if governance ever feels heavy you can drop it
and keep building. This rule is restated on the
[org landing page](../profile/README.md) and is the invariant every contract in
§5 is designed to preserve.

---

## 3. House style (shared across all component repos)

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

## 4. The component repositories in detail

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
The template generates a **root-buildable** workspace: `Product.slnx` + `global.json`
+ `build.sh`/`build.cmd` FAKE verb wrapper, so `dotnet restore|build|test|run`
works at the workspace root with zero FAKE knowledge.

An optional **BOM metapackage** `FS.GG.UI` (`src/Meta/`) pins all 16
`FS.GG.UI.*` members at one exact version so drift fails restore.

**CI.** [`gate.yml`](https://github.com/FS-GG/FS.GG.Rendering/tree/main/.github/workflows)
is the single required pre-merge check; `release.yml` does the heavy packaging and
consumption tests; `template-dispatch.yml` fires the cross-repo
`fs-gg-ui-template-released` event to Templates on a release tag.

### 4.2 FS.GG.SDD — lifecycle CLI + contract backbone

Two packages in one repo (**11 projects: 5 src + 6 test**).

**`FS.GG.Contracts`** — the typed cross-repo contract backbone. A
**FSharp.Core-only BCL leaf** (no project references, no I/O), namespace `Fsgg`,
four modules:

- `Fsgg.ContractVersion` — a self-describing package SemVer (`val value = "1.4.0"`) so a consumer knows which surface it compiled against.
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
> evidence *around* your work, it does not produce your application code. The Spec Kit
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

**The CLI is also the orchestrator** (ADR-0008 / ADR-0009). A scaffolded workspace is
`template@<pin>` **+ `fsgg-sdd`@`<installed>`**, and the CLI seeds artifacts that pin's
workspace is expected to contain (`fs-gg-sdd-*` process skills, `.fsgg/early-stage-guidance.md`),
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
package at scaffold time. The [`new-sdd-workspace`](https://github.com/FS-GG/.github/tree/main/scripts/NewSddWorkspace)
dotnet tool (F# / Spectre.Console) does the three steps — register the provider → `fsgg-sdd scaffold`
→ apply the governance overlay *after* (so it is not flagged writing into the SDD-owned `.fsgg/` tree)
— with no FS.GG.Templates checkout, fetching the pinned descriptor over the network.

**The `fs-gg-governance` overlay** ships a **populated** gate set (real
build/test/evidence checks wired to tooling commands), authored to Governance's
schemas. The composition harness
[`tests/composition/run.sh`](https://github.com/FS-GG/FS.GG.Templates/blob/main/tests/composition/run.sh)
is effectively the live integration test for the registry's dependency edges: it
packs, installs, instantiates, asserts pin coherence (provider, tag comment, and
README must name the same version), and proves the governance matrix end-to-end —
**strict + failing → exit 2, strict + satisfied → exit 0, light + failing → exit
0** — with independent SKIP probes so it never passes by omission.

### 4.5 FS.GG.Game — the simulation core *(extracted + published P5, ADR-0022)*

The platform's newest component, being extracted from Rendering under
[ADR-0022](adr/0022-extract-fs-gg-game-as-an-sdd-driven-component.md). Game logic is
render-independent but has lived *inside* the render core — the `FS.GG.UI.Canvas.*` sim
primitives, the `Scene.Geometry` collision module, the `game` template profile, and four
`game`-gated skills. FS.GG.Game gives that subsystem its own home:

- `FS.GG.Game.Core` — a packable, **FSharp.Core-only** simulation core (RNG, fixed-step,
  collision, pathfinding, grids/spatial partitioning, FOV/LOS, ECS/state model). It becomes
  the **new bottom layer**: it reaches up to *nothing*, sibling to Rendering. A headless
  game sim builds and tests with zero Skia.
- `FS.GG.Game.Render` — a thin adapter (depends on `Game.Core` + `FS.GG.UI.Scene`) that maps
  sim state onto `Scene`. It reaches **up** to Rendering — allowed under the one-way rule,
  which the split preserves. (Rendering itself reaches up only to the BCL-only bottom layers
  `FS.GG.Game.Core` and, since [ADR-0024](adr/0024-wire-fs-gg-audio-into-the-game-scaffold-profile.md)
  step 3, `FS.GG.Audio.*` — both of which reach up to nothing.)

The cut line was settled by a pre-ADR usage audit
([`docs/reports/2026-07-06-p0-scene-geometry-cut-line-audit.md`](reports/2026-07-06-p0-scene-geometry-cut-line-audit.md)):
`Rect`/`Point` stay render-core in Scene, the game-only `Geometry` module moves to
`Game.Core` (Option D), and **no `FS.GG.Math` leaf is born**. The extraction forces a SemVer
major on **both** `FS.GG.UI.Canvas` (loses the four primitives) and `FS.GG.UI.Scene` (loses
the `Geometry` module) — a coordinated `contract-change`, not `[<Obsolete>]` aliases.

**FS.GG.Game is the platform's SDD dogfood.** It is developed with `fsgg-sdd` as its own dev
lifecycle (the `.fsgg/` `charter → ship` process), **coexisting** with a standard Spec Kit
`specs/NNN-*` history rather than replacing it — because SDD *brackets* implementation (§4.2)
rather than authoring it. As the first repo `init`'d by hand (no `fs-gg-ui-template` pin), it
is the forcing workload that makes SDD define a provider-less **"dev-repo" provenance shape**.
The consumer `game` scaffold provider is deferred and `dotnet new fs-gg-ui --profile game` is
**frozen** for the epic's duration — a named sequel epic retires the freeze. Phased plan:
[`docs/reports/2026-07-06-extract-fs-gg-game-component-sdd-driven.md`](reports/2026-07-06-extract-fs-gg-game-component-sdd-driven.md).

**Status (P4, 2026-07-06):** the render edge landed — `FS.GG.Game.Render` projects `Game.Core`
onto `FS.GG.UI.Scene` (consumed from nuget.org) — and the four game product skills migrated
`owner: fs-gg-rendering → fs-gg-game` byte-identically (reconciled from FS.GG.Game's own producer
skill-manifest; registry = manifest = bytes). Rendering keeps **frozen** byte-identical copies of
the game starter + skills — an accepted two-copies cost tracked as the `game-starter-two-copies`
coherence row, retired by the P6 provider epic. Still ahead: the physical `Canvas`/`Scene` major
removal + the package publish (P5).

---

## 5. The contract registry — the single source of truth

Because the system is split, cross-repo coherence is **explicit work**. The
machine-readable source of truth is
[`registry/dependencies.yml`](../registry/dependencies.yml) in this repo (human
projection: `docs/registry/compatibility.md`). It declares:

- **the component repos** and their roles (FS.GG.Game's contract rows land as it
  publishes — `coherent: false` until then, ADR-0022);
- **versioned `contracts:`** — each with an owner, a surface (the on-disk file or
  package that *is* the contract), and its consumers;
- **hard dependency `dependencies:`** edges (downstream → upstream);
- a **`coherence:`** list, where `coherent: false` is a *standing cross-repo
  request* — a tracked promise not yet fully kept.

The protocol: **a `contract-change` issue MUST update this file as part of its
resolution.** The registry is validated in CI on three axes, and the third is the
one that keeps it honest:

- against its **schema** — the typed `Fsgg.Registry` validator (`fsgg-sdd registry
  validate`), plus a gate asserting the declared `fsgg-contracts` version equals the
  actual `FS.GG.Contracts` package version read from SDD source;
- against its **projection** — `scripts/check-projection.py`, so `compatibility.md`
  cannot drift from the registry it projects;
- against **reality** — [`scripts/check-feed-coherence.py`](../scripts/check-feed-coherence.py)
  (coherence id `registry-feed-coherence`, [.github#267](https://github.com/FS-GG/.github/issues/267))
  asserts every `package-version` equals the newest version **live on the org feed**, in both
  directions, on every registry PR *and daily* — because a release that publishes without
  flipping the registry touches no file here, so nothing else can see it. Until this existed,
  publish-before-flip (FR-007) step 2 was gated by nobody noticing, and drifted three times.

A gate that passes when its subject is absent manufactures confidence, so each of these
**fails closed**: "nothing to check" and "checked, and it's fine" must not share an exit code
([.github#266](https://github.com/FS-GG/.github/issues/266)).

**A fourth axis, one layer up: the gates' own pins.** The schema axis above runs a *pinned*
version of the typed validator, so that pin is a subject too — and a frozen one silently
degrades the typed gate toward a "does the YAML parse" check, while the registry row asserting
its coherence stays green. It froze twice ([.github#127](https://github.com/FS-GG/.github/issues/127),
[.github#263](https://github.com/FS-GG/.github/issues/263)), each time found by eye during a
release. [`scripts/check-pin-coherence.py`](../scripts/check-pin-coherence.py) (coherence id
`pin-feed-coherence`) now compares every `# renovate:`-annotated literal in this repo against the
newest version live on the org feed, *and* asserts the `hostRules` feed token without which
Renovate cannot bump any of them — the mechanism the pin's coherence had always been assumed to
rest on, and which `.github` alone never configured. It takes its notion of "what is a pin" from
the org preset's own annotation regex, so the gate and the bot cannot disagree about the subject.

**Sibling registries in this repo.** Two more `.github`-owned registries sit alongside
`dependencies.yml`: [`registry/skills.yml`](../registry/skills.yml) (the skill catalog —
also a versioned contract, `skill-registry`, in the table below) and
[`registry/repos.yml`](../registry/repos.yml) (the **org repo roster**, ADR-0019 — the
single authoritative list of framework repos each org fabric iterates, gated per a
`receives` capability). The roster is *not* a versioned cross-repo contract — it is
validated self-contained by `scripts/repos.sh`, not the typed `Fsgg.Registry` — but it is
the source of truth for participation in each fabric (labels, the coordination-kit
distribution/audit, …), with `.github` as the kit authority.

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
| `game-sim-core` | Game | the `FS.GG.Game.Core` package (BCL-only sim bottom layer, `$(FsGgGameVersion)` axis) | Rendering (template `game`/`sample-pack`) |
| `game-scene-adapter` | Game | the `FS.GG.Game.Render` package (projects sim state onto `FS.GG.UI.Scene` drawables — the one edge back down) | Rendering |
| `fs-gg-audio` | Audio | the `FS.GG.Audio.Core`/`.Host`/`.Engine`/`.Elmish` packages (BCL-only audio bottom layer, `$(FsGgAudioVersion)` axis) | Rendering (template `game`/`sample-pack`, gated) |
| `keyboard-input` | Rendering | the `FS.GG.UI.KeyboardInput` `Keymap` surface (value type + rebind + `Keymap.resolve`/conflict diagnostics; ships in the fs-gg-ui coherent set @ `0.5.0`, [ADR-0028](adr/0028-keyboard-input-config-mechanism-policy-boundary.md)) | Game (`FS.GG.Game.Render` default command→key keymap) |
| `shared-build-config` | **.github** | `dist/dotnet/*` + `sync-build-config.sh` | all component repos |
| `registry-schema` | SDD | the `registry/dependencies.yml` document schema (`schemaVersion` + field vocabulary), modeled by `Fsgg.Registry` | **.github** (the contract-coherence gate) |
| `skill-registry` | **.github** | [`registry/skills.yml`](../registry/skills.yml) — the org's authoritative skill catalog (process + product; `id`, `scope`, `owner`, canonical-body `sha256`, `materializes-when`), reconciled from the producer skill-manifests (ADR-0017) | **.github** (the union gate + registry validation) |

Dependency edges (downstream → upstream): Templates → Rendering (template),
Templates → SDD (scaffold-provider), Templates → Governance (policy/overlay),
SDD → Governance (handoff, **optional**), **.github → SDD** (`registry-schema` —
the coherence gate validates this registry with SDD's typed `Fsgg.Registry`), and
**.github → Rendering + SDD** (`skill-registry` — `skills.yml` is reconciled from the two
producers' skill-manifests).

**Rendering → Game** (`game-sim-core`, [ADR-0022](adr/0022-extract-fs-gg-game-as-an-sdd-driven-component.md) P5) and
**Rendering → Audio** (`fs-gg-audio`@`0.1.0` — the template payload re-pinned onto the stable
channel in FS.GG.Rendering#238, alongside `game-sim-core`@`0.2.0`; [ADR-0024](adr/0024-wire-fs-gg-audio-into-the-game-scaffold-profile.md) step 3,
[.github#238](https://github.com/FS-GG/.github/issues/238)): Rendering's template `game`/`sample-pack`
profiles reach *up* to two BCL-only bottom layers that are siblings of Rendering and themselves reach
up to nothing (`FS.GG.Game.Core` on the `$(FsGgGameVersion)` axis; `FS.GG.Audio.*` on
`$(FsGgAudioVersion)`). **Game → Rendering** now runs over two contracts back down: `game-scene-adapter` (`FS.GG.Game.Render` projects sim state onto `FS.GG.UI.Scene` drawables) and — since [.github#365](https://github.com/FS-GG/.github/issues/365) / [ADR-0028](adr/0028-keyboard-input-config-mechanism-policy-boundary.md) — `keyboard-input` (`FS.GG.Game.Render`'s default command→key keymap consumes `FS.GG.UI.KeyboardInput`'s `Keymap` surface @ `0.5.0`; [FS.GG.Game#109](https://github.com/FS-GG/FS.GG.Game/issues/109)). A
scaffolded product receives all of these through the template, so Templates consumes them
transitively — there is no `templates → game` or `templates → audio` edge.

**The registry's own schema is a governed contract too (ADR-0015).** It was the one
contract in the system that wasn't: the typed validator (the `registry-validator-typed`
coherence row) only pays off if the schema is *versioned* and the gate's `FS.GG.SDD.Cli`
pin *advances with it* — a [review](reports/2026-07-02-code-quality-architecture-review.md)
found the pin frozen for three minors while the schema grew fields under additive
tolerance, degrading the gate toward a YAML-parses check. The `registry-schema` entry (owner SDD, consumer .github) versions the on-disk
`schemaVersion`, so schema growth is now a tracked `contract-change` (bump the version +
advance the pin, in lockstep) rather than silent drift.

**The coherent set has three axes, not two.** A `fs-gg-ui-template@<V>` pin snapshots
the *template* and the *framework* — but a scaffolded workspace also carries the
`fsgg-sdd` CLI that generated it, and the CLI seeds artifacts the pin's workspace is
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
`.claude`/`.codex`/`.agents`, growing the seeded surface, FR-011). Both halves are in
lockstep (2026-07-02, Templates#49/#51): the provider descriptor mirrors `minimumFsggSdd
0.4.0`, the pinned `fs-gg-ui` template (`0.1.61-preview.1`) emits UI skills into
`.agents/skills/` only on the sdd path, and the `fsgg-sdd-orchestrator-axis` coherence
row is `coherent: true`. A behind-CLI scaffold is verified to warn
(`scaffold.cliBehindMinimum`) and stamp used+minimum into `scaffold-provenance`
(original axis resolution closed epic #85).

**The skill union is content-verified in every lane (ADR-0014).** Skills are
content-addressed data: each producer declares a skill-manifest (`{id, scope,
sha256}`, canonical SKILL.md-body digest); ONE `mirror`/`verify` library
(`Fsgg.SkillMirror`, FS.GG.Contracts ≥ 1.4.0) owns all orchestrated fan-out, and the
standalone spec-kit lane runs a vendored byte-equivalent as its single materialize
step. The invariant — three byte-identical union roots, nothing dangling — is
asserted where skills are produced (`doctor`, per-skill `sha256` in
`scaffold-provenance`) *and* where they are consumed: the Templates composition gate
enforces it hard in both lanes via the reusable `skill-union-assert.sh`
(`skill-mirror-verified` coherence row, `coherent: true` since 2026-07-02).

**Skill *absence* is checkable too (ADR-0017).** The manifest is a superset catalog —
a producer declares every skill it *can* emit, but emission is profile/lifecycle-gated,
so "declared ∧ absent" was blanket-tolerated and a genuinely-dropped skill was
indistinguishable from an off-profile one. ADR-0017 records the emission condition per
entry (`materializes-when`) and lifts it into a single authoritative catalog
[`registry/skills.yml`](../registry/skills.yml) (the `skill-registry` governed contract);
the union gate's `--params` mode then evaluates each condition against a scaffold's
provenance and adds `[missing]` (declared ∧ true ∧ absent) and `[unexpected]`
(present ∧ false) — closing the blind spot. Rollout is board-sequenced (`skill-registry-published`
coherence row): the catalog + contract landed, and both producer halves are now emitted — Rendering's
product manifest (Feature 238) and SDD's process manifest (`.agents/skills/skill-manifest.json`,
SDD#111 closing #109). Both producer-side conditions have now cleared (.github#290): Rendering's
predicate-grammar alignment landed in Rendering#77 (closed 2026-07-04), so producer and registry both
speak the ADR-0017 canonical grammar. The enforcing flip now waits on a **.github-side** step only —
publishing the typed `Fsgg.Registry` validator assertion over `skills.yml` — and must not land over a
red `skill-registry-coherence` (the fails-open class, epic #266).

The `coherence:` rows record verified, structurally-enforced invariants — for
example `lockfile-restore-enforcement` (a stale or silently-substituted dependency
fails restore in CI in every repo), `apicompat-publicapi-gate` (a public-API break
on a packable forces a SemVer major), `fs-gg-ui-version`/`-bom` (single-pin and
BOM coherence guarded on every Rendering PR), and
`governance-cli-handoff-consumer-published` (the full strict/light matrix proven
through the composed workspace). Cross-repo decisions are recorded as ADRs
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
 dist/dotnet/*  ── sync-build-config.sh ──▶ Directory.Build.props in all component repos
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
- **Public distribution (dual-publish, [ADR-0012](adr/0012-dual-publish-to-nuget-org.md) +
  [ADR-0013](adr/0013-trusted-publishing-oidc-for-nuget-org.md)).** On release each producer
  additionally pushes the **byte-identical** `.nupkg` to **public nuget.org** (after the
  org-feed push), so public consumers `dotnet tool install` / `dotnet add package` with no
  `--add-source`. Auth is **Trusted Publishing (OIDC)** — a short-lived `NuGet/login` key per
  run, no stored secret. The org GitHub Packages feed stays the **coherence/`-preview` source
  of truth** (Renovate and the contract-coherence gate read it); nuget.org is an additive
  public target (`nuget-org-published` ✅). Package IDs on nuget.org are permanent, freezing the
  `FS.GG.*` identities (no rename — [ADR-0003](adr/0003-rename-fs-skia-ui-version-machinery-to-fs-gg-ui.md)).

---

## 8. Where to start

- **Use FS-GG to build an app** → the [consumer guide](consumer/index.md)
  (install, scaffold, run, drive the lifecycle, optionally govern).
- **Develop FS-GG itself** → start at [`docs/index.md`](index.md) (the split
  decision record), then read the target component's repo `README.md`,
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

**Component repositories**
- [FS.GG.Rendering](https://github.com/FS-GG/FS.GG.Rendering) — [solution](https://github.com/FS-GG/FS.GG.Rendering/blob/main/FS.GG.Rendering.slnx), [template manifest](https://github.com/FS-GG/FS.GG.Rendering/blob/main/.template.config/template.json), [reference rendering verdict](https://github.com/FS-GG/FS.GG.Rendering/blob/main/src/SkiaViewer/ReferenceRendering.fsi)
- [FS.GG.SDD](https://github.com/FS-GG/FS.GG.SDD) — [`Fsgg.Registry`](https://github.com/FS-GG/FS.GG.SDD/blob/main/src/FS.GG.Contracts/Registry.fsi), [`Fsgg.Schemas`](https://github.com/FS-GG/FS.GG.SDD/blob/main/src/FS.GG.Contracts/Schemas.fsi), [`CommandWorkflow.fs`](https://github.com/FS-GG/FS.GG.SDD/blob/main/src/FS.GG.SDD.Commands/CommandWorkflow.fs)
- [FS.GG.Governance](https://github.com/FS-GG/FS.GG.Governance) — [`README.md`](https://github.com/FS-GG/FS.GG.Governance/blob/main/README.md), [reference gate set](https://github.com/FS-GG/FS.GG.Governance/tree/main/samples/sdd-reference-gate-set/.fsgg)
- [FS.GG.Templates](https://github.com/FS-GG/FS.GG.Templates) — [provider descriptor](https://github.com/FS-GG/FS.GG.Templates/blob/main/providers/rendering.providers.yml), [composition harness](https://github.com/FS-GG/FS.GG.Templates/blob/main/tests/composition/run.sh)

> **Process status.** This page is the project's one **system-overview artifact** —
> the synthesis the point artifacts (ADRs, the registry) don't individually produce.
> It is **owned by `FS-GG/.github`** and non-authoritative (detail stays in the
> registry, the ADRs, and each component repo). Its maintenance is a process
> obligation, mirroring the "a `contract-change` must update the registry" rule:
> **any ADR that changes the shape of the system, and any `contract-change` that
> alters the §5 picture, MUST reconcile this page as part of its resolution** —
> update [`registry/dependencies.yml`](../registry/dependencies.yml) first (the
> protocol), then this page. See the
> [coordination protocol](coordination/README.md#system-overview--the-architecture-map).
