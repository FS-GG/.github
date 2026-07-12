---
title: FS.GG project split
category: FS.GG
categoryindex: 6
index: 1
description: Index for the FS.GG split-repository direction and the documents that replace the previous monolithic SpecFlow plan.
---

# FS.GG project split

> **Building an app with FS-GG rather than developing FS-GG itself?** This
> page and the documents below are the cross-repo *decision record* for people
> developing the platform. If you just want to use FS-GG, start at the
> **[consumer guide](consumer/index.md)** instead.

> **Two kinds of document live here.** The **[Living reference](#living-reference)**
> below (architecture map, ADRs, registry projection, coordination protocol, build
> config, reports) is kept **current** and is the instruction of record. The
> **[Documents](#documents)** further down are the **2026-06 planning corpus** — the
> historical proposal for the split, written in future tense about work that has
> since shipped. Read them as a record of intent, not as current instruction; each
> now carries a status banner pointing at what actually shipped.

## Living reference

Kept current — the system as built, and the machinery that keeps it coherent.

- [Architecture](architecture.md) — the newcomer's map of the whole system: the
  four-component split, the one-way dependency rule, the contract registry, the shared
  F# house style, and how the repositories compose. **Start here.**
- [Architecture Decision Records](adr/README.md) — cross-repo decisions (ADR-0001…),
  each with status and supersession history.
- [Registry compatibility projection](registry/compatibility.md) — the human-readable
  projection of `registry/dependencies.yml`: every cross-repo contract, its coherence
  flag, and the evidence behind it.
- [Cross-repo coordination](coordination/README.md) — the coordination protocol, the
  [contract-coherence gate](coordination/contract-coherence-gate.md), the
  [skill-union assertion](coordination/skill-union-assertion.md), and the
  [auto-update fabric](coordination/auto-update-fabric.md).
- [Build config](build/) — the org-shared .NET build configuration (ADR-0006) and how
  consumer repos restore it.
- [Reports](reports/) — dated review/analysis artifacts, including the
  [2026-07-02 code-quality & architecture review](reports/2026-07-02-code-quality-architecture-review.md),
  the [2026-06-30 project-management topologies analysis](2026-06-30-project-management-topologies-adr-registry-projects-v2-analysis.md),
  the [2026-07-12 up-front design practices report](reports/2026-07-12-up-front-design-practices-and-the-proposal-gap.md)
  (what feeds the ADR pipeline, and the stale `Blocked by` premise in ADR-0034),
  and the [2026-07-12 issue-throughput & recurring-error-loops audit](reports/2026-07-12-issue-throughput-and-recurring-error-loops.md)
  (which of the day's 127 closures were real progress, and the five loops that regenerate).

The current recommendation is to stop treating the UI runtime, lifecycle
workflow, and governance system as one self-hosting platform. The rendering
framework should be developed as a normal component repository using standard
Spec Kit and narrow repo-owned checks. Governance rule-engine tooling and SDD
lifecycle tooling should live in separate projects where they can evolve without
blocking rendering work or each other.

## Current direction

The earlier SpecFlow graph operating system proposal was internally consistent,
but it pushed too much authority into one changing platform. It made rendering,
template, release, product-contract, evidence, and governance workflow changes
all part of the same system. That creates a dogfooding loop: the framework is
developed on top of governance machinery that is itself still being designed.

The new direction is deliberately simpler:

- keep the rendering framework buildable, testable, releasable, and
  understandable without an experimental governance platform;
- use standard Spec Kit for feature workflow in each repository;
- keep only narrow deterministic checks in the rendering repository where they
  pay for themselves;
- keep governance rule/evidence/route tooling in its own repository and make it
  earn adoption from the outside;
- keep SDD lifecycle tooling in its own repository so project workflow can
  evolve without becoming the governance rule engine.

## Documents

> **Historical planning corpus (2026-06).** The documents in this section proposed the
> split and are preserved as a record of intent; the work they describe in future tense
> has since shipped. For the system as built and the machinery that keeps it coherent,
> see the [Living reference](#living-reference) above.

- [Architecture](architecture.md) is the newcomer's map of the whole system — the
  four-component split, the one-way dependency rule, the contract registry, the
  shared F# house style, and how the repositories compose — with links to every
  source. **Start here for the big picture.**
- [Project split decision](project-split-decision.md) records why the monolithic
  graph operating system is being replaced by a split-repository strategy.
- [Rendering project](rendering-project.md) defines the runtime repository's
  scope, governance level, and release expectations.
- [Design and controls](design-and-controls.md) defines where design-system
  primitives, themes, controls, and design-specific kits live.
- [Governance project](governance-project.md) defines the separate tooling
  experiment and its adoption bar.
- [SDD project](sdd-project.md) defines the separate spec-driven development
  lifecycle component and its relationship to Governance.
- [Transition and boundaries](transition-and-boundaries.md) explains how the old
  repository, package identities, docs, templates, and cross-repo contracts
  should be handled.
- [Research notes](research-notes.md) preserves the durable research findings
  from the earlier report without keeping the old all-in-one plan as the active
  recommendation.
- [Implementation plans](implementation-plan.md) coordinates the separate
  rendering, SDD, and governance plans.
- [Rendering implementation plan](rendering-implementation-plan.md) starts from
  a fresh standard Spec Kit repository and imports selected product slices.
- [Governance implementation plan](governance-implementation-plan.md) starts
  from its own fresh standard Spec Kit repository after rendering is usable.

## Operating rule

The rendering and SDD projects may be customers of governance tooling, but they
must not depend on that tooling to do ordinary component work. If governance
tooling becomes heavy, brittle, or distracting, the component repositories should
continue on standard Spec Kit and normal build/test/release practices.
