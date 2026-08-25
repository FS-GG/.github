---
schemaVersion: 1
workId: 2871-closed-delivery-paths
title: Preserve declared paths across closed-item delivery
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

# Preserve declared paths across closed-item delivery Charter

## Identity
- Work id: `2871-closed-delivery-paths`
- Lifecycle stage: charter
- Status: chartered

## Principles
- The issue body is authoritative for its `Paths:` declaration throughout the
  delivery lifecycle; a board projection is not a substitute at the terminal
  boundary.
- Preserve the typed distinction between declared paths, `Paths: none`, an
  absent declaration, and an unread body.
- A failed read must fail closed and identify the read failure, never accuse a
  successfully authored issue body.

## Scope Boundaries
- Change only the live delivery fact reader and its production CLI e2e coverage.
- Do not alter scheduler touch-set projection, path grammar, review authority,
  claim authorization, or completion ordering.

## Policy Pointers
- SDD policy comes from `.fsgg/sdd.yml` and `.fsgg/agents.yml`.
- Governance files are optional compatibility pointers and are not evaluated by this command.

## Lifecycle Notes
- Next lifecycle action: `fsgg-sdd specify --work 2871-closed-delivery-paths`.
