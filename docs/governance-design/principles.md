---
title: Goals and principles
category: Governance design
categoryindex: 7
index: 2
description: What the governance system is, the pivot away from the monolith, kernel-as-product, and the four principles the design guarantees.
---

# Goals and principles

## What this is

A **general agent-governance system**: a way to state rules over a body of work,
decide who is competent to judge each rule (a machine, an AI agent, or a human),
collect evidence that the rules hold, and explain every conclusion — without
forcing the work to be shaped like the governance tool.

The system is built so the same core can govern very different domains. Software
is the first adapter, not the substrate. Research, essays, and engineering are
intended targets, not afterthoughts. What differs between domains is only the
*fact vocabulary*. The inference, the arbitration model, the evidence model, and
the rule language are shared.

## The pivot

An earlier proposal was a monolithic "SpecFlow graph operating system": a single
platform owning the project graph, product graph, feature graph, evidence
ledger, generated projections, route planning, and release policy. It was
internally coherent but it made the rendering framework depend on a changing
governance platform, and it became opaque and oppressive in practice — even
documentation edits triggered heavy automatic machinery.

The current direction reverses that. Governance is a **small reusable kernel**
that work *embeds*, not a platform that work *runs on*. Generality lives in the
kernel library, never in one grand unified workflow. See
[Lessons and anti-goals](lessons.md) for the full diagnosis and the
[project-split decision](../project-split-decision.md) for the repository-level
consequence.

## Kernel as product, domains as adapters

The product is the **inference kernel** (facts, rules, fixed-point evaluation,
provenance), the **`CheckTier` arbitration model** (machine / agent / human), the
**reified rule eDSL**, and the **evidence model**. These contain no domain
vocabulary at all.

Everything domain-specific lives in an **adapter**: the set of facts a domain
asserts, the artifacts it inspects, and the probes its rules call. The
design-system adapter knows about tokens and contrast; an essay adapter knows
about sections and citations. The kernel knows about neither.

The boundary test: generic code has zero domain vocabulary, and removing any one
adapter leaves the kernel and the other adapters intact.

## The four principles

The design exists to guarantee four properties *structurally* — by construction,
not by careful configuration that can drift.

### 1. Light by default — the system justifies cost, not the developer

The cost a change incurs is proportional to its risk. An unclassified or
low-stakes change incurs **no machinery**. Heavy checks require a *positive*
match against a small, named, fenced high-stakes surface (a published API, a
release, an irreversible contract). Thinking artifacts — notes, reports, drafts,
experiments — live in a zero-gate zone, because thinking is not contract.

This inverts the previous default-deny floor, where anything unclassified fell
through to the heaviest tier. The burden is on the system to justify spending a
person's time, not on the person to prove a change was cheap.

### 2. Advisory by default — blocking is opt-in and rare

A rule *reports* unless it is explicitly marked blocking. `Severity` is a
first-class axis, orthogonal to `CheckTier`: the tier says who decides, the
severity says whether failure stops you. The full set of blocking rules must be
short enough to list at a glance; a long blocking set is a design smell.

### 3. Explainable by construction

Opacity is treated as a defect, not a tuning problem. Every conclusion carries
provenance, and every check is reified data that renders to a human- and
agent-readable sentence. A gate that fires must state, in one line, the rule, the
thing that matched, and the rendered check that decided it. "No reason" is
unrepresentable because the reason *is* the rule id plus the rendered check.

### 4. Honest escape hatch — free inner loop, unbypassable boundary

There is a real way to turn governance off for developing, debugging, and trying
things out. It is **loud** (it announces itself), **local-only**, and **cannot be
the basis of a merge**: the merge boundary recomputes from scratch against the
base branch and ignores any local mode. So you can develop freely without the
machinery, but you cannot *land* an un-governed state. This is the relief the old
disclosure-only flag never provided.

## Dependency direction

Governance tooling may inspect a consuming project from the outside. A consuming
project must never need governance tooling to do ordinary work.

```text
governance kernel may inspect a project
a project must not require the governance kernel
```

If the kernel becomes heavy, brittle, or distracting, a project drops it and
keeps building. That escape valve is what keeps governance honest.
