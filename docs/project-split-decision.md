---
title: Project split decision
category: FS.GG
categoryindex: 6
index: 2
description: Decision record for splitting rendering, SDD lifecycle, and governance into separate projects that use standard Spec Kit.
---

# Project split decision

Split the rendering framework, SDD lifecycle tooling, and governance tooling
into separate projects. The rendering project should use standard Spec Kit plus
narrow deterministic checks. The SDD project should own lifecycle artifacts and
agent guidance. The governance project should own optional rule/evidence tooling
and should not be required for rendering or SDD contributors to make ordinary
progress.

## Context

The previous proposal designed a repo-native SpecFlow graph operating system
with `ProjectGraph`, `ProductGraph`, `FeatureGraph`, an evidence ledger,
generated projections, route planning, product contracts, consumer graphs,
platform policy, and release provenance.

That design solved real drift problems, but it also made the platform
substantially more monolithic. It asked maintainers to develop a changing UI
framework on top of a changing governance framework. The result is a recursive
maintenance cost:

- product changes can become governance-schema changes;
- governance changes can block rendering work;
- contributors need to understand a custom operating model before touching
  runtime code;
- the governance layer can grow from helpful guardrails into an oppressive
  source of ceremony.

The runtime project is still discovering its long-term shape. During that phase,
custom workflow infrastructure should be a dependency only if it is clearly
more valuable than its cognitive and operational cost.

## Decision

Use a split-repository direction:

| Project | Purpose | Workflow baseline |
|---|---|---|
| Rendering/runtime | Scene, layout, input, viewer, controls, design systems, themes, templates, docs, tests, packages. | Standard Spec Kit plus narrow repo-owned checks. |
| SDD/lifecycle | Charter, specify, clarify, checklist, plan, tasks, normalized work model, generated views, agent commands/skills. | Standard Spec Kit with the F# constitution. |
| Governance/tooling | Rule kernels, evidence helpers, route analyzers, optional validators, profile/gate/report tooling. | Standard Spec Kit, developed as a normal tool product. |
| Templates/package support | Optional later split if release cadence differs from runtime. | Standard Spec Kit or rendering repo workflow, decided later. |

The rendering and SDD repositories should not depend on the governance
repository for ordinary build, test, package, docs, or release work. Governance
tooling may observe them from the outside, validate them as customers, or
provide optional helpers, but it must not become either project's foundation
until it has proved itself.

Implementation starts from fresh standard Spec Kit repositories. This repository
is used as source inventory and provenance, not as the base that is transformed
into either destination.

## Pros

- Lower cognitive load for runtime contributors.
- Smaller blast radius when governance or lifecycle ideas change.
- Independent release cadence for runtime, lifecycle, and rule tooling.
- Cleaner boundaries between product behavior and process experiments.
- Easier deletion or replacement of governance experiments.
- Better adoption test for governance tooling, because it must work as an
  external tool rather than absorbing local exceptions.
- Standard Spec Kit remains available as a familiar baseline in all projects.

## Cons

- Cross-repo coordination becomes explicit work.
- Traceability across rendering, templates, docs, and governance is less
  automatic.
- Version, package, docs, and release policy can drift without lightweight
  contracts.
- Standard Spec Kit will not enforce every evidence and projection rule that the
  monolithic design attempted to encode.
- Some checks may be duplicated until the governance project offers stable,
  low-cost helpers.

## Consequences

The previous SpecFlow graph operating system is no longer the active foundation
for the rendering project. Durable ideas can still move forward, but only as
separate, earned tooling:

- deterministic route explanations;
- evidence freshness helpers;
- package and template drift checks;
- release provenance checks;
- support-bundle helpers;
- optional validators or report generators.

The rendering repository keeps only checks that are simple, local, and clearly
worth their cost.

Design-system work stays with rendering. The default control strategy is one
semantic control set with multiple themes, not separate AntD/Fluent/Material
control copies. Design-specific kits are allowed when a design language adds
composition or workflow behavior that cannot be represented as styling over the
shared controls.

## Alternatives considered

### Keep the monolithic graph operating system

This maximizes internal consistency, but it makes the project hard to work on
while both the runtime and governance system are changing.

### Create a clean new FS.GG.UI repository with the graph OS from day one

This reduces historical clutter, but it still makes the new product depend on
unproven governance infrastructure.

### Rename the current repository in place

This preserves history and repository continuity, but it does not solve package,
template, docs URL, or governance complexity.

### Split projects and use standard Spec Kit

This is the selected direction because it keeps product work pragmatic while
leaving room for governance tooling to mature independently.
