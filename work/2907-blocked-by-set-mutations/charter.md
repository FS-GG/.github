---
schemaVersion: 1
workId: 2907-blocked-by-set-mutations
title: Blocked-by Set Mutations
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

# Blocked-by Set Mutations Charter

## Identity
- Work id: `2907-blocked-by-set-mutations`
- Lifecycle stage: charter
- Status: chartered

## Principles
- Treat `Blocked by` as a set whose individual edges are preserved unless the caller explicitly removes them.
- Bind every derived set mutation to the live Projects-v2 item revision and observed field value; stale observations fail closed.
- Keep the Projects-v2 `Blocked by` field authoritative. Body text is a human-readable projection and never mutation input.
- Ship every new write or lint gate with a discriminating control that demonstrates the gate can fail.

## Scope Boundaries
- Change only the `set-field` command contract, its Projects-v2 write boundary, the inert-body lint rule, and focused tests.
- Preserve unrelated scalar field writes, explicit clearing, board authority, and existing dependency parsing/canonicalization.
- Do not infer dependency edges from issue bodies or silently repair a divergent body projection.

## Policy Pointers
- SDD policy comes from `.fsgg/sdd.yml` and `.fsgg/agents.yml`.
- Governance files are optional compatibility pointers and are not evaluated by this command.

## Lifecycle Notes
- Next lifecycle action: `fsgg-sdd specify --work 2907-blocked-by-set-mutations`.
