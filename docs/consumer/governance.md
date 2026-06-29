---
title: Adopting governance
category: FS.GG
categoryindex: 6
index: 14
description: How an FS-GG consumer turns on governance — the reference gate set, the light/strict/release profiles, the four .fsgg files, and the escape hatch.
---

# Adopting governance

Governance is **optional**. You build, test, document, package, and release
without it; you turn it on when you want rules checked and gates enforced.
[FS.GG.Governance](https://github.com/FS-GG/FS.GG.Governance) is a pure inference
kernel that checks your artifacts — it never becomes a build dependency.

The authoritative adoption doc is
[FS.GG.SDD · adopting governance](https://github.com/FS-GG/FS.GG.SDD/blob/main/docs/adopting-governance.md);
the design rationale is
[FS.GG.Governance · design](https://github.com/FS-GG/FS.GG.Governance/blob/main/docs/governance-design/index.md).
This page is the consumer-level orientation.

## Turn it on

Drop the populated reference gate set into an existing SDD-managed project:

```sh
dotnet new install FS.GG.Templates
dotnet new fs-gg-governance -o ./MyApp --appName MyApp --defaultProfile light
```

This writes the four `.fsgg/*.yml` governance files (below) alongside your
existing SDD-owned files. The kernel CLI, if you want to run checks directly, is
a global tool:

```sh
dotnet tool install --global FS.GG.Governance.Cli \
  --add-source https://nuget.pkg.github.com/FS-GG/index.json   # exposes `fsgg-governance`
```

## Pick a posture (profiles)

`--defaultProfile` sets how hard the gates push:

| Profile | Posture |
|---|---|
| `light` | Non-blocking inner-loop default — rules **report**, nothing blocks. |
| `strict` | The block-on-ship gates actually block. |
| `release` | Release posture — the strictest gate set. |

Start at `light`. It gives you explanations and findings without ever stopping
your inner loop; promote to `strict`/`release` when you want the merge boundary to
enforce.

## What governance guarantees by construction

Four properties hold structurally — by design, not by configuration you have to
remember to set:

1. **Light by default** — an unclassified or low-stakes change incurs *no*
   machinery. Heavy checks require a positive match against a small, named,
   high-stakes surface (a published API, a release, an irreversible contract).
   Notes, drafts, and experiments live in a zero-gate zone.
2. **Advisory by default** — a rule *reports* unless explicitly marked blocking.
   *Who decides* (the `CheckTier`: machine / agent / human) is separate from
   *whether failure stops you* (the `Severity`). The full blocking set is
   listable at a glance.
3. **Explainable by construction** — every conclusion carries provenance and
   every check renders to a sentence. "No reason given" is unrepresentable; the
   reason *is* the rule id plus the rendered check.
4. **Honest escape hatch** — a real off switch for the inner loop that is
   **loud**, **local-only**, and **cannot be the basis of a merge**. The merge
   boundary recomputes from scratch against the base branch and ignores any local
   mode. You develop freely without the machinery, but you cannot *land* an
   un-governed state.

## The four `.fsgg` files

Governance configuration is four versioned YAML files, parsed strictly (unknown
fields, duplicate ids, schema-version range, path escapes, and dangling
references are all located diagnostics):

| File | Declares |
|---|---|
| `governance.yml` | project id, declared domains, governed root, package surfaces, optional refs |
| `policy.yml` *(optional)* | enforcement profiles + default, branch policy, review budget |
| `capabilities.yml` | the routing path-map (`glob → domain`), governed surfaces, and the reified checks |
| `tooling.yml` *(optional)* | external command specs, environment classes, tool-version requirements |

These coexist in one `.fsgg/` directory with the SDD-owned files with no
shadowing. You edit them to describe *your* surfaces and rules — the reference
gate set is a populated starting point, not a fixed shape.

## The handoff from the lifecycle

When governance is present, `fsgg-sdd ship` points ship-ready work at the
governance-owned protected-boundary handoff (the versioned, optional
`governance-handoff` contract). Without governance, `ship` simply reports
merge-boundary readiness and stops there. Either way the lifecycle stays the same
— see [The development lifecycle](lifecycle.md).

## When to drop it

The whole point of the one-directional rule is that you can leave. If governance
becomes heavy, brittle, or distracting, remove the `.fsgg` governance files (and
the tool) and keep building on standard Spec Kit and normal build/test/release.
Nothing in rendering or the lifecycle depends on it.
