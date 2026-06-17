---
title: Governance design
category: Governance design
categoryindex: 7
index: 1
description: Comprehensive design for the FS.GG agent-governance system — a small reusable inference kernel, a reified rule eDSL in plain F#, and light-by-default routing with an honest escape hatch.
---

# Governance design

This folder is the current, comprehensive design for the FS.GG governance
system. It supersedes the monolithic "SpecFlow graph operating system" idea and
re-founds governance as a **small, reusable agent-governance kernel** with
domain-specific adapters — not a platform every project must run on.

The aim is broader than F# software. The same kernel should govern essays,
research, and engineering work as cleanly as it governs a rendering framework.
What changes between domains is the *fact vocabulary*; the inference, the
arbitration model, the evidence model, and the rule language stay the same.

## One sentence

Governance is a **pure inference kernel** over typed facts and rules, where every
rule declares *who is competent to decide it* (machine, agent, or human), every
rule's check is **reified data** that can be evaluated, rendered, hashed, and
explained from one source, and the enforcement layer is **light and advisory by
default** with a loud, local-only escape hatch for everyday work.

## Design documents

Read in order:

- [Goals and principles](principles.md) — what the system is, the pivot away from
  the monolith, kernel-as-product, and the four principles the design is built
  to guarantee (light by default, advisory by default, explainable by
  construction, honest escape hatch).
- [The inference kernel](kernel.md) — the domain-neutral core: facts, rules,
  fixed-point evaluation, provenance, the `CheckTier` arbitration model, and the
  evidence / synthetic-taint model.
- [The rule eDSL](rule-edsl.md) — the reified `Check` algebra in plain F#, its
  combinators, the four interpreters (`eval` / `render` / `hash` / `explain`),
  the `Opaque` escape hatch, and the bridge back to the kernel.
- [Routing, severity, and run modes](routing-and-modes.md) — light-by-default
  routing, the `Severity` axis, the `RunMode` escape hatch, the unbypassable
  merge boundary, and the explainable `Route` output.
- [Domain adapters](adapters.md) — how a domain plugs in: the design-system
  adapter and its rule catalog, plus research, essay, and engineering sketches
  that demonstrate generality.
- [Spec Kit in the system](speckit-in-the-system.md) — how the Spec Kit workflow
  is expressed: a spec-kit adapter, phase checks as reified rules, run-modes
  mapped to phases, and the constitution as the dial.
- [Lessons and anti-goals](lessons.md) — why the previous attempt became opaque
  and oppressive, the anti-goals that prevent a repeat, and how this design
  honours the project-split decision.

## Status

Design only. The inference kernel, the reified eDSL, severity/run-mode routing,
and the adapters described here are envisioned, not yet implemented. The
prior-art enforcement machinery (routing, gates, evidence audit) exists in the
FS-Skia-UI repository and is the starting point this design refactors.

## Internal links

Within this repository:

- [FS.GG project split](../index.md) — the split-repository direction.
- [Project split decision](../project-split-decision.md) — why the monolithic
  graph operating system was replaced.
- [Governance project](../governance-project.md) — scope and adoption bar for
  governance as a separate tool product.
- [Governance implementation plan](../governance-implementation-plan.md) — the
  staged plan (fresh repo, one narrow tool first, optional adoption).
- [Rendering project](../rendering-project.md) — the consuming product and the
  dependency direction governance must respect.
- [Transition and boundaries](../transition-and-boundaries.md) — cross-repo
  contracts, package identities, and migration handling.

Source material in the [FS-Skia-UI repository](https://github.com/EHotwagner/FS-Skia-UI)
(prior art this design refactors):

- [`docs/reports/2026-06-16-0958-design-system-governance-domain-detailed-design.md`](https://github.com/EHotwagner/FS-Skia-UI/blob/main/docs/reports/2026-06-16-0958-design-system-governance-domain-detailed-design.md)
  — the `CheckTier` model and the design-system rule catalog (latest design).
- [`docs/reports/2026-06-07-0838-governance-kernel-split-detailed-design.md`](https://github.com/EHotwagner/FS-Skia-UI/blob/main/docs/reports/2026-06-07-0838-governance-kernel-split-detailed-design.md)
  — the layered kernel and the generic inference substrate.
- [`docs/reports/2026-06-06-1055-governance-kernel-extraction-implementation-plan.md`](https://github.com/EHotwagner/FS-Skia-UI/blob/main/docs/reports/2026-06-06-1055-governance-kernel-extraction-implementation-plan.md)
  — the "typed F# rules, not a new DSL" decision.
- [`docs/governance/routing-and-gates.md`](https://github.com/EHotwagner/FS-Skia-UI/blob/main/docs/governance/routing-and-gates.md)
  — the existing routing/gates implementation this design makes light by default.
- [`docs/governance/evidence-and-audit.md`](https://github.com/EHotwagner/FS-Skia-UI/blob/main/docs/governance/evidence-and-audit.md)
  — the evidence state machine and synthetic-taint propagation.
- [`docs/governance/single-source-generation.md`](https://github.com/EHotwagner/FS-Skia-UI/blob/main/docs/governance/single-source-generation.md)
  — the canonical-source → generated-view → currency-gate pattern.
- [`docs/governance/speckit-placement.md`](https://github.com/EHotwagner/FS-Skia-UI/blob/main/docs/governance/speckit-placement.md)
  — how governance touchpoints attach to the Spec Kit lifecycle.

## External references

Concepts the design builds on:

- [GitHub Spec Kit](https://github.com/github/spec-kit) — the spec → plan →
  tasks → implement workflow governance attaches to.
- [F# computation expressions](https://learn.microsoft.com/en-us/dotnet/fsharp/language-reference/computation-expressions)
  — the heavyweight embedding option we deliberately avoid for rule definition.
- [F# discriminated unions](https://learn.microsoft.com/en-us/dotnet/fsharp/language-reference/discriminated-unions)
  — the closed-union foundation of the reified `Check` algebra.
- [F# active patterns](https://learn.microsoft.com/en-us/dotnet/fsharp/language-reference/active-patterns)
  — used in the lightest-embedding rule surface.
- [Forward chaining](https://en.wikipedia.org/wiki/Forward_chaining) and
  [production systems](https://en.wikipedia.org/wiki/Production_system_(computer_science))
  — the inference model the kernel implements.
- [Datalog](https://en.wikipedia.org/wiki/Datalog) — monotonic, terminating
  fixed-point evaluation over facts and rules.
- [Rete algorithm](https://en.wikipedia.org/wiki/Rete_algorithm) — the classical
  efficient implementation family for production-rule matching.
- [Reason / truth maintenance](https://en.wikipedia.org/wiki/Reason_maintenance)
  — the justification model behind per-fact provenance.
- [Three-valued (Kleene) logic](https://en.wikipedia.org/wiki/Three-valued_logic)
  — `Pass` / `Fail` / `Uncertain` composition in `eval`.
- [Functional core, imperative shell](https://www.destroyallsoftware.com/talks/boundaries)
  — the pure-kernel / effects-at-the-edge discipline.
- [Domain-specific languages](https://en.wikipedia.org/wiki/Domain-specific_language)
  — embedded vs external DSLs; we use an embedded DSL only.
- [Design Tokens Format Module](https://tr.designtokens.org/format/) — the DTCG
  token format the design-system adapter checks against.
- [WCAG contrast minimum](https://www.w3.org/WAI/WCAG21/Understanding/contrast-minimum.html)
  — a deterministic design-system rule the adapter encodes.
