---
title: Governance project
category: FS.GG
categoryindex: 6
index: 5
description: Scope and adoption bar for a separate governance and rule-tooling project.
---

# Governance project

The governance project should be treated as a tool product, not as the hidden
operating system of the rendering repository. It may explore rule evaluation,
evidence models, route explanation, and optional validators, but it must earn
adoption by staying small and useful.

## Scope

The governance project may own:

- deterministic fact and rule evaluation;
- explanation and diagnostics primitives;
- optional evidence freshness helpers;
- route-analysis helpers;
- package, template, docs, and release drift analyzers;
- support-bundle tooling;
- optional validators or report generators consumed by Spec Kit or SDD;
- examples that validate external repositories without requiring them to adopt a
  custom platform.

It should not own rendering product identity, package IDs, docs URLs, template
profiles, design-system choices, controls, themes, or release decisions.

## First useful product

The first useful governance product should be smaller than the previous
SpecFlow graph operating system proposal. A reasonable first target is a compact
rule/evidence helper library with:

- nominal IDs and diagnostics;
- deterministic fact storage;
- fixed-point rule evaluation;
- provenance for derived facts;
- JSON-friendly explanation output;
- simple evidence freshness predicates.

That kernel should have no dependency on FAKE, git, filesystem scanning, Skia,
NuGet publishing, template profiles, or rendering project paths.

## Adoption bar

The governance project should not be considered a platform until at least two
real projects can adopt it cheaply.

An adoption should count only if the consuming project can:

- define its own fact domain;
- run useful validations or explanations;
- avoid copying rendering repository layout or target names;
- avoid adopting rendering package and template vocabulary;
- keep its standard Spec Kit workflow.

If adoption requires the consumer to become shaped like the rendering project,
the tool is not generic.

## Relationship to standard Spec Kit and SDD

The governance project should prefer additive integration with standard Spec Kit
and FS.GG.SDD. Useful forms include:

- read-only status or trace commands;
- report generators;
- validators that inspect existing Spec Kit artifacts;
- optional hooks;
- migration helpers;
- focused extensions that can be removed without breaking the project.

It should not start by replacing Spec Kit's authored artifacts with a custom
graph authority, and it should not own the SDD lifecycle model. SDD may emit
structured artifacts that Governance inspects later.

## Development stance

The governance project should be allowed to move fast and delete ideas. That is
only safe if rendering does not depend on it. Keep the dependency direction:

```text
governance tooling may inspect rendering
rendering must not require governance tooling
```

Once a governance feature is stable, small, and valuable, the rendering project
can choose to adopt it as a normal dependency or CI helper.
