---
schemaVersion: 1
workId: 2727-lifecycle-extraction
title: Lifecycle CLI extraction and typed completion dependency
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

# Lifecycle CLI extraction and typed completion dependency Charter

## Identity
- Work id: `2727-lifecycle-extraction`
- Lifecycle stage: charter
- Status: chartered

## Principles
- Preserve every lifecycle command's observable output, exit code, and side effects.
- Replace mutable initialization order with an explicit typed completion dependency.
- Move implementation and focused coverage together so Lifecycle owns a real project boundary.
- Compose handlers through the registration contract established by FS-GG/.github#2726.

## Scope Boundaries
- In scope: `done`, `landable`, `delivery`, `review`, `route`, `verify-paths`, and
  `followup-audit` handlers, their direct helpers and tests, plus project/solution wiring.
- Out of scope: behavior changes, other command-family extractions, and edits outside the declared
  source, test, and SDD package paths.
- The mutable `completeDelivery` cell and its fail-fast placeholder must be removed, not relocated.
- Packing and release-payload verification remain compatibility gates.

## Policy Pointers
- SDD policy comes from `.fsgg/sdd.yml` and `.fsgg/agents.yml`.
- Governance files are optional compatibility pointers and are not evaluated by this command.

## Lifecycle Notes
- Next lifecycle action: `fsgg-sdd specify --work 2727-lifecycle-extraction`.
