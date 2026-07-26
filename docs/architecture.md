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
organization-level coordination repository. It has since grown three more: **FS.GG.Game**,
the render-independent simulation core extracted from Rendering under
[ADR-0022](adr/0022-extract-fs-gg-game-as-an-sdd-driven-component.md); **FS.GG.Audio**,
onboarded as a standalone component under
[ADR-0023](adr/0023-onboard-fs-gg-audio-as-an-sdd-driven-component.md); and **FS.GG.Net**, the
render-independent transport component (protobuf over WebSocket / gRPC) onboarded under
[ADR-0052](adr/0052-onboard-fs-gg-net-transport-component.md). All three are **published**
to the org feed and nuget.org — [§5](#5-the-contract-registry--the-single-source-of-truth) carries
the versions, generated from the registry. **Seven framework components**, plus `.github`.

This page is a map. Authoritative detail lives in each component repository and in
the decision records linked throughout.

> **Terms (ADR-0020).** The **platform** is FS-GG as a whole — the eight
> repositories below (seven framework components + `.github`; the sixth,
> **FS.GG.Game**, was extracted under ADR-0022 and published at P5; the seventh,
> **FS.GG.Audio**, was onboarded as a standalone component under ADR-0023; the eighth,
> **FS.GG.Net**, the transport component, under ADR-0052). Each repository is a
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
                         │  • registry/repos.lock (kit digests; #527)     │
                         │  • dist/dotnet/ (org-shared build config)      │
                         │  • docs/ (decision records + consumer guide)   │
                         │  • the org CLIs: new-sdd-workspace, and the    │
                         │    coordination engine (fsgg-coord; ADR-0034)  │
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
│ FS.GG.Game           ││ └────── fs-gg-ui-template ───────────────────────────────┘
│ (extracted+published,││          (all three consumed by Templates at scaffold time; see §6)
│ ADR-0022)            │└─ Game.Render ─▶ FS.GG.UI.Scene  (adapter reaches UP — allowed)
│ Game.Core (BCL sim)  │
│ + Game.Render        │   Game.Core → nothing (BCL-only BOTTOM layer, sibling to Rendering).
└──────────────────────┘   FS.GG.Audio.* → nothing either (the second such bottom layer, ADR-0023).
                           FS.GG.Net.* → nothing either (a third bottom layer, ADR-0052; consumed by
                           apps — SC2/BAR clients — not by the template).
                           Rendering's template reaches UP to the game + audio bottom layers
                           (game-sim-core, fs-gg-audio) and to nothing else; none reaches back. One-way preserved.
```

Eight repositories under [github.com/FS-GG](https://github.com/FS-GG) (seven framework
components + `.github`; **FS.GG.Game** was extracted under ADR-0022 — its packages
`FS.GG.Game.Core` + `.Render` are **published** to the org feed + nuget.org;
`game-extraction` **`coherent: true`** since P5, 2026-07-06; **FS.GG.Audio** was onboarded
as a standalone render-independent component under ADR-0023, its `FS.GG.Audio.*` set
**published** — promoted off the `-preview` channel on 2026-07-09 (FS.GG.Audio#4), which made it
the **last `FS.GG.*` producer to go stable**; and **FS.GG.Net** was onboarded under ADR-0052, its
six-package `FS.GG.Net.*` set **published** at `0.1.0` (v0.1.0, 2026-07-19) on both feeds — every
producer in the org ships on a stable channel). [§5](#5-the-contract-registry--the-single-source-of-truth) carries what each is
published *at*:

| Repository | Role | Ships |
|---|---|---|
| [**FS.GG.Rendering**](https://github.com/FS-GG/FS.GG.Rendering) | The UI framework — Scene, layout, input, viewer/host, controls, themes; Elmish/MVU over SkiaSharp/OpenGL. | `FS.GG.UI.*` packages + the `fs-gg-ui` `dotnet new` template |
| [**FS.GG.SDD**](https://github.com/FS-GG/FS.GG.SDD) | The lifecycle CLI + the typed cross-repo contract backbone. | `FS.GG.SDD.Cli` (`fsgg-sdd`) + `FS.GG.Contracts` |
| [**FS.GG.Governance**](https://github.com/FS-GG/FS.GG.Governance) | Optional rule / evidence / gate tooling — a pure inference kernel, advisory by default. | `FS.GG.Governance.Cli` (`fsgg-governance`) + the reference gate set |
| [**FS.GG.Templates**](https://github.com/FS-GG/FS.GG.Templates) | The composition — wires SDD + Rendering + Governance into one workspace at scaffold time. | the `rendering` scaffold provider + `fs-gg-governance` overlay |
| [**FS.GG.Game**](https://github.com/FS-GG/FS.GG.Game) *(extracted, ADR-0022; published P5)* | The render-independent simulation core + a thin Scene adapter — the new BCL-only bottom layer, extracted from Rendering. Developed with `fsgg-sdd` as its lifecycle. | `FS.GG.Game.Core` (BCL-only sim) + `FS.GG.Game.Render` (Scene adapter), on the org feed + nuget.org |
| [**FS.GG.Audio**](https://github.com/FS-GG/FS.GG.Audio) *(onboarded, ADR-0023)* | The render-independent game-audio component — pure `AudioEffect` vocabulary, an `IAudioBackend` device seam, a mixing Engine (buses / fades / ducking / 3D), and an Elmish `Cmd` bridge. Depends on no FS-GG component — a BCL-only bottom layer, sibling to Rendering and `FS.GG.Game.Core`. First consumed cross-repo by Rendering's template `game`/`sample-pack` profiles ([ADR-0024](adr/0024-wire-fs-gg-audio-into-the-game-scaffold-profile.md) step 3, [.github#238](https://github.com/FS-GG/.github/issues/238)), shipped in `fs-gg-ui-template` 0.3.1-preview.1. Developed with `fsgg-sdd` as its lifecycle. | `FS.GG.Audio.Core` / `.Host` / `.Engine` / `.Elmish`, on the org feed + nuget.org |
| [**FS.GG.Net**](https://github.com/FS-GG/FS.GG.Net) *(onboarded, ADR-0052; published 0.1.0)* | The render-independent, domain-neutral transport component — an `ITransport` / `IMessageChannel` seam with `Sequential` / `Multiplexed` client correlation and `serve` / `ServerEcho` on the server side, a client + Kestrel-server WebSocket transport, Google.Protobuf + protobuf-net codecs, a thin gRPC lifecycle bridge, and an Elmish `Cmd` / `Sub` bridge. Depends on no FS-GG component — a BCL-first bottom layer, sibling to `FS.GG.Game.Core` and `FS.GG.Audio`. Consumers are app repos (SC2 / BAR clients), not FS-GG components. Verified against a real SC2 server + an in-process gRPC service. | `FS.GG.Net.Core` / `.WebSocket` / `.WebSocket.Server` / `.Protobuf` / `.Grpc` / `.Elmish`, on the org feed + nuget.org |
| [**FS-GG/.github**](https://github.com/FS-GG/.github) (this repo) | Cross-repo contract registry, the org repo roster + coordination-kit authority (ADR-0019), org-shared build config, consumer + decision docs. **Also a producer** — it owns the two org-level CLIs, and their release workflows live here because the tools do ([ADR-0016](adr/0016-retire-templates-local-new-fullstack-single-scaffolder.md), [ADR-0034](adr/0034-typed-coordination-engine.md)). | `FS.GG.NewSddWorkspace` (`new-sdd-workspace`) + `FS.GG.Coord.Cli` (the ADR-0034 engine) — **both published**, org feed + nuget.org |

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
  settings live in `*.local.props`. A drift check fails the PR — but it measures
  against the commit the receiver **pinned**, not against `.github@main`, so being
  behind is green and only a hand-edit is red (ADR-0036; see §7).

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
`lifecycle` (`spec-kit`/`sdd`/`none` — `sdd` is the default and `spec-kit` a frozen
legacy lane since [ADR-0056](adr/0056-sdd-is-the-default-lifecycle-spec-kit-is-legacy-and-scheduled-for-removal.md),
amending ADR-0002), `productName` (the additive alias
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
five modules:

- `Fsgg.ContractVersion` — a self-describing package SemVer (`val value`, carrying the `fsgg-contracts` version [§5](#5-the-contract-registry--the-single-source-of-truth) records) so a consumer knows which surface it compiled against.
- [`Fsgg.Schemas`](https://github.com/FS-GG/FS.GG.SDD/blob/main/src/FS.GG.Contracts/Schemas.fsi) — one typed source of truth for every `.fsgg` schema shape and its version constant (SDD- and Governance-owned).
- [`Fsgg.Provider`](https://github.com/FS-GG/FS.GG.SDD/blob/main/src/FS.GG.Contracts/Provider.fsi) — the extended scaffold-provider descriptor (canonical `NameParameter`).
- [`Fsgg.Registry`](https://github.com/FS-GG/FS.GG.SDD/blob/main/src/FS.GG.Contracts/Registry.fsi) — **the validator for this repo's [`registry/dependencies.yml`](../registry/dependencies.yml)**. Its version grammar deliberately mirrors `scripts/validate-registry.py` byte-for-byte, including the 4-segment `major.minor.patch.revision` form (ADR-0007), so the typed validator and the (now retired) Python authority cannot disagree.
- [`Fsgg.SkillMirror`](https://github.com/FS-GG/FS.GG.SDD/blob/main/src/FS.GG.Contracts/SkillMirror.fsi) — the one `mirror`/`verify` library that owns all orchestrated skill fan-out (ADR-0014); §5's skill-union invariant below is stated against it.

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
stamps the CLI version used + required minimum into `scaffold-provenance.json`. The one
bounded exception is **creation** (ADR-0030): `new-sdd-workspace` self-updates the CLI to
the newest coherent set *before* it scaffolds, **by default** (`--pinned` opts back into a
reproducible pin) — there is no existing consumer to clobber and no prior run to reproduce,
so the detect-and-warn policy above applies only to *in-project* invocations.

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
whose version is a plain SemVer that bumps on any change to the packed set — content or schema —
rather than *derived from* the four contained `schemaVersion`s (ADR-0055, superseding ADR-0007; the
four schema generations now ship as an in-package `schema-manifest.json`, and
[§5](#5-the-contract-registry--the-single-source-of-truth) carries the current one). This is the
one versioned source of truth the Templates overlay diffs against.

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
— with no FS.GG.Templates checkout, fetching the pinned descriptor over the network. By default it
first self-updates `fsgg-sdd` to the newest coherent set so the scaffold is built by current tooling
(ADR-0030; `--pinned` skips it for a reproducible pin).

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

The platform's simulation core, extracted from Rendering and published under
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
The consumer `game` scaffold provider was deferred and `dotnet new fs-gg-ui --profile game`
**frozen** for the epic's duration. [ADR-0063](adr/0063-scaffold-materializer-sources-skills-from-the-owner-repo.md)
then **cancelled** that deferred provider: the freeze is retired not by a second provider but by the
scaffold materializer sourcing FS.GG.Game's `product-skills/` (and any owner repo's skills) directly
from the `owner`/`source` the registry row already names. Phased plan:
[`docs/reports/2026-07-06-extract-fs-gg-game-component-sdd-driven.md`](reports/2026-07-06-extract-fs-gg-game-component-sdd-driven.md).

`FS.GG.Game.Render` projects `Game.Core` onto `FS.GG.UI.Scene` (consumed from nuget.org), and the
four game product skills migrated `owner: fs-gg-rendering → fs-gg-game` byte-identically (reconciled
from FS.GG.Game's own producer skill-manifest; registry = manifest = bytes). Rendering keeps
**frozen** byte-identical copies of the game starter + skills — an accepted two-copies cost tracked
as the `game-starter-two-copies` coherence row, retired by owner-repo materializer sourcing
([ADR-0063](adr/0063-scaffold-materializer-sources-skills-from-the-owner-repo.md)) rather than the
originally-planned provider epic. Live phase state is
the `game-extraction` coherence row's to report, not this page's.

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
  validate`), run by the reusable `contract-coherence.yml`;
- against its **projection** — `scripts/check-projection.py`, so `compatibility.md`
  cannot drift from the registry it projects;
- against **reality** — two gates, one per subject, because `version` and
  `package-version` name **different facts**:
  [`scripts/check-source-coherence.py`](../scripts/check-source-coherence.py)
  ([.github#741](https://github.com/FS-GG/.github/issues/741)) asserts `fsgg-contracts`'
  `version` equals the `FS.GG.Contracts` **source** SemVer on `FS.GG.SDD@main`, and
  [`scripts/check-feed-coherence.py`](../scripts/check-feed-coherence.py)
  (coherence id `registry-feed-coherence`, [.github#267](https://github.com/FS-GG/.github/issues/267))
  asserts every `package-version` equals the newest version **live on the org feed**, in both
  directions. Both run on every registry PR *and daily* — because an SDD source bump, or a
  release that publishes without flipping the registry, touches no file here, so nothing else
  can see it. Until the feed half existed, publish-before-flip (FR-007) step 2 was gated by
  nobody noticing, and drifted three times.

The source half **used to live under the schema axis**, inside the reusable
`contract-coherence.yml` that all six repos call — which was wrong twice over. It is not a
schema fact, and asserting another repo's `main` from an org-wide required check meant a
Contracts bump wedged every repo at once with no safe landing order, since no PR spans both
repos ([FS.GG.SDD#432](https://github.com/FS-GG/FS.GG.SDD/issues/432)). **The reusable gate now
asserts only pure functions of committed files**; registry-vs-reality is `.github`-local, so a
red stops the repo that owns the registry rather than the org.

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
| `governance-handoff` | SDD | `readiness/<id>/governance-handoff.json` (optional) — @`1.1.0`: [ADR-0035](adr/0035-observed-run-receipts.md) stage 3 made the blocking diagnostic id `ship.unobservedEvidence` reachable, additively (no enum moved, `schemaVersion` still 1). SDD reports the unobserved fact; **Governance owns what it costs at a merge boundary**. ⚠️ this cell used to read *"the emitter still stamps `contractVersion: "1.0.0"`"* — **false since 2026-07-14** ([FS.GG.SDD#427](https://github.com/FS-GG/FS.GG.SDD/issues/427), commit `f51680d3`): the emitter **reads** the constant and the artifact self-declares `1.1.0`, agreeing with this row. What remains is that nothing **gates** the two — they are kept in step by hand, and that gate is `.github`'s: [#1085](https://github.com/FS-GG/.github/issues/1085). Coherence `governance-handoff-emitted-version` is `coherent: true` ([#1082](https://github.com/FS-GG/.github/issues/1082)). | Governance |
| `governance-policy` / `-capabilities` / `-tooling` / `-descriptor` | Governance | the four `.fsgg/*.yml` slots | Templates |
| `governance-reference-gate-set` | Governance | the content-only `FS.GG.Governance.ReferenceGateSet` package | Templates |
| `fs-gg-ui-template` | Rendering | `dotnet new fs-gg-ui` + `FS.GG.UI.*` packages | Templates, SDD |
| `game-sim-core` | Game | the `FS.GG.Game.Core` package (BCL-only sim bottom layer, `$(FsGgGameVersion)` axis) | Rendering (template `game`/`sample-pack`) |
| `game-scene-adapter` | Game | the `FS.GG.Game.Render` package (projects sim state onto `FS.GG.UI.Scene` drawables — the one edge back down) | Rendering |
| `fs-gg-audio` | Audio | the `FS.GG.Audio.Core`/`.Host`/`.Engine`/`.Elmish` packages (BCL-only audio bottom layer, `$(FsGgAudioVersion)` axis) | Rendering (template `game`/`sample-pack`, gated) |
| `keyboard-input` | Rendering | the `FS.GG.UI.KeyboardInput` `Keymap` surface (value type + rebind + `Keymap.resolve`/conflict diagnostics; ships in the fs-gg-ui coherent set @ `0.5.0`, [ADR-0028](adr/0028-keyboard-input-config-mechanism-policy-boundary.md)) | Game (`FS.GG.Game.Render` default command→key keymap) |
| `shared-build-config` | **.github** | `dist/dotnet/*` + `sync-build-config.sh` (+ the receiver's `.config/fsgg-build-config.sha` pin, ADR-0036) | all component repos |
| `registry-schema` | SDD | the `registry/dependencies.yml` document schema (`schemaVersion` + field vocabulary), modeled by `Fsgg.Registry` | **.github** (the contract-coherence gate) |
| `skill-registry` | **.github** | [`registry/skills.yml`](../registry/skills.yml) — the org's authoritative skill catalog (process + product; `id`, `scope`, `owner`, `source`, canonical-body `sha256`, `materializes-when`, and the optional `mirrored` frozen-mirror obligation — absent means *not classified*, never `false`), reconciled from the producer skill-manifests (ADR-0017). An OPTIONAL, ADDITIVE field **is** schema growth under ADR-0015 (decided [#686](https://github.com/FS-GG/.github/issues/686)), so `mirrored` owes a `schemaVersion` 1→2 bump — paid publish-before-flip, once `Fsgg.Registry` learns the field ([FS.GG.SDD#420](https://github.com/FS-GG/FS.GG.SDD/issues/420)) | **.github** (the union gate + registry validation) |

<!-- BEGIN GENERATED: fsgg-versions -->
<!--
  DO NOT EDIT THIS REGION. It is emitted from registry/dependencies.yml by
  scripts/generate-projections, and `projections` in CI fails on any diff.

  This region exists because this page was the one version-bearing projection with no generator
  and no gate (#913). Every number below is the registry's, and the registry is held to the live
  feed by check-feed-coherence.py. Edit registry/dependencies.yml and regenerate; if a number
  here is wrong, the registry is wrong, and fixing it HERE would only hide that.
-->

*Generated from `registry/dependencies.yml`. `version` is the contract SURFACE's SemVer and
`package-version` is what is LIVE on the org feed: different facts, and publish-before-flip
(FR-007) means `package-version` never leads `version` — so read them together, because
`version` alone never tells you what a consumer can restore. One exception the schema itself
carves: for `fs-gg-ui-template`, `version` is the FRAMEWORK pin (`FsGgUiVersion` — the generated
product's `FS.GG.UI.*` pin), which is a different axis from the template package's
`package-version`, not a lagging copy of it.*

| Contract | Owner | `version` | `package-version` |
|---|---|---|---|
| `fsgg-contracts` | FS.GG.SDD | `5.0.1` | `5.0.1` |
| `governance-reference-gate-set` | FS.GG.Governance | `1.4.0` | `1.4.0` |
| `fs-gg-ui-template` | FS.GG.Rendering | `0.19.0` | `0.19.1` |
| `game-sim-core` | FS.GG.Game | `0.10.1` | `0.10.1` |
| `game-scene-adapter` | FS.GG.Game | `0.10.1` | `0.10.1` |
| `fs-gg-audio` | FS.GG.Audio | `0.5.0` | `0.5.0` |
| `fs-gg-net` | FS.GG.Net | `0.4.0` | `0.4.0` |
| `coord-engine` | FS-GG/.github | `0.11.0` | `0.11.0` |
| `new-sdd-workspace` | FS-GG/.github | `0.6.0` | `0.6.0` |

**The orchestrator axis.** `fs-gg-ui-template` pins `minimum-fsgg-sdd` at **`0.6.0`** — the oldest published `fsgg-sdd` that seeds the artifacts a workspace on this pin is expected to contain (ADR-0008; see *The coherent set has three axes* below).

<!-- END GENERATED: fsgg-versions -->

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
upgrade`** (self-update + re-pin + re-seed), never a silent auto-update — for *in-project*
invocations. **ADR-0030** carves the one creation-time exception: `new-sdd-workspace`
self-updates the CLI to the newest coherent set before scaffolding **by default**
(`--pinned` restores a reproducible pin), because at creation there is no consumer artifact
to clobber. Truth stays declarative in the registry so it can be diffed, gated, and flipped
after publish (`FR-007`). The registry pins `minimum-fsgg-sdd` (the version is above, with the other
version leads) — the oldest published CLI
that seeds those artifacts, advanced `0.4.0→0.6.0` on 2026-07-04 because feature 073
(the transient-artifact taxonomy) made `fsgg-sdd` seed a `.gitignore` for regenerable output
into scaffolded products, growing the expected seeded surface (FR-011). Both halves are in
lockstep (Templates#99/PR#100, merged 2026-07-05): the provider descriptor mirrors
`minimumFsggSdd 0.6.0`, and the `fsgg-sdd-orchestrator-axis` coherence
row is `coherent: true`. A behind-CLI scaffold is verified to warn
(`scaffold.cliBehindMinimum`) and stamp used+minimum into `scaffold-provenance`
(original axis resolution closed epic #85).

**The skill union is content-verified in every lane (ADR-0014/ADR-0065).** Skills are
content-addressed **directory** data: each producer manifest retains the canonical
`SKILL.md` body digest for compatibility and additionally records every directory member's
relative path, digest, executable mode, and the whole-tree digest. ONE `mirror`/`verify` library
(`Fsgg.SkillMirror`, FS.GG.Contracts ≥ 1.4.0) owns all orchestrated fan-out, and the
standalone spec-kit lane runs a vendored byte-equivalent as its single materialize
step. The invariant — three byte-identical union roots, nothing dangling — is
asserted where skills are produced (`doctor`, per-skill `sha256` in
`scaffold-provenance`) *and* where they are consumed: the Templates composition gate
enforces it hard in both lanes via the reusable `skill-union-assert.sh`
(`skill-mirror-verified` coherence row, `coherent: true` since 2026-07-02).
ADR-0065 applies the same `.claude/.codex/.agents` default to framework coordination-kit receivers:
`FS.GG.Kit` and `coordination-sync` are separate delivery triggers over the same root contract.
That three-root declaration is the **transport/parity** contract, not a command to expose three
duplicate catalog entries in one runtime. Claude and Codex catalog their supported roots; Codex
installations that also expose `.codex/skills` suppress only that duplicate runtime entry and keep
the materialized mirror intact. `agents/openai.yaml` carries host selection policy, including
explicit-only autonomous drivers, without changing which directory bytes are delivered.

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
fails restore in CI in every repo — but **only where that restore is cold**, per
ADR-0032 §5: validated against a *warm* package folder the row is fails-open — it compares
a record against a record and never contacts the feed, #429/epic #266; SDD and the
`lockfile-sync` generator are cold, Game/Rendering/Audio adoption is in flight. Note
`FSharp.Core` was **never re-published** (ADR-0032, #471): the SDK bundles a *different*
`.nupkg` than nuget.org serves at the same id+version, so a lock's `contentHash` depends
on **which source** resolved it. Cold was therefore not the same as hermetic — the SDK's
`library-packs` folder is injected by MSBuild (`RestoreAdditionalProjectSources`) and a
fresh `NUGET_PACKAGES` does not bypass it. **That hole is now closed** (#504): the org-shared
build config sets `DisableImplicitLibraryPacksFolder`, which removes the folder from the
source list, and all five F# repos (SDD, Rendering, Governance, Game, Audio) have synced it
and re-pinned to nuget.org's hash — so a lock now regenerates byte-identically on any
machine, whatever its SDK patch level or `packageSourceMapping`. `lockfile-sync`'s
source report is **fail-closed** accordingly: a `library-packs` resolution is a regression,
not an un-adopted repo), `apicompat-publicapi-gate` (a public-API break
on a packable forces a SemVer major), `fs-gg-ui-version`/`-bom` (single-pin and
BOM coherence guarded on every Rendering PR), and
`governance-cli-handoff-consumer-published` (the full strict/light matrix proven
through the composed workspace). Cross-repo decisions are recorded as ADRs
(ADR-0002 composition-by-scaffold, ADR-0005 `.fsgg` slot ownership + canonical
`name`, ADR-0006 shared-build-config, ADR-0055 reference-gate-set version
(plain SemVer + in-package schema manifest, superseding ADR-0007), ADR-0008 the CLI orchestrator axis, ADR-0009 detect-and-remediate
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
 dist/dotnet/*  ── FS.GG.Kit package ──▶ Directory.Build.props + Directory.Packages.props
                                         materialized in the 4 build-config receivers  (ADR-0062;
                                         sync-build-config.sh now only DERIVES the kit's FILES)
```

The result is a real, windowed F# UI app plus a `.fsgg/` lifecycle skeleton you
can drive from `charter` to `ship`, with governance gates dropped in only if you
opt into them. There is no single all-in-one template, because that could only
exist by bundling a rendering copy that goes stale — the live, version-pinned
install is what keeps the composition honest. See the
[consumer guide](consumer/index.md) for the walkthrough.

---

## 7. Build, release, and CI conventions

- **One org-shared build config, now delivered as a package** (ADR-0062, #1262).
  [`dist/dotnet/`](../dist/dotnet/) holds the canonical `Directory.Build.props` and
  `Directory.Packages.props`. They no longer byte-copy: they ship inside the **FS.GG.Kit**
  package, and each of the 4 build-config receivers **materializes** them from a pinned
  `FS.GG.Kit` (`FsggKitMaterializeBuildConfig`), refreshed by `kit-materialize.yml` on a
  Renovate bump. Each repo imports a `*.local.props` for repo-specific settings. The drift
  gate is now each receiver's `build-config-drift` job asserting its committed `.props` match
  the pinned package (a `dotnet build -t:FsggKitMaterialize` + git-clean check); the byte-copy
  `build-config-propagate` push and the reusable `sync-build-config.sh --check` step are
  **retired**. [`sync-build-config.sh`](../scripts/sync-build-config.sh) survives only as the
  kit's `FILES`/marker **derive source** (ADR-0058). See [`docs/build/README.md`](build/README.md).
  The pinned `.config/dotnet-tools.json` is distributed by the **coordination-kit** (#1077).
- **`dist/dotnet/` also holds `global.json`, and it is deliberately NOT managed**
  (ADR-0006's 2026-07-17 amendment,
  [#903](https://github.com/FS-GG/.github/issues/903)). It is a fourth canonical
  file, but it is not in `sync-build-config.sh`'s `FILES`, so nothing distributes
  it and no drift gate judges it: **per-repo SDK bands are legitimate.** Receivers
  copy it by hand and Renovate bumps each copy independently, so divergence here is
  the steady state rather than a defect — enforcing byte-coherence would merge-freeze
  every receiver still holding the previous canonical bytes. This is a settled
  decision, not a pending rollout step; `tests/sync-build-config` holds the line.
- **The build-config drift gate no longer compares against a `.sha` pin — RETIRED** (ADR-0062,
  #1262; superseding ADR-0036, [#592](https://github.com/FS-GG/.github/issues/592)). ADR-0036 had
  each receiver commit `.config/fsgg-build-config.sha` — the `.github` commit its managed files came
  from — and `--check` diff against **that** commit's `dist/dotnet/`, so a *required* gate's verdict
  depended only on the receiver's own tree rather than on `.github@main` at CI time (which had twice
  red-lit every open PR in every adopter, once for an **XML comment** change). Under package delivery
  that model is gone: the pin is not managed by the package path, so it would freeze while the
  materialize moved the `.props` forward. Each receiver's committed `.props` are now checked against
  its **pinned FS.GG.Kit** (above), and the `.config/fsgg-build-config.sha` pin is deleted from each.
- **Distribution edges must not make the consumer's gate depend on the producer's `main`.**
  ADR-0036 generalises past build config: any org→repo distribution whose *enforcement* runs
  in the consumer's CI has to be judged against something in the consumer's own tree, or the
  producer's every commit becomes a merge freeze downstream.
- **Locked restore everywhere, and it must be COLD.** Every project commits
  `packages.lock.json`; CI restores `--locked-mode` (gated on `GITHUB_ACTIONS`), and
  `NU1603`/`NU1608` are promoted to errors so a silent version *substitution* fails the
  build. A silent **re-publication** — same version, different bytes — is a different
  animal: `NU1603`/`NU1608` never fire, and the only thing that catches it is the lock
  file's `contentHash`, which `--locked-mode` validates against the package **already in
  the package folder**. So the restore that writes (`--force-evaluate`) or enforces
  (`--locked-mode`) a lock file must run against a fresh `NUGET_PACKAGES` with the HTTP
  cache cleared (ADR-0032 §5); a warm folder compares a record to a record and the gate
  becomes a lottery on runner cache state — green on an unrestorable lock file, red on a
  correct one (#429).
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
- **The coordination fabric is a typed, packaged component ([ADR-0034](adr/0034-typed-coordination-engine.md),
  accepted 2026-07-12; cut over and finished under [ADR-0040](adr/0040-port-the-io-layer.md), 2026-07-15).**
  The client every worker and CI job drives — `scripts/fsgg-coord` — **was** ~7,000 lines of bash whose
  state model was jq regexes over prose: claims are HTML comments, touch-sets are a `Paths:` line in an
  issue body, dependency edges are free text (Projects v2 has no typed dependency field). That is a
  concurrent, transactional, budget-constrained domain — and it **was** modelled in a substrate with no
  types, no `Result`, and whose default failure mode is to **fail open**, which is why
  *"is this item startable?"* was **computed in five places and agreed in none**
  ([#485](https://github.com/FS-GG/.github/issues/485); 34 issues), and why
  [epic #266](https://github.com/FS-GG/.github/issues/266) has 51 children.

  ADR-0034 moved the domain into a pure typed F# core (`FS.GG.Coord.Core`) with **one** schedulability
  function and a three-valued `Green | Red | NoVerdict` on every check, where `NoVerdict` is non-zero.
  **That engine is now the sole implementation.** ADR-0040 Phase D.4 deleted the bash monolith and the
  shadow/differential gates that drove it; `scripts/fsgg-coord` is today an 83-line **resolver shim**
  that finds the `fs.gg.coord.cli` tool via `.config/dotnet-tools.json` and `exec`s it, passing argv and
  exit code through unchanged. Every doc, workflow, and skill that references that PATH still works —
  which was the point of keeping it.

  **What gated the flip was the defect corpus, not a clock ([ADR-0038](adr/0038-the-corpus-is-the-cut-over-gate.md)).**
  ADR-0034 staged the cut-over behind a shadow-mode criterion — both engines run, bash stays
  authoritative, nothing flips until divergence is zero on live traffic. **That clock could not tick**,
  for a structural reason: workers run in per-item worktrees, a worktree worker resolved no engine
  ([#728](https://github.com/FS-GG/.github/issues/728)), and a worker who banks no evidence can never be
  one of the "≥2 distinct workers" the criterion required — while any engine republish reset the window,
  so the engine could not be improved while waiting for the clock that waited for the engine. ADR-0038
  replaced it with a **defect corpus**: one case per historical defect, which needs only a checkout,
  survives a rebuild, and covers every path that has *actually broken* rather than whatever floated past
  a live fleet for three days. It lives in
  [`tests/coord-engine-parity/`](https://github.com/FS-GG/.github/tree/main/tests/coord-engine-parity)
  (each defect a hermetic fixture server; `shim.sh` re-runs the whole corpus *through* the shim, which
  is how the swap was proven transparent) and
  [`tests/coord-engine-e2e/`](https://github.com/FS-GG/.github/tree/main/tests/coord-engine-e2e), which
  drives the compiled engine over HTTP against a fixture GitHub — no token, no network. The shadow was
  demoted from gate to telemetry, then removed with the bash it compared against.

  Two consequences landed on this map. **`.github` is a two-tool producer** (above), the engine shipping
  as a `dotnet tool` on the coherent set. And the **`kit:` row is a digest-pinned shim** — the publish
  cycle breaks by asymmetry: **`.github` builds the engine from source and never depends on the feed**,
  so a broken feed cannot prevent the coordination tool from being fixed. That kit-row shape change is a
  contract-change under [ADR-0015](adr/0015-register-the-registry-schema-as-a-governed-contract.md)
  and landed with the *implementation*, not with the decision.

  The larger prize was not the language. `fsgg-coord` was **already the model** — in every drift that
  can be dated, the tool was right and the prose was wrong — it simply was not the *source*. ADR-0034
  §4.5 inverted that, and **it has landed** ([#731](https://github.com/FS-GG/.github/issues/731)): the
  rules live once in `FS.GG.Coord.Core/Protocol.fs`, and the prose that states them is a **build
  artifact** — a `<!-- BEGIN GENERATED: fsgg-protocol -->` region emitted by
  [`scripts/generate-projections`](../scripts/generate-projections) into
  `docs/coordination/parallel-work.md` and the two `intra-repo-parallel-work/SKILL.md` roots, guarded
  by the `projections` gate exactly like `registry/repos.lock`. A protocol rule can no longer land in
  one tier and not the others, because there are no tiers — which is what **54 vendored copies** of
  the protocol used to guarantee it would.

  **There is one tier left, and it is the fleet's own engine
  ([#1075](https://github.com/FS-GG/.github/issues/1075)).** `projections` compares files *in this
  repo*, so it makes the prose and `Protocol.fs` agree **here** and has nothing to say about the
  engine the fleet actually runs — which is a **published package**, restored from the feed. So a
  protocol rule lands in `Protocol.fs`, regenerates into every `SKILL.md`, and the fleet keeps
  `exec`ing the release that predates it: main's own documents then describe a verb contract the
  fleet's engine does not implement. That is not hypothetical — it is
  [#846](https://github.com/FS-GG/.github/issues/846) verbatim (*"next/take/done/add are all unknown
  command"*), and it has happened **four times** ([#844](https://github.com/FS-GG/.github/issues/844),
  #846, [#964](https://github.com/FS-GG/.github/issues/964),
  [#1067](https://github.com/FS-GG/.github/issues/1067)), each found by whoever happened to notice.

  **No version comparison can see it, which is why it needed a gate of its own rather than a row in
  §5.** Every other producer's source version moves with the *change*, so `version` drifting ahead of
  `package-version` is their legible "merged, not yet published" signal. This engine's `<Version>`
  moves only at **release** time — `release-coord-engine.yml` evaluates the fsproj property and
  requires the `coord-engine/v<version>` tag to match it, so the bump **is** the release act. While
  the source outruns the feed the two scalars are **equal**, and the registry row is green *because*
  the defect is happening. [`scripts/check-engine-freshness.py`](../scripts/check-engine-freshness.py)
  therefore counts **commits**, not scalars: it resolves the tag that produced the feed's newest
  version (never the newest *tag* — a tag is not a publish) and reds when the **wire surface**,
  `Protocol.fs` itself, has drifted unreleased. Drift behind the wire is reported and not red — at
  this repo's velocity a `drift > 0` bar would be red by design between releases, and a gate that
  cries wolf on the happy path teaches that FAILED is noise.

- **Public distribution (dual-publish, [ADR-0012](adr/0012-dual-publish-to-nuget-org.md) +
  [ADR-0013](adr/0013-trusted-publishing-oidc-for-nuget-org.md)).** On release each producer
  additionally pushes the **byte-identical** `.nupkg` to **public nuget.org** (after the
  org-feed push), so public consumers `dotnet tool install` / `dotnet add package` with no
  `--add-source`. Auth is **Trusted Publishing (OIDC)** — a short-lived `NuGet/login` key per
  run, no stored secret. **nuget.org is the read path; the org feed is the publish path**
  ([ADR-0039](adr/0039-nuget-org-is-the-read-path.md), accepted 2026-07-15, amending ADR-0012 §1):
  Renovate resolves every `FS.GG.*` lookup from `api.nuget.org` ([`default.json`](../default.json)),
  because the org feed **requires a credential even to read** and a 401 on a Renovate datasource is
  not an error — it is an empty version list, which is why the bot froze the `FS.GG.SDD.Cli` pin four
  times and opened no PR ([#576](https://github.com/FS-GG/.github/issues/576)). The org feed remains
  the **publish** target and the `-preview` channel, and the `feed-coherence` gate still reads it —
  that gate asserts `package-version` against the feed a producer just pushed to, so the feed is the
  right subject for it. Package IDs on nuget.org are permanent, freezing the
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
