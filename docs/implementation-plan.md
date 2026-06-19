---
title: FS.GG implementation plans
category: FS.GG
categoryindex: 6
index: 9
description: Coordination page for the separate FS.GG rendering, SDD, and governance implementation plans.
---

# FS.GG implementation plans

The split is implemented through separate plans. The rendering plan starts from
a fresh standard Spec Kit repository. The SDD lifecycle plan lives in its own
fresh standard Spec Kit repository. The governance rule-engine plan lives in its
own repository. This repository is source inventory, provenance, and org-level
coordination; it is not the base that is gradually transformed into any
destination.

## Ordering

1. Implement the [rendering repository plan](rendering-implementation-plan.md).
2. Put this repository into bridge/archive mode once rendering is usable.
3. Keep [governance](governance-project.md) focused on rule/evidence/route
   tooling.
4. Implement the [SDD lifecycle project](sdd-project.md) separately from the
   governance rule engine.
5. Add lightweight cross-repo contracts only after the repositories exist.
6. Decide any rebrand as a separate package/template/docs release decision.

Rendering comes first because it is the product. Governance tooling should not
block rendering's build, test, docs, package, template, or release path.

## Core correction

The plan is not "migrate FS.Skia.UI into a new operating system." The plan is:

- create fresh standard Spec Kit repositories;
- import selected code, docs, tests, templates, and reports as source material;
- leave old workflow and governance experiments behind unless deliberately
  copied;
- keep the rendering repository independent from governance tooling;
- keep the SDD lifecycle repository independent from governance enforcement;
- keep the governance repository free to experiment without affecting rendering
  or SDD product work.

## Shared constraints

- Use standard Spec Kit as the baseline workflow in each destination repository.
- Do not introduce mandatory `ProjectGraph`, `ProductGraph`, or `FeatureGraph`
  authority as part of the split.
- Do not make generated `spec.md`, `plan.md`, or `tasks.md` the initial workflow
  authority.
- Do not require governance tooling to run rendering build, test, docs, package,
  template, SDD lifecycle, or release checks.
- Do not rebrand package IDs as part of the split unless that decision is made
  explicitly.
- Do not split templates into a separate repository until release cadence or
  ownership requires it.
- Start the first rendering version light. Each imported test, generated
  fixture, validation gate, or governance mechanism must justify its product
  value, owner, run frequency, and maintenance cost before it moves over.

## Cross-repo contracts

Cross-repo contracts should stay small until there is pressure to formalize
them:

- package APIs and version ranges;
- command-line contracts for optional validators;
- JSON report formats only where needed;
- support-bundle formats if governance tooling creates them;
- docs links, release notes, and migration pages;
- optional design-token or theme report formats if governance tooling later
  checks design drift from outside the rendering repository.

Avoid shared mutable build state, generated workflow projections, and shared
custom graph schemas across repositories until governance tooling has matured.

## Completion

The split direction is complete when:

- the rendering repository exists, uses standard Spec Kit, and can build, test,
  document, package, validate templates, and release without governance tooling;
- the SDD repository exists, uses standard Spec Kit with the F# constitution, and
  owns lifecycle artifacts without implementing the governance rule engine;
- the first rendering validation set is deliberately small and every imported
  check has a written justification;
- the rendering repository documents the boundary between semantic controls,
  design-system primitives, concrete themes, and design-specific kits;
- this repository is documented as bridge/archive and no longer receives normal
  product features;
- the governance repository exists independently and can evolve as optional rule
  and gate tooling;
- any rebrand is handled by an explicit package/template/docs migration plan.
