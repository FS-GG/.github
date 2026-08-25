---
title: FS-GG consumer guide
category: FS.GG
categoryindex: 6
index: 10
description: Cross-component guide for people building apps with FS-GG — install, scaffold, run, drive the lifecycle, and optionally govern.
---

# FS-GG consumer guide

This guide is for **people building apps with FS-GG** — not for people
developing FS-GG itself. It collects the processes that span more than one
component (scaffolding, the lifecycle, governance adoption, automation, versions)
into one place, and links down into each repository's authoritative docs for the
details.

If you are contributing to one of the FS-GG repos themselves, read that repo's
`DEVELOPING.md` / `CONTRIBUTING` instead, and the
[cross-repo decision record](../index.md).

## What FS-GG is, for a consumer

FS-GG is F# tooling for building **desktop UI apps** on `net10.0` — a family of
components you compose as needed. The pieces most consumers start with:

- **[FS.GG.Rendering](https://github.com/FS-GG/FS.GG.Rendering)** — render an
  Elmish/MVU app with SkiaSharp over OpenGL.
- **[FS.GG.SDD](https://github.com/FS-GG/FS.GG.SDD)** — the `fsgg-sdd` CLI:
  scaffold a workspace and drive it through a structured `charter → ship` lifecycle.
- **[FS.GG.Governance](https://github.com/FS-GG/FS.GG.Governance)** *(optional)* —
  check your artifacts against rules you control, advisory by default.
- **[FS.GG.Templates](https://github.com/FS-GG/FS.GG.Templates)** — the
  composition that wires the others together at scaffold time.

Underneath sit three **render-independent building blocks**, each usable on its
own with a plain `dotnet add package`:

- **[FS.GG.Game](https://github.com/FS-GG/FS.GG.Game)** — a BCL-only
  game-simulation core (`FS.GG.Game.Core`) plus a thin Scene adapter.
- **[FS.GG.Audio](https://github.com/FS-GG/FS.GG.Audio)** — game audio: an effect
  vocabulary, a device seam, a mixing engine, and an Elmish bridge.
- **[FS.GG.Net](https://github.com/FS-GG/FS.GG.Net)** — a domain-neutral transport
  (protobuf over WebSocket / gRPC) with an Elmish bridge.

For the authoritative, always-current inventory of every component and what it
ships, see [`docs/architecture.md`](../architecture.md) — this guide links the
live source rather than restating a count that drifts.

You only adopt what you need. The hard rule is one-directional: governance may
*inspect* your work, but rendering and the lifecycle never *require* governance
to build, test, or ship. Your inner loop is never blocked by governance.

> **New here? Build something first.** The
> **[TestSpec tutorial](https://github.com/FS-GG/FS.GG.Game/blob/main/docs/TestSpecTutorial.md)** is the recommended first
> hands-on path: install, scaffold, then build a real game (Pong) from a ready-made
> [TestSpec](../TestSpecs/) by driving it through the whole `charter → ship`
> lifecycle. Come back to the map below when you want the bigger picture.

## Read in order

1. **[Getting started](getting-started.md)** — install the CLI, scaffold a
   runnable app, build it, run it, and make one pass through the lifecycle.
2. **[Which components do I need?](which-products.md)** — a decision guide by goal
   (just a UI, a managed lifecycle, the full stack, adding governance, or pulling
   in a render-independent building block: game-sim, audio, networking).
3. **[The development lifecycle](lifecycle.md)** — what each stage from `charter`
   to `ship` reads, writes, and reports.
4. **[Agent setup instructions](agent-setup.md)** — the Codex/Claude Code runbook
   for GitHub authentication, safe token handoff, workspace and board creation,
   security checks, and verification.
5. **[Who drives the lifecycle](who-drives-the-lifecycle.md)** — who runs the
   commands (human, agent, CI), why they're real CLI commands and not Spec Kit
   slash-commands, and how Codex or Claude Code drives them via seeded skills.
6. **[Adopting governance](governance.md)** — enabling gates, the
   light/strict/release profiles, the four `.fsgg` files, and the escape hatch.
7. **[Output, automation & CI](automation.md)** — the JSON automation contract,
   the `--json` / `--text` / `--rich` projections, and wiring commands into CI.
8. **[Versions, feeds & updates](versioning-and-updates.md)** — installing from
   the right feeds, pinning, and staying current.
9. **[FAQ & troubleshooting](faq.md)** — common questions and failure modes.

## Per-component authoritative docs

The consumer guide is the **map**; these are the **sources of truth** owned by
each component:

| Topic | Doc |
|---|---|
| Consuming the renderer / packages | [FS.GG.Rendering · usage](https://github.com/FS-GG/FS.GG.Rendering/blob/main/docs/usage.md) |
| `init` → `ship`, no governance | [FS.GG.SDD · quickstart](https://github.com/FS-GG/FS.GG.SDD/blob/main/docs/quickstart.md) |
| Installing `fsgg-sdd`, versions, feeds | [FS.GG.SDD · installation](https://github.com/FS-GG/FS.GG.SDD/blob/main/docs/release/installation.md) |
| Schema / compatibility | [FS.GG.SDD · schema](https://github.com/FS-GG/FS.GG.SDD/blob/main/docs/release/schema-reference.md) · [compatibility](https://github.com/FS-GG/FS.GG.SDD/blob/main/docs/release/compatibility-matrix.md) |
| Turning on governance | [FS.GG.SDD · adopting governance](https://github.com/FS-GG/FS.GG.SDD/blob/main/docs/adopting-governance.md) |
| The full-stack composition | [FS.GG.Templates · README](https://github.com/FS-GG/FS.GG.Templates#create-a-full-stack-workspace-composition-primary-path) |
| The governance design / rules | [FS.GG.Governance · design](https://github.com/FS-GG/FS.GG.Governance/blob/main/docs/governance-design/index.md) |
