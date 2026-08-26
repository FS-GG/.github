---
title: Typed source and pure model
category: Design
categoryindex: 4
index: 32
description: The target boundary between literate Quint authority, generated contracts, and implementations.
---

# Typed source and pure model

The design goal is one canonical, executable semantic source: literate Markdown
with ordered Quint blocks. Quint owns declared behavior, invariants, requirements,
and evidence relationships. Prose explains and navigates that model but cannot add
hidden normative meaning.

The boundary is deliberately one-way:

```text
literate Markdown + Quint
          │ typecheck, execute, verify
          ▼
small generated FS.GG contract
          │ bindings, impact, evidence routes
          ▼
implementation + consumer-owned replay adapter
```

Generated `.qnt`, JSON contracts, and F# bindings are projections, not coequal
sources. The compiled contract carries stable FS.GG identities and relationships;
it does not copy Quint's expression language or become a second interpreter.

Each product owns its domain model, real-operation adapter, and observable-state
projection. FS.GG.SDD owns the pinned Quint toolchain, extraction, generic replay
protocol, diagnostics, and evidence identities. `.github` owns registry and CI
routing policy, not product behavior.

Authority: [ADR-0077](../adr/0077-quint-first-typed-specification-authority.md) and
the [accepted Q1 boundary](../coordination/2026-08-26-adr-0077-q1-qualification-amendment.md).
