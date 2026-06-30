# FS-GG

**F# tooling for building desktop UI products** — a SkiaSharp/OpenGL UI
framework, a spec-driven development lifecycle CLI, and optional governance,
composed into one runnable product on `net10.0`.

You describe a Model-View-Update (MVU) app; FS-GG renders it, scaffolds it,
drives it through a structured lifecycle from charter to ship, and — only if you
opt in — checks it against rules you control. Each piece ships on its own and is
usable on its own; you adopt only what you need.

> New here? Start with the **[Consumer guide](https://github.com/FS-GG/.github/blob/main/docs/consumer/index.md)** —
> install, scaffold, build, run, and ship your first product in one sitting.

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

## What you can build

- **A desktop UI app** — Elmish/MVU windows rendered with
  [SkiaSharp](https://github.com/mono/SkiaSharp) over OpenGL: a scene of
  primitives, or a tree of semantic controls (Button, TextBox, DataGrid…) with
  theming, layout, and input routing. The render core is Elmish-free; idiomatic
  Elmish is an optional adapter.
- **A lifecycle-managed product** — every feature moves through
  `charter → specify → clarify → checklist → plan → tasks → analyze → evidence →
  verify → ship`, where each step reads and writes structured artifacts and emits
  a deterministic report you (and your agents and CI) can consume.
- **A governed product** *(optional)* — rules that declare **who decides**
  (machine, agent, or human) and **whether failure stops you**, advisory by
  default, with an honest local escape hatch and an explanation for every verdict.

## Pick your path

| You want to… | Use | Start at |
|---|---|---|
| Just render an F# UI | **FS.GG.Rendering** packages / `fs-gg-ui` template | [Rendering usage](https://github.com/FS-GG/FS.GG.Rendering/blob/main/docs/usage.md) |
| Run a managed dev lifecycle | **FS.GG.SDD** (`fsgg-sdd`) | [SDD quickstart](https://github.com/FS-GG/FS.GG.SDD/blob/main/docs/quickstart.md) |
| Scaffold a full-stack product | **FS.GG.Templates** (`rendering` provider) | [Templates](https://github.com/FS-GG/FS.GG.Templates#create-a-full-stack-product-composition-primary-path) |
| Add rules / gates to a project | **FS.GG.Governance** overlay | [Adopting governance](https://github.com/FS-GG/FS.GG.SDD/blob/main/docs/adopting-governance.md) |

Not sure? See **[Which products do I need?](https://github.com/FS-GG/.github/blob/main/docs/consumer/which-products.md)**

## The products

| Product | What it gives you | Ships |
|---|---|---|
| [**FS.GG.Rendering**](https://github.com/FS-GG/FS.GG.Rendering) | The UI framework: Scene, layout, input, viewer/host, controls, themes — Elmish/MVU over SkiaSharp/OpenGL. | `FS.GG.UI.*` packages + the `fs-gg-ui` `dotnet new` template |
| [**FS.GG.SDD**](https://github.com/FS-GG/FS.GG.SDD) | The lifecycle CLI: scaffold a product, then drive `charter → ship` with structured artifacts and JSON/text/rich reports. | `FS.GG.SDD.Cli` (`fsgg-sdd`) |
| [**FS.GG.Governance**](https://github.com/FS-GG/FS.GG.Governance) | Optional rule / evidence / route tooling — a pure inference kernel that checks your artifacts, advisory by default. | `FS.GG.Governance.Cli` (`fsgg-governance`) + the reference gate set |
| [**FS.GG.Templates**](https://github.com/FS-GG/FS.GG.Templates) | The composition: wires SDD + Rendering + Governance into one ready-to-run product at scaffold time. | the `rendering` scaffold provider + `fs-gg-governance` overlay |

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

> **📐 Want the full picture?** Read the
> **[Architecture guide](https://github.com/FS-GG/.github/blob/main/docs/architecture.md)** —
> the four-product split, the one-way dependency rule, the contract registry, the
> shared F# house style, and how the repositories compose into one runnable
> product, with links to every source.

## The one rule that keeps it honest

> Governance may **inspect** your rendering or lifecycle artifacts; rendering and
> the lifecycle never **require** governance to build, test, document, package, or
> release.

The dependency direction is one-way and your inner loop is never blocked by
governance. You can clone a product repo, read its [Spec Kit](https://github.com/github/spec-kit)
artifacts, run the documented commands, and ship — without learning a custom
platform. If governance ever feels heavy, you drop it and keep building.

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

## Status

Active preview. Rendering ships `FS.GG.UI.*` preview packages and the `fs-gg-ui`
template; SDD and Governance are active and installable. APIs and package
versions may still move before a stable line — pin versions and read each
product's installation and versioning docs. FS-GG is the split of the archived
[`FS-Skia-UI`](https://github.com/EHotwagner/FS-Skia-UI) monolith into focused,
independently shippable products.

## License

MIT.
