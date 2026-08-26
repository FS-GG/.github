---
title: What consumers receive
category: Design
categoryindex: 4
index: 36
description: The intended consumer bundle and its authority boundaries.
---

# What consumers receive

The target consumer distribution is a small, reproducible workspace substrate:

| Delivered surface | Purpose |
|---|---|
| F# tools such as `fsgg-sdd`, `fsgg-coord`, and Governance helpers | Scaffold, inspect, coordinate, verify, migrate, and upgrade the workspace |
| Pinned Quint toolchain and FS.GG profile | Typecheck, execute, and selectively verify canonical specifications |
| Literate Markdown/Quint infrastructure | Keep human explanation and formal semantics in one authored artifact |
| Process and product agent skills | Teach supported workflows and ecosystem-specific maintenance rules |
| Thin GitHub and CI workflows | Invoke declared capabilities and publish evidence without duplicating policy |
| Provenance, manifests, and generated contracts | Bind exact source, tools, projections, and accepted revisions |

F# is the implementation language of the orchestration tools; it is not the
required language of the consumer product. Quint is the intended semantic
authority; skills and generated contracts are not. Application code, tests, and
product-specific replay adapters remain owned by the consumer repository.

Exact package identities and default activation remain governed by the
[migration design](../coordination/2026-08-25-quint-first-typed-sdd-migration-design.md)
and publish-before-adopt registry rollout.
