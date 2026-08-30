---
schemaVersion: 1
workId: 107-repair-phase-live-assertion-writer
title: Typed live repair assertion writer
stage: charter
changeTier: tier1
status: chartered
policyPointers:
  - .fsgg/sdd.yml
  - .fsgg/agents.yml
  - .fsgg/policy.yml
  - .fsgg/capabilities.yml
  - .fsgg/tooling.yml
---

# Typed live repair assertion writer Charter

## Identity
- Work id: `107-repair-phase-live-assertion-writer`
- Lifecycle stage: charter
- Status: chartered

## Principles
- Append-only structured review comments are the only authority; prose and caller-authored compatibility events are not.
- Exact PR, head, review URL, and minted identity bindings fail closed.
- The implementing worker and current critic may not grant the transition they benefit from or confirm.
- The production CLI path and its full exhaustion-to-acceptance lifecycle must be executable and independently reviewable.

## Scope Boundaries
- In: live review assertion writer/reader, review inspect/wait-enter/record routing, public command contract, lifecycle and end-to-end tests.
- Out: changing review ceilings, accepting no-op commits, compatibility-only explicit wait events, and changes to the parked GS2-03.7 implementation.

## Policy Pointers
- SDD policy comes from `.fsgg/sdd.yml` and `.fsgg/agents.yml`.
- Governance files are optional compatibility pointers and are not evaluated by this command.

## Lifecycle Notes
- Tier 1 command and durable-comment contract change; implementation follows an implementation-ready SDD package and includes inversion evidence.
