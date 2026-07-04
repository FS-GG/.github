---
title: Which FS-GG components do I need?
category: FS.GG
categoryindex: 6
index: 12
description: A decision guide for the four common FS-GG consumer goals — render a UI, run a lifecycle, scaffold the full stack, or add governance.
---

# Which components do I need?

You never adopt the whole platform at once. Pick by your goal; each path adds
exactly one more component than the last.

## Quick decision

| Your goal | What you adopt | Don't need |
|---|---|---|
| Render an F# UI inside an app you already have | **Rendering** only | SDD, Governance, Templates |
| Start a new workspace with a managed dev lifecycle | **SDD** (+ Rendering via the provider) | Governance |
| Stand up a runnable full-stack workspace in one go | **Templates** (drives SDD + Rendering) | — |
| Add rules / gates to an existing SDD workspace | **Governance** overlay | new scaffolding |

## The rendering-only path

You just want the UI framework — you have your own app, build, and release.

- Reference the `FS.GG.UI.*` packages (or the `fs-gg-ui` template). They're
  preview packages on `net10.0`, published on **public nuget.org** — add a
  `<PackageReference>` (restore needs no extra feed) or scaffold from the template.
- No `fsgg-sdd`, no `.fsgg/`, no governance.

→ [FS.GG.Rendering · usage](https://github.com/FS-GG/FS.GG.Rendering/blob/main/docs/usage.md#getting-the-packages)
covers all three ways to get the packages and the package map.

## The lifecycle path

You want a structured `charter → ship` lifecycle around a new workspace, and the
reference UI app as the starting point.

```sh
dotnet tool install --global FS.GG.SDD.Cli
fsgg-sdd scaffold --root ./MyApp --provider rendering --param productName=MyApp
fsgg-sdd charter
```

`scaffold` gives you the runnable app **and** the lifecycle skeleton; `init`
gives you the skeleton only (bring your own template). Governance stays off.

→ [Getting started](getting-started.md) · [SDD quickstart](https://github.com/FS-GG/FS.GG.SDD/blob/main/docs/quickstart.md)

## The full-stack path

You want SDD + a runnable Rendering app + Governance config wired together. This
is the [FS.GG.Templates](https://github.com/FS-GG/FS.GG.Templates) composition.
Composition happens at scaffold time — there is no single all-in-one `dotnet new`
template, because that could only exist by bundling a rendering copy that goes
stale.

```sh
# 1. Register the rendering provider in your project.
mkdir -p ./MyApp/.fsgg
cp providers/rendering.providers.yml ./MyApp/.fsgg/providers.yml   # from FS.GG.Templates

# 2. Scaffold the SDD skeleton + the live FS.GG.Rendering app.
fsgg-sdd scaffold --root ./MyApp --provider rendering --param productName=MyApp

# 3. Activate governance with the populated reference gate set.
dotnet new install ./templates/fs-gg-governance
dotnet new fs-gg-governance -o ./MyApp --appName MyApp --defaultProfile light

cd ./MyApp && dotnet build && dotnet run
```

The [`new-sdd-workspace <target> <product>`](https://github.com/FS-GG/.github/tree/main/scripts/NewSddWorkspace)
dotnet tool (in FS-GG/.github) wraps these three steps — no FS.GG.Templates checkout required.

→ [FS.GG.Templates · full-stack](https://github.com/FS-GG/FS.GG.Templates#create-a-full-stack-workspace-composition-primary-path)

## The add-governance path

You already have an SDD-managed project and want to turn on gates.

```sh
dotnet new install FS.GG.Templates
dotnet new fs-gg-governance -o ./MyApp --appName MyApp --defaultProfile light
```

This drops the four `.fsgg/*.yml` governance files (the reference gate set) into
the project. Governance only inspects — it never becomes a build dependency.

→ [Adopting governance](governance.md) · [FS.GG.SDD · adopting governance](https://github.com/FS-GG/FS.GG.SDD/blob/main/docs/adopting-governance.md)

## How the dependencies actually run

```text
Templates ──compose (scaffold-time)──▶ SDD · Rendering · Governance
SDD ──── governance-handoff (optional) ────▶ Governance
Rendering ── depends on no FS-GG component — never on Governance
```

Rendering is always at the bottom and never reaches up. That's what lets you stop
at any path above without the one below it falling apart.
