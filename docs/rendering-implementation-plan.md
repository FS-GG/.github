---
title: Rendering implementation plan
category: FS.GG
categoryindex: 6
index: 9
description: Implementation plan for the rendering repository, starting from a fresh standard Spec Kit repository.
---

# Rendering implementation plan

The rendering repository is implemented first. It starts as a fresh standard
Spec Kit repository, then imports selected FS.Skia.UI runtime material. The goal
is a product repository that can build, test, document, package, validate
templates, and release without depending on governance tooling. The first
version should be deliberately light: import the tests and checks that protect
current product behavior, and leave behind mechanisms whose cost is not yet
justified.

## Objectives

- Create a fresh rendering repository using standard Spec Kit.
- Import only selected runtime product slices from this repository.
- Keep the product workflow understandable without custom governance machinery.
- Keep controls, design-system primitives, themes, and design-specific kits as
  rendering-owned layers.
- Require every imported test, generated fixture, validation gate, or governance
  mechanism to justify its product value and maintenance cost.
- Keep templates with rendering unless their cadence later justifies a separate
  repository.
- Defer package rebrand unless explicitly approved as a release decision.

## Non-goals

- Do not transform this repository in place.
- Do not start by importing old `.specify` customizations or `speckit-*`
  workflow assumptions.
- Do not introduce a mandatory custom feature graph.
- Do not require the governance repository for build, test, docs, template, or
  package validation.
- Do not preserve every historical feature, readiness log, or generated
  artifact as active state.
- Do not import the old test and governance surface wholesale.
- Do not keep a check only because it once caught something or because it makes
  the repository look rigorous.

## Stage R1 - Create the fresh repository

Create the rendering repository before importing product code.

Deliverables:

- empty or minimal repository with README, license, ignore files, and standard
  Spec Kit setup;
- initial solution/project layout;
- basic package metadata policy;
- minimal build/test/docs commands;
- initial docs page that states the repository owns the rendering product;
- explicit statement that the initial validation set is intentionally small.

Exit criteria:

- a fresh checkout has a clear local setup path;
- standard Spec Kit is the feature workflow baseline;
- no custom governance platform is required.

## Stage R2 - Define product shape

Define the product boundary before copying code.

Deliverables:

- package/module map for scene, color, layout, input, viewer, Elmish, controls,
  controls Elmish integration, testing, and template support;
- decision on whether package IDs stay `FS.Skia.UI.*` initially or move later;
- design/control layering document copied or adapted from
  [Design and controls](design-and-controls.md);
- template ownership decision;
- list of product docs to import.

Exit criteria:

- maintainers can explain what rendering owns;
- controls, design-system primitives, themes, and design-specific kits have
  distinct boundaries;
- rebrand is either explicitly deferred or explicitly planned.

## Stage R3 - Define the initial validation set

Decide which tests and checks are worth importing before copying the full test
surface.

Each candidate test or check needs a justification record:

| Field | Purpose |
|---|---|
| Product contract | What user-visible or package/template behavior this protects. |
| Failure mode | The concrete regression it is expected to catch. |
| Owner | Who maintains it when it fails or becomes stale. |
| Frequency | Local inner loop, CI, release only, or manual/advisory. |
| Cost | Runtime, setup complexity, flake risk, fixture size, and maintenance burden. |
| Decision | Import now, defer, archive, or rewrite smaller. |

Default decisions:

- import focused unit tests for current runtime behavior;
- import public API and package checks only when they protect current package
  consumers;
- import template checks that simulate real generated products;
- defer broad historical readiness reports;
- archive generated fixtures that no longer represent a current product
  contract;
- rewrite oppressive checks into smaller tests before importing them.

Exit criteria:

- the initial validation set is small enough for routine product work;
- every imported check has a justification record;
- deferred checks are not lost, but they are not active obligations;
- release-only checks are clearly separated from local development checks.

## Stage R4 - Import selected source

Copy selected product source into the fresh repository.

Candidate imports:

- runtime libraries under `src/**`;
- runtime tests selected by the validation-set justification;
- controls docs and examples;
- design-token and theme sources that belong to the product;
- template files and generated-product smoke tests selected by the
  validation-set justification;
- selected architecture docs and ADRs that remain current.

Rules:

- copy code as product source, not as old workflow state;
- remove or rewrite references to retired governance assumptions;
- keep provenance notes that identify source commit and copied paths;
- keep test/check justification notes with the imported validation surface;
- leave historical readiness logs and old feature workflow artifacts in this
  repository unless a specific migration note needs them.

Exit criteria:

- product code compiles in the fresh repository;
- tests run from the fresh repository;
- imported docs describe current product behavior;
- no old custom governance runtime is needed.

## Stage R5 - Stabilize product validation

Add only product checks that pay for themselves. If a check is valuable but
oppressive, rewrite it smaller before making it part of the default workflow.

Checks to consider:

- unit and integration tests;
- API surface drift checks;
- design-token and theme smoke checks;
- control behavior and accessibility checks;
- package skew checks;
- docs build checks;
- template pack/install/instantiate checks;
- generated-product restore/build checks;
- release package checks.

Exit criteria:

- routine product changes have a documented validation path;
- default local validation is fast enough that contributors will actually run
  it;
- template validation simulates a real generated consumer;
- release validation is explicit and separate from local development checks;
- every active validation mechanism has a current justification and owner;
- none of the checks require the governance repository.

## Stage R6 - Bridge the old repository

After rendering is usable, document the handoff.

Deliverables:

- bridge README or report in this repository;
- source commit and import-path provenance;
- package/template migration notes if identities changed;
- archive note for old specs, reports, and readiness artifacts.

Exit criteria:

- new product work is opened in the rendering repository;
- this repository receives only bridge, archive, provenance, or emergency
  migration fixes;
- governance experiments are not mixed into rendering stabilization work.

## Stage R7 - Decide rebrand separately

Once the rendering repository is stable, decide whether package and template
identity should remain `FS.Skia.UI` or move to a new identity such as
`FS.GG.UI`.

If rebranding:

- choose root namespace and package prefix;
- choose template package ID and short name;
- choose docs URL and bridge policy;
- publish replacement packages before deprecating old packages;
- update template identity as one coherent matrix.

Exit criteria:

- package, namespace, template, docs, and repository names agree;
- migration docs explain old-to-new identity mapping;
- old package IDs are deprecated only after replacement packages exist.

## Acceptance criteria

The rendering plan is complete when:

- the rendering repository starts from standard Spec Kit;
- selected runtime code, docs, tests, templates, and package metadata are
  imported deliberately;
- controls, design-system primitives, themes, and design-specific kits are
  documented and separated;
- fresh checkout restore/build/test/docs/package/template validation works;
- imported tests and governance checks are justified individually rather than
  moved wholesale;
- ordinary rendering work does not depend on governance tooling;
- this repository is bridge/archive for rendering product work.
