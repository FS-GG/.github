---
schemaVersion: 1
workId: 2852-runtime-skill-identity
title: "Bind producer, package, materialized, and runtime-loaded skill identity"
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

# Bind producer, package, materialized, and runtime-loaded skill identity Charter

## Identity
- Work id: `2852-runtime-skill-identity`
- Lifecycle stage: charter
- Status: chartered

## Principles
- Treat skill identity as a content-addressed chain, not a path or line-number convention.
- Name the authority and every artifact compared so a consumer never has to infer which copy won.
- Fail closed when any link in the producer-to-runtime chain is absent, unreadable, duplicated, or divergent.
- Keep runtime identity independent from materialization predicates; this contract answers which bytes loaded, not whether a skill should have materialized.

## Scope Boundaries
- Extend the existing skill registry, package materializer, and runtime-view checks into one end-to-end identity contract.
- Cover the coordination-kit `cross-repo-coordination` skill first while keeping the mechanism generic for all registered kit skills.
- Preserve existing package pinning, runtime-root declarations, and source ownership.
- Do not add parameter handling, change skill-selection predicates, or make runtime-specific configuration a second authority.

## Policy Pointers
- SDD policy comes from `.fsgg/sdd.yml` and `.fsgg/agents.yml`.
- Governance files are optional compatibility pointers and are not evaluated by this command.

## Lifecycle Notes
- Next lifecycle action: `fsgg-sdd specify --work 2852-runtime-skill-identity`.
