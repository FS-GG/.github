---
title: FS.GG documentation
category: FS.GG
categoryindex: 1
index: 0
description: Entry point for using, understanding, extending, and developing FS.GG.
---

# FS.GG documentation

This is the general documentation index for FS.GG. Start with the consumer guides
when building a product; use the architecture, design, and coordination references
when extending the platform itself.

## Start here

- [Consumer guide](../consumer/index.md) — choose, create, and operate an FS.GG workspace.
- [Getting started](../consumer/getting-started.md) — install the tools and create a first product.
- [Agent setup](../consumer/agent-setup.md) — authenticate GitHub, create a repository and Project, and wire the workspace safely.
- [Components](../components.md) — independently adoptable framework packages and their current versions.
- [Tools](../tools.md) — workspace creation, lifecycle, coordination, and governance entry points.
- [Architecture](../architecture.md) — repositories, components, dependency direction, contracts, and composition.
- [Implementation status](implementation-status.md) — what is shipped, pending, or deliberately not yet the default.

## Use FS.GG

- [Choose a product](../consumer/which-products.md)
- [Lifecycle guide](../consumer/lifecycle.md)
- [Who drives the lifecycle](../consumer/who-drives-the-lifecycle.md)
- [Automation](../consumer/automation.md)
- [Governance](../consumer/governance.md)
- [Versioning and updates](../consumer/versioning-and-updates.md)
- [Frequently asked questions](../consumer/faq.md)

## Current design direction

These pages explain the target architecture FS.GG is working toward. They are
orientation, not a second source of truth: the linked ADRs, designs, issues, and
roadmaps own the exact contracts. Unless the status page says a capability is live,
treat it as a design goal rather than shipped behavior.

1. [Typed source and pure model](typed-source-and-pure-model.md)
2. [Why GitHub Substrate v2](github-substrate-v2-benefits.md)
3. [Quint-backed workspaces](quint-backed-workspaces.md)
4. [One Typed SDD lifecycle](single-typed-sdd-lifecycle.md)
5. [What consumers receive](consumer-delivery.md)
6. [Incoherent and contradictory proposals](incoherent-proposals.md)
7. [Implementation status](implementation-status.md)

The governing sources are
[ADR-0077](../adr/0077-quint-first-typed-specification-authority.md), its
[accepted Q1 amendment](../coordination/2026-08-26-adr-0077-q1-qualification-amendment.md),
the [Quint migration design](../coordination/2026-08-25-quint-first-typed-sdd-migration-design.md),
and the [GitHub Substrate v2 roadmap](../github-substrate-v2-roadmap.md).

## Platform reference

- [Architecture Decision Records](../adr/README.md)
- [Cross-repository coordination](../coordination/README.md)
- [Coordination board schema](../coordination/board-schema.md)
- [Parallel work and touch sets](../coordination/parallel-work.md)
- [Contract registry compatibility](../registry/compatibility.md)
- [GitHub Substrate v2 roadmap](../github-substrate-v2-roadmap.md)
- [Shared build configuration](../build/README.md)
- [Design and controls](../design-and-controls.md)

## History and deeper material

- [Platform documentation and historical planning index](../index.md)
- [Research notes](../research-notes.md)
- [Reports](../reports/)
- [Quint qualification reports](../quint/README.md)
