---
title: FS-GG consumer guide
category: FS.GG
categoryindex: 6
index: 10
description: Cross-product guide for people building products with FS-GG — install, scaffold, run, drive the lifecycle, and optionally govern.
---

# FS-GG consumer guide

This guide is for **people building products with FS-GG** — not for people
developing FS-GG itself. It collects the processes that span more than one
product (scaffolding, the lifecycle, governance adoption, automation, versions)
into one place, and links down into each repository's authoritative docs for the
details.

If you are contributing to one of the FS-GG repos themselves, read that repo's
`DEVELOPING.md` / `CONTRIBUTING` instead, and the
[cross-repo decision record](../index.md).

## What FS-GG is, for a consumer

FS-GG is F# tooling for building **desktop UI products** on `net10.0`, made of
four products you compose as needed:

- **[FS.GG.Rendering](https://github.com/FS-GG/FS.GG.Rendering)** — render an
  Elmish/MVU app with SkiaSharp over OpenGL.
- **[FS.GG.SDD](https://github.com/FS-GG/FS.GG.SDD)** — the `fsgg-sdd` CLI:
  scaffold a product and drive it through a structured `charter → ship` lifecycle.
- **[FS.GG.Governance](https://github.com/FS-GG/FS.GG.Governance)** *(optional)* —
  check your artifacts against rules you control, advisory by default.
- **[FS.GG.Templates](https://github.com/FS-GG/FS.GG.Templates)** — the
  composition that wires the three together at scaffold time.

You only adopt what you need. The hard rule is one-directional: governance may
*inspect* your work, but rendering and the lifecycle never *require* governance
to build, test, or ship. Your inner loop is never blocked by a platform.

> **New here? Build something first.** The
> **[TestSpec tutorial](../TestSpecTutorial.md)** is the recommended first
> hands-on path: install, scaffold, then build a real game (Pong) from a ready-made
> [TestSpec](../TestSpecs/) by driving it through the whole `charter → ship`
> lifecycle. Come back to the map below when you want the bigger picture.

## Read in order

1. **[Getting started](getting-started.md)** — install the CLI, scaffold a
   runnable app, build it, run it, and make one pass through the lifecycle.
2. **[Which products do I need?](which-products.md)** — a decision guide for the
   four common goals (just a UI, a managed lifecycle, the full stack, adding
   governance).
3. **[The development lifecycle](lifecycle.md)** — what each stage from `charter`
   to `ship` reads, writes, and reports.
4. **[Adopting governance](governance.md)** — enabling gates, the
   light/strict/release profiles, the four `.fsgg` files, and the escape hatch.
5. **[Output, automation & CI](automation.md)** — the JSON automation contract,
   the `--json` / `--text` / `--rich` projections, and wiring commands into CI.
6. **[Versions, feeds & updates](versioning-and-updates.md)** — installing from
   the right feeds, pinning, and staying current.
7. **[FAQ & troubleshooting](faq.md)** — common questions and failure modes.

## Per-product authoritative docs

The consumer guide is the **map**; these are the **sources of truth** owned by
each product:

| Topic | Doc |
|---|---|
| Consuming the renderer / packages | [FS.GG.Rendering · usage](https://github.com/FS-GG/FS.GG.Rendering/blob/main/docs/usage.md) |
| `init` → `ship`, no governance | [FS.GG.SDD · quickstart](https://github.com/FS-GG/FS.GG.SDD/blob/main/docs/quickstart.md) |
| Installing `fsgg-sdd`, versions, feeds | [FS.GG.SDD · installation](https://github.com/FS-GG/FS.GG.SDD/blob/main/docs/release/installation.md) |
| Schema / compatibility | [FS.GG.SDD · schema](https://github.com/FS-GG/FS.GG.SDD/blob/main/docs/release/schema-reference.md) · [compatibility](https://github.com/FS-GG/FS.GG.SDD/blob/main/docs/release/compatibility-matrix.md) |
| Turning on governance | [FS.GG.SDD · adopting governance](https://github.com/FS-GG/FS.GG.SDD/blob/main/docs/adopting-governance.md) |
| The full-stack composition | [FS.GG.Templates · README](https://github.com/FS-GG/FS.GG.Templates#create-a-full-stack-product-composition-primary-path) |
| The governance design / rules | [FS.GG.Governance · design](https://github.com/FS-GG/FS.GG.Governance/blob/main/docs/governance-design/index.md) |
