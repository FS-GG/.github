---
title: Governance implementation plan
category: FS.GG
categoryindex: 6
index: 11
description: Implementation plan for the separate governance tooling repository, after rendering is established.
---

# Governance implementation plan

The governance repository is implemented after the rendering repository is
usable. It starts as its own fresh standard Spec Kit repository and develops
optional tooling. It must not become a prerequisite for rendering product work.

## Objectives

- Create a fresh governance repository using standard Spec Kit.
- Build small optional tools that can inspect external repositories.
- Avoid rendering-specific package, template, target, and directory assumptions
  in generic code.
- Treat rendering as one customer, not as the tool's internal layout.
- Keep ideas deletable until they prove useful in more than one context.
- Treat every governance mechanism as a cost that must justify itself through
  concrete user value, not as infrastructure that moves over by default.

## Non-goals

- Do not recreate the monolithic SpecFlow graph operating system.
- Do not replace standard Spec Kit authored artifacts as the first move.
- Do not own rendering product identity, package IDs, design systems, controls,
  templates, docs URLs, or release decisions.
- Do not require the rendering repository to adopt governance tooling.
- Do not recreate old governance ceremony simply because it existed.

## Stage G1 - Create the fresh repository

Create the governance repository independently.

Deliverables:

- empty or minimal repository with README, license, ignore files, and standard
  Spec Kit setup;
- initial solution/project layout;
- command-line or library skeleton;
- tests for the first tool slice;
- docs explaining that the project provides optional tooling.

Exit criteria:

- fresh checkout builds and tests independently;
- standard Spec Kit is the workflow baseline;
- there is no dependency on rendering source layout.

## Stage G2 - Choose the first narrow tool

Pick one small problem before building any platform.

Good candidates:

- route explanation over an external repository snapshot;
- evidence freshness helper;
- package/docs/template drift report;
- support-bundle format checker;
- Spec Kit artifact linter.

Bad first cuts:

- mandatory project/product/feature graph authority;
- generated `spec.md`, `plan.md`, or `tasks.md` replacement;
- release platform policy for rendering packages;
- design-system ownership.

Exit criteria:

- the first tool has one clear user and one clear output;
- failure diagnostics are useful without adopting a larger platform;
- the tool can run against fixtures or an external repository checkout.

## Stage G3 - Implement as optional tooling

Build the first tool as a normal library or CLI.

Rules:

- generic code must not mention rendering package IDs, template names, target
  names, or directory layout;
- rendering-specific examples live in adapters or fixtures;
- outputs are reports, diagnostics, or exit codes;
- no shared mutable build state with consumer repositories;
- no generated workflow projections as a first implementation step.

Exit criteria:

- the tool runs against at least one fixture not shaped like rendering;
- rendering can use the tool without changing its standard Spec Kit workflow;
- removing the tool does not break rendering build/test/release.

## Stage G4 - Test against rendering from outside

Use the rendering repository as an external customer.

Deliverables:

- documented command to run the tool against a rendering checkout;
- example report or diagnostic;
- clear statement of whether the result is advisory or blocking;
- list of rendering assumptions that are local adapter code, not generic
  governance code.

Exit criteria:

- rendering can ignore the tool and still operate;
- useful findings can be converted into ordinary rendering issues or Spec Kit
  tasks;
- generic tooling remains free of rendering vocabulary.

## Stage G5 - Decide adoption level

Adopt governance tooling only when it has earned it.

Adoption levels:

| Level | Meaning |
|---|---|
| Advisory | Tool reports are useful but never block rendering work. |
| Optional CI | Rendering can run the tool in CI without making it a release prerequisite. |
| Product check | Rendering maintainers decide the tool protects a real product contract. |
| Shared contract | A stable CLI or report format is versioned and documented. |

Do not promote a tool because it is clever. Promote it only when it reduces real
maintenance cost for rendering or another project.

## Acceptance criteria

The governance plan is complete when:

- the governance repository starts from standard Spec Kit;
- it builds and tests independently;
- it provides at least one optional tool with a narrow, documented purpose;
- generic code does not depend on rendering vocabulary;
- rendering can use or ignore the tool without losing its build/test/release
  path;
- any shared contract is small, versioned, and documented.
