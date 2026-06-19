---
title: FS.GG project split
category: FS.GG
categoryindex: 6
index: 1
description: Index for the FS.GG split-repository direction and the documents that replace the previous monolithic SpecFlow plan.
---

# FS.GG project split

The current recommendation is to stop treating the UI runtime, lifecycle
workflow, and governance system as one self-hosting platform. The rendering
framework should be developed as a normal product repository using standard
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

- [Project split decision](project-split-decision.md) records why the monolithic
  graph operating system is being replaced by a split-repository strategy.
- [Rendering project](rendering-project.md) defines the runtime repository's
  scope, governance level, and release expectations.
- [Design and controls](design-and-controls.md) defines where design-system
  primitives, themes, controls, and design-specific kits live.
- [Governance project](governance-project.md) defines the separate tooling
  experiment and its adoption bar.
- [SDD project](sdd-project.md) defines the separate spec-driven development
  lifecycle product and its relationship to Governance.
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
must not depend on that tooling to do ordinary product work. If governance
tooling becomes heavy, brittle, or distracting, the product repositories should
continue on standard Spec Kit and normal build/test/release practices.
