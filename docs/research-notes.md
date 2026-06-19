---
title: Research notes
category: FS.GG
categoryindex: 6
index: 8
description: Durable research findings preserved from the earlier SpecFlow graph operating system report.
---

# Research notes

The earlier SpecFlow graph operating system report collected useful research.
The active recommendation changed, but several findings remain relevant to the
split-repository direction.

## Spec Kit

Standard Spec Kit remains a reasonable workflow baseline. It provides a known
sequence for specification, planning, tasks, and implementation without forcing
this project to own a custom feature graph.

The split direction treats Spec Kit as the default in each repository. Any
governance tooling should integrate with it incrementally rather than replacing
it from day one.

## Extension catalog

The Spec Kit extension ecosystem is useful as a taxonomy, not as trusted
runtime code. Patterns worth keeping in mind:

- status and health reports;
- traceability helpers;
- plan/spec review gates;
- architecture and docs guards;
- token or context budgeting;
- research/version guards;
- worktree helpers.

The governance project can implement selected ideas as repo-owned tooling. The
rendering project should not install third-party extension code as part of its
core workflow.

## GitHub repository identity

GitHub repository renames redirect many repository URLs, but they do not solve
product identity. GitHub Pages project-site URLs and repository-hosted actions
need explicit cutover planning. If the project rebrands, a bridge repository or
bridge page may still be needed.

## NuGet package identity

NuGet package identity is the package ID. A rename is a new package identity,
not an in-place mutation. Old packages should be deprecated toward replacement
packages after those replacements are published and verified.

This matters for the split because governance and rendering packages should not
share identity by accident. Runtime package IDs belong to the rendering product.
Governance package IDs belong to the governance product.

## Template identity

The `dotnet new` template has its own identity: package ID, template identity,
display name, short name, source name, generated package pins, docs, and
generated commands. If the rendering project rebrands, template identity must
move as one coherent matrix.

Template validation can remain practical without a custom graph:

- package the template;
- install it;
- instantiate it;
- build the generated product;
- check pins, package IDs, and docs links.

## FSharp.Formatting docs

FSharp.Formatting processes `docs/**/*.md` recursively. Markdown frontmatter is
optional and can define title, category, category ordering, page ordering,
description, and keywords. These FS.GG pages use that documented frontmatter
surface so they appear as a coherent docs section.

## Durable lesson from the monolithic plan

The previous report correctly identified many drift risks:

- package/template/docs identity can diverge;
- evidence files can become stale;
- generated artifacts can compete with authored artifacts;
- release provenance is not the same as local test output;
- template validation should simulate real consumers.

The split direction keeps those lessons, but does not put all of them into one
mandatory platform. The rendering repository should solve the high-value cases
with focused checks. The governance repository can explore generalized tooling
without making rendering work harder.

## Design-system boundary

Design-system metadata was one of the areas the monolithic plan wanted to put
into a product graph. The durable lesson is narrower: design tokens and themes
are product contract inputs, but they do not require a custom governance
platform to start.

The rendering project should keep the semantic control set stable and apply
specific design languages through themes. Branded or design-specific modules are
reserved for higher-level patterns where the design language defines structure,
workflow, or interaction beyond styling.
