---
schemaVersion: 1
workId: 2801-mutual-overlap-arbitration
title: Automatic mutual-overlap arbitration
stage: charter
changeTier: tier1
status: chartered
policyPointers:
  - .fsgg/sdd.yml
  - .fsgg/agents.yml
---

# Automatic mutual-overlap arbitration Charter

## Identity
- Work id: `2801-mutual-overlap-arbitration`
- Coordination item: `FS-GG/.github#2801`
- Route: `sdd-required`, receipt revision 1, digest `f370c3a3660ff743715f2bb44ef6441c530e604294c03dbce5b8e27f0e2ccff4`.

## Principles
- Make the live claim generation and current overlapping reservations the only authority for wait edges.
- Detect only an authoritative two-cycle; absence, staleness, conflicts, or unreadable state fail closed.
- Arbitrate through one idempotent ADR-0051 room and one revisioned host precedence chain.
- Preserve both claims while narrowing the loser, and require fresh tree/overlap/review evidence before it resumes.

## Scope Boundaries
- Add a typed wait-for receipt, exact two-cycle detector, precedence receipt, and recoverable production writer.
- Extend only the existing claim/overlap and ADR-0051 room route in the declared CLI and GitHub writer files.
- Add focused pure, compiled production-route, fault-injection, and predicate-inversion coverage.
- Update the overlap policy so automatic arbitration precedes manual negotiation and occurrences fold onto this class row.
- Do not create a generic workflow framework, a second claim authority, durable dependency edges, or per-occurrence policy rows.

## Policy Pointers
- `.fsgg/constitution.md` governs Tier 1 structured contracts, Model–Update–Effect separation, and mandatory test evidence.
- `.agents/skills/intra-repo-parallel-work/references/worktrees-and-overlap.md` owns operator-facing overlap policy.
- ADR-0051 owns the coordination room primitive; this work automates its existing route.

## Lifecycle Notes
- The issue's accepted paths were widened before authoring this SDD package.
- Shared CLI paths overlap live item `.github#2794`; no shared file is edited until sequencing is resolved.
