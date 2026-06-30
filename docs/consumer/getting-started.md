---
title: Getting started with FS-GG
category: FS.GG
categoryindex: 6
index: 11
description: Install the FS-GG lifecycle CLI, scaffold a runnable Skia/Elmish app, build and run it, and make one pass through the lifecycle.
---

# Getting started

This takes you from nothing to a running F# UI app under a managed lifecycle, in
one sitting. By the end you will have a windowed Skia/Elmish product, the
`.fsgg/` lifecycle skeleton, and one feature driven from charter toward ship.

## Prerequisites

- **.NET SDK with `net10.0`** — the FS-GG products target `net10.0`.
- **A GL/X11 session** to see a live window. The renderer's offscreen and
  deterministic test paths run headless; only the live windowed viewer needs a
  GL/X11 display (e.g. `DISPLAY=:1` on Linux).
- **Git** — the lifecycle and (optional) governance read repository state.

## 1. Install the lifecycle CLI

`fsgg-sdd` is a .NET global tool:

```sh
dotnet tool install --global FS.GG.SDD.Cli   # exposes the `fsgg-sdd` command
fsgg-sdd --version
```

For specific versions and feeds, see
[FS.GG.SDD · installation](https://github.com/FS-GG/FS.GG.SDD/blob/main/docs/release/installation.md)
and [Versions, feeds & updates](versioning-and-updates.md).

## 2. Scaffold a runnable product

The default way to start is `fsgg-sdd scaffold`. It establishes the SDD
lifecycle skeleton **and** invokes a template provider to materialize a real,
runnable app — in one command. The reference `rendering` provider materializes
the live, version-pinned [FS.GG.Rendering](https://github.com/FS-GG/FS.GG.Rendering)
`fs-gg-ui` app.

First register the provider. SDD embeds no provider — the reference `rendering`
descriptor ships in [FS.GG.Templates](https://github.com/FS-GG/FS.GG.Templates),
so `scaffold` can't resolve `--provider rendering` until that descriptor is in
your project's `.fsgg/providers.yml`. Fetch the canonical (version-pinned) copy:

```sh
mkdir -p ./MyApp/.fsgg
curl -fsSL https://raw.githubusercontent.com/FS-GG/FS.GG.Templates/main/providers/rendering.providers.yml \
  -o ./MyApp/.fsgg/providers.yml
```

Then scaffold:

```sh
fsgg-sdd scaffold --root ./MyApp --provider rendering --param productName=MyApp
```

What you get:

- a runnable **Skia/OpenGL Elmish/MVU** app (Scene, SkiaViewer, Controls);
- the **`.fsgg/` lifecycle skeleton** (`project.yml`, `sdd.yml`, `agents.yml`,
  `work/`, `readiness/`);
- a `.fsgg/scaffold-provenance.json` recording the externally owned files the
  provider wrote.

Useful flags: `--dry-run` plans without executing; `--no-update` skips refreshing
the template; `--force` materializes into a non-empty directory. The exit code is
meaningful — malformed input (unknown provider, missing parameter, target
collision) exits `1`; a provider defect exits `2`; an incomplete scaffold is
never reported as complete.

> **Provider registration, in depth.** The fetch above grabs the descriptor from
> `main`; to pin it, copy from a checkout of
> [FS.GG.Templates](https://github.com/FS-GG/FS.GG.Templates) at a tag instead, or
> merge the entry into an existing `.fsgg/providers.yml` (see
> [Which products do I need?](which-products.md#the-full-stack-path)). For the
> skeleton only — no runtime app, no provider needed — use `fsgg-sdd init`.

## 3. Build and run it

```sh
cd ./MyApp
dotnet build
dotnet run            # opens the live window (needs a GL/X11 session)
```

You now have a real product. Everything from here is the lifecycle — optional but
recommended, and never required to keep building and running.

## 4. Make one pass through the lifecycle

Drive a unit of work through the stages. Each command reads and writes structured
artifacts under `work/` and `readiness/` and prints a deterministic report:

```sh
fsgg-sdd charter           # frame the product's intent
fsgg-sdd specify           # write the spec for a feature
fsgg-sdd clarify           # resolve underspecified points
fsgg-sdd plan              # design approach
fsgg-sdd tasks             # break down into tasks
fsgg-sdd analyze           # cross-artifact consistency check
fsgg-sdd evidence          # record implementation / verification evidence
fsgg-sdd verify            # evaluate verification readiness
fsgg-sdd ship              # aggregate merge-boundary readiness
```

The full ordering is
`charter → specify → clarify → checklist → plan → tasks → analyze → evidence →
verify → ship`. For a command-by-command walkthrough with no governance
installed, follow the
[SDD quickstart](https://github.com/FS-GG/FS.GG.SDD/blob/main/docs/quickstart.md);
for what each stage means in consumer terms, see
[The development lifecycle](lifecycle.md).

## 5. (Optional) add governance

When you want gates, drop the populated FS-GG reference gate set into the project
and pick a posture:

```sh
dotnet new install FS.GG.Templates
dotnet new fs-gg-governance -o ./MyApp --appName MyApp --defaultProfile light
```

`light` is the non-blocking inner-loop posture; `strict` / `release` make the
block-on-ship gates actually block. This never changes how you build and run —
see [Adopting governance](governance.md).

## Where to go next

- [Which products do I need?](which-products.md) — if the `rendering`-provider
  path above isn't the shape you want.
- [The development lifecycle](lifecycle.md) — a deeper tour of `charter → ship`.
- [Output, automation & CI](automation.md) — feed these commands' JSON into CI.
- [FAQ & troubleshooting](faq.md) — if a step above didn't behave.
