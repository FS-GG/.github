---
title: Transition and boundaries
category: FS.GG
categoryindex: 6
index: 7
description: Transition guidance for moving from the current repository to split rendering, SDD, and governance projects.
---

# Transition and boundaries

The split should reduce pressure on the rendering project, not create a larger
multi-repository process. The transition should therefore be staged around
working software, clear ownership, and small contracts.

## Repository boundaries

Start with three focused projects:

| Repository | Owns | Does not own |
|---|---|---|
| Rendering | Runtime code, controls, docs, tests, templates, packages, release notes. | Experimental governance platform internals. |
| SDD | Lifecycle artifacts, normalized work model, generated views, and agent guidance. | Rule-engine enforcement or rendering product identity. |
| Governance | Optional rule, evidence, route, profile, and gate tooling. | SDD lifecycle ownership or rendering release authority. |

Add a third template repository only if template release cadence, ownership, or
distribution requirements become materially different from the rendering
runtime.

## Existing repository role

This repository should become source inventory, provenance, and archive
material. It should not be transformed into either destination repository. The
rendering, SDD, and governance repositories should be created first as fresh
standard Spec Kit repositories, then selected material can be imported from
here.

Its job is to help answer:

- which runtime source paths should be copied into the rendering project;
- which tests and docs are still current;
- which package and template identities are retained, renamed, or deprecated;
- which lifecycle workflow ideas belong in SDD;
- which rule/evidence experiments should be copied into Governance;
- which tests, generated fixtures, and checks justify their cost in the initial
  rendering repository;
- which historical specs and reports remain archive-only.

## Package and namespace identity

Do not rename package identities casually. NuGet package IDs are product
identity. If the rendering project rebrands, use new package IDs and deprecate
old packages toward explicit alternates after replacements exist.

For the first split, prefer minimizing simultaneous identity churn unless the
rebrand decision is already firm. A practical order is:

1. split product and governance ownership;
2. get the rendering project build/test/package path stable;
3. decide package/template/docs rebrand as a separate release decision;
4. deprecate old package IDs only after replacements are published and tested.

## Docs and templates

Docs and templates should move with the rendering project by default because
they describe and instantiate the product. SDD docs should move only when they
describe lifecycle tooling. Governance docs should move only when they describe
rule/evidence tooling as a separate product.

Template behavior should be treated as a rendering product contract, but it
does not need a custom product graph to start. Standard checks are enough:

- pack the template;
- install it locally;
- instantiate representative profiles;
- restore/build the generated product;
- verify package pins and docs links.

## Cross-repo contracts

Keep cross-repo contracts small:

- versioned package APIs;
- command-line interfaces;
- JSON report formats only when needed;
- documented SDD readiness formats if lifecycle tooling produces them;
- documented support-bundle formats if governance tooling produces them;
- release notes and migration guides.

Avoid shared mutable state, generated workflow projections, or custom graph
schemas across repositories until each owning project has matured.

## Bridge policy

The old repository should eventually carry:

- a bridge README or report;
- source commit and migration notes;
- package/template deprecation guidance if names change;
- links to the rendering, SDD, and governance repositories;
- archived historical reports.

It should not keep receiving normal product features after the rendering project
is active, except for bridge maintenance or emergency migration fixes.
