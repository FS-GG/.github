---
schemaVersion: 1
workId: ci-runtime-optimization
title: CI Runtime Optimization Without Coverage Loss
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

# CI Runtime Optimization Without Coverage Loss Charter

## Identity
- Work id: `ci-runtime-optimization`
- Lifecycle stage: charter
- Status: chartered

## Principles
- Optimize scheduling and repetition, never the meaning of a live verdict.
- Unknown classifications fail closed by running more evidence, not less.
- A check that is conditionally cheap still publishes a stable, explainable context.
- Mutation population, survivor accounting, and non-vacuity are safety contracts.
- Prefer parsing an existing authoritative result over rerunning the subject to recreate it.

## Scope Boundaries
- Change only signature-documentation mutation scheduling, shell-lint fixture ownership,
  and coord-engine test-result accounting.
- Do not alter claim, review, landability, replay, publication, parity, or temporal
  external-state checks.
- Defer cross-workflow compiled-artifact fan-out until a separate exact-SHA trust and
  transfer-cost experiment exists.

## Policy Pointers
- SDD policy comes from `.fsgg/sdd.yml` and `.fsgg/agents.yml`.
- Governance files are optional compatibility pointers and are not evaluated by this command.

## Lifecycle Notes
- Next lifecycle action: `fsgg-sdd specify --work ci-runtime-optimization`.
