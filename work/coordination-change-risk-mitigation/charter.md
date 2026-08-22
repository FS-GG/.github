---
schemaVersion: 1
workId: coordination-change-risk-mitigation
title: Coordination Change Risk Mitigation
stage: charter
changeTier: tier1
status: chartered
policyPointers:
  - .fsgg/constitution.md
  - .fsgg/sdd.yml
  - docs/coordination/2026-08-22-coordination-change-risk-mitigation-design.md
---

# Coordination Change Risk Mitigation Charter

## Identity
- Work id: `coordination-change-risk-mitigation`
- Source design: `docs/coordination/2026-08-22-coordination-change-risk-mitigation-design.md`
- Primary incident: `.github#2753`
- Lifecycle stage: charter
- Status: chartered
- Change tier: Tier 1, because commands, schemas, generated projections, automation, and release contracts change.

## Principles
- One lifecycle question has one typed authority; consumers render or execute its decision without rebuilding it.
- Unknown, unreadable, stale, and contradictory authority are explicit refusal states rather than absence.
- Durable receipts precede issue, board, claim, cleanup, publication, and receiver projections.
- Independent tests observe behavior and consumer parity; they do not become another authored inventory.
- Immutable release evidence is verified in place and extended by append-only receipts, never rewritten.
- Cheap structural completeness runs before expensive confidence and independent review.
- Self-hosting is a digest-bound, host-accepted trust transition rather than an ad hoc candidate run.

## Scope Boundaries
- In scope: the coordination engine command catalogue, lifecycle predicates, completion and bootstrap receipts,
  change-completeness CI, model-based conformance, immutable release recovery, receiver receipts, tests, and hosted
  lifecycle projections described by the source design.
- In scope: compatibility-preserving rollout, inversion tests, measurement fixtures, and reconciliation of premature
  `Done` projections.
- Out of scope: a big-bang engine rewrite, generating heterogeneous parser behavior, weakening independent review,
  changing unrelated GitHub security controls, or repairing historical release assets in place.
- The seven decisions are delivered as independently mergeable phases. A predecessor path is removed only after
  parity and an effective inversion are demonstrated.

## Policy Pointers
- The product constitution at `.fsgg/constitution.md` is the highest-precedence engineering authority.
- Lifecycle order and generated-view behavior come from `.fsgg/sdd.yml`.
- The source design supplies the measured incidents, decisions, rollout, acceptance criteria, and success metrics.
- Optional Governance configuration is compatibility context, not SDD authority.

## Lifecycle Notes
- The existing design is the authoritative intent source for this work package.
- Next lifecycle action: `fsgg-sdd specify --work coordination-change-risk-mitigation`.
