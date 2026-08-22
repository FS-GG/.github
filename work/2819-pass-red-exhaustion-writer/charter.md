---
schemaVersion: 1
workId: 2819-pass-red-exhaustion-writer
title: Round-three pass/red repair-phase agreement
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

# Round-three pass/red repair-phase agreement Charter

## Identity
- Work id: `2819-pass-red-exhaustion-writer`
- Lifecycle stage: charter
- Status: chartered

## Principles
- Preserve the append-only review ledger: a recorded round-three pass is immutable even when a later required check invalidates its operational conclusion.
- Keep read-side projection and write-side authorization on the same typed terminal-set definition.
- Bound recovery to the protocol's one existing repair phase; never synthesize ordinary round four.
- Fail closed on stale head, backlink, claim-generation, or unsettled-check evidence.

## Scope Boundaries
- Align ordinary-exhaustion classification in Core/Lifecycle with escalation admission in the live writer.
- Cover the exact pass-then-red, completed-wait, claim-turnover chain through pure, lifecycle, live-writer, and cross-claim tests.
- Preserve current pending-check and green-check routes and all existing escalation provenance bindings.
- Do not rewrite review records, weaken confirmation ceilings, create a second repair phase, or alter unrelated delivery behavior.

## Policy Pointers
- SDD policy comes from `.fsgg/sdd.yml` and `.fsgg/agents.yml`.
- Governance files are optional compatibility pointers and are not evaluated by this command.

## Lifecycle Notes
- Next lifecycle action: `fsgg-sdd specify --work 2819-pass-red-exhaustion-writer`.
