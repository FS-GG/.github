---
title: The FS-GG development lifecycle
category: FS.GG
categoryindex: 6
index: 13
description: What each FS.GG.SDD lifecycle stage from charter to ship reads, writes, and reports — for consumers driving a product.
---

# The development lifecycle

[FS.GG.SDD](https://github.com/FS-GG/FS.GG.SDD) drives a unit of work through a
fixed, ordered lifecycle. Every stage reads and writes **structured artifacts**
(Markdown is the authoring surface; schema-versioned files are the machine
contract) and emits a **deterministic report**. Humans, agents, CLI automation,
and optional governance all read the same contract.

This page is the consumer-level tour. The command-by-command walkthrough with no
governance installed is the
[SDD quickstart](https://github.com/FS-GG/FS.GG.SDD/blob/main/docs/quickstart.md);
the schemas are the
[schema reference](https://github.com/FS-GG/FS.GG.SDD/blob/main/docs/release/schema-reference.md).

## The ordering

```text
charter → specify → clarify → checklist → plan → tasks → analyze → evidence → verify → ship
```

The ordering is fixed; cross-cutting commands (below) never reorder it.

| Stage | What it does |
|---|---|
| `charter` | Frame the product's intent and boundaries. |
| `specify` | Write the specification for a feature / work item. |
| `clarify` | Surface and resolve underspecified points before planning. |
| `checklist` | Generate a requirements checklist for the work. |
| `plan` | Design the implementation approach. |
| `tasks` | Break the plan into dependency-ordered tasks. |
| `analyze` | Non-destructive cross-artifact consistency check over spec/plan/tasks. |
| `evidence` | Record declared implementation, verification, synthetic, and deferral evidence. |
| `verify` | Evaluate verification readiness over the task/evidence/test obligations into `readiness/<id>/verify.json`. |
| `ship` | Aggregate merge-boundary readiness into `readiness/<id>/ship.json` and point ship-ready work at the (optional) governance handoff. |

## Where the artifacts live

A scaffolded project carries:

- **`.fsgg/`** — the configuration model (`project.yml`, `sdd.yml`, `agents.yml`,
  and, if you adopt it, the governance files).
- **`work/`** — the per-item authoring surface (specs, plans, tasks).
- **`readiness/<id>/`** — the generated machine contract: `work-model.json`,
  `verify.json`, `ship.json`, and friends.

You author in Markdown; the CLI keeps the structured artifacts in sync and never
treats a generated view as a second source of truth.

## Cross-cutting commands

These are **not** lifecycle stages and never alter the `charter → ship` ordering:

- **`fsgg-sdd scaffold`** — create a runnable, SDD-managed product from a template
  provider (see [Getting started](getting-started.md)).
- **`fsgg-sdd agents`** — generate per-target Claude/Codex command + skill
  guidance from `readiness/<id>/work-model.json`, marked generated.
- **`fsgg-sdd refresh`** — bring a work item's generated views back to currency.
- **`fsgg-sdd validate`** — exhaustively exercise SDD's command × projection ×
  state matrices (determinism, degradation, release baseline-conformance,
  governance-handoff compatibility) into one `validation-report`.

## Every command speaks three projections

Each command projects the **same** report three ways, selected by flag with
precedence `--rich` > `--text` > `--json` > default:

- **default / `--json`** — the deterministic JSON automation contract.
- **`--text`** — a portable plain-text summary.
- **`--rich`** — a human-oriented Spectre.Console rendering that degrades to
  plain text with zero ANSI when output is non-interactive or color is disabled
  (`NO_COLOR`, `TERM=dumb`).

The JSON is the contract; plain and rich are projections of it. Build your
scripts and CI on `--json`; use `--rich` at a terminal. See
[Output, automation & CI](automation.md).

## Governance is optional at every stage

SDD builds, installs, and runs the full lifecycle through `ship` with **no
governance present**. Rule evaluation, evidence freshness, routing, profiles, and
gate enforcement all belong to
[FS.GG.Governance](https://github.com/FS-GG/FS.GG.Governance), which SDD
integrates with only through explicit, versioned, optional contracts. `ship`
points ship-ready work at the governance-owned protected-boundary handoff *if and
only if* you've adopted governance — otherwise it simply reports readiness. To
turn it on, see [Adopting governance](governance.md).
