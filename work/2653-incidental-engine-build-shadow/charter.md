---
schemaVersion: 1
workId: 2653-incidental-engine-build-shadow
title: Incidental Engine Build Shadow
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

# Incidental Engine Build Shadow Charter

## Identity
- Work id: `2653-incidental-engine-build-shadow`
- Coordination item: `.github#2653`
- Lifecycle stage: charter
- Status: chartered

## Principles
- **The resolver stays a transparent pipe with a legible tier order.** Every change here is one more
  stated precondition on an existing tier, never a hidden second `exec` and never a new tier.
- **A guard that cannot answer must not act.** `.github#1549` settled that an unanswerable staleness
  question is not freshness; the counterpart adopted here is that an unanswerable *intent* question is
  not permission to swap the engine under the caller. Every failure path resolves to today's behaviour.
- **Resolution may only ever move toward an engine that answers "current".** A change that repairs one
  refusal by manufacturing another in a neighbouring case is not a repair.
- **Coordination knowledge lives outside the kit row.** `.github#1586` split this file pair by
  distribution rather than by taste; the predicate is authored where receivers never load it.
- **A rule enforced by whoever happens to remember it decays** (`#570`). Five agents each rediscovered
  and hand-repaired this in one run; the repair belongs in the tool, not in the next worker's memory.

## Scope Boundaries
- Keep SDD lifecycle ownership separate from optional Governance enforcement.
- The subject is engine RESOLUTION and the wording of the refusal it can raise. The verb partition, the
  staleness verdict itself, the lease clock, and everything under `src/` are out of scope.
- The producers that create the incidental artifact are out of scope, on `.github#2653`'s own second
  limb for criterion 4 and on the filer's follow-up evidence that one of them requires the artifact at
  exactly the probed path.

## Policy Pointers
- SDD policy comes from `.fsgg/sdd.yml` and `.fsgg/agents.yml`.
- Governance files are optional compatibility pointers and are not evaluated by this command.

## Lifecycle Notes
- Next lifecycle action: `fsgg-sdd specify --work 2653-incidental-engine-build-shadow`.
