---
schemaVersion: 1
workId: 3014-repair-phase-turnover
title: Repair-phase turnover for admitted post-ceiling ledgers
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

# Repair-phase turnover for admitted post-ceiling ledgers Charter

## Identity
- Work id: `3014-repair-phase-turnover`
- Lifecycle stage: charter
- Status: chartered

## Principles
- One engine projection must never authorize a state that its typed writer cannot advance.
- Exhaustion authority binds the actual terminal record, head, digest, critic, and completed wait.
- Recovery stays append-only: no structured review comment is edited, deleted, or silently ignored.
- The one repair phase remains bounded and gains no shortcut around independent review or landing gates.

## Scope Boundaries
- Repair the production ordinary-exhaustion turnover writer and its tests/documentation.
- Preserve the exact three-round route byte-for-byte where its historical marker is sufficient.
- Add compatibility only for longer contiguous chains already admitted by the engine; do not increase the
  configured ordinary confirmation ceiling or create a second repair phase.
- Keep SDD lifecycle ownership separate from optional Governance enforcement.

## Policy Pointers
- SDD policy comes from `.fsgg/sdd.yml` and `.fsgg/agents.yml`.
- Governance files are optional compatibility pointers and are not evaluated by this command.

## Lifecycle Notes
- Next lifecycle action: `fsgg-sdd specify --work 3014-repair-phase-turnover`.
