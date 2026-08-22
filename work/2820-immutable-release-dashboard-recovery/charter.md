---
schemaVersion: 1
workId: 2820-immutable-release-dashboard-recovery
title: Immutable Release Dashboard Recovery
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

# Immutable Release Dashboard Recovery Charter

## Identity
- Work id: `2820-immutable-release-dashboard-recovery`
- Lifecycle stage: charter
- Status: chartered

## Principles
- Treat immutable GitHub release assets as append-only recovery history: exact-source retries must never delete, replace, or rewrite them.
- Keep receiver dashboard delivery independently resumable after feed completion and release promotion so journal transport cannot strand notification.
- Derive the receiver census from the checked-in roster, execute every dashboard write, and read each result back before declaring delivery complete.
- Make retry/idempotency, zero-receiver, immutable-journal refusal, and dashboard-write refusal observable with subject-breaking red controls.

## Scope Boundaries
- In: release-kit and coordination-engine recovery ordering, the shared promotion handoff, the release-saga fixture, and CI coverage for immutable exact-source retries.
- In: roster-derived dashboard delivery, read-back, idempotency, zero-receiver refusal, and refused-write behavior for both package workflows.
- Out: mutating immutable GitHub release assets, changing package contents or feed publication semantics, editing receiver repositories, or broadening unrelated release workflows.

## Policy Pointers
- SDD policy comes from `.fsgg/sdd.yml` and `.fsgg/agents.yml`.
- Governance files are optional compatibility pointers and are not evaluated by this command.

## Lifecycle Notes
- Governing board item: `FS-GG/.github#2820`.
- Typed route receipt: revision 1, digest `9fe4b3f2c289f8733621229b996440291022b7962e8ef8e694763368a523485b`, route `sdd-required`.
- Canonical specification: `work/2820-immutable-release-dashboard-recovery/spec.md`.
- Next lifecycle action: `fsgg-sdd specify --work 2820-immutable-release-dashboard-recovery`.
