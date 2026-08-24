---
schemaVersion: 1
workId: 2859-review-wait-evidence-ref
title: Host-Owned Review Wait Boundary
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

# Host-Owned Review Wait Boundary Charter

## Identity
- Work id: `2859-review-wait-evidence-ref`
- Lifecycle stage: charter
- Status: chartered

## Principles
- Make the engine author state it already owns; callers should express intent, not transcribe authority fields.
- Reject an invalid terminal evidence pointer before it becomes immutable.
- Preserve append-only review history and fail closed on unreadable or ambiguous live state.

## Scope Boundaries
- Cover the `review wait` command boundary, its durable ledger validation, focused production-shaped tests, and mirrored operator guidance.
- Exclude review verdict policy, critic judgement, host acceptance, merge, and publication.

## Policy Pointers
- SDD policy comes from `.fsgg/sdd.yml` and `.fsgg/agents.yml`.
- Governance files are optional compatibility pointers and are not evaluated by this command.

## Lifecycle Notes
- Next lifecycle action: `fsgg-sdd specify --work 2859-review-wait-evidence-ref`.
