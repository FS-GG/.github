---
schemaVersion: 1
workId: 2756-review-wait-continuity
title: Durable review wait and critic continuity
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

# Durable review wait and critic continuity Charter

## Identity
- Work id: `2756-review-wait-continuity`
- Lifecycle stage: charter
- Status: chartered

## Principles
- Treat a protocol-created review wait as durable coordination state, never an in-memory sleep.
- Bind every wait to authority-issued item, claim, review, and evidence revisions.
- Preserve the touch-set reservation during a bounded wait without extending an expired mutation capability.
- Make ordinary critic replacement explicit and independently reviewable; do not require unverifiable despawn testimony.

## Scope Boundaries
- Own the review wait and critic-generation contract, its typed coordination projection, and focused regression coverage.
- Reuse the existing claim-generation, review-ledger, and open-PR authorities; do not invent a second parser or capability.
- Keep runtime-specific agent liveness and dispatch in the host adapter.
- Do not broaden into delivery, landing, generic lease, or board scheduler redesign.

## Policy Pointers
- SDD policy comes from `.fsgg/sdd.yml` and `.fsgg/agents.yml`.
- Governance files are optional compatibility pointers and are not evaluated by this command.

## Lifecycle Notes
- Governing board item: `FS-GG/.github#2756`.
- Canonical specification: `work/2756-review-wait-continuity/spec.md`.
- Next lifecycle action: `fsgg-sdd specify --work 2756-review-wait-continuity`.
