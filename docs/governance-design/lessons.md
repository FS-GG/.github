---
title: Lessons and anti-goals
category: Governance design
categoryindex: 7
index: 8
description: Why the previous attempt became opaque and oppressive, the anti-goals that prevent a repeat, and how this design honours the project-split decision.
---

# Lessons and anti-goals

This design is shaped as much by what went wrong before as by what it wants to
achieve. The previous governance attempt became opaque and oppressive in daily
use — even editing documents under `docs/reports` triggered heavy automatic
machinery with hundreds of tests. That is the failure mode the whole redesign
exists to prevent, and not by tuning: by construction.

## Diagnosis — three root causes

1. **Default-deny conflated "unclassified" with "high-risk."** An unmatched path
   fell through to the heaviest tier and the broadest gate. The floor was heavy,
   so the burden was on the developer to prove a change was cheap rather than on
   the system to justify spending their time.

2. **Everything blocked.** There was no advisory tier. A matched rule meant gates
   you had to pass, so thinking artifacts and contract artifacts were treated
   identically — documentation matched a gate just as code did.

3. **It was opaque, with no inner-loop escape.** Rules were compiled with
   text-only route output, so it was hard to see *why* a gate fired. The only
   "escape" disclosed synthetic evidence but never changed the verdict — that is
   disclosure, not relief. There was nowhere to simply try something.

## How the design answers each

- Cause 1 → **light by default**. The floor is the lightest tier; heavy checks
  require a positive match against a small, named, fenced surface. Thinking lives
  in a zero-gate zone. See [Routing, severity, and run modes](routing-and-modes.md).
- Cause 2 → **advisory by default**. `Severity` is orthogonal to `CheckTier`;
  rules report unless explicitly marked blocking, and the blocking set is short
  and listable.
- Cause 3 → **explainable by construction** and the **honest escape hatch**. The
  reified [rule eDSL](rule-edsl.md) makes every check render to a sentence and
  every conclusion carry provenance; `RunMode.Sandbox` turns governance off for
  the inner loop while the merge boundary recomputes independently, so opacity
  cannot land.

## Anti-goals

The design explicitly refuses to become these:

- **A platform every project must run on.** Generality lives in the kernel
  library, not in one unified workflow. A project embeds the kernel; it does not
  run on it.
- **A replacement for authored Spec Kit artifacts.** Governance integrates
  additively — read-only status, report generators, validators, optional hooks —
  and never starts by replacing `spec.md` / `plan.md` / `tasks.md` with a custom
  graph authority.
- **A self-hosting dogfood loop.** Governance must never be on the critical path
  of the work it governs. The dependency direction is one-way: governance may
  inspect a project; a project must not require governance.
- **An opaque oracle.** A conclusion with no renderable reason is a defect. If a
  check cannot be explained, it cannot block.
- **Heavy by default.** If a change is not on a fenced surface, it costs nothing.
  Cost must be justified by risk, every time.

## Relationship to the project-split decision

These anti-goals are the daily-work expression of the
[project-split decision](../project-split-decision.md): rendering and governance
are separate projects, both on standard Spec Kit, and rendering does not depend
on governance. The split solved the problem at the repository level; the
principles here solve the same problem at the level of a single edit. The old
implementation violated the split's spirit even after the repositories were
separated; this design makes the split's intent structural rather than
aspirational.

## A test to keep us honest

If, six months in, editing a note or a report ever triggers a test suite — or a
developer cannot explain in one line why a gate fired, or cannot turn governance
off to try an idea — the design has regressed to the failure it was built to
avoid, regardless of how much value it adds elsewhere.
