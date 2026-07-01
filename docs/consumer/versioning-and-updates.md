---
title: Versions, feeds & updates
category: FS.GG
categoryindex: 6
index: 17
description: How an FS-GG consumer installs from the right feeds, pins versions, and stays current across the four products.
---

# Versions, feeds & updates

FS-GG ships as standard .NET artifacts — global tools, NuGet packages, and
`dotnet new` template packages — so the normal install/update paths apply. This
page maps which artifact comes from where and how to stay current. Authoritative
version and feed details live in each product's installation doc.

## What ships, and how you get it

| Product | Artifact | Install |
|---|---|---|
| **SDD** | `FS.GG.SDD.Cli` global tool (`fsgg-sdd`) | `dotnet tool install --global FS.GG.SDD.Cli` |
| **Rendering** | `FS.GG.UI.*` packages + `fs-gg-ui` template | reference the projects, `dotnet pack` to a local feed, or scaffold from the template |
| **Governance** | `FS.GG.Governance.Cli` global tool (`fsgg-governance`) + reference gate set | `dotnet tool install --global FS.GG.Governance.Cli` |
| **Templates** | `FS.GG.Templates` template package (`fs-gg-governance` overlay, `rendering` provider) | `dotnet new install FS.GG.Templates` |

All FS-GG packages are published to **public nuget.org**, so the installs above need no
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

Pin versions in a product you ship. Preview lines can move APIs and schemas
before a stable line settles, so:

- pin the `fsgg-sdd` tool version in your `dotnet-tools.json` manifest;
- pin `FS.GG.UI.*` package versions;
- read each product's **versioning policy** and **compatibility matrix** before a
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
seeds artifacts a product on a given template pin is expected to contain (the
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
bump a tool across a schema boundary, follow the product's migration notes
(SDD: [`docs/release/migrations`](https://github.com/FS-GG/FS.GG.SDD/tree/main/docs/release/migrations)).
