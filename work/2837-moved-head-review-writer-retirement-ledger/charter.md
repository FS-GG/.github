---
schemaVersion: 1
workId: 2837-moved-head-review-writer-retirement-ledger
title: Restart review-record sealing after retiring an accepted moved-head chain
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

# Restart review-record sealing after retiring an accepted moved-head chain Charter

## Identity
- Work id: `2837-moved-head-review-writer-retirement-ledger`
- Lifecycle stage: charter
- Status: chartered

## Principles
- The review writer and reader must derive one identical live/retired generation partition.
- Structured review history remains append-only: retiring a chain changes interpretation, never old bytes.
- Production-boundary coverage must exercise the real writer and then the live reader on the moved-head route.

## Scope Boundaries
- Restart revision and digest sealing only after an accepted chain belongs to a non-current head.
- Preserve ordinary same-head revision continuity and all existing review marker schemas.
- Keep the change within the issue's declared coordination source and regression paths.

## Policy Pointers
- SDD policy comes from `.fsgg/sdd.yml` and `.fsgg/agents.yml`.
- Governance files are optional compatibility pointers and are not evaluated by this command.

## Lifecycle Notes
- Next lifecycle action: `fsgg-sdd specify --work 2837-moved-head-review-writer-retirement-ledger`.
