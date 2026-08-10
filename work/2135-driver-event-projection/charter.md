---
schemaVersion: 1
workId: 2135-driver-event-projection
title: "coord events: derive material transitions and complete active-state reports"
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

# coord events: derive material transitions and complete active-state reports Charter

## Identity
Make the coordination driver derive material status transitions and the complete
active-item inventory from an engine-owned, versioned event cursor over live
board/claim/PR/review/delivery facts, rather than from host memory and prose.

## Principles
- Live board, claim, PR, review, and delivery-obligation facts are the sole input
  to a material transition; a returning worker process is never itself a
  transition.
- A cursor re-read against unchanged facts is idempotent: it emits no duplicate
  event, and a failed read emits an explicit unreadable/no-verdict event rather
  than a false "nothing active".
- JSON is the authoritative projection consumed by host adapters; the two-line
  text form is a stable rendering of the same facts, never a second source.

## Scope Boundaries
- In: a pure Core projection module, its CLI surface, a durable per-item cursor,
  tests reproducing the omitted-active-item/premature-return/review-repair/
  merged-awaiting-release/external-claim/failed-read cases, and equivalent
  drive-board/work-board guidance for Codex and Claude that consumes the
  projection instead of authoring status prose.
- Out: automatically classifying findings, deciding wave consolidation, or
  changing GitHub's review policy — those remain the driver-planner's
  (`.github#2127`) job, not this projection's.

## Policy Pointers
- `.fsgg/constitution.md` principles I, II, VI, VII, and VIII.
- Issue `.github#2135` acceptance criteria and its mandatory `sdd-required` route.
- `Driver.fs`/`Delivery.fs` (`.github#2127`) typed transition and stage contracts
  this projection is layered over.

## Lifecycle Notes
- Next lifecycle action: `fsgg-sdd specify --work 2135-driver-event-projection`.
