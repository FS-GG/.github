---
schemaVersion: 1
workId: 2772-force-claim-transition
title: Atomic and recoverable forced-claim transition
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

# Atomic and recoverable forced-claim transition Charter

## Identity
- Work id: `2772-force-claim-transition`
- Lifecycle stage: charter
- Status: chartered

## Principles
- Preserve one authoritative comment-order claim election; do not add a second lock predicate.
- Make interruption weaker than the desired final state: a transport failure may leave two
  deterministically ordered markers, but must never remove the old capability before the replacement
  marker exists.
- Treat every observed post-state as typed authorization. An exit code alone must not authorize retry.
- Distinguish failures before replacement creation, after replacement creation, and during cleanup.

## Scope Boundaries
- Change the forced-claim transition in `FS.GG.Coord.GitHub.Writes` and its CLI rendering only as needed
  to expose accurate outcomes.
- Add focused transport fault-injection coverage for replacement creation and old-marker cleanup.
- Preserve ordinary claims, renewals, stale-marker collection, twin/impersonation refusals, comment-order
  election, and board projection behavior.
- Do not change lease policy, scheduler ranking, board fields, or introduce a durable lock outside issue
  claim comments.

## Policy Pointers
- SDD policy comes from `.fsgg/sdd.yml` and `.fsgg/agents.yml`.
- Governance files are optional compatibility pointers and are not evaluated by this command.

## Lifecycle Notes
- Coordination item: `FS-GG/.github#2772`.
- Delivery route: `sdd-required`, revision 1, digest
  `48b390394f8c880a318e403f99974077fc217a006027ff424bc42cafeb66d010`.
- Governing acceptance requires safe interruption states, explicit post-state receipts and retry
  authority, and observed-red fault injection before implementation is review-ready.
