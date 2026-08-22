---
schemaVersion: 1
workId: 2723-fence-arming-premise-rot
title: Arm merge fence and repair repos.sh premise drift
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

# Arm merge fence and repair repos.sh premise drift Charter

## Identity
- Work id: `2723-fence-arming-premise-rot`
- Lifecycle stage: charter
- Status: chartered

## Principles
- Arm no required context until its producer has reported on a real pull request and static
  producibility has been demonstrated for the exact context.
- Preserve a fail-closed merge fence: authorization failures and unreadable live state block once
  armed; unrelated pull requests receive a real passing check rather than a missing context.
- Treat branch-protection changes as per-repository, read-back-verified operations with an explicit
  rollback order.

## Scope Boundaries
- Repair the stale credential premise in `scripts/repos.sh`.
- Record the arming evidence, receiver decisions, residuals, and rollback contract in
  `docs/coordination/reusable-workflow-contract.md`.
- Apply the required-context transition only after all five design steps are evidenced; do not enable
  merge queues implicitly and do not alter the fence producer in this slice.
- Keep SDD lifecycle ownership separate from optional Governance enforcement.

## Policy Pointers
- SDD policy comes from `.fsgg/sdd.yml` and `.fsgg/agents.yml`.
- Governance files are optional compatibility pointers and are not evaluated by this command.

## Lifecycle Notes
- Next lifecycle action: `fsgg-sdd specify --work 2723-fence-arming-premise-rot`.
