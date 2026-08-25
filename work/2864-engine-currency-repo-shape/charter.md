---
schemaVersion: 1
workId: 2864-engine-currency-repo-shape
title: Repository-shape-aware engine currency verification
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

# Repository-shape-aware engine currency verification Charter

## Identity
- Work id: `2864-engine-currency-repo-shape`
- Lifecycle stage: charter
- Status: chartered

## Principles
- A pre-write safety check must observe the engine source that the current repository shape actually resolves.
- An absent, unreadable, or unparsable currency subject is a refusal, never a zero-drift result.
- Canonical guidance, tracked projections, manifests, and packaged kit version move as one reviewed change.

## Scope Boundaries
- Preserve the existing engine resolver and board-write semantics; this work changes the operator protocol only.
- Keep authoring-repository source drift and receiver-repository package-pin drift as explicit, separately measured branches.

## Policy Pointers
- SDD policy comes from `.fsgg/sdd.yml` and `.fsgg/agents.yml`.
- Governance files are optional compatibility pointers and are not evaluated by this command.

## Lifecycle Notes
- Next lifecycle action: `fsgg-sdd specify --work 2864-engine-currency-repo-shape`.
