---
title: Versions, feeds & updates
category: FS.GG
categoryindex: 6
index: 17
description: How an FS-GG consumer installs from the right feeds, pins versions, and stays current across the FS-GG components.
---

# Versions, feeds & updates

FS-GG ships as standard .NET artifacts — global tools, NuGet packages, and
`dotnet new` template packages — so the normal install/update paths apply. This
page maps which artifact comes from where and how to stay current. Authoritative
version and feed details live in each component's installation doc.

## What ships, and how you get it

The component inventory and its live versions are generated from the org registry,
so this table never drifts from what actually ships:

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
| [**FS.GG.SDD**](https://github.com/FS-GG/FS.GG.SDD) | Lifecycle CLI to scaffold a workspace and drive it from charter to ship; ships the typed cross-repo contracts | `5.0.1` |
| [**FS.GG.Rendering**](https://github.com/FS-GG/FS.GG.Rendering) | The UI framework — MVU over SkiaSharp/OpenGL with layout, input, controls and themes, plus the fs-gg-ui template | `0.19.0` |
| [**FS.GG.Governance**](https://github.com/FS-GG/FS.GG.Governance) | Optional tooling that checks your artifacts against rules you control — advisory by default | `1.4.0` |
| [**FS.GG.Templates**](https://github.com/FS-GG/FS.GG.Templates) | Wires SDD, Rendering and Governance into one ready-to-run workspace at scaffold time | — |
| [**FS.GG.Game**](https://github.com/FS-GG/FS.GG.Game) | Game-simulation libraries — a render-independent simulation core with a companion renderer, usable as plain F# libraries | `0.9.0` |
| [**FS.GG.Audio**](https://github.com/FS-GG/FS.GG.Audio) | Audio-engine libraries — synthesis, playback and mixing (buses, fades, ducking, 3D), with an optional Elmish adapter | `0.4.0` |
| [**FS.GG.Net**](https://github.com/FS-GG/FS.GG.Net) | Networking/transport libraries — protobuf messaging over WebSocket or gRPC, render-independent, with an optional Elmish adapter | `0.2.0` |

<!-- END GENERATED: fsgg-component-inventory -->

Acquire any of them with a standard .NET command — the **verb** follows the artifact
kind, and the **package IDs** are stable identity (spell them out; never pin a version
in prose — let `dotnet` resolve the current one from nuget.org):

| Artifact kind | Acquire command |
|---|---|
| **Library** (a `PackageReference`) | `dotnet add package <id>` — e.g. `FS.GG.UI`, `FS.GG.Game.Core`, `FS.GG.Audio.Core`, `FS.GG.Net.Core` |
| **Global tool** (a CLI) | `dotnet tool install --global <id>` — e.g. `FS.GG.SDD.Cli` (`fsgg-sdd`), `FS.GG.Governance.Cli` (`fsgg-governance`) |
| **`dotnet new` template pack** | `dotnet new install <id>` — e.g. `FS.GG.Templates` |

Rendering's `FS.GG.UI.*` are a multi-package set: reference the `FS.GG.UI` entry
package (or scaffold from the `fs-gg-ui` template) and pin the framework via
`FsGgUiVersion`.

Game, Audio, and Net are **render-independent building blocks** — each depends on
no other FS-GG component, so you add just the packages a goal needs (start from the
entry package in each row above and add siblings as required). All FS-GG packages
are published to **public nuget.org**, so the installs above need no
`--add-source`. Rendering's `FS.GG.UI.*` are **preview** packages on `net10.0` — nuget.org
serves prereleases, and your `FsGgUiVersion` pins the exact preview version. The org GitHub
Packages feed (`https://nuget.pkg.github.com/FS-GG/index.json`) remains the coherence/`-preview`
source of truth (Renovate reads it), but public consumption no longer requires it.

## Updating each artifact

```sh
# Global tools (fsgg-sdd, fsgg-governance):
dotnet tool update --global FS.GG.SDD.Cli

# Template packages (FS.GG.Templates):
dotnet new update                 # upgrade installed templates to the latest
dotnet new update --check-only    # preview what would update
```

NuGet package references update the usual way (edit the pinned version, or via
your dependency tooling).

## Pin, don't float

Pin versions in a workspace you ship. Preview lines can move APIs and schemas
before a stable line settles, so:

- pin the `fsgg-sdd` tool version in your `dotnet-tools.json` manifest;
- pin `FS.GG.UI.*` package versions;
- read each component's **versioning policy** and **compatibility matrix** before a
  bump — for the lifecycle CLI, that's
  [versioning policy](https://github.com/FS-GG/FS.GG.SDD/blob/main/docs/release/versioning-policy.md)
  and the
  [compatibility matrix](https://github.com/FS-GG/FS.GG.SDD/blob/main/docs/release/compatibility-matrix.md).

## The one pin that matters in the full-stack path

In the [Templates](https://github.com/FS-GG/FS.GG.Templates) composition, the
only thing that can go stale is the single `FS.GG.UI.Template` version pin in the
`rendering` provider descriptor — because composition installs the live upstream
template rather than vendoring a copy. Templates keeps that pin fresh
automatically (a Renovate manager and an upstream-release auto-PR move it), so you
mostly just consume newer template versions as they land. If you maintain your own
provider descriptor, that single pin is the thing to bump.

## The CLI keeps you coherent — but never behind your back

A scaffolded project has three moving pins, not two: the **template**, the
**framework** (`FS.GG.UI.*`), and the **`fsgg-sdd` CLI itself** — because the CLI
seeds artifacts a workspace on a given template pin is expected to contain (the
`fs-gg-sdd-*` process skills, `.fsgg/early-stage-guidance.md`). An old CLI on a new
pin silently omits them, so the CLI is treated as a first-class member of the
*coherent set* and **orchestrates** its own currency (ADR-0008 / ADR-0009):

- **It detects, read-only, on every command.** If your installed CLI is behind the
  pin's required minimum, an interactive run **warns** (and points at `upgrade`); a
  CI / non-interactive run **fails closed** (non-zero). Detection never writes.
- **It remediates only when you ask.** `fsgg-sdd doctor` reports coherence read-only;
  `fsgg-sdd upgrade` reconciles — self-update + template re-pin + agent re-seed
  (`refresh-agents`) — **each shown as a confirmable diff**. Nothing self-updates as
  a side effect, and it only rewrites state you own (your `.fsgg/providers.yml`), not
  the governed pin (that stays a PR in its owning repo).

So "stay current" is one explicit command (`fsgg-sdd upgrade`), and CI won't let a
behind-CLI scaffold pass unnoticed. Pin the tool in `dotnet-tools.json` as above; the
fail-closed check *protects* that pin rather than fighting it.

> These orchestration verbs roll out with the SDD orchestrator work; run
> `fsgg-sdd --help` to see what your installed version exposes.

## Schema versions

The lifecycle and governance config files carry a `schemaVersion`. The CLIs parse
strictly and tell you when a file's schema is out of the supported range. When you
bump a tool across a schema boundary, follow the component's migration notes
(SDD: [`docs/release/migrations`](https://github.com/FS-GG/FS.GG.SDD/tree/main/docs/release/migrations)).
