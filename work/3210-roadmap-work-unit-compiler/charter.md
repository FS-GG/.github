---
schemaVersion: 1
workId: 3210-roadmap-work-unit-compiler
title: Roadmap work-unit registration and acceptance compiler
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

# Roadmap work-unit registration and acceptance compiler Charter

## Identity
- Work id: `3210-roadmap-work-unit-compiler`
- Lifecycle stage: charter
- Status: chartered

## Principles
- Roadmap state advances only from typed, observed, content-addressed evidence; prose is never authority.
- Selection is deterministic: one source digest names exactly one next unchecked catalog unit and an accepted prerequisite.
- Preparation and acceptance are pure inspect/render/verify computations; GitHub mutation remains behind the existing staged-intake transaction.
- Candidate, merged, acceptance-candidate, acceptance-merge, and protected-main identities stay distinct and are validated at their own boundary.
- A repeated or interrupted invocation over identical immutable inputs converges to byte-identical artifacts and cannot duplicate registration.
- Every positive control has an independently expressed inverted fixture that observes the precise typed refusal.

## Scope Boundaries
- Add typed work-unit catalog, selection, registration, acceptance, evidence-index, and roadmap-close handoff models to `FS.GG.Coord.Core`.
- Add pure CLI `inspect|render|verify` entry points and reuse the established board-ops staged-intake transaction for any GitHub write.
- Consume #3208 lifecycle/review receipts and #3209 qualification results as typed inputs; do not duplicate their parsers or authority.
- Migrate `work-roadmap` to the compiled boundary only after the coherent package and receiver verification are available.
- Keep general roadmap authoring, semantic review judgement, and a new GitHub mutation channel out of scope.

## Policy Pointers
- SDD policy comes from `.fsgg/sdd.yml` and `.fsgg/agents.yml`.
- Governance files are optional compatibility pointers and are not evaluated by this command.

## Lifecycle Notes
- Next lifecycle action: `fsgg-sdd specify --work 3210-roadmap-work-unit-compiler`.
