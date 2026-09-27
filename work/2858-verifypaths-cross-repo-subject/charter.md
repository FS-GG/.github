---
schemaVersion: 1
workId: 2858-verifypaths-cross-repo-subject
title: Explicit verify-paths subject and Coordination cross-repo paths
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

# Explicit verify-paths subject and Coordination cross-repo paths Charter

## Identity
- Work id: `2858-verifypaths-cross-repo-subject`
- Lifecycle stage: charter
- Status: chartered

## Principles
- A verdict must identify the issue-body `Paths:` declaration and PR repository actually compared.
- Explicit caller intent may select a Coordination-owned declaration; branch and closing references cannot silently substitute for it.
- A comparison without a proven path namespace remains a refusal, including under `--warn`.

## Scope Boundaries
- Limit implementation to `verify-paths` subject resolution, cross-repository authorization, and parity tests named by FS-GG/.github#2858.
- Keep implicit cross-repository closing references outside the explicit Coordination authorization path.
- Keep claim, review, delivery, and release decisions with their existing protected workflows.

## Policy Pointers
- SDD policy comes from `.fsgg/sdd.yml` and `.fsgg/agents.yml`.
- Governance files are optional compatibility pointers and are not evaluated by this command.

## Lifecycle Notes
- Next lifecycle action: `fsgg-sdd specify --work 2858-verifypaths-cross-repo-subject`.
